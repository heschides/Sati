using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Data.Cloud;
using Sati.Edi;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Sati.ViewModels.Billing;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ClearinghouseResponseImportTests
{
    private static User Biller(int id = 7) => User.Create(id, "biller", "Synthetic Biller", "hash", "salt", UserRole.Admin, null, 1);
    private static ClaimResponseIngestResultDto Receipt(bool duplicate = false) =>
        new(nameof(ClaimResponseKind.RemittanceAdvice), true, "RemittanceReceived", duplicate ? 0 : 1, !duplicate, "Safe receipt")
        {
            ResponseId = Guid.Parse("a71b4d3e-d5e0-47c1-a9c0-18c2f8bca3e1"),
            AlreadyImported = duplicate,
            BillingPeriodIds = [77]
        };

    [Fact]
    public void ImportButtonRendersAndBindsToTheAccountScopedCommand()
    {
        WpfUiHarness.Run(() =>
        {
            var session = new SessionService(); session.SetUser(Biller());
            var vm = new BillingSubmissionsViewModel(new BillingStub(), new EdiStub(), session,
                new Picker(_ => Task.FromResult<string?>(null)));
            var view = new Sati.Views.Billing.BillingSubmissionsView { DataContext = vm };
            WpfUiHarness.Realize(view, 1100, 900);
            var button = WpfUiHarness.FindByAutomationName<System.Windows.Controls.Button>(view,
                "Import clearinghouse response file");
            Assert.Same(vm.ImportResponseCommand, button.Command);
            Assert.True(button.IsEnabled);
            vm.IsImportingResponse = true;
            WpfUiHarness.Realize(view, 1100, 900);
            Assert.False(button.IsEnabled);
        });
    }

    [Fact]
    public async Task AChosenFileNeverPostsAfterTheAccountChanges()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var pending = new TaskCompletionSource<string?>();
        var billing = new BillingStub();
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => pending.Task));
        var operation = vm.ImportResponseCommand.ExecuteAsync(null);
        vm.ClearForAccountSwitch(); session.SetUser(Biller(8));
        pending.SetResult("SYNTHETIC RESPONSE");
        await operation;
        Assert.Equal(0, billing.ImportCalls);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.IsImportingResponse);
    }

    [Fact]
    public async Task ALateReceiptCannotAppearUnderAnotherAccount()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var pending = new TaskCompletionSource<ClaimResponseIngestResultDto>();
        var billing = new BillingStub { Import = () => pending.Task };
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => Task.FromResult<string?>("SYNTHETIC RESPONSE")));
        var operation = vm.ImportResponseCommand.ExecuteAsync(null);
        Assert.Equal(1, billing.ImportCalls);
        vm.ClearForAccountSwitch(); session.SetUser(Biller(8));
        pending.SetResult(Receipt());
        await operation;
        Assert.Null(vm.StatusMessage);
        Assert.Empty(vm.SubmissionHistory);
    }

    [Fact]
    public async Task ARefreshFailureDoesNotMisrepresentACommittedImport()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var billing = new BillingStub { FailHistory = true };
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => Task.FromResult<string?>("SYNTHETIC RESPONSE")));
        await vm.ImportResponseCommand.ExecuteAsync(null);
        Assert.Contains("was recorded", vm.StatusMessage);
        Assert.DoesNotContain("PRIVATE_PAYLOAD", vm.StatusMessage);
        Assert.False(vm.IsImportingResponse);
    }

    [Fact]
    public async Task DuplicateReceiptExplainsThatNoFinancialRowsWereRepeated()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var billing = new BillingStub { Import = () => Task.FromResult(Receipt(true)) };
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => Task.FromResult<string?>("SYNTHETIC RESPONSE")));
        await vm.ImportResponseCommand.ExecuteAsync(null);
        Assert.Contains("already imported", vm.StatusMessage);
        Assert.Contains("No records were duplicated", vm.StatusMessage);
    }

    [Fact]
    public async Task ArbitraryExceptionContentsNeverReachTheImportStatus()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var billing = new BillingStub { Import = () => throw new InvalidOperationException("PRIVATE_PAYLOAD") };
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => Task.FromResult<string?>("SYNTHETIC RESPONSE")));
        await vm.ImportResponseCommand.ExecuteAsync(null);
        Assert.DoesNotContain("PRIVATE_PAYLOAD", vm.StatusMessage);
        Assert.Contains("could not confirm", vm.StatusMessage);
    }

    [Fact]
    public async Task CancellingTheFileDialogWritesNothing()
    {
        var session = new SessionService(); session.SetUser(Biller());
        var billing = new BillingStub();
        var vm = new BillingSubmissionsViewModel(billing, new EdiStub(), session, new Picker(_ => Task.FromResult<string?>(null)));
        await vm.ImportResponseCommand.ExecuteAsync(null);
        Assert.Equal(0, billing.ImportCalls);
        Assert.False(vm.IsImportingResponse);
    }

    [Fact]
    public async Task ResponseUploadKeepsOriginalCredentialAndDiscardsReceiptAfterReplacementSignIn()
    {
        var sent = new TaskCompletionSource<HttpRequestMessage>();
        var completion = new TaskCompletionSource<HttpResponseMessage>();
        using var handler = new Handler(request => { sent.SetResult(request); return completion.Task; });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://synthetic.invalid") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("original-synthetic-token", DateTimeOffset.UtcNow.AddMinutes(1));
        var service = new CloudBillingService(api);
        var upload = service.ImportResponseAsync(Biller().ToAgencyActor(), "SYNTHETIC RESPONSE");
        var request = await sent.Task;
        api.SetAccessToken("replacement-synthetic-token");
        Assert.Equal("/api/v1/billing/responses", request.RequestUri!.AbsolutePath);
        Assert.Equal("original-synthetic-token", request.Headers.Authorization!.Parameter);
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Receipt()) });
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => upload);
        Assert.False(api.HasSessionEnded);
    }

    [Fact]
    public async Task FileReadingPreservesOriginalAsciiBytesAndLineEndings()
    {
        const string content = "ISA*UNCHANGED~\r\nST*999~\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
        Assert.Equal(content, await ClearinghouseResponseFile.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(9)]
    public async Task NonAsciiAndControlBytesAreRejected(byte unexpected)
    {
        using var stream = new MemoryStream([65, unexpected, 66]);
        await Assert.ThrowsAsync<FormatException>(() => ClearinghouseResponseFile.ReadAsync(stream, default));
    }

    [Fact]
    public async Task OversizedFileIsRejectedBeforeItsContentsAreRead()
    {
        using var stream = new LengthOnlyStream(ClaimResponseReader.MaximumDocumentCharacters + 1L);
        await Assert.ThrowsAsync<FormatException>(() => ClearinghouseResponseFile.ReadAsync(stream, default));
    }

    private sealed class Picker(Func<CancellationToken, Task<string?>> read) : IClearinghouseResponseFilePicker
    { public Task<string?> ReadResponseAsync(CancellationToken cancellationToken) => read(cancellationToken); }

    private sealed class EdiStub : IEdiService
    { public Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey) => throw new NotSupportedException(); }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request); }

    private sealed class LengthOnlyStream(long length) : MemoryStream
    { public override long Length => length; }

    private sealed class BillingStub : IBillingService
    {
        public bool SupportsResponseImport => true;
        public int ImportCalls { get; private set; }
        public bool FailHistory { get; init; }
        public Func<Task<ClaimResponseIngestResultDto>> Import { get; init; } = () => Task.FromResult(Receipt());
        public Task<ClaimResponseIngestResultDto> ImportResponseAsync(AgencyActor actor, string document, CancellationToken cancellationToken = default)
        { ImportCalls++; return Import(); }
        public Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor) => FailHistory
            ? throw new InvalidOperationException("PRIVATE_PAYLOAD") : Task.FromResult<IReadOnlyList<BillingSubmissionHistoryDto>>([]);
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor) => Task.FromResult<IEnumerable<BillingPeriod>>([]);
        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor) => throw new NotSupportedException();
        public BillingValidationResult ValidateNoteForBilling(Note note) => throw new NotSupportedException();
        public Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor) => throw new NotSupportedException();
    }
}
