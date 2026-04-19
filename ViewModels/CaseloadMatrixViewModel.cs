using Sati.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Sati.ViewModels
{
    public class CaseloadMatrixViewModel
    {
        private readonly Settings _settings;
        private readonly ObservableCollection<Person> _people;

        public IEnumerable<MatrixRowViewModel> Rows =>
            _people.Select(p => new MatrixRowViewModel(p, this));

        public int PeopleCount => _people.Count;

        public CaseloadMatrixViewModel(ObservableCollection<Person> people, Settings settings)
        {
            _people = people;
            _settings = settings;
        }

        public FormComplianceStatus GetStatus(Person person, FormType type)
            => person.GetComplianceStatus(type, DateTime.Today, _settings);

        public Brush StatusToBrush(Person person, FormType type) =>
            GetStatus(person, type) switch
            {
                FormComplianceStatus.CompliantOnTime => new SolidColorBrush(Color.FromRgb(0xC2, 0xDF, 0xB3)),
                FormComplianceStatus.CompliantLate => new SolidColorBrush(Color.FromRgb(0xF5, 0xDF, 0xB0)),
                FormComplianceStatus.InWindow => new SolidColorBrush(Color.FromRgb(0xF5, 0xDF, 0xB0)),
                FormComplianceStatus.Overdue => new SolidColorBrush(Color.FromRgb(0xEE, 0xB0, 0x9A)),
                FormComplianceStatus.NotYetDue => new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xD3)),
                FormComplianceStatus.NoForm => new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x82)),
                _ => Brushes.Transparent
            };
    }

    public class MatrixRowViewModel
    {
        private readonly Person _person;
        private readonly CaseloadMatrixViewModel _matrix;

        public MatrixRowViewModel(Person person, CaseloadMatrixViewModel matrix)
        {
            _person = person;
            _matrix = matrix;
        }

        public string FullName => _person.FullName;

        public Brush CompAssessmentBrush => _matrix.StatusToBrush(_person, FormType.ComprehensiveAssessment);
        public Brush PcpBrush => _matrix.StatusToBrush(_person, FormType.PCP);
        public Brush Q1RBrush => _matrix.StatusToBrush(_person, FormType.Q1R);
        public Brush Q2RBrush => _matrix.StatusToBrush(_person, FormType.Q2R);
        public Brush Q3RBrush => _matrix.StatusToBrush(_person, FormType.Q3R);
        public Brush Q4RBrush => _matrix.StatusToBrush(_person, FormType.Q4R);
        public Brush ReclassBrush => _matrix.StatusToBrush(_person, FormType.Reclassification);
        public Brush SafetyPlanBrush => _matrix.StatusToBrush(_person, FormType.SafetyPlan);
        public Brush PrivacyBrush => _matrix.StatusToBrush(_person, FormType.PrivacyPractices);
        public Brush ReleaseAgencyBrush => _matrix.StatusToBrush(_person, FormType.Release_Agency);
        public Brush ReleaseDhhsBrush => _matrix.StatusToBrush(_person, FormType.Release_DHHS);
        public Brush ReleaseMedicalBrush => _matrix.StatusToBrush(_person, FormType.Release_Medical);
    

    public string CompAssessmentLabel => GetLabel(_person, FormType.ComprehensiveAssessment);
        public string PcpLabel => GetLabel(_person, FormType.PCP);
        public string Q1RLabel => GetLabel(_person, FormType.Q1R);
        public string Q2RLabel => GetLabel(_person, FormType.Q2R);
        public string Q3RLabel => GetLabel(_person, FormType.Q3R);
        public string Q4RLabel => GetLabel(_person, FormType.Q4R);
        public string ReclassLabel => GetLabel(_person, FormType.Reclassification);
        public string SafetyPlanLabel => GetLabel(_person, FormType.SafetyPlan);
        public string PrivacyLabel => GetLabel(_person, FormType.PrivacyPractices);
        public string ReleaseAgencyLabel => GetLabel(_person, FormType.Release_Agency);
        public string ReleaseDhhsLabel => GetLabel(_person, FormType.Release_DHHS);
        public string ReleaseMedicalLabel => GetLabel(_person, FormType.Release_Medical);

        private static string GetLabel(Person person, FormType type)
        {
            var form = person.GetCurrentCycleForm(type);
            if (form is null)
                return "No form";

            var due = $"Due: {form.DueDate:MM/dd/yy}";
            if (form.IsCompliant && form.CompletedDate.HasValue)
                return $"{due}\nDone: {form.CompletedDate.Value:MM/dd/yy}";

            return due;
        } 
    }
}