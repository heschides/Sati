using Sati.Contracts.V1;

namespace Sati.Models.Billing;

/// <summary>
/// One entry of the bank deposit that paid a remittance. Append-only: a mistyped amount is
/// corrected by a later entry that names the one it supersedes, so the reconciliation trail
/// shows every figure anyone entered and who entered it. The latest entry is the current one.
/// </summary>
public sealed class EftDepositRecord
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public long RemittanceDepositId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DepositDate { get; set; }
    public string? BankTraceNumber { get; set; }
    public string? Note { get; set; }
    public long? SupersedesRecordId { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public bool IsSynthetic { get; set; }
}

/// <summary>
/// The payer's verdict on one claim from a 277CA, kept per claim so a rejected claim can be
/// found and resent. The batch-level submission event only says how the file fared overall.
/// </summary>
public sealed class ClaimAcknowledgementOutcome
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int BillingPeriodId { get; set; }
    public long EdiGenerationId { get; set; }
    public Guid ResponseId { get; set; }
    public string ClaimReference { get; set; } = string.Empty;
    public ClaimAcknowledgementDisposition Disposition { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public bool IsSynthetic { get; set; }
}

/// <summary>
/// A request to send a claim again: resent as a new claim after a rejection, or as a
/// replacement or void of a claim the payer already adjudicated. Append-only. The original
/// claim line and every earlier submission stay exactly as they were; the correction carries
/// its own claim snapshot and, for a replacement or void, the payer's claim number.
/// </summary>
public sealed class ClaimCorrection
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int BillingPeriodId { get; set; }
    public int ClaimLineId { get; set; }
    public int NoteId { get; set; }
    public ClaimCorrectionAction Action { get; set; }
    public string? PayerClaimControlNumber { get; set; }
    public string ClaimSnapshotJson { get; set; } = string.Empty;

    // Identity fields as this correction bills them, consistent with its snapshot. Service
    // date, procedure, units, and charge are the original line's: the service did not change.
    public string ClientMaineCareId { get; set; } = string.Empty;
    public string RenderingProviderNpi { get; set; } = string.Empty;
    public string DiagnosisCode { get; set; } = string.Empty;
    public int PlaceOfService { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? CorrectsEdiGenerationId { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
}

/// <summary>Which correction file carried a correction. Append-only; a correction may be regenerated.</summary>
public sealed class ClaimCorrectionSubmission
{
    public long ClaimCorrectionId { get; set; }
    public long EdiGenerationId { get; set; }
}
