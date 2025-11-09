// ============================================
// BanksOfCalradia - FinanceLedger.cs
// Author: Dahaka
// Version: 3.2.0 (Double Safe + Localization)
// Description:
//   Unified financial ledger for BanksOfCalradia.
//   - Full double-precision for all monetary fields
//   - Registers bank interests, deposits, withdrawals
//   - Safe hero gold application (overflow guarded)
//   - In-memory audit per player, save-compatible
// ============================================

using BanksOfCalradia.Source.Systems;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Core
{
    [Serializable]
    public class FinanceRecord
    {
        public string Id { get; set; }              // Unique GUID
        public string PlayerId { get; set; }        // Hero.StringId
        public string Source { get; set; }          // BANK, LOAN, SYSTEM...
        public string Description { get; set; }     // Localized description/fallback
        public double Amount { get; set; }          // Raw value (can be negative, now double)
        public string Currency { get; set; } = "Denar";
        public double Timestamp { get; set; }       // CampaignTime in days
        public bool Applied { get; set; }           // True when gold was actually modified
    }

    /// <summary>
    /// In-memory ledger for bank-related financial events.
    /// Safe for very large transactions (double precision).
    /// </summary>
    public class FinanceLedger
    {
        // Turn off to avoid log spam in release
        private const bool VERBOSE = false;

        private readonly Dictionary<string, List<FinanceRecord>> _records = new();

        // ============================================================
        // Create a new record (optionally apply immediately)
        // ============================================================
        public FinanceRecord Add(string playerId, string source, string description, double amount, bool applyNow = false)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Invalid playerId.");

            var record = new FinanceRecord
            {
                Id = Guid.NewGuid().ToString(),
                PlayerId = playerId,
                Source = string.IsNullOrWhiteSpace(source) ? "SYSTEM" : source,
                Description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
                Amount = amount,
                Timestamp = CampaignTime.Now.ToDays,
                Applied = false
            };

            if (!_records.TryGetValue(playerId, out var list))
            {
                list = new List<FinanceRecord>();
                _records[playerId] = list;
            }

            list.Add(record);

            if (VERBOSE)
            {
                var msg = L.T("ledger_new_entry",
                    "[BanksOfCalradia][Ledger] New entry: {SOURCE} | {DESC} | {AMOUNT}");
                msg.SetTextVariable("SOURCE", record.Source);
                msg.SetTextVariable("DESC", record.Description);
                msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(record.Amount));
                Log(msg.ToString());
            }

            if (applyNow)
                Apply(record);

            return record;
        }

        // ============================================================
        // Apply a record: modifies hero gold
        // ============================================================
        public void Apply(FinanceRecord record)
        {
            try
            {
                if (record == null || record.Applied)
                    return;

                var hero = Hero.MainHero;
                if (hero == null)
                {
                    Log(L.S("ledger_apply_fail_nohero",
                        "[BanksOfCalradia][Ledger] Cannot apply transaction: hero not available."));
                    return;
                }

                double safeAmount = record.Amount < -9.22e9 ? -9.22e9 :
                    record.Amount > 9.22e9 ? 9.22e9 :
                    record.Amount;

                int delta = (int)Math.Round(safeAmount, MidpointRounding.AwayFromZero);

                if (delta == 0)
                {
                    record.Applied = true;
                    return;
                }

                hero.ChangeHeroGold(delta);
                record.Applied = true;

                if (VERBOSE)
                {
                    var msg = L.T("ledger_apply_ok",
                        "[BanksOfCalradia][Ledger] Applied {AMOUNT} for: {DESC}");
                    msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(delta));
                    msg.SetTextVariable("DESC", record.Description ?? "-");
                    Log(msg.ToString());
                }
            }
            catch (Exception ex)
            {
                var msg = L.T("ledger_apply_error",
                    "[BanksOfCalradia][Ledger] Error while applying transaction: {ERROR}");
                msg.SetTextVariable("ERROR", ex.Message);
                Log(msg.ToString());
            }
        }

        // ============================================================
        // Inject bank interests into native clan finance view
        // (used as a preview / audit, not hard-applied)
        // ============================================================
        public void InjectToClanFinance(Clan clan)
        {
            try
            {
                if (clan == null || clan != Clan.PlayerClan)
                    return;

                var hero = Hero.MainHero;
                if (hero == null || Campaign.Current == null)
                    return;

                var behavior = Campaign.Current.GetCampaignBehavior<BankCampaignBehavior>();
                if (behavior == null)
                {
                    if (VERBOSE)
                        Log(L.S("ledger_no_behavior", "[BanksOfCalradia][Ledger] BankCampaignBehavior not found."));
                    return;
                }

                var storage = behavior.GetStorage();
                if (storage == null || !storage.SavingsByPlayer.TryGetValue(hero.StringId, out var accounts))
                    return;

                double totalInterest = 0d;

                foreach (var acc in accounts)
                {
                    if (acc.Amount <= 0.01)
                        continue;

                    var settlement = Campaign.Current.Settlements.Find(s => s.StringId == acc.TownId);
                    double prosperity = settlement?.Town?.Prosperity ?? 0d;

                    // Trainer-style APY
                    double annualRate = BankUtils.CalcSavingsAnnualRate((float)prosperity);
                    double dailyRate = annualRate / 365d;
                    double gain = acc.Amount * (dailyRate / 100d);

                    if (gain <= 0.01)
                        continue;

                    totalInterest += gain;

                    string townName = settlement != null ? settlement.Name.ToString() : acc.TownId;
                    var desc = L.T("ledger_interest_desc", "Daily interest ({TOWN})");
                    desc.SetTextVariable("TOWN", townName);

                    // Register in ledger (not applied)
                    Add(hero.StringId, "BANK", desc.ToString(), gain, applyNow: false);
                }

                if (totalInterest > 0.01)
                {
                    var label = L.T("ledger_interest_label", "Bank Interest");

                    var explained = Campaign.Current.Models.ClanFinanceModel
                        .CalculateClanIncome(clan, includeDescriptions: true, applyWithdrawals: false, includeDetails: false);

                    explained.Add((float)totalInterest, label);

                    if (VERBOSE)
                    {
                        var msg = L.T("ledger_interest_injected",
                            "[BanksOfCalradia][Ledger] Bank interest injected into Expected Gold Change: {AMOUNT}");
                        msg.SetTextVariable("AMOUNT", BankUtils.FmtDenars(totalInterest));
                        Log(msg.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = L.T("ledger_inject_error",
                    "[BanksOfCalradia][Ledger] Error injecting interest: {ERROR}");
                msg.SetTextVariable("ERROR", ex.Message);
                Log(msg.ToString());
            }
        }

        // ============================================================
        // Query
        // ============================================================
        public List<FinanceRecord> GetRecords(string playerId, string sourceFilter = null)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return new List<FinanceRecord>();

            if (!_records.TryGetValue(playerId, out var list) || list == null)
                return new List<FinanceRecord>();

            if (string.IsNullOrEmpty(sourceFilter))
                return new List<FinanceRecord>(list);

            return list.FindAll(r =>
                r.Source != null &&
                r.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase));
        }

        public double GetBalance(string playerId, string sourceFilter = null)
        {
            double total = 0d;
            foreach (var record in GetRecords(playerId, sourceFilter))
                total += record.Amount;
            return total;
        }

        // ============================================================
        // Remove / rollback
        // ============================================================
        public bool Remove(string playerId, string recordId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(recordId))
                return false;

            if (!_records.TryGetValue(playerId, out var list) || list == null)
                return false;

            var rec = list.Find(r => r.Id == recordId);
            if (rec == null)
                return false;

            list.Remove(rec);

            if (VERBOSE)
            {
                var msg = L.T("ledger_removed", "[BanksOfCalradia][Ledger] Removed entry {ID}");
                msg.SetTextVariable("ID", recordId);
                Log(msg.ToString());
            }

            return true;
        }

        // ============================================================
        // Logging
        // ============================================================
        private static void Log(string msg)
        {
            if (InformationManager.DisplayMessage == null)
                return;

            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFFAAEEAA)
            ));
        }
    }
}
