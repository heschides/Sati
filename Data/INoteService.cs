using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    public interface INoteService
    {
        Task<Note> AddNoteAsync(Note note);
        Task DeleteNoteAsync(Note note);
        Task UpdateNoteAsync(Note note);
        Task<List<Note>> GetAllByPersonAsync(int personId);
    }
}
