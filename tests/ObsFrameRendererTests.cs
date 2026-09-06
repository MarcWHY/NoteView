using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class ObsFrameRendererTests
    {
        private static int checks;
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var app = new Application();
                if (args.Length > 0 && args[0] == "--benchmark")
                {
                    Benchmark(); app.Shutdown(); return 0;
                }
                var renderer = new ObsFrameRenderer();
                var settings = new AppSettings { BackgroundOpacity = 0, GhostNotes = false };
                byte[] emptyPng = renderer.Render(settings, null, "", "", "", false);
                byte[] empty = Decode(emptyPng);
                Border surface = Field<Border>(renderer, "surface");
                Check(Field<TextBlock>(renderer, "symbol").FontFamily.Source.StartsWith("Cambria Math"),
                    "OBS harmony uses the same music-friendly typeface as the desktop");
                Check(PresentationSource.FromVisual(surface) == null, "OBS surface is unparented and has no window handle");
                Check(app.Windows.Count == 0, "frames render without any application window");
                int visible = 0, transparent = 0;
                for (int i = 3; i < empty.Length; i += 4)
                { if (empty[i] > 0) visible++; else transparent++; }
                Check(visible > 10000, "unparented score and keyboard render nonblank PNG pixels");
                Check(transparent > ObsFrameRenderer.Width * ObsFrameRenderer.Height * .74, "background outside the taller opaque piano keys is transparent");
                Check(empty[3] == 0 && empty[empty.Length - 1] == 0, "transparent frame corners have zero alpha");

                var notes = new[] { new ActiveNote { Number = 85, Velocity = 127, IsHeld = true } };
                byte[] held = Decode(renderer.Render(settings, notes, "", "", "", false));
                notes[0].IsHeld = false; notes[0].Brightness = .2;
                byte[] sustained = Decode(renderer.Render(settings, notes, "", "", "", true));
                StaffView staff = Field<StaffView>(renderer, "staff");
                int keyboardTop = (int)Field<LayoutBoard>(renderer, "board").Bounds(1).Y;
                long heldScore = Delta(held, empty, 0, keyboardTop, true);
                long sustainedScore = Delta(sustained, empty, 0, keyboardTop, true);
                Check(heldScore > 1000, "MIDI note changes unparented score frame immediately");
                Check(sustainedScore > 0 && sustainedScore < heldScore * .26,
                    "independent OBS rendering preserves faint pedal notes; ratio=" + (double)sustainedScore / heldScore);
                notes[0].IsHeld = true; notes[0].Brightness = 1; notes[0].Velocity = 24;
                byte[] quiet = Decode(renderer.Render(settings, notes, "", "", "", false));
                Check(Delta(quiet, held, 0, keyboardTop, false) > 1000, "velocity updates alter OBS note appearance");
                Check(Delta(empty, Decode(renderer.Render(settings, null, "", "", "", false)), 0, ObsFrameRenderer.Height, false) == 0,
                    "zero notes clear held and pedal layers exactly");

                notes = new[] { new ActiveNote { Number = 62, Velocity = 100, IsHeld = true },
                    new ActiveNote { Number = 65, Velocity = 108, IsHeld = false, Brightness = .2 },
                    new ActiveNote { Number = 66, Velocity = 108, IsHeld = true },
                    new ActiveNote { Number = 69, Velocity = 92, IsHeld = true } };
                settings.KeySignatureFifths = 2;
                byte[] original = Decode(renderer.Render(settings, notes, "D", "D 大三和弦", "", true));
                settings.StaffScale = .8; settings.StaffOffsetX = -180; settings.StaffOffsetY = 14;
                byte[] moved = Decode(renderer.Render(settings, notes, "D", "D 大三和弦", "", true));
                Check(Delta(original, moved, 0, keyboardTop, false) > 10000, "OBS score receives scale and position settings");
                Check(Delta(original, moved, keyboardTop + 1, 532, false) == 0, "OBS keyboard remains independent of score transform");
                Check(Delta(original, moved, 540, ObsFrameRenderer.Height, false) == 0, "score transform leaves harmony strip unchanged");
                settings.Width = 2400; settings.Height = 1600;
                Check(Delta(moved, Decode(renderer.Render(settings, notes, "D", "D 大三和弦", "", true)), 0, ObsFrameRenderer.Height, false) == 0,
                    "main-window saved dimensions cannot change OBS frame size or pixels");
                byte[] differentHarmony = Decode(renderer.Render(settings, notes, "Bm7/D", "B 小七和弦，第一转位", "也可能是 D6", true));
                Check(Delta(differentHarmony, Decode(renderer.Render(settings, notes, "Bm7/D", "hidden description", "hidden alternatives", true)), 0, ObsFrameRenderer.Height, false) == 0,
                    "explanations and alternative text never appear in OBS");
                Check(Delta(moved, differentHarmony, 540, ObsFrameRenderer.Height, false) > 1000,
                    "chord symbol, description and alternatives update in compact strip");

                settings.StaffScale = 1; settings.StaffOffsetX = 0; settings.StaffOffsetY = 0;
                Save("obs-transparent.png", renderer.Render(settings, notes, "D", "D 大三和弦", "", true));
                settings.BackgroundOpacity = 100; settings.Background = "#101B26";
                byte[] darkPng = renderer.Render(settings, notes, "D", "D 大三和弦", "", true);
                byte[] dark = Decode(darkPng);
                Check(dark[0] == 38 && dark[1] == 27 && dark[2] == 16 && dark[3] == 255,
                    "opaque OBS background matches configured color");
                Save("obs-dark.png", darkPng);
                settings.BackgroundOpacity = 35;
                byte[] partial = Decode(renderer.Render(settings, notes, "D", "D 大三和弦", "", true));
                Check(Math.Abs(partial[3] - 89) <= 1, "partial background opacity is preserved in PNG alpha");
                settings.BackgroundOpacity = 100; settings.DarkInk = true; settings.Background = "#F1F4EF";
                byte[] lightPng = renderer.Render(settings, notes, "D", "D 大三和弦", "", true);
                byte[] light = Decode(lightPng);
                Check(light[0] == 239 && light[1] == 244 && light[2] == 241 && light[3] == 255,
                    "light theme custom background is preserved");
                Save("obs-light.png", lightPng);
                settings.Background = "invalid"; settings.Accent = null;
                Check(Decode(renderer.Render(settings, notes, null, null, null, false)).Length == empty.Length,
                    "invalid colors and empty harmony use safe defaults");
                app.Shutdown();
                Console.WriteLine("ObsFrameRendererTests: PASS (" + checks + " assertions); previews in artifacts/obs-renderer-tests.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static byte[] Decode(byte[] png)
        {
            Check(png.Length > 100 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71,
                "renderer emits a PNG image");
            using (var stream = new MemoryStream(png))
            {
                BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Check(frame.PixelWidth == ObsFrameRenderer.Width && frame.PixelHeight == ObsFrameRenderer.Height,
                    "every frame stays fixed at 1120 x 640");
                var source = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[ObsFrameRenderer.Width * ObsFrameRenderer.Height * 4];
                source.CopyPixels(pixels, ObsFrameRenderer.Width * 4, 0); return pixels;
            }
        }
        private static void Benchmark()
        {
            foreach (bool transparent in new[] { true, false })
            foreach (bool dense in new[] { false, true })
            {
                var renderer = new ObsFrameRenderer();
                var settings = new AppSettings { BackgroundOpacity = transparent ? 0 : 96,
                    KeySignatureFifths = 2, GhostNotes = true };
                var notes = new List<ActiveNote>();
                int[] normal = { 38, 50, 60, 62, 65, 66, 69, 85 };
                if (dense)
                    for (int number = 21; number <= 108; number++) notes.Add(new ActiveNote { Number = number });
                else
                    foreach (int number in normal) notes.Add(new ActiveNote { Number = number });
                var milliseconds = new List<double>();
                long bytes = 0; int minBytes = int.MaxValue, maxBytes = 0;
                var stopwatch = new Stopwatch();
                for (int frame = -5; frame < 30; frame++)
                {
                    for (int i = 0; i < notes.Count; i++)
                    {
                        notes[i].Velocity = 30 + (i * 19 + (frame + 5) * 7) % 98;
                        notes[i].IsHeld = (i + frame + 5) % 3 == 0;
                    }
                    stopwatch.Restart();
                    byte[] png = renderer.Render(settings, notes, "Dmaj13", "D 大十三和弦", "也可能是 Bm11/D", true);
                    stopwatch.Stop();
                    if (frame < 0) continue;
                    milliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);
                    bytes += png.Length; minBytes = Math.Min(minBytes, png.Length); maxBytes = Math.Max(maxBytes, png.Length);
                }
                double total = 0;
                foreach (double value in milliseconds) total += value;
                milliseconds.Sort();
                Console.WriteLine("OBS Render benchmark: " + (dense ? "dense 88-note, 2/3 sustained" : "normal 8-note, 2/3 sustained") +
                    ", background=" + settings.BackgroundOpacity + "%, frames=30, totalMs=" + total.ToString("F1") +
                    ", avgMs=" + (total / 30).ToString("F2") + ", medianMs=" + ((milliseconds[14] + milliseconds[15]) / 2).ToString("F2") +
                    ", p95Ms=" + milliseconds[28].ToString("F2") + ", maxMs=" + milliseconds[29].ToString("F2") +
                    ", PNG avgBytes=" + (bytes / 30) + ", minBytes=" + minBytes + ", maxBytes=" + maxBytes);
            }
        }
        private static long Delta(byte[] a, byte[] b, int firstRow, int lastRow, bool alphaOnly)
        {
            long sum = 0;
            for (int i = firstRow * ObsFrameRenderer.Width * 4 + (alphaOnly ? 3 : 0); i < lastRow * ObsFrameRenderer.Width * 4; i += alphaOnly ? 4 : 1)
                sum += Math.Abs(a[i] - b[i]);
            return sum;
        }
        private static void Save(string name, byte[] png)
        { File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), png); }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static void Check(bool condition, string label)
        { checks++; if (!condition) throw new Exception("FAILED: " + label); }
    }
}
