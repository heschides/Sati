using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>One identity convention for both retained 837P generators.</summary>
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
}
