// ============================================
// BanksOfCalradia - BankTradeXpUtils.cs
// Author: Dahaka
// Version: 1.0.0
// Description:
//   Handles daily Trade XP gain based on banking profits.
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
        private const float TradeXpMultiplier = 0.00025f;

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

                float totalDailyGain = 0f;

                foreach (var acc in accounts)
                {
                    if (acc.Amount <= 0.01f)
                        continue;

                    var settlement = Campaign.Current?.Settlements?.Find(s => s.StringId == acc.TownId);
                    if (settlement?.Town == null)
                        continue;

                    float prosperity = settlement.Town.Prosperity;

                    const float fator = 250f;
                    const float prosperidadeBase = 7000f;
                    float rawSuavizador = prosperity / prosperidadeBase;
                    float fatorSuavizador = 0.5f + rawSuavizador * 0.5f;
                    float taxaAnual = prosperity / fator * fatorSuavizador;
                    float taxaDiaria = taxaAnual / 120f;

                    totalDailyGain += acc.Amount * (taxaDiaria / 100f);
                }

                if (totalDailyGain <= 1f)
                    return;

                float logComponent = MathF.Log10(totalDailyGain / 2f + 10f);
                float damp = 1f / (1f + (totalDailyGain / 8000f));
                float xpRaw = MathF.Pow(logComponent, 0.85f) * (totalDailyGain * TradeXpMultiplier * 0.8f * damp);

                if (xpRaw >= 0.1f)
                {
                    hero.AddSkillXp(DefaultSkills.Trade, xpRaw);
                }
            }
            catch (Exception ex)
            {
                var msg = L.T("trade_xp_error", "[BanksOfCalradia][Trade XP Error] {ERROR}");
                msg.SetTextVariable("ERROR", ex.Message);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(0xFFFF5555)));
            }
        }
    }
}
