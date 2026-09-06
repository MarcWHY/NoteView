using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NoteView
{
    public sealed class ActiveNote
    {
        public int Number;
        public int Velocity;
        public bool IsHeld;
        public double Brightness = 1;
        public long StrikeId;
    }

    /// <summary>
    /// Tracks MIDI input independently for each channel. Returned notes are snapshots,
    /// aggregated by pitch for display. A retrigger replaces the previous key press.
    /// </summary>
    public sealed class NoteState
    {
        private readonly object sync = new object();
        private readonly int[,] velocities = new int[16, 128];
        private readonly bool[,] held = new bool[16, 128];
        private readonly bool[] sustain = new bool[16];
        private readonly long[,] strikes = new long[16, 128];
        private long strikeSequence;
        private readonly double[,] attack = new double[16, 128], release = new double[16, 128];
        private readonly Func<double> clock;
        public NoteState() : this(delegate { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }) { }
        public NoteState(Func<double> seconds) { clock = seconds; }
        private static double HeldLevel(double age)
        {
            return Envelope(age, age);
        }
        private static double Envelope(double age, double tailAge)
        {
            age = Math.Max(0, age);
            // Keep the short strike highlight, but let physically held notes fade slowly.
            return .14 + .72 * Math.Exp(-age / .10) + .14 * Math.Exp(-Math.Max(0, tailAge) / 4.8);
        }
        private double Level(int channel, int number, double now)
        {
            if (held[channel, number]) return HeldLevel(now - attack[channel, number]);
            double age = now - attack[channel, number];
            double releasedAge = Math.Max(0, now - release[channel, number]);
            // Accelerate only the tail after release, continuously from its current level.
            double value = Envelope(age, age + 2 * releasedAge) * Math.Exp(-releasedAge / 1.8);
            return value < .003 ? 0 : value;
        }

        public bool SustainDown
        {
            get
            {
                lock (sync)
                {
                    for (int channel = 0; channel < 16; channel++)
                        if (sustain[channel]) return true;
                    return false;
                }
            }
        }

        public void Process(int status, int data1, int data2)
        {
            if (status < 0x80 || status >= 0xF0 || data1 < 0 || data1 > 127 || data2 < 0 || data2 > 127)
                return;
            int command = status & 0xF0;
            int channel = status & 0x0F;
            lock (sync)
            {
                if (command == 0x90 && data2 != 0)
                {
                    velocities[channel, data1] = data2;
                    held[channel, data1] = true;
                    attack[channel, data1] = clock();
                    strikes[channel, data1] = ++strikeSequence;
                }
                else if (command == 0x80 || (command == 0x90 && data2 == 0))
                {
                    if (held[channel, data1]) release[channel, data1] = clock();
                    held[channel, data1] = false;
                    if (!sustain[channel]) velocities[channel, data1] = 0;
                }
                else if (command == 0xB0)
                {
                    if (data1 == 64)
                    {
                        sustain[channel] = data2 >= 64;
                        if (!sustain[channel]) ReleaseSustained(channel);
                    }
                    else if (data1 == 120)
                    {
                        // All Sound Off silences even pedal-latched notes immediately.
                        for (int number = 0; number < 128; number++)
                        {
                            velocities[channel, number] = 0;
                            held[channel, number] = false;
                        }
                    }
                    else if (data1 == 121)
                    {
                        // Reset All Controllers resets the pedal, but not pressed keys.
                        sustain[channel] = false;
                        ReleaseSustained(channel);
                    }
                    else if (data1 >= 123 && data1 <= 127)
                    {
                        // All Notes Off and channel mode changes act like note-offs.
                        for (int number = 0; number < 128; number++)
                        {
                            if (held[channel, number]) release[channel, number] = clock();
                            held[channel, number] = false;
                            if (!sustain[channel]) velocities[channel, number] = 0;
                        }
                    }
                }
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                Array.Clear(velocities, 0, velocities.Length);
                Array.Clear(held, 0, held.Length);
                Array.Clear(sustain, 0, sustain.Length);
            }
        }

        public IList<ActiveNote> GetActiveNotes()
        {
            List<ActiveNote> result = new List<ActiveNote>();
            lock (sync)
            {
                double now = clock();
                for (int number = 0; number < 128; number++)
                {
                    int velocity = 0;
                    bool isHeld = false;
                    int selected = -1;
                    for (int channel = 0; channel < 16; channel++)
                    {
                        velocity = Math.Max(velocity, velocities[channel, number]);
                        isHeld |= held[channel, number];
                        if (velocities[channel, number] > 0 && (selected < 0 || attack[channel, number] > attack[selected, number])) selected = channel;
                    }
                    if (velocity > 0)
                    {
                        double brightness = Level(selected, number, now);
                        if (isHeld) brightness = Math.Max(.14, brightness);
                        result.Add(new ActiveNote { Number = number, Velocity = velocity, IsHeld = isHeld, Brightness = brightness, StrikeId = strikes[selected, number] });
                    }
                }
            }
            return result;
        }

        private void ReleaseSustained(int channel)
        {
            for (int number = 0; number < 128; number++)
                if (!held[channel, number]) velocities[channel, number] = 0;
        }
    }

    public sealed class ChordResult
    {
        public bool IsRecognized;
        public int Root = -1;
        public string Quality = "";
        public string Symbol;
        public string Description;
        public string Alternatives;
    }

    public static class MusicTheory
    {
        private static readonly string[] SharpNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private static readonly string[] FlatNames = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
        private static readonly int[] SharpSteps = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
        private static readonly int[] FlatSteps = { 0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6 };
        private static readonly string[] Intervals = { "同度或八度", "小二度", "大二度", "小三度", "大三度", "纯四度", "三全音", "纯五度", "小六度", "大六度", "小七度", "大七度" };

        private sealed class ChordPattern
        {
            public string Suffix;
            public string Description;
            public int Mask;
            public bool AllowOmittedFifth;

            public ChordPattern(string suffix, string description, params int[] intervals)
            {
                Suffix = suffix;
                Description = description;
                foreach (int interval in intervals) Mask |= 1 << interval;
                // Seventh and added-tone voicings may omit the perfect fifth.
                // Keep the root, third, and all other defining tones.
                AllowOmittedFifth = intervals.Length >= 4 && (Mask & 1) != 0 &&
                    (Mask & (1 << 7)) != 0 && (Mask & ((1 << 3) | (1 << 4))) != 0;
            }
        }

        // Exact pitch-class sets; optional fifth omissions are explicitly labeled.
        // Order provides a consistent default when the bass does not decide a root.
        private static readonly ChordPattern[] Patterns =
        {
            new ChordPattern("", "大三和弦", 0, 4, 7),
            new ChordPattern("m", "小三和弦", 0, 3, 7),
            new ChordPattern("dim", "减三和弦", 0, 3, 6),
            new ChordPattern("aug", "增三和弦", 0, 4, 8),
            new ChordPattern("sus2", "挂二和弦", 0, 2, 7),
            new ChordPattern("sus4", "挂四和弦", 0, 5, 7),
            new ChordPattern("6", "大六和弦", 0, 4, 7, 9),
            new ChordPattern("m6", "小六和弦", 0, 3, 7, 9),
            new ChordPattern("7", "属七和弦", 0, 4, 7, 10),
            new ChordPattern("maj7", "大七和弦", 0, 4, 7, 11),
            new ChordPattern("m7", "小七和弦", 0, 3, 7, 10),
            new ChordPattern("m(maj7)", "小大七和弦", 0, 3, 7, 11),
            new ChordPattern("m7b5", "半减七和弦", 0, 3, 6, 10),
            new ChordPattern("dim7", "减七和弦", 0, 3, 6, 9),
            new ChordPattern("add9", "加九和弦", 0, 2, 4, 7),
            new ChordPattern("m(add9)", "小加九和弦", 0, 2, 3, 7),
            new ChordPattern("9", "属九和弦", 0, 2, 4, 7, 10),
            new ChordPattern("maj9", "大九和弦", 0, 2, 4, 7, 11),
            new ChordPattern("m9", "小九和弦", 0, 2, 3, 7, 10),
            new ChordPattern("7sus4", "属七挂四和弦", 0, 5, 7, 10),

            // Added tones and sixth chords: no seventh is implied by "add" or "6".
            new ChordPattern("add11", "加十一和弦", 0, 4, 5, 7),
            new ChordPattern("m(add11)", "小加十一和弦", 0, 3, 5, 7),
            new ChordPattern("add#11", "加升十一和弦", 0, 4, 6, 7),
            new ChordPattern("6/9", "大六加九和弦", 0, 2, 4, 7, 9),
            new ChordPattern("m6/9", "小六加九和弦", 0, 2, 3, 7, 9),

            // Suspensions replace the third. They do not use the no5 fallback.
            new ChordPattern("7sus2", "属七挂二和弦", 0, 2, 7, 10),
            new ChordPattern("maj7sus2", "大七挂二和弦", 0, 2, 7, 11),
            new ChordPattern("maj7sus4", "大七挂四和弦", 0, 5, 7, 11),
            new ChordPattern("9sus4", "属九挂四和弦", 0, 2, 5, 7, 10),
            new ChordPattern("13sus4", "属十三挂四和弦", 0, 2, 5, 7, 9, 10),
            new ChordPattern("7sus4(b9)", "属七挂四降九和弦", 0, 1, 5, 7, 10),
            new ChordPattern("7sus4(add13)", "属七挂四加十三和弦", 0, 5, 7, 9, 10),

            // Full stacks include lower extensions. Explicit no11 patterns below
            // cover the common thirteenth voicings without the natural eleventh.
            new ChordPattern("m(maj9)", "小大九和弦", 0, 2, 3, 7, 11),
            new ChordPattern("11", "属十一和弦", 0, 2, 4, 5, 7, 10),
            new ChordPattern("maj11", "大十一和弦", 0, 2, 4, 5, 7, 11),
            new ChordPattern("m11", "小十一和弦", 0, 2, 3, 5, 7, 10),
            new ChordPattern("m(maj11)", "小大十一和弦", 0, 2, 3, 5, 7, 11),
            new ChordPattern("13", "属十三和弦", 0, 2, 4, 5, 7, 9, 10),
            new ChordPattern("maj13", "大十三和弦", 0, 2, 4, 5, 7, 9, 11),
            new ChordPattern("m13", "小十三和弦", 0, 2, 3, 5, 7, 9, 10),
            new ChordPattern("m(maj13)", "小大十三和弦", 0, 2, 3, 5, 7, 9, 11),

            // Altered fifths are structural tones and may never be omitted.
            new ChordPattern("7(b5)", "属七降五和弦", 0, 4, 6, 10),
            new ChordPattern("7(#5)", "属七升五和弦", 0, 4, 8, 10),
            new ChordPattern("maj7(b5)", "大七降五和弦", 0, 4, 6, 11),
            new ChordPattern("maj7(#5)", "大七升五和弦", 0, 4, 8, 11),
            new ChordPattern("9(b5)", "属九降五和弦", 0, 2, 4, 6, 10),
            new ChordPattern("9(#5)", "属九升五和弦", 0, 2, 4, 8, 10),
            new ChordPattern("maj9(b5)", "大九降五和弦", 0, 2, 4, 6, 11),
            new ChordPattern("maj9(#5)", "大九升五和弦", 0, 2, 4, 8, 11),
            new ChordPattern("m9b5", "半减九和弦", 0, 2, 3, 6, 10),
            new ChordPattern("m11b5", "半减十一和弦", 0, 2, 3, 5, 6, 10),

            // An altered or added extension on a seventh does not imply a ninth.
            new ChordPattern("7(b9)", "属七降九和弦", 0, 1, 4, 7, 10),
            new ChordPattern("7(#9)", "属七升九和弦", 0, 3, 4, 7, 10),
            new ChordPattern("7(#11)", "属七升十一和弦", 0, 4, 6, 7, 10),
            new ChordPattern("7(b13)", "属七降十三和弦", 0, 4, 7, 8, 10),
            new ChordPattern("7(add11)", "属七加十一和弦", 0, 4, 5, 7, 10),
            new ChordPattern("7(add13)", "属七加十三和弦", 0, 4, 7, 9, 10),
            new ChordPattern("maj7(#11)", "大七升十一和弦", 0, 4, 6, 7, 11),
            new ChordPattern("maj7(add13)", "大七加十三和弦", 0, 4, 7, 9, 11),
            new ChordPattern("m7(add11)", "小七加十一和弦", 0, 3, 5, 7, 10),
            new ChordPattern("m7(add13)", "小七加十三和弦", 0, 3, 7, 9, 10),
            new ChordPattern("m(maj7,add11)", "小大七加十一和弦", 0, 3, 5, 7, 11),
            new ChordPattern("m(maj7,add13)", "小大七加十三和弦", 0, 3, 7, 9, 11),

            new ChordPattern("9(#11)", "属九升十一和弦", 0, 2, 4, 6, 7, 10),
            new ChordPattern("9(b13)", "属九降十三和弦", 0, 2, 4, 7, 8, 10),
            new ChordPattern("13(no11)", "属十三和弦 · 省略十一音", 0, 2, 4, 7, 9, 10),
            new ChordPattern("maj9(#11)", "大九升十一和弦", 0, 2, 4, 6, 7, 11),
            new ChordPattern("maj13(no11)", "大十三和弦 · 省略十一音", 0, 2, 4, 7, 9, 11),
            new ChordPattern("m13(no11)", "小十三和弦 · 省略十一音", 0, 2, 3, 7, 9, 10),
            new ChordPattern("m(maj13,no11)", "小大十三和弦 · 省略十一音", 0, 2, 3, 7, 9, 11),

            new ChordPattern("7(b5,b9)", "属七降五降九和弦", 0, 1, 4, 6, 10),
            new ChordPattern("7(b5,#9)", "属七降五升九和弦", 0, 3, 4, 6, 10),
            new ChordPattern("7(#5,b9)", "属七升五降九和弦", 0, 1, 4, 8, 10),
            new ChordPattern("7(#5,#9)", "属七升五升九和弦", 0, 3, 4, 8, 10),
            new ChordPattern("7(b9,#11)", "属七降九升十一和弦", 0, 1, 4, 6, 7, 10),
            new ChordPattern("7(#9,#11)", "属七升九升十一和弦", 0, 3, 4, 6, 7, 10),
            new ChordPattern("7(b9,b13)", "属七降九降十三和弦", 0, 1, 4, 7, 8, 10),
            new ChordPattern("7(#9,b13)", "属七升九降十三和弦", 0, 3, 4, 7, 8, 10),
            new ChordPattern("7(b9,#11,b13)", "属七降九升十一降十三和弦", 0, 1, 4, 6, 7, 8, 10),
            new ChordPattern("7(#9,#11,b13)", "属七升九升十一降十三和弦", 0, 3, 4, 6, 7, 8, 10),
            new ChordPattern("13(b9)", "属十三降九和弦", 0, 1, 4, 5, 7, 9, 10),
            new ChordPattern("13(#9)", "属十三升九和弦", 0, 3, 4, 5, 7, 9, 10),
            new ChordPattern("13(#11)", "属十三升十一和弦", 0, 2, 4, 6, 7, 9, 10),
            new ChordPattern("maj13(#11)", "大十三升十一和弦", 0, 2, 4, 6, 7, 9, 11),
            new ChordPattern("addb9", "加降九和弦", 0, 1, 4, 7),
            new ChordPattern("m(addb9)", "小加降九和弦", 0, 1, 3, 7),
            new ChordPattern("add9(add11)", "加九加十一和弦", 0, 2, 4, 5, 7),
            new ChordPattern("m(add9,add11)", "小加九加十一和弦", 0, 2, 3, 5, 7),
            new ChordPattern("add9(#11)", "加九升十一和弦", 0, 2, 4, 6, 7),
            new ChordPattern("sus2sus4", "挂二挂四和弦", 0, 2, 5, 7),
            new ChordPattern("6sus2", "六挂二和弦", 0, 2, 7, 9),
            new ChordPattern("6sus4", "六挂四和弦", 0, 5, 7, 9),
            new ChordPattern("maj9sus4", "大九挂四和弦", 0, 2, 5, 7, 11),
            new ChordPattern("7(no3)", "属七省略三音", 0, 7, 10),
            new ChordPattern("maj7(no3)", "大七省略三音", 0, 7, 11),
            new ChordPattern("7sus4(#5)", "属七挂四升五和弦", 0, 5, 8, 10),
            new ChordPattern("dim(maj7)", "减大七和弦", 0, 3, 6, 11),
            new ChordPattern("dim7(add9)", "减七加九和弦", 0, 2, 3, 6, 9),
            new ChordPattern("m7(b9)", "小七降九和弦", 0, 1, 3, 7, 10),
            new ChordPattern("m7(b13)", "小七降十三和弦", 0, 3, 7, 8, 10),
            new ChordPattern("maj7(b9)", "大七降九和弦", 0, 1, 4, 7, 11),
            new ChordPattern("maj7(#9)", "大七升九和弦", 0, 3, 4, 7, 11),
            new ChordPattern("7(b9,add13)", "属七降九加十三和弦", 0, 1, 4, 7, 9, 10),
            new ChordPattern("7(#9,add13)", "属七升九加十三和弦", 0, 3, 4, 7, 9, 10),
            new ChordPattern("7(#11,add13)", "属七升十一加十三和弦", 0, 4, 6, 7, 9, 10),
            new ChordPattern("maj7(#11,add13)", "大七升十一加十三和弦", 0, 4, 6, 7, 9, 11)
        };

        public static int ChordPatternCount { get { return Patterns.Length; } }

        public static int OmittedFifthPatternCount
        {
            get
            {
                int count = 0;
                foreach (ChordPattern pattern in Patterns) if (pattern.AllowOmittedFifth) count++;
                return count;
            }
        }

        private sealed class Candidate
        {
            public string Symbol;
            public string Description;
            public bool OmittedFifth;
            public bool Inversion;
            public int PatternOrder;
            public int Root;
            public bool InvertedAddedFlatNinth;
        }

        public static string NoteName(int number, bool flats)
        {
            ValidateNumber(number);
            return PitchName(number % 12, flats) + (number / 12 - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string DisplaySymbol(string symbol)
        {
            return (symbol ?? "").Replace("m11b5", "ø11").Replace("m9b5", "ø9").Replace("m7b5", "ø7")
                .Replace("dim", "°").Replace("aug", "+").Replace("maj", "△").Replace("#", "♯").Replace("b", "♭");
        }

        public static int DiatonicStep(int number, bool flats)
        {
            ValidateNumber(number);
            return (number / 12 - 1) * 7 + (flats ? FlatSteps : SharpSteps)[number % 12];
        }

        public static bool IsAccidental(int number)
        {
            ValidateNumber(number);
            int pitch = number % 12;
            return pitch == 1 || pitch == 3 || pitch == 6 || pitch == 8 || pitch == 10;
        }

        public static ChordResult Recognize(IEnumerable<int> notes, bool flats)
        {
            SortedSet<int> distinctNotes = new SortedSet<int>();
            if (notes != null)
                foreach (int number in notes)
                    if (number >= 0 && number <= 127) distinctNotes.Add(number);
            if (distinctNotes.Count == 0) return Result("—", "等待演奏", "");

            int pitchMask = 0;
            List<int> pitches = new List<int>();
            int bass = distinctNotes.Min % 12;
            foreach (int number in distinctNotes)
            {
                int bit = 1 << (number % 12);
                if ((pitchMask & bit) == 0) pitches.Add(number % 12);
                pitchMask |= bit;
            }
            if (pitches.Count == 1)
            {
                if (distinctNotes.Count == 1)
                    return Result(NoteName(distinctNotes.Min, flats), "单音", "");
                return Result(PitchName(bass, flats), "同音 / 八度叠置", "");
            }
            if (pitches.Count == 2)
            {
                int interval = (pitches[1] - bass + 12) % 12;
                if (interval == 7) return Result(PitchName(bass, flats) + "5", "五度和弦", "", true, bass, "5");
                return Result(JoinPitches(pitches, flats), "双音 · " + Intervals[interval] + "（和弦未定）", "");
            }

            List<Candidate> candidates = new List<Candidate>();
            for (int root = 0; root < 12; root++)
            {
                int relativeMask = 0;
                foreach (int pitch in pitches) relativeMask |= 1 << ((pitch - root + 12) % 12);
                for (int i = 0; i < Patterns.Length; i++)
                {
                    ChordPattern pattern = Patterns[i];
                    bool omittedFifth = relativeMask != pattern.Mask;
                    if (omittedFifth && (!pattern.AllowOmittedFifth ||
                        relativeMask != (pattern.Mask & ~(1 << 7)))) continue;
                    bool inversion = bass != root;
                    candidates.Add(new Candidate
                    {
                        Symbol = PitchName(root, flats) + (omittedFifth ? AddOmittedFifth(pattern.Suffix) : pattern.Suffix) +
                            (inversion ? "/" + PitchName(bass, flats) : ""),
                        Description = pattern.Description + (omittedFifth ? " · 省略五音" : "") +
                            (inversion ? " · 转位" : ""),
                        OmittedFifth = omittedFifth,
                        Inversion = inversion,
                        PatternOrder = i,
                        Root = root
                        , InvertedAddedFlatNinth = inversion && (pattern.Mask & (1 << 1)) != 0 &&
                            (pattern.Mask & ((1 << 10) | (1 << 11))) == 0
                    });
                }
            }
            if (candidates.Count == 0)
            {
                // A separate bass under a complete upper chord, e.g. C / D.
                // Do not discard upper notes or invent an absent root.
                List<string> slash = new List<string>();
                int slashRoot = -1; string slashQuality = "";
                if (pitches.Count >= 4)
                    for (int i = 0; i < Patterns.Length; i++)
                        foreach (int root in pitches)
                        {
                            if (root == bass) continue;
                            int upperMask = 0;
                            foreach (int pitch in pitches) if (pitch != bass) upperMask |= 1 << ((pitch - root + 12) % 12);
                            if (upperMask == Patterns[i].Mask)
                            {
                                if (slash.Count == 0) { slashRoot = root; slashQuality = Patterns[i].Suffix; }
                                slash.Add(PitchName(root, flats) + Patterns[i].Suffix + "/" + PitchName(bass, flats));
                            }
                        }
                if (slash.Count > 0) return Result(slash[0], "独立低音和弦", String.Join(" · ", slash.GetRange(1, slash.Count - 1).ToArray()), true, slashRoot, slashQuality);
                return Result(JoinPitches(pitches, flats), "未匹配常见和弦 · " + pitches.Count.ToString() + " 个音级", "");
            }

            candidates.Sort(delegate(Candidate left, Candidate right)
            {
                // Lexicographic priorities cannot be overturned as the library grows.
                // Prefer a clear bass-root reading over an inverted added-b9 color chord.
                int comparison = left.InvertedAddedFlatNinth.CompareTo(right.InvertedAddedFlatNinth);
                if (comparison != 0) return comparison;
                comparison = left.OmittedFifth.CompareTo(right.OmittedFifth);
                if (comparison != 0) return comparison;
                comparison = left.Inversion.CompareTo(right.Inversion);
                if (comparison != 0) return comparison;
                comparison = left.PatternOrder.CompareTo(right.PatternOrder);
                return comparison != 0 ? comparison : left.Root.CompareTo(right.Root);
            });
            List<string> alternatives = new List<string>();
            for (int i = 1; i < candidates.Count; i++) alternatives.Add(candidates[i].Symbol);
            return Result(candidates[0].Symbol, candidates[0].Description, String.Join(" · ", alternatives.ToArray()), true,
                candidates[0].Root, Patterns[candidates[0].PatternOrder].Suffix);
        }

        private static string PitchName(int pitch, bool flats)
        {
            return (flats ? FlatNames : SharpNames)[pitch];
        }

        private static string AddOmittedFifth(string suffix)
        {
            return suffix.EndsWith(")", StringComparison.Ordinal)
                ? suffix.Substring(0, suffix.Length - 1) + ",no5)"
                : suffix + "(no5)";
        }

        private static string JoinPitches(IEnumerable<int> pitches, bool flats)
        {
            List<string> names = new List<string>();
            foreach (int pitch in pitches) names.Add(PitchName(pitch, flats));
            return String.Join(" · ", names.ToArray());
        }

        private static ChordResult Result(string symbol, string description, string alternatives, bool recognized = false, int root = -1, string quality = "")
        {
            return new ChordResult { Symbol = symbol, Description = description, Alternatives = alternatives, IsRecognized = recognized, Root = root, Quality = quality };
        }

        private static void ValidateNumber(int number)
        {
            if (number < 0 || number > 127) throw new ArgumentOutOfRangeException("number", "MIDI note number must be between 0 and 127.");
        }
    }
}
