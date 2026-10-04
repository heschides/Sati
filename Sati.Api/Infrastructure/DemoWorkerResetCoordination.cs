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
}

internal sealed class SqlDemoWorkerResetCoordination(
    IDbContextFactory<ApiDbContext> contexts, IOptions<SatiApiOptions> options)
    : IDemoWorkerResetCoordination
{
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token) => RunWithLeaseAsync(DemoResetLease.Resource, "Shared",
            operation, unavailableResult, token);

    // The caller already holds the outer shared reset lease (HTTP middleware or RunAsync).
    // This prevents a human reconciliation from declaring absence while an upload is in flight.
    public Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token) => RunWithLeaseAsync(
            $"Sati.ClearinghouseDispatch:{dispatchId:N}", "Exclusive", operation, unavailableResult, token);

    private async Task<T> RunWithLeaseAsync<T>(string resourceName, string mode,
        Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token)
    {
        if (options.Value.ExpectedEnvironment != "Demo" || options.Value.ExpectedDatabaseName != "SatiDemo")
            return await operation(token);

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
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        if (result == -1) return unavailableResult;
        if (result < 0)
            throw new InvalidOperationException("Demo worker reset lease could not be acquired.");

        try { return await operation(token); }
        finally
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
