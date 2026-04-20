using Microsoft.EntityFrameworkCore;
using Sati;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using System.Diagnostics;
using static Azure.Core.HttpHeader;

public class SupervisorService : ISupervisorService
{
    private readonly IDbContextFactory<SatiContext> _contextFactory;
    private readonly IBillingService _billingService;
    private readonly IUserService _userService;

    public SupervisorService(IDbContextFactory<SatiContext> contextFactory, IBillingService billingService, IUserService userService)
    {
        _contextFactory = contextFactory;
        _billingService = billingService;
        _userService = userService;
    }

    public async Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false)
    {
        await using var context = _contextFactory.CreateDbContext();

        var superviseeIds = allSupervisees
            ? await context.Users.Where(u => u.Role == UserRole.CaseManager).Select(u => u.Id).ToListAsync()
            : (await _userService.GetSuperviseesAsync(supervisorId)).Select(u => u.Id).ToList();

        return await context.Notes
            .Include(n => n.Person)
            .Where(n => n.Status == NoteStatus.Logged
                && superviseeIds.Contains(n.Person.UserId))
            .OrderBy(n => n.EventDate)
            .ToListAsync();
    }

    public async Task ApproveNoteAsync(int noteId, int supervisorId)
    {
        await using var context = _contextFactory.CreateDbContext();
        var note = await context.Notes.FindAsync(noteId)
            ?? throw new InvalidOperationException($"Note {noteId} not found.");

        if (note.Status != NoteStatus.Logged)
            throw new InvalidOperationException("Only logged notes can be approved.");

        note.Status = NoteStatus.Approved;
        note.ApprovedById = supervisorId;
        note.ApprovedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        await _billingService.CreateClaimLineAsync(noteId);
    }

    public async Task ReturnNoteAsync(int noteId, int supervisorId, string reason)
    {
        await using var context = _contextFactory.CreateDbContext();
        var note = await context.Notes.FindAsync(noteId)
            ?? throw new InvalidOperationException($"Note {noteId} not found.");

        if (note.Status != NoteStatus.Logged)
            throw new InvalidOperationException("Only logged notes can be returned.");

        note.Status = NoteStatus.Returned;
        note.ReturnedById = supervisorId;
        note.ReturnReason = reason;

        await context.SaveChangesAsync();
    }
}