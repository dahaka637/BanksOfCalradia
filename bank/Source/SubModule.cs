// ============================================
// BanksOfCalradia - SubModule.cs
// Author: Dahaka
// Version: 2.2.0 (Production Release + Finance Fallback)
// Description:
//   Initialization of all core systems for
//   the Banks of Calradia mod.
//   Handles behaviors, models, menus and
//   localization-safe notifications.
// ============================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

// Internal systems
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Processing;
using BanksOfCalradia.Source.UI;

// Test proxy
using BanksOfCalradia.Source.Systems.Testing;

namespace BanksOfCalradia.Source
{
    public class SubModule : MBSubModuleBase
    {
        private bool _bootMessageShown;

        protected override void OnGameStart(Game game, IGameStarter starter)
        {
            base.OnGameStart(game, starter);

            if (game?.GameType is not Campaign || starter is not CampaignGameStarter campaignStarter)
                return;

            try
            {
                // ============================================================
                // 🔧 (1) Proxy do modelo de comida
                // ============================================================
                campaignStarter.AddModel(new BankFoodModelProxy());

                // ============================================================
                // (2) Core persistence and behaviors
                // ============================================================
                var bankBehavior = new BankCampaignBehavior();
                campaignStarter.AddBehavior(bankBehavior);

                // ============================================================
                // (3) Core models and processors
                // ============================================================
                campaignStarter.AddModel(new FinanceProcessor());
                campaignStarter.AddBehavior(new BankLoanProcessor());
                campaignStarter.AddModel(new BankProsperityModel());

                // 🔸 Novo: fallback autodefensivo do sistema financeiro
                campaignStarter.AddBehavior(new BankFinanceFallbackBehavior());

                // ============================================================
                // (4) Bank menus
                // ============================================================
                BankMenu_Savings.RegisterMenu(campaignStarter, bankBehavior);
                BankMenu_Loan.RegisterMenu(campaignStarter, bankBehavior);
                BankMenu_LoanPay.RegisterMenu(campaignStarter, bankBehavior);

                // ============================================================
                // (5) Log de inicialização (localização segura)
                // ============================================================
            }
            catch (Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[BanksOfCalradia][ERROR] Initialization failed: " + e.Message,
                    Color.FromUint(0xFFFF6666)
                ));
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            if (_bootMessageShown)
                return;

            _bootMessageShown = true;

            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    L.S("bank_mod_boot_ok", "[BanksOfCalradia] Mod loaded successfully."),
                    Color.FromUint(0xFFBBAA00)
                ));
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[BanksOfCalradia] Mod loaded.",
                    Color.FromUint(0xFFBBAA00)
                ));
            }
        }
    }
}
