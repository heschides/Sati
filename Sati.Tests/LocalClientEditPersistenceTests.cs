using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Saving a client in local Production writes the client row and any newly generated forms,
/// never the rest of the loaded graph. Marking the whole graph modified made every save of a
/// client with release history fail, because release rows are retained records.
/// </summary>
public sealed class LocalClientEditPersistenceTests
{
    [Fact]
    public async Task AClientWithReleaseHistoryCanBeEditedAfterAddingAWaiverProvider()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var providerId = await PrepareAsync(fixture);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var loaded = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);
        Assert.NotEmpty(loaded.ReleaseObligations);

        await new ConsumerProviderService(fixture.Factory, Session(fixture)).SaveAsync(new PersonProvider
        {
            PersonId = loaded.Id,
            ProviderId = providerId,
            StartDate = DateTime.Today.AddDays(-5)
        });

        var revision = loaded.Revision;
        loaded.PhoneNumber = "207-555-0100";
        await people.EditPersonAsync(loaded);
        Assert.Equal(revision + 1, loaded.Revision);

        // The in-memory copy stays usable for the next save.
        loaded.PhoneNumber = "207-555-0101";
        await people.EditPersonAsync(loaded);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.People.AsNoTracking().SingleAsync(item => item.Id == loaded.Id);
        Assert.Equal("207-555-0101", stored.PhoneNumber);
        Assert.Equal(revision + 2, stored.Revision);
    }

    [Fact]
    public async Task EffectiveDateCannotChangeAfterAnnualObligationsExist()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareAsync(fixture);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var loaded = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);
        Assert.NotEmpty(loaded.Forms);
        var originalDate = loaded.EffectiveDate;

        loaded.EffectiveDate = originalDate!.Value.AddDays(1);
        var error = await Assert.ThrowsAsync<PersonValidationException>(
            () => people.EditPersonAsync(loaded));
        Assert.Contains("effectiveDate", error.Errors.Keys);

        await using var db = fixture.Factory.CreateDbContext();
        var persisted = await db.People.AsNoTracking()
            .SingleAsync(item => item.Id == loaded.Id);
        Assert.Equal(originalDate, persisted.EffectiveDate);
    }

    [Fact]
    public async Task AClientChangedElsewhereIsRefusedAndTheLocalCopyStaysRetryable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareAsync(fixture);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var first = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);
        var second = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);

        second.PhoneNumber = "207-555-0120";
        await people.EditPersonAsync(second);

        first.PhoneNumber = "207-555-0121";
        var revision = first.Revision;
        await Assert.ThrowsAsync<PersonConcurrencyException>(() => people.EditPersonAsync(first));
        Assert.Equal(revision, first.Revision);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal("207-555-0120",
            (await db.People.AsNoTracking().SingleAsync(item => item.Id == fixture.PersonOneId)).PhoneNumber);
    }

    [Fact]
    public async Task SavingAClientDoesNotRewriteItsNotes()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareAsync(fixture);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var loaded = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);
        var loadedNote = Assert.Single(loaded.Notes);

        // Someone else changes the note after this caseload was loaded.
        await using (var other = fixture.Factory.CreateDbContext())
        {
            var note = await other.Notes.SingleAsync(item => item.Id == loadedNote.Id);
            note.Narrative = "Corrected elsewhere.";
            note.Revision++;
            await other.SaveChangesAsync();
        }

        loaded.PhoneNumber = "207-555-0102";
        await people.EditPersonAsync(loaded);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.Notes.AsNoTracking().SingleAsync(item => item.Id == loadedNote.Id);
        Assert.Equal("Corrected elsewhere.", stored.Narrative);
    }

    [Fact]
    public async Task AddingAWaiverStillSavesItsNewForms()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareAsync(fixture, waiver: WaiverType.None, withEffectiveDate: false);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);
        var loaded = (await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(item => item.Id == fixture.PersonOneId);
        var before = loaded.Forms.Count;
        Assert.Null(loaded.EffectiveDate);

        loaded.EffectiveDate = DateTime.Today.AddDays(-10);
        loaded.Waiver = WaiverType.Section21;
        var added = loaded.AddMissingForms(
            Person.GenerateFormList(loaded.EffectiveDate.Value, new Settings()));
        Assert.True(added > 0);
        await people.EditPersonAsync(loaded);

        Assert.All(loaded.Forms, form => Assert.True(form.Id > 0));
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(before + added,
            await db.Forms.CountAsync(form => form.PersonId == loaded.Id));
    }

    private static async Task<int> PrepareAsync(NoteEntryFixture fixture, WaiverType waiver = WaiverType.Section21, bool withEffectiveDate = true)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
        person.EffectiveDate = withEffectiveDate ? DateTime.Today.AddDays(-100) : null;
        person.Bio = "Synthetic biography.";
        person.Waiver = waiver;
        var provider = new Provider
        {
            AgencyId = fixture.CaseManagerOne.AgencyId,
            Type = ProviderType.Waiver,
            Name = "Shared Living Agency"
        };
        db.Providers.Add(provider);
        var note = Note.Create("Visit.", DateTime.Today.AddDays(-3), NoteStatus.Logged, 30,
            person.Id, noteType: NoteType.Visit);
        note.AgencyId = fixture.CaseManagerOne.AgencyId;
        note.GoalProgress = GoalProgressLevel.None;
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return provider.Id;
    }

    private static SessionService Session(NoteEntryFixture fixture)
    {
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        return session;
    }
}
