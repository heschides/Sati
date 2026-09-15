using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class CloudSessionRevocationRaceTests
{
    [Fact]
    public async Task PendingSignInCredentialsCannotDispatchBeforeWorkspaceAcceptance()
    {
        var handler = new CountingHandler();
        var api = Client(handler);
        api.SetAccessToken("OLD");
        ISessionLifetime lifetime = new CloudSessionLifetime(api);
        lifetime.SuspendAccess();
        api.SetAccessToken("NEW");
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => api.GetAsync<int>("/protected"));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, await api.PostAnonymousAsync<object, int>("/login", new()));
        lifetime.ResumeAccess();
        Assert.Equal(1, await api.GetAsync<int>("/protected"));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ResumingAccessNeverRevivesAnEndedSession()
    {
        var api = Client(new CountingHandler());
        api.SetAccessToken("OLD");
        ISessionLifetime lifetime = new CloudSessionLifetime(api);
        lifetime.SuspendAccess();
        api.InvalidateCurrentSession();
        lifetime.ResumeAccess();
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => api.GetAsync<int>("/protected"));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(1) });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeserializedOldResultCannotEscapeAfterNewCredentialsAreInstalled(bool passive)
    {
        var api = Client(new JsonHandler());
        api.SetAccessToken("OLD");
        SessionChangingResponse.DuringRead = () => api.SetAccessToken("NEW");
        try
        {
            await Assert.ThrowsAsync<CloudSessionEndedException>(() => passive
                ? api.GetWithoutRenewalAsync<SessionChangingResponse>("/protected")
                : api.GetAsync<SessionChangingResponse>("/protected"));
            Assert.False(api.HasSessionEnded);
        }
        finally { SessionChangingResponse.DuringRead = null; }
    }

    public sealed class SessionChangingResponse
    {
        internal static Action? DuringRead;
        public SessionChangingResponse() => DuringRead?.Invoke();
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.OK)]
    public async Task OldResponseCannotEndOrPopulateANewSession(HttpStatusCode oldStatus)
    {
        var handler = new BlockingHandler(oldStatus);
        var api = Client(handler);
        api.SetAccessToken("OLD", DateTimeOffset.UtcNow.AddHours(1));
        var pending = api.GetAsync<int>("/protected");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        api.SetAccessToken("NEW", DateTimeOffset.UtcNow.AddHours(1));
        handler.Release.SetResult();
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => pending);
        Assert.False(api.HasSessionEnded);
        Assert.Equal(42, await api.GetAsync<int>("/new-session"));
    }

    [Fact]
    public async Task OldRenewalCannotReplaceFreshCredentials()
    {
        var handler = new BlockingHandler(HttpStatusCode.OK, renewal: true);
        var api = Client(handler);
        api.SetAccessToken("OLD", DateTimeOffset.UtcNow.AddMinutes(1));
        var pending = api.EnsureSessionRenewedAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        api.SetAccessToken("NEW", DateTimeOffset.UtcNow.AddHours(1));
        handler.Release.SetResult();
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => pending);
        Assert.Equal(42, await api.GetAsync<int>("/new-session"));
        Assert.Equal("NEW", handler.LastToken);
    }

    [Fact]
    public async Task RenewalCannotReviveAnExplicitlyEndedSession()
    {
        var handler = new BlockingHandler(HttpStatusCode.OK, renewal: true);
        var api = Client(handler);
        api.SetAccessToken("OLD", DateTimeOffset.UtcNow.AddMinutes(1));
        var pending = api.EnsureSessionRenewedAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        api.InvalidateCurrentSession();
        handler.Release.SetResult();
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => pending);
        Assert.True(api.HasSessionEnded);
    }

    [Fact]
    public async Task CapturedPostUnauthorizedLatchesItsSession()
    {
        var handler = new BlockingHandler(HttpStatusCode.Unauthorized);
        var api = Client(handler);
        api.SetAccessToken("OLD", DateTimeOffset.UtcNow.AddHours(1));
        var pending = api.PostWithCapturedSessionAsync<object, int>("/protected", new());
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release.SetResult();
        await Assert.ThrowsAsync<CloudSessionEndedException>(() => pending);
        Assert.True(api.HasSessionEnded);
    }

    private static CloudApiClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://synthetic.invalid") });

    private sealed class BlockingHandler(HttpStatusCode oldStatus, bool renewal = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastToken { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastToken = request.Headers.Authorization?.Parameter;
            if (request.RequestUri!.AbsolutePath == "/new-session")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(42) };
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(oldStatus) { Content = renewal
                ? JsonContent.Create(new SessionRenewalResponse("STALE-RENEWAL", DateTimeOffset.UtcNow.AddMinutes(30)))
                : JsonContent.Create(1) };
        }
    }
}
