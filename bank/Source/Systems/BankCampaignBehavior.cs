// ============================================
// BanksOfCalradia - BankCampaignBehavior.cs
// Author: Dahaka
// Version: 3.0.0 (Ultra Safe Behavior + Health Gate + Warmup Rebuild)
// Description:
//   Core campaign behavior responsible for:
//   • Registering UI menus safely
//   • Loading/saving BankStorage (JSON)
//   • Ultra-safe warmup + storage health gate (prevents random UI crashes)
//   • Daily delegates (TradeXP & SuccessionChecker) guarded
//
//  Key safety features:
//   - Warmup boot (delayed) to avoid first-frame Gauntlet/menu pipeline issues
//   - Storage health validation + rebuild attempt (serialize→deserialize roundtrip)
//   - If health is broken, bank menu shows a safe error message and HIDES Savings/Loans buttons
//   - Strict town gating for menu navigation (no Settlement access during bootstrap)
// ============================================

using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems.Utils;
using BanksOfCalradia.Source.UI;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace BanksOfCalradia.Source.Systems
{
    public class BankCampaignBehavior : CampaignBehaviorBase
    {
        // ------------------------------------------------------------
        // Storage + concurrency guard
        // ------------------------------------------------------------
        private readonly object _storageLock = new object();

        private BankStorage _bankStorage = new BankStorage();

        // Evita re-registro de menus em edge-cases
        private bool _menusRegistered;

        // Warmup para mitigar race-condition (Gauntlet / menu pipeline)
        private volatile bool _uiWarmupReady;

        // Health gate: se storage estiver inconsistente/bugado, desliga botões críticos
        private volatile BankHealthState _healthState = BankHealthState.WarmingUp;
        private volatile string _healthReason = "Warming up...";
        private volatile int _warmupAttemptCount;

        // Tempo real em que o comportamento foi carregado (usado para o gate de 15s)
        private static readonly DateTime _bankBootRealTime = DateTime.UtcNow;

        // ------------------------------------------------------------
        // Health state machine
        // ------------------------------------------------------------
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
        }

        // ------------------------------------------------------------
        // JSON Persistence (Save/Load)
        // ------------------------------------------------------------
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore == null)
                return;

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
                    // silencioso: nunca crasha o save
                    try
                    {
                        string fallback = "{}";
                        dataStore.SyncData("Bank.StorageJson", ref fallback);
                    }
                    catch
                    {
                        // ignora
                    }
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

            // NÃO acessar Settlement/Hero/UI aqui. Só valida estrutura de dados.
            // Se o storage estiver inconsistente, warmup vai tentar rebuild e/ou marcar como Broken.
            try
            {
                var reason = string.Empty;
                var healthOk = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (!healthOk)
                {
                    _healthState = BankHealthState.Broken;
                    _healthReason = string.IsNullOrWhiteSpace(reason) ? "Bank data validation failed during load." : reason;
                }
                else
                {
                    // Ainda deixa como WarmingUp: sessão ainda nem lançou menus.
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

        // ------------------------------------------------------------
        // Menu Registration
        // ------------------------------------------------------------
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (starter == null)
                return;

            if (_menusRegistered)
                return;

            _menusRegistered = true;

            // Apenas registro (sem lógica pesada / sem Settlement)
            RegisterAllMenus(starter);

            // Warmup para mitigar crash de "primeiro acesso"
            _ = WarmupUiAndStorageAsync();
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

            // Menu principal do banco (sempre existe; se health falhar, mostra erro e esconde botões)
            starter.AddGameMenu(
                "bank_menu",
                L.S("bank_menu_loading", "Loading..."),
                OnBankMenuInit
            );

            // Savings
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_savings",
                L.S("open_savings", "Access Savings Account"),
                a =>
                {
                    // HIDE se health não está OK
                    if (!IsSystemFullyReady())
                        return false;

                    // Town real obrigatória
                    if (!IsTownEnvironmentReady())
                        return false;

                    a.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return true;
                },
                _ => BankSafeUI.Switch("bank_savings")
            );

            // Loans
            starter.AddGameMenuOption(
                "bank_menu",
                "bank_loans",
                L.S("open_loans", "Access Loan Services"),
                a =>
                {
                    if (!IsSystemFullyReady())
                        return false;

                    if (!IsTownEnvironmentReady())
                        return false;

                    a.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return true;
                },
                _ => BankSafeUI.Switch("bank_loanmenu")
            );

            // Back
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

        // ------------------------------------------------------------
        // Warmup — UI + Storage health gate
        // ------------------------------------------------------------
        private async Task WarmupUiAndStorageAsync()
        {
            try
            {
                _uiWarmupReady = false;
                _healthState = BankHealthState.WarmingUp;
                _healthReason = "Warming up...";
                _warmupAttemptCount++;

                // Pequeno delay inicial: evita frame 0/1 do pipeline de menus/gauntlet
                await Task.Delay(350);

                // Aguarda campanha/hero (ambiente mínimo), sem exigir cidade
                for (int i = 0; i < 3; i++)
                {
                    if (IsBaseEnvironmentReady())
                        break;

                    await Task.Delay(i == 0 ? 650 : 800);
                }

                _uiWarmupReady = IsBaseEnvironmentReady();

                if (!_uiWarmupReady)
                {
                    _healthState = BankHealthState.WarmingUp;
                    _healthReason = "Campaign not ready yet.";
                    return;
                }

                // "Puxar dados internamente" (aquecimento):
                // 1) valida storage
                // 2) tenta rebuild via roundtrip JSON
                // 3) se falhar -> Broken (menu mostra erro e oculta botões)
                string reason;
                bool ok = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (ok)
                {
                    _healthState = BankHealthState.Healthy;
                    _healthReason = "OK";
                }
                else
                {
                    // Tentativa extra após um pequeno delay (mitiga edge-case de load incompleto)
                    await Task.Delay(250);

                    ok = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);
                    if (ok)
                    {
                        _healthState = BankHealthState.Healthy;
                        _healthReason = "OK (recovered)";
                    }
                    else
                    {
                        _healthState = BankHealthState.Broken;
                        _healthReason = string.IsNullOrWhiteSpace(reason)
                            ? "A critical error was detected while validating bank data."
                            : reason;
                    }
                }

                //-------------------------------------------------------
                //  PREWARM DO BANCO — criação antecipada das contas
                //-------------------------------------------------------
                try
                {
                    if (_healthState == BankHealthState.Healthy)
                    {
                        // Prewarm agora é o ÚNICO responsável por definir Initialized = true
                        await BankPrewarmSystem.RunPrewarmAsync(GetStorage(), Hero.MainHero?.StringId);
                    }
                }
                catch
                {
                    // Falha silenciosa → não quebrar o menu
                }
            }
            catch
            {
                _healthState = BankHealthState.Broken;
                _healthReason = "A critical error was detected while warming up bank data.";
                _uiWarmupReady = false;
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

                // Passo 1: tenta serializar (se falhar, storage está corrompido/incompatível)
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

                // Passo 2: roundtrip parse (rebuild) — detecta membros inválidos e normaliza
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
        // Town menu label condition (Visit Bank of {CITY}) – FULLY SAFE
        // ------------------------------------------------------------
        private bool MenuCondition_SetDynamicLabel(MenuCallbackArgs args)
        {
            try
            {
                // 0) Banco ainda não está totalmente pronto?
                //    → NÃO mostra a opção no menu da cidade.
                if (!IsSystemFullyReady())
                    return false;

                // 1) Cidade real obrigatória (este botão só pode existir dentro de cidade)
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.Town == null)
                    return false;

                // 2) Configuração padrão do botão
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;

                // 3) Nome da cidade
                string townName = settlement.Name?.ToString() ?? L.S("default_city", "Town");

                // 4) Texto dinâmico seguro
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

        // ------------------------------------------------------------
        // Bank Menu init (shows: loading / broken / normal)
        // ------------------------------------------------------------
        private void OnBankMenuInit(MenuCallbackArgs args)
        {
            try
            {
                // Texto base sempre seguro
                args.MenuTitle = L.T("bank_title", "Bank");

                // ------------------------------------------------------------
                // SE O SISTEMA NÃO ESTÁ PRONTO → mostrar tela de loading
                // ------------------------------------------------------------
                if (!IsSystemFullyReady())
                {
                    float remain = GetRemainingBootTime();
                    string secText = remain > 0f
                        ? $"{remain:F1} seconds remaining..."
                        : "Finalizing modules...";

                    var txt = L.T("bank_bootwait",
                        "Loading bank systems...\n\n" +
                        "Please wait while the game initializes internal modules.\n\n" +
                        "{SEC}");

                    txt.SetTextVariable("SEC", secText);

                    args.MenuTitle = L.T("bank_loading", "Bank (Loading)");
                    BankSafeUI.SetText(args, txt);

                    return;
                }

                // 2) Precisa estar em cidade real pra exibir conteúdo real
                if (!IsTownEnvironmentReady())
                {
                    args.MenuTitle = L.T("bank_unavailable", "Bank (Unavailable)");
                    BankSafeUI.SetText(args, L.T("bank_need_town", "This menu is only available inside a town."));
                    return;
                }

                // 3) Storage com problema: exibe erro e NÃO mostra botões (condições retornam false)
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

                // 4) Normal
                BankSafeUI.SetText(args, BuildBankMainMenuTextSafe());
            }
            catch
            {
                try
                {
                    args.MenuTitle = L.T("bank_error", "Bank (Error)");
                    BankSafeUI.SetText(args, L.T("bank_error_desc",
                        "An error occurred while initializing the bank menu."));
                }
                catch
                {
                    // silencioso
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

                string townName = s.Name?.ToString() ?? L.S("default_city", "Town");

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
            // Se o storage estiver quebrado, não roda lógica diária (evita cascata)
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
                // silencioso em produção
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
                // silencioso em produção
            }
        }

        // ------------------------------------------------------------
        // Manual Sync Utility (sanity / debug safe)
        // ------------------------------------------------------------
        public void SyncBankData()
        {
            // Importante: o jogo salva via SyncData; isso aqui é apenas uma
            // validação/estabilização local para reduzir edge-cases e detectar corrupção cedo.
            try
            {
                string reason;
                bool ok = ValidateAndMaybeRebuildStorage(out reason, allowRebuild: true);

                if (!ok)
                {
                    _healthState = BankHealthState.Broken;
                    _healthReason = string.IsNullOrWhiteSpace(reason)
                        ? "Bank data validation failed during runtime."
                        : reason;
                }
            }
            catch
            {
                _healthState = BankHealthState.Broken;
                _healthReason = "Bank data validation failed during runtime.";
            }
        }

        // ------------------------------------------------------------
        // Public Accessor
        // ------------------------------------------------------------
        public BankStorage GetStorage()
        {
            lock (_storageLock)
            {
                _bankStorage ??= new BankStorage();
                return _bankStorage;
            }
        }

        // Optional getter (useful for debug overlays)
        public (bool uiReady, string health, string reason, int attempts) GetHealthSnapshot()
        {
            return (_uiWarmupReady, _healthState.ToString(), _healthReason, _warmupAttemptCount);
        }

        // ------------------------------------------------------------
        // Boot timing helpers (real time, not CampaignTime)
        // ------------------------------------------------------------
        private float GetElapsedBootSeconds()
        {
            try
            {
                return (float)(DateTime.UtcNow - _bankBootRealTime).TotalSeconds;
            }
            catch
            {
                return 9999f; // fallback seguro
            }
        }

        private float GetRemainingBootTime()
        {
            float remain = 15f - GetElapsedBootSeconds();
            if (remain < 0f) remain = 0f;
            return remain;
        }

        public bool IsSystemFullyReady()
        {
            try
            {
                var storage = GetStorage();

                // ============================================================
                // 1) Esperar SEMPRE 15s de tempo REAL de sessão
                //    (não depende de CampaignTime, nem de save)
                // ============================================================
                if (GetElapsedBootSeconds() < 25f)
                    return false;

                // ============================================================
                // 2) Campaign/Hero obrigatórios
                // ============================================================
                if (Campaign.Current == null || Hero.MainHero == null)
                    return false;

                // ============================================================
                // 3) Ambiente de cidade obrigatório
                // ============================================================
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.Town == null)
                    return false;

                // ============================================================
                // 4) UI warmup concluída
                // ============================================================
                if (!_uiWarmupReady)
                    return false;

                // ============================================================
                // 5) Health deve estar OK
                // ============================================================
                if (_healthState != BankHealthState.Healthy)
                    return false;

                // ============================================================
                // 6) Storage deve estar inicializado
                // ============================================================
                if (storage == null || !storage.Initialized)
                    return false;

                // ============================================================
                // 7) Savings devem existir para a cidade atual
                // ============================================================
                string playerId = Hero.MainHero.StringId;
                string townId = settlement.StringId;

                if (!storage.SavingsByPlayer.TryGetValue(playerId, out var list) || list == null)
                    return false;

                bool found = false;
                foreach (var s in list)
                {
                    if (s != null && s.TownId == townId)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;

                // ============================================================
                // 8) Gauntlet precisa ter tela carregada
                // ============================================================
                if (ScreenManager.TopScreen == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
