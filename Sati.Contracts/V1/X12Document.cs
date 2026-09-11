using System.Globalization;
using System.Text;

namespace Sati.Contracts.V1;

/// <summary>A bounded 5010 envelope reader, not a complete licensed X12 implementation-guide validator.</summary>
internal sealed record X12Document(
    ClaimResponseEnvelope Envelope,
    string Type,
    char ComponentSeparator,
    IReadOnlyList<string[]> Transaction)
{
    internal const int MaximumLength = 2 * 1024 * 1024;
    private const int MaximumSegments = 25000;

    internal static X12Document Read(string x12)
    {
        ArgumentNullException.ThrowIfNull(x12);
        Require(x12.Length is >= 106 and <= MaximumLength, "The X12 document is empty, incomplete, or exceeds the 2 MiB limit.");
        Require(x12.StartsWith("ISA", StringComparison.Ordinal), "The X12 document must begin with its fixed-width ISA header.");
        Require(x12.All(c => c is >= ' ' and <= '~' or '\r' or '\n'), "The X12 document contains unsupported characters.");
        var element = x12[3];
        var repetition = x12[82];
        var component = x12[104];
        var terminator = x12[105];
        var separators = new[] { element, repetition, component, terminator };
        Require(separators.Distinct().Count() == 4 && separators.All(c => c is >= '!' and <= '~' && !char.IsLetterOrDigit(c)),
            "The X12 delimiters must be four distinct punctuation characters.");
        int[] elementPositions = [3, 6, 17, 20, 31, 34, 50, 53, 69, 76, 81, 83, 89, 99, 101, 103];
        Require(elementPositions.All(index => x12[index] == element), "The ISA header does not have the required fixed widths.");
        var isa = x12[..105].Split(element);
        Require(isa.Length == 17 && isa[12] == "00501", "Only the 00501 interchange version is supported.");
        Require(isa[15] is "T" or "P", "ISA15 must explicitly identify a test or production interchange.");
        Require(isa[14] is "0" or "1", "The ISA acknowledgement indicator is invalid.");
        Require(isa[13].Length == 9 && Digits(isa[13]), "The ISA interchange control number is invalid.");
        Require(DateTime.TryParseExact(isa[9], "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "The ISA date is invalid.");
        Require(TimeOnly.TryParseExact(isa[10], "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "The ISA time is invalid.");
        var segments = new List<string[]>();
        var cursor = 0;
        while (cursor < x12.Length)
        {
            while (cursor < x12.Length && x12[cursor] is '\r' or '\n') cursor++;
            if (cursor == x12.Length) break;
            var end = x12.IndexOf(terminator, cursor);
            Require(end >= cursor && end - cursor <= 8192, "The X12 segment is unterminated or too long.");
            var raw = x12[cursor..end];
            Require(raw.Length > 0 && !raw.Contains('\r') && !raw.Contains('\n'), "The X12 document has an empty or broken segment.");
            var fields = raw.Split(element);
            Require(fields.Length <= 100 && fields[0].Length is >= 2 and <= 3 && fields[0].All(char.IsAsciiLetterOrDigit), "The X12 segment shape is invalid.");
            Require(fields.Skip(1).All(f => f.Length <= 2048), "An X12 element exceeds its intake limit.");
            // Repetition is not used by the supported fields. Do not silently treat a repeated value as scalar.
            Require(fields[0] == "ISA" || !raw.Contains(repetition), "Repeated X12 elements are not supported by this intake.");
            segments.Add(fields);
            Require(segments.Count <= MaximumSegments, "The X12 document has too many segments.");
            cursor = end + 1;
        }
        Require(segments.Count >= 7, "The X12 envelope or transaction is incomplete.");
        Require(segments.Count(s => s[0] == "ISA") == 1 && segments.Count(s => s[0] == "GS") == 1 && segments.Count(s => s[0] == "ST") == 1,
            "Import exactly one interchange, functional group, and transaction at a time.");
        Require(segments[0][0] == "ISA" && segments[1][0] == "GS" && segments[2][0] == "ST"
            && segments[^3][0] == "SE" && segments[^2][0] == "GE" && segments[^1][0] == "IEA",
            "The X12 envelope segments are missing or out of order.");
        Require(segments.Count(s => s[0] is "SE" or "GE" or "IEA") == 3, "The X12 envelope contains duplicate trailers.");
        var gs = segments[1]; var st = segments[2]; var se = segments[^3]; var ge = segments[^2]; var iea = segments[^1];
        Require(gs.Length == 9 && st.Length is 3 or 4 && se.Length == 3 && ge.Length == 3 && iea.Length == 3,
            "The X12 envelope field count is invalid.");
        Require(gs[7] == "X" && Digits(gs[6]) && gs[6].Length <= 9, "The X12 functional group identity is invalid.");
        Require(DateTime.TryParseExact(gs[4], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "The functional group date is invalid.");
        Require(st[2].Length is >= 4 and <= 9 && st[2].All(char.IsAsciiLetterOrDigit), "The transaction control number is invalid.");
        Require(Count(se[1]) == segments.Count - 4 && se[2] == st[2], "The transaction segment count or control number does not match its trailer.");
        Require(Count(ge[1]) == 1 && ge[2] == gs[6] && Count(iea[1]) == 1 && iea[2] == isa[13],
            "The group or interchange count/control does not match its trailer.");
        var type = st[1];
        var (expectedGroup, expectedVersion) = type switch
        {
            "999" => ("FA", "005010X231A1"),
            "277" => ("HN", "005010X214"),
            "835" => ("HP", "005010X221A1"),
            "837" => ("HC", "005010X222A1"),
            _ => (string.Empty, string.Empty)
        };
        if (expectedGroup.Length > 0)
        {
            Require(gs[1] == expectedGroup && gs[8] == expectedVersion && (st.Length == 3 || st[3] == expectedVersion),
                "The transaction type, functional group, or implementation version is unsupported or inconsistent.");
        }
        var envelope = new ClaimResponseEnvelope(type switch
        {
            "999" => ClaimResponseKind.FunctionalAcknowledgement,
            "277" => ClaimResponseKind.ClaimAcknowledgement,
            "835" => ClaimResponseKind.RemittanceAdvice,
            _ => ClaimResponseKind.Unrecognised
        }, isa[15] == "T", isa[13])
        {
            SenderQualifier = Required(isa, 5), SenderId = Required(isa, 6),
            ReceiverQualifier = Required(isa, 7), ReceiverId = Required(isa, 8),
            GroupControlNumber = gs[6], TransactionControlNumber = st[2], ImplementationVersion = gs[8]
        };
        return new(envelope, type, component, segments.Skip(2).Take(segments.Count - 4).ToArray());
    }

    internal string CanonicalTransaction()
    {
        var builder = new StringBuilder();
        foreach (var segment in Transaction)
        {
            builder.Append('[');
            for (var i = 0; i < segment.Length; i++)
            {
                if (segment[0] is "ST" or "SE" && i == 2) continue;
                // Length framing remains unambiguous even when a different interchange uses '*' as data.
                foreach (var component in segment[i].Split(ComponentSeparator))
                    builder.Append(component.Length).Append(':').Append(component);
                builder.Append(';');
            }
            builder.Append(']');
        }
        return builder.ToString();
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }

    internal static string Required(string[] fields, int index, int maxLength = 80)
    {
        Require(index < fields.Length && fields[index].Trim().Length is > 0 && fields[index].Trim().Length <= maxLength,
            "A required X12 field is absent or too long.");
        return fields[index].Trim();
    }

    internal static int Count(string value)
    {
        Require(value.Length <= 6 && Digits(value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _), "An X12 count is invalid.");
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    internal static bool Digits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    internal static decimal Money(string value)
    {
        var unsigned = value.StartsWith('-') ? value[1..] : value;
        var pieces = unsigned.Split('.');
        Require(value.Length <= 18 && pieces.Length is 1 or 2 && Digits(pieces[0])
            && (pieces.Length == 1 || pieces[1].Length is 1 or 2 && Digits(pieces[1])),
            "A monetary amount is malformed or has more than two decimal places.");
        Require(decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            && Math.Abs(parsed) <= 9999999999999999.99m, "A monetary amount exceeds the supported precision.");
        return parsed;
    }

    internal static DateTime Date(string value)
    {
        Require(DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date), "An X12 date is invalid.");
        return date;
    }
}
