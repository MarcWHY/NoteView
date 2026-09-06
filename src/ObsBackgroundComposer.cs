using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    // OBS rasterization is software-only. Compositing its soft background in a
    // reusable pixel buffer avoids repainting a full-window WPF image each frame.
    internal sealed class ObsBackgroundComposer
    {
        private readonly int width, height;
        private readonly byte[] background;
        private byte[] small, horizontal;
        private int[] leftPixels, rightPixels, weights;
        private string key;
        private bool transparent;

        public ObsBackgroundComposer(int width, int height)
        { this.width = width; this.height = height; background = new byte[width * height * 4]; }

        public void Prepare(AppSettings settings, Rect score, Rect keyboard, Rect harmony)
        {
            string next = string.Format(CultureInfo.InvariantCulture,
                "{0}|{1:F3}|{2}|{3}|{4}|{5:F3}|{6:F3}|{7:F3}|{8:F3}|{9}|{10}|{11}",
                settings.Background, settings.BackgroundOpacity, settings.HarmonyTint,
                settings.HarmonyMemoryTint, settings.HarmonyRelationTint,
                settings.HarmonyHistoryStrength, settings.HarmonyVariationStrength,
                settings.HarmonyAtmosphereStrength, settings.AtmosphereOpacity, score, keyboard, harmony);
            if (next == key) return;
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(settings.Background); }
            catch { color = Color.FromRgb(16, 27, 38); }
            double opacity = settings.BackgroundOpacity;
            if (double.IsNaN(opacity) || double.IsInfinity(opacity)) opacity = 96;
            int alpha = (int)Math.Round(Math.Max(0, Math.Min(100, opacity)) * 2.55);
            int b = (color.B * alpha + 127) / 255, g = (color.G * alpha + 127) / 255,
                r = (color.R * alpha + 127) / 255;
            var brush = HarmonyVisuals.Atmosphere(settings, score, keyboard, harmony) as ImageBrush;
            transparent = brush == null && alpha == 0;
            if (transparent) { key = next; return; }
            if (brush == null)
            {
                for (int i = 0; i < background.Length; i += 4)
                { background[i] = (byte)b; background[i + 1] = (byte)g; background[i + 2] = (byte)r; background[i + 3] = (byte)alpha; }
            }
            else
            {
                BitmapSource texture = (BitmapSource)brush.ImageSource;
                int sw = texture.PixelWidth, sh = texture.PixelHeight;
                if (small == null || small.Length != sw * sh * 4) small = new byte[sw * sh * 4];
                if (horizontal == null || horizontal.Length != width * sh * 4) horizontal = new byte[width * sh * 4];
                texture.CopyPixels(small, sw * 4, 0);
                ResizeBackground(sw, sh, b, g, r, alpha);
            }
            key = next;
        }

        private unsafe void ResizeBackground(int sw, int sh, int b, int g, int r, int alpha)
        {
            // Separable linear filtering, traversed in memory order. Native-size
            // foreground pixels never pass through this deliberately soft filter.
            if (leftPixels == null)
            {
                leftPixels = new int[width]; rightPixels = new int[width]; weights = new int[width];
                for (int x = 0; x < width; x++)
                {
                    double sx = Math.Max(0, Math.Min(sw - 1, (x + .5) * sw / width - .5));
                    int left = (int)sx;
                    leftPixels[x] = left * 4; rightPixels[x] = Math.Min(sw - 1, left + 1) * 4;
                    weights[x] = (int)Math.Round((sx - left) * 256);
                }
            }
            fixed (byte* input = small, intermediate = horizontal, result = background)
            fixed (int* left = leftPixels, right = rightPixels, xWeight = weights)
            {
                int stride = width * 4;
                for (int y = 0; y < sh; y++)
                {
                    byte* source = input + y * sw * 4, dest = intermediate + y * stride;
                    for (int x = 0; x < width; x++, dest += 4)
                    {
                        byte* a = source + left[x], c = source + right[x];
                        int weight = xWeight[x], inverse = 256 - weight;
                        dest[0] = (byte)((a[0] * inverse + c[0] * weight + 128) >> 8);
                        dest[1] = (byte)((a[1] * inverse + c[1] * weight + 128) >> 8);
                        dest[2] = (byte)((a[2] * inverse + c[2] * weight + 128) >> 8);
                        dest[3] = (byte)((a[3] * inverse + c[3] * weight + 128) >> 8);
                    }
                }
                for (int y = 0; y < height; y++)
                {
                    double sy = Math.Max(0, Math.Min(sh - 1, (y + .5) * sh / height - .5));
                    int top = (int)sy, weight = (int)Math.Round((sy - top) * 256), inverse = 256 - weight;
                    byte* source = intermediate + top * stride, other = intermediate + Math.Min(sh - 1, top + 1) * stride;
                    byte* dest = result + y * stride;
                    for (int x = 0; x < width; x++, source += 4, other += 4, dest += 4)
                    {
                        int a = (source[3] * inverse + other[3] * weight + 128) >> 8, rest = 255 - a;
                        dest[0] = (byte)(((source[0] * inverse + other[0] * weight + 128) >> 8) + (b * rest + 127) / 255);
                        dest[1] = (byte)(((source[1] * inverse + other[1] * weight + 128) >> 8) + (g * rest + 127) / 255);
                        dest[2] = (byte)(((source[2] * inverse + other[2] * weight + 128) >> 8) + (r * rest + 127) / 255);
                        dest[3] = (byte)(a + (alpha * rest + 127) / 255);
                    }
                }
            }
        }

        public unsafe void Composite(byte[] foreground)
        {
            if (foreground == null || foreground.Length != background.Length) throw new ArgumentException("Unexpected frame size.", "foreground");
            if (transparent) return;
            fixed (byte* front = foreground, back = background)
            {
                for (int i = 0; i < foreground.Length; i += 4)
                {
                    int inverse = 255 - front[i + 3];
                    if (inverse == 0) continue;
                    if (inverse == 255) { *(uint*)(front + i) = *(uint*)(back + i); continue; }
                    front[i] = (byte)(front[i] + (back[i] * inverse + 127) / 255);
                    front[i + 1] = (byte)(front[i + 1] + (back[i + 1] * inverse + 127) / 255);
                    front[i + 2] = (byte)(front[i + 2] + (back[i + 2] * inverse + 127) / 255);
                    front[i + 3] = (byte)(front[i + 3] + (back[i + 3] * inverse + 127) / 255);
                }
            }
        }
    }
}
