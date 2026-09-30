using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class AssessmentAvailabilityRegressionTests
{
    [Fact]
    public async Task LocalZeroWindowAllowsActualOpeningAndCompletionThirtyDaysBeforeDue()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var openedOn = DateTime.Today.AddDays(-7);
        var due = openedOn.AddDays(30);
        var target = due.AddDays(90);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            db.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                CompAssessmentOpenDaysBefore = 0
            });
            form = new Form(FormType.ComprehensiveAssessment, due, targetEffectiveDate: target)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.OpenFormAsync(form, openedOn.AddDays(-1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AttestAsync(form, openedOn.AddDays(-1)));
        await service.OpenFormAsync(form, openedOn);
        await service.AttestAsync(form, openedOn);

        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == form.Id);
        Assert.Equal(openedOn, stored.OpenedDate);
        Assert.Equal(openedOn, stored.CompletedDate);
        Assert.Equal(due, stored.DueDate);
        Assert.Equal(target, stored.TargetEffectiveDate);
        Assert.Equal(openedOn, Assert.Single(stored.Attestations).CompletedOn);
    }

    [Fact]
    public void ZeroWindowStillShowsUpcomingAssessmentOnItsOpeningDeadline()
    {
        var settings = new Settings { CompAssessmentOpenDaysBefore = 0 };
        var target = new DateTime(2027, 1, 20);
        var person = Person.CreatePerson(1, "Synthetic", "Consumer", string.Empty,
            new DateTime(1990, 1, 1), null, WaiverType.Section21, settings);
        person.EffectiveDate = target.AddYears(-1);
        var form = new Form(FormType.ComprehensiveAssessment, new DateTime(2026, 10, 22),
            targetEffectiveDate: target) { Id = 73 };
        person.Forms.Add(form);
        var opening = new DateTime(2026, 9, 22);
        var events = new UpcomingEventService();

        Assert.DoesNotContain(events.GenerateEvents([person], settings, opening.AddDays(-1)),
            item => item.FormId == form.Id);
        var item = Assert.Single(events.GenerateEvents([person], settings, opening),
            candidate => candidate.FormId == form.Id);
        Assert.Equal(opening, item.OpenDate);
        Assert.Equal(form.DueDate, item.Date);
        Assert.Equal(target, item.TargetEffectiveDate);
    }
}
