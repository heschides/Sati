using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>Desktop reads use the same strict rules as the API. All fixtures contain complete envelopes.</summary>
public sealed class ClaimResponseReaderTests
{
    private static string Interchange(string type, string body, string usage = "T")
    {
        var (group, version) = type switch
        {
            "999" => ("FA", "005010X231A1"),
            "277" => ("HN", "005010X214"),
            _ => ("HP", "005010X221A1")
        };
        return $"ISA*00*          *00*          *ZZ*SUBMITTER      *ZZ*RECEIVER       *260830*1200*^*00501*000000123*0*{usage}*:~\n"
            + $"GS*{group}*SUBMITTER*RECEIVER*20260830*1200*1*X*{version}~ST*{type}*0001*{version}~"
            + body + $"SE*{body.Count(c => c == '~') + 2}*0001~GE*1*1~IEA*1*000000123~";
    }

    private static string Ack277(params string[] categories)
    {
        var body = "BHT*0085*08*REF*20260830*1200*TH~HL*1**20*1~HL*2*1*21*1~HL*3*2*19*1~";
        for (var i = 0; i < categories.Length; i++)
            body += $"HL*{i + 4}*3*PT*0~TRN*2*CLAIM-{i}~STC*{categories[i]}:21:85*20260830*{(categories[i] == "A2" ? "WQ" : "U")}*100~";
        return Interchange("277", body);
    }

    [Theory]
    [InlineData("T", true)]
    [InlineData("P", false)]
    public void TheUsageIndicatorDecidesWhetherTheInterchangeIsATest(string indicator, bool expected)
    {
        var envelope = ClaimResponseReader.ReadEnvelope(Interchange("835", "BPR*H*0*C*NON************20260830~", indicator));
        Assert.Equal(expected, envelope.IsTestInterchange);
        Assert.Equal(ClaimResponseKind.RemittanceAdvice, envelope.Kind);
        Assert.Equal("000000123", envelope.ControlNumber);
    }

    [Fact]
    public void AnUnrecognisedTransactionIsNotGuessedAt()
    {
        var document = Interchange("270", "BHT*REF~");
        Assert.Equal(ClaimResponseKind.Unrecognised, ClaimResponseReader.ReadEnvelope(document).Kind);
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadDocument(document));
    }

    [Theory]
    [InlineData("A", 1, BillingSubmissionStage.FunctionalAccepted)]
    [InlineData("E", 1, BillingSubmissionStage.FunctionalAccepted)]
    [InlineData("R", 0, BillingSubmissionStage.FunctionalRejected)]
    public void A999IsReadAsSyntaxAcceptedOrRejected(string code, int accepted, BillingSubmissionStage expected)
    {
        var body = $"AK1*HC*1*005010X222A1~AK2*837*0001*005010X222A1~IK5*{code}~AK9*{code}*1*1*{accepted}~";
        Assert.Equal(expected, ClaimResponseReader.ReadAcknowledgement(Interchange("999", body)).Stage);
    }

    [Fact]
    public void EveryClaimAcceptedIsClaimAccepted() =>
        Assert.Equal(BillingSubmissionStage.ClaimAccepted, ClaimResponseReader.ReadAcknowledgement(Ack277("A2", "A2")).Stage);

    [Fact]
    public void EveryClaimRejectedIsClaimRejected() =>
        Assert.Equal(BillingSubmissionStage.ClaimRejected, ClaimResponseReader.ReadAcknowledgement(Ack277("A7", "A3")).Stage);

    [Fact]
    public void AMixedAcknowledgementIsPartialAndSaysHowMany()
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Ack277("A2", "A7", "A2"));
        Assert.Equal(BillingSubmissionStage.PartiallyAccepted, result.Stage);
        Assert.Contains("2 of 3", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAcknowledgementWithNoClaimStatusIsNotTreatedAsAccepted() =>
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadAcknowledgement(Ack277()));

    [Fact]
    public void An835IsReadIntoPerClaimOutcomesAndBalancedDepositTotals()
    {
        var advice = Interchange("835",
            "BPR*I*230.00*C*ACH************20260830~TRN*1*PAYMENT-9001*1999999984~"
            + "N1*PR*SYNTHETIC PAYER*XV*MCDME~N1*PE*EXAMPLE AGENCY*XX*1999999984~"
            + "CLP*CLAIM-1*1*200*200*0*MC*SYN1~"
            + "CLP*CLAIM-2*1*200*150*10*MC*SYN2~CAS*CO*45*40~CAS*PR*1*10~"
            + "CLP*CLAIM-3*4*200*0*0*MC*SYN3~CAS*CO*29*200~"
            + "CLP*CLAIM-4*22*-100*-100*0*MC*SYN4~"
            + "PLB*1999999984*20261231*WO:REF9*20~");
        var remittance = ClaimResponseReader.ReadRemittance(advice);
        Assert.Equal(4, remittance.Claims.Count);
        Assert.Equal("PAYMENT-9001", remittance.PaymentReference);
        Assert.Equal(new DateTime(2026, 8, 30), remittance.PaymentDate);
        Assert.Equal(250, remittance.ClaimPaymentTotal);
        Assert.Equal(20, remittance.ProviderLevelAdjustment);
        Assert.Equal(230, remittance.RemittancePaymentAmount);
        Assert.Equal(RemittanceClaimStatus.Paid, remittance.Claims[0].Status);
        Assert.Equal(RemittanceClaimStatus.PartiallyPaid, remittance.Claims[1].Status);
        Assert.Equal(RemittanceClaimStatus.Denied, remittance.Claims[2].Status);
        Assert.Equal(RemittanceClaimStatus.Reversed, remittance.Claims[3].Status);
        Assert.Equal(50, remittance.Claims[1].AdjustmentAmount);
        Assert.Equal(10, remittance.Claims[1].PatientResponsibilityAmount);
    }

    [Fact]
    public void ProcessedWithNoPaymentNeedsReviewRatherThanReadingAsDenied()
    {
        var advice = Interchange("835",
            "BPR*H*0*C*NON************20260830~TRN*1*PAYMENT-9002*1999999984~"
            + "N1*PR*SYNTHETIC PAYER*XV*MCDME~N1*PE*EXAMPLE AGENCY*XX*1999999984~"
            + "CLP*CLAIM-9*1*150*0*0*MC*SYN9~CAS*CO*45*150~");
        Assert.Equal(RemittanceClaimStatus.NeedsReview, Assert.Single(ClaimResponseReader.ReadRemittance(advice).Claims).Status);
    }
}
