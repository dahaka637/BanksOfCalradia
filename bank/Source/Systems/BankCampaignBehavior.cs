// ============================================
// BanksOfCalradia - BankCampaignBehavior.cs
// Author: Dahaka
// Version: 3.0.6 (Per-Campaign Menu Register HARD-GUARANTEE)
// Description:
//   Core campaign behavior responsible for:
//   • Registering UI menus safely (per campaign; NO duplication)
//   • Loading/saving BankStorage (JSON)
//   • Warmup + storage health gate (prevents random UI crashes)
//   • Daily delegates guarded
//
// Key guarantee in this version:
//   - Menu registration runs EXACTLY ONCE per BankCampaignBehavior instance.
//   - No static flags.
//   - No reliance on "starter uniqueness".
//   - Prevents duplicated buttons even if OnSessionLaunched fires multiple times.
//
// Notes:
//   - Do NOT call RegisterAllMenus from anywhere else.
//   - If you have multiple BankCampaignBehavior instances registered by mistake
//     (e.g., added twice in SubModule), you will still get duplicates. This file
//     guarantees no duplicates per instance.
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems.Utils;
using BanksOfCalradia.Source.UI;
using Newtonsoft.Json;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Systems
{
    public class BankCampaignBehavior : CampaignBehaviorBase
    {
        // ------------------------------------------------------------
        // Storage + concurrency guard
        // ------------------------------------------------------------
        private readonly object _storageLock = new object();
        private BankStorage _bankStorage = new BankStorage();

        // ------------------------------------------------------------
        // Menu registration (PER INSTANCE, NO STATIC)
        // ------------------------------------------------------------
        private readonly object _menuRegLock = new object();
        private bool _menusRegistered;

        // Warmup/health gate (MAIN THREAD only)
        private volatile bool _uiWarmupReady;
        private volatile BankHealthState _healthState = BankHealthState.WarmingUp;
        private volatile string _healthReason = "Warming up...";
        private volatile int _warmupAttemptCount;

        // Warmup step control
        private bool _warmupCompleted;
        private float _tickAccumulator;

        private enum BankHealthState
        {
            WarmingUp = 0,
            Healthy = 1,
            Broken = 2
        }

        // ------------------------------------------------------------
        // Event Registration
        // ------------------------------------------------------------
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);

            // IMPORTANT: warmup/health/prewarm runs only on main thread (TickEvent)
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        // ------------------------------------------------------------
        // JSON Persistence (Save/Load)
        // ------------------------------------------------------------
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore == null)
                return;

            // ============================================================
            // SAVE
            // ============================================================
            if (dataStore.IsSaving)
            {
                try
                {
                    string json;
                    lock (_storageLock)
                    {
                        json = JsonConvert.SerializeObject(GetStorage(), BuildJsonSettings());
                    }

                    dataStore.SyncData("Bank.StorageJson", ref json);
                }
                catch
                {
                    try
                    {
                        string fallback = "{}";
                        dataStore.SyncData("Bank.StorageJson", ref fallback);
                    }
                    catch
                    {
                        // silent
                    }
                }

                return;
            }

            // ============================================================
            // LOAD -> reset do sistema (warmup/health)
            // ============================================================
            SafeResetWarmupState();

            // ============================================================
            // LOAD JSON
            // ============================================================
            string loadedJson = null;
            try
            {
                dataStore.SyncData("Bank.StorageJson", ref loadedJson);
            }
            catch
            {
                loadedJson = null;
            }

            bool ok = false;

            if (!string.IsNullOrEmpty(loadedJson))
            {
                try
                {
                    var settings = BuildJsonSettings();
                    var loaded = JsonConvert.DeserializeObject<BankStorage>(loadedJson, settings);

                    lock (_storageLock)
                    {
                        _bankStorage = loaded ?? new BankStorage();
                    }

                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            }

            if (!ok)
            {
                lock (_storageLock)
                {
                    _bankStorage = new BankStorage();
                }
            }

            // ============================================================
            // Validação / rebuild (ainda no load)
            // ============================================================
            try
            {
                string reason;
                var healthOk = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (!healthOk)
                {
                    _healthState = BankHealthState.Broken;
                    _healthReason = string.IsNullOrWhiteSpace(reason)
                        ? "Bank data validation failed during load."
                        : reason;
                }
                else
                {
                    _healthState = BankHealthState.WarmingUp;
                    _healthReason = "Loaded. Waiting warmup...";
                }
            }
            catch
            {
                _healthState = BankHealthState.Broken;
                _healthReason = "Bank data validation failed during load.";
            }
        }

        private static JsonSerializerSettings BuildJsonSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
        }

        private void SafeResetWarmupState()
        {
            try
            {
                _uiWarmupReady = false;
                _warmupCompleted = false;
                _tickAccumulator = 0f;

                _healthState = BankHealthState.WarmingUp;
                _healthReason = "Warming up...";
                _warmupAttemptCount = 0;
            }
            catch
            {
                // silent
            }
        }

        // ------------------------------------------------------------
        // Menu Registration (HARD GUARANTEE: ONCE PER INSTANCE)
        // ------------------------------------------------------------
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (starter == null)
                return;

            // HARD GUARANTEE:
            // If Bannerlord fires OnSessionLaunched multiple times for the same behavior instance,
            // we register menus only once, preventing duplicated buttons.
            lock (_menuRegLock)
            {
                if (_menusRegistered)
                    return;

                _menusRegistered = true;
            }

            // Reset warmup ONLY on the first session launch for this instance.
            SafeResetWarmupState();

            RegisterAllMenus(starter);
        }

        private void RegisterAllMenus(CampaignGameStarter starter)
        {
            // Submenus
            BankMenu_Savings.RegisterMenu(starter, this);
            BankMenu_Loan.RegisterMenu(starter, this);
            BankMenu_LoanPay.RegisterMenu(starter, this);

            // Menu principal do banco
            starter.AddGameMenu(
                "bank_menu",
                L.S("bank_menu_loading", "Loading..."),
                OnBankMenuInit
            );

            // Opção no menu "town"
            starter.AddGameMenuOption(
                "town",
                "visit_bank",
                L.S("visit_option", "Visit the Bank"),
                MenuCondition_VisitBank,
                _ =>
                {
                    try { GameMenu.SwitchToMenu("bank_menu"); } catch { }
                },
                isLeave: false
            );

            // Savings
            starter.AddGameMenuOption(
                "bank_menu",
                "open_bank_savings",
                L.S("open_savings", "Access Savings Account"),
                MenuCondition_OpenSavings,
                _ =>
                {
                    try { GameMenu.SwitchToMenu("bank_savings"); } catch { }
                },
                isLeave: false
            );

            // Loans
            starter.AddGameMenuOption(
                "bank_menu",
                "open_bank_loans",
                L.S("open_loans", "Access Loan Services"),
                MenuCondition_OpenLoans,
                _ =>
                {
                    try { GameMenu.SwitchToMenu("bank_loanmenu"); } catch { }
                },
                isLeave: false
            );

            // Back
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_back",
                L.S("return_city", "Return to Town"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    a.IsEnabled = true;
                    a.Tooltip = null;
                    return true;
                },
                _ =>
                {
                    try { GameMenu.SwitchToMenu("town"); } catch { }
                },
                isLeave: true
            );
        }

        // ------------------------------------------------------------
        // Tick Warmup (MAIN THREAD ONLY) — no async, no tasks
        // ------------------------------------------------------------
        private void OnTick(float dt)
        {
            try
            {
                _tickAccumulator += dt;
                if (_tickAccumulator < 0.25f)
                    return;

                _tickAccumulator = 0f;

                if (_warmupCompleted)
                    return;

                if (!IsBaseEnvironmentReady())
                {
                    _uiWarmupReady = false;
                    _healthState = BankHealthState.WarmingUp;
                    _healthReason = "Campaign not ready yet.";
                    return;
                }

                _uiWarmupReady = true;

                if (_healthState == BankHealthState.Broken)
                {
                    _warmupCompleted = true;
                    return;
                }

                _warmupAttemptCount++;

                string reason;
                bool ok = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (!ok)
                {
                    _healthState = BankHealthState.Broken;
                    _healthReason = string.IsNullOrWhiteSpace(reason) ? "Bank data validation failed." : reason;
                    _warmupCompleted = true;
                    return;
                }

                TryEnsureRuntimeInitForCurrentTown();

                _healthState = BankHealthState.Healthy;
                _healthReason = "OK";
                _warmupCompleted = true;
            }
            catch
            {
                _healthState = BankHealthState.Broken;
                _healthReason = "Critical error during warmup tick.";
                _uiWarmupReady = false;
                _warmupCompleted = true;
            }
        }

        private void TryEnsureRuntimeInitForCurrentTown()
        {
            try
            {
                var hero = Hero.MainHero;
                var settlement = Settlement.CurrentSettlement;

                if (hero == null || settlement == null || settlement.Town == null)
                    return;

                string playerId = hero.StringId;
                string townId = settlement.StringId;

                if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(townId))
                    return;

                lock (_storageLock)
                {
                    var storage = GetStorage();
                    if (storage == null)
                        return;

                    storage.Initialized = true;

                    var acct = storage.GetOrCreateSavings(playerId, townId);
                    if (acct == null)
                        return;

                    if (acct.Amount < 0)
                        acct.Amount = 0;
                }
            }
            catch
            {
                // silent
            }
        }

        private bool ValidateAndMaybeRebuildStorage(out string reason, bool allowRebuild)
        {
            reason = null;

            try
            {
                BankStorage snapshot;
                lock (_storageLock)
                {
                    snapshot = _bankStorage ?? new BankStorage();
                }

                string json;
                try
                {
                    json = JsonConvert.SerializeObject(snapshot, BuildJsonSettings());
                }
                catch (Exception ex)
                {
                    reason = "Bank data serialization failed: " + ex.Message;
                    return false;
                }

                if (!allowRebuild)
                    return true;

                try
                {
                    var rebuilt = JsonConvert.DeserializeObject<BankStorage>(json, BuildJsonSettings());
                    if (rebuilt == null)
                    {
                        reason = "Bank data rebuild produced null storage.";
                        return false;
                    }

                    lock (_storageLock)
                    {
                        _bankStorage = rebuilt;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    reason = "Bank data rebuild failed: " + ex.Message;
                    return false;
                }
            }
            catch
            {
                reason = "Bank data validation failed.";
                return false;
            }
        }

        // ------------------------------------------------------------
        // Menu Conditions
        // ------------------------------------------------------------
        private bool MenuCondition_VisitBank(MenuCallbackArgs args)
        {
            try
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;

                if (!IsTownEnvironmentReady())
                {
                    args.IsEnabled = false;
                    args.Tooltip = L.T("tt_need_town", "You must be inside a town to access the bank.");
                    return true;
                }

                args.IsEnabled = true;

                if (!IsCoreReadyForSubFeatures())
                    args.Tooltip = L.T("tt_initializing", "The bank system is still initializing.");
                else
                    args.Tooltip = null;

                var settlement = Settlement.CurrentSettlement;
                string townName = settlement != null && settlement.Name != null
                    ? settlement.Name.ToString()
                    : L.S("default_city", "Town");

                var labelText = L.T("visit_label", "Visit Bank of {CITY}");
                labelText.SetTextVariable("CITY", townName);
                args.Text = labelText;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool MenuCondition_OpenSavings(MenuCallbackArgs a)
        {
            try
            {
                a.optionLeaveType = GameMenuOption.LeaveType.Submenu;

                if (!IsTownEnvironmentReady())
                {
                    a.IsEnabled = false;
                    a.Tooltip = L.T("tt_need_town", "You must be inside a town to access the bank.");
                    return true;
                }

                if (!IsSystemFullyReady())
                {
                    a.IsEnabled = false;
                    a.Tooltip = L.T("tt_initializing", "The bank system is still initializing.");
                    return true;
                }

                a.IsEnabled = true;
                a.Tooltip = null;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool MenuCondition_OpenLoans(MenuCallbackArgs a)
        {
            try
            {
                a.optionLeaveType = GameMenuOption.LeaveType.Submenu;

                if (!IsTownEnvironmentReady())
                {
                    a.IsEnabled = false;
                    a.Tooltip = L.T("tt_need_town", "You must be inside a town to access the bank.");
                    return true;
                }

                if (!IsSystemFullyReady())
                {
                    a.IsEnabled = false;
                    a.Tooltip = L.T("tt_initializing", "The bank system is still initializing.");
                    return true;
                }

                a.IsEnabled = true;
                a.Tooltip = null;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        // Bank Menu init (shows: loading / broken / normal)
        // ------------------------------------------------------------
        private void OnBankMenuInit(MenuCallbackArgs args)
        {
            try
            {
                args.MenuTitle = L.T("bank_title", "Bank");

                if (!IsTownEnvironmentReady())
                {
                    args.MenuTitle = L.T("bank_unavailable", "Bank (Unavailable)");
                    BankSafeUI.SetText(args, L.T("bank_need_town", "This menu is only available inside a town."));
                    return;
                }

                if (_healthState == BankHealthState.Broken)
                {
                    args.MenuTitle = L.T("bank_error", "Bank (Error)");

                    var txt = L.T("bank_broken_desc",
                        "A critical error was detected while initializing the bank system.\n\n" +
                        "Savings and Loans were disabled to prevent crashes.\n\n" +
                        "Please contact the mod author and include your game version, mod list, and a crash report.\n\n" +
                        "Details: {REASON}");

                    txt.SetTextVariable("REASON", string.IsNullOrWhiteSpace(_healthReason) ? "Unknown" : _healthReason);
                    BankSafeUI.SetText(args, txt);
                    return;
                }

                if (!IsCoreReadyForSubFeatures())
                {
                    string secText = "Finalizing modules...";

                    var txt = L.T("bank_bootwait",
                        "Loading bank systems...\n\n" +
                        "Please wait while the game initializes internal modules.\n\n" +
                        "{SEC}");

                    txt.SetTextVariable("SEC", secText);

                    args.MenuTitle = L.T("bank_loading", "Bank (Loading)");
                    BankSafeUI.SetText(args, txt);
                    return;
                }

                BankSafeUI.SetText(args, BuildBankMainMenuTextSafe());
            }
            catch
            {
                try
                {
                    args.MenuTitle = L.T("bank_error", "Bank (Error)");
                    BankSafeUI.SetText(args, L.T("bank_error_desc", "An error occurred while initializing the bank menu."));
                }
                catch
                {
                    // silent
                }
            }
        }

        private TextObject BuildBankMainMenuTextSafe()
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                if (s == null || s.Town == null)
                    return new TextObject("Bank\n\n(This menu is only available inside a town.)");

                string townName = s.Name != null ? s.Name.ToString() : L.S("default_city", "Town");

                var text = L.T("menu_text",
                    "Bank of {CITY}\n\n" +
                    "Welcome to the city's bank.\n\n" +
                    "Choose an option below to manage your finances:\n\n" +
                    "- Access Savings Account\n" +
                    "- Loan Services\n" +
                    "- Return to Town");

                text.SetTextVariable("CITY", townName);
                return text;
            }
            catch
            {
                return new TextObject("Bank\n\n(Unable to load bank menu text.)");
            }
        }

        // ------------------------------------------------------------
        // Daily Tick Delegates
        // ------------------------------------------------------------
        private void OnDailyTick()
        {
            if (_healthState == BankHealthState.Broken)
                return;

            try
            {
                lock (_storageLock)
                {
                    BankSuccessionUtils.CheckAndTransferOwnership(GetStorage());
                }
            }
            catch
            {
                // silent
            }

            try
            {
                lock (_storageLock)
                {
                    BankTradeXpUtils.ApplyDailyTradeXp(GetStorage());
                }
            }
            catch
            {
                // silent
            }
        }

        // ------------------------------------------------------------
        // Public Accessor
        // ------------------------------------------------------------
        public BankStorage GetStorage()
        {
            lock (_storageLock)
            {
                if (_bankStorage == null)
                    _bankStorage = new BankStorage();
                return _bankStorage;
            }
        }

        public (bool uiReady, string health, string reason, int attempts) GetHealthSnapshot()
        {
            return (_uiWarmupReady, _healthState.ToString(), _healthReason, _warmupAttemptCount);
        }

        // ------------------------------------------------------------
        // Environment checks
        // ------------------------------------------------------------
        private static bool IsBaseEnvironmentReady()
        {
            try
            {
                return Campaign.Current != null && Hero.MainHero != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTownEnvironmentReady()
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                return s != null && s.Town != null;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        // Readiness gates
        // ------------------------------------------------------------
        private bool IsCoreReadyForSubFeatures()
        {
            try
            {
                if (!IsBaseEnvironmentReady())
                    return false;

                if (!_uiWarmupReady)
                    return false;

                if (!_warmupCompleted)
                    return false;

                if (_healthState != BankHealthState.Healthy)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        // Manual Storage Sync (SAFE – main thread only)
        // ------------------------------------------------------------
        public void SyncBankData()
        {
            try
            {
                if (_healthState == BankHealthState.Broken)
                    return;

                string reason;
                bool ok = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (!ok)
                {
                    _healthState = BankHealthState.Broken;
                    _healthReason = string.IsNullOrWhiteSpace(reason)
                        ? "Bank data validation failed during manual sync."
                        : reason;
                }
            }
            catch
            {
                _healthState = BankHealthState.Broken;
                _healthReason = "Critical error during manual bank data sync.";
            }
        }

        public bool IsSystemFullyReady()
        {
            try
            {
                if (!IsCoreReadyForSubFeatures())
                    return false;

                if (!IsTownEnvironmentReady())
                    return false;

                TryEnsureRuntimeInitForCurrentTown();

                lock (_storageLock)
                {
                    var storage = GetStorage();
                    if (storage == null || !storage.Initialized)
                        return false;

                    var hero = Hero.MainHero;
                    var settlement = Settlement.CurrentSettlement;
                    if (hero == null || settlement == null)
                        return false;

                    var acct = storage.GetOrCreateSavings(hero.StringId, settlement.StringId);
                    if (acct == null)
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
