using System;
using System.Globalization;
using NoteView;

public static class KeySignatureTests
{
    private static int assertions;
    // Independently specified major scales, in order from seven flats to seven
    // sharps. The tests do not reconstruct the implementation's alteration order.
    private static readonly string[] Scales =
    {
        "Cb Db Eb Fb Gb Ab Bb", "Gb Ab Bb Cb Db Eb F", "Db Eb F Gb Ab Bb C",
        "Ab Bb C Db Eb F G", "Eb F G Ab Bb C D", "Bb C D Eb F G A", "F G A Bb C D E",
        "C D E F G A B", "G A B C D E F#", "D E F# G A B C#", "A B C# D E F# G#",
        "E F# G# A B C# D#", "B C# D# E F# G# A#", "F# G# A# B C# D# E#",
        "C# D# E# F# G# A# B#"
    };

    public static int Main()
    {
        try
        {
            TestThirtyKeyNames();
            TestKeyScales();
            TestRequestedNaturals();
            TestChromaticChoices();
            TestEnharmonicOctaves();
            TestAllMidiPitches();
            TestInputBoundaries();
            Console.WriteLine("KeySignatureTests: PASS ({0} assertions)", assertions);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("KeySignatureTests: FAIL after {0} assertions: {1}", assertions, exception);
            return 1;
        }
    }

    private static void TestThirtyKeyNames()
    {
        string[] minorNames = { "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#" };
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            Equal(Scales[fifths + 7].Split(' ')[0] + " 大调", KeySignature.DisplayName(fifths, false), "Major key label");
            Equal(minorNames[fifths + 7] + " 小调", KeySignature.DisplayName(fifths, true), "Minor key label");
            Equal(fifths < 0, KeySignature.UsesFlats(fifths, false), "Flat spelling default false");
            Equal(fifths <= 0, KeySignature.UsesFlats(fifths, true), "Flat spelling default true");
        }
    }

    private static void TestKeyScales()
    {
        int[] majorSteps = { 2, 2, 1, 2, 2, 2, 1 };
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            string[] names = Scales[fifths + 7].Split(' ');
            for (int index = 0; index < names.Length; index++)
            {
                string token = names[index];
                int letter = "CDEFGAB".IndexOf(token[0]);
                int alteration = token.EndsWith("b", StringComparison.Ordinal) ? -1 : token.EndsWith("#", StringComparison.Ordinal) ? 1 : 0;
                Equal(alteration, KeySignature.GetAlteration(letter, fifths), "Signature matches independently listed scale " + token);
                int nextPitch = ParsePitch(names[(index + 1) % names.Length] + "4");
                int midi = ParsePitch(token + "4");
                Equal(majorSteps[index], (nextPitch - midi + 12) % 12, "Expected scale follows major whole/half steps");
                Check(midi, fifths, false, token + "4", letter + 28, "", true);
                Check(midi, fifths, true, token + "4", letter + 28, "", true);
            }
        }
    }

    private static void TestRequestedNaturals()
    {
        Check(65, 2, false, "F4", 31, "♮", false);
        Check(66, 2, false, "F#4", 31, "", true);
        Check(60, 2, false, "C4", 28, "♮", false);
        Check(61, 2, false, "C#4", 28, "", true);
        Check(64, 2, false, "E4", 30, "", true);
        Check(71, -1, false, "B4", 34, "♮", false);
        Check(70, -1, false, "Bb4", 34, "", true);
        Check(64, -2, false, "E4", 30, "♮", false);
        Check(63, -2, false, "Eb4", 30, "", true);
        // Every visible note is compared to the signature, with no accidental
        // memory between calls or across simultaneously sounding notes.
        Check(65, 2, true, "F4", 31, "♮", false);
        Check(66, 2, true, "F#4", 31, "", true);
        Check(65, 2, true, "F4", 31, "♮", false);
    }

    private static void TestChromaticChoices()
    {
        Check(63, 2, true, "D#4", 29, "♯", false);
        Check(68, 2, true, "G#4", 32, "♯", false);
        Check(61, -1, false, "Db4", 29, "♭", false);
        Check(66, -2, false, "Gb4", 32, "♭", false);
        Check(61, 0, false, "C#4", 28, "♯", false);
        Check(61, 0, true, "Db4", 29, "♭", false);
        Check(60, 0, true, "C4", 28, "", true);
    }

    private static void TestEnharmonicOctaves()
    {
        Check(60, 7, false, "B#3", 27, "", true);
        Check(59, -7, true, "Cb4", 28, "", true);
        Check(65, 6, false, "E#4", 30, "", true);
        Check(64, -7, true, "Fb4", 31, "", true);
        Check(65, 7, true, "E#4", 30, "", true);
        Check(0, 7, false, "B#-2", -8, "", true);
        Check(11, -7, true, "Cb0", 0, "", true);
        Check(127, 7, false, "G9", 67, "♮", false);
        Check(127, -7, true, "G9", 67, "♮", false);
    }

    private static void TestAllMidiPitches()
    {
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            string[] scale = Scales[fifths + 7].Split(' ');
            for (int midi = 0; midi < 128; midi++)
            {
                string expectedScaleName = null;
                foreach (string token in scale)
                    if (ParsePitch(token + "4") % 12 == midi % 12) expectedScaleName = token;
                for (int preference = 0; preference < 2; preference++)
                {
                    NotatedPitch note = KeySignature.Spell(midi, fifths, preference == 1);
                    string context = "midi=" + midi + ", fifths=" + fifths + ", flats=" + preference;
                    Equal(midi, ParsePitch(note.Name), "Pitch roundtrip " + context);
                    Equal("CDEFGAB".IndexOf(note.Name[0]), note.Letter, "Name and staff letter agree " + context);
                    int octaveStart = note.Name.Length > 1 && (note.Name[1] == '#' || note.Name[1] == 'b') ? 2 : 1;
                    int octave = int.Parse(note.Name.Substring(octaveStart), CultureInfo.InvariantCulture);
                    Equal(octave * 7 + note.Letter, note.Step, "Name and staff octave agree " + context);
                    True(note.Alteration >= -1 && note.Alteration <= 1, "Single alteration range " + context);
                    Equal(expectedScaleName != null, note.InKey, "Independent scale membership " + context);
                    if (expectedScaleName != null)
                    {
                        Equal(expectedScaleName, note.Name.Substring(0, octaveStart), "Exact scale-tone spelling " + context);
                        Equal("", note.Accidental, "Scale tone omits accidental " + context);
                    }
                    else
                    {
                        True(note.Accidental == "♮" || note.Accidental == "♯" || note.Accidental == "♭", "Chromatic tone has accidental " + context);
                        if (IsWhitePitch(midi % 12))
                        {
                            Equal(0, note.Alteration, "Out-of-key white key restores natural " + context);
                            Equal("♮", note.Accidental, "Out-of-key natural carries cancellation " + context);
                        }
                    }
                }
            }
        }
    }

    private static void TestInputBoundaries()
    {
        foreach (int invalid in new int[] { -8, 8, int.MinValue, int.MaxValue })
        {
            Equal("C 大调", KeySignature.DisplayName(invalid, false), "Invalid key falls back to C");
            Equal("A 小调", KeySignature.DisplayName(invalid, true), "Invalid key falls back to Am");
            Equal(0, KeySignature.GetAlteration(3, invalid), "Invalid key has no signature accidental");
            Equal(true, KeySignature.UsesFlats(invalid, true), "Invalid key uses zero-key preference");
            Check(65, invalid, false, "F4", 31, "", true);
        }
        Throws(delegate { KeySignature.Spell(-1, 0, false); }, "Negative MIDI is rejected");
        Throws(delegate { KeySignature.Spell(128, 0, false); }, "MIDI above 127 is rejected");
        Throws(delegate { KeySignature.GetAlteration(-1, 0); }, "Negative letter is rejected");
        Throws(delegate { KeySignature.GetAlteration(7, 0); }, "Letter above B is rejected");
    }

    private static int ParsePitch(string name)
    {
        // Decode public ASCII notation independently using explicit piano notes.
        int pitch;
        switch (name[0])
        {
            case 'C': pitch = 0; break;
            case 'D': pitch = 2; break;
            case 'E': pitch = 4; break;
            case 'F': pitch = 5; break;
            case 'G': pitch = 7; break;
            case 'A': pitch = 9; break;
            case 'B': pitch = 11; break;
            default: throw new Exception("Unknown letter in " + name);
        }
        int offset = 1;
        if (name[offset] == '#') { pitch++; offset++; }
        else if (name[offset] == 'b') { pitch--; offset++; }
        return (int.Parse(name.Substring(offset), CultureInfo.InvariantCulture) + 1) * 12 + pitch;
    }

    private static bool IsWhitePitch(int pitch)
    {
        return pitch == 0 || pitch == 2 || pitch == 4 || pitch == 5 || pitch == 7 || pitch == 9 || pitch == 11;
    }

    private static void Check(int midi, int fifths, bool flats, string name, int step, string accidental, bool inKey)
    {
        NotatedPitch note = KeySignature.Spell(midi, fifths, flats);
        string context = "MIDI " + midi + " in signature " + fifths;
        Equal(name, note.Name, "Spelling: " + context);
        Equal(step, note.Step, "Staff position: " + context);
        Equal(accidental, note.Accidental, "Accidental: " + context);
        Equal(inKey, note.InKey, "Scale membership: " + context);
    }

    private static void Throws(Action action, string label)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { assertions++; return; }
        throw new Exception(label);
    }

    private static void True(bool value, string label)
    {
        assertions++;
        if (!value) throw new Exception(label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!object.Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
}
