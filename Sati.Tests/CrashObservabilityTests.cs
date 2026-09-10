using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class CrashObservabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SatiCrashObservabilityTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ApplicationErrorParserKeepsOnlyStructuredMetadata()
    {
        const string protectedText = "failed to save a note for Jane Example";
        var xml = $$"""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <Provider Name="Application Error" />
                <EventID>1000</EventID>
                <TimeCreated SystemTime="2026-09-10T04:15:30.0000000Z" />
              </System>
              <EventData>
                <Data Name="AppName">Sati.exe</Data>
                <Data Name="AppVersion">1.3.6.0</Data>
                <Data Name="ModuleName">coreclr.dll</Data>
                <Data Name="ModuleVersion">10.0.12.345</Data>
                <Data Name="ExceptionCode">c0000005</Data>
                <Data Name="FaultingOffset">000000000001ABCD</Data>
                <Data Name="ProcessId">0x04D2</Data>
                <Data Name="AppPath">C:\Users\Jane Example\Sati.exe</Data>
                <Data Name="ExceptionMessage">{{protectedText}}</Data>
              </EventData>
            </Event>
            """;

        var parsed = Assert.IsType<WindowsApplicationErrorCandidate>(
            WindowsCrashEventReader.ParseApplicationErrorXml(xml));

        Assert.Equal(1234, parsed.ProcessId);
        Assert.Equal("Sati.exe", parsed.FaultingApplication);
        Assert.Equal("coreclr.dll", parsed.FaultingModule);
        Assert.Equal("0xC0000005", parsed.ExceptionCode);
        var diagnostic = new CrashDiagnosticDto(
            CrashDiagnosticStatuses.Matched,
            parsed.ProcessId,
            "Sati",
            new DateTime(2026, 9, 10, 4, 15, 0, DateTimeKind.Utc),
            1000,
            42,
            parsed.OccurredAtUtc,
            "Application Error",
            parsed.FaultingApplication,
            parsed.FaultingApplicationVersion,
            parsed.FaultingModule,
            parsed.FaultingModuleVersion,
            parsed.ExceptionCode,
            parsed.FaultOffset,
            true);
        var persisted = CrashDiagnosticRules.Serialize(diagnostic);

        Assert.DoesNotContain(protectedText, persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jane Example", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppPath", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExceptionMessage", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventQueryIsProviderIdAndHeartbeatBounded()
    {
        var start = new DateTime(2026, 9, 10, 4, 14, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 10, 4, 45, 0, DateTimeKind.Utc);

        var xpath = WindowsCrashEventReader.BuildXPath(start, end);

        Assert.Contains("Application Error", xpath);
        Assert.Contains("EventID=1000", xpath);
        Assert.Contains(".NET Runtime", xpath);
        Assert.Contains("EventID=1026", xpath);
        Assert.Contains("2026-09-10T04:14:00Z", xpath);
        Assert.Contains("2026-09-10T04:45:00Z", xpath);
    }

    [Fact]
    public void EventSelectionRequiresBothPersistedPidAndProcessName()
    {
        var heartbeat = new DateTime(2026, 9, 10, 4, 15, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Candidate(44, heartbeat.AddSeconds(2), 9999, "Sati.exe"),
            Candidate(45, heartbeat.AddSeconds(3), 1234, "Other.exe"),
            Candidate(46, heartbeat.AddSeconds(4), 1234, "Sati.exe")
        };

        var match = WindowsCrashEventReader.SelectMatch(
            events,
            [
                new WindowsRuntimeEventCandidate(9999, heartbeat.AddSeconds(4)),
                new WindowsRuntimeEventCandidate(1234, heartbeat.AddSeconds(5))
            ],
            1234,
            "Sati",
            heartbeat);

        Assert.NotNull(match);
        Assert.Equal(46, match.RecordId);
        Assert.True(match.DotNetRuntimeEventObserved);
        Assert.Null(WindowsCrashEventReader.SelectMatch(events, [], 1234, "Sati.Demo", heartbeat));
        Assert.Null(WindowsCrashEventReader.SelectMatch(events, [], 7777, "Sati", heartbeat));
    }

    [Fact]
    public async Task RunMarkerCarriesPidAndHeartbeatAndReadbackRetriesIntoSameIncidentReference()
    {
        var now = new DateTime(2026, 9, 10, 4, 15, 0, DateTimeKind.Utc);
        var user = User.Create(
            91, "case-manager", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 7);
        var runDirectory = Path.Combine(_root, "run-state");
        var first = State(runDirectory, new SequenceEventReader([]), now);
        await first.StartSessionAsync(user, new CapturingReporter());

        var activePath = Path.Combine(runDirectory, "agency-7-agency.json");
        var firstMarker = JsonSerializer.Deserialize<ApplicationRunMarker>(
            File.ReadAllText(activePath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(firstMarker);
        Assert.Equal(Environment.ProcessId, firstMarker.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().ProcessName, firstMarker.ProcessName);
        Assert.Equal(now, firstMarker.LastHeartbeatUtc);
        Assert.StartsWith("CRASH", firstMarker.SessionReference);

        var match = Match(now.AddSeconds(12));
        var reader = new SequenceEventReader([
            new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound),
            new WindowsCrashLookupResult(WindowsCrashLookupState.Matched, match)]);
        var reporter = new CapturingReporter();
        var recovery = State(runDirectory, reader, now.AddMinutes(1));
        await recovery.StartSessionAsync(user, reporter);

        var report = Assert.Single(reporter.Crashes);
        Assert.Equal(firstMarker.SessionReference, report.Reference);
        Assert.Equal(CrashDiagnosticStatuses.Matched, report.Diagnostic.Status);
        Assert.Equal(2, reader.CallCount);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(runDirectory, "Pending"), "crash-*.json"));
        recovery.MarkGracefulExit();
    }

    [Fact]
    public async Task ActiveRunMarkerHeartbeatAdvancesWithoutUserActivity()
    {
        var current = new DateTime(2026, 9, 10, 4, 15, 0, DateTimeKind.Utc);
        var user = User.Create(
            91, "case-manager", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 7);
        var runDirectory = Path.Combine(_root, "heartbeat-run-state");
        var state = new ApplicationRunState(
            runDirectory,
            new SequenceEventReader([]),
            TimeSpan.FromMilliseconds(20),
            () => current,
            (_, _) => Task.CompletedTask,
            Path.Combine(_root, "logs"));
        await state.StartSessionAsync(user, new CapturingReporter());
        current = current.AddMinutes(1);

        var activePath = Path.Combine(runDirectory, "agency-7-agency.json");
        ApplicationRunMarker? marker = null;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await Task.Delay(20);
            try
            {
                marker = JsonSerializer.Deserialize<ApplicationRunMarker>(
                    File.ReadAllText(activePath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (marker?.LastHeartbeatUtc == current)
                    break;
            }
            catch (IOException)
            {
                // Atomic replacement can briefly move the old file out of the way.
            }
        }

        Assert.Equal(current, marker?.LastHeartbeatUtc);
        state.MarkGracefulExit();
    }

    [Fact]
    public async Task MissingWerRecordRemainsPendingAndLaterMatchUpgradesWithoutNewReference()
    {
        var now = new DateTime(2026, 9, 10, 4, 15, 0, DateTimeKind.Utc);
        var user = User.Create(
            91, "case-manager", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 7);
        var runDirectory = Path.Combine(_root, "pending-run-state");
        var first = State(runDirectory, new SequenceEventReader([]), now);
        await first.StartSessionAsync(user, new CapturingReporter());

        var pendingReader = new SequenceEventReader([
            new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound),
            new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound)]);
        var pendingReporter = new CapturingReporter();
        var second = State(runDirectory, pendingReader, now.AddMinutes(1));
        await second.StartSessionAsync(user, pendingReporter);
        var pending = Assert.Single(pendingReporter.Crashes);
        Assert.Equal(CrashDiagnosticStatuses.PendingOrUnavailable, pending.Diagnostic.Status);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(runDirectory, "Pending"), "crash-*.json"));
        second.MarkGracefulExit();

        var matchedReporter = new CapturingReporter();
        var third = State(
            runDirectory,
            new SequenceEventReader([new WindowsCrashLookupResult(
                WindowsCrashLookupState.Matched,
                Match(now.AddSeconds(10)))]),
            now.AddMinutes(2));
        await third.StartSessionAsync(user, matchedReporter);

        var matched = Assert.Single(matchedReporter.Crashes);
        Assert.Equal(pending.Reference, matched.Reference);
        Assert.Equal(CrashDiagnosticStatuses.Matched, matched.Diagnostic.Status);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(runDirectory, "Pending"), "crash-*.json"));
        third.MarkGracefulExit();
    }

    [Fact]
    public async Task LocalIncidentAggregationPersistsOnlyTheBetterDiagnosticForAStableReference()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var factory = new ContextFactory(
            new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options);
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 7, Name = "Synthetic Agency" });
            setup.Users.Add(User.Create(
                91, "admin", "Admin", "hash", "salt", UserRole.Admin, null, 7));
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(User.Create(
            91, "admin", "Admin", "hash", "salt", UserRole.Admin, null, 7));
        var reporter = new LocalIncidentReporter(factory, session);
        var heartbeat = DateTime.UtcNow.AddSeconds(-10);
        var pending = new CrashDiagnosticDto(
            CrashDiagnosticStatuses.PendingOrUnavailable,
            1234,
            "Sati",
            heartbeat);
        var matched = new CrashDiagnosticDto(
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
            true);
        var exception = new UnexpectedApplicationTerminationException();

        await reporter.ReportCrashAsync(
            exception, "application.previous-session-unclean", "REF_LOCAL_CRASH", pending);
        await reporter.ReportCrashAsync(
            exception, "application.previous-session-unclean", "REF_LOCAL_CRASH", matched);

        await using var verification = factory.CreateDbContext();
        var incident = await verification.IncidentGroups.SingleAsync();
        Assert.Equal(1, incident.OccurrenceCount);
        Assert.Equal(CrashDiagnosticStatuses.Matched,
            CrashDiagnosticRules.Deserialize(incident.LastCrashDiagnosticJson)?.Status);
        Assert.Equal("coreclr.dll",
            IncidentContractMapper.ToDto(incident).LastCrashDiagnostic?.FaultingModule);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ApplicationRunState State(string directory, IWindowsCrashEventReader reader, DateTime now) =>
        new(
            directory,
            reader,
            Timeout.InfiniteTimeSpan,
            () => now,
            (_, _) => Task.CompletedTask,
            Path.Combine(_root, "logs"));

    private static WindowsCrashEventMatch Match(DateTime atUtc) => new(
        44,
        atUtc,
        "Application Error",
        "Sati.exe",
        "1.3.6.0",
        "coreclr.dll",
        "10.0.12.345",
        "0xC0000005",
        "0x000000000001ABCD",
        true);

    private static WindowsApplicationErrorCandidate Candidate(
        long recordId,
        DateTime atUtc,
        int processId,
        string application) => new(
        recordId,
        atUtc,
        processId,
        application,
        "1.3.6.0",
        "coreclr.dll",
        "10.0.12.345",
        "0xC0000005",
        "0x000000000001ABCD");

    private sealed class SequenceEventReader(IEnumerable<WindowsCrashLookupResult> results)
        : IWindowsCrashEventReader
    {
        private readonly Queue<WindowsCrashLookupResult> _results = new(results);
        public int CallCount { get; private set; }

        public Task<WindowsCrashLookupResult> FindAsync(
            int processId,
            string processName,
            DateTime lastHeartbeatUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound));
        }
    }

    private sealed class CapturingReporter : IIncidentReporter
    {
        public List<(string Reference, CrashDiagnosticDto Diagnostic)> Crashes { get; } = [];

        public Task ReportAsync(
            Exception exception,
            string operation,
            string reference,
            string severity = IncidentSeverities.Error,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReportCrashAsync(
            Exception exception,
            string operation,
            string reference,
            CrashDiagnosticDto diagnostic,
            string severity = IncidentSeverities.Critical,
            CancellationToken cancellationToken = default)
        {
            Crashes.Add((reference, diagnostic));
            return Task.CompletedTask;
        }
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
