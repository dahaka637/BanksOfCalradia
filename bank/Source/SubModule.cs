// ============================================
// BanksOfCalradia - SubModule.cs
// Author: Dahaka
// Version: 2.2.2 (Production + Full Model Restore)
// Description:
//   Core initialization for Banks of Calradia.
//
//   • Loads Harmony patches (+ SafeUI fallback layer)
//   • Registers behaviors, models, menus
//   • Displays boot messages (localized)
// ============================================

using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Processing;
using BanksOfCalradia.Source.UI;

namespace BanksOfCalradia.Source
{
    public class SubModule : MBSubModuleBase
    {
        private bool _bootMessageShown;

        // ============================================================
        // (0) Carrega Harmony + camada de segurança SafeUI
        // ============================================================
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            try
            {
                var harmony = new Harmony("BanksOfCalradia.Patches");
                harmony.PatchAll();

                // Proteção extra
                BankSafeUIHarmonyBootstrap.InstallExtraPatches(harmony);
            }
            catch
            {
                // silencioso
            }
        }

        // ============================================================
        // (1) Inicialização de Campanha
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
                // (1) Behavior central + persistência
                // --------------------------------------------------------
                var bankBehavior = new BankCampaignBehavior();
                campaignStarter.AddBehavior(bankBehavior);

                // --------------------------------------------------------
                // (2) Processadores do Banco
                // --------------------------------------------------------
                campaignStarter.AddBehavior(new BankLoanProcessor());

                // ========================================================
                // (3) MODELOS QUE PRECISAM EXISTIR PARA FUNCIONAR
                // ========================================================

                // 🔥 RESTAURADO: Sem isso não existe ProsperityGain
                campaignStarter.AddModel(new BankProsperityModel());

                // 🔥 RESTAURADO: Sem isso FoodAid nunca aparece no ExplainedNumber
                campaignStarter.AddModel(new BankFoodModelProxy());

                // ⚠️ NÃO ADICIONAR FinanceProcessor -> substituído pelo fallback
                // ⚠️ NÃO ADICIONAR FoodAid antigo (esse abaixo já faz tudo)
                // ========================================================

                // --------------------------------------------------------
                // (4) Menus
                // --------------------------------------------------------
                BankMenu_Savings.RegisterMenu(campaignStarter, bankBehavior);
                BankMenu_Loan.RegisterMenu(campaignStarter, bankBehavior);
                BankMenu_LoanPay.RegisterMenu(campaignStarter, bankBehavior);
            }
            catch
            {
                // silencioso
            }
        }

        // ============================================================
        // (2) Mensagem de Boot
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
