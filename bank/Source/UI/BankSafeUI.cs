using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Core
{
    public static class BankSafeUI
    {
        // =====================================================================
        // 1. Safe Switch 2.0 – Arroz com feijão, mas à prova de qualquer estado
        // =====================================================================
        public static void Switch(string menuId)
        {
            try
            {
                // Proteção: se o jogo não estiver em estado válido, não tenta trocar
                if (!IsContextOK())
                    return;

                if (string.IsNullOrEmpty(menuId))
                    return;

                // Proteção extra: evita crash durante loading/cutscene/transição
                if (GameStateManager.Current == null ||
                    GameStateManager.Current.ActiveStateDisabledByUser)
                {
                    return;
                }

                GameMenu.SwitchToMenu(menuId);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[BanksOfCalradia] Menu switch blocked by safety wrapper.",
                    Color.FromUint(0xFFFF5555)
                ));
            }
        }

        // =====================================================================
        // 2. Safe Text Write – Não escreve texto até Gauntlet estar pronto
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
                // silencioso e seguro
            }
        }

        // =====================================================================
        // 3. IsContextOK – versão reforçada
        // =====================================================================
        public static bool IsContextOK()
        {
            try
            {
                // Hero inválido = sem contexto jogável
                if (Hero.MainHero == null)
                    return false;

                // Se não estamos em cidade, não deve abrir menus do banco
                var st = Settlement.CurrentSettlement;
                if (st == null || st.Town == null)
                    return false;

                // Proteção: estado do jogo deve existir
                if (GameStateManager.Current == null)
                    return false;

                // Proteção: se o estado estiver bloqueado por UI ou transições
                if (GameStateManager.Current.ActiveStateDisabledByUser)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // 4. Inquiry – invoca só quando for seguro
        // =====================================================================
        public static void Inquiry(InquiryData data)
        {
            try
            {
                // Nunca abrir popups durante loading ou cutscene
                if (!IsContextOK())
                    return;

                DelayAction(() =>
                {
                    try
                    {
                        // Última verificação safety antes de exibir popup
                        if (!IsContextOK())
                            return;

                        InformationManager.ShowInquiry(data);
                    }
                    catch
                    {
                        // proteção silenciosa
                    }
                });
            }
            catch
            {
                // bloqueia crashes silenciosamente
            }
        }

        // =====================================================================
        // 5. DelayAction – reforçado, filtra ações perigosas
        // =====================================================================
        private static async void DelayAction(Action action)
        {
            try
            {
                // Delay mínimo para garantir inicialização de UI
                await System.Threading.Tasks.Task.Delay(75);

                // Só executa se contexto ainda for seguro
                if (!IsContextOK())
                    return;

                action?.Invoke();
            }
            catch
            {
                // silencioso, nunca crasha
            }
        }
    }
}
