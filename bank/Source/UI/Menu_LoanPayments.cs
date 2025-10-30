using System;
using System.Reflection;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.UI
{
    // ============================================
    // BankMenu_LoanPay (hardened)
    // - Lista e pagamento de empréstimos ativos
    // - Popup de seleção (1 contrato por vez)
    // - Filtra contratos quitados (Remaining <= 0.01)
    // - Armazena só LoanId (evita refs quebradas após load)
    // - Blindado contra null/contexto inválido
    // - Localização nativa via helper L
    // ============================================
    public static class BankMenu_LoanPay
    {
        private const string SEP = "____________________";

        // Guardado somente o ID, não a referência
        private static string _selectedLoanId = null;

        private static BankCampaignBehavior _behaviorRef;
        private static CampaignGameStarter _starterRef;
        private static bool _registered;

        // ============================================
        // Registro dos menus
        // ============================================
        public static void RegisterMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            if (_registered)
                return;

            _registered = true;
            _behaviorRef = behavior;
            _starterRef = starter;

            // Menu principal (lista de empréstimos da cidade atual)
            starter.AddGameMenu(
                "bank_loan_pay",
                L.S("loanpay_list_loading", "Loading active loans..."),
                OnMenuInit_List
            );

            // Detalhes do contrato selecionado
            starter.AddGameMenu(
                "bank_loan_detail",
                L.S("loanpay_detail_loading", "Loading contract details..."),
                OnMenuInit_Detail
            );

            // Voltar do detalhe para a lista
            starter.AddGameMenuOption(
                "bank_loan_detail",
                "loan_detail_back",
                L.S("loanpay_detail_back", "Back to loan list"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ => SafeSwitchToMenu("bank_loan_pay")
            );

            // Pagar contrato selecionado
            starter.AddGameMenuOption(
                "bank_loan_detail",
                "loan_detail_pay",
                L.S("loanpay_detail_pay", "Pay this loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    var loan = GetSelectedLoan();
                    return loan != null && loan.Remaining > 0.01f;
                },
                _ => PromptPayment()
            );
        }

        // =========================================================
        // Helpers centrais
        // =========================================================
        private static BankCampaignBehavior GetBehavior()
        {
            if (_behaviorRef == null && Campaign.Current != null)
            {
                try
                {
                    _behaviorRef = Campaign.Current.GetCampaignBehavior<BankCampaignBehavior>();
                }
                catch
                {
                    _behaviorRef = null;
                }
            }
            return _behaviorRef;
        }

        private static BankLoanData GetSelectedLoan()
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;
                if (behavior == null || hero == null)
                    return null;

                if (string.IsNullOrEmpty(_selectedLoanId))
                    return null;

                var storage = behavior.GetStorage();
                if (storage == null)
                    return null;

                var list = storage.GetLoans(hero.StringId);
                if (list == null)
                    return null;

                return list.Find(l => l.LoanId == _selectedLoanId);
            }
            catch
            {
                return null;
            }
        }

        // =========================================================
        // Menu principal (lista/ações)
        // =========================================================
        private static void OnMenuInit_List(MenuCallbackArgs args)
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;
                var settlement = Settlement.CurrentSettlement;

                if (behavior == null || hero == null || settlement == null)
                {
                    Warn(L.S("loanpay_err_ctx", "Error: invalid context (behavior/player/town)."));
                    return;
                }

                var storage = behavior.GetStorage();
                if (storage == null)
                {
                    Warn(L.S("loanpay_err_storage", "Error: bank storage is not available."));
                    return;
                }

                string playerId = hero.StringId ?? "player";
                string townId = settlement.StringId ?? "town";
                string townName = settlement.Name?.ToString() ?? L.S("default_city", "City");

                var loans = storage.GetLoans(playerId) ?? new List<BankLoanData>();

                // Filtra só desta cidade e ativos
                List<BankLoanData> cityLoans = loans.FindAll(l =>
                    l != null &&
                    l.TownId == townId &&
                    l.Remaining > 0.01f
                );

                args.MenuTitle = L.T("loanpay_list_title", "Bank of {CITY} — Active Loans");
                args.MenuTitle.SetTextVariable("CITY", townName);

                float totalDebt = 0f;
                for (int i = 0; i < cityLoans.Count; i++)
                    totalDebt += MathF.Max(0f, cityLoans[i].Remaining);

                if (cityLoans.Count == 0)
                {
                    var bodyEmpty = L.T("loanpay_list_body_empty",
                        "Bank of {CITY}\n\nYou have no pending loans at this bank.\n\n{SEP}");
                    bodyEmpty.SetTextVariable("CITY", townName);
                    bodyEmpty.SetTextVariable("SEP", SEP);
                    SetMenuText(args, bodyEmpty);
                }
                else
                {
                    var body = L.T("loanpay_list_body",
                        "Bank of {CITY}\n\nActive debt at this bank: {DEBT}\n\nUse 'Select contract' to choose which one to manage.\n\n{SEP}");
                    body.SetTextVariable("CITY", townName);
                    body.SetTextVariable("DEBT", BankUtils.FmtDenars(totalDebt));
                    body.SetTextVariable("SEP", SEP);
                    SetMenuText(args, body);
                }

                // Limpa botões gerados anteriormente
                ClearMenuOptions(args);

                // Botão de seleção
                if (_starterRef != null)
                {
                    _starterRef.AddGameMenuOption(
                        "bank_loan_pay",
                        "loan_pick",
                        L.S("loanpay_list_pick", "Select contract"),
                        a =>
                        {
                            a.optionLeaveType = GameMenuOption.LeaveType.Continue;
                            return cityLoans.Count > 0;
                        },
                        _ => ShowLoanPicker()
                    );

                    // Voltar ao menu de empréstimos
                    _starterRef.AddGameMenuOption(
                        "bank_loan_pay",
                        "loan_pay_back",
                        L.S("loanpay_list_back", "Return to Bank"),
                        a =>
                        {
                            a.optionLeaveType = GameMenuOption.LeaveType.Leave;
                            return true;
                        },
                        _ => SafeSwitchToMenu("bank_loanmenu"),
                        isLeave: true
                    );
                }
            }
            catch (Exception e)
            {
                var msg = L.T("loanpay_err_list", "Error building loan list: {ERROR}");
                msg.SetTextVariable("ERROR", e.Message);
                Warn(msg.ToString());
            }
        }

        // =========================================================
        // Popup de seleção dos contratos ativos
        // =========================================================
        private static void ShowLoanPicker()
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;
                var settlement = Settlement.CurrentSettlement;

                if (behavior == null || hero == null || settlement == null)
                {
                    Warn(L.S("loanpay_err_ctx_select", "Error: invalid context when opening selection."));
                    SafeSwitchToMenu("bank_loan_pay");
                    return;
                }

                var storage = behavior.GetStorage();
                if (storage == null)
                {
                    Warn(L.S("loanpay_err_storage", "Error: bank storage is not available."));
                    SafeSwitchToMenu("bank_loan_pay");
                    return;
                }

                string playerId = hero.StringId ?? "player";
                string townId = settlement.StringId ?? "town";

                var allLoans = storage.GetLoans(playerId) ?? new List<BankLoanData>();
                var validLoans = allLoans.FindAll(l =>
                    l != null &&
                    l.TownId == townId &&
                    l.Remaining > 0.01f
                );

                if (validLoans.Count == 0)
                {
                    Warn(L.S("loanpay_none_for_city", "No active contracts to display."));
                    SafeSwitchToMenu("bank_loan_pay");
                    return;
                }

                var elements = new List<InquiryElement>();
                int idx = 1;
                foreach (var loan in validLoans)
                {
                    var label = L.T("loanpay_picker_item", "Contract {INDEX} — Remaining: {AMOUNT}");
                    label.SetTextVariable("INDEX", idx.ToString("00"));
                    label.SetTextVariable("AMOUNT", BankUtils.FmtDenars(loan.Remaining));
                    elements.Add(new InquiryElement(loan.LoanId, label.ToString(), null, true, string.Empty));
                    idx++;
                }

                var title = L.S("loanpay_picker_title", "Select Contract");
                var desc = L.S("loanpay_picker_desc", "Choose a contract to see details and make payments.");
                var ok = L.S("popup_open", "Open");
                var cancel = L.S("popup_cancel", "Cancel");

                var data = new MultiSelectionInquiryData(
                    title,
                    desc,
                    elements,
                    true,
                    1,
                    1,
                    ok,
                    cancel,
                    selected =>
                    {
                        try
                        {
                            if (selected == null || selected.Count == 0 || selected[0].Identifier == null)
                            {
                                Warn(L.S("loanpay_no_selection", "No contract selected."));
                                SafeSwitchToMenu("bank_loan_pay");
                                return;
                            }

                            _selectedLoanId = selected[0].Identifier as string;

                            var loan = GetSelectedLoan();
                            if (loan == null)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                SafeSwitchToMenu("bank_loan_pay");
                                return;
                            }

                            SafeSwitchToMenu("bank_loan_detail");
                        }
                        catch (Exception ex)
                        {
                            var msg = L.T("loanpay_err_open_picker_cb", "Error handling selection: {ERROR}");
                            msg.SetTextVariable("ERROR", ex.Message);
                            Warn(msg.ToString());
                            SafeSwitchToMenu("bank_loan_pay");
                        }
                    },
                    _ =>
                    {
                        SafeSwitchToMenu("bank_loan_pay");
                    }
                );

                MBInformationManager.ShowMultiSelectionInquiry(data);
            }
            catch (Exception e)
            {
                var msg = L.T("loanpay_err_open_picker", "Error opening contract selection: {ERROR}");
                msg.SetTextVariable("ERROR", e.Message);
                Warn(msg.ToString());
                SafeSwitchToMenu("bank_loan_pay");
            }
        }

        // =========================================================
        // Detalhes do contrato selecionado
        // =========================================================
        private static void OnMenuInit_Detail(MenuCallbackArgs args)
        {
            try
            {
                SetMenuText(args, new TextObject(L.S("loading_generic", "Loading...")));

                var loan = GetSelectedLoan();
                if (loan == null)
                {
                    SetMenuText(args, new TextObject(L.S("loanpay_detail_invalid", "No contract selected or contract is invalid.")));
                    return;
                }

                string townName = Settlement.CurrentSettlement?.Name?.ToString() ?? L.S("default_city", "City");

                string statusLine;
                if (loan.Remaining <= 0.01f)
                {
                    statusLine = L.S("loanpay_status_paid", "Status: Paid off");
                }
                else
                {
                    var s = L.T("loanpay_status_remaining", "Remaining: {AMOUNT}");
                    s.SetTextVariable("AMOUNT", BankUtils.FmtDenars(loan.Remaining));
                    statusLine = s.ToString();
                }

                var body = L.T("loanpay_detail_body",
                    "Loan Contract — Bank of {CITY}\n\n" +
                    "• Original amount: {ORIGINAL}\n" +
                    "• {STATUS}\n" +
                    "• Fixed interest: {INTEREST}\n" +
                    "• Daily late fee: {LATEFEE}\n" +
                    "• Term (days): {DAYS}\n" +
                    "{SEP}\n" +
                    "{ACTION_HINT}");
                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("ORIGINAL", BankUtils.FmtDenars(loan.OriginalAmount));
                body.SetTextVariable("STATUS", statusLine);
                body.SetTextVariable("INTEREST", BankUtils.FmtPct(loan.InterestRate / 100f));
                body.SetTextVariable("LATEFEE", BankUtils.FmtPct(loan.LateFeeRate / 100f));
                body.SetTextVariable("DAYS", loan.DurationDays);
                body.SetTextVariable("SEP", SEP);
                body.SetTextVariable("ACTION_HINT",
                    loan.Remaining > 0.01f
                        ? L.S("loanpay_action_hint_pay", "Select 'Pay this loan' to make a partial payment.")
                        : L.S("loanpay_action_hint_done", "This loan is fully paid."));

                args.MenuTitle = L.T("loanpay_detail_title", "Loan Details");
                SetMenuText(args, body);
            }
            catch (Exception e)
            {
                var msg = L.T("loanpay_err_detail", "Error showing details: {ERROR}");
                msg.SetTextVariable("ERROR", e.Message);
                Warn(msg.ToString());
            }
        }

        // =========================================================
        // Pagamento parcial
        // =========================================================
        private static void PromptPayment()
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;

                if (behavior == null || hero == null)
                {
                    Warn(L.S("loanpay_err_ctx_payment", "Error: invalid context for payment."));
                    return;
                }

                var contract = GetSelectedLoan();
                if (contract == null)
                {
                    Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                    SafeSwitchToMenu("bank_loan_pay");
                    return;
                }

                if (contract.Remaining <= 0.01f)
                {
                    Warn(L.S("loanpay_already_paid", "This contract is already paid off."));
                    SafeSwitchToMenu("bank_loan_detail");
                    return;
                }

                // hero.Gold é int; alinhar tipos para MathF.Max (float,float)
                float gold = MathF.Max(0f, (float)hero.Gold);
                float remaining = MathF.Max(0f, contract.Remaining);

                var descObj = L.T("loanpay_popup_desc", "Enter the amount to pay (balance: {BAL}, remaining: {REM}):");
                descObj.SetTextVariable("BAL", BankUtils.FmtDenars(gold));
                descObj.SetTextVariable("REM", BankUtils.FmtDenars(remaining));

                InformationManager.ShowTextInquiry(new TextInquiryData(
                    L.S("loanpay_popup_title", "Loan Payment"),
                    descObj.ToString(),
                    true,
                    true,
                    L.S("popup_confirm", "Confirm"),
                    L.S("popup_cancel", "Cancel"),
                    input =>
                    {
                        try
                        {
                            if (!float.TryParse((input ?? "").Trim(), out float value))
                            {
                                Warn(L.S("loanpay_invalid_value", "Invalid amount."));
                                return;
                            }

                            if (value <= 0f)
                            {
                                Warn(L.S("loanpay_value_gt_zero", "Amount must be greater than zero."));
                                return;
                            }

                            if (value > hero.Gold)
                            {
                                Warn(L.S("loanpay_not_enough_gold", "You do not have enough denars."));
                                return;
                            }

                            var storage = behavior.GetStorage();
                            if (storage == null)
                            {
                                Warn(L.S("loanpay_err_storage", "Error: bank storage is not available."));
                                SafeSwitchToMenu("bank_loan_pay");
                                return;
                            }

                            var list = storage.GetLoans(hero.StringId ?? "player");
                            var current = list?.Find(l => l.LoanId == _selectedLoanId);
                            if (current == null)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                SafeSwitchToMenu("bank_loan_pay");
                                return;
                            }

                            float amountToPay = MathF.Min(value, current.Remaining);
                            if (amountToPay <= 0f)
                            {
                                Warn(L.S("loanpay_value_too_low", "Payment amount too low."));
                                return;
                            }

                            int debit = MathF.Round(amountToPay);
                            if (debit <= 0)
                            {
                                Warn(L.S("loanpay_value_too_low", "Payment amount too low."));
                                return;
                            }

                            if (debit > hero.Gold)
                            {
                                Warn(L.S("loanpay_insufficient_after_round", "Insufficient funds after rounding."));
                                return;
                            }

                            hero.ChangeHeroGold(-debit);
                            current.Remaining -= debit;
                            if (current.Remaining < 0f)
                                current.Remaining = 0f;

                            try
                            {
                                behavior.SyncBankData();
                            }
                            catch
                            {
                                // Silêncio: não vamos crashar por causa do sync
                            }

                            var okMsg = L.T("loanpay_payment_ok", "Payment of {AMOUNT} completed successfully.");
                            okMsg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(debit));
                            InformationManager.DisplayMessage(
                                new InformationMessage(okMsg.ToString(), Color.FromUint(BankUtils.UiGold))
                            );

                            if (current.Remaining <= 0.01f)
                            {
                                current.Remaining = 0f;
                                InformationManager.DisplayMessage(
                                    new InformationMessage(
                                        L.S("loanpay_now_paid", "Loan fully paid."),
                                        Color.FromUint(0xFF66FF66)
                                    )
                                );
                            }

                            SafeSwitchToMenu("bank_loan_detail");
                        }
                        catch (Exception ex)
                        {
                            var msg = L.T("loanpay_err_payment_cb", "Error processing payment: {ERROR}");
                            msg.SetTextVariable("ERROR", ex.Message);
                            Warn(msg.ToString());
                        }
                    },
                    null // TextInquiryData espera Action (sem parâmetro) para cancelar; usar null evita o erro de delegate
                ));
            }
            catch (Exception e)
            {
                var msg = L.T("loanpay_err_payment", "Error processing payment: {ERROR}");
                msg.SetTextVariable("ERROR", e.Message);
                Warn(msg.ToString());
            }
        }

        // =========================================================
        // Utilidades de menu/reflection
        // =========================================================
        private static void ClearMenuOptions(MenuCallbackArgs args)
        {
            try
            {
                var menu = args.MenuContext?.GameMenu;
                if (menu == null)
                    return;

                var listField = typeof(GameMenu).GetField("_menuOptions", BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField == null)
                    return;

                var list = listField.GetValue(menu) as IList<GameMenuOption>;
                list?.Clear();
            }
            catch
            {
                // silencioso
            }
        }

        private static void SetMenuText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args.MenuContext?.GameMenu;
                if (menu == null)
                    return;

                var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    return;

                field.SetValue(menu, text);
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

        private static void Warn(string msg)
        {
            InformationManager.DisplayMessage(
                new InformationMessage(
                    msg ?? "[BanksOfCalradia] Unknown error.",
                    Color.FromUint(0xFFFF6666)
                )
            );
        }
    }
}
