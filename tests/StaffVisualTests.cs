using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class StaffVisualTests
    {
        private static int checks;

        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application();
                foreach (bool light in new[] { false, true })
                foreach (int intensity in new[] { 0, 1, 2 })
                foreach (int velocity in new[] { 24, 100, 127 })
                    CheckSustain(light, intensity, velocity);
                foreach (Size size in new[] { new Size(1050, 425), new Size(830, 300) })
                foreach (bool light in new[] { false, true })
                foreach (bool full in new[] { false, true })
                    CheckViewport(size, light, full);
                CheckClearing();
                WritePreview("sustain-dark.png", false, false, 1, 0, 0, 1050, 425);
                WritePreview("sustain-light.png", true, false, 1, 0, 0, 1050, 425);
                WritePreview("sustain-transparent.png", false, true, 1, 0, 0, 1050, 425);
                WritePreview("score-zoom-move.png", false, false, 1.4, -180, 18, 1050, 425);
                WritePreview("score-minimum-light.png", true, false, .75, 130, -15, 830, 300);
                app.Shutdown();
                Console.WriteLine("StaffVisualTests: PASS (" + checks + " assertions); previews in artifacts/staff-visual-tests.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void CheckSustain(bool light, int intensity, int velocity)
        {
            var view = Create(1050, 425, light, true);
            view.IntensityMode = intensity;
            byte[] empty = Pixels(Render(view));
            // C#6 exercises its head, accidental and ledger lines above the treble staff.
            view.Notes = new[] { new ActiveNote { Number = 85, Velocity = velocity, IsHeld = true } };
            byte[] held = Pixels(Render(view));
            view.Notes = new[] { new ActiveNote { Number = 85, Velocity = velocity, IsHeld = false, Brightness = .2 } };
            byte[] sustained = Pixels(Render(view));
            int scoreRows = (int)view.ScoreViewport.Height;
            long heldDelta = AlphaDelta(held, empty, 1050, 0, scoreRows);
            long sustainDelta = AlphaDelta(sustained, empty, 1050, 0, scoreRows);
            Check(heldDelta > 1000, "held note renders visibly");
            Check(sustainDelta > 0 && sustainDelta < heldDelta * .26,
                "pedal head, sign and ledger remain faint; ratio=" + ((double)sustainDelta / heldDelta));
            int heldPeak = PeakDelta(held, empty, 1050, scoreRows);
            int sustainPeak = PeakDelta(sustained, empty, 1050, scoreRows);
            Check(sustainPeak < heldPeak * .32, "blurred sustained peak is much weaker than held peak");
            long heldKeyboard = ColorDelta(held, empty, 1050, (int)Math.Ceiling(view.ScoreViewport.Height) + 1, 412);
            long sustainedKeyboard = ColorDelta(sustained, empty, 1050, (int)Math.Ceiling(view.ScoreViewport.Height) + 1, 412);
            Check(sustainedKeyboard > 0 && sustainedKeyboard < heldKeyboard * .24,
                "pedal keyboard fill stays subtle without a bright underline or outline");

            DrawingVisual layer = Field<DrawingVisual>(view, "sustainedNotes");
            Effect blur = layer.Effect;
            Check(blur is BlurEffect && ((BlurEffect)blur).KernelType == KernelType.Gaussian,
                "sustain is rendered through a real Gaussian blur layer");
            layer.Effect = null;
            byte[] sharp = Pixels(Render(view, false));
            int spreadPixels = 0, changedPixels = 0;
            for (int i = 3; i < scoreRows * 1050 * 4; i += 4)
            {
                if (sustained[i] != sharp[i]) changedPixels++;
                if (sharp[i] == empty[i] && sustained[i] > empty[i]) spreadPixels++;
            }
            Check(changedPixels > 30 && spreadPixels > 5,
                "blur softens edges and spreads faint alpha beyond the sharp note; changed=" + changedPixels + ", spread=" + spreadPixels);
            layer.Effect = blur;
        }

        private static void CheckViewport(Size size, bool light, bool full)
        {
            var view = Create((int)size.Width, (int)size.Height, light, full);
            view.Notes = SampleNotes();
            view.KeySignatureFifths = 2;
            byte[] original = Pixels(Render(view));
            Rect initialBounds = view.ScoreBounds;
            Rect viewport = view.ScoreViewport;
            Check(view.ScoreScale == 1 && view.ScoreOffsetX == 0 && view.ScoreOffsetY == 0,
                "default score viewport preserves original geometry");
            foreach (double scale in new[] { .5, 1.4, 2.0 })
            foreach (Point offset in new[] { new Point(0, 0), new Point(-180, 18), new Point(600, 400) })
            {
                view.ScoreScale = scale; view.ScoreOffsetX = offset.X; view.ScoreOffsetY = offset.Y;
                byte[] transformed = Pixels(Render(view));
                Check(ColorDelta(transformed, original, (int)size.Width,
                    (int)Math.Ceiling(viewport.Height) + 1, (int)size.Height) == 0,
                    "score scale/position and blurred edges never modify the keyboard");
                Check(view.ScoreViewport == viewport, "moving score does not move its viewport");
                Rect expected = initialBounds;
                Matrix matrix = new Matrix(scale, 0, 0, scale,
                    size.Width * .5 * (1 - scale) + offset.X, viewport.Height * .5 * (1 - scale) + offset.Y);
                expected.Transform(matrix); expected.Intersect(viewport);
                Check(SameBounds(expected, view.ScoreBounds), "score edit bounds follow centered zoom and offsets");
                int clear = 0;
                for (int i = 3; i < (int)viewport.Height * (int)size.Width * 4; i += 4)
                    if (transformed[i] == 0) clear++;
                Check(clear > viewport.Height * size.Width * .8, "transformed score retains true transparent background even at 200% zoom; size=" + size +
                    ", full=" + full + ", scale=" + scale + ", offset=" + offset + ", clear=" + clear / (viewport.Height * size.Width));
            }
            view.ScoreScale = double.NaN; view.ScoreOffsetX = double.PositiveInfinity; view.ScoreOffsetY = double.NegativeInfinity;
            Check(view.ScoreScale == 1 && view.ScoreOffsetX == 0 && view.ScoreOffsetY == 0,
                "invalid numeric viewport values reset safely");
            view.ScoreScale = .1; Check(view.ScoreScale == .5, "minimum score size is enforced");
            view.ScoreScale = 8; Check(view.ScoreScale == 2, "maximum score size is enforced");
        }

        private static bool SameBounds(Rect a, Rect b)
        {
            return a.IsEmpty && b.IsEmpty || !a.IsEmpty && !b.IsEmpty && Math.Abs(a.Left - b.Left) < .001 &&
                Math.Abs(a.Top - b.Top) < .001 && Math.Abs(a.Width - b.Width) < .001 && Math.Abs(a.Height - b.Height) < .001;
        }

        private static void CheckClearing()
        {
            var view = Create(1050, 425, false, true);
            byte[] empty = Pixels(Render(view));
            view.Notes = SampleNotes(); Render(view);
            view.Notes = new List<ActiveNote>();
            Check(ColorDelta(empty, Pixels(Render(view)), 1050, 0, 425) == 0,
                "note release clears both layers without stale blurred pedal notes");
        }

        private static void WritePreview(string name, bool light, bool transparent, double scale,
            double offsetX, double offsetY, int width, int height)
        {
            var view = Create(width, height, light, true);
            view.Notes = SampleNotes(); view.KeySignatureFifths = 2; view.GhostNotes = true;
            view.ScoreScale = scale; view.ScoreOffsetX = offsetX; view.ScoreOffsetY = offsetY;
            RenderTargetBitmap bitmap = Render(view);
            if (!transparent)
            {
                var composite = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (DrawingContext dc = background.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(light ? Color.FromRgb(241, 244, 239) : Color.FromRgb(17, 28, 39)),
                        null, new Rect(0, 0, width, height));
                    dc.DrawImage(bitmap, new Rect(0, 0, width, height));
                }
                composite.Render(background); bitmap = composite;
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(stream);
        }

        private static IList<ActiveNote> SampleNotes()
        {
            return new[] { new ActiveNote { Number = 38, Velocity = 115, IsHeld = false, Brightness = .2 },
                new ActiveNote { Number = 50, Velocity = 100, IsHeld = false, Brightness = .2 },
                new ActiveNote { Number = 60, Velocity = 110, IsHeld = false, Brightness = .2 },
                new ActiveNote { Number = 62, Velocity = 100, IsHeld = true },
                new ActiveNote { Number = 65, Velocity = 112, IsHeld = false, Brightness = .2 },
                new ActiveNote { Number = 66, Velocity = 112, IsHeld = true },
                new ActiveNote { Number = 69, Velocity = 96, IsHeld = true },
                new ActiveNote { Number = 73, Velocity = 80, IsHeld = true },
                new ActiveNote { Number = 85, Velocity = 120, IsHeld = false, Brightness = .2 } };
        }

        private static StaffView Create(int width, int height, bool light, bool full)
        {
            var view = new StaffView { Width = width, Height = height, LightTheme = light,
                GhostNotes = false, FullRange = full };
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            return view;
        }

        private static RenderTargetBitmap Render(StaffView view, bool refresh = true)
        {
            if (refresh) { view.Refresh(); view.UpdateLayout(); }
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view); return bitmap;
        }
        private static byte[] Pixels(BitmapSource bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        private static long AlphaDelta(byte[] a, byte[] b, int width, int firstRow, int lastRow)
        {
            long sum = 0;
            for (int i = firstRow * width * 4 + 3; i < lastRow * width * 4; i += 4) sum += Math.Abs(a[i] - b[i]);
            return sum;
        }
        private static int PeakDelta(byte[] a, byte[] b, int width, int lastRow)
        {
            int maximum = 0;
            for (int i = 3; i < lastRow * width * 4; i += 4) maximum = Math.Max(maximum, Math.Abs(a[i] - b[i]));
            return maximum;
        }
        private static long ColorDelta(byte[] a, byte[] b, int width, int firstRow, int lastRow)
        {
            long sum = 0;
            for (int i = firstRow * width * 4; i < lastRow * width * 4; i++) sum += Math.Abs(a[i] - b[i]);
            return sum;
        }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static void Check(bool condition, string label)
        { checks++; if (!condition) throw new Exception("FAILED: " + label); }
    }
}
