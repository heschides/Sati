using Microsoft.EntityFrameworkCore;
using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    public class NoteService : INoteService
    {
       
        private readonly ProoferContext _context;

        //constructor
        public NoteService(ProoferContext context) {  _context = context; }

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
    }
}
