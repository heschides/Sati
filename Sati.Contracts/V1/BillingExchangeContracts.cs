namespace Sati.Contracts.V1;

public enum BillingSubmissionStage
{
    Generated,
    Transmitted,
    TransportFailed,
    FunctionalAccepted,
    FunctionalRejected,
    ClaimAccepted,
    ClaimRejected,
    PartiallyAccepted,
    Paid,
    Reconciled,
    RemittanceReceived,
    RemittanceNeedsReview,
    ClaimReceived,
    ClaimNeedsReview
}

public enum RemittanceClaimStatus
{
    Paid,
    PartiallyPaid,
    Denied,
    Reversed,
    Unmatched,
    NeedsReview
}

public enum DepositReconciliationStatus
{
    AwaitingEft,
    Matched,
    EftMismatch,
    RemittanceMismatch
}

public sealed record BillingSubmissionHistoryDto(
    long Id,
    int BillingPeriodId,
    int Year,
    int Month,
    string CaseManagerName,
    int ClaimCount,
    DateTime OccurredAtUtc,
    string Stage,
    string? Reference,
    string? ResponseType,
    string? ResponseCode,
    string? Explanation,
    bool IsSynthetic)
{
    public long? EdiGenerationId { get; init; }
    public Guid? ResponseId { get; init; }
}

/// <summary>Preserves financial progress when earlier acknowledgements arrive late.</summary>
public static partial class BillingSubmissionProgressRules
{
    public static BillingSubmissionHistoryDto? Current(IEnumerable<BillingSubmissionHistoryDto> history)
    {
        var rows = history.ToList();
        var generation = rows.Where(row => row.EdiGenerationId.HasValue)
            .MaxBy(row => row.EdiGenerationId)?.EdiGenerationId;
        var candidates = generation.HasValue ? rows.Where(row => row.EdiGenerationId == generation) : rows;
        return candidates.OrderByDescending(row => Rank(row.Stage))
            .ThenByDescending(row => row.OccurredAtUtc).ThenByDescending(row => row.Id).FirstOrDefault();
    }

    public static int Rank(string stage) => Enum.TryParse<BillingSubmissionStage>(stage, out var parsed) ? Rank(parsed) : -1;

    public static int Rank(BillingSubmissionStage stage) => stage switch
    {
        BillingSubmissionStage.Generated => 0,
        BillingSubmissionStage.Transmitted or BillingSubmissionStage.TransportFailed => 1,
        BillingSubmissionStage.FunctionalAccepted or BillingSubmissionStage.FunctionalRejected => 2,
        BillingSubmissionStage.ClaimReceived or BillingSubmissionStage.ClaimNeedsReview or BillingSubmissionStage.ClaimAccepted or BillingSubmissionStage.ClaimRejected or BillingSubmissionStage.PartiallyAccepted => 3,
        BillingSubmissionStage.RemittanceReceived or BillingSubmissionStage.Paid => 4,
        BillingSubmissionStage.RemittanceNeedsReview => 5,
        BillingSubmissionStage.Reconciled => 6,
        _ => -1
    };
}

public sealed record RemittanceClaimOutcomeDto(
    long Id,
    int? BillingPeriodId,
    string ClaimReference,
    string PayerName,
    DateTime ReceivedAtUtc,
    DateTime? PaymentDate,
    string Status,
    decimal BilledAmount,
    decimal? AllowedAmount,
    decimal PaidAmount,
    decimal AdjustmentAmount,
    decimal PatientResponsibilityAmount,
    string? ReasonCode,
    string? Explanation,
    string? PaymentReference,
    bool IsSynthetic);

public sealed record RemittanceDepositDto(
    long Id,
    string PaymentReference,
    string PayerName,
    DateTime ReceivedAtUtc,
    DateTime? PaymentDate,
    decimal ClaimPaymentAmount,
    decimal ProviderLevelAdjustmentAmount,
    string? ProviderLevelAdjustmentSummary,
    decimal RemittancePaymentAmount,
    decimal? EftDepositAmount,
    string Status,
    decimal? Difference,
    string StatusExplanation,
    bool IsSynthetic);

/// <summary>
/// Owns the arithmetic that decides whether an 835 and its EFT can be treated as reconciled.
/// Provider-level adjustments use a signed amount: a takeback is negative; interest is positive.
/// </summary>
public static class DepositReconciliationRules
{
    public static DepositReconciliationStatus GetStatus(
        decimal claimPaymentAmount,
        decimal providerLevelAdjustmentAmount,
        decimal remittancePaymentAmount,
        decimal? eftDepositAmount)
    {
        if (claimPaymentAmount + providerLevelAdjustmentAmount != remittancePaymentAmount)
            return DepositReconciliationStatus.RemittanceMismatch;
        if (!eftDepositAmount.HasValue)
            return DepositReconciliationStatus.AwaitingEft;
        return eftDepositAmount.Value == remittancePaymentAmount
            ? DepositReconciliationStatus.Matched
            : DepositReconciliationStatus.EftMismatch;
    }

    public static string Explain(DepositReconciliationStatus status) => status switch
    {
        DepositReconciliationStatus.Matched => "The 835 payment and EFT deposit match to the penny.",
        DepositReconciliationStatus.AwaitingEft => "The 835 is present, but the EFT deposit has not been recorded yet.",
        DepositReconciliationStatus.EftMismatch => "The EFT deposit does not equal the payment amount reported by the 835.",
        _ => "Claim payments plus provider-level adjustments do not equal the 835 payment amount."
    };
}

/// <summary>
/// A deliberately small, versioned presentation catalog for common demo and workflow codes.
/// It is not a substitute for importing the current authoritative CARC/RARC code lists.
/// </summary>
public static class ClaimAdjustmentReasonCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Reasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CO-16"] = "Provider responsibility — information is missing or invalid.",
            ["CO-18"] = "Provider responsibility — this appears to be a duplicate claim or service.",
            ["CO-45"] = "Provider responsibility — contractual write-off; the charge exceeded the allowed amount.",
            ["CO-96"] = "Provider responsibility — the service is not covered under the payer's rules.",
            ["PR-1"] = "Patient responsibility — deductible amount.",
            ["PR-2"] = "Patient responsibility — coinsurance amount.",
            ["PR-3"] = "Patient responsibility — copayment amount.",
            ["OA-23"] = "Other adjustment — prior payer payment or adjustment affected this amount."
        };

    public static string Humanize(string? code, string? payerExplanation = null)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (Reasons.TryGetValue(normalized, out var explanation))
            return explanation;
        if (!string.IsNullOrWhiteSpace(payerExplanation))
            return payerExplanation.Trim();
        return string.IsNullOrEmpty(normalized)
            ? "No adjustment reason was supplied."
            : $"Reason {normalized} needs review against the current payer guidance.";
    }
}
