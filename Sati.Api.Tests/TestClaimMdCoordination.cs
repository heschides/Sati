using Sati.Api.Infrastructure;

namespace Sati.Api.Tests;

/// <summary>In-memory tests do not make vendor calls and have no SQL application locks.</summary>
internal sealed class TestClaimMdCoordination : IClaimMdSandboxCoordination
{
    public Task<T> RequestAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken token) => request(token);
    public Task<int> PollOnceAsync(Func<CancellationToken, Task<int>> poll, CancellationToken token) => poll(token);
}
