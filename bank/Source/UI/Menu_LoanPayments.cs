using System;
using System.Collections.Generic;
using System.Reflection;
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
    public static class BankMenu_LoanPay
    {
        private const string SEP = "____________________";

        // Considero empréstimo "realmente quitado" abaixo desse threshold
        private const float ACTIVE_LOAN_THRESHOLD = 0.5f;

        private static string _selectedLoanId = null;

        private static BankCampaignBehavior _behaviorRef;
        private static bool _registered;

        // -------------------------------------------------------------
        // Registro de menus
        // -------------------------------------------------------------
        public static void RegisterMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            if (starter == null)
                return;

            if (_registered)
                return;

            _registered = true;
            _behaviorRef = behavior;

            // Menu de lista de empréstimos
            starter.AddGameMenu(
                "bank_loan_pay",
                L.S("loanpay_list_loading", "Loading active loans..."),
                OnMenuInit_List
            );

            // Menu de detalhes do contrato
            starter.AddGameMenu(
                "bank_loan_detail",
                L.S("loanpay_detail_loading", "Loading contract details..."),
                OnMenuInit_Detail
            );

            // Opções do menu de detalhes
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

            starter.AddGameMenuOption(
                "bank_loan_detail",
                "loan_detail_pay",
                L.S("loanpay_detail_pay", "Pay this loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    var loan = GetSelectedLoan();
                    return loan != null && loan.Remaining > ACTIVE_LOAN_THRESHOLD;
                },
                _ => PromptPayment()
            );

            // Opções fixas do menu de lista de empréstimos
            starter.AddGameMenuOption(
                "bank_loan_pay",
                "loan_pick",
                L.S("loanpay_list_pick", "Select contract"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return HasActiveLoansInCurrentTown();
                },
                _ => ShowLoanPicker()
            );

            starter.AddGameMenuOption(
                "bank_loan_pay",
                "loan_pay_back",
                L.S("loanpay_list_back", "Return to Bank"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ => SafeSwitchToMenu("bank_loanmenu"),
                isLeave: true
            );
        }

        // -------------------------------------------------------------
        // Helpers centrais
        // -------------------------------------------------------------
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

                var list = storage.GetLoans(hero.StringId ?? "player");
                if (list == null || list.Count == 0)
                    return null;

                return list.Find(l => l.LoanId == _selectedLoanId);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasActiveLoansInCurrentTown()
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;
                var settlement = Settlement.CurrentSettlement;

                if (behavior == null || hero == null || settlement == null)
                    return false;

                var storage = behavior.GetStorage();
                if (storage == null)
                    return false;

                string playerId = hero.StringId ?? "player";
                string townId = settlement.StringId ?? "town";

                var loans = storage.GetLoans(playerId) ?? new List<BankLoanData>();
                for (int i = 0; i < loans.Count; i++)
                {
                    var l = loans[i];
                    if (l != null && l.TownId == townId && l.Remaining > ACTIVE_LOAN_THRESHOLD)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // -------------------------------------------------------------
        // Lista de empréstimos da cidade atual
        // -------------------------------------------------------------
        private static void OnMenuInit_List(MenuCallbackArgs args)
        {
            try
            {
                var behavior = GetBehavior();
                var hero = Hero.MainHero;
                var settlement = Settlement.CurrentSettlement;

                if (behavior == null || hero == null || settlement == null)
                {
                    Warn(L.S("loanpay_err_ctx", "Error: invalid context for loan list."));
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

                // Filtra só desta cidade e realmente ativos
                List<BankLoanData> cityLoans = loans.FindAll(l =>
                    l != null &&
                    l.TownId == townId &&
                    l.Remaining > ACTIVE_LOAN_THRESHOLD
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
            }
            catch
            {
                Warn(L.S("loanpay_err_list", "Could not open the loan list."));
            }
        }

        // -------------------------------------------------------------
        // Picker de contratos
        // -------------------------------------------------------------
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
                    l.Remaining > ACTIVE_LOAN_THRESHOLD
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

                var data = new MultiSelectionInquiryData(
                    L.S("loanpay_picker_title", "Select Contract"),
                    L.S("loanpay_picker_desc", "Choose a contract to see details and make payments."),
                    elements,
                    true,
                    1,
                    1,
                    L.S("popup_open", "Open"),
                    L.S("popup_cancel", "Cancel"),
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
                            if (loan == null || loan.Remaining <= ACTIVE_LOAN_THRESHOLD)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                SafeSwitchToMenu("bank_loan_pay");
                                return;
                            }

                            SafeSwitchToMenu("bank_loan_detail");
                        }
                        catch
                        {
                            Warn(L.S("loanpay_err_open_picker_cb", "Error handling selection."));
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
            catch
            {
                Warn(L.S("loanpay_err_open_picker", "Error opening contract selection."));
                SafeSwitchToMenu("bank_loan_pay");
            }
        }

        // -------------------------------------------------------------
        // Detalhes do contrato
        // -------------------------------------------------------------
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

                bool isActive = loan.Remaining > ACTIVE_LOAN_THRESHOLD;

                string statusLine;
                if (!isActive)
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
                body.SetTextVariable("INTEREST", $"{loan.InterestRate:0.##}%");
                body.SetTextVariable("LATEFEE", BankUtils.FmtPct(loan.LateFeeRate / 100f));
                body.SetTextVariable("DAYS", loan.DurationDays);
                body.SetTextVariable("SEP", SEP);
                body.SetTextVariable("ACTION_HINT",
                    isActive
                        ? L.S("loanpay_action_hint_pay", "Select 'Pay this loan' to make a partial payment.")
                        : L.S("loanpay_action_hint_done", "This loan is fully paid."));

                args.MenuTitle = L.T("loanpay_detail_title", "Loan Details");
                SetMenuText(args, body);
            }
            catch
            {
                Warn(L.S("loanpay_err_detail", "Error showing loan details."));
            }
        }

        // -------------------------------------------------------------
        // Cálculo de abatimento (dinâmico, igual ao Python)
        // -------------------------------------------------------------
        private static (float custoPago, float descontoEfetivo, float novoSaldo) CalcularAbatimentoAntecipadoPorSaldo(
            float saldoAtual,
            float jurosPercentTotal,
            int diasRestantes,
            float valorPago)
        {
            saldoAtual = MathF.Max(1f, saldoAtual);
            diasRestantes = Math.Max(1, diasRestantes);
            jurosPercentTotal = MathF.Max(0.01f, jurosPercentTotal);
            valorPago = MathF.Max(0f, valorPago);

            float fracaoJurosNoSaldo = jurosPercentTotal / (100f + jurosPercentTotal);
            float jurosEstimadosNoSaldo = saldoAtual * fracaoJurosNoSaldo;

            float tempoRestanteFrac = MathF.Log10(diasRestantes + 1f) / MathF.Log10(360f + 1f);
            if (float.IsNaN(tempoRestanteFrac))
                tempoRestanteFrac = 1f;
            tempoRestanteFrac = MathF.Min(MathF.Max(tempoRestanteFrac, 0.05f), 1f);

            float jurosNaoConsumidos = jurosEstimadosNoSaldo * tempoRestanteFrac;

            float fracPagamento = MathF.Min(valorPago / saldoAtual, 1f);
            float fatorBeneficio = MathF.Pow(fracPagamento, 0.8f) * MathF.Pow(tempoRestanteFrac, 0.9f);

            float descontoEfetivo = jurosNaoConsumidos * fatorBeneficio * 0.85f;
            descontoEfetivo = MathF.Clamp(descontoEfetivo, 0f, jurosEstimadosNoSaldo);

            float custoNecessarioParaQuitar = saldoAtual - descontoEfetivo;

            float custoPago;
            float novoSaldo;
            if (valorPago >= custoNecessarioParaQuitar)
            {
                custoPago = custoNecessarioParaQuitar;
                novoSaldo = 0f;
            }
            else
            {
                custoPago = valorPago;
                novoSaldo = MathF.Max(0f, saldoAtual - custoPago - descontoEfetivo);
            }

            return (custoPago, descontoEfetivo, novoSaldo);
        }

        // -------------------------------------------------------------
        // Pagamento parcial
        // -------------------------------------------------------------
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
                if (contract == null || contract.Remaining <= ACTIVE_LOAN_THRESHOLD)
                {
                    Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                    SafeSwitchToMenu("bank_loan_pay");
                    return;
                }

                float gold = MathF.Max(0f, (float)hero.Gold);

                float remaining = MathF.Max(0f, contract.Remaining);

                var descObj = L.T("loanpay_popup_desc",
                    "Enter the amount to pay (balance: {BAL}, remaining: {REM}):");
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
                                Warn(L.S("loanpay_not_enough_gold", "You do not have enough."));
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

                            float saldoAtual = MathF.Max(current.Remaining, 1f);
                            int diasRestantes = Math.Max(current.DurationDays, 1);

                            var (custoPago, descontoEfetivo, novoSaldo) = CalcularAbatimentoAntecipadoPorSaldo(
                                saldoAtual,
                                current.InterestRate,
                                diasRestantes,
                                amountToPay
                            );

                            int custoFinal = MathF.Round(custoPago);
                            if (custoFinal > hero.Gold)
                            {
                                Warn(L.S("loanpay_not_enough_gold", "You do not have enough."));
                                return;
                            }

                            hero.ChangeHeroGold(-custoFinal);

                            current.Remaining = novoSaldo;
                            if (current.Remaining < ACTIVE_LOAN_THRESHOLD)
                                current.Remaining = 0f;

                            try
                            {
                                behavior.SyncBankData();
                            }
                            catch
                            {
                                // silencioso
                            }

                            if (descontoEfetivo > 1f)
                            {
                                var payMsg = L.T("loanpay_payment_ok_bonus",
                                    "You paid {PAID} and obtained an early-payment benefit of {BONUS}!");
                                payMsg.SetTextVariable("PAID", BankUtils.FmtDenars(custoFinal));
                                payMsg.SetTextVariable("BONUS", BankUtils.FmtDenars(descontoEfetivo));
                                InformationManager.DisplayMessage(
                                    new InformationMessage(payMsg.ToString(), Color.FromUint(BankUtils.UiGold))
                                );

                                float economiaPercent = custoPago > 0f
                                    ? (descontoEfetivo / custoPago) * 100f
                                    : 0f;

                                var econMsg = L.T("loanpay_bonus_log",
                                    "Effective cost: {COST} ({PERCENT}% saved). New debt: {NEWDEBT}");
                                econMsg.SetTextVariable("COST", BankUtils.FmtDenars(custoFinal));
                                econMsg.SetTextVariable("PERCENT", economiaPercent.ToString("0.00"));
                                econMsg.SetTextVariable("NEWDEBT", BankUtils.FmtDenars(current.Remaining));
                                InformationManager.DisplayMessage(
                                    new InformationMessage(econMsg.ToString(), Color.FromUint(0xFFBBDDEE))
                                );
                            }
                            else
                            {
                                var okMsg = L.T("loanpay_payment_ok",
                                    "Payment of {AMOUNT} completed successfully.");
                                okMsg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(custoFinal));
                                InformationManager.DisplayMessage(
                                    new InformationMessage(okMsg.ToString(), Color.FromUint(BankUtils.UiGold))
                                );
                            }

                            if (current.Remaining <= 0f)
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
                        catch
                        {
                            Warn(L.S("loanpay_err_payment_cb", "Error processing payment."));
                        }
                    },
                    null
                ));
            }
            catch
            {
                Warn(L.S("loanpay_err_payment", "Error starting payment."));
            }
        }

        // -------------------------------------------------------------
        // Utilidades
        // -------------------------------------------------------------
        private static void SetMenuText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args.MenuContext?.GameMenu;
                if (menu == null || text == null)
                    return;

                var field = typeof(GameMenu).GetField("_defaultText", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    return;

                field.SetValue(menu, text);
            }
            catch
            {
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
            }
        }

        private static void Warn(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                msg = "[BanksOfCalradia] An error occurred.";

            InformationManager.DisplayMessage(
                new InformationMessage(
                    msg,
                    Color.FromUint(0xFFFF6666)
                )
            );
        }
    }
}
