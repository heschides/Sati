using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed class DemoWorkerResetCoordinationTests
{
    private static IOptions<SatiApiOptions> DemoOptions() => Options.Create(new SatiApiOptions
    {
        ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo"
    });

    [Fact]
    public async Task DemoCoordinationFailsClosedOnNonSqlStorage()
    {
        await using var database = new SyntheticPipelineDatabase();
        var coordination = new SqlDemoWorkerResetCoordination(new OwnedFactory(database.Options()), DemoOptions());
        var entered = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordination.RunAsync(_ =>
        {
            entered = true;
            return Task.FromResult(1);
        }, 0, CancellationToken.None));
        Assert.False(entered);
    }

    [SqlServerFact]
    public async Task ExclusiveResetLeasePreventsWorkerReadsAndVendorCall()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        await using var reset = new ApiDbContext(options);
        await reset.Database.OpenConnectionAsync();
        Assert.True(await TakeResetLeaseAsync(reset) >= 0);
        try
        {
            var coordination = new SqlDemoWorkerResetCoordination(new OwnedFactory(options), DemoOptions());
            var entered = false;
            Assert.Equal(0, await coordination.RunAsync(_ =>
            {
                entered = true;
                return Task.FromResult(7);
            }, 0, CancellationToken.None));
            Assert.False(entered);
        }
        finally { await ReleaseResetLeaseAsync(reset); }
    }

    [SqlServerFact]
    public async Task WorkerLeaseExcludesResetUntilEvidenceCommitAndReleasesAfterFailure()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        var coordination = new SqlDemoWorkerResetCoordination(new OwnedFactory(options), DemoOptions());
        await using var reset = new ApiDbContext(options);
        await reset.Database.OpenConnectionAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = coordination.RunAsync(async _ =>
        {
            entered.SetResult();
            await commit.Task;
            return 3;
        }, 0, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try { Assert.Equal(-1, await TakeResetLeaseAsync(reset)); }
        finally { commit.TrySetResult(); }
        Assert.Equal(3, await work);
        Assert.True(await TakeResetLeaseAsync(reset) >= 0);
        await ReleaseResetLeaseAsync(reset);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordination.RunAsync<int>(
            _ => throw new InvalidOperationException("Synthetic worker failure."), 0, CancellationToken.None));
        Assert.True(await TakeResetLeaseAsync(reset) >= 0);
        await ReleaseResetLeaseAsync(reset);
    }

    [SqlServerFact]
    public async Task ActiveUploadExcludesManualResolutionUntilEvidenceCommit()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        var uploader = new SqlDemoWorkerResetCoordination(new OwnedFactory(options), DemoOptions());
        var administrator = new SqlDemoWorkerResetCoordination(new OwnedFactory(options), DemoOptions());
        var dispatchId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var upload = uploader.RunDispatchAsync(dispatchId, async _ =>
        {
            entered.SetResult();
            await commit.Task;
            return 3;
        }, 0, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var resolutionEntered = false;
        try
        {
            Assert.Equal(0, await administrator.RunDispatchAsync(dispatchId, _ =>
            {
                resolutionEntered = true;
                return Task.FromResult(4);
            }, 0, CancellationToken.None));
            Assert.False(resolutionEntered);
        }
        finally { commit.TrySetResult(); }
        Assert.Equal(3, await upload);
        Assert.Equal(4, await administrator.RunDispatchAsync(dispatchId,
            _ => Task.FromResult(4), 0, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.RunDispatchAsync<int>(dispatchId,
            _ => throw new InvalidOperationException("Synthetic upload failure."), 0, CancellationToken.None));
        Assert.Equal(4, await administrator.RunDispatchAsync(dispatchId,
            _ => Task.FromResult(4), 0, CancellationToken.None));
    }

    private static async Task<int> TakeResetLeaseAsync(ApiDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = N'SatiDemo.FullReset', @LockMode = 'Exclusive', @LockOwner = 'Session', " +
            "@LockTimeout = 0; SELECT @result;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ReleaseResetLeaseAsync(ApiDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXEC sys.sp_releaseapplock @Resource = N'SatiDemo.FullReset', @LockOwner = 'Session';";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class OwnedFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
