using Sati.Data;
using Sati.Views;
using System.Windows;

namespace Sati.Services;

/// <summary>
/// Shows <see cref="LeftoverScheduledWorkWindow"/> at shutdown and carries out the choices.
/// Which items qualify, and what the next workday is, belong to
/// <see cref="ILeftoverScheduledWorkService"/>; this only decides when to ask.
/// </summary>
public sealed class LeftoverScheduledWorkPromptLauncher(
    ISessionService session,
    ILeftoverScheduledWorkService service,
    Func<LeftoverScheduledWorkWindow> windowFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <returns>
    /// False only when the case manager chose to keep Sati open. A failure to load always
    /// returns true: a clean-up prompt must never stop someone closing Sati.
    /// </returns>
    public async Task<bool> TryShowAsync(Window owner)
    {
        if (session.CurrentUser is not { HasCaseManagerPermissions: true } user)
            return true;
        if (!await _gate.WaitAsync(0))
            return true;

        try
        {
            var today = DateTime.Today;
            var items = await service.LoadAsync(user.Id, today);
            if (items.Count == 0)
                return true;

            if (await service.NextWorkdayAsync(user.Id, today) is not DateTime nextWorkday)
                return true;

            var window = windowFactory();
            window.Owner = owner;
            window.Configure(items, nextWorkday, today);
            if (window.ShowDialog() != true)
                return false;

            var result = await service.ApplyAsync(window.Decisions, nextWorkday);
            if (result.Failed > 0)
            {
                MessageBox.Show(
                    owner,
                    (result.Failed == 1 ? "One item" : $"{result.Failed} items") +
                    " could not be changed and were left where they were. " +
                    "Sati will ask about them again the next time it closes.",
                    "Some Scheduled Work Was Not Changed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }
        catch (Exception error)
        {
            AppErrorLog.Record(error, "leftover-scheduled-work.prompt");
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
