// ============================================
// BanksOfCalradia - BankTradeXpUtils.cs
// Author: Dahaka
// Version: 2.1.0 (XP Curve + Production Ready)
// Description:
//   Daily Trade XP based on bank profits.
//   Includes soft-cap XP curve to prevent abuse.
// ============================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Systems.Utils
{
    public static class BankTradeXpUtils
    {
        // Fator base ajustado
        private const double TradeXpMultiplier = 0.15d;

        public static void ApplyDailyTradeXp(BankStorage storage)
        {
            try
            {
                var hero = Hero.MainHero;
                if (hero == null || hero.HeroDeveloper == null)
                    return;

                if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                    accounts == null || accounts.Count == 0)
                    return;

                double totalDailyGain = 0d;

                // Calcula lucros diários
                foreach (var acc in accounts)
                {
                    if (acc.Amount <= 0.01)
                        continue;

                    var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);
                    if (settlement?.Town == null)
                        continue;

                    double prosperity = settlement.Town.Prosperity;

                    const double fator = 250d;
                    const double prosperidadeBase = 7000d;

                    double raw = prosperity / prosperidadeBase;
                    double suav = 0.5d + raw * 0.5d;
                    double taxaAnual = prosperity / fator * suav;
                    double taxaDiaria = taxaAnual / 120d;

                    double lucro = acc.Amount * (taxaDiaria / 100d);
                    totalDailyGain += lucro;
                }

                if (totalDailyGain <= 0d)
                    return;

                // Cálculo de XP (base)
                double logComponent = Math.Log10(totalDailyGain / 2d + 10d);
                double damp = 1d / (1d + (totalDailyGain / 12000d));

                double xpRaw =
                    Math.Pow(logComponent, 0.85d) *
                    (totalDailyGain * TradeXpMultiplier * 0.8d * damp);

                float xpToAdd = (float)Math.Max(0d, xpRaw);

                if (xpToAdd <= 0f)
                    return;

                // ============================================================
                // Aplicação da CURVA DE AMORTECIMENTO (soft cap dinâmico)
                // ============================================================
                if (xpToAdd > 10f)
                {
                    // Fórmula sigmoidal leve
                    double softFactor = Math.Pow(10d / (10d + xpToAdd), 0.25d);

                    xpToAdd = (float)(xpToAdd * softFactor);
                }

                // Aplica XP final
                hero.HeroDeveloper.AddSkillXp(
                    DefaultSkills.Trade,
                    xpToAdd,
                    isAffectedByFocusFactor: true,
                    shouldNotify: true
                );
            }
            catch
            {
                // silencioso em produção
            }
        }
    }
}
