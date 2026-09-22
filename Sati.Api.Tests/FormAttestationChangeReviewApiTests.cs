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
public sealed class FormAttestationChangeReviewApiTests(SatiApiFactory factory)
{
    private const string Route = "/api/v1/form-attestation-change-review-flags";

    [Fact]
    public async Task ReviewQueuesEnforceAudienceCaseloadAndAgency()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var director = await factory.CreateAuthenticatedClientAsync("director-one");
        using var billing = await factory.CreateAuthenticatedClientAsync("billing-only-one");
        using var foreignSupervisor = await factory.CreateAuthenticatedClientAsync("supervisor-two");
        using var foreignAdmin = await factory.CreateAuthenticatedClientAsync("admin-two");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var anonymous = factory.CreateAnonymousClient();

        var created = new List<Guid>();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var form101 = await db.Forms.Where(row => row.PersonId == 101)
                    .Select(row => row.Id).FirstAsync();
                var form103 = await db.Forms.Where(row => row.PersonId == 103)
                    .Select(row => row.Id).FirstAsync();
                var form201 = await db.Forms.Where(row => row.PersonId == 201)
                    .Select(row => row.Id).FirstAsync();
                var due = new DateTime(2026, 7, 15);
                AddFlag(db, created, 1, 101, 501, form101, 2, false, due);
                AddFlag(db, created, 1, 103, 507, form103, 2, false, due);
                AddFlag(db, created, 1, 101, 502, form101, 6, true, due);
                AddFlag(db, created, 2, 201, 601, form201, 6, true, due);
                await db.SaveChangesAsync();
            }

            var supervisorRows = await supervisor.GetFromJsonAsync<List<FormAttestationChangeReviewFlagDto>>(
                Route + "?audience=supervisor");
            var directorRows = await director.GetFromJsonAsync<List<FormAttestationChangeReviewFlagDto>>(
                Route + "?audience=supervisor");
            var billingRows = await billing.GetFromJsonAsync<List<FormAttestationChangeReviewFlagDto>>(
                Route + "?audience=billing");
            var foreignSupervisorRows = await foreignSupervisor.GetFromJsonAsync<List<FormAttestationChangeReviewFlagDto>>(
                Route + "?audience=supervisor");
            var foreignBillingRows = await foreignAdmin.GetFromJsonAsync<List<FormAttestationChangeReviewFlagDto>>(
                Route + "?audience=billing");
            using var deniedSupervisor = await caseManager.GetAsync(Route + "?audience=supervisor");
            using var deniedBilling = await supervisor.GetAsync(Route + "?audience=billing");
            using var deniedAnonymous = await anonymous.GetAsync(Route + "?audience=supervisor");
            using var badAudience = await billing.GetAsync(Route + "?audience=unknown");

            Assert.Equal(HttpStatusCode.Forbidden, deniedSupervisor.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deniedBilling.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, deniedAnonymous.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, badAudience.StatusCode);
            Assert.Contains(supervisorRows!, row => row.NoteId == 501);
            Assert.Contains(supervisorRows!, row => row.NoteId == 502);
            Assert.DoesNotContain(supervisorRows!, row => row.NoteId is 507 or 601);
            Assert.Contains(directorRows!, row => row.NoteId == 507);
            Assert.DoesNotContain(directorRows!, row => row.NoteId == 601);
            Assert.Contains(billingRows!, row => row.NoteId == 502);
            Assert.DoesNotContain(billingRows!, row => row.NoteId is 501 or 507 or 601);
            Assert.Contains(foreignSupervisorRows!, row => row.NoteId == 601);
            Assert.DoesNotContain(foreignSupervisorRows!, row => row.NoteId is 501 or 502 or 507);
            Assert.Contains(foreignBillingRows!, row => row.NoteId == 601);
            Assert.DoesNotContain(foreignBillingRows!, row => row.NoteId is 501 or 502 or 507);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.FormAttestationChangeReviewFlags
                .Where(row => created.Contains(row.FlagId))
                .ExecuteDeleteAsync();
        }
    }

    private static void AddFlag(ApiDbContext db, List<Guid> ids, int agencyId,
        int personId, int noteId, int formId, int noteStatus, bool reachedBilling,
        DateTime due)
    {
        var impact = FormAttestationImpactRules.Evaluate(noteStatus, reachedBilling,
            due, due, null, due);
        var flag = FormAttestationChangeReviewFlag.Create(agencyId, personId,
            noteId, formId, null, due, due, due, null,
            "Synthetic queue scope check.", impact, DateTime.UtcNow);
        db.FormAttestationChangeReviewFlags.Add(flag);
        ids.Add(flag.FlagId);
    }
}
