using System;

namespace BanksOfCalradia.Source.Systems.Data
{
    public class BankSavingsData
    {
        public string TownId { get; set; }
        public string PlayerId { get; set; }
        public float Amount { get; set; }

        // 🔹 Campo de acúmulo de juros fracionados (para evitar perda de precisão)
        public float PendingInterest { get; set; } = 0f;

        // 🔹 Novo campo: se verdadeiro, os juros são automaticamente reinvestidos
        // (modo de débito automático / juros compostos)
        public bool AutoReinvest { get; set; } = false;
    }
}
