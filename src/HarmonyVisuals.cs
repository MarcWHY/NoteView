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
            Color primary = Vivid(Parse(settings.HarmonyTint, Color.FromRgb(164, 188, 203)));
            Color memory = Vivid(Parse(settings.HarmonyMemoryTint, primary));
            Color relation = Vivid(Parse(settings.HarmonyRelationTint, primary));
            double history = Clamp(settings.HarmonyHistoryStrength), variation = Clamp(settings.HarmonyVariationStrength);
            double colorfulness = Math.Max(variation, history * .8);
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                // Keep the harmonic colors spatially distinct before their edges
                // blend. Averaging them into one large glow loses the chord's color.
                Glow(dc, new Point(score.X + score.Width * (.53 - .16 * colorfulness), score.Y + score.Height * (.56 + .12 * history)),
                    score.Width * .76, score.Height * .70, primary,
                    Mix(primary, relation, colorfulness * .55), intensity * (1 - .18 * colorfulness));
                Glow(dc, new Point(score.X + score.Width * .76, score.Y + score.Height * .32),
                    score.Width * .51, score.Height * .50, relation,
                    Mix(relation, primary, .38), intensity * Math.Max(variation, history * .45) * 1.3);
                Glow(dc, new Point(score.X + score.Width * .14, score.Y + score.Height * .31),
                    score.Width * .64, score.Height * .38, memory,
                    Mix(memory, primary, .25), intensity * history * 1.45);
                Glow(dc, new Point(harmony.X + Math.Min(harmony.Width * .24, 170), harmony.Y + harmony.Height * .48),
                    Math.Min(420, harmony.Width * .62), Math.Max(70, harmony.Height * 1.25),
                    Mix(primary, relation, colorfulness * .88), primary, intensity * .85);
                Glow(dc, new Point(keyboard.X + keyboard.Width * .54, keyboard.Y + keyboard.Height * .60),
                    keyboard.Width * .43, Math.Max(45, keyboard.Height * .82),
                    primary, Mix(primary, relation, colorfulness), intensity * .48);
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

        private static void Glow(DrawingContext dc, Point center, double radiusX, double radiusY,
            Color core, Color edge, double intensity)
        {
            if (intensity <= .001) return;
            // Keep even controls placed at the board boundary from leaving a hard glowing edge.
            center.X = Math.Max(2, Math.Min(1118, center.X));
            center.Y = Math.Max(2, Math.Min(638, center.Y));
            radiusX = Math.Min(radiusX, Math.Min(center.X, 1120 - center.X));
            radiusY = Math.Min(radiusY, Math.Min(center.Y, 640 - center.Y));
            var glow = new RadialGradientBrush { RadiusX = .5, RadiusY = .5 };
            glow.GradientStops.Add(new GradientStop(WithAlpha(core, (byte)Math.Round(255 * Clamp(intensity))), 0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(Mix(core, edge, .32), (byte)Math.Round(255 * Clamp(intensity * .80))), .30));
            glow.GradientStops.Add(new GradientStop(WithAlpha(edge, (byte)Math.Round(255 * Clamp(intensity * .34))), .66));
            glow.GradientStops.Add(new GradientStop(WithAlpha(edge, 0), 1));
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
        private static Color Vivid(Color color)
        {
            double high = Math.Max(color.R, Math.Max(color.G, color.B));
            double low = Math.Min(color.R, Math.Min(color.G, color.B));
            double middle = (high + low) * .5;
            // Blended extensions can be much paler than the base. Give both
            // enough chroma to survive faint alpha compositing, without changing hue.
            double saturation = high > low ? Math.Max(1.4, Math.Min(3, 105 / (high - low))) : 1;
            return Color.FromRgb(Saturate(color.R, middle, saturation), Saturate(color.G, middle, saturation), Saturate(color.B, middle, saturation));
        }
        private static byte Saturate(byte component, double middle, double saturation)
        { return (byte)Math.Round(Math.Max(0, Math.Min(255, middle + (component - middle) * saturation))); }
        private static double Clamp(double value)
        { return double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
    }
}
