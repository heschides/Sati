using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClaimMdSandboxConnectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledBodyDeadlineReleasesCallbackAndAllowsFollowingHealthyRequest(bool completesFirstRead)
    {
        var clock = new ManualTimeProvider();
        var body = new CancellationBodyStream(completesFirstRead ? "<result>" : null);
        var content = new TrackedStreamContent(body);
        var coordination = new SerialCoordination();
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(
            body.ReadStarted.Task.IsCompleted
                ? Xml("<result last_responseid=\"0\" />")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var connector = DeadlineClient(handler, coordination, clock);
        using var cleanup = new CancellationTokenSource();
        var stalled = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
        Task<ClaimMdStatusPage>? following = null;
        try
        {
            await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(completesFirstRead ? 2 : 1, body.ReadCalls);
            following = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
            Assert.False(following.IsCompleted);
            Assert.Equal(1, handler.Calls);

            clock.Advance(TimeSpan.FromMilliseconds(44_999));
            Assert.False(body.ReadToken.IsCancellationRequested);
            Assert.False(stalled.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(body.ReadToken.IsCancellationRequested);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stalled);
            var page = await following.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("0", page.Cursor);
            Assert.True(body.IsDisposed);
            Assert.True(content.IsDisposed);
            Assert.Equal(2, coordination.ReleasedCallbacks);
            Assert.Equal(0, coordination.ActiveCallbacks);
            Assert.Equal(2, handler.Calls);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally
        {
            cleanup.Cancel();
            await DrainAsync(stalled);
            if (following is not null) await DrainAsync(following);
        }
    }

    [Fact]
    public async Task HeadersAndBodyShareOneExchangeBudget()
    {
        var clock = new ManualTimeProvider();
        var body = new CancellationBodyStream();
        var headersStarted = Barrier();
        var releaseHeaders = Barrier();
        var handler = new CancellableStubHandler(async (_, token) =>
        {
            headersStarted.TrySetResult();
            await releaseHeaders.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        });
        var connector = DeadlineClient(handler, new SerialCoordination(), clock);
        using var cleanup = new CancellationTokenSource();
        var pending = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
        try
        {
            await headersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(40));
            releaseHeaders.TrySetResult();
            await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromMilliseconds(4_999));
            Assert.False(body.ReadToken.IsCancellationRequested);
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(body.ReadToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(body.IsDisposed);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            cleanup.Cancel();
            releaseHeaders.TrySetResult();
            await DrainAsync(pending);
        }
    }

    [Fact]
    public async Task DeadlineCancelsHeadersWithoutSendingAnotherRequest()
    {
        var clock = new ManualTimeProvider();
        var headersStarted = Barrier();
        var requestToken = CancellationToken.None;
        var coordination = new SerialCoordination();
        var handler = new CancellableStubHandler(async (_, token) =>
        {
            requestToken = token;
            var wait = Task.Delay(Timeout.InfiniteTimeSpan, token);
            headersStarted.TrySetResult();
            await wait;
            return Xml("<result last_responseid=\"0\" />");
        });
        var connector = DeadlineClient(handler, coordination, clock);
        using var cleanup = new CancellationTokenSource();
        var pending = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
        try
        {
            await headersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromMilliseconds(44_999));
            Assert.False(requestToken.IsCancellationRequested);
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(requestToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(1, coordination.ReleasedCallbacks);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally
        {
            cleanup.Cancel();
            await DrainAsync(pending);
        }
    }

    [Fact]
    public async Task DeadlineAlsoCancelsStreamAcquisitionAndDisposesResponseContent()
    {
        var clock = new ManualTimeProvider();
        var content = new CancellationStreamContent();
        var coordination = new SerialCoordination();
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var connector = DeadlineClient(handler, coordination, clock);
        using var cleanup = new CancellationTokenSource();
        var pending = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
        try
        {
            await content.AcquisitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromMilliseconds(44_999));
            Assert.False(content.AcquisitionToken.IsCancellationRequested);
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(content.AcquisitionToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(content.IsDisposed);
            Assert.Equal(1, coordination.ReleasedCallbacks);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            cleanup.Cancel();
            await DrainAsync(pending);
        }
    }

    [Fact]
    public async Task EarlierCallerCancellationCancelsBodyAndReleasesCallback()
    {
        var clock = new ManualTimeProvider();
        var body = new CancellationBodyStream();
        var coordination = new SerialCoordination();
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        var connector = DeadlineClient(handler, coordination, clock);
        using var caller = new CancellationTokenSource();
        var pending = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", caller.Token);
        try
        {
            await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(10));
            caller.Cancel();
            Assert.True(body.ReadToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(caller.IsCancellationRequested);
            Assert.True(body.IsDisposed);
            Assert.Equal(1, coordination.ReleasedCallbacks);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally
        {
            caller.Cancel();
            await DrainAsync(pending);
        }
    }

    [Fact]
    public async Task CoordinationWaitDoesNotConsumeExchangeBudget()
    {
        var clock = new ManualTimeProvider();
        var coordination = new SerialCoordination(initiallyHeld: true);
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(Xml("<result last_responseid=\"0\" />")));
        var connector = DeadlineClient(handler, coordination, clock);
        using var cleanup = new CancellationTokenSource();
        var pending = connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", cleanup.Token);
        try
        {
            clock.Advance(TimeSpan.FromMinutes(3));
            Assert.Equal(0, handler.Calls);
            Assert.Equal(0, clock.CreatedTimerCount);
            Assert.False(pending.IsCompleted);
            coordination.ReleaseInitialHold();
            var page = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("0", page.Cursor);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(1, clock.CreatedTimerCount);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally
        {
            cleanup.Cancel();
            await DrainAsync(pending);
        }
    }

    [Fact]
    public async Task AlreadyCancelledCallerNeverStartsAnExchange()
    {
        var clock = new ManualTimeProvider();
        var coordination = new SerialCoordination();
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(Xml("<result last_responseid=\"0\" />")));
        var connector = DeadlineClient(handler, coordination, clock);
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", caller.Token));

        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, clock.CreatedTimerCount);
        Assert.Equal(0, coordination.ActiveCallbacks);
    }

    [Fact]
    public void HttpClientRegistrationUsesTheSingleExchangeDeadlineOwner()
    {
        using var http = new HttpClient();

        ClaimMdSandboxConnector.ConfigureHttpClient(http);

        Assert.Equal(TimeSpan.FromSeconds(45), ClaimMdSandboxConnector.HttpExchangeTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StreamedResponseRetainsSixteenMiBMaximum(int excessBytes)
    {
        const int maximum = 16 * 1024 * 1024;
        const string prefix = "<result last_responseid=\"0\">";
        const string suffix = "</result>";
        var xml = prefix + new string(' ', maximum - prefix.Length - suffix.Length + excessBytes) + suffix;
        var clock = new ManualTimeProvider();
        var handler = new CancellableStubHandler((_, _) => Task.FromResult(Xml(xml)));
        var connector = DeadlineClient(handler, new SerialCoordination(), clock);

        if (excessBytes == 0)
        {
            var page = await connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None);
            Assert.Equal("0", page.Cursor);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<FormatException>(() =>
                connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None));
            Assert.Equal("Claim.MD response exceeds the safe limit.", failure.Message);
        }
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task UploadPostsOnlyToFixedHostAndRequiresExactD9AndClm01()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://svc.claim.md/services/upload/", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("AccountKey", body);
            Assert.Contains("SYNTHETIC_KEY", body);
            Assert.Contains(upload.FileName, body);
            Assert.Contains("REF*D9*" + claim.RemoteClaimId, body);
            return Xml($"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"{claim.RemoteClaimId}\" fileid=\"123456\" status=\"A\"><messages status=\"A\" mesgid=\"ACK\"/></claim></result>");
        });
        var connector = Client(handler);

        var result = await connector.UploadAsync(upload, CancellationToken.None);
        Assert.Equal("123456", result.ExternalFileId);
        Assert.Equal(ClearinghouseAttemptOutcome.Accepted, result.Outcome);
        Assert.NotNull(result.RawResponse);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MismatchedRemoteClaimIdentityIsUnknownAndRetainsVendorEvidence()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            $"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"WRONG\" fileid=\"123456\" status=\"A\"/></result>")));

        var result = await Client(handler).UploadAsync(upload, CancellationToken.None);

        Assert.Equal(ClearinghouseAttemptOutcome.OutcomeUnknown, result.Outcome);
        Assert.Null(result.ExternalFileId);
        Assert.Contains("WRONG", result.RawResponse);
    }

    [Fact]
    public async Task RejectedClaimStillMeansTheFileWasReceivedNotThatItsPayerApproved()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            $"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"{claim.RemoteClaimId}\" fileid=\"123456\" status=\"R\"><messages status=\"R\" /></claim></result>")));
        var result = await Client(handler).UploadAsync(upload, CancellationToken.None);
        Assert.Equal(ClearinghouseAttemptOutcome.Accepted, result.Outcome);
        Assert.Equal(0, result.AcceptedClaims);
        Assert.Equal(1, result.RejectedClaims);
    }

    [Fact]
    public async Task LaterStatusForSameClaimWinsWithinOnePage()
    {
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            "<result last_responseid=\"13\">" +
            "<claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"A\"><messages responseid=\"11\" status=\"A\" /></claim>" +
            "<claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"R\"><messages responseid=\"12\" status=\"R\" /></claim>" +
            "</result>")));
        var page = await Client(handler).GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None);
        Assert.Equal("R", Assert.Single(page.Claims).Status);
        Assert.Contains("responseid=\"11\"", page.RawXml);
    }

    [Fact]
    public async Task RedirectAndProductionInterchangeNeverSucceed()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://untrusted.invalid/collect") }
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).UploadAsync(TestUpload(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        var production = TestUpload() with { Content = TestUpload().Content.Replace("*T*:", "*P*:", StringComparison.Ordinal) };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client(handler).UploadAsync(production, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task StatusAndEraReadsUseSeparateOpaqueCursorsAndNeverWriteThem()
    {
        var handler = new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("AccountKey=SYNTHETIC_KEY", body);
            return request.RequestUri!.AbsolutePath switch
            {
                "/services/response/" => Xml("<result last_responseid=\"12\"><claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"R\"><messages responseid=\"12\" status=\"R\" /></claim></result>"),
                "/services/eralist/" => Xml("<result last_eraid=\"20\"><era eraid=\"19\" /></result>"),
                "/services/uploadlist/" => Xml("<result><file inboundid=\"9\" file_type=\"claim\" filename=\"claim.edi\" uploadtime=\"1720000000\" /></result>"),
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
        });
        var connector = Client(handler);

        var status = await connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None);
        Assert.Equal("12", status.Cursor);
        Assert.Equal("R", Assert.Single(status.Claims).Status);
        var eras = await connector.GetErasAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", 1, CancellationToken.None);
        Assert.Equal("20", eras.Cursor);
        Assert.Equal("19", Assert.Single(eras.Eras).EraId);
        var uploads = await connector.ListUploadsAsync("CLAIMMD_SANDBOX_KEY_TEST",
            new DateOnly(2026, 9, 26), 1, CancellationToken.None);
        Assert.Equal("9", Assert.Single(uploads).FileId);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public void SecretReferenceMustBeAnApiHostSandboxVariableName()
    {
        var source = new EnvironmentClaimMdSandboxKeySource();
        Assert.Throws<InvalidOperationException>(() => source.Resolve("https://example.invalid/key"));
        Assert.Throws<InvalidOperationException>(() => source.Resolve("CLAIMMD_SANDBOX_KEY_BAD-ALIAS"));
    }

    [Fact]
    public void RealTransportGateCannotOpenAgainstProductionIdentity()
    {
        var production = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Production", ExpectedDatabaseName = "SatiProduction",
            EnableClaimMdSandboxTransport = true
        }), new TestHostEnvironment());
        Assert.False(production.IsEnabled);
        Assert.False(production.IsRealSandboxEnabled);
    }

    private static ClaimMdSandboxConnector Client(StubHandler handler) =>
        DeadlineClient(handler, new TestClaimMdCoordination(), TimeProvider.System);

    private static ClaimMdSandboxConnector DeadlineClient(HttpMessageHandler handler,
        IClaimMdSandboxCoordination coordination, TimeProvider clock)
    {
        var http = new HttpClient(handler);
        ClaimMdSandboxConnector.ConfigureHttpClient(http);
        return new ClaimMdSandboxConnector(http, new FakeKeySource(), coordination, clock);
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task DrainAsync(Task pending)
    {
        try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
    }

    private static HttpResponseMessage Xml(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/xml")
    };

    private static ClearinghouseUpload TestUpload()
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "SYNTHETIC PAYER", "MCDME");
        var line = new ProfessionalClaimLineFacts(88, new DateTime(2026, 8, 12), "G9012", "HI",
            1.33m, 33.25m, "987654321", "1999999984", "F89", 11,
            ProfessionalClaimSnapshotCodec.Serialize(snapshot));
        var content = Professional837Formatter.Generate(77, 2026, 8,
            [new Professional837Claim(99, line, "1", null)],
            TradingPartnerProfile.ClaimMd("ACCT123", "TEST"), true,
            new DateTime(2026, 8, 29, 9, 30, 0), "123456789");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(content)));
        return new ClearinghouseUpload(Guid.NewGuid(), "claim.edi", content, hash,
            TradingPartnerKind.ClaimMd, "CLAIMMD_SANDBOX_KEY_TEST");
    }

    private sealed class FakeKeySource : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference)
        {
            Assert.Equal("CLAIMMD_SANDBOX_KEY_TEST", reference);
            return "SYNTHETIC_KEY";
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request);
        }
    }

    private sealed class CancellableStubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }

    private sealed class SerialCoordination(bool initiallyHeld = false) : IClaimMdSandboxCoordination
    {
        private readonly SemaphoreSlim _lease = new(initiallyHeld ? 0 : 1, 1);
        public int ActiveCallbacks { get; private set; }
        public int ReleasedCallbacks { get; private set; }
        public void ReleaseInitialHold() => _lease.Release();

        public async Task<T> RequestAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken token)
        {
            await _lease.WaitAsync(token);
            ActiveCallbacks++;
            try { return await request(token); }
            finally
            {
                ActiveCallbacks--;
                ReleasedCallbacks++;
                _lease.Release();
            }
        }

        public Task<int> PollOnceAsync(Func<CancellationToken, Task<int>> poll, CancellationToken token) => poll(token);
    }

    private sealed class CancellationBodyStream(string? firstChunk = null) : Stream
    {
        private readonly byte[]? _firstChunk = firstChunk is null ? null : Encoding.UTF8.GetBytes(firstChunk);
        public TaskCompletionSource ReadStarted { get; } = Barrier();
        public CancellationToken ReadToken { get; private set; }
        public bool IsDisposed { get; private set; }
        public int ReadCalls { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            if (ReadCalls == 1 && _firstChunk is not null)
            {
                _firstChunk.CopyTo(buffer);
                return _firstChunk.Length;
            }
            ReadToken = cancellationToken;
            var wait = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            ReadStarted.TrySetResult();
            await wait;
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellationStreamContent : HttpContent
    {
        public TaskCompletionSource AcquisitionStarted { get; } = Barrier();
        public CancellationToken AcquisitionToken { get; private set; }
        public bool IsDisposed { get; private set; }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            AcquisitionToken = cancellationToken;
            var wait = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            AcquisitionStarted.TrySetResult();
            await wait;
            return Stream.Null;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackedStreamContent(Stream body) : StreamContent(body)
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>Test time only advances explicitly; cancellation timers run at their scheduled instant.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _elapsedTicks;
    private TimeSpan _utcOffset;
    public int CreatedTimerCount { get; private set; }
    public int ActiveTimerCount { get { lock (_gate) return _timers.Count(timer => timer.DueTicks != long.MaxValue); } }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_gate) return _elapsedTicks; }
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)
            .AddTicks(_elapsedTicks).Add(_utcOffset);
    }
    public void ShiftUtc(TimeSpan offset) { lock (_gate) _utcOffset += offset; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            CreatedTimerCount++;
            return timer;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        long target;
        lock (_gate) target = checked(_elapsedTicks + elapsed.Ticks);
        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers.Where(timer => timer.DueTicks <= target)
                    .OrderBy(timer => timer.DueTicks).FirstOrDefault();
                if (due is null) { _elapsedTicks = target; return; }
                _elapsedTicks = due.DueTicks;
                due.DueTicks = due.PeriodTicks > 0
                    ? checked(due.DueTicks + due.PeriodTicks) : long.MaxValue;
            }
            due.Callback(due.State);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long DueTicks { get; set; } = long.MaxValue;
        public long PeriodTicks { get; private set; }
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed) return false;
                DueTicks = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(owner._elapsedTicks + dueTime.Ticks);
                PeriodTicks = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
                return true;
            }
        }
        public void Dispose()
        {
            lock (owner._gate) { _disposed = true; owner._timers.Remove(this); }
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
