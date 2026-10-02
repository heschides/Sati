using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ConsumerScheduleApiTests(SatiApiFactory factory)
{
    private static string Route(int personId) => $"/api/v1/people/{personId}/schedule";

    private static SaveConsumerScheduleEntryRequest Appointment(int revision = 0) => new(
        ConsumerScheduleKind.DoctorAppointment, "Primary care visit", "Clinic",
        new DateTime(2026, 10, 15), null, null, ScheduleWeekdays.None,
        9 * 60, 10 * 60, ModivcareRideStatus.NeedsBooking, 8 * 60,
        10 * 60 + 30, null, revision);

    [Fact]
    public async Task ScheduleRoutesRejectOtherCaseloadAndAgency()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var otherAgency = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        using var anonymous = factory.CreateAnonymousClient();
        var savedResponse = await owner.PostAsJsonAsync(Route(101), Appointment());
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ConsumerScheduleEntryDto>())!;
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync(Route(101))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await owner.GetAsync(Route(103))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await owner.PostAsJsonAsync(Route(103), Appointment())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await otherAgency.GetAsync(Route(101))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await otherAgency.PutAsJsonAsync($"{Route(101)}/{saved.Id}",
                    Appointment(saved.Revision))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await otherAgency.DeleteAsync($"{Route(101)}/{saved.Id}?expectedRevision={saved.Revision}"))
                .StatusCode);
        }
        finally
        {
            (await owner.DeleteAsync($"{Route(101)}/{saved.Id}?expectedRevision={saved.Revision}"))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task AppointmentAndWeeklyScheduleRoundTripWithRideStateAndRevision()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var createdResponse = await client.PostAsJsonAsync(Route(101), Appointment());
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<ConsumerScheduleEntryDto>())!;
        ConsumerScheduleEntryDto? weekly = null;
        try
        {
            Assert.Equal("no-store, no-cache", createdResponse.Headers.CacheControl?.ToString());
            Assert.Equal(ModivcareRideStatus.NeedsBooking, created.RideStatus);
            Assert.Equal(1, created.Revision);
            var weeklyRequest = new SaveConsumerScheduleEntryRequest(
                ConsumerScheduleKind.DayProgram, "Community day program", null, null,
                new DateTime(2026, 10, 1), null,
                ScheduleWeekdays.Monday | ScheduleWeekdays.Wednesday | ScheduleWeekdays.Friday,
                9 * 60, 15 * 60, ModivcareRideStatus.Confirmed,
                8 * 60, 15 * 60 + 30, "ABC123", 0);
            var weeklyResponse = await client.PostAsJsonAsync(Route(101), weeklyRequest);
            weeklyResponse.EnsureSuccessStatusCode();
            weekly = (await weeklyResponse.Content.ReadFromJsonAsync<ConsumerScheduleEntryDto>())!;
            Assert.Equal(ScheduleWeekdays.Monday | ScheduleWeekdays.Wednesday |
                ScheduleWeekdays.Friday, weekly.Weekdays);
            var readResponse = await client.GetAsync(Route(101));
            Assert.Equal("no-store, no-cache", readResponse.Headers.CacheControl?.ToString());
            var rows = await readResponse.Content.ReadFromJsonAsync<List<ConsumerScheduleEntryDto>>();
            Assert.Contains(rows!, x => x.Id == created.Id);
            Assert.Contains(rows!, x => x.Id == weekly.Id);

            var updateResponse = await client.PutAsJsonAsync($"{Route(101)}/{created.Id}",
                Appointment(created.Revision) with { RideStatus = ModivcareRideStatus.Requested });
            updateResponse.EnsureSuccessStatusCode();
            var updated = (await updateResponse.Content.ReadFromJsonAsync<ConsumerScheduleEntryDto>())!;
            Assert.Equal(2, updated.Revision);
            Assert.Equal(ModivcareRideStatus.Requested, updated.RideStatus);
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PutAsJsonAsync($"{Route(101)}/{created.Id}", Appointment(created.Revision)))
                .StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.DeleteAsync($"{Route(101)}/{created.Id}?expectedRevision={created.Revision}"))
                .StatusCode);
            created = updated;
        }
        finally
        {
            if (weekly is not null)
                (await client.DeleteAsync($"{Route(101)}/{weekly.Id}?expectedRevision={weekly.Revision}"))
                    .EnsureSuccessStatusCode();
            (await client.DeleteAsync($"{Route(101)}/{created.Id}?expectedRevision={created.Revision}"))
                .EnsureSuccessStatusCode();
        }
        Assert.Contains(await factory.GetAuditEventsAsync("consumer-schedule.created"),
            x => x.ResourceId == "101");
    }

    [Fact]
    public async Task AnInvalidWeeklyPatternIsRejected()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var invalid = Appointment() with
        {
            Kind = ConsumerScheduleKind.Work,
            Date = null,
            EffectiveStart = new DateTime(2026, 10, 1)
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Route(101), invalid)).StatusCode);
    }

    [Fact]
    public async Task ADateOnlyDoctorAppointmentCanBeSavedBeforeTimesAreKnown()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var response = await client.PostAsJsonAsync(Route(101), Appointment() with
        {
            StartMinute = null, EndMinute = null,
            OutboundPickupMinute = null, ReturnPickupMinute = null
        });
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<ConsumerScheduleEntryDto>())!;
        try
        {
            Assert.Null(saved.StartMinute);
            Assert.Null(saved.EndMinute);
            Assert.Contains((await client.GetFromJsonAsync<List<ConsumerScheduleEntryDto>>(Route(101)))!,
                x => x.Id == saved.Id && x.StartMinute is null);
        }
        finally
        {
            (await client.DeleteAsync($"{Route(101)}/{saved.Id}?expectedRevision={saved.Revision}"))
                .EnsureSuccessStatusCode();
        }
    }
}
