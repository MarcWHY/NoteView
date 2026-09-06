using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class StaffPerformanceTests
    {
        private static readonly MethodInfo render = typeof(StaffView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic);
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                string label = args.Length == 0 ? "current" : args[0];
                var app = new Application();
                Console.WriteLine("scenario,onrender_cpu_ms,raster_1680x960_ms,score_raster_ms,keyboard_raster_ms");
                if (label != "regression-only")
                    foreach (string scenario in new[] { "held8", "pedal8", "dense88" }) Benchmark(scenario, label);
                CheckCacheChanges();
                Console.WriteLine("Staff cache regression: PASS");
                app.Shutdown(); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        private static void Benchmark(string scenario, string label)
        {
            var notes = new List<ActiveNote>();
            var pitches = scenario == "dense88" ? Enumerable.Range(21, 88) : new[] { 36, 48, 55, 60, 64, 66, 70, 76 };
            foreach (int n in pitches) notes.Add(new ActiveNote { Number = n, Velocity = 100, IsHeld = scenario != "pedal8", Brightness = .6,
                HasTint = true, TintR = 210, TintG = (byte)(100 + n), TintB = 160 });
            var score = new StaffView { ScoreOnly = true, Width = 440, Height = 440, Notes = notes };
            var keyboard = new StaffView { KeyboardOnly = true, Width = 1120, Height = 140, Notes = notes };
            var surface = new Canvas { Width = 1120, Height = 640 };
            surface.Children.Add(score); surface.Children.Add(keyboard); Canvas.SetLeft(score, 340); Canvas.SetTop(keyboard, 400);
            surface.Measure(new Size(1120, 640)); surface.Arrange(new Rect(0, 0, 1120, 640)); surface.UpdateLayout();
            for (int i = 0; i < 2; i++) { Record(score); Record(keyboard); Raster(surface); }
            var cpu = new List<double>(); var raster = new List<double>();
            var scoreRaster = new List<double>(); var keyboardRaster = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                foreach (var note in notes) { note.Brightness -= .004; note.TintG++; }
                var clock = Stopwatch.StartNew(); Record(score); Record(keyboard); clock.Stop(); cpu.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart(); Raster(surface); clock.Stop(); raster.Add(clock.Elapsed.TotalMilliseconds);
                // RenderTargetBitmap includes each visual's layout offset. Bring
                // the individual layers to the origin before isolating their cost.
                Canvas.SetLeft(score, 0); Canvas.SetTop(keyboard, 0); surface.UpdateLayout();
                clock.Restart(); RasterViewport(score, 440, 440); clock.Stop(); scoreRaster.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart(); RasterViewport(keyboard, 1120, 140); clock.Stop(); keyboardRaster.Add(clock.Elapsed.TotalMilliseconds);
                Canvas.SetLeft(score, 340); Canvas.SetTop(keyboard, 400); surface.UpdateLayout();
            }
            cpu.Sort(); raster.Sort(); scoreRaster.Sort(); keyboardRaster.Sort();
            Console.WriteLine(scenario + "," + cpu[2].ToString("F3", CultureInfo.InvariantCulture) + "," + raster[2].ToString("F3", CultureInfo.InvariantCulture) + "," +
                scoreRaster[2].ToString("F3", CultureInfo.InvariantCulture) + "," + keyboardRaster[2].ToString("F3", CultureInfo.InvariantCulture));
            Save(surface, label + "-" + scenario + ".png");
            CompareReference(label, scenario);
        }
        private static void Record(StaffView view)
        { var drawing = new DrawingGroup(); using (var dc = drawing.Open()) render.Invoke(view, new object[] { dc }); }
        private static RenderTargetBitmap Raster(Visual surface)
        { var bitmap = new RenderTargetBitmap(1680, 960, 144, 144, PixelFormats.Pbgra32); bitmap.Render(surface); return bitmap; }
        private static RenderTargetBitmap RasterViewport(Visual surface, int width, int height)
        { var bitmap = new RenderTargetBitmap(width * 3 / 2, height * 3 / 2, 144, 144, PixelFormats.Pbgra32); bitmap.Render(surface); return bitmap; }
        private static void Save(Visual surface, string filename)
        {
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Raster(surface)));
            using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename))) png.Save(stream);
        }
        private static void CompareReference(string label, string scenario)
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string before = Path.Combine(folder, "before-optimized-" + scenario + ".png");
            if (label.StartsWith("before", StringComparison.Ordinal) || !File.Exists(before)) return;
            byte[] a = ImagePixels(before), b = ImagePixels(Path.Combine(folder, label + "-" + scenario + ".png"));
            long difference = 0; int maximum = 0;
            for (int i = 0; i < a.Length; i++)
            { int delta = Math.Abs(a[i] - b[i]); difference += delta; maximum = Math.Max(maximum, delta); }
            double average = difference / (double)a.Length;
            Console.WriteLine("pixel comparison " + scenario + ": mean byte error=" + average.ToString("F4", CultureInfo.InvariantCulture) + ", max=" + maximum);
            Check(average < .2, "static keyboard caching must preserve the reference appearance " + scenario);
        }
        private static byte[] ImagePixels(string filename)
        {
            using (var stream = File.OpenRead(filename))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
            }
        }
        private static void CheckCacheChanges()
        {
            var view = new StaffView { Width = 640, Height = 440, Notes = new[] { new ActiveNote { Number = 66, Velocity = 100, IsHeld = true } } };
            Prepare(view); var original = Pixels(view);
            view.Notes = new[] { new ActiveNote { Number = 66, Velocity = 100, IsHeld = true, Brightness = .2, HasTint = true, TintR = 255, TintG = 50, TintB = 70 } };
            Check(!original.SequenceEqual(Pixels(view)), "cached placement reflects new note objects, brightness and tint");
            var changed = Pixels(view); view.KeySignatureFifths = 2;
            Check(!changed.SequenceEqual(Pixels(view)), "key signature invalidates score geometry");
            changed = Pixels(view); view.Flats = true; view.KeySignatureFifths = -2;
            Check(!changed.SequenceEqual(Pixels(view)), "flat notation invalidates cached spellings");
            changed = Pixels(view); view.FullRange = false;
            Check(!changed.SequenceEqual(Pixels(view)), "pitch range invalidates layout and keyboard");
            changed = Pixels(view); view.LightTheme = true;
            Check(!changed.SequenceEqual(Pixels(view)), "theme invalidates ink and key material");
            changed = Pixels(view); view.GhostNotes = false;
            Check(!changed.SequenceEqual(Pixels(view)), "ghost visibility updates independently");
            changed = Pixels(view); view.Width = 700; Prepare(view);
            Check(!changed.SequenceEqual(Pixels(view)), "resize invalidates staff and keyboard geometry");
            // A cache warmed with one pitch must not retain that pitch after a new note replaces it.
            view.Notes = new[] { new ActiveNote { Number = 70, Velocity = 100, IsHeld = true } };
            changed = Pixels(view); view.Notes[0].Number = 77;
            Check(!changed.SequenceEqual(Pixels(view)), "in-place pitch mutation invalidates cached geometry");
            changed = Pixels(view); view.Notes = new ActiveNote[0];
            Check(!changed.SequenceEqual(Pixels(view)), "ended notes disappear from cached layouts");
            var keyboard = new StaffView { KeyboardOnly = true, Width = 1120, Height = 140 };
            var box = new Viewbox { Width = 1680, Height = 210, Child = keyboard };
            box.Measure(new Size(1680, 210)); box.Arrange(new Rect(0, 0, 1680, 210)); box.UpdateLayout(); Record(keyboard);
            var field = typeof(StaffView).GetField("whiteKeyBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                var bitmap = (RenderTargetBitmap)field.GetValue(keyboard);
                Check(bitmap.PixelWidth == 2520, "offscreen keyboard cache includes ancestor Viewbox scaling and export density");
            }
        }
        private static void Prepare(StaffView view)
        { view.Measure(new Size(view.Width, view.Height)); view.Arrange(new Rect(0, 0, view.Width, view.Height)); view.UpdateLayout(); }
        private static byte[] Pixels(StaffView view)
        {
            view.Refresh(); view.UpdateLayout(); var bitmap = Raster(view); var data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0); return data;
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
