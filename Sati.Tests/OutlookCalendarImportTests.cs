using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Children;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Sati.Tests;

public sealed class OutlookCalendarImportTests
{
    [Fact]
    public void ReaderImportsTimedAllDayAndUnfoldedOutlookFields()
    {
        var ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:staff-meeting
            DTSTART:20260908T130000Z
            DTEND:20260908T140000Z
            SUMMARY:Staff\, planning
            LOCATION:Conference|
             room
            END:VEVENT
            BEGIN:VEVENT
            UID:holiday
            DTSTART;VALUE=DATE:20260909
            DTEND;VALUE=DATE:20260910
            SUMMARY:Office closed
            END:VEVENT
            END:VCALENDAR
            """.Replace("Conference|", "Conference ", StringComparison.Ordinal);
        var result = OutlookIcsReader.Read(ics, 2026, 2026);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal("Staff, planning", result.Events[0].Title);
        Assert.Equal("Conference room", result.Events[0].Location);
        Assert.False(result.Events[0].IsAllDay);
        Assert.True(result.Events[1].IsAllDay);
        Assert.Equal("All day", result.Events[1].TimeLabel);
    }

    [Fact]
    public void ReaderExpandsCommonWeeklySeriesAndHonorsExclusions()
    {
        var result = OutlookIcsReader.Read("""
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:weekly-supervision
            DTSTART:20260907T090000
            DTEND:20260907T100000
            RRULE:FREQ=WEEKLY;COUNT=4;BYDAY=MO
            EXDATE:20260921T090000
            SUMMARY:Supervision
            END:VEVENT
            END:VCALENDAR
            """, 2026, 2026);

        Assert.Equal(
            [new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Local),
             new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Local),
             new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Local)],
            result.Events.Select(entry => entry.Start).ToList());
    }

    [Fact]
    public async Task CacheIsSeparatedBySatiUserAndEnvironmentAndImportReplacesPriorCopy()
    {
        using var fixture = new ImportFixture();
        var production = new DataEnvironmentInfo(SatiDataEnvironment.Production, "SatiProduction");
        var service = new OutlookCalendarService(production, fixture.CachePath, bytes => bytes, bytes => bytes);
        await fixture.WriteCalendarAsync("first", "20260908T090000", "First event");

        var first = await service.ImportAsync(7, fixture.CalendarPath);

        Assert.Equal(1, first.ImportedCount);
        Assert.Single(await service.GetByYearAsync(7, 2026));
        Assert.Empty(await service.GetByYearAsync(8, 2026));

        await fixture.WriteCalendarAsync("second", "20261003T110000", "Replacement event");
        await service.ImportAsync(7, fixture.CalendarPath);

        var replaced = await service.GetByYearAsync(7, 2026);
        Assert.Single(replaced);
        Assert.Equal("Replacement event", replaced[0].Title);

        var demo = new OutlookCalendarService(
            new DataEnvironmentInfo(SatiDataEnvironment.Demo, "SatiDemo"),
            fixture.CachePath,
            bytes => bytes,
            bytes => bytes);
        Assert.Empty(await demo.GetByYearAsync(7, 2026));
    }

    [Fact]
    public void CalendarViewExposesTheImportAndOutlookEventSurfaces()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "CalendarView.xaml"));

        Assert.Contains("ImportOutlookCalendarCommand", xaml);
        Assert.Contains("SelectedDayOutlookEvents", xaml);
        Assert.Contains("HasOutlookEvents", xaml);
        Assert.Contains("Import an Outlook calendar file", xaml);
    }

    [Fact]
    public async Task CalendarViewModelLoadsImportedEventsAndRefreshesAfterImport()
    {
        var date = new DateTime(2026, 9, 8, 9, 0, 0);
        var outlook = new StubOutlookCalendarService();
        outlook.Events.Add(new ImportedOutlookEvent(
            "planning", "Planning meeting", date, date.AddHours(1), false, "Office"));
        var session = new SessionService();
        session.SetUser(User.Create(
            7, "calendar-user", "Calendar User", "hash", "salt",
            UserRole.CaseManager, null, 1));
        var viewModel = new CalendarViewModel(
            new EmptyExemptDateService(),
            new EmptyNoteService(),
            session,
            outlook,
            new StaticOutlookPicker("outlook.ics"))
        {
            CurrentYear = 2026,
            SelectedMonth = 9
        };

        await viewModel.InitializeAsync();
        var day = viewModel.Months.Single(month => month.Month == 9).Cells
            .OfType<CalendarDay>().Single(entry => entry.Date == date.Date);

        Assert.True(day.HasOutlookEvents);
        Assert.Equal("Planning meeting", Assert.Single(day.OutlookEvents).Title);
        Assert.Contains("1 Outlook event", day.AccessibleLabel);

        await viewModel.ImportOutlookCalendarCommand.ExecuteAsync(null);

        Assert.Equal(1, outlook.ImportCalls);
        Assert.Contains("Imported 1 Outlook events", viewModel.StatusMessage);
        Assert.False(viewModel.IsImportingOutlookCalendar);
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;

    private sealed class ImportFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sati-outlook-tests-{Guid.NewGuid():N}");
        public string CalendarPath => Path.Combine(_directory, "calendar.ics");
        public string CachePath => Path.Combine(_directory, "cache.dat");

        public ImportFixture() => Directory.CreateDirectory(_directory);

        public Task WriteCalendarAsync(string uid, string start, string title) =>
            File.WriteAllTextAsync(CalendarPath, $"""
                BEGIN:VCALENDAR
                VERSION:2.0
                BEGIN:VEVENT
                UID:{uid}
                DTSTART:{start}
                DTEND:{IncrementHour(start)}
                SUMMARY:{title}
                END:VEVENT
                END:VCALENDAR
                """, Encoding.UTF8);

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private static string IncrementHour(string value)
        {
            var parsed = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", null);
            return parsed.AddHours(1).ToString("yyyyMMdd'T'HHmmss");
        }
    }

    private sealed class StubOutlookCalendarService : IOutlookCalendarService
    {
        public List<ImportedOutlookEvent> Events { get; } = [];
        public int ImportCalls { get; private set; }

        public Task<IReadOnlyList<ImportedOutlookEvent>> GetByYearAsync(
            int userId, int year, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportedOutlookEvent>>(
                Events.Where(entry => entry.Start.Year == year).ToList());

        public Task<OutlookCalendarImportResult> ImportAsync(
            int userId, string filePath, CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            return Task.FromResult(new OutlookCalendarImportResult(
                Events.Count, 0, Events.Min(entry => entry.Start), Events.Max(entry => entry.Start)));
        }
    }

    private sealed record StaticOutlookPicker(string Path) : IOutlookCalendarFilePicker
    {
        public string? PickCalendarFile() => Path;
    }

    private sealed class EmptyExemptDateService : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) => Task.FromResult(new List<ExemptDate>());
        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) => throw new NotSupportedException();
        public Task RemoveAsync(int id) => throw new NotSupportedException();
    }

    private sealed class EmptyNoteService : INoteService
    {
        public Task<List<Note>> GetByYearAsync(int userId, int year) => Task.FromResult(new List<Note>());
        public Task<Note> AddNoteAsync(Note note) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }
}
