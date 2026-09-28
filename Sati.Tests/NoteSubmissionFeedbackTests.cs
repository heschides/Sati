using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.ViewModels.Children;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Xunit;

namespace Sati.Tests;

public sealed class NoteSubmissionFeedbackTests
{
    [Fact]
    public async Task PendingAnnualPcpAsksBeforeOpeningAndShowsSelectedYear()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var serviceDate = DateTime.Today;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Forms.Add(new Form(FormType.PCP, serviceDate.AddDays(30),
                targetEffectiveDate: serviceDate.AddDays(30))
            {
                PersonId = fixture.PersonOneId
            });
            await db.SaveChangesAsync();
        }
        var person = (await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(row => row.Id == fixture.PersonOneId);
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.IsFormSelected = true;
        panel.SelectedFormType = FormType.PCP;
        panel.IsAnnualPlan = true;
        panel.SelectedFormObligation = Assert.Single(panel.FormObligations);
        panel.EventDate = serviceDate;
        panel.Status = NoteStatus.Pending;
        panel.Narrative = "Opened the annual PCP.";
        panel.Minutes = 15;
        AnnualPcpConfirmationKind? prompt = null;
        panel.AnnualPcpConfirmationRequested += (_, args) =>
        {
            prompt = args.Kind;
            args.Confirmed = true;
        };

        Assert.True(panel.HasAnnualPcpYear);
        Assert.Contains(serviceDate.AddDays(30).Year.ToString(), panel.AnnualPcpYearText);
        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.Equal(AnnualPcpConfirmationKind.Open, prompt);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(serviceDate, (await verification.Forms.SingleAsync()).OpenedDate);
        var saved = await verification.Notes.SingleAsync();
        Assert.True(saved.IsAnnualPlan);
        Assert.Equal(GoalProgressLevel.None, saved.GoalProgress);
    }

    [Fact]
    public async Task PreWindowAnnualPcpCanOnlySaveAsUnlinkedRevision()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var serviceDate = DateTime.Today;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Forms.Add(new Form(FormType.PCP, serviceDate.AddDays(100),
                targetEffectiveDate: serviceDate.AddDays(100))
            {
                PersonId = fixture.PersonOneId
            });
            await db.SaveChangesAsync();
        }
        var person = (await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetAllPeopleAsync(fixture.CaseManagerOne.Id))
            .Single(row => row.Id == fixture.PersonOneId);
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.IsFormSelected = true;
        panel.SelectedFormType = FormType.PCP;
        panel.IsAnnualPlan = true;
        panel.SelectedFormObligation = Assert.Single(panel.FormObligations);
        panel.EventDate = serviceDate;
        panel.Status = NoteStatus.Pending;
        panel.Narrative = "Discussed a PCP revision.";
        panel.Minutes = 15;
        panel.AnnualPcpConfirmationRequested += (_, args) =>
        {
            Assert.Equal(AnnualPcpConfirmationKind.CreateRevision, args.Kind);
            args.Confirmed = true;
        };

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        await using var verification = fixture.Factory.CreateDbContext();
        var saved = await verification.Notes.SingleAsync();
        Assert.False(saved.IsAnnualPlan);
        Assert.Null(saved.FormId);
        Assert.Null((await verification.Forms.SingleAsync()).OpenedDate);
    }

    [Fact]
    public async Task PendingPcpNoteExplainsWhereToSelectItsExactFormBeforeLogging()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var target = DateTime.Today;
        person.Forms.Add(new Form(FormType.PCP, target, targetEffectiveDate: target)
        {
            PersonId = person.Id
        });
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.IsFormSelected = true;
        panel.SelectedFormType = FormType.PCP;
        panel.IsAnnualPlan = true;
        panel.EventDate = target;
        panel.Status = NoteStatus.Logged;
        panel.GoalProgress = GoalProgressLevel.None;
        panel.Narrative = "The PCP work was completed.";
        panel.Minutes = 15;

        Assert.Single(panel.FormObligations);
        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.Contains("Annual PCP plan year", panel.SubmissionFailureMessage);
        Assert.Equal("The PCP work was completed.", panel.Narrative);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.Notes.ToListAsync());
    }

    [Fact]
    public async Task LateComplianceFailurePreservesTheUnsubmittedDraft()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.SelectedNoteType = NoteType.Contact;
        panel.EventDate = DateTime.Today;
        panel.Status = NoteStatus.Logged;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = "The clinical draft must remain on screen.";
        panel.Minutes = 30;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Forms.Add(new Form(FormType.PCP, DateTime.Today.AddDays(-1),
                targetEffectiveDate: DateTime.Today.AddDays(-1)) { PersonId = person.Id });
            await db.SaveChangesAsync();
        }

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.Equal("The clinical draft must remain on screen.", panel.Narrative);
        Assert.True(panel.HasUnsavedChanges);
        Assert.Contains("PCP", panel.SubmissionFailureMessage);
        Assert.Contains("Pending", panel.NoteAttentionMessage);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.Notes.AnyAsync());
    }

    // A justification carries the service documentation into clinical review. It is
    // not an approved exception: the note is saved with its justification recorded,
    // and billing stays blocked until a supervisor exception or Admin recovery.
    [Fact]
    public async Task CaseManagerJustificationSendsTheNoteToReviewWithoutReleasingBilling()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.Forms.Add(new Form(FormType.PCP, DateTime.Today.AddDays(-1),
            targetEffectiveDate: DateTime.Today.AddDays(-1)) { PersonId = person.Id });
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.SelectedNoteType = NoteType.Contact;
        panel.EventDate = DateTime.Today;
        panel.Status = NoteStatus.Logged;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = "Consumer was seen; the PCP meeting is scheduled.";
        panel.Minutes = 30;
        await panel.SubmitNoteCommand.ExecuteAsync(null);
        Assert.True(panel.IsComplianceDialogVisible);
        panel.PendingJustification = "Consumer was seen; the PCP meeting is scheduled.";

        await panel.SendToSupervisorCommand.ExecuteAsync(null);

        Assert.False(panel.IsComplianceDialogVisible);
        await using var verification = fixture.Factory.CreateDbContext();
        var saved = await verification.Notes.SingleAsync();
        Assert.Equal(NoteStatus.Logged, saved.Status);
        Assert.Equal("Consumer was seen; the PCP meeting is scheduled.", saved.CaseManagerJustification);
    }

    [Fact]
    public async Task BlankJustificationCannotSendABlockedNoteToReview()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.Forms.Add(new Form(FormType.PCP, DateTime.Today.AddDays(-1),
            targetEffectiveDate: DateTime.Today.AddDays(-1)) { PersonId = person.Id });
        var panel = fixture.NoteEntry();
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.SelectedNoteType = NoteType.Contact;
        panel.EventDate = DateTime.Today;
        panel.Status = NoteStatus.Logged;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = "The clinical draft must remain on screen.";
        panel.Minutes = 30;
        await panel.SubmitNoteCommand.ExecuteAsync(null);
        Assert.True(panel.IsComplianceDialogVisible);
        panel.PendingJustification = "   ";

        await panel.SendToSupervisorCommand.ExecuteAsync(null);

        Assert.Equal("The clinical draft must remain on screen.", panel.Narrative);
        Assert.True(panel.IsComplianceDialogVisible);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.False(await verification.Notes.AnyAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudSubmissionRefusalRetainsTypedFeedbackAndRevision(bool update)
    {
        const string message = "PCP documentation is overdue. Save the draft as Pending.";
        using var http = new HttpClient(new RefusalHandler(message)) { BaseAddress = new Uri("https://synthetic.invalid") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("synthetic-token");
        var service = new CloudNoteService(api);
        var note = Note.Rehydrate(77);
        note.Narrative = "Keep this draft.";
        note.EventDate = DateTime.Today;
        note.Status = NoteStatus.Logged;
        note.Minutes = 30;
        note.PersonId = 1;
        note.Revision = 4;

        var error = await Assert.ThrowsAsync<NoteSubmissionException>(async () =>
        {
            if (update) await service.UpdateNoteAsync(note);
            else await service.AddNoteAsync(note);
        });

        Assert.Equal(message, error.Message);
        Assert.Equal("Keep this draft.", note.Narrative);
        Assert.Equal(4, note.Revision);
    }

    [Theory]
    [InlineData(false, "service_time_busy")]
    [InlineData(true, "service_time_busy")]
    [InlineData(false, "service_time_window")]
    [InlineData(true, "service_time_window")]
    [InlineData(false, "service_time_overlap")]
    [InlineData(true, "service_time_overlap")]
    public async Task CloudTimeRefusalRetainsExpectedFeedbackAndRevision(bool update, string code)
    {
        const string message = "This service time cannot be saved. Review the schedule and try again.";
        using var http = new HttpClient(new RefusalHandler(message, code)) { BaseAddress = new Uri("https://synthetic.invalid") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("synthetic-token");
        var service = new CloudNoteService(api);
        var note = Note.Rehydrate(77);
        note.Narrative = "Keep this draft.";
        note.EventDate = DateTime.Today;
        note.Status = NoteStatus.Pending;
        note.Minutes = 30;
        note.PersonId = 1;
        note.Revision = 4;

        var error = await Assert.ThrowsAsync<ServiceTimeWriteConflictException>(async () =>
        {
            if (update) await service.UpdateNoteAsync(note);
            else await service.AddNoteAsync(note);
        });

        Assert.Equal(message, error.Message);
        Assert.Equal("Keep this draft.", note.Narrative);
        Assert.Equal(4, note.Revision);
    }

    private sealed class RefusalHandler(string message, string code = "note_compliance_blocked") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiErrorDto(code, message, string.Empty))
            });
    }
}
