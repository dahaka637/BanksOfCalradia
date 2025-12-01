// ============================================
// BanksOfCalradia - BankMenu_LoanPay.cs
// Author: Dahaka
// Version: 2.6.1 (Ultra Safe Context Hardening)
// Description:
//   Loan payment UI (contract list + details + partial payments)
//   - Strict town-context gate (no fallback "player"/"town" ids)
//   - UI stabilization delay (MenuContext/Gauntlet race protection)
//   - Selection reset to avoid "ghost contract" across towns
//   - Safe parsing (comma/dot/culture-safe) for payment input
//   - Guarded reflection/menu text writes (never crashes the game)
// ============================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
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

            // Lista de empréstimos ativos na cidade atual
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

            // Voltar dos detalhes para a lista
            starter.AddGameMenuOption(
                "bank_loan_detail",
                "loan_detail_back",
                L.S("loanpay_detail_back", "Back to loan list"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_loan_pay"); } catch { }
                }
            );

            // Pagar o contrato selecionado
            starter.AddGameMenuOption(
                "bank_loan_detail",
                "loan_detail_pay",
                L.S("loanpay_detail_pay", "Pay this loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    var loan = GetSelectedLoanStrictInCurrentTown();
                    return loan != null && loan.Remaining > ACTIVE_LOAN_THRESHOLD && IsInValidTown();
                },
                _ =>
                {
                    try { PromptPayment(); } catch { }
                }
            );

            // Selecionar contrato (abre picker)
            starter.AddGameMenuOption(
                "bank_loan_pay",
                "loan_pick",
                L.S("loanpay_list_pick", "Select contract"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return HasActiveLoansInCurrentTown();
                },
                _ =>
                {
                    try { ShowLoanPicker(); } catch { }
                }
            );

            // Voltar ao banco
            starter.AddGameMenuOption(
                "bank_loan_pay",
                "loan_pay_back",
                L.S("loanpay_list_back", "Return to Bank"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_loanmenu"); } catch { }
                },
                isLeave: true
            );
        }

        // -------------------------------------------------------------
        // Context Hardening
        // -------------------------------------------------------------
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
                // silencioso
            }
        }

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

        private static bool TryGetStrictTownContext(
            out BankCampaignBehavior behavior,
            out Hero hero,
            out Settlement settlement,
            out Town town,
            out BankStorage storage,
            out string playerId,
            out string townId)
        {
            behavior = null;
            hero = null;
            settlement = null;
            town = null;
            storage = null;
            playerId = null;
            townId = null;

            try
            {
                behavior = GetBehavior();
                hero = Hero.MainHero;
                settlement = Settlement.CurrentSettlement;
                town = settlement?.Town;

                if (behavior == null || hero == null || town == null)
                    return false;

                storage = behavior.GetStorage();
                if (storage == null)
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

        private static BankLoanData GetSelectedLoanStrictInCurrentTown()
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedLoanId))
                    return null;

                if (!TryGetStrictTownContext(
                        out var behavior,
                        out var hero,
                        out var settlement,
                        out var town,
                        out var storage,
                        out var playerId,
                        out var townId))
                    return null;

                var list = storage.GetLoans(playerId);
                if (list == null || list.Count == 0)
                    return null;

                var loan = list.Find(l => l != null && l.LoanId == _selectedLoanId);
                if (loan == null)
                    return null;

                // Garante que o contrato é desta cidade
                if (string.IsNullOrEmpty(loan.TownId) || loan.TownId != townId)
                    return null;

                return loan;
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
                if (!TryGetStrictTownContext(
                        out var behavior,
                        out var hero,
                        out var settlement,
                        out var town,
                        out var storage,
                        out var playerId,
                        out var townId))
                    return false;

                var loans = storage.GetLoans(playerId);
                if (loans == null || loans.Count == 0)
                    return false;

                for (int i = 0; i < loans.Count; i++)
                {
                    var l = loans[i];
                    if (l == null)
                        continue;

                    if (!string.IsNullOrEmpty(l.TownId)
                        && l.TownId == townId
                        && l.Remaining > ACTIVE_LOAN_THRESHOLD)
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
        // Menu: Lista
        // -------------------------------------------------------------
        private static async void OnMenuInit_List(MenuCallbackArgs args)
        {
            try
            {
                await WaitUiAsync(args);

                // Reset de segurança: evita contrato "fantasma"
                _selectedLoanId = null;

                if (!TryGetStrictTownContext(
                        out var behavior,
                        out var hero,
                        out var settlement,
                        out var town,
                        out var storage,
                        out var playerId,
                        out var townId))
                {
                    args.MenuTitle = L.T("loanpay_err_title", "Loans (Unavailable)");
                    SetMenuText(args, L.T("loanpay_err_ctx", "Context lost. Please reopen the bank."));
                    return;
                }

                string townName = settlement.Name?.ToString() ?? L.S("default_city", "City");

                var loans = storage.GetLoans(playerId) ?? new List<BankLoanData>();

                // Filtra só contratos desta cidade e ativos
                List<BankLoanData> cityLoans = loans.FindAll(l =>
                    l != null &&
                    !string.IsNullOrEmpty(l.TownId) &&
                    l.TownId == townId &&
                    l.Remaining > ACTIVE_LOAN_THRESHOLD
                );

                var title = L.T("loanpay_list_title", "Bank of {CITY} — Active Loans");
                title.SetTextVariable("CITY", townName);
                args.MenuTitle = title;

                if (cityLoans.Count == 0)
                {
                    var bodyEmpty = L.T("loanpay_list_body_empty",
                        "Bank of {CITY}\n\nYou have no pending loans at this bank.\n\n{SEP}");
                    bodyEmpty.SetTextVariable("CITY", townName);
                    bodyEmpty.SetTextVariable("SEP", SEP);
                    SetMenuText(args, bodyEmpty);
                    return;
                }

                float totalDebt = 0f;
                for (int i = 0; i < cityLoans.Count; i++)
                    totalDebt += MathF.Max(0f, cityLoans[i].Remaining);

                var body = L.T("loanpay_list_body",
                    "Bank of {CITY}\n\nActive debt at this bank: {DEBT}\n\nUse 'Select contract' to choose a loan.\n\n{SEP}");
                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("DEBT", BankUtils.FmtDenars(totalDebt));
                body.SetTextVariable("SEP", SEP);
                SetMenuText(args, body);
            }
            catch (Exception e)
            {
                Warn(L.S("loanpay_err_open_list", "[BanksOfCalradia] Error opening loan list: ") + e.Message);
            }
        }

        // -------------------------------------------------------------
        // Picker de contratos
        // -------------------------------------------------------------
        private static async void ShowLoanPicker()
        {
            try
            {
                // Pequeno delay para reduzir race-condition
                await Task.Delay(60);

                if (!TryGetStrictTownContext(
                        out var behavior,
                        out var hero,
                        out var settlement,
                        out var town,
                        out var storage,
                        out var playerId,
                        out var townId))
                {
                    Warn(L.S("loanpay_err_ctx_select", "Error: invalid context when opening selection."));
                    BankSafeUI.Switch("bank_loan_pay");
                    return;
                }

                var allLoans = storage.GetLoans(playerId) ?? new List<BankLoanData>();

                var validLoans = allLoans.FindAll(l =>
                    l != null &&
                    !string.IsNullOrEmpty(l.TownId) &&
                    l.TownId == townId &&
                    l.Remaining > ACTIVE_LOAN_THRESHOLD
                );

                if (validLoans.Count == 0)
                {
                    Warn(L.S("loanpay_none_for_city", "No active contracts to display."));
                    BankSafeUI.Switch("bank_loan_pay");
                    return;
                }

                var elements = new List<InquiryElement>();
                int idx = 1;

                for (int i = 0; i < validLoans.Count; i++)
                {
                    var loan = validLoans[i];

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
                            if (selected == null || selected.Count == 0 || selected[0]?.Identifier == null)
                            {
                                Warn(L.S("loanpay_no_selection", "No contract selected."));
                                _selectedLoanId = null;
                                BankSafeUI.Switch("bank_loan_pay");
                                return;
                            }

                            _selectedLoanId = selected[0].Identifier as string;

                            var loanNow = GetSelectedLoanStrictInCurrentTown();
                            if (loanNow == null || loanNow.Remaining <= ACTIVE_LOAN_THRESHOLD)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                _selectedLoanId = null;
                                BankSafeUI.Switch("bank_loan_pay");
                                return;
                            }

                            BankSafeUI.Switch("bank_loan_detail");
                        }
                        catch
                        {
                            Warn(L.S("loanpay_err_open_picker_cb", "Error handling selection."));
                            _selectedLoanId = null;
                            BankSafeUI.Switch("bank_loan_pay");
                        }
                    },
                    _ =>
                    {
                        try
                        {
                            BankSafeUI.Switch("bank_loan_pay");
                        }
                        catch
                        {
                            // silencioso
                        }
                    }
                );

                MBInformationManager.ShowMultiSelectionInquiry(data);
            }
            catch
            {
                Warn(L.S("loanpay_err_open_picker", "Error opening contract selection."));
                try { BankSafeUI.Switch("bank_loan_pay"); } catch { }
            }
        }

        // -------------------------------------------------------------
        // Menu: Detalhes
        // -------------------------------------------------------------
        private static async void OnMenuInit_Detail(MenuCallbackArgs args)
        {
            try
            {
                await WaitUiAsync(args);

                if (!IsInValidTown())
                {
                    SetMenuText(args, L.T("loanpay_err_ctx", "Context lost. Please reopen the bank."));
                    return;
                }

                var loan = GetSelectedLoanStrictInCurrentTown();
                if (loan == null)
                {
                    args.MenuTitle = L.T("loanpay_detail_title", "Loan Details");
                    SetMenuText(args, L.T("loanpay_detail_invalid", "No contract selected."));
                    return;
                }

                string townName = Settlement.CurrentSettlement?.Name?.ToString() ?? L.S("default_city", "City");
                bool isActive = loan.Remaining > ACTIVE_LOAN_THRESHOLD;

                string statusLine;
                if (isActive)
                {
                    var st = L.T("loanpay_status_remaining", "Remaining: {AMOUNT}");
                    st.SetTextVariable("AMOUNT", BankUtils.FmtDenars(loan.Remaining));
                    statusLine = st.ToString();
                }
                else
                {
                    statusLine = L.S("loanpay_status_paid", "Status: Paid off");
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
                body.SetTextVariable(
                    "ACTION_HINT",
                    isActive
                        ? L.S("loanpay_action_hint_pay", "Select 'Pay this loan' to make a partial payment.")
                        : L.S("loanpay_action_hint_done", "This loan is fully paid.")
                );

                args.MenuTitle = L.T("loanpay_detail_title", "Loan Details");
                SetMenuText(args, body);
            }
            catch (Exception e)
            {
                Warn(L.S("loanpay_err_open_detail", "[BanksOfCalradia] Error showing loan details: ") + e.Message);
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
                var contract = GetSelectedLoanStrictInCurrentTown();
                if (contract == null || contract.Remaining <= ACTIVE_LOAN_THRESHOLD)
                {
                    Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                    BankSafeUI.Switch("bank_loan_pay");
                    return;
                }

                if (!TryGetStrictTownContext(
                        out var behavior,
                        out var hero,
                        out var settlement,
                        out var town,
                        out var storage,
                        out var playerId,
                        out var townId))
                {
                    Warn(L.S("loanpay_err_ctx_payment", "Error: invalid context for payment."));
                    BankSafeUI.Switch("bank_loan_pay");
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
                            // Revalida contexto dentro do callback (evita stale refs)
                            if (!TryGetStrictTownContext(
                                    out var behavior2,
                                    out var hero2,
                                    out var settlement2,
                                    out var town2,
                                    out var storage2,
                                    out var playerId2,
                                    out var townId2))
                            {
                                Warn(L.S("loanpay_err_ctx_payment", "Error: invalid context for payment."));
                                BankSafeUI.Switch("bank_loan_pay");
                                return;
                            }

                            if (!TryParsePositiveFloatFlexible(input, out float value))
                            {
                                Warn(L.S("loanpay_invalid_value", "Invalid amount."));
                                return;
                            }

                            if (value <= 0f)
                            {
                                Warn(L.S("loanpay_value_gt_zero", "Amount must be greater than zero."));
                                return;
                            }

                            if (value > hero2.Gold)
                            {
                                Warn(L.S("loanpay_not_enough_gold", "You do not have enough."));
                                return;
                            }

                            // Rebusca o contrato atual do storage (sempre a fonte da verdade)
                            var list = storage2.GetLoans(playerId2);
                            var current = list?.Find(l => l != null && l.LoanId == _selectedLoanId);
                            if (current == null || string.IsNullOrEmpty(current.TownId) || current.TownId != townId2)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                _selectedLoanId = null;
                                BankSafeUI.Switch("bank_loan_pay");
                                return;
                            }

                            if (current.Remaining <= ACTIVE_LOAN_THRESHOLD)
                            {
                                Warn(L.S("loanpay_not_found", "Contract not found or invalid."));
                                _selectedLoanId = null;
                                BankSafeUI.Switch("bank_loan_pay");
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

                            int custoFinal = (int)MathF.Round(custoPago);
                            if (custoFinal <= 0)
                            {
                                Warn(L.S("loanpay_value_too_low", "Payment amount too low."));
                                return;
                            }

                            if (custoFinal > hero2.Gold)
                            {
                                Warn(L.S("loanpay_not_enough_gold", "You do not have enough."));
                                return;
                            }

                            hero2.ChangeHeroGold(-custoFinal);

                            current.Remaining = novoSaldo;
                            if (current.Remaining < ACTIVE_LOAN_THRESHOLD)
                                current.Remaining = 0f;

                            try { behavior2.SyncBankData(); } catch { }

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

                                // Limpa seleção para evitar "contrato fantasma"
                                _selectedLoanId = null;

                                InformationManager.DisplayMessage(
                                    new InformationMessage(
                                        L.S("loanpay_now_paid", "Loan fully paid."),
                                        Color.FromUint(0xFF66FF66)
                                    )
                                );
                            }

                            BankSafeUI.Switch("bank_loan_detail");
                        }
                        catch
                        {
                            Warn(L.S("loanpay_err_payment_cb", "Error processing payment."));
                            try { BankSafeUI.Switch("bank_loan_detail"); } catch { }
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
        // Parsing seguro (comma/dot/culture-safe)
        // -------------------------------------------------------------
        private static bool TryParsePositiveFloatFlexible(string input, out float value)
        {
            value = 0f;

            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return false;

                string s = input.Trim();
                s = s.Replace(" ", "");

                // 1) CurrentCulture
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out double v1) && v1 > 0d)
                {
                    value = (float)Math.Min(v1, float.MaxValue);
                    return value > 0f;
                }

                // 2) Invariant
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out double v2) && v2 > 0d)
                {
                    value = (float)Math.Min(v2, float.MaxValue);
                    return value > 0f;
                }

                // 3) Normalização separadores mistos
                int lastDot = s.LastIndexOf('.');
                int lastComma = s.LastIndexOf(',');

                if (lastDot >= 0 && lastComma >= 0)
                {
                    char dec = (lastDot > lastComma) ? '.' : ',';
                    char thou = (dec == '.') ? ',' : '.';

                    string normalized = s.Replace(thou.ToString(), "");
                    normalized = normalized.Replace(dec, '.');

                    if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out double v3) && v3 > 0d)
                    {
                        value = (float)Math.Min(v3, float.MaxValue);
                        return value > 0f;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // -------------------------------------------------------------
        // Utilidades
        // -------------------------------------------------------------
        private static void SetMenuText(MenuCallbackArgs args, TextObject text)
        {
            try
            {
                var menu = args?.MenuContext?.GameMenu;
                if (menu == null || text == null)
                    return;

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

        private static void Warn(string msg)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(msg))
                    msg = "[BanksOfCalradia] An error occurred.";

                InformationManager.DisplayMessage(
                    new InformationMessage(msg, Color.FromUint(0xFFFF6666))
                );
            }
            catch
            {
                // silencioso
            }
        }
    }
}
