using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>Writes typed answers onto the published OFI application artwork.</summary>
public sealed class BenefitsApplicationPdfGenerator
{
    private const string Resource = "Sati.Forms.OFI-Application-for-Benefits-2024-04-30.pdf";
    private const double Scale = 612d / 850d;
    private static readonly XPen MarkPen = new(XColors.Black, 1.1);

    public byte[] Generate(BenefitsApplicationSubject subject,
        BenefitsApplicationRequest request, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var errors = BenefitsApplicationRules.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException("The application contains an invalid answer.", nameof(request));

        using var source = OpenSource();
        using var background = XPdfForm.FromStream(source);
        if (background.PageCount != 20)
            throw new InvalidOperationException("The OFI source application must have 20 pages.");

        var document = new PdfDocument();
        document.Info.Title = $"Application for Benefits - Draft - {subject.FullName}";
        document.Info.Subject = BenefitsApplicationRules.SourceRevision;
        document.Info.Author = "Sati";
        document.Info.CreationDate = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        document.Info.ModificationDate = document.Info.CreationDate;

        for (var pageNumber = 1; pageNumber <= 20; pageNumber++)
        {
            background.PageNumber = pageNumber;
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(background, 0, 0, 612, 792);

            if (pageNumber == 4)
            {
                DrawText(graphics, subject.FullName, 40, 785, 357);
                DrawText(graphics, subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), 650, 785, 154);
                DrawText(graphics, subject.Ssn, 413, 785, 220);
            }
            if (pageNumber == 15 && IsChecked(request, "appendixB.authorized"))
            {
                DrawText(graphics, subject.FullName, 169, 133, 636);
                DrawText(graphics, subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), 129, 157, 250);
                DrawText(graphics, subject.Ssn, 551, 157, 252);
                if (request.Answers.TryGetValue("person1.homeAddress", out var homeAddress))
                    DrawText(graphics, homeAddress, 191, 184, 612);
            }

            foreach (var field in BenefitsApplicationRules.Fields.Where(field => field.Page == pageNumber))
            {
                if (field.Key == "appendixB.authorized" ||
                    pageNumber == 15 && !IsChecked(request, "appendixB.authorized") ||
                    !request.Answers.TryGetValue(field.Key, out var answer) ||
                    string.IsNullOrWhiteSpace(answer))
                    continue;
                switch (field.Kind)
                {
                    case BenefitsAnswerKind.Text:
                        if (field.Key == "emergency.explanation")
                            DrawEmergencyExplanation(graphics, answer);
                        else
                            DrawText(graphics, answer, field.X, field.Y, field.Width);
                        break;
                    case BenefitsAnswerKind.YesNo:
                        DrawMark(graphics, answer == "Yes" ? field.X : field.OtherX, field.Y);
                        break;
                    case BenefitsAnswerKind.Check when answer == "True":
                        DrawMark(graphics, field.X, field.Y);
                        break;
                }
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static bool IsChecked(BenefitsApplicationRequest request, string key) =>
        request.Answers.TryGetValue(key, out var value) && value == "True";

    private static void DrawText(XGraphics graphics, string? value, double x,
        double y, double width)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        var size = 8d;
        XFont font;
        do
        {
            font = new XFont("Arial", size, XFontStyleEx.Regular);
            if (graphics.MeasureString(normalized, font).Width <= width * Scale) break;
            size -= 0.25;
        } while (size >= 5);
        if (graphics.MeasureString(normalized, font).Width > width * Scale)
            throw new ArgumentException("An application answer is too long for its printed box.");
        graphics.DrawString(normalized, font, XBrushes.Black,
            new XRect(x * Scale, y * Scale, width * Scale, 12), XStringFormats.TopLeft);
    }

    private static void DrawEmergencyExplanation(XGraphics graphics, string value)
    {
        // The first line begins after the state's printed prompt. The remaining
        // three ruled lines span the page. No answer may overlap the prompt.
        var font = new XFont("Arial", 8, XFontStyleEx.Regular);
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in words)
        {
            var width = lines.Count == 0 ? 238d : 763d;
            var next = line.Length == 0 ? word : $"{line} {word}";
            if (graphics.MeasureString(next, font).Width <= width * Scale)
            {
                line = next;
                continue;
            }
            if (line.Length == 0 || lines.Count == 3)
                throw new ArgumentException("The emergency explanation is too long for the printed form.");
            lines.Add(line);
            line = word;
        }
        if (line.Length > 0) lines.Add(line);
        if (lines.Count > 4)
            throw new ArgumentException("The emergency explanation is too long for the printed form.");
        for (var index = 0; index < lines.Count; index++)
            DrawText(graphics, lines[index], index == 0 ? 569 : 39,
                782 + index * 27, index == 0 ? 238 : 763);
    }

    private static void DrawMark(XGraphics graphics, double x, double y)
    {
        x *= Scale;
        y *= Scale;
        graphics.DrawLine(MarkPen, x, y, x + 5, y + 5);
        graphics.DrawLine(MarkPen, x + 5, y, x, y + 5);
    }

    private static Stream OpenSource()
    {
        var stream = typeof(BenefitsApplicationPdfGenerator).GetTypeInfo().Assembly
            .GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The embedded OFI application is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        stream.Dispose();
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, BenefitsApplicationRules.SourceSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The embedded OFI application revision changed.");
        return new MemoryStream(bytes, writable: false);
    }
}

public sealed record BenefitsApplicationSubject(string FullName, DateTime BirthDate, string? Ssn);
