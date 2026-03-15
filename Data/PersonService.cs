using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public class PersonService : IPersonService
    {
        private readonly SatiContext _context;

        //consstructor
        public PersonService(SatiContext context)
        {
            _context = context;
        }

        // methods
        public async Task<Person> AddPersonAsync(Person person)
        {
            _context.People.Add(person);
            await _context.SaveChangesAsync();
            return person;
 
        }

        public async Task DeletePersonAsync(Person person)
        {
            _context.People.Remove(person);
            await _context.SaveChangesAsync();
        }

        public async Task<Person> EditPersonAsync(Person person)
        {
            _context.People.Update(person);
            await _context.SaveChangesAsync();
            return person;
        }

        public async Task<List<Person>> GetAllPeopleAsync()
        {
            return await _context.People
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .ToListAsync();
            ;

        }
    }
}
