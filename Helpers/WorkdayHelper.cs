using Sati.Models;

namespace Sati.Helpers
{
    /// <summary>
    /// Stateless workday exclusion logic shared by IncentiveService and
    /// SchedulerViewModel. Single source of truth for weekday and holiday
    /// exclusion rules — parallel to FormDueDateCalculator.
    /// </summary>
    public static class WorkdayHelper
    {
        /// <summary>
        /// Returns true if the given weekday should be excluded from
        /// productivity requirements based on Settings. Assumes the caller
        /// has already filtered out Saturday and Sunday.
        /// </summary>
        public static bool IsAlwaysExcludedWorkday(DateTime date, Settings settings)
        {
            var dow = date.DayOfWeek;

            if (settings.ExcludeMonday && dow == DayOfWeek.Monday) return true;
            if (settings.ExcludeTuesday && dow == DayOfWeek.Tuesday) return true;
            if (settings.ExcludeWednesday && dow == DayOfWeek.Wednesday) return true;
            if (settings.ExcludeThursday && dow == DayOfWeek.Thursday) return true;
            if (settings.ExcludeFriday && dow == DayOfWeek.Friday) return true;

            return IsExcludedHoliday(date, settings);
        }

        private static bool IsExcludedHoliday(DateTime date, Settings settings)
        {
            var m = date.Month;
            var d = date.Day;
            var dow = date.DayOfWeek;

            if (settings.ExcludeNewYearsDay && m == 1 && d == 1) return true;
            if (settings.ExcludeMLKDay && m == 1 && dow == DayOfWeek.Monday && IsNthWeekday(date, 3)) return true;
            if (settings.ExcludePresidentsDay && m == 2 && dow == DayOfWeek.Monday && IsNthWeekday(date, 3)) return true;
            if (settings.ExcludeMemorialDay && m == 5 && dow == DayOfWeek.Monday && IsLastMonday(date)) return true;
            if (settings.ExcludeJuneteenth && m == 6 && d == 19) return true;
            if (settings.ExcludeIndependenceDay && m == 7 && d == 4) return true;
            if (settings.ExcludeLaborDay && m == 9 && dow == DayOfWeek.Monday && IsNthWeekday(date, 1)) return true;
            if (settings.ExcludeIndigenousPeoplesDay && m == 10 && dow == DayOfWeek.Monday && IsNthWeekday(date, 2)) return true;
            if (settings.ExcludeVeteransDay && m == 11 && d == 11) return true;
            if (settings.ExcludeThanksgiving && m == 11 && dow == DayOfWeek.Thursday && IsNthWeekday(date, 4)) return true;
            if (settings.ExcludeDayAfterThanksgiving && m == 11 && dow == DayOfWeek.Friday
                && IsNthWeekday(date.AddDays(-1), 4)) return true;
            if (settings.ExcludeChristmas && m == 12 && d == 25) return true;

            return false;
        }

        private static bool IsNthWeekday(DateTime date, int n) => (date.Day - 1) / 7 + 1 == n;
        private static bool IsLastMonday(DateTime date) => date.AddDays(7).Month != date.Month;
    }
}