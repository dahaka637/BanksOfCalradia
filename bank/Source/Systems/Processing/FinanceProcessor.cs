// ============================================
// BanksOfCalradia - BankFinanceProcessor.cs
// Author: Dahaka
// Version: 6.4.0 (Localization + Cleanup)
// Description:
//   Modelo de finanças do clã que injeta a previsão
//   de rendimentos bancários (poupança) no painel
//   de Expected Gold Change, com base nas contas
//   salvas pelo BankCampaignBehavior.
//
//   - Usa o sistema de localização nativo via helper L
//   - Mostra linhas por cidade no modo detalhado (ALT)
//   - Evita poluição de log em runtime normal
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class FinanceProcessor : DefaultClanFinanceModel
    {
        public override int PartyGoldLowerThreshold => base.PartyGoldLowerThreshold;

        // =========================================================
        // Income detalhado (Expected Gold Change)
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
                AddBankInterestToExplainedNumber(clan, ref result, includeDescriptions, includeDetails);
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
        // Resultado consolidado (Expected Gold Change resumido)
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
                // Rendimentos de poupança
                AddBankInterestToExplainedNumber(clan, ref result, includeDescriptions, includeDetails);

                // Visualização das parcelas de empréstimos (somente detalhado)
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
        // Cálculo de juros e previsão de prosperidade (versão calibrada)
        // =========================================================
        internal void AddBankInterestToExplainedNumber(
            Clan clan,
            ref ExplainedNumber goldChange,
            bool includeDescriptions,
            bool includeDetails = false)
        {
            // Apenas para o jogador
            if (clan == null || clan.Leader == null || clan != Clan.PlayerClan)
                return;

            var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
            if (behavior == null)
            {
#if DEBUG
                DebugMsg("[BanksOfCalradia][FinanceModel] BankCampaignBehavior not found.");
#endif
                return;
            }

            var storage = behavior.GetStorage();
            if (storage == null)
            {
#if DEBUG
                DebugMsg("[BanksOfCalradia][FinanceModel] Storage is null.");
#endif
                return;
            }

            var hero = Hero.MainHero;
            if (hero == null || string.IsNullOrEmpty(hero.StringId))
                return;

            if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                accounts == null || accounts.Count == 0)
                return;

            float totalGoldGain = 0f;
            float totalProsperityForecast = 0f;

            // Label padrão para o painel (quando não está no modo detalhado)
            var consolidatedLabel = L.T("finance_interest", "Bank interest");

            foreach (var acc in accounts)
            {
                if (acc.Amount <= 0.01f)
                    continue;

                var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);
                if (settlement == null || settlement.Town == null)
                    continue;

                float prosperity = settlement.Town.Prosperity;
                string townName = settlement.Name.ToString();

                // ============================================================
                // 💹 ALGORITMO CALIBRADO (versão C# do simulador Python)
                // ============================================================
                const float fator = 350f;
                const float prosperidadeBase = 5000f;
                const float prosperidadeAlta = 6000f;
                const float prosperidadeMax = 10000f;
                const float CICLO_DIAS = 120f;

                prosperity = MathF.Max(prosperity, 1f);

                // --- Fator suavizador ---
                float rawSuavizador = prosperidadeBase / prosperity;
                float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);

                // --- Ajuste de pobreza ---
                float bonus = 0f;
                if (prosperity < prosperidadeBase)
                {
                    float ajustePobreza = MathF.Pow((prosperidadeBase - prosperity) / prosperidadeBase, 1.3f);
                    bonus = ajustePobreza * 3f;
                }

                // --- Ajuste de riqueza ---
                float ajusteRiqueza = 0f;
                if (prosperity > prosperidadeAlta)
                {
                    float excesso = (prosperity - prosperidadeAlta) / (prosperidadeMax - prosperidadeAlta);
                    excesso = MathF.Clamp(excesso, 0f, 1f);
                    ajusteRiqueza = MathF.Pow(excesso, 1.6f) * 5.5f;
                    // ➕ leve suavização extra para reduzir juros de cidades ricas
                    ajusteRiqueza *= 1.15f;
                }

                // --- Cálculo das taxas ---
                float taxaAnualBruta = (prosperity / fator) + bonus - ajusteRiqueza;

                // ➖ leve redução global nas cidades acima da média
                float taxaAnual = taxaAnualBruta;
                if (prosperity > prosperidadeBase)
                {
                    float suavReducao = 1f - ((prosperity - prosperidadeBase) / prosperidadeMax) * 0.1f; // até -10 %
                    taxaAnual = taxaAnualBruta * MathF.Max(0.85f, suavReducao);
                }

                float taxaDiaria = taxaAnual / CICLO_DIAS;

                // --- Rendimento diário ---
                float rendimentoDia = acc.Amount * (taxaDiaria / 100f);
                int ganhoInteiro = MathF.Round(rendimentoDia);

                // Evita perder meio denar em contas grandes
                if (ganhoInteiro == 0 && rendimentoDia >= 0.5f)
                    ganhoInteiro = 1;

                if (ganhoInteiro < 1)
                    continue;

                // --- Ganho de prosperidade (forecast informativo) ---
                float ganhoBase = MathF.Pow(acc.Amount / 1_000_000f, 0.55f);
                float fatorProsperidade = MathF.Pow(6000f / (prosperity + 3000f), 0.3f);

                float ganhoProsperidadePrevisto = MathF.Round(
                    ganhoBase
                    * fatorProsperidade
                    * fatorSuavizador
                    * 1.5f                      // ⚡ Boost global +50%
                    * (1f + bonus * 0.05f)
                    * (1f - ajusteRiqueza * 0.03f),
                    4
                );

                totalGoldGain += ganhoInteiro;
                totalProsperityForecast += ganhoProsperidadePrevisto;

                // -----------------------------------------------------
                // Exibição detalhada por cidade (modo ALT)
                // -----------------------------------------------------
                if (includeDetails && includeDescriptions && ganhoInteiro > 0)
                {
                    var line = L.T("finance_interest_city", "Bank interest ({CITY})");
                    line.SetTextVariable("CITY", townName);
                    goldChange.Add(ganhoInteiro, line);
                }
            }

            // ---------------------------------------------------------
            // Consolidação final no painel principal
            // ---------------------------------------------------------
            if (totalGoldGain > 0.01f && !includeDetails)
            {
                goldChange.Add(MathF.Round(totalGoldGain), consolidatedLabel);
            }

            // totalProsperityForecast permanece disponível para uso futuro
        }



        // =========================================================
        // Visualização das parcelas de empréstimos (modo detalhado)
        // =========================================================
        private void AddLoanPreviewVisual(
            Clan clan,
            ref ExplainedNumber goldChange,
            bool includeDescriptions,
            bool includeDetails)
        {
            try
            {
                // Apenas para o jogador e apenas no modo detalhado
                if (clan == null || clan != Clan.PlayerClan)
                    return;
                if (!includeDetails)
                    return;

                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                if (behavior == null)
                    return;

                var storage = behavior.GetStorage();
                var hero = Hero.MainHero;
                if (hero == null || string.IsNullOrEmpty(hero.StringId))
                    return;

                var loans = storage.GetLoans(hero.StringId);
                if (loans == null || loans.Count == 0)
                    return;

                foreach (var loan in loans)
                {
                    if (loan.Remaining <= 0.01f || loan.DurationDays <= 0)
                        continue;

                    int daysRemaining = Math.Max(loan.DurationDays, 1);
                    int due = MathF.Ceiling(loan.Remaining / daysRemaining);
                    if (due <= 0)
                        continue;

                    var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == loan.TownId);
                    string townName = settlement?.Name?.ToString() ?? L.S("default_city", "City");

                    var label = L.T("loan_payment_city", "Loan payment ({CITY})");
                    label.SetTextVariable("CITY", townName);

                    // Linha informativa, não queremos esconder o valor
                    goldChange.Add(-due, label);
                }
            }
            catch
            {
                // silencioso: não quebrar o painel
            }
        }

        // =========================================================
        // Logger utilitário (usado só em DEBUG)
        // =========================================================
        private static void DebugMsg(string msg)
        {
            InformationManager.DisplayMessage(
                new InformationMessage(msg, Color.FromUint(0xFFAACCEE))
            );
        }
    }
}
