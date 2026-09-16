using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class BillingExportGateTests
{
    private static readonly DateTime ServiceDate = new(2026, 8, 12);
    private static readonly ProfessionalClaimSnapshot Frozen = new(1, 2, 201,
        "Synthetic", "Person", new DateTime(1990, 1, 1), "U", "123456", "Test Street", "Bangor", "ME", "04401",
        "Synthetic Agency", "1999999984", "111111111", "Test Street", "Bangor", "ME", "04401", "TEST",
        "Billing", "2075550101", "Synthetic Payer", "99999");
    private static readonly BillingExportSource Source = new(201, NoteWorkflow.Approved,
        ServiceDate, false, null, 21, ServiceDate, null, null, false);

    // Release re-checks the caller's service-date decision rather than forming a
    // second one. That decision already accounts for exact-obligation exceptions
    // and Admin recovery, so a recovered note stays releasable here.
    [Fact]
    public void CallerComplianceErrorsBlockReleaseAndAreReportedVerbatim()
    {
        var blocked = BillingExportGate.Evaluate(Frozen, 2, ServiceDate, false, null,
            Source, ["PCP was due Aug 1, 2026 and was not completed as of this service date."],
            BillingComplianceGate.DefaultRequirements);

        Assert.Contains(blocked, error => error.Contains("PCP was due", StringComparison.Ordinal));
        Assert.Empty(BillingExportGate.Evaluate(Frozen, 2, ServiceDate, false, null,
            Source, [], BillingComplianceGate.DefaultRequirements));
    }

    [Theory]
    [InlineData("missing-approver")]
    [InlineData("foreign-approver")]
    [InlineData("different-approval")]
    [InlineData("missing-time")]
    [InlineData("different-time")]
    [InlineData("different-reason")]
    [InlineData("not-approved")]
    [InlineData("wrong-person")]
    [InlineData("wrong-date")]
    public void StoredExceptionNeverBypassesSourceIntegrity(string change)
    {
        var exception = Source with { ComplianceOverride = true, OverrideReason = "Recorded reason",
            OverrideApprovedById = 21, OverrideApprovedAt = ServiceDate, OverrideApproverInAgency = true };
        exception = change switch
        {
            "missing-approver" => exception with { OverrideApprovedById = null },
            "foreign-approver" => exception with { OverrideApproverInAgency = false },
            "different-approval" => exception with { ApprovedById = 22 },
            "missing-time" => exception with { OverrideApprovedAt = null },
            "different-time" => exception with { OverrideApprovedAt = ServiceDate.AddDays(1) },
            "different-reason" => exception with { OverrideReason = "New reason" },
            "not-approved" => exception with { Status = NoteWorkflow.Logged },
            "wrong-person" => exception with { PersonId = 202 },
            "wrong-date" => exception with { ServiceDate = ServiceDate.AddDays(1) },
            _ => throw new InvalidOperationException()
        };
        Assert.NotEmpty(BillingExportGate.Evaluate(Frozen, 2, ServiceDate, true, "Recorded reason",
            exception, [], BillingComplianceGate.DefaultRequirements));
    }

    [Fact]
    public void InvalidConfigurationFailsClosedEvenForAnException()
    {
        var exception = Source with { ComplianceOverride = true, OverrideReason = "Recorded reason",
            OverrideApprovedById = 21, OverrideApprovedAt = ServiceDate, OverrideApproverInAgency = true };
        Assert.NotEmpty(BillingExportGate.Evaluate(Frozen, 2, ServiceDate, true, "Recorded reason",
            exception, [], (BillingComplianceRequirements)int.MaxValue));
    }
}
