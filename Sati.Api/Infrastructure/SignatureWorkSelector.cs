using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Models;
using Sati.Signatures;

namespace Sati.Api.Infrastructure;

internal sealed class SignatureWorkerGate(SignatureFeature feature, SignatureOptions signature, IOptions<SatiApiOptions> api)
{
    internal bool IsTesting => signature.ExpectedEnvironment == "Testing" && signature.ExpectedDatabaseName == "SatiApiTests";
    internal bool CanRun(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!feature.Enabled || !signature.WorkersEnabled) return false;
        if (api.Value.ExpectedEnvironment != signature.ExpectedEnvironment || api.Value.ExpectedDatabaseName != signature.ExpectedDatabaseName)
            throw new InvalidOperationException("Signature worker target differs from its API target.");
        return true;
    }
}

internal enum SignatureSelectionKind { Selected, Empty, Contended, LaneSkipped, Disabled }
internal sealed record SignatureSelection(SignatureSelectionKind Kind, int AgencyId = 0, long ItemId = 0);

/// <summary>Short durable scheduling offers; no signature, projection, package or mail evidence writes.</summary>
internal sealed class SignatureWorkSelector(IDbContextFactory<ApiDbContext> contexts, SignatureWorkerGate gate, TimeProvider clock)
{
    internal const int MaximumOffersPerPhase = 10;
    internal static string Resource(SignatureWorkKind kind) => $"Sati.Signatures.WorkSelection:{(int)kind}";
    private sealed class Candidate { public int AgencyId { get; init; } public long ItemId { get; init; } }

    internal async Task<SignatureSelection> SelectAsync(SignatureWorkKind kind, IReadOnlyCollection<long> visited, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind) || visited.Count > MaximumOffersPerPhase || visited.Any(x => x <= 0))
            throw new ArgumentException("Signature scheduling phase or exclusions are invalid.");
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Signature selection cannot run inside a retrying execution scope.");
        if (!gate.CanRun(token)) return new(SignatureSelectionKind.Disabled);
        var excluded = visited.ToArray();
        await using var db = await contexts.CreateDbContextAsync(token);
        if (!db.Database.IsSqlServer() && !(db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && gate.IsTesting))
            throw new InvalidOperationException("Signature selection requires SQL Server or explicit Testing storage.");
        return await new SingleAttempt(db).ExecuteAsync(() => SelectOnceAsync(db, kind, excluded, token));
    }

    private async Task<SignatureSelection> SelectOnceAsync(ApiDbContext db, SignatureWorkKind kind, long[] excluded, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsSqlServer() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, token);
        if (!await AcquireAsync(db, kind, token)) return new(SignatureSelectionKind.Contended);
        if (!gate.CanRun(token)) return new(SignatureSelectionKind.Disabled);
        var pivot = await db.SignatureWorkRotation.SingleOrDefaultAsync(x => x.WorkKind == kind, token)
            ?? throw new InvalidOperationException("Signature scheduling metadata is unavailable; migration review is required.");
        if (!gate.CanRun(token)) return new(SignatureSelectionKind.Disabled);
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = kind switch
        {
            SignatureWorkKind.Projection => SignatureComplianceProjectionService.EligibleCandidates(db)
                .Select(x => new Candidate { AgencyId = x.AgencyId, ItemId = x.Id }),
            SignatureWorkKind.Package => SignatureCompletionWorker.EligibleCandidates(db)
                .Select(x => new Candidate { AgencyId = x.AgencyId, ItemId = x.Id }),
            SignatureWorkKind.Mail => SignatureMailWorker.EligibleCandidates(db, now)
                .Select(x => new Candidate { AgencyId = x.AgencyId, ItemId = x.Id }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        candidates = candidates.Where(x => x.ItemId > 0 && !excluded.Contains(x.ItemId));
        var agencies = db.Agencies.Where(agency => agency.Id > 0 && candidates.Any(item => item.AgencyId == agency.Id));
        int? agency = pivot.LastAgencyId is { } previous
            ? await agencies.Where(x => x.Id > previous).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token)
            : await agencies.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token);
        if (agency is null && pivot.LastAgencyId is not null)
            agency = await agencies.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(token);
        if (agency is null)
        {
            await VerifyAsync(db, kind, token);
            return new(gate.CanRun(token) ? SignatureSelectionKind.Empty : SignatureSelectionKind.Disabled);
        }
        pivot.LastAgencyId = agency; pivot.Revision = checked(pivot.Revision + 1);
        var lanePivot = await db.SignatureAgencyWorkRotation.SingleOrDefaultAsync(x => x.AgencyId == agency && x.WorkKind == kind, token);
        var lane = candidates.Where(x => x.AgencyId == agency);
        long? item = lanePivot?.LastItemId is { } last
            ? await lane.Where(x => x.ItemId > last).OrderBy(x => x.ItemId).Select(x => (long?)x.ItemId).FirstOrDefaultAsync(token)
            : await lane.OrderBy(x => x.ItemId).Select(x => (long?)x.ItemId).FirstOrDefaultAsync(token);
        if (item is null && lanePivot?.LastItemId is not null)
            item = await lane.OrderBy(x => x.ItemId).Select(x => (long?)x.ItemId).FirstOrDefaultAsync(token);
        if (item is not null)
        {
            if (lanePivot is null) db.SignatureAgencyWorkRotation.Add(new SignatureAgencyWorkRotation
                { AgencyId = agency.Value, WorkKind = kind, LastItemId = item });
            else { lanePivot.LastItemId = item; lanePivot.Revision = checked(lanePivot.Revision + 1); }
        }
        if (!gate.CanRun(token)) return new(SignatureSelectionKind.Disabled);
        await db.SaveChangesAsync(token);
        await VerifyAsync(db, kind, token);
        if (!gate.CanRun(token)) return new(SignatureSelectionKind.Disabled);
        await transaction.CommitAsync(token);
        return item is null ? new(SignatureSelectionKind.LaneSkipped) : new(SignatureSelectionKind.Selected, agency.Value, item.Value);
    }

    private static async Task<bool> AcquireAsync(ApiDbContext db, SignatureWorkKind kind, CancellationToken token)
    {
        if (!db.Database.IsSqlServer()) return true;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandTimeout = 10;
        command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;";
        var p = command.CreateParameter(); p.ParameterName = "@resource"; p.Value = Resource(kind); command.Parameters.Add(p);
        return OwnsLease(await command.ExecuteScalarAsync(token), token);
    }
    internal static bool OwnsLease(object? result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (result is not int value || value is not (0 or 1 or -1))
            throw new InvalidOperationException("Signature selection admission did not establish ownership.");
        return value != -1;
    }
    private static async Task VerifyAsync(ApiDbContext db, SignatureWorkKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!db.Database.IsSqlServer()) return;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandTimeout = 5;
        command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Transaction');";
        var p = command.CreateParameter(); p.ParameterName = "@resource"; p.Value = Resource(kind); command.Parameters.Add(p);
        if (await command.ExecuteScalarAsync(token) is not string mode || mode != "Exclusive")
            throw new InvalidOperationException("Signature selection ownership was lost.");
    }
    private sealed class SingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
}
