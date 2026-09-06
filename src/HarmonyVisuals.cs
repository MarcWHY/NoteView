using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class HarmonyVisuals
    {
        public static Brush Atmosphere(AppSettings settings, Rect score, Rect keyboard, Rect harmony)
        {
            double intensity = Clamp(settings.AtmosphereOpacity / 100) * Clamp(settings.HarmonyAtmosphereStrength);
            if (intensity <= 0) return Brushes.Transparent;
            Color primary = Parse(settings.HarmonyTint, Color.FromRgb(164, 188, 203));
            Color memory = Parse(settings.HarmonyMemoryTint, primary);
            Color relation = Parse(settings.HarmonyRelationTint, primary);
            double history = Clamp(settings.HarmonyHistoryStrength), variation = Clamp(settings.HarmonyVariationStrength);
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                // Ellipses are contained within the board and feather completely to alpha zero.
                // They follow the movable controls, rather than tinting an opaque full-window rectangle.
                Glow(dc, new Point(score.X + score.Width * .53, score.Y + score.Height * .48),
                    score.Width * .72, score.Height * .68, primary, intensity);
                Glow(dc, new Point(score.X + score.Width * .30, score.Y + score.Height * .60),
                    score.Width * .56, score.Height * .52, memory, intensity * history * .48);
                Glow(dc, new Point(harmony.X + Math.Min(harmony.Width * .24, 170), harmony.Y + harmony.Height * .48),
                    Math.Min(420, harmony.Width * .62), Math.Max(70, harmony.Height * 1.25),
                    Mix(primary, relation, Math.Max(variation, history * .60)), intensity * .85);
                Glow(dc, new Point(keyboard.X + keyboard.Width * .54, keyboard.Y + keyboard.Height * .60),
                    keyboard.Width * .43, Math.Max(45, keyboard.Height * .82),
                    Mix(primary, relation, variation * .72), intensity * .35);
            }
            drawing.Freeze();
            // A soft, low-frequency atmosphere does not need the foreground's pixel density.
            // Rasterize only these gradients at quarter size, then reuse the frozen texture:
            // this avoids expensive full-resolution software gradients in every OBS frame.
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            { dc.PushTransform(new ScaleTransform(.25, .25)); dc.DrawDrawing(drawing); dc.Pop(); }
            var bitmap = new RenderTargetBitmap(280, 160, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill, TileMode = TileMode.None };
            brush.Freeze(); return brush;
        }

        private static void Glow(DrawingContext dc, Point center, double radiusX, double radiusY, Color color, double intensity)
        {
            if (intensity <= .001) return;
            // Keep even controls placed at the board boundary from leaving a hard glowing edge.
            center.X = Math.Max(2, Math.Min(1118, center.X));
            center.Y = Math.Max(2, Math.Min(638, center.Y));
            radiusX = Math.Min(radiusX, Math.Min(center.X, 1120 - center.X));
            radiusY = Math.Min(radiusY, Math.Min(center.Y, 640 - center.Y));
            var glow = new RadialGradientBrush { RadiusX = .5, RadiusY = .5 };
            glow.GradientStops.Add(new GradientStop(WithAlpha(color, (byte)Math.Round(255 * Clamp(intensity))), 0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(color, (byte)Math.Round(255 * Clamp(intensity * .62))), .34));
            glow.GradientStops.Add(new GradientStop(WithAlpha(color, (byte)Math.Round(255 * Clamp(intensity * .16))), .70));
            glow.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
            glow.Freeze();
            dc.DrawEllipse(glow, null, center, radiusX, radiusY);
        }

        public static Brush Accent(string primaryHex, string memoryHex, string relationHex,
            double history, double variation)
        {
            Color primary = Parse(primaryHex, Color.FromRgb(164, 188, 203));
            Color memory = Parse(memoryHex, primary);
            Color relation = Parse(relationHex, primary);
            history = Clamp(history); variation = Clamp(variation);
            Color left = Mix(primary, memory, history * .82);
            Color middle = Mix(primary, relation, Math.Max(variation, history * .72));
            var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            brush.GradientStops.Add(new GradientStop(left, 0));
            brush.GradientStops.Add(new GradientStop(middle, .46));
            brush.GradientStops.Add(new GradientStop(primary, 1));
            brush.Freeze(); return brush;
        }

        private static Color Parse(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return (Color)ColorConverter.ConvertFromString(value); }
            catch { return fallback; }
        }
        private static Color Mix(Color a, Color b, double amount)
        {
            amount = Clamp(amount);
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * amount),
                (byte)Math.Round(a.G + (b.G - a.G) * amount),
                (byte)Math.Round(a.B + (b.B - a.B) * amount));
        }
        private static Color WithAlpha(Color color, byte alpha)
        { color.A = alpha; return color; }
        private static double Clamp(double value)
        { return double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
    }
}
