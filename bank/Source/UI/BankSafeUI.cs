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
        // -----------------------------------------
        // 1. Safe Switch
        // -----------------------------------------
        public static void Switch(string menuId)
        {
            try
            {
                if (!string.IsNullOrEmpty(menuId))
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

        // -----------------------------------------
        // 2. Safe Text Write
        // -----------------------------------------
        public static void SetText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args?.MenuContext?.GameMenu;
                if (menu == null)
                    return;

                var field = typeof(GameMenu).GetField("_defaultText",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                    field.SetValue(menu, text);
            }
            catch
            {
                // falha silenciosa, não causa crash
            }
        }

        // -----------------------------------------
        // 3. Safe Context Getter
        // -----------------------------------------
        public static bool IsContextOK()
        {
            try
            {
                if (Hero.MainHero == null)
                    return false;

                if (Settlement.CurrentSettlement == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // -----------------------------------------
        // 4. Safe Inquiry
        // -----------------------------------------
        public static void Inquiry(InquiryData data)
        {
            try
            {
                // delay 1 frame para garantir que Gauntlet inicializou
                DelayAction(delegate { InformationManager.ShowInquiry(data); });
            }
            catch
            {
                // bloqueia crashes quando UI não está pronta
            }
        }

        private static async void DelayAction(Action action)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(50);
                action?.Invoke();
            }
            catch { }
        }
    }
}
