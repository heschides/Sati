using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public class Settings
    {
        public int Id { get; set; }

        // Abandonment
        public int AbandonedAfterDays { get; set; } = 7;

        // Productivity
        public int ProductivityThreshold { get; set; } = 100;
        public decimal BaseIncentive { get; set; } = 0;
        public decimal PerUnitIncentive { get; set; } = 0;

        // Note templates
        public string VisitTemplate { get; set; } = string.Empty;
        public string ContactTemplate { get; set; } = string.Empty;
        public string DocumentationTemplate { get; set; } = string.Empty;



        // Weekday exclusions
        public bool ExcludeMonday { get; set; } = false;
        public bool ExcludeTuesday { get; set; } = false;
        public bool ExcludeWednesday { get; set; } = false;
        public bool ExcludeThursday { get; set; } = false;
        public bool ExcludeFriday { get; set; } = false;

        // Federal holidays
        public bool ExcludeNewYearsDay { get; set; } = true;
        public bool ExcludeMLKDay { get; set; } = false;
        public bool ExcludePresidentsDay { get; set; } = false;
        public bool ExcludeMemorialDay { get; set; } = true;
        public bool ExcludeJuneteenth { get; set; } = false;
        public bool ExcludeIndependenceDay { get; set; } = true;
        public bool ExcludeLaborDay { get; set; } = true;
        public bool ExcludeIndigenousPeoplesDay { get; set; } = false;
        public bool ExcludeVeteransDay { get; set; } = false;
        public bool ExcludeThanksgiving { get; set; } = true;
        public bool ExcludeDayAfterThanksgiving { get; set; } = true;
        public bool ExcludeChristmas { get; set; } = true;

        // EVENT DATE SETTINGS

        // Reviews (shared across Q1R–Q4R)
        public int ReviewOpenDaysBefore { get; set; }
        public int ReviewDaysAfterDue { get; set; }

        // PCP
        public int PcpOpenDaysBefore { get; set; }
        public int PcpDaysAfterDue { get; set; }

        // Comprehensive Assessment
        public int CompAssessmentOpenDaysBefore { get; set; }
        public int CompAssessmentDaysAfterDue { get; set; }

        // Reclassification
        public int ReclassificationOpenDaysBefore { get; set; }
        public int ReclassificationDaysAfterDue { get; set; }

        // Safety Plan
        public int SafetyPlanOpenDaysBefore { get; set; }
        public int SafetyPlanDaysAfterDue { get; set; }

        // Privacy Practices
        public int PrivacyPracticesOpenDaysBefore { get; set; }
        public int PrivacyPracticesDaysAfterDue { get; set; }

        // Releases
        public int ReleaseAgencyOpenDaysBefore { get; set; }
        public int ReleaseAgencyDaysAfterDue { get; set; }

        public int ReleaseDhhsOpenDaysBefore { get; set; }
        public int ReleaseDhhsDaysAfterDue { get; set; }

        public int ReleaseMedicalOpenDaysBefore { get; set; }
        public int ReleaseMedicalDaysAfterDue { get; set; }
    }
}
