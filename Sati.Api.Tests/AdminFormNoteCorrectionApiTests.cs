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
public sealed class AdminFormNoteCorrectionApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task RevokedFormCanBeCorrectedFromItsLastAttestedDateWithoutAnotherRevocation()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var foreignAdmin = await factory.CreateAuthenticatedClientAsync("admin-two");
        var (personId, formId, noteId, priorDate) = await CreateSubmittedReviewAsync(2);
        var correctedDate = priorDate.AddDays(1);
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var form = await db.Forms.SingleAsync(row => row.Id == formId);
                form.ApplyRevocation();
                db.FormAttestations.Add(new ServerFormAttestation
                {
                    FormId = formId,
                    Kind = "Revoked",
                    ActorKind = "CaseManager",
                    ActorUserId = 12,
                    RecordedAtUtc = DateTime.UtcNow,
                    Reason = "The date was recorded incorrectly."
                });
                await db.SaveChangesAsync();
            }

            var path = $"/api/v1/admin/notes/{noteId}/form-date-correction-target";
            using var deniedCaseManager = await caseManager.GetAsync(path);
            using var deniedForeign = await foreignAdmin.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, deniedCaseManager.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, deniedForeign.StatusCode);
            var target = await admin.GetFromJsonAsync<AdminFormNoteCorrectionTargetDto>(path);
            Assert.NotNull(target);
            Assert.Equal(priorDate, target.ActivityDate);
            Assert.Null(target.CurrentCompletedOn);
            Assert.Equal(1, target.Revision);

            using var response = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date",
                new AdminCorrectFormNoteDateRequest(target.Revision, correctedDate,
                    "The review work actually occurred the following day.", true));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var ledger = await verify.FormAttestations.AsNoTracking()
                .Where(row => row.FormId == formId).OrderBy(row => row.Id).ToListAsync();
            Assert.Equal(["Attested", "Revoked", "Attested"],
                ledger.Select(row => row.Kind).ToArray());
            Assert.Equal(correctedDate, (await verify.Forms.AsNoTracking()
                .SingleAsync(row => row.Id == formId)).CompletedDate);
            var flag = await verify.FormAttestationChangeReviewFlags.AsNoTracking()
                .SingleAsync(row => row.NoteId == noteId);
            Assert.Equal(priorDate, flag.PreviousCompletedOn);
            Assert.Equal(correctedDate, flag.RevisedCompletedOn);
        }
        finally
        {
            await RemoveReviewAsync(personId, noteId);
        }
    }

    [Fact]
    public async Task AdminCorrectionUpdatesBothSourceDatesAndLoggedNoteCanThenBeApproved()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId, noteId, priorDate) = await CreateSubmittedReviewAsync(2);
        var correctedDate = priorDate.AddDays(1);
        var request = new AdminCorrectFormNoteDateRequest(1, correctedDate,
            "The review work was actually completed the following day.", true);

        try
        {
            using var denied = await caseManager.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date", request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var unconfirmed = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date",
                request with { AttestationConfirmed = false });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
            using var corrected = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date", request);
            Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
            var note = (await corrected.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal(correctedDate, note.EventDate);
            Assert.Equal(2, note.Revision);
            Assert.Equal("Logged", note.Status);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.Equal(correctedDate, (await db.Forms.AsNoTracking()
                    .SingleAsync(row => row.Id == formId)).CompletedDate);
                var entries = await db.FormAttestations.AsNoTracking()
                    .Where(row => row.FormId == formId).OrderBy(row => row.Id).ToListAsync();
                Assert.Equal(["Attested", "Revoked", "Attested"],
                    entries.Select(row => row.Kind).ToArray());
                Assert.Equal(noteId, entries[^1].EvidenceNoteId);
                var flag = await db.FormAttestationChangeReviewFlags.AsNoTracking()
                    .SingleAsync(row => row.NoteId == noteId);
                Assert.True(flag.RequiresSupervisorAttention);
                Assert.False(flag.RequiresBillingAttention);
                Assert.False(flag.MustHoldBilling);
                Assert.True(await db.AuditEvents.AsNoTracking().AnyAsync(row =>
                    row.Action == "note.form-date-corrected" &&
                    row.ResourceId == noteId.ToString()));
            }

            using var approved = await admin.PostAsJsonAsync(
                $"/api/v1/supervisor/notes/{noteId}/approve",
                new SupervisorNoteActionRequest(null, ExpectedRevision: 2));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal("Approved", (await approved.Content.ReadFromJsonAsync<NoteDto>())!.Status);
        }
        finally
        {
            await RemoveReviewAsync(personId, noteId);
        }
    }

    [Fact]
    public async Task ApprovedCorrectionRaisesBillingFlagAndExistingClaimBlocksAnotherEdit()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var (personId, formId, noteId, priorDate) = await CreateSubmittedReviewAsync(6);
        var correctedDate = priorDate.AddDays(1);
        int? periodId = null;
        try
        {
            using var corrected = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date",
                new AdminCorrectFormNoteDateRequest(1, correctedDate,
                    "Correcting the approved note to the actual review day.", true));
            Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
            Assert.Equal("Approved", (await corrected.Content.ReadFromJsonAsync<NoteDto>())!.Status);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var flag = await db.FormAttestationChangeReviewFlags.AsNoTracking()
                    .SingleAsync(row => row.NoteId == noteId);
                Assert.True(flag.RequiresSupervisorAttention);
                Assert.True(flag.RequiresBillingAttention);
                Assert.Equal(correctedDate, (await db.Forms.AsNoTracking()
                    .SingleAsync(row => row.Id == formId)).CompletedDate);

                var period = await db.BillingPeriods.SingleOrDefaultAsync(row =>
                    row.UserId == 12 && row.Month == correctedDate.Month &&
                    row.Year == correctedDate.Year);
                if (period is null)
                {
                    period = new ServerBillingPeriod
                    {
                        UserId = 12,
                        Month = correctedDate.Month,
                        Year = correctedDate.Year,
                        Status = 1,
                        SubmittedAt = DateTime.UtcNow
                    };
                    db.BillingPeriods.Add(period);
                    await db.SaveChangesAsync();
                    periodId = period.Id;
                }
                db.ClaimLines.Add(new ServerClaimLine
                {
                    NoteId = noteId,
                    BillingPeriodId = period.Id,
                    DateOfService = correctedDate,
                    ProcedureCode = "T1016",
                    Units = 1,
                    ChargeAmount = 1,
                    ClientMaineCareId = "synthetic",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11
                });
                await db.SaveChangesAsync();
            }

            using var blocked = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-form-date",
                new AdminCorrectFormNoteDateRequest(2, correctedDate.AddDays(1),
                    "A second date edit after claim submission must be blocked.", true));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            await using var verificationScope = factory.Services.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(correctedDate, (await verification.Notes.AsNoTracking()
                .SingleAsync(row => row.Id == noteId)).EventDate);
        }
        finally
        {
            await RemoveReviewAsync(personId, noteId, periodId);
        }
    }

    private async Task<(int PersonId, int FormId, int NoteId, DateTime PriorDate)>
        CreateSubmittedReviewAsync(int status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var due = DateTime.Today;
        var priorDate = due.AddDays(-2);
        var person = new ServerPerson
        {
            Id = await db.People.MaxAsync(row => row.Id) + 1,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Corrected",
            LastName = "Review",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = due.AddMonths(-6)
        };
        var form = new ServerForm
        {
            PersonId = person.Id,
            Type = "Q3R",
            DueDate = due,
            TargetEffectiveDate = due.AddMonths(-6),
            CompletedDate = priorDate
        };
        db.People.Add(person);
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        var note = new ServerNote
        {
            PersonId = person.Id,
            AgencyId = 1,
            Narrative = "Completed the 90-day review.",
            EventDate = priorDate,
            Status = status,
            Minutes = 15,
            NoteType = (int)NoteType.Form,
            FormType = (int)FormType.Q3R,
            FormId = form.Id
        };
        db.Notes.Add(note);
        db.FormAttestations.Add(new ServerFormAttestation
        {
            FormId = form.Id,
            Kind = "Attested",
            CompletedOn = priorDate,
            ActorKind = "CaseManager",
            ActorUserId = 12,
            RecordedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (person.Id, form.Id, note.Id, priorDate);
    }

    private async Task RemoveReviewAsync(int personId, int noteId, int? periodId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.ClaimLines.Where(row => row.NoteId == noteId).ExecuteDeleteAsync();
        if (periodId is int id)
            await db.BillingPeriods.Where(row => row.Id == id).ExecuteDeleteAsync();
        await db.FormAttestationChangeReviewFlags.Where(row => row.NoteId == noteId)
            .ExecuteDeleteAsync();
        var formIds = await db.Forms.Where(row => row.PersonId == personId)
            .Select(row => row.Id).ToListAsync();
        await db.FormAttestations.Where(row => formIds.Contains(row.FormId))
            .ExecuteDeleteAsync();
        await db.Notes.Where(row => row.Id == noteId).ExecuteDeleteAsync();
        await db.Forms.Where(row => row.PersonId == personId).ExecuteDeleteAsync();
        await db.People.Where(row => row.Id == personId).ExecuteDeleteAsync();
    }
}
