using System;
using System.Collections.Generic;
using System.Linq;

namespace Sati.Helpers
{
    /// <summary>
    /// Single source of truth for the healthcare-system option list: its
    /// invariants (the "Other" floor, de-duplication, ordering) and its
    /// per-state defaults. Both the settings window (which edits the list) and
    /// the client combobox (which consumes it) route through here so the two
    /// can never apply different rules.
    /// </summary>
    public static class HealthcareSystemOptions
    {
        // The permanent floor. Canonical casing lives here; any case variant in
        // input is folded into this single entry.
        public const string Other = "Other";

        // State keys are constants rather than loose strings so call sites can't
        // typo them.
        public const string Maine = "Maine";

        // Per-state defaults, keyed case-insensitively. Maine is the only entry
        // today; the dictionary is the seam so other states drop in later without
        // touching any caller. "Default Maine Options" merges DefaultsByState[Maine]
        // into the user's list.
        //

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultsByState { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [Maine] = new[]
                {
                    "MaineHealth",
                    "Northern Light Health",
                    "Central Maine Healthcare",
                    "MaineGeneral Health",
                    "Martin's Point Health Care",
                    "InterMed",
                    "Penobscot Community Health Care",
                    "St. Joseph Healthcare"
                }
            };

        /// <summary>
        /// Cleans and orders a raw option list: trims, drops blanks, removes
        /// case-insensitive duplicates (first occurrence wins), guarantees a single
        /// "Other" pinned last, and sorts the named systems alphabetically.
        /// </summary>
        public static List<string> Normalize(IEnumerable<string> names)
        {
            // Two different comparers on purpose. OrdinalIgnoreCase governs *identity*
            // — whether two entries are "the same system" — which should be a strict,
            // culture-independent match. CurrentCultureIgnoreCase governs *display
            // order*, which should follow the user's locale so the sort reads
            // naturally. Mixing them up is a subtle, real bug.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var named = new List<string>();

            foreach (var raw in names ?? Enumerable.Empty<string>())
            {
                var name = raw?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                // "Other" is handled as the floor below; skip any case variant here
                // so it never lands among the named systems or appears twice.
                if (string.Equals(name, Other, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (seen.Add(name))
                    named.Add(name);
            }

            named.Sort(StringComparer.CurrentCultureIgnoreCase);
            named.Add(Other);
            return named;
        }

        /// <summary>
        /// Non-destructively merges a state's defaults into an existing list.
        /// Idempotent: running it twice produces the same list, because Normalize
        /// de-duplicates. Unknown state keys merge nothing.
        /// </summary>
        public static List<string> MergeDefaults(IEnumerable<string> existing, string state)
        {
            var defaults = DefaultsByState.TryGetValue(state, out var list)
                ? list
                : Array.Empty<string>();

            return Normalize((existing ?? Enumerable.Empty<string>()).Concat(defaults));
        }
    }

    /// <summary>
    /// The combobox-binding vehicle for a healthcare system. Today it carries only a
    /// name; this is the type the ComboBox binds through (SelectedValuePath="Name") so
    /// that when systems become relational, it gains an Id and the binding flips from
    /// Name to Id without the ItemsSource or template changing. The seam promised on
    /// Person.HealthcareSystemName.
    /// </summary>
    public record HealthcareSystemOption(string Name);
}