using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace NoteView
{
    public static class ObsPerformanceBench
    {
        [STAThread] public static int Main()
        {
            var app = new Application();
            Console.WriteLine("scene,atmosphere,mean_ms,p95_ms,mean_png_bytes,background_ms,raster_ms,encode_ms");
            foreach (string scene in new[] { "held8", "pedal8", "mixed8", "dense88" })
            foreach (int atmosphere in new[] { 0, 12 })
            {
                var renderer = new ObsFrameRenderer();
                var settings = new AppSettings { BackgroundOpacity = 0, AtmosphereOpacity = atmosphere,
                    HarmonyTint = "#EDBC70", HarmonyMemoryTint = "#709BDD", HarmonyRelationTint = "#E273BB",
                    HarmonyAtmosphereStrength = 1, HarmonyVariationStrength = .65, GhostNotes = true, KeySignatureFifths = 2 };
                var notes = new List<ActiveNote>();
                int[] pitches = scene == "dense88" ? new int[88] : new[] { 38, 50, 60, 62, 65, 66, 69, 85 };
                if (scene == "dense88") for (int i = 0; i < pitches.Length; i++) pitches[i] = 21 + i;
                for (int i = 0; i < pitches.Length; i++) notes.Add(new ActiveNote { Number = pitches[i],
                    IsHeld = scene == "held8" || (scene != "pedal8" && i % 3 == 0),
                    Velocity = 96, StrikeId = i + 1, HasTint = true, TintR = 210, TintG = 165, TintB = 120 });
                var samples = new List<double>(); long bytes = 0;
                double background = 0, raster = 0, encode = 0;
                for (int frame = -5; frame < 20; frame++)
                {
                    settings.HarmonyHistoryStrength = .8 - (frame + 5) / 50.0;
                    for (int i = 0; i < notes.Count; i++)
                    { notes[i].Brightness = .8 - (frame + 5) / 70.0; notes[i].TintR = (byte)(210 - frame - 5); }
                    var watch = Stopwatch.StartNew();
                    byte[] png = renderer.Render(settings, notes, "Dmaj13", "", "", true);
                    watch.Stop();
                    if (frame >= 0)
                    {
                        samples.Add(watch.Elapsed.TotalMilliseconds); bytes += png.Length;
                        background += Timing(renderer, "LastBackgroundMilliseconds");
                        raster += Timing(renderer, "LastRasterMilliseconds"); encode += Timing(renderer, "LastEncodeMilliseconds");
                    }
                    if (frame == 19) File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        scene + "-" + atmosphere + ".png"), png);
                }
                double total = 0; foreach (double elapsed in samples) total += elapsed;
                samples.Sort();
                Console.WriteLine(scene + "," + atmosphere + "," + (total / samples.Count).ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture) + "," + samples[18].ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture) + "," + bytes / samples.Count + "," +
                    (background / samples.Count).ToString("F2") + "," + (raster / samples.Count).ToString("F2") + "," +
                    (encode / samples.Count).ToString("F2"));
            }
            app.Shutdown(); return 0;
        }
        private static double Timing(ObsFrameRenderer renderer, string name)
        {
            FieldInfo field = typeof(ObsFrameRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? 0 : (double)field.GetValue(renderer);
        }
    }
}
