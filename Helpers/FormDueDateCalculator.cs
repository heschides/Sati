using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// Single source of truth for form due-date math. Each Form represents
    /// the document or review FOR its cycle — not prep work for the next cycle.
    ///
    ///   - Annual non-review documents (PCP, ComprehensiveAssessment,
    ///     Reclassification, SafetyPlan, PrivacyPractices, all Releases)
    ///     take effect at cycleStart. Their DueDate is cycleStart.
    ///
    ///   - Quarterly reviews are anchored to cycleStart with hardcoded
    ///     offsets: +90, +180, +270 days. Q4R is cycleEnd.AddDays(-1) —
    ///     the last day of the cycle, before the anniversary. Computed
    ///     this way so it's leap-year correct and stays inside the
    ///     half-open [cycleStart, cycleEnd) range that cycle queries use.
    ///
    /// Prep deadlines (open dates, late tolerance) are not due dates. They
    /// live on Settings (*OpenDaysBefore, *DaysAfterDue) and are derived
    /// relative to DueDate at display time by UpcomingEventService.
    /// </summary>
    public static class FormDueDateCalculator
    {
        public static DateTime Compute(FormType type, DateTime cycleStart, DateTime cycleEnd)
        {
            return type switch
            {
                // Quarterly reviews — fixed offsets from cycleStart, except Q4R
                FormType.Q1R => cycleStart.AddDays(90),
                FormType.Q2R => cycleStart.AddDays(180),
                FormType.Q3R => cycleStart.AddDays(270),
                FormType.Q4R => cycleEnd.AddDays(-1),
                // Annual non-review documents — take effect at cycleStart
                FormType.PCP => cycleStart,
                FormType.ComprehensiveAssessment => cycleStart,
                FormType.Reclassification => cycleStart,
                FormType.SafetyPlan => cycleStart,
                FormType.PrivacyPractices => cycleStart,
                FormType.Release_Agency => cycleStart,
                FormType.Release_DHHS => cycleStart,
                FormType.Release_Medical => cycleStart,

                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unhandled FormType in FormDueDateCalculator.")
            };
        }
    }
}