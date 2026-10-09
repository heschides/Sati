using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Data;

// Internal persistence projection inputs, never network contracts. Adapters must load
// all modes/accounts for the trusted agency and report any truncated query as incomplete.
public sealed record ClaimReleaseFile(long Id, int AgencyId, int BillingPeriodId, bool IsTest,
    bool IsCorrection, string? ControlNumber, string FileName, string Content);
public sealed record ClaimReleasePeriod(int Id, int OwnerAgencyId);
public sealed record ClaimReleaseLine(int Id, int BillingPeriodId, int NoteId,
    int? NoteAgencyId, int? PersonAgencyId, int OwnerAgencyId, decimal ChargeAmount);
public sealed record ClaimReleaseEvent(int AgencyId, int BillingPeriodId, long? GenerationId,
    BillingSubmissionStage Stage, string? ResponseCode, DateTime OccurredAtUtc);
public sealed record ClaimReleaseAudit(int AgencyId, string MetadataJson);
public sealed record MappedReleaseClaim(int NoteId, int ClaimLineId, SubmittedClaimReference Wire,
    long? CorrectionId, ClaimCorrectionAction? Action);
public sealed record MappedReleaseFile(ClaimReleaseFile File, ParsedClaimSubmission Submission,
    IReadOnlyList<MappedReleaseClaim> Claims);
public sealed record ClaimReleaseHistoryProjection(IReadOnlyList<MappedReleaseFile> Files,
    IReadOnlyList<OriginalClaimHistoryFact> Facts, IReadOnlyList<ClaimReleaseMappingDefect> Defects, bool Complete);

/// <summary>
/// Shared validation of immutable retained submissions and exact delivery evidence.
/// Queries/authorization live in the API/local adapters; permission lives in Contracts.
/// </summary>
public static class OriginalClaimReleaseHistory
{
    public static ClaimReleaseHistoryProjection Project(int agencyId,
        IReadOnlyList<ClaimReleaseFile> files, IReadOnlyList<ClaimReleasePeriod> periods,
        IReadOnlyList<ClaimReleaseLine> lines, IReadOnlyList<ClearinghouseAccount> accounts,
        IReadOnlyList<ClearinghouseDispatch> dispatches, IReadOnlyList<ClearinghouseDispatchAttempt> attempts,
        IReadOnlyList<ClaimCorrection> corrections, IReadOnlyList<ClaimCorrectionSubmission> links,
        IReadOnlyList<ClearinghouseResponseReceipt> receipts, IReadOnlyList<ClaimReleaseEvent> events,
        IReadOnlyList<ClaimReleaseAudit> resolutions, bool complete = true, long? candidateGenerationId = null)
    {
        var mapped = new List<MappedReleaseFile>();
        var facts = new List<OriginalClaimHistoryFact>();
        var defects = new List<ClaimReleaseMappingDefect>();
        if (agencyId <= 0) return new(mapped, facts, defects, false);
        foreach (var unscoped in events.Where(row => row.GenerationId is null && row.Stage != BillingSubmissionStage.Generated))
            defects.Add(new(periods.Any(period => period.Id == unscoped.BillingPeriodId && period.OwnerAgencyId == agencyId)
                ? unscoped.BillingPeriodId : null, null, "unscoped_delivery_event", true));
        foreach (var file in files)
        {
            var ownDispatches = dispatches.Where(row => row.EdiGenerationId == file.Id).ToList();
            var ownEvents = events.Where(row => row.GenerationId == file.Id).ToList();
            var ownReceipts = receipts.Where(row => row.Matches.Any(match => match.EdiGenerationId == file.Id)).ToList();
            var hasDelivery = ownDispatches.Any(row => row.State != ClearinghouseDispatchState.CancelledBeforeSend) ||
                ownDispatches.Any(row => attempts.Any(attempt => attempt.DispatchId == row.Id)) ||
                ownEvents.Any(row => row.Stage != BillingSubmissionStage.Generated) || ownReceipts.Count > 0;
            try
            {
                Require(file.AgencyId == agencyId && file.Id > 0 &&
                    periods.Count(row => row.Id == file.BillingPeriodId && row.OwnerAgencyId == agencyId) == 1);
                var parsed = ClaimResponseReader.ReadSubmission(file.Content);
                var envelope = parsed.Envelope;
                var control = ClaimSubmissionIdentity.RequireControlNumber(envelope.ControlNumber!);
                Require(file.ControlNumber is null || file.ControlNumber == control);
                Require(envelope.IsTestInterchange == file.IsTest && envelope.GroupControlNumber == control &&
                    envelope.SenderQualifier == "ZZ" && envelope.ReceiverQualifier == "ZZ" &&
                    envelope.SenderId == envelope.GroupSenderId && envelope.ReceiverId == envelope.GroupReceiverId);
                var claimRows = new List<MappedReleaseClaim>();
                foreach (var wire in parsed.Claims)
                {
                    Require(wire.ServiceLineReferences.Count == 1 && int.TryParse(wire.ServiceLineReferences[0],
                        NumberStyles.None, CultureInfo.InvariantCulture, out var _) );
                    var noteId = int.Parse(wire.ServiceLineReferences[0], CultureInfo.InvariantCulture);
                    Require(noteId > 0 && wire.ServiceLineReferences[0] == noteId.ToString(CultureInfo.InvariantCulture) &&
                        wire.ClaimReference == ClaimSubmissionIdentity.ClaimReference(control, file.BillingPeriodId, noteId));
                    var source = lines.Where(row => row.NoteId == noteId).ToList();
                    Require(source.Count == 1);
                    var line = source[0];
                    Require(line.BillingPeriodId == file.BillingPeriodId && line.NoteAgencyId == agencyId &&
                        line.PersonAgencyId == agencyId && line.OwnerAgencyId == agencyId);
                    var matchingCorrections = (from link in links
                        join correction in corrections on link.ClaimCorrectionId equals correction.Id
                        where link.EdiGenerationId == file.Id && correction.NoteId == noteId select correction).ToList();
                    if (!file.IsCorrection)
                    {
                        Require(wire.FrequencyCode == "1" && wire.PayerClaimControlNumber is null &&
                            !links.Any(link => link.EdiGenerationId == file.Id) && wire.BilledAmount == line.ChargeAmount);
                        claimRows.Add(new(noteId, line.Id, wire, null, null));
                    }
                    else
                    {
                        Require(matchingCorrections.Count == 1);
                        var correction = matchingCorrections[0];
                        Require(correction.AgencyId == agencyId && correction.BillingPeriodId == file.BillingPeriodId &&
                            correction.ClaimLineId == line.Id && Enum.IsDefined(correction.Action) &&
                            wire.FrequencyCode == ClaimCorrectionRules.FrequencyCode(correction.Action) &&
                            wire.PayerClaimControlNumber == correction.PayerClaimControlNumber &&
                            (correction.Action == ClaimCorrectionAction.Resubmit ? correction.PayerClaimControlNumber is null :
                                !string.IsNullOrWhiteSpace(correction.PayerClaimControlNumber)) &&
                            wire.BilledAmount == (correction.CorrectedChargeAmount ?? line.ChargeAmount) &&
                            correction.CorrectsEdiGenerationId is > 0 && correction.CorrectsEdiGenerationId < file.Id);
                        claimRows.Add(new(noteId, line.Id, wire, correction.Id, correction.Action));
                    }
                }
                Require(claimRows.Select(row => row.NoteId).Distinct().Count() == claimRows.Count);
                if (file.IsCorrection) Require(links.Count(link => link.EdiGenerationId == file.Id) == claimRows.Count);
                var historicalAccounts = HistoricalAccounts(agencyId, file, parsed, accounts);
                Require(envelope.ReceiverId == "330897513" ? parsed.Claims.All(row => row.RemoteClaimId is null) : historicalAccounts.Count == 1);
                Require(ownDispatches.Count <= 1 && ownDispatches.All(row => row.AgencyId == agencyId &&
                    accounts.Any(account => account.Id == row.AccountId && account.AgencyId == agencyId &&
                        account.IsTest == file.IsTest && account.TradingPartnerProfileVersion == row.TradingPartnerProfileVersion &&
                        (account.ConnectorKind == TradingPartnerKind.OfficeAlly ? envelope.ReceiverId == "330897513" :
                            historicalAccounts.Any(historical => historical.Id == account.Id)))));
                Require(ownEvents.All(row => row.AgencyId == agencyId && row.BillingPeriodId == file.BillingPeriodId && Enum.IsDefined(row.Stage)));
                var hash = Hash(file.Content);
                var ownAttempts = attempts.Where(attempt => ownDispatches.Any(dispatch => dispatch.Id == attempt.DispatchId)).ToList();
                Require(ownAttempts.Count <= 1 && ownAttempts.All(attempt => attempt.AttemptNumber == 1 &&
                    attempt.ContentSha256 == hash && attempt.FileName == file.FileName && Enum.IsDefined(attempt.Outcome)));
                foreach (var receipt in ownReceipts)
                {
                    Require(receipt.AgencyId == agencyId && receipt.IsTest == file.IsTest &&
                        Enum.IsDefined(receipt.Kind) && Enum.IsDefined(receipt.Source));
                    if (receipt.Source == ClearinghouseReceiptSource.Connector)
                        Require(receipt.AccountId is Guid accountId && historicalAccounts.Any(account => account.Id == accountId) &&
                            receipt.ConnectorKind == TradingPartnerKind.ClaimMd && receipt.FeedKind is not null);
                    Require(receipt.Matches.Where(match => match.EdiGenerationId == file.Id).All(match =>
                        match.ResponseId == receipt.Id && match.BillingPeriodId == file.BillingPeriodId &&
                        (match.ClaimReference.Length == 0 ? receipt.Kind == ClaimResponseKind.FunctionalAcknowledgement :
                            parsed.Claims.Any(claim => claim.ClaimReference == match.ClaimReference))));
                }
                var receivedEvent = ownEvents.Any(row => IsReceiptStage(row.Stage));
                var receivedDispatch = ownDispatches.Any(row => row.State == ClearinghouseDispatchState.AcceptedByClearinghouse);
                var acceptedAttempt = ownAttempts.Any(row => row.Outcome == ClearinghouseAttemptOutcome.Accepted);
                var unresolvedEvent = ownEvents.Any(row => row.Stage == BillingSubmissionStage.TransportFailed);
                var dispatch = ownDispatches.SingleOrDefault();
                var knownUnsent = dispatch is not null &&
                    (dispatch.State == ClearinghouseDispatchState.CancelledBeforeSend && ownAttempts.Count == 0 && !unresolvedEvent ||
                     dispatch.State == ClearinghouseDispatchState.ConfirmedNotReceived &&
                     ownAttempts.All(row => row.Outcome is ClearinghouseAttemptOutcome.OutcomeUnknown or ClearinghouseAttemptOutcome.TransportFailure) &&
                     HasNonreceiptFinding(agencyId, file, parsed, dispatch, hash, ownAttempts, ownEvents, resolutions));
                var uncertain = dispatch is not null && (!Enum.IsDefined(dispatch.State) ||
                    dispatch.State is ClearinghouseDispatchState.Sending or ClearinghouseDispatchState.OutcomeUnknown or ClearinghouseDispatchState.RejectedByClearinghouse ||
                    dispatch.State == ClearinghouseDispatchState.ConfirmedNotReceived && !knownUnsent) ||
                    ownAttempts.Count > 0 && !acceptedAttempt || unresolvedEvent;
                var delivery = receivedEvent || receivedDispatch || acceptedAttempt ? OriginalClaimDeliveryEvidence.Received :
                    knownUnsent ? OriginalClaimDeliveryEvidence.KnownUnsent : uncertain ? OriginalClaimDeliveryEvidence.Uncertain :
                    dispatch?.State == ClearinghouseDispatchState.Queued ? OriginalClaimDeliveryEvidence.Queued : OriginalClaimDeliveryEvidence.GeneratedOnly;
                foreach (var row in claimRows)
                {
                    var exactReceipt = ownReceipts.Any(receipt => receipt.Matches.Any(match => match.EdiGenerationId == file.Id &&
                        (match.ClaimReference.Length == 0 || match.ClaimReference == row.Wire.ClaimReference)));
                    facts.Add(new(row.NoteId, file.Id, dispatch?.Id, file.IsCorrection,
                        exactReceipt ? OriginalClaimDeliveryEvidence.Received : delivery));
                }
                mapped.Add(new(file, parsed, claimRows));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                var scopedPeriod = periods.Any(period => period.Id == file.BillingPeriodId && period.OwnerAgencyId == agencyId)
                    ? (int?)file.BillingPeriodId : null;
                defects.Add(new(scopedPeriod, null, file.Id == candidateGenerationId ? "candidate_invalid" : "retained_history_invalid", hasDelivery));
            }
        }
        // A correction's predecessor must map this same business claim; dangling links
        // cannot disappear merely because another retained file failed to parse.
        foreach (var file in mapped.Where(row => row.File.IsCorrection))
            foreach (var claim in file.Claims)
            {
                var correction = corrections.Single(row => row.Id == claim.CorrectionId);
                if (!mapped.Any(prior => prior.File.Id == correction.CorrectsEdiGenerationId &&
                    prior.File.AgencyId == agencyId && prior.Claims.Any(row => row.NoteId == claim.NoteId)))
                    defects.Add(new(file.File.BillingPeriodId, claim.NoteId,
                        file.File.Id == candidateGenerationId ? "candidate_invalid" : "correction_predecessor_invalid",
                        facts.Any(row => row.GenerationId == file.File.Id && row.Evidence != OriginalClaimDeliveryEvidence.GeneratedOnly)));
            }
        if (dispatches.Any(row => !files.Any(file => file.Id == row.EdiGenerationId)) ||
            attempts.Any(row => !dispatches.Any(dispatch => dispatch.Id == row.DispatchId)) ||
            receipts.Any(receipt => receipt.Matches.Any(match => !files.Any(file => file.Id == match.EdiGenerationId))) ||
            receipts.Any(receipt => receipt.Matches.Count == 0) ||
            links.Any(link => !files.Any(file => file.Id == link.EdiGenerationId)) ||
            events.Any(row => row.GenerationId is long generationId && !files.Any(file => file.Id == generationId)))
            complete = false;
        return new(mapped, facts, defects, complete);
    }

    public static bool IsReceiptStage(BillingSubmissionStage stage) => stage is
        BillingSubmissionStage.Transmitted or BillingSubmissionStage.FunctionalAccepted or BillingSubmissionStage.FunctionalRejected or
        BillingSubmissionStage.ClaimAccepted or BillingSubmissionStage.ClaimRejected or BillingSubmissionStage.PartiallyAccepted or
        BillingSubmissionStage.Paid or BillingSubmissionStage.Reconciled or BillingSubmissionStage.RemittanceReceived or
        BillingSubmissionStage.RemittanceNeedsReview or BillingSubmissionStage.ClaimReceived or BillingSubmissionStage.ClaimNeedsReview;

    private static List<ClearinghouseAccount> HistoricalAccounts(int agencyId, ClaimReleaseFile file,
        ParsedClaimSubmission parsed, IReadOnlyList<ClearinghouseAccount> accounts) => accounts.Where(account =>
            account.AgencyId == agencyId && account.ConnectorKind == TradingPartnerKind.ClaimMd &&
            account.IsTest == file.IsTest && account.TradingPartnerProfileVersion == TradingPartnerProfile.CurrentVersion &&
            parsed.Envelope.ReceiverId == "CLAIMMD" && parsed.Envelope.SenderId == account.ExternalAccountNumber &&
            !string.IsNullOrEmpty(account.ClaimNamespace) && parsed.Claims.All(claim => claim.ServiceLineReferences.Count == 1 &&
                claim.RemoteClaimId == ClaimSubmissionIdentity.RemoteClaimId(account.ClaimNamespace, agencyId,
                    file.BillingPeriodId, int.Parse(claim.ServiceLineReferences[0], CultureInfo.InvariantCulture)))).ToList();

    private static bool HasNonreceiptFinding(int agencyId, ClaimReleaseFile file, ParsedClaimSubmission parsed, ClearinghouseDispatch dispatch,
        string hash, IReadOnlyList<ClearinghouseDispatchAttempt> attempts, IReadOnlyList<ClaimReleaseEvent> events,
        IReadOnlyList<ClaimReleaseAudit> resolutions)
    {
        var findings = events.Where(row => row.GenerationId == file.Id && row.Stage == BillingSubmissionStage.TransportFailed &&
            row.ResponseCode == "manual-nonreceipt-finding").ToList();
        if (dispatch.ExternalFileId is not null || dispatch.AcceptedClaimCount is not null || dispatch.RejectedClaimCount is not null ||
            findings.Count != 1 || attempts.Any(row => row.CompletedAtUtc > findings[0].OccurredAtUtc) ||
            events.Any(row => row.Stage == BillingSubmissionStage.TransportFailed && row.ResponseCode != "manual-nonreceipt-finding" &&
                row.OccurredAtUtc > findings[0].OccurredAtUtc)) return false;
        var identityDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            parsed.Claims.OrderBy(row => row.ClaimReference, StringComparer.Ordinal)
                .Select(row => $"{row.ClaimReference}|{row.RemoteClaimId}")))));
        foreach (var audit in resolutions.Where(row => row.AgencyId == agencyId))
            try
            {
                using var document = JsonDocument.Parse(audit.MetadataJson);
                var root = document.RootElement;
                if (root.GetProperty("dispatchId").GetGuid() == dispatch.Id &&
                    root.GetProperty("accountId").GetGuid() == dispatch.AccountId &&
                    root.GetProperty("generationId").GetInt64() == file.Id &&
                    root.GetProperty("sourceSha256").GetString() == hash &&
                    root.GetProperty("sourceFileName").GetString() == file.FileName &&
                    root.GetProperty("claimCount").GetInt32() == parsed.Claims.Count &&
                    root.GetProperty("claimIdentitySha256").GetString() == identityDigest &&
                    root.GetProperty("previousState").GetString() is nameof(ClearinghouseDispatchState.Sending) or nameof(ClearinghouseDispatchState.OutcomeUnknown) &&
                    root.GetProperty("newState").GetString() == nameof(ClearinghouseDispatchState.ConfirmedNotReceived) &&
                    root.GetProperty("previousRevision").GetInt64() + 1 == dispatch.Revision &&
                    root.GetProperty("evidenceKind").GetString() == "SupportCase" &&
                    root.GetProperty("manualAttestation").GetBoolean()) return true;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
        return false;
    }

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(content)));
    private static void Require(bool condition)
    {
        if (!condition) throw new FormatException("Retained claim release identity/evidence needs review.");
    }
}
