using System;
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
    // ============================================
    // BankMenu_Loan - Sistema de Empréstimos
    // - Interface limpa e funcional
    // - Contratos reais e persistência (BankStorage)
    // - Localização nativa via helper L
    // - Proteções contra null/crash
    // ============================================
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

        // ------------------------------------------------------------
        // Registro de menus
        // ------------------------------------------------------------
        private static bool _registered;

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
                    return true;
                },
                _ => BankSafeUI.Switch("bank_loan_request")

            );

            // Opção: pagar empréstimos (menu é criado no BankMenu_LoanPay)
            starter.AddGameMenuOption(
                "bank_loanmenu",
                "loan_pay",
                L.S("loan_menu_option_pay", "Pay existing loans"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return true;
                },
                _ => BankSafeUI.Switch("bank_loan_pay")

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
                _ => BankSafeUI.Switch("bank_menu")

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
                    return true;
                },
                _ => PromptEditAmount()
            );

            // Editar parcelas
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_inst_edit",
                L.S("loan_req_edit_installments", "Change number of installments (1–360)"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return true;
                },
                _ => PromptEditInstallments()
            );

            // Confirmar empréstimo
            starter.AddGameMenuOption(
                "bank_loan_request",
                "loan_confirm",
                L.S("loan_req_confirm", "Confirm loan"),
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                    return true;
                },
                _ => ConfirmLoan(behavior)
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
                    ClearSimulation();
                    BankSafeUI.Switch("bank_loanmenu");
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
                // 🛡️ Delay crítico: garante que Gauntlet e MenuContext existem
                await System.Threading.Tasks.Task.Delay(80);

                if (args.MenuContext == null || args.MenuContext.GameMenu == null)
                    await System.Threading.Tasks.Task.Delay(120);

                // 🚨 Proteção absoluta: proíbe execução fora de cidade real
                var settlement = Settlement.CurrentSettlement;
                var hero = Hero.MainHero;

                if (settlement?.Town == null)
                {
                    args.MenuTitle = L.T("loan_menu_unavailable", "Bank Loans (Unavailable)");
                    BankSafeUI.SetText(
                        args,
                        L.T("loan_menu_need_town", "You must be inside a town to access loan services.")
                    );
                    return;
                }

                // Agora é 100% seguro usar Town, Name e Hero.Clan
                string townName = settlement.Name.ToString();
                float prosperity = settlement.Town.Prosperity;
                float renown = hero?.Clan?.Renown ?? 0f;

                // Simulação base com 12 parcelas
                var sim = CalcLoanForecastParametric(prosperity, renown, 12);

                // Dívidas ativas reduzem crédito
                string playerId = hero?.StringId ?? "player";
                float totalDebt = behavior != null
                    ? GetPlayerTotalDebt(behavior, playerId)
                    : 0f;

                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                // Corpo do menu
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

                // Título
                args.MenuTitle = L.T("loan_menu_title", "Bank of {CITY} — Credit");
                args.MenuTitle.SetTextVariable("CITY", townName);

                // Define texto interno do menu de forma segura
                var field = typeof(GameMenu).GetField(
                    "_defaultText",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                field?.SetValue(args.MenuContext.GameMenu, body);
            }
            catch (Exception e)
            {
                Warn(L.S("loan_menu_error", "Error loading loan menu: ") + e.Message);
            }
        }


        // ------------------------------------------------------------
        // Submenu de simulação
        private static async void OnMenuInit_Request(MenuCallbackArgs args, BankCampaignBehavior behavior)
        {
            try
            {
                // 🛡️ Delay crítico: garante GameMenuContext + UI
                await System.Threading.Tasks.Task.Delay(80);

                if (args.MenuContext == null || args.MenuContext.GameMenu == null)
                    await System.Threading.Tasks.Task.Delay(120);

                // 🚨 Proteção absoluta: cidade REAL obrigatória
                var settlement = Settlement.CurrentSettlement;
                var hero = Hero.MainHero;

                if (settlement?.Town == null || hero == null)
                {
                    args.MenuTitle = L.T("loan_req_unavailable", "Loan Request (Unavailable)");
                    BankSafeUI.SetText(
                        args,
                        L.T("loan_req_need_town", "You must be inside a town to simulate loan terms.")
                    );
                    return;
                }

                // Agora é 100% seguro usar Town.Name, Prosperity e Hero.Clan
                string townName = settlement.Name.ToString();
                string playerId = hero.StringId;

                float prosperity = settlement.Town.Prosperity;
                float renown = hero.Clan?.Renown ?? 0f;

                // Simulação
                int parcelas = Math.Max(_sim.Installments, 1);
                var sim = CalcLoanForecastParametric(prosperity, renown, parcelas);

                float totalDebt = behavior != null
                    ? GetPlayerTotalDebt(behavior, playerId)
                    : 0f;

                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                bool hasInput = _sim.RequestedAmount > 0 && _sim.Installments > 0;

                float baseAmount = MathF.Max(1f, (float)_sim.RequestedAmount);
                float jurosPct = sim.totalInterestPct;
                float valorTotalReal = hasInput ? baseAmount * (1f + jurosPct / 100f) : 0f;
                float valorParcelaReal = hasInput
                    ? valorTotalReal / MathF.Max(1f, (float)_sim.Installments)
                    : 0f;

                string req = _sim.RequestedAmount > 0 ? BankUtils.FmtDenars(_sim.RequestedAmount) : "—";
                string parc = _sim.Installments > 0 ? $"{_sim.Installments}x" : "—";
                string juros = hasInput ? BankUtils.FmtPct(jurosPct / 100f) : "—";
                string total = hasInput ? BankUtils.FmtDenars(valorTotalReal) : "—";
                string parcVal = hasInput ? BankUtils.FmtDenars(valorParcelaReal) : "—";

                string warn = "";
                if (_sim.RequestedAmount > availableCredit)
                {
                    var warnObj = L.T("loan_req_warn_credit",
                        "Attention: requested amount ({REQ}) exceeds your available credit ({CREDIT}).");
                    warnObj.SetTextVariable("REQ", BankUtils.FmtDenars(_sim.RequestedAmount));
                    warnObj.SetTextVariable("CREDIT", BankUtils.FmtDenars(availableCredit));
                    warn = "\n " + warnObj.ToString();
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
                body.SetTextVariable("WARN", warn);

                // Título seguro
                args.MenuTitle = L.T("loan_req_title", "Request Loan — {CITY}");
                args.MenuTitle.SetTextVariable("CITY", townName);

                // Escreve o texto no menu de forma 100% segura
                var field = typeof(GameMenu).GetField(
                    "_defaultText",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                field?.SetValue(args.MenuContext.GameMenu, body);
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
                var settlement = Settlement.CurrentSettlement;
                var hero = Hero.MainHero;

                if (hero == null || settlement == null)
                {
                    Warn(L.S("loan_confirm_invalid_player_or_city", "Error: invalid player or town."));
                    return;
                }

                if (behavior == null)
                {
                    Warn(L.S("loan_confirm_behavior_missing", "Error: bank behavior not found."));
                    return;
                }

                string playerId = hero.StringId;
                string townId = settlement.StringId;

                float prosperity = settlement.Town?.Prosperity ?? 0f;
                float renown = hero.Clan?.Renown ?? 0f;

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

                // Segurança: limitar a 360 parcelas
                int parcelas = ClampInt(_sim.Installments, 1, 360);
                var sim = CalcLoanForecastParametric(prosperity, renown, parcelas);

                float totalDebt = GetPlayerTotalDebt(behavior, playerId);
                float availableCredit = MathF.Max(0f, sim.maxLoan - totalDebt);

                if (_sim.RequestedAmount > availableCredit)
                {
                    Warn(L.S("loan_confirm_exceeds_credit", "Requested amount exceeds your available credit."));
                    return;
                }

                // 🔹 Montar o resumo do contrato para exibição
                float jurosPct = sim.totalInterestPct;
                float multa = sim.lateFeePct;
                float valorTotal = _sim.RequestedAmount * (1f + jurosPct / 100f);
                float valorParcela = valorTotal / parcelas;

                var textObj = L.T("loan_confirm_popup_text",
                    "Loan Contract Preview — Bank of {CITY}\n\n" +
                    "• Amount requested: {AMOUNT}\n" +
                    "• Installments: {INSTALLMENTS}x\n" +
                    "• Interest rate: {INTEREST}\n" +
                    "• Daily late fee: {LATEFEE}\n" +
                    "• Total to repay: {TOTAL}\n" +
                    "• Installment value: {INSTALLMENT}\n\n" +
                    "Do you confirm this contract?");
                textObj.SetTextVariable("CITY", settlement.Name?.ToString() ?? "City");
                textObj.SetTextVariable("AMOUNT", BankUtils.FmtDenarsFull(_sim.RequestedAmount));
                textObj.SetTextVariable("INSTALLMENTS", parcelas);
                textObj.SetTextVariable("INTEREST", BankUtils.FmtPct(jurosPct / 100f));
                textObj.SetTextVariable("LATEFEE", BankUtils.FmtPct(multa / 100f));
                textObj.SetTextVariable("TOTAL", BankUtils.FmtDenarsFull(valorTotal));
                textObj.SetTextVariable("INSTALLMENT", BankUtils.FmtDenarsFull(valorParcela));

                // 🔹 Popup de confirmação
                InformationManager.ShowInquiry(
                    new InquiryData(
                        L.S("loan_confirm_popup_title", "Confirm Loan Contract"),
                        textObj.ToString(),
                        true,
                        true,
                        L.S("popup_confirm", "Confirm"),
                        L.S("popup_cancel", "Cancel"),
                        async () =>
                        {
                            try
                            {
                                // COPIA DADOS PARA VARIÁVEIS TEMPORÁRIAS
                                int amount = _sim.RequestedAmount;
                                int insts = parcelas;
                                float juros = sim.totalInterestPct;
                                float multaLate = sim.lateFeePct;

                                // 🔥 LIMPA A SIMULAÇÃO
                                ClearSimulation();

                                // 🔄 Força recarregar a UI zerada
                                BankSafeUI.Switch("bank_loan_request");

                                // Espera 80ms para garantir UI limpa
                                await System.Threading.Tasks.Task.Delay(80);

                                // Agora executa o fluxo normal
                                var storage = behavior.GetStorage();
                                if (storage == null)
                                {
                                    Warn(L.S("loan_confirm_storage_missing", "Error: bank storage not available."));
                                    return;
                                }

                                storage.CreateLoan(
                                    playerId,
                                    townId,
                                    amount,
                                    juros,
                                    multaLate,
                                    insts
                                );

                                hero.ChangeHeroGold(amount);

                                var okMsg = L.T("loan_confirm_ok",
                                    "Loan of {AMOUNT} successfully created.\nTotal interest rate: {INTEREST}.");
                                okMsg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(amount));
                                okMsg.SetTextVariable("INTEREST", BankUtils.FmtPct(juros / 100f));

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
                        },
                        () =>
                        {
                            ClearSimulation();
                            BankSafeUI.Switch("bank_loan_request");
                        }
                    ),
                    true
                );

            }
            catch (Exception e)
            {
                Warn(L.S("loan_confirm_error", "Error registering the loan: ") + e.Message);
            }
        }



        // ------------------------------------------------------------
        // Soma total de dívidas do jogador
        // ------------------------------------------------------------
        private static float GetPlayerTotalDebt(BankCampaignBehavior behavior, string playerId)
        {
            if (behavior == null)
                return 0f;

            var storage = behavior.GetStorage();
            if (storage == null)
                return 0f;

            var loans = storage.GetLoans(playerId ?? "player");
            if (loans == null || loans.Count == 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < loans.Count; i++)
                total += loans[i].Remaining;

            return total;
        }

        // ------------------------------------------------------------
        // Popups
        // ------------------------------------------------------------
        private static void PromptEditAmount()
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
                    if (!TryParsePositiveInt(input, out int val))
                    {
                        Warn(L.S("loan_popup_amount_invalid", "Invalid amount."));
                        return;
                    }

                    // Segurança: não deixar valor negativo ou bizarro
                    _sim.RequestedAmount = Math.Max(0, val);
                    BankSafeUI.Switch("bank_loan_request");

                },
                negativeAction: null
            ));
        }

        private static void PromptEditInstallments()
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
                    if (!int.TryParse((input ?? "").Trim(), out int n))
                    {
                        Warn(L.S("loan_popup_inst_invalid", "Invalid number."));
                        return;
                    }

                    _sim.Installments = ClampInt(n, 1, 360);
                    BankSafeUI.Switch("bank_loan_request");
                },
                negativeAction: null
            ));
        }
        // ------------------------------------------------------------
        // Novo algoritmo de empréstimo (versão sem curva de suavização)
        // ------------------------------------------------------------
        // ==========================================================
        // 🏦 BanksOfCalradia - Loan Forecast (v1.6.1 Curva Suave Calibrada)
        // Fix: usa Math.Exp (double) com cast para float no sigmoide
        // ==========================================================
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

            // Desconto suave por renome (sigmoide)
            // Usa Math.Exp(double) por compatibilidade e faz cast para float
            float x = MathF.Max(1f, renown);
            float expTerm = (float)System.Math.Exp((x - 600f) / 220f);
            float s = 1f / (1f + expTerm);               // decresce com renome
            float fatorDescontoRenome = 0.60f + 0.60f * s; // [0.60, 1.20]
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
        private static bool TryParsePositiveInt(string input, out int value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(input)
                   && int.TryParse(input.Trim(), out int parsed)
                   && parsed > 0
                   && (value = parsed) > 0;
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

        private static void Warn(string msg)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                msg,
                Color.FromUint(0xFFFF6666)
            ));
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
