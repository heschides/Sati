using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// One mapping/protection owner shared by the transitional local context and the API context.
/// Both contexts describe the same physical release-obligation tables.
/// </summary>
public static class ReleaseObligationPersistenceModel
{
    public static void Configure<TAgency, TUser, TPerson, TProvider, TDocumentArtifact>(ModelBuilder modelBuilder)
        where TAgency : class
        where TUser : class
        where TPerson : class
        where TProvider : class
        where TDocumentArtifact : class
    {
        modelBuilder.Entity<ReleaseObligation>(entity =>
        {
            entity.ToTable("ReleaseObligations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ObligationId).IsRequired();
            entity.Property(item => item.StableKey).IsRequired().HasMaxLength(300);
            entity.Property(item => item.Category).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.Trigger).HasConversion<string>().HasMaxLength(30);
            entity.Property(item => item.TargetEffectiveDate).HasColumnType("date");
            entity.Property(item => item.AssignmentKey).HasMaxLength(100);
            entity.Property(item => item.RecipientDisplayName).HasMaxLength(200);
            entity.Property(item => item.AvailableOn).HasColumnType("date");
            entity.Property(item => item.DueOn).HasColumnType("date");
            entity.Property(item => item.AppliesFromOn).HasColumnType("date");
            entity.Property(item => item.RetiredOn).HasColumnType("date");
            entity.Ignore(item => item.CompletedOn);
            entity.Ignore(item => item.WithdrawnOn);
            entity.HasIndex(item => item.ObligationId).IsUnique();
            entity.HasIndex(item => new { item.PersonId, item.StableKey })
                .IsUnique()
                .HasDatabaseName("IX_ReleaseObligations_PersonId_StableKey");
            entity.HasIndex(item => new { item.AgencyId, item.PersonId, item.TargetEffectiveDate });
            entity.HasOne<TAgency>()
                .WithMany()
                .HasForeignKey(item => item.AgencyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TPerson>()
                .WithMany()
                .HasForeignKey(item => item.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TProvider>()
                .WithMany()
                .HasForeignKey(item => item.RecipientProviderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(item => item.Attestations)
                .WithOne(item => item.ReleaseObligation)
                .HasForeignKey(item => item.ReleaseObligationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(item => item.AuthorizationEvents)
                .WithOne(item => item.ReleaseObligation)
                .HasForeignKey(item => item.ReleaseObligationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReleaseObligationAttestation>(entity =>
        {
            entity.ToTable("ReleaseObligationAttestations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CompletedOn).HasColumnType("date");
            entity.Property(item => item.Source).HasConversion<string>().HasMaxLength(30);
            entity.Property(item => item.ActorKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.SignerCapacity).HasConversion<string>().HasMaxLength(30);
            entity.Property(item => item.Reason).HasMaxLength(500);
            entity.HasIndex(item => new { item.ReleaseObligationId, item.RecordedAtUtc });
            entity.HasIndex(item => item.SignatureCompletionId)
                .IsUnique()
                .HasFilter("[SignatureCompletionId] IS NOT NULL");
            entity.HasOne<TUser>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SignatureCompletion>()
                .WithMany()
                .HasForeignKey(item => item.SignatureCompletionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReleaseAuthorizationEvent>(entity =>
        {
            entity.ToTable("ReleaseAuthorizationEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.OccurredOn).HasColumnType("date");
            entity.Property(item => item.Reason).IsRequired().HasMaxLength(500);
            entity.HasIndex(item => new { item.ReleaseObligationId, item.RecordedAtUtc });
            entity.HasOne<TUser>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TDocumentArtifact>()
            .HasOne<ReleaseObligation>()
            .WithMany()
            .HasForeignKey("ReleaseObligationId")
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void ProtectWrites(ChangeTracker changeTracker)
    {
        if (changeTracker.Entries<ReleaseObligationAttestation>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            changeTracker.Entries<ReleaseAuthorizationEvent>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            changeTracker.Entries<ReleaseObligation>()
                .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Release obligations, attestations, and authorization history are retained records.");
        }

        foreach (var entry in changeTracker.Entries<ReleaseObligation>()
                     .Where(item => item.State == EntityState.Modified))
        {
            var changed = entry.Properties.Where(property => property.IsModified)
                .Select(property => property.Metadata.Name)
                .ToHashSet(StringComparer.Ordinal);
            changed.Remove(nameof(ReleaseObligation.RetiredOn));
            changed.Remove(nameof(ReleaseObligation.RetirementRecordedAtUtc));
            if (changed.Count != 0 ||
                entry.OriginalValues.GetValue<DateTime?>(nameof(ReleaseObligation.RetiredOn)) is not null ||
                entry.CurrentValues.GetValue<DateTime?>(nameof(ReleaseObligation.RetiredOn)) is null ||
                entry.OriginalValues.GetValue<DateTime?>(nameof(ReleaseObligation.RetirementRecordedAtUtc)) is not null ||
                entry.CurrentValues.GetValue<DateTime?>(nameof(ReleaseObligation.RetirementRecordedAtUtc)) is null)
            {
                throw new InvalidOperationException(
                    "A release obligation can only receive its first prospective retirement.");
            }
        }
    }
}
