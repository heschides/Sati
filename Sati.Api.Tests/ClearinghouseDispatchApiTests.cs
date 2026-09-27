using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClearinghouseDispatchApiTests
{
    [Fact]
    public async Task FeatureIsOffWithoutAnExplicitServerOptIn()
    {
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database);
        await factory.SeedAsync();
        using var biller = await factory.SignInAsync("synthetic-biller");

        var workspace = (await biller.GetFromJsonAsync<ClearinghouseWorkspaceDto>(
            "/api/v1/billing/clearinghouse"))!;
        Assert.False(workspace.Enabled);
        Assert.Empty(workspace.Accounts);
        Assert.Equal(HttpStatusCode.NotFound, (await biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(1, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task ExistingOfficeAllyProductionGenerationStillReplaysExactBytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var key = Guid.NewGuid().ToString("N");
        var request = new GenerateEdiRequest(false, key);
        var first = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/edi", request);
        var replay = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/edi", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<EdiFileDto>())!.Content,
            (await replay.Content.ReadFromJsonAsync<EdiFileDto>())!.Content);
    }

    [Fact]
    public async Task AccountScopedFileQueuesOnceAndFakeWorkerRecordsOneAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var file = await fixture.GenerateAsync(fixture.AccountId);
        var claims = ClaimResponseReader.ReadSubmission(file.Content).Claims;
        Assert.All(claims, claim => Assert.StartsWith("SATI1-TEST-", claim.RemoteClaimId));

        var request = new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId);
        var first = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var queued = (await first.Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        Assert.Equal(nameof(ClearinghouseDispatchState.Queued), queued.State);
        var replay = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches", request))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        Assert.Equal(queued.Id, replay.Id);

        var worker = fixture.Worker(new SyntheticClearinghouseConnector());
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse,
            (await db.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Single(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.Single(await db.BillingSubmissionEvents.Where(x =>
            x.EdiGenerationId == fixture.GenerationId && x.Stage == BillingSubmissionStage.Transmitted).ToListAsync());
    }

    [Fact]
    public async Task WrongAccountAndAnotherTenantCannotQueueTheRetainedFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var wrongAccount = await fixture.AddAccountAsync("OTHERACCOUNT", "OTHER",
            partner: TradingPartnerKind.OfficeAlly);

        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, wrongAccount))).StatusCode);
        // The foreign account must be invisible, even before profile matching.
        var foreignAccount = await fixture.AddAccountAsync("FOREIGN", "FOREIGN", foreign: true);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, foreignAccount))).StatusCode);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Empty(await db.ClearinghouseDispatches.ToListAsync());
    }

    [Theory]
    [InlineData(ClearinghouseDispatchState.Queued)]
    [InlineData(ClearinghouseDispatchState.Sending)]
    [InlineData(ClearinghouseDispatchState.OutcomeUnknown)]
    public async Task UnresolvedUploadBlocksAnotherFileForThePeriod(ClearinghouseDispatchState state)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        using (var queued = await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        if (state != ClearinghouseDispatchState.Queued)
        {
            await using var db = fixture.Factory.OpenDatabase();
            var dispatch = await db.ClearinghouseDispatches.SingleAsync();
            dispatch.State = ClearinghouseDispatchState.Sending;
            dispatch.Revision++;
            await db.SaveChangesAsync();
            if (state == ClearinghouseDispatchState.OutcomeUnknown)
            {
                dispatch.State = state;
                dispatch.Revision++;
                await db.SaveChangesAsync();
            }
        }
        await fixture.GenerateAsync(fixture.AccountId);
        var response = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConnectorExceptionIsUnknownAndIsNeverRetriedAutomatically()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        var connector = new ThrowingConnector();
        var worker = fixture.Worker(connector);
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(1, connector.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.OutcomeUnknown,
            (await db.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Single(await db.ClearinghouseDispatchAttempts.ToListAsync());
    }

    [Fact]
    public async Task ReturnedVendorEvidenceIsEncryptedAndBoundToTheAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        Assert.True(await fixture.Worker(new EvidenceConnector()).ProcessOneAsync(CancellationToken.None));
        await using var db = fixture.Factory.OpenDatabase();
        var attempt = await db.ClearinghouseDispatchAttempts.SingleAsync();
        Assert.NotNull(attempt.ResponseCiphertext);
        Assert.NotNull(attempt.ResponseSha256);
        var protectedValue = new ProtectedValue(attempt.ResponseCiphertext!, attempt.ResponseNonce!,
            attempt.ResponseTag!, attempt.ResponseWrappedDataKey!, attempt.ResponseKeyId!);
        var protector = fixture.Factory.Services.GetRequiredService<EnvelopeProtector>();
        Assert.Equal("<result>synthetic test response</result>", await protector.UnprotectAsync(
            protectedValue, ClearinghouseDispatchWorker.ResponseBinding(fixture.Actors.AgencyId, attempt)));
        attempt.Id = Guid.NewGuid();
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => protector.UnprotectAsync(
            protectedValue, ClearinghouseDispatchWorker.ResponseBinding(fixture.Actors.AgencyId, attempt)));
    }

    [Fact]
    public async Task MissingSandboxKeyCannotTurnAnUnsentFileIntoAnUnknownUpload()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST";
            account.Revision++;
            await db.SaveChangesAsync();
        }
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        var connector = new ThrowingConnector();
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo",
            EnableClaimMdSandboxTransport = true
        }), fixture.Factory.Services.GetRequiredService<IHostEnvironment>());
        var worker = new ClearinghouseDispatchWorker(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
            connector, gate, fixture.Factory.Services.GetRequiredService<EnvelopeProtector>(),
            new MissingKeySource(), fixture.Factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(0, connector.Calls);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.Queued,
            (await saved.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await saved.ClearinghouseDispatchAttempts.ToListAsync());
    }

    private sealed class MissingKeySource : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference) => throw new InvalidOperationException("No test key provisioned.");
    }

    private sealed class EvidenceConnector : IClearinghouseConnector
    {
        public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token) =>
            Task.FromResult(new ClearinghouseUploadResult(ClearinghouseAttemptOutcome.Accepted,
                "123456", null, 1, 0, "<result>synthetic test response</result>"));
    }

    private sealed class ThrowingConnector : IClearinghouseConnector
    {
        public int Calls { get; private set; }
        public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
        {
            Calls++;
            throw new TimeoutException("No reliable upload verdict");
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SyntheticPipelineDatabase _database;
        public SyntheticPipelineFactory Factory { get; }
        public HttpClient Biller { get; }
        public PipelineActors Actors { get; }
        public int PeriodId { get; }
        public Guid AccountId { get; private set; }
        public long GenerationId { get; private set; }

        private Fixture(SyntheticPipelineDatabase database, SyntheticPipelineFactory factory,
            HttpClient biller, PipelineActors actors, int periodId)
        {
            _database = database; Factory = factory; Biller = biller; Actors = actors; PeriodId = periodId;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var database = new SyntheticPipelineDatabase();
            await database.InitializeAsync();
            var factory = new SyntheticPipelineFactory(database)
            {
                EnableSyntheticDispatch = true, DisableDispatchWorker = true
            };
            var actors = await factory.SeedAsync();
            var periodId = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(factory, actors);
            var biller = await factory.SignInAsync("synthetic-biller");
            var fixture = new Fixture(database, factory, biller, actors, periodId);
            fixture.AccountId = await fixture.AddAccountAsync("CLAIMMDTEST", "TEST");
            return fixture;
        }

        public async Task<Guid> AddAccountAsync(string accountNumber, string claimNamespace,
            bool foreign = false, TradingPartnerKind partner = TradingPartnerKind.ClaimMd)
        {
            await using var db = Factory.OpenDatabase();
            var agencyId = Actors.AgencyId;
            if (foreign)
            {
                var agency = new ServerAgency { Name = "Other Synthetic Agency" };
                db.Agencies.Add(agency);
                await db.SaveChangesAsync();
                agencyId = agency.Id;
            }
            var account = new ClearinghouseAccount
            {
                Id = Guid.NewGuid(), AgencyId = agencyId,
                ConnectorKind = partner, IsTest = true, IsEnabled = true,
                ExternalAccountNumber = accountNumber,
                ClaimNamespace = partner == TradingPartnerKind.ClaimMd ? claimNamespace : null,
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.ClearinghouseAccounts.Add(account);
            await db.SaveChangesAsync();
            return account.Id;
        }

        public async Task<EdiFileDto> GenerateAsync(Guid accountId, string? key = null)
        {
            var response = await Biller.PostAsJsonAsync($"/api/v1/billing/periods/{PeriodId}/edi",
                new GenerateEdiRequest(true, key ?? Guid.NewGuid().ToString("N"))
                { ClearinghouseAccountId = accountId });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var file = (await response.Content.ReadFromJsonAsync<EdiFileDto>())!;
            await using var db = Factory.OpenDatabase();
            GenerationId = await db.EdiGenerations.OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
            return file;
        }

        public ClearinghouseDispatchWorker Worker(IClearinghouseConnector connector) => new(
            Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            Factory.Services.GetRequiredService<ClearinghouseDispatchGate>(),
            Factory.Services.GetRequiredService<EnvelopeProtector>(),
            Factory.Services.GetRequiredService<IClaimMdSandboxKeySource>(),
            Factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());

        public async ValueTask DisposeAsync()
        {
            Biller.Dispose();
            await Factory.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
