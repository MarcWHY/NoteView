using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NoteView
{
    // An expressive visual grammar, rather than a measurement of musical emotion.
    // Recognition chooses the foundation; this layer remembers how we arrived there.
    public sealed class HarmonyColor
    {
        private static readonly Tone Neutral = new Tone(164, 188, 203);
        private readonly Func<double> clock;
        private readonly HarmonicContext context = new HarmonicContext();
        private readonly Dictionary<int, Voice> voices = new Dictionary<int, Voice>();
        private ChordResult basis;
        private string stable = "", identity = "", pending = "", lastVisual = "";
        private int basisMask, coreMask, previousMask;
        private double pendingAt, silenceAt = -1, transitionAt = -10, duration = .7;
        private double lastUpdate = double.NaN, atmosphere, variation;
        private bool hasHistory;
        private Tone from = Neutral, target = Neutral, memory = Neutral, motion = Neutral;
        private Tone current = Neutral, relation = Neutral;

        public ChordResult Chord { get; private set; }
        public string Hex { get; private set; }
        public string MemoryHex { get; private set; }
        public string RelationHex { get; private set; }
        public string Group { get { return stable; } }
        public double HistoryStrength { get; private set; }
        public double VariationStrength { get; private set; }
        public double AtmosphereStrength { get; private set; }

        public HarmonyColor() : this(delegate { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }) { }
        public HarmonyColor(Func<double> seconds)
        {
            clock = seconds;
            Hex = MemoryHex = RelationHex = Neutral.Hex;
        }

        public bool Update(IList<ActiveNote> notes, bool includeSustain, bool flats = false)
        {
            double now = clock();
            double dt = double.IsNaN(lastUpdate) ? 0 : Math.Max(0, now - lastUpdate);
            lastUpdate = now;
            var source = (notes ?? new List<ActiveNote>()).Where(n => n != null && n.Number >= 0 && n.Number <= 127).ToList();
            var sounding = source.Where(n => n.IsHeld || (includeSustain && n.Brightness > 0)).ToList();
            Chord = context.Resolve(sounding, includeSustain, flats);
            if (sounding.Count == 0)
            {
                pending = "";
                if (silenceAt < 0) silenceAt = now;
                if (now - silenceAt >= 1.5 && basis != null)
                {
                    from = Current(now); target = Neutral; motion = Neutral;
                    transitionAt = now; duration = .8; hasHistory = false;
                    basis = null; identity = stable = ""; basisMask = coreMask = previousMask = 0;
                }
            }
            else
            {
                silenceAt = -1;
                string next = Chord.IsRecognized ? Chord.Root + ":" + Chord.Quality : "";
                if (next == "" || next == identity) pending = "";
                else
                {
                    if (pending != next) { pending = next; pendingAt = now; }
                    if (now - pendingAt >= (basis == null ? .08 : .18))
                    { Confirm(Chord, next, now); pending = ""; }
                }
            }

            current = Current(now);
            HistoryStrength = hasHistory ? Smooth(Clamp(1 - (now - transitionAt) / 3.2)) : 0;
            Tone accent = motion;
            double outsideWeight = 0;
            foreach (ActiveNote note in sounding.OrderBy(n => n.Number))
            {
                if (basis == null || (basisMask & (1 << (note.Number % 12))) != 0) continue;
                double weight = note.IsHeld ? 1 : Clamp(note.Brightness) * .5;
                if (weight <= 0) continue;
                Tone tint = PitchAccent(note.Number % 12, target);
                accent = outsideWeight == 0 ? tint : Blend(accent, tint, weight / (outsideWeight + weight));
                outsideWeight += weight;
            }
            double wantedVariation = Clamp(outsideWeight);
            variation = Approach(variation, wantedVariation, dt, .22);
            if (Math.Abs(variation - wantedVariation) < .002) variation = wantedVariation;
            VariationStrength = variation;
            // Preserve the passing tone's hue during its short visual release.
            if (outsideWeight > 0) relation = accent;
            else if (variation == 0) relation = motion;

            double energy = basis == null || sounding.Count == 0 ? 0 :
                sounding.Any(n => n.IsHeld) ? 1 : sounding.Max(n => Clamp(n.Brightness));
            atmosphere = Approach(atmosphere, energy, dt, energy > atmosphere ? .3 : .9);
            if (Math.Abs(atmosphere - energy) < .002) atmosphere = energy;
            AtmosphereStrength = atmosphere;

            UpdateVoices(source, now, dt);
            Hex = current.Hex; MemoryHex = memory.Hex; RelationHex = relation.Hex;
            // Quantized visual state prevents incessant repaint after settling while
            // still reporting local note-color and atmosphere changes independently.
            string visual = Hex + MemoryHex + RelationHex + ":" + Q(HistoryStrength) + ":" +
                Q(VariationStrength) + ":" + Q(AtmosphereStrength) + ":" +
                string.Join(",", voices.OrderBy(v => v.Key).Select(v => v.Key + v.Value.Color.Hex));
            bool changed = visual != lastVisual; lastVisual = visual;
            return changed;
        }

        public void ApplyNoteTints(IList<ActiveNote> notes)
        {
            if (notes == null) return;
            foreach (ActiveNote note in notes)
            {
                Voice voice;
                Tone tint = voices.TryGetValue(note.Number, out voice) ? voice.Color : current;
                note.HasTint = true; note.TintR = tint.ByteR; note.TintG = tint.ByteG; note.TintB = tint.ByteB;
            }
        }

        private void Confirm(ChordResult next, string nextIdentity, double now)
        {
            Tone visible = Current(now);
            int mask = MusicTheory.ChordPitchMask(next);
            string family = Family(next.Quality);
            Tone nextTone = Palette(next.Root, family);
            hasHistory = basis != null;
            previousMask = basisMask;
            memory = visible;
            motion = hasHistory ? Relationship(basis, next, basisMask, mask, nextTone) : nextTone;
            // Shared-tone moves flow slowly; remote or more tense moves announce
            // themselves sooner without flashing the entire composition.
            double shared = Count(mask & basisMask) / (double)Math.Max(1, Count(mask | basisMask));
            duration = hasHistory ? .65 + shared * .55 : .4;
            from = visible; target = nextTone; transitionAt = now;
            basis = next; basisMask = mask; coreMask = CoreMask(next) & mask;
            identity = nextIdentity; stable = next.Root + ":" + family;
        }

        private void UpdateVoices(IList<ActiveNote> source, double now, double dt)
        {
            var present = new HashSet<int>(source.Where(n => n.IsHeld || n.Brightness > 0).Select(n => n.Number));
            foreach (int number in voices.Keys.Where(n => !present.Contains(n)).ToArray()) voices.Remove(number);
            foreach (ActiveNote note in source)
            {
                if (!present.Contains(note.Number)) continue;
                Voice voice;
                bool existing = voices.TryGetValue(note.Number, out voice);
                bool retriggered = existing && voice.StrikeId != note.StrikeId;
                int bit = 1 << (note.Number % 12);
                bool member = (basisMask & bit) != 0;
                Tone wanted = current;
                if (basis != null)
                {
                    if (!member)
                    {
                        // A pedal remnant keeps the color it sounded with. A fresh
                        // non-chord tone is colored by its interval against the basis.
                        wanted = existing && !note.IsHeld && !retriggered ? voice.Color : PitchAccent(note.Number % 12, target);
                    }
                    else if ((coreMask & bit) == 0)
                        wanted = Blend(current, PitchAccent(note.Number % 12, target), .45);
                    else if (hasHistory && (previousMask & bit) == 0)
                        wanted = Blend(current, motion, HistoryStrength * .65);
                }
                if (!existing)
                { voice = new Voice { Color = wanted }; voices[note.Number] = voice; }
                else
                {
                    double speed = member ? .2 : .1;
                    voice.Color = Blend(voice.Color, wanted, 1 - Math.Exp(-dt / speed));
                    if (voice.Color.Distance(wanted) < .5) voice.Color = wanted;
                }
                voice.StrikeId = note.StrikeId;
            }
        }

        private Tone PitchAccent(int pitch, Tone primary)
        {
            int interval = basis == null ? 0 : (pitch - basis.Root + 12) % 12;
            double hue = interval == 1 || interval == 6 ? 337 : interval == 8 ? 26 :
                interval == 10 ? 17 : interval == 11 ? 304 : interval == 2 || interval == 9 ? 177 :
                interval == 5 ? 151 : 277;
            return Blend(Hsl(hue, .72, .68), primary, .16);
        }

        private static Tone Relationship(ChordResult old, ChordResult next, int oldMask, int nextMask, Tone nextTone)
        {
            double difference = Tension(Family(next.Quality)) - Tension(Family(old.Quality));
            int common = Count(oldMask & nextMask);
            Tone movement;
            if (difference > .15) movement = Hsl(339, .73, .69); // tension blooms rose
            else if (difference < -.15) movement = Hsl(45, .72, .72); // resolution opens amber
            else if (common >= 2) movement = Hsl(174, .56, .7); // close voice-leading: jade bridge
            else movement = Hsl(263, .65, .72); // more distant shift: violet bridge
            return Blend(movement, nextTone, .24);
        }

        public static string Family(string quality)
        {
            string q = quality ?? "";
            if (q.Contains("dim") || q.Contains("m7b5") || q.Contains("m9b5") || q.Contains("m11b5")) return "diminished";
            if (q.Contains("aug") || q.Contains("#5")) return "augmented";
            if (q.Contains("b9") || q.Contains("#9") || q.Contains("b13") || q.Contains("b5")) return "altered";
            if (q.Contains("sus") || q == "5" || q.Contains("no3")) return "suspended";
            if (q.StartsWith("m", StringComparison.Ordinal) && !q.StartsWith("maj", StringComparison.Ordinal)) return "minor";
            if (q.StartsWith("7", StringComparison.Ordinal) || q.StartsWith("9", StringComparison.Ordinal) ||
                q.StartsWith("11", StringComparison.Ordinal) || q.StartsWith("13", StringComparison.Ordinal)) return "dominant";
            return "major";
        }
        private static int CoreMask(ChordResult chord)
        {
            string q = chord.Quality;
            bool minor = q.StartsWith("m", StringComparison.Ordinal) && !q.StartsWith("maj", StringComparison.Ordinal);
            int third = q.Contains("sus2") ? 2 : q.Contains("sus4") ? 5 : minor || q.Contains("dim") ? 3 : 4;
            int fifth = q.Contains("dim") || q.Contains("b5") ? 6 : q.Contains("aug") || q.Contains("#5") ? 8 : 7;
            int mask = (1 << chord.Root) | (1 << ((chord.Root + fifth) % 12));
            if (q != "5" && !q.Contains("no3")) mask |= 1 << ((chord.Root + third) % 12);
            return mask;
        }
        private static double Tension(string family)
        { return family == "major" ? .12 : family == "minor" ? .25 : family == "suspended" ? .38 : family == "dominant" ? .58 : family == "augmented" ? .72 : family == "diminished" ? .86 : 1; }
        private static Tone Palette(int root, string family)
        {
            double hue = family == "major" ? 42 : family == "minor" ? 220 : family == "suspended" ? 169 :
                family == "dominant" ? 17 : family == "diminished" ? 275 : family == "augmented" ? 310 : 343;
            return Hsl((hue + ((root * 7) % 12 - 5.5) * 3 + 360) % 360,
                family == "altered" || family == "dominant" ? .67 : .54, .69);
        }
        private Tone Current(double now)
        { return Blend(from, target, Smooth(Clamp((now - transitionAt) / duration))); }
        private static Tone Hsl(double hue, double saturation, double light)
        {
            double c = (1 - Math.Abs(2 * light - 1)) * saturation, h = hue / 60;
            double x = c * (1 - Math.Abs(h % 2 - 1)), m = light - c / 2, r = 0, g = 0, b = 0;
            if (h < 1) { r = c; g = x; } else if (h < 2) { r = x; g = c; }
            else if (h < 3) { g = c; b = x; } else if (h < 4) { g = x; b = c; }
            else if (h < 5) { r = x; b = c; } else { r = c; b = x; }
            return new Tone((r + m) * 255, (g + m) * 255, (b + m) * 255);
        }
        private static Tone Blend(Tone a, Tone b, double t)
        { t = Clamp(t); return new Tone(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t); }
        private static int Count(int bits) { int n = 0; for (; bits != 0; bits &= bits - 1) n++; return n; }
        private static double Approach(double a, double b, double dt, double tau) { return a + (b - a) * (1 - Math.Exp(-dt / tau)); }
        private static double Clamp(double v) { return double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v)); }
        private static double Smooth(double t) { return t * t * (3 - 2 * t); }
        private static int Q(double value) { return (int)Math.Round(Clamp(value) * 255); }
        private sealed class Voice { public long StrikeId; public Tone Color; }
        private struct Tone
        {
            public double R, G, B;
            public Tone(double r, double g, double b) { R = r; G = g; B = b; }
            public byte ByteR { get { return (byte)Q(R / 255); } }
            public byte ByteG { get { return (byte)Q(G / 255); } }
            public byte ByteB { get { return (byte)Q(B / 255); } }
            public string Hex { get { return "#" + ByteR.ToString("X2") + ByteG.ToString("X2") + ByteB.ToString("X2"); } }
            public double Distance(Tone other) { return Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B))); }
        }
    }
}
