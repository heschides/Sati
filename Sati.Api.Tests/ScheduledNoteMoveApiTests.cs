using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ScheduledNoteMoveApiTests(SatiApiFactory factory)
{
    private const string MovesUrl = "/api/v1/notes/schedule-moves/year/2197";

    [Fact]
    public async Task NoteDateMovePersistsFrozenPlanAndStaleEditCannotAppendHistory()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var otherAgency = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        using var anonymous = factory.CreateAnonymousClient();
        var first = new DateTime(2197, 4, 2);
        var second = first.AddDays(1);
        var request = new SaveNoteRequest(
            "Scheduled work must stay private.", first, "Scheduled", 20, null,
            101, null, "Contact", null, null);
        var create = await owner.PostAsJsonAsync("/api/v1/notes", request);
        create.EnsureSuccessStatusCode();
        var note = (await create.Content.ReadFromJsonAsync<NoteDto>())!;

        try
        {
            var move = await owner.PutAsJsonAsync($"/api/v1/notes/{note.Id}",
                request with { EventDate = second, Minutes = 45, ExpectedRevision = note.Revision });
            move.EnsureSuccessStatusCode();
            var moved = (await move.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal(note.Revision + 1, moved.Revision);

            var history = await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(MovesUrl);
            var entry = Assert.Single(history!, row => row.NoteId == note.Id);
            Assert.Equal(first, entry.FromDate);
            Assert.Equal(second, entry.ToDate);
            Assert.Equal(20, entry.ScheduledMinutes);
            Assert.Equal(2, entry.ScheduledUnits);
            Assert.DoesNotContain("Scheduled work must stay private.",
                await (await owner.GetAsync(MovesUrl)).Content.ReadAsStringAsync());

            var stale = await owner.PutAsJsonAsync($"/api/v1/notes/{note.Id}",
                request with { EventDate = first.AddDays(2), ExpectedRevision = note.Revision });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            history = await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(MovesUrl);
            Assert.Single(history!, row => row.NoteId == note.Id);

            Assert.DoesNotContain((await otherAgency.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(MovesUrl))!,
                row => row.NoteId == note.Id);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(MovesUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await owner.GetAsync("/api/v1/notes/schedule-moves/year/1800")).StatusCode);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var stored = await db.Notes.SingleAsync(row => row.Id == note.Id);
            db.Notes.Remove(stored);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task MoveReadRequiresAllFrozenAndCurrentTenantMarkers()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var day = new DateTime(2197, 5, 12);
        var notes = new[]
        {
            new ServerNote { PersonId = 101, AgencyId = 1, EventDate = day.AddDays(1),
                Status = (int)NoteStatus.Scheduled, Minutes = 15, NoteType = (int)NoteType.Contact },
            new ServerNote { PersonId = 101, AgencyId = 2, EventDate = day.AddDays(1),
                Status = (int)NoteStatus.Scheduled, Minutes = 15, NoteType = (int)NoteType.Contact },
            new ServerNote { PersonId = 201, AgencyId = 1, EventDate = day.AddDays(1),
                Status = (int)NoteStatus.Scheduled, Minutes = 15, NoteType = (int)NoteType.Contact },
            new ServerNote { PersonId = 201, AgencyId = 1, EventDate = day.AddDays(1),
                Status = (int)NoteStatus.Scheduled, Minutes = 15, NoteType = (int)NoteType.Contact }
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.Notes.AddRange(notes);
        await db.SaveChangesAsync();
        var moves = new[]
        {
            NewMove(notes[0], 101, 1, 12, day, 2),
            NewMove(notes[0], 101, 2, 12, day.AddDays(2), 3),
            NewMove(notes[0], 101, 1, 13, day.AddDays(3), 4),
            NewMove(notes[1], 101, 1, 12, day.AddDays(4), 2),
            NewMove(notes[2], 201, 1, 12, day.AddDays(5), 2),
            NewMove(notes[3], 101, 1, 12, day.AddDays(6), 2)
        };
        db.ScheduledNoteMoves.AddRange(moves);
        await db.SaveChangesAsync();
        try
        {
            var rows = await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(MovesUrl);
            var only = Assert.Single(rows!);
            Assert.Equal(moves[0].Id, only.Id);
            Assert.Equal(101, only.PersonId);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ScheduledNoteMoves.Where(move => moves.Select(item => item.Id).Contains(move.Id))
                .ExecuteDeleteAsync();
            await db.Notes.Where(note => notes.Select(item => item.Id).Contains(note.Id))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConfirmedManualFormConversionAlsoRecordsThePriorPlan()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var completedOn = DateTime.Today.AddDays(-1);
        var plannedOn = DateTime.Today.AddDays(1);
        int personId;
        int formId;
        int noteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(row => row.Id) + 1;
            var person = new ServerPerson
            {
                Id = personId,
                UserId = 12,
                AgencyId = 1,
                FirstName = "Scheduled",
                LastName = "Form",
                BirthDate = new DateTime(1990, 1, 1),
                EffectiveDate = completedOn.AddYears(-1)
            };
            var form = new ServerForm
            {
                PersonId = personId,
                Type = "SafetyPlan",
                DueDate = completedOn,
                TargetEffectiveDate = completedOn
            };
            db.People.Add(person);
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
            var note = new ServerNote
            {
                PersonId = personId,
                AgencyId = 1,
                Narrative = "Prepare the Safety Plan.",
                EventDate = plannedOn,
                Status = NoteWorkflow.Scheduled,
                Minutes = 30,
                NoteType = (int)NoteType.Form,
                Activities = (int)NoteActivity.Form,
                FormType = (int)FormType.SafetyPlan,
                FormId = formId
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        try
        {
            var url = $"/api/v1/people/{personId}/forms/SafetyPlan/attestation";
            var first = await owner.PostAsJsonAsync(url,
                new AttestFormRequest(formId, completedOn));
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            var preview = (await first.Content.ReadFromJsonAsync<ApiErrorDto>())!;
            var confirmed = await owner.PostAsJsonAsync(url,
                new AttestFormRequest(formId, completedOn, noteId,
                    ConfirmScheduledNoteConversion: true,
                    ScheduledNoteConversionToken: preview.ConfirmationToken));
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

            var history = await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(
                $"/api/v1/notes/schedule-moves/year/{plannedOn.Year}");
            var move = Assert.Single(history!, row => row.NoteId == noteId);
            Assert.Equal(plannedOn, move.FromDate);
            Assert.Equal(completedOn, move.ToDate);
            Assert.Equal(30, move.ScheduledMinutes);
            Assert.Equal(2, move.ScheduledUnits);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.FormAttestations.Where(row => row.FormId == formId).ExecuteDeleteAsync();
            await db.Notes.Where(row => row.PersonId == personId).ExecuteDeleteAsync();
            await db.Forms.Where(row => row.PersonId == personId).ExecuteDeleteAsync();
            await db.People.Where(row => row.Id == personId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CalendarYearAndMoveHistoryExcludeGhostConsumers()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var day = new DateTime(2197, 6, 2);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var ghost = new ServerPerson
        {
            UserId = 12,
            AgencyId = 1,
            FirstName = "Ghost",
            LastName = "Calendar",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = new DateTime(2025, 1, 1),
            Status = (int)PersonStatus.Ghost
        };
        db.People.Add(ghost);
        await db.SaveChangesAsync();
        var note = new ServerNote
        {
            PersonId = ghost.Id,
            AgencyId = 1,
            EventDate = day.AddDays(1),
            Status = NoteWorkflow.Scheduled,
            Minutes = 15,
            NoteType = (int)NoteType.Contact
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        db.ScheduledNoteMoves.Add(NewMove(note, ghost.Id, 1, 12, day, 2));
        await db.SaveChangesAsync();
        try
        {
            Assert.DoesNotContain((await owner.GetFromJsonAsync<List<NoteDto>>(
                "/api/v1/notes/year/2197"))!, row => row.Id == note.Id);
            Assert.DoesNotContain((await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(
                MovesUrl))!, row => row.NoteId == note.Id);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ScheduledNoteMoves.Where(row => row.NoteId == note.Id).ExecuteDeleteAsync();
            await db.Notes.Where(row => row.Id == note.Id).ExecuteDeleteAsync();
            await db.People.Where(row => row.Id == ghost.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task MovingAReminderDoesNotCreateRescheduledUnits()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var first = new DateTime(2197, 8, 2);
        var request = new SaveNoteRequest(
            "Calendar reminder", first, "Scheduled", 30, null,
            101, null, "Reminder", null, null);
        var createdResponse = await owner.PostAsJsonAsync("/api/v1/notes", request);
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<NoteDto>())!;
        try
        {
            var move = await owner.PutAsJsonAsync($"/api/v1/notes/{created.Id}",
                request with { EventDate = first.AddDays(1), ExpectedRevision = created.Revision });
            move.EnsureSuccessStatusCode();
            Assert.DoesNotContain((await owner.GetFromJsonAsync<List<ScheduledNoteMoveDto>>(
                MovesUrl))!, row => row.NoteId == created.Id);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var stored = await db.Notes.SingleAsync(row => row.Id == created.Id);
            db.Notes.Remove(stored);
            await db.SaveChangesAsync();
        }
    }

    private static ScheduledNoteMove NewMove(
        ServerNote note, int personId, int agencyId, int userId,
        DateTime fromDate, int revision) => new()
    {
        NoteId = note.Id,
        PersonId = personId,
        AgencyId = agencyId,
        UserId = userId,
        FromDate = fromDate,
        ToDate = note.EventDate!.Value.Date,
        ScheduledMinutes = 15,
        ScheduledUnits = 1,
        NoteRevision = revision,
        MovedAtUtc = DateTime.UtcNow
    };
}
