using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace Sati.ViewModels.Supervisor
{
    public partial class TeamOverviewViewModel : ObservableObject
    {
        [ObservableProperty] private IReadOnlyList<CaseManagerSummaryViewModel> caseManagers = [];
        [ObservableProperty] private PlotModel? complianceChartModel;
        [ObservableProperty] private int totalClients;
        [ObservableProperty] private int totalOverdue;
        [ObservableProperty] private int totalNotesThisMonth;
        [ObservableProperty] private string avgComplianceLabel = "—";

        public void Refresh(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            ComplianceChartModel = null;
            CaseManagers = managers;
            TotalClients = managers.Sum(cm => cm.ClientCount);
            TotalOverdue = managers.Sum(cm => cm.OverdueCount);
            TotalNotesThisMonth = managers.Sum(cm => cm.NotesThisMonth);
            AvgComplianceLabel = managers.Count == 0 ? "—"
                                    : $"{managers.Average(cm => cm.ProgressPercent):0}%";
            ComplianceChartModel = BuildComplianceChart(managers);
        }

        private static PlotModel BuildComplianceChart(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                PlotMargins = new OxyThickness(10, 10, 20, 10),
            };

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.None,
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Maximum = 100,
                Title = "% of monthly threshold",
                TitleColor = OxyColor.FromRgb(0x8A, 0x7A, 0x6A),
                TextColor = OxyColor.FromRgb(0x3D, 0x2B, 0x1F),
                TicklineColor = OxyColor.FromRgb(0xED, 0xD9, 0xC0),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(80, 0x3D, 0x2B, 0x1F),
            };

            var series = new BarSeries
            {
                FillColor = OxyColor.FromRgb(0xC8, 0x79, 0x41),
                StrokeColor = OxyColors.Transparent,
                BarWidth = 0.6,
            };

            foreach (var cm in managers)
            {
                categoryAxis.Labels.Add(cm.DisplayName);
                series.Items.Add(new BarItem { Value = (double)cm.ProgressPercent });
            }

            // Color code bars by status
            for (int i = 0; i < managers.Count; i++)
            {
                series.Items[i] = new BarItem
                {
                    Value = (double)managers[i].ProgressPercent,
                    Color = managers[i].StatusLevel switch
                    {
                        "Ok" => OxyColor.FromRgb(0x5A, 0x8A, 0x5A),
                        "Danger" => OxyColor.FromRgb(0xA6, 0x60, 0x7A),
                        _ => OxyColor.FromRgb(0xC8, 0x79, 0x41),
                    }
                };
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(series);

            return model;
        }
    }
}