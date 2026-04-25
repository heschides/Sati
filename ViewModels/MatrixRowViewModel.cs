using Sati.Models;

namespace Sati.ViewModels
{
    /// <summary>
    /// One row of the caseload matrix: the person, plus a FormCellViewModel
    /// for each form type. Twelve named cell properties (rather than a
    /// dictionary or list) so the XAML can bind directly:
    ///
    ///     <DataGridTemplateColumn Header="PCP">
    ///       <DataTemplate>
    ///         <local:FormCell DataContext="{Binding Pcp}" />
    ///
    /// Bindings to indexed collections work in WPF but lose static type
    /// checking and tooling support. Named properties give us both.
    /// </summary>
    public class MatrixRowViewModel
    {
        public Person Person { get; }
        public string FullName => Person.FullName;

        public FormCellViewModel Q1R { get; }
        public FormCellViewModel Q2R { get; }
        public FormCellViewModel Q3R { get; }
        public FormCellViewModel Q4R { get; }

        public FormCellViewModel Pcp { get; }
        public FormCellViewModel CompAssessment { get; }
        public FormCellViewModel Reclassification { get; }
        public FormCellViewModel SafetyPlan { get; }
        public FormCellViewModel PrivacyPractices { get; }
        public FormCellViewModel ReleaseAgency { get; }
        public FormCellViewModel ReleaseDhhs { get; }
        public FormCellViewModel ReleaseMedical { get; }

        public MatrixRowViewModel(Person person, DateTime today)
        {
            Person = person;

            Q1R = new FormCellViewModel(person, FormType.Q1R, today);
            Q2R = new FormCellViewModel(person, FormType.Q2R, today);
            Q3R = new FormCellViewModel(person, FormType.Q3R, today);
            Q4R = new FormCellViewModel(person, FormType.Q4R, today);

            Pcp = new FormCellViewModel(person, FormType.PCP, today);
            CompAssessment = new FormCellViewModel(person, FormType.ComprehensiveAssessment, today);
            Reclassification = new FormCellViewModel(person, FormType.Reclassification, today);
            SafetyPlan = new FormCellViewModel(person, FormType.SafetyPlan, today);
            PrivacyPractices = new FormCellViewModel(person, FormType.PrivacyPractices, today);
            ReleaseAgency = new FormCellViewModel(person, FormType.Release_Agency, today);
            ReleaseDhhs = new FormCellViewModel(person, FormType.Release_DHHS, today);
            ReleaseMedical = new FormCellViewModel(person, FormType.Release_Medical, today);
        }
    }
}