using System.Globalization;
using System.Reflection;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>Fills OADS's April 2026 AcroForm without changing its page content or signatures.</summary>
public sealed class SafetyDevicePdfGenerator
{
    public const string ResourceName = "Sati.Forms.Safety-Device-Request-Form-2026-04.pdf";

    public byte[] Generate(SafetyDeviceSubject subject, SafetyDeviceRequest request, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        var errors = SafetyDeviceRules.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.SelectMany(entry => entry.Value)), nameof(request));

        using var blank = typeof(SafetyDevicePdfGenerator).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded OADS form '{ResourceName}' is missing.");
        using var document = PdfReader.Open(blank, PdfDocumentOpenMode.Modify);
        var form = document.AcroForm
            ?? throw new InvalidOperationException("The OADS safety device form has no AcroForm.");
        form.Elements.SetBoolean("/NeedAppearances", false);
        document.Info.Title = $"Safety Device Request Form - {subject.FullName}";
        document.Info.Subject = SafetyDeviceRules.SourceRevision;
        document.Info.ModificationDate = generatedAtUtc;

        SetText(form, "Member Legal Name", subject.FullName);
        SetText(form, "Date of Birth", subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
        SetText(form, "Evergreen If known", subject.EvergreenId);
        SetText(form, "MaineCare", subject.MaineCareId);
        SetText(form, "Member Address", subject.Address);
        SetText(form, "Legal Guardians if any", subject.GuardianName);
        SetText(form, "MemberLegal Guardian Contact Information", request.MemberOrGuardianContact, 30);
        SetText(form, "Case Manager", subject.CaseManagerName);
        SetText(form, "Case Mgr Email Address", subject.CaseManagerEmail);
        SetText(form, "ServiceWaiver Program Names and Address", request.ProgramNameAndAddress, 70);
        SetText(form, "ServiceWaiver Program Contacts", request.ProgramContacts, 32);
        SetText(form, "ServiceWaiver Program Contact Numberss", request.ProgramContactNumbers, 30);
        SetText(form, "ServiceWaiver Program Contact Email Addresss", request.ProgramContactEmails, 70);
        SetText(form, "Medical Providers Name", request.MedicalProviderName, 28);
        SetText(form, "for the person", request.LessRestrictiveStrategies, 68);
        SetText(form, "reviewed by the Team", request.EvaluationPlan, 68);
        SetText(form, "identify accommodations used to minimize the impact on other such persons",
            request.OtherResidentsAccommodations, 68);
        SetText(form, "recommended by the Medical Provider",
            request.PlanningTeamMeetingDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));

        for (var index = 0; index < (request.Devices?.Count ?? 0); index++)
        {
            var entry = request.Devices![index];
            var prefix = index < 5 ? "Safety Devices" : "Medical Providers Recommendations";
            var row = index % 5;
            SetText(form, $"{prefix}.{row}.0", entry.NameAndType, 17);
            SetText(form, $"{prefix}.{row}.1", entry.Purpose, 12);
            SetText(form, $"{prefix}.{row}.2", entry.WhenUsed, 15);
            SetText(form, $"{prefix}.{row}.3.0", entry.Level);
            // .3.1 is the provider date and signature cell, never case-manager input.
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void SetText(PdfAcroForm form, string name, string? value, int? wrapWidth = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (form.Fields[name] is not PdfTextField field)
            throw new InvalidOperationException($"'{name}' is not a text field on the OADS safety device form.");
        field.Text = value.Trim();
        if (wrapWidth is not int width) return;
        var lines = SafetyDeviceRules.WrapForPdf(value, width).Split('\n');
        if (lines.Length < 2) return;
        var appearance = field.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")
            ?? throw new InvalidOperationException($"'{name}' has no normal appearance stream.");
        var height = appearance.Elements.GetRectangle("/BBox").Height;
        appearance.Stream.Value = BuildMultilineAppearance(lines, height);
    }

    private static byte[] BuildMultilineAppearance(IReadOnlyList<string> lines, double height)
    {
        const double fontSize = 9d;
        using var stream = new MemoryStream();
        WriteAscii(stream, "/Tx BMC\nq\nBT\n0 0 0 rg\n/F0 9 Tf\n");
        for (var index = 0; index < lines.Count; index++)
        {
            var baseline = height - 4d - fontSize - index * fontSize * 1.15d;
            WriteAscii(stream, $"1 0 0 1 2 {baseline.ToString("0.###", CultureInfo.InvariantCulture)} Tm\n(");
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
}
