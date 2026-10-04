namespace Sati.Contracts.V1;

/// <summary>A human finding about one quarantined Claim.MD test upload, never an automated vendor verdict.</summary>
public enum ClaimMdReconciliationDecision { ConfirmedReceived = 1, ConfirmedNotReceived = 2 }

public enum ClaimMdEvidenceKind { AccountFileRecord = 1, SupportCase = 2 }

public sealed record ClaimMdClaimIdentity(string ClaimReference, string RemoteClaimId);

public sealed record ClaimMdObservedClaim(string ClaimReference, string RemoteClaimId, string Status);

/// <summary>Safe, bounded facts for an operator to compare with authoritative vendor evidence.</summary>
public sealed record ClaimMdReconciliationManifestDto(
    Guid DispatchId, long Revision, Guid AccountId, string AccountNumber, long EdiGenerationId,
    string DispatchState, DateTime RequestedAtUtc, DateTime EvidenceNotBeforeUtc,
    string FileName, string ContentSha256,
    IReadOnlyList<ClaimMdClaimIdentity> Claims,
    string ReceivedAttestation, string NotReceivedAttestation);

public sealed record ReconcileClaimMdDispatchRequest(
    long ExpectedRevision, Guid ExpectedAccountId, string EvidenceAccountNumber,
    long ExpectedEdiGenerationId, string ExpectedContentSha256, string EvidenceFileName,
    string Decision, string EvidenceKind,
    string EvidenceReference, string EvidenceSha256, DateTime EvidenceObservedAtUtc,
    string Attestation, string? ExternalFileId,
    int? AcceptedClaimCount, int? RejectedClaimCount,
    IReadOnlyList<ClaimMdObservedClaim>? Claims);

public sealed record ClaimMdReconciliationResultDto(
    Guid DispatchId, long Revision, string State, string? ExternalFileId,
    string EvidenceReference, string Message);

/// <summary>Authoritative validation for a manual finding and for durable dispatch-state transitions.</summary>
public static class ClaimMdReconciliationRules
{
    public const string ReceivedAttestation =
        "I reviewed authoritative Claim.MD evidence for this exact test account, file, and every claim identity; this is my manual receipt finding, not proof that Claim.MD retained the original bytes or that a payer accepted the claims.";
    public const string NotReceivedAttestation =
        "I reviewed authoritative Claim.MD support evidence for this exact test account and file; support confirmed it was not received after allowing for delayed processing, and I accept responsibility for this manual finding.";

    public static bool CanResolveFrom(int state) => state is 2 or 5; // Sending or OutcomeUnknown.

    public static bool CanAdvanceDispatch(int before, int after) => before switch
    {
        1 => after is 2 or 6,      // Queued -> Sending or CancelledBeforeSend.
        2 => after is 3 or 4 or 5 or 7,
        5 => after is 3 or 4 or 7,
        _ => false
    };

    public static bool TryParseDecision(string? value, out ClaimMdReconciliationDecision decision) =>
        Enum.TryParse(value, false, out decision) &&
        value is (nameof(ClaimMdReconciliationDecision.ConfirmedReceived) or
                  nameof(ClaimMdReconciliationDecision.ConfirmedNotReceived));

    public static bool TryParseEvidenceKind(string? value, out ClaimMdEvidenceKind kind) =>
        Enum.TryParse(value, false, out kind) &&
        value is (nameof(ClaimMdEvidenceKind.AccountFileRecord) or nameof(ClaimMdEvidenceKind.SupportCase));

    /// <summary>Returns a safe error code; null means the human evidence is internally consistent.</summary>
    public static string? Validate(
        ClaimMdReconciliationManifestDto source, ReconcileClaimMdDispatchRequest request, DateTime nowUtc)
    {
        if (request.ExpectedRevision != source.Revision ||
            request.ExpectedAccountId != source.AccountId ||
            request.EvidenceAccountNumber != source.AccountNumber ||
            request.ExpectedEdiGenerationId != source.EdiGenerationId ||
            request.ExpectedContentSha256 != source.ContentSha256 ||
            request.EvidenceFileName != source.FileName)
            return "source_changed";
        if (!TryParseDecision(request.Decision, out var decision) ||
            !TryParseEvidenceKind(request.EvidenceKind, out var kind))
            return "evidence_invalid";
        if (request.EvidenceReference is not { Length: >= 6 and <= 120 } reference ||
            !reference.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':') ||
            !IsSha256(request.EvidenceSha256) ||
            request.EvidenceObservedAtUtc.Kind != DateTimeKind.Utc ||
            request.EvidenceObservedAtUtc > nowUtc.AddMinutes(5) ||
            request.EvidenceObservedAtUtc < source.EvidenceNotBeforeUtc)
            return "evidence_invalid";

        var claims = request.Claims ?? [];
        if (decision == ClaimMdReconciliationDecision.ConfirmedNotReceived)
            return kind != ClaimMdEvidenceKind.SupportCase ||
                   request.Attestation != NotReceivedAttestation ||
                   request.ExternalFileId is not null ||
                   request.AcceptedClaimCount is not null ||
                   request.RejectedClaimCount is not null || claims.Count != 0
                ? "absence_evidence_insufficient" : null;

        if (request.Attestation != ReceivedAttestation ||
            request.ExternalFileId is not { Length: >= 1 and <= 20 } fileId ||
            !fileId.All(char.IsAsciiDigit) ||
            source.Claims.Count is < 1 or > 2000 ||
            request.AcceptedClaimCount is null or < 0 || request.RejectedClaimCount is null or < 0 ||
            claims.Count != source.Claims.Count ||
            claims.Count != request.AcceptedClaimCount + request.RejectedClaimCount ||
            claims.Any(claim => claim is null) ||
            claims.Count(claim => claim.Status == "A") != request.AcceptedClaimCount ||
            claims.Count(claim => claim.Status == "R") != request.RejectedClaimCount ||
            claims.Any(claim => claim.Status is not ("A" or "R")))
            return "receipt_evidence_insufficient";
        var expected = source.Claims.Select(claim => (claim.ClaimReference, claim.RemoteClaimId))
            .ToHashSet();
        var observed = claims.Select(claim => (claim.ClaimReference, claim.RemoteClaimId)).ToHashSet();
        return observed.Count == claims.Count && expected.SetEquals(observed)
            ? null : "claim_identity_mismatch";
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
