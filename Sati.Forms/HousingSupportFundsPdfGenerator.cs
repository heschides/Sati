using System.Globalization;
using System.Reflection;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>
/// Fills Maine DHHS OADS's own Housing Support Funds AcroForm. The original page
/// content and all fields remain in place. Consumer and guardian signatures,
/// signature dates, and every DHHS Staff Only field are intentionally untouched.
/// </summary>
public sealed class HousingSupportFundsPdfGenerator
{
    public const string ResourceName =
        "Sati.Forms.Housing-Support-Funds-Application-2025-06-30.pdf";

    public byte[] Generate(
        HousingSupportFundsSubject subject,
        HousingSupportFundsRequest request,
        DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        var validation = HousingSupportFundsRules.Validate(request);
        if (validation.Count > 0)
            throw new ArgumentException(
                string.Join(" ", validation.SelectMany(entry => entry.Value)), nameof(request));

        using var blank = OpenBlank();
        using var document = PdfReader.Open(blank, PdfDocumentOpenMode.Modify);
        var form = document.AcroForm
            ?? throw new InvalidOperationException("The Housing Support Funds source has no AcroForm.");

        // PDFsharp updates the text appearances below. Keep viewer-side regeneration
        // disabled so renderers use the explicit, portable checkbox appearances too.
        form.Elements.SetBoolean("/NeedAppearances", false);
        document.Info.Title = $"Housing Support Funds application - {subject.ConsumerName}";
        document.Info.Subject = HousingSupportFundsRules.SourceRevision;
        document.Info.ModificationDate = generatedAtUtc;

        SetText(form, "Consumer Name", subject.ConsumerName);
        SetText(form, "Consumer Telephone", request.ConsumerTelephone);
        SetText(form, "Consumer email", request.ConsumerEmail);
        SetText(form, "Consumer address", request.ConsumerAddress);
        SetText(form, "Landlord name", request.LandlordName);
        SetText(form, "Landlord address", request.LandlordAddress);
        SetText(form, "Landlord telephone", request.LandlordTelephone);
        SetText(form, "Landlord email", request.LandlordEmail);
        SetMoney(form, "Monthly rentalmortgage amount", request.MonthlyHousingAmount);
        SetMoney(form, "Amount requested", request.AmountRequested);
        SetText(form, "If yes subsidy type Section 8 Project Based Rental Assistance etc", request.SubsidyType);
        SetText(form, "Guardian name", subject.HasGuardian ? subject.GuardianName : null);
        SetText(form, "Address StreetCityStZip", subject.HasGuardian ? request.GuardianAddress : null);
        SetText(form, "Guardian Telephone", subject.HasGuardian ? request.GuardianTelephone : null);
        SetText(form, "Guardian email", subject.HasGuardian ? request.GuardianEmail : null);
        SetText(form, "Representative Payee", request.RepresentativePayeeName);
        SetText(form, "Address StreetCityStZip_2", request.RepresentativePayeeAddress);
        SetText(form, "Consumer Case ManagerCommunity Resource Coordinator", subject.CaseManagerName);
        SetText(form, "Provider name", subject.ProviderName);
        SetText(form, "Provider Address", subject.ProviderAddress);
        SetText(form, "Provider Telephone", subject.ProviderTelephone);
        SetText(form, "Provider email", subject.ProviderEmail);
        SetMultilineText(form,
            "Please share any additional details regarding your request for Housing Support Funds",
            request.AdditionalDetails);
        SetText(form, "I, Consumer Name", subject.ConsumerName);

        var waiver = HousingSupportFundsRules.NormalizeWaiver(subject.Waiver);
        SetCheck(form, "Section 21", waiver == "Section 21");
        SetCheck(form, "Section 29", waiver == "Section 29");
        SetCheck(form, "Housing rental", request.HousingType == "Rental");
        SetCheck(form, "Housing owned", request.HousingType == "Consumer-owned home");
        SetCheck(form, "Is consumers living situation a Shared Living Situation Check One Yes",
            subject.IsSharedLiving);
        SetCheck(form, "No", !subject.IsSharedLiving);
        SetCheck(form, "Does consumer receive a subsidy Yes", request.ReceivesSubsidy == true);
        SetCheck(form, "No_2", request.ReceivesSubsidy == false);

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void SetMoney(PdfAcroForm form, string fieldName, decimal? value) =>
        SetText(form, fieldName, value?.ToString("0.00", CultureInfo.InvariantCulture));

    private static void SetText(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (form.Fields[fieldName] is not PdfTextField field)
            throw new InvalidOperationException($"'{fieldName}' is not a text field on the Housing Support Funds form.");
        field.Text = value.Trim();
    }

    private static void SetMultilineText(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (form.Fields[fieldName] is not PdfTextField field)
            throw new InvalidOperationException($"'{fieldName}' is not a text field on the Housing Support Funds form.");

        var normalized = value.Trim();
        field.Text = normalized;
        var appearance = field.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")
            ?? throw new InvalidOperationException($"'{fieldName}' has no normal appearance stream.");
        var box = appearance.Elements.GetRectangle("/BBox");
        var (lines, fontSize) = FitLines(normalized, box.Width, box.Height);
        appearance.Stream.Value = BuildMultilineAppearance(lines, fontSize, box.Height);
    }

    private static (IReadOnlyList<string> Lines, double FontSize) FitLines(
        string value,
        double width,
        double height)
    {
        for (var fontSize = 10d; fontSize >= 7d; fontSize -= 0.5d)
        {
            var charactersPerLine = Math.Max(1, (int)Math.Floor((width - 4d) / (fontSize * 0.6d)));
            var lines = WrapForField(value, charactersPerLine);
            var maxLines = Math.Max(1, (int)Math.Floor((height - 8d) / (fontSize * 1.2d)));
            if (lines.Count <= maxLines)
                return (lines, fontSize);
        }

        return (WrapForField(value, Math.Max(1, (int)Math.Floor((width - 4d) / 4.2d))), 7d);
    }

    private static IReadOnlyList<string> WrapForField(string value, int charactersPerLine)
    {
        var lines = new List<string>();
        foreach (var paragraph in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length > charactersPerLine)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = "";
                    }
                    for (var offset = 0; offset < word.Length; offset += charactersPerLine)
                        lines.Add(word.Substring(offset, Math.Min(charactersPerLine, word.Length - offset)));
                    continue;
                }
                if (current.Length == 0)
                    current = word;
                else if (current.Length + 1 + word.Length <= charactersPerLine)
                    current += $" {word}";
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0)
                lines.Add(current);
            else if (paragraph.Length == 0)
                lines.Add("");
        }
        return lines;
    }

    private static byte[] BuildMultilineAppearance(
        IReadOnlyList<string> lines,
        double fontSize,
        double height)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "/Tx BMC\nq\nBT\n0 0 0 rg\n");
        WriteAscii(stream, $"/F0 {fontSize.ToString("0.0", CultureInfo.InvariantCulture)} Tf\n");
        var lineHeight = fontSize * 1.2d;
        var baseline = height - 4d - fontSize;
        for (var index = 0; index < lines.Count; index++)
        {
            WriteAscii(stream,
                $"1 0 0 1 2 {(baseline - (index * lineHeight)).ToString("0.###", CultureInfo.InvariantCulture)} Tm\n(");
            WritePdfString(stream, lines[index]);
            WriteAscii(stream, ") Tj\n");
        }
        WriteAscii(stream, "ET\nQ\nEMC\n");
        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static void WritePdfString(Stream stream, string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var valueByte in Encoding.GetEncoding(1252,
                     EncoderFallback.ReplacementFallback,
                     DecoderFallback.ReplacementFallback).GetBytes(value))
        {
            if (valueByte is (byte)'(' or (byte)')' or (byte)'\\')
                stream.WriteByte((byte)'\\');
            stream.WriteByte(valueByte);
        }
    }

    private static void SetCheck(PdfAcroForm form, string fieldName, bool isChecked)
    {
        if (form.Fields[fieldName] is not PdfCheckBoxField field)
            throw new InvalidOperationException($"'{fieldName}' is not a checkbox on the Housing Support Funds form.");

        var widgets = WidgetsOf(field).ToList();
        var onState = OnStateOf(field, widgets)
            ?? throw new InvalidOperationException($"Checkbox '{fieldName}' declares no checked appearance state.");
        InstallVectorCheckAppearance(field, widgets, onState);
        var state = isChecked ? onState : "/Off";
        field.Elements.SetName(PdfAcroField.Keys.V, state);
        if (widgets.Count == 0)
            field.Elements.SetName(PdfAnnotation.Keys.AS, state);
        else
            foreach (var widget in widgets)
                widget.Elements.SetName(PdfAnnotation.Keys.AS, state);
    }

    private static IEnumerable<PdfDictionary> WidgetsOf(PdfCheckBoxField field)
    {
        var kids = field.Elements.GetArray("/Kids");
        if (kids is null) yield break;
        foreach (var item in kids.Elements)
        {
            if (item is PdfReference reference && reference.Value is PdfDictionary referenced)
                yield return referenced;
            else if (item is PdfDictionary direct)
                yield return direct;
        }
    }

    private static string? OnStateOf(PdfCheckBoxField field, IReadOnlyList<PdfDictionary> widgets)
    {
        static string? State(PdfDictionary dictionary) =>
            dictionary.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Elements.Keys
                .FirstOrDefault(value => !string.Equals(value, "/Off", StringComparison.Ordinal));

        return State(field) ?? widgets.Select(State).FirstOrDefault(value => value is not null);
    }

    /// <summary>
    /// The published blank uses an unembedded ZapfDingbats glyph for its checkmark.
    /// Several standards-compliant renderers cannot display that glyph and print an
    /// apparently unchecked box despite a canonical /On value. Replace only the /On
    /// appearance stream with a small vector tick so the stored value and printed page
    /// agree without changing the state's page content or removing interactivity.
    /// </summary>
    private static void InstallVectorCheckAppearance(
        PdfCheckBoxField field,
        IReadOnlyList<PdfDictionary> widgets,
        string onState)
    {
        var content = Encoding.ASCII.GetBytes(
            "q\n0 0 0 RG\n1.4 w\n1.6 3.7 m\n4.2 1.4 l\n10.4 7.0 l\nS\nQ\n");
        foreach (var dictionary in new[] { (PdfDictionary)field }.Concat(widgets))
        {
            var appearance = dictionary.Elements.GetDictionary("/AP")?
                .Elements.GetDictionary("/N")?
                .Elements.GetDictionary(onState);
            if (appearance?.Stream is not null)
                appearance.Stream.Value = content;
        }
    }

    private static Stream OpenBlank() =>
        typeof(HousingSupportFundsPdfGenerator).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"Embedded blank form '{ResourceName}' is missing.");
}
