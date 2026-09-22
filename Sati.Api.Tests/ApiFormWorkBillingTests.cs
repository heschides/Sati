using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ApiFormWorkBillingTests
{
    [Fact]
    public async Task PhoneCallInSameNoteDoesNotMakeLateFormWorkBillable()
    {
        await using var factory = new SatiApiFactory();
        var scenario = await SeedLinkedFormNoteAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(item => item.Id == scenario.NoteId);
            note.Activities = (int)(NoteActivity.Form | NoteActivity.Phone);
            var form = await db.Forms.SingleAsync(item => item.Id == scenario.FormId);
            form.DueDate = scenario.ActivityDate.AddDays(-1);
            form.CompletedDate = scenario.ActivityDate;
            await db.SaveChangesAsync();
        }

        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var response = await billing.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(scenario.NoteId, false, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("completed after", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateFormWorkCanBeApprovedClinicallyButCannotBeBilled()
    {
        await using var factory = new SatiApiFactory();
        var noteId = await factory.CreateNoteInStatusAsync((int)NoteStatus.Logged,
            noteType: (int)NoteType.Form);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(item => item.Id == noteId);
            var form = new ServerForm
            {
                PersonId = note.PersonId,
                Type = "Q3R",
                TargetEffectiveDate = note.EventDate!.Value.Date,
                DueDate = note.EventDate.Value.Date.AddDays(-1),
                CompletedDate = note.EventDate.Value.Date
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            note.FormType = (int)FormType.Q3R;
            note.FormId = form.Id;
            await db.SaveChangesAsync();
        }
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var held = await supervisor.GetFromJsonAsync<List<NoteDto>>(
            "/api/v1/supervisor/notes?compliant=false&allSupervisees=false");
        Assert.Contains(held!, note => note.Id == noteId &&
            note.ComplianceFailureReasons!.Any(reason =>
                reason.Contains("completed after", StringComparison.Ordinal)));

        using var approve = await supervisor.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve",
            new SupervisorNoteActionRequest(null, 1));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal((int)NoteStatus.Approved,
            (await factory.GetNoteStateAsync(noteId)).Status);

        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var claim = await billing.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(noteId, false, null));
        Assert.Equal(HttpStatusCode.BadRequest, claim.StatusCode);
        Assert.Contains("completed after", await claim.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateFormWorkIsRejectedAtClaimCreationDespiteSupervisorOverride()
    {
        await using var factory = new SatiApiFactory();
        var scenario = await SeedLinkedFormNoteAsync(factory);
        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var form = await db.Forms.SingleAsync(item => item.Id == scenario.FormId);
            form.DueDate = scenario.ActivityDate.AddDays(-1);
            var note = await db.Notes.SingleAsync(item => item.Id == scenario.NoteId);
            note.ComplianceOverride = true;
            note.OverrideReason = "Supervisor reviewed the historical gap.";
            await db.SaveChangesAsync();
        }

        using var response = await billing.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(scenario.NoteId, false, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("completed after", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateCorrectionBlocksDraftPeriodSubmission()
    {
        await using var factory = new SatiApiFactory();
        var scenario = await SeedLinkedFormNoteAsync(factory);
        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        var periodId = await CreateClaimAsync(billing, scenario.NoteId);
        await MoveDueDateBeforeWorkAsync(factory, scenario);

        using var response = await billing.PostAsync(
            $"/api/v1/billing/periods/{periodId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("completed after", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateCorrectionBlocksEdiAndReplayAfterPeriodSubmission()
    {
        await using var factory = new SatiApiFactory();
        var scenario = await SeedLinkedFormNoteAsync(factory);
        using var billing = await factory.CreateAuthenticatedClientAsync("admin-one");
        var periodId = await CreateClaimAsync(billing, scenario.NoteId);
        using var submitted = await billing.PostAsync(
            $"/api/v1/billing/periods/{periodId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var key = Guid.NewGuid().ToString("N");
        using var first = await billing.PostAsJsonAsync(
            $"/api/v1/billing/periods/{periodId}/edi", new GenerateEdiRequest(true, key));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await MoveDueDateBeforeWorkAsync(factory, scenario);

        using var replay = await billing.PostAsJsonAsync(
            $"/api/v1/billing/periods/{periodId}/edi", new GenerateEdiRequest(true, key));

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Contains("completed after", await replay.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private static async Task<(int NoteId, int FormId, DateTime ActivityDate)>
        SeedLinkedFormNoteAsync(SatiApiFactory factory)
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var note = await db.Notes.SingleAsync(item => item.Id == noteId);
        var activityDate = note.EventDate!.Value.Date;
        var form = new ServerForm
        {
            PersonId = note.PersonId,
            Type = "Q3R",
            TargetEffectiveDate = activityDate,
            DueDate = activityDate,
            CompletedDate = activityDate
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        note.NoteType = (int)NoteType.Form;
        note.FormType = (int)FormType.Q3R;
        note.FormId = form.Id;
        await db.SaveChangesAsync();
        return (noteId, form.Id, activityDate);
    }

    private static async Task<int> CreateClaimAsync(HttpClient billing, int noteId)
    {
        using var response = await billing.PostAsJsonAsync("/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(noteId, false, null));
        Assert.True(response.IsSuccessStatusCode,
            $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ClaimLineDto>())!.BillingPeriodId;
    }

    private static async Task MoveDueDateBeforeWorkAsync(
        SatiApiFactory factory,
        (int NoteId, int FormId, DateTime ActivityDate) scenario)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var form = await db.Forms.SingleAsync(item => item.Id == scenario.FormId);
        form.DueDate = scenario.ActivityDate.AddDays(-1);
        await db.SaveChangesAsync();
    }
}
