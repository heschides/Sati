using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class ReviewItemService : IReviewItemService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public ReviewItemService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<List<ReviewItem>> GetForCaseloadAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = CurrentActor();
            if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, userId))
                throw new UnauthorizedAccessException("The caseload is outside your current access.");
            return await context.ReviewItems
                .Include(r => r.Appointment)
                .Where(r => r.Person!.UserId == userId && r.Person.AgencyId == actor.AgencyId)
                .ToListAsync();
        }

        public async Task<List<ReviewItem>> GetForPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await EnsurePersonAccessAsync(context, CurrentActor(), personId);
            return await context.ReviewItems
                .Where(r => r.PersonId == personId)
                .ToListAsync();
        }

        // Generation runs against one context for the whole caseload. Existing
        // items are fetched per person rather than in bulk because the caseload
        // is small and per-person keeps the generator's signature honest — it
        // reasons about one client at a time.
        public async Task<int> EnsureCurrentCycleItemsAsync(IEnumerable<Person> people, DateTime today)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = CurrentActor();
            await LocalTenantAccess.EnsureCurrentActorAsync(context, actor);
            if (!actor.HasCaseManagerPermissions && !actor.HasSupervisorPermissions)
                throw new UnauthorizedAccessException("Current consumer-record permission is required.");
            // IDs identify the request; cycle and provider facts come from stored records.
            // Validate the entire batch before generating anything.
            var storedPeople = new List<Person>();
            foreach (var id in people.Select(person => person.Id).Distinct())
            {
                await EnsurePersonAccessAsync(context, actor, id);
                storedPeople.Add(await context.People.AsNoTracking().SingleAsync(person => person.Id == id));
            }
            var created = 0;

            foreach (var person in storedPeople)
            {
                var boundaries = person.GetCurrentCycleBoundaries(today);
                if (boundaries is null)
                    continue;

                var anchor = boundaries.Value.cycleStart;

                var existing = await context.ReviewItems
                    .Where(r => r.PersonId == person.Id && r.CycleAnchor == anchor)
                    .ToListAsync();

                var missing = ReviewItemGenerator.GenerateMissing(person, anchor, existing);
                if (missing.Count == 0)
                    continue;

                context.ReviewItems.AddRange(missing);
                created += missing.Count;
            }

            if (created > 0)
                await context.SaveChangesAsync();

            return created;
        }

        // Loads, mutates through the sanctioned mutators, saves. The item is
        // re-read here rather than attached from the caller's detached copy so
        // that the private setters stay the only write path and no stale
        // in-memory state gets written back over newer data.
        public async Task<ReviewItem> SetStageDateAsync(int reviewItemId, ReviewStage stage, DateTime? date)
        {
            await using var context = _contextFactory.CreateDbContext();

            var item = await context.ReviewItems.FindAsync(reviewItemId)
                ?? throw new InvalidOperationException($"ReviewItem {reviewItemId} not found.");
            await EnsurePersonAccessAsync(context, CurrentActor(), item.PersonId);

            switch (stage)
            {
                case ReviewStage.Requested:
                    if (date.HasValue) item.MarkRequested(date.Value); else item.ClearRequested();
                    break;
                case ReviewStage.Received:
                    if (date.HasValue) item.MarkReceived(date.Value); else item.ClearReceived();
                    break;
                case ReviewStage.Logged:
                    if (date.HasValue) item.MarkLogged(date.Value); else item.ClearLogged();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage,
                        "NotStarted is a derived state, not a settable one.");
            }

            await context.SaveChangesAsync();
            return item;
        }

        // Upsert-or-remove for the one appointment on a Medical/Dental review item.
        // The Date is the anchor: a null date removes the row (Appointment.Date is
        // non-nullable — no date means no appointment), which is also how a
        // provider-name-with-no-date gets discarded. A non-null date creates the row
        // if absent or updates it through the sanctioned mutator. Mirrors
        // SetStageDateAsync: mutate the tracked graph, save, return the item — its
        // Appointment nav reflects the change for the cell's Refresh.
        public async Task<ReviewItem> SetAppointmentAsync(int reviewItemId, DateTime? date, string? providerName)
        {
            await using var context = _contextFactory.CreateDbContext();

            var item = await context.ReviewItems
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == reviewItemId)
                ?? throw new InvalidOperationException($"ReviewItem {reviewItemId} not found.");
            await EnsurePersonAccessAsync(context, CurrentActor(), item.PersonId);

            if (date is null)
            {
                if (item.Appointment is not null)
                {
                    context.Remove(item.Appointment);
                    item.Appointment = null;
                }
            }
            else if (item.Appointment is null)
            {
                var appointment = new Appointment(reviewItemId, date.Value, providerName);
                context.Add(appointment);
                item.Appointment = appointment;
            }
            else
            {
                item.Appointment.Update(date.Value, providerName);
            }

            await context.SaveChangesAsync();
            return item;
        }

        // Most recent doctor and dentist appointments for a client, across all
        // cycles — each is Max(Date) within its category. Feeds the Clients-tab
        // column and its 365-day overdue tint. AsNoTracking: read-only display,
        // never written back. Null when the client has none of that kind.
        public async Task<(Appointment? Medical, Appointment? Dental)> GetLatestAppointmentsAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await EnsurePersonAccessAsync(context, CurrentActor(), personId);

            var medical = await context.Appointments
                .AsNoTracking()
                .Where(a => a.ReviewItem!.PersonId == personId
                         && a.ReviewItem.Category == ReviewCategory.Medical)
                .OrderByDescending(a => a.Date)
                .FirstOrDefaultAsync();

            var dental = await context.Appointments
                .AsNoTracking()
                .Where(a => a.ReviewItem!.PersonId == personId
                         && a.ReviewItem.Category == ReviewCategory.Dental)
                .OrderByDescending(a => a.Date)
                .FirstOrDefaultAsync();

            return (medical, dental);
        }

        private User CurrentActor() => _sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");

        private static async Task EnsurePersonAccessAsync(SatiContext context, User actor, int personId)
        {
            if (!await LocalTenantAccess.CanAccessPersonAsync(context, actor, personId))
                throw new UnauthorizedAccessException("The consumer is outside your current access.");
        }
    }
}
