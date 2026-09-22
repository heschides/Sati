using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;

namespace Sati.ViewModels
{
    /// <summary>
    /// One matrix cell. While the next annual form is being prepared, show it
    /// after the current form is complete so an overdue renewal cannot look green.
    /// An unfinished current form remains visible until it is resolved.
    /// </summary>
    public class FormCellViewModel
    {
        public Form? Form { get; }
        public FormCellStatus Status { get; }
        public bool IsRenewal { get; }

        public FormCellViewModel(
            Person person,
            FormType type,
            DateTime today,
            ComplianceScheduleSettings? schedule = null)
        {
            var asOf = today.Date;
            if (ComplianceScheduleRules.HasRenewalOverlap(type.ToString()))
            {
                var (current, renewal) = AnnualFormSlots.Resolve(
                    person, type, asOf, schedule ?? new ComplianceScheduleSettings());
                Form = current?.Form;
                if (renewal?.Form is Form next &&
                    (Form is null || Form.IsSatisfiedAsOf(asOf)))
                {
                    Form = next;
                    IsRenewal = true;
                }
            }
            else
            {
                Form = person.GetCurrentCycleForm(type, asOf);
            }

            Status = FormCellStatusCalculator.Compute(Form, asOf);
        }

        public DateTime? DueDate => Form?.DueDate;
        public DateTime? CompletedDate => Form?.CompletedDate;
        public DateTime? OpenedDate => Form?.OpenedDate;

        public bool IsOpen => Form is { OpenedDate: not null, IsCompliant: false };

        public string CellText
        {
            get
            {
                if (Form is null)
                    return string.Empty;

                if (IsRenewal)
                {
                    var detail = Form.CompletedDate is DateTime completed
                        ? $"Done {completed:M/d/yy}"
                        : Status == FormCellStatus.Overdue
                            ? $"OVERDUE {Form.DueDate:M/d/yy}"
                            : $"Due {Form.DueDate:M/d/yy}";
                    return $"RENEWAL{Environment.NewLine}{detail}";
                }

                var lines = new List<string>();
                if (Status == FormCellStatus.Overdue)
                    lines.Add("OVERDUE");

                lines.Add($"Due: {Form.DueDate:M/d/yy}");

                if (Form.CompletedDate.HasValue)
                    lines.Add($"Completed: {Form.CompletedDate.Value:M/d/yy}");
                else if (IsOpen)
                    lines.Add($"Opened: {Form.OpenedDate!.Value:M/d/yy}");

                return string.Join(Environment.NewLine, lines);
            }
        }
    }
}
