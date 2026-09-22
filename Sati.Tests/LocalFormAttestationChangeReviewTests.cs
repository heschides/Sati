using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class LocalFormAttestationChangeReviewTests
{
    [Fact]
    public async Task RevokingAndCorrectingSubmittedReviewLeavesTwoAuditableSupervisorFlags()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(-2);
        var (detached, noteId) = await SeedLinkedFormNoteAsync(fixture, due, NoteStatus.Logged);
        var service = FormServiceFor(fixture);

        await service.AttestAsync(detached, due);
        await service.RevokeAttestationAsync(detached, "Actual review date was the following day.");

        await using (var afterRevocation = fixture.Factory.CreateDbContext())
        {
            var first = Assert.Single(await afterRevocation.FormAttestationChangeReviewFlags
                .AsNoTracking().ToListAsync());
            Assert.Equal(noteId, first.NoteId);
            Assert.Equal(due, first.PreviousCompletedOn);
            Assert.Null(first.RevisedCompletedOn);
            Assert.True(first.RequiresSupervisorAttention);
            Assert.False(first.RequiresBillingAttention);
            Assert.True(first.MustHoldBilling);
            Assert.Equal(FormAttestationBillingHoldReason.MissingAttestation,
                first.BillingHoldReasons);
        }

        await service.AttestAsync(detached, due.AddDays(1));

        await using var verification = fixture.Factory.CreateDbContext();
        var flags = await verification.FormAttestationChangeReviewFlags.AsNoTracking()
            .OrderBy(flag => flag.Id).ToListAsync();
        Assert.Equal(2, flags.Count);
        var correction = flags[1];
        Assert.Null(correction.PreviousCompletedOn);
        Assert.Equal(due.AddDays(1), correction.RevisedCompletedOn);
        Assert.Equal("Actual review date was the following day.", correction.Reason);
        Assert.Equal(FormAttestationBillingHoldReason.ActivityDateMismatch |
                     FormAttestationBillingHoldReason.CompletedAfterDueDate,
            correction.BillingHoldReasons);
        Assert.Equal(due.AddDays(1),
            (await verification.Forms.AsNoTracking().SingleAsync(form => form.Id == detached.Id)).CompletedDate);
    }

    [Fact]
    public async Task OnTimeReplacementAfterRevocationRecordsCurrentNoHoldState()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(-2);
        var (detached, noteId) = await SeedLinkedFormNoteAsync(
            fixture, due, NoteStatus.Logged, noteDate: due);
        var service = FormServiceFor(fixture);

        await service.AttestAsync(detached, due.AddDays(-1));
        await service.RevokeAttestationAsync(detached,
            "Correcting the activity date to the documented review date.");
        await service.AttestAsync(detached, due);

        await using var db = fixture.Factory.CreateDbContext();
        var flags = await db.FormAttestationChangeReviewFlags.AsNoTracking()
            .Where(flag => flag.NoteId == noteId)
            .OrderBy(flag => flag.Id).ToListAsync();
        Assert.Equal(2, flags.Count);
        Assert.True(flags[0].MustHoldBilling);
        Assert.False(flags[1].MustHoldBilling);
        Assert.Null(flags[1].PreviousCompletedOn);
        Assert.Equal(due, flags[1].RevisedCompletedOn);
        Assert.Equal("Correcting the activity date to the documented review date.",
            flags[1].Reason);
    }

    [Fact]
    public async Task ApprovedClaimNoteFlagsSupervisorAndBillingWithoutRewritingClaim()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(-2);
        var (detached, noteId) = await SeedLinkedFormNoteAsync(fixture, due, NoteStatus.Approved);
        int claimLineId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var period = new BillingPeriod
            {
                UserId = fixture.CaseManagerOne.Id,
                Month = due.Month,
                Year = due.Year,
                Status = BillingStatus.Submitted,
                SubmittedAt = DateTime.UtcNow
            };
            var claim = new ClaimLine
            {
                NoteId = noteId,
                BillingPeriod = period,
                DateOfService = due,
                ProcedureCode = "T1016",
                ChargeAmount = 1,
                PlaceOfService = 99
            };
            db.ClaimLines.Add(claim);
            await db.SaveChangesAsync();
            claimLineId = claim.Id;
        }

        var service = FormServiceFor(fixture);
        await service.AttestAsync(detached, due);
        await service.RevokeAttestationAsync(detached, "Correcting the review date.");

        await using var verification = fixture.Factory.CreateDbContext();
        var flag = Assert.Single(await verification.FormAttestationChangeReviewFlags
            .AsNoTracking().ToListAsync());
        Assert.True(flag.RequiresSupervisorAttention);
        Assert.True(flag.RequiresBillingAttention);
        Assert.Equal(claimLineId, flag.ClaimLineId);
        Assert.Equal(BillingStatus.Submitted,
            (await verification.BillingPeriods.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(noteId,
            (await verification.ClaimLines.AsNoTracking().SingleAsync()).NoteId);
    }

    [Fact]
    public async Task CitedNoteMustMatchExactFormAndAttestedActivityDate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(5);
        var noteDate = DateTime.Today.AddDays(-2);
        var (detached, noteId) = await SeedLinkedFormNoteAsync(
            fixture, due, NoteStatus.Pending, noteDate);
        var service = FormServiceFor(fixture);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AttestAsync(detached, noteDate.AddDays(1), noteId));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var note = await db.Notes.SingleAsync(item => item.Id == noteId);
            note.FormId = null;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AttestAsync(detached, noteDate, noteId));
    }

    private static FormService FormServiceFor(NoteEntryFixture fixture)
    {
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        return new FormService(fixture.Factory, session);
    }

    private static async Task<(Form Form, int NoteId)> SeedLinkedFormNoteAsync(
        NoteEntryFixture fixture,
        DateTime due,
        NoteStatus status,
        DateTime? noteDate = null)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
        person.EffectiveDate = DateTime.Today.AddMonths(-6);
        var form = new Form(FormType.Q3R, due, targetEffectiveDate: due.AddDays(-270))
        {
            PersonId = fixture.PersonOneId
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();

        var note = Note.Create(
            "Completed the review.", noteDate ?? due,
            status, 15, fixture.PersonOneId, FormType.Q3R, NoteType.Form,
            formId: form.Id);
        note.AgencyId = fixture.CaseManagerOne.AgencyId;
        note.GoalProgress = GoalProgressLevel.None;
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        return (await db.Forms.AsNoTracking().SingleAsync(item => item.Id == form.Id), note.Id);
    }
}
