using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class CheckRequestService(IDbContextFactory<SatiContext> contextFactory, ISessionService session)
    : ICheckRequestService
{
    private User Actor => session.CurrentUser ?? throw new UnauthorizedAccessException("Sign in to use check requests.");

    public async Task<List<CheckRequestListItem>> GetAllForPersonAsync(int personId)
    {
        await using var db = contextFactory.CreateDbContext();
        await EnsureCanReadPersonAsync(db, personId);
        return await db.CheckRequests.AsNoTracking()
            .Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.RequestDate)
            .ThenByDescending(x => x.Id)
            .Select(x => new CheckRequestListItem(
                x.Id, x.Revision, x.RequestDate, x.PayableTo, x.Amount, x.NeededByDate, x.PublishedAtUtc))
            .ToListAsync();
    }

    public async Task<CheckRequest?> GetByIdAsync(int id)
    {
        await using var db = contextFactory.CreateDbContext();
        var request = await db.CheckRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (request is null) return null;
        await EnsureCanReadPersonAsync(db, request.PersonId);
        return request;
    }

    public async Task<CheckRequest> CreateDraftAsync(int personId)
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId)
            ?? throw new KeyNotFoundException("The consumer was not found.");
        EnsureOwnsPerson(actor, person);
        var owner = await db.Users.AsNoTracking().Include(x => x.Agency).Include(x => x.Supervisor)
            .SingleAsync(x => x.Id == person.UserId);
        var request = CheckRequest.CreateForClient(person, owner, DateTime.UtcNow);
        db.CheckRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    public Task<CheckRequest> UpdateAsync(CheckRequest request) => SaveAsync(request, publish: false);
    public Task<CheckRequest> PublishAsync(CheckRequest request) => SaveAsync(request, publish: true);

    private async Task<CheckRequest> SaveAsync(CheckRequest incoming, bool publish)
    {
        var actor = Actor;
        await using var db = contextFactory.CreateDbContext();
        var stored = await db.CheckRequests.SingleOrDefaultAsync(x => x.Id == incoming.Id);
        if (stored is null || stored.Revision != incoming.Revision)
            throw new CheckRequestConcurrencyException();
        if (stored.IsPublished)
            throw new CheckRequestLockedException();
        if (!await OwnsPersonAsync(db, actor, stored.PersonId))
            throw new UnauthorizedAccessException("Only the consumer's assigned case manager can change this request.");

        var errors = publish
            ? CheckRequestPublication.FindPublicationBlockers(incoming.RequestDate, incoming.PayableTo,
                incoming.MailingAddress, incoming.Amount, incoming.NeededByDate, incoming.Reason, false)
            : CheckRequestPublication.FindDraftErrors(incoming.RequestDate, incoming.PayableTo,
                incoming.MailingAddress, incoming.Amount, incoming.NeededByDate, incoming.Reason);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        CopyEditable(incoming, stored);
        if (publish)
        {
            stored.Publish(actor, DateTime.UtcNow);
            LocalAuditTrail.Record(db, actor, LocalAuditActions.CheckRequestPublished, "CheckRequest", stored.Id);
        }
        stored.Revision++;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException ex) { throw new CheckRequestConcurrencyException(ex); }

        CopyEditable(stored, incoming);
        incoming.RehydratePublication(stored.PublishedAtUtc, stored.PublishedByUserId, stored.PublishedByName);
        incoming.Revision = stored.Revision;
        return incoming;
    }

    private static void CopyEditable(CheckRequest source, CheckRequest target)
    {
        target.RequestDate = source.RequestDate?.Date;
        target.PayableTo = Normalize(source.PayableTo);
        target.MailingAddress = Normalize(source.MailingAddress);
        target.Amount = source.Amount;
        target.NeededByDate = source.NeededByDate?.Date;
        target.Reason = Normalize(source.Reason);
    }

    private async Task EnsureCanReadPersonAsync(SatiContext db, int personId)
    {
        var actor = Actor;
        var owner = await (from person in db.People.AsNoTracking()
                           join user in db.Users.AsNoTracking() on person.UserId equals user.Id
                           where person.Id == personId && person.AgencyId == actor.AgencyId
                           select new CaseloadParticipant(user.Id, user.AgencyId, user.Permissions, user.SupervisorId))
            .SingleOrDefaultAsync();
        if (owner == default || !CaseloadTransferRules.CanReachOwnOrSupervisedCaseload(actor.ToAgencyActor(), owner))
            throw new UnauthorizedAccessException("This consumer is outside your caseload access.");
    }

    private static async Task<bool> OwnsPersonAsync(SatiContext db, User actor, int personId) =>
        await db.People.AsNoTracking().AnyAsync(person => person.Id == personId &&
            person.UserId == actor.Id && person.AgencyId == actor.AgencyId && actor.HasCaseManagerPermissions);

    private static void EnsureOwnsPerson(User actor, Person person)
    {
        if (person.UserId != actor.Id || person.AgencyId != actor.AgencyId || !actor.HasCaseManagerPermissions)
            throw new UnauthorizedAccessException("Only the consumer's assigned case manager can create a check request.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
