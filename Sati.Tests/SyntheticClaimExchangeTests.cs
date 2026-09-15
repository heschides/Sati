using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Exercises Sati's outbound claim with a deterministic, in-memory clearinghouse/payer double.
/// This is not an X12 validator, payer certification, or network transport test.
/// </summary>
public sealed class SyntheticClaimExchangeTests
{
    [Fact]
    public void Test837ReceivesAcknowledgementsAndABalancedSynthetic835()
    {
        var generatedAt = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Local);
        var claim = CreateSubmittedPeriod();
        var edi837 = EdiGenerator.Generate(claim, isTest: true, generatedAt, "123456789");

        var submission = ClaimResponseReader.ReadSubmission(edi837);
        var submittedClaim = Assert.Single(submission.Claims);
        var response = MockClearinghouse.Respond(edi837, MockClearinghouseScenario.PartialPayment,
            generatedAt.AddMinutes(2).ToUniversalTime());
        var functional = ClaimResponseReader.ReadDocument(response.FunctionalAcknowledgement);
        var acknowledgement = ClaimResponseReader.ReadDocument(response.ClaimAcknowledgement!);
        var remittance = ClaimResponseReader.ReadRemittance(response.RemittanceAdvice!);

        Assert.Equal("123456789-77-99", submittedClaim.ClaimReference);
        Assert.Equal(submission.Envelope.GroupControlNumber, functional.FunctionalAcknowledgement!.OriginalGroupControlNumber);
        Assert.Equal(submission.Envelope.TransactionControlNumber,
            Assert.Single(functional.FunctionalAcknowledgement.OriginalTransactionControlNumbers));
        Assert.NotEqual(submission.Envelope.ControlNumber, functional.Envelope.ControlNumber);
        Assert.Equal(BillingSubmissionStage.FunctionalAccepted, functional.FunctionalAcknowledgement.Stage);
        var accepted = Assert.Single(acknowledgement.ClaimAcknowledgements);
        Assert.Equal(submittedClaim.ClaimReference, accepted.ClaimReference);
        Assert.Equal(ClaimAcknowledgementDisposition.Accepted, accepted.Disposition);
        Assert.Equal("99", Assert.Single(accepted.ServiceLineReferences));

        var outcome = Assert.Single(remittance.Claims);
        Assert.Equal(submittedClaim.ClaimReference, outcome.ClaimReference);
        Assert.Equal("99", Assert.Single(outcome.ServiceLineReferences));
        Assert.Equal(33.25m, outcome.BilledAmount);
        Assert.Equal(26.60m, outcome.PaidAmount);
        Assert.Equal(6.65m, outcome.AdjustmentAmount);
        Assert.Equal(outcome.BilledAmount, outcome.PaidAmount + outcome.AdjustmentAmount);
        Assert.Equal(DepositReconciliationStatus.AwaitingEft,
            DepositReconciliationRules.GetStatus(remittance.ClaimPaymentTotal, -remittance.ProviderLevelAdjustment,
                remittance.RemittancePaymentAmount, null));
    }

    [Fact]
    public void SimulatorRefusesAProduction837()
    {
        var generatedAt = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Local);
        var edi837 = EdiGenerator.Generate(
            CreateSubmittedPeriod(), isTest: false, generatedAt, "123456789");

        var error = Assert.Throws<InvalidOperationException>(() =>
            MockClearinghouse.Respond(edi837, MockClearinghouseScenario.PartialPayment,
                generatedAt.AddMinutes(2).ToUniversalTime()));

        Assert.Contains("test interchanges only", error.Message);
    }

    private static BillingPeriod CreateSubmittedPeriod()
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "SYNTHETIC PAYER", "MCDME");
        return new BillingPeriod
        {
            Id = 77,
            UserId = 12,
            Month = 8,
            Year = 2026,
            Status = BillingStatus.Submitted,
            Lines =
            [
                new ClaimLine
                {
                    Id = 88,
                    NoteId = 99,
                    DateOfService = new DateTime(2026, 8, 12),
                    ProcedureCode = "G9012",
                    ProcedureModifier = "HI",
                    Units = 1.33m,
                    ChargeAmount = 33.25m,
                    ClientMaineCareId = "987654321",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11,
                    ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot)
                }
            ]
        };
    }
}
