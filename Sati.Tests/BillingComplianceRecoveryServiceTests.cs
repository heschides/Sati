using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.Services.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class BillingComplianceRecoveryServiceTests : IAsyncDisposable
{
    private SqliteConnection _connection = null!;
    private IDbContextFactory<SatiContext> _factory = null!;
    private User _admin = null!;
    private User _caseManager = null!;
    private User _foreignAdmin = null!;
    private int _personId;
    private int[] _noteIds = [];

    public BillingComplianceRecoveryServiceTests() =>
        InitializeAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task LocalRecoveryPersistsExactEvidenceAndReleasesOnlySelectedNote()
    {
        var service = ServiceFor(_admin);
        var plan = await service.PrepareComplianceRecoveryAsync(
            _admin.ToAgencyActor(), _personId);
        Assert.Equal(_noteIds, plan.NoteOptions.Select(item => item.NoteId));
        var evidence = Assert.Single(plan.Obligations).EvidenceId;
        Assert.StartsWith("form-attestation:", evidence, StringComparison.Ordinal);

        var decision = await service.RecordComplianceRecoveryAsync(
            _admin.ToAgencyActor(),
            _personId,
            new CreateBillingComplianceRecoveryRequest(
                [_noteIds[0]],
                "The PCP is complete; release only the selected note.",
                AttestationConfirmed: true));
        Assert.Equal([_noteIds[0]], decision.NoteIds);

        var candidates = (await service.GetApprovedUnbilledNotesAsync(
            _admin.ToAgencyActor())).ToDictionary(item => item.Id);
        Assert.True(service.ValidateNoteForBilling(candidates[_noteIds[0]]).IsValid);
        var unselected = service.ValidateNoteForBilling(candidates[_noteIds[1]]);
        Assert.False(unselected.IsValid);
        Assert.Contains(unselected.Errors,
            error => error.Contains("PCP", StringComparison.Ordinal));

        await using var db = _factory.CreateDbContext();
        var stored = await db.BillingComplianceRecoveryDecisions.AsNoTracking()
            .Include(item => item.Obligations)
            .Include(item => item.Notes)
            .SingleAsync();
        Assert.Equal(evidence, Assert.Single(stored.Obligations).EvidenceId);
        Assert.Equal([_noteIds[0]], stored.Notes.Select(item => item.NoteId));
        var selectedNote = await db.Notes.AsNoTracking()
            .SingleAsync(item => item.Id == _noteIds[0]);
        Assert.Equal(NoteStatus.Approved, selectedNote.Status);
        Assert.False(selectedNote.ComplianceOverride);
        Assert.Equal(1, selectedNote.Revision);

        db.Remove(stored);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LocalRecoveryRequiresCurrentAdminAndSameAgencyConsumer()
    {
        var caseManagerService = ServiceFor(_caseManager);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            caseManagerService.PrepareComplianceRecoveryAsync(
                _caseManager.ToAgencyActor(), _personId));

        var foreignService = ServiceFor(_foreignAdmin);
        var foreign = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            foreignService.PrepareComplianceRecoveryAsync(
                _foreignAdmin.ToAgencyActor(), _personId));
        Assert.Contains("not found", foreign.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new ContextFactory(options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        var agency = BillingAgency(501, "Recovery Agency");
        var foreignAgency = BillingAgency(502, "Foreign Agency");
        var admin = User.Create(1001, "recovery-admin", "Recovery Admin", "hash", "salt",
            UserRole.Admin, null, agency.Id);
        var caseManager = User.Create(1002, "recovery-cm", "Recovery CM", "hash", "salt",
            UserRole.CaseManager, null, agency.Id);
        var foreignAdmin = User.Create(1003, "foreign-admin", "Foreign Admin", "hash", "salt",
            UserRole.Admin, null, foreignAgency.Id);
        db.Agencies.AddRange(agency, foreignAgency);
        db.Users.AddRange(admin, caseManager, foreignAdmin);

        var person = Person.CreatePerson(
            caseManager.Id,
            "Synthetic",
            "Recovery",
            string.Empty,
            new DateTime(1990, 1, 1),
            effective: null,
            WaiverType.Section21,
            new Settings());
        person.AgencyId = agency.Id;
        person.MaineCareId = "991122";
        person.DiagnosisCode = "F89";
        person.PlaceOfService = (int)PlaceOfService.Office;
        person.BillingStreet = "10 Test Street";
        person.BillingCity = "Portland";
        person.BillingState = "ME";
        person.BillingZip = "04101";
        person.Gender = Gender.Unknown;
        db.People.Add(person);
        await db.SaveChangesAsync();

        var pcp = new Form(
            FormType.PCP,
            new DateTime(2010, 7, 31),
            targetEffectiveDate: new DateTime(2010, 7, 31))
        {
            PersonId = person.Id
        };
        pcp.Attest(FormAttestation.Attested(
            new DateTime(2010, 8, 5),
            AttestationActorKind.CaseManager,
            caseManager.Id,
            new DateTime(2010, 8, 6, 12, 0, 0, DateTimeKind.Utc)));
        db.Forms.Add(pcp);
        var notes = Enumerable.Range(0, 2).Select(index =>
        {
            var note = Note.Create(
                $"Synthetic recovery note {index + 1}",
                new DateTime(2010, 8, 3),
                NoteStatus.Approved,
                15,
                person.Id);
            note.AgencyId = agency.Id;
            note.ApprovedById = admin.Id;
            note.ApprovedAt = new DateTime(2010, 8, 4, 12, 0, 0, DateTimeKind.Utc);
            return note;
        }).ToArray();
        db.Notes.AddRange(notes);
        await db.SaveChangesAsync();

        _admin = admin;
        _caseManager = caseManager;
        _foreignAdmin = foreignAdmin;
        _personId = person.Id;
        _noteIds = notes.Select(item => item.Id).ToArray();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private BillingService ServiceFor(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return new BillingService(_factory, session);
    }

    private static Agency BillingAgency(int id, string name) => new()
    {
        Id = id,
        Name = name,
        Npi = "1999999984",
        TaxId = "111111111",
        Street = "1 Test Street",
        City = "Portland",
        State = "ME",
        Zip = "04101",
        BillingProcedureCode = "G9012",
        BillingModifier = "HI",
        BillingUnitRate = 25m,
        EdiSubmitterId = $"SATI{id}",
        EdiPayerName = "MEDICAID MAINE",
        EdiPayerId = "MCDME",
        EdiContactName = "Test Billing",
        EdiContactPhone = "2075550101"
    };

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
