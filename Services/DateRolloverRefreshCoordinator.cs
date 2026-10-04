namespace Sati.Services;

/// <summary>
/// Uses the shell's existing timer and activation events. Deferred or failed
/// refreshes remain due; overlapping events cannot refresh the same day twice.
/// </summary>
public sealed class DateRolloverRefreshCoordinator(TimeProvider clock)
{
    private readonly LatestRequestTracker _requests = new();
    private DateOnly _refreshedDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private int? _accountId;
    private bool _refreshing;

    public void BindAccount(int? accountId)
    {
        if (_accountId == accountId) return;
        _accountId = accountId;
        _refreshedDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        _requests.Invalidate();
    }

    public async Task<bool> CheckAsync(int? accountId, Func<bool> isBlocked,
        Func<DateOnly, Func<bool>, Task<bool>> refresh)
    {
        BindAccount(accountId);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (accountId is null || _refreshing || today == _refreshedDate || isBlocked())
            return false;

        _refreshing = true;
        var request = _requests.Begin();
        bool IsCurrent() => _requests.IsCurrent(request) && _accountId == accountId &&
            today == DateOnly.FromDateTime(clock.GetLocalNow().DateTime) && !isBlocked();
        try
        {
            if (!await refresh(today, IsCurrent) || !IsCurrent()) return false;
            _refreshedDate = today;
            return true;
        }
        finally { _refreshing = false; }
    }
}
