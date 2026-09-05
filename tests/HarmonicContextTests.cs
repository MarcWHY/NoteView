using System;
using System.Linq;
namespace NoteView
{
    public static class HarmonicContextTests
    {
        static int checks;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        public static int Main()
        {
            try
            {
                for (int root = 0; root < 12; root++)
                {
                    double time = 0;
                    var state = new NoteState(delegate { return time; });
                    var context = new HarmonicContext();
                    int d = 48 + root, f = d + 3, a = d + 7, bb = d + 8;
                    state.Process(0x90, d, 100); state.Process(0x90, f, 90); state.Process(0x90, a, 110);
                    Check(context.Resolve(state.GetActiveNotes(), true, true).Root == root, "minor foundation root");
                    state.Process(0xB0, 64, 127); state.Process(0x80, a, 0);
                    time = .3;
                    context.Resolve(state.GetActiveNotes(), true, true);
                    state.Process(0x90, bb, 100);
                    var passing = context.Resolve(state.GetActiveNotes(), true, true);
                    Check(passing.Root == root && passing.Quality == "m", "one flat-sixth over clear fifth retains minor foundation");
                    time = .8;
                    Check(context.Resolve(state.GetActiveNotes(), true, true).Root == root, "held passing note does not force a relabel");
                    // Rearticulate a full new major voicing, processing every event separately.
                    state.Process(0x90, d, 100); context.Resolve(state.GetActiveNotes(), true, true);
                    state.Process(0x90, f, 100); context.Resolve(state.GetActiveNotes(), true, true);
                    state.Process(0x90, bb, 100);
                    Check(context.Resolve(state.GetActiveNotes(), true, true).Root == (root + 8) % 12, "new full voicing can replace foundation");
                    // A fading anchor must not preserve a foundation indefinitely.
                    state.Clear(); context.Resolve(state.GetActiveNotes(), true, true);
                    state.Process(0x90, d, 100); state.Process(0x90, f, 100); state.Process(0x90, a, 100);
                    context.Resolve(state.GetActiveNotes(), true, true);
                    state.Process(0xB0, 64, 127); state.Process(0x80, a, 0); state.Process(0x90, bb, 100);
                    Check(context.Resolve(state.GetActiveNotes(), true, true).Root == root, "new passing event remains grounded");
                    time += 5;
                    Check(context.Resolve(state.GetActiveNotes(), true, true).Root == (root + 8) % 12, "faded anchor releases preference");
                    state.Clear(); Check(!context.Resolve(state.GetActiveNotes(), true, true).IsRecognized, "silence clears label and basis");
                }
                string[] input = { "Bbmaj7", "F#dim7", "Cm7b5", "Caug", "Cm(maj7)", "C7(b9,#11,b13)", "Bbmaj9/D", "C6/9" };
                string[] expected = { "B♭△7", "F♯°7", "Cø7", "C+", "Cm(△7)", "C7(♭9,♯11,♭13)", "B♭△9/D", "C6/9" };
                for (int i = 0; i < input.Length; i++)
                {
                    Check(MusicTheory.DisplaySymbol(input[i]) == expected[i], "musical chord typography");
                    Check(MusicTheory.DisplaySymbol(expected[i]) == expected[i], "formatting is safe for OBS to repeat");
                }
                Console.WriteLine("HarmonicContextTests PASS: " + checks); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
