using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// Single source of truth for form due-date math. Each Form is identified by
    /// the annual effective date it belongs to. Its due date is derived from that
    /// identity and must never be used to infer the cycle in reverse.
    ///
    /// Two families share the same target effective date:
    ///
    ///   - Quarterly reviews count FORWARD by exactly +90/+180/+270/+360
    ///     calendar days.
    ///
    ///   - Annual documents (PCP, ComprehensiveAssessment, Reclassification,
    ///     SafetyPlan, PrivacyPractices, all Releases) count BACKWARD from
    ///     their own target effective date. A form set to 0 is due ON that
    ///     date. Each form
    ///     reads its OWN setting — nothing is hardcoded — so a different
    ///     agency can retune any deadline from Settings without code changes.
    ///
    /// Prep deadlines (open dates, late tolerance) are not due dates. They
    /// live on Settings (*OpenDaysBefore, *DaysAfterDue) and are derived
    /// relative to DueDate at display time by UpcomingEventService.
    /// </summary>
    public static class FormDueDateCalculator
    {
        public static DateTime Compute(
            FormType type,
            DateTime targetEffectiveDate,
            Settings settings)
        {
            return Contracts.V1.ComplianceScheduleRules.DueDate(
                type.ToString(), targetEffectiveDate, ToSchedule(settings));
        }

        /// <summary>
        /// Calculates the first date on which work may be recorded for an
        /// obligation. Open-window settings are measured backward from that form's
        /// due date: with the confirmed defaults, for example, a CA due 90 days
        /// before the target and open 30 days before due is available 120 days
        /// before the target.
        /// </summary>
        public static DateTime ComputeAvailableDate(
            FormType type,
            DateTime targetEffectiveDate,
            Settings settings) =>
            ComputeAvailableDateForDueDate(
                type,
                Compute(type, targetEffectiveDate, settings),
                settings);

        /// <summary>
        /// Calculates availability from a stored deadline. This overload is used by
        /// read paths so a deliberately adjusted due date and its open window cannot
        /// disagree. New obligations should derive that deadline from the target
        /// effective date with <see cref="Compute"/> first.
        /// </summary>
        public static DateTime ComputeAvailableDateForDueDate(
            FormType type,
            DateTime dueDate,
            Settings settings) =>
            Contracts.V1.ComplianceScheduleRules.AvailableOn(
                type.ToString(), dueDate, ToSchedule(settings));

        public static int GetOpenDaysBeforeDue(FormType type, Settings settings) =>
            Contracts.V1.ComplianceScheduleRules.OpenDaysBefore(
                type.ToString(), ToSchedule(settings));

        public static Contracts.V1.ComplianceScheduleSettings ToSchedule(Settings settings) => new(
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

        // Compatibility seam for callers compiled around the former cycle-start /
        // cycle-end API. The first date is now the explicit target effective date;
        // the inferred end can no longer decide an obligation's identity.
        public static DateTime Compute(
            FormType type,
            DateTime targetEffectiveDate,
            DateTime ignoredCycleEnd,
            Settings settings)
        {
            _ = ignoredCycleEnd;
            return Compute(type, targetEffectiveDate, settings);
        }
    }
}
