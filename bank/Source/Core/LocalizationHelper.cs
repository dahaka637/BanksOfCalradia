using TaleWorlds.Localization;

namespace BanksOfCalradia.Source.Core
{
    /// <summary>
    /// Helper para textos localizados do mod.
    /// Permite usar L.S("id", "fallback") para texto direto
    /// e L.T("id", "fallback") para TextObject.
    /// </summary>
    public static class L
    {
        // Retorna TextObject localizado com fallback em inglês.
        public static TextObject T(string id, string fallback)
            => new($"{{=bank_{id}}}{fallback}");

        // Retorna string localizada com fallback em inglês.
        public static string S(string id, string fallback)
            => T(id, fallback).ToString();
    }
}
