using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>SQL Server application locks coordinate all API hosts sharing the Demo database.</summary>
internal interface IClaimMdSandboxCoordination
{
    Task<T> RequestAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken token);
    Task<int> PollOnceAsync(Func<CancellationToken, Task<int>> poll, CancellationToken token);
}

internal sealed class SqlClaimMdSandboxCoordination(IDbContextFactory<ApiDbContext> contexts)
    : IClaimMdSandboxCoordination
{
    private const string RequestResource = "Sati.ClaimMdSandbox.ApiRequest";
    private const string PollResource = "Sati.ClaimMdSandbox.Poller";

    public async Task<T> RequestAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken token)
    {
        await using var lease = await AcquireAsync(RequestResource, 120_000, token)
            ?? throw new InvalidOperationException("Claim.MD request coordination is unavailable.");
        // The lock is held during the delay and HTTP exchange. Across all API hosts this
        // permits at most one new request per second, well below Claim.MD's 100/minute cap.
        await Task.Delay(TimeSpan.FromSeconds(1), token);
        return await request(token);
    }

    public async Task<int> PollOnceAsync(Func<CancellationToken, Task<int>> poll, CancellationToken token)
    {
        await using var lease = await AcquireAsync(PollResource, 0, token);
        return lease is null ? 0 : await poll(token);
    }

    private async Task<ApplicationLease?> AcquireAsync(string resource, int timeoutMs, CancellationToken token)
    {
        var db = await contexts.CreateDbContextAsync(token);
        ApplicationLease? acquiredLease = null;
        try
        {
            if (!db.Database.IsSqlServer())
                throw new InvalidOperationException("Claim.MD sandbox coordination requires SQL Server.");
            await db.Database.OpenConnectionAsync(token);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
                "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', " +
                "@LockTimeout = @timeout; SELECT @result;";
            command.CommandTimeout = Math.Max(5, timeoutMs / 1000 + 10);
            var resourceParameter = command.CreateParameter();
            resourceParameter.ParameterName = "@resource";
            resourceParameter.Value = resource;
            command.Parameters.Add(resourceParameter);
            var timeoutParameter = command.CreateParameter();
            timeoutParameter.ParameterName = "@timeout";
            timeoutParameter.Value = timeoutMs;
            command.Parameters.Add(timeoutParameter);
            var result = await command.ExecuteScalarAsync(token);
            if (SqlSessionAdmission.ConfirmedAcquired(result)) acquiredLease = new ApplicationLease(db, resource);
            if (!SqlSessionAdmission.OwnsLease(result, token))
            {
                if (timeoutMs == 0)
                {
                    await db.DisposeAsync();
                    return null; // another host owns the poll
                }
                throw new InvalidOperationException("Claim.MD sandbox coordination lock was not acquired.");
            }
            return acquiredLease!;
        }
        catch
        {
            if (acquiredLease is not null) await acquiredLease.DisposeAsync();
            else await db.DisposeAsync();
            throw;
        }
    }

    private sealed class ApplicationLease(ApiDbContext db, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                command.CommandTimeout = 5;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@resource";
                parameter.Value = resource;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync();
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
