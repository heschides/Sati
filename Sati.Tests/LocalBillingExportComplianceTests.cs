using System.IO;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using SatiLogica.Contracts;
using Sati.Data;
using Sati.Edi;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class LocalBillingExportComplianceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedFileCannotBeReplayedByARevokedOrDisabledSession(bool disabled)
    {
        await using var fixture = await ExportFixture.CreateAsync();
        await fixture.ExportAsync();
        await using var db = fixture.Inner.Factory.CreateDbContext();
        var actor = await db.Users.SingleAsync(x => x.Id == fixture.Inner.CaseManagerOne.Id);
        if (disabled) actor.IsEnabled = false;
        else actor.SecurityVersion++;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SessionExpiredException>(() => fixture.ExportAsync());
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
    [InlineData("person-tenant", false)]
    [InlineData("uncompleted", true)]
    [InlineData("historical", true)]
    [InlineData("settings", true)]
    [InlineData("status", true)]
    [InlineData("service-date", true)]
    [InlineData("exception", true)]
    [InlineData("overlap", true)]
    [InlineData("person-tenant", true)]
    [InlineData("period-status", true)]
    public async Task ExportAndReplayRevalidateWithoutMutatingFinancialEvidence(string change, bool replay)
    {
        await using var fixture = await ExportFixture.CreateAsync();
        string? original = null;
        if (replay) original = await File.ReadAllTextAsync(await fixture.ExportAsync());
        await using var db = fixture.Inner.Factory.CreateDbContext();
        var note = await db.Notes.SingleAsync();
        var person = await db.People.SingleAsync(x => x.Id == fixture.Inner.PersonOneId);
        var frozen = (await db.ClaimLines.SingleAsync()).ClaimSnapshotJson;
        var today = TenantClock.MaineDate(DateTimeOffset.UtcNow);
        if (change is "uncompleted" or "historical" or "settings")
        {
            db.Forms.Add(new Form(
                change == "settings" ? FormType.PrivacyPractices : FormType.PCP,
                note.EventDate!.Value.AddDays(-1),
                change == "historical" ? today : null,
                targetEffectiveDate: note.EventDate!.Value.AddDays(-1).Date) { PersonId = person.Id });
            if (change == "settings")
                (await db.Settings.SingleAsync()).BillingComplianceRequirements |= BillingComplianceRequirements.PrivacyPractices;
        }
        else if (change == "status") note.Status = NoteStatus.Logged;
        else if (change == "overlap")
        {
            note.StartTime = 60;
            var peer = Note.Create("Synthetic legacy overlapping note", note.EventDate,
                NoteStatus.Approved, 15, fixture.Inner.PersonTwoId);
            peer.AgencyId = fixture.Inner.CaseManagerOne.AgencyId;
            peer.StartTime = 65;
            db.Notes.Add(peer);
        }
        else if (change == "service-date") note.EventDate = note.EventDate!.Value.AddDays(1);
        else if (change == "exception")
        {
            note.ComplianceOverride = true;
            note.OverrideReason = "Incomplete exception must not authorize export";
        }
        else if (change == "person-tenant") person.AgencyId = fixture.Inner.CaseManagerTwo.AgencyId;
        else if (change == "period-status") (await db.BillingPeriods.SingleAsync()).Status = BillingStatus.Draft;
        await db.SaveChangesAsync();
        var auditBefore = await db.AuditEvents.CountAsync();
        var eventsBefore = await db.BillingSubmissionEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExportAsync());

        Assert.Equal(replay ? 1 : 0, await db.EdiGenerations.CountAsync());
        Assert.Equal(auditBefore, await db.AuditEvents.CountAsync());
        Assert.Equal(eventsBefore, await db.BillingSubmissionEvents.CountAsync());
        Assert.Equal(frozen, (await db.ClaimLines.AsNoTracking().SingleAsync()).ClaimSnapshotJson);
        if (replay) Assert.Equal(original, (await db.EdiGenerations.AsNoTracking().SingleAsync()).Content);
    }

    [Fact]
    public async Task CompleteFrozenExceptionStillExportsAndReplaysTheOriginalBytes()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        await using var db = fixture.Inner.Factory.CreateDbContext();
        await RecordExactPcpExceptionAsync(db, fixture.Inner.CaseManagerOne.Id);
        var first = await File.ReadAllTextAsync(await fixture.ExportAsync());
        var note = await db.Notes.SingleAsync();
        (await db.People.SingleAsync(x => x.Id == note.PersonId)).FirstName = "Changed live name";
        (await db.Agencies.SingleAsync(x => x.Id == fixture.Inner.CaseManagerOne.AgencyId)).BillingUnitRate = 999m;
        await db.SaveChangesAsync();
        var retry = await File.ReadAllTextAsync(await fixture.ExportAsync());
        Assert.Equal(first, retry);
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedUnselectedObligationBlocksExportAndReplayWithoutChangingRetainedEvidence(bool replay)
    {
        await using var fixture = await ExportFixture.CreateAsync();
        await using var db = fixture.Inner.Factory.CreateDbContext();
        var pcp = await RecordExactPcpExceptionAsync(db, fixture.Inner.CaseManagerOne.Id);
        var note = await db.Notes.SingleAsync();
        var completedOn = note.EventDate!.Value.Date.AddDays(-2);
        var assessment = new Form(FormType.ComprehensiveAssessment,
            ComplianceScheduleRules.DueDate("ComprehensiveAssessment", pcp.TargetEffectiveDate,
                new ComplianceScheduleSettings()), targetEffectiveDate: pcp.TargetEffectiveDate)
            { PersonId = note.PersonId };
        assessment.Attest(FormAttestation.Attested(completedOn, AttestationActorKind.Supervisor,
            fixture.Inner.CaseManagerOne.Id, DateTime.UtcNow));
        db.Forms.Add(assessment);
        await db.SaveChangesAsync();
        var frozen = (await db.ClaimLines.SingleAsync()).ClaimSnapshotJson;
        var path = await fixture.ExportAsync();
        var originalBytes = await File.ReadAllBytesAsync(path);
        var original = await File.ReadAllTextAsync(path);
        var fileName = Path.GetFileName(path);

        await fixture.RevokeAttestationAsync(assessment);
        Assert.Null(await db.Forms.AsNoTracking().Where(x => x.Id == assessment.Id)
            .Select(x => x.CompletedDate).SingleAsync());
        Assert.Single(await db.FormAttestations.AsNoTracking().Where(x =>
            x.FormId == assessment.Id && x.Kind == FormAttestationKind.Revoked).ToListAsync());
        var auditBefore = await db.AuditEvents.CountAsync();
        var eventsBefore = await db.BillingSubmissionEvents.CountAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ExportAsync(replay ? null : Guid.NewGuid().ToString("N")));

        Assert.Contains("Comprehensive Assessment", failure.Message, StringComparison.Ordinal);
        Assert.Equal(auditBefore, await db.AuditEvents.CountAsync());
        Assert.Equal(eventsBefore, await db.BillingSubmissionEvents.CountAsync());
        Assert.Equal(frozen, (await db.ClaimLines.AsNoTracking().SingleAsync()).ClaimSnapshotJson);
        var retained = Assert.Single(await db.EdiGenerations.AsNoTracking().ToListAsync());
        Assert.Equal(original, retained.Content);
        Assert.Equal(fileName, retained.FileName);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    private static async Task<Form> RecordExactPcpExceptionAsync(SatiContext db, int approverId)
    {
        var note = await db.Notes.SingleAsync();
        var line = await db.ClaimLines.SingleAsync();
        var target = note.EventDate!.Value.Date.AddDays(-1);
        var pcp = new Form(FormType.PCP,
            ComplianceScheduleRules.DueDate("PCP", target, new ComplianceScheduleSettings()),
            targetEffectiveDate: target) { PersonId = note.PersonId };
        db.Forms.Add(pcp);
        await db.SaveChangesAsync();
        note.ComplianceOverride = line.IsComplianceException = true;
        note.OverrideReason = line.ComplianceExceptionReason = "Recorded supervisory exception";
        note.ApprovedById = note.OverrideApprovedById = approverId;
        note.ApprovedAt = note.OverrideApprovedAt = DateTime.UtcNow;
        note.OverrideAttestationConfirmed = true;
        note.OverrideObligationIds = [$"form:{pcp.Id}"];
        await db.SaveChangesAsync();
        return pcp;
    }

    [Fact]
    public async Task ExactAdminRecoveryPermitsExportAndReplayOfTheFrozenClaim()
    {
        await using var fixture = await ExportFixture.CreateAsync();
        await using var db = fixture.Inner.Factory.CreateDbContext();
        var note = await db.Notes.SingleAsync();
        var line = await db.ClaimLines.SingleAsync();
        var frozen = line.ClaimSnapshotJson;
        // Recovery is recorded for an approved unbilled note, before claim promotion.
        db.ClaimLines.Remove(line);
        fixture.Inner.CaseManagerOne.Permissions |= UserPermissions.Administration;
        (await db.Users.SingleAsync(x => x.Id == fixture.Inner.CaseManagerOne.Id)).Permissions =
            fixture.Inner.CaseManagerOne.Permissions;
        var person = await db.People.SingleAsync(x => x.Id == note.PersonId);
        person.MaineCareId = line.ClientMaineCareId;
        person.DiagnosisCode = line.DiagnosisCode;
        person.PlaceOfService = line.PlaceOfService;
        person.BillingStreet = "10 Test Street";
        person.BillingCity = "Portland";
        person.BillingState = "ME";
        person.BillingZip = "04101";
        var agency = await db.Agencies.SingleAsync(x => x.Id == person.AgencyId);
        agency.Npi = "1999999984";
        agency.TaxId = "111111111";
        agency.Street = "1 Test Street";
        agency.City = "Portland";
        agency.State = "ME";
        agency.Zip = "04101";
        agency.BillingProcedureCode = "G9012";
        agency.BillingModifier = "HI";
        agency.BillingUnitRate = 25m;
        agency.EdiSubmitterId = "SATITEST";
        agency.EdiPayerName = "Synthetic Payer";
        agency.EdiPayerId = "99999";
        agency.EdiContactName = "Synthetic Contact";
        agency.EdiContactPhone = "2075550101";
        var target = note.EventDate!.Value.Date.AddDays(-1);
        var completedOn = note.EventDate.Value.Date.AddDays(1);
        var pcp = new Form(FormType.PCP,
            ComplianceScheduleRules.DueDate("PCP", target, new ComplianceScheduleSettings()),
            targetEffectiveDate: target) { PersonId = note.PersonId };
        pcp.Attest(FormAttestation.Attested(completedOn, AttestationActorKind.Supervisor,
            fixture.Inner.CaseManagerOne.Id, DateTime.UtcNow));
        db.Forms.Add(pcp);
        await db.SaveChangesAsync();
        var decision = await fixture.RecordAdminRecoveryAsync(note.Id, note.PersonId);
        Assert.Equal([note.Id], decision.NoteIds);
        Assert.Equal($"form:{pcp.Id}", Assert.Single(decision.Obligations).ObligationId);
        db.ClaimLines.Add(line);
        await db.SaveChangesAsync();

        var path = await fixture.ExportAsync();
        var originalBytes = await File.ReadAllBytesAsync(path);
        var replayPath = await fixture.ExportAsync();

        Assert.Equal(path, replayPath);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(replayPath));
        Assert.Single(await db.EdiGenerations.AsNoTracking().ToListAsync());
        Assert.Equal(frozen, (await db.ClaimLines.AsNoTracking().SingleAsync()).ClaimSnapshotJson);
        Assert.False((await db.Notes.AsNoTracking().SingleAsync()).ComplianceOverride);
    }

    private sealed class ExportFixture(NoteEntryFixture inner, int periodId, SessionService session) : IAsyncDisposable
    {
        public NoteEntryFixture Inner { get; } = inner;
        private readonly string _key = Guid.NewGuid().ToString("N");
        private readonly HashSet<string> _keys = [];

        public Task<string> ExportAsync(string? key = null)
        {
            var requestKey = key ?? _key;
            _keys.Add(requestKey);
            return new EdiService(Inner.Factory, session).GenerateAndSaveAsync(periodId, true, requestKey);
        }

        public Task RevokeAttestationAsync(Form form) => new FormService(Inner.Factory, session)
            .RevokeAttestationAsync(form, "Synthetic unselected assessment evidence was withdrawn.");

        public Task<Sati.Contracts.V1.BillingComplianceRecoveryDecision> RecordAdminRecoveryAsync(int noteId, int personId)
        {
            // Permission changes require a fresh captured session, just as in the app.
            session.SetUser(Inner.CaseManagerOne);
            return new BillingService(Inner.Factory, session).RecordComplianceRecoveryAsync(
                Inner.CaseManagerOne.ToAgencyActor(), personId,
                new CreateBillingComplianceRecoveryRequest([noteId],
                    "The exact PCP evidence is complete; recover this synthetic service note.", true));
        }

        public static async Task<ExportFixture> CreateAsync()
        {
            var inner = await NoteEntryFixture.CreateAsync();
            inner.CaseManagerOne.Permissions = UserPermissions.CaseManagement | UserPermissions.Billing | UserPermissions.Supervision;
            await using var db = inner.Factory.CreateDbContext();
            (await db.Users.SingleAsync(x => x.Id == inner.CaseManagerOne.Id)).Permissions = inner.CaseManagerOne.Permissions;
            db.Settings.Add(new Settings { AgencyId = inner.CaseManagerOne.AgencyId });
            var serviceDate = TenantClock.MaineDate(DateTimeOffset.UtcNow).AddDays(-7);
            var note = Note.Create("Synthetic export regression", serviceDate, NoteStatus.Approved, 15, inner.PersonOneId);
            note.AgencyId = inner.CaseManagerOne.AgencyId;
            db.Notes.Add(note);
            var period = new BillingPeriod
            {
                UserId = inner.CaseManagerOne.Id, Month = serviceDate.Month, Year = serviceDate.Year,
                Status = BillingStatus.Submitted, SubmittedAt = DateTime.UtcNow
            };
            period.Lines.Add(new ClaimLine
            {
                Note = note, DateOfService = serviceDate, ProcedureCode = "G9012", ProcedureModifier = "HI",
                Units = 1, ChargeAmount = 25, ClientMaineCareId = "111111", RenderingProviderNpi = "1999999984",
                DiagnosisCode = "F89", PlaceOfService = 11,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(new ProfessionalClaimSnapshot(
                    1, inner.CaseManagerOne.AgencyId, inner.PersonOneId, "Synthetic", "Person", new DateTime(1990, 1, 1),
                    "U", "111111", "10 Test Street", "Portland", "ME", "04101", "Synthetic Agency", "1999999984",
                    "111111111", "1 Test Street", "Portland", "ME", "04101", "SATITEST", "Synthetic Contact",
                    "2075550101", "Synthetic Payer", "99999"))
            });
            db.BillingPeriods.Add(period);
            await db.SaveChangesAsync();
            var session = new SessionService();
            session.SetUser(inner.CaseManagerOne);
            return new ExportFixture(inner, period.Id, session);
        }

        public async ValueTask DisposeAsync()
        {
            // Only this isolated fixture's random-key files are eligible for cleanup.
            await using (var db = Inner.Factory.CreateDbContext())
            {
                var keys = _keys.ToList();
                var names = await db.EdiGenerations.Where(x => keys.Contains(x.IdempotencyKey))
                    .Select(x => x.FileName).ToListAsync();
                foreach (var name in names)
                {
                    Assert.StartsWith("837P.OATEST_", name);
                    Assert.Equal(Path.GetFileName(name), name);
                    File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sati", "EDI", name));
                }
            }
            await Inner.DisposeAsync();
        }
    }
}
