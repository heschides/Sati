using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class CheckRequestAutomationService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session) : ICheckRequestAutomationService
{
    private User Actor => session.CurrentUser
        ?? throw new UnauthorizedAccessException("Sign in to use weekly check-request defaults.");

    public async Task<CheckRequestTemplateDto?> GetTemplateAsync(int personId)
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId))
            throw new UnauthorizedAccessException(
                "Only the assigned case manager can view this weekly check-request default.");
        var template = await db.CheckRequestTemplates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PersonId == personId);
        return template is null ? null : ToDtoValue(template);
    }

    public async Task<CheckRequestTemplateDto> SaveTemplateAsync(
        int personId,
        SaveCheckRequestTemplateRequest request)
    {
        var actor = Actor;
        var errors = CheckRequestTemplateRules.Validate(
            request.GenerateOn, request.NeededByDaysAfterRequest, request.PayableTo,
            request.MailingAddress, request.Amount, request.Reason);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId))
            throw new UnauthorizedAccessException(
                "Only the assigned case manager can change this weekly check-request default.");
        var person = await db.People.AsNoTracking().SingleAsync(item => item.Id == personId);
        if (!person.CaseManagerIsRepPayee)
            throw new InvalidOperationException(
                "Representative Payee must be enabled for this consumer before saving a weekly default.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var template = await db.CheckRequestTemplates.SingleOrDefaultAsync(item => item.PersonId == personId);
        var now = DateTime.UtcNow;
        if (template is null)
        {
            if (request.ExpectedRevision != 0)
                throw new InvalidOperationException("The weekly default changed. Reload it and try again.");
            template = new CheckRequestTemplate
            {
                PersonId = personId,
                Revision = 1,
                EffectiveFrom = DateTime.Today,
                CreatedAtUtc = now
            };
            db.CheckRequestTemplates.Add(template);
        }
        else
        {
            if (template.Revision != request.ExpectedRevision)
                throw new InvalidOperationException("The weekly default changed. Reload it and try again.");
            template.Revision++;
        }

        Apply(request, template);
        template.UpdatedAtUtc = now;
        LocalAuditTrail.Record(db, actor, LocalAuditActions.CheckRequestTemplateUpdated,
            "Person", personId);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("The weekly default changed. Reload it and try again.", exception);
        }
        return ToDtoValue(template);
    }

    public async Task<WeeklyCheckRequestDraftResultDto> EnsureWeeklyDraftsAsync()
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasCaseManagerPermissions)
            return new WeeklyCheckRequestDraftResultDto(0, []);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var templates = await (from template in db.CheckRequestTemplates
            join person in db.People on template.PersonId equals person.Id
            where template.IsEnabled && person.UserId == actor.Id &&
                  person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
            select new { Template = template, Person = person }).ToListAsync();
        var templateIds = templates.Select(item => item.Template.Id).ToList();
        var pendingTemplateIds = templateIds.Count == 0
            ? new List<int>()
            : await db.CheckRequests.AsNoTracking()
                .Where(request => request.TemplateId != null &&
                    templateIds.Contains(request.TemplateId.Value) &&
                    !db.CheckRequestWorkflowEvents.Any(workflow =>
                        workflow.CheckRequestId == request.Id &&
                        workflow.Action == CheckRequestWorkflowAction.Submitted))
                .Select(request => request.TemplateId!.Value)
                .Distinct()
                .ToListAsync();
        var pendingSet = pendingTemplateIds.ToHashSet();
        var owner = await db.Users.AsNoTracking().Include(user => user.Agency)
            .Include(user => user.Supervisor).SingleAsync(user => user.Id == actor.Id);
        var today = DateTime.Today;
        var created = 0;

        foreach (var item in templates)
        {
            if (pendingSet.Contains(item.Template.Id)) continue;
            var scheduledFor = WeeklyCheckRequestSchedule.MostRecentOccurrence(
                item.Template.GenerateOn, item.Template.EffectiveFrom, today);
            if (scheduledFor is null) continue;
            if (await db.CheckRequests.AsNoTracking().AnyAsync(request =>
                    request.TemplateId == item.Template.Id &&
                    request.ScheduledForDate == scheduledFor.Value))
                continue;

            var draft = CheckRequest.CreateForClient(item.Person, owner, DateTime.UtcNow);
            draft.MarkAutomaticallyGenerated(item.Template.Id, scheduledFor.Value);
            draft.RequestDate = scheduledFor.Value;
            draft.PayableTo = item.Template.PayableTo;
            draft.MailingAddress = item.Template.MailingAddress;
            draft.Amount = item.Template.Amount;
            draft.NeededByDate = scheduledFor.Value.AddDays(item.Template.NeededByDaysAfterRequest);
            draft.Reason = item.Template.Reason;
            db.CheckRequests.Add(draft);
            LocalAuditTrail.Record(db, actor, LocalAuditActions.CheckRequestDraftGenerated,
                "Person", item.Person.Id, JsonSerializer.Serialize(new
                {
                    templateId = item.Template.Id,
                    scheduledForDate = scheduledFor.Value
                }));
            created++;
        }

        if (created > 0) await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var pending = await LoadPendingAsync(db, actor);
        return new WeeklyCheckRequestDraftResultDto(created, pending);
    }

    public async Task<IReadOnlyList<GeneratedCheckRequestDraftDto>> GetPendingDraftsAsync()
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasCaseManagerPermissions) return [];
        return await LoadPendingAsync(db, actor);
    }

    public async Task<IReadOnlyList<TimeOffCheckRequestCollisionDto>> GetTimeOffCollisionsAsync(
        DateTime timeOffDate)
    {
        var actor = Actor;
        var date = timeOffDate.Date;
        if (!actor.HasCaseManagerPermissions || date < DateTime.Today) return [];
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!await db.ExemptDates.AsNoTracking().AnyAsync(item =>
                item.UserId == actor.Id && item.Date == date))
            return [];

        var templates = await (from template in db.CheckRequestTemplates.AsNoTracking()
            join person in db.People.AsNoTracking() on template.PersonId equals person.Id
            where template.IsEnabled && template.GenerateOn == date.DayOfWeek &&
                  template.EffectiveFrom <= date && person.UserId == actor.Id &&
                  person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
            select new { Template = template, Person = person }).ToListAsync();
        var templateIds = templates.Select(item => item.Template.Id).ToList();
        var requests = templateIds.Count == 0
            ? new List<CheckRequest>()
            : await db.CheckRequests.AsNoTracking()
                .Where(request => request.TemplateId != null &&
                    templateIds.Contains(request.TemplateId.Value))
                .OrderBy(request => request.ScheduledForDate).ThenBy(request => request.Id)
                .ToListAsync();
        var requestIds = requests.Select(request => request.Id).ToList();
        var submittedIds = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(workflow => requestIds.Contains(workflow.CheckRequestId) &&
                workflow.Action == CheckRequestWorkflowAction.Submitted)
            .Select(workflow => workflow.CheckRequestId)
            .ToListAsync();
        var submittedSet = submittedIds.ToHashSet();

        return templates.Select(item =>
            {
                var exact = requests.FirstOrDefault(request =>
                    request.TemplateId == item.Template.Id && request.ScheduledForDate == date);
                if (exact is not null && submittedSet.Contains(exact.Id)) return null;
                var pending = requests.FirstOrDefault(request =>
                    request.TemplateId == item.Template.Id && !submittedSet.Contains(request.Id));
                return new TimeOffCheckRequestCollisionDto(
                    item.Template.Id, item.Person.Id, item.Person.FullName, date,
                    item.Template.PayableTo, item.Template.Amount, pending?.Id);
            })
            .Where(item => item is not null)
            .Cast<TimeOffCheckRequestCollisionDto>()
            .ToList();
    }

    public async Task<WeeklyCheckRequestDraftResultDto> EnsureTimeOffDraftsAsync(
        DateTime timeOffDate)
    {
        var actor = Actor;
        var date = timeOffDate.Date;
        if (!actor.HasCaseManagerPermissions || date < DateTime.Today)
            return new WeeklyCheckRequestDraftResultDto(0, []);
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!await db.ExemptDates.AsNoTracking().AnyAsync(item =>
                item.UserId == actor.Id && item.Date == date))
            return new WeeklyCheckRequestDraftResultDto(0, []);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var templates = await (from template in db.CheckRequestTemplates
            join person in db.People on template.PersonId equals person.Id
            where template.IsEnabled && template.GenerateOn == date.DayOfWeek &&
                  template.EffectiveFrom <= date && person.UserId == actor.Id &&
                  person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
            select new { Template = template, Person = person }).ToListAsync();
        var templateIds = templates.Select(item => item.Template.Id).ToList();
        var pendingTemplateIds = templateIds.Count == 0
            ? new List<int>()
            : await db.CheckRequests.AsNoTracking()
                .Where(request => request.TemplateId != null &&
                    templateIds.Contains(request.TemplateId.Value) &&
                    !db.CheckRequestWorkflowEvents.Any(workflow =>
                        workflow.CheckRequestId == request.Id &&
                        workflow.Action == CheckRequestWorkflowAction.Submitted))
                .Select(request => request.TemplateId!.Value).Distinct().ToListAsync();
        var pendingSet = pendingTemplateIds.ToHashSet();
        var owner = await db.Users.AsNoTracking().Include(user => user.Agency)
            .Include(user => user.Supervisor).SingleAsync(user => user.Id == actor.Id);
        var created = 0;

        foreach (var item in templates)
        {
            if (pendingSet.Contains(item.Template.Id)) continue;
            if (await db.CheckRequests.AsNoTracking().AnyAsync(request =>
                    request.TemplateId == item.Template.Id && request.ScheduledForDate == date))
                continue;

            var draft = CheckRequest.CreateForClient(item.Person, owner, DateTime.UtcNow);
            draft.MarkAutomaticallyGenerated(item.Template.Id, date);
            draft.RequestDate = DateTime.Today;
            draft.PayableTo = item.Template.PayableTo;
            draft.MailingAddress = item.Template.MailingAddress;
            draft.Amount = item.Template.Amount;
            draft.NeededByDate = date.AddDays(item.Template.NeededByDaysAfterRequest);
            draft.Reason = item.Template.Reason;
            db.CheckRequests.Add(draft);
            LocalAuditTrail.Record(db, actor, LocalAuditActions.CheckRequestDraftGenerated,
                "Person", item.Person.Id, JsonSerializer.Serialize(new
                {
                    templateId = item.Template.Id,
                    scheduledForDate = date,
                    trigger = "time-off"
                }));
            created++;
        }

        try
        {
            if (created > 0) await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException(
                "Check-request drafts changed while Sati was preparing for time off. Reload and try again.",
                exception);
        }

        var pending = await LoadPendingAsync(db, actor);
        return new WeeklyCheckRequestDraftResultDto(
            created, pending.Where(item => templateIds.Contains(item.TemplateId)).ToList());
    }

    private static async Task<IReadOnlyList<GeneratedCheckRequestDraftDto>> LoadPendingAsync(
        SatiContext db,
        User actor)
    {
        var requests = await (from request in db.CheckRequests.AsNoTracking()
            join person in db.People.AsNoTracking() on request.PersonId equals person.Id
            where request.TemplateId != null && request.ScheduledForDate != null &&
                  person.UserId == actor.Id && person.AgencyId == actor.AgencyId &&
                  !db.CheckRequestWorkflowEvents.Any(workflow =>
                      workflow.CheckRequestId == request.Id &&
                      workflow.Action == CheckRequestWorkflowAction.Submitted)
            orderby request.ScheduledForDate, request.Id
            select request).ToListAsync();
        var ids = requests.Select(request => request.Id).ToList();
        var actions = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(workflow => ids.Contains(workflow.CheckRequestId))
            .Select(workflow => new { workflow.CheckRequestId, workflow.Action })
            .ToListAsync();
        return requests.Select(request => new GeneratedCheckRequestDraftDto(
            request.Id,
            request.TemplateId!.Value,
            request.PersonId,
            request.ConsumerName,
            request.ScheduledForDate!.Value,
            request.NeededByDate,
            request.PayableTo,
            request.Amount,
            CheckRequestWorkflowRules.Resolve(request.IsPublished,
                actions.Where(action => action.CheckRequestId == request.Id)
                    .Select(action => action.Action))))
            .ToList();
    }

    private static CheckRequestTemplateDto ToDtoValue(CheckRequestTemplate template) => new(
        template.Id, template.PersonId, template.Revision, template.IsEnabled,
        template.GenerateOn, template.NeededByDaysAfterRequest, template.PayableTo,
        template.MailingAddress, template.Amount, template.Reason, template.EffectiveFrom,
        template.CreatedAtUtc, template.UpdatedAtUtc);

    private static void Apply(SaveCheckRequestTemplateRequest source, CheckRequestTemplate target)
    {
        target.IsEnabled = source.IsEnabled;
        target.GenerateOn = source.GenerateOn;
        target.NeededByDaysAfterRequest = source.NeededByDaysAfterRequest;
        target.PayableTo = source.PayableTo!.Trim();
        target.MailingAddress = source.MailingAddress!.Trim();
        target.Amount = source.Amount;
        target.Reason = source.Reason!.Trim();
    }
}
