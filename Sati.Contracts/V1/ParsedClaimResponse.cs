namespace Sati.Contracts.V1;

public enum ClaimAcknowledgementDisposition { Accepted, Received, Rejected, NeedsReview }

/// <summary>AK1/AK2 identify the original 837 group/transaction, not this response's ISA13.</summary>
public sealed record FunctionalAcknowledgementDetail(
    string OriginalGroupControlNumber,
    IReadOnlyList<string> OriginalTransactionControlNumbers,
    BillingSubmissionStage Stage,
    string ResponseCode);

/// <summary>A patient-level 277CA claim trace; provider/receiver aggregate statuses are never claims.</summary>
public sealed record ClaimAcknowledgementDetail(
    string ClaimReference,
    ClaimAcknowledgementDisposition Disposition,
    string CategoryCode,
    string StatusCode,
    decimal? BilledAmount)
{
    public IReadOnlyList<string> ServiceLineReferences { get; init; } = [];
}

/// <summary>Strictly validated, bounded single-transaction response. Content remains PHI.</summary>
public sealed record ParsedClaimResponse(
    ClaimResponseEnvelope Envelope,
    FunctionalAcknowledgementDetail? FunctionalAcknowledgement,
    IReadOnlyList<ClaimAcknowledgementDetail> ClaimAcknowledgements,
    RemittanceResult? Remittance)
{
    /// <summary>Length-framed transaction content, with envelope and ST02/SE02 omitted.
    /// May be hashed for duplicate detection; must never be logged or returned to the client.</summary>
    public string CanonicalTransaction { get; init; } = string.Empty;
}

public sealed record SubmittedClaimReference(
    string ClaimReference,
    decimal BilledAmount,
    IReadOnlyList<string> ServiceLineReferences);

/// <summary>Correlation facts extracted from the exact retained outbound 837P.</summary>
public sealed record ParsedClaimSubmission(
    ClaimResponseEnvelope Envelope,
    IReadOnlyList<SubmittedClaimReference> Claims,
    string BillingProviderNpi);
