using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Keeps the API and local EF models aligned for immutable, append-only billing
/// compliance recovery decisions.
/// </summary>
public static class BillingComplianceRecoveryPersistenceModel
{
    public const int ExplanationMaxLength = 4_000;
    public const int ObligationIdMaxLength = 500;
    public const int ObligationNameMaxLength = 200;
    public const int EvidenceIdMaxLength = 500;

    public static void Configure<TAgency, TUser, TPerson, TNote>(ModelBuilder modelBuilder)
        where TAgency : class
        where TUser : class
        where TPerson : class
        where TNote : class
    {
        modelBuilder.Entity<BillingComplianceRecoveryDecision>(entity =>
        {
            entity.ToTable("BillingComplianceRecoveryDecisions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DecisionId).IsRequired();
            entity.Property(item => item.Explanation)
                .IsRequired()
                .HasMaxLength(ExplanationMaxLength);
            entity.HasIndex(item => item.DecisionId).IsUnique();
            entity.HasIndex(item => new { item.AgencyId, item.PersonId, item.RecordedAtUtc });
            entity.HasOne<TAgency>()
                .WithMany()
                .HasForeignKey(item => item.AgencyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>()
                .WithMany()
                .HasForeignKey(item => item.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPerson>()
                .WithMany()
                .HasForeignKey(item => item.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(item => item.Obligations)
                .WithOne()
                .HasForeignKey(item => item.BillingComplianceRecoveryDecisionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(item => item.Notes)
                .WithOne()
                .HasForeignKey(item => item.BillingComplianceRecoveryDecisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillingComplianceRecoveryObligation>(entity =>
        {
            entity.ToTable("BillingComplianceRecoveryObligations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ObligationId)
                .IsRequired()
                .HasMaxLength(ObligationIdMaxLength);
            entity.Property(item => item.Name)
                .IsRequired()
                .HasMaxLength(ObligationNameMaxLength);
            entity.Property(item => item.DueDate).HasColumnType("date");
            entity.Property(item => item.CompletedDate).HasColumnType("date");
            entity.Property(item => item.EvidenceId)
                .IsRequired()
                .HasMaxLength(EvidenceIdMaxLength);
            entity.HasIndex(item => new
            {
                item.BillingComplianceRecoveryDecisionId,
                item.ObligationId
            }).IsUnique();
        });

        modelBuilder.Entity<BillingComplianceRecoveryNote>(entity =>
        {
            entity.ToTable("BillingComplianceRecoveryNotes");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new
            {
                item.BillingComplianceRecoveryDecisionId,
                item.NoteId
            }).IsUnique();
            // A later effective-dated policy can add a blocker that an older
            // decision did not cover. Keep every decision immutable and allow a
            // later, separately attested decision for the same note.
            entity.HasIndex(item => item.NoteId);
            entity.HasOne<TNote>()
                .WithMany()
                .HasForeignKey(item => item.NoteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        if (changeTracker.Entries<BillingComplianceRecoveryDecision>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            changeTracker.Entries<BillingComplianceRecoveryObligation>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            changeTracker.Entries<BillingComplianceRecoveryNote>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Billing compliance recovery decisions are append-only.");
        }
    }
}
