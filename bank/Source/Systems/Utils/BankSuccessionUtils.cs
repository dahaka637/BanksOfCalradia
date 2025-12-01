// ============================================
// BanksOfCalradia - BankSuccessionUtils.cs
// Author: Dahaka
// Version: 3.0.0 (Health-Aware + Prewarm-Safe)
// Description:
//   Handles ownership transfer of savings and loans
//   when the main hero changes (death / clan succession).
//
//   Now compatible with:
//     • HealthGate (no transfer during corrupted storage)
//     • Prewarm System (accounts pre-created for all towns)
//     • Normalization (duplicate prevention + data fix)
//     • New storage structure (double savings, float loans)
//
//   Safety guarantees:
//     • Never runs during warmup or broken state
//     • Never corrupts storage
//     • Always fixes duplicates per TownId
//     • PlayerId updated correctly
// ============================================

using System.Linq;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Systems.Utils
{
    public static class BankSuccessionUtils
    {
        // ============================================================
        // Public entry point — Safe wrapper
        // ============================================================
        public static void CheckAndTransferOwnership(BankStorage storage)
        {
            if (storage == null)
                return;

            // ⚠️ BLOCK if storage is not safe or warmup is not done
            try
            {
                var behavior = Campaign.Current?.GetCampaignBehavior<BankCampaignBehavior>();
                if (behavior != null)
                {
                    var (uiReady, health, _, _) = behavior.GetHealthSnapshot();

                    // Only allow succession on HEALTHY + UI READY
                    if (!uiReady || health != "Healthy")
                        return;
                }
            }
            catch
            {
                // If anything fails, do NOT run succession
                return;
            }

            var currentHero = Hero.MainHero;
            var currentLeader = Clan.PlayerClan?.Leader;

            string currHeroId = currentHero?.StringId;
            string currLeaderId = currentLeader?.StringId;

            if (string.IsNullOrEmpty(currHeroId))
                return;

            // 🔹 Owners = union of keys in savings and loans
            var allOwners = storage.SavingsByPlayer.Keys
                .Union(storage.LoansByPlayer.Keys)
                .Distinct()
                .ToList();

            foreach (var ownerId in allOwners)
            {
                if (string.IsNullOrEmpty(ownerId))
                    continue;

                // Don't transfer from new hero or clan leader
                if (ownerId == currHeroId || ownerId == currLeaderId)
                    continue;

                MergeAccounts(storage, ownerId, currHeroId);
            }

            // 🔥 Normalization: ensure no duplicates, invalid entries, etc.
            NormalizeAll(storage);
        }


        // ============================================================
        // Merge old owner's accounts into the new hero
        // ============================================================
        private static void MergeAccounts(BankStorage storage, string oldId, string newId)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || oldId == newId)
                return;

            // --------------------------
            // Savings transfer
            // --------------------------
            if (storage.SavingsByPlayer.TryGetValue(oldId, out var oldSavings) && oldSavings.Count > 0)
            {
                if (!storage.SavingsByPlayer.ContainsKey(newId))
                    storage.SavingsByPlayer[newId] = new List<BankSavingsData>();

                foreach (var acc in oldSavings)
                {
                    if (acc == null)
                        continue;

                    // Find matching TownId
                    var existing = storage.SavingsByPlayer[newId].Find(a => a.TownId == acc.TownId);

                    if (existing == null)
                    {
                        storage.SavingsByPlayer[newId].Add(new BankSavingsData
                        {
                            PlayerId = newId,
                            TownId = acc.TownId,
                            Amount = acc.Amount,
                            PendingInterest = acc.PendingInterest
                        });
                    }
                    else
                    {
                        existing.Amount += acc.Amount;
                        existing.PendingInterest += acc.PendingInterest;
                    }
                }

                storage.SavingsByPlayer.Remove(oldId);
            }

            // --------------------------
            // Loans transfer
            // --------------------------
            if (storage.LoansByPlayer.TryGetValue(oldId, out var oldLoans) && oldLoans.Count > 0)
            {
                if (!storage.LoansByPlayer.ContainsKey(newId))
                    storage.LoansByPlayer[newId] = new List<BankLoanData>();

                var existingIds = storage.LoansByPlayer[newId]
                    .Select(l => l.LoanId)
                    .ToHashSet();

                foreach (var loan in oldLoans)
                {
                    if (loan == null || existingIds.Contains(loan.LoanId))
                        continue;

                    loan.PlayerId = newId;  // important fix
                    storage.LoansByPlayer[newId].Add(loan);
                }

                storage.LoansByPlayer.Remove(oldId);
            }
        }


        // ============================================================
        // Post-merge normalization (critical)
        // ============================================================
        private static void NormalizeAll(BankStorage storage)
        {
            if (storage == null)
                return;

            NormalizeSavingsTable(storage);
            NormalizeLoansTable(storage);
        }


        // ============================================================
        // Savings: remove duplicates, clean nulls, clamp negative values
        // ============================================================
        private static void NormalizeSavingsTable(BankStorage storage)
        {
            var newTable = new Dictionary<string, List<BankSavingsData>>();

            foreach (var kvp in storage.SavingsByPlayer)
            {
                string playerId = string.IsNullOrWhiteSpace(kvp.Key) ? "player" : kvp.Key;
                var list = kvp.Value ?? new List<BankSavingsData>();

                // Remove null entries
                list = list.Where(s => s != null).ToList();

                // Merge duplicates by TownId
                var compact = list
                    .GroupBy(s => s.TownId ?? "town")
                    .Select(g =>
                    {
                        var first = g.First();
                        first.PlayerId = playerId;
                        first.TownId = g.Key;

                        // Sum amounts & interests properly
                        first.Amount = g.Sum(x => x.Amount < 0 ? 0 : x.Amount);
                        first.PendingInterest = g.Sum(x => x.PendingInterest < 0 ? 0 : x.PendingInterest);

                        return first;
                    })
                    .ToList();

                newTable[playerId] = compact;
            }

            storage.SavingsByPlayer = newTable;
        }


        // ============================================================
        // Loans: normalize persistency, remove nulls, clamp invalids
        // ============================================================
        private static void NormalizeLoansTable(BankStorage storage)
        {
            var newTable = new Dictionary<string, List<BankLoanData>>();

            foreach (var kvp in storage.LoansByPlayer)
            {
                string playerId = string.IsNullOrWhiteSpace(kvp.Key) ? "player" : kvp.Key;

                var list = kvp.Value ?? new List<BankLoanData>();
                list = list.Where(l => l != null).ToList();

                // Deduplicate by LoanId
                var compact = list
                    .GroupBy(l => l.LoanId ?? "NULL")
                    .Select(g =>
                    {
                        var loan = g.First();

                        if (loan.OriginalAmount < 0) loan.OriginalAmount = 0;
                        if (loan.Remaining < 0) loan.Remaining = 0;
                        if (loan.DurationDays < 1) loan.DurationDays = 1;
                        if (loan.InterestRate < 0) loan.InterestRate = 0;
                        if (loan.LateFeeRate < 0) loan.LateFeeRate = 0;

                        loan.PlayerId = playerId;
                        loan.LoanId = loan.LoanId ?? System.Guid.NewGuid().ToString();

                        // Cap remaining to 10x contracted
                        float contracted = loan.OriginalAmount * (1f + loan.InterestRate / 100f);
                        float cap = contracted * 10f;
                        if (loan.Remaining > cap)
                            loan.Remaining = cap;

                        return loan;
                    })
                    .ToList();

                newTable[playerId] = compact;
            }

            storage.LoansByPlayer = newTable;
        }
    }
}
