using Sati.Contracts.V1;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Sati.Views.ClientDocuments;

/// <summary>
/// Safe, non-executable live preview for the same small template language used by
/// DocumentTemplatePdfComposer. Blank merge fields remain visible as bracketed
/// placeholders here; the generated PDF intentionally leaves them blank.
/// </summary>
public partial class DocumentTemplatePreview : UserControl
{
    private static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(23, 50, 77));
    private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(47, 125, 122));
    private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(205, 214, 220));
    private static readonly Brush HeaderFill = new SolidColorBrush(Color.FromRgb(238, 246, 245));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(DocumentTemplatePreview),
        new PropertyMetadata(string.Empty, OnPreviewInputChanged));

    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(DocumentTemplateRenderContext), typeof(DocumentTemplatePreview),
        new PropertyMetadata(null, OnPreviewInputChanged));

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public DocumentTemplateRenderContext? Context
    {
        get => (DocumentTemplateRenderContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public DocumentTemplatePreview()
    {
        InitializeComponent();
        RenderPreview();
    }

    private static void OnPreviewInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DocumentTemplatePreview)sender).RenderPreview();

    private void RenderPreview()
    {
        if (PreviewViewer is null)
            return;

        var document = new FlowDocument
        {
            Background = Brushes.White,
            Foreground = Navy,
            FontFamily = new FontFamily("Arial"),
            FontSize = 13,
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity
        };

        if (string.IsNullOrWhiteSpace(Source))
        {
            document.Blocks.Add(new Paragraph(new Run("The rendered form will appear here as you type."))
            {
                Foreground = Brushes.DimGray,
                FontStyle = FontStyles.Italic
            });
            PreviewViewer.Document = document;
            return;
        }

        var lines = Source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;
            if (line.Equals(DocumentTemplateRules.PageBreakMarker, StringComparison.Ordinal))
            {
                document.Blocks.Add(new Paragraph(new Run("— PAGE BREAK —"))
                {
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(0, 8, 0, 0),
                    Margin = new Thickness(0, 14, 0, 8)
                });
                continue;
            }
            if (line.StartsWith('|'))
            {
                var tableLines = new List<string>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
                    tableLines.Add(lines[index++].Trim());
                index--;
                AddTable(document, tableLines);
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                document.Blocks.Add(Heading(Merge(line[3..]), 18, Teal));
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                document.Blocks.Add(Heading(Merge(line[2..]), 27, Navy));
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(18, 1, 0, 5) };
                list.ListItems.Add(new ListItem(new Paragraph(new Run(Merge(line[2..])))
                {
                    Margin = new Thickness(0, 0, 0, 3)
                }));
                document.Blocks.Add(list);
                continue;
            }

            document.Blocks.Add(new Paragraph(new Run(Merge(line)))
            {
                Margin = new Thickness(0, 0, 0, 8),
                LineHeight = 19
            });
        }

        PreviewViewer.Document = document;
    }

    private static Paragraph Heading(string text, double size, Brush color) =>
        new(new Run(text))
        {
            FontWeight = FontWeights.Bold,
            FontSize = size,
            Foreground = color,
            Margin = new Thickness(0, size == 27 ? 6 : 10, 0, size == 27 ? 10 : 4),
            KeepWithNext = true
        };

    private void AddTable(FlowDocument document, IReadOnlyList<string> lines)
    {
        var rows = lines.Select(ParseCells).Where(cells => !IsSeparator(cells)).ToList();
        if (rows.Count == 0)
            return;

        var columnCount = rows.Max(row => row.Count);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10) };
        for (var column = 0; column < columnCount; column++)
            table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new TableRow { Background = rowIndex == 0 ? HeaderFill : Brushes.White };
            for (var column = 0; column < columnCount; column++)
            {
                var value = column < rows[rowIndex].Count ? Merge(rows[rowIndex][column]) : string.Empty;
                row.Cells.Add(new TableCell(new Paragraph(new Run(value))
                {
                    Margin = new Thickness(0)
                })
                {
                    BorderBrush = Border,
                    BorderThickness = new Thickness(.5),
                    Padding = new Thickness(5),
                    FontWeight = rowIndex == 0 ? FontWeights.Bold : FontWeights.Normal
                });
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        document.Blocks.Add(table);
    }

    private string Merge(string text)
    {
        var values = Values(Context);
        return DocumentTemplateRules.TokenPattern().Replace(text, match =>
        {
            var token = match.Groups[1].Value;
            return values.GetValueOrDefault(token) ?? $"[{ReadableToken(token)}]";
        });
    }

    private static Dictionary<string, string?> Values(DocumentTemplateRenderContext? context) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["agency.name"] = context?.AgencyName,
            ["agency.address"] = context?.AgencyAddress,
            ["agency.phone"] = context?.AgencyPhone,
            ["consumer.full_name"] = context?.ConsumerFullName,
            ["consumer.birth_date"] = Format(context?.ConsumerBirthDate),
            ["cycle.start"] = Format(context?.CycleStart),
            ["cycle.end"] = Format(context?.CycleEnd),
            ["case_manager.name"] = context?.CaseManagerName,
            ["case_manager.role"] = context?.CaseManagerRole,
            ["provider.name"] = context?.ProviderName,
            ["provider.address"] = context?.ProviderAddress,
            ["provider.phone"] = context?.ProviderPhone,
            ["provider.fax"] = context?.ProviderFax
        };

    private static string? Format(DateTime? value) =>
        value?.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    private static string ReadableToken(string token) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.Replace('.', ' ').Replace('_', ' '));

    private static List<string> ParseCells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private static bool IsSeparator(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(cell => Regex.IsMatch(cell, "^:?-{3,}:?$", RegexOptions.CultureInvariant));
}
