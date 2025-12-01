// ============================================
// BanksOfCalradia - BankMenu_Loan.cs
// Author: Dahaka
// Version: 2.6.1 (Ultra Safe Context Hardening)
// Description:
//   Loans interface with real contracts + persistence (BankStorage)
//   - Clean UI (credit summary + simulation)
//   - Localized texts via helper L
//   - Strict town context gate (no "town" fallback ids)
//   - UI delay hardening (Gauntlet/MenuContext stabilization)
//   - Guarded callbacks (Inquiry/TextInquiry/actions)
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
    public static class BankMenu_Loan
    {
        // ------------------------------------------------------------
        // Estado atual da simulação (mantido em memória estática)
        // ------------------------------------------------------------
        private class LoanSimState
        {
            public int RequestedAmount = 0;   // valor que o jogador digitou
            public int Installments = 0;      // número de parcelas
        }

        private static readonly LoanSimState _sim = new LoanSimState();

        // Separador usado no corpo do menu
        private const string SEP = "____________________";

        private static bool _registered;

        // ------------------------------------------------------------
        // Context Hardening
        // ------------------------------------------------------------
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

        private static bool TryGetStrictTownContext(
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
                hero = Hero.MainHero;
                if (hero == null)
                    return false;

                settlement = Settlement.CurrentSettlement;
                town = settlement?.Town;
                if (town == null)
                    return false;

                if (behavior == null)
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

        private static bool TryGetTownContextNoStorage(
            out Hero hero,
            out Settlement settlement,
            out Town town,
            out string playerId,
            out string townId)
        {
            hero = null;
            settlement = null;
            town = null;
            playerId = null;
            townId = null;

            try
            {
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
                // silencioso
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
                // silencioso
            }
        }

        private static void Warn(string msg)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFFF6666)));
            }
            catch
            {
                // silencioso
            }
        }

        // ------------------------------------------------------------
        // Registro de menus
        // ------------------------------------------------------------
        public static void RegisterMenu(CampaignGameStarter starter, BankCampaignBehavior behavior)
        {
            if (starter == null)
                return;

            if (_registered)
                return;
            _registered = true;

            // Menu principal de empréstimos
            starter.AddGameMenu(
                "bank_loanmenu",
                L.S("loan_menu_loading", "Loading bank loan information..."),
                args => OnMenuInit_Main(args, behavior)
            );

            // Opção: solicitar empréstimo
            starter.AddGameMenuOption(
                "bank_loanmenu",
                "loan_request",
                L.S("loan_menu_option_request", "Request a loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_loan_request"); } catch { }
                }
            );

            // Opção: pagar empréstimos (menu é criado no BankMenu_LoanPay)
            starter.AddGameMenuOption(
                "bank_loanmenu",
                "loan_pay",
                L.S("loan_menu_option_pay", "Pay existing loans"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_loan_pay"); } catch { }
                }
            );

            // Opção: voltar ao banco
            starter.AddGameMenuOption(
                "bank_loanmenu",
                "loan_back",
                L.S("loan_menu_option_back", "Return to Bank"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try { BankSafeUI.Switch("bank_menu"); } catch { }
                }
            );

            // Submenu de simulação/contratação
            starter.AddGameMenu(
                "bank_loan_request",
                L.S("loan_req_loading", "Loading loan simulation..."),
                args => OnMenuInit_Request(args, behavior)
            );

            // Editar valor
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_amount_edit",
                L.S("loan_req_edit_amount", "Change requested amount"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { PromptEditAmount(); } catch { }
                }
            );

            // Editar parcelas
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_inst_edit",
                L.S("loan_req_edit_installments", "Change number of installments (1–360)"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { PromptEditInstallments(); } catch { }
                }
            );

            // Confirmar empréstimo
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_confirm",
                L.S("loan_req_confirm", "Confirm loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return IsInValidTown();
                },
                _ =>
                {
                    try { ConfirmLoan(behavior); } catch { }
                }
            );

            // Cancelar
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_cancel",
                L.S("loan_req_back", "Back"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                _ =>
                {
                    try
                    {
                        ClearSimulation();
                        BankSafeUI.Switch("bank_loanmenu");
                    }
                    catch
                    {
                        // silencioso
                    }
                }
            );
        }

        // ------------------------------------------------------------
        // Menu principal
        // ------------------------------------------------------------
        private static async void OnMenuInit_Main(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                await WaitUiAsync(args);

                if (!TryGetTownContextNoStorage(out var hero, out var settlement, out var town, out var playerId, out var townId))
                {
                    args.MenuTitle = L.T("loan_menu_unavailable", "Bank Loans (Unavailable)");
                    SafeSetMenuText(args, L.T("loan_menu_need_town", "You must be inside a town to access loan services."));
                    return;
                }

                string townName = settlement.Name?.ToString() ?? L.S("default_city", "City");
                float prosperity = town.Prosperity;
                float renown = hero?.Clan?.Renown ?? 0f;

                var sim = CalcLoanForecastParametric(prosperity, renown, 12);

                float totalDebt = 0f;
                if (behavior?.GetStorage() != null)
                    totalDebt = GetPlayerTotalDebt(behavior, playerId);
                else
                {
                    args.MenuTitle = L.T("loan_menu_unavailable", "Bank Loans (Unavailable)");
                    SafeSetMenuText(args, L.T("loan_menu_loading",
                        "The bank system is still initializing.\n\nPlease reopen this menu shortly."));
                    return;
                }


                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                var body = L.T("loan_menu_body",
                    "Bank of {CITY} — Credit\n\n" +
                    "• Estimated interest (12x): {INTEREST}\n" +
                    "• Daily late fee: {LATEFEE}\n" +
                    "• Available credit: {CREDIT}\n" +
                    "• Active debt: {DEBT}\n\n" +
                    "{SEP}\n\n" +
                    "Select an option below.");
                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("INTEREST", BankUtils.FmtPct(sim.totalInterestPct / 100f));
                body.SetTextVariable("LATEFEE", BankUtils.FmtPct(sim.lateFeePct / 100f));
                body.SetTextVariable("CREDIT", BankUtils.FmtDenars(availableCredit));
                body.SetTextVariable("DEBT", BankUtils.FmtDenars(totalDebt));
                body.SetTextVariable("SEP", SEP);

                var title = L.T("loan_menu_title", "Bank of {CITY} — Credit");
                title.SetTextVariable("CITY", townName);

                args.MenuTitle = title;
                SafeSetMenuText(args, body);
            }
            catch (Exception e)
            {
                Warn(L.S("loan_menu_error", "Error loading loan menu: ") + e.Message);
            }
        }

        // ------------------------------------------------------------
        // Submenu de simulação
        // ------------------------------------------------------------
        private static async void OnMenuInit_Request(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                await WaitUiAsync(args);

                if (!TryGetTownContextNoStorage(out var hero, out var settlement, out var town, out var playerId, out var townId))
                {
                    args.MenuTitle = L.T("loan_req_unavailable", "Loan Request (Unavailable)");
                    SafeSetMenuText(args, L.T("loan_req_need_town", "You must be inside a town to simulate loan terms."));
                    return;
                }

                string townName = settlement.Name?.ToString() ?? L.S("default_city", "City");
                float prosperity = town.Prosperity;
                float renown = hero?.Clan?.Renown ?? 0f;

                int parcelas = Math.Max(_sim.Installments, 1);
                var sim = CalcLoanForecastParametric(prosperity, renown, parcelas);

                float totalDebt = 0f;
                if (behavior != null)
                    totalDebt = GetPlayerTotalDebt(behavior, playerId);

                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                bool hasInput = _sim.RequestedAmount > 0 && _sim.Installments > 0;

                float baseAmount = MathF.Max(1f, (float)_sim.RequestedAmount);
                float jurosPct = sim.totalInterestPct;
                float valorTotalReal = hasInput ? baseAmount * (1f + jurosPct / 100f) : 0f;
                float valorParcelaReal = hasInput ? (valorTotalReal / MathF.Max(1f, (float)_sim.Installments)) : 0f;

                string req = _sim.RequestedAmount > 0 ? BankUtils.FmtDenars(_sim.RequestedAmount) : "—";
                string parc = _sim.Installments > 0 ? $"{_sim.Installments}x" : "—";
                string juros = hasInput ? BankUtils.FmtPct(jurosPct / 100f) : "—";
                string total = hasInput ? BankUtils.FmtDenars(valorTotalReal) : "—";
                string parcVal = hasInput ? BankUtils.FmtDenars(valorParcelaReal) : "—";

                string warnExtra = "";
                if (_sim.RequestedAmount > 0 && _sim.RequestedAmount > availableCredit)
                {
                    var warnObj = L.T("loan_req_warn_credit",
                        "Attention: requested amount ({REQ}) exceeds your available credit ({CREDIT}).");
                    warnObj.SetTextVariable("REQ", BankUtils.FmtDenars(_sim.RequestedAmount));
                    warnObj.SetTextVariable("CREDIT", BankUtils.FmtDenars(availableCredit));
                    warnExtra = "\n\n" + warnObj.ToString();
                }

                var body = L.T("loan_req_body",
                    "Loan Request — Bank of {CITY}\n\n" +
                    "Contract parameters:\n" +
                    "• Requested amount: {REQ}\n" +
                    "• Installments: {INST}\n\n" +
                    "{SEP}\n\n" +
                    "Current simulation:\n" +
                    "• Available credit: {CREDIT}\n" +
                    "• Total interest rate: {INTEREST}\n" +
                    "• Total with interest: {TOTAL}\n" +
                    "• Installment value: {INSTALLMENT}\n" +
                    "• Daily late fee: {LATEFEE}{WARN}");
                body.SetTextVariable("CITY", townName);
                body.SetTextVariable("REQ", req);
                body.SetTextVariable("INST", parc);
                body.SetTextVariable("SEP", SEP);
                body.SetTextVariable("CREDIT", BankUtils.FmtDenars(availableCredit));
                body.SetTextVariable("INTEREST", juros);
                body.SetTextVariable("TOTAL", total);
                body.SetTextVariable("INSTALLMENT", parcVal);
                body.SetTextVariable("LATEFEE", BankUtils.FmtPct(sim.lateFeePct / 100f));
                body.SetTextVariable("WARN", warnExtra);

                var title = L.T("loan_req_title", "Request Loan — {CITY}");
                title.SetTextVariable("CITY", townName);

                args.MenuTitle = title;
                SafeSetMenuText(args, body);
            }
            catch (Exception e)
            {
                Warn(L.S("loan_req_error", "Error updating loan simulation: ") + e.Message);
            }
        }

        // ------------------------------------------------------------
        // Confirmação real do empréstimo
        // ------------------------------------------------------------
        private static void ConfirmLoan(BankCampaignBehavior behavior)
        {
            try
            {
                if (!TryGetStrictTownContext(behavior, out var hero, out var settlement, out var town, out var storage, out var playerId, out var townId))
                {
                    Warn(L.S("loan_confirm_invalid_player_or_city", "Error: invalid player or town."));
                    return;
                }

                if (_sim.RequestedAmount <= 0)
                {
                    Warn(L.S("loan_confirm_need_amount", "Set the loan amount first."));
                    return;
                }

                if (_sim.Installments <= 0)
                {
                    Warn(L.S("loan_confirm_need_installments", "Set the number of installments before confirming."));
                    return;
                }

                int parcelas = ClampInt(_sim.Installments, 1, 360);

                float prosperity = town.Prosperity;
                float renown = hero.Clan?.Renown ?? 0f;

                var sim = CalcLoanForecastParametric(prosperity, renown, parcelas);

                float totalDebt = GetPlayerTotalDebt(behavior, playerId);
                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                if (_sim.RequestedAmount > availableCredit)
                {
                    Warn(L.S("loan_confirm_exceeds_credit", "Requested amount exceeds your available credit."));
                    return;
                }

                // Capturar estável para o popup/ação
                int reqAmount = _sim.RequestedAmount;
                float jurosPct = sim.totalInterestPct;
                float multaLate = sim.lateFeePct;

                float valorTotal = reqAmount * (1f + jurosPct / 100f);
                float valorParcela = valorTotal / parcelas;

                string townName = settlement.Name?.ToString() ?? "City";

                var textObj = L.T("loan_confirm_popup_text",
                    "Loan Contract Preview — Bank of {CITY}\n\n" +
                    "• Amount requested: {AMOUNT}\n" +
                    "• Installments: {INSTALLMENTS}x\n" +
                    "• Interest rate: {INTEREST}\n" +
                    "• Daily late fee: {LATEFEE}\n" +
                    "• Total to repay: {TOTAL}\n" +
                    "• Installment value: {INSTALLMENT}\n\n" +
                    "Do you confirm this contract?");
                textObj.SetTextVariable("CITY", townName);
                textObj.SetTextVariable("AMOUNT", BankUtils.FmtDenarsFull(reqAmount));
                textObj.SetTextVariable("INSTALLMENTS", parcelas);
                textObj.SetTextVariable("INTEREST", BankUtils.FmtPct(jurosPct / 100f));
                textObj.SetTextVariable("LATEFEE", BankUtils.FmtPct(multaLate / 100f));
                textObj.SetTextVariable("TOTAL", BankUtils.FmtDenarsFull(valorTotal));
                textObj.SetTextVariable("INSTALLMENT", BankUtils.FmtDenarsFull(valorParcela));

                // Usa BankSafeUI.Inquiry para reduzir race-condition de UI
                BankSafeUI.Inquiry(new InquiryData(
                    L.S("loan_confirm_popup_title", "Confirm Loan Contract"),
                    textObj.ToString(),
                    true,
                    true,
                    L.S("popup_confirm", "Confirm"),
                    L.S("popup_cancel", "Cancel"),
                    () =>
                    {
                        try
                        {
                            StartCreateLoan(behavior, playerId, townId, reqAmount, parcelas, jurosPct, multaLate);
                        }
                        catch
                        {
                            // silencioso
                        }
                    },
                    () =>
                    {
                        try
                        {
                            ClearSimulation();
                            BankSafeUI.Switch("bank_loan_request");
                        }
                        catch
                        {
                            // silencioso
                        }
                    }
                ));
            }
            catch (Exception e)
            {
                Warn(L.S("loan_confirm_error", "Error registering the loan: ") + e.Message);
            }
        }

        private static async void StartCreateLoan(
            BankCampaignBehavior behavior,
            string playerId,
            string townId,
            int amount,
            int installments,
            float jurosPct,
            float multaLate)
        {
            try
            {
                // Captura hero atual (pode mudar se o jogador sair do menu)
                var hero = Hero.MainHero;
                if (hero == null)
                {
                    Warn(L.S("loan_confirm_invalid_player_or_city", "Error: invalid player or town."));
                    return;
                }

                // Limpa (antes) para evitar reentradas estranhas
                ClearSimulation();

                // Recarrega UI "limpa" e dá um pequeno tempo para estabilizar
                BankSafeUI.Switch("bank_loan_request");
                await Task.Delay(80);

                var storage = behavior?.GetStorage();
                if (storage == null)
                {
                    Warn(L.S("loan_confirm_storage_missing", "Error: bank storage not available."));
                    return;
                }

                storage.CreateLoan(
                    playerId,
                    townId,
                    amount,
                    jurosPct,
                    multaLate,
                    installments
                );

                // Concede o dinheiro do empréstimo
                hero.ChangeHeroGold(amount);

                var okMsg = L.T("loan_confirm_ok",
                    "Loan of {AMOUNT} successfully created.\nTotal interest rate: {INTEREST}.");
                okMsg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                okMsg.SetTextVariable("INTEREST", BankUtils.FmtPct(jurosPct / 100f));

                InformationManager.DisplayMessage(new InformationMessage(
                    okMsg.ToString(),
                    Color.FromUint(BankUtils.UiGold)
                ));

                BankSafeUI.Switch("bank_loanmenu");
            }
            catch (Exception ex)
            {
                Warn(L.S("loan_confirm_error", "Error registering the loan: ") + ex.Message);
            }
        }

        // ------------------------------------------------------------
        // Soma total de dívidas do jogador
        // ------------------------------------------------------------
        private static float GetPlayerTotalDebt(BankCampaignBehavior behavior, string playerId)
        {
            try
            {
                if (behavior == null)
                    return 0f;

                var storage = behavior.GetStorage();
                if (storage == null)
                    return 0f;

                var loans = storage.GetLoans(playerId ?? "player");
                if (loans == null)
                    return 0f;

                if (loans == null || loans.Count == 0)
                    return 0f;

                float total = 0f;
                for (int i = 0; i < loans.Count; i++)
                    total += loans[i].Remaining;

                return total;
            }
            catch
            {
                return 0f;
            }
        }

        // ------------------------------------------------------------
        // Popups
        // ------------------------------------------------------------
        private static void PromptEditAmount()
        {
            try
            {
                InformationManager.ShowTextInquiry(new TextInquiryData(
                    L.S("loan_popup_amount_title", "Requested amount"),
                    L.S("loan_popup_amount_desc", "Enter the desired amount (denars):"),
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: L.S("popup_confirm", "Confirm"),
                    negativeText: L.S("popup_cancel", "Cancel"),
                    affirmativeAction: input =>
                    {
                        try
                        {
                            if (!TryParsePositiveIntFlexible(input, out int val))
                            {
                                Warn(L.S("loan_popup_amount_invalid", "Invalid amount."));
                                return;
                            }

                            _sim.RequestedAmount = Math.Max(0, val);
                            BankSafeUI.Switch("bank_loan_request");
                        }
                        catch
                        {
                            // silencioso
                        }
                    },
                    negativeAction: null
                ));
            }
            catch
            {
                // silencioso
            }
        }

        private static void PromptEditInstallments()
        {
            try
            {
                InformationManager.ShowTextInquiry(new TextInquiryData(
                    L.S("loan_popup_inst_title", "Installments"),
                    L.S("loan_popup_inst_desc", "Enter the number of installments (1–360):"),
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: L.S("popup_confirm", "Confirm"),
                    negativeText: L.S("popup_cancel", "Cancel"),
                    affirmativeAction: input =>
                    {
                        try
                        {
                            if (!TryParsePositiveIntFlexible(input, out int n))
                            {
                                Warn(L.S("loan_popup_inst_invalid", "Invalid number."));
                                return;
                            }

                            _sim.Installments = ClampInt(n, 1, 360);
                            BankSafeUI.Switch("bank_loan_request");
                        }
                        catch
                        {
                            // silencioso
                        }
                    },
                    negativeAction: null
                ));
            }
            catch
            {
                // silencioso
            }
        }

        // ------------------------------------------------------------
        // Loan Forecast (v1.6.1 Curva Suave Calibrada)
        // Fix: usa Math.Exp (double) com cast para float no sigmoide
        // ------------------------------------------------------------
        private static (float maxLoan, float totalInterestPct, float totalWithInterest, float installmentValue, float lateFeePct)
            CalcLoanForecastParametric(float prosperity, float renown, int installments)
        {
            const float PESO_RENOME_VALOR = 2.5f;
            const float PESO_PROSP_VALOR = 5.0f;
            const float PESO_RENOME_JUROS = 2.2f;
            const float PESO_PROSP_JUROS = 1.25f;
            const float PESO_PARCELAS_JUROS = 1.15f;

            const float SUAV_RENOME = 400f;
            const float SUAV_PROSP = 7000f;
            const float SUAV_LOG = 0.9f;

            const float ESCALA_VALOR = 5.0f;
            const float JUROS_BASE = 12.0f;
            const float JUROS_MIN = 4.0f;
            const float JUROS_MAX = 300.0f;

            const float MULTA_BASE = 1.15f;
            const float MULTA_ESCALA_PROSP = 1.1f;
            const float MULTA_ESCALA_RENOME = 1.05f;
            const float MULTA_MIN = 0.5f;
            const float MULTA_MAX = 5.0f;

            installments = MathF.Min(MathF.Max(installments, 1), 360);
            prosperity = MathF.Max(prosperity, 1f);
            renown = MathF.Max(renown, 1f);

            float fatorRenome = MathF.Log10(1f + (renown / SUAV_RENOME)) * 3.5f;
            fatorRenome = MathF.Min(fatorRenome, 3.5f);

            float fatorProsp = MathF.Log10(1f + (prosperity / SUAV_PROSP) * 2.0f) * 2.0f;

            float efeitoRenome = MathF.Log(1f + fatorRenome * (1f / SUAV_LOG)) / 1.4f;
            float efeitoProsp = MathF.Log(1f + fatorProsp * (1f / SUAV_LOG));

            float valorMax = MathF.Pow(prosperity, 0.85f) * MathF.Pow(renown, 0.7f) / ESCALA_VALOR;
            valorMax *= ((1f + efeitoProsp * PESO_PROSP_VALOR) * (1f + efeitoRenome * PESO_RENOME_VALOR)) * 2f;

            float risco = ((1f / (1f + efeitoProsp * PESO_PROSP_JUROS))
                         + (1f / (1f + efeitoRenome * PESO_RENOME_JUROS))) / 2f;

            float fatorParcelas = 1f + MathF.Log(1f + installments) * PESO_PARCELAS_JUROS;
            float jurosFinal = JUROS_BASE * risco * fatorParcelas;
            jurosFinal = MathF.Clamp(jurosFinal, JUROS_MIN, JUROS_MAX);

            float x = MathF.Max(1f, renown);
            float expTerm = (float)System.Math.Exp((x - 600f) / 220f);
            float s = 1f / (1f + expTerm);
            float fatorDescontoRenome = 0.60f + 0.60f * s;
            fatorDescontoRenome = MathF.Min(1.20f, MathF.Max(0.60f, fatorDescontoRenome));
            jurosFinal *= fatorDescontoRenome;

            float valorTotal = valorMax * (1f + jurosFinal / 100f);
            float valorParcela = valorTotal / installments;

            float fatorMultaProsp = MathF.Pow(prosperity / SUAV_PROSP, 0.7f);
            float fatorMultaRenome = MathF.Pow(1.2f, -(renown / SUAV_RENOME));
            float multa = MULTA_BASE + fatorMultaProsp * MULTA_ESCALA_PROSP * (fatorMultaRenome / MULTA_ESCALA_RENOME);
            multa = MathF.Clamp(multa, MULTA_MIN, MULTA_MAX);

            static float ArredondarBonito(float v) => MathF.Round(v / 50f) * 50f;
            float valorMaxR = ArredondarBonito(valorMax);
            float valorTotalR = MathF.Round(valorTotal);
            float valorParcelaR = MathF.Round(valorParcela);

            return (valorMaxR, jurosFinal, valorTotalR, valorParcelaR, multa);
        }

        // ------------------------------------------------------------
        // Utilidades
        // ------------------------------------------------------------
        private static bool TryParsePositiveIntFlexible(string input, out int value)
        {
            value = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return false;

                string s = input.Trim();
                s = s.Replace(" ", "");

                // 1) Tenta direto no CurrentCulture
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out double v1) && v1 > 0d)
                {
                    if (v1 % 1d != 0d)
                        return false;

                    if (v1 > int.MaxValue)
                    {
                        value = int.MaxValue;
                        return true;
                    }

                    value = (int)v1;
                    return true;
                }

                // 2) Invariant
                if (double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out double v2) && v2 > 0d)
                {
                    if (v2 % 1d != 0d)
                        return false;

                    if (v2 > int.MaxValue)
                    {
                        value = int.MaxValue;
                        return true;
                    }

                    value = (int)v2;
                    return true;
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
                        if (v3 % 1d != 0d)
                            return false;

                        if (v3 > int.MaxValue)
                        {
                            value = int.MaxValue;
                            return true;
                        }

                        value = (int)v3;
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

        private static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void ClearSimulation()
        {
            _sim.RequestedAmount = 0;
            _sim.Installments = 0;
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
                // Evita crash se o menu não existir ainda
            }
        }
    }
}
