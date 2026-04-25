using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;

        public PersonService(IDbContextFactory<SatiContext> contextFactory, ISettingsService settingsService)
        {
            _contextFactory = contextFactory;
            _settingsService = settingsService;
        }

        public async Task<Person> AddPersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Add(person);
            await context.SaveChangesAsync();
            return person;
        }

        public async Task DeletePersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Remove(person);
            await context.SaveChangesAsync();
        }

        public async Task<Person> EditPersonAsync(Person person)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.People.Update(person);
            await context.SaveChangesAsync();
            return person;
        }

        public async Task<List<Person>> GetAllPeopleAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var people = await context.People
                .Where(p => p.UserId == userId)
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .OrderBy(p => p.LastName)
                .ToListAsync();

            var settings = await _settingsService.LoadAsync();
            var today = DateTime.Today;
            var anyChanges = false;

            foreach (var person in people)
            {
                if (BackfillFirstCycleDueDates(person, settings))
                    anyChanges = true;

                if (person.EnsureCurrentCycleForms(today, settings))
                    anyChanges = true;
            }

            if (anyChanges)
                await context.SaveChangesAsync();

            return people;
        }

        // Option B backfill: recomputes due dates for first-cycle forms only,
        // against current settings. Scoped to the first cycle because second-cycle
        // and later forms represent real history — they were generated under the
        // settings that existed at the time, and rewriting them would lose that.
        //
        // Production-safe because production starts with correct settings, so
        // first-cycle dates will already match the calculator output and this
        // method will be a no-op. Its only practical effect is keeping Josh's
        // existing test data in sync as he tunes the *DaysBeforeAnniversary
        // values during development.
        //
        // Returns true if any due dates were changed.
        private static bool BackfillFirstCycleDueDates(Person person, Settings settings)
        {
            if (person.EffectiveDate is null)
                return false;

            var effective = person.EffectiveDate.Value;
            var firstCycleStart = effective;
            var firstCycleEnd = effective.AddYears(1);
            var changed = false;

            foreach (var form in person.Forms)
            {
                // First-cycle filter: form's due date falls within the first
                // anniversary window. This excludes any second-cycle forms
                // EnsureCurrentCycleForms may have generated on a prior load.
                if (form.DueDate < firstCycleStart || form.DueDate > firstCycleEnd)
                    continue;

                var expected = FormDueDateCalculator.Compute(
                    form.Type, firstCycleStart, firstCycleEnd, settings);

                if (form.DueDate != expected)
                {
                    form.DueDate = expected;
                    changed = true;
                }
            }

            return changed;
        }
    }
}