using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class PersonPhotoViewModelTests
{
    [Fact]
    public async Task ASlowPreviousConsumerPhotoCannotReplaceTheCurrentConsumerPhoto()
    {
        var service = new DelayedPhotoService();
        var session = new Session();
        var viewModel = new PersonPhotoViewModel(service, session);
        var first = Person.Rehydrate(41, session.CurrentUser!.Id);
        var second = Person.Rehydrate(42, session.CurrentUser.Id);

        var firstLoad = viewModel.LoadForAsync(first);
        var secondLoad = viewModel.LoadForAsync(second);
        service.Complete(42, [4, 2]);
        await secondLoad;
        service.Complete(41, [4, 1]);
        await firstLoad;

        Assert.Equal([4, 2], viewModel.PhotoBytes);
        Assert.False(viewModel.IsLoading);
    }

    private sealed class DelayedPhotoService : IPersonPhotoService
    {
        private readonly Dictionary<int, TaskCompletionSource<PersonPhotoDto?>> _loads = [];

        public Task<PersonPhotoDto?> GetAsync(int personId)
        {
            var source = new TaskCompletionSource<PersonPhotoDto?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _loads.Add(personId, source);
            return source.Task;
        }

        public void Complete(int personId, byte[] content) =>
            _loads[personId].SetResult(new PersonPhotoDto("image/png", content, 1, 1, 1));

        public Task<PersonPhotoDto> SaveAsync(
            int personId,
            string contentType,
            byte[] content,
            long? expectedRevision) => throw new NotSupportedException();

        public Task DeleteAsync(int personId, long expectedRevision) =>
            throw new NotSupportedException();
    }

    private sealed class Session : ISessionService
    {
        public bool AllowComplianceOverride { get; set; }
        public User? CurrentUser { get; private set; } =
            User.Create(7, "photo.cm", "Photo Case Manager", "hash", "salt", UserRole.CaseManager, null, 1);

        public void SetUser(User user) => CurrentUser = user;
    }
}
