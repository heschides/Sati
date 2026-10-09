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
    // Deliberately red design reproducer, outside normal acceptance. Promote to an
    // ordinary regression only with the reviewed durable recovery implementation.
    [DispatchIsolationReproducerFact]
    [Trait("Category", "DispatchIsolationDesignReproducer")]
    public async Task MissingAccountKeyMustAllowHealthyAgencyByTheNextWorkerTurn()
    {
        await using var fixture = await Fixture.CreateAsync();
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
        var firstWorker = CreateIsolationWorker(fixture.Factory, keys, connector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstWorker.ProcessOneAsync(CancellationToken.None));

        // Independently constructed host, same owned in-memory database and synthetic
        // vault. Leases are test stand-ins: this is not SQL multi-host concurrency proof.
        await using var secondHost = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var secondWorker = CreateIsolationWorker(secondHost, keys, connector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondWorker.ProcessOneAsync(CancellationToken.None));
        bool healthyAgencyProgressed;
        await using (var db = secondHost.OpenDatabase())
        {
            var first = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == firstDispatchId);
            var other = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == otherDispatchId);
            healthyAgencyProgressed = other.State == ClearinghouseDispatchState.AcceptedByClearinghouse;
            Assert.Equal(ClearinghouseDispatchState.Queued, first.State);
            Assert.Equal(ClearinghouseDispatchState.Queued, other.State);
            Assert.Equal(firstRevision, first.Revision); Assert.Equal(otherRevision, other.Revision);
            Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
            Assert.Empty(await db.BillingSubmissionEvents.Where(x => x.Stage == BillingSubmissionStage.Transmitted).ToListAsync());
            Assert.Equal(firstFile.Content, (await db.EdiGenerations.SingleAsync(x => x.Id == fixture.GenerationId)).Content);
            Assert.Equal(otherFile.Content, (await db.EdiGenerations.SingleAsync(x => x.Id == otherGenerationId)).Content);
        }
        Assert.Empty(connector.Dispatches);
        Assert.Equal(new[] { missingReference, missingReference }, keys.ResolvedReferences);

        // Positive control: neither B's identity nor current compliance caused the
        // failure. Restore only A's synthetic key; both retained files can send once.
        keys.MissingKeyRestored = true;
        Assert.True(await secondWorker.ProcessOneAsync(CancellationToken.None));
        Assert.True(await secondWorker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(new[] { firstDispatchId, otherDispatchId }, connector.Dispatches);
        await using (var db = secondHost.OpenDatabase())
        {
            Assert.All(await db.ClearinghouseDispatches.ToListAsync(), row =>
                Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse, row.State));
            Assert.Equal(2, await db.ClearinghouseDispatchAttempts.CountAsync());
            Assert.Equal(2, await db.BillingSubmissionEvents.CountAsync(x => x.Stage == BillingSubmissionStage.Transmitted));
        }
        Assert.True(healthyAgencyProgressed,
            "Healthy agency B received no turn: both bounded worker calls selected A's missing key; " +
            "zero uploads occurred before restoring A. Durable account isolation is not implemented.");
    }

    private static ClearinghouseDispatchWorker CreateIsolationWorker(SyntheticPipelineFactory factory,
        IClaimMdSandboxKeySource keys, IClearinghouseConnector connector) => new(
        factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
        new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
            EnableClaimMdSandboxTransport = true
        }), factory.Services.GetRequiredService<IHostEnvironment>()),
        factory.Services.GetRequiredService<EnvelopeProtector>(), keys, new TestDemoWorkerResetCoordination(),
        factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());

    private sealed class IsolationKeySource(string missingReference, string healthyReference) : IClaimMdSandboxKeySource
    {
        public bool MissingKeyRestored { get; set; }
        public List<string> ResolvedReferences { get; } = [];
        public string Resolve(string? reference)
        {
            Assert.True(reference == missingReference || reference == healthyReference);
            ResolvedReferences.Add(reference!);
            if (reference == missingReference && !MissingKeyRestored)
                throw new InvalidOperationException("Synthetic account key unavailable.");
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
}

public sealed class DispatchIsolationReproducerFactAttribute : FactAttribute
{
    public DispatchIsolationReproducerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SATI_RUN_DISPATCH_ISOLATION_REPRO") != "1")
            Skip = "Deliberately red design reproducer; opt in with SATI_RUN_DISPATCH_ISOLATION_REPRO=1. " +
                "See BACKGROUND_WORKERS_HANDOFF.md's known-unsent isolation proposal.";
    }
}
