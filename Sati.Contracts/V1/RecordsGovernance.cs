namespace Sati.Contracts.V1;

public enum RetentionRecordClass { Clinical = 0, Billing = 1, Audit = 2, Chat = 3, Documents = 4, Evidence = 5 }
public enum PreservationScope { Agency = 0, Person = 1, Record = 2 }
public enum GovernanceHoldAction { Place = 0, Amend = 1, RequestRelease = 2, ApproveRelease = 3, RejectRelease = 4 }
public enum RetentionExecutionMode { PolicyOnly = 0, SyntheticFixture = 1 }
public sealed record GovernanceHoldRequest(Guid OperationId, GovernanceHoldAction Action, Guid? HoldId,
    int ExpectedRevision, PreservationScope Scope, RetentionRecordClass? RecordClass, int? PersonId,
    string? RecordId, string Reason, string? CaseReference = null, string? IssuedBy = null);
public sealed record GovernanceHoldVersionDto(int Revision, GovernanceHoldAction Action, int ActorId,
    DateTime RecordedAtUtc, string Reason, string? CaseReference, string? IssuedBy);
public sealed record GovernanceHoldDto(Guid Id, int Revision, PreservationScope Scope,
    RetentionRecordClass? RecordClass, int? PersonId, string? RecordId, bool IsReleased,
    int PlacedById, int? ReleaseRequestedById, IReadOnlyList<GovernanceHoldVersionDto> History);
public sealed record RetentionPolicyRequest(Guid OperationId, RetentionRecordClass RecordClass,
    int ExpectedVersion, int? RetentionDays, string Reason);
public sealed record RetentionPolicyDto(long Id, int Version, RetentionRecordClass RecordClass,
    int? RetentionDays, int AuthorId, DateTime RecordedAtUtc, string Reason, string EnforcementMode);
public sealed record PreservationRecord(RetentionRecordClass RecordClass, string RecordId, int? PersonId,
    DateTime RecordedAtUtc, string Fingerprint);
public sealed record RetentionDependency(RetentionRecordClass RecordClass, string RecordId);
public sealed record RetentionCandidate(PreservationRecord Record, IReadOnlyList<RetentionDependency> Dependencies);
public sealed record RetentionStoreInventory(bool Available, bool Complete,
    IReadOnlyList<RetentionCandidate> Candidates, IReadOnlyList<string> Blockers);
public sealed record RetentionPreviewDto(Guid Id, long PolicyId, int PolicyVersion, int GovernanceRevision,
    RetentionRecordClass RecordClass, DateTime PreparedAtUtc, DateTime? CutoffUtc,
    int? CandidateCount, int? HeldCount, DateTime? OldestUtc, DateTime? NewestUtc,
    int? ClearCount, int? UnavailableCount, int? DependencyCount,
    IReadOnlyList<string> Blockers, bool CanExecute, string EnforcementMode);
public sealed record PreservationReceipt(Guid PlanId, int GovernanceRevision, string Adapter, string OpaqueReference, string ContentHash,
    DateTime RecordedAtUtc, bool BackupsPreserved, bool EncryptedObjectsPreserved, bool RecoveryVerified);
public sealed record RetentionBatchResult(Guid PlanId, int DeletedCount, int Checkpoint, bool Completed,
    IReadOnlyList<string> Blockers);

public static class RecordsGovernanceRules
{
    public const string ConflictCode = "records_governance_changed";
    public const int MaximumBatch = 50;
    public const int MaximumPreview = 500;
    public static bool CanExecute(RetentionExecutionMode mode, bool verifiedSyntheticFixture) =>
        mode == RetentionExecutionMode.SyntheticFixture && verifiedSyntheticFixture;
    public static void Validate(RetentionPolicyRequest request)
    {
        if (request.OperationId == Guid.Empty || request.ExpectedVersion < 0 || !Enum.IsDefined(request.RecordClass) ||
            request.RetentionDays is < 1 or > 36500 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new ArgumentException("Supply valid policy fields; null duration means indefinite preservation.");
    }
    public static void RequireAdmin(AgencyActor actor)
    {
        if (!UserPermissionRules.HasAdminPermissions(actor.Permissions))
            throw new UnauthorizedAccessException("Agency administration permission is required.");
    }
    public static void Validate(GovernanceHoldRequest request)
    {
        if (request.OperationId == Guid.Empty || !Enum.IsDefined(request.Action) || !Enum.IsDefined(request.Scope) ||
            request.RecordClass is { } recordClass && !Enum.IsDefined(recordClass) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 ||
            request.CaseReference?.Length > 100 || request.IssuedBy?.Length > 150 || request.ExpectedRevision < 0)
            throw new ArgumentException("A valid operation, scope, revision and bounded reason are required.");
        if (request.Action == GovernanceHoldAction.Place && request.HoldId is not null ||
            request.Action != GovernanceHoldAction.Place && request.HoldId is null)
            throw new ArgumentException("The hold identity does not match the operation.");
        if (request.Scope == PreservationScope.Person && request.PersonId is not > 0 ||
            request.Scope == PreservationScope.Agency && request.PersonId is not null ||
            request.Scope != PreservationScope.Record && request.RecordId is not null ||
            request.Scope == PreservationScope.Record && (request.RecordClass is null ||
                string.IsNullOrWhiteSpace(request.RecordId) || request.RecordId.Length > 80))
            throw new ArgumentException("Supply the exact identity required by the hold scope.");
    }
    public static bool Matches(GovernanceHoldDto hold, PreservationRecord record) => !hold.IsReleased &&
        (hold.RecordClass is null || hold.RecordClass == record.RecordClass) && (hold.Scope switch
        {
            PreservationScope.Agency => true,
            PreservationScope.Person => hold.PersonId == record.PersonId,
            PreservationScope.Record => hold.RecordId == record.RecordId,
            _ => true
        });
    public static LegalHoldStatus EvaluatePerson(int personId, bool legacyActive, IReadOnlyList<GovernanceHoldDto> holds)
    {
        var active = holds.Where(x => !x.IsReleased).ToArray();
        if (legacyActive || active.Any(x => x.Scope == PreservationScope.Agency || x.PersonId == personId)) return LegalHoldStatus.Active;
        // Until the dependency adapter can identify a record's person, preservation is conservative.
        return active.Any(x => x.Scope == PreservationScope.Record && x.PersonId is null) ? LegalHoldStatus.Unavailable : LegalHoldStatus.Clear;
    }
    public static void ValidateTransition(GovernanceHoldDto hold, AgencyActor actor, GovernanceHoldRequest request)
    {
        RequireAdmin(actor);
        if (hold.Revision != request.ExpectedRevision || hold.IsReleased)
            throw new InvalidOperationException(ConflictCode);
        if (request.Action == GovernanceHoldAction.RequestRelease && hold.ReleaseRequestedById is not null ||
            request.Action is GovernanceHoldAction.ApproveRelease or GovernanceHoldAction.RejectRelease && hold.ReleaseRequestedById is null)
            throw new InvalidOperationException("No matching pending release request is available.");
        if (request.Action is GovernanceHoldAction.ApproveRelease or GovernanceHoldAction.RejectRelease &&
            (actor.UserId == hold.ReleaseRequestedById || actor.UserId == hold.PlacedById))
            throw new UnauthorizedAccessException("A different administrator must independently decide the release.");
        if (request.Scope != hold.Scope || request.RecordClass != hold.RecordClass ||
            request.PersonId != hold.PersonId || request.RecordId != hold.RecordId)
            throw new ArgumentException("Hold scope is immutable; place an additional hold to broaden preservation.");
    }
    // Preservation covers the entire connected component, including reverse references.
    // Cycles are traversed once; a missing dependency is never treated as clear.
    public static LegalHoldStatus Evaluate(RetentionCandidate candidate,
        IReadOnlyDictionary<(RetentionRecordClass, string), RetentionCandidate> records,
        IReadOnlyList<GovernanceHoldDto> holds, IReadOnlySet<int> legacyHeldPeople)
    {
        var pending = new Stack<RetentionCandidate>(); pending.Push(candidate);
        var visited = new HashSet<(RetentionRecordClass, string)>();
        while (pending.TryPop(out var current))
        {
            if (!visited.Add((current.Record.RecordClass, current.Record.RecordId))) continue;
            if (current.Record.PersonId is int personId && legacyHeldPeople.Contains(personId) ||
                holds.Any(hold => Matches(hold, current.Record))) return LegalHoldStatus.Active;
            foreach (var dependency in current.Dependencies)
            {
                if (!records.TryGetValue((dependency.RecordClass, dependency.RecordId), out var linked))
                    return LegalHoldStatus.Unavailable;
                pending.Push(linked);
            }
            foreach (var dependent in records.Values.Where(x => x.Dependencies.Any(d =>
                d.RecordClass == current.Record.RecordClass && d.RecordId == current.Record.RecordId)))
                pending.Push(dependent);
        }
        return LegalHoldStatus.Clear;
    }
    public static bool ValidReceipt(PreservationReceipt receipt, Guid planId, int governanceRevision) =>
        receipt.PlanId == planId && receipt.GovernanceRevision == governanceRevision &&
        receipt.BackupsPreserved && receipt.EncryptedObjectsPreserved && receipt.RecoveryVerified &&
        receipt.RecordedAtUtc.Kind == DateTimeKind.Utc &&
        !string.IsNullOrWhiteSpace(receipt.Adapter) && receipt.Adapter.Length <= 80 &&
        Guid.TryParse(receipt.OpaqueReference, out var reference) && reference != Guid.Empty && receipt.ContentHash.Length == 64 &&
        receipt.ContentHash.All(Uri.IsHexDigit);
}
