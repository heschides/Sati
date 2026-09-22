using Sati.Contracts.V1;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationChangeReviewRowTests
{
    [Fact]
    public void LatestCorrectionClearsTheCurrentHoldWithoutErasingHistory()
    {
        var recorded = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var corrected = Flag(recorded, new DateTime(2026, 9, 3),
            FormAttestationBillingHoldReason.None);
        var earlierRevocation = Flag(recorded, null,
            FormAttestationBillingHoldReason.MissingAttestation);

        var rows = FormAttestationChangeReviewRow.Latest([corrected, earlierRevocation]);

        var row = Assert.Single(rows);
        Assert.False(row.MustHoldBilling);
        Assert.Contains("No current form-date billing hold", row.HoldLabel);
    }

    private static FormAttestationChangeReviewFlagDto Flag(
        DateTime recorded, DateTime? revised, FormAttestationBillingHoldReason hold) =>
        new(Guid.NewGuid(), 1, 2, 3, null, new DateTime(2026, 9, 3),
            new DateTime(2026, 9, 3), new DateTime(2026, 9, 4), revised,
            "Corrected source date", true, true, hold, recorded);
}
