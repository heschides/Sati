namespace Sati.Contracts.V1;

public sealed record BillingComplianceExceptionValidation(
    bool Accepted,
    IReadOnlyList<string> SelectedObligationIds,
    IReadOnlyList<string> Errors);

/// <summary>
/// Shared rule for a supervisor's one-note compliance exception. The decision
/// names the exact blockers being excepted; any current blocker that is not
/// named continues to block the note.
/// </summary>
public static class BillingComplianceExceptionRules
{
    public const int ExplanationMaxLength = 4_000;

    public static BillingComplianceExceptionValidation Validate(
        IEnumerable<BillingComplianceBlocker> currentBlockers,
        IEnumerable<string>? selectedObligationIds,
        string? explanation,
        bool attestationConfirmed)
    {
        ArgumentNullException.ThrowIfNull(currentBlockers);

        var blockers = currentBlockers.ToArray();
        var blockerIds = blockers
            .Select(blocker => blocker.ObligationId)
            .ToHashSet(StringComparer.Ordinal);
        var selected = Normalize(selectedObligationIds);
        var errors = new List<string>();
        var normalizedExplanation = explanation?.Trim() ?? string.Empty;

        if (normalizedExplanation.Length is < 1 or > ExplanationMaxLength)
            errors.Add($"An explanation is required and must not exceed {ExplanationMaxLength:N0} characters.");
        if (!attestationConfirmed)
            errors.Add("The supervisor must attest to the exception decision.");
        if (blockers.Length == 0)
            errors.Add("This note has no current compliance blocker to except.");
        if (selected.Count == 0)
            errors.Add("Select at least one exact compliance blocker.");
        if (selected.Any(id => !blockerIds.Contains(id)))
            errors.Add("A selected compliance blocker is not current for this note.");
        return new BillingComplianceExceptionValidation(
            errors.Count == 0,
            selected,
            errors);
    }

    /// <summary>
    /// Returns the current blockers that were not covered by the exact
    /// supervisor decision. A narrow exception is still a valid, immutable
    /// decision; these unrelated obligations continue to gate billing.
    /// </summary>
    public static IReadOnlyList<BillingComplianceBlocker> RemainingBlockers(
        IEnumerable<BillingComplianceBlocker> currentBlockers,
        IEnumerable<string>? selectedObligationIds)
    {
        ArgumentNullException.ThrowIfNull(currentBlockers);
        var selected = Normalize(selectedObligationIds).ToHashSet(StringComparer.Ordinal);
        return currentBlockers
            .Where(blocker => !selected.Contains(blocker.ObligationId))
            .ToArray();
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? obligationIds) =>
        obligationIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
}
