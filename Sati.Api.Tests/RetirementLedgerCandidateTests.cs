using System.Collections.Immutable;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

// Bookkeeping only: fake atomic state retained across client objects, not disk/process/backend proof.
public sealed class RetirementLedgerCandidateTests
{
    private readonly RetirementProfile profile = new("synthetic-demo/sql", Guid.NewGuid());
    private static RetirementOwnerHandle Register(RetirementLedgerCandidate client, Guid? incarnation = null)
    {
        var result = client.RegisterOwner(incarnation ?? Guid.NewGuid());
        Assert.Equal(RetirementStatus.Registered, result.Status);
        return Assert.IsType<RetirementOwnerHandle>(result.Owner);
    }
    private static Guid Held(RetirementLedgerCandidate client, RetirementOwnerHandle owner, long sequence)
    {
        var result = client.RecordHeld(owner, sequence, "daily:scope-1");
        Assert.Equal(RetirementStatus.Committed, result.Status);
        return result.Nonce!.Value;
    }

    [Fact]
    public void MoreThan128CompletionsRetireRowsAndOldReplayNeverCreatesNewRecords()
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        for (long sequence = 1; sequence <= 512; sequence++)
        {
            var nonce = Held(client, owner, sequence);
            Assert.Single(store.State.Rows);
            Assert.Equal(RetirementStatus.Committed, client.RecordConfirmedTerminal(owner, sequence, nonce).Status);
            Assert.Empty(store.State.Rows);
        }
        Assert.Equal(512, store.State.Owners[owner.Slot].Floor);
        var before = store.State;
        for (long sequence = 1; sequence <= 512; sequence++)
        {
            Assert.Equal(RetirementStatus.Retired, client.RecordHeld(owner, sequence, "changed-fingerprint").Status);
            Assert.Equal(RetirementStatus.Retired, client.RecordConfirmedTerminal(owner, sequence, Guid.NewGuid()).Status);
        }
        Assert.Same(before, store.State);
        Assert.Single(store.State.Owners);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldOrUncertainHolePreventsUnsafeRetirementAndExcessRows(bool uncertain)
    {
        var bounded = new RetirementProfile(profile.Target, profile.Epoch, records: 3);
        var store = new FakeStore(bounded);
        var client = new RetirementLedgerCandidate(bounded, store);
        var owner = Register(client);
        var one = Held(client, owner, 1);
        var two = Held(client, owner, 2);
        Held(client, owner, 3);
        Assert.Equal(RetirementStatus.Committed, client.RecordConfirmedTerminal(owner, 2, two).Status);
        Assert.Equal(0, store.State.Owners[owner.Slot].Floor);
        Assert.Equal(3, store.State.Rows.Count);
        Assert.Equal(RetirementStatus.ReplayTerminal, client.RecordHeld(owner, 2, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.Full, client.RecordHeld(owner, 4, "daily:scope-1").Status);
        if (uncertain)
        {
            Assert.Equal(RetirementStatus.Committed, client.RecordUncertain(owner, 1, one).Status);
            Assert.Equal(RetirementStatus.ReplayUncertain, client.RecordConfirmedTerminal(owner, 1, one).Status);
            Assert.Equal(0, store.State.Owners[owner.Slot].Floor);
            Assert.Equal(3, store.State.Rows.Count);
        }
        else
        {
            Assert.Equal(RetirementStatus.Committed, client.RecordConfirmedTerminal(owner, 1, one).Status);
            Assert.Equal(2, store.State.Owners[owner.Slot].Floor);
            Assert.Single(store.State.Rows);
            Held(client, owner, 4);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostCommitResponseNeverReturnsNonceAndExactRetryFindsAtMostOneRecord(bool committed)
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        store.NextFault = committed ? Fault.AfterCommit : Fault.BeforeCommit;
        var unknown = client.RecordHeld(owner, 1, "daily:scope-1");
        Assert.Equal(RetirementStatus.Unavailable, unknown.Status);
        Assert.Null(unknown.Nonce);
        Assert.Equal(committed ? 1 : 0, store.State.Rows.Count);
        var replay = client.RecordHeld(owner, 1, "daily:scope-1");
        Assert.Equal(committed ? RetirementStatus.ReplayHeld : RetirementStatus.Committed, replay.Status);
        Assert.Single(store.State.Rows);
        Assert.Equal(replay.Nonce, store.State.Rows.Single().Value.Nonce);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousTerminalCommitDoesNotLoseSafetyAcrossClientRestart(bool committed)
    {
        var store = new FakeStore(profile);
        var first = new RetirementLedgerCandidate(profile, store);
        var owner = Register(first);
        var nonce = Held(first, owner, 1);
        store.NextFault = committed ? Fault.AfterCommit : Fault.BeforeCommit;
        Assert.Equal(RetirementStatus.Unavailable, first.RecordConfirmedTerminal(owner, 1, nonce).Status);
        var restarted = new RetirementLedgerCandidate(profile, store);
        Assert.Equal(committed ? RetirementStatus.Retired : RetirementStatus.ReplayHeld,
            restarted.RecordHeld(owner, 1, "daily:scope-1").Status);
        Assert.Equal(committed ? 0 : 1, store.State.Rows.Count);
        Assert.Equal(committed ? 1 : 0, store.State.Owners[owner.Slot].Floor);
    }

    [Fact]
    public void StoreConflictCannotReturnPositiveResultOrConsumeSequence()
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        var before = store.State;
        store.NextFault = Fault.Conflict;
        var conflict = client.RecordHeld(owner, 1, "daily:scope-1");
        Assert.Equal(RetirementStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Nonce);
        Assert.Same(before, store.State);
        Assert.Equal(RetirementStatus.Ready, client.PeekNextSequence(owner).Status);
        Assert.Equal(1, client.PeekNextSequence(owner).NextSequence);
        Held(client, owner, 1);
    }

    [Fact]
    public void ClientRestartPreservesOldOwnerUncertaintyAndRetiredFloor()
    {
        var store = new FakeStore(profile);
        var first = new RetirementLedgerCandidate(profile, store);
        var owner = Register(first);
        var one = Held(first, owner, 1);
        Assert.Equal(RetirementStatus.Committed, first.RecordConfirmedTerminal(owner, 1, one).Status);
        var two = Held(first, owner, 2);
        Assert.Equal(RetirementStatus.Committed, first.RecordUncertain(owner, 2, two).Status);
        var restarted = new RetirementLedgerCandidate(profile, store);
        Assert.Equal(RetirementStatus.ReplayOwner, restarted.RegisterOwner(owner.Incarnation).Status);
        Assert.Equal(RetirementStatus.Retired, restarted.RecordHeld(owner, 1, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.ReplayUncertain, restarted.RecordHeld(owner, 2, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.ReplayUncertain, restarted.RecordConfirmedTerminal(owner, 2, two).Status);
        var replacement = Register(restarted);
        Assert.NotEqual(owner.Slot, replacement.Slot);
        Assert.Single(store.State.Rows);
        Assert.Equal(1, store.State.Owners[owner.Slot].Floor);
    }

    [Fact]
    public async Task CompetingClientsCommitOneRetainedRecordWithoutBlindRetries()
    {
        var store = new FakeStore(profile);
        var owner = Register(new(profile, store));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return new RetirementLedgerCandidate(profile, store).RecordHeld(owner, 1, "daily:scope-1");
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(calls);
        Assert.Single(results, x => x.Status == RetirementStatus.Committed);
        Assert.All(results, x => Assert.Contains(x.Status,
            new[] { RetirementStatus.Committed, RetirementStatus.ReplayHeld, RetirementStatus.Conflict }));
        Assert.Single(store.State.Rows);
        Assert.Equal(1, store.State.Owners[owner.Slot].Issued);
        Assert.Equal(2, store.State.Version);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("incarnation")]
    [InlineData("nonce")]
    [InlineData("slot")]
    public void StaleOwnerCannotAlterTerminalFloorOrAnotherOwnersRecords(string change)
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        var nonce = Held(client, owner, 1);
        var stale = change switch
        {
            "generation" => owner with { Generation = owner.Generation + 1 },
            "incarnation" => owner with { Incarnation = Guid.NewGuid() },
            "nonce" => owner with { Nonce = Guid.NewGuid() },
            _ => owner with { Slot = owner.Slot + 1 }
        };
        var before = store.State;
        Assert.Equal(RetirementStatus.Invalid, client.RecordHeld(stale, 1, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordConfirmedTerminal(stale, 1, nonce).Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordUncertain(stale, 1, nonce).Status);
        Assert.Same(before, store.State);
    }

    [Fact]
    public void ConflictWrongNonceAndOutOfOrderRequestsCannotChangeRecordedState()
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        Held(client, owner, 1);
        var before = store.State;
        Assert.Equal(RetirementStatus.Conflict, client.RecordHeld(owner, 1, "other").Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordHeld(owner, 3, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordHeld(owner, 0, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordHeld(owner, 2, new string('x', 129)).Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordConfirmedTerminal(owner, 1, Guid.NewGuid()).Status);
        Assert.Same(before, store.State);
    }

    [Fact]
    public void OwnerRegistrationIsBoundedAndLostResponseReplaysExistingBinding()
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var incarnation = Guid.NewGuid();
        store.NextFault = Fault.AfterCommit;
        Assert.Equal(RetirementStatus.Unavailable, client.RegisterOwner(incarnation).Status);
        var replay = client.RegisterOwner(incarnation);
        Assert.Equal(RetirementStatus.ReplayOwner, replay.Status);
        for (var i = 1; i < 16; i++) Register(client);
        var before = store.State;
        Assert.Equal(RetirementStatus.Full, client.RegisterOwner(Guid.NewGuid()).Status);
        Assert.Equal(RetirementStatus.Invalid, client.RegisterOwner(Guid.Empty).Status);
        Assert.Same(before, store.State);
        Assert.Equal(16, store.State.Owners.Count);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("epoch")]
    [InlineData("profile")]
    [InlineData("missing")]
    [InlineData("quarantine")]
    [InlineData("forgotten-row")]
    public void IncompatibleMissingOrIncompleteStateNeverBootstrapsAnEmptyAuthority(string change)
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        Held(client, owner, 1);
        var selected = change switch
        {
            "target" => new RetirementProfile("other/sql", profile.Epoch),
            "epoch" => new RetirementProfile(profile.Target, Guid.NewGuid()),
            "profile" => new RetirementProfile(profile.Target, profile.Epoch, owners: 15),
            _ => profile
        };
        if (change == "missing") store.Missing = true;
        if (change == "quarantine") store.State = store.State with { Authoritative = false };
        if (change == "forgotten-row") store.State = store.State with { Rows = store.State.Rows.Clear() };
        var before = store.State;
        Assert.Equal(change is "missing" or "quarantine" ? RetirementStatus.Unavailable : RetirementStatus.Invalid,
            new RetirementLedgerCandidate(selected, store).RecordHeld(owner, 2, "daily:scope-1").Status);
        Assert.Same(before, store.State);
    }

    [Fact]
    public void VersionAndSequenceExhaustionRefuseWraparoundWithoutChangingState()
    {
        var store = new FakeStore(profile);
        var client = new RetirementLedgerCandidate(profile, store);
        var owner = Register(client);
        store.State = store.State with { Version = long.MaxValue };
        var before = store.State;
        Assert.Equal(RetirementStatus.Exhausted, client.RecordHeld(owner, 1, "daily:scope-1").Status);
        Assert.Same(before, store.State);
        // Synthetic high-water fixture: every prior sequence terminal/retired.
        store.State = store.State with
        {
            Version = 1,
            Owners = store.State.Owners.SetItem(owner.Slot, new(owner, long.MaxValue - 1, long.MaxValue - 1))
        };
        Assert.Equal(long.MaxValue, client.PeekNextSequence(owner).NextSequence);
        var last = client.RecordHeld(owner, long.MaxValue, "daily:scope-1");
        Assert.Equal(RetirementStatus.Committed, last.Status);
        Assert.Equal(RetirementStatus.Committed, client.RecordConfirmedTerminal(owner, long.MaxValue, last.Nonce!.Value).Status);
        before = store.State;
        Assert.Equal(RetirementStatus.Exhausted, client.PeekNextSequence(owner).Status);
        Assert.Equal(RetirementStatus.Invalid, client.RecordHeld(owner, long.MinValue, "daily:scope-1").Status);
        Assert.Equal(RetirementStatus.Retired, client.RecordHeld(owner, long.MaxValue, "different").Status);
        Assert.Same(before, store.State);
    }

    private enum Fault { None, BeforeCommit, AfterCommit, Conflict }
    private sealed class FakeStore(RetirementProfile profile) : IRetirementCandidateStore
    {
        private readonly object gate = new();
        internal RetirementState State = new(profile, 0,
            ImmutableDictionary<int, RetirementOwner>.Empty,
            ImmutableDictionary<RetirementKey, RetirementRow>.Empty);
        internal Fault NextFault;
        internal bool Missing;
        public RetirementState? Read() { lock (gate) return Missing ? null : State; }
        public RetirementCommit CompareAndCommit(long expectedVersion, RetirementState next)
        {
            lock (gate)
            {
                if (State.Version != expectedVersion) return RetirementCommit.Conflict;
                var fault = NextFault; NextFault = Fault.None;
                if (fault == Fault.Conflict) return RetirementCommit.Conflict;
                if (fault == Fault.BeforeCommit) return RetirementCommit.Unknown;
                Assert.Equal(expectedVersion + 1, next.Version);
                State = next;
                return fault == Fault.AfterCommit ? RetirementCommit.Unknown : RetirementCommit.Committed;
            }
        }
    }
}
