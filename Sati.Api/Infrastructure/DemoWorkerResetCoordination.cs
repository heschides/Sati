using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>Hold the reset exclusion across a hosted worker's reads, vendor call, and evidence commit.</summary>
internal interface IDemoWorkerResetCoordination
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token);
    Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token);
    Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId,
        Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token);
}

internal interface IAccountPreflightLease
{
    Task VerifyAsync(CancellationToken token);
}

internal sealed class UncoordinatedAccountPreflightLease : IAccountPreflightLease
{
    public Task VerifyAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}

internal sealed class SqlDemoWorkerResetCoordination(
    IDbContextFactory<ApiDbContext> contexts, IOptions<SatiApiOptions> options)
    : IDemoWorkerResetCoordination
{
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token) => RunWithLeaseAsync(DemoResetLease.Resource, "Shared",
            (_, heldToken) => operation(heldToken), unavailableResult, token);

    // The caller already holds the outer shared reset lease (HTTP middleware or RunAsync).
    // This prevents a human reconciliation from declaring absence while an upload is in flight.
    public Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token) => RunWithLeaseAsync(
            $"Sati.ClearinghouseDispatch:{dispatchId:N}", "Exclusive",
            (_, heldToken) => operation(heldToken), unavailableResult, token);

    public Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId,
        Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token)
    {
        if (agencyId <= 0 || accountId == Guid.Empty) throw new ArgumentException("Invalid account preflight scope.");
        return RunWithLeaseAsync($"Sati.ClearinghousePreflight:{agencyId}:{accountId:N}", "Exclusive",
            operation, unavailableResult, token);
    }

    private async Task<T> RunWithLeaseAsync<T>(string resourceName, string mode,
        Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token)
    {
        if (options.Value.ExpectedEnvironment != "Demo" || options.Value.ExpectedDatabaseName != "SatiDemo")
            return await operation(new UncoordinatedAccountPreflightLease(), token);

        await using var db = await contexts.CreateDbContextAsync(token);
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Demo worker reset coordination requires SQL Server.");
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = @mode, @LockOwner = 'Session', " +
            "@LockTimeout = 0; SELECT @result;";
        command.CommandTimeout = 10;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = resourceName;
        command.Parameters.Add(resource);
        var lockMode = command.CreateParameter();
        lockMode.ParameterName = "@mode";
        lockMode.Value = mode;
        command.Parameters.Add(lockMode);
        var result = await command.ExecuteScalarAsync(token);
        try
        {
            if (!SqlSessionAdmission.OwnsLease(result, token)) return unavailableResult;
            return await operation(new SessionLease(db, resourceName, mode), token);
        }
        finally
        {
            if (SqlSessionAdmission.ConfirmedAcquired(result))
            {
                await using var release = db.Database.GetDbConnection().CreateCommand();
                release.CommandText = "EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                release.CommandTimeout = 5;
                var parameter = release.CreateParameter();
                parameter.ParameterName = "@resource";
                parameter.Value = resourceName;
                release.Parameters.Add(parameter);
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    private sealed class SessionLease(ApiDbContext db, string resourceName, string mode) : IAccountPreflightLease
    {
        public async Task VerifyAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Session');";
            command.CommandTimeout = 5;
            var resource = command.CreateParameter(); resource.ParameterName = "@resource";
            resource.Value = resourceName; command.Parameters.Add(resource);
            if (await command.ExecuteScalarAsync(token) is not string current || current != mode)
                throw new InvalidOperationException("The account preflight lease was lost.");
        }
    }
}
