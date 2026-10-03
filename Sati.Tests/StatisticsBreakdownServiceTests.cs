using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class StatisticsBreakdownServiceTests
{
    private static readonly DateTime ServiceDate = new(2026, 1, 10);

    [Fact]
    public async Task LocalReportScopesNotesToOwnedPeopleAndMatchingAgencyMarkers()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new StatisticsBreakdownService(fixture.Factory, session);

        var foreignPerson = Person.CreatePerson(
            fixture.CaseManagerTwo.Id, "Foreign", "Consumer", string.Empty,
            new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings());
        foreignPerson.AgencyId = fixture.CaseManagerTwo.AgencyId;
        var ghostPerson = Person.CreatePerson(
            fixture.CaseManagerOne.Id, "Ghost", "Consumer", string.Empty,
            new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings());
        ghostPerson.AgencyId = fixture.CaseManagerOne.AgencyId;
        ghostPerson.Status = PersonStatus.Ghost;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.People.AddRange(foreignPerson, ghostPerson);
            await db.SaveChangesAsync();

            var owned = Note.Create("Owned narrative", ServiceDate, NoteStatus.Logged, 16, fixture.PersonOneId);
            owned.AgencyId = fixture.CaseManagerOne.AgencyId;
            owned.IsUnbilled = true;
            var mismatchedNote = Note.Create("Wrong note agency", ServiceDate, NoteStatus.Logged, 60, fixture.PersonOneId);
            mismatchedNote.AgencyId = fixture.CaseManagerTwo.AgencyId;
            var foreign = Note.Create("Foreign narrative", ServiceDate, NoteStatus.Logged, 60, foreignPerson.Id);
            foreign.AgencyId = fixture.CaseManagerTwo.AgencyId;
            var ghost = Note.Create("Ghost narrative", ServiceDate, NoteStatus.Logged, 60, ghostPerson.Id);
            ghost.AgencyId = fixture.CaseManagerOne.AgencyId;
            db.Notes.AddRange(owned, mismatchedNote, foreign, ghost);
            await db.SaveChangesAsync();
        }

        var report = await service.GetAsync(ServiceDate, ServiceDate, StatisticsPeriod.Month);

        Assert.Equal(2, report.Totals.DocumentedUnits);
        Assert.Equal(2, report.Totals.NonBillableUnits);
        Assert.Equal(0, report.Totals.BillableMarkedUnits);
        Assert.Equal(1, report.Totals.NonBillableServiceDays);
        Assert.Equal(0, report.Totals.BillableMarkedServiceDays);
        Assert.Equal(1, report.Totals.DocumentedNoteCount);
        Assert.DoesNotContain(report.Clients, row => row.PersonId == foreignPerson.Id);
        Assert.DoesNotContain(report.Clients, row => row.PersonId == ghostPerson.Id);
        Assert.Equal(2, Assert.Single(report.Clients, row => row.PersonId == fixture.PersonOneId)
            .Metrics.DocumentedUnits);
        var selected = await service.GetAsync(ServiceDate, ServiceDate, StatisticsPeriod.Month,
            fixture.PersonOneId);
        Assert.Equal(fixture.PersonOneId, Assert.Single(selected.Clients).PersonId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            ServiceDate, ServiceDate, StatisticsPeriod.Month, foreignPerson.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            ServiceDate, ServiceDate, StatisticsPeriod.Month, ghostPerson.Id));
    }

    [Fact]
    public async Task LocalReportRejectsPermissionRevokedAfterSignIn()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new StatisticsBreakdownService(fixture.Factory, session);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id)).Permissions =
                UserPermissions.None;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            ServiceDate, ServiceDate, StatisticsPeriod.Month));
    }

    [Fact]
    public async Task LocalClaimMovesFromInternallyLockedToPayerTransmittedOnlyWithExactExchangeEvidence()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new StatisticsBreakdownService(fixture.Factory, session);
        var agencyId = fixture.CaseManagerOne.AgencyId;
        await using var db = fixture.Factory.CreateDbContext();
        var note = Note.Create("Narrative stays in SQL", ServiceDate,
            NoteStatus.Approved, 16, fixture.PersonOneId, noteType: NoteType.Form);
        note.AgencyId = agencyId;
        note.Activities = NoteActivity.Form | NoteActivity.Visit;
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var period = new BillingPeriod
        {
            UserId = fixture.CaseManagerOne.Id,
            Year = ServiceDate.Year,
            Month = ServiceDate.Month,
            Status = BillingStatus.Submitted,
            SubmittedAt = ServiceDate.AddDays(20)
        };
        period.Lines.Add(new ClaimLine
        {
            NoteId = note.Id,
            DateOfService = ServiceDate,
            ProcedureCode = "T1016",
            Units = 1.07m,
            ChargeAmount = 1.07m
        });
        db.BillingPeriods.Add(period);
        await db.SaveChangesAsync();

        var locked = await service.GetAsync(ServiceDate, ServiceDate, StatisticsPeriod.Month);
        Assert.Equal(2, locked.Totals.DocumentedUnits);
        Assert.Equal(1.07m, locked.Totals.LockedClaimUnits);
        Assert.Equal(1, locked.Totals.LockedClaimCount);
        Assert.Equal(0m, locked.Totals.SubmittedClaimUnits);
        Assert.Equal(0, locked.Totals.SubmittedClaimCount);

        var generation = new EdiGeneration
        {
            AgencyId = agencyId,
            ActorUserId = fixture.CaseManagerOne.Id,
            BillingPeriodId = period.Id,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            FileName = "statistics-fixture.837",
            Content = "Synthetic test fixture",
            CreatedAtUtc = ServiceDate.AddDays(21)
        };
        db.EdiGenerations.Add(generation);
        await db.SaveChangesAsync();
        db.BillingSubmissionEvents.Add(new BillingSubmissionEvent
        {
            AgencyId = agencyId,
            BillingPeriodId = period.Id,
            EdiGenerationId = generation.Id,
            Stage = BillingSubmissionStage.Transmitted,
            IsSynthetic = false,
            OccurredAtUtc = ServiceDate.AddDays(22)
        });
        await db.SaveChangesAsync();

        var transmitted = await service.GetAsync(ServiceDate, ServiceDate, StatisticsPeriod.Month);
        Assert.Equal(1.07m, transmitted.Totals.LockedClaimUnits);
        Assert.Equal(1.07m, transmitted.Totals.SubmittedClaimUnits);
        Assert.Equal(1.07m, transmitted.Totals.SubmittedFormUnits);
        Assert.Equal(1.07m, transmitted.Totals.SubmittedVisitUnits);
        Assert.Equal(1, transmitted.Totals.SubmittedClaimCount);
    }
}
