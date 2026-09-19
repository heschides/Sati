using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Data;

/// <summary>
/// Keeps the API and local EF models aligned for bank deposit entries, per-claim 277CA
/// verdicts, and claim corrections. All four are append-only financial evidence.
/// </summary>
public static class BillingCorrectionPersistenceModel
{
    public static void Configure<TAgency, TUser, TPeriod, TGeneration, TClaimLine, TDeposit>(ModelBuilder model)
        where TAgency : class where TUser : class where TPeriod : class
        where TGeneration : class where TClaimLine : class where TDeposit : class
    {
        model.Entity<EftDepositRecord>(entity =>
        {
            entity.ToTable("EftDepositRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.DepositDate).HasColumnType("date");
            entity.Property(x => x.BankTraceNumber).HasMaxLength(EftDepositRules.BankTraceMaxLength);
            entity.Property(x => x.Note).HasMaxLength(EftDepositRules.NoteMaxLength);
            entity.HasIndex(x => new { x.AgencyId, x.RemittanceDepositId });
            // One correction per entry: two people correcting the same figure cannot both win.
            entity.HasIndex(x => x.SupersedesRecordId).IsUnique().HasFilter("[SupersedesRecordId] IS NOT NULL");
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TDeposit>().WithMany().HasForeignKey(x => x.RemittanceDepositId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EftDepositRecord>().WithMany().HasForeignKey(x => x.SupersedesRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClaimAcknowledgementOutcome>(entity =>
        {
            entity.ToTable("ClaimAcknowledgementOutcomes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClaimReference).IsRequired().HasMaxLength(80);
            entity.Property(x => x.CategoryCode).IsRequired().HasMaxLength(10);
            entity.Property(x => x.StatusCode).IsRequired().HasMaxLength(10);
            entity.HasIndex(x => new { x.AgencyId, x.EdiGenerationId, x.ClaimReference });
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPeriod>().WithMany().HasForeignKey(x => x.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TGeneration>().WithMany().HasForeignKey(x => x.EdiGenerationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClearinghouseResponseReceipt>().WithMany().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClaimCorrection>(entity =>
        {
            entity.ToTable("ClaimCorrections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PayerClaimControlNumber).HasMaxLength(ClaimCorrectionRules.PayerClaimControlNumberMaxLength);
            entity.Property(x => x.ClaimSnapshotJson).IsRequired();
            entity.Property(x => x.ClientMaineCareId).IsRequired().HasMaxLength(80);
            entity.Property(x => x.RenderingProviderNpi).IsRequired().HasMaxLength(10);
            entity.Property(x => x.DiagnosisCode).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Reason).IsRequired().HasMaxLength(ClaimCorrectionRules.ReasonMaxLength);
            entity.HasIndex(x => new { x.AgencyId, x.BillingPeriodId });
            entity.HasIndex(x => x.ClaimLineId);
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPeriod>().WithMany().HasForeignKey(x => x.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TClaimLine>().WithMany().HasForeignKey(x => x.ClaimLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TGeneration>().WithMany().HasForeignKey(x => x.CorrectsEdiGenerationId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClaimCorrectionSubmission>(entity =>
        {
            entity.ToTable("ClaimCorrectionSubmissions");
            entity.HasKey(x => new { x.ClaimCorrectionId, x.EdiGenerationId });
            entity.HasOne<ClaimCorrection>().WithMany().HasForeignKey(x => x.ClaimCorrectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TGeneration>().WithMany().HasForeignKey(x => x.EdiGenerationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites(ChangeTracker tracker)
    {
        if (tracker.Entries<EftDepositRecord>().Any(IsChangedOrRemoved) ||
            tracker.Entries<ClaimAcknowledgementOutcome>().Any(IsChangedOrRemoved) ||
            tracker.Entries<ClaimCorrection>().Any(IsChangedOrRemoved) ||
            tracker.Entries<ClaimCorrectionSubmission>().Any(IsChangedOrRemoved))
            throw new InvalidOperationException(
                "Bank deposit entries, claim acknowledgements, and claim corrections are append-only.");
    }

    private static bool IsChangedOrRemoved(EntityEntry entry) =>
        entry.State is EntityState.Modified or EntityState.Deleted;
}
