using Sati.Contracts.V1;
namespace Sati.Models;

public sealed class RecordsGovernanceState
{
    public int AgencyId { get; set; }
    public int Revision { get; set; }
}
public sealed class RecordsHold
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public int Revision { get; set; }
    public PreservationScope Scope { get; set; }
    public RetentionRecordClass? RecordClass { get; set; }
    public int? PersonId { get; set; }
    public string? RecordId { get; set; }
    public bool IsReleased { get; set; }
    public int PlacedById { get; set; }
    public int? ReleaseRequestedById { get; set; }
    public Guid? ReleaseRequestId { get; set; }
    public int? LegacyHoldId { get; set; }
}
public sealed class RecordsHoldEvent
{
    public long Id { get; set; }
    public Guid HoldId { get; set; }
    public int AgencyId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public int Revision { get; set; }
    public GovernanceHoldAction Action { get; set; }
    public int ActorId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public string? CaseReference { get; set; }
    public string? IssuedBy { get; set; }
}
public sealed class RecordsRetentionPolicy
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public int Version { get; set; }
    public RetentionRecordClass RecordClass { get; set; }
    public int? RetentionDays { get; set; }
    public int AuthorId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class RecordsRetentionPlan
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public long PolicyId { get; set; }
    public int GovernanceRevision { get; set; }
    public DateTime PreparedAtUtc { get; set; }
    public string PreviewJson { get; set; } = "";
    public string CandidatesJson { get; set; } = "";
    public int Checkpoint { get; set; }
    public int Revision { get; set; }
    public bool Completed { get; set; }
}
public sealed class RecordsRetentionBatch
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public Guid PlanId { get; set; }
    public Guid OperationId { get; set; }
    public int ActorId { get; set; }
    public int Checkpoint { get; set; }
    public int DeletedCount { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string PreservationJson { get; set; } = "";
    public bool Completed { get; set; }
}
