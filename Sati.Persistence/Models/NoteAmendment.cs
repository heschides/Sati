using Sati.Contracts.V1;

namespace Sati.Models;

public sealed class NoteAmendment
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public int NoteId { get; set; }
    public int AuthorId { get; set; }
    public int OriginalNoteRevision { get; set; }
    public string OriginalSnapshotJson { get; set; } = string.Empty;
    public long? BaseApprovedVersionId { get; set; }
    public int Revision { get; set; } = 1;
    public NoteAmendmentStatus Status { get; set; }
    public long CurrentVersionId { get; set; }
    public long? SubmittedVersionId { get; set; }
    public long? ApprovedVersionId { get; set; }
    public bool ChangesFinancialFacts { get; set; }
}

public sealed class NoteAmendmentVersion
{
    public long Id { get; set; }
    public Guid AmendmentId { get; set; }
    public int Number { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ContentJson { get; set; } = string.Empty;
    public string FinancialContentJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int RecordedById { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class NoteAmendmentEvent
{
    public long Id { get; set; }
    public Guid AmendmentId { get; set; }
    public int AgencyId { get; set; }
    public int ActorId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public NoteAmendmentAction Action { get; set; }
    public long VersionId { get; set; }
    public string ResponseJson { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class NoteAmendmentFinancialReview
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int NoteId { get; set; }
    public long ApprovedVersionId { get; set; }
    public int ReviewedById { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
}
