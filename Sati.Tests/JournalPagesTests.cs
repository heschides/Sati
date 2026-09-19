using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Journal pages, formatting, and checked text. The stored shape is
/// <see cref="JournalDocument"/>; every journal written before it is plain text and has to
/// keep reading, and keep taking reminders, exactly as before.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class JournalPagesTests
{
    private static readonly DateTime Stamp = new(2026, 9, 19, 9, 5, 0);

    // -------------------------------------------------------------------------
    // The stored shape
    // -------------------------------------------------------------------------

    [Fact]
    public void AJournalWrittenBeforePagesReadsAsOnePageOfItsLines()
    {
        const string legacy = "Guardian prefers afternoons.\r\nUses a communication device.";

        var document = JournalDocument.Parse(legacy);

        Assert.False(JournalDocument.IsDocument(legacy));
        var page = Assert.Single(document.Pages);
        Assert.Equal(JournalDocument.DefaultPageName, page.Name);
        Assert.Equal(legacy, page.ToPlainText());
    }

    [Theory]
    [InlineData("{ this is a note the case manager wrote with a brace }")]
    [InlineData("{\"sati-journal\":99,\"pages\":[]}")]
    [InlineData("{\"pages\":[{\"name\":\"x\",\"p\":[]}]}")]
    public void PlainTextThatMerelyLooksLikeADocumentStaysPlainText(string stored)
    {
        Assert.False(JournalDocument.IsDocument(stored));
        Assert.Equal(stored, JournalDocument.Parse(stored).Pages[0].ToPlainText());
    }

    [Fact]
    public void PagesNamesMarksAndCheckboxesSurviveARoundTrip()
    {
        var document = new JournalDocument(
        [
            new JournalPage("Intake", [
                new JournalParagraph([
                    JournalInline.Checkbox(isChecked: true),
                    JournalInline.Run("Call "),
                    JournalInline.Run("guardian", bold: true, underline: true),
                    JournalInline.Run(" today", italic: true)
                ]),
                JournalParagraph.Blank,
                JournalParagraph.FromText("Second line")
            ]),
            new JournalPage("Housing", [JournalParagraph.FromText("Lease renews in May.")])
        ]);

        var stored = document.Serialize();
        var read = JournalDocument.Parse(stored);

        Assert.True(JournalDocument.IsDocument(stored));
        Assert.Equal(["Intake", "Housing"], read.Pages.Select(page => page.Name));
        Assert.Equal(document.Pages[0], read.Pages[0]);
        Assert.Equal(document.Pages[1], read.Pages[1]);
        Assert.Equal("[x] Call guardian today", read.Pages[0].Paragraphs[0].ToPlainText());
    }

    [Fact]
    public void AReminderLandsAtTheTopOfTheFirstPageAndLeavesTheOtherPagesAlone()
    {
        var paged = new JournalDocument(
        [
            new JournalPage("Journal", [JournalParagraph.FromText("Handwritten line.")]),
            new JournalPage("Housing", [JournalParagraph.FromText("Lease renews in May.")])
        ]).Serialize();

        var result = JournalEntry.PrependReminder(paged, Stamp, "Send the release form.");

        var document = JournalDocument.Parse(result);
        var first = document.Pages[0].Paragraphs.Select(paragraph => paragraph.ToPlainText()).ToList();
        Assert.Equal($"September 19, 2026 9:05 AM — {JournalEntry.ReminderLabel}", first[0]);
        Assert.Equal("Send the release form.", first[1]);
        Assert.Equal(string.Empty, first[2]);
        Assert.Equal("Handwritten line.", first[3]);
        Assert.Equal("Lease renews in May.", document.Pages[1].ToPlainText());
    }

    [Fact]
    public void AReminderDoesNotTurnAPlainJournalIntoADocument()
    {
        var result = JournalEntry.PrependReminder("Handwritten line.", Stamp, "Send the release form.");

        Assert.False(JournalDocument.IsDocument(result));
        Assert.EndsWith("Handwritten line.", result);
    }

    [Theory]
    [InlineData("  Housing   notes ", "Housing notes")]
    [InlineData("   ", "Fallback")]
    [InlineData(null, "Fallback")]
    public void PageNamesAreTrimmedAndNeverBlank(string? name, string expected) =>
        Assert.Equal(expected, JournalDocument.NormalizePageName(name, "Fallback"));

    [Fact]
    public void APageNameIsBoundedByTheContract() =>
        Assert.Equal(JournalDocument.MaxPageNameLength,
            JournalDocument.NormalizePageName(new string('x', 200)).Length);

    // -------------------------------------------------------------------------
    // Pages on screen
    // -------------------------------------------------------------------------

    [Fact]
    public void AddingAPageSelectsItOpensItsNameAndReportsTheEdit()
    {
        var pages = new JournalPagesViewModel();
        pages.Load("Existing text.");
        var edits = 0;
        pages.DocumentEdited += (_, _) => edits++;

        pages.AddPageCommand.Execute(null);

        Assert.Equal(2, pages.Pages.Count);
        Assert.Same(pages.Pages[1], pages.SelectedPage);
        Assert.True(pages.SelectedPage!.IsRenaming);
        Assert.Equal(1, edits);
        Assert.Equal(["Journal", "Page 2"], JournalDocument.Parse(pages.Serialize()).Pages.Select(p => p.Name));
    }

    [Fact]
    public void RenamingCommitsANormalizedNameAndEscapeKeepsTheOldOne()
    {
        var pages = new JournalPagesViewModel();
        var page = pages.Pages[0];

        pages.BeginRenameCommand.Execute(page);
        page.EditingName = "  Medical  ";
        pages.CommitRenameCommand.Execute(page);
        Assert.Equal("Medical", page.Name);

        pages.BeginRenameCommand.Execute(page);
        page.EditingName = "Something else";
        pages.CancelRenameCommand.Execute(page);
        Assert.Equal("Medical", page.Name);
        Assert.False(page.IsRenaming);
    }

    [Fact]
    public void OnlyAnEmptyPageThatIsNotTheLastCanBeDeleted()
    {
        var pages = new JournalPagesViewModel();
        pages.Load("Written on.");
        Assert.False(pages.DeletePageCommand.CanExecute(pages.Pages[0]));

        pages.AddPageCommand.Execute(null);
        var blank = pages.Pages[1];
        Assert.False(pages.DeletePageCommand.CanExecute(pages.Pages[0]));
        Assert.True(pages.DeletePageCommand.CanExecute(blank));

        pages.DeletePageCommand.Execute(blank);
        Assert.Single(pages.Pages);
        Assert.Equal("Written on.", pages.Pages[0].ToPage().ToPlainText());
    }

    [Fact]
    public void AReminderWrittenElsewhereUpdatesThePageInPlaceAndKeepsTheSelectedTab()
    {
        var stored = new JournalDocument(
        [
            new JournalPage("Journal", [JournalParagraph.FromText("One.")]),
            new JournalPage("Housing", [JournalParagraph.FromText("Two.")])
        ]).Serialize();
        var pages = new JournalPagesViewModel();
        pages.Load(stored);
        var housing = pages.Pages[1];
        pages.SelectedPage = housing;
        var first = pages.Pages[0];
        var redraws = 0;
        first.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JournalPageViewModel.Paragraphs)) redraws++;
        };

        pages.Load(JournalEntry.PrependReminder(stored, Stamp, "Call back."));

        Assert.Same(housing, pages.SelectedPage);
        Assert.Same(first, pages.Pages[0]);
        Assert.Equal(1, redraws);
        Assert.StartsWith("September 19, 2026", first.ToPage().ToPlainText());
    }

    [Fact]
    public void AnEditThatChangesNothingIsNotReported()
    {
        var pages = new JournalPagesViewModel();
        pages.Load("Same.");
        var edits = 0;
        pages.DocumentEdited += (_, _) => edits++;

        pages.Pages[0].ApplyEditorContent([JournalParagraph.FromText("Same.")]);

        Assert.Equal(0, edits);
    }

    // -------------------------------------------------------------------------
    // The client page
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithNoClientsJournalLoadedAnEditIsPutBackRatherThanKeptUnsaved()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var page = fixture.ClientsPage();

        page.JournalPages.Pages[0].ApplyEditorContent([JournalParagraph.FromText("Nowhere to go.")]);

        Assert.True(string.IsNullOrEmpty(page.Journal));
        Assert.True(page.JournalPages.Pages[0].IsEmpty);
    }

    [Fact]
    public async Task ACheckedPassageBecomesAScheduledReminderForItsClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var page = fixture.ClientsPage();
        var announced = 0;
        page.JournalNoteCreated += (_, _) => announced++;
        var date = DateTime.Today.AddDays(3);

        await page.CreateNoteFromJournalAsync(
            new JournalCheckNoteRequest(fixture.PersonOneId, "Renew the bus pass", date, NoteType.Reminder));

        await using var db = fixture.Factory.CreateDbContext();
        var note = await db.Notes.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.PersonOneId, note.PersonId);
        Assert.Equal("Renew the bus pass", note.Narrative);
        Assert.Equal(NoteType.Reminder, note.NoteType);
        Assert.Equal(NoteStatus.Scheduled, note.Status);
        Assert.Equal(date, note.EventDate);
        Assert.Null(note.Minutes);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task ACheckedPassageCanBecomeScheduledWorkOfTheChosenType()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var page = fixture.ClientsPage();

        await page.CreateNoteFromJournalAsync(new JournalCheckNoteRequest(
            fixture.PersonOneId, "Home visit about the lease", DateTime.Today.AddDays(1), NoteType.Visit));

        await using var db = fixture.Factory.CreateDbContext();
        var note = await db.Notes.AsNoTracking().SingleAsync();
        Assert.Equal(NoteType.Visit, note.NoteType);
        Assert.Equal(NoteStatus.Scheduled, note.Status);
    }

    [Fact]
    public async Task ACheckedPassageCannotBecomeANoteForAnotherAgencysClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var outsider = fixture.ClientsPage(fixture.CaseManagerTwo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => outsider.CreateNoteFromJournalAsync(
            new JournalCheckNoteRequest(fixture.PersonOneId, "Not yours", DateTime.Today, NoteType.Reminder)));

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Empty(await db.Notes.AsNoTracking().ToListAsync());
    }

    // -------------------------------------------------------------------------
    // The question after checking
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThePromptKeepsTheClientItWasOpenedForAndTheKindChosen()
    {
        JournalCheckNoteRequest? sent = null;
        var prompt = new JournalCheckPromptViewModel(request =>
        {
            sent = request;
            return Task.CompletedTask;
        }, () => new DateTime(2026, 9, 19));

        prompt.Open(12, "  Pick up the lease  ");
        Assert.True(prompt.IsOpen);
        Assert.Equal(new DateTime(2026, 9, 19), prompt.ReminderDate);
        Assert.True(prompt.IsReminder);

        prompt.IsScheduled = true;
        prompt.ScheduledNoteType = NoteType.Phone;
        prompt.ReminderDate = new DateTime(2026, 9, 22);
        await prompt.AssignCommand.ExecuteAsync(null);

        Assert.Equal(new JournalCheckNoteRequest(12, "Pick up the lease", new DateTime(2026, 9, 22), NoteType.Phone), sent);
        Assert.False(prompt.IsOpen);
    }

    [Fact]
    public async Task APastDateIsRefusedBecauseTheNoteWouldLapseAtOnce()
    {
        var calls = 0;
        var prompt = new JournalCheckPromptViewModel(_ =>
        {
            calls++;
            return Task.CompletedTask;
        }, () => new DateTime(2026, 9, 19));
        prompt.Open(12, "Too late");
        prompt.ReminderDate = new DateTime(2026, 9, 18);

        await prompt.AssignCommand.ExecuteAsync(null);

        Assert.Equal(0, calls);
        Assert.True(prompt.IsOpen);
        Assert.True(prompt.HasErrorMessage);
    }

    [Fact]
    public void BlankTextOpensNoQuestion()
    {
        var prompt = new JournalCheckPromptViewModel(_ => Task.CompletedTask);
        prompt.Open(12, "   \r\n");
        Assert.False(prompt.IsOpen);
    }

    [Fact]
    public async Task AFailedSaveLeavesTheQuestionOpenWithAMessage()
    {
        var prompt = new JournalCheckPromptViewModel(
            _ => Task.FromException(new HttpRequestException("offline")));
        prompt.Open(12, "Call back");

        await prompt.AssignCommand.ExecuteAsync(null);

        Assert.True(prompt.IsOpen);
        Assert.True(prompt.HasErrorMessage);
        Assert.DoesNotContain("offline", prompt.ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // The editor's rich text
    // -------------------------------------------------------------------------

    [Fact]
    public void TheEditorReadsBackWhatItWasGiven()
    {
        IReadOnlyList<JournalParagraph> paragraphs =
        [
            new([JournalInline.Checkbox(false), JournalInline.Run("Call "), JournalInline.Run("today", bold: true)]),
            JournalParagraph.Blank,
            new([JournalInline.Run("under", underline: true), JournalInline.Run(" and ", italic: true)])
        ];

        WpfUiHarness.Run(() =>
        {
            var document = JournalFlowDocument.Build(paragraphs, isChecked => new CheckBox { IsChecked = isChecked });
            Assert.Equal(paragraphs, JournalFlowDocument.Read(document));
        });
    }

    [Fact]
    public void FormattingAppliedBySpansAndSplitRunsIsReadAsTheContractsMarks()
    {
        WpfUiHarness.Run(() =>
        {
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("plain "));
            paragraph.Inlines.Add(new Underline(new Bold(new Run("both"))));
            paragraph.Inlines.Add(new Run(" more"));
            paragraph.Inlines.Add(new Run(" text"));
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run("next"));
            var document = new FlowDocument(paragraph);

            var read = JournalFlowDocument.Read(document);

            Assert.Equal(2, read.Count);
            Assert.Equal(
                [
                    JournalInline.Run("plain "),
                    JournalInline.Run("both", bold: true, underline: true),
                    JournalInline.Run(" more text")
                ],
                read[0].Inlines);
            Assert.Equal("next", read[1].ToPlainText());
        });
    }

    [Fact]
    public async Task TheEditorShowsTheSelectedPageAndCheckingTextAddsACheckbox()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        WpfUiHarness.Run(() =>
        {
            var page = fixture.ClientsPage();
            page.JournalPages.Load(new JournalDocument(
            [
                new JournalPage("Journal", [JournalParagraph.FromText("First page")]),
                new JournalPage("Housing", [JournalParagraph.FromText("Lease renews")])
            ]).Serialize());
            var editor = new JournalEditor { DataContext = page };
            WpfUiHarness.Realize(editor, 420, 360);

            var box = WpfUiHarness.FindByAutomationName<RichTextBox>(editor, "Journal");
            Assert.Equal("First page", TextOf(box));

            page.JournalPages.SelectedPage = page.JournalPages.Pages[1];
            Assert.Equal("Lease renews", TextOf(box));

            // The tabs and the formatting commands are named for a screen reader.
            WpfUiHarness.FindByAutomationName<Button>(editor, "Add journal page");
            WpfUiHarness.FindByAutomationName<Button>(editor, "Bold");
            WpfUiHarness.FindByAutomationName<Button>(editor, "Add checkbox");
        });
    }

    private static string TextOf(RichTextBox box) =>
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.TrimEnd('\r', '\n');
}
