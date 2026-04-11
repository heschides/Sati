using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class NoteService : INoteService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public NoteService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Note> AddNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Notes.Add(note);
            await context.SaveChangesAsync();
            return note;
        }

        public async Task DeleteNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Notes.Remove(note);
            await context.SaveChangesAsync();
        }

        public async Task UpdateNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Notes.Update(note);
            await context.SaveChangesAsync();
        }

        public async Task<List<Note>> GetAllByPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Notes
                .Where(n => n.PersonId == personId)
                .ToListAsync();
        }

        public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays)
        {
            await using var context = _contextFactory.CreateDbContext();
            var threshold = DateTime.Now.AddDays(-abandonedAfterDays);
            var abandonedNotes = await context.Notes
                .Where(n => n.Status == NoteStatus.Pending &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value < threshold)
                .ToListAsync();

            foreach (var note in abandonedNotes)
                note.Status = NoteStatus.Abandoned;

            await context.SaveChangesAsync();
        }

        public async Task<List<Note>> GetMonthlyNotesAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var firstDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            return await context.Notes
                .Where(n => n.EventDate >= firstDay &&
                            n.EventDate <= lastDay &&
                            n.Person.UserId == userId)
                .ToListAsync();
        }
    }
}