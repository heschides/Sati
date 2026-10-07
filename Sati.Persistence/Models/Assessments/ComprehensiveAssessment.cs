using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sati.Contracts.V1;

namespace Sati.Models.Assessments;

public enum AssessmentStatus { Draft, ReadyForReview, Returned, Approved, Superseded }

public class ComprehensiveAssessment
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int AuthorUserId { get; set; }
    public User AuthorUser { get; set; } = null!;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;
    public int Version { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string DocumentJson { get; set; } = "{}";
    [NotMapped] public SubmitAssessmentRequest? SubmissionRequest { get; set; }
}
