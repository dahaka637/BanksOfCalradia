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
//   • Safe warmup (delayed) to avoid first-frame Gauntlet issues
//   • Calls Utils: TradeXP & SuccessionChecker
// ============================================

using System;
using System.Threading.Tasks;
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

        // Evita re-registro de menus em edge-cases
        private bool _menusRegistered;

        // "Warmup" para evitar crash no primeiro acesso (Gauntlet/menus ainda subindo)
        private volatile bool _uiWarmupReady;

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
            if (dataStore == null)
                return;

            if (dataStore.IsSaving)
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    };

                    string json = JsonConvert.SerializeObject(_bankStorage, settings);
                    dataStore.SyncData("Bank.StorageJson", ref json);
                }
                catch
                {
                    // Falha silenciosa em produção
                }
                return;
            }

            // Loading
            string loadedJson = null;
            try
            {
                dataStore.SyncData("Bank.StorageJson", ref loadedJson);
            }
            catch
            {
                loadedJson = null;
            }

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
            if (starter == null)
                return;

            // Evita duplicar em edge cases de bootstrap/reload
            if (_menusRegistered)
                return;

            _menusRegistered = true;

            // Registra menus imediatamente (só registra, não executa lógica pesada)
            // Importante: NÃO dependa de Settlement.CurrentSettlement aqui.
            RegisterAllMenus(starter);

            // Warmup para mitigar crash "primeiro acesso / frame 0"
            _ = WarmupUiAsync();
        }

        // Aquece o estado para evitar interações cedo demais
        private async Task WarmupUiAsync()
        {
            try
            {
                _uiWarmupReady = false;

                // 1) Delay curto (frame 0/1) sei lá meit pra 350
                await Task.Delay(350);

                // 2) Precisa do mínimo do mínimo: campanha/hero - não sei, meti pra 800
                if (!IsBaseEnvironmentReady())
                {
                    await Task.Delay(800);
                }

                // 3) Ainda não pronto? Faz mais um try curto - alterei para mais 800
                if (!IsBaseEnvironmentReady())
                {
                    await Task.Delay(800);
                }

                // Não exige Settlement aqui, porque o jogador pode estar no mapa.
                // Quando entrar em uma cidade, as condições/menu init vão cuidar do resto.
                _uiWarmupReady = IsBaseEnvironmentReady();
            }
            catch
            {
                _uiWarmupReady = false;
            }
        }

        // Ambiente mínimo para liberar UI (sem exigir estar dentro de cidade)
        private bool IsBaseEnvironmentReady()
        {
            try
            {
                if (Campaign.Current == null) return false;
                if (Hero.MainHero == null) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Ambiente completo (dentro de uma cidade real)
        private static bool IsTownEnvironmentReady()
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                if (s == null) return false;
                if (s.Town == null) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RegisterAllMenus(CampaignGameStarter starter)
        {
            // Submenus
            BankMenu_Savings.RegisterMenu(starter, this);
            BankMenu_Loan.RegisterMenu(starter, this);
            BankMenu_LoanPay.RegisterMenu(starter, this);

            // Opção no menu "town"
            starter.AddGameMenuOption(
                "town",
                "visit_bank",
                L.S("visit_option", "Visit the Bank"),
                MenuCondition_SetDynamicLabel,
                _ => BankSafeUI.Switch("bank_menu"),
                isLeave: false
            );

            // Menu principal do banco:
            // Em vez de "texto fixo" (que pode pegar Settlement null e ficar errado pra sempre),
            // usamos init delegate para sempre atualizar corretamente e evitar edge-crashes.
            starter.AddGameMenu(
                "bank_menu",
                L.S("bank_menu_loading", "Loading..."),
                args => OnBankMenuInit(args)
            );

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_savings",
                L.S("open_savings", "Access Savings Account"),
                a =>
                {
                    // Se não estiver em cidade real, nem deixa clicar
                    if (!IsTownEnvironmentReady()) return false;
                    a.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return true;
                },
                _ => BankSafeUI.Switch("bank_savings")
            );

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_loans",
                L.S("open_loans", "Access Loan Services"),
                a =>
                {
                    if (!IsTownEnvironmentReady()) return false;
                    a.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return true;
                },
                _ => BankSafeUI.Switch("bank_loanmenu")
            );

            starter.AddGameMenuOption(
                "bank_menu",
                "bank_back",
                L.S("return_city", "Return to Town"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ => BankSafeUI.Switch("town"),
                isLeave: true
            );
        }

        // Init seguro do menu principal
        private void OnBankMenuInit(MenuCallbackArgs args)
        {
            try
            {
                // Se o warmup ainda não liberou (primeiros frames), mostra texto neutro e pronto.
                // Evita o jogador clicar e estourar menus antes do Gauntlet/estado estar estável.
                if (!_uiWarmupReady)
                {
                    args.MenuTitle = L.T("bank_title_loading", "Bank");
                    BankSafeUI.SetText(args, L.T("bank_loading", "Initializing bank interface...\n\nPlease try again in a moment."));
                    return;
                }

                // Exige cidade real para o menu principal existir de fato
                if (!IsTownEnvironmentReady())
                {
                    args.MenuTitle = L.T("bank_unavailable", "Bank (Unavailable)");
                    BankSafeUI.SetText(args, L.T("bank_need_town", "This menu is only available inside a town."));
                    return;
                }

                // Texto real (com cidade)
                string menuText = GetBankMainMenuText();

                args.MenuTitle = L.T("bank_title", "Bank");
                BankSafeUI.SetText(args, new TaleWorlds.Localization.TextObject(menuText));
            }
            catch
            {
                // Silencioso para não crashar
                try
                {
                    args.MenuTitle = L.T("bank_error", "Bank (Error)");
                    BankSafeUI.SetText(args, L.T("bank_error_desc", "An error occurred while initializing the bank menu."));
                }
                catch { }
            }
        }

        private bool MenuCondition_SetDynamicLabel(MenuCallbackArgs args)
        {
            // ✅ GATE 1: warmup base
            if (!_uiWarmupReady)
                return false;

            // ✅ GATE 2: cidade real
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || settlement.Town == null)
                return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;

            string townName = settlement.Name?.ToString() ?? L.S("default_city", "Town");
            var labelText = L.T("visit_label", "Visit Bank of {CITY}");
            labelText.SetTextVariable("CITY", townName);
            args.Text = labelText;

            return true;
        }

        private string GetBankMainMenuText()
        {
            var s = Settlement.CurrentSettlement;

            // fallback seguro
            if (s == null || s.Town == null)
                return "Bank\n\n(This menu is only available inside a town.)";

            string townName = s.Name?.ToString() ?? L.S("default_city", "Town");

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
                BankSuccessionUtils.CheckAndTransferOwnership(_bankStorage);
            }
            catch
            {
                // Silencioso em produção
            }

            try
            {
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
                _ = json;
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
