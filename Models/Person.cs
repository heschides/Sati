using Sati.Data;
using Sati.Models;

namespace Sati
{
    public class Person
    {
        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public int Id { get; private set; }
        public int UserId { get; private set; }
        public User? User { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string? Bio { get; set; }
        public WaiverType Waiver { get; set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; } = null!;
        public string? MaineCareId { get; set; }
        public string? DiagnosisCode { get; set; }
        public int? PlaceOfService { get; set; }

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public List<Form> Forms { get; set; } = [];
        public List<Note> Notes { get; set; } = [];

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        protected Person() { }

        // -------------------------------------------------------------------------
        // Factory
        // -------------------------------------------------------------------------

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
                Waiver = waiver
            };

            if (effective is null)
                return person;

            person.Forms = GenerateFormList(effective.Value, settings);
            return person;
        }

        // Sentinel factory for filter dropdowns that need an "All Persons" row.
        // Bypasses GenerateFormList deliberately — this object never enters the DB
        // and has no forms, notes, or real identity. Id = -1 is a marker, not a key.
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

        public static List<Form> GenerateFormList(DateTime effective, Settings settings)
        {
            // First-cycle generation: cycleStart is the effective date itself,
            // cycleEnd is one year later. All due dates flow through the
            // calculator so this method never carries shadow math.
            var cycleStart = effective;
            var cycleEnd = effective.AddYears(1);

            return Enum.GetValues<FormType>()
                .Select(type => new Form
                {
                    Type = type,
                    DueDate = FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                    IsCompliant = true
                })
                .ToList();
        }



        // Returns (cycleStart, cycleEnd) bracketing the cycle that contains `today`.
        // cycleStart is the most recent anniversary on or before today; cycleEnd
        // is the next anniversary. Returns null if EffectiveDate is unset.
        //
        // Why a helper instead of duplicating the math: GetCurrentCycleForm and
        // EnsureCurrentCycleForms both need it, and shadow copies drift.
        public (DateTime cycleStart, DateTime cycleEnd)? GetCurrentCycleBoundaries(DateTime today)
        {
            if (EffectiveDate is null)
                return null;

            var effective = EffectiveDate.Value;
            var yearsElapsed = today.Year - effective.Year;
            if (today < effective.AddYears(yearsElapsed))
                yearsElapsed--;

            var cycleStart = effective.AddYears(yearsElapsed);
            var cycleEnd = effective.AddYears(yearsElapsed + 1);
            return (cycleStart, cycleEnd);
        }

        public Form? GetCurrentCycleForm(FormType type)
        {
            var boundaries = GetCurrentCycleBoundaries(DateTime.Today);
            if (boundaries is null)
                return null;

            var (cycleStart, cycleEnd) = boundaries.Value;

            var currentCycle = Forms
                .Where(f => f.Type == type &&
                            f.DueDate >= cycleStart &&
                            f.DueDate <= cycleEnd)
                .OrderByDescending(f => f.DueDate)
                .FirstOrDefault();

            if (currentCycle is not null)
                return currentCycle;

            return Forms
                .Where(f => f.Type == type)
                .OrderByDescending(f => f.DueDate)
                .FirstOrDefault();
        }

        public FormComplianceStatus GetComplianceStatus(FormType type, DateTime referenceDate, Settings settings)
        {
            var form = GetCurrentCycleForm(type);

            if (form is null)
                return FormComplianceStatus.NoForm;

            if (form.IsCompliant)
            {
                return form.CompletedDate.HasValue && form.CompletedDate.Value > form.DueDate
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

        private static int GetOpenDaysBefore(FormType type, Settings settings) => type switch
        {
            FormType.Q1R or FormType.Q2R or FormType.Q3R or FormType.Q4R
                => settings.ReviewOpenDaysBefore,
            FormType.PCP
                => settings.PcpOpenDaysBefore,
            FormType.ComprehensiveAssessment
                => settings.CompAssessmentOpenDaysBefore,
            FormType.Reclassification
                => settings.ReclassificationOpenDaysBefore,
            FormType.SafetyPlan
                => settings.SafetyPlanOpenDaysBefore,
            FormType.PrivacyPractices
                => settings.PrivacyPracticesOpenDaysBefore,
            FormType.Release_Agency
                => settings.ReleaseAgencyOpenDaysBefore,
            FormType.Release_DHHS
                => settings.ReleaseDhhsOpenDaysBefore,
            FormType.Release_Medical
                => settings.ReleaseMedicalOpenDaysBefore,
            _ => 30
        };

        // Generates form records for the current cycle if they don't already exist.
        // Called by PersonService.GetAllPeopleAsync on every load — idempotent, so
        // calling it when forms already exist is a no-op.
        //
        // Returns true if any forms were added (caller should save). Returns false
        // if nothing changed.
        //
        // Why this exists: without it, a client past their first anniversary keeps
        // showing only first-cycle forms. The matrix would show those forms as
        // "complete" indefinitely — the false-green problem.
        public bool EnsureCurrentCycleForms(DateTime today, Settings settings)
        {
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return false;

            var (cycleStart, cycleEnd) = boundaries.Value;
            var added = false;

            foreach (var type in Enum.GetValues<FormType>())
            {
                var existsForCycle = Forms.Any(f =>
                    f.Type == type &&
                    f.DueDate >= cycleStart &&
                    f.DueDate <= cycleEnd);

                if (existsForCycle)
                    continue;

                Forms.Add(new Form
                {
                    Type = type,
                    DueDate = FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                    IsCompliant = false,    // see comment in GenerateFormList re: opposite default
                    PersonId = Id
                });
                added = true;
            }

            return added;
        }
    }
}