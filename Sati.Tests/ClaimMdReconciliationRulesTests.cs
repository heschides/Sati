using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ClaimMdReconciliationRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ClaimMdReconciliationManifestDto Source = new(
        Guid.NewGuid(), 2, Guid.NewGuid(), "CLAIMMDTEST", 45, "OutcomeUnknown",
        Now.AddMinutes(-20), Now.AddMinutes(-10),
        "837P.CMDTEST.txt", new string('A', 64),
        [new("123456789-1-7", "SATI1-TEST-1-1-7"),
         new("123456789-1-8", "SATI1-TEST-1-1-8")],
        ClaimMdReconciliationRules.ReceivedAttestation,
        ClaimMdReconciliationRules.NotReceivedAttestation);

    [Fact]
    public void ReceiptFindingRequiresTheExactSetOfVendorObservedClaimIdentitiesAndCounts()
    {
        var request = new ReconcileClaimMdDispatchRequest(Source.Revision, Source.AccountId,
            Source.AccountNumber, Source.EdiGenerationId, Source.ContentSha256, Source.FileName,
            "ConfirmedReceived", "AccountFileRecord",
            "FILE-123456", new string('B', 64), Now.AddMinutes(-1),
            ClaimMdReconciliationRules.ReceivedAttestation, "123456", 1, 1,
            [new("123456789-1-7", "SATI1-TEST-1-1-7", "A"),
             new("123456789-1-8", "SATI1-TEST-1-1-8", "R")]);

        Assert.Null(ClaimMdReconciliationRules.Validate(Source, request, Now));
        Assert.Equal("claim_identity_mismatch", ClaimMdReconciliationRules.Validate(Source,
            request with { Claims = [request.Claims![0], request.Claims[1] with
                { RemoteClaimId = "SATI1-OTHER-1-1-8" }] }, Now));
        Assert.Equal("receipt_evidence_insufficient", ClaimMdReconciliationRules.Validate(Source,
            request with { AcceptedClaimCount = 2 }, Now));
        Assert.Equal("receipt_evidence_insufficient", ClaimMdReconciliationRules.Validate(Source,
            request with { ExternalFileId = null }, Now));
        Assert.Equal("receipt_evidence_insufficient", ClaimMdReconciliationRules.Validate(Source,
            request with { Claims = [null!, request.Claims![1]] }, Now));
    }

    [Fact]
    public void NonReceiptFindingNeedsAuthoritativeSupportReferenceAndCannotCarryAReceipt()
    {
        var request = new ReconcileClaimMdDispatchRequest(Source.Revision, Source.AccountId,
            Source.AccountNumber, Source.EdiGenerationId, Source.ContentSha256, Source.FileName,
            "ConfirmedNotReceived", "SupportCase",
            "CASE-123456", new string('C', 64), Now.AddMinutes(-1),
            ClaimMdReconciliationRules.NotReceivedAttestation, null, null, null, null);

        Assert.Null(ClaimMdReconciliationRules.Validate(Source, request, Now));
        Assert.Equal("absence_evidence_insufficient", ClaimMdReconciliationRules.Validate(Source,
            request with { EvidenceKind = "AccountFileRecord" }, Now));
        Assert.Equal("absence_evidence_insufficient", ClaimMdReconciliationRules.Validate(Source,
            request with { ExternalFileId = "123456" }, Now));
        Assert.Equal("source_changed", ClaimMdReconciliationRules.Validate(Source,
            request with { ExpectedContentSha256 = new string('D', 64) }, Now));
        Assert.Equal("source_changed", ClaimMdReconciliationRules.Validate(Source,
            request with { ExpectedRevision = Source.Revision - 1 }, Now));
        Assert.Equal("evidence_invalid", ClaimMdReconciliationRules.Validate(Source,
            request with { EvidenceObservedAtUtc = Source.EvidenceNotBeforeUtc.AddSeconds(-1) }, Now));
    }

    [Fact]
    public void NonReceiptIsTerminalAndDistinctFromVendorRejection()
    {
        Assert.True(ClaimMdReconciliationRules.CanAdvanceDispatch(2, 7));
        Assert.True(ClaimMdReconciliationRules.CanAdvanceDispatch(5, 7));
        Assert.False(ClaimMdReconciliationRules.CanAdvanceDispatch(7, 2));
        Assert.False(ClaimMdReconciliationRules.CanAdvanceDispatch(7, 4));
        Assert.False(ClaimMdReconciliationRules.CanAdvanceDispatch(1, 7));
    }
}
