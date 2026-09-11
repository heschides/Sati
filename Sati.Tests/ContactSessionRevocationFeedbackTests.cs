using System.Reflection;
using System.Net;
using System.Net.Http;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class ContactSessionRevocationFeedbackTests
{
    [Theory]
    [InlineData("local-session")]
    [InlineData("cloud-session")]
    [InlineData("permission")]
    [InlineData("network")]
    [InlineData("server")]
    public async Task RefusedOrUnconfirmedSaveRetainsContactDraftAndReportsInline(string failure)
    {
        Exception exception = failure switch
        {
            "local-session" => new SessionExpiredException(new UnauthorizedAccessException()),
            "cloud-session" => new CloudSessionEndedException(),
            "permission" => new UnauthorizedAccessException("Permission revoked."),
            "server" => new CloudApiException(HttpStatusCode.InternalServerError, "Unavailable", null),
            _ => CloudConnectivityException.From(new HttpRequestException("Synthetic network failure"), false)
        };
        var contacts = new Contacts { Failure = exception };
        var (vm, _) = Create(contacts);
        await vm.SaveContactCommand.ExecuteAsync(null);
        Assert.True(vm.IsContactEditorOpen);
        Assert.Equal("Synthetic", vm.ContactFirstName);
        Assert.Equal("Contact", vm.ContactLastName);
        Assert.Equal(1, contacts.Saves);
        Assert.Equal(0, contacts.Reads);
        if (failure.Contains("session"))
        {
            Assert.Contains("Sign in again", vm.ContactStatusMessage);
            Assert.Contains("refresh the contact list before retrying", vm.ContactStatusMessage);
        }
        else if (failure == "permission") Assert.Contains("not saved", vm.ContactStatusMessage);
        else Assert.Contains("could not confirm", vm.ContactStatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSaveCannotClearAnotherPersonOrReauthenticatedSessionsEditor(bool newSession)
    {
        var contacts = new Contacts { Hold = true };
        var (vm, session) = Create(contacts);
        var pending = vm.SaveContactCommand.ExecuteAsync(null);
        await contacts.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (newSession) session.SetUser(User.Create(1, "synthetic", "Synthetic", "", "", UserRole.CaseManager, null, 1));
        else SelectWithoutLoadingChildren(vm, Person.Rehydrate(2, 1, DateTime.UtcNow));
        vm.ContactFirstName = "New editor draft";
        contacts.Release.TrySetResult(new PersonContact { PersonId = 1 });
        await pending;
        Assert.True(vm.IsContactEditorOpen);
        Assert.Equal("New editor draft", vm.ContactFirstName);
        Assert.Equal(0, contacts.Reads);
    }

    [Fact]
    public async Task SuccessfulCurrentSaveRefreshesAndClosesItsEditor()
    {
        var contacts = new Contacts();
        var (vm, _) = Create(contacts);
        await vm.SaveContactCommand.ExecuteAsync(null);
        Assert.False(vm.IsContactEditorOpen);
        Assert.Equal(string.Empty, vm.ContactFirstName);
        Assert.Equal(1, contacts.Saves);
        Assert.Equal(1, contacts.Reads);
    }

    private static (NewClientViewModel Vm, SessionService Session) Create(Contacts contacts)
    {
        var session = new SessionService();
        session.SetUser(User.Create(1, "synthetic", "Synthetic", "", "", UserRole.CaseManager, null, 1));
        var vm = new NewClientViewModel(null!, session, null!, null!, new SettingsService(), null!,
            contacts, null!, null!, null!, null!, null!, null!, null!, null!);
        SelectWithoutLoadingChildren(vm, Person.Rehydrate(1, 1, DateTime.UtcNow));
        vm.IsContactEditorOpen = true;
        vm.ContactFirstName = "Synthetic";
        vm.ContactLastName = "Contact";
        return (vm, session);
    }

    // Isolate the contact command from unrelated consumer-workspace children.
    private static void SelectWithoutLoadingChildren(NewClientViewModel vm, Person person) =>
        typeof(NewClientViewModel).GetField("selectedPerson", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, person);

    private sealed class SettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class Contacts : IPersonContactService
    {
        public Exception? Failure;
        public bool Hold;
        public int Saves, Reads;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PersonContact> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PersonContact> SaveAsync(PersonContact contact)
        {
            Saves++;
            Started.TrySetResult();
            return Failure is not null ? Task.FromException<PersonContact>(Failure)
                : Hold ? Release.Task : Task.FromResult(contact);
        }
        public Task<List<PersonContact>> GetActiveByPersonAsync(int personId)
        {
            Reads++;
            return Task.FromResult(new List<PersonContact>());
        }
        public Task ArchiveAsync(int contactId) => throw new NotSupportedException();
    }
}
