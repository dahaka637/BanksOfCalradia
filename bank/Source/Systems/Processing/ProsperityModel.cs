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

                    float prosperity = town.Prosperity;

                    // Mesmo algoritmo do BankFinanceProcessor
                    float ganhoBase = MathF.Pow(acc.Amount / 1_000_000f, 0.55f);
                    float fatorProsperidade = MathF.Pow(7000f / (prosperity + 3000f), 0.3f);
                    float ganhoPrevisto = MathF.Round(ganhoBase * fatorProsperidade, 2);

                    totalGain += ganhoPrevisto;
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
                    dbg.SetTextVariable("GAIN", totalGain.ToString("0.00"));

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
