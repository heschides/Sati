using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class SupervisorService : ISupervisorService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly IUserService _userService;

        public SupervisorService(IDbContextFactory<SatiContext> contextFactory, IUserService userService)
        {
            _contextFactory = contextFactory;
            _userService = userService;
        }

        // -------------------------------------------------------------------------
        // Queue retrieval
        // -------------------------------------------------------------------------

        // Returns Logged notes whose consumer passes the compliance gate.
        // These go to the supervisor approval queue — content review only,
        // structural compliance already verified.
        public async Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false)
        {
            var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
            var today = DateTime.Today;
            return notes.Where(n => IsComplianceGatePassed(n.Person, today));
        }

        // Returns Logged notes whose consumer fails the compliance gate.
        // These sit in the non-compliant queue until compliance is met or
        // the abandonment threshold passes.
        public async Task<IEnumerable<Note>> GetNonCompliantNotesAsync(int supervisorId, bool allSupervisees = false)
        {
            var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
            var today = DateTime.Today;
            return notes.Where(n => !IsComplianceGatePassed(n.Person, today));
        }

        // -------------------------------------------------------------------------
        // Approval actions
        // -------------------------------------------------------------------------

        public async Task ApproveNoteAsync(int noteId, int supervisorId)
        {
            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .FirstOrDefaultAsync(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            if (note.Status != NoteStatus.Logged)
                throw new InvalidOperationException("Only logged notes can be approved.");

            // Hard compliance guard — service enforces the rule even if UI
            // pre-filters. Cannot be bypassed by calling this method directly.
            if (!IsComplianceGatePassed(note.Person, DateTime.Today))
                throw new InvalidOperationException(
                    $"Cannot approve note {noteId}: consumer {note.Person.FullName} " +
                    $"does not meet compliance requirements. Use ApproveWithOverrideAsync " +
                    $"if a supervisor exception is warranted.");

            note.Status = NoteStatus.Approved;
            note.ApprovedById = supervisorId;
            note.ApprovedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
        }

        // Override path — for notes in the non-compliant queue where the
        // supervisor has judged that billing is appropriate despite the gap.
        // Requires a written justification. Creates a flagged claim line that
        // the billing department must acknowledge before submission.
        public async Task ApproveWithOverrideAsync(int noteId, int supervisorId, string overrideReason)
        {
            if (string.IsNullOrWhiteSpace(overrideReason))
                throw new ArgumentException("Override reason is required.", nameof(overrideReason));

            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                .FirstOrDefaultAsync(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            if (note.Status != NoteStatus.Logged)
                throw new InvalidOperationException("Only logged notes can be approved.");

            note.Status = NoteStatus.Approved;
            note.ApprovedById = supervisorId;
            note.ApprovedAt = DateTime.UtcNow;
            note.ComplianceOverride = true;
            note.OverrideReason = overrideReason;
            note.OverrideApprovedById = supervisorId;
            note.OverrideApprovedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
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
            note.ReturnedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
        }

        // -------------------------------------------------------------------------
        // Compliance gate
        // -------------------------------------------------------------------------

        // Returns true if the person meets all billing compliance requirements
        // as of today. The rule:
        //
        //   Annual forms (current cycle):
        //     PCP, Reclassification, ComprehensiveAssessment — all IsCompliant = true
        //
        //   90-day reviews — previous cycle (if not first cycle):
        //     All four (Q1R–Q4R) must be IsCompliant = true.
        //     Missing records = fail.
        //
        //   90-day reviews — current cycle:
        //     Any review whose DueDate <= today must be IsCompliant = true.
        //     Reviews not yet due are not evaluated.
        //
        // Missing form record for any required check = fail (can't verify = not compliant).
        private static bool IsComplianceGatePassed(Person person, DateTime today)
        {
            // Annual forms — current cycle
            var annualTypes = new[]
            {
                FormType.PCP,
                FormType.Reclassification,
                FormType.ComprehensiveAssessment
            };

            foreach (var type in annualTypes)
            {
                var form = person.GetCurrentCycleForm(type);
                if (form is null || !form.IsCompliant)
                    return false;
            }

            // Get cycle boundaries for review checks
            var boundaries = person.GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return false;

            var (cycleStart, cycleEnd) = boundaries.Value;
            var isFirstCycle = cycleStart == person.EffectiveDate;

            // Previous cycle reviews — required if not first cycle
            if (!isFirstCycle)
            {
                var prevCycleStart = cycleStart.AddYears(-1);
                var prevCycleEnd = cycleStart;

                var reviewTypes = new[] { FormType.Q1R, FormType.Q2R, FormType.Q3R, FormType.Q4R };
                foreach (var type in reviewTypes)
                {
                    var prevForm = person.Forms
                        .Where(f => f.Type == type &&
                                    f.DueDate >= prevCycleStart &&
                                    f.DueDate <= prevCycleEnd)
                        .OrderByDescending(f => f.DueDate)
                        .FirstOrDefault();

                    if (prevForm is null || !prevForm.IsCompliant)
                        return false;
                }
            }

            // Current cycle reviews — only those past due
            var currentReviews = person.Forms
                .Where(f => (f.Type == FormType.Q1R ||
                             f.Type == FormType.Q2R ||
                             f.Type == FormType.Q3R ||
                             f.Type == FormType.Q4R) &&
                             f.DueDate >= cycleStart &&
                             f.DueDate <= cycleEnd &&
                             f.DueDate.Date <= today.Date);

            foreach (var review in currentReviews)
            {
                if (!review.IsCompliant)
                    return false;
            }

            return true;
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        // Shared query for both queue methods — loads Logged notes with Person
        // and Forms included so IsComplianceGatePassed can evaluate in memory
        // without additional DB round trips.
        private async Task<IEnumerable<Note>> GetLoggedNotesAsync(int supervisorId, bool allSupervisees)
        {
            await using var context = _contextFactory.CreateDbContext();

            var superviseeIds = allSupervisees
                ? await context.Users
                    .Where(u => u.Role == UserRole.CaseManager)
                    .Select(u => u.Id)
                    .ToListAsync()
                : (await _userService.GetSuperviseesAsync(supervisorId))
                    .Select(u => u.Id)
                    .ToList();

            return await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .Where(n => n.Status == NoteStatus.Logged
                         && superviseeIds.Contains(n.Person.UserId))
                .OrderBy(n => n.EventDate)
                .ToListAsync();
        }
    }
}