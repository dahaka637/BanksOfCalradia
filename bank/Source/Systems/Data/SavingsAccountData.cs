using System;

namespace BanksOfCalradia.Source.Systems.Data
{
    public class BankSavingsData
    {
        public string TownId { get; set; }
        public string PlayerId { get; set; }
        public float Amount { get; set; }

        // 🔹 Novo campo de acúmulo de juros fracionados
        public float PendingInterest { get; set; } = 0f;
    }

}
