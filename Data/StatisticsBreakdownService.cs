using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using SatiLogica.Contracts;

namespace Sati.Data;

/// <summary>Transitional local Production read over minimum necessary service facts.</summary>
public sealed class StatisticsBreakdownService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    TimeProvider? timeProvider = null) : IStatisticsBreakdownService
{
    public async Task<StatisticsBreakdownReportDto> GetAsync(
        DateTime windowStart,
        DateTime windowEnd,
        StatisticsPeriod period,
        int? personId = null)
    {
        StatisticsBreakdownBuilder.ValidateWindow(windowStart, windowEnd, period);
        if (personId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId));

        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, actor.Id))
            throw new UnauthorizedAccessException("Current case-management permission is required.");

        var peopleQuery = context.People.AsNoTracking()
            .Where(person => person.UserId == actor.Id &&
                             person.AgencyId == actor.AgencyId &&
                             person.Status != PersonStatus.Ghost);
        if (personId is int selectedPersonId)
        {
            if (!await peopleQuery.AnyAsync(person => person.Id == selectedPersonId))
                throw new UnauthorizedAccessException("This report requires current access to the selected consumer.");
            peopleQuery = peopleQuery.Where(person => person.Id == selectedPersonId);
        }

        var people = await peopleQuery
            .Select(person => new
            {
                person.Id,
                person.FirstName,
                person.LastName
            })
            .ToListAsync();
        var clients = people.Select(person => new StatisticsClientFact(
            person.Id,
            FormatName(person.Id, person.FirstName, person.LastName))).ToList();
        var personIds = clients.Select(client => client.PersonId).ToList();
        var start = windowStart.Date;
        var endExclusive = windowEnd.Date.AddDays(1);

        var noteRows = await context.Notes.AsNoTracking()
            .Where(note => personIds.Contains(note.PersonId) &&
                           note.AgencyId == actor.AgencyId &&
                           note.EventDate.HasValue &&
                           note.EventDate.Value >= start &&
                           note.EventDate.Value < endExclusive &&
                           (note.Status == NoteStatus.Pending ||
                            note.Status == NoteStatus.Logged ||
                            note.Status == NoteStatus.Approved ||
                            note.Status == NoteStatus.Abandoned ||
                            note.Status == NoteStatus.ComplianceBlocked))
            .Select(note => new
            {
                note.PersonId,
                EventDate = note.EventDate!.Value,
                note.Status,
                note.Minutes,
                note.Activities,
                note.NoteType,
                note.IsUnbilled
            })
            .ToListAsync();
        var notes = noteRows.Select(note => new StatisticsNoteFact(
            note.PersonId, note.EventDate, note.Status?.ToString(),
            note.Minutes, (int?)note.Activities, note.NoteType?.ToString(),
            note.IsUnbilled)).ToList();

        var claimRows = await (from line in context.ClaimLines.AsNoTracking()
                               join billingPeriod in context.BillingPeriods.AsNoTracking()
                                   on line.BillingPeriodId equals billingPeriod.Id
                               join note in context.Notes.AsNoTracking()
                                   on line.NoteId equals note.Id
                               join person in peopleQuery on note.PersonId equals person.Id
                               where personIds.Contains(person.Id) &&
                                     note.AgencyId == actor.AgencyId &&
                                     person.AgencyId == actor.AgencyId &&
                                     billingPeriod.UserId == actor.Id &&
                                     (billingPeriod.SubmittedAt != null ||
                                      billingPeriod.Status != BillingStatus.Draft) &&
                                     line.DateOfService >= start &&
                                     line.DateOfService < endExclusive
                               select new
                               {
                                   note.PersonId,
                                   line.DateOfService,
                                   line.Units,
                                   note.Activities,
                                   note.NoteType,
                                   WasSubmitted = context.BillingSubmissionEvents.AsNoTracking()
                                       .Any(submission =>
                                           submission.AgencyId == actor.AgencyId &&
                                           submission.BillingPeriodId == billingPeriod.Id &&
                                           submission.Stage == BillingSubmissionStage.Transmitted &&
                                           !submission.IsSynthetic &&
                                           submission.EdiGenerationId.HasValue &&
                                           context.EdiGenerations.AsNoTracking().Any(generation =>
                                               generation.Id == submission.EdiGenerationId.Value &&
                                               generation.AgencyId == actor.AgencyId &&
                                               generation.BillingPeriodId == billingPeriod.Id &&
                                               !generation.IsCorrection &&
                                               !generation.IsTest))
                               }).ToListAsync();
        var claims = claimRows.Select(row => new StatisticsClaimFact(
            row.PersonId, row.DateOfService, row.Units,
            (int?)row.Activities, row.NoteType?.ToString(),
            row.WasSubmitted, WasLocked: true)).ToList();

        var configuredWindow = await context.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == actor.AgencyId)
            .Select(settings => (int?)settings.AbandonedAfterDays)
            .SingleOrDefaultAsync();
        var documentationWindowDays = ProductivityForecast.NormalizeDocumentationWindowDays(configuredWindow);
        var today = TenantClock.MaineDate((timeProvider ?? TimeProvider.System).GetUtcNow());
        return StatisticsBreakdownBuilder.Build(
            windowStart, windowEnd, period, today, documentationWindowDays,
            clients, notes, claims);
    }

    private static string FormatName(int personId, string? firstName, string? lastName)
    {
        var name = $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();
        return name.Length == 0 ? $"Consumer #{personId}" : name;
    }
}
