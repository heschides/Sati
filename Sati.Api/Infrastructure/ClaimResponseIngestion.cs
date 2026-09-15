using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal sealed class ClaimResponseRejected(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Matches retained outbound evidence, then commits one encrypted receipt and all effects atomically.</summary>
internal sealed class ClaimResponseIngestion(
    ApiDbContext db, EnvelopeProtector protector, AuditTrail audit,
    IOptions<SatiApiOptions> options, IHostEnvironment environment)
{
    public const string ParserVersion = "x12-intake-1";
    public const int MaximumDocumentCharacters = ClaimResponseReader.MaximumDocumentCharacters;

    public bool IsEnabled =>
        options.Value.ExpectedEnvironment == "Demo" && options.Value.ExpectedDatabaseName == "SatiDemo" ||
        environment.IsEnvironment("Testing") && options.Value.ExpectedEnvironment == "Testing" &&
        options.Value.ExpectedDatabaseName == "SatiApiTests";

    public async Task<ClaimResponseIngestResultDto> ImportAsync(string document, Actor actor,
        int? assertedPeriodId, CancellationToken cancellationToken)
    {
        if (!actor.HasBillingPermissions)
            throw new ClaimResponseRejected("forbidden", "Billing permission is required.");
        if (!IsEnabled)
            throw new ClaimResponseRejected("response_intake_unavailable", "Clearinghouse response import is not enabled in this environment.");
        if (string.IsNullOrWhiteSpace(document) || document.Length > MaximumDocumentCharacters)
            throw new ClaimResponseRejected("invalid_response", "Select a nonempty X12 response file no larger than 2 MiB.");
        ParsedClaimResponse parsed;
        try { parsed = ClaimResponseReader.ReadDocument(document); }
        catch (Exception failure) when (failure is FormatException or InvalidOperationException or OverflowException)
        { throw new ClaimResponseRejected("invalid_response", "The response is malformed or uses an unsupported X12 layout. Nothing was imported."); }
        if (!parsed.Envelope.IsTestInterchange)
            throw new ClaimResponseRejected("response_environment_mismatch", "This environment accepts test responses only. Nothing was imported.");

        var rawHash = Hash(document);
        var source = JsonSerializer.Serialize(new { parsed.Envelope.SenderQualifier, parsed.Envelope.SenderId,
            parsed.Envelope.ReceiverQualifier, parsed.Envelope.ReceiverId, parsed.Envelope.Kind, parsed.Envelope.IsTestInterchange });
        var semanticHash = Hash(source + parsed.CanonicalTransaction);
        var identityHash = Hash(source + JsonSerializer.Serialize(new { parsed.Envelope.ControlNumber,
            parsed.Envelope.GroupControlNumber, parsed.Envelope.TransactionControlNumber }));
        var paymentHash = parsed.Remittance is { } advice
            ? Hash(source + JsonSerializer.Serialize(new { advice.PaymentOriginatorId, advice.PaymentReference,
                advice.PayeeId, advice.PayerId, advice.PaymentDate })) : null;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var duplicate = await FindReceiptAsync(actor.AgencyId, rawHash, semanticHash, cancellationToken);
        if (duplicate is not null)
            return Replay(duplicate, assertedPeriodId);
        if (await db.ClearinghouseResponseReceipts.AnyAsync(row => row.AgencyId == actor.AgencyId &&
                (row.IdentitySha256 == identityHash || paymentHash != null && row.PaymentIdentitySha256 == paymentHash), cancellationToken))
            throw new ClaimResponseRejected("response_identity_conflict", "A different response already uses this document or payment identity. Review the retained receipt before retrying.");

        var matches = await MatchAsync(parsed, actor.AgencyId, assertedPeriodId, cancellationToken);
        var receipt = new ClearinghouseResponseReceipt
        {
            Id = Guid.NewGuid(), AgencyId = actor.AgencyId, ActorUserId = actor.UserId,
            ReceivedAtUtc = DateTime.UtcNow, IsTest = parsed.Envelope.IsTestInterchange, Kind = parsed.Envelope.Kind,
            ParserVersion = ParserVersion, RawSha256 = rawHash, SemanticSha256 = semanticHash,
            IdentitySha256 = identityHash, PaymentIdentitySha256 = paymentHash
        };
        var protectedValue = await protector.ProtectAsync(document, Binding(receipt), cancellationToken);
        receipt.Ciphertext = protectedValue.Ciphertext;
        receipt.Nonce = protectedValue.Nonce;
        receipt.Tag = protectedValue.Tag;
        receipt.WrappedDataKey = protectedValue.WrappedDataKey;
        receipt.KeyId = protectedValue.KeyId;
        receipt.Matches = matches.Select(match => new ClearinghouseResponseMatch
        {
            ResponseId = receipt.Id, BillingPeriodId = match.Generation.BillingPeriodId,
            EdiGenerationId = match.Generation.Id, ClaimReference = match.ClaimReference
        }).ToList();
        db.ClearinghouseResponseReceipts.Add(receipt);
        await RecordEffectsAsync(parsed, receipt, matches, cancellationToken);
        audit.Record(actor, "billing-response.imported", "ClearinghouseResponse", metadataJson:
            JsonSerializer.Serialize(new { responseId = receipt.Id, kind = receipt.Kind.ToString(),
                claimCount = receipt.ClaimOutcomesRecorded, periodCount = matches.Select(x => x.Generation.BillingPeriodId).Distinct().Count() }));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var completed = await FindReceiptAsync(actor.AgencyId, rawHash, semanticHash, cancellationToken);
            if (completed is not null) return Replay(completed, assertedPeriodId);
            throw new ClaimResponseRejected("response_write_conflict", "The response could not be committed. No partial import was saved; retry the same file or ask an administrator to review the receipt history.");
        }
        return Result(receipt, false);
    }

    internal static FieldBinding Binding(ClearinghouseResponseReceipt receipt) =>
        new(receipt.AgencyId, 0, $"ClearinghouseResponse:{receipt.Id:N}:Document:{receipt.RawSha256}:{receipt.ParserVersion}");

    private Task<ClearinghouseResponseReceipt?> FindReceiptAsync(int agencyId, string raw, string semantic, CancellationToken token) =>
        db.ClearinghouseResponseReceipts.AsNoTracking().Include(row => row.Matches)
            .FirstOrDefaultAsync(row => row.AgencyId == agencyId && (row.RawSha256 == raw || row.SemanticSha256 == semantic), token);

    private static ClaimResponseIngestResultDto Replay(ClearinghouseResponseReceipt receipt, int? assertedPeriodId)
    {
        if (assertedPeriodId.HasValue && receipt.Matches.Any(match => match.BillingPeriodId != assertedPeriodId))
            throw new ClaimResponseRejected("response_period_mismatch", "The response does not belong exclusively to the selected billing period.");
        return Result(receipt, true);
    }

    private static ClaimResponseIngestResultDto Result(ClearinghouseResponseReceipt receipt, bool replay) =>
        new(receipt.Kind.ToString(), receipt.IsTest, receipt.StageRecorded?.ToString(),
            replay ? 0 : receipt.ClaimOutcomesRecorded, !replay && receipt.DepositRecorded,
            replay ? "This response was already imported. No records were added." :
                "The response and its matching results were retained. Any recorded payment still requires bank reconciliation.")
        {
            ResponseId = receipt.Id, AlreadyImported = replay, ReceivedAtUtc = DateTime.SpecifyKind(receipt.ReceivedAtUtc, DateTimeKind.Utc),
            BillingPeriodIds = receipt.Matches.Select(match => match.BillingPeriodId).Distinct().Order().ToList()
        };

    private async Task<List<Match>> MatchAsync(ParsedClaimResponse parsed, int agencyId, int? assertedPeriodId, CancellationToken token)
    {
        var references = parsed.ClaimAcknowledgements.Select(x => x.ClaimReference)
            .Concat(parsed.Remittance?.Claims.Select(x => x.ClaimReference) ?? []).Distinct(StringComparer.Ordinal).ToList();
        var controls = references.Select(reference => reference.Split('-')[0]).Where(value => value.Length == 9 && value.All(char.IsAsciiDigit)).ToList();
        if (parsed.FunctionalAcknowledgement is { } ack) controls.Add(ack.OriginalGroupControlNumber);
        var generations = await (from generation in db.EdiGenerations.AsNoTracking()
                                 join period in db.BillingPeriods.AsNoTracking() on generation.BillingPeriodId equals period.Id
                                 join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                                 where generation.AgencyId == agencyId && owner.AgencyId == agencyId &&
                                    (generation.ControlNumber == null || controls.Contains(generation.ControlNumber))
                                 select generation).Take(501).ToListAsync(token);
        if (generations.Count > 500)
            throw new ClaimResponseRejected("response_legacy_limit", "The response requires a legacy-submission review before it can be matched safely.");
        var submissions = new List<(ServerEdiGeneration Generation, ParsedClaimSubmission Submission)>();
        foreach (var generation in generations)
        {
            ParsedClaimSubmission submission;
            try { submission = ClaimResponseReader.ReadSubmission(generation.Content); }
            catch (Exception failure) when (failure is FormatException or InvalidOperationException or OverflowException)
            { throw new ClaimResponseRejected("retained_submission_invalid", "A retained submission requires review before response import can continue."); }
            if (generation.IsTest != submission.Envelope.IsTestInterchange)
                throw new ClaimResponseRejected("retained_submission_invalid", "A retained submission has inconsistent environment evidence.");
            if (submission.Envelope.IsTestInterchange == parsed.Envelope.IsTestInterchange &&
                submission.Envelope.SenderId == parsed.Envelope.ReceiverId &&
                submission.Envelope.SenderQualifier == parsed.Envelope.ReceiverQualifier &&
                submission.Envelope.ReceiverId == parsed.Envelope.SenderId &&
                submission.Envelope.ReceiverQualifier == parsed.Envelope.SenderQualifier)
                submissions.Add((generation, submission));
        }
        var matches = new List<Match>();
        if (parsed.FunctionalAcknowledgement is { } functional)
        {
            var candidates = submissions.Where(row => row.Submission.Envelope.GroupControlNumber == functional.OriginalGroupControlNumber &&
                (functional.OriginalTransactionControlNumbers.Count == 0 ||
                 functional.OriginalTransactionControlNumbers.Count == 1 &&
                 functional.OriginalTransactionControlNumbers[0] == row.Submission.Envelope.TransactionControlNumber)).ToList();
            if (candidates.Count != 1) throw Unmatched();
            matches.Add(new(candidates[0].Generation, candidates[0].Submission, string.Empty));
        }
        else
        {
            if (references.Count == 0) throw Unmatched();
            foreach (var reference in references)
            {
                var candidates = submissions.Where(row => row.Submission.Claims.Any(claim => claim.ClaimReference == reference)).ToList();
                if (candidates.Count != 1) throw Unmatched();
                var candidate = candidates[0];
                var submittedClaim = candidate.Submission.Claims.Single(claim => claim.ClaimReference == reference);
                var remitted = parsed.Remittance?.Claims.SingleOrDefault(claim => claim.ClaimReference == reference);
                var acknowledged = parsed.ClaimAcknowledgements.SingleOrDefault(claim => claim.ClaimReference == reference);
                var amount = remitted is not null ? Math.Abs(remitted.BilledAmount) : acknowledged?.BilledAmount;
                if (amount.HasValue && amount.Value != submittedClaim.BilledAmount) throw Unmatched();
                if (remitted is not null && remitted.ServiceLineReferences.Any(line => !submittedClaim.ServiceLineReferences.Contains(line, StringComparer.Ordinal)))
                    throw Unmatched();
                if (acknowledged is not null && acknowledged.ServiceLineReferences.Any(line => !submittedClaim.ServiceLineReferences.Contains(line, StringComparer.Ordinal)))
                    throw Unmatched();
                if (parsed.Remittance is { } remittance && (remittance.PayeeQualifier != "XX" || remittance.PayeeId != candidate.Submission.BillingProviderNpi))
                    throw Unmatched();
                matches.Add(new(candidate.Generation, candidate.Submission, reference));
            }
        }
        if (matches.Count == 0) throw Unmatched();
        if (assertedPeriodId.HasValue && matches.Any(match => match.Generation.BillingPeriodId != assertedPeriodId))
            throw new ClaimResponseRejected("response_period_mismatch", "The response does not belong exclusively to the selected billing period.");
        return matches;
    }

    private async Task RecordEffectsAsync(ParsedClaimResponse parsed, ClearinghouseResponseReceipt receipt, List<Match> matches, CancellationToken token)
    {
        var generationIds = matches.Select(match => match.Generation.Id).Distinct().ToList();
        var priorClaimReferences = parsed.Remittance is not null
            ? (await db.RemittanceClaimOutcomes.AsNoTracking().Where(row => row.AgencyId == receipt.AgencyId &&
                    row.EdiGenerationId.HasValue && generationIds.Contains(row.EdiGenerationId.Value))
                .Select(row => row.ClaimReference).ToListAsync(token)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in matches.GroupBy(match => match.Generation.Id))
        {
            var generation = group.First().Generation;
            var stage = parsed.FunctionalAcknowledgement?.Stage ?? AcknowledgementStage(parsed, group);
            if (parsed.Remittance is { } remittance)
            {
                var groupClaims = remittance.Claims.Where(claim => group.Any(match => match.ClaimReference == claim.ClaimReference)).ToList();
                var needsReview = groupClaims.Any(claim => claim.Status != RemittanceClaimStatus.Paid || priorClaimReferences.Contains(claim.ClaimReference)) ||
                    DepositReconciliationRules.GetStatus(remittance.ClaimPaymentTotal, -remittance.ProviderLevelAdjustment,
                        remittance.RemittancePaymentAmount, null) == DepositReconciliationStatus.RemittanceMismatch;
                stage = needsReview ? BillingSubmissionStage.RemittanceNeedsReview :
                    groupClaims.Count == group.First().Submission.Claims.Count ? BillingSubmissionStage.Paid : BillingSubmissionStage.RemittanceReceived;
                foreach (var claim in groupClaims)
                    db.RemittanceClaimOutcomes.Add(new ServerRemittanceClaimOutcome
                    {
                        AgencyId = receipt.AgencyId, BillingPeriodId = generation.BillingPeriodId,
                        EdiGenerationId = generation.Id, ResponseId = receipt.Id, ClaimReference = claim.ClaimReference,
                        PayerName = remittance.PayerName ?? string.Empty, ReceivedAtUtc = receipt.ReceivedAtUtc,
                        PaymentDate = remittance.PaymentDate, Status = claim.Status, BilledAmount = claim.BilledAmount,
                        AllowedAmount = null, PaidAmount = claim.PaidAmount, AdjustmentAmount = claim.AdjustmentAmount,
                        PatientResponsibilityAmount = claim.PatientResponsibilityAmount, ReasonCode = claim.ReasonCode,
                        Explanation = claim.Explanation, PaymentReference = remittance.PaymentReference, IsSynthetic = receipt.IsTest
                    });
                receipt.ClaimOutcomesRecorded += groupClaims.Count;
            }
            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = receipt.AgencyId, BillingPeriodId = generation.BillingPeriodId, EdiGenerationId = generation.Id,
                ResponseId = receipt.Id, OccurredAtUtc = receipt.ReceivedAtUtc, Stage = stage,
                Reference = receipt.Id.ToString("N"), ResponseType = parsed.Envelope.Kind == ClaimResponseKind.FunctionalAcknowledgement ? "999" :
                    parsed.Envelope.Kind == ClaimResponseKind.RemittanceAdvice ? "835" : "277CA",
                ResponseCode = parsed.FunctionalAcknowledgement?.ResponseCode,
                Explanation = "Response matched to the retained submission. Refer to claim outcomes for payment and review details.",
                IsSynthetic = receipt.IsTest
            });
            receipt.StageRecorded = receipt.StageRecorded is null || BillingSubmissionProgressRules.Rank(stage) > BillingSubmissionProgressRules.Rank(receipt.StageRecorded.Value)
                ? stage : receipt.StageRecorded;
        }
        if (parsed.Remittance is { } advice)
        {
            db.RemittanceDeposits.Add(new ServerRemittanceDeposit
            {
                AgencyId = receipt.AgencyId, ResponseId = receipt.Id, PaymentReference = advice.PaymentReference ?? string.Empty,
                PayerName = advice.PayerName ?? string.Empty, ReceivedAtUtc = receipt.ReceivedAtUtc, PaymentDate = advice.PaymentDate,
                ClaimPaymentAmount = advice.ClaimPaymentTotal, ProviderLevelAdjustmentAmount = -advice.ProviderLevelAdjustment,
                ProviderLevelAdjustmentSummary = advice.ProviderLevelAdjustment == 0 ? null : "Provider-level adjustment reported on the remittance.",
                RemittancePaymentAmount = advice.RemittancePaymentAmount, EftDepositAmount = null, IsSynthetic = receipt.IsTest
            });
            receipt.DepositRecorded = true;
        }
    }

    private static BillingSubmissionStage AcknowledgementStage(ParsedClaimResponse parsed, IEnumerable<Match> matches)
    {
        var claims = parsed.ClaimAcknowledgements.Where(claim => matches.Any(match => match.ClaimReference == claim.ClaimReference)).ToList();
        if (claims.Count == 0) return BillingSubmissionStage.RemittanceReceived;
        var complete = claims.Count == matches.First().Submission.Claims.Count;
        if (claims.Any(claim => claim.Disposition == ClaimAcknowledgementDisposition.NeedsReview)) return BillingSubmissionStage.ClaimNeedsReview;
        if (claims.All(claim => claim.Disposition == ClaimAcknowledgementDisposition.Received)) return BillingSubmissionStage.ClaimReceived;
        if (claims.Any(claim => claim.Disposition == ClaimAcknowledgementDisposition.Received)) return BillingSubmissionStage.ClaimNeedsReview;
        if (claims.All(claim => claim.Disposition == ClaimAcknowledgementDisposition.Rejected))
            return complete ? BillingSubmissionStage.ClaimRejected : BillingSubmissionStage.ClaimNeedsReview;
        if (claims.All(claim => claim.Disposition == ClaimAcknowledgementDisposition.Accepted) && complete)
            return BillingSubmissionStage.ClaimAccepted;
        return BillingSubmissionStage.PartiallyAccepted;
    }

    private static ClaimResponseRejected Unmatched() => new("response_unmatched",
        "The response could not be matched uniquely to retained submissions in this agency and environment. Nothing was imported.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Match(ServerEdiGeneration Generation, ParsedClaimSubmission Submission, string ClaimReference);
}
