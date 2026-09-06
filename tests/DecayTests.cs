using System;
using System.Linq;
using System.Reflection;
using System.Windows;
namespace NoteView
{
    public static class DecayTests
    {
        static int checks;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        [STAThread] public static int Main()
        {
            try
            {
                var app = new Application();
                double now = 0;
                var state = new NoteState(delegate { return now; });
                state.Process(0x90, 65, 105);
                Check(state.GetActiveNotes()[0].Brightness == 1, "attack full brightness");
                double previous = 1;
                for (int i = 1; i <= 200; i++)
                {
                    now = i * .1; var n = state.GetActiveNotes()[0];
                    Check(n.Brightness <= previous && n.Brightness >= .14, "held decay monotonic with floor");
                    Check(n.Velocity == 105, "velocity unchanged by age"); previous = n.Brightness;
                    if (i == 2) Check(n.Brightness > .47 && n.Brightness < .50, "held strike remains prominent within 200 ms");
                    if (i == 5) Check(n.Brightness > .29 && n.Brightness < .31, "slower attack settles into the held tail");
                    if (i == 30) Check(n.Brightness > .21, "held notes retain a visible slow tail after three seconds");
                }
                Check(previous < .143, "held level approaches floor");
                state.Process(0xB0, 64, 127); state.Process(0x80, 65, 0);
                Check(Math.Abs(previous - state.GetActiveNotes()[0].Brightness) < 1e-10, "release is continuous");
                for (int i = 1; i <= 120; i++)
                {
                    now = 20 + i * .1; var n = state.GetActiveNotes()[0];
                    Check(n.Brightness <= previous && n.Velocity == 105, "pedal decay preserves velocity"); previous = n.Brightness;
                }
                Check(previous == 0 && state.SustainDown, "vanishes while pedal stays down");
                var settings = new AppSettings { BackgroundOpacity = 0, GhostNotes = false, KeySignatureFifths = 2 };
                var renderer = new ObsFrameRenderer();
                byte[] faded = renderer.Render(settings, state.GetActiveNotes(), "", "", "", false);
                byte[] empty = renderer.Render(settings, null, "", "", "", false);
                Check(faded.SequenceEqual(empty), "head accidental ledger and keyboard fully disappear");
                state.Process(0x90, 65, 30);
                Check(state.GetActiveNotes()[0].Brightness == 1 && state.GetActiveNotes()[0].Velocity == 30, "retrigger resets envelope and color velocity");
                var view = new StaffView(); var method = typeof(StaffView).GetMethod("NoteColor", BindingFlags.Instance | BindingFlags.NonPublic);
                object color = method.Invoke(view, new object[] { 30 });
                now += 4;
                Check(color.Equals(method.Invoke(view, new object[] { state.GetActiveNotes()[0].Velocity })), "RGB is constant during decay");
                Check(color.Equals(method.Invoke(view, new object[] { 105 })), "strike velocity never changes harmonic RGB");
                byte[] held = renderer.Render(settings, state.GetActiveNotes(), "", "", "", false);
                Check(!held.SequenceEqual(empty), "held minimum remains visible");
                state.Process(0xB0, 64, 0); state.Process(0x80, 65, 0);
                Check(state.GetActiveNotes().Count == 0, "pedal up releases MIDI state");
                now = 0; state.Process(0x90, 60, 100); state.Process(0xB0, 64, 127);
                now = .05; double atRelease = state.GetActiveNotes()[0].Brightness;
                state.Process(0x80, 60, 0);
                Check(Math.Abs(atRelease - state.GetActiveNotes()[0].Brightness) < 1e-10, "early release stays continuous");
                now = .2;
                Check(state.GetActiveNotes()[0].Brightness < .36, "early pedal release retains fast attack decay");
                state.Clear(); now = 0;
                state.Process(0x90, 60, 100); state.Process(0x90, 64, 100); state.Process(0xB0, 64, 127);
                now = .5; double beforeRelease = state.GetActiveNotes().First(n => n.Number == 64).Brightness;
                state.Process(0x80, 64, 0);
                Check(Math.Abs(beforeRelease - state.GetActiveNotes().First(n => n.Number == 64).Brightness) < 1e-10, "slow held tail releases without a brightness jump");
                now = 3;
                var comparison = state.GetActiveNotes();
                Check(comparison.First(n => n.Number == 60).Brightness > 4 * comparison.First(n => n.Number == 64).Brightness, "held note clearly outlasts pedal note from same strike");
                app.Shutdown(); Console.WriteLine("DecayTests PASS: " + checks); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
