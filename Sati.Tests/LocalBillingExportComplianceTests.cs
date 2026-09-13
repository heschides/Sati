using System.IO;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Edi;
using Sati.Models;
using Sati.Models.Billing;
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
    [InlineData("current", false)]
    [InlineData("historical", false)]
    [InlineData("settings", false)]
    [InlineData("status", false)]
    [InlineData("service-date", false)]
    [InlineData("exception", false)]
    [InlineData("overlap", false)]
    [InlineData("person-tenant", false)]
    [InlineData("current", true)]
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
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        if (change is "current" or "historical" or "settings")
        {
            db.Forms.Add(new Form(
                change == "settings" ? FormType.PrivacyPractices : FormType.PCP,
                change == "historical" ? note.EventDate!.Value.AddDays(-1) : today.AddDays(-1),
                change == "historical" ? today : null) { PersonId = person.Id });
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
        var note = await db.Notes.SingleAsync();
        var line = await db.ClaimLines.SingleAsync();
        note.ComplianceOverride = line.IsComplianceException = true;
        note.OverrideReason = line.ComplianceExceptionReason = "Recorded supervisory exception";
        note.ApprovedById = note.OverrideApprovedById = fixture.Inner.CaseManagerOne.Id;
        note.ApprovedAt = note.OverrideApprovedAt = DateTime.UtcNow;
        db.Forms.Add(new Form(FormType.PCP, note.EventDate!.Value.AddDays(-1)) { PersonId = note.PersonId });
        await db.SaveChangesAsync();
        var first = await File.ReadAllTextAsync(await fixture.ExportAsync());
        (await db.People.SingleAsync(x => x.Id == note.PersonId)).FirstName = "Changed live name";
        (await db.Agencies.SingleAsync(x => x.Id == fixture.Inner.CaseManagerOne.AgencyId)).BillingUnitRate = 999m;
        await db.SaveChangesAsync();
        var retry = await File.ReadAllTextAsync(await fixture.ExportAsync());
        Assert.Equal(first, retry);
        Assert.Single(await db.EdiGenerations.ToListAsync());
    }

    private sealed class ExportFixture(NoteEntryFixture inner, int periodId, SessionService session) : IAsyncDisposable
    {
        public NoteEntryFixture Inner { get; } = inner;
        private readonly string _key = Guid.NewGuid().ToString("N");

        public Task<string> ExportAsync() => new EdiService(Inner.Factory, session).GenerateAndSaveAsync(periodId, true, _key);

        public static async Task<ExportFixture> CreateAsync()
        {
            var inner = await NoteEntryFixture.CreateAsync();
            inner.CaseManagerOne.Permissions = UserPermissions.Billing | UserPermissions.Supervision;
            await using var db = inner.Factory.CreateDbContext();
            (await db.Users.SingleAsync(x => x.Id == inner.CaseManagerOne.Id)).Permissions = inner.CaseManagerOne.Permissions;
            db.Settings.Add(new Settings { AgencyId = inner.CaseManagerOne.AgencyId });
            var serviceDate = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow).AddDays(-7);
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
                var names = await db.EdiGenerations.Where(x => x.IdempotencyKey == _key).Select(x => x.FileName).ToListAsync();
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
