using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

/// <summary>
/// An API status is not a 277CA. Retain its XML and map only A=received, R=rejected;
/// neither value is a payer adjudication. A failed or ambiguous page advances nothing.
/// </summary>
internal sealed class ClaimMdStatusProcessor(ApiDbContext db, EnvelopeProtector protector)
{
    internal const string ParserVersion = "claimmd-api-status-1";

    internal async Task<bool> ProcessAsync(Guid accountId, string expectedCursor,
        ClaimMdStatusPage page, CancellationToken token)
    {
        if (expectedCursor.Length == 0 || page.Cursor.Length == 0 || page.Claims.Count == 0)
            return false;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == accountId && row.IsEnabled && row.IsTest && row.ConnectorKind == TradingPartnerKind.ClaimMd, token);
        if (account is null) throw new InvalidOperationException("Claim.MD test account is unavailable.");
        var checkpoint = await db.ClearinghouseFeedCheckpoints.SingleOrDefaultAsync(row =>
            row.AccountId == accountId && row.FeedKind == ClearinghouseFeedKind.Status, token);
        if ((checkpoint?.Cursor ?? "0") != expectedCursor) return false;
        if (page.Cursor == expectedCursor) return false;

        var fileIds = page.Claims.Select(claim => claim.FileId).Distinct().ToArray();
        var dispatches = await db.ClearinghouseDispatches.AsNoTracking().Where(row =>
            row.AccountId == accountId && row.AgencyId == account.AgencyId &&
            row.State == ClearinghouseDispatchState.AcceptedByClearinghouse &&
            row.ExternalFileId != null && fileIds.Contains(row.ExternalFileId)).ToListAsync(token);
        var matched = new List<(ClaimMdStatusClaim Claim, ServerEdiGeneration Generation)>();
        foreach (var fileId in fileIds)
        {
            var dispatch = dispatches.Where(row => row.ExternalFileId == fileId).ToArray();
            if (dispatch.Length != 1) throw new InvalidOperationException("Claim.MD status does not identify one retained file.");
            var generation = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == dispatch[0].EdiGenerationId && row.AgencyId == account.AgencyId && row.IsTest, token);
            if (generation is null || !ClearinghouseAccountSelection.Matches(
                generation.Content, account, generation.BillingPeriodId))
                throw new InvalidOperationException("Claim.MD status source file is invalid.");
            var submission = ClaimResponseReader.ReadSubmission(generation.Content);
            foreach (var claim in page.Claims.Where(row => row.FileId == fileId))
            {
                if (!submission.Claims.Any(row => row.ClaimReference == claim.ClaimReference &&
                        row.RemoteClaimId == claim.RemoteClaimId))
                    throw new InvalidOperationException("Claim.MD status claim identity does not match retained evidence.");
                matched.Add((claim, generation));
            }
        }
        if (matched.Count != page.Claims.Count)
            throw new InvalidOperationException("Claim.MD status page cannot be fully matched.");

        var receipt = new ClearinghouseResponseReceipt
        {
            Id = Guid.NewGuid(), AgencyId = account.AgencyId, Source = ClearinghouseReceiptSource.Connector,
            AccountId = account.Id, ConnectorKind = TradingPartnerKind.ClaimMd,
            FeedKind = ClearinghouseFeedKind.Status, ExternalArtifactId = "status:" + page.Cursor,
            ContentType = "application/xml", ConnectorVersion = "claimmd-api-1.19",
            ReceivedAtUtc = DateTime.UtcNow, Kind = ClaimResponseKind.ClaimMdApiStatus,
            IsTest = true, ParserVersion = ParserVersion,
            RawSha256 = Hash(page.RawXml),
            SemanticSha256 = Hash($"ClaimMD|{account.Id:N}|status|{page.RawXml}"),
            IdentitySha256 = Hash($"ClaimMD|{account.Id:N}|status|{page.Cursor}")
        };
        var protectedValue = await protector.ProtectAsync(page.RawXml,
            ClaimResponseIngestion.Binding(receipt), token);
        receipt.Ciphertext = protectedValue.Ciphertext;
        receipt.Nonce = protectedValue.Nonce;
        receipt.Tag = protectedValue.Tag;
        receipt.WrappedDataKey = protectedValue.WrappedDataKey;
        receipt.KeyId = protectedValue.KeyId;
        receipt.Matches = matched.Select(item => new
            { item.Generation.Id, item.Generation.BillingPeriodId, item.Claim.ClaimReference })
            .Distinct().Select(item => new ClearinghouseResponseMatch
            {
                ResponseId = receipt.Id, EdiGenerationId = item.Id,
                BillingPeriodId = item.BillingPeriodId, ClaimReference = item.ClaimReference
            }).ToList();
        receipt.ClaimOutcomesRecorded = matched.Count;
        db.ClearinghouseResponseReceipts.Add(receipt);
        foreach (var item in matched)
            db.ClaimAcknowledgementOutcomes.Add(new ClaimAcknowledgementOutcome
            {
                AgencyId = account.AgencyId, BillingPeriodId = item.Generation.BillingPeriodId,
                EdiGenerationId = item.Generation.Id, ResponseId = receipt.Id,
                ClaimReference = item.Claim.ClaimReference,
                Disposition = item.Claim.Status == "R"
                    ? ClaimAcknowledgementDisposition.Rejected : ClaimAcknowledgementDisposition.Received,
                CategoryCode = "CLMMD", StatusCode = item.Claim.Status,
                ReceivedAtUtc = receipt.ReceivedAtUtc, IsSynthetic = true
            });
        foreach (var group in matched.GroupBy(item => item.Generation.Id))
        {
            var generation = group.First().Generation;
            var stage = group.Any(item => item.Claim.Status == "R")
                ? BillingSubmissionStage.ClaimNeedsReview : BillingSubmissionStage.ClaimReceived;
            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = account.AgencyId, BillingPeriodId = generation.BillingPeriodId,
                EdiGenerationId = generation.Id, ResponseId = receipt.Id,
                OccurredAtUtc = receipt.ReceivedAtUtc, Stage = stage,
                Reference = receipt.Id.ToString("N"), ResponseType = "ClaimMD-API",
                ResponseCode = null,
                Explanation = "Claim.MD status retained. A is receipt, not payer adjudication; rejected claims require billing review.",
                IsSynthetic = true
            });
            receipt.StageRecorded = receipt.StageRecorded is null ||
                BillingSubmissionProgressRules.Rank(stage) > BillingSubmissionProgressRules.Rank(receipt.StageRecorded.Value)
                    ? stage : receipt.StageRecorded;
        }
        if (checkpoint is null)
        {
            checkpoint = new ClearinghouseFeedCheckpoint
            {
                Id = Guid.NewGuid(), AgencyId = account.AgencyId, AccountId = account.Id,
                FeedKind = ClearinghouseFeedKind.Status, Revision = 0
            };
            db.ClearinghouseFeedCheckpoints.Add(checkpoint);
        }
        else checkpoint.Revision++;
        checkpoint.Cursor = page.Cursor;
        checkpoint.LastReceiptId = receipt.Id;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
