using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sati.Models;

namespace Sati.Data;

/// <summary>Full-server scheduling only; the restricted portal does not map this owner.</summary>
public static class SignatureWorkPersistenceModel
{
    public static void Configure<TAgency>(ModelBuilder model) where TAgency : class
    {
        model.Entity<SignatureWorkRotation>(e =>
        {
            e.ToTable("SignatureWorkRotation", t => t.HasCheckConstraint("CK_SignatureWorkRotation_Scope",
                "[WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)"));
            e.HasKey(x => x.WorkKind); e.Property(x => x.WorkKind).ValueGeneratedNever();
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasData(Enum.GetValues<SignatureWorkKind>().Select(kind => new SignatureWorkRotation { WorkKind = kind }));
        });
        model.Entity<SignatureAgencyWorkRotation>(e =>
        {
            e.ToTable("SignatureAgencyWorkRotation", t => t.HasCheckConstraint("CK_SignatureAgencyWorkRotation_Scope",
                "[AgencyId] > 0 AND [WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastItemId] IS NULL OR [LastItemId] > 0)"));
            e.HasKey(x => new { x.AgencyId, x.WorkKind }); e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SignatureWorkRotation>().WithMany().HasForeignKey(x => x.WorkKind).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SignatureCompletion>().HasIndex(x => new { x.AgencyId, x.Id });
        model.Entity<SignatureOutbox>().HasIndex(x => new { x.AgencyId, x.Id });
    }

    public static void ProtectWrites(ChangeTracker tracker)
    {
        foreach (var e in tracker.Entries<SignatureWorkRotation>())
        {
            if (e.State == EntityState.Unchanged || e.State == EntityState.Detached) continue;
            if (e.State == EntityState.Deleted || !Enum.IsDefined(e.Entity.WorkKind) || e.Entity.LastAgencyId is <= 0 ||
                e.State == EntityState.Added && (e.Entity.Revision != 1 || e.Entity.LastAgencyId is not null) ||
                e.State == EntityState.Modified && (e.Property(x => x.WorkKind).IsModified ||
                    e.Entity.Revision != checked(e.OriginalValues.GetValue<long>(nameof(e.Entity.Revision)) + 1)))
                throw new InvalidOperationException("Signature phase position requires retained scope and a new revision.");
        }
        foreach (var e in tracker.Entries<SignatureAgencyWorkRotation>())
        {
            if (e.State == EntityState.Unchanged || e.State == EntityState.Detached) continue;
            if (e.State == EntityState.Deleted || e.Entity.AgencyId <= 0 || !Enum.IsDefined(e.Entity.WorkKind) || e.Entity.LastItemId is <= 0 ||
                e.State == EntityState.Added && e.Entity.Revision != 1 ||
                e.State == EntityState.Modified && (e.Property(x => x.AgencyId).IsModified || e.Property(x => x.WorkKind).IsModified ||
                    e.Entity.Revision != checked(e.OriginalValues.GetValue<long>(nameof(e.Entity.Revision)) + 1)))
                throw new InvalidOperationException("Signature item position requires immutable scope and a new revision.");
        }
    }
}
