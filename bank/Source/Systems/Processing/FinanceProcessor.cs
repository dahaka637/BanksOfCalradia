// ============================================
// BanksOfCalradia - BankFinanceProcessor.cs
// Author: Dahaka
// Version: 6.8.0 (Production • Integer Money)
// Description:
//   Financial engine for Banks of Calradia.
//   • Double precision calculations
//   • Integer-denar outputs (0.5 => 1)
//   • Safe compound interest (auto-reinvest)
//   • Clean, stable, production-safe
//   • No inheritance — used by Harmony patches
// ============================================

using BanksOfCalradia.Source.Core;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class FinanceProcessor
    {
        private const float PROSPERIDADE_BASE = 5000f;
        private const float PROSPERIDADE_ALTA = 6000f;
        private const float PROSPERIDADE_MAX = 10000f;
        private const float CICLO_DIAS = 120f;

        // ============================================================================
        // UTILITÁRIO: arredondamento universal para denares inteiros
        // ============================================================================
        private static double RoundToDenars(double value)
        {
            return Math.Round(value, 0, MidpointRounding.AwayFromZero);
        }

        // ============================================================================
        // JUROS PRINCIPAIS — chamado pelos patches Harmony
        // ============================================================================
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

            double totalGoldGain = 0d;
            bool anyAutoReinvestChange = false;

            double totalReinvested = 0d;
            int reinvestCount = 0;
            var reinvestEntries = new List<(string Town, double Amount)>();

            var consolidatedLabel = L.T("finance_interest", "Bank interest");

            foreach (var acc in accounts)
            {
                if (acc == null || acc.Amount <= 0.01d)
                    continue;

                var settlement =
                    Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);

                if (settlement?.Town == null)
                    continue;

                double prosperity = settlement.Town.Prosperity;
                string townName = settlement.Name.ToString();

                double ganhoDiario = ComputeInterestForAccount(acc.Amount, prosperity);
                if (ganhoDiario <= 0.0)
                    continue;

                // Auto reinvest
                if (acc.AutoReinvest)
                {
                    if (applyWithdrawals && ganhoDiario >= 1.0d)
                    {
                        acc.Amount += ganhoDiario;
                        anyAutoReinvestChange = true;
                        totalReinvested += ganhoDiario;
                        reinvestCount++;
                        reinvestEntries.Add((townName, ganhoDiario));
                    }
                    continue;
                }

                // Sem reinvestimento → vai para o Expected Gold Change
                totalGoldGain += ganhoDiario;

                if (includeDetails && includeDescriptions && ganhoDiario > 0)
                {
                    var line = L.T("finance_interest_city", "Bank interest ({CITY})");
                    line.SetTextVariable("CITY", townName);
                    goldChange.Add((float)ganhoDiario, line);
                }
            }

            // Mensagens de reinvestimento
            if (applyWithdrawals && reinvestCount > 0)
            {
                if (reinvestCount <= 3)
                {
                    foreach (var (Town, Amount) in reinvestEntries)
                        ShowReinvestInfo(Town, Amount);
                }
                else
                {
                    ShowReinvestSummary(totalReinvested, reinvestCount);
                }
            }

            if (applyWithdrawals && anyAutoReinvestChange)
            {
                try { behavior.SyncBankData(); } catch { }
            }

            // Consolidado
            if (totalGoldGain >= 1.0d && !includeDetails)
            {
                double totalInt = RoundToDenars(totalGoldGain);
                if (totalInt >= 1.0d)
                    goldChange.Add((float)totalInt, consolidatedLabel);
            }
        }

        // ============================================================================
        // PREVIEW DAS PARCELAS (ALT) — chamado pelos patches Harmony
        // ============================================================================
        internal void AddLoanPreviewVisual(
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

                double currentDay = CampaignTime.Now.ToDays;
                const int GRACE_DAYS = 5;

                foreach (var loan in loans)
                {
                    if (loan.Remaining <= 0.01d || loan.DurationDays <= 0)
                        continue;

                    if (loan.CreatedAt <= 0f)
                        loan.CreatedAt = (float)(currentDay - GRACE_DAYS);

                    if (currentDay - loan.CreatedAt < GRACE_DAYS)
                        continue;

                    double due = loan.Remaining / Math.Max(loan.DurationDays, 1);
                    if (due <= 0)
                        continue;

                    double dueRounded = Math.Round(due, 0, MidpointRounding.AwayFromZero);
                    if (dueRounded < 1.0d)
                        continue;

                    string townName =
                        Campaign.Current?.Settlements?
                        .Find(s => s.StringId == loan.TownId)?
                        .Name?.ToString()
                        ?? L.S("default_city", "City");

                    var label = L.T("loan_payment_city", "Loan payment ({CITY})");
                    label.SetTextVariable("CITY", townName);

                    goldChange.Add(-(float)dueRounded, label);
                }
            }
            catch { }
        }

        // ============================================================================
        // Cálculo individual — juros diários (double precision)
        // ============================================================================
        private static double ComputeInterestForAccount(double amount, double prosperity)
        {
            double p = Math.Max(prosperity, 1d);

            double pobrezaRatio = Math.Max(0d, (PROSPERIDADE_BASE - p) / PROSPERIDADE_BASE);
            double incentivoPobreza = Math.Pow(pobrezaRatio, 1.05d) * 0.15d;

            double penalidadeRiqueza = 0d;
            if (p > PROSPERIDADE_ALTA)
            {
                double excesso = Math.Max(0d, (p - PROSPERIDADE_ALTA) /
                                              (PROSPERIDADE_MAX - PROSPERIDADE_ALTA));

                penalidadeRiqueza = Math.Pow(excesso, 1d) * 0.025d;
            }

            double taxaBase = 6.5d + Math.Pow(PROSPERIDADE_BASE / p, 0.45d) * 6.0d;
            taxaBase *= (1.0d + incentivoPobreza - penalidadeRiqueza);

            double ajusteLog = 1.0d / (1.0d + (p / 25000.0d));
            double taxaAnual =
                Math.Round(taxaBase * (0.95d + ajusteLog * 0.15d), 2);

            double taxaDiaria = taxaAnual / CICLO_DIAS;

            double rendimentoDia = amount * (taxaDiaria / 100d);
            double rendimentoCentavos = Math.Round(rendimentoDia, 2);
            double rendimentoInteiro = RoundToDenars(rendimentoCentavos);

            if (rendimentoInteiro < 1.0d)
                return 0d;

            return rendimentoInteiro;
        }

        // ============================================================================
        // Cálculo independente defensivo (para failsafe)
        // ============================================================================
        internal static double CalculateStandaloneDailyInterest()
        {
            try
            {
                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                var storage = behavior?.GetStorage();
                var hero = Hero.MainHero;

                if (storage == null || hero == null || string.IsNullOrEmpty(hero.StringId))
                    return 0d;

                if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                    accounts == null || accounts.Count == 0)
                    return 0d;

                double totalGoldGain = 0d;

                foreach (var acc in accounts)
                {
                    if (acc?.Amount <= 0.01d)
                        continue;

                    var settlement =
                        Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);

                    if (settlement?.Town == null)
                        continue;

                    double ganho =
                        ComputeInterestForAccount(acc.Amount, settlement.Town.Prosperity);

                    totalGoldGain += ganho;
                }

                return RoundToDenars(totalGoldGain);
            }
            catch
            {
                return 0d;
            }
        }

        // ============================================================================
        // Mensagens de reinvestimento automático
        // ============================================================================
        private static void ShowReinvestInfo(string townName, double amount)
        {
            try
            {
                string prefix = L.S("finance_auto_reinvest_msg", "Interest of");
                string mid = L.S("finance_auto_reinvest_to", "added to savings in");
                string msg = $"{prefix} +{BankUtils.FmtDenars(amount)} {mid} {townName}";
                InformationManager.DisplayMessage(
                    new InformationMessage(msg, Color.FromUint(0xFFEEEEEE)));
            }
            catch { }
        }

        private static void ShowReinvestSummary(double totalAmount, int count)
        {
            try
            {
                string prefix =
                    L.S("finance_auto_reinvest_summary_1",
                        "Automatic reinvestment completed:");

                string mid =
                    L.S("finance_auto_reinvest_summary_2",
                        "Total of");

                TextObject suffixObj =
                    L.T("finance_auto_reinvest_summary_3",
                        "added across {COUNT} bank accounts.");

                suffixObj.SetTextVariable("COUNT", count);

                string msg =
                    $"{prefix} {mid} +{BankUtils.FmtDenars(totalAmount)} {suffixObj}";

                InformationManager.DisplayMessage(
                    new InformationMessage(msg, Color.FromUint(0xFFEEEEEE)));
            }
            catch { }
        }

    }
}
