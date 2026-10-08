using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using SatiLogica.Contracts;
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
    [InlineData("uncompleted", false)]
    [InlineData("historical", false)]
    [InlineData("settings", false)]
    [InlineData("status", false)]
    [InlineData("service-date", false)]
    [InlineData("exception", false)]
    [InlineData("overlap", false)]
    [InlineData("uncompleted", true)]
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
        var today = TenantClock.MaineDate(DateTimeOffset.UtcNow);
        if (change is "uncompleted" or "historical" or "settings")
        {
            db.Forms.Add(new ServerForm
            {
                PersonId = 201,
                Type = change == "settings" ? "PrivacyPractices" : "PCP",
                // Each seeded obligation is due before the note's service date, which is
                // what places that service inside a non-billable gap.
                TargetEffectiveDate = note.EventDate!.Value.AddDays(-1).Date,
                DueDate = note.EventDate!.Value.AddDays(-1),
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
        await RecordExactPcpExceptionAsync(db);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedUnselectedObligationBlocksExportAndReplayWithoutChangingRetainedEvidence(bool replay)
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pcp = await RecordExactPcpExceptionAsync(db);
        var note = await db.Notes.SingleAsync(x => x.Id == 603);
        var completedOn = note.EventDate!.Value.Date.AddDays(-2);
        var assessment = new ServerForm
        {
            PersonId = note.PersonId, Type = "ComprehensiveAssessment",
            TargetEffectiveDate = pcp.TargetEffectiveDate,
            DueDate = ComplianceScheduleRules.DueDate("ComprehensiveAssessment",
                pcp.TargetEffectiveDate, new ComplianceScheduleSettings()),
            CompletedDate = completedOn,
            Attestations = [new ServerFormAttestation
            {
                Kind = "Attested", ActorKind = "Supervisor", ActorUserId = 21,
                CompletedOn = completedOn, RecordedAtUtc = DateTime.UtcNow
            }]
        };
        db.Forms.Add(assessment);
        await db.SaveChangesAsync();
        var frozen = await db.ClaimLines.Where(x => x.Id == 1402)
            .Select(x => x.ClaimSnapshotJson).SingleAsync();
        var key = Guid.NewGuid().ToString("N");
        using var first = await ExportAsync(client, key);
        first.EnsureSuccessStatusCode();
        var original = (await first.Content.ReadFromJsonAsync<EdiFileDto>())!;

        using var revoke = await client.PostAsJsonAsync(
            $"/api/v1/people/{note.PersonId}/forms/ComprehensiveAssessment/attestation/revoke",
            new RevokeFormAttestationRequest(assessment.Id, "Synthetic unselected assessment evidence was withdrawn."));
        revoke.EnsureSuccessStatusCode();
        Assert.Null(await db.Forms.AsNoTracking().Where(x => x.Id == assessment.Id)
            .Select(x => x.CompletedDate).SingleAsync());
        Assert.Single(await db.FormAttestations.AsNoTracking().Where(x =>
            x.FormId == assessment.Id && x.Kind == "Revoked").ToListAsync());
        var auditBefore = await db.AuditEvents.CountAsync();
        var eventsBefore = await db.BillingSubmissionEvents.CountAsync();

        using var refused = await ExportAsync(client, replay ? key : Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = (await refused.Content.ReadFromJsonAsync<ApiErrorDto>())!;
        Assert.Equal("billing_export_blocked", error.Code);
        Assert.Contains("Comprehensive Assessment", error.Message, StringComparison.Ordinal);
        Assert.Equal(auditBefore, await db.AuditEvents.CountAsync());
        Assert.Equal(eventsBefore, await db.BillingSubmissionEvents.CountAsync());
        Assert.Equal(frozen, await db.ClaimLines.AsNoTracking().Where(x => x.Id == 1402)
            .Select(x => x.ClaimSnapshotJson).SingleAsync());
        var retained = Assert.Single(await db.EdiGenerations.AsNoTracking().ToListAsync());
        Assert.Equal(original.Content, retained.Content);
        Assert.Equal(original.FileName, retained.FileName);
    }

    [Fact]
    public async Task ExactAdminRecoveryPermitsExportAndReplayOfTheFrozenClaim()
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-two");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var note = await db.Notes.SingleAsync(x => x.Id == 603);
        var line = await db.ClaimLines.SingleAsync(x => x.Id == 1402);
        var frozen = line.ClaimSnapshotJson;
        // Recovery is recorded for an approved unbilled note, before claim promotion.
        db.ClaimLines.Remove(line);
        var target = note.EventDate!.Value.Date.AddDays(-1);
        var completedOn = note.EventDate.Value.Date.AddDays(1);
        var pcp = new ServerForm
        {
            PersonId = note.PersonId, Type = "PCP", TargetEffectiveDate = target,
            DueDate = ComplianceScheduleRules.DueDate("PCP", target, new ComplianceScheduleSettings()),
            CompletedDate = completedOn,
            Attestations = [new ServerFormAttestation
            {
                Kind = "Attested", ActorKind = "Supervisor", ActorUserId = 21,
                CompletedOn = completedOn, RecordedAtUtc = DateTime.UtcNow
            }]
        };
        db.Forms.Add(pcp);
        await db.SaveChangesAsync();
        using var recovery = await client.PostAsJsonAsync(
            $"/api/v1/billing/compliance-recovery/{note.PersonId}",
            new CreateBillingComplianceRecoveryRequest([note.Id],
                "The exact PCP evidence is complete; recover this synthetic service note.", true));
        recovery.EnsureSuccessStatusCode();
        var decision = (await recovery.Content.ReadFromJsonAsync<BillingComplianceRecoveryDecision>())!;
        Assert.Equal([note.Id], decision.NoteIds);
        Assert.Equal($"form:{pcp.Id}", Assert.Single(decision.Obligations).ObligationId);
        db.ClaimLines.Add(line);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString("N");

        using var first = await ExportAsync(client, key);
        first.EnsureSuccessStatusCode();
        var original = (await first.Content.ReadFromJsonAsync<EdiFileDto>())!;
        using var replay = await ExportAsync(client, key);
        replay.EnsureSuccessStatusCode();

        Assert.Equal(original, await replay.Content.ReadFromJsonAsync<EdiFileDto>());
        Assert.Single(await db.EdiGenerations.AsNoTracking().ToListAsync());
        Assert.Equal(frozen, (await db.ClaimLines.AsNoTracking().SingleAsync(x => x.Id == 1402)).ClaimSnapshotJson);
        Assert.False((await db.Notes.AsNoTracking().SingleAsync(x => x.Id == note.Id)).ComplianceOverride);
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
            PersonId = 201, Type = "PCP", TargetEffectiveDate = new DateTime(2026, 8, 10),
            DueDate = new DateTime(2026, 8, 10),
            CompletedDate = new DateTime(2026, 8, 11)
        };
        db.Forms.Add(form);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString("N");
        (await ExportAsync(client, key)).EnsureSuccessStatusCode();
        form.CompletedDate = null;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExportAsync(client, key)).StatusCode);
        form.CompletedDate = TenantClock.MaineDate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExportAsync(client, key)).StatusCode);
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, string key) =>
        client.PostAsJsonAsync("/api/v1/billing/periods/1202/edi", new GenerateEdiRequest(true, key));

    private static async Task<ServerForm> RecordExactPcpExceptionAsync(ApiDbContext db)
    {
        var note = await db.Notes.SingleAsync(x => x.Id == 603);
        var line = await db.ClaimLines.SingleAsync(x => x.Id == 1402);
        var target = note.EventDate!.Value.Date.AddDays(-1);
        var pcp = new ServerForm
        {
            PersonId = note.PersonId, Type = "PCP", TargetEffectiveDate = target,
            DueDate = ComplianceScheduleRules.DueDate("PCP", target, new ComplianceScheduleSettings())
        };
        db.Forms.Add(pcp);
        await db.SaveChangesAsync();
        note.ComplianceOverride = line.IsComplianceException = true;
        note.OverrideReason = line.ComplianceExceptionReason = "Recorded supervisory exception";
        note.ApprovedById = note.OverrideApprovedById = 21;
        note.ApprovedAt = note.OverrideApprovedAt = DateTime.UtcNow;
        note.OverrideAttestationConfirmed = true;
        note.OverrideObligationIdsJson = JsonSerializer.Serialize(new[] { $"form:{pcp.Id}" });
        await db.SaveChangesAsync();
        return pcp;
    }
}
