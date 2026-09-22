using Sati.Contracts.V1;

namespace Sati.ViewModels;

/// <summary>One latest review notice per affected form-work note.</summary>
public sealed class FormAttestationChangeReviewRow(FormAttestationChangeReviewFlagDto flag)
{
    public int NoteId => flag.NoteId;
    public int? FormId => flag.FormId;
    public string RecordLabel => flag.ReleaseObligationId is long releaseId
        ? $"Client #{flag.PersonId} · Note #{flag.NoteId} · Release #{releaseId}"
        : $"Client #{flag.PersonId} · Note #{flag.NoteId} · Form #{flag.FormId}";
    public string DateLabel =>
        $"Completion {Format(flag.PreviousCompletedOn)} → {Format(flag.RevisedCompletedOn)}; " +
        $"due {flag.DueDate:MM/dd/yy}; note activity {Format(flag.NoteActivityDate)}";
    public string ReasonLabel => $"Reason: {flag.Reason}";
    public string ClaimLabel => flag.ClaimLineId is int claimLineId
        ? $"Claim line #{claimLineId}"
        : "No claim line recorded";
    public string HoldLabel => flag.ReleaseObligationId is not null
        ? "Release date change; review the linked claim"
        : flag.MustHoldBilling
        ? "Billing hold: " + flag.BillingHoldReasons
        : "No current form-date billing hold";
    public string RecordedLabel => $"Change recorded {flag.CreatedAtUtc.ToLocalTime():MM/dd/yy h:mm tt}";
    public bool MustHoldBilling => flag.MustHoldBilling;

    public static IReadOnlyList<FormAttestationChangeReviewRow> Latest(
        IReadOnlyList<FormAttestationChangeReviewFlagDto> flags) =>
        flags.OrderByDescending(flag => flag.CreatedAtUtc)
            .GroupBy(flag => (flag.NoteId, flag.FormId, flag.ReleaseObligationId))
            .Select(group => new FormAttestationChangeReviewRow(group.First()))
            .ToArray();

    private static string Format(DateTime? date) =>
        date is DateTime value ? value.ToString("MM/dd/yy") : "none";
}
