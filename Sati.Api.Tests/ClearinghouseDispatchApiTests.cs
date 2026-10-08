using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        var connector = new SingleAttemptScopeConnector();
        var worker = fixture.Worker(connector);
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.True(connector.HadSingleAttemptScope);
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
    public async Task ResetLeaseBlocksUploadBeforeSendingOrVendorRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        var reset = new BlockedResetLeaseCoordination();
        var connector = new ThrowingConnector();
        var worker = fixture.Worker(connector, reset);

        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(1, reset.Calls);
        Assert.Equal(0, connector.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.Queued, (await db.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
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
    public async Task HttpExchangeDeadlineKeepsAnUploadUnknownAndNeverRetriesItAutomatically()
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
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .EnsureSuccessStatusCode();
        var clock = new ManualTimeProvider();
        using var body = new StalledUploadResponseBody();
        using var handler = new StalledUploadHandler(body);
        // Only the connector's fake-time deadline can cancel this synthetic exchange.
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var keys = new SyntheticSandboxKeySource();
        var connector = new ObservedDeadlineConnector(new ClaimMdSandboxConnector(
            http, keys, new TestClaimMdCoordination(), clock));
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
            EnableClaimMdSandboxTransport = true
        }), fixture.Factory.Services.GetRequiredService<IHostEnvironment>());
        var worker = new ClearinghouseDispatchWorker(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
            connector, gate, fixture.Factory.Services.GetRequiredService<EnvelopeProtector>(),
            keys, new TestDemoWorkerResetCoordination(),
            fixture.Factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());
        using var caller = new CancellationTokenSource();
        var processing = worker.ProcessOneAsync(caller.Token);
        try
        {
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
            await using (var sending = fixture.Factory.OpenDatabase())
            {
                Assert.Equal(ClearinghouseDispatchState.Sending,
                    (await sending.ClearinghouseDispatches.SingleAsync()).State);
                Assert.Empty(await sending.ClearinghouseDispatchAttempts.ToListAsync());
            }
            clock.Advance(TimeSpan.FromMilliseconds(44_999));
            Assert.False(processing.IsCompleted);
            Assert.False(body.ReadToken.IsCancellationRequested);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsAssignableFrom<OperationCanceledException>(connector.Failure);
            Assert.True(body.ReadToken.IsCancellationRequested);
            Assert.False(caller.IsCancellationRequested);
            Assert.True(body.WasDisposed);

            Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
            Assert.Equal(1, handler.Calls);
            await using var saved = fixture.Factory.OpenDatabase();
            var dispatch = await saved.ClearinghouseDispatches.SingleAsync();
            Assert.Equal(ClearinghouseDispatchState.OutcomeUnknown, dispatch.State);
            Assert.Equal("upload_outcome_unknown", dispatch.SafeErrorCode);
            var attempt = Assert.Single(await saved.ClearinghouseDispatchAttempts.ToListAsync());
            Assert.Equal(1, attempt.AttemptNumber);
            Assert.Equal(ClearinghouseAttemptOutcome.OutcomeUnknown, attempt.Outcome);
            Assert.Null(attempt.ResponseCiphertext);
            Assert.Empty(await saved.BillingSubmissionEvents.Where(row =>
                row.EdiGenerationId == fixture.GenerationId &&
                row.Stage == BillingSubmissionStage.Transmitted).ToListAsync());
        }
        finally
        {
            // The unfixed transport must leave no stalled task behind when this test fails.
            body.Release();
            caller.Cancel();
            await processing.WaitAsync(TimeSpan.FromSeconds(5));
        }
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
            new MissingKeySource(), new TestDemoWorkerResetCoordination(),
            fixture.Factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(0, connector.Calls);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.Queued,
            (await saved.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await saved.ClearinghouseDispatchAttempts.ToListAsync());
    }

    [Fact]
    public async Task UncertainUploadReconciliationRequiresAnAdmin()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var queued = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        using var author = await fixture.Factory.SignInAsync("synthetic-author");

        var response = await author.GetAsync(
            $"/api/v1/admin/clearinghouse/dispatches/{queued.Id}/reconciliation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UncertainUploadReconciliationRefusesAStaleRevisionWithoutChangingTheDispatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var queued = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var dispatch = await db.ClearinghouseDispatches.SingleAsync();
            dispatch.State = ClearinghouseDispatchState.Sending;
            dispatch.Revision++;
            await db.SaveChangesAsync();
            dispatch.State = ClearinghouseDispatchState.OutcomeUnknown;
            dispatch.Revision++;
            await db.SaveChangesAsync();
        }

        var path = $"/api/v1/admin/clearinghouse/dispatches/{queued.Id}/reconciliation";
        var manifest = (await fixture.Biller.GetFromJsonAsync<ClaimMdReconciliationManifestDto>(path))!;
        var response = await fixture.Biller.PostAsJsonAsync(path,
            new ReconcileClaimMdDispatchRequest(0, manifest.AccountId, manifest.AccountNumber,
                manifest.EdiGenerationId, manifest.ContentSha256, manifest.FileName,
                "ConfirmedNotReceived", "SupportCase", "CASE-12345678", new string('A', 64),
                DateTime.UtcNow, manifest.NotReceivedAttestation, null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.OutcomeUnknown,
            (await saved.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await saved.AuditEvents.Where(x => x.Action ==
            "billing-clearinghouse.dispatch-reconciled").ToListAsync());
    }

    [Fact]
    public async Task ManualReceivedFindingRequiresAllClaimIdentitiesAndRetainsAuditWithoutChangingAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var queued = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        Assert.True(await fixture.Worker(new ThrowingConnector()).ProcessOneAsync(CancellationToken.None));
        var path = $"/api/v1/admin/clearinghouse/dispatches/{queued.Id}/reconciliation";
        var manifest = (await fixture.Biller.GetFromJsonAsync<ClaimMdReconciliationManifestDto>(path))!;
        var claims = manifest.Claims.Select(claim =>
            new ClaimMdObservedClaim(claim.ClaimReference, claim.RemoteClaimId, "A")).ToArray();
        var request = new ReconcileClaimMdDispatchRequest(manifest.Revision, manifest.AccountId,
            manifest.AccountNumber, manifest.EdiGenerationId, manifest.ContentSha256,
            manifest.FileName, "ConfirmedReceived", "AccountFileRecord",
            "FILE-123456", new string('B', 64), DateTime.UtcNow,
            manifest.ReceivedAttestation, "123456", claims.Length, 0, claims);
        var mismatched = await fixture.Biller.PostAsJsonAsync(path, request with
        {
            Claims = [claims[0] with { RemoteClaimId = "WRONG" }]
        });
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);

        var result = await fixture.Biller.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var db = fixture.Factory.OpenDatabase();
        var dispatch = await db.ClearinghouseDispatches.SingleAsync();
        Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse, dispatch.State);
        Assert.Equal("123456", dispatch.ExternalFileId);
        Assert.Equal(claims.Length, dispatch.AcceptedClaimCount);
        var attempt = await db.ClearinghouseDispatchAttempts.SingleAsync();
        Assert.Equal(ClearinghouseAttemptOutcome.OutcomeUnknown, attempt.Outcome);
        var audit = await db.AuditEvents.SingleAsync(row => row.Action ==
            "billing-clearinghouse.dispatch-reconciled");
        Assert.Contains("FILE-123456", audit.MetadataJson);
        Assert.Contains(manifest.ContentSha256, audit.MetadataJson);
        Assert.DoesNotContain("test file content", audit.MetadataJson);
        Assert.Single(await db.BillingSubmissionEvents.Where(row =>
            row.EdiGenerationId == fixture.GenerationId &&
            row.Stage == BillingSubmissionStage.Transmitted && row.IsSynthetic).ToListAsync());
        Assert.False(await fixture.Worker(new ThrowingConnector()).ProcessOneAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ManualNonReceiptNeedsSupportEvidenceAndLeavesASeparateGenerationAsTheOnlyResendPath()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.GenerateAsync(fixture.AccountId);
        var queued = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        Assert.True(await fixture.Worker(new ThrowingConnector()).ProcessOneAsync(CancellationToken.None));
        var path = $"/api/v1/admin/clearinghouse/dispatches/{queued.Id}/reconciliation";
        var manifest = (await fixture.Biller.GetFromJsonAsync<ClaimMdReconciliationManifestDto>(path))!;
        var request = new ReconcileClaimMdDispatchRequest(manifest.Revision, manifest.AccountId,
            manifest.AccountNumber, manifest.EdiGenerationId, manifest.ContentSha256,
            manifest.FileName, "ConfirmedNotReceived", "SupportCase",
            "CASE-123456", new string('C', 64), DateTime.UtcNow,
            manifest.NotReceivedAttestation, null, null, null, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Biller.PostAsJsonAsync(path,
            request with { EvidenceKind = "AccountFileRecord" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Biller.PostAsJsonAsync(path, request)).StatusCode);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            Assert.Equal(ClearinghouseDispatchState.ConfirmedNotReceived,
                (await db.ClearinghouseDispatches.SingleAsync()).State);
            Assert.Equal(ClearinghouseAttemptOutcome.OutcomeUnknown,
                (await db.ClearinghouseDispatchAttempts.SingleAsync()).Outcome);
            Assert.Single(await db.AuditEvents.Where(row => row.Action ==
                "billing-clearinghouse.dispatch-reconciled").ToListAsync());
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await fixture.Biller.PostAsJsonAsync(path, request)).StatusCode);
        await fixture.GenerateAsync(fixture.AccountId);
        Assert.Equal(HttpStatusCode.OK,
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
                new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).StatusCode);
    }

    [Fact]
    public async Task ReconciliationCannotDeclareNonReceiptWhileItsUploadLeaseIsHeld()
    {
        var blocked = new BlockedDemoWorkerResetCoordination();
        await using var fixture = await Fixture.CreateAsync(blocked);
        await fixture.GenerateAsync(fixture.AccountId);
        var queued = (await (await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId)))
            .Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!;
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var dispatch = await db.ClearinghouseDispatches.SingleAsync();
            dispatch.State = ClearinghouseDispatchState.Sending;
            dispatch.Revision++;
            await db.SaveChangesAsync();
        }
        var path = $"/api/v1/admin/clearinghouse/dispatches/{queued.Id}/reconciliation";
        var manifest = (await fixture.Biller.GetFromJsonAsync<ClaimMdReconciliationManifestDto>(path))!;
        var request = new ReconcileClaimMdDispatchRequest(manifest.Revision, manifest.AccountId,
            manifest.AccountNumber, manifest.EdiGenerationId, manifest.ContentSha256,
            manifest.FileName, "ConfirmedNotReceived", "SupportCase",
            "CASE-123456", new string('C', 64), DateTime.UtcNow,
            manifest.NotReceivedAttestation, null, null, null, null);

        var response = await fixture.Biller.PostAsJsonAsync(path, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, blocked.Calls);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.Sending,
            (await saved.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await saved.AuditEvents.Where(row => row.Action ==
            "billing-clearinghouse.dispatch-reconciled").ToListAsync());
    }

    [Fact]
    public async Task ReconciliationDoesNotRevealAnotherAgencysDispatch()
    {
        var blocked = new BlockedDemoWorkerResetCoordination();
        await using var fixture = await Fixture.CreateAsync(blocked);
        Guid foreignDispatchId;
        Guid foreignAccountId;
        long foreignGenerationId;
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var agency = new ServerAgency { Name = "Foreign Synthetic Agency" };
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();
            var owner = new ServerUser
            {
                AgencyId = agency.Id, Username = "foreign-reconciliation-owner",
                DisplayName = "Foreign Owner", Role = "Admin",
                Permissions = UserPermissionRules.FromLegacyRole("Admin")
            };
            db.Users.Add(owner);
            await db.SaveChangesAsync();
            var period = new ServerBillingPeriod
            {
                UserId = owner.Id, Year = 2026, Month = 10, Status = 1
            };
            db.BillingPeriods.Add(period);
            await db.SaveChangesAsync();
            var generation = new ServerEdiGeneration
            {
                AgencyId = agency.Id, ActorUserId = owner.Id, BillingPeriodId = period.Id,
                IdempotencyKey = Guid.NewGuid().ToString("N"), IsTest = true,
                ControlNumber = "123456789", FileName = "foreign.CMDTEST.txt",
                Content = "foreign synthetic retained file"
            };
            db.EdiGenerations.Add(generation);
            foreignAccountId = Guid.NewGuid();
            db.ClearinghouseAccounts.Add(new ClearinghouseAccount
            {
                Id = foreignAccountId, AgencyId = agency.Id, IsTest = true, IsEnabled = true,
                ConnectorKind = TradingPartnerKind.ClaimMd,
                ExternalAccountNumber = "FOREIGNTEST", ClaimNamespace = "FOREIGN",
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            foreignGenerationId = generation.Id;
            foreignDispatchId = Guid.NewGuid();
            db.ClearinghouseDispatches.Add(new ClearinghouseDispatch
            {
                Id = foreignDispatchId, AgencyId = agency.Id, AccountId = foreignAccountId,
                EdiGenerationId = generation.Id, RequestingUserId = owner.Id,
                RequestedAtUtc = DateTime.UtcNow,
                State = ClearinghouseDispatchState.OutcomeUnknown,
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion
            });
            await db.SaveChangesAsync();
        }

        var path = $"/api/v1/admin/clearinghouse/dispatches/{foreignDispatchId}/reconciliation";
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Biller.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Biller.PostAsJsonAsync(path,
            new ReconcileClaimMdDispatchRequest(0, foreignAccountId, "FOREIGNTEST",
                foreignGenerationId, new string('A', 64), "foreign.CMDTEST.txt",
                "ConfirmedNotReceived", "SupportCase", "CASE-123456", new string('B', 64),
                DateTime.UtcNow, ClaimMdReconciliationRules.NotReceivedAttestation,
                null, null, null, null))).StatusCode);
        Assert.Equal(0, blocked.Calls);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.OutcomeUnknown,
            (await saved.ClearinghouseDispatches.SingleAsync(row => row.Id == foreignDispatchId)).State);
        Assert.Empty(await saved.AuditEvents.Where(row => row.Action ==
            "billing-clearinghouse.dispatch-reconciled").ToListAsync());
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

    private sealed class SingleAttemptScopeConnector : IClearinghouseConnector
    {
        public bool HadSingleAttemptScope { get; private set; }
        public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
        {
            HadSingleAttemptScope = ExecutionStrategy.Current is { RetriesOnFailure: false };
            return new SyntheticClearinghouseConnector().UploadAsync(upload, token);
        }
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

    private sealed class SyntheticSandboxKeySource : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference)
        {
            Assert.Equal("CLAIMMD_SANDBOX_KEY_TEST", reference);
            return "SYNTHETIC_KEY";
        }
    }

    private sealed class ObservedDeadlineConnector(ClaimMdSandboxConnector connector) : IClearinghouseConnector
    {
        public OperationCanceledException? Failure { get; private set; }

        public async Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload,
            CancellationToken token)
        {
            try { return await connector.UploadAsync(upload, token); }
            catch (OperationCanceledException exception)
            {
                Failure = exception;
                throw;
            }
        }
    }

    private sealed class StalledUploadHandler(StalledUploadResponseBody body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https://svc.claim.md/services/upload/", request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body)
            });
        }
    }

    private sealed class StalledUploadResponseBody : Stream
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ReadStarted => _readStarted.Task;
        public CancellationToken ReadToken { get; private set; }
        public bool WasDisposed { get; private set; }
        public void Release() => _released.TrySetResult(0);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadToken = cancellationToken;
            _readStarted.TrySetResult();
            return await _released.Task.WaitAsync(cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

        public static async Task<Fixture> CreateAsync(IDemoWorkerResetCoordination? coordination = null)
        {
            var database = new SyntheticPipelineDatabase();
            await database.InitializeAsync();
            var factory = new SyntheticPipelineFactory(database)
            {
                EnableSyntheticDispatch = true, DisableDispatchWorker = true,
                ResetCoordinationOverride = coordination
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

        public ClearinghouseDispatchWorker Worker(IClearinghouseConnector connector,
            IDemoWorkerResetCoordination? resetCoordination = null) => new(
            Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            Factory.Services.GetRequiredService<ClearinghouseDispatchGate>(),
            Factory.Services.GetRequiredService<EnvelopeProtector>(),
            Factory.Services.GetRequiredService<IClaimMdSandboxKeySource>(),
            resetCoordination ?? new TestDemoWorkerResetCoordination(),
            Factory.Services.GetRequiredService<ILogger<ClearinghouseDispatchWorker>>());

        public async ValueTask DisposeAsync()
        {
            Biller.Dispose();
            await Factory.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
