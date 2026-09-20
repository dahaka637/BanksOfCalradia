// ============================================
// BanksOfCalradia - SubModule.cs
// Author: Dahaka
// Version: 2.2.0 (Sandbox crash fix: finance model no longer Harmony-patched)
// Description:
//   Core initialization for Banks of Calradia.
//
//   • Loads Harmony patches (+ SafeUI fallback layer)
//   • Registers campaign behaviors and models
//   • DOES NOT register any menus (menus are behavior-owned)
// ============================================

using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Processing;

namespace BanksOfCalradia.Source
{
    public class SubModule : MBSubModuleBase
    {
        private bool _bootMessageShown;

        // ============================================================
        // (0) Harmony + SafeUI bootstrap
        // ============================================================
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            try
            {
                var harmony = new Harmony("BanksOfCalradia.Patches");
                harmony.PatchAll();
            }
            catch
            {
                // silencioso
            }
        }

        // ============================================================
        // (1) Game start (Campaign only)
        // ============================================================
        protected override void OnGameStart(Game game, IGameStarter starter)
        {
            base.OnGameStart(game, starter);

            if (game?.GameType is not Campaign ||
                starter is not CampaignGameStarter campaignStarter)
                return;

            try
            {
                // --------------------------------------------------------
                // Behavior central (menus + storage + warmup)
                // --------------------------------------------------------
                campaignStarter.AddBehavior(new BankCampaignBehavior());

                // --------------------------------------------------------
                // Processadores do banco
                // --------------------------------------------------------
                campaignStarter.AddBehavior(new BankLoanProcessor());

                // --------------------------------------------------------
                // Modelos necessários
                // --------------------------------------------------------
                campaignStarter.AddModel(new BankProsperityModel());
                campaignStarter.AddModel(new BankFoodModelProxy());
                campaignStarter.AddModel(new BankClanFinanceModel());
            }
            catch
            {
                // silencioso
            }
        }

        // ============================================================
        // (2) Boot message (once)
        // ============================================================
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
