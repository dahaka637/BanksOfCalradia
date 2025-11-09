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
        public static string FmtPct(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return "0 %";

            if (v < 0d)
                v = 0d;

            double displayValue = v >= 1d ? v : v * 100d;
            return displayValue.ToString("0.##", CultureInfo.InvariantCulture) + " %";
        }

        /// <summary>
        /// Formata grandes valores em denares com abreviação automática:
        /// K = mil, M = milhão, B = bilhão, T = trilhão, Q = quatrilhão.
        /// </summary>
        // =========================================================
        // Exibe valores abreviados sem centavos (K / M / B / T)
        // =========================================================
        // =========================================================
        // Exibe valores abreviados (K / M / B / T)
        // - Sem exibir centavos para valores inteiros
        // - Mostra até 2 casas apenas nas abreviações
        // =========================================================
        public static string FmtDenars(double value)
        {
            try
            {
                double abs = Math.Abs(value);
                string suffix;
                double shortVal;

                if (abs >= 1_000_000_000_000)
                {
                    shortVal = value / 1_000_000_000_000d;
                    suffix = "T";
                }
                else if (abs >= 1_000_000_000)
                {
                    shortVal = value / 1_000_000_000d;
                    suffix = "B";
                }
                else if (abs >= 1_000_000)
                {
                    shortVal = value / 1_000_000d;
                    suffix = "M";
                }
                else if (abs >= 1_000)
                {
                    shortVal = value / 1_000d;
                    suffix = "K";
                }
                else
                {
                    shortVal = value;
                    suffix = "";
                }

                string icon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";

                // 🔹 Inteiros: sem casas decimais
                // 🔹 Abreviados: até 2 casas significativas
                string formatted = abs < 1_000 ? shortVal.ToString("N0") : shortVal.ToString("0.##");

                return $"{formatted}{suffix} {icon}";
            }
            catch
            {
                return value.ToString("N0");
            }
        }




        // =========================================================
        // Exibe valores completos sem centavos + ícone da moeda
        // =========================================================
        public static string FmtDenarsFull(double value)
        {
            try
            {
                string formatted = string.Format("{0:N0}", value);
                string icon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";
                return $"{formatted} {icon}";
            }
            catch
            {
                return value.ToString("N0");
            }
        }




        /// <summary>
        /// Versão localizada de FmtDenars. Usa chave de idioma para a moeda.
        /// </summary>
        public static string FmtDenarsL(double v)
        {
            return FmtDenars(v); // delega ao formato principal
        }

        /// <summary>
        /// Apenas o número de denares, sem texto.
        /// Útil para logs internos ou cálculos.
        /// </summary>
        public static string FmtNumber(double v)
        {
            return Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        }

        // ============================================================
        // Financial helpers
        // ============================================================

        public static float CalcSavingsAnnualRate(float prosperity)
        {
            const float maxAPY = 0.45f;
            const float maxPros = 6000f;

            float factor = MathF.Max(0f, MathF.Min(1f, prosperity / maxPros));
            return maxAPY * factor;
        }

        public static float CalcLoanAnnualRate(float prosperity, float renown)
        {
            const float baseAPR = 0.50f;
            float pTerm = 1f - MathF.Min(prosperity / 12000f, 0.5f);
            float rTerm = 1f - MathF.Min(renown / 1000f, 0.5f);
            float apr = baseAPR * pTerm * rTerm;
            return MathF.Max(0.05f, apr);
        }

        public static float CalcLateFeeDailyRate(float prosperity)
        {
            const float baseFee = 0.03f;
            float mult = 0.5f + MathF.Min(prosperity / 12000f, 0.5f);
            return baseFee * mult;
        }

        public static float CalcLoanLimit(float prosperity, float renown)
        {
            return prosperity * (renown / 10f) * 0.8f;
        }

        // ============================================================
        // Rate normalization helpers
        // ============================================================

        public static float ToFractionRate(float rate)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
                return 0f;
            return rate > 1f ? rate / 100f : rate;
        }

        public static float ToPercentRate(float rate)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f)
                return 0f;
            return rate < 1f ? rate * 100f : rate;
        }
    }
}
