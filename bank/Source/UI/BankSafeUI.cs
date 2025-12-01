// ============================================
// BanksOfCalradia - BankSafeUI.cs
// Author: Dahaka
// Version: 3.0.0 (Ultra Safe UI + ExecuteAction Shield + Pipeline Finalizers)
// Description:
//   Safety layer for bank menus (bank_*):
//   - Safe Switch / Safe Text write
//   - Strict context gates for bank_* navigation
//   - Harmony patch for GameMenu.SwitchToMenu (bank_* gate)
//   - Harmony patch for GameMenuItemVM.ExecuteAction (swallow bank-related NREs)
//   - Optional Harmony bootstrap that finalizes (swallows) exceptions in menu pipeline
// ============================================

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace BanksOfCalradia.Source.Core
{
    public static class BankSafeUI
    {
        // Cooldown/guard contra double-click e race de transição
        private const int SWITCH_COOLDOWN_MS = 160;
        private const int TRANSITION_RELEASE_MS = 140;

        private static long _lastSwitchTicksUtc;
        private static int _transitionGuard; // 0/1

        // =====================================================================
        // 1. Safe Switch – chama menus sem crashar (bank_* é estrito)
        // =====================================================================
        public static void Switch(string menuId)
        {
            SafeSwitchInternal(menuId);
        }

        private static void SafeSwitchInternal(string menuId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(menuId))
                    return;

                // Context mínimo sempre é exigido para qualquer troca
                if (!IsBaseContextOK())
                    return;

                // Bank_* exige contexto estrito (campanha + hero + town + UI)
                if (IsBankMenuId(menuId) && !IsContextOK())
                    return;

                // Throttle
                long now = DateTime.UtcNow.Ticks;
                long last = Interlocked.Read(ref _lastSwitchTicksUtc);
                if (TicksToMs(now - last) < SWITCH_COOLDOWN_MS)
                    return;

                Interlocked.Exchange(ref _lastSwitchTicksUtc, now);

                // Guard contra reentrância
                if (Interlocked.Exchange(ref _transitionGuard, 1) == 1)
                    return;

                try
                {
                    if (GameStateManager.Current == null || GameStateManager.Current.ActiveStateDisabledByUser)
                        return;

                    GameMenu.SwitchToMenu(menuId);
                }
                catch
                {
                    // Silencioso em produção
                }
                finally
                {
                    ReleaseTransitionGuardLater();
                }
            }
            catch
            {
                // Silencioso
            }
        }

        private static async void ReleaseTransitionGuardLater()
        {
            try
            {
                await Task.Delay(TRANSITION_RELEASE_MS);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _transitionGuard, 0);
            }
        }

        private static long TicksToMs(long ticks)
        {
            // 10.000 ticks = 1ms
            return ticks / 10_000;
        }

        // =====================================================================
        // 2. Safe Text Write – altera texto do menu sem NullRef
        //    (usa contexto mínimo; não exige Settlement para permitir fallback)
        // =====================================================================
        public static void SetText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                if (!IsBaseContextOK())
                    return;

                var ctx = args?.MenuContext;
                var menu = ctx?.GameMenu;
                if (menu == null || text == null)
                    return;

                var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    return;

                field.SetValue(menu, text);
            }
            catch
            {
                // Silencioso
            }
        }

        // =====================================================================
        // 3. IsContextOK — validação completa (strict: exige estar em cidade)
        // =====================================================================
        public static bool IsContextOK()
        {
            try
            {
                if (Campaign.Current == null)
                    return false;

                if (Hero.MainHero == null)
                    return false;

                var st = Settlement.CurrentSettlement;
                if (st == null || st.Town == null)
                    return false;

                if (GameStateManager.Current == null)
                    return false;

                if (GameStateManager.Current.ActiveStateDisabledByUser)
                    return false;

                if (ScreenManager.TopScreen == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Contexto mínimo (não exige Settlement) — usado só em fallback interno.
        public static bool IsBaseContextOK()
        {
            try
            {
                if (GameStateManager.Current == null)
                    return false;

                if (GameStateManager.Current.ActiveStateDisabledByUser)
                    return false;

                if (ScreenManager.TopScreen == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // 4. Inquiry seguro — popups blindados contra race-condition
        // =====================================================================
        public static void Inquiry(InquiryData data)
        {
            try
            {
                if (!IsContextOK())
                    return;

                DelayAction(() =>
                {
                    try
                    {
                        if (!IsContextOK())
                            return;

                        InformationManager.ShowInquiry(data);
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        // =====================================================================
        // 5. DelayAction — aguarda UI estabilizar antes de executar
        // =====================================================================
        private static async void DelayAction(Action action)
        {
            try
            {
                await Task.Delay(75);

                if (!IsBaseContextOK())
                    return;

                action?.Invoke();
            }
            catch
            {
            }
        }

        // Helpers internos para os patches Harmony
        internal static bool IsBankMenuId(string menuId)
        {
            return !string.IsNullOrEmpty(menuId) &&
                   menuId.StartsWith("bank_", StringComparison.Ordinal);
        }

        internal static void NotifySoftFail(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    message = "[BanksOfCalradia] Bank UI recovered from an invalid state. Please try again.";

                InformationManager.DisplayMessage(
                    new InformationMessage(
                        message,
                        Color.FromUint(0xFFFF6666)
                    )
                );
            }
            catch
            {
            }
        }

        // =====================================================================
        // ExecuteAction detection helpers
        // =====================================================================
        internal static bool LooksBankRelatedFromVm(object vm, out string hintId)
        {
            hintId = null;

            try
            {
                // 1) Tenta extrair qualquer string "bank_*" diretamente do VM (Id, StringId, etc.)
                if (TryExtractAnyBankString(vm, out hintId))
                    return true;

                // 2) Tenta pegar o menu atual
                if (TryGetActiveMenuId(out var menuId) && IsBankMenuId(menuId))
                {
                    hintId = menuId;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryExtractAnyBankString(object obj, out string found)
        {
            found = null;
            if (obj == null)
                return false;

            var t = obj.GetType();

            // Properties
            try
            {
                var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var p in props)
                {
                    if (p == null || p.GetIndexParameters().Length != 0)
                        continue;

                    if (p.PropertyType == typeof(string))
                    {
                        string v = null;
                        try { v = p.GetValue(obj) as string; } catch { }
                        if (IsBankMenuId(v))
                        {
                            found = v;
                            return true;
                        }
                    }

                    // Se tiver algum objeto que contenha string "bank_*" dentro (GameMenuOption, etc.)
                    object sub = null;
                    try { sub = p.GetValue(obj); } catch { }
                    if (sub != null && sub != obj && TryExtractAnyBankString(sub, out found))
                        return true;
                }
            }
            catch
            {
            }

            // Fields
            try
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var f in fields)
                {
                    if (f == null)
                        continue;

                    if (f.FieldType == typeof(string))
                    {
                        string v = null;
                        try { v = f.GetValue(obj) as string; } catch { }
                        if (IsBankMenuId(v))
                        {
                            found = v;
                            return true;
                        }
                    }

                    object sub = null;
                    try { sub = f.GetValue(obj); } catch { }
                    if (sub != null && sub != obj && TryExtractAnyBankString(sub, out found))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetActiveMenuId(out string menuId)
        {
            menuId = null;

            try
            {
                var camp = Campaign.Current;
                if (camp == null)
                    return false;

                // Use reflection to avoid API differences between game versions
                object gmMgr = null;

                try
                {
                    var p1 = camp.GetType().GetProperty("GameMenuManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p1 != null) gmMgr = p1.GetValue(camp);
                }
                catch { }

                if (gmMgr == null)
                {
                    try
                    {
                        var f1 = camp.GetType().GetField("_gameMenuManager", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (f1 != null) gmMgr = f1.GetValue(camp);
                    }
                    catch { }
                }

                if (gmMgr == null)
                    return false;

                object ctx = null;

                // Common names
                ctx = TryGetMemberValue(gmMgr, "CurrentMenuContext");
                if (ctx == null) ctx = TryGetMemberValue(gmMgr, "MenuContext");
                if (ctx == null) ctx = TryGetMemberValue(gmMgr, "CurrentGameMenuContext");

                if (ctx == null)
                    return false;

                object gm = TryGetMemberValue(ctx, "GameMenu");
                if (gm == null)
                    return false;

                // Common id accessors
                var sid = TryGetMemberValue(gm, "StringId") as string;
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    menuId = sid;
                    return true;
                }

                sid = TryGetMemberValue(gm, "_id") as string;
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    menuId = sid;
                    return true;
                }

                sid = TryGetMemberValue(gm, "_stringId") as string;
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    menuId = sid;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static object TryGetMemberValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return null;

            var t = obj.GetType();

            try
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetIndexParameters().Length == 0)
                    return p.GetValue(obj);
            }
            catch
            {
            }

            try
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                    return f.GetValue(obj);
            }
            catch
            {
            }

            return null;
        }
    }

    // ============================================================================
    // 6. PATCH HARMONY — SwitchToMenu (gate principal dos menus bank_*)
    // ============================================================================
    [HarmonyPatch(typeof(GameMenu), "SwitchToMenu")]
    public static class Patch_SwitchToMenu_Safe
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(string menuId)
        {
            try
            {
                // Libera tudo que não é menu do banco
                if (!BankSafeUI.IsBankMenuId(menuId))
                    return true;

                // Para menus bank_* exige contexto seguro
                if (!BankSafeUI.IsContextOK())
                    return false;

                return true;
            }
            catch
            {
                // Em qualquer erro no patch, não bloqueia o jogo
                return true;
            }
        }
    }

    // ============================================================================
    // 6.1 PATCH HARMONY — GameMenuItemVM.ExecuteAction (shield)
    //
    // Problema típico em crash logs:
    //   Exception occurred inside invoke: ExecuteAction
    //   Target type: GameMenuItemVM
    //   Inner message: Object reference not set...
    //
    // Este patch:
    //   - Só engole exception quando parecer "bank-related"
    //   - Mantém comportamento normal para menus vanilla
    // ============================================================================
    [HarmonyPatch(typeof(GameMenuItemVM), "ExecuteAction")]
    public static class Patch_GameMenuItemVM_ExecuteAction_Safe
    {
        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        static Exception Finalizer(Exception __exception, GameMenuItemVM __instance)
        {
            try
            {
                if (__exception == null)
                    return null;

                // Se nem contexto mínimo existe, deixa o jogo lidar (não mascarar)
                if (!BankSafeUI.IsBaseContextOK())
                    return __exception;

                // Só engole se for relacionado ao bank_*
                if (!BankSafeUI.LooksBankRelatedFromVm(__instance, out var hint))
                    return __exception;

                // Engole + aviso leve (sem travar loop)
                BankSafeUI.NotifySoftFail(
                    "[BanksOfCalradia] A bank action was blocked due to an unstable UI state. " +
                    "Please open the bank again and try once more."
                );

                return null;
            }
            catch
            {
                return __exception;
            }
        }
    }

    // ============================================================================
    // 7. BOOTSTRAP — patches extras para pipeline de menus (Init/Condition/etc.)
    //
    // Objetivo:
    //   - Se algum método do pipeline de menus estourar exception
    //   - E esse pipeline estiver atendendo um menu bank_*
    //   - Então engolir a exception e aplicar fallback,
    //     em vez de deixar o jogo crashar.
    //
    // ============================================================================
    public static class BankSafeUIHarmonyBootstrap
    {
        // Chame isso depois do harmony.PatchAll() no SubModule.
        public static int InstallExtraPatches(Harmony harmony)
        {
            if (harmony == null)
                return 0;

            int patched = 0;

            try
            {
                patched += PatchAllMenuPipelineMethods(harmony, typeof(GameMenu));
            }
            catch
            {
            }

            try
            {
                patched += PatchAllMenuPipelineMethods(harmony, typeof(GameMenuOption));
            }
            catch
            {
            }

            return patched;
        }

        // Critérios para candidatos do pipeline:
        //  - métodos de instância
        //  - possuem MenuCallbackArgs nos parâmetros
        //  - nomes típicos: Init, Condition, Consequence, Run
        private static int PatchAllMenuPipelineMethods(Harmony harmony, Type targetType)
        {
            int count = 0;

            var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var m in methods)
            {
                try
                {
                    if (m == null || m.IsAbstract)
                        continue;

                    if (m.IsSpecialName)
                        continue;

                    if (m.ContainsGenericParameters)
                        continue;

                    var ps = m.GetParameters();
                    if (ps == null || ps.Length == 0)
                        continue;

                    bool hasMenuArgs = ps.Any(p => p != null && p.ParameterType == typeof(MenuCallbackArgs));
                    if (!hasMenuArgs)
                        continue;

                    var name = m.Name ?? string.Empty;
                    bool looksLikePipeline =
                        name.IndexOf("Init", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Condition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Consequence", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!looksLikePipeline)
                        continue;

                    var prefixMi = typeof(BankSafeUIHarmonyPatches).GetMethod(
                        nameof(BankSafeUIHarmonyPatches.GenericPrefix),
                        BindingFlags.Static | BindingFlags.Public
                    );

                    var finalizerMi = typeof(BankSafeUIHarmonyPatches).GetMethod(
                        nameof(BankSafeUIHarmonyPatches.GenericFinalizer),
                        BindingFlags.Static | BindingFlags.Public
                    );

                    if (prefixMi == null || finalizerMi == null)
                        continue;

                    var prefix = new HarmonyMethod(prefixMi) { priority = Priority.First };
                    var finalizer = new HarmonyMethod(finalizerMi) { priority = Priority.Last };

                    harmony.Patch(m, prefix: prefix, finalizer: finalizer);
                    count++;
                }
                catch
                {
                    // Se algum método não puder ser patchado, ignoramos
                }
            }

            return count;
        }
    }

    // ============================================================================
    // 8. PATCHES GENÉRICOS — prefix + finalizer
    // ============================================================================
    public static class BankSafeUIHarmonyPatches
    {
        [HarmonyPriority(Priority.First)]
        public static void GenericPrefix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            // NO-OP em produção.
        }

        [HarmonyPriority(Priority.Last)]
        public static Exception GenericFinalizer(Exception __exception, object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                if (__exception == null)
                    return null;

                if (!TryGetMenuId(__instance, __args, out var menuId))
                    return __exception;

                if (!BankSafeUI.IsBankMenuId(menuId))
                    return __exception;

                TryApplyFallbackText(__args, menuId);

                // Engole exception para menus bank_*
                return null;
            }
            catch
            {
                return __exception;
            }
        }

        private static bool TryGetMenuId(object instance, object[] args, out string menuId)
        {
            menuId = null;

            try
            {
                // Caso 1: MenuCallbackArgs nos argumentos
                if (args != null)
                {
                    foreach (var a in args)
                    {
                        if (a is MenuCallbackArgs mca)
                        {
                            var ctx = mca.MenuContext;
                            var gm = ctx?.GameMenu;
                            if (gm != null && TryGetGameMenuIdFromGameMenu(gm, out menuId))
                                return true;
                        }

                        if (a is string s && BankSafeUI.IsBankMenuId(s))
                        {
                            menuId = s;
                            return true;
                        }
                    }
                }

                // Caso 2: instance é GameMenu
                if (instance is GameMenu gm2)
                {
                    if (TryGetGameMenuIdFromGameMenu(gm2, out menuId))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetGameMenuIdFromGameMenu(object gameMenu, out string menuId)
        {
            menuId = null;

            try
            {
                var t = gameMenu.GetType();

                var p = t.GetProperty("StringId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(gameMenu) as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        menuId = v;
                        return true;
                    }
                }

                var f1 = t.GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f1 != null)
                {
                    var v = f1.GetValue(gameMenu) as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        menuId = v;
                        return true;
                    }
                }

                var f2 = t.GetField("_stringId", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f2 != null)
                {
                    var v = f2.GetValue(gameMenu) as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        menuId = v;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void TryApplyFallbackText(object[] args, string menuId)
        {
            try
            {
                if (args == null)
                    return;

                var mca = args.FirstOrDefault(a => a is MenuCallbackArgs) as MenuCallbackArgs;
                if (mca == null)
                    return;

                if (!BankSafeUI.IsBaseContextOK())
                    return;

                var title = new TextObject("Bank");
                var body = new TextObject(
                    "The bank interface encountered an unexpected state and was safely recovered.\n\n" +
                    "If this keeps happening, reopen the bank and try again."
                );

                try { mca.MenuTitle = title; } catch { }

                try
                {
                    var gm = mca.MenuContext?.GameMenu;
                    if (gm == null)
                        return;

                    var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field == null)
                        return;

                    field.SetValue(gm, body);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }
    }
}
