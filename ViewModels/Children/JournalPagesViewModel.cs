using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children;

/// <summary>
/// One named page of a consumer journal. The editor owns the live rich text; this holds the
/// page as the contract stores it, so the page can be serialized without the view.
/// </summary>
public partial class JournalPageViewModel : ObservableObject
{
    private readonly Action<JournalPageViewModel> _edited;

    internal JournalPageViewModel(JournalPage page, Action<JournalPageViewModel> edited)
    {
        _edited = edited;
        name = page.Name;
        editingName = page.Name;
        Paragraphs = page.Paragraphs;
    }

    [ObservableProperty]
    private string name;

    // The text in the rename box, committed to Name only on Enter or focus loss so
    // an abandoned rename (Escape) leaves the page as it was.
    [ObservableProperty]
    private string editingName;

    [ObservableProperty]
    private bool isRenaming;

    /// <summary>
    /// The page's content as last saved from, or loaded into, the editor. A change
    /// raised through PropertyChanged means the content was REPLACED from outside —
    /// a load or a reminder the server wrote — and the editor must redraw. An edit
    /// made in the editor arrives through <see cref="ApplyEditorContent"/>, which
    /// deliberately does not raise it, so typing is never redrawn under the caret.
    /// </summary>
    public IReadOnlyList<JournalParagraph> Paragraphs { get; private set; }

    public bool IsEmpty => Paragraphs.All(paragraph => paragraph.IsBlank);

    public JournalPage ToPage() => new(Name, Paragraphs);

    /// <summary>Called by the editor after the case manager changes this page.</summary>
    public void ApplyEditorContent(IReadOnlyList<JournalParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (Paragraphs.SequenceEqual(paragraphs))
            return;
        Paragraphs = paragraphs;
        OnPropertyChanged(nameof(IsEmpty));
        _edited(this);
    }

    internal void ReplaceContent(IReadOnlyList<JournalParagraph> paragraphs)
    {
        Paragraphs = paragraphs;
        OnPropertyChanged(nameof(Paragraphs));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// The pages of the journal on screen: which exist, which is selected, and adding, renaming,
/// and removing them. Every change is reported through <see cref="DocumentEdited"/>; the
/// owner serializes the pages into the journal column and its existing debounced save does
/// the rest. <see cref="JournalDocument"/> owns the stored shape.
/// </summary>
public partial class JournalPagesViewModel : ObservableObject
{
    public ObservableCollection<JournalPageViewModel> Pages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePageCommand))]
    private JournalPageViewModel? selectedPage;

    public JournalPagesViewModel()
    {
        Load(null);
    }

    /// <summary>Raised after any change the case manager makes to the pages or their content.</summary>
    public event EventHandler? DocumentEdited;

    public string Serialize() =>
        new JournalDocument(Pages.Select(page => page.ToPage())).Serialize();

    /// <summary>
    /// Shows a stored journal. When the page names are unchanged the existing page objects
    /// take the new content in place, so the selected tab survives a reminder being written
    /// at the top of the first page.
    /// </summary>
    public void Load(string? stored)
    {
        var document = JournalDocument.Parse(stored);
        if (Pages.Count == document.Pages.Count &&
            Pages.Select(page => page.Name).SequenceEqual(document.Pages.Select(page => page.Name)))
        {
            for (var index = 0; index < Pages.Count; index++)
            {
                Pages[index].IsRenaming = false;
                if (!Pages[index].Paragraphs.SequenceEqual(document.Pages[index].Paragraphs))
                    Pages[index].ReplaceContent(document.Pages[index].Paragraphs);
            }
            SelectedPage ??= Pages[0];
            return;
        }

        var selectedIndex = SelectedPage is null ? 0 : Math.Max(0, Pages.IndexOf(SelectedPage));
        Pages.Clear();
        foreach (var page in document.Pages)
            Pages.Add(new JournalPageViewModel(page, OnPageEdited));
        SelectedPage = Pages[Math.Min(selectedIndex, Pages.Count - 1)];
        AddPageCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddPage() => Pages.Count < JournalDocument.MaxPages;

    /// <summary>Adds a page after the last one, selects it, and opens its name for editing.</summary>
    [RelayCommand(CanExecute = nameof(CanAddPage))]
    private void AddPage()
    {
        var name = NextPageName();
        var page = new JournalPageViewModel(JournalPage.Empty(name), OnPageEdited);
        Pages.Add(page);
        SelectedPage = page;
        page.EditingName = name;
        page.IsRenaming = true;
        AddPageCommand.NotifyCanExecuteChanged();
        DeletePageCommand.NotifyCanExecuteChanged();
        DocumentEdited?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void BeginRename(JournalPageViewModel? page)
    {
        if (page is null)
            return;
        foreach (var other in Pages.Where(other => other != page))
            other.IsRenaming = false;
        page.EditingName = page.Name;
        page.IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename(JournalPageViewModel? page)
    {
        if (page is null || !page.IsRenaming)
            return;
        page.IsRenaming = false;
        var name = JournalDocument.NormalizePageName(page.EditingName, page.Name);
        page.EditingName = name;
        if (name == page.Name)
            return;
        page.Name = name;
        DocumentEdited?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelRename(JournalPageViewModel? page)
    {
        if (page is null)
            return;
        page.IsRenaming = false;
        page.EditingName = page.Name;
    }

    // Only an empty page can be removed, so deleting a page can never delete
    // anything written on it. Clearing the text first is the deliberate act.
    private bool CanDeletePage(JournalPageViewModel? page) =>
        Pages.Count > 1 && (page ?? SelectedPage) is { IsEmpty: true };

    [RelayCommand(CanExecute = nameof(CanDeletePage))]
    private void DeletePage(JournalPageViewModel? page)
    {
        page ??= SelectedPage;
        if (page is null || !CanDeletePage(page))
            return;
        var index = Pages.IndexOf(page);
        Pages.Remove(page);
        SelectedPage = Pages[Math.Min(index, Pages.Count - 1)];
        AddPageCommand.NotifyCanExecuteChanged();
        DeletePageCommand.NotifyCanExecuteChanged();
        DocumentEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageEdited(JournalPageViewModel page)
    {
        DeletePageCommand.NotifyCanExecuteChanged();
        DocumentEdited?.Invoke(this, EventArgs.Empty);
    }

    private string NextPageName()
    {
        var number = Pages.Count + 1;
        while (Pages.Any(page => string.Equals(page.Name, $"Page {number}", StringComparison.OrdinalIgnoreCase)))
            number++;
        return $"Page {number}";
    }
}
