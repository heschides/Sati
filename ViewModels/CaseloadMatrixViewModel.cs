using System.Collections.ObjectModel;
using Sati.Models;

namespace Sati.ViewModels
{
    /// <summary>
    /// View-model for the read-only Caseload Matrix tab — a date-aware
    /// compliance grid showing every client's form status at a glance.
    ///
    /// This is the transitional Evergreen-compatibility shim flagged in
    /// AGENDA: it replicates Evergreen's display so colleagues adopting Sati
    /// have a familiar view. It's intentionally isolated from the rest of the
    /// app so removing it later is a single-file delete.
    ///
    /// Architecture: Rebuilds the row collection wholesale from a source list
    /// of People. Cells compute their own state in their constructors via
    /// FormCellViewModel — no two-way binding, no per-cell change tracking.
    /// When data changes upstream, call Rebuild to refresh.
    /// </summary>
    public class CaseloadMatrixViewModel
    {
        public ObservableCollection<MatrixRowViewModel> Rows { get; } = [];

        public int PeopleCount => Rows.Count;

        // Builds rows from a snapshot of People. Caller passes today's date
        // explicitly so tests can supply any reference date, and so a single
        // load uses one consistent "today" across all rows even if it crosses
        // midnight mid-build.
        public void Rebuild(IEnumerable<Person> people, DateTime today)
        {
            Rows.Clear();
            foreach (var person in people)
                Rows.Add(new MatrixRowViewModel(person, today));
        }
    }
}