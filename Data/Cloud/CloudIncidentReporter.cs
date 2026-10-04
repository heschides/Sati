using System.Net;
using System.Reflection;
using Sati.Contracts.V1;
using Sati.Services;

namespace Sati.Data.Cloud;

/// <summary>
/// Durable, account-bound client incident delivery. The local envelope's origin only
/// selects which signed-in account may send it; the API independently authenticates
/// and derives the actual agency for every request.
/// </summary>
internal sealed class CloudIncidentReporter : IIncidentReporter, IDisposable
{
    internal const int MaximumBackgroundAttempts = 8;
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);
    private readonly CloudApiClient _api;
    private readonly IncidentOutbox _outbox;
    private readonly ISessionService _session;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _retryGate = new();
    private CancellationTokenSource? _retryCancellation;
    private Task? _retryTask;
    private SessionStamp? _retrySession;
    private bool _disposed;

    internal Task? CurrentRetryTask
    {
        get { lock (_retryGate) return _retryTask; }
    }

    public CloudIncidentReporter(CloudApiClient api, IncidentOutbox outbox,
        ISessionService session, TimeProvider timeProvider)
        : this(api, outbox, session, timeProvider, Task.Delay, Random.Shared.NextDouble)
    {
    }

    internal CloudIncidentReporter(CloudApiClient api, IncidentOutbox outbox,
        ISessionService session, TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task> delay, Func<double> jitter)
    {
        _api = api;
        _outbox = outbox;
        _session = session;
        _timeProvider = timeProvider;
        _delay = delay;
        _jitter = jitter;
        _api.SessionEnded += OnSessionEnded;
        _api.AccessTokenChanged += OnAccessTokenChanged;
    }

    public Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = IncidentSeverities.Error,
        CancellationToken cancellationToken = default) =>
        ReportCoreAsync(exception, operation, reference, severity, null, cancellationToken);

    public Task ReportCrashAsync(
        Exception exception,
        string operation,
        string reference,
        CrashDiagnosticDto diagnostic,
        string severity = IncidentSeverities.Critical,
        CancellationToken cancellationToken = default) =>
        ReportCoreAsync(exception, operation, reference, severity, diagnostic, cancellationToken);

    private async Task ReportCoreAsync(
        Exception exception,
        string operation,
        string reference,
        string severity,
        CrashDiagnosticDto? diagnostic,
        CancellationToken cancellationToken)
    {
        try
        {
            var origin = CaptureSession();
            if (origin is null)
                return; // No authenticated origin exists; AppErrorLog still has the local reference.

            _outbox.Enqueue(new IncidentReportRequest(
                reference,
                "Desktop",
                severity,
                AppErrorLog.SafeArea(operation),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
                AppErrorLog.CreateFingerprint(exception),
                _timeProvider.GetUtcNow().UtcDateTime,
                diagnostic), origin.UserId, origin.AgencyId,
                _timeProvider.GetUtcNow().UtcDateTime);

            await FlushAsync(cancellationToken);
        }
        catch
        {
            // Once enqueued, an incident remains in the local outbox. Incident
            // reporting must not replace the original application failure.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var stamp = CaptureSession();
        if (stamp is null)
        {
            StopRetry();
            return;
        }

        StopRetryForDifferentSession(stamp);
        try
        {
            var outcome = await FlushAccountAsync(stamp, cancellationToken);
            if (outcome.NeedsRetry)
                ScheduleRetry(stamp, outcome.RetryAfter);
            else
                StopRetryForSession(stamp);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopped this flush; the durable envelope remains queued.
        }
    }

    public IncidentOutboxSupportStatus? GetOutboxStatus()
    {
        var user = _session.CurrentUser;
        if (user is null || user.Id <= 0 || user.AgencyId < 0)
            return null;
        try
        {
            var status = _outbox.StatusFor(user.Id, user.AgencyId,
                _timeProvider.GetUtcNow().UtcDateTime);
            return new IncidentOutboxSupportStatus(status.PendingForCurrentAccount,
                status.OldestPendingAge, status.HeldForOtherAccounts, status.Quarantined);
        }
        catch
        {
            return null;
        }
    }

    private async Task<FlushOutcome> FlushAccountAsync(SessionStamp stamp,
        CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(stamp))
                return FlushOutcome.Done;

            foreach (var envelope in _outbox.ReadPendingFor(stamp.UserId, stamp.AgencyId,
                         _timeProvider.GetUtcNow().UtcDateTime))
            {
                // The account may switch while an earlier response is in flight. Capture
                // the token generation on each POST and never start a later one as B.
                if (!IsCurrent(stamp))
                    return FlushOutcome.Done;
                try
                {
                    await _api.PostWithCapturedSessionAsync<IncidentReportRequest, IncidentGroupDto>(
                        "/api/v1/incidents", envelope.Report,
                        stamp.CredentialGeneration, cancellationToken);
                    _outbox.Complete(envelope);
                }
                catch (CloudApiException exception) when (IsPermanentRejection(exception.StatusCode))
                {
                    // A bad envelope must not block later valid reports from this account.
                    _outbox.Reject(envelope);
                }
                catch (CloudApiException exception) when (exception.StatusCode is
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Sign-in or permission has changed. No automatic authentication.
                    return FlushOutcome.Done;
                }
                catch (CloudApiException exception)
                {
                    return new FlushOutcome(true, exception.RetryAfter);
                }
                catch (CloudConnectivityException)
                {
                    return new FlushOutcome(true, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Unknown delivery outcome: retain the same support reference and
                    // retry after backoff. The server deduplicates accepted references.
                    return new FlushOutcome(true, null);
                }
            }
            return FlushOutcome.Done;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private static bool IsPermanentRejection(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status is not
            (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);

    private void ScheduleRetry(SessionStamp stamp, TimeSpan? retryAfter)
    {
        lock (_retryGate)
        {
            if (_disposed || !IsCurrent(stamp))
                return;
            if (_retryTask is { IsCompleted: false } && _retrySession == stamp)
                return;

            CancelRetryUnderLock();
            _retrySession = stamp;
            var source = new CancellationTokenSource();
            _retryCancellation = source;
            _retryTask = Task.Run(() => RunRetriesAsync(
                stamp, retryAfter, source));
        }
    }

    private async Task RunRetriesAsync(SessionStamp stamp, TimeSpan? retryAfter,
        CancellationTokenSource source)
    {
        var cancellationToken = source.Token;
        try
        {
            for (var attempt = 1; attempt <= MaximumBackgroundAttempts; attempt++)
            {
                await _delay(Backoff(attempt, retryAfter, _jitter()), cancellationToken);
                if (!IsCurrent(stamp))
                    return;
                var outcome = await FlushAccountAsync(stamp, cancellationToken);
                if (!outcome.NeedsRetry)
                    return;
                retryAfter = outcome.RetryAfter;
            }
            // Leave the envelope for the next explicit flush/sign-in. No busy loop.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // The outbox remains durable; no sensitive exception text is logged.
        }
        finally
        {
            lock (_retryGate)
            {
                if (ReferenceEquals(_retryCancellation, source))
                {
                    _retryCancellation = null;
                    _retryTask = null;
                    _retrySession = null;
                }
            }
            source.Dispose();
        }
    }

    internal static TimeSpan Backoff(int attempt, TimeSpan? retryAfter, double jitter)
    {
        var seconds = Math.Min(MaximumBackoff.TotalSeconds,
            5 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 20)));
        seconds *= 0.8 + 0.4 * Math.Clamp(jitter, 0, 1);
        // Respect a server Retry-After even when longer than the ordinary cap.
        if (retryAfter is TimeSpan serverDelay && serverDelay.TotalSeconds > seconds)
            seconds = Math.Min(TimeSpan.FromMinutes(30).TotalSeconds, serverDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private SessionStamp? CaptureSession()
    {
        var user = _session.CurrentUser;
        if (user is not { Id: > 0, AgencyId: >= 0 } || _session.HasSessionEnded)
            return null;
        var generation = _api.CapturedAccountGeneration(user.Id, user.AgencyId);
        return generation is long captured
            ? new SessionStamp(user, user.Id, user.AgencyId, captured)
            : null;
    }

    private bool IsCurrent(SessionStamp stamp) =>
        !_disposed && !_session.HasSessionEnded &&
        ReferenceEquals(_session.CurrentUser, stamp.User) &&
        _api.IsCapturedAccountSession(stamp.UserId, stamp.AgencyId,
            stamp.CredentialGeneration);

    private void StopRetryForDifferentSession(SessionStamp stamp)
    {
        lock (_retryGate)
            if (_retrySession is not null && _retrySession != stamp)
                CancelRetryUnderLock();
    }

    private void StopRetryForSession(SessionStamp stamp)
    {
        lock (_retryGate)
            if (_retrySession == stamp)
                CancelRetryUnderLock();
    }

    private void StopRetry()
    {
        lock (_retryGate)
            CancelRetryUnderLock();
    }

    private void CancelRetryUnderLock()
    {
        _retryCancellation?.Cancel();
        _retryCancellation = null;
        _retryTask = null;
        _retrySession = null;
    }

    private void OnSessionEnded(object? sender, EventArgs args) => StopRetry();

    private void OnAccessTokenChanged(object? sender, EventArgs args)
    {
        lock (_retryGate)
            if (_retrySession is { } stamp && !IsCurrent(stamp))
                CancelRetryUnderLock();
    }

    public void Dispose()
    {
        _disposed = true;
        _api.SessionEnded -= OnSessionEnded;
        _api.AccessTokenChanged -= OnAccessTokenChanged;
        StopRetry();
        _flushGate.Dispose();
    }

    private sealed record SessionStamp(object User, int UserId, int AgencyId,
        long CredentialGeneration);
    private readonly record struct FlushOutcome(bool NeedsRetry, TimeSpan? RetryAfter)
    {
        public static readonly FlushOutcome Done = new(false, null);
    }
}
