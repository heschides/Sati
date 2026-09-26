using System.Globalization;
using System.Text;

namespace Sati.Contracts.V1;

/// <summary>Persistence-neutral input for one frozen professional claim.</summary>
public sealed record Professional837Claim(
    int NoteId,
    ProfessionalClaimLineFacts Line,
    string FrequencyCode,
    string? PayerClaimControlNumber);

/// <summary>
/// The one 837P rendering owner for local and API generation. Billing eligibility,
/// correction permission, submission history, and transmission remain with callers.
/// </summary>
public static class Professional837Formatter
{
    private const string VersionCode = "005010X222A1";
    private const string SubSep = ":";

    public static void ValidatePeriod(int year, int month, IReadOnlyList<Professional837Claim> claims) =>
        _ = ReadAndValidateRows(year, month, claims);

    public static string Generate(
        int periodId,
        int year,
        int month,
        IReadOnlyList<Professional837Claim> claims,
        TradingPartnerProfile profile,
        bool isTest,
        DateTime generatedAt,
        string controlNumber)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ClaimSubmissionIdentity.RequireControlNumber(controlNumber);
        if (profile.Version != TradingPartnerProfile.CurrentVersion)
            throw new InvalidOperationException("Unsupported trading-partner profile version.");
        var rows = ReadAndValidateRows(year, month, claims);
        var envelope = rows[0].Snapshot;
        var submitterId = profile.SenderId(envelope);
        var receiverId = profile.ReceiverId;
        var date = generatedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = generatedAt.ToString("HHmm", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();

        builder.AppendLine(Segment("ISA", "00", "          ", "00", "          ", "ZZ",
            submitterId.PadRight(15), "ZZ", receiverId.PadRight(15), date[2..], time,
            "^", "00501", controlNumber, "0", isTest ? "T" : "P", SubSep));
        builder.AppendLine(Segment("GS", "HC", submitterId, receiverId, date, time, controlNumber, "X", VersionCode));
        builder.AppendLine(Segment("ST", "837", "0001", VersionCode));
        builder.AppendLine(Segment("BHT", "0019", "00", controlNumber, date, time, "CH"));
        builder.AppendLine(Segment("NM1", "41", "2", envelope.BillingProviderName, "", "", "", "", "46", submitterId));
        builder.AppendLine(Segment("PER", "IC", envelope.SubmitterContactName, "TE", envelope.SubmitterContactPhone));
        builder.AppendLine(Segment("NM1", "40", "2", profile.ReceiverName, "", "", "", "", "46", receiverId));
        builder.AppendLine(Segment("HL", "1", "", "20", "1"));
        builder.AppendLine(Segment("PRV", "BI", "PXC", "251B00000X"));
        builder.AppendLine(Segment("NM1", "85", "2", envelope.BillingProviderName, "", "", "", "", "XX", envelope.BillingProviderNpi));
        builder.AppendLine(Segment("N3", envelope.BillingProviderStreet));
        builder.AppendLine(Segment("N4", envelope.BillingProviderCity, envelope.BillingProviderState, envelope.BillingProviderZip));
        builder.AppendLine(Segment("REF", "EI", envelope.BillingProviderTaxId));

        var hierarchicalId = 2;
        foreach (var group in rows.GroupBy(row => row.Snapshot.PersonId))
        {
            var subscriber = group.First().Snapshot;
            builder.AppendLine(Segment("HL", hierarchicalId++.ToString(CultureInfo.InvariantCulture), "1", "22", "0"));
            builder.AppendLine(Segment("SBR", "P", "18", "", "", "", "", "", "", "MC"));
            builder.AppendLine(Segment("NM1", "IL", "1", subscriber.SubscriberLastName,
                subscriber.SubscriberFirstName, "", "", "", "MI", subscriber.SubscriberMemberId));
            builder.AppendLine(Segment("N3", subscriber.SubscriberStreet));
            builder.AppendLine(Segment("N4", subscriber.SubscriberCity, subscriber.SubscriberState, subscriber.SubscriberZip));
            builder.AppendLine(Segment("DMG", "D8",
                subscriber.SubscriberBirthDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                subscriber.SubscriberGenderCode));
            builder.AppendLine(Segment("NM1", "PR", "2", subscriber.PayerName, "", "", "", "", "PI", subscriber.PayerId));

            var lineNumber = 1;
            foreach (var row in group)
            {
                var line = row.Claim.Line;
                var units = BillingRules.FormatDecimal(line.Units ?? 0m);
                var charge = BillingRules.FormatDecimal(line.ChargeAmount);
                var procedure = $"HC{SubSep}{line.ProcedureCode}" +
                    (string.IsNullOrWhiteSpace(line.ProcedureModifier) ? string.Empty : $"{SubSep}{line.ProcedureModifier}");
                builder.AppendLine(Segment("CLM", ClaimSubmissionIdentity.ClaimReference(controlNumber, periodId, row.Claim.NoteId), charge, "", "",
                    $"{line.PlaceOfService:D2}{SubSep}{SubSep}{row.Claim.FrequencyCode}", "Y", "A", "Y", "I"));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                if (profile.Kind == TradingPartnerKind.ClaimMd)
                    builder.AppendLine(Segment("REF", "D9", ClaimSubmissionIdentity.RemoteClaimId(
                        profile.ClaimNamespace!, envelope.AgencyId, periodId, row.Claim.NoteId)));
                // Both D9 and F8 are Loop 2300 references and precede the diagnosis segment.
                if (row.Claim.PayerClaimControlNumber is { Length: > 0 } payerClaimNumber)
                    builder.AppendLine(Segment("REF", "F8", payerClaimNumber));
                builder.AppendLine(Segment("HI", $"ABK{SubSep}{line.DiagnosisCode}"));
                builder.AppendLine(Segment("LX", lineNumber++.ToString(CultureInfo.InvariantCulture)));
                builder.AppendLine(Segment("SV1", procedure, charge, "UN", units,
                    line.PlaceOfService.ToString("D2", CultureInfo.InvariantCulture), "", ""));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                builder.AppendLine(Segment("REF", "6R", row.Claim.NoteId.ToString(CultureInfo.InvariantCulture)));
            }
        }

        // SE01 counts ST through SE inclusive; ISA and GS are outside the transaction set.
        var segmentCount = builder.ToString().Count(character => character == '~') - 1;
        builder.AppendLine(Segment("SE", segmentCount.ToString(CultureInfo.InvariantCulture), "0001"));
        builder.AppendLine(Segment("GE", "1", controlNumber));
        builder.AppendLine(Segment("IEA", "1", controlNumber));
        return builder.ToString();
    }

    private static List<ClaimRow> ReadAndValidateRows(
        int year, int month, IReadOnlyList<Professional837Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var readiness = ProfessionalClaimReadiness.EvaluatePeriod(year, month, claims.Select(claim => claim.Line));
        if (!readiness.IsReady)
            throw new InvalidOperationException(readiness.ExplainFailure());
        if (claims.Any(claim => claim.NoteId <= 0 || claim.FrequencyCode is not ("1" or "7" or "8") ||
                (claim.FrequencyCode is "7" or "8" && string.IsNullOrWhiteSpace(claim.PayerClaimControlNumber)) ||
                (claim.FrequencyCode == "1" && claim.PayerClaimControlNumber is not null) ||
                (claim.PayerClaimControlNumber is not null && !BillingRules.IsSafeX12Element(claim.PayerClaimControlNumber, 50))))
            throw new InvalidOperationException("A claim has an invalid frequency, note identity, or payer claim number.");

        return claims.Select(claim => new ClaimRow(claim,
            ProfessionalClaimSnapshotCodec.Deserialize(claim.Line.ClaimSnapshotJson))).ToList();
    }

    private static string Segment(string id, params string[] elements) =>
        id + "*" + string.Join("*", elements) + "~";

    private sealed record ClaimRow(Professional837Claim Claim, ProfessionalClaimSnapshot Snapshot);
}
