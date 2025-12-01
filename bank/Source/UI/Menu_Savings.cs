// ============================================
// BanksOfCalradia - BankMenu_Savings.cs
// Author: Dahaka
// Version: 2.6.1 (Ultra Safe Context Hardening)
// Description:
//   Savings interface with fixed options and
//   dynamic withdraw fee based on town economy.
//   - Full double precision (supports trillions)
//   - Clean screen (interest + balance)
//   - Deposit and withdraw submenus
//   - Quick buttons (100 -> 10,000,000)
//   - Deposit all / Withdraw all
//   - Localized texts via helper L
//   - Ultra crash-safe (strict context gate + delays + guarded callbacks)
// ============================================

using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
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

        private static bool _registered;

        // ============================================================
        // Context Hardening
        // ============================================================

        private static bool IsInValidTown()
        {
            try
            {
                return Campaign.Current != null
                       && Hero.MainHero != null
                       && Settlement.CurrentSettlement?.Town != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetStrictContext(
            BankCampaignBehavior behavior,
            out Hero hero,
            out Settlement settlement,
            out Town town,
            out BankStorage storage,
            out string playerId,
            out string townId)
        {
            hero = null;
            settlement = null;
            town = null;
            storage = null;
            playerId = null;
            townId = null;

            try
            {
                if (behavior == null)
                    return false;

                storage = behavior.GetStorage();
                if (storage == null)
                    return false;

                hero = Hero.MainHero;
                if (hero == null)
                    return false;

                settlement = Settlement.CurrentSettlement;
                town = settlement?.Town;
                if (town == null)
                    return false;

                playerId = hero.StringId;
                townId = settlement.StringId;

                if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(townId))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task WaitUiAsync(MenuCallbackArgs args)
        {
            try
            {
                await Task.Delay(80);

                if (args?.MenuContext == null || args.MenuContext.GameMenu == null)
                    await Task.Delay(120);
            }
            catch
            {
                // silent
            }
        }

        private static void SafeSetMenuText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args?.MenuContext?.GameMenu;
                if (menu == null)
                    return;

                var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(menu, text);
            }
            catch
            {
                // silent
            }
        }

        private static void SafeSync(BankCampaignBehavior behavior)
        {
            try { behavior?.SyncBankData(); } catch { }
        }

        private static void Warn(string msg)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFFF6666)));
            }
            catch
            {
                // silent
            }
        }

        private static void InfoGold(string msg)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(BankUtils.UiGold)));
            }
            catch
            {
                // silent
            }
        }

        // ============================================================
        // Dynamic withdraw fee (Reversed Risk Curve – Balanced Final)
        // ============================================================
        private static float GetDynamicWithdrawFee(Settlement settlement)
        {
            if (settlement?.Town == null)
                return 0.01f; // fallback: 1%

            float prosperity = settlement.Town.Prosperity;
            float security = settlement.Town.Security;
            float loyalty = settlement.Town.Loyalty;

            const float MinFee = 0.0000f;   // 0%
            const float MaxFee = 0.0500f;   // 5%
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
            float rebate = MathF.Pow(stability, 4f) * 0.004f;
            fee = MathF.Max(MinFee, fee - rebate);

            return MathF.Clamp(fee, MinFee, MaxFee);
        }

        // ============================================================
        // Register savings menus
        // ============================================================
        public static void RegisterMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            if (_registered)
                return;
            _registered = true;

            starter.AddGameMenu(
                "bank_savings",
                L.S("savings_menu_loading", "Loading savings data..."),
                args => OnMenuInit_Main(args, behavior)
            );

            starter.AddGameMenuOption(
                "bank_savings",
                "savings_deposit",
                L.S("savings_menu_deposit", "Deposit Money"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_savings_deposit"); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings",
                "savings_withdraw",
                L.S("savings_menu_withdraw", "Withdraw Money"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_savings_withdraw"); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings",
                "savings_toggle_reinvest",
                L.S("savings_toggle_reinvest", "Toggle Auto-Reinvestment"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { ToggleAutoReinvest(behavior); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings",
                "savings_back",
                L.S("savings_menu_back", "Return to Bank"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_menu"); } catch { }
                },
                true
            );

            RegisterDepositMenu(starter, behavior);
            RegisterWithdrawMenu(starter, behavior);
        }

        // ============================================================
        // Main savings menu (Ultra Safe)
        // ============================================================
        private static async void OnMenuInit_Main(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                await WaitUiAsync(args);

                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("savings_need_town", "You must be inside a town to access savings."));
                    return;
                }

                string townName = settlement.Name?.ToString() ?? L.S("default_city", "City");
                float prosperity = town.Prosperity;

                // Interest model (original logic preserved)
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

                var acct = storage.GetOrCreateSavings(playerId, townId);

                // FALHA CRÍTICA DE PREWARM / RACE → PREVINE CRASH
                if (acct == null)
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("savings_prewarm_fail",
                        "The bank system is still initializing.\n\nPlease exit the menu and try again in a moment."));
                    return;
                }

                if (acct.Amount < 0)
                    acct.Amount = 0;

                double balance = acct.Amount;


                string reinvestStatus = acct.AutoReinvest
                    ? L.S("savings_reinvest_on", "Enabled")
                    : L.S("savings_reinvest_off", "Disabled");

                var title = L.T("savings_menu_title", "Savings - Bank of {CITY}");
                title.SetTextVariable("CITY", townName);

                var body = L.T("savings_menu_body",
                    "Savings - Bank of {CITY}\n\n" +
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

                args.MenuTitle = title;
                SafeSetMenuText(args, body);
            }
            catch (Exception e)
            {
                try
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        L.S("savings_menu_error", "[BanksOfCalradia] Error loading savings menu: ") + e.Message,
                        Color.FromUint(0xFFFF3333)
                    ));
                }
                catch
                {
                    // silent
                }
            }
        }

        // ============================================================
        // Deposit submenu (Ultra Safe)
        // ============================================================
        private static void RegisterDepositMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            starter.AddGameMenu(
                "bank_savings_deposit",
                L.S("savings_deposit_loading", "Loading..."),
                args => OnMenuInit_Deposit(args, behavior)
            );

            foreach (int val in QuickValues)
            {
                int amount = val;

                starter.AddGameMenuOption(
                    "bank_savings_deposit",
                    $"deposit_{amount}",
                    L.S("savings_deposit_fixed", "Deposit") + $" {amount:N0}",
                    a =>
                    {
                        a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                        return IsInValidTown();
                    },
                    _ =>
                    {
                        try { TryDepositFixed(behavior, amount); } catch { }
                    },
                    false
                );
            }

            starter.AddGameMenuOption(
                "bank_savings_deposit",
                "deposit_all",
                L.S("savings_deposit_all", "Deposit all"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { TryDepositAll(behavior); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings_deposit",
                "deposit_custom",
                L.S("savings_deposit_custom", "Custom amount..."),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { PromptCustomDeposit(behavior); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings_deposit",
                "deposit_back",
                L.S("savings_deposit_back", "Back to Savings"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_savings"); } catch { }
                },
                true
            );
        }

        private static async void OnMenuInit_Deposit(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                await WaitUiAsync(args);

                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("ctx_lost", "Context lost. Please reopen the bank."));
                    return;
                }

                var savings = storage.GetOrCreateSavings(playerId, townId);

                if (savings == null)
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("ctx_lost",
                        "The bank system is still initializing.\n\nPlease reopen this menu shortly."));
                    return;
                }

                if (savings.Amount < 0)
                    savings.Amount = 0;

                double balance = savings.Amount;


                var title = L.T("savings_deposit_title", "Deposit to Savings - Bank balance: {BALANCE}");
                title.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(balance));
                args.MenuTitle = title;

                var body = L.T("savings_deposit_body",
                    "Select a fixed amount to deposit or use 'Custom amount...'.\n\n" +
                    "Current bank balance: {BALANCE}");
                body.SetTextVariable("BALANCE", BankUtils.FmtDenarsFull(balance));
                SafeSetMenuText(args, body);
            }
            catch
            {
                args.MenuTitle = L.T("savings_err_title", "Savings (Error)");
                SafeSetMenuText(args, L.T("savings_err_ctx", "[BanksOfCalradia] Context not available."));
            }
        }

        private static void TryDepositAll(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                double amount = hero.Gold;
                if (amount <= 0)
                {
                    Warn(L.S("savings_no_gold", "You do not have enough gold."));
                    return;
                }

                var acct = storage.GetOrCreateSavings(playerId, townId);

                int delta = (int)Math.Floor(amount);
                if (delta <= 0)
                {
                    Warn(L.S("savings_no_gold", "You do not have enough gold."));
                    return;
                }

                hero.ChangeHeroGold(-delta);
                acct.Amount += amount;
                if (acct.Amount < 0) acct.Amount = 0;

                SafeSync(behavior);

                var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
                msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

                InfoGold(msg.ToString());
                BankSafeUI.Switch("bank_savings_deposit");
            }
            catch
            {
                // silent
            }
        }

        private static void TryDepositFixed(BankCampaignBehavior behavior, int amount)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                int final = amount;
                if (final > hero.Gold)
                    final = hero.Gold;

                if (final <= 0)
                {
                    Warn(L.S("savings_no_gold", "You do not have enough gold."));
                    return;
                }

                var acct = storage.GetOrCreateSavings(playerId, townId);

                hero.ChangeHeroGold(-final);
                acct.Amount += final;
                if (acct.Amount < 0) acct.Amount = 0;

                SafeSync(behavior);

                var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. New balance: {BALANCE}.");
                msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(final));
                msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct.Amount));

                InfoGold(msg.ToString());
                BankSafeUI.Switch("bank_savings_deposit");
            }
            catch
            {
                // silent
            }
        }

        private static void PromptCustomDeposit(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                InformationManager.ShowTextInquiry(new TextInquiryData(
                    L.S("savings_deposit_popup_title", "Custom Deposit"),
                    L.S("savings_deposit_popup_desc", "Enter the amount to deposit:"),
                    true, true,
                    L.S("popup_confirm", "Confirm"),
                    L.S("popup_cancel", "Cancel"),
                    input =>
                    {
                        try
                        {
                            if (!TryGetStrictContext(behavior, out var h2, out var s2, out var t2, out var st2, out var pid2, out var tid2))
                                return;

                            if (!TryParsePositiveAmount(input, out double amount))
                            {
                                Warn(L.S("savings_invalid_value", "Invalid value."));
                                return;
                            }

                            if (amount > h2.Gold)
                                amount = h2.Gold;

                            if (amount <= 0.0d)
                            {
                                Warn(L.S("savings_no_gold", "You do not have enough gold."));
                                return;
                            }

                            int delta = (int)Math.Floor(amount);
                            if (delta <= 0)
                            {
                                Warn(L.S("savings_no_gold", "You do not have enough gold."));
                                return;
                            }

                            var acct2 = st2.GetOrCreateSavings(pid2, tid2);

                            h2.ChangeHeroGold(-delta);
                            acct2.Amount += amount;
                            if (acct2.Amount < 0) acct2.Amount = 0;

                            SafeSync(behavior);

                            var msg = L.T("savings_deposit_done", "Deposit of {AMOUNT} completed. Current bank balance: {BALANCE}.");
                            msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                            msg.SetTextVariable("BALANCE", BankUtils.FmtDenars(acct2.Amount));

                            InfoGold(msg.ToString());
                            BankSafeUI.Switch("bank_savings_deposit");
                        }
                        catch
                        {
                            // silent
                        }
                    },
                    () => { }
                ));
            }
            catch
            {
                // silent
            }
        }

        // ============================================================
        // Toggle Auto-Reinvestment
        // ============================================================
        private static void ToggleAutoReinvest(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                var acct = storage.GetOrCreateSavings(playerId, townId);
                acct.AutoReinvest = !acct.AutoReinvest;

                SafeSync(behavior);

                string msg = acct.AutoReinvest
                    ? L.S("savings_reinvest_enabled", "Automatic reinvestment ENABLED - interest will now be added directly to your savings.")
                    : L.S("savings_reinvest_disabled", "Automatic reinvestment DISABLED - interest will now go to your personal gold.");

                InfoGold(msg);
                BankSafeUI.Switch("bank_savings");
            }
            catch
            {
                // silent
            }
        }

        // ============================================================
        // Withdraw submenu (Ultra Safe + Delay)
        // ============================================================
        private static void RegisterWithdrawMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            starter.AddGameMenu(
                "bank_savings_withdraw",
                L.S("savings_withdraw_loading", "Loading..."),
                args => OnMenuInit_Withdraw(args, behavior)
            );

            foreach (int val in QuickValues)
            {
                int gross = val;

                starter.AddGameMenuOption(
                    "bank_savings_withdraw",
                    $"withdraw_{gross}",
                    L.S("savings_withdraw_fixed", "Withdraw") + $" {gross:N0}",
                    a =>
                    {
                        a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                        return IsInValidTown();
                    },
                    _ =>
                    {
                        try { TryWithdrawFixed(behavior, gross); } catch { }
                    },
                    false
                );
            }

            starter.AddGameMenuOption(
                "bank_savings_withdraw",
                "withdraw_all",
                L.S("savings_withdraw_all", "Withdraw all"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { TryWithdrawAll(behavior); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings_withdraw",
                "withdraw_custom",
                L.S("savings_withdraw_custom", "Custom amount..."),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { PromptCustomWithdraw(behavior); } catch { }
                },
                false
            );

            starter.AddGameMenuOption(
                "bank_savings_withdraw",
                "withdraw_back",
                L.S("savings_withdraw_back", "Back to Savings"),
                a =>
                {
                    a.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_savings"); } catch { }
                },
                true
            );
        }

        private static async void OnMenuInit_Withdraw(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                await WaitUiAsync(args);

                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("ctx_lost", "Context lost. Please reopen the bank."));
                    return;
                }

                var acct = storage.GetOrCreateSavings(playerId, townId);

                if (acct == null)
                {
                    args.MenuTitle = L.T("savings_unavailable", "Savings (Unavailable)");
                    SafeSetMenuText(args, L.T("ctx_lost",
                        "Savings data is not ready yet.\n\nPlease reopen the bank menu."));
                    return;
                }

                double savingsBalance = acct.Amount;

                if (savingsBalance < 0) savingsBalance = 0;

                float feeRate = GetDynamicWithdrawFee(settlement);

                var title = L.T("savings_withdraw_title", "Withdraw from Savings - Bank balance: {BALANCE}");
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
            catch
            {
                args.MenuTitle = L.T("savings_err_title", "Savings (Error)");
                SafeSetMenuText(args, L.T("savings_err_ctx", "[BanksOfCalradia] Context not available."));
            }
        }

        // ============================================================
        // Withdraw Operations (supports huge balances safely)
        // ============================================================

        private static double ComputeMaxGrossForGoldCap(double feeRate)
        {
            // We must not overflow ChangeHeroGold(int). We'll cap net <= int.MaxValue in a single transaction.
            // gross - ceil(gross*feeRate) <= int.MaxValue
            // Approx: gross*(1-feeRate) <= int.MaxValue
            double keep = Math.Max(0.000001d, 1d - Math.Max(0d, Math.Min(0.95d, feeRate)));
            return Math.Floor(int.MaxValue / keep);
        }

        private static void TryWithdrawAll(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                var acct = storage.GetOrCreateSavings(playerId, townId);
                double gross = acct.Amount;
                if (gross <= 0.0001d)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                double feeRate = GetDynamicWithdrawFee(settlement);
                double grossCap = ComputeMaxGrossForGoldCap(feeRate);

                if (gross > grossCap)
                    gross = grossCap;

                if (gross <= 0.0001d)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                double feeVal = Math.Round(gross * feeRate, 2);
                double net = Math.Max(0, gross - feeVal);

                int netInt = (net >= int.MaxValue) ? int.MaxValue : (int)Math.Floor(net);
                if (netInt <= 0)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                acct.Amount = Math.Max(0d, acct.Amount - gross);
                if (acct.Amount < 0.0001d) acct.Amount = 0d;

                hero.ChangeHeroGold(netInt);
                SafeSync(behavior);

                var msg = L.T("savings_withdraw_done_all",
                    "Withdraw of {GROSS} completed (Fee: {FEE} -> {FEEVAL}). Received: {NET}. Balance: {BAL}.");
                msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
                msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
                msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
                msg.SetTextVariable("NET", BankUtils.FmtDenars(netInt));
                msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

                InfoGold(msg.ToString());
                BankSafeUI.Switch("bank_savings_withdraw");
            }
            catch
            {
                // silent
            }
        }

        private static void TryWithdrawFixed(BankCampaignBehavior behavior, int gross)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                var acct = storage.GetOrCreateSavings(playerId, townId);

                double grossVal = gross;
                if (grossVal > acct.Amount)
                    grossVal = Math.Floor(acct.Amount);

                if (grossVal <= 0)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                double feeRate = GetDynamicWithdrawFee(settlement);
                double grossCap = ComputeMaxGrossForGoldCap(feeRate);

                if (grossVal > grossCap)
                    grossVal = grossCap;

                if (grossVal <= 0)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                double feeVal = Math.Ceiling(grossVal * feeRate);
                double net = Math.Max(0, grossVal - feeVal);

                int netInt = (net >= int.MaxValue) ? int.MaxValue : (int)Math.Floor(net);
                if (netInt <= 0)
                {
                    Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                    return;
                }

                acct.Amount = Math.Max(0d, acct.Amount - grossVal);
                if (acct.Amount < 0.0001d) acct.Amount = 0d;

                hero.ChangeHeroGold(netInt);
                SafeSync(behavior);

                var msg = L.T("savings_withdraw_done_fixed",
                    "Withdraw of {GROSS} completed (Fee: {FEE} -> {FEEVAL}). Received: {NET}. Balance: {BAL}.");
                msg.SetTextVariable("GROSS", BankUtils.FmtDenars(grossVal));
                msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
                msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
                msg.SetTextVariable("NET", BankUtils.FmtDenars(netInt));
                msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

                InfoGold(msg.ToString());
                BankSafeUI.Switch("bank_savings_withdraw");
            }
            catch
            {
                // silent
            }
        }

        private static void PromptCustomWithdraw(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                    return;

                InformationManager.ShowTextInquiry(new TextInquiryData(
                    L.S("savings_withdraw_popup_title", "Custom Withdraw"),
                    L.S("savings_withdraw_popup_desc", "Enter the amount to withdraw:"),
                    true, true,
                    L.S("popup_confirm", "Confirm"),
                    L.S("popup_cancel", "Cancel"),
                    input =>
                    {
                        try
                        {
                            if (!TryGetStrictContext(behavior, out var h2, out var s2, out var t2, out var st2, out var pid2, out var tid2))
                                return;

                            if (!TryParsePositiveAmount(input, out double gross))
                            {
                                Warn(L.S("savings_invalid_value", "Invalid value."));
                                return;
                            }

                            var acct = st2.GetOrCreateSavings(pid2, tid2);

                            if (gross > acct.Amount)
                                gross = Math.Floor(acct.Amount);

                            if (gross <= 0)
                            {
                                Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                                return;
                            }

                            double feeRate = GetDynamicWithdrawFee(s2);
                            double grossCap = ComputeMaxGrossForGoldCap(feeRate);

                            if (gross > grossCap)
                                gross = grossCap;

                            if (gross <= 0)
                            {
                                Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                                return;
                            }

                            double feeVal = Math.Ceiling(gross * feeRate);
                            double net = Math.Max(0, gross - feeVal);

                            int netInt = (net >= int.MaxValue) ? int.MaxValue : (int)Math.Floor(net);
                            if (netInt <= 0)
                            {
                                Warn(L.S("savings_no_balance", "Insufficient savings balance."));
                                return;
                            }

                            acct.Amount = Math.Max(0d, acct.Amount - gross);
                            if (acct.Amount < 0.0001d) acct.Amount = 0d;

                            h2.ChangeHeroGold(netInt);
                            SafeSync(behavior);

                            var msg = L.T("savings_withdraw_done_custom",
                                "Withdraw of {GROSS} completed (Fee: {FEE} -> {FEEVAL}). Received: {NET}. Balance: {BAL}.");
                            msg.SetTextVariable("GROSS", BankUtils.FmtDenars(gross));
                            msg.SetTextVariable("FEE", BankUtils.FmtPct((float)feeRate));
                            msg.SetTextVariable("FEEVAL", BankUtils.FmtDenars(feeVal));
                            msg.SetTextVariable("NET", BankUtils.FmtDenars(netInt));
                            msg.SetTextVariable("BAL", BankUtils.FmtDenars(acct.Amount));

                            InfoGold(msg.ToString());
                            BankSafeUI.Switch("bank_savings_withdraw");
                        }
                        catch
                        {
                            // silent
                        }
                    },
                    () => { }
                ));
            }
            catch
            {
                // silent
            }
        }

        // ============================================================
        // Utilities
        // ============================================================

        private static bool TryParsePositiveAmount(string input, out double value)
        {
            value = 0d;

            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return false;

                string s = input.Trim();
                s = s.Replace(" ", "");

                // Try current culture
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out double v1) && v1 > 0)
                {
                    value = v1;
                    return true;
                }

                // Try invariant
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out double v2) && v2 > 0)
                {
                    value = v2;
                    return true;
                }

                // Normalize mixed separators
                // If both '.' and ',' exist, decimal separator is the last one.
                int lastDot = s.LastIndexOf('.');
                int lastComma = s.LastIndexOf(',');

                if (lastDot >= 0 && lastComma >= 0)
                {
                    char dec = (lastDot > lastComma) ? '.' : ',';
                    char thou = (dec == '.') ? ',' : '.';

                    string normalized = s.Replace(thou.ToString(), "");
                    normalized = normalized.Replace(dec, '.');

                    if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out double v3) && v3 > 0)
                    {
                        value = v3;
                        return true;
                    }
                }
                else if (lastComma >= 0 && lastDot < 0)
                {
                    string normalized = s.Replace('.', ' ').Replace(" ", "").Replace(',', '.');
                    if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out double v4) && v4 > 0)
                    {
                        value = v4;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
