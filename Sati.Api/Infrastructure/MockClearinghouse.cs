using System.Globalization;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed record MockClearinghouseDocuments(
    string FunctionalAcknowledgement, string? ClaimAcknowledgement, string? RemittanceAdvice);

/// <summary>Synthetic test-only responses using the same bounded intake contracts as uploaded files.</summary>
internal static class MockClearinghouse
{
    public static MockClearinghouseDocuments Respond(string edi837, MockClearinghouseScenario scenario, DateTime respondedAtUtc)
    {
        var submission = ClaimResponseReader.ReadSubmission(edi837);
        if (!submission.Envelope.IsTestInterchange)
            throw new InvalidOperationException("The mock clearinghouse accepts test interchanges only. ISA15 must be 'T'.");
        if (!Enum.IsDefined(scenario))
            throw new InvalidOperationException("The mock clearinghouse scenario is unknown.");
        if (submission.Claims.Any(c => c.ServiceLineReferences.Count > 1))
            throw new InvalidOperationException("The mock currently supports one service line per claim.");
        var stamp = respondedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = respondedAtUtc.ToString("HHmm", CultureInfo.InvariantCulture);
        var rejected = scenario == MockClearinghouseScenario.SyntaxRejected;
        var ackBody = $"AK1*HC*{submission.Envelope.GroupControlNumber}*005010X222A1~\n"
            + $"AK2*837*{submission.Envelope.TransactionControlNumber}*005010X222A1~\n"
            + (rejected ? "IK3*CLM*1**8~\nIK5*R*5~\nAK9*R*1*1*0~\n" : "IK5*A~\nAK9*A*1*1*1~\n");
        var acknowledgement999 = Wrap(submission, scenario, "999", "0001", ackBody, stamp, time);
        if (rejected) return new(acknowledgement999, null, null);

        var acknowledgement277 = BuildAcknowledgement(submission, scenario, stamp, time);
        return new(acknowledgement999, acknowledgement277,
            scenario == MockClearinghouseScenario.ClaimsRejected ? null : BuildRemittance(submission, scenario, stamp, time));
    }

    private static string BuildAcknowledgement(ParsedClaimSubmission submission, MockClearinghouseScenario scenario, string stamp, string time)
    {
        var body = new StringBuilder()
            .Append($"BHT*0085*08*MOCK277-{submission.Envelope.ControlNumber}*{stamp}*{time}*TH~\n")
            .Append("HL*1**20*1~\nNM1*PR*2*MOCK PAYER*****PI*MCDME~\n")
            .Append($"HL*2*1*21*1~\nNM1*41*2*EXAMPLE AGENCY*****46*{submission.Envelope.SenderId}~\n")
            .Append($"TRN*2*MOCK-RECEIVER~\nSTC*A1:19:PR*{stamp}*WQ*{Money(submission.Claims.Sum(c => c.BilledAmount))}~\n")
            .Append($"HL*3*2*19*1~\nNM1*85*2*EXAMPLE AGENCY*****XX*{submission.BillingProviderNpi}~\n");
        for (var i = 0; i < submission.Claims.Count; i++)
        {
            var claim = submission.Claims[i];
            var rejected = scenario == MockClearinghouseScenario.ClaimsRejected
                || scenario == MockClearinghouseScenario.PartiallyAccepted && i % 2 == 1;
            // A1 is receipt only. A7 is rejection despite its 'A' prefix. Only A2 means acceptance into adjudication.
            var status = rejected ? "A7:21:85" : "A2:20:PR";
            body.Append($"HL*{i + 4}*3*PT*0~\nNM1*QC*1*EXAMPLE*SYNTHETIC****MI*TESTMEMBER~\n")
                .Append($"TRN*2*{claim.ClaimReference}~\nSTC*{status}*{stamp}*{(rejected ? "U" : "WQ")}*{Money(claim.BilledAmount)}~\n");
            if (!rejected && claim.ServiceLineReferences.Count == 1)
                body.Append($"SVC*HC:G9012*{Money(claim.BilledAmount)}~\nSTC*A2:20:PR*{stamp}*WQ*{Money(claim.BilledAmount)}~\nREF*FJ*{claim.ServiceLineReferences[0]}~\n");
        }
        return Wrap(submission, scenario, "277", "0002", body.ToString(), stamp, time);
    }

    private static string BuildRemittance(ParsedClaimSubmission submission, MockClearinghouseScenario scenario, string stamp, string time)
    {
        var rows = new StringBuilder();
        decimal paidTotal = 0;
        for (var i = 0; i < submission.Claims.Count; i++)
        {
            if (scenario == MockClearinghouseScenario.PartiallyAccepted && i % 2 == 1) continue;
            var claim = submission.Claims[i];
            var (code, billed, paid, reason) = scenario switch
            {
                MockClearinghouseScenario.Denied => ("4", claim.BilledAmount, 0m, "29"),
                MockClearinghouseScenario.Reversal => ("22", -claim.BilledAmount, -claim.BilledAmount, (string?)null),
                MockClearinghouseScenario.PartialPayment =>
                    ("1", claim.BilledAmount, decimal.Round(claim.BilledAmount * .8m, 2, MidpointRounding.AwayFromZero), "45"),
                _ => ("1", claim.BilledAmount, claim.BilledAmount, (string?)null)
            };
            paidTotal += paid;
            rows.Append($"LX*{i + 1}~\nCLP*{claim.ClaimReference}*{code}*{Money(billed)}*{Money(paid)}*0*MC*MOCK{i + 1:D6}*11*1~\n");
            if (billed != paid)
                rows.Append($"CAS*CO*{reason}*{Money(billed - paid)}~\n");
            rows.Append("NM1*QC*1*EXAMPLE*SYNTHETIC****MI*TESTMEMBER~\n")
                .Append($"SVC*HC:G9012*{Money(billed)}*{Money(paid)}~\n");
            if (claim.ServiceLineReferences.Count == 1)
                rows.Append($"REF*6R*{claim.ServiceLineReferences[0]}~\n");
            rows.Append($"DTM*472*{stamp}~\n");
        }
        // Raw PLB positive reduces payment. A reversal-only cycle forwards its negative balance, yielding BPR02 zero.
        var providerAdjustment = paidTotal < 0 ? paidTotal
            : scenario == MockClearinghouseScenario.ProviderLevelAdjustment ? Math.Min(25m, paidTotal) : 0m;
        var deposit = paidTotal - providerAdjustment;
        var body = new StringBuilder()
            .Append($"BPR*{(deposit == 0 ? "H" : "I")}*{Money(deposit)}*C*{(deposit == 0 ? "NON" : "ACH")}************{stamp}~\n")
            .Append($"TRN*1*MOCK-{submission.Envelope.ControlNumber}-{(int)scenario}*1999999984~\n")
            .Append("N1*PR*MOCK PAYER*XV*MCDME~\n")
            .Append($"N1*PE*EXAMPLE AGENCY*XX*{submission.BillingProviderNpi}~\n")
            .Append(rows);
        if (providerAdjustment != 0)
            body.Append($"PLB*{submission.BillingProviderNpi}*{stamp}*{(paidTotal < 0 ? "FB" : "WO")}:MOCKREF*{Money(providerAdjustment)}~\n");
        return Wrap(submission, scenario, "835", "0003", body.ToString(), stamp, time);
    }

    private static string Wrap(ParsedClaimSubmission submission, MockClearinghouseScenario scenario,
        string type, string transactionControl, string body, string stamp, string time)
    {
        var (group, version) = type switch
        {
            "999" => ("FA", "005010X231A1"),
            "277" => ("HN", "005010X214"),
            _ => ("HP", "005010X221A1")
        };
        var original = long.Parse(submission.Envelope.ControlNumber!, CultureInfo.InvariantCulture);
        var control = ((original + 100 + (int)scenario * 3 + int.Parse(transactionControl, CultureInfo.InvariantCulture)) % 1_000_000_000)
            .ToString("D9", CultureInfo.InvariantCulture);
        var sender = submission.Envelope.ReceiverId;
        var receiver = submission.Envelope.SenderId;
        var count = body.Count(c => c == '~') + 2;
        return $"ISA*00*          *00*          *{submission.Envelope.ReceiverQualifier}*{sender.PadRight(15)}*"
            + $"{submission.Envelope.SenderQualifier}*{receiver.PadRight(15)}*{stamp[2..]}*{time}*^*00501*{control}*0*T*:~\n"
            + $"GS*{group}*{sender}*{receiver}*{stamp}*{time}*{control}*X*{version}~\n"
            + $"ST*{type}*{transactionControl}*{version}~\n{body}SE*{count}*{transactionControl}~\nGE*1*{control}~\nIEA*1*{control}~\n";
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
