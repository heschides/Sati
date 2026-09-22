namespace Sati.Contracts.V1;

/// <summary>
/// A durable notice that a submitted form-work note's completion date changed.
/// The caller must enforce Supervisor or Billing access before returning it.
/// </summary>
public sealed record FormAttestationChangeReviewFlagDto(
    Guid FlagId,
    int PersonId,
    int NoteId,
    int? FormId,
    int? ClaimLineId,
    DateTime? NoteActivityDate,
    DateTime DueDate,
    DateTime? PreviousCompletedOn,
    DateTime? RevisedCompletedOn,
    string Reason,
    bool RequiresSupervisorAttention,
    bool RequiresBillingAttention,
    FormAttestationBillingHoldReason BillingHoldReasons,
    DateTime CreatedAtUtc,
    long? ReleaseObligationId = null)
{
    public bool MustHoldBilling => BillingHoldReasons != FormAttestationBillingHoldReason.None;
}
