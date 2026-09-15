using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models.Billing;

namespace Sati.Data;

public static class ClearinghousePersistenceModel
{
    public static void Configure<TAgency, TUser, TPeriod, TGeneration>(ModelBuilder model)
        where TAgency : class where TUser : class where TPeriod : class where TGeneration : class
    {
        model.Entity<ClearinghouseResponseReceipt>(entity =>
        {
            entity.ToTable("ClearinghouseResponseReceipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ParserVersion).HasMaxLength(40);
            entity.Property(x => x.RawSha256).HasMaxLength(64);
            entity.Property(x => x.SemanticSha256).HasMaxLength(64);
            entity.Property(x => x.IdentitySha256).HasMaxLength(64);
            entity.Property(x => x.PaymentIdentitySha256).HasMaxLength(64);
            entity.Property(x => x.KeyId).HasMaxLength(500);
            entity.Property(x => x.Nonce).HasMaxLength(12);
            entity.Property(x => x.Tag).HasMaxLength(16);
            entity.HasIndex(x => new { x.AgencyId, x.RawSha256 }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.IsTest, x.SemanticSha256 }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.IsTest, x.IdentitySha256 }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.IsTest, x.PaymentIdentitySha256 }).IsUnique().HasFilter("[PaymentIdentitySha256] IS NOT NULL");
            entity.HasIndex(x => new { x.AgencyId, x.ReceivedAtUtc });
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Matches).WithOne().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClearinghouseResponseMatch>(entity =>
        {
            entity.ToTable("ClearinghouseResponseMatches");
            entity.HasKey(x => new { x.ResponseId, x.EdiGenerationId, x.ClaimReference });
            entity.Property(x => x.ClaimReference).HasMaxLength(80);
            entity.HasOne<TGeneration>().WithMany().HasForeignKey(x => x.EdiGenerationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPeriod>().WithMany().HasForeignKey(x => x.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ProtectWrites(ChangeTracker tracker)
    {
        if (tracker.Entries<ClearinghouseResponseReceipt>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            tracker.Entries<ClearinghouseResponseMatch>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Clearinghouse response evidence is immutable.");
        var newReceiptIds = tracker.Entries<ClearinghouseResponseReceipt>()
            .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id).ToHashSet();
        if (tracker.Entries<ClearinghouseResponseMatch>().Any(entry => entry.State == EntityState.Added && !newReceiptIds.Contains(entry.Entity.ResponseId)))
            throw new InvalidOperationException("A retained clearinghouse receipt cannot acquire new matches.");
    }
}
