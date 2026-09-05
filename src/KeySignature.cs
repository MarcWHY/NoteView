using System;
using System.Globalization;

namespace NoteView
{
    /// <summary>A MIDI pitch spelled for a staff and its current key signature.</summary>
    public sealed class NotatedPitch
    {
        // C4 is diatonic step 28; Letter is C=0, D=1, ..., B=6.
        public int Step;
        public int Letter;
        public int Alteration;
        public string Name;
        // The accidental to draw on this note, relative to the key signature.
        public string Accidental;
        public bool InKey;
    }

    /// <summary>
    /// Stateless notation for the 15 conventional key signatures. Negative fifths
    /// are flats; positive fifths are sharps. Major and relative minor share spelling.
    /// </summary>
    public static class KeySignature
    {
        private static readonly int[] NaturalPitches = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
        private static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };
        private static readonly string[] MajorNames =
            { "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#" };
        private static readonly string[] MinorNames =
            { "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#" };

        public static string DisplayName(int fifths, bool minor)
        {
            return (minor ? MinorNames : MajorNames)[Normalize(fifths) + 7] + (minor ? " 小调" : " 大调");
        }

        public static bool UsesFlats(int fifths, bool fallback)
        {
            fifths = Normalize(fifths);
            return fifths == 0 ? fallback : fifths < 0;
        }

        public static int GetAlteration(int letter, int fifths)
        {
            if (letter < 0 || letter > 6) throw new ArgumentOutOfRangeException("letter");
            fifths = Normalize(fifths);
            int[] order = fifths < 0 ? FlatOrder : SharpOrder;
            for (int index = 0; index < Math.Abs(fifths); index++)
                if (order[index] == letter) return fifths < 0 ? -1 : 1;
            return 0;
        }

        public static NotatedPitch Spell(int midi, int fifths, bool preferFlats)
        {
            if (midi < 0 || midi > 127) throw new ArgumentOutOfRangeException("midi");
            fifths = Normalize(fifths);
            int pitchClass = midi % 12;

            // Prefer the seven key tones, including E#, B#, Cb and Fb. This must
            // precede the natural-note fallback: C in C# major is spelled B#.
            for (int letter = 0; letter < 7; letter++)
            {
                int alteration = GetAlteration(letter, fifths);
                if (Mod12(NaturalPitches[letter] + alteration) == pitchClass)
                    return CreatePitch(midi, letter, alteration, fifths);
            }

            // A white key outside the signature restores its natural letter.
            // Thus F in D major gets a natural rather than the spelling E#.
            for (int letter = 0; letter < 7; letter++)
                if (NaturalPitches[letter] == pitchClass)
                    return CreatePitch(midi, letter, 0, fifths);

            // MIDI alone cannot establish a chromatic note's harmonic function.
            // Use the signature's polarity, or the user's preference in C / Am.
            int chromaticAlteration = UsesFlats(fifths, preferFlats) ? -1 : 1;
            for (int letter = 0; letter < 7; letter++)
                if (Mod12(NaturalPitches[letter] + chromaticAlteration) == pitchClass)
                    return CreatePitch(midi, letter, chromaticAlteration, fifths);

            throw new InvalidOperationException("The MIDI pitch could not be spelled.");
        }

        private static NotatedPitch CreatePitch(int midi, int letter, int alteration, int fifths)
        {
            // Subtract the spelling before finding the octave, so C4 -> B#3
            // and B3 -> Cb4 keep both the pitch and the staff position correct.
            int octave = (midi - NaturalPitches[letter] - alteration) / 12 - 1;
            bool inKey = alteration == GetAlteration(letter, fifths);
            return new NotatedPitch
            {
                Step = octave * 7 + letter,
                Letter = letter,
                Alteration = alteration,
                Name = "CDEFGAB"[letter].ToString() + (alteration < 0 ? "b" : alteration > 0 ? "#" : "")
                    + octave.ToString(CultureInfo.InvariantCulture),
                Accidental = inKey ? "" : alteration == 0 ? "♮" : alteration < 0 ? "♭" : "♯",
                InKey = inKey
            };
        }

        private static int Normalize(int fifths)
        {
            // Corrupt or older persisted settings fall back to C major / A minor.
            return fifths < -7 || fifths > 7 ? 0 : fifths;
        }

        private static int Mod12(int pitch)
        {
            return (pitch % 12 + 12) % 12;
        }
    }
}
