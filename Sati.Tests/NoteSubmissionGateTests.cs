using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class NoteSubmissionGateTests
{
    [Fact]
    public void UnsupportedRequirementBitsCannotClearLoggedSubmission()
    {
        var today = new DateTime(2026, 9, 11);
        var result = NoteSubmissionGate.Evaluate(NoteWorkflow.Logged, null,
            [new ComplianceFormSnapshot("PCP", today.AddDays(-1), null)], today, today,
            (BillingComplianceRequirements)(1 << 20));

        Assert.False(result.Passed);
        Assert.Contains("configuration", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NoteWorkflow.Pending)]
    [InlineData(NoteWorkflow.HeldForCompliance)]
    [InlineData(NoteWorkflow.ComplianceBlocked)]
    public void InvalidConfigurationDoesNotPreventRetainingServiceDocumentation(int status)
    {
        var today = new DateTime(2026, 9, 11);
        var result = NoteSubmissionGate.Evaluate(status, null,
            [new ComplianceFormSnapshot("PCP", today.AddDays(-1), null)], today, today,
            (BillingComplianceRequirements)(1 << 20));

        Assert.True(result.Passed);
    }
}
