// ============================================
// BanksOfCalradia - BankProsperityModel.cs
// Author: Dahaka
// Version: 1.2.0 (Stable Release - Safe & Optimized)
// Description:
//   Injects prosperity forecast from players' savings,
//   using the same logic as BankFinanceProcessor.
//
//   • Integrates with SettlementProsperityModel
//   • Adds daily forecast to Expected Change
//   • Fully safe and non-blocking for Campaign boot
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankProsperityModel : DefaultSettlementProsperityModel
    {
        // Configuração: controle de mensagens e logs
        private const bool DEBUG_MODE = false;

        public override ExplainedNumber CalculateProsperityChange(Town town, bool includeDescriptions = false)
        {
            // Base calculation from the native model
            var result = base.CalculateProsperityChange(town, includeDescriptions);

            try
            {
                AddBankProsperityForecast(town, ref result, includeDescriptions);
            }
            catch (Exception ex)
            {
                #if DEBUG
                var msg = L.T("prosperity_model_error", "[BanksOfCalradia] Prosperity model error: {ERROR}");
                msg.SetTextVariable("ERROR", ex.Message);

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString(),
                    Color.FromUint(0xFFFF5555)
                ));
                #endif
            }

            return result;
        }

        // =====================================================
        // Bank prosperity forecast (same logic as FinanceModel)
        // =====================================================
        private void AddBankProsperityForecast(Town town, ref ExplainedNumber result, bool includeDescriptions)
        {
            if (town == null || Campaign.Current == null)
                return;

            var behavior = Campaign.Current.GetCampaignBehavior<BankCampaignBehavior>();
            if (behavior == null)
                return;

            var storage = behavior.GetStorage();
            if (storage == null || storage.SavingsByPlayer.Count == 0)
                return;

            float totalGain = 0f;

            foreach (var kvp in storage.SavingsByPlayer)
            {
                var accounts = kvp.Value;
                if (accounts == null || accounts.Count == 0)
                    continue;

                foreach (var acc in accounts)
                {
                    // Filtra contas inválidas ou sem saldo relevante
                    if (acc.Amount <= 0.01f || acc.TownId != town.Settlement.StringId)
                        continue;

                    float prosperity = MathF.Max(town.Prosperity, 1f);

                    // ============================================================
                    // 💹 NOVO ALGORITMO CALIBRADO (versão ajustada – mais prosperidade, menos juros em cidades ricas)
                    // ============================================================
                    const float prosperidadeBase = 5000f;
                    const float prosperidadeAlta = 6000f;
                    const float prosperidadeMax = 10000f;

                    // --- Fator suavizador ---
                    float rawSuavizador = prosperidadeBase / prosperity;
                    float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);

                    // --- Bônus de pobreza ---
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
                        // ➕ leve suavização extra para reduzir o efeito de cidades ricas
                        ajusteRiqueza *= 1.15f;
                    }

                    // --- Ganho de prosperidade diário ---
                    float ganhoBase = MathF.Pow(acc.Amount / 1_000_000f, 0.55f);
                    float fatorProsperidade = MathF.Pow(6000f / (prosperity + 3000f), 0.3f);

                    // ⚡ Boost global de +50% em prosperidade
                    float ganhoPrevisto = MathF.Round(
                        ganhoBase
                        * fatorProsperidade
                        * fatorSuavizador
                        * 1.5f
                        * (1f + bonus * 0.05f)
                        * (1f - ajusteRiqueza * 0.03f),
                        4
                    );

                    totalGain += MathF.Max(0f, ganhoPrevisto);
                }
            }

            if (totalGain > 0.01f)
            {
                var label = L.T("prosperity_bank_label", "Bank savings influence");
                result.Add(totalGain, label);

                #if DEBUG
                if (DEBUG_MODE)
                {
                    var dbg = L.T("prosperity_bank_debug",
                        "[BanksOfCalradia][DEBUG] {TOWN}: +{GAIN} prosperity/day (bank forecast)");
                    dbg.SetTextVariable("TOWN", town.Name?.ToString() ?? L.S("default_city", "City"));
                    dbg.SetTextVariable("GAIN", totalGain.ToString("0.0000"));

                    InformationManager.DisplayMessage(new InformationMessage(
                        dbg.ToString(),
                        Color.FromUint(0xFFAACCEE)
                    ));
                }
                #endif
            }
        }

    }
}
