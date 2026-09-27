using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClaimMdSandboxCoordinationTests
{
    [Fact]
    public async Task NonSqlStorageFailsClosedBeforeAnyVendorRequest()
    {
        await using var database = new SyntheticPipelineDatabase();
        var coordination = new SqlClaimMdSandboxCoordination(new OwnedFactory(database.Options()));
        var sent = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordination.RequestAsync(_ =>
        {
            sent = true;
            return Task.FromResult(1);
        }, CancellationToken.None));
        Assert.False(sent);
    }

    [SqlServerFact]
    public async Task SeparateApiHostsShareOneRequestBudgetAndOnePoller()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        var first = new SqlClaimMdSandboxCoordination(new OwnedFactory(options));
        var second = new SqlClaimMdSandboxCoordination(new OwnedFactory(options));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPoll = first.PollOnceAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;
        Assert.Equal(0, await second.PollOnceAsync(_ => Task.FromResult(2), CancellationToken.None));
        release.SetResult();
        Assert.Equal(1, await firstPoll);

        var sent = new List<DateTime>();
        var sync = new object();
        var requests = new[] { first, second }.Select(coordinator => coordinator.RequestAsync(_ =>
        {
            lock (sync) sent.Add(DateTime.UtcNow);
            return Task.FromResult(1);
        }, CancellationToken.None));
        await Task.WhenAll(requests);
        Assert.Equal(2, sent.Count);
        Assert.True((sent.Max() - sent.Min()).TotalMilliseconds >= 900,
            "Two hosts must not begin Claim.MD requests in the same second.");
    }

    private sealed class OwnedFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
