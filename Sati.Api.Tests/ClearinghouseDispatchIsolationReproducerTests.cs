using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    // Retained failing boundary from DEC-0234; now an ordinary acceptance regression.
    [Fact]
    public Task MissingAccountKeyMustAllowHealthyAgencyByTheNextWorkerTurn() => VerifyMissingKeyIsolationAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlMissingKeyAllowsHealthyAgencyAndDurableDueRecovery() => VerifyMissingKeyIsolationAsync(true);

    private static async Task VerifyMissingKeyIsolationAsync(bool sqlServer)
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer);
        const string missingReference = "CLAIMMD_SANDBOX_KEY_ISOLATION_A";
        const string healthyReference = "CLAIMMD_SANDBOX_KEY_ISOLATION_B";
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = missingReference;
            account.Revision++;
            await db.SaveChangesAsync();
        }
        var firstFile = await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();

        const string prefix = "isolation-b-";
        var otherActors = await fixture.Factory.SeedAsync(prefix);
        Assert.NotEqual(fixture.Actors.AgencyId, otherActors.AgencyId);
        var otherPeriod = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(
            fixture.Factory, otherActors, prefix);
        var otherAccount = new ClearinghouseAccount
        {
            Id = Guid.NewGuid(), AgencyId = otherActors.AgencyId,
            ConnectorKind = TradingPartnerKind.ClaimMd, IsTest = true, IsEnabled = true,
            ExternalAccountNumber = "ISOLATIONB", ClaimNamespace = "ISOLB",
            SecretReference = healthyReference, TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        await using (var db = fixture.Factory.OpenDatabase())
        {
            db.ClearinghouseAccounts.Add(otherAccount);
            await db.SaveChangesAsync();
        }
        using var otherBiller = await fixture.Factory.SignInAsync(prefix + "synthetic-biller");
        using var generationResponse = await otherBiller.PostAsJsonAsync($"/api/v1/billing/periods/{otherPeriod}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = otherAccount.Id });
        generationResponse.EnsureSuccessStatusCode();
        var otherFile = (await generationResponse.Content.ReadFromJsonAsync<EdiFileDto>())!;
        long otherGenerationId;
        await using (var db = fixture.Factory.OpenDatabase())
            otherGenerationId = await db.EdiGenerations.Where(x => x.AgencyId == otherActors.AgencyId)
                .Select(x => x.Id).SingleAsync();
        (await otherBiller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(otherGenerationId, otherAccount.Id))).EnsureSuccessStatusCode();

        Guid firstDispatchId, otherDispatchId;
        long firstRevision, otherRevision;
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var first = await db.ClearinghouseDispatches.SingleAsync(x => x.AgencyId == fixture.Actors.AgencyId);
            var other = await db.ClearinghouseDispatches.SingleAsync(x => x.AgencyId == otherActors.AgencyId);
            // Verify the fixture's selection order without changing immutable intent.
            Assert.True(first.RequestedAtUtc < other.RequestedAtUtc);
            firstDispatchId = first.Id; otherDispatchId = other.Id;
            firstRevision = first.Revision; otherRevision = other.Revision;
        }
        var keys = new IsolationKeySource(missingReference, healthyReference);
        var connector = new IsolationRecordingConnector();
        var clock = new PreflightTestClock();
        var firstWorker = CreateIsolationWorker(fixture.Factory, keys, connector, clock,
            sqlServer ? SqlPreflightCoordination(fixture.Factory) : null);
        Assert.True(await firstWorker.ProcessOneAsync(CancellationToken.None));

        // Independent host, same owned database/vault. Only the SQL variant proves
        // provider session ownership; the SQLite variant uses lease stand-ins.
        await using var secondHost = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var secondWorker = CreateIsolationWorker(secondHost, keys, connector, clock,
            sqlServer ? SqlPreflightCoordination(secondHost) : null);
        Assert.True(await secondWorker.ProcessOneAsync(CancellationToken.None));
        DateTime nextEligible;
        await using (var db = secondHost.OpenDatabase())
        {
            var first = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == firstDispatchId);
            var other = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == otherDispatchId);
            Assert.Equal(ClearinghouseDispatchState.Queued, first.State);
            Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse, other.State);
            Assert.Equal(firstRevision, first.Revision); Assert.Equal(otherRevision + 2, other.Revision);
            var attempt = Assert.Single(await db.ClearinghouseDispatchAttempts.ToListAsync());
            Assert.Equal(otherDispatchId, attempt.DispatchId);
            var transmission = Assert.Single(await db.BillingSubmissionEvents.Where(x => x.Stage == BillingSubmissionStage.Transmitted).ToListAsync());
            Assert.Equal(otherActors.AgencyId, transmission.AgencyId);
            var readiness = await db.ClearinghouseDispatchReadiness.SingleAsync();
            Assert.Equal(fixture.Actors.AgencyId, readiness.AgencyId);
            Assert.Equal(1, readiness.FailureCount);
            Assert.Equal(ClearinghousePreflightDisposition.Deferred, readiness.Disposition);
            nextEligible = readiness.NextEligibleAtUtc!.Value;
            Assert.Equal(firstFile.Content, (await db.EdiGenerations.SingleAsync(x => x.Id == fixture.GenerationId)).Content);
            Assert.Equal(otherFile.Content, (await db.EdiGenerations.SingleAsync(x => x.Id == otherGenerationId)).Content);
        }
        Assert.Equal(new[] { otherDispatchId }, connector.Dispatches);
        Assert.Equal(new[] { missingReference, healthyReference }, keys.ResolvedReferences);

        // Positive control: neither B's identity nor current compliance caused the
        // failure. Restore only A's synthetic key; both retained files can send once.
        await secondHost.DisposeAsync();
        await using var restartedHost = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var restartedWorker = CreateIsolationWorker(restartedHost, keys, connector, clock,
            sqlServer ? SqlPreflightCoordination(restartedHost) : null);
        keys.MissingKeyRestored = true;
        Assert.False(await restartedWorker.ProcessOneAsync(CancellationToken.None));
        clock.SetUtc(nextEligible.AddTicks(-1));
        Assert.False(await restartedWorker.ProcessOneAsync(CancellationToken.None));
        clock.SetUtc(nextEligible);
        Assert.True(await restartedWorker.ProcessOneAsync(CancellationToken.None));
        Assert.False(await restartedWorker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(new[] { otherDispatchId, firstDispatchId }, connector.Dispatches);
        await using (var db = restartedHost.OpenDatabase())
        {
            Assert.All(await db.ClearinghouseDispatches.ToListAsync(), row =>
                Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse, row.State));
            Assert.Equal(2, await db.ClearinghouseDispatchAttempts.CountAsync());
            Assert.Equal(2, await db.BillingSubmissionEvents.CountAsync(x => x.Stage == BillingSubmissionStage.Transmitted));
            Assert.Equal(ClearinghousePreflightDisposition.Ready,
                (await db.ClearinghouseDispatchReadiness.SingleAsync()).Disposition);
        }
    }

    private static SqlDemoWorkerResetCoordination SqlPreflightCoordination(SyntheticPipelineFactory factory) => new(
        factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
        Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" }));

    private static ClearinghouseDispatchWorker CreateIsolationWorker(SyntheticPipelineFactory factory,
        IClaimMdSandboxKeySource keys, IClearinghouseConnector connector, TimeProvider? clock = null,
        IDemoWorkerResetCoordination? coordination = null)
    {
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
            EnableClaimMdSandboxTransport = true
        }), factory.Services.GetRequiredService<IHostEnvironment>());
        var contexts = factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>();
        return new(contexts, connector, gate,
        factory.Services.GetRequiredService<EnvelopeProtector>(), keys, coordination ?? new TestDemoWorkerResetCoordination(),
        factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>(), clock ?? TimeProvider.System,
        factory.Services.GetRequiredService<ClearinghouseDispatchSchedule>(),
        new ClearinghouseDispatchSelector(contexts, gate, clock ?? TimeProvider.System));
    }

    private sealed class IsolationKeySource(string missingReference, string healthyReference) : IClaimMdSandboxKeySource
    {
        public bool MissingKeyRestored { get; set; }
        public List<string> ResolvedReferences { get; } = [];
        public string Resolve(string? reference)
        {
            Assert.True(reference == missingReference || reference == healthyReference);
            ResolvedReferences.Add(reference!);
            if (reference == missingReference && !MissingKeyRestored)
                throw new ClaimMdAccountKeyUnavailableException();
            return "SYNTHETIC_ONLY_KEY";
        }
    }

    private sealed class IsolationRecordingConnector : IClearinghouseConnector
    {
        private readonly SyntheticClearinghouseConnector _inner = new();
        public List<Guid> Dispatches { get; } = [];
        public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
        {
            Dispatches.Add(upload.DispatchId);
            return _inner.UploadAsync(upload, token);
        }
    }

    private sealed class PreflightTestClock : TimeProvider
    {
        private DateTime _now = DateTime.UtcNow;
        public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);
        public void SetUtc(DateTime now) => _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
