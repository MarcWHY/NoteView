using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace NoteView
{
    public static class HarmonyColorTests
    {
        static int checks;
        static double now;
        static IList<ActiveNote> Notes(params int[] pitches) { return pitches.Select(n => new ActiveNote { Number = n, Velocity = 100, IsHeld = true }).ToList(); }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        static void Settle(HarmonyColor engine, IList<ActiveNote> notes)
        { engine.Update(notes, true); now += .21; engine.Update(notes, true); now += 1.4; engine.Update(notes, true); }
        [STAThread] public static int Main()
        {
            try
            {
                var app = new Application();
                var engine = new HarmonyColor(delegate { return now; });
                string neutral = engine.Hex;
                Settle(engine, Notes(60)); Check(engine.Hex == neutral, "single note has no invented harmony");
                Settle(engine, Notes(48, 64, 67)); string major = engine.Hex;
                Check(engine.Group == "0:major" && major != neutral, "major establishes a warm palette");
                Settle(engine, Notes(52, 67, 72)); Check(engine.Hex == major, "inversion retains palette");
                Settle(engine, Notes(48, 64, 67, 74)); Check(engine.Hex == major, "melodic added ninth retains palette");
                Settle(engine, Notes(48, 64, 67, 69)); Check(engine.Hex == major, "sixth retains major group");
                Settle(engine, Notes(45, 60, 64)); string minor = engine.Hex;
                Check(engine.Group == "9:minor" && minor != major, "new A minor voicing replaces old major foundation");
                engine.Update(Notes(43, 59, 62, 65), true); now += .08;
                engine.Update(Notes(45, 60, 64), true); now += .3;
                engine.Update(Notes(45, 60, 64), true); Check(engine.Hex == minor, "brief passing harmony does not flash color");
                var heldDominant = Notes(43, 59, 62, 65);
                heldDominant.Add(new ActiveNote { Number = 60, Velocity = 100, IsHeld = false, Brightness = .4 });
                Settle(engine, heldDominant); Check(engine.Group == "7:dominant", "held chord wins over stale pedal pitch");
                string dominant = engine.Hex;
                Settle(engine, Notes(60, 61, 62, 67)); Check(engine.Hex == dominant, "unrecognized phrase retains established palette");
                engine.Update(Notes(), true); now += .3; engine.Update(Notes(), true);
                Check(engine.Hex == dominant, "short silence retains palette");
                now += 1.6; engine.Update(Notes(), true); now += 1; engine.Update(Notes(), true);
                Check(engine.Hex == neutral, "long silence returns to neutral");
                string[] families = { "major", "minor", "suspended", "dominant", "diminished", "augmented", "altered" };
                int[][] chords = { new[] { 60,64,67 }, new[] {60,63,67}, new[] {60,65,67}, new[] {60,64,67,70}, new[] {60,63,66}, new[] {60,64,68}, new[] {60,61,64,67,70} };
                var colors = new HashSet<string>();
                for (int i = 0; i < chords.Length; i++)
                {
                    var e = new HarmonyColor(delegate { return now; }); Settle(e, Notes(chords[i]));
                    Check(e.Group == "0:" + families[i], "quality category " + families[i]); colors.Add(e.Hex);
                    var preview = Notes(chords[i]);
                    for (int n = 0; n < preview.Count; n++) preview[n].Velocity = 35 + n * 20;
                    e.ApplyNoteTints(preview);
                    File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "palette-" + families[i] + ".png"),
                        new ObsFrameRenderer().Render(new AppSettings { HarmonyTint = e.Hex,
                            HarmonyMemoryTint = e.MemoryHex, HarmonyRelationTint = e.RelationHex,
                            HarmonyHistoryStrength = e.HistoryStrength, HarmonyVariationStrength = e.VariationStrength,
                            HarmonyAtmosphereStrength = e.AtmosphereStrength }, preview,
                            MusicTheory.Recognize(chords[i], false).Symbol, "", "", false));
                }
                Check(colors.Count == 7, "harmonic character palettes are distinct");
                var f = new HarmonyColor(delegate { return now; }); Settle(f, Notes(53,69,72));
                Check(f.Hex != major, "new root changes shade within major family");
                var staff = new StaffView { AccentColor = Colors.Coral };
                MethodInfo colorMethod = typeof(StaffView).GetMethod("NoteColor", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo alphaMethod = typeof(StaffView).GetMethod("NoteOpacity", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(colorMethod.Invoke(staff, new object[] { 20 }).Equals(colorMethod.Invoke(staff, new object[] { 120 })), "velocity does not change color");
                double quiet = (double)alphaMethod.Invoke(staff, new object[] { new ActiveNote { Velocity = 20, Brightness = 1 } });
                double loud = (double)alphaMethod.Invoke(staff, new object[] { new ActiveNote { Velocity = 120, Brightness = 1 } });
                Check(loud > quiet * 2, "velocity changes strike brightness");
                var main = new MainWindow(true);
                typeof(MainWindow).GetField("harmonyColor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, new HarmonyColor(delegate { return now; }));
                main.SetPreviewChord();
                MethodInfo animate = typeof(MainWindow).GetMethod("AnimateNotes", BindingFlags.Instance | BindingFlags.NonPublic);
                now += .25; animate.Invoke(main, null); now += .2; animate.Invoke(main, null);
                var scoreView = (StaffView)typeof(MainWindow).GetField("staff", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
                var keyboardView = (StaffView)typeof(MainWindow).GetField("keyboard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
                Check(scoreView.AccentColor == keyboardView.AccentColor && scoreView.AccentColor == (Color)ColorConverter.ConvertFromString(main.Settings.HarmonyTint),
                    "desktop score keyboard and OBS settings receive the same confirmed palette");
                var dmState = new NoteState(delegate { return now; });
                var dmColor = new HarmonyColor(delegate { return now; });
                dmState.Process(0x90, 50, 100); dmState.Process(0x90, 65, 100); dmState.Process(0x90, 69, 100);
                Settle(dmColor, dmState.GetActiveNotes()); string dmTint = dmColor.Hex;
                dmState.Process(0xB0, 64, 127); dmState.Process(0x80, 69, 0); dmState.Process(0x90, 70, 100);
                Settle(dmColor, dmState.GetActiveNotes());
                Check(dmColor.Chord.Symbol == "Dm" && dmColor.Hex == dmTint, "Dm label and tint both survive Bb over retained A");
                foreach (int pitch in new[] { 50, 65, 70 }) { dmState.Process(0x90, pitch, 100); dmColor.Update(dmState.GetActiveNotes(), true); }
                Settle(dmColor, dmState.GetActiveNotes());
                Check(dmColor.Chord.Root == 10 && dmColor.Hex != dmTint, "confirmed new voicing changes both label and tint");
                Console.WriteLine("HarmonyColorTests PASS: " + checks); app.Shutdown(); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
