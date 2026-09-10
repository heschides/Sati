using System.Globalization;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Sati.Models;

namespace Sati.Reporting;

/// <summary>Reproduces the agency's original one-page check requisition form.</summary>
public sealed class CheckRequestPdfExporter
{
    public byte[] Generate(CheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = CreateDocument(request);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        if (request.PublishedAtUtc is DateTime published)
        {
            renderer.PdfDocument.Info.CreationDate = published;
            renderer.PdfDocument.Info.ModificationDate = published;
        }
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    public static string SuggestedFileName(CheckRequest request)
    {
        var date = (request.PublishedAtUtc ?? request.RequestDate ?? DateTime.Today)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var consumer = new string(request.ConsumerName
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return $"Check-Request-{date}-{(string.IsNullOrEmpty(consumer) ? "Consumer" : consumer)}.pdf";
    }

    private static Document CreateDocument(CheckRequest request)
    {
        var document = new Document();
        document.Info.Title = $"Check Requisition - {request.ConsumerName}";
        document.Info.Subject = "Representative payee check request";
        document.Info.Author = "SatiLogica";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Times New Roman";
        normal.Font.Size = 12;
        normal.Font.Color = Colors.Black;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.LeftMargin = Unit.FromInch(0.5);
        section.PageSetup.RightMargin = Unit.FromInch(0.5);
        section.PageSetup.TopMargin = Unit.FromInch(0.45);
        section.PageSetup.BottomMargin = Unit.FromInch(0.5);

        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.75);
        table.Borders.Color = Colors.Black;
        table.AddColumn(Unit.FromInch(2.75));
        table.AddColumn(Unit.FromInch(2.05));
        table.AddColumn(Unit.FromInch(2.70));

        var header = table.AddRow();
        header.Height = Unit.FromInch(0.78);
        header.VerticalAlignment = VerticalAlignment.Center;
        AddCentered(header.Cells[0], request.AgencyName, 11, bold: true);
        AddCentered(header.Cells[0], "Rep Payee Account", 11, bold: true);
        AddCentered(header.Cells[0], $"FOR: {request.ConsumerName}", 11, bold: true);
        AddCentered(header.Cells[1], "Check Requisition", 14, bold: true);
        AddField(header.Cells[2], "Date:", FormatDate(request.RequestDate));

        var payee = table.AddRow();
        payee.Height = Unit.FromInch(0.55);
        payee.Cells[0].MergeRight = 1;
        AddField(payee.Cells[0], "Check payable to:", request.PayableTo);
        AddField(payee.Cells[2], "Address:", request.MailingAddress);

        var amount = table.AddRow();
        amount.Height = Unit.FromInch(0.5);
        amount.Cells[0].MergeRight = 1;
        AddField(amount.Cells[0], "Amount:", request.Amount > 0 ? request.Amount.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : "");
        AddField(amount.Cells[2], "Date NEEDED by:", FormatDate(request.NeededByDate));

        var reason = table.AddRow();
        reason.Height = Unit.FromInch(0.65);
        reason.Cells[0].MergeRight = 2;
        AddField(reason.Cells[0], "Reason:", request.Reason);

        var manager = table.AddRow();
        manager.Height = Unit.FromInch(0.42);
        manager.Cells[0].MergeRight = 1;
        AddField(manager.Cells[0], "Adult Case Manager Signature:", "");
        AddCentered(manager.Cells[2], request.CaseManagerName, 12, bold: true);

        var supervisor = table.AddRow();
        supervisor.Height = Unit.FromInch(0.42);
        supervisor.Cells[0].MergeRight = 1;
        AddField(supervisor.Cells[0], "Supervisor Signature:", "");
        AddCentered(supervisor.Cells[2], request.SupervisorName, 12, bold: true);

        var cut = section.AddParagraph();
        cut.Format.SpaceBefore = Unit.FromPoint(5);
        cut.Format.Font.Size = 9;
        cut.AddText(new string('-', 132) + " cut here");
        return document;
    }

    private static void AddField(Cell cell, string label, string? value)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        var paragraph = cell.AddParagraph();
        paragraph.Format.LeftIndent = Unit.FromPoint(5);
        paragraph.Format.RightIndent = Unit.FromPoint(5);
        paragraph.AddFormattedText(label + " ", TextFormat.Bold);
        paragraph.AddText(value ?? string.Empty);
    }

    private static void AddCentered(Cell cell, string? text, double size, bool bold)
    {
        var paragraph = cell.AddParagraph(text ?? string.Empty);
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = bold;
    }

    private static string FormatDate(DateTime? value) =>
        value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
}
