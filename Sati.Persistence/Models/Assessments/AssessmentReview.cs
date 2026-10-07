namespace Sati.Models.Assessments;

/// <summary>Append-only clinical snapshot; the mutable authoring row is never review evidence.</summary>
public sealed class AssessmentSubmission
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int AssessmentId { get; set; }
    public int PersonId { get; set; }
    public int AuthorUserId { get; set; }
    public int AssessmentVersion { get; set; }
    public int CycleNumber { get; set; }
    public int DocumentRevision { get; set; }
    public int FormId { get; set; }
    public DateTime TargetEffectiveDate { get; set; }
    public DateTime DueDate { get; set; }
    public int RulesVersion { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string DocumentJson { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
}

/// <summary>Clinical comments and decisions stay here; the operational audit receives IDs/hashes only.</summary>
public sealed class AssessmentReviewEvent
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int AssessmentId { get; set; }
    public int SubmissionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Location { get; set; } = "document";
    public string Text { get; set; } = string.Empty;
    public bool Blocking { get; set; }
    public long? FlagId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int AssessmentRevision { get; set; }
    public int? ArtifactId { get; set; }
    public DateTime? CompletedOn { get; set; }
}
