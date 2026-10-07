using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

public sealed class RecordsGovernanceTests
{
    private static readonly AgencyActor Admin = new(1, 1, UserPermissions.Administration);
    private static readonly AgencyActor Reviewer = new(2, 1, UserPermissions.Administration);
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AReleaseRemainsActiveUntilAnIndependentExactRevisionDecision()
    {
        await using var fixture = await Fixture.Create();
        var placed = await fixture.Hold(Place());
        var request = Change(placed, GovernanceHoldAction.RequestRelease);
        var pending = await fixture.Hold(request);
        Assert.False(pending.IsReleased);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Hold(Change(pending, GovernanceHoldAction.ApproveRelease)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Hold(Change(placed, GovernanceHoldAction.ApproveRelease), Reviewer));
        var approve = Change(pending, GovernanceHoldAction.ApproveRelease);
        var released = await fixture.Hold(approve, Reviewer);
        Assert.True(released.IsReleased);
        Assert.Equal(3, released.History.Count);
        Assert.Equal(released, await fixture.Hold(approve, Reviewer), DtoEquality.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Hold(approve with { OperationId = Guid.NewGuid() }, Reviewer));
        await Assert.ThrowsAsync<RecordsGovernanceConflictException>(() => fixture.Hold(approve with { Reason = "Changed decision" }, Reviewer));
        await fixture.Read(db => Assert.Equal(3, db.Set<RecordsHoldEvent>().Count()));
    }

    [Fact]
    public async Task PlacerCannotApproveADifferentAdminsRequestAndAmendmentInvalidatesPendingDecision()
    {
        await using var fixture = await Fixture.Create();
        var placed = await fixture.Hold(Place());
        var pending = await fixture.Hold(Change(placed, GovernanceHoldAction.RequestRelease), Reviewer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Hold(Change(pending, GovernanceHoldAction.ApproveRelease)));
        var amended = await fixture.Hold(Change(pending, GovernanceHoldAction.Amend));
        Assert.Null(amended.ReleaseRequestedById);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Hold(Change(amended, GovernanceHoldAction.ApproveRelease), Reviewer));
    }

    [Fact]
    public async Task ForeignScopeStalePolicyAndNonAdminWritesLeaveNoState()
    {
        await using var fixture = await Fixture.Create();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Hold(Place(), Admin with { Permissions = UserPermissions.CaseManagement }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Hold(Place() with { Scope = PreservationScope.Person, PersonId = 99 }));
        var own = await fixture.Hold(Place());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Hold(Change(own, GovernanceHoldAction.Amend), new(3, 2, UserPermissions.Administration)));
        var policy = await fixture.Policy();
        await Assert.ThrowsAsync<RecordsGovernanceConflictException>(() => fixture.Policy());
        await fixture.Read(db => { Assert.Single(db.Set<RecordsRetentionPolicy>()); Assert.Single(db.Set<RecordsHoldEvent>()); });
        var request = new RetentionPolicyRequest(Guid.NewGuid(), RetentionRecordClass.Clinical, policy.Version, 20, "Updated synthetic proposal");
        var next = await fixture.Policy(request);
        Assert.Equal(2, next.Version);
        await fixture.Read(db => { var first = db.Set<RecordsRetentionPolicy>().Single(x => x.Id == policy.Id); first.Reason = "Overwrite";
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges()); });
    }

    [Fact]
    public void AllSixClassesAndReverseDependenciesPreserveTheConnectedComponent()
    {
        var graph = Enum.GetValues<RetentionRecordClass>().Select((type, index) => new RetentionCandidate(
            new(type, type.ToString(), 7, Now.AddYears(-10), "synthetic"), index == 0 ? [] : [new((RetentionRecordClass)(index - 1), ((RetentionRecordClass)(index - 1)).ToString())])).ToArray();
        var records = graph.ToDictionary(x => (x.Record.RecordClass, x.Record.RecordId));
        var held = new GovernanceHoldDto(Guid.NewGuid(), 1, PreservationScope.Record, RetentionRecordClass.Clinical, 7,
            "Clinical", false, 1, null, []);
        foreach (var candidate in graph) Assert.Equal(LegalHoldStatus.Active, RecordsGovernanceRules.Evaluate(candidate, records, [held], new HashSet<int>()));
        foreach (var candidate in graph) Assert.Equal(LegalHoldStatus.Active, RecordsGovernanceRules.Evaluate(candidate, records, [], new HashSet<int> { 7 }));
        var missing = graph[0] with { Dependencies = [new(RetentionRecordClass.Documents, "missing-store")] };
        records[(missing.Record.RecordClass, missing.Record.RecordId)] = missing;
        foreach (var candidate in records.Values) Assert.Equal(LegalHoldStatus.Unavailable, RecordsGovernanceRules.Evaluate(candidate, records, [], new HashSet<int>()));
    }

    [Fact]
    public async Task UnavailableInventoryHasUnknownCountsAndRuntimeCannotExecute()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy();
        var preview = await fixture.Preview(policy.Id, new UnavailableRecordsRetentionStore());
        Assert.Null(preview.CandidateCount); Assert.Null(preview.HeldCount); Assert.Null(preview.OldestUtc);
        Assert.False(preview.CanExecute); Assert.Contains("dependency_inventory_unavailable_or_incomplete", preview.Blockers);
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, new Store(), RetentionExecutionMode.PolicyOnly);
        Assert.Contains("runtime_policy_only", result.Blockers);
    }
    [Fact]
    public async Task AClaimedFixtureCannotPurgeADiskDatabase()
    {
        await using var fixture = await Fixture.Create(onDisk: true); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store);
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.Contains("runtime_policy_only", result.Blockers); await fixture.Read(db => Assert.Single(db.Records));
    }

    [Fact]
    public async Task PreviewShowsPolicyDatesExclusionsAndDependencies()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(3);
        await fixture.Hold(Place() with { Scope = PreservationScope.Record, RecordClass = RetentionRecordClass.Clinical, RecordId = "record-1" });
        var preview = await fixture.Preview(policy.Id, new Store());
        Assert.Equal(3, preview.CandidateCount); Assert.Equal(1, preview.HeldCount); Assert.Equal(2, preview.ClearCount);
        Assert.Equal(0, preview.UnavailableCount); Assert.Equal(0, preview.DependencyCount);
        Assert.Equal(Now.AddYears(-10), preview.OldestUtc); Assert.Equal(policy.Version, preview.PolicyVersion);
        Assert.False(preview.CanExecute);
    }

    [Fact]
    public async Task NewHoldDefeatsAPreparedPlanEvenWhenItTargetsAnUnrelatedClass()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store);
        await fixture.Hold(Place() with { RecordClass = RetentionRecordClass.Audit });
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.Contains("governance_changed_reprepare", result.Blockers);
        await fixture.Read(db => Assert.Single(db.Records));
    }

    [Fact]
    public async Task ChangedPolicyDefeatsAPreparedPlan()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store);
        await fixture.Policy(new(Guid.NewGuid(), RetentionRecordClass.Clinical, 1, 100, "Changed synthetic period"));
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.NotEmpty(result.Blockers); await fixture.Read(db => Assert.Single(db.Records));
    }

    [Theory]
    [InlineData("backup")][InlineData("object")][InlineData("recovery")][InlineData("reference")][InlineData("plan")][InlineData("epoch")]
    public async Task MissingOrMisboundPreservationEvidenceBlocksDestruction(string failure)
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store { ReceiptFailure = failure }; var preview = await fixture.Preview(policy.Id, store);
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.Contains("backup_object_recovery_evidence_unavailable", result.Blockers); await fixture.Read(db => Assert.Single(db.Records));
    }

    [Fact]
    public async Task BatchesAreBoundedAndExactRetriesDoNotDeleteExtraRecords()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(53);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store); var operation = Guid.NewGuid();
        var first = await fixture.Execute(preview.Id, operation, 0, store);
        Assert.Equal(50, first.DeletedCount); Assert.False(first.Completed);
        Assert.Equal(first, await fixture.Execute(preview.Id, operation, 0, store));
        await Assert.ThrowsAsync<RecordsGovernanceConflictException>(() => fixture.Execute(preview.Id, operation, 1, store));
        var last = await fixture.Execute(preview.Id, Guid.NewGuid(), 50, store);
        Assert.Equal(3, last.DeletedCount); Assert.True(last.Completed);
        Assert.Equal(first, await fixture.Execute(preview.Id, operation, 0, store));
        await fixture.Read(db => { Assert.Empty(db.Records); Assert.Equal(2, db.Set<RecordsRetentionBatch>().Count()); });
    }

    [Fact]
    public async Task FailedBatchRollsBackDeletionAndCheckpointThenRetriesExactly()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(3);
        var store = new Store { FailAfterDelete = true }; var preview = await fixture.Preview(policy.Id, store); var operation = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Execute(preview.Id, operation, 0, store));
        await fixture.Read(db => { Assert.Equal(3, db.Records.Count()); Assert.Empty(db.Set<RecordsRetentionBatch>());
            Assert.Equal(0, db.Set<RecordsRetentionPlan>().Single().Checkpoint); });
        store.FailAfterDelete = false;
        Assert.Equal(3, (await fixture.Execute(preview.Id, operation, 0, store)).DeletedCount);
        Assert.Equal(3, (await fixture.Execute(preview.Id, operation, 0, store)).DeletedCount);
    }

    [Fact]
    public async Task MissingDependencyOrChangedFingerprintBlocksAPreparedBatch()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store);
        await fixture.Read(async db => { db.Records.Single().Fingerprint = "new-synthetic-revision"; await db.SaveChangesAsync(); });
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.Contains("record_changed_or_preserved", result.Blockers); await fixture.Read(db => Assert.Single(db.Records));
        store.MissingDependency = true;
        var unavailable = await fixture.Preview(policy.Id, store);
        Assert.Equal(1, unavailable.UnavailableCount); Assert.Equal(0, unavailable.ClearCount);
        Assert.Contains("required_dependency_unavailable", unavailable.Blockers);
    }
    [Fact]
    public async Task ANewLinkedCopyDefeatsAnOtherwiseUnchangedPreparedPlan()
    {
        await using var fixture = await Fixture.Create(); var policy = await fixture.Policy(); await fixture.SeedRecords(1);
        var store = new Store(); var preview = await fixture.Preview(policy.Id, store);
        store.NewLinkedCopy = true;
        var result = await fixture.Execute(preview.Id, Guid.NewGuid(), 0, store);
        Assert.Contains("record_changed_or_preserved", result.Blockers); await fixture.Read(db => Assert.Single(db.Records));
    }

    private static GovernanceHoldRequest Place() => new(Guid.NewGuid(), GovernanceHoldAction.Place, null, 0,
        PreservationScope.Agency, null, null, null, "Synthetic preservation reason");
    private static GovernanceHoldRequest Change(GovernanceHoldDto hold, GovernanceHoldAction action) => new(Guid.NewGuid(), action,
        hold.Id, hold.Revision, hold.Scope, hold.RecordClass, hold.PersonId, hold.RecordId, "Synthetic decision reason");
    private sealed class DtoEquality : IEqualityComparer<GovernanceHoldDto>
    { public static DtoEquality Instance { get; } = new(); public bool Equals(GovernanceHoldDto? x, GovernanceHoldDto? y) =>
        System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y); public int GetHashCode(GovernanceHoldDto obj) => obj.Id.GetHashCode(); }

    private sealed class Fixture(SqliteConnection connection, string? ownedFile) : IAsyncDisposable
    {
        private Db NewDb() => new(new DbContextOptionsBuilder<Db>().UseSqlite(connection).Options);
        public static async Task<Fixture> Create(bool onDisk = false)
        { var path = onDisk ? Path.Combine(Path.GetTempPath(), $"SatiRetentionTest_{Guid.NewGuid():N}.db") : null;
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path ?? ":memory:", Pooling = false }.ConnectionString);
            await connection.OpenAsync(); var fixture = new Fixture(connection, path);
            await using var db = fixture.NewDb(); await db.Database.EnsureCreatedAsync();
            db.AddRange(new TestAgency { Id = 1 }, new TestAgency { Id = 2 });
            db.AddRange(new TestUser { Id = 1 }, new TestUser { Id = 2 }, new TestUser { Id = 3 }); await db.SaveChangesAsync(); return fixture; }
        public async Task<T> Write<T>(Func<Db, Task<T>> action)
        { await using var db = NewDb(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await action(db); await tx.CommitAsync(); return result; }
        public async Task Read(Action<Db> action) { await using var db = NewDb(); action(db); }
        public async Task Read(Func<Db, Task> action) { await using var db = NewDb(); await action(db); }
        public Task<GovernanceHoldDto> Hold(GovernanceHoldRequest request, AgencyActor? actor = null) => Write(db =>
            RecordsGovernanceWorkflow.HoldAsync(db, actor ?? Admin, request, person => Task.FromResult(person == 7), (_, _, _) => { }, Now));
        public Task<RetentionPolicyDto> Policy(RetentionPolicyRequest? request = null) => Write(db => RecordsGovernanceWorkflow.PolicyAsync(db,
            Admin, request ?? new(Guid.NewGuid(), RetentionRecordClass.Clinical, 0, 30, "Synthetic proposal; not approved for runtime"), (_, _) => { }, Now));
        public Task<RetentionPreviewDto> Preview(long id, IRecordsRetentionStore store) => Write(db =>
            RecordsRetentionWorkflow.PreviewAsync(db, Admin, id, store, new HashSet<int>(), Now));
        public Task<RetentionBatchResult> Execute(Guid id, Guid operation, int checkpoint, IRecordsRetentionStore store, RetentionExecutionMode mode = RetentionExecutionMode.SyntheticFixture) =>
            Write(db => RecordsRetentionWorkflow.ExecuteBatchAsync(db, Admin, id, operation, checkpoint, store, new HashSet<int>(), mode, true, Now));
        public async Task SeedRecords(int count) => await Read(async db => { for (var n = 1; n <= count; n++) db.Records.Add(new TestRecord { Id = $"record-{n}", Fingerprint = "synthetic-v1" }); await db.SaveChangesAsync(); });
        public async ValueTask DisposeAsync()
        { await connection.DisposeAsync(); if (ownedFile is not null) File.Delete(ownedFile); }
    }
    private sealed class TestAgency { public int Id { get; set; } }
    private sealed class TestUser { public int Id { get; set; } }
    private sealed class TestRecord { public string Id { get; set; } = ""; public string Fingerprint { get; set; } = ""; }
    private sealed class Db(DbContextOptions<Db> options) : DbContext(options)
    {
        public DbSet<TestRecord> Records => Set<TestRecord>();
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<TestAgency>().HasKey(x => x.Id); model.Entity<TestUser>().HasKey(x => x.Id);
            model.Entity<TestRecord>().HasKey(x => x.Id); RecordsGovernanceModel.Configure<TestAgency, TestUser>(model); }
        public override int SaveChanges(bool acceptAllChangesOnSuccess) { RecordsGovernanceModel.Validate(ChangeTracker); return base.SaveChanges(acceptAllChangesOnSuccess); }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default) { RecordsGovernanceModel.Validate(ChangeTracker); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
    }
    private sealed class Store : IRecordsRetentionStore
    {
        public bool FailAfterDelete { get; set; }
        public bool MissingDependency { get; set; }
        public bool NewLinkedCopy { get; set; }
        public string? ReceiptFailure { get; init; }
        public async Task<RetentionStoreInventory> InventoryAsync(DbContext db, int agencyId, CancellationToken ct)
        {
            var records = (await db.Set<TestRecord>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => new RetentionCandidate(
                new(RetentionRecordClass.Clinical, x.Id, 7, Now.AddYears(-10), x.Fingerprint), MissingDependency ? [new(RetentionRecordClass.Documents, "missing")] : [])).ToList();
            if (NewLinkedCopy) records.Add(new(new(RetentionRecordClass.Documents, "new-linked-copy", 7, Now, "new-copy-v1"), [new(RetentionRecordClass.Clinical, "record-1")]));
            return new(true, true, records, []);
        }
        public async Task<PreservationReceipt?> PreservationAsync(DbContext db, Guid planId, CancellationToken ct)
        { var plan = await db.Set<RecordsRetentionPlan>().SingleAsync(x => x.Id == planId, ct);
            var epoch = await db.Set<RecordsGovernanceState>().Where(x => x.AgencyId == plan.AgencyId).Select(x => x.Revision).SingleAsync(ct);
            return new(ReceiptFailure == "plan" ? Guid.NewGuid() : planId, epoch + (ReceiptFailure == "epoch" ? 1 : 0), "private-fixture",
                ReceiptFailure == "reference" ? "unprotected-secret-location" : Guid.NewGuid().ToString(), new string('A', 64), Now,
                ReceiptFailure != "backup", ReceiptFailure != "object", ReceiptFailure != "recovery"); }
        public async Task DeleteAsync(DbContext db, IReadOnlyList<RetentionCandidate> candidates, CancellationToken ct)
        { var ids = candidates.Select(x => x.Record.RecordId).ToArray(); db.RemoveRange(await db.Set<TestRecord>().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct));
            await db.SaveChangesAsync(ct); if (FailAfterDelete) throw new InvalidOperationException("Synthetic adapter failed after transactional deletion."); }
    }
}
