// ============================================
// BanksOfCalradia - BankCampaignBehavior.cs
// Author: Dahaka
// Version: 2.6.0 (Boot-Safe + Cleanup)
// Description:
//   Manages initialization, persistence, and menus for
//   the Banks of Calradia system using native localization.
//
//   • Savings (deposits & withdrawals)
//   • Loans (requests & payments)
//   • Auto JSON persistence
//   • Trade XP gain based on daily banking profits
// ============================================

using System;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.UI;                 // Menu_Savings, Menu_Loan, Menu_LoanPayments
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
        private const float TradeXpMultiplier = 0.00025f;

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
            {
                string json = null;
                dataStore.SyncData("Bank.StorageJson", ref json);

                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var settings = new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore,
                            MissingMemberHandling = MissingMemberHandling.Ignore
                        };

                        _bankStorage = JsonConvert.DeserializeObject<BankStorage>(json, settings) ?? new BankStorage();
                    }
                    catch
                    {
                        // Avoid InformationManager during load phase
                        _bankStorage = new BankStorage();
                    }
                }
                else
                {
                    _bankStorage = new BankStorage();
                }
            }
        }

        // ============================================
        // Menu Registration (Main Bank Menu)
        // ============================================
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Register menu sets (ensure types exist under BanksOfCalradia.Source.UI)
            BankMenu_Savings.RegisterMenu(starter, this);
            BankMenu_Loan.RegisterMenu(starter, this);
            BankMenu_LoanPay.RegisterMenu(starter, this);

            // Add "Visit Bank" option to town menu
            starter.AddGameMenuOption(
                "town",
                "visit_bank",
                L.S("visit_option", "Visit the Bank"),
                MenuCondition_SetDynamicLabel,
                _ => GameMenu.SwitchToMenu("bank_menu"),
                isLeave: false
            );

            // Main Bank Menu
            starter.AddGameMenu("bank_menu", GetBankMainMenuText(), null);

            // Savings
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_savings",
                L.S("open_savings", "Access Savings Account"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => GameMenu.SwitchToMenu("bank_savings"),
                isLeave: false
            );

            // Loans
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_loans",
                L.S("open_loans", "Access Loan Services"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => GameMenu.SwitchToMenu("bank_loanmenu"),
                isLeave: false
            );

            // Return
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_back",
                L.S("return_city", "Return to Town"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => GameMenu.SwitchToMenu("town"),
                isLeave: true
            );
        }

        // ============================================
        // Dynamic “Visit Bank of {CITY}” Label
        // ============================================
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

        // ============================================
        // Main Menu Text (Localized)
        // ============================================
        private string GetBankMainMenuText()
        {
            var s = Settlement.CurrentSettlement;
            string townName = s?.Name?.ToString() ?? L.S("default_city", "Town");

            var text = L.T(
                "menu_text",
                "Bank of {CITY}\n\nWelcome to the city's bank.\n\nChoose an option below to manage your finances:\n\n- Access Savings Account\n- Loan Services\n- Return to Town"
            );

            text.SetTextVariable("CITY", townName);
            return text.ToString();
        }

        // ============================================
        // Daily Trade XP from Banking Profits
        // ============================================
        private void OnDailyTick()
        {
            var hero = Hero.MainHero;
            if (hero == null || hero.Clan != Clan.PlayerClan)
                return;

            ApplyDailyTradeXp(hero);
        }

        private void ApplyDailyTradeXp(Hero hero)
        {
            try
            {
                var storage = _bankStorage;
                if (storage == null)
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

                // 🟡 Soft cap: reduz XP para lucros muito altos (curva suave e progressiva)
                float damp = 1f / (1f + (totalDailyGain / 8000f)); // 8k = ponto médio de amortecimento

                // ⚙️ XP final balanceada com curva de redução
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

                InformationManager.DisplayMessage(new InformationMessage(
                    msg.ToString(),
                    Color.FromUint(0xFFFF5555)
                ));
            }
        }

        // ============================================
        // Public Accessors
        // ============================================
        public BankStorage GetStorage() => _bankStorage ??= new BankStorage();

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
                _ = json; // Bannerlord persists via IDataStore; aqui mantemos consistência
            }
            catch
            {
                // Silencioso em release
            }
        }
    }
}
