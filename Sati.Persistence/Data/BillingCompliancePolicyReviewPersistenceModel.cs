using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;

namespace Sati.Data;

public static class BillingCompliancePolicyReviewPersistenceModel
{
    public const int RecordKeyMaxLength = 80;

    public static void Configure<TAgency, TPerson, TNote, TClaimLine>(ModelBuilder modelBuilder)
        where TAgency : class
        where TPerson : class
        where TNote : class
        where TClaimLine : class
    {
        modelBuilder.Entity<BillingCompliancePolicyReviewFlag>(entity =>
        {
            entity.ToTable("BillingCompliancePolicyReviewFlags");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.FlagId).IsRequired();
            entity.Property(item => item.RecordKey)
                .IsRequired()
                .HasMaxLength(RecordKeyMaxLength);
            entity.Property(item => item.ServiceDate).HasColumnType("date");
            entity.Property(item => item.ChangeKind).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.PreviousBlockingObligationIdsJson).IsRequired();
            entity.Property(item => item.NewBlockingObligationIdsJson).IsRequired();
            entity.HasIndex(item => item.FlagId).IsUnique();
            entity.HasIndex(item => new { item.PolicyVersionId, item.RecordKey }).IsUnique();
            entity.HasIndex(item => new { item.AgencyId, item.CreatedAtUtc });
            entity.HasOne(item => item.PolicyVersion)
                .WithMany()
                .HasForeignKey(item => item.PolicyVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TAgency>()
                .WithMany()
                .HasForeignKey(item => item.AgencyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPerson>()
                .WithMany()
                .HasForeignKey(item => item.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TNote>()
                .WithMany()
                .HasForeignKey(item => item.NoteId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TClaimLine>()
                .WithMany()
                .HasForeignKey(item => item.ClaimLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        if (changeTracker.Entries<BillingCompliancePolicyReviewFlag>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Billing-compliance policy review flags are append-only.");
        }
    }
}
