using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class IncidentOutboxTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SatiIncidentOutboxTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnvelopeSurvivesUntilAcknowledgedAndDuplicateReferenceIsNotQueuedTwice()
    {
        var outbox = new IncidentOutbox(_root);
        var report = Report("REF_OUTBOX01");

        outbox.Enqueue(report, 91, 7);
        outbox.Enqueue(report, 91, 7);

        var pending = Assert.Single(outbox.ReadPending());
        Assert.Equal(report, pending.Report);

        outbox.Complete(pending);
        Assert.Empty(outbox.ReadPending());
    }

    [Fact]
    public void MatchedCrashDiagnosticReplacesQueuedPendingEnvelopeWithTheSameReference()
    {
        var outbox = new IncidentOutbox(_root);
        var heartbeat = DateTime.UtcNow.AddSeconds(-10);
        var pending = Report("REF_CRASH_UPGRADE") with
        {
            CrashDiagnostic = new CrashDiagnosticDto(
                CrashDiagnosticStatuses.PendingOrUnavailable,
                1234,
                "Sati",
                heartbeat)
        };
        var matched = pending with
        {
            CrashDiagnostic = new CrashDiagnosticDto(
                CrashDiagnosticStatuses.Matched,
                1234,
                "Sati",
                heartbeat,
                1000,
                44,
                heartbeat.AddSeconds(2),
                "Application Error",
                "Sati.exe",
                "1.3.6.0",
                "coreclr.dll",
                "10.0.12.345",
                "0xC0000005",
                "0x000000000001ABCD",
                true)
        };

        outbox.Enqueue(pending, 91, 7);
        var inFlight = Assert.Single(outbox.ReadPending());
        outbox.Enqueue(matched, 91, 7);
        outbox.Complete(inFlight);

        var upgraded = Assert.Single(outbox.ReadPending());
        Assert.Equal(CrashDiagnosticStatuses.Matched, upgraded.Report.CrashDiagnostic?.Status);
    }

    [Fact]
    public async Task ReporterRetriesTheDurableEnvelopeAfterServiceRecovery()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("test-token", null, 91, 7);
        var outbox = new IncidentOutbox(_root);
        using var reporter = new CloudIncidentReporter(api, outbox, Session(91, 7),
            TimeProvider.System);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_OUTBOX02");

        Assert.Single(outbox.ReadPending());

        handler.IsAvailable = true;
        await reporter.FlushAsync();

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task QueuedIncidentIsNotSentUsingTheNextAccountsCredential()
    {
        var handler = new RecoveringHandler { IsAvailable = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("account-A-token", null, 91, 7);
        var outbox = new IncidentOutbox(_root);
        var session = Session(91, 7);
        using var reporter = new CloudIncidentReporter(api, outbox, session,
            TimeProvider.System);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_ACCOUNT_A");
        Assert.Single(outbox.ReadPending());

        handler.IsAvailable = true;
        api.SetAuthenticatedAccessToken("account-B-token", null, 92, 8);
        // Login installs B's token before the shell installs B's user. A flush in
        // this gap must not send A's outbox with B's new credential.
        await reporter.FlushAsync();
        Assert.Equal(1, handler.RequestCount);
        session.SetUser(User.Create(92, "second", "Second Account", string.Empty,
            string.Empty, UserRole.CaseManager, null, 8));
        await reporter.FlushAsync();

        Assert.Equal(1, handler.RequestCount);
        Assert.Single(outbox.ReadPending());
        api.SetAuthenticatedAccessToken("account-A-token-again", null, 91, 7);
        session.SetUser(User.Create(91, "first", "First Account", string.Empty,
            string.Empty, UserRole.CaseManager, null, 7));
        await reporter.FlushAsync();
        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TransientFailureRetriesDuringTheSameSessionWithoutAnotherUserAction()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("test-token", null, 91, 7);
        var outbox = new IncidentOutbox(_root);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new CloudIncidentReporter(api, outbox, Session(91, 7),
            TimeProvider.System,
            async (_, token) => { waiting.TrySetResult(); await resume.Task.WaitAsync(token); },
            () => 0.5);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_RETRY_IDLE");
        Assert.Single(outbox.ReadPending());

        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        handler.IsAvailable = true;
        resume.TrySetResult();
        await WaitUntilAsync(() => outbox.ReadPending().Count == 0);

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task BackgroundRetryStopsWhenNextAccountsTokenArrivesBeforeUserSwitch()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("account-A-token", null, 91, 7);
        var session = Session(91, 7);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outbox = new IncidentOutbox(_root);
        using var reporter = new CloudIncidentReporter(api, outbox, session,
            TimeProvider.System,
            async (_, token) => { waiting.TrySetResult(); await resume.Task.WaitAsync(token); },
            () => 0.5);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_A_RETRY");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, handler.RequestCount);

        // CloudAuthService sets the credential before ShellWindow sets the new user.
        // The retry must not capture that credential for A's queued incident.
        handler.IsAvailable = true;
        var retryTask = reporter.CurrentRetryTask;
        api.SetAuthenticatedAccessToken("account-B-token", null, 92, 8);
        Assert.Null(reporter.CurrentRetryTask);
        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_DURING_SWITCH");
        resume.TrySetResult();
        if (retryTask is not null)
            await retryTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("REF_A_RETRY", Assert.Single(outbox.ReadPending()).Report.Reference);
    }

    [Fact]
    public async Task BackgroundRetryStopsAsSoonAsTheAuthenticatedSessionEnds()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("account-A-token", null, 91, 7);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outbox = new IncidentOutbox(_root);
        using var reporter = new CloudIncidentReporter(api, outbox, Session(91, 7),
            TimeProvider.System,
            async (_, token) => { waiting.TrySetResult(); await resume.Task.WaitAsync(token); },
            () => 0.5);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "calendar.day.open", "REF_SIGNOUT_RETRY");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var retryTask = reporter.CurrentRetryTask;

        handler.IsAvailable = true;
        api.InvalidateCurrentSession();
        Assert.Null(reporter.CurrentRetryTask);
        resume.TrySetResult();
        if (retryTask is not null)
            await retryTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, handler.RequestCount);
        Assert.Single(outbox.ReadPending());
    }

    [Fact]
    public async Task PermanentlyInvalidEnvelopeDoesNotBlockTheNextValidReport()
    {
        var handler = new RejectFirstHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("test-token", null, 91, 7);
        var outbox = new IncidentOutbox(_root);
        outbox.Enqueue(Report("REF_INVALID01"), 91, 7);
        outbox.Enqueue(Report("REF_VALID02"), 91, 7);

        using var reporter = new CloudIncidentReporter(api, outbox, Session(91, 7),
            TimeProvider.System);
        await reporter.FlushAsync();

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(2, handler.RequestCount);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(_root, "Rejected"), "*.json"));
    }

    [Fact]
    public async Task NextMatchingSessionReportsAnUncleanPreviousTerminationOnce()
    {
        var runStateRoot = Path.Combine(_root, "run-state");
        var user = User.Create(
            91, "case-manager", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 7);
        var firstReporter = new CapturingReporter();
        var firstRun = new ApplicationRunState(runStateRoot);
        await firstRun.StartSessionAsync(user, firstReporter);
        Assert.Empty(firstReporter.Operations);

        var recoveryReporter = new CapturingReporter();
        var recoveryRun = new ApplicationRunState(runStateRoot);
        await recoveryRun.StartSessionAsync(user, recoveryReporter);

        Assert.Equal(["application.previous-session-unclean"], recoveryReporter.Operations);

        recoveryRun.MarkGracefulExit();
        var cleanReporter = new CapturingReporter();
        await new ApplicationRunState(runStateRoot).StartSessionAsync(user, cleanReporter);
        Assert.Empty(cleanReporter.Operations);
    }

    [Fact]
    public void LegacyUnscopedEnvelopeIsQuarantinedAndNeverSent()
    {
        var pendingDirectory = Path.Combine(_root, "Pending");
        Directory.CreateDirectory(pendingDirectory);
        File.WriteAllText(Path.Combine(pendingDirectory, "legacy.json"),
            System.Text.Json.JsonSerializer.Serialize(Report("REF_LEGACY")));

        var outbox = new IncidentOutbox(_root);
        Assert.Empty(outbox.ReadPendingFor(91, 7));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "Rejected"), "*.json"));
    }

    [Fact]
    public void AccountQueuesStaySeparateAndOldUnreturnedEnvelopeIsQuarantined()
    {
        var outbox = new IncidentOutbox(_root);
        var now = DateTime.UtcNow;
        outbox.Enqueue(Report("REF_A01"), 91, 7, now.AddDays(-31));
        outbox.Enqueue(Report("REF_A02"), 91, 7, now.AddHours(-2));
        outbox.Enqueue(Report("REF_B01"), 92, 8, now.AddHours(-1));
        outbox.Enqueue(Report("REF_A03"), 91, 7, now.AddMinutes(-30));

        Assert.Equal(["REF_A02", "REF_A03"],
            outbox.ReadPendingFor(91, 7, now).Select(item => item.Report.Reference));
        Assert.Equal("REF_B01", Assert.Single(outbox.ReadPendingFor(92, 8, now)).Report.Reference);
        var status = outbox.StatusFor(91, 7, now);
        Assert.Equal(2, status.PendingForCurrentAccount);
        Assert.InRange(status.OldestPendingAge!.Value.TotalMinutes, 119, 121);
        Assert.Equal(1, status.HeldForOtherAccounts);
        Assert.Equal(1, status.Quarantined);
    }

    [Fact]
    public async Task PlatformOperatorWithAgencyZeroCanDeliverItsOwnEnvelope()
    {
        var handler = new RecoveringHandler { IsAvailable = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAuthenticatedAccessToken("operator-token", null, 93, 0);
        var session = new SessionService();
        session.SetUser(User.Create(93, "operator", "Platform Operator", string.Empty,
            string.Empty, UserRole.PlatformOperator, null, 0));
        var outbox = new IncidentOutbox(_root);
        using var reporter = new CloudIncidentReporter(api, outbox, session,
            TimeProvider.System);

        await reporter.ReportAsync(new InvalidOperationException("local only"),
            "platform.health.open", "REF_PLATFORM01");

        Assert.Empty(outbox.ReadPending());
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(0, reporter.GetOutboxStatus()?.PendingForCurrentAccount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static IncidentReportRequest Report(string reference) => new(
        reference,
        "Desktop",
        IncidentSeverities.Error,
        "calendar.day.open",
        "1.2.5",
        "ABCDEF0123456789ABCDEF0123456789",
        DateTime.UtcNow);

    private static SessionService Session(int userId, int agencyId)
    {
        var session = new SessionService();
        session.SetUser(User.Create(userId, "synthetic", "Synthetic User", string.Empty,
            string.Empty, UserRole.CaseManager, null, agencyId));
        return session;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var limit = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < limit)
            await Task.Delay(20);
        Assert.True(predicate());
    }

    private sealed class RecoveringHandler : HttpMessageHandler
    {
        public bool IsAvailable { get; set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (!IsAvailable)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var now = DateTime.UtcNow;
            var dto = new IncidentGroupDto(
                1, 1, IncidentScopes.Agency, "Desktop", IncidentSeverities.Error,
                "calendar.day.open", "1.2.5", "1.2.5",
                "ABCDEF0123456789ABCDEF0123456789", "Open", 1,
                now, now, "REF_OUTBOX02", "CaseManager");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(dto)
            });
        }
    }

    private sealed class RejectFirstHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            var now = DateTime.UtcNow;
            var dto = new IncidentGroupDto(
                1, 1, IncidentScopes.Agency, "Desktop", IncidentSeverities.Error,
                "calendar.day.open", "1.2.5", "1.2.5",
                "ABCDEF0123456789ABCDEF0123456789", "Open", 1,
                now, now, "REF_VALID02", "CaseManager");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(dto)
            });
        }
    }

    private sealed class CapturingReporter : IIncidentReporter
    {
        public List<string> Operations { get; } = [];

        public Task ReportAsync(
            Exception exception,
            string operation,
            string reference,
            string severity = "Error",
            CancellationToken cancellationToken = default)
        {
            Operations.Add(operation);
            return Task.CompletedTask;
        }
    }
}
