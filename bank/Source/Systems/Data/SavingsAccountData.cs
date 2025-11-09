using System;

namespace BanksOfCalradia.Source.Systems.Data
{
    public class BankSavingsData
    {
        public string TownId { get; set; }
        public string PlayerId { get; set; }

        // Armazenamento em double, compatível com versões antigas (float)
        private double _amount;
        public double Amount
        {
            get => _amount;
            set => _amount = value;
        }

        public double PendingInterest { get; set; } = 0d;
        public bool AutoReinvest { get; set; } = false;
    }

}
