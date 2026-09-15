using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormDeletionBillingBypassTests(SatiApiFactory factory)
{
    [Fact]
    public async Task DeletingAnUnattestedOverdueFormCannotRemoveItsBillingBlock()
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        // The helper's service date is 2026-08-03, so this due date places that
        // service inside the form's historical non-billable window.
        var formId = await AddFormForNoteAsync(noteId, "PCP", new DateTime(2026, 8, 1));
        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var before = await CreateClaimAsync(billing, noteId);
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        Assert.Contains("not completed as of this service date", await before.Content.ReadAsStringAsync());

        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var deletion = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([formId]));

        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        var error = await deletion.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("form_retention_required", error!.Code);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var retained = await db.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
            Assert.Null(retained.CompletedDate);
            Assert.False(await db.ClaimLines.AnyAsync(claim => claim.NoteId == noteId));
        }

        using var after = await CreateClaimAsync(billing, noteId);
        Assert.Equal(HttpStatusCode.BadRequest, after.StatusCode);
        Assert.Contains("not completed as of this service date", await after.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("PCP", 3, false)]
    [InlineData("Release_Medical", -2, false)]
    [InlineData("PCP", -30, true)]
    public async Task FutureOptionalAndLegacyCompletedFormsAreRetained(
        string type, int daysUntilDue, bool completed)
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        var dueDate = DateTime.Today.AddDays(daysUntilDue);
        var completedDate = completed ? DateTime.Today.AddDays(-10) : (DateTime?)null;
        var formId = await AddFormForNoteAsync(noteId, type, dueDate, completedDate);
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([formId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("form_retention_required", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var retained = await db.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        Assert.Equal(type, retained.Type);
        Assert.Equal(dueDate, retained.DueDate);
        Assert.Equal(completedDate, retained.CompletedDate);
        Assert.False(await db.FormAttestations.AnyAsync(item => item.FormId == formId));
        if (type == "Release_Medical")
        {
            var requirements = await db.Settings.Where(settings => settings.AgencyId == 1)
                .Select(settings => (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
                .SingleOrDefaultAsync() ?? BillingComplianceGate.DefaultRequirements;
            Assert.False(BillingComplianceGate.IsRequired(type, requirements));
        }
    }

    [Fact]
    public async Task MissingEffectiveDateDoesNotAllowDeletion()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(person => person.Id == personId);
            person.EffectiveDate = null;
            formId = await db.Forms.Where(form => form.PersonId == personId).Select(form => form.Id).FirstAsync();
            await db.SaveChangesAsync();
        }
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([formId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertRetainedAsync(formId);
    }

    [Fact]
    public async Task DeletingALegacyCompletedFormCannotEraseTheHistoricalBillingGap()
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        var formId = await AddFormForNoteAsync(noteId, "PCP",
            DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-3));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(note => note.Id == noteId);
            note.EventDate = DateTime.Today.AddDays(-5);
            note.ApprovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var before = await CreateClaimAsync(billing, noteId);
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        Assert.Contains("not completed as of this service date", await before.Content.ReadAsStringAsync());
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var deletion = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([formId]));

        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        await AssertRetainedAsync(formId);
        using var after = await CreateClaimAsync(billing, noteId);
        Assert.Equal(HttpStatusCode.BadRequest, after.StatusCode);
        Assert.Contains("not completed as of this service date", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MixedOwnedBatchRetainsEveryFormWithoutPartialDeletion()
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        var overdueId = await AddFormForNoteAsync(noteId, "PCP", DateTime.Today.AddDays(-2));
        var futureOptionalId = await AddFormForNoteAsync(noteId, "PrivacyPractices", DateTime.Today.AddDays(10));
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([futureOptionalId, overdueId, futureOptionalId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertRetainedAsync(overdueId, futureOptionalId);
    }

    [Theory]
    [InlineData(19, 1)] // Same agency, another case manager.
    [InlineData(22, 2)] // Another agency.
    public async Task MixedUnauthorizedBatchIsNotDisclosingAndRetainsOwnedForms(
        int foreignOwnerId, int foreignAgencyId)
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        var foreignPersonId = await factory.CreateBillingWorkflowPersonAsync();
        int ownedId, foreignId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var foreignPerson = await db.People.SingleAsync(person => person.Id == foreignPersonId);
            foreignPerson.UserId = foreignOwnerId;
            foreignPerson.AgencyId = foreignAgencyId;
            await db.SaveChangesAsync();
            ownedId = await db.Forms.Where(form => form.PersonId == personId).Select(form => form.Id).FirstAsync();
            foreignId = await db.Forms.Where(form => form.PersonId == foreignPersonId).Select(form => form.Id).FirstAsync();
        }
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest([ownedId, foreignId]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertRetainedAsync(ownedId, foreignId);
    }

    [Fact]
    public async Task OwningUserIdCannotOverrideThePersonsAgencyBoundary()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(person => person.Id == personId);
            // Deliberately inconsistent synthetic legacy row: ownership alone is
            // insufficient authority to access another agency's records.
            person.AgencyId = 2;
            formId = await db.Forms.Where(form => form.PersonId == personId).Select(form => form.Id).FirstAsync();
            await db.SaveChangesAsync();
        }
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        try
        {
            using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
                new DeleteFormsRequest([formId]));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertRetainedAsync(formId);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(person => person.Id == personId);
            person.AgencyId = 1;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedCaseManagementCannotInvokeEvenAnEmptyDeletion(bool empty)
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int formId;
        UserPermissions originalPermissions;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            formId = await db.Forms.Where(form => form.PersonId == personId).Select(form => form.Id).FirstAsync();
            originalPermissions = await db.Users.Where(user => user.Id == 12).Select(user => user.Permissions).SingleAsync();
        }
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await factory.ChangeUserPermissionsAsync(12, UserPermissions.Billing);
        try
        {
            using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
                new DeleteFormsRequest(empty ? [] : [formId]));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await AssertRetainedAsync(formId);
        }
        finally
        {
            await factory.ChangeUserPermissionsAsync(12, originalPermissions);
        }
    }

    [Fact]
    public async Task AuthenticatedAuthorizedEmptyRequestRemainsANoOp()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete", new DeleteFormsRequest([]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<CountDto>())!.Count);
    }

    [Fact]
    public async Task AnonymousEmptyRequestRequiresAuthentication()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.PostAsJsonAsync("/api/v1/forms/delete", new DeleteFormsRequest([]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OversizedRequestIsRejectedBeforeRecordLookup()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete",
            new DeleteFormsRequest(Enumerable.Range(1, 101).ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NullFormIdsAreRejectedAsInvalidInput()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await owner.PostAsJsonAsync("/api/v1/forms/delete", new { formIds = (int[]?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task AssertRetainedAsync(params int[] formIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(formIds.Length, await db.Forms.CountAsync(form => formIds.Contains(form.Id)));
    }

    private async Task<int> AddFormForNoteAsync(
        int noteId, string type, DateTime dueDate, DateTime? completedDate = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var note = await db.Notes.SingleAsync(item => item.Id == noteId);
        var form = new ServerForm
        {
            PersonId = note.PersonId,
            Type = type,
            TargetEffectiveDate = dueDate.Date,
            DueDate = dueDate,
            CompletedDate = completedDate
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        return form.Id;
    }

    private static Task<HttpResponseMessage> CreateClaimAsync(HttpClient client, int noteId) =>
        client.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(noteId, false, null));
}
