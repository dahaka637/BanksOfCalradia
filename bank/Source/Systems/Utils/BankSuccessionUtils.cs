// ============================================
// BanksOfCalradia - BankSuccessionUtils.cs
// Author: Dahaka
// Version: 1.3.0 (Production Clean)
// Description:
//   Detecta sucessões ou trocas de herói e garante
//   que todas as contas bancárias e empréstimos
//   pertençam ao jogador atual.
//
//   • Varre todos os owners registrados no banco
//   • Mescla saldos e empréstimos antigos no herói ativo
//   • Remove dados órfãos automaticamente
// ============================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Systems.Utils
{
    public static class BankSuccessionUtils
    {
        /// <summary>
        /// Verifica e corrige o ownership de todas as contas bancárias e empréstimos,
        /// garantindo que pertençam ao herói atual ou líder do clã.
        /// </summary>
        public static void CheckAndTransferOwnership(BankStorage storage)
        {
            if (storage == null)
                return;

            var currentHero = Hero.MainHero;
            var currentLeader = Clan.PlayerClan?.Leader;

            string currHeroId = currentHero?.StringId;
            string currLeaderId = currentLeader?.StringId;

            if (string.IsNullOrEmpty(currHeroId))
                return;

            // 🔹 Obtém todos os owners registrados (contas + empréstimos)
            var allOwners = storage.SavingsByPlayer.Keys
                .Union(storage.LoansByPlayer.Keys)
                .Distinct()
                .ToList();

            foreach (var ownerId in allOwners)
            {
                if (string.IsNullOrEmpty(ownerId))
                    continue;

                // Ignora o herói atual e o líder do clã
                if (ownerId == currHeroId || ownerId == currLeaderId)
                    continue;

                // Mescla dados antigos no herói atual
                MergeAccounts(storage, ownerId, currHeroId);
            }
        }

        /// <summary>
        /// Mescla as contas e empréstimos de um owner antigo no herói atual.
        /// </summary>
        private static void MergeAccounts(BankStorage storage, string oldId, string newId)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || oldId == newId)
                return;

            // ---------- Contas Poupança ----------
            if (storage.SavingsByPlayer.TryGetValue(oldId, out var oldSavings) && oldSavings.Count > 0)
            {
                if (!storage.SavingsByPlayer.ContainsKey(newId))
                    storage.SavingsByPlayer[newId] = new System.Collections.Generic.List<BankSavingsData>();

                foreach (var acc in oldSavings)
                {
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

            // ---------- Empréstimos ----------
            if (storage.LoansByPlayer.TryGetValue(oldId, out var oldLoans) && oldLoans.Count > 0)
            {
                if (!storage.LoansByPlayer.ContainsKey(newId))
                    storage.LoansByPlayer[newId] = new System.Collections.Generic.List<BankLoanData>();

                var existingIds = storage.LoansByPlayer[newId]
                    .Select(l => l.LoanId)
                    .ToHashSet();

                foreach (var loan in oldLoans)
                {
                    if (loan == null || existingIds.Contains(loan.LoanId))
                        continue;

                    storage.LoansByPlayer[newId].Add(loan);
                }

                storage.LoansByPlayer.Remove(oldId);
            }

            // Informação desativada em produção
            // InformationManager.DisplayMessage(new InformationMessage(
            //     $"[BanksOfCalradia] Bank accounts and loans merged into your new character.",
            //     Color.FromUint(0xFFD4AF37)
            // ));

        }
    }
}
