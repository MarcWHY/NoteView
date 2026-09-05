using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteView
{
    // Preserve a sounding harmonic foundation when a single melodic tone is added.
    public sealed class HarmonicContext
    {
        private ChordResult basis;
        private int basisCore, basisAnchor;
        private readonly Dictionary<int, long> basisStrikes = new Dictionary<int, long>();
        private string lastInput = "";
        private bool lastFlats;
        private ChordResult selected = new ChordResult();
        public ChordResult Resolve(IList<ActiveNote> notes, bool includeSustain, bool flats)
        {
            var sounding = notes.Where(n => n.IsHeld || (includeSustain && n.Brightness > 0)).ToList();
            string input = flats + "|" + string.Join(",", sounding.Select(n => n.Number + ":" + n.StrikeId + ":" + n.IsHeld + ":" + (n.Brightness >= .04)));
            if (input == lastInput) return selected;
            lastInput = input;
            if (flats != lastFlats) { basis = null; lastFlats = flats; }
            if (sounding.Count == 0) { basis = null; basisCore = 0; basisStrikes.Clear(); return selected = new ChordResult(); }
            var held = sounding.Where(n => n.IsHeld).ToList();
            var heldChord = MusicTheory.Recognize(held.Select(n => n.Number), flats);
            var raw = heldChord.IsRecognized && held.Select(n => n.Number % 12).Distinct().Count() >= 3
                ? heldChord : MusicTheory.Recognize(sounding.Select(n => n.Number), flats);
            int freshCount = 0;
            if (basis != null)
            {
                int support = 0;
                foreach (var note in sounding)
                    if (note.IsHeld || note.Brightness >= .04) support |= 1 << (note.Number % 12);
                var fresh = held.Where(n => !basisStrikes.ContainsKey(n.Number) || basisStrikes[n.Number] != n.StrikeId)
                    .Select(n => n.Number % 12).Distinct().ToList();
                freshCount = fresh.Count;
                int outside = fresh.Count(pitch => (basisCore & (1 << pitch)) == 0);
                // D-F-A plus a new Bb stays Dm while its supporting tones remain.
                // Rearticulating a full new voicing (three pitch classes) releases this preference.
                bool passingTone = outside == 1 && fresh.Count < 3 && (support & basisAnchor) != 0;
                if (passingTone && (!raw.IsRecognized || raw.Root != basis.Root || raw.Quality != basis.Quality)) return selected = basis;
            }
            selected = raw;
            if (raw.IsRecognized)
            {
                bool newBasis = basis == null || raw.Root != basis.Root || raw.Quality != basis.Quality || freshCount >= 3;
                basis = raw; basisCore = CoreMask(raw);
                string q = raw.Quality;
                int fifth = q.Contains("dim") || q.Contains("b5") ? 6 : q.Contains("aug") || q.Contains("#5") ? 8 : 7;
                basisAnchor = 1 << ((raw.Root + fifth) % 12);
                if (newBasis) { basisStrikes.Clear(); foreach (var note in sounding) basisStrikes[note.Number] = note.StrikeId; }
            }
            else if (!sounding.Any(n => (basisCore & (1 << (n.Number % 12))) != 0 && (n.IsHeld || n.Brightness >= .04)))
            { basis = null; basisStrikes.Clear(); }
            return selected;
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
    }
}
