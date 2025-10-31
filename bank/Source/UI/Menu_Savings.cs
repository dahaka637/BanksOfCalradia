// ============================================
// BanksOfCalradia - BankMenu_Savings.cs
// Author: Dahaka
// Version: 2.5.1 (Localization + Hardening)
// Description:
//   Savings interface with fixed options and
//   dynamic withdraw fee based on town economy.
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
        // Dynamic withdraw fee based on town economy
        // ============================================================
        private static float GetDynamicWithdrawFee(Settlement settlement)
        {
            if (settlement?.Town == null)
                return 0.05f; // fallback 5%

            float prosperity = settlement.Town.Prosperity;
            float security = settlement.Town.Security;
            float loyalty = settlement.Town.Loyalty;

            float pFactor = MathF.Clamp(1f - prosperity / 12000f, 0f, 1f);
            float sFactor = MathF.Clamp(1f - security / 100f, 0f, 1f);
            float lFactor = MathF.Clamp(1f - loyalty / 100f, 0f, 1f);

            float baseFee = 0.035f;
            float riskBonus = (pFactor * 0.4f + sFactor * 0.4f + lFactor * 0.2f) * 0.07f;

            float dynamicFee = MathF.Clamp(baseFee + riskBonus, 0.02f, 0.13f);
            return dynamicFee;
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
            starter.AddGameMenuOption(
                "bank_savings", "savings_deposit",
                L.S("savings_menu_deposit", "Deposit Money"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => SafeSwitchToMenu("bank_savings_deposit"),
                isLeave: false
            );

            starter.AddGameMenuOption(
                "bank_savings", "savings_withdraw",
                L.S("savings_menu_withdraw", "Withdraw Money"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                _ => SafeSwitchToMenu("bank_savings_withdraw"),
                isLeave: false
            );

            starter.AddGameMenuOption(
                "bank_savings", "savings_back",
                L.S("savings_menu_back", "Return to Bank"),
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_menu"),
                isLeave: true
            );

            // Submenus
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

                // ============================================================
                // 💹 Novo cálculo calibrado – mais prosperidade, menos juros em cidades ricas
                // ============================================================
                const float fator = 350f;
                const float prosperidadeBase = 5000f;
                const float prosperidadeAlta = 6000f;
                const float prosperidadeMax = 10000f;
                const float CICLO_DIAS = 120f;

                prosperity = MathF.Max(prosperity, 1f);

                // --- Fator suavizador ---
                float rawSuavizador = prosperidadeBase / prosperity;
                float fatorSuavizador = 0.7f + (rawSuavizador * 0.7f);

                // --- Ajuste de pobreza ---
                float bonus = 0f;
                if (prosperity < prosperidadeBase)
                {
                    float ajustePobreza = MathF.Pow((prosperidadeBase - prosperity) / prosperidadeBase, 1.3f);
                    bonus = ajustePobreza * 3f;
                }

                // --- Ajuste de riqueza ---
                float ajusteRiqueza = 0f;
                if (prosperity > prosperidadeAlta)
                {
                    float excesso = (prosperity - prosperidadeAlta) / (prosperidadeMax - prosperidadeAlta);
                    excesso = MathF.Clamp(excesso, 0f, 1f);
                    ajusteRiqueza = MathF.Pow(excesso, 1.6f) * 5.5f;
                    // ➕ leve suavização extra para reduzir juros de cidades ricas
                    ajusteRiqueza *= 1.15f;
                }

                // --- Cálculo das taxas ---
                float taxaAnualBruta = (prosperity / fator) + bonus - ajusteRiqueza;

                // ➖ leve redução global nas cidades acima da média
                float taxaAnual = taxaAnualBruta;
                if (prosperity > prosperidadeBase)
                {
                    float suavReducao = 1f - ((prosperity - prosperidadeBase) / prosperidadeMax) * 0.1f; // até -10 %
                    taxaAnual = taxaAnualBruta * MathF.Max(0.85f, suavReducao);
                }

                float taxaDiaria = taxaAnual / CICLO_DIAS;

                // --- Simulação de rendimento (para exibição no painel) ---
                float rendimentoDia = 10_000f * (taxaDiaria / 100f); // base simbólica para exibição
                float rendimentoTotal = rendimentoDia * CICLO_DIAS;
                float ganhoProsperidadeDia = MathF.Round(
                    MathF.Pow(10_000f / 1_000_000f, 0.55f)
                    * MathF.Pow(6000f / (prosperity + 3000f), 0.3f)
                    * fatorSuavizador
                    * 1.5f
                    * (1f + bonus * 0.05f)
                    * (1f - ajusteRiqueza * 0.03f),
                    4
                );

                // ============================================================
                // 🔹 Dados e interface (inalterados)
                // ============================================================
                float withdrawRate = GetDynamicWithdrawFee(settlement);

                var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
                if (acct.Amount < 0f) acct.Amount = 0f;
                float balance = acct.Amount;

                var body = L.T("savings_menu_body",
                    "Savings — Bank of {CITY}\n\n" +
                    "• Annual interest rate: {INTEREST_AA}\n" +
                    "• Daily interest rate: {INTEREST_AD}\n" +
                    "• Local prosperity: {PROSPERITY}\n" +
                    "• Prosperity gain (forecast): +{GAIN}/day\n" +
                    "• Withdraw fee: {WITHDRAW_FEE}\n" +
                    "• Current balance: {BALANCE}\n");
                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("INTEREST_AA", BankUtils.FmtPct(taxaAnual / 100f));
                body.SetTextVariable("INTEREST_AD", BankUtils.FmtPct(taxaDiaria / 100f));
                body.SetTextVariable("PROSPERITY", prosperity.ToString("0"));
                body.SetTextVariable("GAIN", ganhoProsperidadeDia.ToString("0.0000"));
                body.SetTextVariable("WITHDRAW_FEE", BankUtils.FmtPct(withdrawRate));
                body.SetTextVariable("BALANCE", BankUtils.FmtDenars(balance));

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
        // Deposit submenu (static buttons)
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

                    float savingsBalance = behavior.GetStorage().GetOrCreateSavings(playerId, townId).Amount;

                    var title = L.T("savings_deposit_title", "Deposit to Savings — Bank balance: {BALANCE}");
                    title.SetTextVariable("BALANCE", BankUtils.FmtDenars(savingsBalance));
                    args.MenuTitle = title;

                    var body = L.T("savings_deposit_body",
                        "Select a fixed amount to deposit or use 'Custom amount...'.\n\n" +
                        "Current bank balance: {BALANCE}");
                    body.SetTextVariable("BALANCE", BankUtils.FmtDenars(savingsBalance));

                    SafeSetMenuText(args, body);
                }
            );

            // Quick buttons
            foreach (int val in QuickValues)
            {
                int amount = val;
                starter.AddGameMenuOption(
                    "bank_savings_deposit",
                    $"deposit_{amount}",
                    L.S("savings_deposit_fixed", "Deposit") + $" {amount:N0}",
                    a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                    _ => TryDepositFixed(behavior, amount),
                    isLeave: false
                );
            }

            // Deposit all
            starter.AddGameMenuOption(
                "bank_savings_deposit", "deposit_all",
                L.S("savings_deposit_all", "Deposit all"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => TryDepositAll(behavior),
                isLeave: false
            );

            // Custom amount
            starter.AddGameMenuOption(
                "bank_savings_deposit", "deposit_custom",
                L.S("savings_deposit_custom", "Custom amount..."),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => PromptCustomDeposit(behavior),
                isLeave: false
            );

            // Back
            starter.AddGameMenuOption(
                "bank_savings_deposit", "deposit_back",
                L.S("savings_deposit_back", "Back to Savings"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_savings"),
                isLeave: true
            );
        }

        private static void TryDepositAll(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            int amount = hero.Gold;
            if (amount <= 0)
            {
                ShowWarn(L.S("savings_no_gold", "You do not have enough gold."));
                return;
            }

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);

            hero.ChangeHeroGold(-amount);
            acct.Amount += amount;
            if (acct.Amount < 0f) acct.Amount = 0f;
            SafeSync(behavior);

            var msg = L.T("savings_deposit_done",
                "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
            msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(
                msg.ToString(),
                Color.FromUint(BankUtils.UiGold)
            ));

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
            if (acct.Amount < 0f) acct.Amount = 0f;
            SafeSync(behavior);

            var msg = L.T("savings_deposit_done",
                "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
            msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(
                msg.ToString(),
                Color.FromUint(BankUtils.UiGold)
            ));

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

                    if (!TryParsePositiveInt(input, out int amount))
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

                    h2.ChangeHeroGold(-amount);
                    acct.Amount += amount;
                    if (acct.Amount < 0f) acct.Amount = 0f;
                    SafeSync(behavior);

                    var msg = L.T("savings_deposit_done",
                        "Deposit of {AMOUNT} completed. Current bank balance: {BALANCE}.");
                    msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                    msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

                    InformationManager.DisplayMessage(new InformationMessage(
                        msg.ToString(),
                        Color.FromUint(BankUtils.UiGold)
                    ));

                    SafeSwitchToMenu("bank_savings_deposit");
                },
                () => { }

            ));
        }

        // ============================================
        // Withdraw submenu (static buttons)
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
                    float savingsBalance = acct.Amount;
                    if (savingsBalance < 0f) savingsBalance = 0f;

                    float feeRate = GetDynamicWithdrawFee(s);

                    var title = L.T("savings_withdraw_title", "Withdraw from Savings — Bank balance: {BALANCE}");
                    title.SetTextVariable("BALANCE", BankUtils.FmtDenars(savingsBalance));
                    args.MenuTitle = title;

                    var body = L.T("savings_withdraw_body",
                        "Select a fixed amount to withdraw or use 'Custom amount...'.\n\n" +
                        "Current withdraw fee: {FEE}\n" +
                        "Current bank balance: {BALANCE}");
                    body.SetTextVariable("FEE", BankUtils.FmtPct(feeRate));
                    body.SetTextVariable("BALANCE", BankUtils.FmtDenars(savingsBalance));

                    SafeSetMenuText(args, body);
                }
            );

            foreach (int val in QuickValues)
            {
                int gross = val;
                starter.AddGameMenuOption(
                    "bank_savings_withdraw",
                    $"withdraw_{gross}",
                    L.S("savings_withdraw_fixed", "Withdraw") + $" {gross:N0}",
                    a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                    _ => TryWithdrawFixed(behavior, gross),
                    isLeave: false
                );
            }

            // Withdraw all
            starter.AddGameMenuOption(
                "bank_savings_withdraw", "withdraw_all",
                L.S("savings_withdraw_all", "Withdraw all"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => TryWithdrawAll(behavior),
                isLeave: false
            );

            // Custom
            starter.AddGameMenuOption(
                "bank_savings_withdraw", "withdraw_custom",
                L.S("savings_withdraw_custom", "Custom amount..."),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                _ => PromptCustomWithdraw(behavior),
                isLeave: false
            );

            // Back
            starter.AddGameMenuOption(
                "bank_savings_withdraw", "withdraw_back",
                L.S("savings_withdraw_back", "Back to Savings"),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => SafeSwitchToMenu("bank_savings"),
                isLeave: true
            );
        }

        private static void TryWithdrawAll(BankCampaignBehavior behavior)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);
            int gross = (int)MathF.Floor(acct.Amount);
            if (gross <= 0)
            {
                ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                return;
            }

            float feeRate = GetDynamicWithdrawFee(s);
            int feeInt = (int)MathF.Ceiling(gross * feeRate);
            int net = Math.Max(0, gross - feeInt);

            acct.Amount -= gross;
            if (acct.Amount < 0f) acct.Amount = 0f;
            hero.ChangeHeroGold(net);
            SafeSync(behavior);

            var msg = L.T("savings_withdraw_done_all",
                "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
            msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
            msg.SetTextVariable("FEE", BankUtils.FmtPct(feeRate));
            msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeInt));
            msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
            msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(
                msg.ToString(),
                Color.FromUint(BankUtils.UiGold)
            ));

            SafeSwitchToMenu("bank_savings_withdraw");
        }

        private static void TryWithdrawFixed(BankCampaignBehavior behavior, int gross)
        {
            if (!TryGetContext(behavior, out var hero, out var s, out var playerId, out var townId))
                return;

            var acct = behavior.GetStorage().GetOrCreateSavings(playerId, townId);

            if (gross > acct.Amount)
                gross = (int)MathF.Floor(acct.Amount);

            if (gross <= 0)
            {
                ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                return;
            }

            float feeRate = GetDynamicWithdrawFee(s);
            int feeInt = (int)MathF.Ceiling(gross * feeRate);
            int net = Math.Max(0, gross - feeInt);

            acct.Amount -= gross;
            if (acct.Amount < 0f) acct.Amount = 0f;
            hero.ChangeHeroGold(net);
            SafeSync(behavior);

            var msg = L.T("savings_withdraw_done_fixed",
                "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
            msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
            msg.SetTextVariable("FEE", BankUtils.FmtPct(feeRate));
            msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeInt));
            msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
            msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

            InformationManager.DisplayMessage(new InformationMessage(
                msg.ToString(),
                Color.FromUint(BankUtils.UiGold)
            ));

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

                    if (!TryParsePositiveInt(input, out int gross))
                    {
                        ShowWarn(L.S("savings_invalid_value", "Invalid value."));
                        return;
                    }

                    var acct = behavior.GetStorage().GetOrCreateSavings(playerId2, townId2);

                    if (gross > acct.Amount)
                        gross = (int)MathF.Floor(acct.Amount);

                    if (gross <= 0)
                    {
                        ShowWarn(L.S("savings_no_balance", "Insufficient savings balance."));
                        return;
                    }

                    float feeRate = GetDynamicWithdrawFee(s2);
                    int feeInt = (int)MathF.Ceiling(gross * feeRate);
                    int net = Math.Max(0, gross - feeInt);

                    acct.Amount -= gross;
                    if (acct.Amount < 0f) acct.Amount = 0f;
                    h2.ChangeHeroGold(net);
                    SafeSync(behavior);

                    var msg = L.T("savings_withdraw_done_custom",
                        "Withdraw of {GROSS} completed (Fee: {FEE} → {FEEVAL}). Received: {NET}. Balance: {BAL}.");
                    msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
                    msg.SetTextVariable("FEE", BankUtils.FmtPct(feeRate));
                    msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeInt));
                    msg.SetTextVariable("NET", BankUtils.FmtDenars(net));
                    msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

                    InformationManager.DisplayMessage(new InformationMessage(
                        msg.ToString(),
                        Color.FromUint(BankUtils.UiGold)
                    ));

                    SafeSwitchToMenu("bank_savings_withdraw");
                },
                () => { }

            ));
        }

        // ============================================
        // Utilities
        // ============================================
        private static bool TryParsePositiveInt(string input, out int value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(input)
                   && int.TryParse(input.Trim(), out int parsed)
                   && parsed > 0
                   && (value = parsed) > 0;
        }

        private static void ShowWarn(string msg)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                msg,
                Color.FromUint(0xFFFF6666)
            ));
        }
    }
}
