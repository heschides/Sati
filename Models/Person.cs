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
            string bio, DateTime birthdate, DateTime? effective, WaiverType waiver)
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

            person.Forms = GenerateFormList(effective.Value);
            return person;
        }

        // -------------------------------------------------------------------------
        // Methods
        // -------------------------------------------------------------------------

        public static List<Form> GenerateFormList(DateTime effective)
        {
            return
            [
                new Form { DueDate = effective.AddDays(90),  Type = FormType.Q1R,                   IsCompliant = true },
                new Form { DueDate = effective.AddDays(180), Type = FormType.Q2R,                   IsCompliant = true },
                new Form { DueDate = effective.AddDays(270), Type = FormType.Q3R,                   IsCompliant = true },
                new Form { DueDate = effective.AddYears(1), Type = FormType.Q4R,                   IsCompliant = true },
                new Form { DueDate = effective.AddYears(1), Type = FormType.PCP,                   IsCompliant = true },
                new Form { DueDate = effective.AddYears(1), Type = FormType.Reclassification,      IsCompliant = true },
                new Form { DueDate = effective.AddYears(1), Type = FormType.ComprehensiveAssessment, IsCompliant = true },
                new Form { DueDate = effective.AddYears(1), Type = FormType.Release_Agency,        IsCompliant = true },
                new Form { DueDate = effective.AddYears(1).AddDays(365), Type = FormType.Release_DHHS,          IsCompliant = true },
                new Form { DueDate = effective.AddYears(1).AddDays(365), Type = FormType.Release_Medical,       IsCompliant = true },
                new Form { DueDate = effective.AddYears(1).AddDays(365), Type = FormType.SafetyPlan,            IsCompliant = true },
                new Form { DueDate = effective.AddYears(1).AddDays(365), Type = FormType.PrivacyPractices,      IsCompliant = true },
            ];
        }

        public Form? GetCurrentCycleForm(FormType type)
        {
            if (EffectiveDate is null)
                return null;

            var effective = EffectiveDate.Value;
            var today = DateTime.Today;
            var yearsElapsed = today.Year - effective.Year;
            if (today < effective.AddYears(yearsElapsed))
                yearsElapsed--;

            var cycleStart = effective.AddYears(yearsElapsed);
            var cycleEnd = effective.AddYears(yearsElapsed + 1);

            var currentCycle = Forms
                .Where(f => f.Type == type &&
                            f.DueDate >= cycleStart &&
                            f.DueDate < cycleEnd)
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
    }
}