// ============================================
// BanksOfCalradia - BankStorage.cs
// Author: Dahaka
// Version: 3.0.0 (Ultra Safe Storage + Integrity Guard + Self-Heal)
// Description:
//   Core persistence layer for the banking system.
//   Serialized as JSON inside the game save.
//
//   Safety goals:
//   • Never throw in production (crash-proof)
//   • Self-heal null dictionaries/lists and invalid entries
//   • Normalize numeric fields (NaN/Inf/negative)
//   • Deduplicate "one savings per town"
//   • Validate loan invariants (ids/town/player/amounts/rates/dates)
//   • Stable under partial/old JSON loads (MissingMemberHandling.Ignore)
//
// Notes:
//   - This class MUST remain "pure storage": no UI/menu calls except optional debug summary.
//   - It should never depend on Settlement.CurrentSettlement or Screen state for core logic.
// ============================================

using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
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

        public bool Initialized { get; set; } = false;
        public BankStorage()
        {
            EnsureInitialized();
        }

        // ============================================================
        // Core integrity / self-heal
        // ============================================================
        public void EnsureInitialized()
        {
            try
            {
                SavingsByPlayer ??= new Dictionary<string, List<BankSavingsData>>();
                LoansByPlayer ??= new Dictionary<string, List<BankLoanData>>();
            }
            catch
            {
                // last resort fallback
                SavingsByPlayer = new Dictionary<string, List<BankSavingsData>>();
                LoansByPlayer = new Dictionary<string, List<BankLoanData>>();
            }
        }

        /// <summary>
        /// Runs a full integrity pass. Safe to call anytime.
        /// Returns true if storage looks healthy after the pass.
        /// </summary>
        public bool ValidateAndRepair(out string reason)
        {
            reason = null;

            try
            {
                EnsureInitialized();

                // Repair savings
                try
                {
                    RepairSavings();
                }
                catch (Exception ex)
                {
                    reason = "Savings repair failed: " + ex.Message;
                    // Continue to loans, but signal not healthy.
                    // We still try to repair the rest to reduce cascades.
                }

                // Repair loans
                try
                {
                    RepairLoans();
                }
                catch (Exception ex)
                {
                    reason = (reason == null ? "" : reason + " | ") + "Loans repair failed: " + ex.Message;
                }

                // If we got here, storage is at least usable.
                return true;
            }
            catch (Exception ex)
            {
                reason = "Storage validation failed: " + ex.Message;
                return false;
            }
        }

        private void RepairSavings()
        {
            if (SavingsByPlayer == null)
            {
                SavingsByPlayer = new Dictionary<string, List<BankSavingsData>>();
                return;
            }

            // Remove null/empty keys
            var badKeys = new List<string>();
            foreach (var kvp in SavingsByPlayer)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    badKeys.Add(kvp.Key);
            }
            for (int i = 0; i < badKeys.Count; i++)
                SavingsByPlayer.Remove(badKeys[i]);

            // For each player list: ensure non-null, remove null entries, normalize,
            // deduplicate by TownId (one account per town).
            var players = new List<string>(SavingsByPlayer.Keys);
            for (int p = 0; p < players.Count; p++)
            {
                string playerId = players[p];
                if (!SavingsByPlayer.TryGetValue(playerId, out var list) || list == null)
                {
                    SavingsByPlayer[playerId] = new List<BankSavingsData>();
                    continue;
                }

                // Remove null entries
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null)
                        list.RemoveAt(i);
                }

                // Normalize + cache last seen per town
                var perTown = new Dictionary<string, BankSavingsData>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s == null)
                        continue;

                    s.PlayerId = string.IsNullOrEmpty(s.PlayerId) ? playerId : s.PlayerId;
                    s.TownId = string.IsNullOrEmpty(s.TownId) ? "town" : s.TownId;

                    // normalize amount
                    if (double.IsNaN(s.Amount) || double.IsInfinity(s.Amount) || s.Amount < 0d)
                        s.Amount = 0d;

                    // AutoReinvest default (safe)
                    // (if field does not exist in old versions, JSON ignore keeps default false)
                    // no-op

                    // Deduplicate: keep the entry with the highest amount (safer than picking first)
                    if (perTown.TryGetValue(s.TownId, out var existing) && existing != null)
                    {
                        if (s.Amount > existing.Amount)
                            perTown[s.TownId] = s;
                    }
                    else
                    {
                        perTown[s.TownId] = s;
                    }
                }

                // Rebuild list from perTown
                list.Clear();
                foreach (var kv in perTown)
                {
                    if (kv.Value != null)
                        list.Add(kv.Value);
                }
            }
        }

        private void RepairLoans()
        {
            if (LoansByPlayer == null)
            {
                LoansByPlayer = new Dictionary<string, List<BankLoanData>>();
                return;
            }

            // Remove null/empty keys
            var badKeys = new List<string>();
            foreach (var kvp in LoansByPlayer)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    badKeys.Add(kvp.Key);
            }
            for (int i = 0; i < badKeys.Count; i++)
                LoansByPlayer.Remove(badKeys[i]);

            var players = new List<string>(LoansByPlayer.Keys);
            for (int p = 0; p < players.Count; p++)
            {
                string playerId = players[p];
                if (!LoansByPlayer.TryGetValue(playerId, out var list) || list == null)
                {
                    LoansByPlayer[playerId] = new List<BankLoanData>();
                    continue;
                }

                // Remove null entries
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null)
                        list.RemoveAt(i);
                }

                // Normalize + deduplicate by LoanId
                var byId = new Dictionary<string, BankLoanData>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    var l = list[i];
                    if (l == null)
                        continue;

                    // IDs
                    if (string.IsNullOrWhiteSpace(l.LoanId))
                        l.LoanId = Guid.NewGuid().ToString();

                    // Player/town IDs
                    l.PlayerId = string.IsNullOrWhiteSpace(l.PlayerId) ? playerId : l.PlayerId;
                    l.TownId = string.IsNullOrWhiteSpace(l.TownId) ? "town" : l.TownId;

                    // Numeric normalization
                    l.OriginalAmount = SafeInt((int)l.OriginalAmount, 1, int.MaxValue);

                    l.Remaining = SafeFloat(l.Remaining, 0f, float.MaxValue);

                    l.InterestRate = SafeFloat(l.InterestRate, 0f, 10_000f);
                    l.LateFeeRate = SafeFloat(l.LateFeeRate, 0f, 10_000f);

                    // duration must be >=1
                    l.DurationDays = SafeInt(l.DurationDays, 1, 10_000);

                    // created at
                    l.CreatedAt = SafeFloat(l.CreatedAt, 0f, float.MaxValue);

                    // Remaining cannot exceed hard cap (10x contracted)
                    float contracted = l.OriginalAmount * (1f + (l.InterestRate / 100f));
                    float cap = contracted * 10f;
                    if (l.Remaining > cap)
                        l.Remaining = cap;

                    // If remaining is tiny, collapse to zero
                    if (l.Remaining < 0.0001f)
                        l.Remaining = 0f;

                    // Deduplicate: keep higher Remaining (safer to not lose debt)
                    if (byId.TryGetValue(l.LoanId, out var existing) && existing != null)
                    {
                        if (l.Remaining > existing.Remaining)
                            byId[l.LoanId] = l;
                    }
                    else
                    {
                        byId[l.LoanId] = l;
                    }
                }

                list.Clear();
                foreach (var kv in byId)
                {
                    if (kv.Value != null)
                        list.Add(kv.Value);
                }
            }
        }

        private static int SafeInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static float SafeFloat(float v, float min, float max)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // ============================================================
        // Savings
        // ============================================================
        public BankSavingsData GetOrCreateSavings(string playerId, string townId)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(playerId))
                playerId = "player";

            if (string.IsNullOrEmpty(townId))
                townId = "town";

            if (!SavingsByPlayer.TryGetValue(playerId, out var list) || list == null)
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
                    Amount = 0d
                };
                list.Add(entry);
            }
            else
            {
                // Normalize existing
                entry.PlayerId = string.IsNullOrEmpty(entry.PlayerId) ? playerId : entry.PlayerId;
                entry.TownId = string.IsNullOrEmpty(entry.TownId) ? townId : entry.TownId;

                if (double.IsNaN(entry.Amount) || double.IsInfinity(entry.Amount) || entry.Amount < 0d)
                    entry.Amount = 0d;
            }

            return entry;
        }

        // ============================================================
        // Loans
        // ============================================================
        public List<BankLoanData> GetLoans(string playerId)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(playerId))
                playerId = "player";

            if (!LoansByPlayer.TryGetValue(playerId, out var list) || list == null)
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
            EnsureInitialized();

            // Input sanity
            if (string.IsNullOrEmpty(playerId))
                playerId = "player";
            if (string.IsNullOrEmpty(townId))
                townId = "town";

            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
                amount = 0f;

            if (float.IsNaN(interestRate) || float.IsInfinity(interestRate))
                interestRate = 0f;

            if (float.IsNaN(lateFeeRate) || float.IsInfinity(lateFeeRate))
                lateFeeRate = 0f;

            if (durationDays < 1)
                durationDays = 1;

            // Valores arredondados
            int originalInt = MathF.Round(amount);
            if (originalInt < 1) originalInt = 1;

            float factor = 1f + (interestRate / 100f);
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f)
                factor = 1f;

            int totalInt = MathF.Round(originalInt * factor);
            if (totalInt < originalInt) totalInt = originalInt;

            float nowDays = 0f;
            try
            {
                if (Campaign.Current != null)
                    nowDays = (float)CampaignTime.Now.ToDays;
            }
            catch
            {
                nowDays = 0f;
            }

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
                CreatedAt = nowDays
            };

            // Final sanity on loan invariants
            NormalizeLoan(loan);

            var loans = GetLoans(playerId);
            loans.Add(loan);

            return loan;
        }

        public void RemoveLoan(string playerId, string loanId)
        {
            EnsureInitialized();

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

        private static void NormalizeLoan(BankLoanData loan)
        {
            if (loan == null)
                return;

            if (string.IsNullOrWhiteSpace(loan.LoanId))
                loan.LoanId = Guid.NewGuid().ToString();

            if (string.IsNullOrWhiteSpace(loan.PlayerId))
                loan.PlayerId = "player";

            if (string.IsNullOrWhiteSpace(loan.TownId))
                loan.TownId = "town";

            loan.OriginalAmount = SafeInt((int)loan.OriginalAmount, 1, int.MaxValue);

            loan.Remaining = SafeFloat(loan.Remaining, 0f, float.MaxValue);

            if (loan.Remaining < 0.0001f)
                loan.Remaining = 0f;

            loan.InterestRate = SafeFloat(loan.InterestRate, 0f, 10_000f);
            loan.LateFeeRate = SafeFloat(loan.LateFeeRate, 0f, 10_000f);

            loan.DurationDays = SafeInt(loan.DurationDays, 1, 10_000);
            loan.CreatedAt = SafeFloat(loan.CreatedAt, 0f, float.MaxValue);

            // cap: 10x contracted
            float contracted = loan.OriginalAmount * (1f + (loan.InterestRate / 100f));
            float cap = contracted * 10f;
            if (loan.Remaining > cap)
                loan.Remaining = cap;
        }

        // ============================================================
        // Legacy daily updates (kept for compatibility)
        // ============================================================
        public void ApplyDailyLoanUpdates()
        {
            // Blindagem: evita acesso a CampaignTime/Current fora da campanha
            if (Campaign.Current == null)
                return;

            EnsureInitialized();

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

                    // normalize loan before applying math
                    NormalizeLoan(loan);

                    float nowDays;
                    try
                    {
                        nowDays = (float)CampaignTime.Now.ToDays;
                    }
                    catch
                    {
                        // if time not available, skip
                        continue;
                    }

                    float daysElapsed = nowDays - loan.CreatedAt;
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

                    if (loan.Remaining < 0.0001f)
                        loan.Remaining = 0f;
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
                EnsureInitialized();

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
