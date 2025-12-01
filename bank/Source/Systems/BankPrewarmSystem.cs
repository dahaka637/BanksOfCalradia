using System;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using BanksOfCalradia.Source.Systems;
using BanksOfCalradia.Source.Systems.Data;

namespace BanksOfCalradia.Source.Core
{
    /// <summary>
    /// Sistema responsável por:
    ///  • Criar TODAS as contas bancárias de poupança em TODAS as cidades.
    ///  • Normalizar dados antigos/corrompidos.
    ///  • Blindar o storage antes que qualquer menu de banco seja aberto.
    ///
    /// Roda apenas uma vez por save. Totalmente seguro e sem exceções.
    /// </summary>
    public static class BankPrewarmSystem
    {
        private static bool _sessionStarted;

        public static async Task RunPrewarmAsync(BankStorage storage, string playerId)
        {
            if (storage == null)
                return;

            if (string.IsNullOrWhiteSpace(playerId))
                playerId = "player";

            // Reset da sessão quando necessário
            if (!storage.Initialized)
                _sessionStarted = false;

            // Já inicializado neste save
            if (storage.Initialized)
                return;

            // Já rodou nesta sessão
            if (_sessionStarted)
                return;

            try
            {
                await WaitForCampaignReady();

                await PrewarmSavingsForAllTowns(storage, playerId);

                SafeNormalizeAll(storage, playerId);

                storage.Initialized = true;
                _sessionStarted = true;
            }
            catch
            {
                // Mesmo em erro, não impede funcionamento básico
                storage.Initialized = true;
                _sessionStarted = true;
            }
        }

        // ============================================================
        // Espera mundo carregar
        // ============================================================

        private static async Task WaitForCampaignReady()
        {
            int attempts = 0;

            while (attempts < 50)
            {
                attempts++;

                try
                {
                    if (Campaign.Current != null &&
                        Hero.MainHero != null &&
                        Campaign.Current.Settlements != null &&
                        Campaign.Current.Settlements.Count > 0)
                    {
                        return;
                    }
                }
                catch { }

                await Task.Delay(150);
            }
        }

        // ============================================================
        // Criação de contas
        // ============================================================

        private static async Task PrewarmSavingsForAllTowns(BankStorage storage, string playerId)
        {
            var settlements = Campaign.Current?.Settlements;
            if (settlements == null)
                return;

            foreach (var st in settlements)
            {
                try
                {
                    if (st?.Town != null)
                        storage.GetOrCreateSavings(playerId, st.StringId);
                }
                catch
                {
                }

                await Task.Delay(5);
            }
        }

        // ============================================================
        // Normalização
        // ============================================================

        private static void SafeNormalizeAll(BankStorage storage, string playerId)
        {
            try
            {
                if (storage.SavingsByPlayer.TryGetValue(playerId, out var list) && list != null)
                {
                    foreach (var s in list)
                        NormalizeSavings(s);
                }
            }
            catch
            {
            }
        }

        private static void NormalizeSavings(BankSavingsData s)
        {
            if (s == null) return;

            if (string.IsNullOrWhiteSpace(s.PlayerId))
                s.PlayerId = "player";

            if (string.IsNullOrWhiteSpace(s.TownId))
                s.TownId = "town";

            if (double.IsNaN(s.Amount) || double.IsInfinity(s.Amount))
                s.Amount = 0d;

            if (double.IsNaN(s.PendingInterest) || double.IsInfinity(s.PendingInterest))
                s.PendingInterest = 0d;
        }

        // ============================================================
        // Reset manual (usado em casos extremos)
        // ============================================================

        public static void ResetSession()
        {
            _sessionStarted = false;
        }
    }
}
