namespace Sati.Models.Billing
{
    public class ClaimLine
    {
        public int Id { get; set; }
        public int NoteId { get; set; }
        public int BillingPeriodId { get; set; }

        public DateTime DateOfService { get; set; }
        public string ProcedureCode { get; set; } = string.Empty;
        public decimal? Units { get; set; }
        public string ClientMaineCareId { get; set; } = string.Empty;
        public string RenderingProviderNpi { get; set; } = string.Empty;
        public string DiagnosisCode { get; set; } = string.Empty;
        public int PlaceOfService { get; set; }

        public bool IsComplianceException { get; set; }
        public string? ComplianceExceptionReason { get; set; }

        public BillingPeriod BillingPeriod { get; set; } = null!;
        public Note Note { get; set; } = null!;
    }
}