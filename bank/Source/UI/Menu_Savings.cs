// ============================================
// BanksOfCalradia - BankMenu_Savings.cs
// Author: Dahaka
// Version: 2.6.0 (Double Precision + Visual Fix)
// Description:
//   Savings interface with fixed options and
//   dynamic withdraw fee based on town economy.
//   - Full double precision (supports trillions)
//   - Clean screen (interest + balance)
//   - Deposit and withdraw submenus
//   - Quick buttons (100 → 10,000,000)
//   - Deposit all / Withdraw all
//   - Localized texts via helper L
//   - Crash-safe (null checks, reflection guarded)
// ============================================

using System;
using System.Reflection;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.UI
{
    public static class BankMenu_Savings
    {
        // Quick deposit / withdraw fixed values
        private static readonly int[] QuickValues = { 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000 };

        // ============================================================
        // Helpers de contexto
        // ============================================================
        private static bool TryGetContext(
            BankCampaignBehavior behavior,
            out Hero hero,
            out Settlement settlement,
            out string playerId,
            out string townId)
        {
            hero = Hero.MainHero;
            settlement = Settlement.CurrentSettlement;

            if (behavior == null || behavior.GetStorage() == null)
            {
                ShowWarn(L.S("savings_err_storage", "[BanksOfCalradia] Bank storage unavailable."));
                playerId = "player";
                townId = "town";
                return false;
            }

            if (hero == null)
            {
                ShowWarn(L.S("savings_err_hero", "[BanksOfCalradia] Player not found."));
                playerId = "player";
                townId = "town";
                return false;
            }

            playerId = hero.StringId;
            townId = settlement?.StringId ?? "town";
            return true;
        }

        private static void SafeSetMenuText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args?.MenuContext?.GameMenu;
                if (menu == null) return;

                var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(menu, text);
            }
            catch
            {
                // silencioso
            }
        }

        private static void SafeSwitchToMenu(string menuId)
        {
            try
            {
                if (!string.IsNullOrEmpty(menuId))
                    GameMenu.SwitchToMenu(menuId);
            }
            catch
            {
                // silencioso
            }
        }

        private static void SafeSync(BankCampaignBehavior behavior)
        {
            try { behavior?.SyncBankData(); } catch { }
        }

        // ============================================================
        // Dynamic withdraw fee (Reversed Risk Curve – Balanced Final)
        // ============================================================
        private static float GetDynamicWithdrawFee(Settlement settlement)
        {
            if (settlement?.Town == null)
                return 0.02f; // fallback 2 %

            float prosperity = settlement.Town.Prosperity;
            float security = settlement.Town.Security;
            float loyalty = settlement.Town.Loyalty;

            const float MinFee = 0.0000f;
            const float MaxFee = 0.1000f;
            const float ProsperityRef = 10000f;
            const float RiskGamma = 1.30f;

            const float wP = 0.55f;
            const float wS = 0.30f;
            const float wL = 0.15f;

            float pRisk = 1f - MathF.Clamp(prosperity / ProsperityRef, 0f, 1f);
            float sRisk = MathF.Clamp((100f - security) / 100f, 0f, 1f);
            float lRisk = MathF.Clamp((100f - loyalty) / 100f, 0f, 1f);

            float combined = (pRisk * wP) + (sRisk * wS) + (lRisk * wL);
            float curved = MathF.Pow(MathF.Clamp(combined, 0f, 1f), RiskGamma);
            float fee = MinFee + (MaxFee - MinFee) * curved;

            float stability = 1f - combined;
            float rebate = MathF.Pow(stability, 4f) * 0.002f;
            fee = MathF.Max(MinFee, fee - rebate);

            return MathF.Clamp(fee, MinFee, MaxFee);
        }

        // ============================================
        // Register savings menus
        // ============================================
        public static void RegisterMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            // Main savings menu
            starter.AddGameMenu(
                "bank_savings",
                L.S("savings_menu_loading", "Loading savings data..."),
                args => OnMenuInit(args, behavior)
            );

            // Main options
            starter.AddGameMenuOption("bank_savings", "savings_deposit",
                L.S("savings_menu_deposit", "Deposit Money"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => SafeSwitchToMenu("bank_savings_deposit"), false);

            starter.AddGameMenuOption("bank_savings", "savings_withdraw",
                L.S("savings_menu_withdraw", "Withdraw Money"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => SafeSwitchToMenu("bank_savings_withdraw"), false);

            starter.AddGameMenuOption("bank_savings", "savings_toggle_reinvest",
                L.S("savings_toggle_reinvest", "Toggle Auto-Reinvestment"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => ToggleAutoReinvest(behavior), false);

            starter.AddGameMenuOption("bank_savings", "savings_back",
                L.S("savings_menu_back", "Return to Bank"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_menu"), true);

            RegisterDepositMenu(starter, behavior);
            RegisterWithdrawMenu(starter, behavior);
        }

        // ============================================
        // Main savings menu
        // ============================================
        private static void OnMenuInit(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetContext(behavior, out var hero, out var settlement, out var playerId, out var townId))
                {
                    SafeSetMenuText(args, new TextObject(L.S("savings_err_ctx", "[BanksOfCalradia] Context not available.")));
                    return;
                }

                string townName = settlement?.Name?.ToString() ?? L.S("default_city", "City");
                float prosperity = settlement?.Town?.Prosperity ?? 0f;

                // 💹 Curva Calibrada Premium (mantida igual)
                const float prosperidadeBase = 5000f;
                const float prosperidadeAlta = 6000f;
                const float prosperidadeMax = 10000f;
                const float CICLO_DIAS = 120f;

                prosperity = MathF.Max(prosperity, 1f);
                float rawSuavizador = prosperidadeBase / prosperity;
                float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);
                float pobrezaRatio = MathF.Max(0f, (prosperidadeBase - prosperity) / prosperidadeBase);
                float incentivoPobreza = MathF.Pow(pobrezaRatio, 1.05f) * 0.15f;
                float penalidadeRiqueza = 0f;

                if (prosperity > prosperidadeAlta)
                {
                    float excesso = (prosperity - prosperidadeAlta) / (prosperidadeMax - prosperidadeAlta);
                    excesso = MathF.Max(0f, excesso);
                    penalidadeRiqueza = MathF.Pow(excesso, 1f) * 0.025f;
                }

                float taxaBase = 6.5f + MathF.Pow(prosperidadeBase / prosperity, 0.45f) * 6.0f;
                taxaBase *= (1.0f + incentivoPobreza - penalidadeRiqueza);

                float ajusteLog = 1.0f / (1.0f + (prosperity / 25000.0f));
                float taxaAnual = taxaBase * (0.95f + ajusteLog * 0.15f);
                taxaAnual = MathF.Round(taxaAnual, 2);
                float taxaDiaria = taxaAnual / CICLO_DIAS;

                float withdrawRate = GetDynamicWithdrawFee(settlement);
                var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
                if (acct.Amount < 0) acct.Amount = 0;
                double balance = acct.Amount;

                string reinvestStatus = acct.AutoReinvest
                    ? L.S("savings_reinvest_on", "Enabled")
                    : L.S("savings_reinvest_off", "Disabled");

                var body = L.T("savings_menu_body",
                    "Savings — Bank of {CITY}\n\n" +
                    "• Annual interest rate: {INTEREST_AA}\n" +
                    "• Daily interest rate: {INTEREST_AD}\n" +
                    "• Local prosperity: {PROSPERITY}\n" +
                    "• Withdraw fee: {WITHDRAW_FEE}\n" +
                    "• Current balance: {BALANCE}\n" +
                    "• Auto-Reinvestment: {REINVEST}\n");

                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("INTEREST_AA", BankUtils.FmtPct(taxaAnual / 100f));
                body.SetTextVariable("INTEREST_AD", BankUtils.FmtPct(taxaDiaria / 100f));
                body.SetTextVariable("PROSPERITY", prosperity.ToString("0"));
                body.SetTextVariable("WITHDRAW_FEE", BankUtils.FmtPct(withdrawRate));
                body.SetTextVariable("BALANCE", BankUtils.FmtDenars(balance));
                body.SetTextVariable("REINVEST", reinvestStatus);

                args.MenuTitle = body;
                SafeSetMenuText(args, body);
            }
            catch (Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    L.S("savings_menu_error", "[BanksOfCalradia] Error loading savings menu: ") + e.Message,
                    Color.FromUint(0xFFFF3333)
                ));
            }
        }

        // ============================================
        // Deposit submenu
        // ============================================
        private static void RegisterDepositMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            starter.AddGameMenu(
                "bank_savings_deposit",
                L.S("savings_deposit_loading", "Loading..."),
                args =>
                {
                    if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                    {
                        SafeSetMenuText(args, new TextObject(L.S("savings_err_ctx", "Context not available.")));
                        return;
                    }

                    double savingsBalance = behavior.GetStorage().GetOrCreateSavings(playerId, townId).Amount;
                    var title = L.T("savings_deposit_title", "Deposit to Savings — Bank balance: {BALANCE}");
                    title.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(savingsBalance));
                    args.MenuTitle = title;

                    var body = L.T("savings_deposit_body",
                        "Select a fixed amount to deposit or use 'Custom amount...'.\n\n" +
                        "Current bank balance: {BALANCE}");
                    body.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(savingsBalance));

                    SafeSetMenuText(args, body);
                }
            );

            foreach (int val in QuickValues)
            {
                int amount = val;
                starter.AddGameMenuOption("bank_savings_deposit",
                    $"deposit_{amount}",
                    L.S("savings_deposit_fixed", "Deposit") + $" {amount:N0}",
                    a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                    _ => TryDepositFixed(behavior, amount), false);
            }

            starter.AddGameMenuOption("bank_savings_deposit", "deposit_all",
                L.S("savings_deposit_all", "Deposit all"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => TryDepositAll(behavior), false);

            starter.AddGameMenuOption("bank_savings_deposit", "deposit_custom",
                L.S("savings_deposit_custom", "Custom amount..."),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => PromptCustomDeposit(behavior), false);

            starter.AddGameMenuOption("bank_savings_deposit", "deposit_back",
                L.S("savings_deposit_back", "Back to Savings"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_savings"), true);
        }

        private static void TryDepositAll(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            double amount = hero.Gold;
            if (amount <= 0)
            {
                ShowWarn(L.S("savings_no_gold", "You do not have enough gold."));
                return;
            }

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            hero.ChangeHeroGold(-(int)Math.Floor(amount));
            acct.Amount += amount;
            if (acct.Amount < 0) acct.Amount = 0;
            SafeSync(behavior);

            var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
            msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
            SafeSwitchToMenu("bank_savings_deposit");
        }

        private static void TryDepositFixed(BankCampaignBehavior behavior, int amount)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            if (amount > hero.Gold)
                amount = hero.Gold;
            if (amount <= 0)
            {
                ShowWarn(L.S("savings_no_gold", "You do not have enough gold."));
                return;
            }

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            hero.ChangeHeroGold(-amount);
            acct.Amount += amount;
            if (acct.Amount < 0) acct.Amount = 0;
            SafeSync(behavior);

            var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
            msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
            SafeSwitchToMenu("bank_savings_deposit");
        }

        private static void PromptCustomDeposit(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out _, out _, out _))
                return;

            InformationManager.ShowTextInquiry(new TextInquiryData(
                L.S("savings_deposit_popup_title", "Custom Deposit"),
                L.S("savings_deposit_popup_desc", "Enter the amount to deposit:"),
                true, true,
                L.S("popup_confirm", "Confirm"),
                L.S("popup_cancel", "Cancel"),
                input =>
                {
                    if (!TryGetContext(behavior, out var h2, out var s2, out var playerId2, out var townId2))
                        return;

                    if (!TryParsePositiveDouble(input, out double amount))
                    {
                        ShowWarn(L.S("savings_invalid_value", "Invalid value."));
                        return;
                    }

                    if (amount > h2.Gold)
                        amount = h2.Gold;
                    if (amount <= 0)
                    {
                        ShowWarn(L.S("savings_no_gold", "You do not have enough gold."));
                        return;
                    }

                    var acct = behavior.GetStorage().GetOrCreateSavings(playerId2, townId2);
                    h2.ChangeHeroGold(-(int)Math.Floor(amount));
                    acct.Amount += amount;
                    if (acct.Amount < 0) acct.Amount = 0;
                    SafeSync(behavior);

                    var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. Current bank balance: {BALANCE}.");
                    msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                    msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
                    SafeSwitchToMenu("bank_savings_deposit");
                },
                () => { }
            ));
        }

        // ============================================
        // Toggle Auto-Reinvestment
        // ============================================
        private static void ToggleAutoReinvest(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out var settlement, out var playerId, out var townId))
                return;

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            acct.AutoReinvest = !acct.AutoReinvest; // alterna o estado
            SafeSync(behavior);

            string msg = acct.AutoReinvest
                ? L.S("savings_reinvest_enabled",
                      "Automatic reinvestment ENABLED — interest will now be added directly to your savings.")
                : L.S("savings_reinvest_disabled",
                      "Automatic reinvestment DISABLED — interest will now go to your personal gold.");

            InformationManager.DisplayMessage(new InformationMessage(
                msg,
                Color.FromUint(acct.AutoReinvest ? BankUtils.UiGold : 0xFFFF6666)
            ));

            // Recarrega o menu principal para atualizar o texto
            SafeSwitchToMenu("bank_savings");
        }

        // ============================================
        // Withdraw submenu
        // ============================================
        private static void RegisterWithdrawMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            starter.AddGameMenu(
                "bank_savings_withdraw",
                L.S("savings_withdraw_loading", "Loading..."),
                args =>
                {
                    if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                    {
                        SafeSetMenuText(args, new TextObject(L.S("savings_err_ctx", "Context not available.")));
                        return;
                    }

                    var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
                    double savingsBalance = acct.Amount;
                    if (savingsBalance < 0) savingsBalance = 0;

                    float feeRate = GetDynamicWithdrawFee(s);
                    var title = L.T("savings_withdraw_title", "Withdraw from Savings — Bank balance: {BALANCE}");
                    title.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(savingsBalance));

                    args.MenuTitle = title;

                    var body = L.T("savings_withdraw_body",
                        "Select a fixed amount to withdraw or use 'Custom amount...'.\n\n" +
                        "Current withdraw fee: {FEE}\n" +
                        "Current bank balance: {BALANCE}");
                    body.SetTextVariable("FEE", BankUtils.FmtPct(feeRate));
                    body.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(savingsBalance));

                    SafeSetMenuText(args, body);
                }
            );

            foreach (int val in QuickValues)
            {
                int gross = val;
                starter.AddGameMenuOption("bank_savings_withdraw",
                    $"withdraw_{gross}",
                    L.S("savings_withdraw_fixed", "Withdraw") + $" {gross:N0}",
                    a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                    _ => TryWithdrawFixed(behavior, gross), false);
            }

            starter.AddGameMenuOption("bank_savings_withdraw", "withdraw_all",
                L.S("savings_withdraw_all", "Withdraw all"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => TryWithdrawAll(behavior), false);

            starter.AddGameMenuOption("bank_savings_withdraw", "withdraw_custom",
                L.S("savings_withdraw_custom", "Custom amount..."),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => PromptCustomWithdraw(behavior), false);

            starter.AddGameMenuOption("bank_savings_withdraw", "withdraw_back",
                L.S("savings_withdraw_back", "Back to Savings"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_savings"), true);
        }

        private static void TryWithdrawAll(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            double gross = Math.Floor(acct.Amount);
            if (gross <= 0)
            {
                ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                return;
            }

            double feeRate = GetDynamicWithdrawFee(s);
            double feeVal = Math.Ceiling(gross * feeRate);
            double net = Math.Max(0, gross - feeVal);

            acct.Amount -= gross;
            if (acct.Amount < 0) acct.Amount = 0;
            hero.ChangeHeroGold((int)Math.Floor(net));
            SafeSync(behavior);

            var msg = L.T("savings_withdraw_done_all",
                "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
            msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
            msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
            msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
            msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
            msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
            SafeSwitchToMenu("bank_savings_withdraw");
        }

        private static void TryWithdrawFixed(BankCampaignBehavior behavior, int gross)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            double grossVal = gross;
            if (grossVal > acct.Amount)
                grossVal = Math.Floor(acct.Amount);

            if (grossVal <= 0)
            {
                ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                return;
            }

            double feeRate = GetDynamicWithdrawFee(s);
            double feeVal = Math.Ceiling(grossVal * feeRate);
            double net = Math.Max(0, grossVal - feeVal);

            acct.Amount -= grossVal;
            if (acct.Amount < 0) acct.Amount = 0;
            hero.ChangeHeroGold((int)Math.Floor(net));
            SafeSync(behavior);

            var msg = L.T("savings_withdraw_done_fixed",
                "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
            msg.SetTextVariable("GROSS", BankUtils.FmtDenars(grossVal));
            msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
            msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
            msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
            msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
            SafeSwitchToMenu("bank_savings_withdraw");
        }

        private static void PromptCustomWithdraw(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out _, out _, out _))
                return;

            InformationManager.ShowTextInquiry(new TextInquiryData(
                L.S("savings_withdraw_popup_title", "Custom Withdraw"),
                L.S("savings_withdraw_popup_desc", "Enter the amount to withdraw:"),
                true, true,
                L.S("popup_confirm", "Confirm"),
                L.S("popup_cancel", "Cancel"),
                input =>
                {
                    if (!TryGetContext(behavior, out var h2, out var s2, out var playerId2, out var townId2))
                        return;

                    if (!TryParsePositiveDouble(input, out double gross))
                    {
                        ShowWarn(L.S("savings_invalid_value", "Invalid value."));
                        return;
                    }

                    var acct = behavior.GetStorage().GetOrCreateSavings(playerId2, townId2);
                    if (gross > acct.Amount)
                        gross = Math.Floor(acct.Amount);

                    if (gross <= 0)
                    {
                        ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                        return;
                    }

                    double feeRate = GetDynamicWithdrawFee(s2);
                    double feeVal = Math.Ceiling(gross * feeRate);
                    double net = Math.Max(0, gross - feeVal);

                    acct.Amount -= gross;
                    if (acct.Amount < 0) acct.Amount = 0;
                    h2.ChangeHeroGold((int)Math.Floor(net));
                    SafeSync(behavior);

                    var msg = L.T("savings_withdraw_done_custom",
                        "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
                    msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
                    msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
                    msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
                    msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
                    msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Color.FromUint(BankUtils.UiGold)));
                    SafeSwitchToMenu("bank_savings_withdraw");
                },
                () => { }
            ));
        }

        // ============================================
        // Utilities
        // ============================================
        private static bool TryParsePositiveDouble(string input, out double value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(input)
                   && double.TryParse(input.Trim(), out double parsed)
                   && parsed > 0
                   && (value = parsed) > 0;
        }

        private static void ShowWarn(string msg)
        {
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFFF6666)));
        }
    }
}
