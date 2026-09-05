using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class StaffLayoutTests
    {
        private static int checks;

        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application();
                foreach (bool flats in new[] { false, true })
                foreach (bool full in new[] { false, true })
                foreach (Size size in new[] { new Size(1050, 425), new Size(830, 300) })
                    CheckLayout(flats, full, size);

                foreach (int fifths in new[] { -7, -6, -5, -4, -3, -2, -1, 1, 2, 3, 4, 5, 6, 7 })
                foreach (bool full in new[] { false, true })
                foreach (Size size in new[] { new Size(1050, 425), new Size(830, 300) })
                    CheckLayout(false, full, size, fifths);
                CheckKeyAccidentals();
                CheckCompactPairs();

                Render("staff-column-default.png", 1050, 425, false, false,
                    new[] { 48, 60, 64, 67, 71, 74, 78, 81 });
                Render("staff-column-extremes.png", 1050, 425, false, false,
                    new[] { 21, 22, 23, 24, 59, 60, 61, 62, 103, 104, 105, 106, 107, 108 });
                Render("staff-column-minimum.png", 830, 300, true, false,
                    new[] { 36, 48, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 84, 96 });
                Render("staff-column-transparent.png", 1050, 425, false, true,
                    new[] { 48, 60, 64, 67, 71, 74, 78, 81 });
                Render("staff-key-d-major.png", 1050, 425, false, false,
                    new[] { 50, 54, 57, 60, 61, 62, 65, 66, 69, 74 }, 2);
                Render("staff-key-close-pair.png", 1050, 425, false, false,
                    new[] { 62, 65, 66, 69 }, 2);
                Render("staff-key-close-pair-minimum-light.png", 830, 300, true, false,
                    new[] { 62, 65, 66, 69 }, 2, 2);
                int[] allPitches = new int[88];
                for (int i = 0; i < allPitches.Length; i++) allPitches[i] = i + 21;
                Render("staff-key-seven-sharps-full.png", 1050, 425, false, false, allPitches, 7);
                Render("staff-key-seven-flats-full.png", 1050, 425, false, false, allPitches, -7);
                Render("staff-key-seven-sharps-minimum-light.png", 830, 300, true, false, allPitches, 7);
                Render("staff-key-seven-flats-minimum-light.png", 830, 300, true, false, allPitches, -7);
                Render("staff-key-seven-sharps-transparent.png", 1050, 425, false, true,
                    new[] { 36, 49, 53, 56, 59, 60, 61, 65, 72, 84, 108 }, 7);
                Render("staff-key-seven-flats-transparent.png", 1050, 425, false, true,
                    new[] { 35, 47, 59, 60, 64, 66, 71, 76, 83, 95, 107 }, -7);
                app.Shutdown();
                Console.WriteLine("StaffLayoutTests: PASS (" + checks + " assertions); previews in artifacts/staff-tests.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void CheckLayout(bool flats, bool full, Size size, int fifths = 0)
        {
            StaffView view = CreateView(size.Width, size.Height, flats);
            view.FullRange = full;
            view.KeySignatureFifths = fifths;
            object layout = Invoke(view, "CreateLayout");
            double anchor = Field<double>(layout, "NoteX");
            double spacing = Field<double>(layout, "Spacing");
            double headWidth = Field<double>(layout, "HeadWidth");
            double staffLeft = Field<double>(layout, "StaffLeft");
            double staffRight = Field<double>(layout, "StaffRight");
            int first = Field<int>(layout, "First"), last = Field<int>(layout, "Last");
            Check(staffRight - staffLeft >= 230 && staffRight - staffLeft <= (fifths == 0 ? 300 : 430), "compact staff width");
            Check(Math.Abs((staffRight + staffLeft) / 2 - size.Width / 2) < .001, "staff centered");
            Check(headWidth > 8, "readable heads at minimum size");
            StaffView narrower = CreateView(size.Width - 100, size.Height, flats);
            narrower.FullRange = full;
            narrower.KeySignatureFifths = fifths;
            object narrowerLayout = Invoke(narrower, "CreateLayout");
            Check(Math.Abs(Field<double>(narrowerLayout, "HeadWidth") - headWidth) < .001,
                "changing keyboard width does not shrink noteheads");

            var active = new Dictionary<int, ActiveNote>();
            for (int number = first; number <= last; number++)
            {
                active.Clear(); active.Add(number, Note(number));
                IList placements = (IList)Invoke(view, "PlaceNotes", layout, active);
                Check(Math.Abs(Field<double>(placements[0], "X") - anchor) < .001,
                    "single note x is independent of pitch " + number);
            }
            foreach (int[] chord in new[] { new[] { 36, 48, 60, 72, 84 }, new[] { 48, 60, 64, 67, 71, 74, 77, 81 } })
            {
                if (fifths != 0) break;
                active.Clear(); foreach (int number in chord) active[number] = Note(number);
                IList placements = (IList)Invoke(view, "PlaceNotes", layout, active);
                foreach (object placement in placements)
                    Check(Math.Abs(Field<double>(placement, "X") - anchor) < .001, "octaves and spaced harmony remain in one column");
            }
            active.Clear(); active[60] = Note(60); active[61] = Note(61); active[62] = Note(62);
            IList seconds = (IList)Invoke(view, "PlaceNotes", layout, active);
            Check(Field<double>(seconds[0], "X") != Field<double>(seconds[1], "X"), "unisons or seconds use different columns");

            active.Clear(); for (int number = first; number <= last; number++) active[number] = Note(number);
            IList dense = (IList)Invoke(view, "PlaceNotes", layout, active);
            IList signature = (IList)Field<object>(layout, "SignaturePlacements");
            Check(signature.Count == 2 * Math.Abs(fifths), "signature repeated after both clefs");
            int[] expectedSteps = fifths < 0 ? new[] { 34, 37, 33, 36, 32, 35, 31 } : new[] { 38, 35, 39, 36, 33, 37, 34 };
            for (int i = 0; i < signature.Count; i++)
            {
                Check(Field<int>(signature[i], "Step") == expectedSteps[i % Math.Abs(fifths)] - 14 * (i / Math.Abs(fifths)),
                    "conventional treble and bass signature octave placement");
                Rect sign = Field<Rect>(signature[i], "Bounds");
                Check(sign.Left > staffLeft + spacing * 3.3, "signature leaves space for clefs");
                Check(sign.Top >= 0 && sign.Bottom < Field<double>(layout, "KeyboardTop"), "signature inside viewport");
                for (int j = i + 1; j < signature.Count; j++)
                    Check(!sign.IntersectsWith(Field<Rect>(signature[j], "Bounds")), "signature glyphs do not overlap");
            }
            HashSet<int> ghosts = Field<HashSet<int>>(layout, "GhostSteps");
            var expectedGhosts = new HashSet<int>();
            for (int number = first; number <= last; number++)
            {
                NotatedPitch spelling = KeySignature.Spell(number, fifths, flats);
                if (spelling.InKey) expectedGhosts.Add(spelling.Step);
            }
            Check(ghosts.SetEquals(expectedGhosts), "ghost positions follow every diatonic signature tone");
            for (int i = 0; i < dense.Count; i++)
            {
                object current = dense[i];
                Rect head = Field<Rect>(current, "HeadBounds");
                Check(head.Top >= 0 && head.Bottom < Field<double>(layout, "KeyboardTop") - 5,
                    "extreme notes remain above keyboard inside viewport");
                Check(head.Left >= staffLeft && head.Right <= staffRight, "cluster stays within compact staff");
                foreach (object signatureSign in signature)
                    Check(!head.IntersectsWith(Field<Rect>(signatureSign, "Bounds")), "signature avoids active heads");
                Check(Field<double>(current, "X") - Field<double>(current, "HeadGroupX") <= (headWidth + 1.2) * 3.01,
                    "full chromatic range needs no more than four columns within each head group");
                ActiveNote note = Field<ActiveNote>(current, "Note");
                if (note.Number == first || note.Number == last || (note.Number >= 59 && note.Number <= 62))
                    CheckLedgerLines(view, layout, current);
                for (int j = i + 1; j < dense.Count; j++)
                    Check(!head.IntersectsWith(Field<Rect>(dense[j], "HeadBounds")), "no filled notehead overlap");
                FormattedText accidental = Field<FormattedText>(current, "Accidental");
                if (accidental == null) continue;
                Rect textBox = Field<Rect>(current, "AccidentalBounds");
                Rect glyph = accidental.BuildGeometry(textBox.TopLeft).Bounds;
                Check(glyph.Right < Field<double>(current, "HeadGroupX") - headWidth / 2, "accidentals stay left of their head group");
                Check(glyph.Left > staffLeft + spacing * 3.3, "accidentals leave clef space");
                Check(glyph.Left > Field<double>(layout, "SignatureRight") + (fifths == 0 ? 0 : 3),
                    "active accidentals leave a gap after the key signature");
                Check(glyph.Top >= 0 && glyph.Bottom < Field<double>(layout, "KeyboardTop"), "accidentals inside viewport");
                for (int j = 0; j < dense.Count; j++)
                {
                    Check(!glyph.IntersectsWith(Field<Rect>(dense[j], "HeadBounds")), "accidental avoids every notehead; key=" + fifths + ", full=" + full + ", size=" + size + ", sign=" + note.Number + " " + glyph + ", head=" + Field<ActiveNote>(dense[j], "Note").Number + " " + Field<Rect>(dense[j], "HeadBounds"));
                    if (j <= i) continue;
                    FormattedText other = Field<FormattedText>(dense[j], "Accidental");
                    if (other != null)
                    {
                        Rect otherBounds = Field<Rect>(dense[j], "AccidentalBounds");
                        Check(!glyph.IntersectsWith(other.BuildGeometry(otherBounds.TopLeft).Bounds), "no accidental glyph overlap");
                    }
                }
            }
            var keyboard = Field<Dictionary<int, double>>(layout, "KeyX");
            Check(keyboard[last] - keyboard[first] > size.Width * .8, "keyboard retains independent horizontal range");
        }

        private static void CheckLedgerLines(StaffView view, object layout, object placement)
        {
            ActiveNote note = Field<ActiveNote>(placement, "Note");
            double x = Field<double>(placement, "X");
            int step = (int)Invoke(view, "DiatonicStep", note.Number);
            int low = step >= 28 ? 30 : 18;
            int high = step >= 28 ? 38 : 26;
            int expected = step < low ? (low - step) / 2 : step > high ? (step - high) / 2 : 0;
            var drawing = new DrawingGroup();
            using (DrawingContext context = drawing.Open())
                Invoke(view, "DrawLedgerLines", context, note.Number, x, layout, Colors.White);
            Check(drawing.Children.Count == expected, "correct ledger count for extreme and middle-C notes");
            foreach (GeometryDrawing child in drawing.Children)
            {
                LineGeometry line = (LineGeometry)child.Geometry;
                Check(Math.Abs((line.StartPoint.X + line.EndPoint.X) / 2 - x) < .001,
                    "ledger follows displaced notehead");
                Check(Math.Abs(line.EndPoint.X - line.StartPoint.X - Field<double>(layout, "HeadWidth") * 1.5) < .001,
                    "ledger extends beyond both sides of notehead");
            }
        }

        private static void CheckKeyAccidentals()
        {
            var view = CreateView(1050, 425, false);
            foreach (int fifths in new[] { 2, -1, -2, 7, -7 })
            {
                view.KeySignatureFifths = fifths;
                object layout = Invoke(view, "CreateLayout");
                int[] pitches = fifths == 2 ? new[] { 60, 61, 65, 66 } : fifths < 0 && fifths > -7 ? new[] { 70, 71 } : new[] { 59, 60, 64, 65, 71, 72 };
                var active = new Dictionary<int, ActiveNote>();
                foreach (int number in pitches) active[number] = Note(number);
                IList placements = (IList)Invoke(view, "PlaceNotes", layout, active);
                foreach (object placement in placements)
                {
                    int number = Field<ActiveNote>(placement, "Note").Number;
                    FormattedText sign = Field<FormattedText>(placement, "Accidental");
                    if (fifths == 2)
                        Check((sign == null ? "" : sign.Text) == (number == 60 || number == 65 ? "♮" : "♯"),
                            "D major simultaneous natural/sharp pairs carry an explicit sign on each group");
                    else if (fifths == -1 || fifths == -2)
                        Check((sign == null ? "" : sign.Text) == (number == 71 ? "♮" : "♭"),
                            "F and Bb major simultaneous natural/flat pairs carry an explicit sign on each group");
                    CheckLedgerLines(view, layout, placement);
                }
                Check(Math.Abs(Field<double>(Invoke(view, "CreateLayout"), "NoteX") - Field<double>(layout, "NoteX")) < .001,
                    "playing chromatic clusters leaves key-dependent geometry static");
            }
            view.KeySignatureFifths = 7;
            Check((int)Invoke(view, "DiatonicStep", 60) == 27, "C4 physical key is B#3 in C# major");
            view.KeySignatureFifths = -7;
            Check((int)Invoke(view, "DiatonicStep", 59) == 28, "B3 physical key is Cb4 in Cb major");
            foreach (int fifths in new[] { 2, -1, -2 })
            foreach (int number in fifths == 2 ? new[] { 60, 61, 65, 66 } : new[] { 70, 71 })
            {
                view.KeySignatureFifths = fifths;
                var active = new Dictionary<int, ActiveNote>(); active[number] = Note(number);
                IList placements = (IList)Invoke(view, "PlaceNotes", Invoke(view, "CreateLayout"), active);
                FormattedText sign = Field<FormattedText>(placements[0], "Accidental");
                string expected = fifths == 2 ? (number == 60 || number == 65 ? "♮" : "") : number == 71 ? "♮" : "";
                Check((sign == null ? "" : sign.Text) == expected,
                    "single signature tone needs no repeated sign while natural cancellation stays explicit");
            }
        }

        private static void CheckCompactPairs()
        {
            // Key, lower MIDI note, upper MIDI note: D-major C/C# and F/F#,
            // F-major Bb/B, and Bb-major Eb/E. Exercise bass and treble octaves.
            foreach (int[] pair in new[] { new[] { 2, 60, 61 }, new[] { 2, 65, 66 },
                new[] { -1, 70, 71 }, new[] { -2, 63, 64 } })
            foreach (Size size in new[] { new Size(1050, 425), new Size(830, 300) })
            foreach (bool full in new[] { false, true })
            foreach (bool light in new[] { false, true })
            foreach (int intensity in new[] { 0, 2 })
            foreach (int octave in new[] { -12, 0 })
            foreach (bool harmony in new[] { false, true })
            {
                var view = CreateView(size.Width, size.Height, pair[0] < 0);
                view.FullRange = full;
                view.KeySignatureFifths = pair[0];
                view.LightTheme = light;
                view.IntensityMode = intensity;
                object layout = Invoke(view, "CreateLayout");
                int lower = pair[1] + octave, upper = pair[2] + octave;
                var active = new Dictionary<int, ActiveNote>();
                active[lower] = new ActiveNote { Number = lower, Velocity = 12, IsHeld = false };
                active[upper] = new ActiveNote { Number = upper, Velocity = 127, IsHeld = true };
                if (harmony)
                {
                    active[lower - 3] = Note(lower - 3);
                    active[lower + 4] = Note(lower + 4);
                }
                IList placements = (IList)Invoke(view, "PlaceNotes", layout, active);
                object lowerPlacement = null, upperPlacement = null;
                foreach (object placement in placements)
                {
                    int number = Field<ActiveNote>(placement, "Note").Number;
                    if (number == lower) lowerPlacement = placement;
                    if (number == upper) upperPlacement = placement;
                }
                string context = "key=" + pair[0] + ", pair=" + lower + "/" + upper +
                    ", size=" + size + ", full=" + full + ", light=" + light +
                    ", intensity=" + intensity + ", harmony=" + harmony;
                Check(lowerPlacement != null && upperPlacement != null, "both close-pair notes placed; " + context);
                Check(Math.Abs(Field<double>(lowerPlacement, "Y") - Field<double>(upperPlacement, "Y")) < .001,
                    "altered unisons share one staff position; " + context);
                Check(Field<FormattedText>(lowerPlacement, "Accidental").Text == (pair[0] > 0 ? "♮" : "♭"),
                    "lower close-pair note keeps explicit sign; " + context);
                Check(Field<FormattedText>(upperPlacement, "Accidental").Text == (pair[0] > 0 ? "♯" : "♮"),
                    "upper close-pair note keeps explicit sign; " + context);
                double separation = Math.Abs(Field<double>(lowerPlacement, "X") - Field<double>(upperPlacement, "X"));
                object left = Field<double>(lowerPlacement, "X") < Field<double>(upperPlacement, "X") ? lowerPlacement : upperPlacement;
                object right = left == lowerPlacement ? upperPlacement : lowerPlacement;
                Rect rightGlyph = AccidentalGlyph(right);
                Check(separation <= 40, "ordinary close-pair heads stay within 40 px, actual=" + separation + "; " + context);
                Check(separation >= Field<double>(layout, "HeadWidth") + rightGlyph.Width + 2,
                    "close pair leaves room for a readable intervening sign; " + context);
                Check(rightGlyph.Left > Field<Rect>(left, "HeadBounds").Right &&
                    rightGlyph.Right < Field<Rect>(right, "HeadBounds").Left,
                    "right note's sign stays visibly between the two heads; " + context);
                for (int i = 0; i < placements.Count; i++)
                {
                    object current = placements[i];
                    Rect head = Field<Rect>(current, "HeadBounds");
                    Check(head.Left >= Field<double>(layout, "StaffLeft") && head.Right <= Field<double>(layout, "StaffRight"),
                        "close pair and surrounding harmony stay within staff; " + context);
                    for (int j = i + 1; j < placements.Count; j++)
                        Check(!head.IntersectsWith(Field<Rect>(placements[j], "HeadBounds")),
                            "close-pair harmony has no notehead overlap; " + context);
                    if (Field<FormattedText>(current, "Accidental") == null) continue;
                    Rect glyph = AccidentalGlyph(current);
                    for (int j = 0; j < placements.Count; j++)
                    {
                        Check(!glyph.IntersectsWith(Field<Rect>(placements[j], "HeadBounds")),
                            "close-pair accidental avoids every notehead; " + context);
                        if (j > i && Field<FormattedText>(placements[j], "Accidental") != null)
                            Check(!glyph.IntersectsWith(AccidentalGlyph(placements[j])),
                                "close-pair accidental glyphs remain separate; " + context);
                    }
                }
            }
        }

        private static Rect AccidentalGlyph(object placement)
        {
            return Field<FormattedText>(placement, "Accidental").BuildGeometry(
                Field<Rect>(placement, "AccidentalBounds").TopLeft).Bounds;
        }

        private static StaffView CreateView(double width, double height, bool flats)
        {
            var view = new StaffView { Width = width, Height = height, Flats = flats };
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            return view;
        }

        private static void Render(string name, int width, int height, bool light, bool transparent, int[] pitches, int fifths = 0, int intensity = 0)
        {
            var view = CreateView(width, height, light);
            view.LightTheme = light;
            view.KeySignatureFifths = fifths;
            view.IntensityMode = intensity;
            var notes = new List<ActiveNote>();
            for (int i = 0; i < pitches.Length; i++)
                notes.Add(new ActiveNote { Number = pitches[i], Velocity = 35 + i * 19 % 93, IsHeld = i % 5 != 0 });
            view.Notes = notes; view.Refresh(); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            if (!transparent)
            {
                var background = new DrawingVisual();
                using (DrawingContext context = background.RenderOpen())
                    context.DrawRectangle(new SolidColorBrush(light ? Color.FromRgb(241, 244, 239) : Color.FromRgb(17, 28, 39)),
                        null, new Rect(0, 0, width, height));
                bitmap.Render(background);
            }
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(stream);
            if (transparent)
            {
                byte[] pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
                int transparentCount = 0;
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] == 0) transparentCount++;
                Check(transparentCount > width * height * .8, "score and keyboard background remain truly transparent");
                int scoreHeight = (int)Field<double>(Invoke(view, "CreateLayout"), "KeyboardTop");
                int clearScorePixels = 0;
                for (int i = 3; i < width * scoreHeight * 4; i += 4) if (pixels[i] == 0) clearScorePixels++;
                Check(clearScorePixels > width * scoreHeight * (fifths == 0 ? .96 : .94), "empty score area retains zero alpha");
            }
        }

        private static ActiveNote Note(int number) { return new ActiveNote { Number = number, Velocity = 100, IsHeld = true }; }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static object Invoke(object target, string name, params object[] args)
        { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        private static void Check(bool condition, string label)
        { checks++; if (!condition) throw new Exception("FAILED: " + label); }
    }
}
