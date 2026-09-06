using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class HarmonyHistoryTests
    {
        private static int checks, failures;
        private static void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }
        private static void Test(string name, Action body)
        {
            try { body(); Console.WriteLine("PASS: " + name); }
            catch (Exception error) { failures++; Console.Error.WriteLine(name + ": " + error); }
        }

        [STAThread] public static int Main()
        {
            var app = new Application();
            Test("Chord membership", ChordMembership);
            Test("Passing tone and resolution", PassingToneAndResolution);
            Test("Minor foundation and fresh voicing", MinorFoundation);
            Test("History and common-tone continuity", HistoryContinuity);
            Test("Same destination, different harmonic history", SameDestinationDifferentHistory);
            Test("Pending harmony cancellation", PendingCancellation);
            Test("Velocity and note-order invariance", VelocityAndOrder);
            Test("Silence and new phrase", SilenceAndNewPhrase);
            Test("OBS snapshot isolation", ObsSnapshot);
            app.Shutdown();
            Console.WriteLine("HarmonyHistoryTests: " + (failures == 0 ? "PASS" : "FAIL") +
                " (" + checks + " assertions, " + failures + " failed groups).");
            return failures == 0 ? 0 : 1;
        }

        private static void ChordMembership()
        {
            Check(MusicTheory.ChordPitchMask(null) == 0, "null chord has no members");
            Check(MusicTheory.ChordPitchMask(new ChordResult()) == 0, "unknown chord has no invented members");
            for (int root = 0; root < 12; root++)
            {
                int c = 48 + root;
                var minor = MusicTheory.Recognize(new[] { c, c + 3, c + 7 }, false);
                Check(MusicTheory.ChordPitchMask(minor) == Mask(c, c + 3, c + 7), "minor membership transposes " + root);
                var seventh = MusicTheory.Recognize(new[] { c, c + 4, c + 10 }, false);
                Check(seventh.Symbol.Contains("no5"), "seventh shell retains omission metadata " + root);
                Check(MusicTheory.ChordPitchMask(seventh) == Mask(c, c + 4, c + 7, c + 10),
                    "omitted fifth remains theoretical member " + root);
                var power = MusicTheory.Recognize(new[] { c, c + 7 }, false);
                Check(MusicTheory.ChordPitchMask(power) == Mask(c, c + 7), "power chord has no invented third " + root);
            }
            var slash = MusicTheory.Recognize(new[] { 39, 60, 64, 67 }, false);
            Check(slash.Symbol == "C/D#", "fixture is an independent bass under a complete C triad");
            Check(MusicTheory.ChordPitchMask(slash) == Mask(60, 64, 67), "slash bass stays outside upper chord membership");
        }

        private static void PassingToneAndResolution()
        {
            var r = new Rig(); r.Replace(60, 64, 67); r.Advance(2);
            string baseTint = r.Engine.Hex;
            var core = r.Colored();
            SavePreview(r, "01-major");
            r.Strike(66); r.Advance(.5);
            var altered = r.Colored();
            Check(r.Engine.Chord.Root == 0 && r.Engine.Chord.Quality == "", "F sharp retains the C foundation");
            Check(r.Engine.Hex == baseTint, "passing sharp eleven does not repaint the whole foundation");
            foreach (int n in new[] { 60, 64, 67 })
                Check(Distance(Note(core, n), Note(altered, n)) <= 2, "common note keeps its base tint " + n);
            Check(Distance(Note(altered, 66), Note(altered, 64)) > 40, "sharp eleven receives a clearly different local color");
            Check(r.Engine.VariationStrength > .7, "passing tone contributes a visible relation gradient");
            SavePreview(r, "02-passing-f-sharp");
            r.Off(66); r.Strike(67); r.Tick();
            Check(r.Engine.VariationStrength > .5, "passing-tone atmosphere releases continuously");
            r.Advance(1.8);
            Check(r.Engine.VariationStrength == 0 && r.Engine.Hex == baseTint, "resolution settles to the original foundation");
            SavePreview(r, "03-passing-resolution");
        }

        private static void MinorFoundation()
        {
            var r = new Rig(); r.Replace(50, 65, 69); r.Advance(2);
            string minor = r.Engine.Hex;
            r.Pedal(true); r.Off(69); r.Strike(70); r.Advance(.45);
            var notes = r.Colored();
            Check(r.Engine.Chord.Root == 2 && r.Engine.Chord.Quality == "m", "retained A keeps the D minor foundation under B flat");
            Check(r.Engine.Hex == minor, "D minor preserves its base color");
            Check(Distance(Note(notes, 70), Note(notes, 65)) > 40, "B flat has its own passing-tone color");
            foreach (int pitch in new[] { 50, 65, 70 }) { r.Strike(pitch); r.Tick(); }
            r.Advance(4.5);
            Check(r.Engine.Chord.Root == 10 && r.Engine.Group == "10:major", "three fresh notes establish B flat major");
            Check(r.Engine.Hex != minor, "a newly articulated foundation completes the palette change");

            // An old pedal tone must retain its old sound color without becoming
            // a chord member in the newly established, physically held harmony.
            var pedal = new Rig(); pedal.Replace(48, 64, 67); pedal.Advance(2);
            string cColor = ColorText(Note(pedal.Colored(), 48));
            pedal.Pedal(true); pedal.Replace(43, 59, 62, 65); pedal.Advance(1.4);
            Check(pedal.Engine.Group == "7:dominant", "new held G7 takes priority over the old C pedal residue");
            Check((MusicTheory.ChordPitchMask(pedal.Engine.Chord) & 1) == 0, "old pedal C is not part of the G7 membership");
            Check(ColorText(Note(pedal.Colored(), 48)) == cColor, "old pedal C retains its original tint");
        }

        private static void HistoryContinuity()
        {
            var r = new Rig(); r.Replace(60, 64, 67); r.Advance(2);
            string oldBase = r.Engine.Hex;
            var before = r.Colored();
            r.Replace(60, 63, 67); r.Advance(.2);
            var change = r.Colored();
            Check(r.Engine.Group == "0:minor", "fresh C minor voicing is confirmed");
            Check(r.Engine.MemoryHex == oldBase && r.Engine.HistoryStrength > .98, "new harmony remembers the preceding visible palette");
            Check(r.Engine.Hex == oldBase, "foundation begins the new transition from its previous color");
            foreach (int n in new[] { 60, 67 })
                Check(Distance(Note(before, n), Note(change, n)) <= 2, "shared note is continuous at confirmation " + n);
            r.Advance(.35);
            Check(r.Engine.Hex != oldBase && r.Engine.HistoryStrength > .85, "transition advances while previous harmony remains visible");
            Check(r.Engine.RelationHex != r.Engine.Hex && r.Engine.RelationHex != r.Engine.MemoryHex,
                "relationship supplies a third bridge color");
            SavePreview(r, "04-c-minor-transition");
            r.Advance(4);
            string minor = r.Engine.Hex;
            Check(r.Engine.HistoryStrength == 0 && minor != oldBase, "history fades after the new harmony settles");
            var settled = r.Colored();
            Check(Distance(Note(settled, 60), Note(settled, 63)) <= 2, "newly admitted core note converges to its established base");
            SavePreview(r, "05-c-minor-settled");
            r.Replace(60, 64, 67); r.Advance(.55);
            Check(r.Engine.MemoryHex == minor && r.Engine.HistoryStrength > .8, "return to major remembers the minor departure");
            SavePreview(r, "06-major-return");
        }

        private static void PendingCancellation()
        {
            var r = new Rig(); r.Replace(60, 64, 67); r.Advance(2);
            r.Replace(45, 60, 64); r.Advance(.1);
            Check(r.Engine.Group == "0:major", "a short alternate voicing has not changed the color foundation");
            r.Replace(60, 64, 67); r.Advance(.25);
            r.Replace(45, 60, 64); r.Advance(.1);
            Check(r.Engine.Group == "0:major", "second appearance cannot reuse an abandoned confirmation timer");
            r.Advance(.125);
            Check(r.Engine.Group == "9:minor", "new candidate eventually receives its own full confirmation interval");
        }

        private static void SameDestinationDifferentHistory()
        {
            var resolution = new Rig(); var distant = new Rig();
            resolution.Replace(43, 59, 62, 65); resolution.Advance(2);
            distant.Replace(54, 70, 73); distant.Advance(2);
            Check(resolution.Engine.Group == "7:dominant" && distant.Engine.Group == "6:major",
                "fixtures begin in G7 and distant F sharp major");
            resolution.Replace(60, 64, 67); distant.Replace(60, 64, 67);
            resolution.Advance(.55); distant.Advance(.55);
            Check(resolution.Engine.Group == "0:major" && distant.Engine.Group == "0:major",
                "both histories arrive at the same C major destination");
            Check(resolution.Engine.MemoryHex != distant.Engine.MemoryHex && resolution.Engine.Hex != distant.Engine.Hex,
                "same destination has different remembered and currently blended colors");
            resolution.Advance(1.3); distant.Advance(1.3);
            Check(resolution.Engine.HistoryStrength > .4 && distant.Engine.HistoryStrength > .4,
                "relationship remains visible after the foundation transition finishes");
            Check(resolution.Engine.Hex == distant.Engine.Hex && resolution.Engine.RelationHex != distant.Engine.RelationHex,
                "identical settled C bases retain distinct resolution and distant-motion gradient colors");
            var releaseColor = (Color)ColorConverter.ConvertFromString(resolution.Engine.RelationHex);
            var distantColor = (Color)ColorConverter.ConvertFromString(distant.Engine.RelationHex);
            Check(releaseColor.R > releaseColor.B && distantColor.B > distantColor.R,
                "dominant resolution has an amber bridge and distant motion a violet bridge");
            resolution.Advance(3); distant.Advance(3);
            Check(resolution.Engine.HistoryStrength == 0 && distant.Engine.HistoryStrength == 0 &&
                resolution.Engine.Hex == distant.Engine.Hex, "both histories eventually converge to the same C foundation");
            var a = resolution.Colored(); var b = distant.Colored();
            Check(a.Zip(b, (x, y) => Distance(x, y) <= 2).All(same => same),
                "history-dependent core note colors also converge after the phrase settles");
        }

        private static void VelocityAndOrder()
        {
            var soft = new Rig(20); var loud = new Rig(120);
            soft.Replace(60, 64, 67); loud.Replace(60, 64, 67);
            CompareSequence(soft, loud, 60);
            soft.Strike(66); loud.Strike(66); CompareSequence(soft, loud, 25);
            soft.Pedal(true); loud.Pedal(true); soft.Off(66); loud.Off(66);
            CompareSequence(soft, loud, 35);
            soft.Replace(45, 60, 64); loud.Replace(45, 60, 64);
            CompareSequence(soft, loud, 100);
            string tint = loud.Engine.Hex;
            var original = loud.Colored();
            loud.Engine.Update(original.Reverse().ToList(), true);
            Check(loud.Engine.Hex == tint, "reordered snapshots do not restart a palette transition");
            loud.Pedal(false); loud.Replace(60, 64, 67); loud.Advance(5);
            tint = loud.Engine.Hex;
            loud.Strike(72); loud.Tick(); loud.Advance(.4);
            Check(loud.Engine.Hex == tint && loud.Engine.HistoryStrength == 0, "octave duplication retains the same established harmony");
        }

        private static void CompareSequence(Rig soft, Rig loud, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                soft.Advance(.025); loud.Advance(.025);
                Check(soft.Engine.Hex == loud.Engine.Hex && soft.Engine.MemoryHex == loud.Engine.MemoryHex &&
                    soft.Engine.RelationHex == loud.Engine.RelationHex, "all gradient RGB values are velocity independent");
                Check(soft.Engine.VariationStrength == loud.Engine.VariationStrength &&
                    soft.Engine.HistoryStrength == loud.Engine.HistoryStrength &&
                    soft.Engine.AtmosphereStrength == loud.Engine.AtmosphereStrength,
                    "velocity never changes relationship classification or atmosphere hue weighting");
                var a = soft.Colored(); var b = loud.Colored();
                Check(a.Count == b.Count && a.Zip(b, (x, y) => Distance(x, y) == 0).All(same => same),
                    "local note RGB is independent of strike velocity throughout the phrase");
            }
        }

        private static void SilenceAndNewPhrase()
        {
            var r = new Rig(); string neutral = r.Engine.Hex;
            r.Replace(60, 64, 67); r.Advance(2); r.Strike(66); r.Advance(.4);
            double active = r.Engine.AtmosphereStrength;
            r.State.Clear(); r.Tick();
            Check(r.Engine.AtmosphereStrength == active, "atmosphere does not pop away at silence");
            r.Advance(.5);
            Check(r.Engine.AtmosphereStrength > 0 && r.Engine.AtmosphereStrength < active, "atmosphere gently fades during silence");
            r.Advance(7);
            Check(r.Engine.AtmosphereStrength == 0 && r.Engine.VariationStrength == 0 && r.Engine.HistoryStrength == 0,
                "silence removes all harmonic atmosphere and motion");
            Check(r.Engine.Hex == neutral && r.Engine.Group == "", "long silence resets the neutral foundation");
            r.Replace(50, 65, 69); r.Advance(.15);
            Check(r.Engine.Group == "2:minor" && r.Engine.HistoryStrength == 0,
                "next phrase starts without obsolete harmonic history or pending confirmation");
        }

        private static void ObsSnapshot()
        {
            var r = new Rig(); r.Replace(60, 64, 67); r.Advance(2); r.Strike(66); r.Advance(.5);
            var settings = Settings(r); var notes = r.Colored();
            var expectedSettings = settings.Snapshot(); var expectedNotes = notes.Select(Clone).ToList();
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var ready = new ManualResetEvent(false))
            {
                int callbacks = 0; byte[] actual = null; Exception error = null;
                var worker = new ObsFrameWorker(delegate(byte[] png)
                {
                    if (Interlocked.Increment(ref callbacks) == 1)
                    { entered.Set(); release.WaitOne(20000); }
                    else { actual = png; ready.Set(); }
                }, delegate(Exception ex) { error = ex; ready.Set(); });
                var thread = (Thread)typeof(ObsFrameWorker).GetField("thread", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
                try
                {
                    worker.Submit(new AppSettings(), null, "", "", "", false);
                    Check(entered.WaitOne(20000), "worker is held after its first immutable frame");
                    worker.Submit(settings, notes, "C", "", "", false);
                    settings.HarmonyTint = "#00FF00"; settings.HarmonyMemoryTint = "#FF0000";
                    settings.HarmonyRelationTint = "#0000FF"; settings.HarmonyHistoryStrength = 1;
                    settings.HarmonyVariationStrength = 0; settings.HarmonyAtmosphereStrength = 0;
                    foreach (var note in notes) { note.TintR = 0; note.TintG = 255; note.TintB = 0; note.HasTint = false; }
                    notes.Clear(); release.Set();
                    Check(ready.WaitOne(20000) && error == null && actual != null, "worker renders the pending colorful snapshot");
                    worker.Dispose(); Check(thread.Join(10000), "worker shuts down after snapshot test");
                    byte[] expected = new ObsFrameRenderer().Render(expectedSettings, expectedNotes, "C", "", "", false);
                    Check(SamePixels(expected, actual), "desktop RGB, gradient history and atmosphere survive immutable OBS snapshotting");
                }
                finally { release.Set(); worker.Dispose(); thread.Join(10000); }
            }
        }

        private static AppSettings Settings(Rig r)
        {
            return new AppSettings { HarmonyTint = r.Engine.Hex, HarmonyMemoryTint = r.Engine.MemoryHex,
                HarmonyRelationTint = r.Engine.RelationHex, HarmonyHistoryStrength = r.Engine.HistoryStrength,
                HarmonyVariationStrength = r.Engine.VariationStrength, HarmonyAtmosphereStrength = r.Engine.AtmosphereStrength,
                Background = "#0B1420", BackgroundOpacity = 100, AtmosphereOpacity = 12 };
        }
        private static void SavePreview(Rig r, string name)
        {
            var notes = r.Colored();
            // Equal display energy in these synthetic examples makes their color
            // relationships comparable independently from note-age fading.
            foreach (var note in notes) note.Brightness = note.IsHeld ? .9 : .2;
            byte[] png = new ObsFrameRenderer().Render(Settings(r), notes,
                r.Engine.Chord.IsRecognized ? r.Engine.Chord.Symbol : "", "", "", r.State.SustainDown);
            File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".png"), png);
        }
        private static bool SamePixels(byte[] a, byte[] b)
        {
            using (var sa = new MemoryStream(a)) using (var sb = new MemoryStream(b))
            {
                var fa = BitmapFrame.Create(sa, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var fb = BitmapFrame.Create(sb, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (fa.PixelWidth != fb.PixelWidth || fa.PixelHeight != fb.PixelHeight || fa.Format != fb.Format) return false;
                int stride = (fa.PixelWidth * fa.Format.BitsPerPixel + 7) / 8;
                var pa = new byte[stride * fa.PixelHeight]; var pb = new byte[pa.Length];
                fa.CopyPixels(pa, stride, 0); fb.CopyPixels(pb, stride, 0); return pa.SequenceEqual(pb);
            }
        }
        private static ActiveNote Clone(ActiveNote n)
        { return new ActiveNote { Number = n.Number, Velocity = n.Velocity, IsHeld = n.IsHeld, Brightness = n.Brightness,
            StrikeId = n.StrikeId, HasTint = n.HasTint, TintR = n.TintR, TintG = n.TintG, TintB = n.TintB }; }
        private static int Mask(params int[] notes) { int mask = 0; foreach (int n in notes) mask |= 1 << (n % 12); return mask; }
        private static ActiveNote Note(IList<ActiveNote> notes, int number) { return notes.Single(n => n.Number == number); }
        private static string ColorText(ActiveNote n) { return n.TintR + ":" + n.TintG + ":" + n.TintB; }
        private static int Distance(ActiveNote a, ActiveNote b)
        { return Math.Max(Math.Abs(a.TintR - b.TintR), Math.Max(Math.Abs(a.TintG - b.TintG), Math.Abs(a.TintB - b.TintB))); }

        private sealed class Rig
        {
            public double Now;
            public readonly NoteState State;
            public readonly HarmonyColor Engine;
            private readonly int velocity;
            public Rig(int strikeVelocity = 100)
            { velocity = strikeVelocity; State = new NoteState(delegate { return Now; }); Engine = new HarmonyColor(delegate { return Now; }); }
            public void Strike(int pitch) { State.Process(0x90, pitch, velocity); }
            public void Off(int pitch) { State.Process(0x80, pitch, 0); }
            public void Pedal(bool down) { State.Process(0xB0, 64, down ? 127 : 0); }
            public void Replace(params int[] pitches)
            { foreach (var note in State.GetActiveNotes().Where(n => n.IsHeld)) Off(note.Number); foreach (int pitch in pitches) Strike(pitch); Tick(); }
            public void Tick() { Engine.Update(State.GetActiveNotes(), true); }
            public void Advance(double duration)
            { double end = Now + duration; while (Now < end - .000000001) { Now = Math.Min(end, Now + .025); Tick(); } }
            public IList<ActiveNote> Colored() { var notes = State.GetActiveNotes(); Engine.ApplyNoteTints(notes); return notes; }
        }
    }
}
