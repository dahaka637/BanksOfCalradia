using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace BanksOfCalradia.Source.Core
{
    public static class BankSafeUI
    {
        // =====================================================================
        // 1. Safe Switch – chama menus do banco sem crashar
        // =====================================================================
        public static void Switch(string menuId)
        {
            try
            {
                if (!IsContextOK())
                    return;

                if (string.IsNullOrEmpty(menuId))
                    return;

                if (GameStateManager.Current == null ||
                    GameStateManager.Current.ActiveStateDisabledByUser)
                {
                    return;
                }

                GameMenu.SwitchToMenu(menuId);
            }
            catch
            {
                // Silencioso em produção – nunca crasha o jogo
            }
        }

        // =====================================================================
        // 2. Safe Text Write – altera texto do menu sem NullRef
        // =====================================================================
        public static void SetText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                if (!IsContextOK())
                    return;

                var ctx = args?.MenuContext;
                if (ctx == null)
                    return;

                var menu = ctx.GameMenu;
                if (menu == null)
                    return;

                var field = typeof(GameMenu).GetField(
                    "_defaultText",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );

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
                        // Silencioso
                    }
                });
            }
            catch
            {
                // Silencioso
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

                if (!IsContextOK())
                    return;

                action?.Invoke();
            }
            catch
            {
                // Silencioso
            }
        }

        // Helpers internos para os patches Harmony
        internal static bool IsBankMenuId(string menuId)
        {
            return !string.IsNullOrEmpty(menuId) &&
                   menuId.StartsWith("bank_", StringComparison.Ordinal);
        }
    }

    // ============================================================================
    // 6. PATCH HARMONY — SwitchToMenu (gate principal dos menus bank_*)
    // ============================================================================
    [HarmonyPatch(typeof(GameMenu), "SwitchToMenu")]
    public static class Patch_SwitchToMenu_Safe
    {
        static bool Prefix(string menuId)
        {
            try
            {
                // Libera tudo que não é menu do banco
                if (!BankSafeUI.IsBankMenuId(menuId))
                    return true;

                // Para menus bank_* exige contexto seguro
                if (!BankSafeUI.IsContextOK())
                {
                    // Bloqueia a mudança de menu, evitando crash
                    return false;
                }

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
    // 7. BOOTSTRAP — patches extras para pipeline de menus (Init/Condition/etc.)
    //
    //    Objetivo:
    //      - Se algum método do pipeline de menus estourar exception
    //      - E esse pipeline estiver atendendo um menu bank_*
    //      - Então engolir a exception e aplicar fallback no texto do banco,
    //        em vez de deixar o jogo crashar.
    //
    //    Só atua em bank_* e não toca em town ou menus vanilla.
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
                // Silencioso
            }

            try
            {
                patched += PatchAllMenuPipelineMethods(harmony, typeof(GameMenuOption));
            }
            catch
            {
                // Silencioso
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
                if (m == null || m.IsAbstract)
                    continue;

                if (m.IsSpecialName)
                    continue;

                var ps = m.GetParameters();
                if (ps == null || ps.Length == 0)
                    continue;

                bool hasMenuArgs = ps.Any(p => p.ParameterType == typeof(MenuCallbackArgs));
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

                try
                {
                    var prefix = new HarmonyMethod(typeof(BankSafeUIHarmonyPatches).GetMethod(
                        nameof(BankSafeUIHarmonyPatches.GenericPrefix),
                        BindingFlags.Static | BindingFlags.Public
                    ));

                    var finalizer = new HarmonyMethod(typeof(BankSafeUIHarmonyPatches).GetMethod(
                        nameof(BankSafeUIHarmonyPatches.GenericFinalizer),
                        BindingFlags.Static | BindingFlags.Public
                    ));

                    harmony.Patch(m, prefix: prefix, finalizer: finalizer);
                    count++;
                }
                catch
                {
                    // Silencioso – se algum método não puder ser patchado, ignoramos
                }
            }

            return count;
        }
    }

    // ============================================================================
    // 8. PATCHES GENÉRICOS — prefix + finalizer
    //    Funcionam para qualquer assinatura usando __instance / __args.
    //    Prefix aqui é NO-OP (não altera nada), finalizer é quem engole
    //    exceptions para menus bank_*.
    // ============================================================================
    public static class BankSafeUIHarmonyPatches
    {
        // Prefix genérico: não interfere em nada, deixamos como NO-OP.
        public static void GenericPrefix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            // Intencionalmente vazio em produção.
        }

        // Finalizer genérico:
        //  - Se não há exception → retorna null (sem alteração)
        //  - Se há exception:
        //      - Se menu é bank_* → engole (retorna null) e aplica fallback
        //      - Se não é bank_* → devolve exception (comportamento normal)
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
                // Se algo falhar na proteção, não mascarar erros genéricos
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
                            if (ctx != null)
                            {
                                var gm = ctx.GameMenu;
                                if (gm != null && TryGetGameMenuIdFromGameMenu(gm, out menuId))
                                    return true;
                            }
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
                    "If this keeps happening, try opening the bank again after a moment."
                );

                try
                {
                    mca.MenuTitle = title;
                }
                catch
                {
                }

                try
                {
                    var ctx = mca.MenuContext;
                    var gm = ctx?.GameMenu;
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
