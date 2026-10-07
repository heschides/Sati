using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models.Assessments;

namespace Sati.Data;

public static class AssessmentReviewPersistenceModel
{
    public static void Configure<TAssessment, TPerson, TForm, TAgency, TUser, TArtifact>(ModelBuilder model)
        where TAssessment : class where TPerson : class where TForm : class where TAgency : class
        where TUser : class where TArtifact : class
    {
        model.Entity<AssessmentSubmission>(e =>
        {
            e.ToTable("AssessmentSubmissions"); e.HasKey(x => x.Id);
            e.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.ConsumerName).HasMaxLength(300).IsRequired();
            e.Property(x => x.DocumentJson).IsRequired();
            e.Property(x => x.TargetEffectiveDate).HasColumnType("date");
            e.Property(x => x.DueDate).HasColumnType("date");
            e.HasIndex(x => new { x.AssessmentId, x.CycleNumber }).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.SubmittedAtUtc });
            e.HasOne<TAssessment>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TPerson>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TForm>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AssessmentReviewEvent>(e =>
        {
            e.ToTable("AssessmentReviewEvents"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.Property(x => x.Location).HasMaxLength(100).IsRequired();
            e.Property(x => x.Text).HasMaxLength(4000).IsRequired();
            e.Property(x => x.CompletedOn).HasColumnType("date");
            e.HasIndex(x => new { x.AssessmentId, x.AssessmentRevision }).IsUnique();
            e.HasOne<AssessmentSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TAssessment>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssessmentReviewEvent>().WithMany().HasForeignKey(x => x.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites<TAssessment>(ChangeTracker tracker) where TAssessment : class
    {
        if (tracker.Entries<AssessmentSubmission>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            tracker.Entries<AssessmentReviewEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Assessment submissions and review history are append-only.");
        foreach (var entry in tracker.Entries<TAssessment>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            var status = entry.Property("Status").OriginalValue?.ToString();
            if (status == "Approved" && (entry.State == EntityState.Deleted || entry.Properties.Any(p => p.IsModified)) ||
                status == "ReadyForReview" && (entry.State == EntityState.Deleted || entry.Property("DocumentJson").IsModified))
                throw new InvalidOperationException("Submitted answers and approved assessment versions are immutable.");
        }
    }
}
