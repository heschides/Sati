namespace Sati.Contracts.V1;

/// <summary>What an agency administrator reviewed before selecting a feed's first cursor.</summary>
public enum ClaimMdFeedReviewKind
{
    VerifiedEmpty = 1,
    ExistingReconciled = 2
}

public sealed record ClaimMdFeedStartRequest(
    string Cursor,
    ClaimMdFeedReviewKind ReviewKind,
    string EvidenceReference);

/// <summary>
/// Server-side Demo setup metadata. SecretReference is an API-host environment-variable name,
/// never a Claim.MD AccountKey. Agency scope comes only from the authenticated actor.
/// </summary>
public sealed record ClaimMdTestAccountOnboardingRequest(
    Guid AccountId,
    long ExpectedRevision,
    string ExternalAccountNumber,
    string ClaimNamespace,
    string SecretReference,
    bool DedicatedTestAccountConfirmed,
    string TestAccountEvidenceReference,
    bool RemoteClaimIdOnlyDuplicateFieldConfirmed,
    string DuplicateFieldEvidenceReference,
    ClaimMdFeedStartRequest StatusFeed,
    ClaimMdFeedStartRequest EraFeed);

public sealed record ClaimMdTestAccountOnboardingDto(
    Guid AccountId,
    string ExternalAccountNumber,
    string ClaimNamespace,
    string StatusCursor,
    string EraCursor,
    long Revision,
    bool AlreadyProvisioned);

/// <summary>Input boundaries for the reviewed, one-time Claim.MD test-account setup.</summary>
public static class ClaimMdOnboardingRules
{
    public static Dictionary<string, string[]> Validate(ClaimMdTestAccountOnboardingRequest? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request is null)
        {
            errors["request"] = ["Provide the reviewed Claim.MD test-account setup."];
            return errors;
        }
        if (request.AccountId == Guid.Empty)
            errors["accountId"] = ["Choose a permanent nonempty account ID."];
        if (request.ExpectedRevision != 0)
            errors["expectedRevision"] = ["A new account must start at revision zero."];
        try
        {
            _ = TradingPartnerProfile.ClaimMd(request.ExternalAccountNumber, request.ClaimNamespace);
            if (!string.Equals(request.ExternalAccountNumber,
                    request.ExternalAccountNumber.ToUpperInvariant(), StringComparison.Ordinal))
                errors["externalAccountNumber"] = ["Use the account number's canonical uppercase spelling."];
        }
        catch (ArgumentException)
        {
            errors["externalAccountNumber"] = ["Provide a valid account number and permanent uppercase claim namespace."];
        }
        if (!request.DedicatedTestAccountConfirmed || !IsEvidenceReference(request.TestAccountEvidenceReference))
            errors["testAccountEvidenceReference"] = ["Confirm the dedicated test account with an opaque evidence ID."];
        if (!request.RemoteClaimIdOnlyDuplicateFieldConfirmed ||
            !IsEvidenceReference(request.DuplicateFieldEvidenceReference))
            errors["duplicateFieldEvidenceReference"] =
                ["Confirm the vendor's remote_claimid-only duplicate setting with an opaque evidence ID."];
        ValidateFeed("statusFeed", request.StatusFeed, errors);
        ValidateFeed("eraFeed", request.EraFeed, errors);
        if (request.StatusFeed is not null && request.EraFeed is not null &&
            string.Equals(request.StatusFeed.EvidenceReference, request.EraFeed.EvidenceReference,
                StringComparison.Ordinal))
            errors["eraFeed.evidenceReference"] = ["Review status and ERA feeds with separate evidence IDs."];
        return errors;
    }

    public static bool IsCanonicalCursor(string? cursor) =>
        cursor is { Length: >= 1 and <= 20 } &&
        cursor.All(char.IsAsciiDigit) &&
        (cursor.Length == 1 || cursor[0] != '0');

    private static void ValidateFeed(string name, ClaimMdFeedStartRequest? feed,
        Dictionary<string, string[]> errors)
    {
        if (feed is null)
        {
            errors[name] = ["Provide a separately reviewed feed cursor and evidence ID."];
            return;
        }
        if (!IsCanonicalCursor(feed.Cursor) ||
            feed.ReviewKind is not (ClaimMdFeedReviewKind.VerifiedEmpty or ClaimMdFeedReviewKind.ExistingReconciled) ||
            feed.ReviewKind == ClaimMdFeedReviewKind.VerifiedEmpty && feed.Cursor != "0" ||
            feed.ReviewKind == ClaimMdFeedReviewKind.ExistingReconciled && feed.Cursor == "0")
            errors[$"{name}.cursor"] =
                ["Use 0 only for a verified-empty feed; use its reviewed positive high-water ID for an existing feed."];
        if (!IsEvidenceReference(feed.EvidenceReference))
            errors[$"{name}.evidenceReference"] = ["Provide an opaque, bounded review evidence ID."];
    }

    private static bool IsEvidenceReference(string? value) =>
        value is { Length: >= 8 and <= 64 } &&
        (value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') &&
        value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
