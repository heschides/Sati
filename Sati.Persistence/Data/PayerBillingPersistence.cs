using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Data;

public static class PayerBillingPersistenceModel
{
    public static void Configure<TAgency, TUser>(ModelBuilder model) where TAgency : class where TUser : class
    {
        model.Entity<PayerBillingConfigurationVersion>(e =>
        {
            e.ToTable("PayerBillingConfigurationVersions"); e.HasKey(x => x.VersionId);
            e.Property(x => x.ProfileKey).HasMaxLength(40).IsRequired();
            e.Property(x => x.EffectiveOn).HasColumnType("date");
            e.Property(x => x.ConfigurationJson).IsRequired();
            e.HasIndex(x => new { x.AgencyId, x.ProfileKey, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.ProfileKey, x.EffectiveOn }).IsUnique();
            e.HasOne<TAgency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
    public static void ProtectWrites(ChangeTracker tracker)
    {
        if (tracker.Entries<PayerBillingConfigurationVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Payer configuration versions are append-only.");
    }
}

public sealed class PayerBillingConflictException(string message) : InvalidOperationException(message);

/// <summary>Callers validate the current actor inside the transaction before accessing this owner.</summary>
public static class PayerBillingStore
{
    public static bool IsWriteConflict(DbUpdateException exception) => exception is DbUpdateConcurrencyException ||
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 2601 or 2627 };
    public static async Task<IReadOnlyList<PayerBillingVersionDto>> ReadAsync(DbContext db, int agencyId, CancellationToken ct = default) =>
        (await db.Set<PayerBillingConfigurationVersion>().AsNoTracking().Where(v => v.AgencyId == agencyId)
            .OrderBy(v => v.ProfileKey).ThenByDescending(v => v.Revision).ToListAsync(ct)).Select(v => v.ToDto()).ToList();

    public static async Task<PayerBillingVersionDto> AppendAsync(DbContext db, AgencyActor actor,
        PublishPayerBillingRequest request, DateTime utcNow, Action<string> audit, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Payer publication requires its transaction.");
        if (!PayerBillingRules.CanPublish(actor)) throw new UnauthorizedAccessException("Only an administrator can publish payer configuration.");
        var set = db.Set<PayerBillingConfigurationVersion>();
        var existing = await set.AsNoTracking().SingleOrDefaultAsync(v => v.VersionId == request.ChangeId, ct);
        if (existing is not null)
        {
            if (existing.AgencyId != actor.AgencyId || existing.CreatedByUserId != actor.UserId ||
                existing.ConfigurationJson != JsonSerializer.Serialize(request.Configuration) || existing.Revision != request.ExpectedRevision + 1)
                throw new PayerBillingConflictException("The change identity was already used. Refresh and prepare a new change.");
            return existing.ToDto();
        }
        var latest = await set.AsNoTracking().Where(v => v.AgencyId == actor.AgencyId && v.ProfileKey == request.Configuration.ProfileKey)
            .OrderByDescending(v => v.Revision).FirstOrDefaultAsync(ct);
        if ((latest?.Revision ?? 0) != request.ExpectedRevision)
            throw new PayerBillingConflictException("The payer profile changed. Refresh before publishing.");
        if (latest is not null && request.Configuration.EffectiveOn <= latest.EffectiveOn)
            throw new PayerBillingConflictException("A new version must start after the existing version's start. Retained dates cannot be rewritten.");
        var version = PayerBillingConfigurationVersion.Create(actor, request, request.ExpectedRevision + 1, utcNow);
        set.Add(version);
        audit(JsonSerializer.Serialize(new { version.VersionId, version.ProfileKey, version.Revision, version.EffectiveOn }));
        await db.SaveChangesAsync(ct);
        return version.ToDto();
    }

    public static async Task<IDbContextTransaction> BeginPublicationAsync(DbContext db, int agencyId, CancellationToken ct = default)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (db.Database.IsSqlServer())
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@resource";
                parameter.DbType = DbType.String; parameter.Size = 255; parameter.Value = $"Sati:PayerBilling:{agencyId}";
                command.Parameters.Add(parameter);
                if (await command.ExecuteScalarAsync(ct) is not int result || result < 0)
                    throw new PayerBillingConflictException("Another payer configuration change is in progress. Refresh and try again.");
            }
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public static async Task<PayerBillingVersionDto?> SelectAsync(DbContext db, int agencyId,
        DateTime serviceDate, PayerClaimPreparation? preparation, CancellationToken ct = default)
    {
        var versions = await ReadAsync(db, agencyId, ct);
        if (versions.Count == 0 && preparation is null) return null; // Explicit legacy compatibility, never live certification.
        var selected = PayerBillingRules.Resolve(versions, agencyId, preparation?.ProfileKey ?? "", serviceDate);
        if (selected is null) throw new PayerBillingConflictException("ProfileKey: No effective payer configuration covers this service date. Use Payer configuration to prepare the claim.");
        if (selected.VersionId != preparation?.ExpectedVersionId)
            throw new PayerBillingConflictException("ConfigurationVersionId: The configuration changed. Refresh the payer preview.");
        return selected;
    }
}
