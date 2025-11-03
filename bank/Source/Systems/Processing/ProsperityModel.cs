// ============================================
// BanksOfCalradia - BankProsperityModel.cs
// Author: Dahaka
// Version: 1.6.0 (Smart Food Sustainability Dampener)
// Description:
//   • Mantém todo o cálculo base da versão 1.5.2
//   • Introduz amortecimento suave baseado em ExpectedFoodChange
//   • Reduz dinamicamente o ganho de prosperidade conforme o risco de fome aumenta
//   • Evita saturação econômica e colapso alimentar
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankProsperityModel : DefaultSettlementProsperityModel
    {
        internal const float MIN_SUSTAINABILITY = 0.10f;
        internal const float MAX_DIVERSION = 0.85f;
        internal const float FOOD_GAIN_MULTIPLIER = 3.25f;
        internal const float MAX_FOOD_PER_DAY_CAP = 120f;
        internal const float TARGET_STOCK_BUFFER = 200f;
        private const bool DEBUG_MODE = false;

        public override ExplainedNumber CalculateProsperityChange(Town town, bool includeDescriptions = false)
        {
            var result = base.CalculateProsperityChange(town, includeDescriptions);
            try
            {
                var fx = ComputeBankEffects(town);
                if (fx.FinalProsperityGain > 0.00005f)
                {
                    var labelPros = L.T("prosperity_bank_label", "Bank influence");
                    result.Add(fx.FinalProsperityGain, labelPros);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[BoC][ERROR] Prosperity calc failed for {town?.Name}: {ex.Message}",
                    Color.FromUint(0xFFFF5555)
                ));
            }
            return result;
        }

        internal static BankEffects ComputeBankEffects(Town town)
        {
            BankEffects fx = new();
            if (town == null || Campaign.Current == null)
                return fx;

            var behavior = Campaign.Current.GetCampaignBehavior<BankCampaignBehavior>();
            if (behavior == null)
                return fx;

            var storage = behavior.GetStorage();
            if (storage == null || storage.SavingsByPlayer.Count == 0)
                return fx;

            bool hasInvestment = storage.SavingsByPlayer
                .SelectMany(kvp => kvp.Value)
                .Any(acc => acc.TownId == town.Settlement.StringId && acc.Amount > 0.01f);

            if (!hasInvestment)
                return fx;

            // === Ganho bancário bruto (Curva Calibrada Premium simplificada) ===
            float totalGain = 0f;
            foreach (var kvp in storage.SavingsByPlayer)
            {
                foreach (var acc in kvp.Value)
                {
                    if (acc.Amount <= 0.01f || acc.TownId != town.Settlement.StringId)
                        continue;

                    float prosperity = MathF.Max(town.Prosperity, 1f);
                    const float prosperidadeBase = 5000f;
                    const float prosperidadeAlta = 6000f;
                    const float prosperidadeMax = 10000f;

                    float rawSuavizador = prosperidadeBase / prosperity;
                    float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);
                    float pobrezaRatio = MathF.Max(0f, (prosperidadeBase - prosperity) / prosperidadeBase);
                    float incentivoPobreza = MathF.Pow(pobrezaRatio, 1.05f) * 0.15f;
                    float penalidadeRiqueza = 0f;

                    if (prosperity > prosperidadeAlta)
                    {
                        float excesso = (prosperity - prosperidadeAlta) / (prosperidadeMax - prosperidadeAlta);
                        penalidadeRiqueza = MathF.Pow(MathF.Max(0f, excesso), 1f) * 0.025f;
                    }

                    float ganhoBase = MathF.Pow(acc.Amount / 1_000_000f, 0.55f);
                    float fatorProsperidade = MathF.Pow(6000f / (prosperity + 3000f), 0.3f);

                    float ganhoPrevisto = ganhoBase * fatorProsperidade * fatorSuavizador * 1.9f *
                                          (1f + incentivoPobreza * 2f) * (1f - penalidadeRiqueza * 0.3f);

                    totalGain += MathF.Max(0f, MathF.Round(ganhoPrevisto, 4));
                }
            }

            fx.TotalGain = totalGain;

            // === Avalia expectativa de comida ===
            float expectedFood = 0f;
            try
            {
                var foodModel = Campaign.Current.Models.SettlementFoodModel;
                if (foodModel != null)
                    expectedFood = foodModel.CalculateTownFoodStocksChange(town, true, false).ResultNumber;
            }
            catch { expectedFood = 0f; }

            // === Curva de amortecimento suave ===
            float sustainFactor;
            if (expectedFood >= 10f)
                sustainFactor = 1f;
            else if (expectedFood > 0f)
                sustainFactor = MathF.Pow(expectedFood / 10f, 1.5f);
            else
                sustainFactor = 0f;

            // === Calcula o ganho ajustado ===
            float adjustedGain = totalGain * sustainFactor;
            fx.FinalProsperityGain = adjustedGain;

            // === Debug opcional ===
            if (DEBUG_MODE)
            {
                string msg = $"[BoC][Sustain-Damp] {town.Name} | Food={expectedFood:+0.00;-0.00} | " +
                             $"RawGain={totalGain:0.0000} | Sustain={sustainFactor:0.00} | Final={adjustedGain:0.0000}";
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFF88FF55)));
            }

            return fx;
        }

        internal class BankEffects
        {
            public float TotalGain;
            public float Sustainability;
            public float DiversionRatio;
            public float FinalProsperityGain;
            public float FoodPerDay;
            public float CurrentFood;
            public float HungerSeverity;
        }
    }

    // =====================================================
    // Debug wrapper opcional (mantido para compatibilidade)
    // =====================================================
    public class BankIssueModel : DefaultIssueModel
    {
        public override void GetIssueEffectsOfSettlement(IssueEffect effect, Settlement settlement, ref ExplainedNumber explainedNumber)
        {
            base.GetIssueEffectsOfSettlement(effect, settlement, ref explainedNumber);
        }
    }
}
