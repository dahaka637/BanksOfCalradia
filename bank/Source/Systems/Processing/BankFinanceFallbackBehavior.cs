// ============================================
// BanksOfCalradia - BankFinanceFallbackBehavior.cs
// Author: Dahaka
// Version: 1.1 (Self-Defensive Fallback • Gold Fix)
// Description:
//   Garante que o jogador receba juros bancários mesmo
//   se outro mod substituir o ClanFinanceModel padrão.
//   - Usa ChangeHeroGold() para compatibilidade total
//   - Evita duplicações e conflitos
// ============================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Core;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankFinanceFallbackBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTickClan(Clan clan)
        {
            try
            {
                if (clan == null || clan != Clan.PlayerClan)
                    return;

                // Se o modelo ativo já é o nosso, não faz nada
                if (FinanceProcessor.IsActiveModel())
                    return;

                int interest = FinanceProcessor.CalculateStandaloneDailyInterest();
                if (interest <= 0)
                    return;

                var hero = Hero.MainHero;
                hero?.ChangeHeroGold(interest);

#if DEBUG
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[BanksOfCalradia][FinanceFallback] Applied bank interest: {interest}",
                    Color.FromUint(0xFF88D8AA)));
#endif
            }
            catch
            {
                // silencioso: nunca quebrar o tick diário
            }
        }
    }
}
