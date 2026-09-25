using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Sati.Services.Billing;
using System.Data.Common;
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
    private readonly CommandCapture _commands = new();

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
    public async Task ApprovedCandidateGraphSplitsFormsAndReleaseObligations()
    {
        _commands.Clear();

        var candidates = (await ServiceFor(_admin).GetApprovedUnbilledNotesAsync(
            _admin.ToAgencyActor())).ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Same(candidates[0].Person, candidates[1].Person);
        Assert.DoesNotContain(_commands.ReaderCommands, command =>
            command.Contains("FROM \"Notes\"", StringComparison.Ordinal) &&
            command.Contains("\"Forms\"", StringComparison.Ordinal) &&
            command.Contains("\"ReleaseObligations\"", StringComparison.Ordinal));
        Assert.Contains(_commands.ReaderCommands, command =>
            command.Contains("\"Forms\"", StringComparison.Ordinal));
        Assert.Contains(_commands.ReaderCommands, command =>
            command.Contains("\"ReleaseObligations\"", StringComparison.Ordinal));
        var candidateCommand = Assert.Single(_commands.ReaderCommands, command =>
            command.Contains("FROM \"Notes\"", StringComparison.Ordinal) &&
            command.Contains("JOIN \"People\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"Narrative\"", candidateCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("\"VisitDocumentationJson\"", candidateCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Bio\"", candidateCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Journal\"", candidateCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovedCandidatesRequireMatchingNotePersonAndOwnerAgency()
    {
        _commands.Clear();
        await using (var db = _factory.CreateDbContext())
        {
            var mismatchedNote = await db.Notes.SingleAsync(note => note.Id == _noteIds[0]);
            mismatchedNote.AgencyId = _foreignAdmin.AgencyId;
            await db.SaveChangesAsync();
        }

        var service = ServiceFor(_admin);
        var candidates = (await service.GetApprovedUnbilledNotesAsync(
            _admin.ToAgencyActor())).ToList();
        Assert.Equal([_noteIds[1]], candidates.Select(note => note.Id));

        await using (var db = _factory.CreateDbContext())
        {
            var restoredNote = await db.Notes.SingleAsync(note => note.Id == _noteIds[0]);
            restoredNote.AgencyId = _admin.AgencyId;
            var mismatchedOwner = await db.Users.SingleAsync(user => user.Id == _caseManager.Id);
            mismatchedOwner.AgencyId = _foreignAdmin.AgencyId;
            await db.SaveChangesAsync();
        }

        candidates = (await service.GetApprovedUnbilledNotesAsync(
            _admin.ToAgencyActor())).ToList();
        Assert.Empty(candidates);

        var candidateCommands = _commands.ReaderCommands.Where(command =>
            command.Contains("FROM \"Notes\"", StringComparison.Ordinal) &&
            command.Contains("JOIN \"People\"", StringComparison.Ordinal) &&
            command.Contains("JOIN \"Users\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, candidateCommands.Count);
        Assert.All(candidateCommands, command =>
            Assert.Contains("AgencyId", command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BillingOverviewAggregatesSixMonthsAndEveryDraftWithoutReadingPeriodHistory()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var foreignPerson = Person.CreatePerson(
                _foreignAdmin.Id,
                "Foreign",
                "Overview",
                string.Empty,
                new DateTime(1990, 1, 1),
                effective: null,
                WaiverType.Section21,
                new Settings());
            foreignPerson.AgencyId = _foreignAdmin.AgencyId;
            foreignPerson.Gender = Gender.Unknown;
            db.People.Add(foreignPerson);
            await db.SaveChangesAsync();

            var noteFacts = new[]
            {
                (_personId, _admin.AgencyId, new DateTime(2026, 4, 2)),
                (_personId, _admin.AgencyId, new DateTime(2026, 9, 2)),
                (_personId, _admin.AgencyId, new DateTime(2026, 3, 2)),
                (_personId, _admin.AgencyId, new DateTime(2026, 10, 2)),
                (foreignPerson.Id, _foreignAdmin.AgencyId, new DateTime(2026, 9, 3))
            };
            var notes = noteFacts.Select((fact, index) =>
            {
                var note = Note.Create(
                    $"Overview source {index + 1}",
                    fact.Item3,
                    NoteStatus.Approved,
                    15,
                    fact.Item1);
                note.AgencyId = fact.Item2;
                return note;
            }).ToArray();
            db.Notes.AddRange(notes);
            await db.SaveChangesAsync();

            db.BillingPeriods.AddRange(
                Period(_caseManager.Id, 2026, 4, BillingStatus.Accepted, notes[0], 20m),
                Period(_caseManager.Id, 2026, 9, BillingStatus.Draft, notes[1], 10m),
                Period(_caseManager.Id, 2026, 3, BillingStatus.Draft, notes[2], 1000m),
                Period(_caseManager.Id, 2026, 10, BillingStatus.Draft, notes[3], 200m),
                Period(_foreignAdmin.Id, 2026, 9, BillingStatus.Draft, notes[4], 999m));
            await db.SaveChangesAsync();
        }

        _commands.Clear();
        var overview = await ServiceFor(_admin).GetBillingPeriodOverviewAsync(
            _admin.ToAgencyActor(),
            new DateTime(2026, 9, 24));

        Assert.Equal(1210m, overview.DraftRevenue);
        Assert.Equal(
            [202604, 202605, 202606, 202607, 202608, 202609],
            overview.Months.Select(month => month.Year * 100 + month.Month));
        Assert.Equal([20m, 0m, 0m, 0m, 0m, 10m],
            overview.Months.Select(month => month.BilledAmount));
        Assert.DoesNotContain(overview.Months, month => month.BilledAmount == 999m);

        var aggregateCommands = _commands.ReaderCommands
            .Where(command =>
                command.Contains("ClaimLines", StringComparison.Ordinal)
                && command.Contains("BillingPeriods", StringComparison.Ordinal)
                && command.Contains("Users", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, aggregateCommands.Length);
        Assert.Contains(aggregateCommands, command => command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.All(aggregateCommands, command =>
        {
            Assert.DoesNotContain("Narrative", command, StringComparison.Ordinal);
            Assert.DoesNotContain("ClaimSnapshotJson", command, StringComparison.Ordinal);
            Assert.DoesNotContain("NoteId", command, StringComparison.Ordinal);
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ServiceFor(_caseManager).GetBillingPeriodOverviewAsync(
                _caseManager.ToAgencyActor(),
                new DateTime(2026, 9, 24)));
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

    [Fact]
    public async Task LaterBackdatedBlockerRequiresAndAllowsASecondImmutableRecovery()
    {
        var service = ServiceFor(_admin);
        await service.RecordComplianceRecoveryAsync(
            _admin.ToAgencyActor(),
            _personId,
            new CreateBillingComplianceRecoveryRequest(
                [_noteIds[0]],
                "The original PCP gap is complete.",
                AttestationConfirmed: true));

        await using (var db = _factory.CreateDbContext())
        {
            var assessment = new Form(
                FormType.ComprehensiveAssessment,
                new DateTime(2010, 7, 30),
                targetEffectiveDate: new DateTime(2010, 10, 28))
            {
                PersonId = _personId
            };
            assessment.Attest(FormAttestation.Attested(
                new DateTime(2010, 8, 7),
                AttestationActorKind.CaseManager,
                _caseManager.Id,
                new DateTime(2010, 8, 8, 12, 0, 0, DateTimeKind.Utc)));
            db.Forms.Add(assessment);
            await db.SaveChangesAsync();
        }

        var secondPlan = await service.PrepareComplianceRecoveryAsync(
            _admin.ToAgencyActor(), _personId);
        var note = Assert.Single(secondPlan.NoteOptions,
            option => option.NoteId == _noteIds[0]);
        Assert.Equal(2, note.BlockingObligationIds.Count);

        await service.RecordComplianceRecoveryAsync(
            _admin.ToAgencyActor(),
            _personId,
            new CreateBillingComplianceRecoveryRequest(
                [_noteIds[0]],
                "A corrected assessment record exposed a second historical gap.",
                AttestationConfirmed: true));

        await using var verification = _factory.CreateDbContext();
        Assert.Equal(2, await verification.BillingComplianceRecoveryDecisions
            .CountAsync(decision => decision.PersonId == _personId));
        Assert.Equal(2, await verification.BillingComplianceRecoveryNotes
            .CountAsync(item => item.NoteId == _noteIds[0]));
    }

    private async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
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

    private static BillingPeriod Period(
        int userId,
        int year,
        int month,
        BillingStatus status,
        Note note,
        decimal charge) => new()
    {
        UserId = userId,
        Year = year,
        Month = month,
        Status = status,
        Lines =
        [
            new ClaimLine
            {
                NoteId = note.Id,
                DateOfService = note.EventDate ?? new DateTime(year, month, 1),
                ChargeAmount = charge
            }
        ]
    };

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly List<string> _readerCommands = [];
        public IReadOnlyList<string> ReaderCommands
        {
            get
            {
                lock (_readerCommands)
                    return _readerCommands.ToArray();
            }
        }

        public void Clear()
        {
            lock (_readerCommands)
                _readerCommands.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            lock (_readerCommands)
                _readerCommands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
