using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Contracts.V1;
using Sati.Helpers;
using System.Data;

namespace Sati.Services.Billing
{
    public class BillingService : IBillingService
    {
        public bool SupportsMockClearinghouse => false;
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService? _sessionService;
        private BillingCompliancePolicyContext _complianceContext =
            BillingCompliancePolicyContext.Default();
        private IReadOnlyDictionary<int, IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>>
            _recoveryDecisionsByNoteId =
                new Dictionary<int, IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>>();

        public BillingService(IDbContextFactory<SatiContext> contextFactory, ISessionService? sessionService = null)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor suppliedActor, int userId, int month, int year)
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2200)
                throw new ArgumentOutOfRangeException(nameof(month), "The billing period is invalid.");
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            if (!await context.Users.AnyAsync(user => user.Id == userId && user.AgencyId == actor.AgencyId))
                throw new InvalidOperationException("The billing user was not found in your agency.");
            var period = await context.BillingPeriods
                .FirstOrDefaultAsync(b => b.UserId == userId
                    && b.Month == month
                    && b.Year == year);

            if (period is not null)
                return period;

            period = new BillingPeriod
            {
                UserId = userId,
                Month = month,
                Year = year,
                Status = BillingStatus.Draft
            };

            context.BillingPeriods.Add(period);
            try
            {
                await context.SaveChangesAsync();
                return period;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                var completed = await context.BillingPeriods.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId &&
                        candidate.Month == month && candidate.Year == year);
                if (completed is not null)
                    return completed;
                throw;
            }
        }

        public async Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor suppliedActor, int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var periods = await context.BillingPeriods
                .Where(period => period.UserId == userId &&
                    context.Users.Any(user => user.Id == period.UserId && user.AgencyId == actor.AgencyId))
                .Include(b => b.Lines)
                .Include(b => b.User)
                .OrderByDescending(b => b.Year)
                .ThenByDescending(b => b.Month)
                .ToListAsync();
            foreach (var period in periods)
            {
                period.CaseManagerName = period.User.DisplayName;
                ApplyClaimReadiness(period);
            }
            return periods;
        }

        public async Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var periods = await context.BillingPeriods
                .Where(period => context.Users.Any(user =>
                    user.Id == period.UserId && user.AgencyId == actor.AgencyId))
                .Include(b => b.Lines)
                .Include(b => b.User)
                .OrderByDescending(b => b.Year)
                .ThenByDescending(b => b.Month)
                .ToListAsync();
            foreach (var period in periods)
            {
                period.CaseManagerName = period.User.DisplayName;
                ApplyClaimReadiness(period);
            }
            return periods;
        }

        public async Task<ClaimLine> CreateClaimLineAsync(AgencyActor suppliedActor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);

            var note = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Agency)
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                        .ThenInclude(form => form.Attestations)
                .Include(n => n.Person)
                    .ThenInclude(p => p.ReleaseObligations)
                        .ThenInclude(obligation => obligation.Attestations)
                .FirstOrDefaultAsync(n => n.Id == noteId && n.Person.AgencyId == actor.AgencyId)
                ?? throw new InvalidOperationException($"Note {noteId} was not found in your agency.");

            if (note.Person is null)
                throw new InvalidOperationException($"Note {noteId} has no associated person.");
            await ReleaseComplianceProjectionLoader.PopulateAsync(
                context, [note.Person], actor.AgencyId);

            if (note.Status != NoteStatus.Approved)
                throw new InvalidOperationException("Only an approved service note can become a claim line.");

            var complianceContext = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, actor.AgencyId);
            var recoveryDecisions = await LoadRecoveryDecisionsForNoteAsync(
                context, actor.AgencyId, note.PersonId, note.Id);
            var validation = ValidateNoteForBilling(note, complianceContext, recoveryDecisions);
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Note {noteId} is not ready for billing: {string.Join("; ", validation.Errors)}");

            if (await context.ClaimLines.AnyAsync(line => line.NoteId == noteId))
                throw new InvalidOperationException("This service note already has a billing claim line.");

            var serviceDate = note.EventDate!.Value.Date;
            var period = await context.BillingPeriods
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate =>
                    candidate.UserId == note.Person.UserId && candidate.Month == serviceDate.Month &&
                    candidate.Year == serviceDate.Year);
            if (period is null)
            {
                period = new BillingPeriod
                {
                    UserId = note.Person.UserId,
                    Month = serviceDate.Month,
                    Year = serviceDate.Year,
                    Status = BillingStatus.Draft
                };
                context.BillingPeriods.Add(period);
            }
            if (period.Status != BillingStatus.Draft)
                throw new InvalidOperationException("This billing period is no longer a draft.");

            var units = BillingRules.CalculateSection13Units(note.Minutes);
            var procedureCode = note.Person.Agency!.BillingProcedureCode!;
            var unitRate = note.Person.Agency.BillingUnitRate!.Value;

            var claimLine = new ClaimLine
            {
                NoteId = noteId,
                DateOfService = serviceDate,
                ProcedureCode = procedureCode,
                ProcedureModifier = note.Person.Agency.BillingModifier,
                Units = units,
                ChargeAmount = BillingRules.CalculateCharge(units, unitRate),
                ClientMaineCareId = note.Person.MaineCareId ?? string.Empty,
                RenderingProviderNpi = note.Person.Agency?.Npi ?? string.Empty,
                DiagnosisCode = note.Person.DiagnosisCode ?? string.Empty,
                PlaceOfService = (int?)note.Person.PlaceOfService ?? (int)PlaceOfService.Other,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(
                    CreateClaimSnapshot(note.Person, note.Person.Agency!)),
                // A claim's exception marker is an official financial-record fact.
                // It must reflect the documented supervisor decision on the note,
                // never a value supplied by the billing caller.
                IsComplianceException = note.ComplianceOverride,
                ComplianceExceptionReason = note.ComplianceOverride ? note.OverrideReason : null
            };

            // Attach through the period's collection rather than by copying its id.
            // The first claim line of a new month is created alongside the period
            // itself, whose identity is still 0 until SaveChanges runs; assigning
            // BillingPeriodId here would persist a line pointing at no period.
            period.Lines.Add(claimLine);
            var readiness = ApplyClaimReadiness(period);
            if (!readiness.IsReady)
            {
                throw new InvalidOperationException(
                    $"The exact claim line is not 837P-ready: {readiness.ExplainFailure()}");
            }
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingClaimLineCreated, "Note", noteId);
            try
            {
                await context.SaveChangesAsync();
                return claimLine;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                if (await context.ClaimLines.AsNoTracking().AnyAsync(line => line.NoteId == noteId))
                    throw new InvalidOperationException("This service note already has a billing claim line.");
                throw;
            }
        }

        public async Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor suppliedActor, int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            return await context.ClaimLines
                .Include(c => c.BillingPeriod)
                .Where(c => c.BillingPeriod.UserId == userId
                    && c.BillingPeriod.Status == BillingStatus.Draft
                    && context.Users.Any(user => user.Id == c.BillingPeriod.UserId &&
                        user.AgencyId == actor.AgencyId))
                .OrderBy(c => c.DateOfService)
                .ToListAsync();
        }

        public async Task SubmitBillingPeriodAsync(AgencyActor suppliedActor, int billingPeriodId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            var period = await context.BillingPeriods.Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.Id == billingPeriodId &&
                    context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
                ?? throw new InvalidOperationException($"Billing period {billingPeriodId} was not found in your agency.");

            if (period.Status == BillingStatus.Submitted)
                return;

            if (period.Status != BillingStatus.Draft)
                throw new InvalidOperationException("Only draft billing periods can be submitted.");
            if (period.Lines.Count == 0)
                throw new InvalidOperationException("A billing period with no claim lines cannot be submitted.");

            try
            {
                EdiGenerator.ValidatePeriod(period);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"This billing period is not ready to submit: {exception.Message}", exception);
            }

            await RevalidateDraftPeriodComplianceAsync(context, actor.AgencyId, period);

            period.Status = BillingStatus.Submitted;
            period.SubmittedAt = DateTime.UtcNow;
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingPeriodSubmitted,
                "BillingPeriod", billingPeriodId);
            try
            {
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                var completed = await context.BillingPeriods.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == billingPeriodId);
                if (completed?.Status == BillingStatus.Submitted)
                    return;
                throw new InvalidOperationException(
                    "The billing period changed while it was being submitted.");
            }
        }

        private static async Task RevalidateDraftPeriodComplianceAsync(
            SatiContext context,
            int agencyId,
            BillingPeriod period)
        {
            var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToArray();
            var notes = await context.Notes
                .Include(note => note.Person)
                    .ThenInclude(person => person.Agency)
                .Include(note => note.Person)
                    .ThenInclude(person => person.Forms)
                        .ThenInclude(form => form.Attestations)
                .Include(note => note.Person)
                    .ThenInclude(person => person.ReleaseObligations)
                        .ThenInclude(obligation => obligation.Attestations)
                .Where(note => noteIds.Contains(note.Id) &&
                               note.Person.AgencyId == agencyId)
                .AsSplitQuery()
                .ToListAsync();
            if (notes.Count != noteIds.Length)
                throw new InvalidOperationException(
                    "A draft claim line no longer has an accessible source note.");
            await ReleaseComplianceProjectionLoader.PopulateAsync(
                context, notes.Select(note => note.Person), agencyId);

            var policy = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, agencyId);
            var decisions = await context.BillingComplianceRecoveryDecisions.AsNoTracking()
                .Include(item => item.Obligations)
                .Include(item => item.Notes)
                .Where(item => item.AgencyId == agencyId &&
                               item.Notes.Any(note => noteIds.Contains(note.NoteId)))
                .ToListAsync();
            var decisionsByNote = decisions
                .Select(item => item.ToContract())
                .SelectMany(decision => decision.NoteIds.Select(noteId => (noteId, decision)))
                .ToLookup(item => item.noteId, item => item.decision);
            var notesById = notes.ToDictionary(note => note.Id);

            foreach (var line in period.Lines)
            {
                var note = notesById[line.NoteId];
                if (note.EventDate?.Date != line.DateOfService.Date)
                    throw new InvalidOperationException(
                        $"Draft claim line {line.Id} no longer matches its source note's service date.");
                var validation = ValidateNoteForBilling(
                    note,
                    policy,
                    decisionsByNote[note.Id].ToArray());
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Draft claim line {line.Id} is no longer eligible for submission: " +
                        string.Join("; ", validation.Errors));
                }
            }
        }

        public async Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            _complianceContext = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, actor.AgencyId);
            var notes = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Agency)
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                        .ThenInclude(form => form.Attestations)
                .Include(n => n.Person)
                    .ThenInclude(p => p.ReleaseObligations)
                        .ThenInclude(obligation => obligation.Attestations)
                .Where(n => n.Status == NoteStatus.Approved
                         && n.Person.AgencyId == actor.AgencyId
                         && !context.ClaimLines.Any(c => c.NoteId == n.Id))
                .OrderBy(n => n.EventDate)
                .ToListAsync();
            await ReleaseComplianceProjectionLoader.PopulateAsync(
                context, notes.Select(note => note.Person), actor.AgencyId);

            var noteIds = notes.Select(note => note.Id).ToArray();
            var decisions = await context.BillingComplianceRecoveryDecisions.AsNoTracking()
                .Include(item => item.Obligations)
                .Include(item => item.Notes)
                .Where(item => item.AgencyId == actor.AgencyId &&
                               item.Notes.Any(note => noteIds.Contains((int)note.NoteId)))
                .ToListAsync();
            _recoveryDecisionsByNoteId = noteIds.ToDictionary(
                noteId => noteId,
                noteId => (IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>)decisions
                    .Where(item => item.Notes.Any(note => note.NoteId == noteId))
                    .Select(item => item.ToContract())
                    .ToArray());
            return notes;
        }

        public BillingValidationResult ValidateNoteForBilling(Note note)
            => ValidateNoteForBilling(
                note,
                _complianceContext,
                _recoveryDecisionsByNoteId.GetValueOrDefault(note.Id) ?? []);

        internal static BillingValidationResult ValidateNoteForBilling(
            Note note,
            BillingCompliancePolicyContext complianceContext,
            IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>? recoveryDecisions = null)
        {
            var errors = ValidateNonComplianceBillingRequirements(note).ToList();
            errors.AddRange(EvaluateBillingComplianceRelease(
                note, complianceContext, recoveryDecisions));

            return new BillingValidationResult(
                IsValid: errors.Count == 0,
                Note: note,
                Errors: errors);
        }

        /// <summary>
        /// The one compliance decision for releasing a note to billing: the
        /// service-date policy, minus an exact-obligation Supervisor exception or an
        /// Admin recovery whose frozen evidence still matches. Claim creation and
        /// 837P release share it, so a released note cannot be re-blocked by a
        /// second, weaker rule, and release never re-forms the frozen claim's
        /// identity checks.
        /// </summary>
        internal static IReadOnlyList<string> EvaluateBillingComplianceRelease(
            Note note,
            BillingCompliancePolicyContext complianceContext,
            IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>? recoveryDecisions = null)
        {
            var errors = new List<string>();
            if (note.Person is not null && note.EventDate is not null)
            {
                // Claim eligibility is historical: later noncompliance cannot
                // make an earlier service date non-billable. A supervisor
                // exception releases only the exact blockers recorded in the
                // immutable approval decision.
                var serviceDate = note.EventDate.Value;
                var compliance = note.Person.EvaluateBillingWindowDetailed(
                    serviceDate,
                    complianceContext.Resolve(serviceDate),
                    complianceContext.Schedule);
                if (compliance.Passed)
                {
                    // No exception is needed for this service date.
                }
                else if (IsReleasedByRecovery(
                             note,
                             complianceContext,
                             recoveryDecisions ?? []))
                {
                    // An immutable Admin recovery decision releases this exact note
                    // and only the exact completed blocker facts it recorded.
                }
                else if (note.ComplianceOverride)
                {
                    var exception = BillingComplianceExceptionRules.Validate(
                        compliance.Blockers ?? [],
                        note.OverrideObligationIds,
                        note.OverrideReason,
                        note.OverrideAttestationConfirmed &&
                        note.OverrideApprovedById is not null &&
                        note.OverrideApprovedAt is not null);
                    errors.AddRange(exception.Errors.Select(error => $"Compliance exception: {error}"));
                    if (exception.Accepted)
                    {
                        errors.AddRange(BillingComplianceExceptionRules.RemainingBlockers(
                                compliance.Blockers ?? [],
                                exception.SelectedObligationIds)
                            .Select(blocker =>
                                $"{blocker.Name} was due {blocker.DueDate:MMM d, yyyy} " +
                                "and was not completed as of this service date."));
                    }
                }
                else
                {
                    errors.AddRange(compliance.Reasons);
                }
            }

            return errors;
        }

        public async Task<BillingComplianceRecoveryPlan> PrepareComplianceRecoveryAsync(
            AgencyActor suppliedActor,
            int personId,
            CancellationToken cancellationToken = default)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateAdminActorAsync(context, suppliedActor, cancellationToken);
            var (person, notes, policy) = await LoadRecoveryInputsAsync(
                context, actor.AgencyId, personId, cancellationToken);
            return PrepareRecoveryPlan(person, notes, policy,
                BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow));
        }

        public async Task<Sati.Contracts.V1.BillingComplianceRecoveryDecision> RecordComplianceRecoveryAsync(
            AgencyActor suppliedActor,
            int personId,
            CreateBillingComplianceRecoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateAdminActorAsync(context, suppliedActor, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var (person, notes, policy) = await LoadRecoveryInputsAsync(
                context, actor.AgencyId, personId, cancellationToken);
            var recordedAtUtc = DateTime.UtcNow;
            var plan = PrepareRecoveryPlan(
                person, notes, policy, BillingRules.MaineBusinessDate(recordedAtUtc));
            var result = BillingComplianceRecoveryRules.CreateDecision(
                plan,
                request.SelectedNoteIds ?? [],
                actor.Id,
                DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
                request.Explanation,
                request.AttestationConfirmed);
            if (!result.Accepted || result.Decision is null)
                throw new ArgumentException(string.Join(" ", result.Errors), nameof(request));

            context.BillingComplianceRecoveryDecisions.Add(
                Sati.Models.BillingComplianceRecoveryDecision.FromContract(result.Decision));
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.BillingComplianceRecoveryRecorded,
                "Person",
                personId,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    result.Decision.DecisionId,
                    result.Decision.PersonId,
                    result.Decision.NoteIds,
                    obligations = result.Decision.Obligations.Select(item => new
                    {
                        item.ObligationId,
                        item.DueDate,
                        item.CompletedDate,
                        item.EvidenceId
                    })
                }));
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    "A selected note was already recovered by another administrator. Refresh the checklist.",
                    exception);
            }

            return result.Decision;
        }

        private static IReadOnlyList<string> ValidateNonComplianceBillingRequirements(Note note)
        {
            var errors = new List<string>();
            if (note.Status != NoteStatus.Approved)
                errors.Add("Service note is not approved.");
            if (note.EventDate is null)
                errors.Add("No service date.");
            if (BillingRules.CalculateSection13Units(note.Minutes) < 1)
                errors.Add("Units must be at least 1 (minimum billable unit for Section 13 TCM).");
            if (string.IsNullOrWhiteSpace(note.Person?.MaineCareId))
                errors.Add("Consumer has no MaineCare ID.");
            if (!BillingRules.IsValidDiagnosisCode(note.Person?.DiagnosisCode))
                errors.Add("Consumer diagnosis code is missing or invalid.");
            if (note.Person?.PlaceOfService is null)
                errors.Add("Consumer has no place of service.");
            if (!HasValidSubscriberClaimIdentity(note.Person))
                errors.Add("Consumer claim name, birth date, or structured claim address is incomplete or invalid.");
            if (!BillingRules.IsValidNpi(note.Person?.Agency?.Npi))
                errors.Add("Agency NPI is missing or invalid.");
            if (note.Person?.Agency is Agency agency)
                errors.AddRange(ValidateBillingConfiguration(agency));
            return errors;
        }

        private static bool IsReleasedByRecovery(
            Note note,
            BillingCompliancePolicyContext policy,
            IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision> decisions)
        {
            if (note.Person is null || note.EventDate is not DateTime serviceDate ||
                decisions.Count == 0)
                return false;

            var noteSnapshot = new BillingRecoveryNoteSnapshot(
                note.Id, note.PersonId, serviceDate, IsSubmittedOrBilled: false);
            var obligations = BuildRecoveryObligations(
                note.Person, policy.Schedule, serviceDate);
            var version = policy.ResolveSnapshot(serviceDate);
            return decisions.Any(decision => BillingComplianceRecoveryRules.IsReleased(
                noteSnapshot, obligations, version, decision));
        }

        private static BillingComplianceRecoveryPlan PrepareRecoveryPlan(
            Person person,
            IReadOnlyList<Note> notes,
            BillingCompliancePolicyContext policy,
            DateTime agencyToday)
        {
            var eligible = notes
                .Where(note => ValidateNonComplianceBillingRequirements(note).Count == 0)
                .Select(note => new BillingRecoveryNoteSnapshot(
                    note.Id,
                    person.Id,
                    note.EventDate!.Value.Date,
                    IsSubmittedOrBilled: false))
                .ToArray();
            return BillingComplianceRecoveryRules.Prepare(
                policy.AgencyId,
                person.Id,
                policy.RecoveryVersions,
                BuildRecoveryObligations(person, policy.Schedule, agencyToday),
                eligible,
                agencyToday);
        }

        internal static IReadOnlyList<BillingComplianceObligationSnapshot> BuildRecoveryObligations(
            Person person,
            ComplianceScheduleSettings schedule,
            DateTime asOfDate)
        {
            var releaseFacts = ExpectedBillingComplianceObligations.IncludeMissingReleases(
                person.EffectiveDate,
                person.ReleaseObligations.Select(item => item.ToComplianceFact()),
                asOfDate,
                person.ReleaseProviderLinksForCompliance);
            var reconciledReleaseCycles = releaseFacts
                .Where(item => item.TargetEffectiveDate is not null)
                .Select(item => item.TargetEffectiveDate!.Value.Date)
                .ToHashSet();
            var formSnapshots = person.Forms
                .Where(form => form.Type is not (FormType.Release_Agency or
                    FormType.Release_DHHS or FormType.Release_Medical) ||
                    !reconciledReleaseCycles.Contains(
                        (form.TargetEffectiveDate == default
                            ? form.DueDate
                            : form.TargetEffectiveDate).Date))
                .Select(form =>
                {
                    var attestation = form.CompletedDate is DateTime completedOn
                        ? form.Attestations
                            .Where(item => item.Kind == FormAttestationKind.Attested &&
                                           item.CompletedOn?.Date == completedOn.Date)
                            .OrderBy(item => item.RecordedAtUtc)
                            .ThenBy(item => item.Id)
                            .FirstOrDefault()
                        : null;
                    var completionEvidence = attestation is { Id: > 0 }
                        ? $"form-attestation:{attestation.Id}"
                        : form.CompletedDate is DateTime completed
                            ? $"form-completion:{form.Id}:{completed:yyyy-MM-dd}"
                            : null;
                    var openingEvidence = form.OpenedDate is DateTime opened
                        ? $"form-opened:{form.Id}:{opened:yyyy-MM-dd}"
                        : null;
                    return new ComplianceFormSnapshot(
                        form.Type.ToString(),
                        form.DueDate,
                        form.CompletedDate,
                        form.OpenedDate,
                        form.Id > 0 ? $"form:{form.Id}" : null,
                        completionEvidence,
                        openingEvidence,
                        form.TargetEffectiveDate == default
                            ? null
                            : form.TargetEffectiveDate);
                });
            var withExpectedForms = ExpectedBillingComplianceObligations.IncludeMissingForms(
                person.EffectiveDate,
                formSnapshots,
                asOfDate,
                schedule);
            var formsAndOpening = BillingComplianceGate.IncludeOpeningObligations(
                withExpectedForms);
            return BillingComplianceRecoveryRules.FromComplianceSnapshots(
                    person.Id, formsAndOpening)
                .Concat(ReleaseBillingRules.BuildRecoveryObligations(
                    person.Id,
                    releaseFacts))
                .ToArray();
        }

        private async Task<(Person Person, IReadOnlyList<Note> Notes, BillingCompliancePolicyContext Policy)>
            LoadRecoveryInputsAsync(
                SatiContext context,
                int agencyId,
                int personId,
                CancellationToken cancellationToken)
        {
            var person = await context.People
                .Include(item => item.Agency)
                .Include(item => item.Forms)
                    .ThenInclude(form => form.Attestations)
                .Include(item => item.ReleaseObligations)
                    .ThenInclude(obligation => obligation.Attestations)
                .SingleOrDefaultAsync(item =>
                    item.Id == personId && item.AgencyId == agencyId,
                    cancellationToken)
                ?? throw new InvalidOperationException("The consumer was not found in your agency.");

            var notes = await context.Notes
                .Where(note => note.PersonId == personId &&
                               note.Status == NoteStatus.Approved &&
                               !context.ClaimLines.Any(line => line.NoteId == note.Id))
                .OrderBy(note => note.EventDate)
                .ThenBy(note => note.Id)
                .ToListAsync(cancellationToken);
            foreach (var note in notes)
                note.Person = person;

            var policy = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, agencyId, cancellationToken);
            await ReleaseComplianceProjectionLoader.PopulateAsync(
                context, [person], agencyId, cancellationToken);
            var decisions = await context.BillingComplianceRecoveryDecisions.AsNoTracking()
                .Include(item => item.Obligations)
                .Include(item => item.Notes)
                .Where(item => item.AgencyId == agencyId && item.PersonId == personId)
                .ToListAsync(cancellationToken);
            var contracts = decisions.Select(item => item.ToContract()).ToArray();
            notes = notes.Where(note => !IsReleasedByRecovery(
                    note,
                    policy,
                    contracts.Where(decision => decision.NoteIds.Contains(note.Id)).ToArray()))
                .ToList();
            return (person, notes, policy);
        }

        internal static async Task<IReadOnlyList<Sati.Contracts.V1.BillingComplianceRecoveryDecision>>
            LoadRecoveryDecisionsForNoteAsync(
                SatiContext context,
                int agencyId,
                int personId,
                int noteId)
        {
            var rows = await context.BillingComplianceRecoveryDecisions.AsNoTracking()
                .Include(item => item.Obligations)
                .Include(item => item.Notes)
                .Where(item => item.AgencyId == agencyId && item.PersonId == personId &&
                               item.Notes.Any(note => note.NoteId == noteId))
                .ToListAsync();
            return rows.Select(item => item.ToContract()).ToArray();
        }

        public async Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var agency = await context.Agencies.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == actor.AgencyId);
            return ToBillingConfiguration(agency);
        }

        public async Task SaveBillingConfigurationAsync(AgencyActor suppliedActor, BillingConfiguration configuration)
        {
            var normalized = NormalizeBillingConfiguration(configuration);
            var errors = ValidateBillingConfiguration(normalized);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(configuration));

            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var agency = await context.Agencies.SingleAsync(candidate => candidate.Id == actor.AgencyId);
            agency.BillingProcedureCode = normalized.ProcedureCode;
            agency.BillingModifier = normalized.Modifier;
            agency.BillingUnitRate = normalized.UnitRate;
            agency.EdiSubmitterId = normalized.EdiSubmitterId;
            agency.EdiPayerName = normalized.PayerName;
            agency.EdiPayerId = normalized.PayerId;
            agency.EdiContactName = normalized.ContactName;
            agency.EdiContactPhone = normalized.ContactPhone;
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingConfigurationUpdated,
                "Agency", agency.Id);
            await context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            return await (from item in context.BillingSubmissionEvents.AsNoTracking()
                          join period in context.BillingPeriods.AsNoTracking() on item.BillingPeriodId equals period.Id
                          join owner in context.Users.AsNoTracking() on period.UserId equals owner.Id
                          where item.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId
                          orderby item.OccurredAtUtc descending
                          select new BillingSubmissionHistoryDto(
                              item.Id, period.Id, period.Year, period.Month, owner.DisplayName,
                              period.Lines.Count, item.OccurredAtUtc, item.Stage.ToString(),
                              item.Reference, item.ResponseType, item.ResponseCode,
                              item.Explanation, item.IsSynthetic)).ToListAsync();
        }

        public async Task<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>
            GetBillingCompliancePolicyReviewFlagsAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var flags = await context.BillingCompliancePolicyReviewFlags.AsNoTracking()
                .Include(flag => flag.PolicyVersion)
                .Where(flag => flag.AgencyId == actor.AgencyId)
                .OrderByDescending(flag => flag.CreatedAtUtc)
                .ThenByDescending(flag => flag.Id)
                .ToListAsync();
            return flags.Select(flag => flag.ToContract()).ToArray();
        }

        public async Task ReturnBillingPeriodToDraftAsync(AgencyActor suppliedActor, int billingPeriodId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var period = await context.BillingPeriods
                .SingleOrDefaultAsync(candidate => candidate.Id == billingPeriodId &&
                    context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
                ?? throw new InvalidOperationException($"Billing period {billingPeriodId} was not found in your agency.");

            var hasExchangeHistory = await context.EdiGenerations.AsNoTracking()
                .AnyAsync(item => item.AgencyId == actor.AgencyId && item.BillingPeriodId == billingPeriodId) ||
                await context.BillingSubmissionEvents.AsNoTracking()
                    .AnyAsync(item => item.AgencyId == actor.AgencyId && item.BillingPeriodId == billingPeriodId);
            var errors = BillingPeriodWorkflow.ValidateReturnToDraft(
                period.Status == BillingStatus.Submitted,
                hasExchangeHistory);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            period.Status = BillingStatus.Draft;
            period.SubmittedAt = null;
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingPeriodReturnedToDraft,
                "BillingPeriod", billingPeriodId);
            try
            {
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "The billing period changed while it was being returned to draft.");
            }
        }

        public Task<MockClearinghouseResultDto> SubmitToMockClearinghouseAsync(
            AgencyActor actor,
            int billingPeriodId,
            MockClearinghouseScenario scenario) =>
            throw new NotSupportedException("The mock clearinghouse is available only in Demo.");

        public async Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            return await context.RemittanceClaimOutcomes.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .Select(item => new RemittanceClaimOutcomeDto(
                    item.Id, item.BillingPeriodId, item.ClaimReference, item.PayerName,
                    item.ReceivedAtUtc, item.PaymentDate, item.Status.ToString(),
                    item.BilledAmount, item.AllowedAmount, item.PaidAmount,
                    item.AdjustmentAmount, item.PatientResponsibilityAmount,
                    item.ReasonCode, item.Explanation, item.PaymentReference,
                    item.IsSynthetic))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor suppliedActor)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateBillingActorAsync(context, suppliedActor);
            var deposits = await context.RemittanceDeposits.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .ToListAsync();
            return deposits.Select(ToDepositDto).ToList();
        }

        private static RemittanceDepositDto ToDepositDto(RemittanceDeposit item)
        {
            var status = DepositReconciliationRules.GetStatus(
                item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount,
                item.RemittancePaymentAmount, item.EftDepositAmount);
            return new RemittanceDepositDto(
                item.Id, item.PaymentReference, item.PayerName, item.ReceivedAtUtc,
                item.PaymentDate, item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount,
                item.ProviderLevelAdjustmentSummary, item.RemittancePaymentAmount,
                item.EftDepositAmount, status.ToString(),
                item.EftDepositAmount - item.RemittancePaymentAmount,
                DepositReconciliationRules.Explain(status), item.IsSynthetic);
        }

        private static BillingConfiguration ToBillingConfiguration(Agency agency) => new(
            agency.BillingProcedureCode ?? string.Empty,
            agency.BillingModifier,
            agency.BillingUnitRate,
            agency.EdiSubmitterId ?? string.Empty,
            agency.EdiPayerName ?? string.Empty,
            agency.EdiPayerId ?? string.Empty,
            agency.EdiContactName ?? string.Empty,
            agency.EdiContactPhone ?? string.Empty);

        private static BillingConfiguration NormalizeBillingConfiguration(BillingConfiguration configuration) => new(
            (configuration.ProcedureCode ?? string.Empty).Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(configuration.Modifier) ? null : configuration.Modifier.Trim().ToUpperInvariant(),
            configuration.UnitRate,
            (configuration.EdiSubmitterId ?? string.Empty).Trim(),
            (configuration.PayerName ?? string.Empty).Trim(),
            (configuration.PayerId ?? string.Empty).Trim(),
            (configuration.ContactName ?? string.Empty).Trim(),
            new string((configuration.ContactPhone ?? string.Empty).Where(char.IsDigit).ToArray()));

        private static IReadOnlyList<string> ValidateBillingConfiguration(Agency agency)
        {
            var errors = ValidateBillingConfiguration(ToBillingConfiguration(agency)).ToList();
            if (!BillingRules.IsSafeX12Element(agency.Name, 60) ||
                !BillingRules.IsValidNpi(agency.Npi) ||
                !BillingRules.IsSafeX12Element(agency.TaxId, 50) ||
                !BillingRules.IsSafeX12Element(agency.Street, 55) ||
                !BillingRules.IsSafeX12Element(agency.City, 30) ||
                !BillingRules.IsSafeX12Element(agency.State, 2) ||
                !BillingRules.IsSafeX12Element(agency.Zip, 15))
                errors.Add("Agency billing provider name, NPI, tax ID, or structured address is incomplete or invalid.");
            return errors;
        }

        private static IReadOnlyList<string> ValidateBillingConfiguration(BillingConfiguration configuration)
        {
            var errors = new List<string>();
            if (!BillingRules.IsValidProcedureCode(configuration.ProcedureCode))
                errors.Add("Agency billing procedure code is missing or invalid.");
            if (!BillingRules.IsValidModifier(configuration.Modifier))
                errors.Add("Agency billing modifier is invalid.");
            if (configuration.UnitRate is null or <= 0)
                errors.Add("Agency billing unit rate is missing or invalid.");
            if (!BillingRules.IsSafeX12Element(configuration.EdiSubmitterId, 15))
                errors.Add("EDI submitter ID is missing, invalid, or longer than 15 characters.");
            if (!BillingRules.IsSafeX12Element(configuration.PayerName, 60) ||
                !BillingRules.IsSafeX12Element(configuration.PayerId, 80))
                errors.Add("EDI payer name or payer ID is missing or invalid.");
            if (!BillingRules.IsSafeX12Element(configuration.ContactName, 60) ||
                configuration.ContactPhone.Length is < 10 or > 15 ||
                configuration.ContactPhone.Any(character => !char.IsDigit(character)))
                errors.Add("EDI contact name or telephone number is missing or invalid.");
            return errors;
        }

        private static bool HasValidSubscriberClaimIdentity(Person? person) =>
            person is not null &&
            BillingRules.IsSafeX12Element(person.FirstName, 35) &&
            BillingRules.IsSafeX12Element(person.LastName, 60) &&
            person.BirthDate >= new DateTime(1900, 1, 1) &&
            BillingRules.IsSafeX12Element(person.BillingStreet, 55) &&
            BillingRules.IsSafeX12Element(person.BillingCity, 30) &&
            BillingRules.IsSafeX12Element(person.BillingState, 2) &&
            BillingRules.IsSafeX12Element(person.BillingZip, 15);

        private static ProfessionalClaimSnapshot CreateClaimSnapshot(Person person, Agency agency) => new(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            agency.Id,
            person.Id,
            person.FirstName!,
            person.LastName!,
            person.BirthDate.Date,
            person.Gender == Gender.Male ? "M" : person.Gender == Gender.Female ? "F" : "U",
            person.MaineCareId!,
            person.BillingStreet!,
            person.BillingCity!,
            person.BillingState!,
            person.BillingZip!,
            agency.Name,
            agency.Npi!,
            agency.TaxId!,
            agency.Street!,
            agency.City!,
            agency.State!,
            agency.Zip!,
            agency.EdiSubmitterId!,
            agency.EdiContactName!,
            agency.EdiContactPhone!,
            agency.EdiPayerName!,
            agency.EdiPayerId!);

        private static ProfessionalClaimPeriodReadiness ApplyClaimReadiness(BillingPeriod period)
        {
            var readiness = ProfessionalClaimReadiness.EvaluatePeriod(
                period.Year, period.Month, period.Lines.Select(ToReadinessFacts));
            foreach (var pair in period.Lines.Zip(readiness.Lines))
            {
                pair.First.ClientName = pair.Second.ClientName;
                pair.First.ReadinessErrors = pair.Second.Errors;
            }
            return readiness;
        }

        private static ProfessionalClaimLineFacts ToReadinessFacts(ClaimLine line) => new(
            line.Id,
            line.DateOfService,
            line.ProcedureCode,
            line.ProcedureModifier,
            line.Units,
            line.ChargeAmount,
            line.ClientMaineCareId,
            line.RenderingProviderNpi,
            line.DiagnosisCode,
            line.PlaceOfService,
            line.ClaimSnapshotJson);

        private async Task<User> ValidateBillingActorAsync(
            SatiContext context,
            AgencyActor suppliedActor)
        {
            if (!UserPermissionRules.IsSupported(suppliedActor.Permissions) ||
                !UserPermissionRules.HasBillingPermissions(suppliedActor.Permissions))
                throw new UnauthorizedAccessException("Billing permission is required.");

            var actor = await context.Users.SingleOrDefaultAsync(user =>
                       user.Id == suppliedActor.UserId)
                   ?? throw new UnauthorizedAccessException(
                       "The billing actor no longer matches the current user record.");
            if (!AccountSessionRules.IsCurrentSession(actor.IsEnabled, actor.SecurityVersion, suppliedActor.SecurityVersion))
            {
                if (_sessionService?.CurrentUser is User current && current.Id == suppliedActor.UserId &&
                    current.SecurityVersion == suppliedActor.SecurityVersion)
                    _sessionService.Invalidate(current);
                throw new SessionExpiredException(new UnauthorizedAccessException(AccountSessionRules.SessionExpired));
            }
            if (actor.AgencyId != suppliedActor.AgencyId || actor.Permissions != suppliedActor.Permissions)
                throw new UnauthorizedAccessException("The billing actor no longer matches the current user record.");
            if (_sessionService is not null)
            {
                var signedIn = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
                if (signedIn.Id != suppliedActor.UserId || signedIn.SecurityVersion != suppliedActor.SecurityVersion)
                    throw new UnauthorizedAccessException("The billing actor does not match the signed-in account.");
            }
            return actor;
        }

        private async Task<User> ValidateAdminActorAsync(
            SatiContext context,
            AgencyActor suppliedActor,
            CancellationToken cancellationToken)
        {
            if (!UserPermissionRules.IsSupported(suppliedActor.Permissions) ||
                !UserPermissionRules.HasAdminPermissions(suppliedActor.Permissions))
                throw new UnauthorizedAccessException("Administration permission is required.");

            var actor = await context.Users.SingleOrDefaultAsync(
                    user => user.Id == suppliedActor.UserId,
                    cancellationToken)
                ?? throw new UnauthorizedAccessException(
                    "The administrator no longer matches the current user record.");
            if (!AccountSessionRules.IsCurrentSession(
                    actor.IsEnabled, actor.SecurityVersion, suppliedActor.SecurityVersion))
            {
                if (_sessionService?.CurrentUser is User current &&
                    current.Id == suppliedActor.UserId &&
                    current.SecurityVersion == suppliedActor.SecurityVersion)
                    _sessionService.Invalidate(current);
                throw new SessionExpiredException(new UnauthorizedAccessException(
                    AccountSessionRules.SessionExpired));
            }
            if (actor.AgencyId != suppliedActor.AgencyId ||
                actor.Permissions != suppliedActor.Permissions)
                throw new UnauthorizedAccessException(
                    "The administrator no longer matches the current user record.");
            if (_sessionService is not null)
            {
                var signedIn = await LocalTenantAccess.EnsureSessionAsync(
                    context, _sessionService);
                if (signedIn.Id != suppliedActor.UserId ||
                    signedIn.SecurityVersion != suppliedActor.SecurityVersion)
                    throw new UnauthorizedAccessException(
                        "The administrator does not match the signed-in account.");
            }
            return actor;
        }
    }
}
