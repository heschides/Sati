using Sati.Api.Infrastructure;

namespace Sati.Api.Tests;

/// <summary>Explicit synthetic-test replacement for the SQL Server reset lease.</summary>
internal sealed class TestDemoWorkerResetCoordination : IDemoWorkerResetCoordination
{
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token) => operation(token);
    public Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token) => operation(token);
}

internal sealed class BlockedDemoWorkerResetCoordination : IDemoWorkerResetCoordination
{
    public int Calls { get; private set; }
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token)
    {
        Calls++;
        return Task.FromResult(unavailableResult);
    }
    public Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token)
    {
        Calls++;
        return Task.FromResult(unavailableResult);
    }
}

/// <summary>Refuses only the outer reset lease, so dispatch exclusion cannot mask a missing reset guard.</summary>
internal sealed class BlockedResetLeaseCoordination : IDemoWorkerResetCoordination
{
    public int Calls { get; private set; }
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
        CancellationToken token)
    {
        Calls++;
        return Task.FromResult(unavailableResult);
    }
    public Task<T> RunDispatchAsync<T>(Guid dispatchId, Func<CancellationToken, Task<T>> operation,
        T unavailableResult, CancellationToken token) => operation(token);
}
