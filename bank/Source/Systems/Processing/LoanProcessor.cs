// ============================================
// BanksOfCalradia - BankLoanProcessor.cs
// Author: Dahaka
// Version: 1.3.1 (Stable Release - Safe Logging)
// Description:
//   Processamento diário dos empréstimos do jogador.
//   - Cobra parcelas
//   - Faz pagamento integral ou parcial
//   - Aplica multa e congelamento ao exceder o teto
//   - Remove contratos quitados
//   - Suporte a localização dinâmica via helper L
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Data;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankLoanProcessor : CampaignBehaviorBase
    {
        // ============================================================
        // Configurações de exibição/log
        // ============================================================
        private const bool GAMEPLAY_MESSAGES = true; // Defina como false em release para silenciar mensagens
#if DEBUG
        private const bool VERBOSE_LOG = false;
#else
        private const bool VERBOSE_LOG = false;
#endif

        // ============================================================
        // Registro de eventos
        // ============================================================
        public override void RegisterEvents()
        {
            // Processa diariamente no tick do clã do jogador
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Nenhum estado interno precisa ser salvo
        }

        // ============================================================
        // Gatilho diário (executa o processamento de empréstimos)
        // ============================================================
        private void OnDailyTickClan(Clan clan)
        {
            try
            {
                if (clan == null || clan != Clan.PlayerClan)
                    return;

                var hero = Hero.MainHero;
                if (hero == null)
                    return;

                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                if (behavior == null)
                    return;

                var storage = behavior.GetStorage();
                if (storage == null)
                    return;

                ProcessAllLoansForPlayer(storage, hero);

                // Persiste dados do banco
                try { behavior.SyncBankData(); }
                catch { /* Ignora erros não críticos */ }
            }
            catch (Exception e)
            {
                var msg = L.T("loan_processor_error", "[BanksOfCalradia][LoanProcessor] {ERROR}");
                msg.SetTextVariable("ERROR", e.Message);
                LogWarn(msg.ToString());
            }
        }

        // ============================================================
        // Processa todos os empréstimos do jogador
        // ============================================================
        private void ProcessAllLoansForPlayer(BankStorage storage, Hero hero)
        {
            var playerId = hero.StringId;
            var loans = storage.GetLoans(playerId);
            if (loans == null || loans.Count == 0)
                return;

            int totalContracts = 0, fullyPaidToday = 0, installmentsPaid = 0, partials = 0, penalties = 0;
            List<string> toRemove = new();

            foreach (var loan in loans)
            {
                if (loan.Remaining <= 0.01f || loan.DurationDays <= 0)
                {
                    if (loan.Remaining <= 0.01f)
                        toRemove.Add(loan.LoanId);
                    continue;
                }

                totalContracts++;
                int installmentDue = CalcInstallmentDue(loan);
                if (installmentDue <= 0)
                {
                    toRemove.Add(loan.LoanId);
                    continue;
                }

                int goldAvailable = Math.Max(0, hero.Gold);
                var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == loan.TownId);
                string townName = settlement?.Name?.ToString() ?? L.S("loan_city_unknown", "Unknown city");

                if (goldAvailable >= installmentDue)
                {
                    // Pagamento integral
                    hero.ChangeHeroGold(-installmentDue);
                    loan.Remaining -= installmentDue;
                    loan.DurationDays = Math.Max(0, loan.DurationDays - 1);
                    installmentsPaid++;

                    ShowPaymentInfo(BuildInstallmentInfoMessage(townName, loan.Remaining, loan.DurationDays), installmentDue);

                    if (loan.Remaining <= 0.01f || loan.DurationDays <= 0)
                    {
                        loan.Remaining = 0f;
                        toRemove.Add(loan.LoanId);
                        fullyPaidToday++;
                        ShowSuccess(L.S("loan_fully_paid", "Loan fully paid. Contract removed."));
                    }
                }
                else if (goldAvailable > 0)
                {
                    // Pagamento parcial
                    hero.ChangeHeroGold(-goldAvailable);
                    loan.Remaining -= goldAvailable;
                    partials++;

                    float restanteAntesMulta = MathF.Max(0f, loan.Remaining);
                    var partialMsg = L.T("loan_partial_payment", "Partial payment: {PAID} | Remaining before fee: {REMAINING}");
                    partialMsg.SetTextVariable("PAID", BankUtils.FmtDenars(goldAvailable));
                    partialMsg.SetTextVariable("REMAINING", BankUtils.FmtDenars(restanteAntesMulta));
                    ShowPaymentInfo(partialMsg.ToString(), goldAvailable);

                    // Aplica multa
                    int multaAplicada = ApplyLateFeeIfAny(loan, ref penalties);
                    if (multaAplicada > 0)
                        ShowPenalty(BuildPenaltyMessage(loan, multaAplicada));

                    if (MaybeFreezePenalty(loan))
                        ShowPenalty(L.S("loan_penalty_frozen", "Daily late fee frozen after exceeding 10x the contracted amount."));

                    if (loan.Remaining <= 0.01f)
                    {
                        loan.Remaining = 0f;
                        toRemove.Add(loan.LoanId);
                        fullyPaidToday++;
                        ShowSuccess(L.S("loan_fully_paid", "Loan fully paid. Contract removed."));
                    }
                }
                else
                {
                    // Sem dinheiro → aplica multa
                    int multaAplicada = ApplyLateFeeIfAny(loan, ref penalties);
                    if (multaAplicada > 0)
                        ShowPenalty(BuildPenaltyMessage(loan, multaAplicada));

                    if (MaybeFreezePenalty(loan))
                        ShowPenalty(L.S("loan_penalty_frozen", "Daily late fee frozen after exceeding 10x the contracted amount."));
                }
            }

            // Remove contratos quitados
            foreach (var id in toRemove)
                storage.RemoveLoan(playerId, id);

            // Log resumo (apenas se verbose)
            if (VERBOSE_LOG && totalContracts > 0)
            {
                var summary = L.T("loan_process_summary",
                    "[Loan] Processed: {TOTAL} | Full: {FULL} | Partials: {PARTIAL} | Penalties: {PENALTIES} | Paid today: {PAIDTODAY}");
                summary.SetTextVariable("TOTAL", totalContracts);
                summary.SetTextVariable("FULL", installmentsPaid);
                summary.SetTextVariable("PARTIAL", partials);
                summary.SetTextVariable("PENALTIES", penalties);
                summary.SetTextVariable("PAIDTODAY", fullyPaidToday);
                LogInfo(summary.ToString());
            }
        }

        // ============================================================
        // Auxiliares financeiros
        // ============================================================
        private static int CalcInstallmentDue(BankLoanData loan)
        {
            int daysRemaining = Math.Max(loan.DurationDays, 1);
            int due = MathF.Ceiling(loan.Remaining / daysRemaining);
            return Math.Max(due, 1);
        }

        private static int ApplyLateFeeIfAny(BankLoanData loan, ref int penaltiesCounter)
        {
            if (loan.LateFeeRate > 0f && loan.Remaining > 0.01f)
            {
                float normalizedRate = loan.LateFeeRate > 1f ? loan.LateFeeRate / 100f : loan.LateFeeRate;
                int multaInt = MathF.Ceiling(loan.Remaining * normalizedRate);
                if (multaInt > 0)
                {
                    loan.Remaining += multaInt;
                    penaltiesCounter++;
                    return multaInt;
                }
            }
            return 0;
        }

        private static bool MaybeFreezePenalty(BankLoanData loan)
        {
            float contractedTotal = loan.OriginalAmount * (1f + loan.InterestRate / 100f);
            float cap = contractedTotal * 10f;
            if (loan.LateFeeRate > 0f && loan.Remaining > cap)
            {
                loan.LateFeeRate = 0f;
                return true;
            }
            return false;
        }

        // ============================================================
        // Mensagens e logging
        // ============================================================
        private static string BuildInstallmentInfoMessage(string townName, float remaining, int installmentsLeft)
        {
            var msg = L.T("loan_payment_info",
                "Bank loan from {CITY}: remaining debt {AMOUNT} | Installments left: {INSTALLMENTS}");
            msg.SetTextVariable("CITY", townName);
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(remaining));
            msg.SetTextVariable("INSTALLMENTS", installmentsLeft);
            return msg.ToString();
        }

        private static string BuildPenaltyMessage(BankLoanData loan, int appliedFee)
        {
            var msg = L.T("loan_penalty_applied",
                "Late fee of {RATE} applied. New remaining: {AMOUNT}");
            msg.SetTextVariable("RATE", BankUtils.FmtPct(loan.LateFeeRate > 1f ? loan.LateFeeRate / 100f : loan.LateFeeRate));
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(loan.Remaining));
            return msg.ToString();
        }

        private static void ShowPaymentInfo(string msg, int paidAmount = 0)
        {
            if (!GAMEPLAY_MESSAGES) return;

            // 🔹 Primeira linha — estilo nativo com ícone oficial e tradução dinâmica
            if (paidAmount > 0)
            {
                string coinIcon = "{=!}<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";
                var paidMsg = L.T("loan_installment_paid_icon", "Installment paid: -{AMOUNT} {ICON}");
                paidMsg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(paidAmount));
                paidMsg.SetTextVariable("ICON", coinIcon);

                InformationManager.DisplayMessage(new InformationMessage(
                    paidMsg.ToString(),
                    Color.FromUint(0xFFEEEEEE)
                ));
            }

            // 🔹 Segunda linha — detalhes complementares (tradução já aplicada no caller)
            InformationManager.DisplayMessage(new InformationMessage(
                msg,
                Color.FromUint(0xFFEEEEEE)
            ));
        }


        private static void ShowPenalty(string msg)
        {
            if (!GAMEPLAY_MESSAGES) return;
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFE57A7A)));
        }

        private static void ShowSuccess(string msg)
        {
            if (!GAMEPLAY_MESSAGES) return;
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFF7AE57A)));
        }

        private static void LogInfo(string msg)
        {
            if (!VERBOSE_LOG) return;
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFAACCEE)));
        }

        private static void LogWarn(string msg)
        {
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFFF6666)));
        }
    }
}
