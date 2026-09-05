using System;
using System.Collections.Generic;
using NoteView;

public static class MusicTheoryTests
{
    private static int assertions;

    public static int Main()
    {
        try
        {
            TestNoteState();
            TestNoteNames();
            TestChords();
            TestExpandedChords();
            TestOmittedFifths();
            TestRecognitionBoundaries();
            Console.WriteLine("MusicTheoryTests: PASS ({0} assertions)", assertions);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("MusicTheoryTests: FAIL after {0} assertions: {1}", assertions, exception);
            return 1;
        }
    }

    private static void TestNoteState()
    {
        NoteState state = new NoteState();
        Equal(0, state.GetActiveNotes().Count, "Initially empty");
        state.Process(0x90, 60, 95);
        CheckNote(state, 60, 95, true, "Note on");
        state.Process(0x90, 60, 0);
        Equal(0, state.GetActiveNotes().Count, "Zero-velocity note-on releases");

        state.Process(0x90, 60, 50);
        state.Process(0x90, 60, 100);
        CheckNote(state, 60, 100, true, "Retrigger replaces velocity");
        state.Process(0x80, 60, 20);
        Equal(0, state.GetActiveNotes().Count, "One off releases retrigger without stuck note");

        state.Process(0x90, 60, 70);
        state.Process(0xB0, 64, 127);
        True(state.SustainDown, "Pedal on");
        state.Process(0x80, 60, 0);
        CheckNote(state, 60, 70, false, "Pedal preserves released key");
        state.Process(0x90, 60, 40);
        CheckNote(state, 60, 40, true, "Retrigger under pedal refreshes intensity");
        state.Process(0xB0, 64, 63);
        CheckNote(state, 60, 40, true, "Pedal release preserves held key");
        True(!state.SustainDown, "Pedal threshold below 64 off");
        state.Process(0x80, 60, 0);
        Equal(0, state.GetActiveNotes().Count, "Released held note clears");

        state.Process(0x91, 60, 110);
        state.Process(0x90, 60, 50);
        Equal(1, state.GetActiveNotes().Count, "Channel unisons aggregate");
        CheckNote(state, 60, 110, true, "Aggregate uses strongest velocity");
        state.Process(0x81, 60, 0);
        CheckNote(state, 60, 50, true, "Other channel note survives");
        state.Process(0xB0, 64, 64);
        state.Process(0x80, 60, 0);
        state.Process(0xB1, 64, 0);
        CheckNote(state, 60, 50, false, "Other channel pedal has no effect");
        state.Process(0xB0, 64, 0);
        Equal(0, state.GetActiveNotes().Count, "Correct channel pedal releases note");

        state.Process(0x90, 60, 90);
        state.Process(0xB0, 64, 127);
        state.Process(0xB0, 123, 0);
        CheckNote(state, 60, 90, false, "All Notes Off respects pedal");
        state.Process(0xB0, 64, 0);
        Equal(0, state.GetActiveNotes().Count, "Pedal releases All Notes Off latch");
        state.Process(0x90, 64, 90);
        state.Process(0xB0, 123, 0);
        Equal(0, state.GetActiveNotes().Count, "All Notes Off without pedal silences");

        state.Process(0x90, 60, 75);
        state.Process(0x90, 64, 90);
        state.Process(0xB0, 64, 127);
        state.Process(0x80, 60, 0);
        state.Process(0xB0, 121, 0);
        Equal(1, state.GetActiveNotes().Count, "Reset Controllers releases sustained only");
        CheckNote(state, 64, 90, true, "Reset Controllers preserves held keys");
        True(!state.SustainDown, "Reset Controllers clears pedal");

        state.Process(0x91, 67, 55);
        state.Process(0xB0, 64, 127);
        state.Process(0x80, 64, 0);
        state.Process(0xB0, 120, 0);
        Equal(1, state.GetActiveNotes().Count, "All Sound Off only clears channel");
        CheckNote(state, 67, 55, true, "Other channel survives All Sound Off");
        state.Clear();
        Equal(0, state.GetActiveNotes().Count, "Panic clears all notes");
        True(!state.SustainDown, "Panic clears all pedals");

        state.Process(0x9F, 127, 127);
        state.Process(0x90, 0, 1);
        Equal(0, state.GetActiveNotes()[0].Number, "Snapshot is sorted");
        Equal(127, state.GetActiveNotes()[1].Number, "Channel 16 and upper MIDI boundary");
        IList<ActiveNote> snapshot = state.GetActiveNotes();
        snapshot[0].Velocity = 127;
        CheckNote(state, 0, 1, true, "Snapshot mutations do not alter state");
        state.Process(0xF8, 60, 127);
        state.Process(0x90, 128, 127);
        state.Process(0x90, 60, -1);
        Equal(2, state.GetActiveNotes().Count, "Malformed and non-note messages ignored");
        state.Clear();
    }

    private static void TestNoteNames()
    {
        Equal("C4", MusicTheory.NoteName(60, false), "Middle C name");
        Equal("C#4", MusicTheory.NoteName(61, false), "Sharp spelling");
        Equal("Db4", MusicTheory.NoteName(61, true), "Flat spelling");
        Equal("C-1", MusicTheory.NoteName(0, true), "Lowest MIDI name");
        Equal("G9", MusicTheory.NoteName(127, false), "Highest MIDI name");
        Equal(28, MusicTheory.DiatonicStep(60, false), "Middle C staff step");
        Equal(28, MusicTheory.DiatonicStep(61, false), "C sharp shares C position");
        Equal(29, MusicTheory.DiatonicStep(61, true), "D flat shares D position");
        Equal(27, MusicTheory.DiatonicStep(59, false), "B below middle C");
        Equal(35, MusicTheory.DiatonicStep(72, false), "Next octave");
        Equal(-7, MusicTheory.DiatonicStep(0, false), "Negative octave staff step");
        for (int pitch = 0; pitch < 12; pitch++)
            Equal(pitch == 1 || pitch == 3 || pitch == 6 || pitch == 8 || pitch == 10,
                MusicTheory.IsAccidental(60 + pitch), "Accidental class " + pitch);
    }

    private static void TestChords()
    {
        Equal("—", MusicTheory.Recognize(null, false).Symbol, "Null input");
        Equal("—", MusicTheory.Recognize(new int[] { -1, 128 }, false).Symbol, "Invalid notes ignored");
        Equal("C4", Chord(60).Symbol, "One note fallback");
        Equal("C", Chord(60, 72, 60).Symbol, "Octave doubling is not a chord");
        Equal("C5", Chord(60, 67).Symbol, "Perfect fifth has explicit power-chord name");
        True(Chord(60, 64).Description.IndexOf("和弦未定", StringComparison.Ordinal) >= 0, "Other dyads remain ambiguous");

        string[] suffixes = { "", "m", "dim", "aug", "sus2", "sus4", "6", "m6", "7", "maj7", "m7", "m(maj7)", "m7b5", "dim7", "add9", "m(add9)", "9", "maj9", "m9", "7sus4" };
        int[][] intervals =
        {
            new int[] { 0, 4, 7 }, new int[] { 0, 3, 7 }, new int[] { 0, 3, 6 }, new int[] { 0, 4, 8 },
            new int[] { 0, 2, 7 }, new int[] { 0, 5, 7 }, new int[] { 0, 4, 7, 9 }, new int[] { 0, 3, 7, 9 },
            new int[] { 0, 4, 7, 10 }, new int[] { 0, 4, 7, 11 }, new int[] { 0, 3, 7, 10 }, new int[] { 0, 3, 7, 11 },
            new int[] { 0, 3, 6, 10 }, new int[] { 0, 3, 6, 9 }, new int[] { 0, 2, 4, 7 }, new int[] { 0, 2, 3, 7 },
            new int[] { 0, 2, 4, 7, 10 }, new int[] { 0, 2, 4, 7, 11 }, new int[] { 0, 2, 3, 7, 10 }, new int[] { 0, 5, 7, 10 }
        };
        for (int pattern = 0; pattern < intervals.Length; pattern++)
        {
            for (int root = 0; root < 12; root++)
            {
                List<int> notes = new List<int>();
                foreach (int interval in intervals[pattern]) notes.Add(48 + root + interval);
                string rootName = MusicTheory.NoteName(48 + root, false);
                rootName = rootName.Substring(0, rootName.Length - 1);
                Equal(rootName + suffixes[pattern], MusicTheory.Recognize(notes, false).Symbol,
                    "Transposed chord " + rootName + suffixes[pattern]);
            }
        }

        Equal("C/E", Chord(52, 60, 67).Symbol, "Major first inversion");
        Equal("C/G", Chord(43, 60, 64).Symbol, "Major second inversion");
        Equal("C7/Bb", MusicTheory.Recognize(new int[] { 46, 60, 64, 67 }, true).Symbol, "Seventh inversion and flat bass");
        Equal("Db", MusicTheory.Recognize(new int[] { 61, 65, 68 }, true).Symbol, "Flat chord root");
        Equal("C", Chord(79, 64, 48, 60, 76, 67).Symbol, "Voicing, order, and octave doubling invariant");
        True(Chord(60, 64, 67, 69).Alternatives.IndexOf("Am7/C", StringComparison.Ordinal) >= 0, "C6 / Am7 ambiguity exposed");
        Equal("Am7", Chord(45, 60, 64, 67).Symbol, "Bass selects minor seventh reading");
        True(Chord(60, 62, 67).Alternatives.IndexOf("Gsus4/C", StringComparison.Ordinal) >= 0, "Sus ambiguity exposed");
        True(Chord(60, 63, 66, 69).Alternatives.IndexOf("D#dim7/C", StringComparison.Ordinal) >= 0, "Symmetric diminished ambiguity exposed");
        ChordResult cluster = Chord(60, 61, 62, 67);
        True(cluster.Description.StartsWith("未匹配", StringComparison.Ordinal), "Cluster does not overclaim chord");
        Equal("", cluster.Alternatives, "Unknown chord has no invented alternatives");
        Equal("C7(no5)", Chord(60, 64, 70).Symbol, "Common seventh shell labels missing fifth");
        Equal("C/D#", Chord(39, 60, 64, 67).Symbol, "Complete triad over independent bass");
    }

    private static ChordResult Chord(params int[] notes)
    {
        return MusicTheory.Recognize(notes, false);
    }

    private static void TestExpandedChords()
    {
        // Independent lead-sheet degree fixtures, not reflection over the library.
        // The original 20 qualities remain covered by TestChords above.
        string[] fixtures =
        {
            "add11|1 3 5 11", "m(add11)|1 b3 5 11", "add#11|1 3 5 #11",
            "6/9|1 3 5 6 9", "m6/9|1 b3 5 6 9",
            "7sus2|1 2 5 b7", "maj7sus2|1 2 5 7", "maj7sus4|1 4 5 7",
            "9sus4|1 4 5 b7 9", "13sus4|1 4 5 b7 9 13",
            "7sus4(b9)|1 4 5 b7 b9", "7sus4(add13)|1 4 5 b7 13",
            "m(maj9)|1 b3 5 7 9", "11|1 3 5 b7 9 11", "maj11|1 3 5 7 9 11",
            "m11|1 b3 5 b7 9 11", "m(maj11)|1 b3 5 7 9 11",
            "13|1 3 5 b7 9 11 13", "maj13|1 3 5 7 9 11 13",
            "m13|1 b3 5 b7 9 11 13", "m(maj13)|1 b3 5 7 9 11 13",
            "7(b5)|1 3 b5 b7", "7(#5)|1 3 #5 b7", "maj7(b5)|1 3 b5 7", "maj7(#5)|1 3 #5 7",
            "9(b5)|1 3 b5 b7 9", "9(#5)|1 3 #5 b7 9",
            "maj9(b5)|1 3 b5 7 9", "maj9(#5)|1 3 #5 7 9",
            "m9b5|1 b3 b5 b7 9", "m11b5|1 b3 b5 b7 9 11",
            "7(b9)|1 3 5 b7 b9", "7(#9)|1 3 5 b7 #9",
            "7(#11)|1 3 5 b7 #11", "7(b13)|1 3 5 b7 b13",
            "7(add11)|1 3 5 b7 11", "7(add13)|1 3 5 b7 13",
            "maj7(#11)|1 3 5 7 #11", "maj7(add13)|1 3 5 7 13",
            "m7(add11)|1 b3 5 b7 11", "m7(add13)|1 b3 5 b7 13",
            "m(maj7,add11)|1 b3 5 7 11", "m(maj7,add13)|1 b3 5 7 13",
            "9(#11)|1 3 5 b7 9 #11", "9(b13)|1 3 5 b7 9 b13",
            "13(no11)|1 3 5 b7 9 13", "maj9(#11)|1 3 5 7 9 #11",
            "maj13(no11)|1 3 5 7 9 13", "m13(no11)|1 b3 5 b7 9 13",
            "m(maj13,no11)|1 b3 5 7 9 13",
            "7(b5,b9)|1 3 b5 b7 b9", "7(b5,#9)|1 3 b5 b7 #9",
            "7(#5,b9)|1 3 #5 b7 b9", "7(#5,#9)|1 3 #5 b7 #9",
            "7(b9,#11)|1 3 5 b7 b9 #11", "7(#9,#11)|1 3 5 b7 #9 #11",
            "7(b9,b13)|1 3 5 b7 b9 b13", "7(#9,b13)|1 3 5 b7 #9 b13",
            "7(b9,#11,b13)|1 3 5 b7 b9 #11 b13", "7(#9,#11,b13)|1 3 5 b7 #9 #11 b13",
            "13(b9)|1 3 5 b7 b9 11 13", "13(#9)|1 3 5 b7 #9 11 13",
            "13(#11)|1 3 5 b7 9 #11 13", "maj13(#11)|1 3 5 7 9 #11 13",
            "addb9|1 b9 3 5", "m(addb9)|1 b9 b3 5", "sus2sus4|1 9 11 5",
            "6sus4|1 11 5 13", "maj9sus4|1 9 11 5 7", "7(no3)|1 5 b7",
            "dim(maj7)|1 b3 b5 7", "m7(b13)|1 b3 5 b13 b7"
        };
        Equal(106, MusicTheory.ChordPatternCount, "Documented library size");
        Equal(72, fixtures.Length, "New quality fixture count");
        foreach (string fixture in fixtures)
        {
            string[] pieces = fixture.Split('|');
            string suffix = pieces[0];
            for (int root = 0; root < 12; root++)
            {
                List<int> notes = NotesFromDegrees(48 + root, pieces[1]);
                Equal(Pitch(root, false) + suffix, MusicTheory.Recognize(notes, false).Symbol,
                    "Extended root position " + fixture + " root " + root);
                Equal(Pitch(root, true) + suffix, MusicTheory.Recognize(notes, true).Symbol,
                    "Extended flat spelling " + fixture + " root " + root);
                // Every chord tone can be the bass; ambiguity must preserve the reading.
                for (int tone = 1; tone < notes.Count; tone++)
                {
                    List<int> inversion = new List<int>(notes);
                    int bass = 24 + root + (notes[tone] - 48 - root) % 12;
                    inversion.Add(bass);
                    string expected = Pitch(root, false) + suffix + "/" + Pitch(bass % 12, false);
                    HasReading(MusicTheory.Recognize(inversion, false), expected,
                        "Extended inversion " + fixture + " bass " + bass);
                }
            }
        }
    }

    private static void TestOmittedFifths()
    {
        // Intentionally written as actual played degrees and expected display names.
        string[] fixtures =
        {
            "9(no5)|1 3 b7 9", "maj9(no5)|1 3 7 9", "m9(no5)|1 b3 b7 9",
            "m(maj9,no5)|1 b3 7 9", "11(no5)|1 3 b7 9 11", "m11(no5)|1 b3 b7 9 11",
            "maj11(no5)|1 3 7 9 11", "m(maj11,no5)|1 b3 7 9 11",
            "13(no5)|1 3 b7 9 11 13", "maj13(no5)|1 3 7 9 11 13",
            "m13(no5)|1 b3 b7 9 11 13", "m(maj13,no5)|1 b3 7 9 11 13",
            "7(b9,no5)|1 3 b7 b9", "7(#9,no5)|1 3 b7 #9", "7(#11,no5)|1 3 b7 #11",
            "7(b13,no5)|1 3 b7 b13", "7(add11,no5)|1 3 b7 11", "7(add13,no5)|1 3 b7 13",
            "maj7(#11,no5)|1 3 7 #11", "maj7(add13,no5)|1 3 7 13",
            "m7(add11,no5)|1 b3 b7 11", "m7(add13,no5)|1 b3 b7 13",
            "m(maj7,add11,no5)|1 b3 7 11", "m(maj7,add13,no5)|1 b3 7 13",
            "9(#11,no5)|1 3 b7 9 #11", "9(b13,no5)|1 3 b7 9 b13",
            "13(no11,no5)|1 3 b7 9 13", "maj9(#11,no5)|1 3 7 9 #11",
            "maj13(no11,no5)|1 3 7 9 13", "m13(no11,no5)|1 b3 b7 9 13",
            "m(maj13,no11,no5)|1 b3 7 9 13",
            "7(b9,#11,no5)|1 3 b7 b9 #11", "7(#9,#11,no5)|1 3 b7 #9 #11",
            "7(b9,b13,no5)|1 3 b7 b9 b13", "7(#9,b13,no5)|1 3 b7 #9 b13",
            "7(b9,#11,b13,no5)|1 3 b7 b9 #11 b13", "7(#9,#11,b13,no5)|1 3 b7 #9 #11 b13",
            "13(b9,no5)|1 3 b7 b9 11 13", "13(#9,no5)|1 3 b7 #9 11 13",
            "13(#11,no5)|1 3 b7 9 #11 13", "maj13(#11,no5)|1 3 7 9 #11 13"
        };
        True(MusicTheory.OmittedFifthPatternCount > fixtures.Length, "Expanded no5 variants include seventh shells and added tones");
        foreach (string fixture in fixtures)
        {
            string[] pieces = fixture.Split('|');
            for (int root = 0; root < 12; root++)
            {
                List<int> notes = NotesFromDegrees(48 + root, pieces[1]);
                HasReading(MusicTheory.Recognize(notes, false), Pitch(root, false) + pieces[0],
                    "Omitted fifth " + fixture + " root " + root);
                int bass = 24 + root + (notes[1] - 48 - root) % 12;
                notes.Add(bass);
                HasReading(MusicTheory.Recognize(notes, true), Pitch(root, true) + pieces[0] + "/" + Pitch(bass % 12, true),
                    "Omitted fifth inversion and flats " + fixture + " root " + root);
            }
        }
        Equal("C9(no5)", Chord(48, 64, 70, 74).Symbol, "Practical ninth shell");
        True(Chord(48, 64, 70, 74).Description.IndexOf("省略五音", StringComparison.Ordinal) >= 0,
            "Omission explained in Chinese");
        Equal("C7(add13,no5)", Chord(48, 64, 70, 81).Symbol, "Thirteenth shell does not silently infer ninth or eleventh");
        Equal("C7(b5)", Chord(48, 64, 66, 70).Symbol, "Exact altered fifth outranks no5 sharp-eleventh reading");
        HasReading(Chord(48, 64, 66, 70), "C7(#11,no5)", "Enharmonic extension remains an explicit alternative");
        Equal("C9(b5)", Chord(48, 62, 64, 66, 70).Symbol, "Exact ninth with altered fifth has priority");
        HasReading(Chord(48, 62, 64, 66, 70), "C9(#11,no5)", "Ninth sharp-eleventh omission remains an alternative");
        Equal("A#maj9(b5)/C", Chord(48, 64, 70, 74, 81).Symbol, "Exact inversion outranks incomplete bass-root reading");
        HasReading(Chord(48, 64, 70, 74, 81), "C13(no11,no5)", "Practical 13 shell retained as alternative");
    }

    private static void TestRecognitionBoundaries()
    {
        Equal("Am/C", Chord(48, 64, 69).Symbol, "Complete minor inversion remains exact");
        Equal("C7(#5,b9)", Chord(48, 61, 64, 68, 70).Symbol, "Altered fifth cannot disappear");
        HasReading(Chord(48, 61, 64, 68, 70), "C7(b9,b13,no5)", "Enharmonic altered extension is labeled");
        HasNoReading(Chord(48, 61, 64, 70), "C7(#5,b9", "Absent altered fifth never inferred");
        HasNoReading(Chord(48, 67, 70, 74), "C9", "Missing third never inferred");
        HasNoReading(Chord(64, 67, 70, 74), "C9", "Missing root never inferred");
        HasNoReading(Chord(48, 64, 67, 74), "C9", "Missing seventh remains add9");
        Equal("Cadd9", Chord(48, 64, 67, 74).Symbol, "Added ninth does not imply a seventh");
        Equal("C7sus2", Chord(48, 62, 67, 70).Symbol, "Suspension does not invent a third");
        Equal("Cmaj13(#11)", Chord(48, 62, 64, 66, 67, 69, 71).Symbol,
            "Final library pattern keeps root-position priority independent of index");
        Equal("Bmaj13(#11)", Chord(59, 73, 75, 77, 78, 80, 82).Symbol,
            "Late pattern with highest root cannot lose to inversion by additive score");
        ChordResult fullCluster = Chord(60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71);
        True(fullCluster.Description.StartsWith("未匹配", StringComparison.Ordinal), "Chromatic cluster remains unknown");
        Equal("", fullCluster.Alternatives, "Chromatic cluster has no invented harmony");
        True(Chord(60, 61, 62, 63, 64, 65, 66, 67).Description.StartsWith("未匹配", StringComparison.Ordinal),
            "Dense eight-note cluster remains unknown");
        ChordResult normal = Chord(48, 61, 64, 66, 67, 68, 70);
        Equal("C7(b9,#11,b13)", normal.Symbol, "Combined alterations display correctly");
        Equal(normal.Symbol, Chord(82, 80, 79, 78, 76, 73, 60, 48).Symbol,
            "Complex chord is independent of order and octave doubling");
    }

    private static List<int> NotesFromDegrees(int root, string degrees)
    {
        int[] majorScale = { 0, 2, 4, 5, 7, 9, 11 };
        List<int> notes = new List<int>();
        foreach (string degreeText in degrees.Split(' '))
        {
            int alteration = degreeText[0] == 'b' ? -1 : degreeText[0] == '#' ? 1 : 0;
            int degree = Int32.Parse(alteration == 0 ? degreeText : degreeText.Substring(1));
            notes.Add(root + majorScale[(degree - 1) % 7] + ((degree - 1) / 7) * 12 + alteration);
        }
        return notes;
    }

    private static string Pitch(int pitch, bool flats)
    {
        string[] names = flats ? new string[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" }
            : new string[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[pitch];
    }

    private static void HasReading(ChordResult result, string expected, string message)
    {
        bool found = result.Symbol == expected;
        foreach (string alternative in result.Alternatives.Split(new string[] { " · " }, StringSplitOptions.RemoveEmptyEntries))
            found |= alternative == expected;
        True(found, message + ": expected [" + expected + "] in [" + result.Symbol + " | " + result.Alternatives + "]");
    }

    private static void HasNoReading(ChordResult result, string forbiddenPrefix, string message)
    {
        True(!result.Symbol.StartsWith(forbiddenPrefix, StringComparison.Ordinal), message + " primary");
        foreach (string alternative in result.Alternatives.Split(new string[] { " · " }, StringSplitOptions.RemoveEmptyEntries))
            True(!alternative.StartsWith(forbiddenPrefix, StringComparison.Ordinal), message + " alternative " + alternative);
    }

    private static void CheckNote(NoteState state, int number, int velocity, bool held, string message)
    {
        ActiveNote found = null;
        foreach (ActiveNote note in state.GetActiveNotes()) if (note.Number == number) found = note;
        True(found != null, message + " exists");
        Equal(velocity, found.Velocity, message + " velocity");
        Equal(held, found.IsHeld, message + " held state");
    }

    private static void True(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + ": expected [" + expected + "] but got [" + actual + "]");
    }
}

