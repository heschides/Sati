using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// One recipient-specific authorization obligation. Rows are retained after a provider
/// relationship ends; RetiredOn limits their prospective application without erasing history.
/// </summary>
public sealed class ReleaseObligation
{
    private readonly List<ReleaseObligationAttestation> _attestations = [];
    private readonly List<ReleaseAuthorizationEvent> _authorizationEvents = [];

    public long Id { get; private set; }
    public Guid ObligationId { get; private set; }
    public int AgencyId { get; private set; }
    public int PersonId { get; private set; }
    public string StableKey { get; private set; } = string.Empty;
    public ReleaseObligationCategory Category { get; private set; }
    public ReleaseObligationTrigger Trigger { get; private set; }
    public DateTime TargetEffectiveDate { get; private set; }
    public string? AssignmentKey { get; private set; }
    public int? RecipientProviderId { get; private set; }
    public string? RecipientDisplayName { get; private set; }
    public DateTime AvailableOn { get; private set; }
    public DateTime DueOn { get; private set; }
    public DateTime AppliesFromOn { get; private set; }
    public DateTime? RetiredOn { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RetirementRecordedAtUtc { get; private set; }
    public IReadOnlyCollection<ReleaseObligationAttestation> Attestations => _attestations;
    public IReadOnlyCollection<ReleaseAuthorizationEvent> AuthorizationEvents =>
        _authorizationEvents;

    public DateTime? CompletedOn => _attestations.Count == 0
        ? null
        : _attestations.Min(x => x.CompletedOn).Date;

    public DateTime? WithdrawnOn => _authorizationEvents
        .Where(x => x.Kind == ReleaseAuthorizationEventKind.Withdrawn)
        .Select(x => (DateTime?)x.OccurredOn.Date)
        .Min();

    private ReleaseObligation() { }

    public static ReleaseObligation Create(
        int agencyId,
        int personId,
        ReleaseObligationPlan plan,
        DateTime createdAtUtc,
        int? recipientProviderId = null,
        string? recipientDisplayName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(agencyId, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(personId, 0);
        ArgumentNullException.ThrowIfNull(plan);
        RequireUtc(createdAtUtc, nameof(createdAtUtc));

        var expectedKey = ReleaseObligationRules.StableKey(
            plan.TargetEffectiveDate, plan.Category, plan.Trigger, plan.AssignmentKey);
        if (!string.Equals(expectedKey, plan.StableKey, StringComparison.Ordinal))
            throw new ArgumentException("The release plan's stable key does not match its scope.",
                nameof(plan));
        if (plan.AvailableOn == default || plan.DueOn == default ||
            plan.AppliesFromOn == default)
            throw new ArgumentException("The release plan is missing a required date.", nameof(plan));
        if (plan.Category == ReleaseObligationCategory.Dhhs)
        {
            if (recipientProviderId is not null || !string.IsNullOrWhiteSpace(recipientDisplayName))
                throw new ArgumentException(
                    "A DHHS obligation does not identify a provider recipient.", nameof(plan));
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(recipientProviderId ?? 0, 0);
            if (string.IsNullOrWhiteSpace(recipientDisplayName))
                throw new ArgumentException(
                    "A recipient-specific release requires a recipient snapshot.",
                    nameof(recipientDisplayName));
        }

        return new ReleaseObligation
        {
            ObligationId = Guid.NewGuid(),
            AgencyId = agencyId,
            PersonId = personId,
            StableKey = plan.StableKey,
            Category = plan.Category,
            Trigger = plan.Trigger,
            TargetEffectiveDate = plan.TargetEffectiveDate.Date,
            AssignmentKey = plan.AssignmentKey,
            RecipientProviderId = recipientProviderId,
            RecipientDisplayName = Normalize(recipientDisplayName),
            AvailableOn = plan.AvailableOn.Date,
            DueOn = plan.DueOn.Date,
            AppliesFromOn = plan.AppliesFromOn.Date,
            RetiredOn = plan.RetiredOn?.Date,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Retire(DateTime retiredOn, DateTime recordedAtUtc)
    {
        RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
        retiredOn = retiredOn.Date;
        if (RetiredOn is DateTime existing)
        {
            if (existing.Date == retiredOn)
                return;
            throw new InvalidOperationException(
                "A release retirement date is retained once it has been recorded.");
        }

        RetiredOn = retiredOn;
        RetirementRecordedAtUtc = recordedAtUtc;
    }

    public ReleaseObligationAttestation AttestManually(
        DateTime completedOn,
        DateTime agencyToday,
        AttestationActorKind actorKind,
        int actorUserId,
        DateTime recordedAtUtc,
        string? reason = null)
    {
        if (actorKind == AttestationActorKind.System)
            throw new ArgumentException(
                "A manual release attestation requires a human actor.", nameof(actorKind));
        if (completedOn.Date < AvailableOn.Date)
            throw new ArgumentOutOfRangeException(nameof(completedOn),
                $"This release obligation becomes available on {AvailableOn:yyyy-MM-dd}.");

        var attestation = ReleaseObligationAttestation.CreateManual(
            StableKey, completedOn, agencyToday, actorKind, actorUserId, recordedAtUtc, reason);
        _attestations.Add(attestation);
        return attestation;
    }

    public ReleaseObligationAttestation AttestFromElectronicSignature(
        DateTime completedOn,
        DateTime agencyToday,
        bool hasGuardian,
        SignerCapacity signerCapacity,
        int signatureCompletionId,
        DateTime recordedAtUtc)
    {
        if (completedOn.Date < AvailableOn.Date)
            throw new ArgumentOutOfRangeException(nameof(completedOn),
                $"This release obligation becomes available on {AvailableOn:yyyy-MM-dd}.");
        if (_attestations.Any(x => x.SignatureCompletionId == signatureCompletionId))
            return _attestations.Single(x => x.SignatureCompletionId == signatureCompletionId);

        var attestation = ReleaseObligationAttestation.CreateElectronicSignature(
            StableKey, completedOn, agencyToday, hasGuardian, signerCapacity,
            signatureCompletionId, recordedAtUtc);
        _attestations.Add(attestation);
        return attestation;
    }

    public ReleaseAuthorizationEvent Withdraw(
        DateTime withdrawnOn,
        DateTime agencyToday,
        int actorUserId,
        DateTime recordedAtUtc,
        string reason)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(actorUserId, 0);
        RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A withdrawal explanation is required.", nameof(reason));
        if (withdrawnOn == default)
            throw new ArgumentException("A withdrawal date is required.", nameof(withdrawnOn));
        withdrawnOn = withdrawnOn.Date;
        if (withdrawnOn > agencyToday.Date)
            throw new ArgumentException("The withdrawal date cannot be in the future.",
                nameof(withdrawnOn));
        if (CompletedOn is not DateTime completedOn)
            throw new InvalidOperationException(
                "An authorization cannot be withdrawn before it has been attested.");
        if (withdrawnOn < completedOn.Date)
            throw new ArgumentException(
                "The withdrawal date cannot precede the authorization completion date.",
                nameof(withdrawnOn));
        if (_authorizationEvents.Any(x => x.Kind == ReleaseAuthorizationEventKind.Withdrawn))
            throw new InvalidOperationException("This authorization is already withdrawn.");

        var change = ReleaseAuthorizationEvent.Withdrawn(
            withdrawnOn, actorUserId, recordedAtUtc, reason);
        _authorizationEvents.Add(change);
        return change;
    }

    public bool IsAuthorizationActive(DateTime asOfDate)
    {
        return ReleaseAuthorizationRules.IsActive(CompletedOn, WithdrawnOn, asOfDate);
    }

    public ReleaseComplianceFact ToComplianceFact() =>
        new(
            StableKey,
            Category,
            DueOn,
            AppliesFromOn,
            RetiredOn,
            _attestations.Select(x => x.ToFact(StableKey)).ToArray(),
            ObligationId,
            TargetEffectiveDate,
            AvailableOn,
            RecipientDisplayName);

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recorded timestamp must be UTC.", parameterName);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReleaseObligationAttestation
{
    public long Id { get; private set; }
    public long ReleaseObligationId { get; private set; }
    public ReleaseObligation ReleaseObligation { get; private set; } = null!;
    public DateTime CompletedOn { get; private set; }
    public ReleaseAttestationSource Source { get; private set; }
    public AttestationActorKind ActorKind { get; private set; }
    public int? ActorUserId { get; private set; }
    public SignerCapacity? SignerCapacity { get; private set; }
    public int? SignatureCompletionId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string? Reason { get; private set; }

    private ReleaseObligationAttestation() { }

    internal static ReleaseObligationAttestation CreateManual(
        string obligationKey,
        DateTime completedOn,
        DateTime agencyToday,
        AttestationActorKind actorKind,
        int actorUserId,
        DateTime recordedAtUtc,
        string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obligationKey);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(actorUserId, 0);
        if (actorKind == AttestationActorKind.System)
            throw new ArgumentException("A manual attestation requires a human actor.",
                nameof(actorKind));
        EnsureValid(completedOn, agencyToday, recordedAtUtc,
            ReleaseAttestationSource.Manual, signatureCompletionId: null);

        return new ReleaseObligationAttestation
        {
            CompletedOn = completedOn.Date,
            Source = ReleaseAttestationSource.Manual,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            RecordedAtUtc = recordedAtUtc,
            Reason = Normalize(reason)
        };
    }

    internal static ReleaseObligationAttestation CreateElectronicSignature(
        string obligationKey,
        DateTime completedOn,
        DateTime agencyToday,
        bool hasGuardian,
        SignerCapacity signerCapacity,
        int signatureCompletionId,
        DateTime recordedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obligationKey);
        if (!ReleaseSigningRules.CanSign(hasGuardian, signerCapacity))
            throw new ArgumentException(
                "The signer capacity does not satisfy the consumer's release signing policy.",
                nameof(signerCapacity));
        EnsureValid(completedOn, agencyToday, recordedAtUtc,
            ReleaseAttestationSource.ElectronicSignature, signatureCompletionId);

        return new ReleaseObligationAttestation
        {
            CompletedOn = completedOn.Date,
            Source = ReleaseAttestationSource.ElectronicSignature,
            ActorKind = AttestationActorKind.System,
            SignerCapacity = signerCapacity,
            SignatureCompletionId = signatureCompletionId,
            RecordedAtUtc = recordedAtUtc
        };
    }

    internal ReleaseAttestationFact ToFact(string obligationKey) =>
        new(obligationKey, CompletedOn, RecordedAtUtc, Source, SignatureCompletionId, Id);

    private static void EnsureValid(
        DateTime completedOn,
        DateTime agencyToday,
        DateTime recordedAtUtc,
        ReleaseAttestationSource source,
        int? signatureCompletionId)
    {
        var errors = ReleaseAttestationRules.Validate(
            completedOn, agencyToday, recordedAtUtc, source, signatureCompletionId);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(completedOn));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum ReleaseAuthorizationEventKind
{
    Withdrawn
}

/// <summary>
/// An append-only prospective authorization change. This is intentionally separate from the
/// attestation collection so withdrawal cannot silently revoke historical completion.
/// </summary>
public sealed class ReleaseAuthorizationEvent
{
    public long Id { get; private set; }
    public long ReleaseObligationId { get; private set; }
    public ReleaseObligation ReleaseObligation { get; private set; } = null!;
    public ReleaseAuthorizationEventKind Kind { get; private set; }
    public DateTime OccurredOn { get; private set; }
    public int ActorUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    private ReleaseAuthorizationEvent() { }

    internal static ReleaseAuthorizationEvent Withdrawn(
        DateTime occurredOn,
        int actorUserId,
        DateTime recordedAtUtc,
        string reason) =>
        new()
        {
            Kind = ReleaseAuthorizationEventKind.Withdrawn,
            OccurredOn = occurredOn.Date,
            ActorUserId = actorUserId,
            RecordedAtUtc = recordedAtUtc,
            Reason = reason.Trim()
        };
}
