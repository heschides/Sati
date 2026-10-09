namespace Sati.Contracts.V1;

public enum OriginalClaimReleaseOperation { Generate, Queue, BeginSending }
public enum OriginalClaimDeliveryEvidence { GeneratedOnly, Queued, Uncertain, Received, KnownUnsent }
public sealed record OriginalClaimHistoryFact(int NoteId, long GenerationId, Guid? DispatchId,
    bool IsCorrection, OriginalClaimDeliveryEvidence Evidence);
public sealed record ClaimReleaseMappingDefect(int? BillingPeriodId, int? NoteId, string Code, bool HasDeliveryEvidence);
public sealed record OriginalClaimReleaseDecision(bool Allowed, string? Code);

/// <summary>
/// Sole owner of original-claim delivery permission. Callers separately prove current
/// authority, retained identity, source compliance and correction permission.
/// </summary>
public static class OriginalClaimReleaseRules
{
    public static OriginalClaimReleaseDecision Evaluate(int billingPeriodId, IReadOnlySet<int> noteIds,
        OriginalClaimReleaseOperation operation, IReadOnlyList<OriginalClaimHistoryFact> history,
        IReadOnlyList<ClaimReleaseMappingDefect> defects, bool complete,
        long? candidateGenerationId = null, Guid? candidateDispatchId = null)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(defects);
        if (!complete || billingPeriodId <= 0 || noteIds.Count == 0 || noteIds.Any(id => id <= 0) ||
            !Enum.IsDefined(operation) ||
            (operation != OriginalClaimReleaseOperation.Generate && candidateGenerationId is not > 0))
            return Held("claim_history_incomplete");
        if (defects.Any(defect =>
                (defect.HasDeliveryEvidence || defect.Code == "candidate_invalid") &&
                (defect.NoteId is int noteId ? noteIds.Contains(noteId) :
                    defect.BillingPeriodId is null || defect.BillingPeriodId == billingPeriodId)))
            return Held("claim_history_invalid");
        foreach (var noteId in noteIds)
        {
            var prior = history.Where(fact => fact.NoteId == noteId).ToList();
            if (prior.Any(fact => fact.Evidence == OriginalClaimDeliveryEvidence.Received))
                return Held("claim_received_requires_correction");
            if (prior.Any(fact => fact.Evidence == OriginalClaimDeliveryEvidence.Uncertain || !Enum.IsDefined(fact.Evidence)))
                return Held("claim_delivery_uncertain");
            if (prior.Any(fact => fact.Evidence == OriginalClaimDeliveryEvidence.Queued &&
                    !(operation == OriginalClaimReleaseOperation.BeginSending &&
                      fact.GenerationId == candidateGenerationId && fact.DispatchId == candidateDispatchId)))
                return Held("claim_already_reserved");
            if (operation == OriginalClaimReleaseOperation.BeginSending &&
                !prior.Any(fact => fact.GenerationId == candidateGenerationId && fact.DispatchId == candidateDispatchId &&
                    fact.Evidence == OriginalClaimDeliveryEvidence.Queued && !fact.IsCorrection))
                return Held("candidate_not_queued");
        }
        return new(true, null);
    }

    private static OriginalClaimReleaseDecision Held(string reason) => new(false, reason);
}
