using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// Durable, append-only notice that a linked submitted form-work note was
/// affected by a change to its form attestation. It records the facts seen at
/// correction time without rewriting the note or an existing claim.
/// </summary>
public sealed class FormAttestationChangeReviewFlag
{
    public long Id { get; private set; }
    public Guid FlagId { get; private set; }
    public int AgencyId { get; private set; }
    public int PersonId { get; private set; }
    public int NoteId { get; private set; }
    public int FormId { get; private set; }
    public int? ClaimLineId { get; private set; }
    public DateTime? NoteActivityDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? PreviousCompletedOn { get; private set; }
    public DateTime? RevisedCompletedOn { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public bool RequiresSupervisorAttention { get; private set; }
    public bool RequiresBillingAttention { get; private set; }
    public FormAttestationBillingHoldReason BillingHoldReasons { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public bool MustHoldBilling =>
        BillingHoldReasons != FormAttestationBillingHoldReason.None;

    private FormAttestationChangeReviewFlag() { }

    public FormAttestationChangeReviewFlagDto ToContract() => new(
        FlagId, PersonId, NoteId, FormId, ClaimLineId, NoteActivityDate, DueDate,
        PreviousCompletedOn, RevisedCompletedOn, Reason,
        RequiresSupervisorAttention, RequiresBillingAttention, BillingHoldReasons,
        DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc));

    public static FormAttestationChangeReviewFlag Create(
        int agencyId,
        int personId,
        int noteId,
        int formId,
        int? claimLineId,
        DateTime? noteActivityDate,
        DateTime dueDate,
        DateTime? previousCompletedOn,
        DateTime? revisedCompletedOn,
        string reason,
        FormAttestationImpact impact,
        DateTime createdAtUtc)
    {
        if (agencyId <= 0 || personId <= 0 || noteId <= 0 || formId <= 0)
            throw new ArgumentOutOfRangeException(nameof(noteId), "The linked record identities are required.");
        if (claimLineId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(claimLineId));
        if (dueDate == default)
            throw new ArgumentOutOfRangeException(nameof(dueDate));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason for the attestation change is required.", nameof(reason));
        ArgumentNullException.ThrowIfNull(impact);
        if (!impact.AttestationDateChanged ||
            (!impact.RequiresSupervisorAttention && !impact.RequiresBillingAttention))
            throw new ArgumentException("A changed, submitted record with a review audience is required.", nameof(impact));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The review-flag timestamp must be UTC.", nameof(createdAtUtc));

        return new FormAttestationChangeReviewFlag
        {
            FlagId = Guid.NewGuid(),
            AgencyId = agencyId,
            PersonId = personId,
            NoteId = noteId,
            FormId = formId,
            ClaimLineId = claimLineId,
            NoteActivityDate = noteActivityDate?.Date,
            DueDate = dueDate.Date,
            PreviousCompletedOn = previousCompletedOn?.Date,
            RevisedCompletedOn = revisedCompletedOn?.Date,
            Reason = reason.Trim(),
            RequiresSupervisorAttention = impact.RequiresSupervisorAttention,
            RequiresBillingAttention = impact.RequiresBillingAttention,
            BillingHoldReasons = impact.BillingHoldReasons,
            CreatedAtUtc = createdAtUtc
        };
    }
}
