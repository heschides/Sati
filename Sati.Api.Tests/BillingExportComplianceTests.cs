using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

// Every case has its own synthetic database; changes never leak into the shared
// count-sensitive authorization or clearinghouse fixtures.
public sealed class BillingExportComplianceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedFileCannotBeReplayedByARevokedOrDisabledSession(bool disabled)
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        var key = Guid.NewGuid().ToString("N");
        (await ExportAsync(client, key)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var actor = await db.Users.SingleAsync(x => x.Id == 21);
        if (disabled) actor.IsEnabled = false;
        else actor.SecurityVersion++;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ExportAsync(client, key)).StatusCode);
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    [Theory]
    [InlineData("current", false)]
    [InlineData("historical", false)]
    [InlineData("settings", false)]
    [InlineData("status", false)]
    [InlineData("service-date", false)]
    [InlineData("exception", false)]
    [InlineData("overlap", false)]
    [InlineData("current", true)]
    [InlineData("historical", true)]
    [InlineData("settings", true)]
    [InlineData("status", true)]
    [InlineData("service-date", true)]
    [InlineData("exception", true)]
    [InlineData("overlap", true)]
    [InlineData("person-tenant", true)]
    [InlineData("period-status", true)]
    public async Task ExportAndExactRetryRefuseChangedSourceWithoutChangingEvidence(string change, bool replay)
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        var key = Guid.NewGuid().ToString("N");
        EdiFileDto? original = null;
        if (replay)
        {
            var generated = await ExportAsync(client, key);
            generated.EnsureSuccessStatusCode();
            original = await generated.Content.ReadFromJsonAsync<EdiFileDto>();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var line = await db.ClaimLines.SingleAsync(x => x.Id == 1402);
        var frozen = line.ClaimSnapshotJson;
        var note = await db.Notes.SingleAsync(x => x.Id == 603);
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        if (change is "current" or "historical" or "settings")
        {
            db.Forms.Add(new ServerForm
            {
                PersonId = 201,
                Type = change == "settings" ? "PrivacyPractices" : "PCP",
                DueDate = change == "historical" ? note.EventDate!.Value.AddDays(-1) : today.AddDays(-1),
                CompletedDate = change == "historical" ? today : null
            });
            if (change == "settings")
            {
                var settings = await db.Settings.SingleOrDefaultAsync(x => x.AgencyId == 2);
                if (settings is null)
                {
                    settings = new ServerSettings { AgencyId = 2 };
                    db.Settings.Add(settings);
                }
                settings.BillingComplianceRequirements |= BillingComplianceRequirements.PrivacyPractices;
            }
        }
        else if (change == "status") note.Status = 2;
        else if (change == "overlap")
        {
            note.StartTime = 60;
            var otherConsumer = new ServerPerson { UserId = 22, AgencyId = 2, FirstName = "Synthetic", LastName = "Peer" };
            db.People.Add(otherConsumer);
            await db.SaveChangesAsync();
            db.Notes.Add(new ServerNote
            {
                PersonId = otherConsumer.Id, AgencyId = 2, Narrative = "Synthetic legacy overlapping note",
                EventDate = note.EventDate, Status = 6, Minutes = 15, StartTime = 65
            });
        }
        else if (change == "service-date") note.EventDate = note.EventDate!.Value.AddDays(1);
        else if (change == "exception")
        {
            note.ComplianceOverride = true;
            note.OverrideReason = "Incomplete or newly added exception must not authorize the frozen claim";
        }
        else if (change == "person-tenant")
            (await db.People.SingleAsync(x => x.Id == 201)).AgencyId = 1;
        else if (change == "period-status")
            (await db.BillingPeriods.SingleAsync(x => x.Id == 1202)).Status = 0;
        await db.SaveChangesAsync();
        var auditBefore = await db.AuditEvents.CountAsync();
        var eventsBefore = await db.BillingSubmissionEvents.CountAsync();

        var refused = await ExportAsync(client, key);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(frozen, await db.ClaimLines.AsNoTracking().Where(x => x.Id == 1402)
            .Select(x => x.ClaimSnapshotJson).SingleAsync());
        Assert.Equal(replay ? 1 : 0, await db.EdiGenerations.CountAsync());
        Assert.Equal(auditBefore, await db.AuditEvents.CountAsync());
        Assert.Equal(eventsBefore, await db.BillingSubmissionEvents.CountAsync());
        if (original is not null)
        {
            var retained = await db.EdiGenerations.AsNoTracking().SingleAsync();
            Assert.Equal(original.Content, retained.Content);
            Assert.Equal(original.FileName, retained.FileName);
        }
    }

    [Fact]
    public async Task CompleteStoredExceptionPermitsExportAndRetryWithoutRebuildingFrozenInputs()
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var note = await db.Notes.SingleAsync(x => x.Id == 603);
        var line = await db.ClaimLines.SingleAsync(x => x.Id == 1402);
        note.ComplianceOverride = line.IsComplianceException = true;
        note.OverrideReason = line.ComplianceExceptionReason = "Recorded supervisory exception";
        note.ApprovedById = note.OverrideApprovedById = 21;
        note.ApprovedAt = note.OverrideApprovedAt = DateTime.UtcNow;
        db.Forms.Add(new ServerForm { PersonId = 201, Type = "PCP", DueDate = new DateTime(2026, 8, 1) });
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString("N");
        var first = await ExportAsync(client, key);
        first.EnsureSuccessStatusCode();
        var original = await first.Content.ReadFromJsonAsync<EdiFileDto>();

        (await db.People.SingleAsync(x => x.Id == 201)).FirstName = "Changed live name";
        (await db.Agencies.SingleAsync(x => x.Id == 2)).BillingUnitRate = 999m;
        await db.SaveChangesAsync();
        var retry = await ExportAsync(client, key);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(original, await retry.Content.ReadFromJsonAsync<EdiFileDto>());
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    [Fact]
    public async Task RemovingAttestationAfterGenerationBlocksRetryAndLateCompletionDoesNotCureServiceGap()
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var form = new ServerForm
        {
            PersonId = 201, Type = "PCP", DueDate = new DateTime(2026, 8, 10),
            CompletedDate = new DateTime(2026, 8, 11)
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString("N");
        (await ExportAsync(client, key)).EnsureSuccessStatusCode();
        form.CompletedDate = null;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExportAsync(client, key)).StatusCode);
        form.CompletedDate = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExportAsync(client, key)).StatusCode);
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, string key) =>
        client.PostAsJsonAsync("/api/v1/billing/periods/1202/edi", new GenerateEdiRequest(true, key));
}
