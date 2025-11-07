// ============================================
// BanksOfCalradia - BankFoodModelProxy.cs
// Author: Dahaka
// Version: 1.8 (Final Production • Localized & Stable)
// Description:
//   • Mantém ajuda alimentar persistente entre ciclos UI/simulação
//   • Remove entradas antigas automaticamente (TTL)
//   • Integrado ao sistema de tradução do mod (L.T)
//   • Código otimizado e seguro para produção
// ============================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

using BanksOfCalradia.Source.Core;

namespace BanksOfCalradia.Source.Systems.Testing
{
    public class BankFoodModelProxy : DefaultSettlementFoodModel
    {
        private static readonly Dictionary<string, (float amount, double timestamp)> _foodAidMap = new();

        // Tempo máximo de persistência antes da remoção automática
        private const float AID_TTL_DAYS = 5f;

        // ===========================================
        // 🔹 Interface pública — registra ou atualiza ajuda alimentar
        // ===========================================
        public static void RegisterAid(Town town, float amount)
        {
            if (town == null)
                return;

            string id = town.Settlement.StringId;

            // Remove ajuda se o valor for insignificante
            if (amount <= 0.001f)
            {
                _foodAidMap.Remove(id);
                return;
            }

            _foodAidMap[id] = (amount, CampaignTime.Now.ToDays);
        }

        // ===========================================
        // 🔹 Cálculo principal de variação alimentar (hook Bannerlord)
        // ===========================================
        public override ExplainedNumber CalculateTownFoodStocksChange(
            Town town, bool includeDescriptions = false, bool forSimulation = false)
        {
            var result = base.CalculateTownFoodStocksChange(town, includeDescriptions, forSimulation);
            if (town == null)
                return result;

            try
            {
                string id = town.Settlement.StringId;

                // Limpa entradas antigas (tempo limite ultrapassado)
                foreach (var key in _foodAidMap.Keys.ToList())
                {
                    if (CampaignTime.Now.ToDays - _foodAidMap[key].timestamp > AID_TTL_DAYS)
                        _foodAidMap.Remove(key);
                }

                // Aplica ajuda alimentar se existir para esta cidade
                if (_foodAidMap.TryGetValue(id, out var entry))
                {
                    float aidValue = entry.amount;
                    var label = L.T("food_aid_bank_label", "Bank Food Aid");

                    result.Add(aidValue, label);
                }
            }
            catch
            {
                // Silencioso: qualquer erro é ignorado para não travar o modelo
            }

            return result;
        }
    }
}
