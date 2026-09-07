using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sati.Helpers
{
    /// <summary>
    /// The single owner of Sati's legibility arithmetic: WCAG 2.1 relative
    /// luminance, contrast ratio, and the flattening of a WPF brush into the
    /// colors a reader actually receives.
    /// </summary>
    /// <remarks>
    /// Themes are a token system — views name a role such as
    /// <c>TextMutedBrush</c> and never a color — so legibility is a property of
    /// the token pairs, not of any one screen. Scoring the pairs is therefore
    /// finite and complete, which is what <c>ThemeLegibilityTests</c> asserts.
    /// <para>
    /// A gradient or a tiled pattern paints many colors under the same run of
    /// text, so <see cref="PaintedColors"/> returns every one of them and the
    /// caller scores against the worst. Translucent layers are composited over
    /// the ground they sit on rather than judged in isolation, because a
    /// #14-alpha stroke over parchment is not a dark color to the eye.
    /// </para>
    /// </remarks>
    public static class ThemeContrast
    {
        /// <summary>WCAG AA for body text and any text below 18.66px / 14px bold.</summary>
        public const double NormalTextMinimum = 4.5;

        /// <summary>WCAG AA for large text and for control boundaries.</summary>
        public const double LargeTextMinimum = 3.0;

        /// <summary>Relative luminance as defined by WCAG 2.1, ignoring alpha.</summary>
        public static double RelativeLuminance(Color color)
        {
            static double Channel(byte value)
            {
                var scaled = value / 255.0;
                return scaled <= 0.03928
                    ? scaled / 12.92
                    : Math.Pow((scaled + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.R))
                 + (0.7152 * Channel(color.G))
                 + (0.0722 * Channel(color.B));
        }

        /// <summary>
        /// The WCAG contrast ratio between two opaque colors, from 1 (identical)
        /// to 21 (black on white). Order does not matter.
        /// </summary>
        public static double Ratio(Color first, Color second)
        {
            var a = RelativeLuminance(first);
            var b = RelativeLuminance(second);
            var lighter = Math.Max(a, b);
            var darker = Math.Min(a, b);
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>Source-over composite of <paramref name="over"/> onto an opaque ground.</summary>
        public static Color Composite(Color over, Color ground)
        {
            if (over.A == 255)
                return over;

            var alpha = over.A / 255.0;
            return Color.FromRgb(
                (byte)Math.Round((over.R * alpha) + (ground.R * (1 - alpha))),
                (byte)Math.Round((over.G * alpha) + (ground.G * (1 - alpha))),
                (byte)Math.Round((over.B * alpha) + (ground.B * (1 - alpha))));
        }

        /// <summary>
        /// Every color <paramref name="brush"/> can put under a glyph, already
        /// flattened against <paramref name="ground"/>. Empty when the brush
        /// paints something this cannot reason about, such as an image.
        /// </summary>
        public static IReadOnlyList<Color> PaintedColors(Brush? brush, Color ground)
        {
            var colors = new List<Color>();
            Collect(brush, ground, colors, depth: 0);
            return colors;
        }

        /// <summary>
        /// The worst contrast <paramref name="foreground"/> reaches anywhere over
        /// <paramref name="background"/>, or <see langword="null"/> when either
        /// brush paints nothing this can score.
        /// </summary>
        public static double? WorstRatio(Brush? foreground, Brush? background, Color ground)
        {
            var backgrounds = PaintedColors(background, ground);
            if (backgrounds.Count == 0)
                return null;

            double? worst = null;
            foreach (var behind in backgrounds)
            {
                foreach (var front in PaintedColors(foreground, behind))
                {
                    var ratio = Ratio(front, behind);
                    if (worst is null || ratio < worst)
                        worst = ratio;
                }
            }

            return worst;
        }

        private static void Collect(Brush? brush, Color ground, List<Color> into, int depth)
        {
            // Patterns nest a VisualBrush around a DrawingBrush; nothing in the
            // themes goes deeper, and the bound keeps a cycle from hanging a test.
            if (brush is null || depth > 4)
                return;

            var opacity = Math.Clamp(brush.Opacity, 0, 1);

            void Add(Color color)
            {
                var scaled = Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
                into.Add(Composite(scaled, ground));
            }

            switch (brush)
            {
                case SolidColorBrush solid:
                    Add(solid.Color);
                    break;

                case GradientBrush gradient:
                    foreach (var stop in gradient.GradientStops)
                        Add(stop.Color);
                    break;

                case DrawingBrush drawing:
                    CollectDrawing(drawing.Drawing, ground, into, opacity);
                    break;

                case VisualBrush visual:
                    // The blurred pattern brushes wrap a Border whose Background
                    // is the crisp DrawingBrush; the blur moves no color, so the
                    // wrapped brush is what a reader ends up seeing.
                    if (visual.Visual is Border border)
                        Collect(border.Background, ground, into, depth + 1);
                    else if (visual.Visual is Panel panel)
                        Collect(panel.Background, ground, into, depth + 1);
                    break;
            }
        }

        private static void CollectDrawing(Drawing? drawing, Color ground, List<Color> into, double opacity)
        {
            // The first fill in a tile covers it edge to edge; every later fill and
            // stroke is translucent decoration composited over that same ground.
            var layers = new List<Color>();
            Flatten(drawing, layers);
            if (layers.Count == 0)
                return;

            var tileGround = Composite(
                Color.FromArgb((byte)Math.Round(layers[0].A * opacity), layers[0].R, layers[0].G, layers[0].B),
                ground);

            into.Add(tileGround);
            for (var index = 1; index < layers.Count; index++)
            {
                var layer = layers[index];
                into.Add(Composite(
                    Color.FromArgb((byte)Math.Round(layer.A * opacity), layer.R, layer.G, layer.B),
                    tileGround));
            }
        }

        private static void Flatten(Drawing? drawing, List<Color> into)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (var child in group.Children)
                        Flatten(child, into);
                    break;

                case GeometryDrawing geometry:
                    if (geometry.Brush is SolidColorBrush fill)
                        into.Add(fill.Color);
                    if (geometry.Pen?.Brush is SolidColorBrush stroke)
                        into.Add(stroke.Color);
                    break;
            }
        }
    }
}
