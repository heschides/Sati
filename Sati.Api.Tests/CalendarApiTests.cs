using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class CalendarApiTests
{
    private readonly SatiApiFactory _factory;

    public CalendarApiTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CalendarYearNotesStayInsideTheSignedInUsersCaseload()
    {
        using var agencyOne = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await _factory.CreateAuthenticatedClientAsync("case-manager-two");

        var first = await agencyOne.GetFromJsonAsync<List<NoteDto>>("/api/v1/notes/year/2026");
        var second = await agencyTwo.GetFromJsonAsync<List<NoteDto>>("/api/v1/notes/year/2026");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Contains(first, note => note.Id == 501 && note.PersonId == 101);
        Assert.DoesNotContain(first, note => note.PersonId == 201);
        Assert.Contains(second, note => note.Id == 601 && note.PersonId == 201);
        Assert.DoesNotContain(second, note => note.PersonId == 101);
    }

    [Fact]
    public async Task FutureDatedEmailIsAuthoritativelyStoredAsScheduledWork()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var reminderDate = new DateTime(2097, 4, 19, 14, 30, 0);
        var response = await client.PostAsJsonAsync(
            "/api/v1/notes",
            new SaveNoteRequest(
                "Call the guardian about transportation.",
                reminderDate,
                "Logged",
                60,
                120,
                101,
                null,
                "Email",
                "should be discarded",
                null));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(created);
        Assert.Equal("Email", created.NoteType);
        Assert.Equal("Scheduled", created.Status);
        Assert.Equal(reminderDate.Date, created.EventDate);
        Assert.Equal(60, created.Minutes);
        Assert.Null(created.StartTime);
        Assert.Null(created.CaseManagerJustification);
        Assert.Null(created.GoalProgress);

        var year = await client.GetFromJsonAsync<List<NoteDto>>("/api/v1/notes/year/2097");
        Assert.Contains(year!, note =>
            note.Id == created.Id &&
            note.NoteType == "Email" &&
            note.EventDate == reminderDate.Date);

        var cleanup = await client.DeleteAsync(
            $"/api/v1/notes/{created.Id}?expectedRevision={created.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
    }

    [Fact]
    public async Task ExplicitFutureReminderStillHasTheNonServiceShape()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var reminderDate = new DateTime(2097, 5, 20);
        var response = await client.PostAsJsonAsync(
            "/api/v1/notes",
            new SaveNoteRequest(
                "Call after the planning meeting.",
                reminderDate,
                "Pending",
                30,
                60,
                101,
                "PCP",
                "Reminder",
                "discard this",
                "{}"));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<NoteDto>();

        Assert.NotNull(created);
        Assert.Equal("Reminder", created.NoteType);
        Assert.Equal("Scheduled", created.Status);
        Assert.Null(created.Minutes);
        Assert.Null(created.StartTime);
        Assert.Null(created.FormType);
        Assert.Null(created.CaseManagerJustification);
        Assert.Null(created.VisitDocumentationJson);

        var cleanup = await client.DeleteAsync(
            $"/api/v1/notes/{created.Id}?expectedRevision={created.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
    }

    [Fact]
    public async Task AnUndatedReminderCannotBecomeAnInvisibleNoteRow()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            "/api/v1/notes",
            new SaveNoteRequest(
                "This belongs in the journal route.",
                null,
                "Scheduled",
                null,
                null,
                101,
                null,
                "Reminder",
                null,
                null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The decision about whether a day counts toward the documented daily average belongs to the
    /// case manager whose day it is. The request carries no user id, so the route cannot be asked
    /// to write or read somebody else's calendar.
    /// </summary>
    [Fact]
    public async Task ServiceDayInclusionsAreNormalizedPerUserAndCannotCrossUsers()
    {
        using var owner = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        using var otherUser = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var requested = new DateTime(2097, 4, 9, 13, 20, 0);

        var setResponse = await owner.PutAsJsonAsync(
            "/api/v1/service-day-inclusions",
            new SetServiceDayInclusionRequest(requested, true));
        setResponse.EnsureSuccessStatusCode();
        var created = await setResponse.Content.ReadFromJsonAsync<ServiceDayInclusionDto>();
        Assert.NotNull(created);
        Assert.Equal(requested.Date, created.Date);
        Assert.True(created.IsIncluded);

        // One row per day: setting the same day again answers with the same record.
        var changeResponse = await owner.PutAsJsonAsync(
            "/api/v1/service-day-inclusions",
            new SetServiceDayInclusionRequest(requested.Date, false));
        changeResponse.EnsureSuccessStatusCode();
        var changed = await changeResponse.Content.ReadFromJsonAsync<ServiceDayInclusionDto>();
        Assert.Equal(created.Id, changed!.Id);
        Assert.False(changed.IsIncluded);

        var ownerDays = await owner.GetFromJsonAsync<List<ServiceDayInclusionDto>>(
            "/api/v1/service-day-inclusions/2097");
        var otherDays = await otherUser.GetFromJsonAsync<List<ServiceDayInclusionDto>>(
            "/api/v1/service-day-inclusions/2097");
        Assert.Contains(ownerDays!, item => item.Id == created.Id);
        Assert.DoesNotContain(otherDays!, item => item.Id == created.Id);

        // Another user's delete reaches only their own rows, so the owner's survives.
        var foreignDelete = await otherUser.DeleteAsync(
            $"/api/v1/service-day-inclusions/{requested:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NoContent, foreignDelete.StatusCode);
        ownerDays = await owner.GetFromJsonAsync<List<ServiceDayInclusionDto>>(
            "/api/v1/service-day-inclusions/2097");
        Assert.Contains(ownerDays!, item => item.Id == created.Id);

        var ownerDelete = await owner.DeleteAsync(
            $"/api/v1/service-day-inclusions/{requested:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        ownerDays = await owner.GetFromJsonAsync<List<ServiceDayInclusionDto>>(
            "/api/v1/service-day-inclusions/2097");
        Assert.DoesNotContain(ownerDays!, item => item.Id == created.Id);

        var malformed = await owner.DeleteAsync("/api/v1/service-day-inclusions/not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    /// <summary>
    /// The read takes a user id so a reviewer can see a case manager's settled zero days. It is
    /// gated before it reaches the query, so it cannot be pointed at somebody else's calendar.
    /// </summary>
    [Fact]
    public async Task AReadForAnotherUsersServiceDaysIsRefused()
    {
        using var owner = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        using var otherUser = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var requested = new DateTime(2096, 3, 6);

        (await owner.PutAsJsonAsync(
            "/api/v1/service-day-inclusions",
            new SetServiceDayInclusionRequest(requested, true))).EnsureSuccessStatusCode();
        var mine = await owner.GetFromJsonAsync<List<ServiceDayInclusionDto>>(
            "/api/v1/service-day-inclusions/2096");
        var ownerId = Assert.Single(mine!).Id;

        // An id the actor cannot reach is refused, whether it is nobody or another case manager.
        var unknown = await owner.GetAsync("/api/v1/service-day-inclusions/2096?userId=0");
        var foreign = await otherUser.GetAsync(
            $"/api/v1/service-day-inclusions/2096?userId={await UserIdOfAsync(owner)}");

        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.NotEqual(0, ownerId);
    }

    private static async Task<int> UserIdOfAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/me");
        return me!["id"].GetInt32();
    }

    [Fact]
    public async Task ServiceDayInclusionsRefuseAnonymousCallers()
    {
        using var anonymous = _factory.CreateClient();

        var read = await anonymous.GetAsync("/api/v1/service-day-inclusions/2097");
        var write = await anonymous.PutAsJsonAsync(
            "/api/v1/service-day-inclusions",
            new SetServiceDayInclusionRequest(new DateTime(2097, 4, 9), true));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task ExemptDateLifecycleIsNormalizedAndCannotCrossUsers()
    {
        using var owner = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        using var otherUser = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var requested = new DateTime(2098, 6, 12, 15, 45, 0);

        var createResponse = await owner.PostAsJsonAsync(
            "/api/v1/exempt-dates",
            new AddExemptDateRequest(requested, "Calendar API test"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ExemptDateDto>();
        Assert.NotNull(created);
        Assert.Equal(requested.Date, created.Date);

        var ownerDates = await owner.GetFromJsonAsync<List<ExemptDateDto>>(
            "/api/v1/exempt-dates/2098");
        var otherDates = await otherUser.GetFromJsonAsync<List<ExemptDateDto>>(
            "/api/v1/exempt-dates/2098");
        Assert.Contains(ownerDates!, item => item.Id == created.Id);
        Assert.DoesNotContain(otherDates!, item => item.Id == created.Id);

        var foreignDelete = await otherUser.DeleteAsync($"/api/v1/exempt-dates/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        ownerDates = await owner.GetFromJsonAsync<List<ExemptDateDto>>(
            "/api/v1/exempt-dates/2098");
        Assert.Contains(ownerDates!, item => item.Id == created.Id);

        var ownerDelete = await owner.DeleteAsync($"/api/v1/exempt-dates/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        ownerDates = await owner.GetFromJsonAsync<List<ExemptDateDto>>(
            "/api/v1/exempt-dates/2098");
        Assert.DoesNotContain(ownerDates!, item => item.Id == created.Id);
    }
}
