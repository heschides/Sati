using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;
using static Sati.Enums;

namespace Sati.Data
{
    public class NoteService : INoteService
    {
       
        private readonly SatiContext _context;

        //constructor
        public NoteService(SatiContext context) {  _context = context; }

        public async Task<Note> AddNoteAsync(Note note)
        {
            _context.Notes.Add(note);
            await _context.SaveChangesAsync();
            return note;
        }

        public async Task DeleteNoteAsync(Note note)
        {
            _context.Notes.Remove(note);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateNoteAsync(Note note)
        {
            _context.Notes.Update(note);
            await _context.SaveChangesAsync();
        }

        public Task<List<Note>> GetAllByPersonAsync(int personId)
        {
            return _context.Notes.Where(n => n.PersonId == personId).ToListAsync();
        }

        public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays)
        {
            var threshold = DateTime.Now.AddDays(-abandonedAfterDays);
            var abandonedNotes = await _context.Notes
                .Where(n => n.Status == NoteStatus.Pending &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value < threshold)
                .ToListAsync();

            foreach (var note in abandonedNotes)
                note.Status = NoteStatus.Abandoned;

            await _context.SaveChangesAsync();
        }

        public async Task<List<Note>> GetMonthlyNotesAsync()
        {
            var firstDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            var currentMonthNotes = await _context.Notes
                .Where(n => n.EventDate >= firstDay && n.EventDate <= lastDay).ToListAsync();
            return currentMonthNotes;
        }
    }
}
