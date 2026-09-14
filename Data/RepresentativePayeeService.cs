using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class RepresentativePayeeService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService session) : IRepresentativePayeeService
{
    private User Actor => session.CurrentUser ??
        throw new UnauthorizedAccessException("Sign in to use the representative-payee workflow.");

    public async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetSupervisorQueueAsync()
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasSupervisorPermissions)
            throw new UnauthorizedAccessException("Supervision permission is required to review check requests.");

        var agencyWide = UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions);
        var requests = await (from request in db.CheckRequests.AsNoTracking()
            join person in db.People.AsNoTracking() on request.PersonId equals person.Id
            join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
            where person.AgencyId == actor.AgencyId &&
                  (agencyWide || owner.SupervisorId == actor.Id)
            select request).ToListAsync();
        return await BuildQueueAsync(db, requests, status => status == CheckRequestWorkflowStatus.Submitted);
    }

    public async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetFinanceQueueAsync()
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await EnsureFinanceAsync(db, actor);
        var requests = await (from request in db.CheckRequests.AsNoTracking()
            join person in db.People.AsNoTracking() on request.PersonId equals person.Id
            where person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee
            select request).ToListAsync();
        return await BuildQueueAsync(db, requests, status => status is
            CheckRequestWorkflowStatus.Approved or CheckRequestWorkflowStatus.Released or
            CheckRequestWorkflowStatus.ReceiptAcknowledged);
    }

    public async Task<IReadOnlyList<RepresentativePayeeConsumerDto>> GetConsumersAsync()
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await EnsureFinanceAsync(db, actor);
        return await db.People.AsNoTracking()
            .Where(person => person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee)
            .OrderBy(person => person.LastName).ThenBy(person => person.FirstName)
            .Select(person => new RepresentativePayeeConsumerDto(
                person.Id, ((person.FirstName ?? "") + " " + (person.LastName ?? "")).Trim(),
                person.RepPayeeMonthlyIncome))
            .ToListAsync();
    }

    public async Task<RepresentativePayeeWorkspaceDto> GetWorkspaceAsync(int personId)
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await EnsureFinanceAsync(db, actor);
        var consumer = await db.People.AsNoTracking()
            .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId &&
                             person.CaseManagerIsRepPayee)
            .Select(person => new RepresentativePayeeConsumerDto(
                person.Id, ((person.FirstName ?? "") + " " + (person.LastName ?? "")).Trim(),
                person.RepPayeeMonthlyIncome))
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("The representative-payee consumer was not found.");
        var entries = await db.RepresentativePayeeLedgerEntries.AsNoTracking()
            .Where(entry => entry.PersonId == personId)
            .OrderByDescending(entry => entry.EntryDate).ThenByDescending(entry => entry.Id)
            .Select(entry => new RepresentativePayeeLedgerEntryDto(
                entry.Id, entry.PersonId, entry.CheckRequestId, entry.EntryDate, entry.Kind,
                entry.Amount, entry.Description, entry.RecordedAtUtc, entry.RecordedByUserId,
                entry.RecordedByName)).ToListAsync();
        return new RepresentativePayeeWorkspaceDto(consumer, entries, entries.Sum(entry => entry.Amount));
    }

    public async Task<CheckRequestWorkflowQueueItemDto> ApplyActionAsync(
        int checkRequestId,
        CheckRequestWorkflowAction action,
        string? note = null)
    {
        var actor = Actor;
        var errors = CheckRequestWorkflowRules.ValidateNote(action, note);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        await using var db = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await db.CheckRequests.SingleOrDefaultAsync(x => x.Id == checkRequestId)
            ?? throw new KeyNotFoundException("The check request was not found.");
        var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == request.PersonId);
        var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == person.UserId);
        await EnsureActionAuthorizedAsync(db, actor, person, owner, action);

        var existing = await db.CheckRequestWorkflowEvents
            .Where(x => x.CheckRequestId == request.Id).OrderBy(x => x.Id).ToListAsync();
        var current = CheckRequestWorkflowRules.Resolve(request.IsPublished, existing.Select(x => x.Action));
        if (!CheckRequestWorkflowRules.CanApply(current, action))
            throw new InvalidOperationException(
                $"This action cannot be applied while the check request is {CheckRequestWorkflowRules.Describe(current).ToLowerInvariant()}.");

        var now = DateTime.UtcNow;
        var workflowEvent = new CheckRequestWorkflowEvent
        {
            CheckRequestId = request.Id,
            Checkpoint = CheckRequestWorkflowRules.Checkpoint(action),
            Action = action,
            OccurredAtUtc = now,
            ActorUserId = actor.Id,
            ActorName = actor.DisplayName,
            Note = Normalize(note)
        };
        db.CheckRequestWorkflowEvents.Add(workflowEvent);
        if (action == CheckRequestWorkflowAction.Released)
        {
            db.RepresentativePayeeLedgerEntries.Add(new RepresentativePayeeLedgerEntry
            {
                PersonId = person.Id,
                CheckRequestId = request.Id,
                EntryDate = now.Date,
                Kind = RepresentativePayeeLedgerEntryKind.CheckRelease,
                Amount = -request.Amount,
                Description = $"Check #{request.Id} released to {request.PayableTo}",
                RecordedAtUtc = now,
                RecordedByUserId = actor.Id,
                RecordedByName = actor.DisplayName
            });
        }
        LocalAuditTrail.Record(db, actor, AuditAction(action), "CheckRequest", request.Id);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("This workflow step was already completed. Reload the queue.", exception);
        }

        existing.Add(workflowEvent);
        return ToQueueDto(request, existing);
    }

    public async Task<RepresentativePayeeLedgerEntryDto> AddLedgerEntryAsync(
        int personId,
        DateTime? entryDate,
        decimal amount,
        string? description)
    {
        var errors = RepresentativePayeeLedgerRules.ValidateManualEntry(entryDate, amount, description);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        await EnsureFinanceAsync(db, actor);
        var exists = await db.People.AsNoTracking().AnyAsync(person =>
            person.Id == personId && person.AgencyId == actor.AgencyId && person.CaseManagerIsRepPayee);
        if (!exists) throw new KeyNotFoundException("The representative-payee consumer was not found.");
        var entry = new RepresentativePayeeLedgerEntry
        {
            PersonId = personId,
            EntryDate = entryDate!.Value.Date,
            Kind = amount > 0 ? RepresentativePayeeLedgerEntryKind.Deposit : RepresentativePayeeLedgerEntryKind.Expense,
            Amount = amount,
            Description = description!.Trim(),
            RecordedAtUtc = DateTime.UtcNow,
            RecordedByUserId = actor.Id,
            RecordedByName = actor.DisplayName
        };
        db.RepresentativePayeeLedgerEntries.Add(entry);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.RepresentativePayeeLedgerEntryAdded,
            "Person", personId);
        await db.SaveChangesAsync();
        return ToDto(entry);
    }

    private static async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> BuildQueueAsync(
        SatiContext db,
        IReadOnlyList<CheckRequest> requests,
        Func<CheckRequestWorkflowStatus, bool> include)
    {
        var ids = requests.Select(x => x.Id).ToList();
        var events = await db.CheckRequestWorkflowEvents.AsNoTracking()
            .Where(x => ids.Contains(x.CheckRequestId)).OrderBy(x => x.Id).ToListAsync();
        return requests.Select(request => ToQueueDto(request,
                events.Where(x => x.CheckRequestId == request.Id).ToList()))
            .Where(item => include(item.Status))
            .OrderBy(item => item.NeededByDate ?? DateTime.MaxValue)
            .ThenBy(item => item.CheckRequestId)
            .ToList();
    }

    private static CheckRequestWorkflowQueueItemDto ToQueueDto(
        CheckRequest request,
        IReadOnlyList<CheckRequestWorkflowEvent> events)
    {
        var last = events.OrderBy(x => x.Id).LastOrDefault();
        return new CheckRequestWorkflowQueueItemDto(
            request.Id, request.PersonId, request.ConsumerName, request.CaseManagerName,
            request.SupervisorName, request.PayableTo, request.Amount, request.NeededByDate,
            request.Reason, CheckRequestWorkflowRules.Resolve(request.IsPublished,
                events.Select(x => x.Action)), last?.OccurredAtUtc, last?.ActorName, last?.Note);
    }

    private async Task EnsureFinanceAsync(SatiContext db, User actor)
    {
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        if (!actor.HasRepresentativePayeePermissions)
            throw new UnauthorizedAccessException("Representative-payee permission is required.");
    }

    private static async Task EnsureActionAuthorizedAsync(
        SatiContext db,
        User actor,
        Person person,
        User owner,
        CheckRequestWorkflowAction action)
    {
        if (person.AgencyId != actor.AgencyId)
            throw new KeyNotFoundException("The check request was not found.");
        if (action == CheckRequestWorkflowAction.Submitted)
        {
            if (!person.CaseManagerIsRepPayee)
                throw new InvalidOperationException(
                    "Representative Payee must be enabled for this consumer before submitting a check request.");
            if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, person.Id))
                throw new UnauthorizedAccessException("Only the assigned case manager can submit this check request.");
            return;
        }
        if (action is CheckRequestWorkflowAction.Approved or CheckRequestWorkflowAction.Returned)
        {
            var canReview = actor.HasSupervisorPermissions &&
                (UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions) || owner.SupervisorId == actor.Id);
            if (!canReview) throw new UnauthorizedAccessException("The assigned supervisor must review this check request.");
            return;
        }
        if (!actor.HasRepresentativePayeePermissions || !person.CaseManagerIsRepPayee)
            throw new UnauthorizedAccessException("Representative-payee permission is required for this consumer.");
    }

    private static RepresentativePayeeLedgerEntryDto ToDto(RepresentativePayeeLedgerEntry entry) => new(
        entry.Id, entry.PersonId, entry.CheckRequestId, entry.EntryDate, entry.Kind, entry.Amount,
        entry.Description, entry.RecordedAtUtc, entry.RecordedByUserId, entry.RecordedByName);

    private static string AuditAction(CheckRequestWorkflowAction action) => action switch
    {
        CheckRequestWorkflowAction.Submitted => LocalAuditActions.CheckRequestSubmitted,
        CheckRequestWorkflowAction.Approved => LocalAuditActions.CheckRequestApproved,
        CheckRequestWorkflowAction.Returned => LocalAuditActions.CheckRequestReturned,
        CheckRequestWorkflowAction.Released => LocalAuditActions.CheckRequestReleased,
        CheckRequestWorkflowAction.ReceiptAcknowledged => LocalAuditActions.CheckRequestReceiptAcknowledged,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
