using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// Single source of truth for form due-date math. Both the Person model
    /// (when generating new forms) and PersonService (when backfilling existing
    /// forms against current settings) call this — never duplicate the math.
    ///
    /// Two anchor strategies live here:
    ///
    ///   - 90-day reviews are anchored to the *previous* anniversary (cycleStart)
    ///     with hardcoded offsets: +90, +180, +270, +365 days. The offsets are
    ///     literally what the form names mean (Q1 review = 90 days into the cycle),
    ///     so they're not configurable.
    ///
    ///   - Annual forms are anchored to the *next* anniversary (cycleEnd) with
    ///     a configurable lead time: anniversary − N days, where N comes from
    ///     Settings.*DaysBeforeAnniversary.
    /// </summary>
    public static class FormDueDateCalculator
    {
        public static DateTime Compute(FormType type, DateTime cycleStart, DateTime cycleEnd, Settings settings)
        {
            return type switch
            {
                // 90-day reviews — fixed offsets from cycle start.
                FormType.Q1R => cycleStart.AddDays(90),
                FormType.Q2R => cycleStart.AddDays(180),
                FormType.Q3R => cycleStart.AddDays(270),
                FormType.Q4R => cycleStart.AddDays(365),

                // Annual forms — settings-driven offsets back from anniversary.
                FormType.PCP => cycleEnd.AddDays(-settings.PcpDaysBeforeAnniversary),
                FormType.ComprehensiveAssessment => cycleEnd.AddDays(-settings.CompAssessmentDaysBeforeAnniversary),
                FormType.Reclassification => cycleEnd.AddDays(-settings.ReclassificationDaysBeforeAnniversary),
                FormType.SafetyPlan => cycleEnd.AddDays(-settings.SafetyPlanDaysBeforeAnniversary),
                FormType.PrivacyPractices => cycleEnd.AddDays(-settings.PrivacyPracticesDaysBeforeAnniversary),
                FormType.Release_Agency => cycleEnd.AddDays(-settings.ReleaseAgencyDaysBeforeAnniversary),
                FormType.Release_DHHS => cycleEnd.AddDays(-settings.ReleaseDhhsDaysBeforeAnniversary),
                FormType.Release_Medical => cycleEnd.AddDays(-settings.ReleaseMedicalDaysBeforeAnniversary),

                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unhandled FormType in FormDueDateCalculator.")
            };
        }
    }
}