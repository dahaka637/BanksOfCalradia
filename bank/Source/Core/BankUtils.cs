using System;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Core
{
    /// <summary>
    /// Utilitários visuais e financeiros do sistema bancário.
    /// Mantém formatos compatíveis com os menus existentes.
    /// </summary>
    public static class BankUtils
    {
        // Golden color used across the mod
        public static readonly uint UiGold = 0xFFBBAA00;

        // ============================================================
        // Formatters
        // ============================================================

        /// <summary>
        /// Formata porcentagem aceitando tanto fração (0.02) quanto percentual (2.0).
        /// 0.02 → "2 %"
        /// 2.0  → "2 %"
        /// </summary>
        public static string FmtPct(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return "0 %";

            // Se for negativo, mostra 0 %
            if (v < 0f)
                v = 0f;

            // Se já parece percentual (>= 1), usa direto,
            // senão converte de fração para porcentagem.
            float displayValue = v >= 1f ? v : v * 100f;

            return displayValue.ToString("0.##", CultureInfo.InvariantCulture) + " %";
        }

        /// <summary>
        /// Formata em denares com separador de milhar e sufixo em português.
        /// Mantido por compatibilidade com o código já existente.
        /// </summary>
        public static string FmtDenars(float v)
        {
            var rounded = MathF.Round(v);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0}",
                rounded
            );
        }

        /// <summary>
        /// Versão localizada de FmtDenars. Usa a chave de idioma para o rótulo da moeda.
        /// Ex.: "1,250 denars" / "1.250 denares" / "1.250 dinares"
        /// </summary>
        public static string FmtDenarsL(float v)
        {
            var rounded = MathF.Round(v);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0}",
                rounded
            );
        }


        /// <summary>
        /// Apenas o número de denares, sem texto.
        /// Útil para logs internos ou cálculos.
        /// </summary>
        public static string FmtNumber(float v)
        {
            return MathF.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        }

        // ============================================================
        // Financial helpers
        // ============================================================

        /// <summary>
        /// APY da poupança baseado na prosperidade.
        /// </summary>
        public static float CalcSavingsAnnualRate(float prosperity)
        {
            const float maxAPY = 0.45f;
            const float maxPros = 6000f;

            float factor = MathF.Max(0f, MathF.Min(1f, prosperity / maxPros));
            return maxAPY * factor;
        }

        /// <summary>
        /// APR do empréstimo baseado em prosperidade e renome.
        /// Garante APR mínimo.
        /// </summary>
        public static float CalcLoanAnnualRate(float prosperity, float renown)
        {
            const float baseAPR = 0.50f;
            float pTerm = 1f - MathF.Min(prosperity / 12000f, 0.5f);
            float rTerm = 1f - MathF.Min(renown / 1000f, 0.5f);
            float apr = baseAPR * pTerm * rTerm;
            return MathF.Max(0.05f, apr);
        }

        /// <summary>
        /// Taxa diária de multa baseada na economia da cidade.
        /// </summary>
        public static float CalcLateFeeDailyRate(float prosperity)
        {
            const float baseFee = 0.03f;
            float mult = 0.5f + MathF.Min(prosperity / 12000f, 0.5f);
            return baseFee * mult;
        }

        /// <summary>
        /// Limite bruto de empréstimo baseado em prosperidade e renome.
        /// </summary>
        public static float CalcLoanLimit(float prosperity, float renown)
        {
            return prosperity * (renown / 10f) * 0.8f;
        }

        // ============================================================
        // Rate normalization helpers
        // ============================================================

        /// <summary>
        /// Normaliza taxa que pode vir em % (2.0) ou fração (0.02) para fração.
        /// 2.0 → 0.02 ; 0.02 → 0.02
        /// </summary>
        public static float ToFractionRate(float rate)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
                return 0f;

            return rate > 1f ? rate / 100f : rate;
        }

        /// <summary>
        /// Normaliza taxa que pode vir em fração (0.02) ou % (2.0) para percentual.
        /// 0.02 → 2.0 ; 2.0 → 2.0
        /// </summary>
        public static float ToPercentRate(float rate)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
                return 0f;

            return rate < 1f ? rate * 100f : rate;
        }
    }
}
