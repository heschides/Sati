namespace Sati.Contracts.V1;

public sealed record BillingComplianceObligationSnapshot(
    string ObligationId,
    int PersonId,
    string Name,
    BillingComplianceRequirements Requirement,
    DateTime DueDate,
    DateTime? CompletedDate,
    DateTime? AppliesFromDate = null,
    DateTime? RetiredDate = null,
    string? EvidenceId = null);

public sealed record BillingRecoveryNoteSnapshot(
    int NoteId,
    int PersonId,
    DateTime ServiceDate,
    bool IsSubmittedOrBilled = false);

public sealed record BillingRecoveryNoteOption(
    int NoteId,
    DateTime ServiceDate,
    IReadOnlyList<string> BlockingObligationIds,
    bool IsSelectedByDefault = true);

public sealed record BillingRecoveryObligationOption(
    string ObligationId,
    string Name,
    DateTime DueDate,
    DateTime CompletedDate,
    string EvidenceId);

public sealed record CreateBillingComplianceRecoveryRequest(
    IReadOnlyList<int> SelectedNoteIds,
    string? Explanation,
    bool AttestationConfirmed);

public sealed record BillingComplianceRecoveryPlan(
    int AgencyId,
    int PersonId,
    IReadOnlyList<BillingRecoveryObligationOption> Obligations,
    IReadOnlyList<BillingRecoveryNoteOption> NoteOptions,
    IReadOnlyList<int> UnresolvedNoteIds);

public sealed record BillingComplianceRecoveryDecision(
    Guid DecisionId,
    int AgencyId,
    int PersonId,
    int AdminUserId,
    DateTime RecordedAtUtc,
    string Explanation,
    bool AttestationConfirmed,
    IReadOnlyList<BillingRecoveryObligationOption> Obligations,
    IReadOnlyList<int> NoteIds);

public sealed record BillingComplianceRecoveryDecisionResult(
    bool Accepted,
    BillingComplianceRecoveryDecision? Decision,
    IReadOnlyList<string> Errors);

/// <summary>
/// Builds the post-compliance recovery checklist and records only the notes an
/// administrator explicitly leaves selected. Supervisor note overrides are a
/// separate workflow and are intentionally not represented here.
/// </summary>
public static class BillingComplianceRecoveryRules
{
    public static BillingComplianceRecoveryPlan Prepare(
        int agencyId,
        int personId,
        IEnumerable<BillingCompliancePolicyVersionSnapshot> policies,
        IEnumerable<BillingComplianceObligationSnapshot> obligations,
        IEnumerable<BillingRecoveryNoteSnapshot> notes,
        DateTime asOfDate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(agencyId, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(personId, 0);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(obligations);
        ArgumentNullException.ThrowIfNull(notes);

        var policyList = policies.ToArray();
        var obligationList = obligations.Where(item => item.PersonId == personId).ToArray();
        ValidateObligations(obligationList);

        var options = new List<BillingRecoveryNoteOption>();
        var unresolved = new List<int>();
        var includedObligations = new Dictionary<string, BillingRecoveryObligationOption>(
            StringComparer.Ordinal);

        foreach (var note in notes
                     .Where(item => item.PersonId == personId && !item.IsSubmittedOrBilled)
                     .OrderBy(item => item.ServiceDate)
                     .ThenBy(item => item.NoteId))
        {
            var policy = BillingCompliancePolicyRules.ResolveForServiceDate(
                policyList, agencyId, note.ServiceDate)
                ?? throw new InvalidOperationException(
                    $"No billing-compliance policy covers service date {note.ServiceDate:yyyy-MM-dd}.");

            var blockers = obligationList
                .Where(obligation =>
                    (policy.Requirements & obligation.Requirement) == obligation.Requirement &&
                    IsApplicable(obligation, note.ServiceDate) &&
                    BillingComplianceGate.IsWithinBlockedInterval(
                        obligation.DueDate, obligation.CompletedDate, note.ServiceDate))
                .OrderBy(obligation => obligation.ObligationId, StringComparer.Ordinal)
                .ToArray();

            if (blockers.Length == 0)
                continue;

            if (blockers.Any(blocker =>
                    blocker.CompletedDate is null ||
                    blocker.CompletedDate.Value.Date > asOfDate.Date ||
                    string.IsNullOrWhiteSpace(blocker.EvidenceId)))
            {
                unresolved.Add(note.NoteId);
                continue;
            }

            foreach (var blocker in blockers)
            {
                includedObligations.TryAdd(
                    blocker.ObligationId,
                    new BillingRecoveryObligationOption(
                        blocker.ObligationId,
                        blocker.Name,
                        blocker.DueDate.Date,
                        blocker.CompletedDate!.Value.Date,
                        blocker.EvidenceId!.Trim()));
            }

            options.Add(new BillingRecoveryNoteOption(
                note.NoteId,
                note.ServiceDate.Date,
                blockers.Select(blocker => blocker.ObligationId).ToArray()));
        }

        return new BillingComplianceRecoveryPlan(
            agencyId,
            personId,
            includedObligations.Values.OrderBy(item => item.ObligationId, StringComparer.Ordinal).ToArray(),
            options,
            unresolved);
    }

    public static BillingComplianceRecoveryDecisionResult CreateDecision(
        BillingComplianceRecoveryPlan plan,
        IEnumerable<int> selectedNoteIds,
        int adminUserId,
        DateTime recordedAtUtc,
        string? explanation,
        bool attestationConfirmed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedNoteIds);

        var errors = new List<string>();
        if (adminUserId <= 0)
            errors.Add("An administrator is required.");
        if (!attestationConfirmed)
            errors.Add("The administrator must attest to the recovery decision.");
        if (string.IsNullOrWhiteSpace(explanation))
            errors.Add("An explanation is required.");
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            errors.Add("The recorded timestamp must be UTC.");

        var selected = selectedNoteIds.Distinct().Order().ToArray();
        if (selected.Length == 0)
            errors.Add("At least one note must be selected.");

        var available = plan.NoteOptions.ToDictionary(option => option.NoteId);
        var unknown = selected.Where(noteId => !available.ContainsKey(noteId)).ToArray();
        if (unknown.Length > 0)
            errors.Add("A selected note is not eligible for recovery.");

        if (errors.Count > 0)
            return new BillingComplianceRecoveryDecisionResult(false, null, errors);

        var obligationIds = selected
            .SelectMany(noteId => available[noteId].BlockingObligationIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var obligationIndex = plan.Obligations.ToDictionary(
            obligation => obligation.ObligationId, StringComparer.Ordinal);
        var selectedObligations = obligationIds.Select(id => obligationIndex[id]).ToArray();

        var decision = new BillingComplianceRecoveryDecision(
            Guid.NewGuid(),
            plan.AgencyId,
            plan.PersonId,
            adminUserId,
            DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
            explanation!.Trim(),
            true,
            selectedObligations,
            selected);
        return new BillingComplianceRecoveryDecisionResult(true, decision, []);
    }

    public static bool IsReleased(
        BillingRecoveryNoteSnapshot note,
        IEnumerable<BillingComplianceObligationSnapshot> obligations,
        BillingCompliancePolicyVersionSnapshot policy,
        BillingComplianceRecoveryDecision decision)
    {
        if (note.PersonId != decision.PersonId || policy.AgencyId != decision.AgencyId ||
            note.IsSubmittedOrBilled || !decision.NoteIds.Contains(note.NoteId) ||
            decision.AdminUserId <= 0 || !decision.AttestationConfirmed ||
            string.IsNullOrWhiteSpace(decision.Explanation) ||
            decision.RecordedAtUtc.Kind != DateTimeKind.Utc)
            return false;

        var blockers = obligations
            .Where(obligation => obligation.PersonId == note.PersonId &&
                                 (policy.Requirements & obligation.Requirement) == obligation.Requirement &&
                                 IsApplicable(obligation, note.ServiceDate) &&
                                 BillingComplianceGate.IsWithinBlockedInterval(
                                     obligation.DueDate, obligation.CompletedDate, note.ServiceDate))
            .ToArray();
        if (blockers.Length == 0 || blockers.Any(blocker => blocker.CompletedDate is null))
            return false;

        var decisionObligations = decision.Obligations.ToDictionary(
            obligation => obligation.ObligationId, StringComparer.Ordinal);
        return blockers.All(blocker =>
            decisionObligations.TryGetValue(blocker.ObligationId, out var selected) &&
            selected.DueDate.Date == blocker.DueDate.Date &&
            selected.CompletedDate.Date == blocker.CompletedDate!.Value.Date &&
            string.Equals(Normalize(selected.EvidenceId), Normalize(blocker.EvidenceId),
                StringComparison.Ordinal));
    }

    public static IReadOnlyList<BillingComplianceObligationSnapshot> FromComplianceSnapshots(
        int personId,
        IEnumerable<ComplianceFormSnapshot> snapshots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(personId, 0);
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Requirement = BillingComplianceGate.RequirementFor(snapshot.Type)
            })
            .Where(item => item.Requirement != BillingComplianceRequirements.None)
            .Select(item => new BillingComplianceObligationSnapshot(
                BillingComplianceGate.ResolveObligationId(item.Snapshot),
                personId,
                BillingComplianceGate.DisplayName(item.Snapshot.Type),
                item.Requirement,
                item.Snapshot.DueDate.Date,
                item.Snapshot.CompletedDate?.Date,
                EvidenceId: Normalize(item.Snapshot.EvidenceId)))
            .ToArray();
    }

    private static void ValidateObligations(
        IReadOnlyCollection<BillingComplianceObligationSnapshot> obligations)
    {
        if (obligations.Any(obligation =>
                string.IsNullOrWhiteSpace(obligation.ObligationId) ||
                string.IsNullOrWhiteSpace(obligation.Name)))
            throw new ArgumentException("Every billing-compliance obligation needs an id and name.");

        if (obligations.GroupBy(obligation => obligation.ObligationId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            throw new ArgumentException("Billing-compliance obligation ids must be unique.");

        if (obligations.Any(obligation =>
                obligation.Requirement == BillingComplianceRequirements.None ||
                !BillingComplianceGate.IsSupported(obligation.Requirement) ||
                !IsSingleFlag(obligation.Requirement)))
            throw new ArgumentException("Each obligation must identify one supported billing requirement.");

        if (obligations.Any(obligation =>
                obligation.AppliesFromDate is DateTime appliesFrom &&
                obligation.RetiredDate is DateTime retired &&
                retired.Date < appliesFrom.Date))
            throw new ArgumentException("An obligation retirement cannot precede its applicable date.");
    }

    private static bool IsApplicable(
        BillingComplianceObligationSnapshot obligation,
        DateTime serviceDate) =>
        (obligation.AppliesFromDate is null ||
         serviceDate.Date >= obligation.AppliesFromDate.Value.Date) &&
        (obligation.RetiredDate is null ||
         serviceDate.Date < obligation.RetiredDate.Value.Date);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSingleFlag(BillingComplianceRequirements value)
    {
        var bits = (int)value;
        return (bits & (bits - 1)) == 0;
    }
}
