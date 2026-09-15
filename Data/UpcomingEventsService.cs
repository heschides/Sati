using Sati.Models;

using Sati.Contracts.V1;

namespace Sati.Data
{
    public class UpcomingEventService : IUpcomingEventService
    {
        public List<UpcomingEvent> GenerateEvents(IEnumerable<IEventSource> people, Settings settings, DateTime? asOf = null)
        {
            var today = asOf ?? DateTime.Today;
            var events = new List<UpcomingEvent>();

            foreach (var person in people)
            {
                if (person.EffectiveDate is null)
                    continue;

                GenerateFormEvents(person, today, settings, events);
                GenerateReleaseEvents(person, today, settings, events);
                GenerateScheduledNoteEvents(person, today, events);
            }

            return events.OrderBy(e => e.Date).ToList();
        }

        private static void GenerateFormEvents(IEventSource person, DateTime today,
                    Settings settings, List<UpcomingEvent> events)
        {
            // All form types in one table. The plan currently in force and the next
            // renewal are distinct obligations, so an upcoming renewal can open
            // without replacing the current plan on the profile.
            var formMeta = FormMeta(settings);

            foreach (var (type, daysAfter, label) in formMeta)
            {
                var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
                    person.EffectiveDate!.Value, today);
                foreach (var form in ActionableFormsThroughUpcomingTarget(
                             person, type, today, currentTarget))
                {
                    // IsSatisfiedAsOf, not IsCompliant: a completion dated in the future is
                    // recorded but not yet in force, and the billing gate still treats
                    // that form as outstanding. Asking the same question keeps this list
                    // and the gate from naming different forms.
                    if (form.IsSatisfiedAsOf(today))
                        continue;

                    var dueDate = form.DueDate.Date;
                    var openDate = AvailableDate(form, type, settings);
                    var lateDate = dueDate.AddDays(daysAfter);

                    // Explicit missed cycles are retained work, not expired reminders.
                    // The configurable post-due window still bounds current-cycle noise,
                    // but it must not make an older, unattested annual obligation vanish.
                    var isHistoricalMissedCycle = form.TargetEffectiveDate != default &&
                                                  form.TargetEffectiveDate.Date < currentTarget;
                    if (today < openDate ||
                        (today > lateDate && !isHistoricalMissedCycle))
                        continue;

                    var kind = today > dueDate
                        ? UpcomingEventKind.LateReview
                        : UpcomingEventKind.OpenReview;
                    events.Add(new UpcomingEvent
                    {
                        PersonId = person.Id,
                        ClientName = person.FullName,
                        Title = $"{label} — {person.FullName}",
                        Date = dueDate,
                        Kind = kind,
                        FormType = type,
                        FormId = form.Id > 0 ? form.Id : null,
                        TargetEffectiveDate = form.TargetEffectiveDate == default
                            ? null
                            : form.TargetEffectiveDate.Date,
                        OpenDate = openDate,
                        OpenedDate = form.OpenedDate
                    });
                }
            }
        }

        private static IEnumerable<Form> ActionableFormsThroughUpcomingTarget(
            IEventSource person,
            FormType type,
            DateTime asOf,
            DateTime currentTarget)
        {
            var nextTarget = currentTarget.AddYears(1);
            var returned = new HashSet<Form>();

            // A persisted target is the annual identity. Include every unfinished
            // historical target through the upcoming renewal so missed work remains
            // reachable instead of being silently replaced by today's cycle.
            foreach (var form in person.Forms
                         .Where(form => form.Type == type &&
                                        form.TargetEffectiveDate != default &&
                                        form.TargetEffectiveDate.Date >=
                                            person.EffectiveDate!.Value.Date &&
                                        form.TargetEffectiveDate.Date <= nextTarget)
                         .OrderBy(form => form.TargetEffectiveDate)
                         .ThenBy(form => form.DueDate)
                         .ThenBy(form => form.Id))
            {
                returned.Add(form);
                yield return form;
            }

            // Bounded compatibility for pre-migration rows that have no target.
            // These cannot represent an arbitrary historical cycle safely, so retain
            // only the old current/upcoming selectors and never infer a new identity.
            var current = person.GetCurrentCycleForm(type, asOf);
            if (current is not null && returned.Add(current))
                yield return current;

            var upcoming = Person.FindFormForTargetEffectiveDate(
                person.Forms,
                type,
                nextTarget);
            if (upcoming is not null && returned.Add(upcoming))
                yield return upcoming;
        }

        private static DateTime AvailableDate(Form form, FormType type, Settings settings) =>
            FormDueDateCalculator.ComputeAvailableDateForDueDate(
                type,
                form.DueDate,
                settings);

        private static void GenerateReleaseEvents(
            IEventSource person,
            DateTime today,
            Settings settings,
            List<UpcomingEvent> events)
        {
            var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
                person.EffectiveDate!.Value, today);
            var nextTarget = currentTarget.AddYears(1);

            foreach (var obligation in person.ReleaseComplianceFacts
                         .Where(item => item.TargetEffectiveDate is DateTime target &&
                                        target.Date >= person.EffectiveDate!.Value.Date &&
                                        target.Date <= nextTarget)
                         .Where(item => item.RetiredOn is null || today.Date < item.RetiredOn.Value.Date)
                         .OrderBy(item => item.DueOn)
                         .ThenBy(item => item.StableKey, StringComparer.Ordinal))
            {
                var completedOn = ReleaseAttestationRules.CompletedOn(
                    obligation.StableKey, obligation.Attestations);
                if (completedOn is DateTime completed && completed.Date <= today.Date)
                    continue;

                var formType = ReleaseBillingRules.FormTypeFor(obligation.Category);
                var type = Enum.Parse<FormType>(formType);
                var openDate = obligation.AvailableOn?.Date ??
                               ComplianceScheduleRules.AvailableOn(
                                   formType, obligation.DueOn, ToSchedule(settings));
                var lateDate = obligation.DueOn.Date.AddDays(ReleaseDaysAfterDue(type, settings));
                var isHistoricalMissedCycle = obligation.TargetEffectiveDate!.Value.Date <
                                              currentTarget;
                if (today.Date < openDate ||
                    (today.Date > lateDate && !isHistoricalMissedCycle))
                    continue;

                events.Add(new UpcomingEvent
                {
                    PersonId = person.Id,
                    ClientName = person.FullName,
                    Title = $"{ReleaseLabel(obligation)} — {person.FullName}",
                    Date = obligation.DueOn.Date,
                    Kind = today.Date > obligation.DueOn.Date
                        ? UpcomingEventKind.LateReview
                        : UpcomingEventKind.OpenReview,
                    FormType = type,
                    TargetEffectiveDate = obligation.TargetEffectiveDate.Value.Date,
                    ReleaseObligationId = obligation.ObligationId,
                    OpenDate = openDate
                });
            }
        }

        private static int ReleaseDaysAfterDue(FormType type, Settings settings) => type switch
        {
            FormType.Release_Agency => settings.ReleaseAgencyDaysAfterDue,
            FormType.Release_DHHS => settings.ReleaseDhhsDaysAfterDue,
            FormType.Release_Medical => settings.ReleaseMedicalDaysAfterDue,
            _ => 0
        };

        private static string ReleaseLabel(ReleaseComplianceFact obligation)
        {
            var category = obligation.Category switch
            {
                ReleaseObligationCategory.Agency => "Agency release",
                ReleaseObligationCategory.Medical => "Medical release",
                ReleaseObligationCategory.Dhhs => "DHHS release",
                _ => "Release"
            };
            return string.IsNullOrWhiteSpace(obligation.RecipientDisplayName)
                ? category
                : $"{category} — {obligation.RecipientDisplayName.Trim()}";
        }

        private static ComplianceScheduleSettings ToSchedule(Settings settings) => new(
            settings.ReviewOpenDaysBefore,
            settings.PcpOpenDaysBefore,
            settings.CompAssessmentOpenDaysBefore,
            settings.ReclassificationOpenDaysBefore,
            settings.SafetyPlanOpenDaysBefore,
            settings.PrivacyPracticesOpenDaysBefore,
            settings.ReleaseAgencyOpenDaysBefore,
            settings.ReleaseDhhsOpenDaysBefore,
            settings.ReleaseMedicalOpenDaysBefore,
            settings.PcpDaysBeforeAnniversary,
            settings.CompAssessmentDaysBeforeAnniversary,
            settings.ReclassificationDaysBeforeAnniversary,
            settings.SafetyPlanDaysBeforeAnniversary,
            settings.PrivacyPracticesDaysBeforeAnniversary,
            settings.ReleaseAgencyDaysBeforeAnniversary,
            settings.ReleaseDhhsDaysBeforeAnniversary,
            settings.ReleaseMedicalDaysBeforeAnniversary);

        private static void GenerateScheduledNoteEvents(IEventSource person, DateTime today,
                    List<UpcomingEvent> events)
        {
            var lookahead = today.AddDays(30);

            var scheduledNotes = person.Notes
                .Where(n => n.Status == NoteStatus.Scheduled &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value >= today &&
                            n.EventDate.Value <= lookahead)
                .OrderBy(n => n.EventDate);

            foreach (var note in scheduledNotes)
            {
                var kind = note.NoteType switch
                {
                    NoteType.Contact => UpcomingEventKind.ScheduledContact,
                    NoteType.Phone => UpcomingEventKind.ScheduledPhone,
                    NoteType.Email => UpcomingEventKind.ScheduledEmail,
                    NoteType.Form => UpcomingEventKind.ScheduledForm,
                    NoteType.Reminder => UpcomingEventKind.ScheduledReminder,
                    NoteType.Visit => UpcomingEventKind.ScheduledVisit,
                    _ => UpcomingEventKind.ScheduledOther
                };

                var label = note.NoteType switch
                {
                    NoteType.Contact => $"Contact — {person.FullName}",
                    NoteType.Phone => $"Phone — {person.FullName}",
                    NoteType.Email => $"Email — {person.FullName}",
                    NoteType.Form when note.FormType is FormType formType =>
                        $"{Person.FormDisplayName(formType)} — {person.FullName}",
                    NoteType.Form => $"Form (type not recorded) — {person.FullName}",
                    NoteType.Reminder => $"Reminder — {person.FullName}",
                    NoteType.Visit => $"Visit — {person.FullName}",
                    _ => $"Other — {person.FullName}"
                };

                events.Add(new UpcomingEvent
                {
                    PersonId = person.Id,
                    ClientName = person.FullName,
                    Title = label,
                    Date = note.EventDate!.Value,
                    Kind = kind,
                    FormType = note.NoteType == NoteType.Form ? note.FormType : null
                });
            }
        }

        // One table, two readers. GenerateEvents uses it for the open/late window;
        // NextFormSuggestion uses it for the note panel's follow-up hint. A second
        // copy of these labels would let the two disagree about a form's name.
        private static (FormType Type, int DaysAfter, string Label)[] FormMeta(Settings settings) =>
        [
            (FormType.PCP,                     settings.PcpDaysAfterDue,              "PCP"),
            (FormType.ComprehensiveAssessment, settings.CompAssessmentDaysAfterDue,   "Comp. Assessment"),
            (FormType.Reclassification,        settings.ReclassificationDaysAfterDue, "Reclassification"),
            (FormType.SafetyPlan,              settings.SafetyPlanDaysAfterDue,       "Safety Plan"),
            (FormType.PrivacyPractices,        settings.PrivacyPracticesDaysAfterDue, "Privacy Practices"),
            (FormType.Q1R,                     settings.ReviewDaysAfterDue,           "Q1 Review"),
            (FormType.Q2R,                     settings.ReviewDaysAfterDue,           "Q2 Review"),
            (FormType.Q3R,                     settings.ReviewDaysAfterDue,           "Q3 Review"),
            (FormType.Q4R,                     settings.ReviewDaysAfterDue,           "Q4 Review"),
        ];

        /// <summary>
        /// The client's next outstanding form by due date, ignoring the open/late
        /// window that <see cref="GenerateEvents"/> applies.
        ///
        /// GenerateEvents answers "what is actionable right now", which is correct
        /// for the dashboard but leaves the note panel's follow-up hint blank for
        /// most of every cycle: with the default zero-day review window a quarterly
        /// review is only ever "open" on its exact due date. This answers the
        /// different question the note panel actually asks — "what is coming up next
        /// for this client" — using the same stored form records, the same
        /// GetCurrentCycleForm lookup, and the same IsSatisfiedAsOf test, so it can
        /// never name a form the compliance gate considers already met.
        /// </summary>
        public UpcomingEvent? NextFormSuggestion(IEventSource person, Settings settings, DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;
            if (person.EffectiveDate is null)
                return null;

            UpcomingEvent? next = null;
            foreach (var (type, _, label) in FormMeta(settings))
            {
                var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
                    person.EffectiveDate!.Value, today);
                foreach (var form in ActionableFormsThroughUpcomingTarget(
                             person, type, today, currentTarget))
                {
                    if (form.IsSatisfiedAsOf(today))
                        continue;

                    var dueDate = form.DueDate.Date;
                    var openDate = AvailableDate(form, type, settings);
                    if (next is not null && dueDate >= next.Date)
                        continue;

                    next = new UpcomingEvent
                    {
                        PersonId = person.Id,
                        ClientName = person.FullName,
                        Title = $"{label} — {person.FullName}",
                        Date = dueDate,
                        Kind = today > dueDate
                            ? UpcomingEventKind.LateReview
                            : today >= openDate
                                ? UpcomingEventKind.OpenReview
                                : UpcomingEventKind.UpcomingForm,
                        FormType = type,
                        FormId = form.Id > 0 ? form.Id : null,
                        TargetEffectiveDate = form.TargetEffectiveDate == default
                            ? null
                            : form.TargetEffectiveDate.Date,
                        OpenDate = openDate,
                        OpenedDate = form.OpenedDate
                    };
                }
            }

            return next;
        }
    }
}
