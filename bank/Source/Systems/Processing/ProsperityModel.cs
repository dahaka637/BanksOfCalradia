// ============================================
// BanksOfCalradia - BankProsperityModel.cs
// Author: Dahaka
// Version: 3.3.1 (v1.7.1 Stable Curve • Double Safe)
// Description:
//   • Espelha 100% o algoritmo Python v1.7.1 (Stable Curve)
//   • Corrigido para suportar double precision (investimentos massivos)
//   • Food Aid e Prosperity Gain balanceados conforme metas calibradas
//   • Mantém EMA, suavização e sistema de tradução L.T()
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

            // ==========================================================
            // 🔹 Soma dos investimentos (agora em double precision)
            // ==========================================================
            double invested = 0d;
            try
            {
                invested = storage.SavingsByPlayer
                    .SelectMany(k => k.Value)
                    .Where(a => a != null && a.TownId == town.Settlement.StringId && a.Amount > 0.01)
                    .Sum(a => a.Amount);
            }
            catch
            {
                return fx;
            }

            if (invested <= 1d)
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

        // ==========================================================
        // 🔹 ALGORITMO PRINCIPAL (v1.7.1 Stable Curve + Double Safe)
        // ==========================================================
        private static void ComputeAlgorithm(Town town, double invested, out float foodAid, out float prosGain)
        {
            // Conversão segura (mantém limites do float)
            float investedF = (float)Math.Min(invested, 9.22e15);

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

            // Urgência alimentar
            float urgStock = 1f - Smooth01(Clamp01((ratio - 0.5f) / 0.8f));
            float pesoTrend = food <= estoqueRazoavel ? 1f : 0.25f;
            float urgTrend = expected < 0f ? Clamp01((float)(-expected / 12f) * pesoTrend) : 0f;

            float urgTotal = 0.65f * urgStock + 0.35f * urgTrend;
            if (food < estoqueMinimo || (food < estoqueRazoavel && expected <= -10f))
                urgTotal = 1f;

            urgTotal = Clamp01(urgTotal);

            // Curva calibrada (v1.7.1)
            float ganhoBase = (float)Math.Pow(investedF / 1_000_000f, 0.46f);
            float fatorProsperidade = (float)Math.Pow(5000f / (p + 2000f), 0.44f);
            float suavizador = 0.68f + (5000f / p) * 0.42f;

            float pobrezaRatio = Math.Max(0f, (5000f - p) / 5000f);
            float incentivoPobreza = (float)Math.Pow(pobrezaRatio, 1.05f) * 0.09f;

            float penalidadeRiqueza = 0f;
            if (p > 5000f)
            {
                float excesso = (p - 5000f) / 5000f;
                penalidadeRiqueza = (float)Math.Pow(Math.Max(0f, excesso), 1.2f) * 0.06f;
            }

            float nerfFactor = 1f / (1f + (float)Math.Pow(investedF / 6_000_000f, 0.28f));

            float ganhoTotal =
                ganhoBase *
                fatorProsperidade *
                suavizador *
                5.8f *
                (1f + incentivoPobreza * 1.3f) *
                (1f - penalidadeRiqueza * 0.5f) *
                nerfFactor;

            // Distribuição entre prosperidade e ajuda alimentar
            prosGain = ganhoTotal * (1f - urgTotal);
            foodAid = ganhoTotal * FOOD_PER_PROSP * urgTotal;
        }

        // ==========================================================
        // 🔹 UTILITÁRIOS
        // ==========================================================
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
