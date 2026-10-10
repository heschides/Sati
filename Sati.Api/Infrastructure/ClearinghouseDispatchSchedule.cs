namespace Sati.Api.Infrastructure;

internal enum DispatchTurnKind { Disabled, Processed, Empty, Contended }
internal sealed record DispatchTurn(DispatchTurnKind Kind, DateTime? NextEligibleUtc = null)
{
    public static DispatchTurn Disabled { get; } = new(DispatchTurnKind.Disabled);
    public static DispatchTurn Processed { get; } = new(DispatchTurnKind.Processed);
    public static DispatchTurn Contended { get; } = new(DispatchTurnKind.Contended);
}

/// <summary>Local scheduling hints only. Durable eligibility and cross-host admission stay in SQL.</summary>
internal sealed class ClearinghouseDispatchSchedule(TimeProvider clock, ClearinghouseDispatchGate gate)
{
    internal static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MinimumTurnInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(60);
    private readonly object sync = new();
    private TaskCompletionSource changed = NewSignal();
    private long generation;
    private long? activityAt;
    private int running;

    internal long Generation { get { lock (sync) return generation; } }

    // Call only from validated allowlisted handlers, after commit and owned lease disposal.
    internal void SignalActivity()
    {
        if (!gate.IsEnabled) return;
        TaskCompletionSource previous;
        lock (sync)
        {
            activityAt = clock.GetTimestamp();
            generation = unchecked(generation + 1);
            previous = changed;
            changed = NewSignal();
        }
        previous.TrySetResult();
    }

    internal async Task RunAsync(Func<CancellationToken, Task<DispatchTurn>> process,
        Action failure, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("The dispatch scheduling owner already has a runner.");
        try
        {
            var startup = true;
            long? lastStart = null, emptyAt = null, contentionAt = null;
            long consumed = 0;
            DispatchTurn? previous = null;
            while (!token.IsCancellationRequested && gate.IsEnabled)
            {
                var state = ReadState();
                if (!startup && state.ActiveRemaining <= TimeSpan.Zero)
                {
                    // A past due time is never a dormant wake source. No SQL lease is held here.
                    await WaitForChangeAsync(state.Generation, Timeout.InfiniteTimeSpan, token);
                    continue;
                }

                var pacing = Remaining(lastStart, MinimumTurnInterval);
                var cooldown = Remaining(contentionAt, ReconciliationInterval);
                var blocked = pacing > cooldown ? pacing : cooldown;
                if (blocked > TimeSpan.Zero)
                {
                    if (!startup && state.ActiveRemaining < blocked) blocked = state.ActiveRemaining;
                    // Signals renew activity but never bypass pacing or a shared-failure cooldown.
                    await Task.Delay(blocked, clock, token);
                    continue;
                }

                var reconciliation = Remaining(emptyAt, ReconciliationInterval);
                var due = previous?.NextEligibleUtc is { } utc
                    ? utc - clock.GetUtcNow().UtcDateTime : Timeout.InfiniteTimeSpan;
                var shouldRun = startup || state.Generation != consumed ||
                    previous?.Kind is DispatchTurnKind.Processed or DispatchTurnKind.Contended ||
                    emptyAt is not null && reconciliation <= TimeSpan.Zero ||
                    previous?.NextEligibleUtc is not null && due <= TimeSpan.Zero;
                if (!shouldRun)
                {
                    var wait = state.ActiveRemaining;
                    if (reconciliation > TimeSpan.Zero && reconciliation < wait) wait = reconciliation;
                    if (due > TimeSpan.Zero && due < wait) wait = due;
                    await WaitForChangeAsync(state.Generation, wait, token);
                    continue;
                }

                token.ThrowIfCancellationRequested();
                if (!gate.IsEnabled) break;
                // A pause during scheduling must not admit work using an expired activity snapshot.
                state = ReadState();
                if (!startup && state.ActiveRemaining <= TimeSpan.Zero) continue;
                consumed = state.Generation;
                lastStart = state.Timestamp;
                startup = false;
                try { previous = await process(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    failure();
                    previous = DispatchTurn.Contended;
                }
                if (previous.Kind == DispatchTurnKind.Disabled) break;
                emptyAt = previous.Kind == DispatchTurnKind.Empty ? clock.GetTimestamp() : null;
                contentionAt = previous.Kind == DispatchTurnKind.Contended ? clock.GetTimestamp() : null;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            lock (sync) activityAt = null;
            Volatile.Write(ref running, 0);
        }
    }

    private (long Generation, TimeSpan ActiveRemaining, long Timestamp) ReadState()
    {
        lock (sync)
        {
            var now = clock.GetTimestamp();
            var remaining = activityAt is { } since
                ? ActivityWindow - clock.GetElapsedTime(since, now) : TimeSpan.Zero;
            return (generation, remaining, now);
        }
    }

    private TimeSpan Remaining(long? since, TimeSpan interval) => since is null
        ? TimeSpan.Zero : interval - clock.GetElapsedTime(since.Value);

    private async Task WaitForChangeAsync(long observed, TimeSpan delay, CancellationToken token)
    {
        Task signal;
        lock (sync)
        {
            // Atomically couple wait registration to the discovery generation. No hint can be lost.
            if (generation != observed) return;
            signal = changed.Task;
        }
        if (delay == Timeout.InfiniteTimeSpan)
        {
            await signal.WaitAsync(token);
            return;
        }
        using var stopWait = CancellationTokenSource.CreateLinkedTokenSource(token);
        var wake = signal.WaitAsync(stopWait.Token);
        var timer = Task.Delay(delay, clock, stopWait.Token);
        try { await await Task.WhenAny(wake, timer); }
        finally { await stopWait.CancelAsync(); }
        token.ThrowIfCancellationRequested();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
