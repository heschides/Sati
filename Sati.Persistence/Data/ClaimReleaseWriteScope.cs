using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Sati.Data;

/// <summary>Common first lock for authoritative claim history and release decisions.</summary>
public static class ClaimReleaseWriteScope
{
    public static Task ExecuteOnceAsync(DbContext db, Func<Task> operation) =>
        ExecuteOnceAsync(db, async () => { await operation(); return true; });

    public static Task<T> ExecuteOnceAsync<T>(DbContext db, Func<Task<T>> operation)
    {
        // A nested nonretrying strategy cannot neutralize a retrying outer caller.
        if (ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Claim release writes cannot run inside a retrying execution scope.");
        return new SingleAttempt(db).ExecuteAsync(operation);
    }

    public static async Task<IDbContextTransaction> BeginAsync(DbContext db, int agencyId, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Claim release must own its decision transaction.");
        if (!db.Database.IsSqlServer() && db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException("Claim release requires a supported relational database.");
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try { await AcquireAsync(db, agencyId, token); return transaction; }
        catch { await transaction.DisposeAsync(); throw; }
    }

    // Existing narrower owners call this immediately after BeginTransaction, before
    // acquiring their own lock or reading anything used in an authoritative decision.
    public static async Task AcquireAsync(DbContext db, int agencyId, CancellationToken token = default)
    {
        if (agencyId <= 0) throw new ArgumentOutOfRangeException(nameof(agencyId));
        var transaction = db.Database.CurrentTransaction ??
            throw new InvalidOperationException("Claim release admission requires an owned transaction.");
        if (!db.Database.IsSqlServer()) return;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource,
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @result;
            """;
        command.CommandTimeout = 15;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource"; resource.DbType = DbType.String; resource.Size = 255;
        resource.Value = FormattableString.Invariant($"Sati:ClaimRelease:{agencyId}");
        command.Parameters.Add(resource);
        try
        {
            if (await command.ExecuteScalarAsync(token) is not int result)
                throw new InvalidOperationException("The database did not confirm claim release admission.");
            if (result < 0) { token.ThrowIfCancellationRequested(); throw new ClaimReleaseWriteConflictException(); }
        }
        catch (SqlException) when (token.IsCancellationRequested)
        {
            // SqlClient can surface command cancellation as a provider exception.
            // Preserve caller cancellation without exposing its raw SQL payload.
            throw new OperationCanceledException(token);
        }
    }

    private sealed class SingleAttempt(DbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }
}

public sealed class ClaimReleaseWriteConflictException() : InvalidOperationException(
    "Another billing or source change is in progress. Refresh and retry this request; this attempt made no change.");
