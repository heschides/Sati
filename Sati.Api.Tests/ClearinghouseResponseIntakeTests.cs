using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClearinghouseResponseIntakeTests
{
    [Fact]
    public async Task AutoCorrelationRetainsEncryptedEvidenceAndOneAuditedTransaction()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var document = fixture.Remittance();
        var result = await fixture.ImportAsync(document);
        Assert.Equal([77], result.BillingPeriodIds);
        Assert.False(result.AlreadyImported);
        Assert.Equal(1, result.ClaimOutcomesRecorded);
        await using var db = fixture.Context();
        var receipt = await db.ClearinghouseResponseReceipts.Include(x => x.Matches).SingleAsync();
        Assert.Equal(result.ResponseId, receipt.Id);
        Assert.Equal(7, receipt.ActorUserId);
        Assert.Equal(ClaimResponseIngestion.ParserVersion, receipt.ParserVersion);
        Assert.Equal(64, receipt.RawSha256.Length);
        Assert.NotEqual(document, System.Text.Encoding.UTF8.GetString(receipt.Ciphertext));
        var decrypted = await fixture.Protector.UnprotectAsync(new ProtectedValue(receipt.Ciphertext,
            receipt.Nonce, receipt.Tag, receipt.WrappedDataKey, receipt.KeyId), ClaimResponseIngestion.Binding(receipt));
        Assert.Equal(document, decrypted);
        Assert.Equal(fixture.GenerationId, Assert.Single(receipt.Matches).EdiGenerationId);
        Assert.Equal(receipt.Id, (await db.RemittanceClaimOutcomes.SingleAsync()).ResponseId);
        Assert.Equal(receipt.Id, (await db.RemittanceDeposits.SingleAsync()).ResponseId);
        Assert.Equal("billing-response.imported", (await db.AuditEvents.SingleAsync()).Action);
    }

    [Fact]
    public async Task CiphertextCannotBeMovedToAnotherAgencyOrReceipt()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        await using var db = fixture.Context();
        var receipt = await db.ClearinghouseResponseReceipts.SingleAsync();
        var value = new ProtectedValue(receipt.Ciphertext, receipt.Nonce, receipt.Tag, receipt.WrappedDataKey, receipt.KeyId);
        receipt.AgencyId = 2;
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => fixture.Protector.UnprotectAsync(value, ClaimResponseIngestion.Binding(receipt)));
        receipt.AgencyId = 1;
        receipt.Id = Guid.NewGuid();
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => fixture.Protector.UnprotectAsync(value, ClaimResponseIngestion.Binding(receipt)));
    }

    [Fact]
    public async Task ReimportAndReenvelopeReturnSameReceiptWithoutFinancialDuplicates()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var document = fixture.Remittance();
        var first = await fixture.ImportAsync(document);
        var exact = await fixture.ImportAsync(document);
        var semantic = await fixture.ImportAsync(fixture.Remittance(responseControl: "555555556", transaction: "8888"));
        Assert.Equal(first.ResponseId, exact.ResponseId);
        Assert.Equal(first.ResponseId, semantic.ResponseId);
        Assert.True(exact.AlreadyImported);
        Assert.True(semantic.AlreadyImported);
        Assert.Equal(0, semantic.ClaimOutcomesRecorded);
        await fixture.AssertCountsAsync(receipts: 1, events: 1, outcomes: 1, deposits: 1, audits: 1);
    }

    [Fact]
    public async Task ConcurrentImportsHaveOneReceiptAndOneSetOfFinancialEffects()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var document = fixture.Remittance();
        var results = await Task.WhenAll(Task.Run(() => fixture.ImportAsync(document)), Task.Run(() => fixture.ImportAsync(document)));
        Assert.Equal(results[0].ResponseId, results[1].ResponseId);
        Assert.Single(results, result => !result.AlreadyImported);
        await fixture.AssertCountsAsync(1, 1, 1, 1, 1);
    }

    [Fact]
    public async Task AnImportedDocumentCannotBeReassignedThroughLegacyPeriodRoute()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        var error = await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.Remittance(), assertedPeriod: 78));
        Assert.Equal("response_period_mismatch", error.Code);
        await fixture.AssertCountsAsync(1, 1, 1, 1, 1);
    }

    [Theory]
    [InlineData("wrong-period")]
    [InlineData("wrong-agency")]
    [InlineData("unknown-claim")]
    [InlineData("wrong-charge")]
    [InlineData("wrong-payee")]
    [InlineData("wrong-sender")]
    [InlineData("wrong-service")]
    [InlineData("production")]
    public async Task InvalidScopeOrEvidenceWritesNothing(string fault)
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var document = fixture.Remittance();
        document = fault switch
        {
            "unknown-claim" => document.Replace("123456789-77-900", "123456789-77-901", StringComparison.Ordinal),
            "wrong-charge" => document.Replace("*100", "*101", StringComparison.Ordinal),
            "wrong-payee" => document.Replace("*XX*1999999984", "*XX*1999999976", StringComparison.Ordinal),
            "wrong-sender" => document.Replace("330897513", "330897514", StringComparison.Ordinal),
            "wrong-service" => document.Replace("REF*6R*900", "REF*6R*901", StringComparison.Ordinal),
            "production" => document.Replace("*0*T*:~", "*0*P*:~", StringComparison.Ordinal),
            _ => document
        };
        var error = await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(document,
            agency: fault == "wrong-agency" ? 2 : 1, assertedPeriod: fault == "wrong-period" ? 78 : null));
        Assert.DoesNotContain("900", error.Message);
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task FunctionalAckUsesOriginalGroupNotResponseControlNumber()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var accepted = await fixture.ImportAsync(fixture.Acknowledgement());
        Assert.Equal(nameof(BillingSubmissionStage.FunctionalAccepted), accepted.StageRecorded);
        var invalid = fixture.Acknowledgement("987654321", "123456789");
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(invalid));
        await fixture.AssertCountsAsync(1, 1, 0, 0, 1);
    }

    [Fact]
    public async Task ClaimAcknowledgementLineMustBelongToTheMatchedClaim()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.ClaimAcknowledgement(line: "901")));
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
        Assert.Equal(nameof(BillingSubmissionStage.ClaimAccepted), (await fixture.ImportAsync(fixture.ClaimAcknowledgement())).StageRecorded);
    }

    [Theory]
    [InlineData("A1", nameof(BillingSubmissionStage.ClaimReceived))]
    [InlineData("A5", nameof(BillingSubmissionStage.ClaimNeedsReview))]
    public async Task ReceiptOnlyAndUnresolvedClaimResponsesNeverInventAcceptance(string category, string stage)
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        Assert.Equal(stage, (await fixture.ImportAsync(fixture.ClaimAcknowledgement(category))).StageRecorded);
    }

    [Fact]
    public async Task OneRemittanceCanMatchTwoPeriodsWithoutDuplicatingItsDeposit()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.AddSecondGenerationAsync();
        var result = await fixture.ImportAsync(fixture.MultiPeriodRemittance());
        Assert.Equal([77, 78], result.BillingPeriodIds);
        Assert.Equal(2, result.ClaimOutcomesRecorded);
        await fixture.AssertCountsAsync(1, 2, 2, 1, 1);
        await using var db = fixture.Context();
        Assert.Equal(200m, (await db.RemittanceDeposits.SingleAsync()).RemittancePaymentAmount);
        Assert.Equal(2, await db.ClearinghouseResponseMatches.CountAsync());
    }

    [Fact]
    public async Task OneUnknownClaimRollsBackTheWholeMultiPeriodRemittance()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.MultiPeriodRemittance()));
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task AmbiguousLegacyClaimsAreNeverAssignedToTheNewestGeneration()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await using (var db = fixture.Context())
        {
            var original = await db.EdiGenerations.AsNoTracking().SingleAsync();
            db.EdiGenerations.Add(new ServerEdiGeneration { AgencyId = 1, ActorUserId = 7, BillingPeriodId = 77,
                IdempotencyKey = "legacy-second", Content = original.Content, IsTest = true, FileName = "legacy.837", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.Remittance()));
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task ChangedPaymentWithSameIdentityConflictsInsteadOfDuplicating()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        var changed = fixture.Remittance(responseControl: "555555557").Replace("N1*PR*TEST PAYER", "N1*PR*OTHER PAYER", StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(changed));
        Assert.Equal("response_identity_conflict", error.Code);
        await fixture.AssertCountsAsync(1, 1, 1, 1, 1);
    }

    [Fact]
    public async Task ASecondPaymentForTheSameClaimRequiresReview()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        var second = fixture.Remittance(responseControl: "555555558").Replace("PAYMENT-ONE", "PAYMENT-TWO", StringComparison.Ordinal);
        Assert.Equal(nameof(BillingSubmissionStage.RemittanceNeedsReview), (await fixture.ImportAsync(second)).StageRecorded);
    }

    [Fact]
    public async Task PersistenceFailureRollsBackReceiptMatchesOutcomesDepositAndAudit()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await using (var db = fixture.Context())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_test_deposit BEFORE INSERT ON RemittanceDeposits BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;");
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.Remittance()));
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
        await using var verify = fixture.Context();
        Assert.Equal(0, await verify.ClearinghouseResponseMatches.CountAsync());
    }

    [Fact]
    public async Task EvidenceAndGenerationCannotBeChangedOrDeleted()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        await using (var db = fixture.Context())
        {
            (await db.ClearinghouseResponseReceipts.SingleAsync()).RawSha256 = new string('0', 64);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            db.ClearinghouseResponseMatches.Remove(await db.ClearinghouseResponseMatches.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Context())
        {
            (await db.EdiGenerations.SingleAsync()).Content = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DuplicateProtectionIsEnforcedByDatabaseIndexes()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await fixture.ImportAsync(fixture.Remittance());
        await using var db = fixture.Context();
        var receipt = await db.ClearinghouseResponseReceipts.AsNoTracking().SingleAsync();
        receipt.Id = Guid.NewGuid();
        receipt.RawSha256 = new string('A', 64);
        receipt.IdentitySha256 = new string('B', 64);
        receipt.PaymentIdentitySha256 = new string('C', 64);
        db.ClearinghouseResponseReceipts.Add(receipt);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LateAcknowledgementCannotRegressRemittanceAndDenialIsNotPaid()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var paid = await fixture.ImportAsync(fixture.Remittance());
        await fixture.ImportAsync(fixture.Acknowledgement());
        await using var db = fixture.Context();
        var rows = (await db.BillingSubmissionEvents.ToListAsync()).Select(row => new BillingSubmissionHistoryDto(
            row.Id, row.BillingPeriodId, 2026, 8, "Test", 1, row.OccurredAtUtc, row.Stage.ToString(), row.Reference,
            row.ResponseType, row.ResponseCode, row.Explanation, row.IsSynthetic) { EdiGenerationId = row.EdiGenerationId });
        Assert.Equal(nameof(BillingSubmissionStage.Paid), BillingSubmissionProgressRules.Current(rows)!.Stage);
        var denial = await fixture.ImportAsync(fixture.Remittance(responseControl: "555555558", denied: true));
        Assert.Equal(nameof(BillingSubmissionStage.RemittanceNeedsReview), denial.StageRecorded);
        Assert.Equal(BillingSubmissionProgress.NeedsAttention, BillingSubmissionProgressRules.Classify(denial.StageRecorded));
    }

    [Fact]
    public async Task ProductionAndNonBillingActorsAreRefusedBeforeWriting()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.Remittance(), production: true));
        await Assert.ThrowsAsync<ClaimResponseRejected>(() => fixture.ImportAsync(fixture.Remittance(), permissions: UserPermissions.CaseManagement));
        await fixture.AssertCountsAsync(0, 0, 0, 0, 0);
    }

    [Fact]
    public void MigrationIsAdditiveAndContainsDatabaseDuplicateConstraints()
    {
        var operations = new Sati.Migrations.AddClearinghouseResponseIntake().UpOperations;
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index => index.IsUnique && index.Columns.Contains("SemanticSha256"));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index => index.IsUnique && index.Columns.Contains("ControlNumber"));
    }

    private sealed class IntakeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly string _connectionString;
        public EnvelopeProtector Protector { get; } = new(new TestKeyWrapper());
        public long GenerationId { get; private set; }

        private IntakeFixture()
        {
            _connectionString = $"Data Source=Intake-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=15";
            _anchor = new SqliteConnection(_connectionString);
            _anchor.Open();
        }
        public ApiDbContext Context() => new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connectionString).Options);
        public static async Task<IntakeFixture> CreateAsync()
        {
            var fixture = new IntakeFixture();
            await using var db = fixture.Context();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(new ServerAgency { Id = 1, Name = "Synthetic One" }, new ServerAgency { Id = 2, Name = "Synthetic Two" });
            db.Users.Add(new ServerUser { Id = 7, AgencyId = 1, Username = "synthetic", DisplayName = "Synthetic", Role = "Admin", Permissions = UserPermissions.Billing });
            var period = new ServerBillingPeriod { Id = 77, UserId = 7, Year = 2026, Month = 8, Status = 1 };
            db.BillingPeriods.Add(period);
            db.BillingPeriods.Add(new ServerBillingPeriod { Id = 78, UserId = 7, Year = 2026, Month = 9, Status = 1 });
            var snapshot = new ProfessionalClaimSnapshot(ProfessionalClaimSnapshotCodec.CurrentVersion,
                1, 101, "Test", "Person", new DateTime(1990, 1, 1), "U", "123456789", "1 Test Street", "Portland", "ME", "04101",
                "Synthetic One", "1999999984", "111111111", "2 Provider Street", "Portland", "ME", "04101", "SUBMITTER", "Billing", "2075550101", "TEST PAYER", "MCDME");
            period.Lines.Add(new ServerClaimLine { NoteId = 900, BillingPeriodId = 77, DateOfService = new DateTime(2026, 8, 1),
                ProcedureCode = "G9012", Units = 1, ChargeAmount = 100, ClientMaineCareId = "123456789", RenderingProviderNpi = "1999999984",
                DiagnosisCode = "F89", PlaceOfService = 11, ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot) });
            var generation = new ServerEdiGeneration { AgencyId = 1, ActorUserId = 7, BillingPeriodId = 77, IsTest = true,
                IdempotencyKey = "synthetic-first", FileName = "synthetic.837", ControlNumber = "123456789",
                Content = ServerEdiGenerator.Generate(period, true, new DateTime(2026, 8, 1), "123456789") };
            db.EdiGenerations.Add(generation);
            await db.SaveChangesAsync();
            fixture.GenerationId = generation.Id;
            return fixture;
        }
        public async Task<ClaimResponseIngestResultDto> ImportAsync(string document, int agency = 1, int? assertedPeriod = null,
            bool production = false, UserPermissions permissions = UserPermissions.Billing)
        {
            await using var db = Context();
            var service = new ClaimResponseIngestion(db, Protector, new AuditTrail(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }),
                Options.Create(new SatiApiOptions { ExpectedDatabaseName = production ? "SatiProduction" : "SatiApiTests", ExpectedEnvironment = production ? "Production" : "Testing" }), new TestEnvironment());
            return await service.ImportAsync(document, new Actor(7, agency, "Admin", "Synthetic", permissions), assertedPeriod, CancellationToken.None);
        }
        public string Acknowledgement(string originalGroup = "123456789", string responseControl = "555555555") =>
            Wrap("999", "FA", "005010X231A1", $"AK1*HC*{originalGroup}*005010X222A1~AK2*837*0001*005010X222A1~IK5*A~AK9*A*1*1*1~", responseControl);
        public string ClaimAcknowledgement(string category = "A2", string line = "900") =>
            Wrap("277", "HN", "005010X214", "BHT*0085*08*CLAIMRECEIPT*20260901*1200*TH~HL*1**20*1~NM1*PR*2*TEST PAYER*****PI*MCDME~" +
                "HL*2*1*21*1~NM1*41*2*SYNTHETIC ONE*****46*SUBMITTER~HL*3*2*19*1~NM1*85*2*SYNTHETIC ONE*****XX*1999999984~" +
                $"HL*4*3*PT*0~NM1*QC*1*PERSON*TEST****MI*123456789~TRN*2*123456789-77-900~STC*{category}:20:PR*20260901*WQ*100~" +
                (category == "A2" ? $"SVC*HC:G9012*100~STC*A2:20:PR*20260901*WQ*100~REF*FJ*{line}~" : string.Empty), "555555554");

        public async Task AddSecondGenerationAsync()
        {
            await using var db = Context();
            var original = await db.ClaimLines.AsNoTracking().SingleAsync();
            var period = await db.BillingPeriods.Include(x => x.Lines).SingleAsync(x => x.Id == 78);
            period.Lines.Add(new ServerClaimLine { NoteId = 901, BillingPeriodId = 78, DateOfService = original.DateOfService.AddMonths(1),
                ProcedureCode = original.ProcedureCode, Units = original.Units, ChargeAmount = original.ChargeAmount,
                ClientMaineCareId = original.ClientMaineCareId, RenderingProviderNpi = original.RenderingProviderNpi,
                DiagnosisCode = original.DiagnosisCode, PlaceOfService = original.PlaceOfService, ClaimSnapshotJson = original.ClaimSnapshotJson });
            db.EdiGenerations.Add(new ServerEdiGeneration { AgencyId = 1, ActorUserId = 7, BillingPeriodId = 78, IsTest = true,
                IdempotencyKey = "synthetic-second", FileName = "second.837", ControlNumber = "987654321",
                Content = ServerEdiGenerator.Generate(period, true, new DateTime(2026, 8, 1), "987654321") });
            await db.SaveChangesAsync();
        }
        public string MultiPeriodRemittance()
        {
            var original = Remittance();
            var body = original[original.IndexOf("BPR*", StringComparison.Ordinal)..original.IndexOf("SE*", StringComparison.Ordinal)]
                .Replace("BPR*I*100", "BPR*I*200", StringComparison.Ordinal);
            body += "LX*2~CLP*987654321-78-901*1*100*100*0*MC*SECONDCLAIM*11*1~NM1*QC*1*PERSON*TEST****MI*123456789~SVC*HC:G9012*100*100~REF*6R*901~";
            return Wrap("835", "HP", "005010X221A1", body, "555555555");
        }
        public string Remittance(string responseControl = "555555555", string transaction = "0001", bool denied = false) =>
            Wrap("835", "HP", "005010X221A1",
                $"BPR*I*{(denied ? "0" : "100")}*C*{(denied ? "NON" : "CHK")}************20260901~TRN*1*{(denied ? "DENIAL-ONE" : "PAYMENT-ONE")}*1234567890~N1*PR*TEST PAYER*XV*MCDME~N1*PE*SYNTHETIC ONE*XX*1999999984~LX*1~" +
                $"CLP*123456789-77-900*{(denied ? "4*100*0" : "1*100*100")}*0*MC*PAYERCLAIM*11*1~" +
                (denied ? "CAS*CO*29*100~" : "") +
                $"NM1*QC*1*PERSON*TEST****MI*123456789~SVC*HC:G9012*100*{(denied ? "0" : "100")}~REF*6R*900~",
                responseControl, transaction);
        private static string Wrap(string type, string group, string version, string body, string control, string transaction = "0001")
        {
            var count = body.Count(c => c == '~') + 2;
            return $"ISA*00*          *00*          *ZZ*330897513      *ZZ*SUBMITTER      *260901*1200*^*00501*{control}*0*T*:~" +
                $"GS*{group}*330897513*SUBMITTER*20260901*1200*{control}*X*{version}~ST*{type}*{transaction}*{version}~" + body +
                $"SE*{count}*{transaction}~GE*1*{control}~IEA*1*{control}~";
        }
        public async Task AssertCountsAsync(int receipts, int events, int outcomes, int deposits, int audits)
        {
            await using var db = Context();
            Assert.Equal(receipts, await db.ClearinghouseResponseReceipts.CountAsync());
            Assert.Equal(events, await db.BillingSubmissionEvents.CountAsync());
            Assert.Equal(outcomes, await db.RemittanceClaimOutcomes.CountAsync());
            Assert.Equal(deposits, await db.RemittanceDeposits.CountAsync());
            Assert.Equal(audits, await db.AuditEvents.CountAsync());
        }
        public ValueTask DisposeAsync() => _anchor.DisposeAsync();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

[Collection(SatiApiCollection.Name)]
public sealed class ClearinghouseResponseHttpTests(SatiApiFactory factory)
{
    [Theory]
    [InlineData("case-manager-one")]
    [InlineData("admin-without-billing-one")]
    public async Task NewIntakeRouteRequiresLiveBillingPermission(string username)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(username);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest("invalid"))).StatusCode);
    }
    [Fact]
    public async Task NewIntakeRouteRequiresAuthentication()
    {
        using var client = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest("invalid"))).StatusCode);
    }
}
