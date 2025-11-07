// ============================================
// BanksOfCalradia - BankProsperityModel.cs
// Author: Dahaka
// Version: 3.2.1 (Final Production • Framework Compatible)
// Description:
//   • Food Aid e Prosperity Gain espelhando o trainer Python
//   • Ajuda depende de investimento real na cidade
//   • Balança dinâmica: estoque/tendência definem direção (food vs pros)
//   • Curva "v1.6.0 • Prosperity Weighted" calibrada e estável
//   • Suavização temporal (EMA) e anti-oscilações para Food Aid
//   • Sistema de tradução integrado via helper L.T()
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankProsperityModel : DefaultSettlementProsperityModel
    {
        private static readonly Dictionary<string, float> _lastFoodAid = new();

        private const float EMA_ALPHA = 0.22f;
        private const float MIN_AID_DELTA = 0.02f;
        private const float MAX_AID_JUMP_FRAC = 0.25f;
        private const float MIN_STOCK_FLOOR = 25f;
        private const float FOOD_PER_PROSP = 5f;

        public override ExplainedNumber CalculateProsperityChange(Town town, bool includeDescriptions = false)
        {
            var result = base.CalculateProsperityChange(town, includeDescriptions);

            var effects = ComputeAndApplyFoodAndPros(town);

            if (effects.ProsGain > 0.00005f)
            {
                var labelPros = L.T("prosperity_bank_label", "Bank influence");
                result.Add(effects.ProsGain, labelPros);
            }

            return result;
        }

        private static Effects ComputeAndApplyFoodAndPros(Town town)
        {
            Effects fx = default;

            if (town == null || Campaign.Current == null)
                return fx;

            var behavior = Campaign.Current.GetCampaignBehavior<BankCampaignBehavior>();
            var storage = behavior?.GetStorage();
            if (storage == null || storage.SavingsByPlayer == null || storage.SavingsByPlayer.Count == 0)
                return fx;

            float invested = 0f;
            try
            {
                invested = storage.SavingsByPlayer
                    .SelectMany(k => k.Value)
                    .Where(a => a != null && a.TownId == town.Settlement.StringId && a.Amount > 0.01f)
                    .Sum(a => a.Amount);
            }
            catch
            {
                return fx;
            }

            if (invested <= 1f)
            {
                BankFoodModelProxy.RegisterAid(town, 0f);
                return fx;
            }

            ComputeAlgorithm(town, invested, out float foodAidRaw, out float prosGainRaw);

            string id = town.Settlement.StringId;
            float lastAid = _lastFoodAid.ContainsKey(id) ? _lastFoodAid[id] : 0f;

            float ema = lastAid + EMA_ALPHA * (foodAidRaw - lastAid);
            float maxStep = (float)(Math.Abs(lastAid) * MAX_AID_JUMP_FRAC + 0.8f);
            float smoothAid = Clamp(ema, lastAid - maxStep, lastAid + maxStep);

            if (Math.Abs(smoothAid - lastAid) > MIN_AID_DELTA || (foodAidRaw <= 0f && lastAid != 0f))
            {
                _lastFoodAid[id] = smoothAid;
                BankFoodModelProxy.RegisterAid(town, smoothAid);
            }

            fx.FoodAid = Math.Max(0f, foodAidRaw);
            fx.ProsGain = Math.Max(0f, prosGainRaw);
            return fx;
        }

        private static void ComputeAlgorithm(Town town, float invested, out float foodAid, out float prosGain)
        {
            float p = Math.Max(town.Prosperity, 1f);
            float food = town.FoodStocks;

            float expected = 0f;
            try
            {
                expected = Campaign.Current.Models.SettlementFoodModel
                    .CalculateTownFoodStocksChange(town, true, false).ResultNumber;
            }
            catch { }

            float estoqueMinimo = Math.Max(MIN_STOCK_FLOOR, p / 50f);
            float estoqueRazoavel = p / 20f;

            float ratio = SafeDiv(food, estoqueRazoavel);
            float urgStock = 1f - Smooth01(Clamp01((ratio - 0.5f) / 0.8f));

            float pesoTrend = food <= estoqueRazoavel ? 1f : 0.25f;
            float urgTrend = expected < 0f ? Clamp01((float)(-expected / 12f) * pesoTrend) : 0f;

            float urgTotal = 0.65f * urgStock + 0.35f * urgTrend;
            if (food < estoqueMinimo || (food < estoqueRazoavel && expected <= -10f))
                urgTotal = 1f;

            urgTotal = Clamp01(urgTotal);

            float ganhoBase = (float)Math.Pow(invested / 1_000_000f, 0.52f);
            float fatorProsperidade = (float)Math.Pow(5000f / (p + 2000f), 0.42f);
            float suavizador = 0.75f + (5000f / p) * 0.55f;

            float pobrezaRatio = Math.Max(0f, (5000f - p) / 5000f);
            float incentivoPobreza = (float)Math.Pow(pobrezaRatio, 1.05f) * 0.20f;

            float penalidadeRiqueza = 0f;
            if (p > 5000f)
            {
                float excesso = (p - 5000f) / (10000f - 5000f);
                penalidadeRiqueza = (float)Math.Pow(Math.Max(0f, excesso), 1.2f) * 0.06f;
            }

            float nerfFactor = 1f / (1f + (float)Math.Pow(invested / 15_000_000f, 0.22f));

            float ganhoTotal =
                ganhoBase *
                fatorProsperidade *
                suavizador *
                10.5f *
                (1f + incentivoPobreza * 1.8f) *
                (1f - penalidadeRiqueza * 0.5f) *
                nerfFactor;

            prosGain = ganhoTotal * (1f - urgTotal);
            foodAid = ganhoTotal * FOOD_PER_PROSP * urgTotal;
        }

        private static float Clamp01(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x;
        }

        private static float Clamp(float x, float min, float max)
        {
            if (x < min) return min;
            if (x > max) return max;
            return x;
        }

        private static float SafeDiv(float a, float b)
        {
            return b > 1e-6f ? a / b : 0f;
        }

        private static float Smooth01(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }

        private struct Effects
        {
            public float FoodAid;
            public float ProsGain;
        }
    }
}
