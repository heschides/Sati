using Sati.Data;
using Sati.Models;

using Sati.Contracts.V1;

namespace Sati
{
    public class Person : IEventSource
    {
        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public int Id { get; private set; }
        public int UserId { get; private set; }
        public int Revision { get; set; } = 1;
        public User? User { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime BirthDate { get; set; }
        public Gender Gender { get; set; } = Gender.Unknown;
        public string SubjectPronoun => Gender switch
        {
            Gender.Male => "he",
            Gender.Female => "she",
            Gender.NonBinary => "they",
            _ => "they"
        };
        public string ObjectPronoun => Gender switch
        {
            Gender.Male => "him",
            Gender.Female => "her",
            Gender.NonBinary => "them",
            _ => "them"
        };
        public string PossessivePronoun => Gender switch
        {
            Gender.Male => "his",
            Gender.Female => "her",
            Gender.NonBinary => "their",
            _ => "their"
        };
        public string ReflexivePronoun => Gender switch
        {
            Gender.Male => "himself",
            Gender.Female => "herself",
            Gender.NonBinary => "themselves",
            _ => "themselves"
        };
        public DateTime? EffectiveDate { get; set; }
        public string? Bio { get; set; }

        // Per-consumer freeform working journal — indefinitely long, distinct from
        // the date-scoped Scratchpad. Maps to nvarchar(max). MUST be excluded from
        // GetAllPeopleAsync: like Bio and Note.Narrative, an unbounded column on the
        // caseload-load path inflates the memory-grant estimate and feeds the
        // RESOURCE_SEMAPHORE stalls. Loaded and saved on demand via
        // IPersonService.GetJournalAsync / SaveJournalAsync only.
        public string? Journal { get; set; }

        public WaiverType Waiver { get; set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; } = null!;

        // Creation-time classification for wholly synthetic consumers. Only an
        // authenticated Admin may set this at birth; ordinary profile edits cannot
        // change it. The Admin test-data deletion command requires it.
        public bool IsTestData { get; set; }

        // Set once, at creation, by CreatePerson or Rehydrate — the only two writers,
        // same as Id and UserId. Never exposed as an editable field, so there is no
        // path (accidental or otherwise) for an edit to move a record's deletion
        // window. A row that predates this column is backfilled to a fixed sentinel
        // far enough in the past that it is permanently outside any window, rather
        // than a guessed real creation date. See HANDOFF_CLIENT_DELETION_POLICY.md, A2.
        public DateTime CreatedAtUtc { get; private set; }

        // Archive state. Active people appear on caseloads and generate compliance
        // work; the others do not. See HANDOFF_CLIENT_DELETION_POLICY.md's archive
        // semantics — this is a visibility and work-generation change, not a data one.
        public PersonStatus Status { get; set; } = PersonStatus.Active;
        public string? StatusNote { get; set; }
        public DateTime? StatusChangedAtUtc { get; set; }
        public int? StatusChangedByUserId { get; set; }
        public string? MaineCareId { get; set; }
        public string? DiagnosisCode { get; set; }
        public int? PlaceOfService { get; set; }

        // The client's Evergreen ID — Maine's case management system of record.
        // Surfaces on payment/authorization forms as the "EIS #" (the forms
        // predate Evergreen and still use the legacy EIS label). Nullable: not
        // every client has one recorded yet.
        public string? EvergreenId { get; set; }

        // The client's id in the agency's Credible instance, captured when a consumer is
        // imported from a Credible export. It is the dedupe and idempotency key for import:
        // re-importing the same export must report rather than duplicate.
        //
        // Deliberately bounded rather than left to the nvarchar(max) convention that
        // EvergreenId and MaineCareId follow. Dedupe wants a filtered unique index on
        // (AgencyId, CredibleClientId) eventually, and an unbounded column cannot be indexed —
        // that is what forced Form.Type to be narrowed in a later migration. Bounding it now
        // costs nothing and leaves that index a one-step change.
        //
        // Not unique across agencies: two agencies run separate Credible instances whose ids
        // collide numerically and mean different people.
        public string? CredibleClientId { get; set; }

        // -------------------------------------------------------------------------
        // Contact & support details
        // -------------------------------------------------------------------------

        // Active Vocational Rehabilitation case (Dept. of Labor) running alongside
        // Section 17 services. Distinct from the MaineCare-funded employment
        // supports below.
        public bool OpenWithVR { get; set; }
        public string? VrCounselorName { get; set; }
        public string? VrAssistantName { get; set; }

        // HasGuardian governs field visibility only; unchecking it does not null
        // GuardianName, so a lapsed-and-resumed guardianship doesn't destroy data.
        public bool HasGuardian { get; set; }
        public string? GuardianName { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        // Structured claim address. The ordinary Address remains the human-facing mailing/display
        // value; X12 must not try to parse city/state/ZIP back out of free text.
        public string? BillingStreet { get; set; }
        public string? BillingCity { get; set; }
        public string? BillingState { get; set; }
        public string? BillingZip { get; set; }
        public string? PrimaryCareProvider { get; set; }

        // Deliberately denormalized as a name string. The seam for a future
        // relational model is pre-cut: this column never renames (a future
        // HealthcareSystemId/HealthcareSystem nav gets added beside it and
        // backfilled by name match), the ComboBox binds via SelectedValuePath so
        // flipping "Name" → "Id" later touches one attribute, and the option list
        // lives as JSON on Settings so its shape can grow without breaking rows.
        public string? HealthcareSystemName { get; set; }

        // Representative-payee profile information. This is current consumer financial
        // context, not a payment instruction. A future billing notification workflow must
        // reference these fields through its own audited request/approval record rather
        // than treating a profile edit as authorization to release funds.
        public bool CaseManagerIsRepPayee { get; set; }
        public bool CaseManagerIsDhhsRepresentative { get; set; }
        public bool UsesModivcare { get; set; }
        public decimal? RepPayeeMonthlyIncome { get; set; }
        public string? RepPayeeRegularCheckRequestNeeds { get; set; }

        // -------------------------------------------------------------------------
        // Waiver services & employment
        // -------------------------------------------------------------------------

        // One flag per statutory waiver service. Columns rather than a child
        // table: the service list is statute-stable, changing only when the
        // state changes it, and flat flags keep queries and bindings simple.
        public bool HasHomeSupport { get; set; }
        public bool HasSelfDirectedHomeSupport { get; set; }
        public bool HasSharedLiving { get; set; }
        public bool HasCommunitySupport1To1 { get; set; }
        public bool HasCommunitySupportSelfDirected { get; set; }
        public bool HasCommunitySupportDayProgram { get; set; }

        // Meaningful only when HasCommunitySupportDayProgram is true.
        public int DayProgramCount { get; set; } = 1;

        public bool HasEmploymentSpecialist { get; set; }
        public bool HasWorkSupports { get; set; }

        public bool IsEmployed { get; set; }

        // Employed with no employment-related supports from any funding stream
        // (waiver or VR) — the population whose employment parameters the case
        // manager must track directly per state requirement.
        public bool RequiresEmploymentTracking =>
            IsEmployed && !HasEmploymentSpecialist && !HasWorkSupports && !OpenWithVR;

        // Quarterly note-review slots derive from service flags. Self-directed
        // services are exempt from note review and contribute no slots.
        public int HomeNoteSlots =>
            (HasHomeSupport ? 1 : 0) + (HasSharedLiving ? 1 : 0);

        public int CommunityNoteSlots =>
            (HasCommunitySupport1To1 ? 1 : 0) +
            (HasCommunitySupportDayProgram ? DayProgramCount : 0);

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public List<Form> Forms { get; set; } = [];
        public List<ReleaseObligation> ReleaseObligations { get; set; } = [];
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<ReleaseComplianceFact> ReleaseComplianceSnapshots { get; set; } = [];
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<ReleaseProviderLinkFact> ReleaseProviderLinksForCompliance { get; set; } = [];
        IReadOnlyCollection<ReleaseComplianceFact> IEventSource.ReleaseComplianceFacts =>
            // A populated snapshot is the latest authoritative read returned by the
            // release-obligation boundary. Local Production also carries detached EF
            // entities on Person, but those entities do not update when a short-lived
            // context records an attestation, withdrawal, or provider reconciliation.
            // Prefer the refreshed snapshot once one has been published so the profile
            // and billing presentation cannot keep reading that stale detached graph.
            ReleaseComplianceSnapshots.Count != 0
                ? ReleaseComplianceSnapshots
                : ReleaseObligations.Select(item => item.ToComplianceFact()).ToArray();
        public List<Note> Notes { get; set; } = [];
        public List<PersonContact> Contacts { get; set; } = [];

        // Explicit interface implementation: exposes the entity's Notes as the
        // read-only INoteInfo surface IEventSource requires. List<Note> doesn't
        // satisfy IEnumerable<INoteInfo> directly (C# property types must match
        // exactly), so this adapter bridges them. Note already implements INoteInfo.
        IEnumerable<INoteInfo> IEventSource.Notes => Notes;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        protected Person() { }

        // -------------------------------------------------------------------------
        // Factory
        // -------------------------------------------------------------------------

        // Settings is unused in the body but kept on the signature so existing
        // callers (NewClientViewModel) don't break. Remove in cleanup.
        public static Person CreatePerson(int userId, string firstName, string lastName,
                   string bio, DateTime birthdate, DateTime? effective, WaiverType waiver, Settings settings)
        {
            var person = new Person
            {
                UserId = userId,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Bio = bio.Trim(),
                BirthDate = birthdate,
                EffectiveDate = effective,
                Waiver = waiver,
                CreatedAtUtc = DateTime.UtcNow
            };

            if (effective is null)
                return person;

            person.Forms = GenerateFormList(effective.Value, settings);
            return person;
        }

        // Sentinel for filter dropdowns needing an "All Persons" row. Never
        // enters the DB; Id = -1 is a marker, not a key.
        public static Person CreateSentinel(string label)
        {
            return new Person
            {
                Id = -1,
                FirstName = label,
                LastName = string.Empty
            };
        }

        // -------------------------------------------------------------------------
        // Methods
        // -------------------------------------------------------------------------

        // First-cycle generation, for the creation dialog. Generation creates
        // obligations; it never manufactures evidence that the work happened. A
        // case manager may record an actual completion through an attestation before
        // the graph is saved, but absence of an attestation remains absence.
        public static List<Form> GenerateFormList(DateTime effective, Settings settings)
        {
            var targetEffectiveDate = effective.Date;

            return Contracts.V1.PersonSaveRules.FormTypes
                .Select(typeName => Enum.Parse<FormType>(typeName))
                .Select(type => new Form(
                    type,
                    FormDueDateCalculator.Compute(
                        type,
                        targetEffectiveDate,
                        settings),
                    completedOn: null,
                    targetEffectiveDate: targetEffectiveDate))
                .ToList();
        }

        // Returns (cycleStart, cycleEnd) bracketing the cycle containing today.
        // Before the first effective date, returns the first planned cycle so its
        // pre-service obligations can be prepared. Null if EffectiveDate is unset.
        public (DateTime cycleStart, DateTime cycleEnd)? GetCurrentCycleBoundaries(DateTime today)
        {
            if (EffectiveDate is null)
                return null;

            var cycleStart = ResolveCurrentTargetEffectiveDate(EffectiveDate.Value, today);
            var cycleEnd = cycleStart.AddYears(1);
            return (cycleStart, cycleEnd);
        }

        /// <summary>
        /// Resolves the annual effective date whose plan is in force on
        /// <paramref name="asOf"/>. Before a consumer's first effective date, the
        /// first date is returned so pre-service work can still be prepared.
        /// </summary>
        public static DateTime ResolveCurrentTargetEffectiveDate(
            DateTime initialEffectiveDate,
            DateTime asOf) =>
            Contracts.V1.ComplianceScheduleRules.CurrentTargetEffectiveDate(
                initialEffectiveDate, asOf);

        // Which quarter of the current cycle today falls in, 1-4. Quarters are
        // 90-day blocks from cycleStart, matching how Q1R-Q4R due dates are
        // anchored (prevAnniversary + 90/180/270/360) — so "we're in Q3" means
        // Q3R is the review currently in play.
        //
        // The clamp matters: a 365-day cycle divided into 90-day blocks leaves a
        // 5-day tail, and a leap year leaves 6. Without it, the last days before
        // the anniversary would report quarter 5. Those days belong to Q4.
        //
        // Null when EffectiveDate is unset — same contract as the boundaries
        // method it delegates to.
        public int? GetCurrentQuarter(DateTime today)
        {
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return null;

            var elapsed = (today.Date - boundaries.Value.cycleStart.Date).Days;
            return Math.Clamp(elapsed / 90 + 1, 1, 4);
        }

        // Transitional fallback for rows created before TargetEffectiveDate existed.
        // The migration must backfill every real row; this keeps detached legacy DTOs
        // readable during a rolling client/API upgrade without letting due-date math
        // override an explicit identity.
        private static bool LegacyFormBelongsToCycle(
            DateTime dueDate,
            DateTime cycleStart,
            DateTime cycleEnd) => dueDate > cycleStart && dueDate <= cycleEnd;

        // Returns the current-cycle form of the given type, or null if none
        // exists — the caller surfaces that as NoForm rather than borrowing a
        // stale form.
        public Form? GetCurrentCycleForm(FormType type, DateTime? asOf = null)
                    => FindCurrentCycleForm(Forms, EffectiveDate, type, asOf);

        // Static single-source-of-truth for current-cycle form lookup. Extracted so
        // PersonSummary (the blob-free sidebar DTO) can answer GetCurrentCycleForm
        // without a shadow copy of cycle math.
        public static Form? FindCurrentCycleForm(
            List<Form> forms, DateTime? effectiveDate, FormType type, DateTime? asOf = null)
        {
            if (effectiveDate is null)
                return null;

            var today = asOf ?? DateTime.Today;
            var target = ResolveCurrentTargetEffectiveDate(effectiveDate.Value, today);
            var identified = FindFormForTargetEffectiveDate(forms, type, target);
            if (identified is not null)
                return identified;

            // Compatibility only. An explicit row always wins, and migrated data
            // never reaches this branch.
            return forms
                .Where(form =>
                    form.Type == type &&
                    form.TargetEffectiveDate == default &&
                    LegacyFormBelongsToCycle(form.DueDate, target, target.AddYears(1)))
                .OrderByDescending(form => form.DueDate)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds one obligation by its stable annual identity. The due date is
        /// deliberately absent from this lookup: changing a deadline must not create
        /// or select a different annual obligation.
        /// </summary>
        public static Form? FindFormForTargetEffectiveDate(
            IEnumerable<Form> forms,
            FormType type,
            DateTime targetEffectiveDate) =>
            forms
                .Where(form =>
                    form.Type == type &&
                    form.TargetEffectiveDate.Date == targetEffectiveDate.Date)
                .OrderByDescending(form => form.DueDate)
                .FirstOrDefault();

        public FormComplianceStatus GetComplianceStatus(FormType type, DateTime referenceDate, Settings settings)
        {
            var form = GetCurrentCycleForm(type, referenceDate);

            if (form is null)
                return FormComplianceStatus.NoForm;

            // IsSatisfiedAsOf, not IsCompliant. A completion date that has not arrived
            // is recorded but not in force; reporting it Compliant here would put this
            // status ahead of the billing gate, which still treats the form as
            // outstanding until the date arrives.
            if (form.IsSatisfiedAsOf(referenceDate))
            {
                return form.CompletedDate!.Value > form.DueDate
                    ? FormComplianceStatus.CompliantLate
                    : FormComplianceStatus.CompliantOnTime;
            }

            var openDaysBefore = GetOpenDaysBefore(type, settings);
            var openDate = form.DueDate.AddDays(-openDaysBefore);

            if (referenceDate < openDate)
                return FormComplianceStatus.NotYetDue;

            if (referenceDate <= form.DueDate)
                return FormComplianceStatus.InWindow;

            return FormComplianceStatus.Overdue;
        }

        public static int GetOpenDaysBefore(FormType type, Settings settings) =>
            FormDueDateCalculator.GetOpenDaysBeforeDue(type, settings);

        // Ensures forms exist for every annual effective date through the next
        // renewal. Every generated row is outstanding. Only an explicit attestation
        // can supply a completion date.
        //
        // This is the only thing that generates forms for an ongoing caseload, so if
        // it does not run, clients silently stop having compliance records once their
        // pre-created cycles run out. It was gated off between 57af6fa and the unique
        // index because it races: the membership check below reads this person's own
        // Forms, and two callers could both pass it and both insert. The index now
        // decides that race in the database, and PersonService discards the losing
        // insert and re-reads, so the guard is no longer needed.
        //
        public bool EnsureCurrentCycleForms(DateTime today, Settings settings)
        {
            if (EffectiveDate is null)
                return false;

            var added = false;

            // Cycle 0 starts on the effective date; cycle N starts N years later.
            // Generate every cycle from admission through the one after the current
            // one — not just the current-and-next pair this used to do. A backdated
            // admission left the years in between with no forms at all, and a form
            // that was never created cannot be enforced: BillingComplianceGate has no
            // row to fail, so an entire year silently carried no compliance
            // requirements. Absent is not the same as satisfied, and generating the
            // row is what makes the difference visible.
            //
            // Closed cycles are generated outstanding, so a real historical gap
            // surfaces rather than being replaced by invented completion evidence.
            foreach (var targetEffectiveDate in
                     Contracts.V1.ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                         EffectiveDate.Value, today, MaxGeneratedCycles))
            {
                added |= AddMissingFormsForTargetEffectiveDate(targetEffectiveDate, settings);
            }

            return added;
        }

        // This is a corruption guard, not a rolling window. Every cycle is generated
        // or the operation fails explicitly; older obligations are never dropped.
        private const int MaxGeneratedCycles = 150;

        // Adds only the candidates this person does not already have, keyed by
        // (type, target effective date). A due-date policy change adjusts an existing
        // obligation; it must not create a second obligation for the same year.
        //
        // Callers hold a freshly generated form list, whose members all carry Id == 0.
        // Assigning such a list over Forms looks like replacement but is not: saves go
        // through context.People.Update on a detached graph, which marks every Id == 0
        // child Added while the stored rows — absent from the graph — survive. That is
        // how a "replace the forms" call becomes a second full set, and it is the same
        // duplicate shape FormDuplicateRepair exists to clean up.
        //
        // Existing rows always win. A generated form knows nothing that should
        // overwrite a real completion date.
        public int AddMissingForms(IEnumerable<Form> candidates)
        {
            var present = Forms
                .Where(form => form.TargetEffectiveDate != default)
                .Select(form => (form.Type, form.TargetEffectiveDate.Date))
                .ToHashSet();
            var legacyDueDates = Forms
                .Where(form => form.TargetEffectiveDate == default)
                .Select(form => (form.Type, form.DueDate.Date))
                .ToHashSet();
            var added = 0;

            foreach (var candidate in candidates)
            {
                var identity = (candidate.Type, candidate.TargetEffectiveDate.Date);
                if (present.Contains(identity) ||
                    legacyDueDates.Contains((candidate.Type, candidate.DueDate.Date)))
                    continue;

                present.Add(identity);
                candidate.PersonId = Id;
                Forms.Add(candidate);
                added++;
            }

            return added;
        }

        // Idempotent: only adds forms missing for one target effective date.
        private bool AddMissingFormsForTargetEffectiveDate(
            DateTime targetEffectiveDate,
            Settings settings)
        {
            var target = targetEffectiveDate.Date;
            var presentForTarget = Forms
                .Where(form => form.TargetEffectiveDate.Date == target)
                .Select(form => form.Type)
                .ToHashSet();

            var added = false;

            foreach (var typeName in Contracts.V1.PersonSaveRules.FormTypes)
            {
                var type = Enum.Parse<FormType>(typeName);
                if (presentForTarget.Contains(type))
                    continue;

                var dueDate = FormDueDateCalculator.Compute(
                    type,
                    target,
                    settings);

                // A detached legacy row can arrive during a rolling upgrade. Match
                // it only by the former identity for compatibility; migrated rows
                // always use TargetEffectiveDate above.
                if (Forms.Any(form =>
                    form.TargetEffectiveDate == default &&
                    form.Type == type &&
                    form.DueDate.Date == dueDate.Date))
                    continue;

                Forms.Add(new Form(
                    type,
                    dueDate,
                    completedOn: null,
                    targetEffectiveDate: target)
                {
                    PersonId = Id
                });
                added = true;
            }

            return added;
        }

        // Returns whether the billing compliance gate passes, and if not, every
        // reason it failed. One pass produces both, so they can't drift.
        public (bool Passed, IReadOnlyList<string> Reasons) EvaluateComplianceGate(
            DateTime today,
            FormType? beingCompleted = null,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements,
            int pcpOpenDaysBefore = 90)
        {
            // Kept in the public signature for source compatibility with callers
            // that also use the setting for UI availability. Billing itself uses
            // the fixed ninety-day PCP-opening rule.
            _ = pcpOpenDaysBefore;
            var obligations = BillingComplianceSnapshots(
                today,
                new Contracts.V1.ComplianceScheduleSettings());
            var result = Contracts.V1.BillingComplianceGate.Evaluate(
                EffectiveDate,
                obligations,
                today,
                beingCompleted?.ToString(),
                requirements);
            return (result.Passed, result.Reasons);
        }

        // Date-keyed historical billing window. It delegates to the same shared
        // requirement mapping as the current-state gate, so enabling or disabling
        // a document affects both decisions consistently. A note ON the due date
        // bills; a note ON or after completion bills. Only the gap between blocks.
        public static bool IsBillingWindowBlocked(
            FormType formType,
            DateTime dueDate,
            DateTime? completedDate,
            DateTime serviceDate,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements) =>
            Contracts.V1.BillingComplianceGate.IsBillingWindowBlocked(
                formType.ToString(), dueDate, completedDate, serviceDate, requirements);

        // Network hydration seam for the HTTP-backed Demo client. Only identity is
        // set here; CloudContractMapper applies the safe DTO fields afterward.
        // This never accepts password, tenant, or persistence-only material.
        //
        // createdAtUtc defaults to the CLR default (0001-01-01), not DateTime.UtcNow:
        // this is a bare identity stub, and an unspecified creation date must read as
        // permanently outside the deletion window, not as "just created."
        public static Person Rehydrate(int id, int userId, DateTime createdAtUtc = default) => new()
        {
            Id = id,
            UserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        /// <summary>
        /// Moves this consumer to another case manager's caseload.
        ///
        /// <para>
        /// A named operation rather than an open setter on <see cref="UserId"/>, which stays
        /// private precisely so a consumer cannot be reassigned by an ordinary property
        /// assignment somewhere in a view model. Changing who holds a clinical record is an
        /// authorization decision, and it should be as hard to do by accident as it is to
        /// find in a diff.
        /// </para>
        ///
        /// <para>
        /// This enforces nothing about <i>who</i> may perform the move — that belongs to
        /// <c>Sati.Contracts.V1.CaseloadTransferRules</c>, which both the API and the
        /// desktop-local service consult before calling this. The entity's job is only to make
        /// the mutation deliberate.
        /// </para>
        ///
        /// <para>
        /// It deliberately does <b>not</b> touch <see cref="Revision"/>. <c>userId</c> is a
        /// tracked lifecycle field, so <c>PersonLifecycleLedger.RecordChanged</c> already sees
        /// the move, writes the version row, and bumps the revision. Incrementing here as well
        /// would advance it twice for one change and hand every other open copy of the record a
        /// stale token for a transfer that happened once.
        /// </para>
        /// </summary>
        public void TransferTo(int userId)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(userId, 0);
            UserId = userId;
        }

        public IReadOnlyList<string> EvaluateBillingWindow(
            DateTime noteDate,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements,
            Contracts.V1.ComplianceScheduleSettings? schedule = null) =>
            EvaluateBillingWindowDetailed(noteDate, requirements, schedule).Reasons;

        public Contracts.V1.BillingComplianceResult EvaluateBillingWindowDetailed(
            DateTime noteDate,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements,
            Contracts.V1.ComplianceScheduleSettings? schedule = null) =>
            Contracts.V1.BillingComplianceGate.EvaluateBillingWindowDetailed(
                BillingComplianceSnapshots(
                    noteDate,
                    schedule ?? new Contracts.V1.ComplianceScheduleSettings()),
                noteDate,
                requirements);

        private IReadOnlyList<Contracts.V1.ComplianceFormSnapshot> BillingComplianceSnapshots(
            DateTime asOfDate,
            Contracts.V1.ComplianceScheduleSettings schedule)
        {
            var releaseFacts = Contracts.V1.ExpectedBillingComplianceObligations
                .IncludeMissingReleases(
                    EffectiveDate,
                    ((IEventSource)this).ReleaseComplianceFacts,
                    asOfDate,
                    ReleaseProviderLinksForCompliance);
            // The always-present DHHS obligation marks a reconciled annual cycle. Once that
            // row exists, the old three fixed release forms for the same cycle must not create
            // a second, contradictory gate beside the recipient-specific obligations.
            var reconciledReleaseCycles = releaseFacts
                .Select(item => item.TargetEffectiveDate)
                .Where(item => item is not null)
                .Select(item => item!.Value.Date)
                .ToHashSet();
            var formSnapshots = Forms
                .Where(form => !IsLegacyReleaseForm(form.Type) ||
                               !reconciledReleaseCycles.Contains(
                                   (form.TargetEffectiveDate == default
                                       ? form.DueDate
                                       : form.TargetEffectiveDate).Date))
                .Select(form => new Contracts.V1.ComplianceFormSnapshot(
                    form.Type.ToString(),
                    form.DueDate,
                    form.CompletedDate,
                    form.OpenedDate,
                    form.Id > 0 ? $"form:{form.Id}" : null,
                    TargetEffectiveDate: form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate));
            var withExpectedForms = Contracts.V1.ExpectedBillingComplianceObligations
                .IncludeMissingForms(
                    EffectiveDate,
                    formSnapshots,
                    asOfDate,
                    schedule);
            var withOpening = Contracts.V1.BillingComplianceGate
                .IncludePcpOpeningObligations(withExpectedForms);
            return withOpening.Concat(
                    Contracts.V1.ReleaseBillingRules.BuildComplianceSnapshots(
                        releaseFacts,
                        asOfDate))
                .ToArray();
        }

        private static bool IsLegacyReleaseForm(FormType type) => type is
            FormType.Release_Agency or FormType.Release_DHHS or FormType.Release_Medical;

        public static string FormDisplayName(FormType type) => type switch
        {
            FormType.PCP => "PCP",
            FormType.ComprehensiveAssessment => "Comprehensive Assessment",
            FormType.Reclassification => "Reclassification",
            FormType.SafetyPlan => "Safety Plan",
            FormType.PrivacyPractices => "Privacy Practices",
            FormType.Release_Agency => "Agency Release",
            FormType.Release_DHHS => "DHHS Release",
            FormType.Release_Medical => "Medical Release",
            FormType.Q1R => "Q1 Review",
            FormType.Q2R => "Q2 Review",
            FormType.Q3R => "Q3 Review",
            FormType.Q4R => "Q4 Review",
            _ => type.ToString()
        };
    }
}
