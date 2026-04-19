using Sati.Models;

public interface ISupervisorService
{
    Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false);
    Task ApproveNoteAsync(int noteId, int supervisorId);
    Task ReturnNoteAsync(int noteId, int supervisorId, string reason);
}