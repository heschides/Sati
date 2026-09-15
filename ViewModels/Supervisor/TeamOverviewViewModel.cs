using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Sati.Helpers;

namespace Sati.ViewModels.Supervisor
{
    public partial class TeamOverviewViewModel : ObservableObject
    {
        [ObservableProperty] private IReadOnlyList<CaseManagerSummaryViewModel> caseManagers = [];
        [ObservableProperty] private PlotModel? complianceChartModel;
        [ObservableProperty] private PlotModel? teamComplianceChartModel;
        [ObservableProperty] private int totalClients;
        [ObservableProperty] private int totalOverdue;
        [ObservableProperty] private int totalNotesThisMonth;
        [ObservableProperty] private int clientsClear;
        [ObservableProperty] private int clientsNeedingAttention;
        [ObservableProperty] private string avgComplianceLabel = "—";
        [ObservableProperty] private string complianceSummary = "No caseload data is available.";
        [ObservableProperty] private bool hasComplianceData;

        public void Refresh(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            ComplianceChartModel = null;
            TeamComplianceChartModel = null;
            CaseManagers = managers;
            TotalClients = managers.Sum(cm => cm.ClientCount);
            TotalOverdue = managers.Sum(cm => cm.OverdueCount);
            TotalNotesThisMonth = managers.Sum(cm => cm.NotesThisMonth);
            ClientsClear = managers.Sum(cm => cm.ClientsClearOfOverdueItems);
            ClientsNeedingAttention = managers.Sum(cm => cm.ClientsWithOverdueItems);
            HasComplianceData = TotalClients > 0;
            AvgComplianceLabel = TotalClients == 0
                ? "—"
                : $"{100m * ClientsClear / TotalClients:0}%";
            ComplianceSummary = TotalClients == 0
                ? "No caseload data is available."
                : $"{ClientsClear} of {TotalClients} clients have no overdue required forms";
            ComplianceChartModel = BuildComplianceChart(managers);
            TeamComplianceChartModel = BuildTeamComplianceChart(ClientsClear, ClientsNeedingAttention);
        }

        internal static PlotModel BuildComplianceChart(IReadOnlyList<CaseManagerSummaryViewModel> managers)
        {
            var textColor = PlotTheme.Color(
                "TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
            var mutedColor = PlotTheme.Color(
                "TextMutedBrush", OxyColor.FromRgb(0x8A, 0x7A, 0x6A));
            var gridColor = PlotTheme.ColorWithAlpha(
                "TextPrimaryBrush", 50, textColor);
            var clearColor = PlotTheme.Color(
                "ChartBlueBrush", OxyColor.FromRgb(0x17, 0x69, 0xD2));
            var attentionColor = PlotTheme.Color(
                "ChartCopperBrush", OxyColor.FromRgb(0xD4, 0x7A, 0x24));

            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                PlotAreaBorderColor = OxyColors.Transparent,
                PlotAreaBorderThickness = new OxyThickness(0),
                TextColor = textColor,
                PlotMargins = new OxyThickness(104, 18, 22, 42),
            };
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.BottomCenter,
                LegendOrientation = LegendOrientation.Horizontal,
                LegendPlacement = LegendPlacement.Outside,
                LegendTextColor = mutedColor,
                LegendBorder = OxyColors.Transparent
            });

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = textColor,
                TicklineColor = OxyColors.Transparent,
                AxislineStyle = LineStyle.None,
                MajorGridlineStyle = LineStyle.None,
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Maximum = 100,
                MajorStep = 25,
                StringFormat = "0'%'",
                TextColor = mutedColor,
                TicklineColor = OxyColors.Transparent,
                AxislineStyle = LineStyle.None,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridColor,
                MinorGridlineStyle = LineStyle.None
            };

            var clearSeries = new BarSeries
            {
                Title = "Clear",
                IsStacked = true,
                FillColor = clearColor,
                StrokeColor = OxyColors.Transparent,
                BarWidth = 0.58,
                TrackerFormatString = "{1}\nClear: {2:0}%"
            };
            var attentionSeries = new BarSeries
            {
                Title = "Has overdue items",
                IsStacked = true,
                FillColor = attentionColor,
                StrokeColor = OxyColors.Transparent,
                BarWidth = 0.58,
                TrackerFormatString = "{1}\nHas overdue items: {2:0}%"
            };

            foreach (var manager in managers)
            {
                categoryAxis.Labels.Add(manager.DisplayName);
                clearSeries.Items.Add(new BarItem { Value = (double)manager.CompliancePercent });
                attentionSeries.Items.Add(new BarItem
                {
                    Value = manager.ClientCount == 0 ? 0 : 100d - (double)manager.CompliancePercent
                });
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(clearSeries);
            model.Series.Add(attentionSeries);
            return model;
        }

        internal static PlotModel BuildTeamComplianceChart(int clientsClear, int clientsNeedingAttention)
        {
            var textColor = PlotTheme.Color(
                "TextPrimaryBrush", OxyColor.FromRgb(0x3D, 0x2B, 0x1F));
            var clearColor = PlotTheme.Color(
                "ChartBlueBrush", OxyColor.FromRgb(0x17, 0x69, 0xD2));
            var attentionColor = PlotTheme.Color(
                "ChartCopperBrush", OxyColor.FromRgb(0xD4, 0x7A, 0x24));
            var emptyColor = PlotTheme.ColorWithAlpha(
                "TextPrimaryBrush", 30, textColor);

            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = textColor,
                PlotMargins = new OxyThickness(4)
            };
            PlotDonut.AddGradientLayers(model,
            [
                new PlotDonut.Slice("Clear", clientsClear, clearColor),
                new PlotDonut.Slice("Needs attention", clientsNeedingAttention, attentionColor)
            ], emptyColor);
            return model;
        }
    }
}
