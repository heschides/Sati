namespace Sati.Api.Infrastructure;

// DEC-0244 reference model only. Not registered in DI or used by routes/workers.
// A lock here models one atomic controller; independent copies do NOT share a budget.
// It proves no durable backend, authentication, SQL closure or distributed fencing.
internal enum BudgetClass { Daily, Recovery, Background, Validation }
internal enum BudgetOperation
{
    DailyRead, DailyWrite, Export, Dispatch, Poll, NoteSweep, Signature,
    Reset, RecoveryRepair, Health, ValidateIdentity
}
internal enum BudgetAdmission
{
    Granted, ReplayHeld, ReplayUncertain, ReplayReleased, Busy, MetadataFull,
    Conflict, Invalid, Unavailable
}
internal enum BudgetChange
{
    Confirmed, AlreadyRecorded, InvalidGrant, InvalidChild, LimitExceeded,
    Uncertain, ChildrenOpen, Released, Unavailable
}

internal sealed class BudgetCandidateProfile
{
    internal BudgetCandidateProfile(string target, Guid epoch, int daily = 12, int recovery = 8,
        int background = 8, int validation = 4, int agencyCap = 4,
        int maxRecords = 128, int maxChildren = 16)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 128 || epoch == Guid.Empty)
            throw new ArgumentException("An exact synthetic target and epoch are required.");
        if (new[] { daily, recovery, background, validation, agencyCap }.Any(x => x is < 1 or > 1024)
            || maxRecords is < 1 or > 4096 || maxChildren is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(daily), "Candidate profile outside bounded model range.");
        Target = target; Epoch = epoch; Daily = daily; Recovery = recovery;
        Background = background; Validation = validation; AgencyCap = agencyCap;
        MaxRecords = maxRecords; MaxChildren = maxChildren;
    }

    internal string Target { get; }
    internal Guid Epoch { get; }
    internal int Daily { get; }
    internal int Recovery { get; }
    internal int Background { get; }
    internal int Validation { get; }
    internal int AgencyCap { get; }
    internal int MaxRecords { get; }
    internal int MaxChildren { get; }
    internal int Total => Daily + Recovery + Background + Validation;
    internal int Capacity(BudgetClass value) => value switch
    {
        BudgetClass.Daily => Daily, BudgetClass.Recovery => Recovery,
        BudgetClass.Background => Background, BudgetClass.Validation => Validation,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

// Agency is an already validated scope in this model, NOT authorization evidence.
// A future trusted adapter must choose operation/scope before calling admission.
internal sealed record BudgetRequest(string Target, Guid Epoch, Guid RequestId,
    Guid Incarnation, BudgetOperation Operation, int AgencyId);
internal sealed record BudgetGrant(Guid RequestId, Guid Incarnation, Guid Epoch, Guid Nonce);
internal sealed record BudgetAdmissionResult(BudgetAdmission Status, BudgetGrant? Grant = null);
internal sealed record BudgetSnapshot(int Charged, int Daily, int Recovery, int Background,
    int Validation, int Records, int UncertainRecords, int OpenChildren, int ChildRecords);

internal sealed class SharedWorkloadBudgetCandidate
{
    private readonly BudgetCandidateProfile profile;
    internal SharedWorkloadBudgetCandidate(BudgetCandidateProfile profile) =>
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

    private enum Phase { Held, Uncertain, Released }
    private sealed class Entry(BudgetRequest request, BudgetGrant grant, BudgetClass workClass, int cost)
    {
        internal readonly BudgetRequest Request = request;
        internal readonly BudgetGrant Grant = grant;
        internal readonly BudgetClass Class = workClass;
        internal readonly int Cost = cost;
        internal Phase State = Phase.Held;
        // Each identity denotes ONE attempted open, including a failed/uncertain attempt.
        // Reopening the same connection object requires a fresh attempt identity.
        internal readonly Dictionary<Guid, bool> Children = [];
    }

    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private bool available = true;

    internal BudgetAdmissionResult TryAdmit(BudgetRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (!available) return new(BudgetAdmission.Unavailable);
            if (request.Target != profile.Target || request.Epoch != profile.Epoch
                || request.RequestId == Guid.Empty || request.Incarnation == Guid.Empty
                || !TryCost(request.Operation, out var workClass, out var cost, out var scoped)
                || (scoped ? request.AgencyId <= 0 : request.AgencyId != 0))
                return new(BudgetAdmission.Invalid);

            if (entries.TryGetValue(request.RequestId, out var prior))
            {
                if (prior.Request != request) return new(BudgetAdmission.Conflict);
                return new(prior.State switch
                {
                    Phase.Held => BudgetAdmission.ReplayHeld,
                    Phase.Uncertain => BudgetAdmission.ReplayUncertain,
                    _ => BudgetAdmission.ReplayReleased
                }, prior.Grant);
            }

            if (entries.Count >= profile.MaxRecords) return new(BudgetAdmission.MetadataFull);
            // Whole-operation reservation; no waiting, borrowing or nested reacquisition.
            if (Used(workClass) + cost > profile.Capacity(workClass)
                || (scoped && Used(workClass, request.AgencyId) + cost > profile.AgencyCap))
                return new(BudgetAdmission.Busy);

            var grant = new BudgetGrant(request.RequestId, request.Incarnation, request.Epoch, Guid.NewGuid());
            entries.Add(request.RequestId, new(request, grant, workClass, cost));
            return new(BudgetAdmission.Granted, grant);
        }
    }

    // Record BEFORE a proposed protected open. AlreadyRecorded never authorizes a second open.
    // A refused/failed open needs positive never-opened/closed evidence or retains its debit.
    internal BudgetChange RegisterChildOpen(BudgetGrant grant, Guid child)
    {
        lock (gate)
        {
            if (!available) return BudgetChange.Unavailable;
            if (!Find(grant, out var entry)) return BudgetChange.InvalidGrant;
            if (entry.State == Phase.Released) return BudgetChange.Released;
            if (entry.State == Phase.Uncertain) return BudgetChange.Uncertain;
            if (child == Guid.Empty) return BudgetChange.InvalidChild;
            if (entry.Children.TryGetValue(child, out var open))
                return open ? BudgetChange.AlreadyRecorded : BudgetChange.InvalidChild;
            if (entry.Children.Count >= profile.MaxChildren
                || entry.Children.Values.Count(x => x) >= entry.Cost)
                return BudgetChange.LimitExceeded;
            entry.Children.Add(child, true);
            return BudgetChange.Confirmed;
        }
    }

    // Synthetic positive closure observation, not a SQL-provider/fencing implementation.
    internal BudgetChange ConfirmChildClosed(BudgetGrant grant, Guid child)
    {
        lock (gate)
        {
            if (!available) return BudgetChange.Unavailable;
            if (!Find(grant, out var entry)) return BudgetChange.InvalidGrant;
            if (!entry.Children.TryGetValue(child, out var open)) return BudgetChange.InvalidChild;
            if (!open) return BudgetChange.AlreadyRecorded;
            entry.Children[child] = false;
            return BudgetChange.Confirmed;
        }
    }

    internal BudgetChange MarkUncertain(BudgetGrant grant)
    {
        lock (gate)
        {
            if (!available) return BudgetChange.Unavailable;
            if (!Find(grant, out var entry)) return BudgetChange.InvalidGrant;
            if (entry.State == Phase.Released) return BudgetChange.Released;
            if (entry.State == Phase.Uncertain) return BudgetChange.AlreadyRecorded;
            entry.State = Phase.Uncertain;
            return BudgetChange.Confirmed;
        }
    }

    internal BudgetChange Release(BudgetGrant grant)
    {
        lock (gate)
        {
            if (!available) return BudgetChange.Unavailable;
            if (!Find(grant, out var entry)) return BudgetChange.InvalidGrant;
            if (entry.State == Phase.Released) return BudgetChange.AlreadyRecorded;
            if (entry.State == Phase.Uncertain) return BudgetChange.Uncertain;
            if (entry.Children.Values.Any(x => x)) return BudgetChange.ChildrenOpen;
            entry.State = Phase.Released;
            // Retain fingerprint/closed-child evidence: no TTL or unsafe replay eviction.
            return BudgetChange.Confirmed;
        }
    }

    // Model fault seam. Restarting this object would lose debt and is NEVER a safe backend restart.
    internal void SetAvailabilityForModel(bool value) { lock (gate) available = value; }

    internal BudgetSnapshot Snapshot()
    {
        lock (gate)
        {
            return new(Used(), Used(BudgetClass.Daily), Used(BudgetClass.Recovery),
                Used(BudgetClass.Background), Used(BudgetClass.Validation), entries.Count,
                entries.Values.Count(x => x.State == Phase.Uncertain),
                entries.Values.Sum(x => x.Children.Values.Count(open => open)),
                entries.Values.Sum(x => x.Children.Count));
        }
    }

    internal int AgencyUsage(BudgetClass workClass, int agencyId)
    {
        lock (gate) return Used(workClass, agencyId);
    }

    private int Used(BudgetClass? workClass = null, int? agencyId = null) => entries.Values
        .Where(x => x.State != Phase.Released && (workClass == null || x.Class == workClass)
            && (agencyId == null || x.Request.AgencyId == agencyId)).Sum(x => x.Cost);

    private bool Find(BudgetGrant grant, out Entry entry)
    {
        if (entries.TryGetValue(grant.RequestId, out var found) && found.Grant == grant)
        {
            entry = found; return true;
        }
        entry = null!; return false;
    }

    // Provisional normal-path costs from W8, NOT failure-safe operating costs or route bindings.
    private static bool TryCost(BudgetOperation operation, out BudgetClass workClass,
        out int cost, out bool scoped)
    {
        (workClass, cost, scoped) = operation switch
        {
            BudgetOperation.DailyRead => (BudgetClass.Daily, 1, true),
            BudgetOperation.DailyWrite => (BudgetClass.Daily, 2, true),
            BudgetOperation.Export or BudgetOperation.Dispatch => (BudgetClass.Background, 4, true),
            BudgetOperation.Poll => (BudgetClass.Background, 3, true),
            BudgetOperation.NoteSweep or BudgetOperation.Signature => (BudgetClass.Background, 2, true),
            BudgetOperation.Reset => (BudgetClass.Recovery, 4, false),
            BudgetOperation.RecoveryRepair => (BudgetClass.Recovery, 2, true),
            BudgetOperation.Health => (BudgetClass.Recovery, 1, false),
            BudgetOperation.ValidateIdentity => (BudgetClass.Validation, 1, false),
            _ => (default, 0, false)
        };
        return cost > 0;
    }
}
