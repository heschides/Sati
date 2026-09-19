namespace Sati.Contracts.V1;

/// <summary>
/// How a claim is sent again. The value is the 837P claim frequency code (CLM05-3) the
/// correction carries, so it is persisted and compared as that number.
/// </summary>
public enum ClaimCorrectionAction
{
    /// <summary>Sent as a new claim (frequency 1). For a claim the payer never adjudicated.</summary>
    Resubmit = 1,

    /// <summary>Replaces an adjudicated claim (frequency 7), citing the payer's claim number.</summary>
    Replace = 7,

    /// <summary>Voids an adjudicated claim (frequency 8), citing the payer's claim number.</summary>
    Void = 8
}

/// <summary>Where one claim stands, from its latest submission.</summary>
public enum ClaimLifecycleState
{
    NotSent,
    CorrectionWaitingToSend,
    AwaitingFileCheck,
    FileRejected,
    AwaitingClaimCheck,
    ClaimRejected,
    ClaimReceived,
    AwaitingPayment,
    Paid,
    PartiallyPaid,
    Denied,
    Reversed,
    NeedsReview
}

public enum ClaimFileVerdict { None, Accepted, Rejected }

/// <summary>
/// One time a claim was sent: the original (<see cref="Action"/> null) or a correction, with
/// whatever the file check, the claim check, and the payment report said about it.
/// </summary>
public sealed record ClaimSubmissionFacts(
    ClaimCorrectionAction? Action,
    ClaimFileVerdict FileVerdict,
    ClaimAcknowledgementDisposition? ClaimVerdict,
    RemittanceClaimStatus? RemittanceStatus,
    string? PayerClaimControlNumber);

public sealed record ClaimCorrectionOptions(
    ClaimLifecycleState State,
    IReadOnlyList<ClaimCorrectionAction> AllowedActions,
    string Explanation,
    string? PayerClaimControlNumber);

/// <summary>
/// Sole owner of which correction a claim allows. Shared by the API, which enforces it, and
/// the desktop, which offers only what it allows.
/// </summary>
/// <remarks>
/// <para>
/// The distinction that matters is whether the payer ever adjudicated the claim. A claim
/// turned away at the file check or the claim check was never judged, so it has no payer
/// claim number and is simply sent again as a new claim. A claim that reached the payment
/// report has a payer claim number, and a second new claim for the same service would be a
/// duplicate (CO-18); it is corrected by replacing (frequency 7) or voiding (frequency 8)
/// the claim the payer holds, citing that number.
/// </para>
/// <para>
/// The frequency codes are the X12 837P standard. Whether and how MaineCare accepts each
/// through the clearinghouse is a companion-guide question this rule does not answer.
/// </para>
/// </remarks>
public static class ClaimCorrectionRules
{
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 500;
    public const int PayerClaimControlNumberMaxLength = 50;

    public static string FrequencyCode(ClaimCorrectionAction? action) =>
        action is null ? "1" : ((int)action.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static ClaimCorrectionOptions Evaluate(
        IReadOnlyList<ClaimSubmissionFacts> submissions,
        bool hasUnsentCorrection)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        if (hasUnsentCorrection)
            return None(ClaimLifecycleState.CorrectionWaitingToSend,
                "A correction is waiting to be sent. Generate the correction file before correcting again.");
        if (submissions.Count == 0)
            return None(ClaimLifecycleState.NotSent, "This claim has not been sent yet.");

        var latest = submissions[^1];
        var state = StateOf(latest);
        var standing = StandingClaimNumber(submissions);

        return state switch
        {
            ClaimLifecycleState.FileRejected or ClaimLifecycleState.ClaimRejected =>
                standing is null
                    ? Allow(state, [ClaimCorrectionAction.Resubmit], null,
                        "Turned away before the payer reviewed it. Fix the problem, then resend it as a new claim.")
                    : AdjudicatedOptions(state, standing,
                        "The last correction was turned away before review. The payer still holds the earlier claim."),
            ClaimLifecycleState.Paid or ClaimLifecycleState.PartiallyPaid =>
                AdjudicatedOptions(state, standing,
                    "Adjudicated. Replace it to change what was billed, or void it if it should not have been billed."),
            ClaimLifecycleState.Denied =>
                standing is null
                    ? Allow(state, [ClaimCorrectionAction.Resubmit], null,
                        "Denied, but the payment report gave no payer claim number to replace. It can only be resent as a new claim.")
                    : Allow(state, [ClaimCorrectionAction.Replace], standing.PayerClaimControlNumber,
                        "Denied. If the denial can be fixed, send a replacement citing the payer's claim number."),
            ClaimLifecycleState.Reversed =>
                Allow(state, [ClaimCorrectionAction.Resubmit], null,
                    "The payer took this payment back. If the service should still be billed, resend it as a new claim."),
            ClaimLifecycleState.AwaitingFileCheck => None(state, "Waiting for the clearinghouse's file check."),
            ClaimLifecycleState.AwaitingClaimCheck => None(state, "The file was accepted. Waiting for the payer's claim check."),
            ClaimLifecycleState.ClaimReceived or ClaimLifecycleState.AwaitingPayment =>
                None(state, "Accepted for review. Waiting for the payment report."),
            _ => None(state, "Something in the payer's answer needs a person to read it before this claim is changed.")
        };
    }

    public static ClaimLifecycleState StateOf(ClaimSubmissionFacts submission) =>
        submission.RemittanceStatus switch
        {
            RemittanceClaimStatus.Paid => ClaimLifecycleState.Paid,
            RemittanceClaimStatus.PartiallyPaid => ClaimLifecycleState.PartiallyPaid,
            RemittanceClaimStatus.Denied => ClaimLifecycleState.Denied,
            RemittanceClaimStatus.Reversed => ClaimLifecycleState.Reversed,
            RemittanceClaimStatus.NeedsReview or RemittanceClaimStatus.Unmatched => ClaimLifecycleState.NeedsReview,
            _ => submission.ClaimVerdict switch
            {
                ClaimAcknowledgementDisposition.Rejected => ClaimLifecycleState.ClaimRejected,
                ClaimAcknowledgementDisposition.Received => ClaimLifecycleState.ClaimReceived,
                ClaimAcknowledgementDisposition.NeedsReview => ClaimLifecycleState.NeedsReview,
                ClaimAcknowledgementDisposition.Accepted => ClaimLifecycleState.AwaitingPayment,
                _ => submission.FileVerdict switch
                {
                    ClaimFileVerdict.Rejected => ClaimLifecycleState.FileRejected,
                    ClaimFileVerdict.Accepted => ClaimLifecycleState.AwaitingClaimCheck,
                    _ => ClaimLifecycleState.AwaitingFileCheck
                }
            }
        };

    public static string Describe(ClaimLifecycleState state) => state switch
    {
        ClaimLifecycleState.NotSent => "Not sent",
        ClaimLifecycleState.CorrectionWaitingToSend => "Correction waiting to send",
        ClaimLifecycleState.AwaitingFileCheck => "Awaiting file check",
        ClaimLifecycleState.FileRejected => "File rejected",
        ClaimLifecycleState.AwaitingClaimCheck => "Awaiting claim check",
        ClaimLifecycleState.ClaimRejected => "Claim rejected",
        ClaimLifecycleState.ClaimReceived => "Received by payer",
        ClaimLifecycleState.AwaitingPayment => "Awaiting payment",
        ClaimLifecycleState.Paid => "Paid",
        ClaimLifecycleState.PartiallyPaid => "Partially paid",
        ClaimLifecycleState.Denied => "Denied",
        ClaimLifecycleState.Reversed => "Reversed",
        _ => "Needs review"
    };

    public static string Describe(ClaimCorrectionAction action) => action switch
    {
        ClaimCorrectionAction.Resubmit => "Resend as new claim",
        ClaimCorrectionAction.Replace => "Replace claim",
        _ => "Void claim"
    };

    /// <summary>Why a correction request is refused, or null when its reason is acceptable.</summary>
    public static string? ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < ReasonMinLength)
            return $"Explain the correction in at least {ReasonMinLength} characters.";
        return trimmed.Length > ReasonMaxLength
            ? $"The correction reason is limited to {ReasonMaxLength} characters."
            : null;
    }

    // The claim the payer currently holds: the latest adjudication that carried a claim number,
    // unless a later payment report reversed it (then nothing is standing and a new claim is due).
    private static ClaimSubmissionFacts? StandingClaimNumber(IReadOnlyList<ClaimSubmissionFacts> submissions)
    {
        for (var index = submissions.Count - 1; index >= 0; index--)
        {
            var submission = submissions[index];
            if (submission.RemittanceStatus == RemittanceClaimStatus.Reversed)
                return null;
            if (submission.RemittanceStatus is RemittanceClaimStatus.Paid or
                    RemittanceClaimStatus.PartiallyPaid or RemittanceClaimStatus.Denied &&
                !string.IsNullOrWhiteSpace(submission.PayerClaimControlNumber))
                return submission;
        }
        return null;
    }

    private static ClaimCorrectionOptions AdjudicatedOptions(
        ClaimLifecycleState state, ClaimSubmissionFacts? standing, string explanation)
    {
        if (standing is null)
            return None(state, "The payment report gave no payer claim number, so this claim cannot be replaced or voided from Sati.");
        IReadOnlyList<ClaimCorrectionAction> actions = standing.RemittanceStatus == RemittanceClaimStatus.Denied
            ? [ClaimCorrectionAction.Replace]
            : [ClaimCorrectionAction.Replace, ClaimCorrectionAction.Void];
        return Allow(state, actions, standing.PayerClaimControlNumber, explanation);
    }

    private static ClaimCorrectionOptions Allow(
        ClaimLifecycleState state, IReadOnlyList<ClaimCorrectionAction> actions, string? number, string explanation) =>
        new(state, actions, explanation, number);

    private static ClaimCorrectionOptions None(ClaimLifecycleState state, string explanation) =>
        new(state, [], explanation, null);
}

/// <summary>Sole owner of what a recorded bank deposit may say.</summary>
public static class EftDepositRules
{
    public const decimal MaximumAmount = 1_000_000_000m;
    public const int BankTraceMaxLength = 80;
    public const int NoteMaxLength = 500;

    /// <summary>Field → problems. Empty when the entry is acceptable.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(
        decimal amount, DateTime depositDate, DateTime today, string? bankTraceNumber, string? note, bool isCorrection)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount < 0 || amount >= MaximumAmount)
            errors["amount"] = ["Enter the deposit amount as zero or more."];
        else if (decimal.Round(amount, 2) != amount)
            errors["amount"] = ["Enter the deposit amount to the cent."];
        if (depositDate.Date > today.Date)
            errors["depositDate"] = ["A deposit date cannot be in the future."];
        if (bankTraceNumber?.Trim().Length > BankTraceMaxLength)
            errors["bankTraceNumber"] = [$"The bank trace number is limited to {BankTraceMaxLength} characters."];
        var trimmedNote = note?.Trim() ?? string.Empty;
        if (trimmedNote.Length > NoteMaxLength)
            errors["note"] = [$"The note is limited to {NoteMaxLength} characters."];
        else if (isCorrection && trimmedNote.Length == 0)
            errors["note"] = ["Say why the earlier entry is being corrected."];
        return errors;
    }
}

public sealed record BillingClaimStatusDto(
    int ClaimLineId,
    int NoteId,
    string ClientName,
    DateTime DateOfService,
    decimal ChargeAmount,
    string State,
    string StateLabel,
    string Explanation,
    IReadOnlyList<string> AllowedActions,
    string? PayerClaimControlNumber,
    int SubmissionCount);

public sealed record CreateClaimCorrectionRequest(int ClaimLineId, ClaimCorrectionAction Action, string Reason);

public sealed record ClaimCorrectionDto(
    long Id,
    int ClaimLineId,
    string Action,
    string? PayerClaimControlNumber,
    string Reason,
    DateTime RequestedAtUtc);

public sealed record RecordEftDepositRequest(
    decimal Amount,
    DateTime DepositDate,
    string? BankTraceNumber,
    string? Note,
    long? PreviousRecordId);

public sealed record EftDepositRecordDto(
    long Id,
    long RemittanceDepositId,
    decimal Amount,
    DateTime DepositDate,
    string? BankTraceNumber,
    string? Note,
    long? SupersedesRecordId,
    string RecordedBy,
    DateTime RecordedAtUtc);
