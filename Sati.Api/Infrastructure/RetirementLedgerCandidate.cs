using System.Collections.Immutable;

namespace Sati.Api.Infrastructure;

// DEC-0245 bookkeeping reference only. No DI, storage backend, SQL grant or closure adapter.
internal enum RetirementPhase { Held, Uncertain, Terminal }
internal enum RetirementStatus
{
    Committed, Ready, Registered, ReplayOwner, ReplayHeld, ReplayUncertain, ReplayTerminal,
    Retired, Invalid, Conflict, Full, Unavailable, Exhausted
}
internal enum RetirementCommit { Committed, Conflict, Unknown }
internal sealed record RetirementOwnerHandle(int Slot, long Generation, Guid Incarnation, Guid Nonce);
internal sealed record RetirementOwner(RetirementOwnerHandle Handle, long Issued, long Floor);
internal readonly record struct RetirementKey(int Slot, long Generation, long Sequence);
internal sealed record RetirementRow(string Fingerprint, Guid Nonce, RetirementPhase Phase);
internal sealed record RetirementResult(RetirementStatus Status,
    RetirementOwnerHandle? Owner = null, Guid? Nonce = null, long? NextSequence = null);

internal sealed class RetirementProfile
{
    internal RetirementProfile(string target, Guid epoch, int owners = 16, int records = 128)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 128 || epoch == Guid.Empty)
            throw new ArgumentException("Exact synthetic target and epoch required.");
        if (owners is < 1 or > 64 || records is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(owners));
        Target = target; Epoch = epoch; Owners = owners; Records = records;
    }
    internal string Target { get; }
    internal Guid Epoch { get; }
    internal int Owners { get; }
    internal int Records { get; }
    internal bool Matches(RetirementProfile other) => Target == other.Target && Epoch == other.Epoch
        && Owners == other.Owners && Records == other.Records;
}

internal sealed record RetirementState(RetirementProfile Profile, long Version,
    ImmutableDictionary<int, RetirementOwner> Owners,
    ImmutableDictionary<RetirementKey, RetirementRow> Rows, bool Authoritative = true);

// Read null/non-authoritative means unavailable, never implicit empty state/bootstrap.
// Committed must mean a positively durable ack in a REAL implementation; none exists here.
internal interface IRetirementCandidateStore
{
    RetirementState? Read();
    RetirementCommit CompareAndCommit(long expectedVersion, RetirementState next);
}

internal sealed class RetirementLedgerCandidate(RetirementProfile profile, IRetirementCandidateStore store)
{
    private readonly RetirementProfile profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private readonly IRetirementCandidateStore store = store ?? throw new ArgumentNullException(nameof(store));

    internal RetirementResult RegisterOwner(Guid incarnation) => Change(state =>
    {
        if (incarnation == Guid.Empty) return (null, new(RetirementStatus.Invalid));
        var prior = state.Owners.Values.FirstOrDefault(x => x.Handle.Incarnation == incarnation);
        if (prior != null) return (null, new(RetirementStatus.ReplayOwner, prior.Handle));
        if (state.Owners.Count >= profile.Owners) return (null, new(RetirementStatus.Full));
        var slot = Enumerable.Range(1, profile.Owners).First(x => !state.Owners.ContainsKey(x));
        // No reuse/rotation API: old generations and uncertainty cannot be evicted.
        var handle = new RetirementOwnerHandle(slot, 1, incarnation, Guid.NewGuid());
        return (state with { Owners = state.Owners.Add(slot, new(handle, 0, 0)) },
            new(RetirementStatus.Registered, handle));
    });

    internal RetirementResult PeekNextSequence(RetirementOwnerHandle owner) => Change(state =>
    {
        if (!FindOwner(state, owner, out var current)) return (null, new(RetirementStatus.Invalid));
        return current.Issued == long.MaxValue
            ? (null, new(RetirementStatus.Exhausted))
            : (null, new(RetirementStatus.Ready, NextSequence: current.Issued + 1));
    });

    // Fingerprint is bounded opaque trusted operation metadata, NOT a narrative or payload.
    // Records bookkeeping for an external reservation; this method never grants SQL capacity.
    internal RetirementResult RecordHeld(RetirementOwnerHandle owner, long sequence, string fingerprint) => Change(state =>
    {
        if (!FindOwner(state, owner, out var current) || sequence <= 0)
            return (null, new(RetirementStatus.Invalid));
        // MUST precede fingerprint lookup: old requests cannot be re-created after deletion.
        if (sequence <= current.Floor) return (null, new(RetirementStatus.Retired));
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 128)
            return (null, new(RetirementStatus.Invalid));
        var key = Key(owner, sequence);
        if (state.Rows.TryGetValue(key, out var prior))
            return (null, prior.Fingerprint == fingerprint
                ? Replay(prior) : new(RetirementStatus.Conflict));
        if (current.Issued == long.MaxValue) return (null, new(RetirementStatus.Exhausted));
        if (sequence != current.Issued + 1) return (null, new(RetirementStatus.Invalid));
        if (state.Rows.Count >= profile.Records) return (null, new(RetirementStatus.Full));
        var row = new RetirementRow(fingerprint, Guid.NewGuid(), RetirementPhase.Held);
        return (state with
        {
            Owners = state.Owners.SetItem(owner.Slot, current with { Issued = sequence }),
            Rows = state.Rows.Add(key, row)
        }, new(RetirementStatus.Committed, Nonce: row.Nonce));
    });

    internal RetirementResult RecordUncertain(RetirementOwnerHandle owner, long sequence, Guid nonce) =>
        SetPhase(owner, sequence, nonce, terminal: false);

    // Positive terminal observation supplied synthetically by tests. Not provider-close evidence.
    // Real integration must prove the whole reservation is released before calling this.
    internal RetirementResult RecordConfirmedTerminal(RetirementOwnerHandle owner, long sequence, Guid nonce) =>
        SetPhase(owner, sequence, nonce, terminal: true);

    private RetirementResult SetPhase(RetirementOwnerHandle owner, long sequence, Guid nonce, bool terminal) => Change(state =>
    {
        if (!FindOwner(state, owner, out var current) || sequence <= 0)
            return (null, new(RetirementStatus.Invalid));
        if (sequence <= current.Floor) return (null, new(RetirementStatus.Retired));
        var key = Key(owner, sequence);
        if (!state.Rows.TryGetValue(key, out var row) || nonce == Guid.Empty || row.Nonce != nonce)
            return (null, new(RetirementStatus.Invalid));
        if (row.Phase != RetirementPhase.Held) return (null, Replay(row));
        var rows = state.Rows.SetItem(key, row with
        {
            Phase = terminal ? RetirementPhase.Terminal : RetirementPhase.Uncertain
        });
        var floor = current.Floor;
        // No skipping a held/uncertain hole. floor+1 is evaluated only below Issued, so cannot wrap.
        while (floor < current.Issued
            && rows.TryGetValue(Key(owner, floor + 1), out var first)
            && first.Phase == RetirementPhase.Terminal)
        {
            rows = rows.Remove(Key(owner, ++floor));
        }
        return (state with
        {
            Rows = rows, Owners = state.Owners.SetItem(owner.Slot, current with { Floor = floor })
        }, new(RetirementStatus.Committed));
    });

    private RetirementResult Change(Func<RetirementState, (RetirementState? Next, RetirementResult Result)> change)
    {
        var state = store.Read();
        if (state == null || !state.Authoritative) return new(RetirementStatus.Unavailable);
        if (!profile.Matches(state.Profile) || !Consistent(state)) return new(RetirementStatus.Invalid);
        var (next, result) = change(state);
        if (next == null) return result;
        if (state.Version == long.MaxValue) return new(RetirementStatus.Exhausted);
        next = next with { Version = state.Version + 1 };
        return store.CompareAndCommit(state.Version, next) switch
        {
            RetirementCommit.Committed => result,
            RetirementCommit.Conflict => new(RetirementStatus.Conflict),
            _ => new(RetirementStatus.Unavailable) // no nonce/ack from an ambiguous commit
        };
    }

    private static bool FindOwner(RetirementState state, RetirementOwnerHandle handle, out RetirementOwner owner)
    {
        if (state.Owners.TryGetValue(handle.Slot, out var found) && found.Handle == handle)
        {
            owner = found; return true;
        }
        owner = null!; return false;
    }

    private static RetirementKey Key(RetirementOwnerHandle owner, long sequence) =>
        new(owner.Slot, owner.Generation, sequence);
    private static RetirementResult Replay(RetirementRow row) => new(row.Phase switch
    {
        RetirementPhase.Held => RetirementStatus.ReplayHeld,
        RetirementPhase.Uncertain => RetirementStatus.ReplayUncertain,
        _ => RetirementStatus.ReplayTerminal
    }, Nonce: row.Nonce);

    // Reject incomplete/corrupt snapshots instead of repairing missing debt to zero.
    private bool Consistent(RetirementState state)
    {
        if (state.Version < 0 || state.Owners.Count > profile.Owners || state.Rows.Count > profile.Records)
            return false;
        var incarnations = new HashSet<Guid>();
        foreach (var (slot, owner) in state.Owners)
        {
            if (slot < 1 || slot > profile.Owners || owner.Handle.Slot != slot
                || owner.Handle.Generation <= 0 || owner.Handle.Incarnation == Guid.Empty
                || owner.Handle.Nonce == Guid.Empty || !incarnations.Add(owner.Handle.Incarnation)
                || owner.Floor < 0 || owner.Floor > owner.Issued
                || owner.Issued - owner.Floor > profile.Records) return false;
            if (state.Rows.Count(x => x.Key.Slot == slot && x.Key.Generation == owner.Handle.Generation)
                != owner.Issued - owner.Floor) return false;
        }
        foreach (var (key, row) in state.Rows)
        {
            if (!state.Owners.TryGetValue(key.Slot, out var owner)
                || key.Generation != owner.Handle.Generation || key.Sequence <= owner.Floor
                || key.Sequence > owner.Issued || row.Nonce == Guid.Empty
                || string.IsNullOrWhiteSpace(row.Fingerprint) || row.Fingerprint.Length > 128
                || !Enum.IsDefined(row.Phase)) return false;
        }
        return true;
    }
}
