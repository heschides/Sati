namespace Sati.Contracts.V1;

/// <summary>
/// Why the note documenting a form's completion cannot be billed. These reasons
/// are independent of the ordinary compliance window for later, unrelated work.
/// </summary>
[Flags]
public enum FormAttestationBillingHoldReason
{
    None = 0,
    MissingAttestation = 1 << 0,
    MissingActivityDate = 1 << 1,
    ActivityDateMismatch = 1 << 2,
    CompletedAfterDueDate = 1 << 3
}

public sealed record FormAttestationImpact(
    bool AttestationDateChanged,
    bool RequiresSupervisorAttention,
    bool RequiresBillingAttention,
    FormAttestationBillingHoldReason BillingHoldReasons)
{
    public bool MustHoldBilling => BillingHoldReasons != FormAttestationBillingHoldReason.None;
}

/// <summary>
/// Classifies the effect of changing the attestation for one already-linked form
/// note. The caller persists any review flag; this pure rule neither changes the
/// note's clinical status nor assumes that a sent claim can be rewritten.
/// </summary>
public static class FormAttestationImpactRules
{
    public static FormAttestationImpact Evaluate(
        int? noteStatus,
        bool hasReachedBilling,
        DateTime? noteActivityDate,
        DateTime? previousAttestationDate,
        DateTime? revisedAttestationDate,
        DateTime dueDate)
    {
        if (dueDate == default)
            throw new ArgumentOutOfRangeException(nameof(dueDate), "A form due date is required.");

        var changed = previousAttestationDate?.Date != revisedAttestationDate?.Date;
        var reasons = FormAttestationBillingHoldReason.None;
        if (noteActivityDate is null)
            reasons |= FormAttestationBillingHoldReason.MissingActivityDate;

        if (revisedAttestationDate is not DateTime completedOn)
        {
            reasons |= FormAttestationBillingHoldReason.MissingAttestation;
        }
        else
        {
            if (noteActivityDate is DateTime activityDate && activityDate.Date != completedOn.Date)
                reasons |= FormAttestationBillingHoldReason.ActivityDateMismatch;
            if (completedOn.Date > dueDate.Date)
                reasons |= FormAttestationBillingHoldReason.CompletedAfterDueDate;
        }

        return new FormAttestationImpact(
            changed,
            changed && noteStatus is NoteWorkflow.Logged or NoteWorkflow.Approved or NoteWorkflow.Returned,
            changed && (hasReachedBilling || noteStatus == NoteWorkflow.Approved),
            reasons);
    }
}
