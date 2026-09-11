using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class StrictClaimResponseReaderTests
{
    internal static string Wrap(string type, string body, string usage = "T", string control = "000000123")
    {
        var (group, version) = type switch
        {
            "999" => ("FA", "005010X231A1"),
            "277" => ("HN", "005010X214"),
            "837" => ("HC", "005010X222A1"),
            _ => ("HP", "005010X221A1")
        };
        var count = body.Count(c => c == '~') + 2;
        return $"ISA*00*          *00*          *ZZ*CLEARINGHOUSE  *ZZ*SATITEST1      *260910*1200*^*00501*{control}*0*{usage}*:~\n"
               + $"GS*{group}*CLEARINGHOUSE*SATITEST1*20260910*1200*1*X*{version}~\n"
               + $"ST*{type}*0001*{version}~\n{body}SE*{count}*0001~GE*1*1~IEA*1*{control}~";
    }

    internal static string Ack277(string category) => Wrap("277",
        "BHT*0085*08*ACK-1*20260910*1200*TH~"
        + "HL*1**20*1~NM1*PR*2*SYNTHETIC PAYER*****PI*MCDME~"
        + "HL*2*1*21*1~NM1*41*2*EXAMPLE AGENCY*****46*SATITEST1~"
        + "TRN*2*RECEIVER-TRACE~STC*A1:19:PR*20260910*WQ*100~"
        + "HL*3*2*19*1~NM1*85*2*EXAMPLE AGENCY*****XX*1999999984~"
        + "HL*4*3*PT*0~NM1*QC*1*EXAMPLE*ALEX****MI*987654321~"
        + $"TRN*2*77-900~STC*{category}:21:85*20260910*{(category is "A0" or "A1" or "A2" ? "WQ" : "U")}*100~");

    internal static string Remittance(string billed = "100", string paid = "100", string extra = "") => Wrap("835",
        "BPR*I*100*C*ACH************20260910~TRN*1*PAY-1*1999999984~"
        + "N1*PR*SYNTHETIC PAYER*XV*MCDME~N1*PE*EXAMPLE AGENCY*XX*1999999984~"
        + $"CLP*77-900*1*{billed}*{paid}*0*MC*PAYER-1*11*1~{extra}");

    [Fact]
    public void A7RejectionMustNeverBeAcceptedBecauseItsCategoryStartsWithA()
    {
        Assert.Equal(BillingSubmissionStage.ClaimRejected,
            ClaimResponseReader.ReadAcknowledgement(Ack277("A7")).Stage);
    }

    [Fact]
    public void InvalidMoneyMustNeverBecomeZeroAndARecordedOutcome()
    {
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadRemittance(Remittance(paid: "invalid")));
    }

    [Theory]
    [InlineData("A0", ClaimAcknowledgementDisposition.Received)]
    [InlineData("A1", ClaimAcknowledgementDisposition.Received)]
    [InlineData("A2", ClaimAcknowledgementDisposition.Accepted)]
    [InlineData("A3", ClaimAcknowledgementDisposition.Rejected)]
    [InlineData("A6", ClaimAcknowledgementDisposition.Rejected)]
    [InlineData("A7", ClaimAcknowledgementDisposition.Rejected)]
    [InlineData("A8", ClaimAcknowledgementDisposition.Rejected)]
    [InlineData("A4", ClaimAcknowledgementDisposition.NeedsReview)]
    [InlineData("A5", ClaimAcknowledgementDisposition.NeedsReview)]
    [InlineData("ZZ", ClaimAcknowledgementDisposition.NeedsReview)]
    public void CategoryMeaningIsExplicitAndOnlyPatientTracesBecomeClaims(string category, ClaimAcknowledgementDisposition expected)
    {
        var row = Assert.Single(ClaimResponseReader.ReadDocument(Ack277(category)).ClaimAcknowledgements);
        Assert.Equal("77-900", row.ClaimReference);
        Assert.Equal(expected, row.Disposition);
        Assert.Equal(100, row.BilledAmount);
    }

    [Fact]
    public void ReceiptAloneCannotUseTheHistoricAcceptedStage()
    {
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadAcknowledgement(Ack277("A1")));
    }

    [Fact]
    public void UnknownResponseIsaControlDoesNotOverwriteOriginal999Identity()
    {
        var document = Wrap("999", "AK1*HC*54321*005010X222A1~AK2*837*0042*005010X222A1~IK5*A~AK9*A*1*1*1~", control: "900000456");
        var parsed = ClaimResponseReader.ReadDocument(document);
        Assert.Equal("900000456", parsed.Envelope.ControlNumber);
        Assert.Equal("54321", parsed.FunctionalAcknowledgement!.OriginalGroupControlNumber);
        Assert.Equal("0042", Assert.Single(parsed.FunctionalAcknowledgement.OriginalTransactionControlNumbers));
        Assert.Equal(BillingSubmissionStage.FunctionalAccepted, parsed.FunctionalAcknowledgement.Stage);
    }

    [Theory]
    [InlineData("AK1*HC*42~AK2*837*0001~IK5*R~AK9*A*1*1*1~")]
    [InlineData("AK1*HC*42~AK2*837*0001~IK5*A~AK9*R*1*1*0~")]
    [InlineData("AK1*HC*42~AK9*E*1*1*0~")]
    [InlineData("AK1*HC*42~AK9*P*2*2*1~")]
    [InlineData("AK1*HC*42~AK2*270*0001~IK5*A~AK9*A*1*1*1~")]
    [InlineData("AK1*HC*42~AK2*837*0001~AK2*837*0002~IK5*A~AK9*A*1*1*1~")]
    public void ContradictoryOrUnsupported999CannotBecomeAGroupVerdict(string body) =>
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadDocument(Wrap("999", body)));

    [Fact]
    public void GroupOnlyAccepted999RetainsGroupIdentityWithoutInventingATransaction()
    {
        var functional = ClaimResponseReader.ReadDocument(Wrap("999", "AK1*HC*42~AK9*A*1*1*1~")).FunctionalAcknowledgement!;
        Assert.Equal("42", functional.OriginalGroupControlNumber);
        Assert.Empty(functional.OriginalTransactionControlNumbers);
    }

    [Theory]
    [InlineData("1,000.00")]
    [InlineData("1e2")]
    [InlineData("NaN")]
    [InlineData("+100")]
    [InlineData("100.001")]
    [InlineData("9999999999999999999999999999")]
    [InlineData("")]
    public void AmbiguousOrOutOfRangeMoneyRefusesTheWholeAdvice(string value) =>
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadRemittance(Remittance(paid: value)));

    [Fact]
    public void DifferentLegalDelimitersPreserveStatusAndSemanticDigest()
    {
        var ordinary = Ack277("A2");
        var alternate = ordinary.Replace('*', '|').Replace(':', '>').Replace('~', '!').Replace('^', '?');
        var parsed = ClaimResponseReader.ReadDocument(alternate);
        Assert.Equal(ClaimAcknowledgementDisposition.Accepted, Assert.Single(parsed.ClaimAcknowledgements).Disposition);
        Assert.Equal(ClaimResponseReader.ReadDocument(ordinary).CanonicalTransaction, parsed.CanonicalTransaction);
    }

    [Fact]
    public void ReenvelopingChangesRawIdentityButNotSemanticDigest()
    {
        var first = Remittance();
        var second = first.Replace("000000123", "900000987").Replace("*0001", "*0042");
        Assert.NotEqual(first, second);
        Assert.Equal(ClaimResponseReader.ReadDocument(first).CanonicalTransaction, ClaimResponseReader.ReadDocument(second).CanonicalTransaction);
    }

    [Theory]
    [InlineData("ISA15")]
    [InlineData("IEA02")]
    [InlineData("GE02")]
    [InlineData("SE01")]
    [InlineData("SE02")]
    [InlineData("Delimiter")]
    [InlineData("MissingTrailer")]
    [InlineData("ExtraTransaction")]
    [InlineData("ExtraInterchange")]
    [InlineData("EmbeddedNewline")]
    [InlineData("ControlCharacter")]
    [InlineData("Unicode")]
    [InlineData("RepeatedElement")]
    public void MalformedEnvelopesFailClosed(string defect)
    {
        var document = Remittance();
        document = defect switch
        {
            "ISA15" => document.Replace("*0*T*:", "*0*X*:"),
            "IEA02" => document.Replace("IEA*1*000000123", "IEA*1*900000000"),
            "GE02" => document.Replace("GE*1*1", "GE*1*2"),
            "SE01" => document.Replace("SE*7*0001", "SE*6*0001"),
            "SE02" => document.Replace("SE*7*0001", "SE*7*0042"),
            "Delimiter" => document.Replace("*0*T*:", "*0*T**"),
            "MissingTrailer" => document[..document.IndexOf("IEA", StringComparison.Ordinal)],
            "ExtraTransaction" => document.Replace("GE*1*1", "ST*835*0002~SE*2*0002~GE*2*1"),
            "ExtraInterchange" => document + document,
            "EmbeddedNewline" => document.Replace("PAY-1", "PAY\n-1"),
            "ControlCharacter" => document.Replace("PAY-1", "PAY\0-1"),
            "Unicode" => document.Replace("PAY-1", "PAY-é"),
            _ => document.Replace("PAY-1", "PAY^OTHER")
        };
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadDocument(document));
    }

    [Fact]
    public void OversizedDocumentRejectedBeforeSplitting()
    {
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadDocument(new string('X', ClaimResponseReader.MaximumDocumentCharacters + 1)));
    }

    [Theory]
    [InlineData("MissingTrace")]
    [InlineData("DuplicateTrace")]
    [InlineData("BadParent")]
    [InlineData("MissingClaimStatus")]
    [InlineData("ConflictingAction")]
    public void UnboundOrContradictory277ClaimDetailsAreRejected(string defect)
    {
        var document = Ack277("A2");
        document = defect switch
        {
            "MissingTrace" => document.Replace("TRN*2*77-900~", "REF*1K*77-900~"),
            "DuplicateTrace" => document.Replace("SE*15*0001", "TRN*2*77-900~STC*A2:20:PR*20260910*WQ*100~SE*17*0001"),
            "BadParent" => document.Replace("HL*4*3*PT", "HL*4*2*PT"),
            "MissingClaimStatus" => document.Replace("STC*A2:21:85", "REF*A2:21:85"),
            _ => document.Replace("A2:21:85*20260910*WQ", "A2:21:85*20260910*U")
        };
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadDocument(document));
    }

    [Fact]
    public void ProviderAdjustmentsHaveRawSubtractiveSignsIncludingInterest()
    {
        var body = "BPR*I*110*C*ACH************20260910~TRN*1*PAY-1*1999999984~N1*PR*PAYER*XV*MCDME~N1*PE*AGENCY*XX*1999999984~"
            + "CLP*77-900*1*100*100*0*MC*P1~PLB*1999999984*20260910*WO:TAKEBACK*10*L6:INTEREST*-20~";
        var parsed = ClaimResponseReader.ReadRemittance(Wrap("835", body));
        Assert.Equal(-10, parsed.ProviderLevelAdjustment);
        Assert.Equal(110, parsed.RemittancePaymentAmount);
        Assert.Equal(DepositReconciliationStatus.AwaitingEft,
            DepositReconciliationRules.GetStatus(parsed.ClaimPaymentTotal, -parsed.ProviderLevelAdjustment, parsed.RemittancePaymentAmount, null));
    }

    [Fact]
    public void PatientResponsibilityComesFromPRWithoutDoubleCountingClaimAdjustments()
    {
        var body = "BPR*I*70*C*ACH************20260910~TRN*1*PAY-1*1999999984~N1*PR*PAYER*XV*MCDME~N1*PE*AGENCY*XX*1999999984~"
            + "CLP*77-900*1*100*70*10*MC*P1~CAS*CO*45*20~CAS*PR*1*10~";
        var claim = Assert.Single(ClaimResponseReader.ReadRemittance(Wrap("835", body)).Claims);
        Assert.Equal(30, claim.AdjustmentAmount);
        Assert.Equal(10, claim.PatientResponsibilityAmount);
        Assert.Equal(RemittanceClaimStatus.PartiallyPaid, claim.Status);
    }

    [Theory]
    [InlineData("BPR*I*100", "BPR*I*99")]
    [InlineData("*C*ACH", "*D*ACH")]
    [InlineData("*C*ACH", "*C*NON")]
    [InlineData("20260910~TRN", "20260230~TRN")]
    [InlineData("TRN*1*PAY-1", "TRN*2*PAY-1")]
    [InlineData("N1*PE*EXAMPLE AGENCY*XX", "N1*PE*EXAMPLE AGENCY*FI")]
    [InlineData("CLP*77-900*1*100*100*0", "CLP*77-900*1*100*100*5")]
    [InlineData("CLP*77-900*1*100*100", "CLP*77-900*4*100*100")]
    [InlineData("CLP*77-900*1*100*100", "CLP*77-900*99*100*100")]
    public void RemittanceFinancialContradictionsRefusePosting(string original, string replacement) =>
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadRemittance(Remittance().Replace(original, replacement)));

    [Theory]
    [InlineData("CAS*CO*45*invalid~")]
    [InlineData("CAS*CO*45~")]
    [InlineData("CAS*CO*45*1~")]
    [InlineData("PLB*1999999984*20260910*WO:REF~")]
    [InlineData("PLB*1999999984*20260910*WO:REF*invalid~")]
    [InlineData("CLP*77-900*1*100*100*0*MC*P2~")]
    public void MalformedAdjustmentAndDuplicateClaimCannotBeIgnored(string extra) =>
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadRemittance(Remittance(extra: extra)));

    [Fact]
    public void Retained837RequiresUniqueClaimReferencesAndRetainsServiceIdentity()
    {
        var body = "BHT*0019*00*REF*20260910*1200*CH~NM1*85*2*AGENCY*****XX*1999999984~"
            + "CLM*123456789-77-900*100***11::1*Y*A*Y*I~LX*1~SV1*HC:G9012*100*UN*1~REF*6R*900~";
        var parsed = ClaimResponseReader.ReadSubmission(Wrap("837", body));
        Assert.Equal("1999999984", parsed.BillingProviderNpi);
        var claim = Assert.Single(parsed.Claims);
        Assert.Equal("123456789-77-900", claim.ClaimReference);
        Assert.Equal("900", Assert.Single(claim.ServiceLineReferences));
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadSubmission(Wrap("837", body + "CLM*123456789-77-900*100~")));
    }

    [Fact]
    public void OverpaymentNeedsReviewAndNeverClaimsToBeAnUnderpayment()
    {
        var advice = Remittance(paid: "110", extra: "CAS*CO*45*-10~").Replace("BPR*I*100", "BPR*I*110");
        var claim = Assert.Single(ClaimResponseReader.ReadRemittance(advice).Claims);
        Assert.Equal(RemittanceClaimStatus.NeedsReview, claim.Status);
        Assert.Contains("exceeds", claim.Explanation);
        Assert.DoesNotContain("below", claim.Explanation);
    }

    [Fact]
    public void UnsupportedFinancialSegmentsCannotBeSilentlyDropped()
    {
        Assert.Throws<FormatException>(() => ClaimResponseReader.ReadRemittance(Remittance(extra: "MOA***UNSUPPORTED~")));
    }
}
