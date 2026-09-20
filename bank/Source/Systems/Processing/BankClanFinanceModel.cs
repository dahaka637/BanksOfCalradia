// ============================================
// BanksOfCalradia - BankClanFinanceModel.cs
// Author: Dahaka
// Description:
//   Overrides the native clan finance model instead of Harmony-patching it.
//   DefaultClanFinanceModel's methods are virtual and meant to be extended
//   via the model system (CampaignGameStarter.AddModel); reflectively
//   Harmony-patching them was causing a crash on Sandbox start (the patch
//   forced DefaultClanFinanceModel's static constructor to run at an
//   unexpected point, throwing a NullReferenceException).
// ============================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

namespace BanksOfCalradia.Source.Systems.Processing
{
    public class BankClanFinanceModel : DefaultClanFinanceModel
    {
        public override ExplainedNumber CalculateClanIncome(
            Clan clan,
            bool includeDescriptions = false,
            bool applyWithdrawals = false,
            bool includeDetails = false)
        {
            var result = base.CalculateClanIncome(clan, includeDescriptions, applyWithdrawals, includeDetails);

            try
            {
                new FinanceProcessor().AddBankInterestToExplainedNumber(
                    clan, ref result, includeDescriptions, includeDetails, applyWithdrawals);
            }
            catch
            {
                // silencioso
            }

            return result;
        }

        public override ExplainedNumber CalculateClanGoldChange(
            Clan clan,
            bool includeDescriptions = false,
            bool applyWithdrawals = false,
            bool includeDetails = false)
        {
            var result = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);

            try
            {
                var fp = new FinanceProcessor();

                fp.AddBankInterestToExplainedNumber(
                    clan, ref result, includeDescriptions, includeDetails, applyWithdrawals);

                fp.AddLoanPreviewVisual(clan, ref result, includeDescriptions, includeDetails);
            }
            catch
            {
                // silencioso
            }

            return result;
        }
    }
}
