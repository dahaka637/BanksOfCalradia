// ============================================
// BanksOfCalradia - BankTradeXpUtils.cs
// Author: Dahaka
// Version: 1.1.0 (Double Safe Precision)
// Description:
//   Handles daily Trade XP gain based on banking profits
//   Now fully compatible with double-precision investments.
// ============================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Systems.Utils
{
    public static class BankTradeXpUtils
    {
        private const double TradeXpMultiplier = 0.00029d;

        public static void ApplyDailyTradeXp(BankStorage storage)
        {
            try
            {
                var hero = Hero.MainHero;
                if (hero == null || hero.Clan != Clan.PlayerClan)
                    return;

                if (!storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts) ||
                    accounts == null || accounts.Count == 0)
                    return;

                double totalDailyGain = 0d;

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
                    double rawSuavizador = prosperity / prosperidadeBase;
                    double fatorSuavizador = 0.5d + rawSuavizador * 0.5d;
                    double taxaAnual = prosperity / fator * fatorSuavizador;
                    double taxaDiaria = taxaAnual / 120d;

                    totalDailyGain += acc.Amount * (taxaDiaria / 100d);
                }

                if (totalDailyGain <= 1d)
                    return;

                double logComponent = Math.Log10(totalDailyGain / 2d + 10d);
                double damp = 1d / (1d + (totalDailyGain / 12000d));
                double xpRaw = Math.Pow(logComponent, 0.85d) * (totalDailyGain * TradeXpMultiplier * 0.8d * damp);

                // Conversão final para float (Trade XP usa float internamente)
                float xpToAdd = (float)Math.Max(0d, xpRaw);

                if (xpToAdd >= 0.1f)
                {
                    hero.AddSkillXp(DefaultSkills.Trade, xpToAdd);
                }
            }
            catch (Exception ex)
            {
                var msg = L.T("trade_xp_error", "[BanksOfCalradia][Trade XP Error] {ERROR}");
                msg.SetTextVariable("ERROR", ex.Message);
                InformationManager.DisplayMessage(
                    new InformationMessage(msg.ToString(), Color.FromUint(0xFFFF5555))
                );
            }
        }
    }
}
