using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public PersonService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
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
            return await context.People
                .Where(p => p.UserId == userId)
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .OrderBy(p => p.LastName)
                .ToListAsync();
        }
    }
}