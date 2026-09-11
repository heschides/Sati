using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class PermissionAwareShellStartupTests
{
    [Theory]
    [InlineData("InitializeAsync")]
    [InlineData("ReinitializeAsync")]
    public void ShellInitializesOwnCaseworkOnlyInsideTheCaseManagementGate(string method)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "ViewModels", "ShellViewModel.cs"));
        var initialize = Body(source, $"public async Task {method}()");
        var gatedWork = Body(initialize, "if (IsCaseManagementAvailable)");
        string[] caseworkCalls = ["await NotesViewModel.InitializeAsync();",
            "await NotesViewModel.NotesLog.NoteEntry.InitializeAsync();",
            "await NotesViewModel.NotesLog.ReloadAsync();", "await NotesViewModel.Clients.ReloadAsync();"];
        foreach (var call in caseworkCalls)
        {
            Assert.Contains(call, gatedWork);
            Assert.DoesNotContain(call, initialize.Replace(gatedWork, string.Empty, StringComparison.Ordinal));
        }
        Assert.DoesNotContain("Scratchpad.InitializeAsync", gatedWork);
        Assert.DoesNotContain("NavigateByRoleAsync", gatedWork);
        Assert.Contains("await Scratchpad.InitializeAsync();", initialize);
        Assert.Contains("await NavigateByRoleAsync();", initialize);
    }

    [Theory]
    [InlineData(UserPermissions.Billing)]
    [InlineData(UserPermissions.Supervision)]
    [InlineData(UserPermissions.Administration)]
    [InlineData(UserPermissions.None)]
    public async Task NonCaseManagerRetainsPersonalScratchpadsWithoutLoadingConsumerSchedule(UserPermissions permissions)
    {
        var session = Session(permissions);
        var personal = new PersonalScratchpads();
        var work = new RecordingWork();
        var viewModel = new ScratchpadViewModel(personal, session, work);
        try
        {
            await viewModel.InitializeAsync();
            Assert.Equal("PERSONAL TODAY", viewModel.ScratchpadContent);
            Assert.Equal("PERSONAL TOMORROW", viewModel.TomorrowAgendaContent);
            Assert.Equal(2, personal.LoadCalls);
            Assert.Equal(0, work.LoadCalls);
            Assert.False(viewModel.HasScheduledWorkItems);
            Assert.False(viewModel.HasScheduledWorkLoadError);
        }
        finally { viewModel.ClearForAccountSwitch(); }
    }

    [Fact]
    public async Task SameAccountLosingCaseManagementClearsConsumerScheduleButKeepsPersonalText()
    {
        var session = Session(UserPermissions.CaseManagement | UserPermissions.Billing);
        var work = new RecordingWork();
        var viewModel = new ScratchpadViewModel(new PersonalScratchpads(), session, work);
        try
        {
            await viewModel.InitializeAsync();
            Assert.True(viewModel.HasScheduledWorkItems);
            session.CurrentUser!.Permissions = UserPermissions.Billing;
            await viewModel.InitializeAsync();
            Assert.Equal(1, work.LoadCalls);
            Assert.False(viewModel.HasScheduledWorkItems);
            Assert.False(viewModel.HasScheduledWorkLoadError);
            Assert.Equal("PERSONAL TODAY", viewModel.ScratchpadContent);
        }
        finally { viewModel.ClearForAccountSwitch(); }
    }

    [Fact]
    public async Task DelayedConsumerScheduleCannotPublishAfterCaseManagementIsRemoved()
    {
        var session = Session(UserPermissions.CaseManagement);
        var work = new RecordingWork { Hold = true };
        var viewModel = new ScratchpadViewModel(new PersonalScratchpads(), session, work);
        try
        {
            var loading = viewModel.RefreshScheduledWorkAsync();
            await work.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.CurrentUser!.Permissions = UserPermissions.Billing;
            work.Release.SetResult();
            Assert.False(await loading);
            Assert.False(viewModel.HasScheduledWorkItems);
        }
        finally { work.Release.TrySetResult(); viewModel.ClearForAccountSwitch(); }
    }

    [Fact]
    public async Task NonCaseManagerCannotAddStructuredWorkThroughScratchpad()
    {
        var session = Session(UserPermissions.Billing);
        var work = new RecordingWork();
        var viewModel = new ScratchpadViewModel(new PersonalScratchpads(), session, work);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            viewModel.AddDailyAgendaItemsAsync([], DateOnly.FromDateTime(DateTime.Today)));
        Assert.Equal(0, work.AddCalls);
    }

    [Fact]
    public async Task NonCaseManagerCannotOpenRetainedStructuredWork()
    {
        var viewModel = new ScratchpadViewModel(new PersonalScratchpads(), Session(UserPermissions.Billing));
        var opened = false;
        viewModel.ScheduledWorkOpeningAsync = _ => { opened = true; return Task.CompletedTask; };
        await viewModel.OpenScheduledWorkCommand.ExecuteAsync(WorkItem());
        Assert.False(opened);
    }

    [Fact]
    public async Task CurrentCaseManagerStillLoadsAndOpensStructuredWork()
    {
        var work = new RecordingWork();
        var viewModel = new ScratchpadViewModel(new PersonalScratchpads(), Session(UserPermissions.CaseManagement), work);
        try
        {
            await viewModel.InitializeAsync();
            Assert.Equal(1, work.LoadCalls);
            Assert.True(viewModel.HasScheduledWorkItems);
            var opened = false;
            viewModel.ScheduledWorkOpeningAsync = _ => { opened = true; return Task.CompletedTask; };
            await viewModel.OpenScheduledWorkCommand.ExecuteAsync(WorkItem());
            Assert.True(opened);
        }
        finally { viewModel.ClearForAccountSwitch(); }
    }

    private static string Body(string source, string signature)
    {
        var declaration = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(declaration >= 0, $"Expected capability boundary: {signature}");
        var start = source.IndexOf('{', declaration);
        var depth = 1;
        for (var index = start + 1; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[(start + 1)..index];
        }
        throw new InvalidOperationException("Unclosed method body.");
    }

    private static SessionService Session(UserPermissions permissions)
    {
        var session = new SessionService();
        var user = User.Create(41, "synthetic", "Synthetic User", string.Empty, string.Empty, UserRole.CaseManager, null, 1);
        user.Permissions = permissions;
        session.SetUser(user);
        return session;
    }

    private static WorkAgendaItem WorkItem() => new(Note.Create("Synthetic scheduled task", DateTime.Today,
        NoteStatus.Scheduled, 15, 123, null, NoteType.Visit));

    private sealed class RecordingWork : IWorkAgendaService
    {
        public int LoadCalls { get; private set; }
        public int AddCalls { get; private set; }
        public bool Hold { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<WorkAgendaItem>> LoadAsync(int userId, DateTime date)
        {
            LoadCalls++;
            Started.TrySetResult();
            if (Hold) await Release.Task;
            return [WorkItem()];
        }
        public Task<WorkAgendaAddResult> AddFromDailyAgendaAsync(int userId, DateTime date,
            IReadOnlyList<DailyAgendaItem> selectedItems)
        { AddCalls++; return Task.FromResult(new WorkAgendaAddResult(selectedItems.Count, 0)); }
    }

    private sealed class PersonalScratchpads : IScratchpadService
    {
        public int LoadCalls { get; private set; }
        public Task<Scratchpad> LoadTodayAsync(int userId)
        { LoadCalls++; return Task.FromResult(new Scratchpad { Id = 1, UserId = userId, Date = DateTime.Today, Content = "PERSONAL TODAY" }); }
        public Task<Scratchpad> LoadTomorrowAsync(int userId)
        { LoadCalls++; return Task.FromResult(new Scratchpad { Id = 2, UserId = userId, Date = DateTime.Today.AddDays(1), Content = "PERSONAL TOMORROW" }); }
        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(int scratchpadId, int userId, string authorDisplayName, string content) => throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => Task.CompletedTask;
    }
}
