// ============================================
// BanksOfCalradia - BankFinanceProcessor.cs
// Author: Dahaka
// Version: 6.6.3 (Production Stable)
// Description:
//   Core finance model for Banks of Calradia.
//   Injects savings interest projection into
//   the clan Expected Gold Change panel, based
//   on accounts stored in BankCampaignBehavior.
//
//   • Uses native localization helper (L)
//   • Displays detailed per-town lines (ALT mode)
//   • Self-defensive mode for compatibility
//   • Supports per-account Auto-Reinvest (compound interest)
//   • Distinct message logic: individual OR summary
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class FinanceProcessor : DefaultClanFinanceModel
    {
        // =========================================================
        // Constantes principais do modelo de poupança
        // =========================================================
        private const float PROSPERIDADE_BASE = 5000f;
        private const float PROSPERIDADE_ALTA = 6000f;
        private const float PROSPERIDADE_MAX = 10000f;
        private const float CICLO_DIAS = 120f;

        public override int PartyGoldLowerThreshold => base.PartyGoldLowerThreshold;

        // =========================================================
        // Verifica se este é o modelo ativo
        // =========================================================
        internal static bool IsActiveModel()
        {
            var models = Campaign.Current?.Models;
            if (models == null)
                return false;

            var active = models.ClanFinanceModel;
            return active != null && active.GetType() == typeof(FinanceProcessor);
        }

        // =========================================================
        // Expected Gold Change — detalhado
        // =========================================================
        public override ExplainedNumber CalculateClanIncome(
            Clan clan,
            bool includeDescriptions = true,
            bool applyWithdrawals = false,
            bool includeDetails = false)
        {
            var result = base.CalculateClanIncome(clan, includeDescriptions, applyWithdrawals, includeDetails);

            try
            {
                AddBankInterestToExplainedNumber(clan, ref result, includeDescriptions, includeDetails, applyWithdrawals);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugMsg($"[BanksOfCalradia][FinanceModel] Error calculating bank income: {ex.Message}");
#endif
            }

            return result;
        }

        // =========================================================
        // Expected Gold Change — resumido (consolidado)
        // =========================================================
        public override ExplainedNumber CalculateClanGoldChange(
            Clan clan,
            bool includeDescriptions = true,
            bool applyWithdrawals = false,
            bool includeDetails = false)
        {
            var result = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);

            try
            {
                AddBankInterestToExplainedNumber(clan, ref result, includeDescriptions, includeDetails, applyWithdrawals);
                AddLoanPreviewVisual(clan, ref result, includeDescriptions, includeDetails);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugMsg($"[BanksOfCalradia][FinanceModel] Error calculating expected gold change: {ex.Message}");
#endif
            }

            return result;
        }

        // =========================================================
        // Cálculo de juros da poupança (Curva Calibrada Premium)
        // =========================================================
        internal void AddBankInterestToExplainedNumber(
            Clan clan,
            ref ExplainedNumber goldChange,
            bool includeDescriptions,
            bool includeDetails = false,
            bool applyWithdrawals = false)
        {
            if (clan == null || clan.Leader == null || clan != Clan.PlayerClan)
                return;

            var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
            var storage = behavior?.GetStorage();
            var hero = Hero.MainHero;

            if (storage == null || hero == null || string.IsNullOrEmpty(hero.StringId))
                return;

            if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                accounts == null || accounts.Count == 0)
                return;

            float totalGoldGain = 0f;
            bool anyAutoReinvestChange = false;

            float totalReinvested = 0f;
            int reinvestCount = 0;
            var reinvestEntries = new List<(string Town, int Amount)>();

            var consolidatedLabel = L.T("finance_interest", "Bank interest");

            foreach (var acc in accounts)
            {
                if (acc == null || acc.Amount <= 0.01f)
                    continue;

                var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);
                if (settlement?.Town == null)
                    continue;

                float prosperity = settlement.Town.Prosperity;
                string townName = settlement.Name.ToString();

                int ganhoInteiro = ComputeInterestForAccount(acc.Amount, prosperity);
                if (ganhoInteiro < 1)
                    continue;

                // --------------------------------------------------
                // Auto-Reinvest (juros compostos)
                // --------------------------------------------------
                if (acc.AutoReinvest)
                {
                    if (applyWithdrawals)
                    {
                        acc.Amount = Math.Max(0f, acc.Amount + ganhoInteiro);
                        anyAutoReinvestChange = true;
                        totalReinvested += ganhoInteiro;
                        reinvestCount++;
                        reinvestEntries.Add((townName, ganhoInteiro));
                    }

                    continue;
                }

                // --------------------------------------------------
                // Comportamento normal (sem reinvestimento automático)
                // --------------------------------------------------
                totalGoldGain += ganhoInteiro;

                if (includeDetails && includeDescriptions && ganhoInteiro > 0)
                {
                    var line = L.T("finance_interest_city", "Bank interest ({CITY})");
                    line.SetTextVariable("CITY", townName);
                    goldChange.Add(ganhoInteiro, line);
                }
            }

            // =========================================================
            // Exibição de mensagens de reinvestimento (após o loop)
            // =========================================================
            if (applyWithdrawals && reinvestCount > 0)
            {
                if (reinvestCount <= 3)
                {
                    // Mostra todas as contas individualmente (sem resumo)
                    foreach (var (Town, Amount) in reinvestEntries)
                        ShowReinvestInfo(Town, Amount);
                }
                else
                {
                    // Mostra apenas o total consolidado (sem individuais)
                    ShowReinvestSummary(totalReinvested, reinvestCount);
                }
            }

            // Persiste dados atualizados
            if (applyWithdrawals && anyAutoReinvestChange)
                try { behavior.SyncBankData(); } catch { }

            // Consolidação no painel principal
            if (totalGoldGain > 0.01f && !includeDetails)
                goldChange.Add((float)Math.Round(totalGoldGain), consolidatedLabel);
        }

        // =========================================================
        // Helper: cálculo individual de juros
        // =========================================================
        private static int ComputeInterestForAccount(float amount, float prosperity)
        {
            float p = Math.Max(prosperity, 1f);

            float rawSuavizador = PROSPERIDADE_BASE / p;
            float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);
            _ = fatorSuavizador;

            // Incentivo à pobreza
            float pobrezaRatio = Math.Max(0f, (PROSPERIDADE_BASE - p) / PROSPERIDADE_BASE);
            float incentivoPobreza = (float)Math.Pow(pobrezaRatio, 1.05f) * 0.15f;

            // Penalidade por riqueza
            float penalidadeRiqueza = 0f;
            if (p > PROSPERIDADE_ALTA)
            {
                float excesso = Math.Max(0f, (p - PROSPERIDADE_ALTA) / (PROSPERIDADE_MAX - PROSPERIDADE_ALTA));
                penalidadeRiqueza = (float)Math.Pow(excesso, 1f) * 0.025f;
            }

            float taxaBase = 6.5f + (float)Math.Pow(PROSPERIDADE_BASE / p, 0.45f) * 6.0f;
            taxaBase *= (1.0f + incentivoPobreza - penalidadeRiqueza);

            float ajusteLog = 1.0f / (1.0f + (p / 25000.0f));
            float taxaAnual = (float)Math.Round(taxaBase * (0.95f + ajusteLog * 0.15f), 2);
            float taxaDiaria = taxaAnual / CICLO_DIAS;

            float rendimentoDia = amount * (taxaDiaria / 100f);
            int ganhoInteiro = (int)Math.Round(rendimentoDia);

            if (ganhoInteiro == 0 && rendimentoDia >= 0.5f)
                ganhoInteiro = 1;

            return Math.Max(ganhoInteiro, 0);
        }

        // =========================================================
        // Fallback defensivo — cálculo total independente
        // =========================================================
        internal static int CalculateStandaloneDailyInterest()
        {
            try
            {
                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                var storage = behavior?.GetStorage();
                var hero = Hero.MainHero;

                if (storage == null || hero == null || string.IsNullOrEmpty(hero.StringId))
                    return 0;

                if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                    accounts == null || accounts.Count == 0)
                    return 0;

                float totalGoldGain = 0f;

                foreach (var acc in accounts)
                {
                    if (acc?.Amount <= 0.01f)
                        continue;

                    var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);
                    if (settlement?.Town == null)
                        continue;

                    int ganhoInteiro = ComputeInterestForAccount(acc.Amount, settlement.Town.Prosperity);
                    if (ganhoInteiro > 0)
                        totalGoldGain += ganhoInteiro;
                }

                return totalGoldGain > 0.01f ? (int)Math.Round(totalGoldGain) : 0;
            }
            catch
            {
                return 0;
            }
        }

        // =========================================================
        // Visualização de parcelas de empréstimos (modo ALT)
        // =========================================================
        private void AddLoanPreviewVisual(
            Clan clan,
            ref ExplainedNumber goldChange,
            bool includeDescriptions,
            bool includeDetails)
        {
            try
            {
                if (clan == null || clan != Clan.PlayerClan || !includeDetails)
                    return;

                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                var storage = behavior?.GetStorage();
                var hero = Hero.MainHero;

                if (storage == null || hero == null || string.IsNullOrEmpty(hero.StringId))
                    return;

                var loans = storage.GetLoans(hero.StringId);
                if (loans == null || loans.Count == 0)
                    return;

                float currentDay = (float)CampaignTime.Now.ToDays;
                const int GRACE_DAYS = 5;

                foreach (var loan in loans)
                {
                    if (loan.Remaining <= 0.01f || loan.DurationDays <= 0)
                        continue;

                    if (loan.CreatedAt <= 0f)
                        loan.CreatedAt = currentDay - GRACE_DAYS;

                    if (currentDay - loan.CreatedAt < GRACE_DAYS)
                        continue;

                    int due = (int)Math.Ceiling(loan.Remaining / Math.Max(loan.DurationDays, 1));
                    if (due <= 0)
                        continue;

                    string townName = Campaign.Current?.Settlements?.Find(s => s.StringId == loan.TownId)?.Name?.ToString()
                        ?? L.S("default_city", "City");

                    var label = L.T("loan_payment_city", "Loan payment ({CITY})");
                    label.SetTextVariable("CITY", townName);

                    goldChange.Add(-due, label);
                }
            }
            catch
            {
                // silencioso: não interrompe o painel
            }
        }

        // =========================================================
        // Mensagem individual de reinvestimento automático
        // =========================================================
        private static void ShowReinvestInfo(string townName, int amount)
        {
            try
            {
                string icon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";
                string prefix = L.S("finance_auto_reinvest_msg", "Interest of");
                string mid = L.S("finance_auto_reinvest_to", "added to savings in");

                string msg = $"{prefix} +{BankUtils.FmtDenars(amount)} {mid} {townName} {icon}";

                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFEEEEEE)));
            }
            catch { }
        }

        // =========================================================
        // Mensagem consolidada de reinvestimento automático
        // =========================================================
        private static void ShowReinvestSummary(float totalAmount, int count)
        {
            try
            {
                string icon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";

                // Partes sem variáveis
                string prefix = L.S("finance_auto_reinvest_summary_1", "Automatic reinvestment completed:");
                string mid = L.S("finance_auto_reinvest_summary_2", "Total of");

                // Parte com variável {COUNT}: use TextObject para setar a variável
                TextObject suffixObj = L.T("finance_auto_reinvest_summary_3", "added across {COUNT} bank accounts.");
                suffixObj.SetTextVariable("COUNT", count);
                string suffix = suffixObj.ToString();

                string msg = $"{prefix} {mid} +{BankUtils.FmtDenars((int)totalAmount)} {suffix} {icon}";

                InformationManager.DisplayMessage(
                    new InformationMessage(msg, Color.FromUint(0xFFEEEEEE))
                );
            }
            catch
            {
                // silencioso
            }
        }


        // =========================================================
        // Logger utilitário (modo DEBUG)
        // =========================================================
        private static void DebugMsg(string msg)
        {
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFAACCEE)));
        }
    }
}
