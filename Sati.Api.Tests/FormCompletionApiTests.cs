using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormCompletionApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ReclassificationAtomicallyRecordsItsImpliedAssessmentWithSeparateDates()
    {
        const int personId = 102;
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var assessmentId = await factory.CreateOutstandingFormAsync(
            personId, "ComprehensiveAssessment");
        var reclassificationId = await factory.CreateOutstandingFormAsync(
            personId, "Reclassification");
        var reclassificationOn = DateTime.Today.AddDays(-2);
        var assessmentOn = reclassificationOn.AddDays(-3);

        try
        {
            var response = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Reclassification/attestation",
                new AttestFormRequest(
                    reclassificationId,
                    reclassificationOn,
                    ComprehensiveAssessmentCompletedOn: assessmentOn));

            response.EnsureSuccessStatusCode();
            var people = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
            var forms = people!.Single(person => person.Id == personId).Forms;
            Assert.Equal(
                reclassificationOn,
                forms.Single(form => form.Id == reclassificationId).CompletedDate);
            Assert.Equal(
                assessmentOn,
                forms.Single(form => form.Id == assessmentId).CompletedDate);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var assessmentDraft = await db.Notes.AsNoTracking()
                .SingleAsync(note => note.FormId == assessmentId);
            var reclassificationDraft = await db.Notes.AsNoTracking()
                .SingleAsync(note => note.FormId == reclassificationId);
            Assert.Equal(assessmentOn.Date, assessmentDraft.EventDate);
            Assert.Equal(reclassificationOn.Date, reclassificationDraft.EventDate);
            Assert.Equal(NoteWorkflow.Pending, assessmentDraft.Status);
            Assert.Equal(NoteWorkflow.Pending, reclassificationDraft.Status);
        }
        finally
        {
            foreach (var (type, id) in new[]
                     {
                         ("Reclassification", reclassificationId),
                         ("ComprehensiveAssessment", assessmentId)
                     })
            {
                var revoke = await owner.PostAsJsonAsync(
                    $"/api/v1/people/{personId}/forms/{type}/attestation/revoke",
                    new RevokeFormAttestationRequest(id, "Atomic attestation test cleanup."));
                if (revoke.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict))
                    revoke.EnsureSuccessStatusCode();
            }
        }
    }

    [Fact]
    public async Task AttestationStoresTheEnteredDateInsteadOfSynthesizingTodayOrDueDate()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var fixture = await CreateIsolatedAttestationFixtureAsync();
        var completedOn = DateTime.Today.AddDays(-5);
        var auditBefore = await factory.GetAuditEventsAsync("form.attested");

        try
        {
            var response = await owner.PostAsJsonAsync(
                $"/api/v1/people/{fixture.PersonId}/forms/{fixture.Type}/attestation",
                new { FormId = fixture.FormId, CompletedOn = completedOn, EvidenceNoteId = (int?)null });

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var saved = await response.Content.ReadFromJsonAsync<FormDto>();
            Assert.NotNull(saved);
            Assert.Equal(completedOn.Date, saved.CompletedDate);
            Assert.NotEqual(DateTime.Today, saved.CompletedDate);
            Assert.NotEqual(fixture.DueDate.Date, saved.CompletedDate);

            var auditAfter = await factory.GetAuditEventsAsync("form.attested");
            var audit = Assert.Single(auditAfter.Skip(auditBefore.Count));
            Assert.Equal(fixture.FormId.ToString(), audit.ResourceId);
            Assert.Contains(completedOn.ToString("yyyy-MM-dd"), audit.MetadataJson);

            var historyPath =
                $"/api/v1/people/{fixture.PersonId}/forms/{fixture.Type}/attestations?formId={fixture.FormId}";
            var attestedHistory = await owner.GetFromJsonAsync<List<FormAttestationHistoryDto>>(historyPath)
                ?? throw new InvalidOperationException("The attestation history response was empty.");
            var attested = Assert.IsType<FormAttestationHistoryDto>(attestedHistory.First());
            Assert.Equal("Attested", attested.Kind);
            Assert.Equal(completedOn.Date, attested.CompletedOn);
            Assert.Equal("case-manager-one", attested.ActorDisplayName);
            Assert.Equal("CaseManager", attested.ActorKind);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var draft = await db.Notes.AsNoTracking().SingleAsync(note =>
                    note.FormId == fixture.FormId && note.EventDate == completedOn.Date);
                Assert.Equal(NoteWorkflow.Pending, draft.Status);
            }
            Assert.True(attested.RecordedAtUtc > DateTime.UtcNow.AddMinutes(-1));

            var revoke = await owner.PostAsJsonAsync(
                $"/api/v1/people/{fixture.PersonId}/forms/{fixture.Type}/attestation/revoke",
                new { FormId = fixture.FormId, Reason = "API regression-test cleanup." });
            revoke.EnsureSuccessStatusCode();

            var revokedHistory = await owner.GetFromJsonAsync<List<FormAttestationHistoryDto>>(historyPath)
                ?? throw new InvalidOperationException("The revoked history response was empty.");
            Assert.Equal("Revoked", revokedHistory.First().Kind);
            Assert.Equal("API regression-test cleanup.", revokedHistory.First().Reason);
        }
        finally
        {
            await DeleteIsolatedAttestationFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task AttestationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("director-one");
        var foreignPeople = await supervisor.GetFromJsonAsync<List<PersonDto>>(
            "/api/v1/caseload?userId=19");
        var foreignForm = foreignPeople!
            .Single(candidate => candidate.Id == 103)
            .Forms.First();

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/people/103/forms/{foreignForm.Type}/attestation",
            new { FormId = foreignForm.Id, CompletedOn = DateTime.Today, EvidenceNoteId = (int?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PendingAttestationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await owner.GetAsync("/api/v1/people/103/attestations/pending");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AttestationHistoryRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("director-one");
        var foreignPeople = await supervisor.GetFromJsonAsync<List<PersonDto>>(
            "/api/v1/caseload?userId=19");
        var foreignForm = foreignPeople!.Single(candidate => candidate.Id == 103).Forms.First();

        var response = await owner.GetAsync(
            $"/api/v1/people/103/forms/{foreignForm.Type}/attestations?formId={foreignForm.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PendingRouteReturnsMatchingEvidenceWithoutCompletingTheForm()
    {
        var evidenceNoteId = await factory.CreatePendingAttestationEvidenceAsync();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var pending = await owner.GetFromJsonAsync<List<PendingAttestationDto>>(
            "/api/v1/people/102/attestations/pending");

        var suggestion = Assert.Single(pending!, candidate => candidate.EvidenceNoteId == evidenceNoteId);
        Assert.Equal("PCP", suggestion.FormType);
        var people = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.Null(people!.Single(candidate => candidate.Id == 102).Forms
            .Single(candidate => candidate.Id == suggestion.FormId).CompletedDate);
    }

    [Fact]
    public async Task RevocationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("director-one");
        var foreignPeople = await supervisor.GetFromJsonAsync<List<PersonDto>>(
            "/api/v1/caseload?userId=19");
        var foreignForm = foreignPeople!
            .Single(candidate => candidate.Id == 103)
            .Forms.First();

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/people/103/forms/{foreignForm.Type}/attestation/revoke",
            new RevokeFormAttestationRequest(foreignForm.Id, "Unauthorized attempt."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SimultaneousAttestationsReturnATypedConflictForTheLoser()
    {
        using var firstClient = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var secondClient = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var fixture = await CreateIsolatedAttestationFixtureAsync();
        var path =
            $"/api/v1/people/{fixture.PersonId}/forms/{fixture.Type}/attestation";
        var payload = new AttestFormRequest(
            fixture.FormId, DateTime.Today.AddDays(-3));

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(path, payload),
            secondClient.PostAsJsonAsync(path, payload));

        try
        {
            var responseDetails = await Task.WhenAll(responses.Select(async response =>
                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"));
            Assert.True(responses.Count(response => response.StatusCode == HttpStatusCode.OK) == 1,
                string.Join(" | ", responseDetails));
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var error = await conflict.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.Equal("form_attestation_changed", error?.Code);
        }
        finally
        {
            var revoke = await firstClient.PostAsJsonAsync(
                $"{path}/revoke",
                new RevokeFormAttestationRequest(
                    fixture.FormId, "Concurrency regression-test cleanup."));
            revoke.EnsureSuccessStatusCode();
            await DeleteIsolatedAttestationFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task UpdateRejectsAFutureCompletionDateWithoutChangingStoredState()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = before!
            .SelectMany(person => person.Forms)
            .First();

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(DateTime.Today.AddDays(1), form.OpenedDate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var stored = after!
            .SelectMany(person => person.Forms)
            .Single(candidate => candidate.Id == form.Id);
        Assert.Equal(form.CompletedDate, stored.CompletedDate);
        Assert.Equal(form.IsCompliant, stored.IsCompliant);
    }

    [Fact]
    public async Task UpdateRejectsANonFutureCompletionChange()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = before!.SelectMany(person => person.Forms).First();
        var attempted = form.CompletedDate?.AddDays(-1) ?? DateTime.Today.AddDays(-10);

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(attempted, form.OpenedDate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.Equal(
            form.CompletedDate,
            after!.SelectMany(person => person.Forms)
                .Single(candidate => candidate.Id == form.Id).CompletedDate);
    }

    private async Task<AttestationFixture> CreateIsolatedAttestationFixtureAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var personId = await db.People.MaxAsync(person => person.Id) + 1;
        var targetEffectiveDate = DateTime.Today;
        var form = new ServerForm
        {
            Type = nameof(FormType.PCP),
            DueDate = targetEffectiveDate,
            TargetEffectiveDate = targetEffectiveDate
        };
        db.People.Add(new ServerPerson
        {
            Id = personId,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Attestation",
            LastName = "Fixture",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = targetEffectiveDate.AddYears(-1),
            IsTestData = true,
            CreatedAtUtc = DateTime.UtcNow,
            Forms = [form]
        });
        await db.SaveChangesAsync();
        return new AttestationFixture(
            personId, form.Id, form.Type, form.DueDate);
    }

    private async Task DeleteIsolatedAttestationFixtureAsync(
        AttestationFixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.FormAttestationChangeReviewFlags
            .Where(flag => flag.FormId == fixture.FormId)
            .ExecuteDeleteAsync();
        await db.FormAttestations
            .Where(attestation => attestation.FormId == fixture.FormId)
            .ExecuteDeleteAsync();
        await db.Notes
            .Where(note => note.PersonId == fixture.PersonId)
            .ExecuteDeleteAsync();
        await db.Forms
            .Where(form => form.PersonId == fixture.PersonId)
            .ExecuteDeleteAsync();
        await db.People
            .Where(person => person.Id == fixture.PersonId)
            .ExecuteDeleteAsync();
    }

    private sealed record AttestationFixture(
        int PersonId,
        int FormId,
        string Type,
        DateTime DueDate);
}
