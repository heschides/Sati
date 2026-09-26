using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>Generation identity and stable remote claim identity for 837P submissions.</summary>
public static class ClaimSubmissionIdentity
{
    public static string RequireControlNumber(string controlNumber)
    {
        if (controlNumber is null || controlNumber.Length != 9 ||
            controlNumber.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("An interchange control number must contain exactly nine digits.", nameof(controlNumber));
        return controlNumber;
    }

    public static string ClaimReference(string controlNumber, int billingPeriodId, int noteId)
    {
        RequireControlNumber(controlNumber);
        if (billingPeriodId <= 0 || noteId <= 0)
            throw new ArgumentException("A claim requires persisted billing-period and service-note identities.");
        // Nine digits plus two positive Int32 identifiers fits CLM01's 38-character limit.
        return string.Create(CultureInfo.InvariantCulture, $"{controlNumber}-{billingPeriodId}-{noteId}");
    }

    public static string RemoteClaimId(string claimNamespace, int agencyId, int billingPeriodId, int noteId)
    {
        if (string.IsNullOrEmpty(claimNamespace) || claimNamespace.Length > 8 ||
            claimNamespace.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
            throw new ArgumentException("A stable claim namespace is required.", nameof(claimNamespace));
        if (agencyId <= 0 || billingPeriodId <= 0 || noteId <= 0)
            throw new ArgumentException("A remote claim requires persisted agency, billing-period, and service-note identities.");
        // At most 47 characters with three positive Int32 identifiers, within REF02's limit.
        return string.Create(CultureInfo.InvariantCulture,
            $"SATI1-{claimNamespace}-{agencyId}-{billingPeriodId}-{noteId}");
    }
}
