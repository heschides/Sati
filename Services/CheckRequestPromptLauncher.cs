using Sati.Data;
using Sati.Views;
using System.Windows;

namespace Sati.Services;

/// <summary>
/// Generates due weekly drafts and owns the three reminder entry points: sign-in,
/// calendar-day rollover, and shutdown. Review opens the frozen draft; defer never
/// changes or submits it.
/// </summary>
public sealed class CheckRequestPromptLauncher(
    ICheckRequestAutomationService automation,
    CheckRequestAutomationPreferenceService preferences,
    ISessionService session,
    Func<CheckRequestPromptWindow> windowFactory,
    Func<TimeOffCheckRequestPromptWindow> timeOffWindowFactory)
{
    private readonly SemaphoreSlim promptGate = new(1, 1);
    private int? lastDailyPromptUserId;
    private DateOnly? lastDailyPromptDate;
    private int? lastDayBeforePromptUserId;
    private DateOnly? lastDayBeforePromptDate;

    /// <returns>
    /// False only when a shutdown prompt chose Review now, meaning the pending
    /// close should be cancelled so the case manager can finish the request.
    /// </returns>
    public async Task<bool> TryShowAsync(
        Window owner,
        ViewModels.ShellViewModel shell,
        CheckRequestPromptReason reason)
    {
        if (session.CurrentUser is not { HasCaseManagerPermissions: true } user)
            return true;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (reason == CheckRequestPromptReason.NewDay &&
            lastDailyPromptUserId == user.Id && lastDailyPromptDate == today)
            return true;
        if (!await promptGate.WaitAsync(0)) return true;

        try
        {
            if (!await preferences.LoadForUserAsync(user.Id)) return true;

            var pending = (await automation.EnsureWeeklyDraftsAsync()).PendingDrafts;

            if (reason != CheckRequestPromptReason.Shutdown)
            {
                lastDailyPromptUserId = user.Id;
                lastDailyPromptDate = today;
            }
            if (pending.Count == 0) return true;

            var window = windowFactory();
            window.Owner = owner;
            window.Configure(pending, reason);
            window.ShowDialog();
            if (!window.ReviewRequested || window.SelectedDraft is null) return true;

            await shell.OpenGeneratedCheckRequestDraftAsync(window.SelectedDraft);
            return reason != CheckRequestPromptReason.Shutdown;
        }
        catch (Exception error)
        {
            // A reminder must never strand someone at sign-in or prevent shutdown.
            AppErrorLog.Record(error, "check-request-reminder");
            return true;
        }
        finally
        {
            promptGate.Release();
        }
    }

    public Task TryShowNewlyScheduledTimeOffAsync(
        Window owner,
        ViewModels.ShellViewModel shell,
        DateTime timeOffDate) =>
        TryShowTimeOffAsync(owner, shell, timeOffDate.Date, newlyScheduled: true);

    public async Task TryShowDayBeforeTimeOffAsync(
        Window owner,
        ViewModels.ShellViewModel shell)
    {
        if (session.CurrentUser is not { HasCaseManagerPermissions: true } user) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (lastDayBeforePromptUserId == user.Id && lastDayBeforePromptDate == today) return;
        await TryShowTimeOffAsync(owner, shell, DateTime.Today.AddDays(1), newlyScheduled: false);
        lastDayBeforePromptUserId = user.Id;
        lastDayBeforePromptDate = today;
    }

    private async Task TryShowTimeOffAsync(
        Window owner,
        ViewModels.ShellViewModel shell,
        DateTime timeOffDate,
        bool newlyScheduled)
    {
        if (session.CurrentUser is not { HasCaseManagerPermissions: true } user) return;
        if (!await promptGate.WaitAsync(0)) return;
        try
        {
            if (!await preferences.LoadForUserAsync(user.Id)) return;
            var collisions = await automation.GetTimeOffCollisionsAsync(timeOffDate);
            if (collisions.Count == 0) return;

            var window = timeOffWindowFactory();
            window.Owner = owner;
            window.Configure(collisions, newlyScheduled);
            window.ShowDialog();
            if (!window.PrepareRequested || window.SelectedCollision is null) return;

            var result = await automation.EnsureTimeOffDraftsAsync(timeOffDate);
            var draft = result.PendingDrafts.FirstOrDefault(item =>
                item.TemplateId == window.SelectedCollision.TemplateId);
            if (draft is not null)
                await shell.OpenGeneratedCheckRequestDraftAsync(draft);
        }
        catch (Exception error)
        {
            AppErrorLog.Record(error, "time-off-check-request-reminder");
        }
        finally
        {
            promptGate.Release();
        }
    }
}
