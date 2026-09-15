namespace Sati.Models;

/// <summary>
/// Immutable administrator decision that releases only its explicitly selected
/// notes after every named compliance obligation has been completed.
/// </summary>
public sealed class BillingComplianceRecoveryDecision
{
    private readonly List<BillingComplianceRecoveryObligation> _obligations = [];
    private readonly List<BillingComplianceRecoveryNote> _notes = [];

    public long Id { get; private set; }
    public Guid DecisionId { get; private set; }
    public int AgencyId { get; private set; }
    public int PersonId { get; private set; }
    public int AdminUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string Explanation { get; private set; } = string.Empty;
    public bool AttestationConfirmed { get; private set; }
    public IReadOnlyCollection<BillingComplianceRecoveryObligation> Obligations => _obligations;
    public IReadOnlyCollection<BillingComplianceRecoveryNote> Notes => _notes;

    private BillingComplianceRecoveryDecision() { }

    public static BillingComplianceRecoveryDecision FromContract(
        Contracts.V1.BillingComplianceRecoveryDecision source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.AttestationConfirmed || string.IsNullOrWhiteSpace(source.Explanation) ||
            source.RecordedAtUtc.Kind != DateTimeKind.Utc || source.NoteIds.Count == 0 ||
            source.Obligations.Count == 0 ||
            source.Obligations.Any(item => string.IsNullOrWhiteSpace(item.EvidenceId)))
            throw new ArgumentException("The recovery decision is incomplete.", nameof(source));

        var entity = new BillingComplianceRecoveryDecision
        {
            DecisionId = source.DecisionId,
            AgencyId = source.AgencyId,
            PersonId = source.PersonId,
            AdminUserId = source.AdminUserId,
            RecordedAtUtc = source.RecordedAtUtc,
            Explanation = source.Explanation.Trim(),
            AttestationConfirmed = true
        };
        entity._obligations.AddRange(source.Obligations.Select(
            obligation => BillingComplianceRecoveryObligation.Create(obligation)));
        entity._notes.AddRange(source.NoteIds.Distinct().Select(BillingComplianceRecoveryNote.Create));
        return entity;
    }

    public Contracts.V1.BillingComplianceRecoveryDecision ToContract() => new(
        DecisionId,
        AgencyId,
        PersonId,
        AdminUserId,
        DateTime.SpecifyKind(RecordedAtUtc, DateTimeKind.Utc),
        Explanation,
        AttestationConfirmed,
        _obligations.Select(item => item.ToContract()).ToArray(),
        _notes.Select(item => item.NoteId).ToArray());
}

public sealed class BillingComplianceRecoveryObligation
{
    public long Id { get; private set; }
    public long BillingComplianceRecoveryDecisionId { get; private set; }
    public string ObligationId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public DateTime DueDate { get; private set; }
    public DateTime CompletedDate { get; private set; }
    public string EvidenceId { get; private set; } = string.Empty;

    private BillingComplianceRecoveryObligation() { }

    internal static BillingComplianceRecoveryObligation Create(
        Contracts.V1.BillingRecoveryObligationOption source) =>
        new()
        {
            ObligationId = source.ObligationId,
            Name = source.Name,
            DueDate = source.DueDate.Date,
            CompletedDate = source.CompletedDate.Date,
            EvidenceId = source.EvidenceId.Trim()
        };

    internal Contracts.V1.BillingRecoveryObligationOption ToContract() => new(
        ObligationId, Name, DueDate, CompletedDate, EvidenceId);
}

public sealed class BillingComplianceRecoveryNote
{
    public long Id { get; private set; }
    public long BillingComplianceRecoveryDecisionId { get; private set; }
    public int NoteId { get; private set; }

    private BillingComplianceRecoveryNote() { }

    internal static BillingComplianceRecoveryNote Create(int noteId) => new() { NoteId = noteId };
}
