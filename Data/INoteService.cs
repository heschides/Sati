using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface INoteService
    {
        Task<Note> AddNoteAsync(Note note);
        Task DeleteNoteAsync(Note note);
        Task UpdateNoteAsync(Note note);
        Task<List<Note>> GetAllByPersonAsync(int personId);
        Task UpdateAbandonedNotesAsync(int abandonedAfterDays);
        Task<List<Note>> GetMonthlyNotesAsync();

    }
}
