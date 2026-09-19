using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sati.Contracts.V1;

/// <summary>
/// Sole owner of the stored shape of a consumer journal: named pages of paragraphs, each a
/// sequence of text runs (bold, italic, underline) and checkboxes. Shared by <c>Sati.Api</c>
/// and the desktop so the writer that prepends a reminder and the editor that saves the page
/// read and write one format.
///
/// The journal column predates pages and formatting, and every journal written before them is
/// plain text. Plain text is still valid input: it reads as a single page named
/// <see cref="DefaultPageName"/>, one paragraph per line. Only a value that parses as this
/// document — the version marker included — is treated as one, so a plain-text journal that
/// happens to begin with a brace is not misread.
///
/// The format is deliberately a closed set of marks rather than markup or XAML. Stored text
/// is never interpreted, and a client cannot smuggle anything into the record that the editor
/// would execute or render beyond what is listed here.
/// </summary>
public sealed class JournalDocument
{
    public const int CurrentVersion = 1;
    public const string DefaultPageName = "Journal";
    public const int MaxPageNameLength = 40;
    public const int MaxPages = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        MaxDepth = 16
    };

    public JournalDocument(IEnumerable<JournalPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var list = pages.ToList();
        Pages = list.Count == 0 ? [JournalPage.Empty(DefaultPageName)] : list;
    }

    /// <summary>Never empty: a journal always has at least one page to write on.</summary>
    public IReadOnlyList<JournalPage> Pages { get; }

    public static JournalDocument Empty() => new([JournalPage.Empty(DefaultPageName)]);

    /// <summary>
    /// Reads a stored journal. A document reads as itself; anything else — including null,
    /// empty, and every journal written before pages existed — reads as plain text.
    /// </summary>
    public static JournalDocument Parse(string? stored) =>
        TryParseDocument(stored, out var document) ? document : FromPlainText(stored);

    /// <summary>True only for a value this type wrote. Plain text, however it begins, is false.</summary>
    public static bool IsDocument(string? stored) => TryParseDocument(stored, out _);

    public static JournalDocument FromPlainText(string? text) =>
        new([new JournalPage(DefaultPageName, ParagraphsFromText(text))]);

    public string Serialize()
    {
        var stored = new StoredDocument
        {
            Version = CurrentVersion,
            Pages = Pages.Select(page => new StoredPage
            {
                Name = page.Name,
                Paragraphs = page.Paragraphs.Select(paragraph => paragraph.Inlines
                    .Select(inline => inline.IsCheckbox
                        ? new StoredInline { Checkbox = inline.IsChecked ? 2 : 1 }
                        : new StoredInline
                        {
                            Text = inline.Text,
                            Bold = inline.Bold,
                            Italic = inline.Italic,
                            Underline = inline.Underline
                        })
                    .ToList()).ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(stored, JsonOptions);
    }

    /// <summary>
    /// The journal as unformatted text, pages headed by their names. For reading the record
    /// outside the editor; checkboxes render as [ ] and [x].
    /// </summary>
    public string ToPlainText() => string.Join("\r\n\r\n", Pages.Select(page =>
        Pages.Count == 1 ? page.ToPlainText() : $"== {page.Name} ==\r\n{page.ToPlainText()}"));

    /// <summary>
    /// Places <paramref name="lines"/> at the top of the first page, separated from what was
    /// there by one blank paragraph. The first page is where a reader starts; the entry is
    /// written as plain paragraphs because the application, not the case manager, wrote it.
    /// </summary>
    public JournalDocument PrependToFirstPage(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var first = Pages[0];
        var existing = first.Paragraphs.SkipWhile(paragraph => paragraph.IsBlank).ToList();
        var entry = lines.Select(JournalParagraph.FromText).ToList();
        var paragraphs = existing.Count == 0
            ? entry
            : [.. entry, JournalParagraph.Blank, .. existing];
        return new JournalDocument([new JournalPage(first.Name, paragraphs), .. Pages.Skip(1)]);
    }

    /// <summary>The name a page is stored under: trimmed, bounded, never blank.</summary>
    public static string NormalizePageName(string? name, string fallback = DefaultPageName)
    {
        var trimmed = string.Join(' ', (name ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length == 0)
            trimmed = fallback;
        return trimmed.Length <= MaxPageNameLength ? trimmed : trimmed[..MaxPageNameLength];
    }

    private static bool TryParseDocument(string? stored, out JournalDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(stored) || stored.TrimStart()[0] != '{')
            return false;

        StoredDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StoredDocument>(stored, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null || parsed.Version != CurrentVersion || parsed.Pages is null)
            return false;

        document = new JournalDocument(parsed.Pages
            .Where(page => page is not null)
            .Take(MaxPages)
            .Select(page => new JournalPage(
                NormalizePageName(page.Name),
                (page.Paragraphs ?? [])
                    .Select(paragraph => new JournalParagraph((paragraph ?? [])
                        .Where(inline => inline is not null)
                        .Select(inline => inline.Checkbox is 1 or 2
                            ? JournalInline.Checkbox(inline.Checkbox == 2)
                            : JournalInline.Run(inline.Text ?? string.Empty,
                                inline.Bold, inline.Italic, inline.Underline))
                        .Where(inline => inline.IsCheckbox || inline.Text.Length > 0)
                        .ToList()))
                    .ToList())));
        return true;
    }

    private static List<JournalParagraph> ParagraphsFromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(JournalParagraph.FromText)
            .ToList();
    }

    // Short member names keep a long journal's stored size close to its text. The shape is
    // versioned by CurrentVersion; a reader that does not recognize the version reads the
    // value as plain text rather than guessing.
    private sealed class StoredDocument
    {
        [JsonPropertyName("sati-journal")] public int Version { get; set; }
        [JsonPropertyName("pages")] public List<StoredPage>? Pages { get; set; }
    }

    private sealed class StoredPage
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("p")] public List<List<StoredInline>>? Paragraphs { get; set; }
    }

    private sealed class StoredInline
    {
        [JsonPropertyName("t")] public string? Text { get; set; }
        [JsonPropertyName("b")] public bool Bold { get; set; }
        [JsonPropertyName("i")] public bool Italic { get; set; }
        [JsonPropertyName("u")] public bool Underline { get; set; }
        // 1 = unchecked, 2 = checked; absent for text.
        [JsonPropertyName("c")] public int Checkbox { get; set; }
    }
}

public sealed record JournalPage(string Name, IReadOnlyList<JournalParagraph> Paragraphs)
{
    public static JournalPage Empty(string name) => new(name, []);

    public bool IsEmpty => Paragraphs.All(paragraph => paragraph.IsBlank);

    public string ToPlainText() => string.Join("\r\n", Paragraphs.Select(p => p.ToPlainText()));

    public bool Equals(JournalPage? other) =>
        other is not null && Name == other.Name && Paragraphs.SequenceEqual(other.Paragraphs);

    public override int GetHashCode() => HashCode.Combine(Name, Paragraphs.Count);
}

public sealed record JournalParagraph(IReadOnlyList<JournalInline> Inlines)
{
    public static JournalParagraph Blank { get; } = new([]);

    public static JournalParagraph FromText(string text) =>
        text.Length == 0 ? Blank : new([JournalInline.Run(text)]);

    public bool IsBlank => Inlines.All(inline => !inline.IsCheckbox && string.IsNullOrWhiteSpace(inline.Text));

    public string ToPlainText() => string.Concat(Inlines.Select(inline => inline.IsCheckbox
        ? inline.IsChecked ? "[x] " : "[ ] "
        : inline.Text));

    public bool Equals(JournalParagraph? other) =>
        other is not null && Inlines.SequenceEqual(other.Inlines);

    public override int GetHashCode() => Inlines.Count;
}

/// <summary>A run of text with its marks, or a checkbox. A checkbox carries no text.</summary>
public sealed record JournalInline(
    string Text, bool Bold, bool Italic, bool Underline, bool IsCheckbox, bool IsChecked)
{
    public static JournalInline Run(string text, bool bold = false, bool italic = false, bool underline = false) =>
        new(text, bold, italic, underline, IsCheckbox: false, IsChecked: false);

    public static JournalInline Checkbox(bool isChecked) =>
        new(string.Empty, false, false, false, IsCheckbox: true, IsChecked: isChecked);
}
