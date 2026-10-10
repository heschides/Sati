using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

// Independent simulated owner incarnations share ONE model. No database/service calls or timers.
public sealed class SharedWorkloadBudgetCandidateTests
{
    private const string Target = "synthetic-demo/sql";
    private readonly Guid epoch = Guid.NewGuid();
    private BudgetCandidateProfile Profile(int daily = 12, int recovery = 8, int background = 8,
        int validation = 4, int agencyCap = 4, int maxRecords = 128, int maxChildren = 16) =>
        new(Target, epoch, daily, recovery, background, validation, agencyCap, maxRecords, maxChildren);
    private BudgetRequest Request(BudgetOperation operation = BudgetOperation.DailyRead, int agency = 1) =>
        new(Target, epoch, Guid.NewGuid(), Guid.NewGuid(), operation, agency);
    private static BudgetGrant Admit(SharedWorkloadBudgetCandidate model, BudgetRequest request)
    {
        var result = model.TryAdmit(request);
        Assert.Equal(BudgetAdmission.Granted, result.Status);
        return Assert.IsType<BudgetGrant>(result.Grant);
    }

    [Theory]
    [InlineData(6, 4, 4, 2)]
    [InlineData(12, 8, 8, 4)]
    [InlineData(24, 16, 16, 8)]
    public async Task OverlappingOwnersShareOneAggregateEnvelope(int daily, int recovery, int background, int validation)
    {
        var profile = Profile(daily, recovery, background, validation);
        var model = new SharedWorkloadBudgetCandidate(profile);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenders = Enumerable.Range(1, 64).Select(i => Task.Run(async () =>
        {
            await start.Task;
            return model.TryAdmit(Request(BudgetOperation.DailyWrite, i));
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(contenders);
        Assert.Equal(daily / 2, results.Count(x => x.Status == BudgetAdmission.Granted));
        Assert.All(results.Where(x => x.Grant == null), x => Assert.Equal(BudgetAdmission.Busy, x.Status));
        Assert.Equal(daily, model.Snapshot().Charged);
        Assert.Equal(daily, model.Snapshot().Daily);
        Assert.Equal(daily / 2, model.Snapshot().Records);
        foreach (var grant in results.Where(x => x.Grant != null).Select(x => x.Grant!))
            Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
        Assert.Equal(0, model.Snapshot().Charged);
    }

    [Fact]
    public void SaturatedBackgroundLeavesDailyRecoveryAndValidationSharesAvailable()
    {
        var profile = Profile();
        var model = new SharedWorkloadBudgetCandidate(profile);
        Admit(model, Request(BudgetOperation.Export, 1));
        Admit(model, Request(BudgetOperation.Dispatch, 2));
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.NoteSweep, 3)).Status);
        for (var i = 1; i <= 6; i++) Admit(model, Request(BudgetOperation.DailyWrite, i));
        for (var i = 0; i < 2; i++) Admit(model, Request(BudgetOperation.Reset, 0));
        for (var i = 0; i < 4; i++) Admit(model, Request(BudgetOperation.ValidateIdentity, 0));
        var snapshot = model.Snapshot();
        Assert.Equal(profile.Total, snapshot.Charged);
        Assert.Equal((12, 8, 8, 4), (snapshot.Daily, snapshot.Recovery, snapshot.Background, snapshot.Validation));
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.DailyRead, 99)).Status);
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.Health, 0)).Status);
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.ValidateIdentity, 0)).Status);
    }

    [Fact]
    public void AgencyCannotOccupyWholeClassAndCannotBorrowUnusedShares()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var first = Admit(model, Request(BudgetOperation.DailyWrite, 1));
        Admit(model, Request(BudgetOperation.DailyWrite, 1));
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.DailyRead, 1)).Status);
        Admit(model, Request(BudgetOperation.DailyWrite, 2));
        Admit(model, Request(BudgetOperation.Export, 1)); // agency shares are explicitly per class
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.Signature, 1)).Status);
        Assert.Equal(4, model.AgencyUsage(BudgetClass.Daily, 1));
        Assert.Equal(4, model.AgencyUsage(BudgetClass.Background, 1));
        Assert.Equal(BudgetChange.Confirmed, model.Release(first));
        Admit(model, Request(BudgetOperation.DailyRead, 1));
        Assert.Equal(3, model.AgencyUsage(BudgetClass.Daily, 1));
    }

    [Theory]
    [InlineData(BudgetOperation.DailyRead, 1, BudgetClass.Daily, 1)]
    [InlineData(BudgetOperation.DailyWrite, 1, BudgetClass.Daily, 2)]
    [InlineData(BudgetOperation.Export, 1, BudgetClass.Background, 4)]
    [InlineData(BudgetOperation.Dispatch, 1, BudgetClass.Background, 4)]
    [InlineData(BudgetOperation.Poll, 1, BudgetClass.Background, 3)]
    [InlineData(BudgetOperation.NoteSweep, 1, BudgetClass.Background, 2)]
    [InlineData(BudgetOperation.Signature, 1, BudgetClass.Background, 2)]
    [InlineData(BudgetOperation.Reset, 0, BudgetClass.Recovery, 4)]
    [InlineData(BudgetOperation.RecoveryRepair, 1, BudgetClass.Recovery, 2)]
    [InlineData(BudgetOperation.Health, 0, BudgetClass.Recovery, 1)]
    [InlineData(BudgetOperation.ValidateIdentity, 0, BudgetClass.Validation, 1)]
    public void CatalogueOwnsClassAndWholeOperationCost(object operationCase, int agency, object classCase, int cost)
    {
        var operation = (BudgetOperation)operationCase;
        var workClass = (BudgetClass)classCase;
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var grant = Admit(model, Request(operation, agency));
        Assert.Equal(cost, model.Snapshot().Charged);
        var snapshot = model.Snapshot();
        Assert.Equal(cost, workClass switch
        {
            BudgetClass.Daily => snapshot.Daily, BudgetClass.Recovery => snapshot.Recovery,
            BudgetClass.Background => snapshot.Background, _ => snapshot.Validation
        });
        Assert.Equal(cost, model.AgencyUsage(workClass, agency));
        var children = Enumerable.Range(0, cost).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var child in children) Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        Assert.Equal(BudgetChange.LimitExceeded, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.ChildrenOpen, model.Release(grant));
        foreach (var child in children) Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
        Assert.Equal(cost, model.Snapshot().Charged); // inner close does not surrender outer reservation
        Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
        Assert.Equal(0, model.AgencyUsage(workClass, agency));
        Assert.Equal(0, model.Snapshot().Charged);
    }

    [Fact]
    public async Task LostGrantResponseReplaysExactlyOnceAcrossConcurrentRequests()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request(BudgetOperation.Export);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
        {
            await start.Task; return model.TryAdmit(request);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(calls);
        Assert.Single(results, x => x.Status == BudgetAdmission.Granted);
        Assert.Equal(39, results.Count(x => x.Status == BudgetAdmission.ReplayHeld));
        Assert.Single(results.Select(x => x.Grant).Distinct());
        Assert.Equal(4, model.Snapshot().Charged);
        Assert.Equal(1, model.Snapshot().Records);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("operation")]
    [InlineData("agency")]
    public void SameRequestIdWithDifferentFingerprintCannotTakeOrReleaseCredit(string changed)
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request();
        var grant = Admit(model, request);
        var conflict = changed switch
        {
            "owner" => request with { Incarnation = Guid.NewGuid() },
            "operation" => request with { Operation = BudgetOperation.DailyWrite },
            _ => request with { AgencyId = 2 }
        };
        Assert.Equal(BudgetAdmission.Conflict, model.TryAdmit(conflict).Status);
        Assert.Null(model.TryAdmit(conflict).Grant);
        Assert.Equal(BudgetChange.InvalidGrant, model.Release(grant with { Incarnation = Guid.NewGuid() }));
        Assert.Equal(1, model.Snapshot().Charged);
        Assert.Equal(1, model.Snapshot().Records);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("epoch")]
    [InlineData("request")]
    [InlineData("owner")]
    [InlineData("operation")]
    [InlineData("agency-zero")]
    [InlineData("agency-negative")]
    [InlineData("global-agency")]
    public void InvalidScopeHasNoAdmissionOrMetadataEffects(string changed)
    {
        var request = Request();
        request = changed switch
        {
            "target" => request with { Target = "other/sql" },
            "epoch" => request with { Epoch = Guid.NewGuid() },
            "request" => request with { RequestId = Guid.Empty },
            "owner" => request with { Incarnation = Guid.Empty },
            "operation" => request with { Operation = (BudgetOperation)999 },
            "agency-zero" => request with { AgencyId = 0 },
            "agency-negative" => request with { AgencyId = -1 },
            _ => request with { Operation = BudgetOperation.ValidateIdentity, AgencyId = 1 }
        };
        var model = new SharedWorkloadBudgetCandidate(Profile());
        Assert.Equal(BudgetAdmission.Invalid, model.TryAdmit(request).Status);
        Assert.Equal(0, model.Snapshot().Records);
        Assert.Equal(0, model.Snapshot().Charged);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("epoch")]
    [InlineData("nonce")]
    [InlineData("request")]
    public void StaleOrForgedGrantCannotChangeAnyReservation(string changed)
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var grant = Admit(model, Request());
        var child = Guid.NewGuid();
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        var stale = changed switch
        {
            "owner" => grant with { Incarnation = Guid.NewGuid() },
            "epoch" => grant with { Epoch = Guid.NewGuid() },
            "nonce" => grant with { Nonce = Guid.NewGuid() },
            _ => grant with { RequestId = Guid.NewGuid() }
        };
        var before = model.Snapshot();
        Assert.Equal(BudgetChange.InvalidGrant, model.RegisterChildOpen(stale, Guid.NewGuid()));
        Assert.Equal(BudgetChange.InvalidGrant, model.ConfirmChildClosed(stale, child));
        Assert.Equal(BudgetChange.InvalidGrant, model.MarkUncertain(stale));
        Assert.Equal(BudgetChange.InvalidGrant, model.Release(stale));
        Assert.Equal(before, model.Snapshot());
    }

    [Fact]
    public void CrashAndReplacementOwnerCannotReclaimUncertainDebtEvenAfterKnownChildrenClose()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request(BudgetOperation.Export);
        var grant = Admit(model, request);
        var child = Guid.NewGuid();
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        Assert.Equal(BudgetChange.Confirmed, model.MarkUncertain(grant));
        Assert.Equal(BudgetChange.Uncertain, model.Release(grant));
        Assert.Equal(BudgetChange.Uncertain, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.Uncertain, model.Release(grant));
        Assert.Equal(BudgetAdmission.ReplayUncertain, model.TryAdmit(request).Status);
        Assert.Equal(BudgetAdmission.Conflict, model.TryAdmit(request with { Incarnation = Guid.NewGuid() }).Status);
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.Export, 1)).Status);
        Admit(model, Request(BudgetOperation.Export, 2));
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.Export, 3)).Status);
        Assert.Equal(8, model.Snapshot().Charged);
        Assert.Equal(1, model.Snapshot().UncertainRecords);
        // No clock/TTL/replacement-controller API exists to erase this retained debt.
    }

    [Fact]
    public void UncertaintyWithNoOutstandingChildrenStillHoldsWholeReservation()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request(BudgetOperation.Export);
        var grant = Admit(model, request);
        var child = Guid.NewGuid();
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.Confirmed, model.MarkUncertain(grant));
        Assert.Equal(BudgetChange.Uncertain, model.Release(grant));
        Assert.Equal(4, model.Snapshot().Charged);
        Assert.Equal(0, model.Snapshot().OpenChildren);
        Assert.Equal(BudgetAdmission.ReplayUncertain, model.TryAdmit(request).Status);
        Assert.Equal(BudgetAdmission.Busy, model.TryAdmit(Request(BudgetOperation.Export, 1)).Status);
    }

    [Fact]
    public void FailedOpenRemainsDebitedUntilPositiveNeverOpenedOrClosureEvidence()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var grant = Admit(model, Request());
        var attempt = Guid.NewGuid();
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, attempt));
        // Simulated failed/cancelled provider open: an exception alone is no closure observation.
        Assert.Equal(BudgetChange.ChildrenOpen, model.Release(grant));
        Assert.Equal(BudgetChange.LimitExceeded, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.InvalidChild, model.ConfirmChildClosed(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, attempt));
        Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
    }

    [Fact]
    public void OpenAndCloseReplayNeverAuthorizesAnotherOpenOrDoubleRelease()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request();
        var grant = Admit(model, request);
        var child = Guid.NewGuid();
        Assert.Equal(BudgetChange.InvalidChild, model.RegisterChildOpen(grant, Guid.Empty));
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        Assert.Equal(BudgetChange.AlreadyRecorded, model.RegisterChildOpen(grant, child));
        Assert.Equal(1, model.Snapshot().OpenChildren);
        Assert.Equal(BudgetChange.ChildrenOpen, model.Release(grant));
        Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.AlreadyRecorded, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.InvalidChild, model.RegisterChildOpen(grant, child));
        Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
        Assert.Equal(BudgetChange.AlreadyRecorded, model.Release(grant));
        Assert.Equal(BudgetChange.Released, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.Released, model.MarkUncertain(grant));
        Assert.Equal(BudgetAdmission.ReplayReleased, model.TryAdmit(request).Status);
        Assert.Equal(0, model.Snapshot().Charged);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(128)]
    public void RetainedReplayCapacityStopsNewWorkWithoutEvictingTerminalIdentity(int recordLimit)
    {
        var model = new SharedWorkloadBudgetCandidate(Profile(maxRecords: recordLimit));
        var original = Request();
        var first = Admit(model, original);
        Assert.Equal(BudgetChange.Confirmed, model.Release(first));
        for (var i = 1; i < recordLimit; i++)
            Assert.Equal(BudgetChange.Confirmed, model.Release(Admit(model, Request())));
        var before = model.Snapshot();
        for (var i = 0; i < 100; i++)
            Assert.Equal(BudgetAdmission.MetadataFull, model.TryAdmit(Request(agency: i + 1)).Status);
        var replay = model.TryAdmit(original);
        Assert.Equal(BudgetAdmission.ReplayReleased, replay.Status);
        Assert.Equal(first, replay.Grant);
        Assert.Equal(before, model.Snapshot());
        Assert.Equal(recordLimit, before.Records);
        Assert.Equal(0, before.Charged);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(16)]
    public void SequentialChildAttemptsAreBoundedAndClosedIdentityCannotBeReused(int childLimit)
    {
        var model = new SharedWorkloadBudgetCandidate(Profile(maxChildren: childLimit));
        var grant = Admit(model, Request());
        for (var i = 0; i < childLimit; i++)
        {
            var child = Guid.NewGuid();
            Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
            Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
            Assert.Equal(BudgetChange.InvalidChild, model.RegisterChildOpen(grant, child));
        }
        Assert.Equal(BudgetChange.LimitExceeded, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(childLimit, model.Snapshot().ChildRecords);
        Assert.Equal(0, model.Snapshot().OpenChildren);
        Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
    }

    [Fact]
    public void CancelBeforeGrantHasNoEffectsCancelAfterGrantDoesNotImplicitlyFreeCredit()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.TryAdmit(Request(), cancellation.Token));
        Assert.Equal(0, model.Snapshot().Records);
        using var after = new CancellationTokenSource();
        var request = Request();
        var result = model.TryAdmit(request, after.Token);
        Assert.Equal(BudgetAdmission.Granted, result.Status);
        after.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.TryAdmit(request, after.Token));
        Assert.Equal(1, model.Snapshot().Charged);
        Assert.Equal(BudgetChange.Confirmed, model.MarkUncertain(result.Grant!));
        Assert.Equal(BudgetChange.Uncertain, model.Release(result.Grant!));
    }

    [Fact]
    public void CoordinatorOutageRefusesNewOpensAndRetainsDebtUntilClosureCanBeConfirmed()
    {
        var model = new SharedWorkloadBudgetCandidate(Profile());
        var request = Request();
        var grant = Admit(model, request);
        var child = Guid.NewGuid();
        Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
        var before = model.Snapshot();
        model.SetAvailabilityForModel(false);
        Assert.Equal(BudgetAdmission.Unavailable, model.TryAdmit(Request()).Status);
        Assert.Equal(BudgetAdmission.Unavailable, model.TryAdmit(request).Status);
        Assert.Equal(BudgetChange.Unavailable, model.RegisterChildOpen(grant, Guid.NewGuid()));
        Assert.Equal(BudgetChange.Unavailable, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.Unavailable, model.MarkUncertain(grant));
        Assert.Equal(BudgetChange.Unavailable, model.Release(grant));
        Assert.Equal(before, model.Snapshot());
        model.SetAvailabilityForModel(true);
        Assert.Equal(BudgetAdmission.ReplayHeld, model.TryAdmit(request).Status);
        Assert.Equal(BudgetChange.ChildrenOpen, model.Release(grant));
        Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
        Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
    }

    [Theory]
    [InlineData(0, 128, 16)]
    [InlineData(1025, 128, 16)]
    [InlineData(12, 0, 16)]
    [InlineData(12, 4097, 16)]
    [InlineData(12, 128, 0)]
    [InlineData(12, 128, 65)]
    public void MalformedProfileCannotCreateController(int daily, int records, int children) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile(daily: daily, maxRecords: records, maxChildren: children));

    [Fact]
    public void ExactTargetAndEpochAreRequiredForProfile()
    {
        Assert.Throws<ArgumentException>(() => new BudgetCandidateProfile(" ", epoch));
        Assert.Throws<ArgumentException>(() => new BudgetCandidateProfile(new string('x', 129), epoch));
        Assert.Throws<ArgumentException>(() => new BudgetCandidateProfile(Target, Guid.Empty));
        Assert.Throws<ArgumentNullException>(() => new SharedWorkloadBudgetCandidate(null!));
    }

    [Fact]
    public void DefaultMetadataEnvelopeRemainsBoundedAfterEveryRetainedChildAttempt()
    {
        var profile = Profile();
        var model = new SharedWorkloadBudgetCandidate(profile);
        for (var i = 0; i < profile.MaxRecords; i++)
        {
            var grant = Admit(model, Request());
            for (var j = 0; j < profile.MaxChildren; j++)
            {
                var child = Guid.NewGuid();
                Assert.Equal(BudgetChange.Confirmed, model.RegisterChildOpen(grant, child));
                Assert.Equal(BudgetChange.Confirmed, model.ConfirmChildClosed(grant, child));
            }
            Assert.Equal(BudgetChange.Confirmed, model.Release(grant));
        }
        Assert.Equal(BudgetAdmission.MetadataFull, model.TryAdmit(Request()).Status);
        Assert.Equal(128, model.Snapshot().Records);
        Assert.Equal(2048, model.Snapshot().ChildRecords);
        Assert.Equal(0, model.Snapshot().Charged);
        Assert.Equal(0, model.Snapshot().OpenChildren);
    }
}
