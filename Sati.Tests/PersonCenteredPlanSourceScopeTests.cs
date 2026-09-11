using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class PersonCenteredPlanSourceScopeTests
{
    [Fact]
    public async Task SupervisorOpeningAConsumerRequestsThatConsumersAssignedAuthor()
    {
        var session = new SessionService();
        session.SetUser(User.Create(77, "supervisor", "Supervisor", string.Empty, string.Empty,
            UserRole.Supervisor, null, 1));
        var consumer = Person.Rehydrate(123, 31);
        var source = new RecordingSource();
        var viewModel = new PersonCenteredPlanViewModel(source, session);

        await viewModel.LoadPersonAsync(consumer);

        Assert.Equal(consumer.Id, source.PersonId);
        Assert.Equal(consumer.UserId, source.PreferredAuthorUserId);
    }

    private sealed class RecordingSource : IPersonCenteredPlanSourceService
    {
        public int PersonId { get; private set; }
        public int PreferredAuthorUserId { get; private set; }
        public Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId)
        {
            PersonId = personId;
            PreferredAuthorUserId = preferredAuthorUserId;
            return Task.FromResult<PersonCenteredPlanSource?>(null);
        }
    }
}
