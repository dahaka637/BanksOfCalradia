using System;

namespace BanksOfCalradia.Source.Systems.Data
{
    public class BankLoanData
    {
        public string LoanId { get; set; }         // GUID
        public string TownId { get; set; }
        public string PlayerId { get; set; }

        public float OriginalAmount { get; set; }  // Valor contratado
        public float Remaining { get; set; }       // Quanto falta pagar

        public float InterestRate { get; set; }    // APR fixado na contratação
        public float LateFeeRate { get; set; }     // Multa diária fixada
        public int DurationDays { get; set; }    // Prazo contratado

        public float CreatedAt { get; set; }       // CampaignTime.Now.ToDays (snapshot)
    }
}
