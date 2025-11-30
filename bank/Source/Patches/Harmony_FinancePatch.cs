using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;

using BanksOfCalradia.Source.Systems.Processing;

namespace BanksOfCalradia.Source.Patches
{
    // ============================================================================
    //  PATCH 1 → Adiciona juros ao "Expected Gold Change (detailed)"
    // ============================================================================
    [HarmonyPatch(typeof(DefaultClanFinanceModel))]
    public static class Harmony_FinancePatch_Income
    {
        [HarmonyPostfix]
        [HarmonyPatch("CalculateClanIncome")]
        private static void Postfix(
            Clan clan,
            bool includeDescriptions,
            bool applyWithdrawals,
            bool includeDetails,
            ref ExplainedNumber __result)
        {
            try
            {
                // Usa seu FinanceProcessor original para calcular juros
                FinanceProcessor fp = new FinanceProcessor();
                fp.AddBankInterestToExplainedNumber(
                    clan,
                    ref __result,
                    includeDescriptions,
                    includeDetails,
                    applyWithdrawals
                );
            }
            catch
            {
                // silencioso
            }
        }
    }

    // ============================================================================
    //  PATCH 2 → Adiciona juros + parcelas (ALT) ao "Gold Change (summary)"
    // ============================================================================
    [HarmonyPatch(typeof(DefaultClanFinanceModel))]
    public static class Harmony_FinancePatch_GoldChange
    {
        [HarmonyPostfix]
        [HarmonyPatch("CalculateClanGoldChange")]
        private static void Postfix(
            Clan clan,
            bool includeDescriptions,
            bool applyWithdrawals,
            bool includeDetails,
            ref ExplainedNumber __result)
        {
            try
            {
                FinanceProcessor fp = new FinanceProcessor();

                // juros
                fp.AddBankInterestToExplainedNumber(
                    clan,
                    ref __result,
                    includeDescriptions,
                    includeDetails,
                    applyWithdrawals
                );

                // preview das parcelas de empréstimo
                fp.GetType()
                  .GetMethod("AddLoanPreviewVisual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  ?.Invoke(fp, new object[] { clan, __result, includeDescriptions, includeDetails });
            }
            catch
            {
                // silencioso
            }
        }
    }
}
