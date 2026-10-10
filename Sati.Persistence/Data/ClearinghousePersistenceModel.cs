using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Data;

public static class ClearinghousePersistenceModel
{
    public static void Configure<TAgency, TUser, TPeriod, TGeneration>(ModelBuilder model)
        where TAgency : class where TUser : class where TPeriod : class where TGeneration : class
    {
        model.Entity<TGeneration>().HasAlternateKey("AgencyId", "Id");

        model.Entity<ClearinghouseDispatchRotation>(entity =>
        {
            entity.ToTable("ClearinghouseDispatchRotation", table => table.HasCheckConstraint(
                "CK_ClearinghouseDispatchRotation_Scope", "[Id] = 1 AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasData(new ClearinghouseDispatchRotation());
        });
        model.Entity<ClearinghouseAgencyDispatchRotation>(entity =>
        {
            entity.ToTable("ClearinghouseAgencyDispatchRotation", table => table.HasCheckConstraint(
                "CK_ClearinghouseAgencyDispatchRotation_Scope", "[AgencyId] > 0 AND [Revision] > 0"));
            entity.HasKey(x => x.AgencyId);
            entity.Property(x => x.AgencyId).ValueGeneratedNever();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseAccount>(entity =>
        {
            entity.ToTable("ClearinghouseAccounts", table => table.HasCheckConstraint(
                "CK_ClearinghouseAccounts_ClaimMdProfile",
                "[ConnectorKind] <> 2 OR ([ClaimNamespace] IS NOT NULL AND [TradingPartnerProfileVersion] > 0)"));
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.AgencyId, x.Id });
            entity.Property(x => x.ExternalAccountNumber).HasMaxLength(80);
            entity.Property(x => x.ClaimNamespace).HasMaxLength(8);
            entity.Property(x => x.SecretReference).HasMaxLength(500);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.AgencyId, x.ConnectorKind, x.IsTest }).IsUnique()
                .HasFilter("[IsEnabled] = 1");
            entity.HasIndex(x => x.ClaimNamespace).IsUnique().HasFilter("[ClaimNamespace] IS NOT NULL");
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseDispatchReadiness>(entity =>
        {
            entity.ToTable("ClearinghouseDispatchReadiness", table => table.HasCheckConstraint(
                "CK_ClearinghouseDispatchReadiness_State",
                "[AgencyId] > 0 AND [Revision] > 0 AND [ValidatedAccountRevision] >= 0 AND (" +
                "([Disposition] = 1 AND [FailureCount] = 0 AND [RecoveryCycleId] = '00000000-0000-0000-0000-000000000000' " +
                "AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NULL AND [SafeFailureCode] IS NULL) OR " +
                "([Disposition] = 2 AND [FailureCount] BETWEEN 1 AND 4 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [NextEligibleAtUtc] IS NOT NULL AND [LastFailureAtUtc] IS NOT NULL AND [NextEligibleAtUtc] > [LastFailureAtUtc] " +
                "AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable') OR " +
                "([Disposition] = 3 AND [FailureCount] = 5 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NOT NULL " +
                "AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable'))"));
            entity.HasKey(x => new { x.AgencyId, x.AccountId });
            entity.Property(x => x.SafeFailureCode).HasMaxLength(40);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Disposition, x.NextEligibleAtUtc });
            entity.HasOne<ClearinghouseAccount>().WithMany()
                .HasForeignKey(x => new { x.AgencyId, x.AccountId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseDispatch>(entity =>
        {
            entity.ToTable("ClearinghouseDispatches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalFileId).HasMaxLength(128);
            entity.Property(x => x.SafeErrorCode).HasMaxLength(80);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => x.EdiGenerationId).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.State, x.RequestedAtUtc });
            // Preserve the unfiltered FK-supporting index when adding the filtered lane index.
            entity.HasIndex(x => new { x.AgencyId, x.AccountId });
            entity.HasIndex(x => new { x.State, x.RequestedAtUtc, x.Id })
                .IncludeProperties(x => new { x.AgencyId, x.AccountId });
            entity.HasIndex(x => new { x.AgencyId, x.AccountId, x.RequestedAtUtc, x.Id })
                .HasDatabaseName("IX_ClearinghouseDispatches_QueuedLane").HasFilter("[State] = 1");
            entity.HasOne<ClearinghouseAccount>().WithMany()
                .HasForeignKey(x => new { x.AgencyId, x.AccountId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TGeneration>().WithMany()
                .HasForeignKey("AgencyId", "EdiGenerationId")
                .HasPrincipalKey("AgencyId", "Id").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany()
                .HasForeignKey(x => x.RequestingUserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseDispatchAttempt>(entity =>
        {
            entity.ToTable("ClearinghouseDispatchAttempts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ContentSha256).HasMaxLength(64);
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.VendorCode).HasMaxLength(40);
            entity.Property(x => x.CorrelationId).HasMaxLength(80);
            entity.Property(x => x.ResponseSha256).HasMaxLength(64);
            entity.Property(x => x.ResponseNonce).HasMaxLength(12);
            entity.Property(x => x.ResponseTag).HasMaxLength(16);
            entity.Property(x => x.ResponseKeyId).HasMaxLength(500);
            entity.HasIndex(x => new { x.DispatchId, x.AttemptNumber }).IsUnique();
            entity.HasOne<ClearinghouseDispatch>().WithMany().HasForeignKey(x => x.DispatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseFeedCheckpoint>(entity =>
        {
            entity.ToTable("ClearinghouseFeedCheckpoints");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Cursor).HasMaxLength(256);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.AccountId, x.FeedKind }).IsUnique();
            entity.HasOne<ClearinghouseAccount>().WithMany()
                .HasForeignKey(x => new { x.AgencyId, x.AccountId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClearinghouseResponseReceipt>().WithMany()
                .HasForeignKey(x => new { x.AgencyId, x.LastReceiptId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClearinghouseResponseReceipt>(entity =>
        {
            entity.ToTable("ClearinghouseResponseReceipts");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.AgencyId, x.Id });
            entity.Property(x => x.ExternalArtifactId).HasMaxLength(128);
            entity.Property(x => x.ContentType).HasMaxLength(80);
            entity.Property(x => x.ConnectorVersion).HasMaxLength(40);
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
            entity.HasIndex(x => new { x.AccountId, x.FeedKind, x.ExternalArtifactId }).IsUnique()
                .HasFilter("[AccountId] IS NOT NULL AND [FeedKind] IS NOT NULL AND [ExternalArtifactId] IS NOT NULL");
            entity.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClearinghouseAccount>().WithMany()
                .HasForeignKey(x => new { x.AgencyId, x.AccountId })
                .HasPrincipalKey(x => new { x.AgencyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
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
        foreach (var entry in tracker.Entries<ClearinghouseDispatchRotation>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Dispatch rotation cannot be deleted to reset scheduling position.");
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var row = entry.Entity;
            if (row.Id != ClearinghouseDispatchRotation.SingletonId || row.LastAgencyId is <= 0 ||
                entry.State == EntityState.Added && row.Revision != 1 ||
                entry.State == EntityState.Modified && (entry.Property(x => x.Id).IsModified ||
                    row.Revision != checked(entry.OriginalValues.GetValue<long>(nameof(row.Revision)) + 1)))
                throw new InvalidOperationException("Dispatch rotation requires its fixed scope and a new revision.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseAgencyDispatchRotation>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Agency dispatch rotation cannot be deleted to reset scheduling position.");
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var row = entry.Entity;
            if (row.AgencyId <= 0 || entry.State == EntityState.Added && row.Revision != 1 ||
                entry.State == EntityState.Modified && (entry.Property(x => x.AgencyId).IsModified ||
                    row.Revision != checked(entry.OriginalValues.GetValue<long>(nameof(row.Revision)) + 1)))
                throw new InvalidOperationException("Agency dispatch rotation requires immutable scope and a new revision.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseDispatchReadiness>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Account preflight recovery cannot be deleted to bypass a hold.");
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var row = entry.Entity;
            ClearinghousePreflightRules.Validate(row.Snapshot());
            if (row.AgencyId <= 0 || row.AccountId == Guid.Empty || row.ValidatedAccountRevision < 0 ||
                entry.State == EntityState.Added && row.Revision != 1 ||
                entry.State == EntityState.Modified && (entry.Property(x => x.AgencyId).IsModified ||
                    entry.Property(x => x.AccountId).IsModified || row.Revision !=
                    entry.OriginalValues.GetValue<long>(nameof(ClearinghouseDispatchReadiness.Revision)) + 1))
                throw new InvalidOperationException("Account preflight updates require immutable scope and a new revision.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseAccount>().Where(x => x.State == EntityState.Added))
        {
            var account = entry.Entity;
            if (account.TradingPartnerProfileVersion != TradingPartnerProfile.CurrentVersion)
                throw new InvalidOperationException("Unsupported clearinghouse trading-partner profile version.");
            if (account.ConnectorKind == TradingPartnerKind.ClaimMd)
                _ = TradingPartnerProfile.ClaimMd(account.ExternalAccountNumber, account.ClaimNamespace ?? string.Empty);
            else if (account.ConnectorKind != TradingPartnerKind.OfficeAlly || account.ClaimNamespace is not null)
                throw new InvalidOperationException("Unsupported clearinghouse account profile.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseResponseReceipt>().Where(x => x.State == EntityState.Added))
        {
            var receipt = entry.Entity;
            if (receipt.Source == ClearinghouseReceiptSource.Connector)
            {
                if (receipt.AccountId is null || receipt.ConnectorKind is null || receipt.FeedKind is null ||
                    string.IsNullOrWhiteSpace(receipt.ExternalArtifactId) ||
                    string.IsNullOrWhiteSpace(receipt.ContentType) ||
                    string.IsNullOrWhiteSpace(receipt.ConnectorVersion))
                    throw new InvalidOperationException("Automated clearinghouse receipts require account and artifact provenance.");
            }
            else if (receipt.Source is ClearinghouseReceiptSource.Manual or ClearinghouseReceiptSource.Mock)
            {
                if (receipt.ActorUserId is null || receipt.AccountId is not null ||
                    receipt.ExternalArtifactId is not null)
                    throw new InvalidOperationException("Manual and mock receipts require a human actor and cannot claim connector provenance.");
            }
            else
                throw new InvalidOperationException("Unsupported clearinghouse receipt source.");
        }
        if (tracker.Entries<ClearinghouseDispatchAttempt>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Clearinghouse upload attempts are immutable.");
        if (tracker.Entries<ClearinghouseAccount>().Any(x => x.State == EntityState.Deleted) ||
            tracker.Entries<ClearinghouseDispatch>().Any(x => x.State == EntityState.Deleted) ||
            tracker.Entries<ClearinghouseFeedCheckpoint>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("Clearinghouse routing and dispatch history cannot be deleted.");
        foreach (var entry in tracker.Entries<ClearinghouseAccount>().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Property(x => x.AgencyId).IsModified || entry.Property(x => x.ConnectorKind).IsModified ||
                entry.Property(x => x.IsTest).IsModified || entry.Property(x => x.ClaimNamespace).IsModified ||
                entry.Property(x => x.ExternalAccountNumber).IsModified ||
                entry.Entity.Revision != entry.OriginalValues.GetValue<long>(nameof(ClearinghouseAccount.Revision)) + 1)
                throw new InvalidOperationException("Clearinghouse account identity is fixed and updates require a new revision.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseDispatch>().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Property(x => x.AgencyId).IsModified || entry.Property(x => x.AccountId).IsModified ||
                entry.Property(x => x.EdiGenerationId).IsModified || entry.Property(x => x.RequestingUserId).IsModified ||
                entry.Property(x => x.TradingPartnerProfileVersion).IsModified ||
                entry.Entity.Revision != entry.OriginalValues.GetValue<long>(nameof(ClearinghouseDispatch.Revision)) + 1)
                throw new InvalidOperationException("A clearinghouse dispatch cannot change its source or account and updates require a new revision.");
            var before = entry.OriginalValues.GetValue<ClearinghouseDispatchState>(nameof(ClearinghouseDispatch.State));
            var after = entry.Entity.State;
            if (before != after && !ClaimMdReconciliationRules.CanAdvanceDispatch((int)before, (int)after))
                throw new InvalidOperationException("Clearinghouse dispatch cannot return to sending or skip a required state.");
        }
        foreach (var entry in tracker.Entries<ClearinghouseFeedCheckpoint>().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Property(x => x.AgencyId).IsModified || entry.Property(x => x.AccountId).IsModified ||
                entry.Property(x => x.FeedKind).IsModified ||
                entry.Entity.Revision != entry.OriginalValues.GetValue<long>(nameof(ClearinghouseFeedCheckpoint.Revision)) + 1)
                throw new InvalidOperationException("A clearinghouse feed cannot change its identity and updates require a new revision.");
        }
        if (tracker.Entries<ClearinghouseResponseReceipt>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            tracker.Entries<ClearinghouseResponseMatch>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Clearinghouse response evidence is immutable.");
        var newReceiptIds = tracker.Entries<ClearinghouseResponseReceipt>()
            .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id).ToHashSet();
        if (tracker.Entries<ClearinghouseResponseMatch>().Any(entry => entry.State == EntityState.Added && !newReceiptIds.Contains(entry.Entity.ResponseId)))
            throw new InvalidOperationException("A retained clearinghouse receipt cannot acquire new matches.");
    }

}
