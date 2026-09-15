using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class ScratchpadHistoryViewModelTests
{
    [Fact]
    public async Task SavedCommentAppearsInlineWithoutClosingHistory()
    {
        var session = Session(41, "Case Manager");
        var entry = new Scratchpad
        {
            Id = 7,
            UserId = 41,
            Date = DateTime.Today.AddDays(-1),
            Content = "Called the housing office."
        };
        var service = new RecordingScratchpadService([entry]);
        var viewModel = new ScratchpadHistoryViewModel(service, session);

        await viewModel.InitializeAsync();
        viewModel.ToggleEditorCommand.Execute(null);
        viewModel.NewComment = "The office returned the call the next morning.";
        await viewModel.SaveCommentCommand.ExecuteAsync(null);

        var saved = Assert.Single(entry.Comments);
        Assert.Equal("The office returned the call the next morning.", saved.Content);
        Assert.False(viewModel.IsEditorUnlocked);
        Assert.Equal(string.Empty, viewModel.NewComment);
        Assert.Equal("Comment saved.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ClearedOldAccountLoadCannotPublishHistoryLater()
    {
        var session = Session(41, "Old User");
        var service = new BlockingScratchpadService();
        var viewModel = new ScratchpadHistoryViewModel(service, session);

        var oldLoad = viewModel.InitializeAsync();
        await service.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Clear();
        session.SetUser(User.Create(
            42, "new.user", "New User", "hash", "salt",
            UserRole.CaseManager, null, 3));
        service.ReleaseLoad.SetResult();
        await oldLoad;

        Assert.Empty(viewModel.VisibleEntries);
        Assert.Null(viewModel.SelectionStart);
        Assert.False(viewModel.IsLoading);
    }

    private static SessionService Session(int id, string displayName)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            id, $"user.{id}", displayName, "hash", "salt",
            UserRole.CaseManager, null, 3));
        return session;
    }

    private class RecordingScratchpadService(List<Scratchpad> history) : IScratchpadService
    {
        public virtual Task<List<Scratchpad>> GetHistoryAsync(int userId) =>
            Task.FromResult(history);

        public Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId,
            int userId,
            string authorDisplayName,
            string content) => Task.FromResult(new ScratchpadComment
        {
            Id = 11,
            ScratchpadId = scratchpadId,
            AuthorUserId = userId,
            AuthorDisplayName = authorDisplayName,
            CreatedAtUtc = DateTime.UtcNow,
            Content = content
        });

        public Task<Scratchpad> LoadTodayAsync(int userId) => throw new NotSupportedException();
        public Task<Scratchpad> LoadTomorrowAsync(int userId) => throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => throw new NotSupportedException();
    }

    private sealed class BlockingScratchpadService : RecordingScratchpadService
    {
        public BlockingScratchpadService()
            : base([])
        {
        }

        public TaskCompletionSource LoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<List<Scratchpad>> GetHistoryAsync(int userId)
        {
            LoadStarted.SetResult();
            await ReleaseLoad.Task;
            return
            [
                new Scratchpad
                {
                    Id = 17,
                    UserId = userId,
                    Date = DateTime.Today.AddDays(-1),
                    Content = "Old account history"
                }
            ];
        }
    }
}
