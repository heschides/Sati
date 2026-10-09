using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;
using Xunit.Abstractions;

namespace Sati.Api.Tests;

[Collection(ApiExceptionRedactionCollection.Name)]
public sealed class ApiExceptionRedactionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EscapingAuthenticatedWriteFailurePersistsIncidentAfterSingleAttemptFilterUnwinds()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.ConfiguredIncident);
        using var response = await harness.Client.PostAsync(harness.Probe.Path, content: null);

        await AssertGenericErrorAsync(harness, response);
        await harness.Diagnostics.WaitForStopAsync();
        AssertSafeHeaders(harness, response);
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.True(harness.Probe.Authenticated);
        Assert.True(harness.Probe.HadSingleAttemptScope);
        var observed = Assert.IsType<ConfiguredIncidentFactory>(harness.ConfiguredIncidents);
        Assert.Equal(1, observed.IncidentContexts);
        Assert.True(observed.IncidentStrategyRetries);
        Assert.True(observed.IncidentOuterScopeWasAbsent);
        Assert.Null(ExecutionStrategy.Current);
        output.WriteLine($"Authenticated write entered={harness.Probe.Entered}; thrown={harness.Probe.Thrown}; " +
            $"filter single-attempt scope={harness.Probe.HadSingleAttemptScope}; " +
            $"incident contexts={observed.IncidentContexts}; configured retries={observed.IncidentStrategyRetries}; " +
            $"incident ambient scope absent={observed.IncidentOuterScopeWasAbsent}.");

        // The missing incident is the regression target. Provider error logging is
        // independent of containment of an exception escaping the request delegate.
        var incident = Assert.Single(await harness.IncidentsAsync());
        Assert.Equal(1, incident.AgencyId);
        Assert.Equal("Agency", incident.Scope);
        Assert.Equal("Api", incident.Source);
        Assert.Equal("Error", incident.Severity);
        Assert.Equal("Admin", incident.LastActorRole);
        Assert.Equal(harness.Probe.TraceIdentifier, incident.LastReference);
        Assert.Equal(1, incident.OccurrenceCount);
        Assert.Equal("POST.unmatched", incident.Operation);
        Assert.Null(incident.LastCrashDiagnosticJson);
        Assert.All(harness.Probe.Sentinels, sentinel =>
        {
            Assert.DoesNotContain(sentinel, harness.Logs.Text);
            Assert.DoesNotContain(sentinel, harness.Diagnostics.ExceptionText);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapingNestedOrUncancelledOperationFailureHasOnlySafeObservableEvidence(bool operationCancelled)
    {
        await using var harness = await BoundaryHarness.CreateAsync(
            operationCancelled ? FailureMode.UncancelledOperation : FailureMode.Nested);
        using var response = await harness.Client.GetAsync(harness.Probe.Path);

        await AssertGenericErrorAsync(harness, response);
        await harness.Diagnostics.WaitForStopAsync();
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.True(harness.Probe.Authenticated);
        var incident = Assert.Single(await harness.IncidentsAsync());
        Assert.Equal(1, incident.AgencyId);
        Assert.Equal("Agency", incident.Scope);
        Assert.Equal("Api", incident.Source);
        Assert.Equal("Error", incident.Severity);
        Assert.Equal("Admin", incident.LastActorRole);
        Assert.Equal(harness.Probe.TraceIdentifier, incident.LastReference);
        Assert.Equal(1, incident.OccurrenceCount);
        Assert.Equal(64, incident.ExceptionFingerprint.Length);
        Assert.Null(incident.LastCrashDiagnosticJson);
        Assert.DoesNotContain(harness.Probe.NarrativeSentinel, incident.Operation);
        Assert.Contains(harness.Logs.Entries, entry => entry.Level == LogLevel.Error &&
            entry.Message.Contains("Unhandled API error", StringComparison.Ordinal));
        AssertSafeObservedSinks(harness);
        AssertSafeHeaders(harness, response);
    }

    [Fact]
    public async Task ExceptionDataWithASafeMessageDoesNotEscapeThroughRawObservableReferences()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.DataOnly);
        using var response = await harness.Client.GetAsync(harness.Probe.Path);

        await AssertGenericErrorAsync(harness, response);
        await harness.Diagnostics.WaitForStopAsync();
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.True(harness.Probe.Authenticated);
        Assert.Single(await harness.IncidentsAsync());
        AssertSafeObservedSinks(harness);
        AssertSafeHeaders(harness, response);
    }

    [Fact]
    public async Task FailureAfterResponseStartsDoesNotExposeRawExceptionThroughHosting()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.StartedResponse);
        using var cleanup = new CancellationTokenSource();
        var request = harness.Client.GetAsync(harness.Probe.Path,
            HttpCompletionOption.ResponseHeadersRead, cleanup.Token);
        HttpResponseMessage? response = null;
        Exception? clientFailure = null;
        var waitStage = "ready";
        var waitElapsed = Stopwatch.StartNew();
        try
        {
            await harness.Probe.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(harness.Probe.ResponseStarted);
            waitStage = "headers";
            response = await request.WaitAsync(TimeSpan.FromSeconds(15));
            harness.Probe.Release.TrySetResult();
            waitStage = "body";
            clientFailure = await Record.ExceptionAsync(() => response.Content.ReadAsStringAsync(cleanup.Token));
            waitStage = "hosting-stop";
            await harness.Diagnostics.WaitForStopAsync();
        }
        catch (TimeoutException)
        {
            WriteSafeProbeTimeout(harness, request, "started-response", waitStage, waitElapsed);
            throw;
        }
        finally
        {
            harness.Probe.Release.TrySetResult();
            cleanup.Cancel();
            try { response ??= await request; } catch (Exception) { }
            response?.Dispose();
        }
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.NotNull(clientFailure);
        AssertSafeObservedSinks(harness);
        Assert.All(harness.Probe.Sentinels, sentinel =>
            Assert.DoesNotContain(sentinel, clientFailure?.ToString() ?? string.Empty));
        Assert.Single(await harness.IncidentsAsync());
    }

    [Fact]
    public async Task SecondaryErrorResponseWriteFailureDoesNotExposeEitherException()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.ResponseWriteFailure);
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await harness.Client.GetAsync(harness.Probe.Path);
            await response.Content.ReadAsStringAsync();
        });

        await harness.Diagnostics.WaitForStopAsync();
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.True(harness.Probe.WriteAttempts > 0, "The error handler must encounter the synthetic failing response stream.");
        Assert.NotNull(failure);
        AssertSafeObservedSinks(harness);
        Assert.All(harness.Probe.Sentinels, sentinel =>
            Assert.DoesNotContain(sentinel, failure?.ToString() ?? string.Empty));
        Assert.Single(await harness.IncidentsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAbortedCancellationOrIoFailurePreservesCancellationWithoutAnIncident(bool ioFailure)
    {
        await using var harness = await BoundaryHarness.CreateAsync(
            ioFailure ? FailureMode.CancelledIo : FailureMode.CancelledOperation);
        using var cleanup = new CancellationTokenSource();
        var request = harness.Client.GetAsync(harness.Probe.Path, cleanup.Token);
        HttpResponseMessage? response = null;
        var waitStage = "ready";
        var waitElapsed = Stopwatch.StartNew();
        try
        {
            await harness.Probe.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            waitStage = "abort-and-response";
            harness.Probe.RequestAbort.Cancel();
            response = await request.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(499, (int)response.StatusCode);
            waitStage = "hosting-stop";
            await harness.Diagnostics.WaitForStopAsync();
        }
        catch (TimeoutException)
        {
            WriteSafeProbeTimeout(harness, request, ioFailure ? "cancelled-io" : "cancelled-operation", waitStage, waitElapsed);
            throw;
        }
        finally
        {
            harness.Probe.RequestAbort.Cancel();
            cleanup.Cancel();
            try { response ??= await request; } catch (Exception) { }
            response?.Dispose();
        }
        Assert.Equal(1, harness.Probe.Entered);
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.Empty(await harness.IncidentsAsync());
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        AssertSafeObservedSinks(harness);
    }

    private void WriteSafeProbeTimeout(BoundaryHarness harness, Task request, string scenario,
        string stage, Stopwatch elapsed)
    {
        // Shape/progress only: preserve the timeout and never print response, logs or tokens.
        try
        {
            var probe = harness.Probe;
            output.WriteLine($"Boundary probe timeout: scenario={scenario}; stage={stage}; elapsedMs={elapsed.ElapsedMilliseconds}; " +
                $"Entered={probe.Entered}; Thrown={probe.Thrown}; ResponseStarted={probe.ResponseStarted}; " +
                $"PrefixWriteStarted={probe.PrefixWriteStarted}; PrefixWriteCompleted={probe.PrefixWriteCompleted}; " +
                $"ResponseStartCalled={probe.ResponseStartCalled}; ResponseStartCompleted={probe.ResponseStartCompleted}; " +
                $"ReadyStatus={probe.Ready.Task.Status}; RequestStatus={request.Status}; " +
                $"RequestCompleted={request.IsCompleted}; RequestFaulted={request.IsFaulted}; RequestCanceled={request.IsCanceled}; " +
                $"DiagnosticsStarts={harness.Diagnostics.Starts}; DiagnosticsStops={harness.Diagnostics.Stops}.");
        }
        catch { /* Diagnostic output failure must not replace the original timeout. */ }
    }

    [Fact]
    public async Task IncidentRecordingFailurePreservesTheGenericResponseAndSafeObservableEvidence()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.Nested, failIncidentStorage: true);
        using var response = await harness.Client.GetAsync(harness.Probe.Path);

        await AssertGenericErrorAsync(harness, response);
        await harness.Diagnostics.WaitForStopAsync();
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.Equal(1, harness.IncidentFailure!.Attempts);
        Assert.Empty(await harness.IncidentsAsync());
        AssertSafeObservedSinks(harness);
        AssertSafeHeaders(harness, response);
    }

    [Fact]
    public async Task BoundaryLoggerFailureDoesNotReplaceTheOriginalGenericResponseOrIncident()
    {
        await using var harness = await BoundaryHarness.CreateAsync(FailureMode.Nested);
        harness.Logs.ThrowOnBoundaryError = true;
        using var response = await harness.Client.GetAsync(harness.Probe.Path);

        await AssertGenericErrorAsync(harness, response);
        await harness.Diagnostics.WaitForStopAsync();
        Assert.Equal(1, harness.Probe.Thrown);
        Assert.Equal(1, harness.Logs.InjectedFailures);
        Assert.Single(await harness.IncidentsAsync());
        AssertSafeObservedSinks(harness);
        AssertSafeHeaders(harness, response);
    }

    private static async Task AssertGenericErrorAsync(BoundaryHarness harness, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("server_error", error.Code);
        Assert.Equal("The request could not be completed.", error.Message);
        Assert.Equal(harness.Probe.TraceIdentifier, error.CorrelationId);
        Assert.All(harness.Probe.Sentinels, sentinel => Assert.DoesNotContain(sentinel, error.ToString()));
    }

    private static void AssertSafeHeaders(BoundaryHarness harness, HttpResponseMessage response)
    {
        Assert.Equal(harness.Probe.TraceIdentifier, Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")), StringComparison.Ordinal);
    }

    private void AssertSafeObservedSinks(BoundaryHarness harness)
    {
        output.WriteLine($"Request entered={harness.Probe.Entered}; thrown={harness.Probe.Thrown}; " +
            $"diagnostic starts={harness.Diagnostics.Starts}; stops={harness.Diagnostics.Stops}; " +
            $"raw exception payloads={harness.Diagnostics.RawExceptionCount}; log entries={harness.Logs.Entries.Count}; " +
            $"response write attempts={harness.Probe.WriteAttempts}.");
        Assert.True(harness.Diagnostics.Starts > 0, "The DiagnosticListener must observe this request's hosting activity.");
        Assert.True(harness.Diagnostics.Stops > 0, "The DiagnosticListener must observe this request completing.");
        Assert.NotEmpty(harness.Logs.Entries);
        foreach (var sentinel in harness.Probe.Sentinels.Append(harness.Logs.FailureSentinel))
        {
            Assert.DoesNotContain(sentinel, harness.Logs.Text);
            Assert.DoesNotContain(sentinel, harness.Diagnostics.ExceptionText);
        }
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.HasExceptionReference);
        Assert.Equal(0, harness.Diagnostics.RawExceptionCount);
    }

    private enum FailureMode
    {
        Nested, DataOnly, ConfiguredIncident, UncancelledOperation, StartedResponse, ResponseWriteFailure, CancelledOperation, CancelledIo
    }

    private sealed class BoundaryHarness : IAsyncDisposable
    {
        private readonly SatiApiFactory _parent;
        private readonly WebApplicationFactory<Program> _child;
        public HttpClient Client { get; }
        public FailureProbe Probe { get; }
        public BoundaryLogCapture Logs { get; }
        public RequestDiagnostics Diagnostics { get; }
        public FailingIncidentFactory? IncidentFailure { get; }
        public ConfiguredIncidentFactory? ConfiguredIncidents { get; }

        private BoundaryHarness(SatiApiFactory parent, WebApplicationFactory<Program> child, HttpClient client,
            FailureProbe probe, BoundaryLogCapture logs, RequestDiagnostics diagnostics, FailingIncidentFactory? incidentFailure,
            ConfiguredIncidentFactory? configuredIncidents)
        {
            _parent = parent; _child = child; Client = client; Probe = probe; Logs = logs;
            Diagnostics = diagnostics; IncidentFailure = incidentFailure;
            ConfiguredIncidents = configuredIncidents;
        }

        public static async Task<BoundaryHarness> CreateAsync(FailureMode mode, bool failIncidentStorage = false)
        {
            var parent = new SatiApiFactory();
            var probe = new FailureProbe(mode);
            WebApplicationFactory<Program>? child = null;
            HttpClient? client = null;
            RequestDiagnostics? diagnostics = null;
            try
            {
                using var seedClient = await parent.CreateAuthenticatedClientAsync("admin-one");
                var logs = new BoundaryLogCapture();
                diagnostics = new RequestDiagnostics(probe.Path);
                await using var seedContext = await parent.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>()
                    .CreateDbContextAsync();
                // The general endpoint fixture uses an artificial retrying SQLite strategy;
                // replace it only to isolate request containment in the same private database.
                // This does not establish incident persistence with the production retry strategy.
                var contextOptions = new DbContextOptionsBuilder<ApiDbContext>()
                    .UseSqlite(seedContext.Database.GetConnectionString()).Options;
                var ordinaryContexts = new OrdinaryContextFactory(contextOptions);
                var configuredIncidents = mode == FailureMode.ConfiguredIncident
                    ? new ConfiguredIncidentFactory(parent.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), probe) : null;
                var incidentFailure = failIncidentStorage
                    ? new FailingIncidentFactory(ordinaryContexts, () => probe.Thrown > 0, probe.IncidentSentinel) : null;
                IDbContextFactory<ApiDbContext> childContexts = incidentFailure is not null ? incidentFailure : ordinaryContexts;
                if (configuredIncidents is not null) childContexts = configuredIncidents;
                var writeFilter = configuredIncidents is not null ? new SingleAttemptWriteFilter(configuredIncidents) : null;
                child = parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ILoggerProvider>(logs);
                    services.AddSingleton<IStartupFilter>(new FailureStartupFilter(probe, writeFilter));
                    services.RemoveAll<IDbContextFactory<ApiDbContext>>();
                    services.AddSingleton(childContexts);
                }));
                client = child.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
                client.DefaultRequestHeaders.Authorization = seedClient.DefaultRequestHeaders.Authorization;
                return new BoundaryHarness(parent, child, client, probe, logs, diagnostics, incidentFailure, configuredIncidents);
            }
            catch
            {
                await CleanupAsync(parent, child, client, probe, diagnostics);
                throw;
            }
        }

        public async Task<List<ServerIncidentGroup>> IncidentsAsync()
        {
            await using var scope = _parent.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            return await db.IncidentGroups.AsNoTracking().Where(incident => incident.LastReference == Probe.TraceIdentifier).ToListAsync();
        }

        public ValueTask DisposeAsync() => CleanupAsync(_parent, _child, Client, Probe, Diagnostics);

        private static async ValueTask CleanupAsync(SatiApiFactory parent, WebApplicationFactory<Program>? child,
            HttpClient? client, FailureProbe probe, RequestDiagnostics? diagnostics)
        {
            probe.Release.TrySetResult();
            try { probe.RequestAbort.Cancel(); }
            finally
            {
                try { client?.Dispose(); }
                finally
                {
                    try { if (child is not null) await child.DisposeAsync(); }
                    finally
                    {
                        try { await parent.DisposeAsync(); }
                        finally
                        {
                            try { diagnostics?.Dispose(); }
                            finally { probe.RequestAbort.Dispose(); }
                        }
                    }
                }
            }
        }
    }

    private sealed class FailureStartupFilter(FailureProbe probe, SingleAttemptWriteFilter? writeFilter) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            // Minimal hosting wires Program's source pipeline before this unmatched fallthrough.
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != probe.Path) { await continuation(); return; }
                if (writeFilter is null) { await probe.InvokeAsync(context); return; }
                var invocation = new DefaultEndpointFilterInvocationContext(context, []);
                await writeFilter.InvokeAsync(invocation, async _ =>
                {
                    await probe.InvokeAsync(context);
                    return null;
                });
            });
        };
    }

    private sealed class FailureProbe(FailureMode mode)
    {
        private int _entered;
        private int _thrown;
        private int _writes;
        private int _prefixWriteStarted;
        private int _prefixWriteCompleted;
        private int _responseStartCalled;
        private int _responseStartCompleted;
        public string Path { get; } = $"/api/v1/synthetic-boundary-{Guid.NewGuid():N}";
        public string NarrativeSentinel { get; } = $"SYNTHETIC_NARRATIVE_{Guid.NewGuid():N}";
        public string SecretSentinel { get; } = $"SYNTHETIC_SECRET_{Guid.NewGuid():N}";
        public string WriteSentinel { get; } = $"SYNTHETIC_HANDLER_WRITE_{Guid.NewGuid():N}";
        public string IncidentSentinel { get; } = $"SYNTHETIC_INCIDENT_STORAGE_{Guid.NewGuid():N}";
        public string DataSentinel { get; } = $"SYNTHETIC_EXCEPTION_DATA_{Guid.NewGuid():N}";
        public IEnumerable<string> Sentinels => [NarrativeSentinel, SecretSentinel, WriteSentinel, IncidentSentinel, DataSentinel];
        public string TraceIdentifier { get; private set; } = string.Empty;
        public bool Authenticated { get; private set; }
        public bool ResponseStarted { get; private set; }
        public bool HadSingleAttemptScope { get; private set; }
        public int Entered => Volatile.Read(ref _entered);
        public int Thrown => Volatile.Read(ref _thrown);
        public int WriteAttempts => Volatile.Read(ref _writes);
        public bool PrefixWriteStarted => Volatile.Read(ref _prefixWriteStarted) != 0;
        public bool PrefixWriteCompleted => Volatile.Read(ref _prefixWriteCompleted) != 0;
        public bool ResponseStartCalled => Volatile.Read(ref _responseStartCalled) != 0;
        public bool ResponseStartCompleted => Volatile.Read(ref _responseStartCompleted) != 0;
        public CancellationTokenSource RequestAbort { get; } = new();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InvokeAsync(HttpContext context)
        {
            Interlocked.Increment(ref _entered);
            TraceIdentifier = context.TraceIdentifier;
            Authenticated = context.User.Identity?.IsAuthenticated == true;
            HadSingleAttemptScope = ExecutionStrategy.Current is { RetriesOnFailure: false };
            if (mode is FailureMode.CancelledOperation or FailureMode.CancelledIo)
            {
                context.RequestAborted = RequestAbort.Token;
                var wait = Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                Ready.TrySetResult();
                try { await wait; }
                catch (OperationCanceledException error)
                {
                    Interlocked.Increment(ref _thrown);
                    if (mode == FailureMode.CancelledIo) throw new IOException(NarrativeSentinel, error);
                    throw new OperationCanceledException(NarrativeSentinel, error, context.RequestAborted);
                }
            }
            if (mode == FailureMode.StartedResponse)
            {
                Interlocked.Exchange(ref _prefixWriteStarted, 1);
                await context.Response.WriteAsync("synthetic-started-prefix");
                Interlocked.Exchange(ref _prefixWriteCompleted, 1);
                Interlocked.Exchange(ref _responseStartCalled, 1);
                await context.Response.StartAsync();
                Interlocked.Exchange(ref _responseStartCompleted, 1);
                ResponseStarted = context.Response.HasStarted;
                Ready.TrySetResult();
                await Release.Task.WaitAsync(context.RequestAborted);
            }
            if (mode == FailureMode.ResponseWriteFailure)
                context.Response.Body = new FaultingResponseStream(context.Response.Body, () => Interlocked.Increment(ref _writes), WriteSentinel);
            Interlocked.Increment(ref _thrown);
            if (mode == FailureMode.DataOnly)
            {
                var dataOnly = new InvalidOperationException("Synthetic safe failure.");
                dataOnly.Data["synthetic-untrusted-detail"] = DataSentinel;
                throw dataOnly;
            }
            var inner = new ArgumentException(SecretSentinel);
            inner.Data["synthetic-untrusted-detail"] = DataSentinel;
            if (mode == FailureMode.UncancelledOperation)
                throw new OperationCanceledException(NarrativeSentinel, inner, CancellationToken.None);
            throw new InvalidOperationException(NarrativeSentinel, inner);
        }
    }

    private sealed class FaultingResponseStream(Stream inner, Action writeAttempt, string sentinel) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        private Exception Failure() { writeAttempt(); return new IOException(sentinel); }
        public override void Write(byte[] buffer, int offset, int count) => throw Failure();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromException(Failure());
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException(Failure());
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class FailingIncidentFactory(IDbContextFactory<ApiDbContext> inner, Func<bool> shouldFail, string sentinel)
        : IDbContextFactory<ApiDbContext>
    {
        public int Attempts { get; private set; }
        public ApiDbContext CreateDbContext()
        {
            // DemoMutationLeaseMiddleware resolves a scoped context even for this GET;
            // fail only the subsequent incident recording after the synthetic handler throws.
            if (!shouldFail()) return inner.CreateDbContext();
            Attempts++;
            throw new InvalidOperationException(sentinel);
        }
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class OrdinaryContextFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class ConfiguredIncidentFactory(IDbContextFactory<ApiDbContext> inner, FailureProbe probe)
        : IDbContextFactory<ApiDbContext>
    {
        private int _incidentContexts;
        public int IncidentContexts => Volatile.Read(ref _incidentContexts);
        public bool IncidentStrategyRetries { get; private set; }
        public bool IncidentOuterScopeWasAbsent { get; private set; }
        public ApiDbContext CreateDbContext() => Observe(inner.CreateDbContext());
        public async Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Observe(await inner.CreateDbContextAsync(cancellationToken));
        private ApiDbContext Observe(ApiDbContext context)
        {
            if (probe.Thrown > 0)
            {
                Interlocked.Increment(ref _incidentContexts);
                IncidentStrategyRetries = context.Database.CreateExecutionStrategy().RetriesOnFailure;
                IncidentOuterScopeWasAbsent = ExecutionStrategy.Current is null;
            }
            return context;
        }
    }

    private sealed record LogEntry(LogLevel Level, string Category, string Message, string Text, bool HasExceptionReference);

    private sealed class BoundaryLogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();
        private int _failures;
        public string FailureSentinel { get; } = $"SYNTHETIC_LOGGER_FAILURE_{Guid.NewGuid():N}";
        public bool ThrowOnBoundaryError { get; set; }
        public int InjectedFailures => Volatile.Read(ref _failures);
        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();
        public string Text => string.Join('\n', _entries.Select(entry => entry.Text));
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }

        private sealed class CaptureLogger(BoundaryLogCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner._entries.Enqueue(new LogEntry(LogLevel.Trace, category, "scope", FormatState(state), HasException(state)));
                return null;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                owner._entries.Enqueue(new LogEntry(level, category, message,
                    $"{category}|{message}|{FormatState(state)}|{exception}", exception is not null || HasException(state)));
                if (owner.ThrowOnBoundaryError && level == LogLevel.Error && message.Contains("Unhandled API error", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref owner._failures);
                    throw new InvalidOperationException(owner.FailureSentinel);
                }
            }
            private static string FormatState(object? state) => state is IEnumerable<KeyValuePair<string, object?>> values
                ? string.Join('|', values.Select(value => $"{value.Key}={value.Value}")) : state?.ToString() ?? string.Empty;
            private static bool HasException(object? state) => state is Exception ||
                state is IEnumerable<KeyValuePair<string, object?>> values && values.Any(value => value.Value is Exception);
        }
    }

    private sealed class RequestDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _path;
        private readonly ConcurrentQueue<string> _exceptions = new();
        private readonly ConcurrentBag<IDisposable> _subscriptions = new();
        private readonly IDisposable _allListeners;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _starts;
        private int _stops;
        private int _rawExceptions;
        public int Starts => Volatile.Read(ref _starts);
        public int Stops => Volatile.Read(ref _stops);
        public int RawExceptionCount => Volatile.Read(ref _rawExceptions);
        public string ExceptionText => string.Join('\n', _exceptions);
        public RequestDiagnostics(string path) { _path = path; _allListeners = DiagnosticListener.AllListeners.Subscribe(this); }
        public Task WaitForStopAsync() => _stopped.Task.WaitAsync(TimeSpan.FromSeconds(15));
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                _subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is null) return;
            var properties = value.Value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var context = properties.FirstOrDefault(property => property.Name.Equals("HttpContext", StringComparison.OrdinalIgnoreCase))
                ?.GetValue(value.Value) as HttpContext;
            if (context?.Request.Path != _path) return;
            if (value.Key == "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start") Interlocked.Increment(ref _starts);
            if (value.Key == "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop")
            {
                Interlocked.Increment(ref _stops);
                _stopped.TrySetResult();
            }
            if (value.Key is "Microsoft.AspNetCore.Diagnostics.HandledException" or "Microsoft.AspNetCore.Hosting.UnhandledException")
                foreach (var property in properties)
                    if (property.GetValue(value.Value) is Exception exception)
                    {
                        Interlocked.Increment(ref _rawExceptions);
                        _exceptions.Enqueue(exception.ToString());
                        for (Exception? current = exception; current is not null; current = current.InnerException)
                            foreach (var key in current.Data.Keys)
                                _exceptions.Enqueue($"Exception.Data: {key}={current.Data[key]}");
                    }
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose()
        {
            _allListeners.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}

// Hosting diagnostics are process-wide and these private factories set process configuration.
// Isolate their pre-probe handshake from unrelated fixture startup without changing its deadline.
[CollectionDefinition(ApiExceptionRedactionCollection.Name, DisableParallelization = true)]
public sealed class ApiExceptionRedactionCollection
{
    public const string Name = "API escaping exception boundary isolation";
}
