using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;

namespace Sati.Data;

public static class FormAttestationChangeReviewPersistenceModel
{
    public static void Configure<TAgency, TPerson, TNote, TForm, TClaimLine>(ModelBuilder modelBuilder)
        where TAgency : class
        where TPerson : class
        where TNote : class
        where TForm : class
        where TClaimLine : class
    {
        modelBuilder.Entity<FormAttestationChangeReviewFlag>(entity =>
        {
            entity.ToTable("FormAttestationChangeReviewFlags");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.FlagId).IsRequired();
            entity.Property(item => item.NoteActivityDate).HasColumnType("date");
            entity.Property(item => item.DueDate).HasColumnType("date");
            entity.Property(item => item.PreviousCompletedOn).HasColumnType("date");
            entity.Property(item => item.RevisedCompletedOn).HasColumnType("date");
            entity.Property(item => item.Reason).IsRequired();
            entity.Property(item => item.BillingHoldReasons).HasConversion<int>();
            entity.Ignore(item => item.MustHoldBilling);
            entity.HasIndex(item => item.FlagId).IsUnique();
            entity.HasIndex(item => new { item.AgencyId, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.AgencyId, item.RequiresSupervisorAttention, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.AgencyId, item.RequiresBillingAttention, item.CreatedAtUtc });
            entity.HasOne<TAgency>().WithMany().HasForeignKey(item => item.AgencyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPerson>().WithMany().HasForeignKey(item => item.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TNote>().WithMany().HasForeignKey(item => item.NoteId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TForm>().WithMany().HasForeignKey(item => item.FormId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReleaseObligation>().WithMany()
                .HasForeignKey(item => item.ReleaseObligationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_FormAttestationChangeReviewFlags_OneSource",
                "([FormId] IS NOT NULL AND [ReleaseObligationId] IS NULL) OR ([FormId] IS NULL AND [ReleaseObligationId] IS NOT NULL)"));
            entity.HasOne<TClaimLine>().WithMany().HasForeignKey(item => item.ClaimLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        if (changeTracker.Entries<FormAttestationChangeReviewFlag>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Form-attestation change review flags are append-only.");
    }
}
