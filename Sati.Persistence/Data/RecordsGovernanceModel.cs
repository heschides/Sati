using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;
namespace Sati.Data;

public static class RecordsGovernanceModel
{
    public static void Configure<TAgency, TUser>(ModelBuilder model) where TAgency : class where TUser : class
    {
        model.Entity<RecordsGovernanceState>(entity =>
        {
            entity.ToTable("RecordsGovernanceStates"); entity.HasKey(x => x.AgencyId);
            entity.Property(x => x.AgencyId).ValueGeneratedNever();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecordsHold>(entity =>
        {
            entity.ToTable("RecordsHolds"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.RecordId).HasMaxLength(80);
            entity.HasIndex(x => new { x.AgencyId, x.IsReleased });
            entity.HasIndex(x => new { x.AgencyId, x.LegacyHoldId }).IsUnique().HasFilter("[LegacyHoldId] IS NOT NULL");
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.PlacedById).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecordsHoldEvent>(entity =>
        {
            entity.ToTable("RecordsHoldEvents"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500); entity.Property(x => x.CaseReference).HasMaxLength(100);
            entity.Property(x => x.IssuedBy).HasMaxLength(150); entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.AgencyId, x.OperationId }).IsUnique();
            entity.HasIndex(x => new { x.HoldId, x.Revision }).IsUnique();
            entity.HasOne<RecordsHold>().WithMany().HasForeignKey(x => x.HoldId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecordsRetentionPolicy>(entity =>
        {
            entity.ToTable("RecordsRetentionPolicies"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500); entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.AgencyId, x.OperationId }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.RecordClass, x.Version }).IsUnique();
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecordsRetentionPlan>(entity =>
        {
            entity.ToTable("RecordsRetentionPlans"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasOne<RecordsRetentionPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecordsRetentionBatch>(entity =>
        {
            entity.ToTable("RecordsRetentionBatches"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AgencyId, x.OperationId }).IsUnique();
            entity.HasIndex(x => new { x.PlanId, x.Checkpoint }).IsUnique();
            entity.HasOne<RecordsRetentionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
    public static void Validate(ChangeTracker tracker)
    {
        foreach (var entry in tracker.Entries().Where(x => x.Entity is RecordsHoldEvent or RecordsRetentionPolicy or RecordsRetentionBatch))
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Governance decision, policy and execution history is append-only.");
        if (tracker.Entries().Any(x => x.Entity is RecordsHold or RecordsRetentionPlan or RecordsGovernanceState && x.State == EntityState.Deleted))
            throw new InvalidOperationException("Governance aggregates and checkpoints cannot be deleted.");
    }
}
