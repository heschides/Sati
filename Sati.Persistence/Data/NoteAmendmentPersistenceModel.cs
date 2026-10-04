using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;
using Sati.Contracts.V1;

namespace Sati.Data;

public static class NoteAmendmentPersistenceModel
{
    public static void ConfigureClaimLine<TLine>(ModelBuilder model) where TLine : class => model.Entity<TLine>()
        .HasOne<NoteAmendmentVersion>().WithMany().HasForeignKey("AmendedNoteVersionId").OnDelete(DeleteBehavior.Restrict);
    public static void ProtectOriginalNotes<TNote>(DbContext db) where TNote : class
    {
        var ids = db.ChangeTracker.Entries<TNote>().Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .Select(e => (int)e.Property("Id").CurrentValue!).ToArray();
        if (ids.Length > 0 && db.Set<NoteAmendment>().AsNoTracking().Any(a => ids.Contains(a.NoteId)))
            throw new NoteAmendmentWorkflowException(409, NoteAmendmentRules.RevisionCode, "A note with linked amendment history cannot be overwritten or deleted. Create a further amendment.");
    }
    public static void Configure<TNote>(ModelBuilder model) where TNote : class
    {
        model.Entity<NoteAmendmentFinancialReview>(e =>
        {
            e.ToTable("NoteAmendmentFinancialReviews"); e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(NoteAmendmentRules.ReasonLimit);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => x.ApprovedVersionId).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.ReviewedById, x.OperationId }).IsUnique();
            e.HasOne<NoteAmendmentVersion>().WithMany().HasForeignKey(x => x.ApprovedVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NoteAmendment>(e =>
        {
            e.ToTable("NoteAmendments"); e.HasKey(x => x.Id);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.AgencyId, x.NoteId });
            e.HasIndex(x => x.NoteId).IsUnique().HasFilter("[Status] IN (0, 1, 2)");
            e.HasOne<TNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NoteAmendmentVersion>(e =>
        {
            e.ToTable("NoteAmendmentVersions"); e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.FinancialContentJson).HasMaxLength(1000);
            e.Property(x => x.Reason).HasMaxLength(NoteAmendmentRules.ReasonLimit);
            e.HasIndex(x => new { x.AmendmentId, x.Number }).IsUnique();
            e.HasOne<NoteAmendment>().WithMany().HasForeignKey(x => x.AmendmentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NoteAmendmentEvent>(e =>
        {
            e.ToTable("NoteAmendmentEvents"); e.HasKey(x => x.Id);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.ReviewReason).HasMaxLength(NoteAmendmentRules.ReasonLimit);
            e.HasIndex(x => new { x.AgencyId, x.ActorId, x.OperationId }).IsUnique();
            e.HasOne<NoteAmendment>().WithMany().HasForeignKey(x => x.AmendmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<NoteAmendmentVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
    public static void ProtectWrites(ChangeTracker tracker)
    {
        if (tracker.Entries<NoteAmendmentFinancialReview>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Financial amendment reviews are append-only.");
        if (tracker.Entries<NoteAmendmentVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            tracker.Entries<NoteAmendmentEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Amendment versions and decisions are append-only.");
        foreach (var e in tracker.Entries<NoteAmendment>())
        {
            if (e.State == EntityState.Deleted || e.State == EntityState.Modified &&
                (e.OriginalValues.GetValue<NoteAmendmentStatus>(nameof(NoteAmendment.Status)) is NoteAmendmentStatus.Approved or NoteAmendmentStatus.Rejected ||
                 new[] { nameof(NoteAmendment.NoteId), nameof(NoteAmendment.AgencyId), nameof(NoteAmendment.AuthorId),
                     nameof(NoteAmendment.OriginalNoteRevision), nameof(NoteAmendment.OriginalSnapshotJson), nameof(NoteAmendment.BaseApprovedVersionId) }
                     .Any(name => e.Property(name).IsModified)))
                throw new InvalidOperationException("The original amendment identity and finalized amendments are immutable.");
        }
    }
}
