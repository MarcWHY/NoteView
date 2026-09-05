using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NoteView
{
    // A visual interpretation of harmonic character, not a psychoacoustic measurement.
    public sealed class HarmonyColor
    {
        private readonly Func<double> clock;
        private string stable = "", pending = "";
        private readonly HarmonicContext context = new HarmonicContext();
        public ChordResult Chord { get; private set; }
        private ChordResult candidate;
        private double pendingAt, silenceAt = -1, transitionAt;
        private Tone from = new Tone(164, 188, 203), target = new Tone(164, 188, 203);
        public string Hex { get; private set; }
        public string Group { get { return stable; } }
        public HarmonyColor() : this(delegate { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }) { }
        public HarmonyColor(Func<double> seconds) { clock = seconds; Hex = target.Hex; }

        public bool Update(IList<ActiveNote> notes, bool includeSustain, bool flats = false)
        {
            double now = clock();
            var sounding = notes.Where(n => n.IsHeld || (includeSustain && n.Brightness > 0)).ToList();
            candidate = context.Resolve(notes, includeSustain, flats);
            Chord = candidate;
            if (sounding.Count == 0)
            {
                pending = "";
                if (silenceAt < 0) silenceAt = now;
                if (now - silenceAt >= 1.5 && stable != "")
                { stable = ""; Transition(new Tone(164, 188, 203), now); }
            }
            else
            {
                silenceAt = -1;
                string next = candidate != null && candidate.IsRecognized ? candidate.Root + ":" + Family(candidate.Quality) : "";
                if (next == "" || next == stable) pending = "";
                else
                {
                    if (pending != next) { pending = next; pendingAt = now; }
                    if (now - pendingAt >= (stable == "" ? .08 : .18))
                    {
                        stable = next; pending = "";
                        Transition(Palette(candidate.Root, Family(candidate.Quality)), now);
                    }
                }
            }
            string previous = Hex; Hex = Current(now).Hex; return previous != Hex;
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
        private static Tone Palette(int root, string family)
        {
            double hue = family == "major" ? 42 : family == "minor" ? 220 : family == "suspended" ? 169 :
                family == "dominant" ? 17 : family == "diminished" ? 275 : family == "augmented" ? 310 : 343;
            hue = (hue + ((root * 7) % 12 - 5.5) * 3 + 360) % 360;
            double saturation = family == "altered" || family == "dominant" ? .67 : .54;
            double light = .69, chroma = (1 - Math.Abs(2 * light - 1)) * saturation;
            double h = hue / 60, x = chroma * (1 - Math.Abs(h % 2 - 1)), m = light - chroma / 2;
            double r = 0, g = 0, b = 0;
            if (h < 1) { r = chroma; g = x; } else if (h < 2) { r = x; g = chroma; }
            else if (h < 3) { g = chroma; b = x; } else if (h < 4) { g = x; b = chroma; }
            else if (h < 5) { r = x; b = chroma; } else { r = chroma; b = x; }
            return new Tone((r + m) * 255, (g + m) * 255, (b + m) * 255);
        }
        private void Transition(Tone next, double now) { from = Current(now); target = next; transitionAt = now; }
        private Tone Current(double now)
        {
            double t = Math.Max(0, Math.Min(1, (now - transitionAt) / .14)); t = t * t * (3 - 2 * t);
            return new Tone(from.R + (target.R - from.R) * t, from.G + (target.G - from.G) * t, from.B + (target.B - from.B) * t);
        }
        private struct Tone
        {
            public double R, G, B;
            public Tone(double r, double g, double b) { R = r; G = g; B = b; }
            public string Hex { get { return "#" + ((int)Math.Round(R)).ToString("X2") + ((int)Math.Round(G)).ToString("X2") + ((int)Math.Round(B)).ToString("X2"); } }
        }
    }
}
