using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using BanksOfCalradia.Source.Core;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Systems
{
    /// <summary>
    /// Core de persistência do sistema bancário.
    /// Serializado em JSON dentro do save do jogo.
    /// </summary>
    public class BankStorage
    {
        // Savings: PlayerId → list of accounts (one per town)
        public Dictionary<string, List<BankSavingsData>> SavingsByPlayer { get; set; }
            = new Dictionary<string, List<BankSavingsData>>();

        // Loans: PlayerId → list of contracts
        public Dictionary<string, List<BankLoanData>> LoansByPlayer { get; set; }
            = new Dictionary<string, List<BankLoanData>>();

        public BankStorage() { }

        // ============================================================
        // Savings
        // ============================================================
        public BankSavingsData GetOrCreateSavings(string playerId, string townId)
        {
            if (string.IsNullOrEmpty(playerId))
                playerId = "player";

            if (string.IsNullOrEmpty(townId))
                townId = "town";

            if (!SavingsByPlayer.TryGetValue(playerId, out var list))
            {
                list = new List<BankSavingsData>();
                SavingsByPlayer[playerId] = list;
            }

            // Procura existente por TownId (uma conta por cidade)
            BankSavingsData entry = null;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && s.TownId == townId)
                {
                    entry = s;
                    break;
                }
            }

            if (entry == null)
            {
                entry = new BankSavingsData
                {
                    PlayerId = playerId,
                    TownId = townId,
                    Amount = 0f
                };
                list.Add(entry);
            }

            return entry;
        }

        // ============================================================
        // Loans
        // ============================================================
        public List<BankLoanData> GetLoans(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                playerId = "player";

            if (!LoansByPlayer.TryGetValue(playerId, out var list))
            {
                list = new List<BankLoanData>();
                LoansByPlayer[playerId] = list;
            }

            return list;
        }

        public BankLoanData CreateLoan(
            string playerId,
            string townId,
            float amount,
            float interestRate,
            float lateFeeRate,
            int durationDays)
        {
            // Input sanity
            if (string.IsNullOrEmpty(playerId))
                playerId = "player";
            if (string.IsNullOrEmpty(townId))
                townId = "town";

            if (amount < 0f) amount = 0f;
            if (float.IsNaN(amount) || float.IsInfinity(amount)) amount = 0f;

            if (float.IsNaN(interestRate) || float.IsInfinity(interestRate)) interestRate = 0f;
            if (float.IsNaN(lateFeeRate) || float.IsInfinity(lateFeeRate)) lateFeeRate = 0f;

            if (durationDays < 1) durationDays = 1;

            // Valores arredondados
            int originalInt = MathF.Round(amount);
            if (originalInt < 1) originalInt = 1;

            float factor = 1f + (interestRate / 100f);
            if (factor < 0f) factor = 0f;

            int totalInt = MathF.Round(originalInt * factor);
            if (totalInt < originalInt) totalInt = originalInt;

            var loan = new BankLoanData
            {
                LoanId = Guid.NewGuid().ToString(),
                PlayerId = playerId,
                TownId = townId,

                // armazenados como float, mas sempre inteiros
                OriginalAmount = originalInt,
                Remaining = totalInt,

                // taxas normalizadas e arredondadas
                InterestRate = (float)Math.Round(interestRate, 2, MidpointRounding.AwayFromZero),
                LateFeeRate = (float)Math.Round(lateFeeRate, 2, MidpointRounding.AwayFromZero),

                DurationDays = durationDays,
                CreatedAt = (float)CampaignTime.Now.ToDays
            };

            var loans = GetLoans(playerId);
            loans.Add(loan);
            return loan;
        }

        public void RemoveLoan(string playerId, string loanId)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(loanId))
                return;

            if (!LoansByPlayer.TryGetValue(playerId, out var list) || list == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && item.LoanId == loanId)
                {
                    list.RemoveAt(i);
                    break;
                }
            }
        }

        // ============================================================
        // Legacy daily updates (kept for compatibility)
        // ============================================================
        public void ApplyDailyLoanUpdates()
        {
            // Blindagem: evita acesso a CampaignTime/Current fora da campanha
            if (Campaign.Current == null)
                return;

            if (LoansByPlayer == null || LoansByPlayer.Count == 0)
                return;

            foreach (var kvp in LoansByPlayer)
            {
                var loans = kvp.Value;
                if (loans == null || loans.Count == 0)
                    continue;

                foreach (var loan in loans)
                {
                    // Skip inválidos ou já pagos
                    if (loan == null || loan.Remaining <= 0.01f)
                        continue;

                    float daysElapsed = (float)CampaignTime.Now.ToDays - loan.CreatedAt;
                    if (daysElapsed > loan.DurationDays)
                    {
                        // Normaliza multa: aceita 0.02 ou 2.0 → sempre fração
                        float lateRate = loan.LateFeeRate;
                        if (lateRate > 1f) lateRate = lateRate / 100f;
                        if (lateRate < 0f) lateRate = 0f;

                        if (lateRate > 0f)
                        {
                            float penalty = loan.Remaining * lateRate;
                            int penaltyInt = MathF.Ceiling(penalty);
                            if (penaltyInt > 0)
                                loan.Remaining += penaltyInt;
                        }

                        // Teto rígido: 10x do contratado com juros
                        float contracted = loan.OriginalAmount * (1f + loan.InterestRate / 100f);
                        float cap = contracted * 10f;
                        if (loan.Remaining > cap)
                            loan.Remaining = cap;
                    }
                }
            }
        }

        // ============================================================
        // Debug / Diagnostics
        // ============================================================
        public void PrintDebugSummary()
        {
            try
            {
                int totalSavingsEntries = 0;
                foreach (var list in SavingsByPlayer.Values)
                    totalSavingsEntries += list?.Count ?? 0;

                int totalLoans = 0;
                foreach (var list in LoansByPlayer.Values)
                    totalLoans += list?.Count ?? 0;

                // Header
                var header = L.T("storage_debug_header",
                    "[BanksOfCalradia] Storage summary: Players: {PLAYERS}, Savings: {SAVINGS}, Loans: {LOANS}");
                header.SetTextVariable("PLAYERS", SavingsByPlayer.Count);
                header.SetTextVariable("SAVINGS", totalSavingsEntries);
                header.SetTextVariable("LOANS", totalLoans);

                InformationManager.DisplayMessage(new InformationMessage(
                    header.ToString(),
                    Color.FromUint(BankUtils.UiGold)
                ));

                // Linhas detalhadas por player/cidade
                foreach (var kvp in SavingsByPlayer)
                {
                    string player = kvp.Key;
                    var savingsList = kvp.Value;
                    if (savingsList == null) continue;

                    foreach (var s in savingsList)
                    {
                        if (s == null) continue;

                        string townName = s.TownId;

                        // Resolve nome da cidade (apenas se Campaign estiver pronta)
                        var sett = FindSettlementSafe(s.TownId);
                        if (sett != null)
                            townName = sett.Name?.ToString() ?? s.TownId;

                        var line = L.T("storage_debug_savings_line",
                            "  • {PLAYER} → {TOWN} = {AMOUNT}");
                        line.SetTextVariable("PLAYER", player);
                        line.SetTextVariable("TOWN", townName);
                        line.SetTextVariable("AMOUNT", BankUtils.FmtDenars(s.Amount));

                        InformationManager.DisplayMessage(new InformationMessage(
                            line.ToString(),
                            Color.FromUint(0xFFDDCC55)
                        ));
                    }
                }
            }
            catch
            {
                // Silencioso: debug não deve causar crash jamais
            }
        }

        private static Settlement FindSettlementSafe(string townId)
        {
            if (string.IsNullOrEmpty(townId))
                return null;

            var camp = Campaign.Current;
            if (camp?.Settlements == null)
                return null;

            foreach (var st in camp.Settlements)
            {
                if (st != null && st.StringId == townId)
                    return st;
            }

            return null;
        }
    }
}
