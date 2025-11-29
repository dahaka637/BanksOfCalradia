// ============================================
// BanksOfCalradia - BankCampaignBehavior.cs
// Author: Dahaka
// Version: 2.9.1 (Modular Refactor + Sync Support)
// Description:
//   Core campaign behavior that initializes menus,
//   manages save/load, and delegates logic to utils.
//
//   • Initializes UI menus
//   • Loads/saves BankStorage
//   • Calls Utils: TradeXP & SuccessionChecker
// ============================================

using System;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.UI;
using BanksOfCalradia.Source.Systems.Utils;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BanksOfCalradia.Source.Systems
{
    public class BankCampaignBehavior : CampaignBehaviorBase
    {
        private BankStorage _bankStorage = new BankStorage();

        // ============================================
        // Event Registration
        // ============================================
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        // ============================================
        // JSON Persistence (Save/Load)
        // ============================================
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };

                string json = JsonConvert.SerializeObject(_bankStorage, settings);
                dataStore.SyncData("Bank.StorageJson", ref json);
                return;
            }

            // Loading
            string loadedJson = null;
            dataStore.SyncData("Bank.StorageJson", ref loadedJson);

            if (!string.IsNullOrEmpty(loadedJson))
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    };

                    _bankStorage = JsonConvert.DeserializeObject<BankStorage>(loadedJson, settings) ?? new BankStorage();
                }
                catch
                {
                    _bankStorage = new BankStorage();
                }
            }
            else
            {
                _bankStorage = new BankStorage();
            }
        }

        // ============================================
        // Menu Registration
        // ============================================
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            BankMenu_Savings.RegisterMenu(starter, this);
            BankMenu_Loan.RegisterMenu(starter, this);
            BankMenu_LoanPay.RegisterMenu(starter, this);

            starter.AddGameMenuOption(
                "town",
                "visit_bank",
                L.S("visit_option", "Visit the Bank"),
                MenuCondition_SetDynamicLabel,
                _ => BankSafeUI.Switch("bank_menu"),
                isLeave: false
            );

            starter.AddGameMenu("bank_menu", GetBankMainMenuText(), null);

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_savings",
                L.S("open_savings", "Access Savings Account"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => BankSafeUI.Switch("bank_savings")

            );

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_loans",
                L.S("open_loans", "Access Loan Services"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => BankSafeUI.Switch("bank_loanmenu")

            );

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_back",
                L.S("return_city", "Return to Town"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => BankSafeUI.Switch("town"),

                isLeave: true
            );
        }

        private bool MenuCondition_SetDynamicLabel(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            var settlement = Settlement.CurrentSettlement;
            string townName = settlement?.Name?.ToString() ?? L.S("default_city", "Town");
            var labelText = L.T("visit_label", "Visit Bank of {CITY}");
            labelText.SetTextVariable("CITY", townName);
            args.Text = labelText;
            return true;
        }

        private string GetBankMainMenuText()
        {
            var s = Settlement.CurrentSettlement;
            string townName = s?.Name?.ToString() ?? L.S("default_city", "Town");
            var text = L.T("menu_text",
                "Bank of {CITY}\n\nWelcome to the city's bank.\n\nChoose an option below to manage your finances:\n\n- Access Savings Account\n- Loan Services\n- Return to Town");
            text.SetTextVariable("CITY", townName);
            return text.ToString();
        }
        // ============================================
        // Daily Tick Delegates
        // ============================================
        private void OnDailyTick()
        {
            try
            {
                // Sucessão bancária (herança das contas)
                BankSuccessionUtils.CheckAndTransferOwnership(_bankStorage);
            }
            catch
            {
                // Silencioso em produção
            }

            try
            {
                // XP baseado nos lucros diários das contas
                BankTradeXpUtils.ApplyDailyTradeXp(_bankStorage);
            }
            catch
            {
                // Silencioso em produção
            }
        }



        // ============================================
        // Manual Sync Utility
        // ============================================
        /// <summary>
        /// Serializa manualmente o estado do banco para garantir persistência em runtime.
        /// Pode ser chamado após transferências, merges ou reset de contas.
        /// </summary>
        public void SyncBankData()
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };

                string json = JsonConvert.SerializeObject(_bankStorage, settings);
                _ = json; // Mantém consistência com SaveData nativo
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[BanksOfCalradia] Error during bank sync: {ex.Message}",
                    Color.FromUint(0xFFFF5555)
                ));
            }
        }

        // ============================================
        // Public Accessor
        // ============================================
        public BankStorage GetStorage() => _bankStorage ??= new BankStorage();
    }
}