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
                if (args.Length > 0 && args[0] == "--benchmark-cache")
                { BenchmarkAtmosphereCache(); app.Shutdown(); return 0; }
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
                Check(transparent > ObsFrameRenderer.PixelWidth * ObsFrameRenderer.PixelHeight * .74, "background outside the taller opaque piano keys is transparent");
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
                TestAtmosphereAndNoteTint();
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
                Check(frame.PixelWidth == ObsFrameRenderer.PixelWidth && frame.PixelHeight == ObsFrameRenderer.PixelHeight,
                    "every frame uses the 1.5x 1680 x 960 output resolution");
                var source = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[ObsFrameRenderer.PixelWidth * ObsFrameRenderer.PixelHeight * 4];
                source.CopyPixels(pixels, ObsFrameRenderer.PixelWidth * 4, 0); return pixels;
            }
        }
        private static void TestAtmosphereAndNoteTint()
        {
            var renderer = new ObsFrameRenderer();
            var settings = new AppSettings { BackgroundOpacity = 0, GhostNotes = false, AtmosphereOpacity = 0,
                HarmonyTint = "#EDBC70", HarmonyMemoryTint = "#709BDD", HarmonyRelationTint = "#E273BB",
                HarmonyHistoryStrength = .8, HarmonyVariationStrength = .6, HarmonyAtmosphereStrength = .9 };
            byte[] baseline = Decode(renderer.Render(settings, null, "Cmaj7", "", "", false));
            TextBlock label = Field<TextBlock>(renderer, "symbol");
            var gradient = label.Foreground as LinearGradientBrush;
            Check(gradient != null && gradient.IsFrozen && gradient.GradientStops.Count >= 3 &&
                gradient.GradientStops[0].Color != gradient.GradientStops[2].Color,
                "chord label carries a frozen gradient of historical and current harmony colors");
            Check(label.HorizontalAlignment == HorizontalAlignment.Left && label.ActualWidth < 300,
                "harmony gradient spans the visible text instead of unused board width");
            settings.AtmosphereOpacity = 12;
            byte[] glowPng = renderer.Render(settings, null, "Cmaj7", "", "", false);
            byte[] glow = Decode(glowPng);
            int[] corners = { 3, (ObsFrameRenderer.PixelWidth - 1) * 4 + 3,
                (ObsFrameRenderer.PixelHeight - 1) * ObsFrameRenderer.PixelWidth * 4 + 3, glow.Length - 1 };
            foreach (int corner in corners) Check(glow[corner] == 0, "active atmosphere keeps each transparent frame corner at alpha zero");
            int litBackground = 0, strongestBackground = 0;
            for (int i = 3; i < (int)(400 * ObsFrameRenderer.RenderScale) * ObsFrameRenderer.PixelWidth * 4; i += 4)
                if (baseline[i] == 0 && glow[i] > 0) { litBackground++; strongestBackground = Math.Max(strongestBackground, glow[i]); }
            Check(litBackground > 10000 && strongestBackground >= 15 && strongestBackground <= 55,
                "transparent score background receives a broad but faint local atmosphere, peak=" + strongestBackground);
            Save("obs-harmonic-atmosphere-transparent.png", glowPng);
            settings.BackgroundOpacity = 100;
            Save("obs-harmonic-atmosphere-dark.png", renderer.Render(settings, null, "Cmaj7", "", "", false));
            settings.BackgroundOpacity = 0;
            settings.AtmosphereOpacity = 0;
            Check(Delta(baseline, Decode(renderer.Render(settings, null, "Cmaj7", "", "", false)), 0, ObsFrameRenderer.Height, false) == 0,
                "disabling atmosphere restores the exact transparent baseline");
            settings.AtmosphereOpacity = 12; settings.HarmonyAtmosphereStrength = 0;
            Check(Delta(baseline, Decode(renderer.Render(settings, null, "Cmaj7", "", "", false)), 0, ObsFrameRenderer.Height, false) == 0,
                "silent atmosphere fades all the way to the original transparent baseline");

            var notes = new[] { new ActiveNote { Number = 62, Velocity = 100, IsHeld = true },
                new ActiveNote { Number = 68, Velocity = 100, IsHeld = true } };
            byte[] plain = Decode(renderer.Render(settings, notes, "Cmaj7", "", "", false));
            notes[0].HasTint = notes[1].HasTint = true;
            notes[0].TintR = 92; notes[0].TintG = 218; notes[0].TintB = 224;
            notes[1].TintR = 239; notes[1].TintG = 112; notes[1].TintB = 174;
            byte[] tinted = Decode(renderer.Render(settings, notes, "Cmaj7", "", "", false));
            Check(Delta(plain, tinted, 0, 400, false) > 1000 && Delta(plain, tinted, 400, 540, false) > 1000,
                "distinct note tints color both staff heads/accidentals and matching piano keys");
            Check(Delta(plain, tinted, 0, ObsFrameRenderer.Height, true) == 0,
                "harmonic hue changes preserve velocity/decay alpha and existing note positions");
            notes[0].HasTint = notes[1].HasTint = false;
            Check(Delta(plain, Decode(renderer.Render(settings, notes, "Cmaj7", "", "", false)), 0, ObsFrameRenderer.Height, false) == 0,
                "notes without an explicit tint retain the global accent fallback");
        }
        private static void Benchmark()
        {
            foreach (bool atmosphere in new[] { false, true })
            foreach (bool transparent in new[] { true, false })
            foreach (bool dense in new[] { false, true })
            {
                var renderer = new ObsFrameRenderer();
                var settings = new AppSettings { BackgroundOpacity = transparent ? 0 : 96,
                    AtmosphereOpacity = atmosphere ? 12 : 0, HarmonyAtmosphereStrength = 1,
                    HarmonyTint = "#EDBC70", HarmonyMemoryTint = "#709BDD", HarmonyRelationTint = "#E273BB",
                    HarmonyHistoryStrength = .75, HarmonyVariationStrength = .65,
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
                        notes[i].HasTint = true; notes[i].TintR = (byte)(110 + i % 3 * 55);
                        notes[i].TintG = (byte)(190 - i % 3 * 25); notes[i].TintB = (byte)(120 + i % 3 * 55);
                    }
                    settings.HarmonyHistoryStrength = .75 - Math.Max(0, frame) / 100.0;
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
                    ", background=" + settings.BackgroundOpacity + "%, atmosphere=" + settings.AtmosphereOpacity + "%, frames=30, totalMs=" + total.ToString("F1") +
                    ", avgMs=" + (total / 30).ToString("F2") + ", medianMs=" + ((milliseconds[14] + milliseconds[15]) / 2).ToString("F2") +
                    ", p95Ms=" + milliseconds[28].ToString("F2") + ", maxMs=" + milliseconds[29].ToString("F2") +
                    ", PNG avgBytes=" + (bytes / 30) + ", minBytes=" + minBytes + ", maxBytes=" + maxBytes);
                var probe = new RenderTargetBitmap(ObsFrameRenderer.PixelWidth, ObsFrameRenderer.PixelHeight,
                    96 * ObsFrameRenderer.RenderScale, 96 * ObsFrameRenderer.RenderScale, PixelFormats.Pbgra32);
                stopwatch.Restart(); probe.Render(Field<Border>(renderer, "surface")); stopwatch.Stop();
                double drawMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(probe));
                stopwatch.Restart(); using (var stream = new MemoryStream()) encoder.Save(stream); stopwatch.Stop();
                Console.WriteLine("Breakdown: rasterMs=" + drawMilliseconds.ToString("F2") + ", pngMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F2"));
            }
        }
        private static void BenchmarkAtmosphereCache()
        {
            foreach (bool atmosphere in new[] { false, true })
            foreach (bool steady in new[] { true, false })
            {
                var renderer = new ObsFrameRenderer();
                var settings = new AppSettings { BackgroundOpacity = 0, AtmosphereOpacity = atmosphere ? 12 : 0, HarmonyAtmosphereStrength = 1,
                    HarmonyTint = "#EDBC70", HarmonyMemoryTint = "#709BDD", HarmonyRelationTint = "#E273BB",
                    HarmonyHistoryStrength = .75, HarmonyVariationStrength = .65, KeySignatureFifths = 2, GhostNotes = true };
                var notes = new List<ActiveNote>();
                foreach (int number in new[] { 38, 50, 60, 62, 65, 66, 69, 85 }) notes.Add(new ActiveNote { Number = number, Velocity = 90 });
                double total = 0; var watch = new Stopwatch();
                for (int frame = -5; frame < 30; frame++)
                {
                    for (int i = 0; i < notes.Count; i++) notes[i].IsHeld = (i + frame + 5) % 3 == 0;
                    if (!steady) settings.HarmonyHistoryStrength = .75 - Math.Max(0, frame) / 100.0;
                    watch.Restart(); renderer.Render(settings, notes, "Dmaj13", "", "", true); watch.Stop();
                    if (frame >= 0) total += watch.Elapsed.TotalMilliseconds;
                }
                Console.WriteLine("Atmosphere cache: " + (atmosphere ? "quarter-res" : "disabled") +
                    ", " + (steady ? "steady" : "changing") + ", avgMs=" + (total / 30).ToString("F2"));
            }
        }
        private static long Delta(byte[] a, byte[] b, int firstRow, int lastRow, bool alphaOnly)
        {
            long sum = 0;
            int firstPixelRow = (int)Math.Round(firstRow * ObsFrameRenderer.RenderScale);
            int lastPixelRow = (int)Math.Round(lastRow * ObsFrameRenderer.RenderScale);
            for (int i = firstPixelRow * ObsFrameRenderer.PixelWidth * 4 + (alphaOnly ? 3 : 0); i < lastPixelRow * ObsFrameRenderer.PixelWidth * 4; i += alphaOnly ? 4 : 1)
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
