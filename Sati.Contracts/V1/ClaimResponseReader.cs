using static Sati.Contracts.V1.X12Document;

namespace Sati.Contracts.V1;

public enum ClaimResponseKind { FunctionalAcknowledgement, ClaimAcknowledgement, RemittanceAdvice, Unrecognised }

/// <summary>ControlNumber is this document's ISA13, never the original 837's control number.</summary>
public sealed record ClaimResponseEnvelope(ClaimResponseKind Kind, bool IsTestInterchange, string? ControlNumber)
{
    public string SenderQualifier { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string ReceiverQualifier { get; init; } = string.Empty;
    public string ReceiverId { get; init; } = string.Empty;
    public string GroupControlNumber { get; init; } = string.Empty;
    public string TransactionControlNumber { get; init; } = string.Empty;
    public string ImplementationVersion { get; init; } = string.Empty;
}

public sealed record ClaimAcknowledgementResult(
    ClaimResponseEnvelope Envelope, BillingSubmissionStage Stage, string? ResponseCode, string Explanation);

public sealed record RemittanceClaimResult(
    string ClaimReference, decimal BilledAmount, decimal PaidAmount, decimal AdjustmentAmount,
    decimal PatientResponsibilityAmount, RemittanceClaimStatus Status,
    string? GroupCode, string? ReasonCode, string Explanation)
{
    public string ClaimStatusCode { get; init; } = string.Empty;
    public IReadOnlyList<string> ServiceLineReferences { get; init; } = [];

    /// <summary>The payer's own claim number (CLP07), cited when the claim is later replaced or voided.</summary>
    public string? PayerClaimControlNumber { get; init; }
}

/// <summary>ProviderLevelAdjustment is the raw signed PLB sum: positive reduces the payment.
/// Sati's deposit model stores its negation, so claim payments plus that ledger adjustment equals BPR02.</summary>
public sealed record RemittanceResult(
    ClaimResponseEnvelope Envelope, string? PaymentReference, string? PayerName, DateTime? PaymentDate,
    decimal ClaimPaymentTotal, decimal ProviderLevelAdjustment, decimal RemittancePaymentAmount,
    IReadOnlyList<RemittanceClaimResult> Claims)
{
    public string PaymentDirection { get; init; } = string.Empty;
    public string PaymentOriginatorId { get; init; } = string.Empty;
    public string PayeeId { get; init; } = string.Empty;
    public string PayeeQualifier { get; init; } = string.Empty;
    public string PayerId { get; init; } = string.Empty;
    public string PayerQualifier { get; init; } = string.Empty;
}

/// <summary>Shared authoritative reading of bounded 5010 999, 277CA, and 835 documents.
/// This supported subset is not payer certification or a full X12 conformance validator.</summary>
public static class ClaimResponseReader
{
    public const int MaximumDocumentCharacters = X12Document.MaximumLength;
    public const int MaximumDocumentLength = MaximumDocumentCharacters;

    public static ClaimResponseEnvelope ReadEnvelope(string x12) => X12Document.Read(x12).Envelope;

    public static ParsedClaimResponse ReadDocument(string x12)
    {
        var document = X12Document.Read(x12);
        var parsed = document.Type switch
        {
            "999" => new ParsedClaimResponse(document.Envelope, ReadFunctional(document), [], null),
            "277" => new ParsedClaimResponse(document.Envelope, null, ReadClaimAcknowledgements(document), null),
            "835" => new ParsedClaimResponse(document.Envelope, null, [], ReadRemittance(document)),
            _ => throw new FormatException("Only 5010 999, 277CA, and 835 responses are supported. TA1, 997 and other transaction types require separate handling.")
        };
        return parsed with { CanonicalTransaction = document.CanonicalTransaction() };
    }

    public static ParsedClaimSubmission ReadSubmission(string x12)
    {
        var document = X12Document.Read(x12);
        Require(document.Type == "837", "The retained submission is not a supported 837P.");
        var provider = ExactlyOne(document.Transaction.Where(s => s[0] == "NM1" && s.Length > 1 && s[1] == "85"));
        Require(Required(provider, 8) == "XX", "The retained billing provider must identify its NPI.");
        var npi = Required(provider, 9, 10);
        Require(npi.Length == 10 && Digits(npi), "The retained billing provider NPI is invalid.");
        var claims = new List<SubmittedClaimReference>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < document.Transaction.Count; i++)
        {
            var segment = document.Transaction[i];
            if (segment[0] != "CLM") continue;
            var reference = Required(segment, 1, 38);
            Require(references.Add(reference), "The retained submission contains a duplicate claim reference.");
            var amount = Money(Required(segment, 2, 18));
            Require(amount > 0m, "The retained claim charge must be positive.");
            var lines = new List<string>();
            for (var j = i + 1; j < document.Transaction.Count && document.Transaction[j][0] is not ("CLM" or "HL" or "SE"); j++)
            {
                var detail = document.Transaction[j];
                if (detail[0] == "REF" && detail.Length > 1 && detail[1] == "6R")
                    AddUnique(lines, Required(detail, 2, 50));
            }
            var composite = segment.Length > 5 ? segment[5].Split(document.ComponentSeparator) : [];
            var frequency = composite.Length > 2 && composite[2].Length > 0 ? composite[2] : "1";
            Require(frequency is "1" or "7" or "8", "The retained claim frequency is unsupported.");
            claims.Add(new(reference, amount, lines) { FrequencyCode = frequency });
            Require(claims.Count <= 5000, "The retained submission contains too many claims.");
        }
        Require(claims.Count > 0, "The retained submission has no claim references.");
        return new(document.Envelope, claims, npi);
    }

    public static ClaimAcknowledgementResult ReadAcknowledgement(string x12)
    {
        var parsed = ReadDocument(x12);
        if (parsed.FunctionalAcknowledgement is { } functional)
            return new(parsed.Envelope, functional.Stage, functional.ResponseCode,
                functional.Stage == BillingSubmissionStage.FunctionalAccepted
                    ? "The clearinghouse accepted this transaction's syntax; claim adjudication is a separate decision."
                    : "The clearinghouse rejected this transaction's syntax. Review the retained acknowledgement before correcting it.");
        Require(parsed.Envelope.Kind == ClaimResponseKind.ClaimAcknowledgement, "The document is not an acknowledgement.");
        var claims = parsed.ClaimAcknowledgements;
        // Historic aggregate API has no receipt/unknown stage. Refuse rather than invent a verdict.
        Require(claims.All(c => c.Disposition is ClaimAcknowledgementDisposition.Accepted or ClaimAcknowledgementDisposition.Rejected),
            "The acknowledgement includes receipt-only or unresolved statuses requiring review.");
        var accepted = claims.Count(c => c.Disposition == ClaimAcknowledgementDisposition.Accepted);
        var stage = accepted == claims.Count ? BillingSubmissionStage.ClaimAccepted
            : accepted == 0 ? BillingSubmissionStage.ClaimRejected : BillingSubmissionStage.PartiallyAccepted;
        return new(parsed.Envelope, stage, string.Join(",", claims.Select(c => c.CategoryCode).Distinct()),
            accepted == claims.Count ? "Every referenced claim was accepted into adjudication; payment has not been determined."
            : accepted == 0 ? "Every referenced claim was rejected before adjudication."
            : $"The acknowledgement accepted {accepted} of {claims.Count} referenced claims into adjudication. The rest were rejected.");
    }

    public static RemittanceResult ReadRemittance(string x12) =>
        ReadDocument(x12).Remittance ?? throw new FormatException("The document is not a remittance advice.");

    private static FunctionalAcknowledgementDetail ReadFunctional(X12Document document)
    {
        var segments = document.Transaction;
        var ak1 = ExactlyOne(segments.Where(s => s[0] == "AK1"));
        var ak9 = ExactlyOne(segments.Where(s => s[0] == "AK9"));
        Require(segments[1] == ak1 && segments[^2] == ak9, "The functional acknowledgement loop order is invalid.");
        Require(Required(ak1, 1) == "HC", "The acknowledgement does not identify an original healthcare-claim group.");
        var originalGroup = Required(ak1, 2, 9);
        Require(Digits(originalGroup), "The original group control number is invalid.");
        if (ak1.Length > 3) Require(ak1[3] == "005010X222A1", "The acknowledged claim implementation version is unsupported.");
        // Retained generator emits one ST. Never flatten partial group acceptance into a batch verdict.
        Require(Count(Required(ak9, 2)) == 1 && Count(Required(ak9, 3)) == 1,
            "Acknowledgements of multiple or incomplete original transactions require separate handling.");
        var acceptedCount = Count(Required(ak9, 4));
        var code = Required(ak9, 1);
        Require(code is "A" or "E" or "R", "The functional acknowledgement disposition requires separate review.");
        Require(acceptedCount == (code is "A" or "E" ? 1 : 0), "The acknowledgement disposition conflicts with its accepted count.");
        var ak2s = segments.Where(s => s[0] == "AK2").ToArray();
        Require(ak2s.Length <= 1, "The acknowledgement contains multiple original transaction loops.");
        var originals = new List<string>();
        if (ak2s.Length == 1)
        {
            var ak2 = ak2s[0];
            Require(Required(ak2, 1) == "837", "The original acknowledged transaction is not an 837.");
            var transaction = Required(ak2, 2, 9);
            Require(transaction.Length >= 4 && transaction.All(char.IsAsciiLetterOrDigit), "The acknowledged transaction control number is invalid.");
            if (ak2.Length > 3) Require(ak2[3] == "005010X222A1", "The original transaction implementation version is unsupported.");
            originals.Add(transaction);
            var verdict = ExactlyOne(segments.Where(s => s[0] == "IK5"));
            var ikCode = Required(verdict, 1);
            Require(ikCode is "A" or "E" or "R" && ((ikCode is "A" or "E") == (code is "A" or "E")),
                "The transaction and group acknowledgement dispositions are inconsistent.");
            Require(Array.IndexOf(segments.ToArray(), ak2) < Array.IndexOf(segments.ToArray(), verdict), "The acknowledgement verdict precedes its transaction.");
        }
        else Require(!segments.Any(s => s[0] == "IK5"), "A transaction verdict is missing its original transaction identity.");
        Require(segments.All(s => s[0] is "ST" or "AK1" or "AK2" or "IK3" or "IK4" or "CTX" or "IK5" or "AK9" or "SE"),
            "The 999 contains unsupported segments.");
        return new(originalGroup, originals, code is "A" or "E" ? BillingSubmissionStage.FunctionalAccepted : BillingSubmissionStage.FunctionalRejected, code);
    }

    private static IReadOnlyList<ClaimAcknowledgementDetail> ReadClaimAcknowledgements(X12Document document)
    {
        var bht = ExactlyOne(document.Transaction.Where(s => s[0] == "BHT"));
        Require(document.Transaction[1] == bht, "The claim acknowledgement business header is out of order.");
        Require(Required(bht, 1) == "0085" && Required(bht, 2) == "08", "Only the 277 claim acknowledgement business purpose is supported.");
        Date(Required(bht, 4));
        Require(document.Transaction.All(s => s[0] is "ST" or "BHT" or "HL" or "NM1" or "N3" or "N4" or "PER" or "TRN" or "STC" or "REF" or "DTP" or "QTY" or "AMT" or "SVC" or "SE"),
            "The claim acknowledgement contains unsupported segments.");
        var hierarchy = new Dictionary<string, string>(StringComparer.Ordinal);
        var claims = new List<ClaimAcknowledgementDetail>();
        var traces = new HashSet<string>(StringComparer.Ordinal);
        string? level = null, reference = null;
        var statuses = new List<(string Category, string Status, decimal? Billed)>();
        var lineReferences = new List<string>();
        var serviceLevel = false;
        void FinishClaim()
        {
            if (reference is null) return;
            Require(statuses.Count > 0, "A claim trace has no claim-level status.");
            var dispositions = statuses.Select(s => Disposition(s.Category)).ToArray();
            var disposition = dispositions.Contains(ClaimAcknowledgementDisposition.Rejected) ? ClaimAcknowledgementDisposition.Rejected
                : dispositions.Contains(ClaimAcknowledgementDisposition.NeedsReview) ? ClaimAcknowledgementDisposition.NeedsReview
                : dispositions.All(d => d == ClaimAcknowledgementDisposition.Accepted) ? ClaimAcknowledgementDisposition.Accepted
                : ClaimAcknowledgementDisposition.Received;
            var amounts = statuses.Where(s => s.Billed.HasValue).Select(s => s.Billed!.Value).Distinct().ToArray();
            Require(amounts.Length <= 1, "The claim acknowledgement carries conflicting claim charges.");
            claims.Add(new(reference, disposition, string.Join(",", statuses.Select(s => s.Category).Distinct()),
                string.Join(",", statuses.Select(s => s.Status).Distinct()), amounts.Length == 1 ? amounts[0] : null)
                { ServiceLineReferences = lineReferences.ToArray() });
            Require(claims.Count <= 5000, "The acknowledgement has too many claims.");
            reference = null; statuses.Clear(); lineReferences.Clear(); serviceLevel = false;
        }
        foreach (var segment in document.Transaction)
        {
            if (segment[0] == "HL")
            {
                FinishClaim();
                var id = Required(segment, 1, 12);
                var nextLevel = Required(segment, 3, 2);
                Require(Digits(id) && !hierarchy.ContainsKey(id), "The 277 hierarchy has a duplicate or invalid identifier.");
                Require(segment.Length == 5 && segment[4] is "0" or "1", "The 277 hierarchy shape is invalid.");
                var parent = segment[2];
                Require(nextLevel switch
                {
                    "20" => parent.Length == 0 && hierarchy.Count == 0,
                    "21" => hierarchy.TryGetValue(parent, out var p) && p == "20",
                    "19" => hierarchy.TryGetValue(parent, out var p) && p == "21",
                    "PT" => hierarchy.TryGetValue(parent, out var p) && p == "19",
                    _ => false
                }, "The 277 claim hierarchy is unsupported or has an invalid parent.");
                hierarchy.Add(id, nextLevel); level = nextLevel;
            }
            else if (segment[0] == "TRN")
            {
                Required(segment, 2, 50);
                if (level == "PT")
                {
                    FinishClaim();
                    Require(Required(segment, 1) == "2", "A claim acknowledgement must use the referenced transaction trace.");
                    reference = Required(segment, 2, 50);
                    Require(traces.Add(reference), "The acknowledgement contains a duplicate claim trace.");
                }
            }
            else if (segment[0] == "SVC")
            {
                Require(reference is not null && statuses.Count > 0, "A service status is not attached to an identified claim.");
                serviceLevel = true; Money(Required(segment, 2, 18));
            }
            else if (segment[0] == "STC")
            {
                var parts = Required(segment, 1, 80).Split(document.ComponentSeparator);
                Require(parts.Length is 2 or 3 && parts[0].Length <= 4 && Digits(parts[1]), "The claim status composite is invalid.");
                Date(Required(segment, 2));
                decimal? amount = segment.Length > 4 && segment[4].Length > 0 ? Money(segment[4]) : null;
                Require(!amount.HasValue || amount.Value >= 0, "A claim acknowledgement charge cannot be negative.");
                Require(segment.Length <= 10 || segment.Skip(10).All(string.IsNullOrEmpty), "Additional status composites require separate handling.");
                if (level == "PT")
                {
                    Require(reference is not null, "A patient status is missing its claim trace.");
                    var disposition = Disposition(parts[0]);
                    Require(disposition switch
                    {
                        ClaimAcknowledgementDisposition.Accepted or ClaimAcknowledgementDisposition.Received => Required(segment, 3) == "WQ",
                        ClaimAcknowledgementDisposition.Rejected => Required(segment, 3) == "U",
                        _ => true
                    }, "The claim status category and action code disagree.");
                    if (serviceLevel)
                        Require(Disposition(parts[0]) == ClaimAcknowledgementDisposition.Accepted,
                            "Service-level rejection or unresolved status requires separate handling.");
                    else statuses.Add((parts[0], parts[1], amount));
                }
                else
                {
                    Require(level is not null, "An aggregate status has no hierarchy context.");
                    Require(Disposition(parts[0]) is ClaimAcknowledgementDisposition.Accepted or ClaimAcknowledgementDisposition.Received,
                        "Provider/receiver-level rejection or unresolved status requires separate handling.");
                }
            }
            else if (segment[0] == "REF" && segment.Length > 1 && segment[1] is "FJ" or "6R")
            {
                Require(reference is not null && serviceLevel, "A line reference has no claim/service context.");
                AddUnique(lineReferences, Required(segment, 2, 50));
            }
            else if (segment[0] == "AMT") Money(Required(segment, 2, 18));
        }
        FinishClaim();
        Require(claims.Count > 0, "The acknowledgement has no patient-level claim trace and status pairs.");
        return claims;
    }

    private static ClaimAcknowledgementDisposition Disposition(string category) => category switch
    {
        "A2" => ClaimAcknowledgementDisposition.Accepted,
        "A0" or "A1" => ClaimAcknowledgementDisposition.Received,
        "A3" or "A6" or "A7" or "A8" => ClaimAcknowledgementDisposition.Rejected,
        _ => ClaimAcknowledgementDisposition.NeedsReview
    };

    private static RemittanceResult ReadRemittance(X12Document document)
    {
        var segments = document.Transaction;
        Require(segments.All(s => s[0] is "ST" or "BPR" or "TRN" or "CUR" or "REF" or "DTM" or "N1" or "N3" or "N4"
            or "PER" or "RDM" or "LX" or "CLP" or "CAS" or "NM1" or "AMT" or "QTY" or "SVC" or "LQ" or "PLB" or "SE"),
            "The remittance contains unsupported segments, including supplemental institutional/Medicare financial summaries.");
        var bpr = ExactlyOne(segments.Where(s => s[0] == "BPR"));
        Require(segments[1] == bpr && bpr.Length is >= 17 and <= 22, "The remittance financial header is missing, incomplete, or out of order.");
        Require(Required(bpr, 3) == "C", "Only credit-direction 835 payments are supported; a debit flag requires separate handling.");
        Require(Required(bpr, 4) is "ACH" or "CHK" or "NON", "The remittance payment method is unsupported.");
        var payment = Money(Required(bpr, 2, 18));
        Require(Required(bpr, 1) is "C" or "D" or "H" or "I" or "P" or "U" or "X", "The remittance handling code is invalid.");
        Require(bpr[4] != "NON" || payment == 0, "A nonpayment remittance cannot contain a payment amount.");
        Require(payment >= 0, "An 835 total payment cannot be negative; use the payer's balance-forwarding advice.");
        var paymentDate = Date(Required(bpr, 16));
        var trn = ExactlyOne(segments.Where(s => s[0] == "TRN"));
        Require(Required(trn, 1) == "1", "The payment must carry a reassociation trace.");
        var paymentReference = Required(trn, 2, 50);
        var originator = Required(trn, 3, 10);
        Require(originator.Length == 10, "The payment originator identifier is invalid.");
        var payer = ExactlyOne(segments.Where(s => s[0] == "N1" && s.Length > 1 && s[1] == "PR"));
        var payee = ExactlyOne(segments.Where(s => s[0] == "N1" && s.Length > 1 && s[1] == "PE"));
        var payeeQualifier = Required(payee, 3, 2);
        var payeeId = Required(payee, 4, 80);
        Require(payeeQualifier == "XX" && payeeId.Length == 10 && Digits(payeeId), "Only remittances identifying the payee by NPI are supported.");
        Require(!segments.Any(s => s[0] == "CUR" && (s.Length < 3 || s[2] != "USD")), "Non-USD remittances require separate handling.");
        var claims = new List<RemittanceClaimResult>();
        var claimReferences = new HashSet<string>(StringComparer.Ordinal);
        decimal providerAdjustment = 0m;
        var reachedProviderAdjustments = false;
        var reachedClaim = false;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment[0] is "CAS" or "SVC" or "AMT" or "QTY")
                Require(reachedClaim && !reachedProviderAdjustments, "Claim financial detail has no claim context.");
            if (segment[0] is "BPR" or "TRN" or "N1")
                Require(!reachedClaim && !reachedProviderAdjustments, "A remittance header appears after claim detail.");
            if (segment[0] == "PLB")
            {
                reachedProviderAdjustments = true;
                Require(Required(segment, 1) == payeeId, "A provider adjustment identifies a different payee.");
                Date(Required(segment, 2));
                Require(segment.Length is >= 5 and <= 15 && (segment.Length - 3) % 2 == 0, "The provider adjustment pairs are incomplete.");
                for (var field = 3; field < segment.Length; field += 2)
                {
                    Required(segment, field, 80);
                    providerAdjustment += Money(Required(segment, field + 1, 18));
                }
            }
            if (segment[0] != "CLP") continue;
            reachedClaim = true;
            Require(!reachedProviderAdjustments, "A claim appears after provider-level adjustments.");
            var reference = Required(segment, 1, 38);
            Require(claimReferences.Add(reference), "Repeated claim references, including reversal/correction pairs, require separate handling.");
            var code = Required(segment, 2, 2);
            Require(code is "1" or "2" or "3" or "4" or "19" or "20" or "21" or "22", "The remittance claim status requires separate handling.");
            var billed = Money(Required(segment, 3, 18));
            var paid = Money(Required(segment, 4, 18));
            var patient = segment.Length > 5 && segment[5].Length > 0 ? Money(segment[5]) : 0m;
            Require(code == "22" ? billed <= 0 && paid <= 0 && patient <= 0 : billed >= 0 && paid >= 0 && patient >= 0,
                "The claim amounts have signs inconsistent with the payer's disposition.");
            decimal adjustment = 0m, patientAdjustment = 0m;
            string? groupCode = null, reasonCode = null;
            var lineReferences = new List<string>();
            for (var detail = index + 1; detail < segments.Count && segments[detail][0] is not ("CLP" or "PLB" or "SE"); detail++)
            {
                var row = segments[detail];
                if (row[0] == "CAS")
                {
                    var group = Required(row, 1, 2);
                    Require(group is "CO" or "PR" or "OA" or "PI", "The adjustment group is unsupported.");
                    Require(row.Length is >= 4 and <= 20, "The adjustment segment is incomplete or too long.");
                    groupCode ??= group; reasonCode ??= Required(row, 2, 10);
                    for (var field = 2; field < row.Length; field += 3)
                    {
                        if (row.Skip(field).All(string.IsNullOrEmpty)) break;
                        Required(row, field, 10);
                        var amount = Money(Required(row, field + 1, 18));
                        adjustment += amount;
                        if (group == "PR") patientAdjustment += amount;
                        if (field + 2 < row.Length && row[field + 2].Length > 0) Money(row[field + 2]);
                    }
                }
                else if (row[0] == "SVC")
                {
                    Required(row, 1, 80);
                    Money(Required(row, 2, 18)); Money(Required(row, 3, 18));
                }
                else if (row[0] == "REF" && row.Length > 1 && row[1] == "6R")
                    AddUnique(lineReferences, Required(row, 2, 50));
                else if (row[0] is "AMT" or "QTY")
                    Money(Required(row, 2, 18));
            }
            Require(billed - paid == adjustment, "Claim payment and adjustments do not balance to the billed amount.");
            Require(patient == patientAdjustment, "Patient responsibility does not equal the PR adjustments.");
            var status = code switch
            {
                "4" => RemittanceClaimStatus.Denied,
                "22" => RemittanceClaimStatus.Reversed,
                _ => paid == 0 || paid > billed ? RemittanceClaimStatus.NeedsReview
                    : paid == billed ? RemittanceClaimStatus.Paid : RemittanceClaimStatus.PartiallyPaid
            };
            Require(code != "4" || paid == 0, "A denied claim contains a payment requiring review.");
            claims.Add(new(reference, billed, paid, adjustment, patient, status, groupCode, reasonCode,
                status switch
                {
                    RemittanceClaimStatus.Paid => "Paid in full on this advice.",
                    RemittanceClaimStatus.PartiallyPaid => "Paid below billed charges; review the retained adjustment detail.",
                    RemittanceClaimStatus.Denied => "The payer denied the claim; review its reasons before deciding the next action.",
                    RemittanceClaimStatus.Reversed => "This advice reverses a prior payment; the prior evidence remains retained.",
                    _ => paid > billed ? "The reported payment exceeds billed charges; review the retained adjustment detail."
                        : "The payer processed the claim with no payment; review its adjustment detail."
                }) {
                ClaimStatusCode = code, ServiceLineReferences = lineReferences,
                PayerClaimControlNumber = segment.Length > 7 && segment[7].Length is > 0 and <= ClaimCorrectionRules.PayerClaimControlNumberMaxLength
                    ? segment[7] : null
            });
            Require(claims.Count <= 5000, "The remittance has too many claims.");
        }
        Require(claims.Count > 0, "Provider-only remittances require separate handling because no retained claim can be matched.");
        var total = claims.Sum(c => c.PaidAmount);
        Require(total - providerAdjustment == payment, "Claim payments minus provider adjustments do not equal the remittance payment.");
        return new(document.Envelope, paymentReference, Required(payer, 2, 80), paymentDate, total, providerAdjustment, payment, claims)
        {
            PaymentDirection = bpr[3], PaymentOriginatorId = originator, PayeeId = payeeId, PayeeQualifier = payeeQualifier,
            PayerQualifier = payer.Length > 3 ? payer[3] : string.Empty, PayerId = payer.Length > 4 ? payer[4] : string.Empty
        };
    }

    private static string[] ExactlyOne(IEnumerable<string[]> segments)
    {
        var rows = segments.Take(2).ToArray();
        Require(rows.Length == 1, "A required X12 segment is missing or duplicated.");
        return rows[0];
    }

    private static void AddUnique(List<string> references, string reference)
    {
        Require(!references.Contains(reference, StringComparer.Ordinal), "The claim contains a duplicate service-line reference.");
        references.Add(reference);
    }
}
