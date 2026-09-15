using OxyPlot;
using OxyPlot.Series;

namespace Sati.Helpers;

/// <summary>
/// Builds a subtle radial color transition from aligned solid OxyPlot rings. Every layer
/// repeats the exact same values and angles, so the treatment adds depth without changing
/// the proportions a user reads.
/// </summary>
internal static class PlotDonut
{
    internal sealed record Slice(string Label, double Value, OxyColor Color);

    public static void AddGradientLayers(
        PlotModel model,
        IReadOnlyList<Slice> slices,
        OxyColor emptyColor)
    {
        const double innerDiameter = 0.62;
        var layers = new[]
        {
            (Diameter: 0.90, Shade: -0.075),
            (Diameter: 0.88, Shade: -0.050),
            (Diameter: 0.86, Shade: -0.025),
            (Diameter: 0.84, Shade:  0.000),
            (Diameter: 0.82, Shade:  0.025),
            (Diameter: 0.80, Shade:  0.050),
            (Diameter: 0.78, Shade:  0.075)
        };
        var positiveSlices = slices.Where(slice => slice.Value > 0).ToList();

        foreach (var layer in layers)
        {
            var series = new PieSeries
            {
                InnerDiameter = innerDiameter / layer.Diameter,
                Diameter = layer.Diameter,
                StrokeThickness = 0,
                StartAngle = 270,
                AngleSpan = 360,
                InsideLabelFormat = string.Empty,
                OutsideLabelFormat = string.Empty,
                TickHorizontalLength = 0,
                TickRadialLength = 0,
                RenderInLegend = false
            };

            if (positiveSlices.Count == 0)
            {
                series.Slices.Add(new PieSlice("No data", 1)
                {
                    Fill = Shade(emptyColor, layer.Shade)
                });
            }
            else
            {
                foreach (var slice in positiveSlices)
                {
                    series.Slices.Add(new PieSlice(slice.Label, slice.Value)
                    {
                        Fill = Shade(slice.Color, layer.Shade)
                    });
                }
            }

            model.Series.Add(series);
        }
    }

    private static OxyColor Shade(OxyColor color, double amount) => OxyColor.FromArgb(
        color.A,
        Blend(color.R, amount),
        Blend(color.G, amount),
        Blend(color.B, amount));

    private static byte Blend(byte component, double amount)
    {
        var value = amount >= 0
            ? component + (255 - component) * amount
            : component * (1 + amount);
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
