using Sati.Data;
using Sati.Views;
using System.Windows;

namespace Sati.Services;

public enum UndocumentedDayPromptReason
{
    SignIn,
    Shutdown
}

/// <summary>
/// Warns about workdays whose documentation window is about to close with nothing written up.
/// When one of those days passes out of the window its units are gone for good, the month's
/// requirement does not shrink, and the pace needed for every remaining day steps up. That
/// warning is worth a modal, but only while the case manager can still act on it.
/// </summary>
/// <remarks>
/// The launcher is deliberately thin: which days are at risk is
/// <see cref="Contracts.V1.ProductivityForecast.DaysNearingTheirDocumentationDeadline"/>'s
/// decision, and this only decides when to show it and what the buttons do.
/// </remarks>
public sealed class UndocumentedDayPromptLauncher(
    ISessionService session,
    Func<UndocumentedDayPromptWindow> windowFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int? _lastPromptUserId;
    private DateOnly? _lastPromptDate;

    /// <returns>
    /// False only when a shutdown prompt chose to write the notes now, meaning the pending close
    /// should be cancelled. A failure always returns true: a reminder must never strand someone
    /// at sign-in or stop them closing Sati.
    /// </returns>
    public async Task<bool> TryShowAsync(
        Window owner,
        ViewModels.CaseManagerDashboardViewModel dashboard,
        UndocumentedDayPromptReason reason)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        if (session.CurrentUser is not { HasCaseManagerPermissions: true } user)
            return true;

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (reason == UndocumentedDayPromptReason.SignIn &&
            _lastPromptUserId == user.Id && _lastPromptDate == today)
        {
            return true;
        }

        if (!await _gate.WaitAsync(0))
            return true;

        try
        {
            var atRisk = dashboard.DaysNearingTheirDocumentationDeadline;
            if (reason == UndocumentedDayPromptReason.SignIn)
            {
                _lastPromptUserId = user.Id;
                _lastPromptDate = today;
            }

            if (atRisk.Count == 0)
                return true;

            var window = windowFactory();
            window.Owner = owner;
            window.Configure(atRisk, dashboard.DocumentationWindowDaysForDisplay, reason);
            window.ShowDialog();
            return !(reason == UndocumentedDayPromptReason.Shutdown && window.WriteThemNowRequested);
        }
        catch (Exception error)
        {
            AppErrorLog.Record(error, "undocumented-day-reminder");
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
