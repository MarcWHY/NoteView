using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace NoteView
{
    /// <summary>A stationary, single-column grand staff and an independent 88-key MIDI monitor.</summary>
    public sealed class StaffView : FrameworkElement
    {
        private static readonly int[] SharpSignatureSteps = { 38, 35, 39, 36, 33, 37, 34 };
        private static readonly int[] FlatSignatureSteps = { 34, 37, 33, 36, 32, 35, 31 };
        private readonly Dictionary<uint, SolidColorBrush> brushes = new Dictionary<uint, SolidColorBrush>();
        private readonly Dictionary<ulong, Pen> pens = new Dictionary<ulong, Pen>();
        private readonly Typeface symbolFace = new Typeface("Segoe UI Symbol");
        private readonly ContainerVisual scoreNotes = new ContainerVisual();
        private readonly DrawingVisual sustainedNotes = new DrawingVisual();
        private readonly DrawingVisual heldNotes = new DrawingVisual();
        private readonly BlurEffect sustainBlur = new BlurEffect { Radius = 2.2, KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Quality };
        private GlyphTypeface musicFont;
        private bool fontLoaded;
        private double scoreScale = 1;
        private double scoreOffsetX, scoreOffsetY;
        private double keyboardScale = 1, keyboardOffsetX, keyboardOffsetY;
        private readonly Dictionary<int, HeadSlot> stableHeadSlots = new Dictionary<int, HeadSlot>();
        private Layout cachedLayout, placementLayout, staffDrawingLayout, keyboardDrawingLayout;
        private double layoutWidth, layoutHeight;
        private int layoutKey;
        private bool layoutFullRange, layoutScoreOnly, layoutKeyboardOnly, layoutFlats;
        private bool staffDrawingLight, keyboardDrawingLight;
        private DrawingGroup staffDrawing;
        private List<NotePlacement> cachedPlacements;
        private readonly List<KeyDrawing> keyDrawings = new List<KeyDrawing>();
        private DrawingGroup keyboardTopDrawing;
        private RenderTargetBitmap whiteKeyBitmap, blackKeyBitmap;
        private double keyboardBitmapScale;
        private NotatedPitch[] spellings;
        private int spellingKey;
        private bool spellingFlats;

        public IList<ActiveNote> Notes { get; set; }
        public bool ScoreOnly { get; set; }
        public bool KeyboardOnly { get; set; }
        public bool Flats { get; set; }
        public int KeySignatureFifths { get; set; }
        public bool FullRange { get; set; }
        public bool GhostNotes { get; set; }
        public bool LightTheme { get; set; }
        public int IntensityMode { get; set; }
        public Color AccentColor { get; set; }
        // Offscreen renderers set their export density explicitly. On desktop the
        // actual window DPI and ancestor scaling take precedence.
        public double RenderPixelScale { get; set; }

        /// <summary>Scales only the score around the center of its available area.</summary>
        public double ScoreScale
        {
            get { return scoreScale; }
            set { scoreScale = Finite(value) ? Math.Max(.5, Math.Min(2, value)) : 1; InvalidateVisual(); }
        }
        public double ScoreOffsetX
        {
            get { return scoreOffsetX; }
            set { scoreOffsetX = Finite(value) ? value : 0; InvalidateVisual(); }
        }
        public double ScoreOffsetY
        {
            get { return scoreOffsetY; }
            set { scoreOffsetY = Finite(value) ? value : 0; InvalidateVisual(); }
        }
        public double KeyboardScale { get { return keyboardScale; } set { keyboardScale = Finite(value) ? Math.Max(.6, Math.Min(1.5, value)) : 1; InvalidateVisual(); } }
        public double KeyboardOffsetX { get { return keyboardOffsetX; } set { keyboardOffsetX = Finite(value) ? value : 0; InvalidateVisual(); } }
        public double KeyboardOffsetY { get { return keyboardOffsetY; } set { keyboardOffsetY = Finite(value) ? value : 0; InvalidateVisual(); } }
        /// <summary>The score's local clipping area; the independent keyboard starts below it.</summary>
        public Rect ScoreViewport
        {
            get { return ActualWidth < 140 || ActualHeight < 150 ? Rect.Empty :
                new Rect(0, 0, ActualWidth, CreateLayout().KeyboardTop); }
        }
        /// <summary>Stable bounds for the whole selected pitch range, after scale, offset and clipping.</summary>
        public Rect ScoreBounds
        {
            get
            {
                if (ActualWidth < 140 || ActualHeight < 150) return Rect.Empty;
                Layout layout = CreateLayout();
                double top = StepY(38, layout), bottom = StepY(18, layout);
                for (int number = layout.First; number <= layout.Last; number++)
                {
                    double y = PitchY(number, layout);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                Rect bounds = new Rect(layout.StaffLeft - 21, top - layout.HeadWidth,
                    layout.StaffRight - layout.StaffLeft + 24, bottom - top + layout.HeadWidth * 2);
                bounds = ScoreTransform(layout).TransformBounds(bounds);
                bounds.Intersect(new Rect(0, 0, ActualWidth, layout.KeyboardTop));
                return bounds;
            }
        }

        public StaffView()
        {
            Notes = new List<ActiveNote>();
            FullRange = true;
            GhostNotes = true;
            AccentColor = Color.FromRgb(107, 216, 240);
            RenderPixelScale = 1.5;
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
            // Blur only the pedal-retained notes. The staff and held notes stay crisp.
            sustainedNotes.Effect = sustainBlur;
            scoreNotes.Children.Add(sustainedNotes);
            scoreNotes.Children.Add(heldNotes);
            AddVisualChild(scoreNotes);
        }

        protected override int VisualChildrenCount { get { return 1; } }

        protected override Visual GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException("index");
            return scoreNotes;
        }

        public void Refresh()
        {
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth < 140 || ActualHeight < (KeyboardOnly ? 20 : 150))
            {
                using (DrawingContext empty = sustainedNotes.RenderOpen()) { }
                using (DrawingContext empty = heldNotes.RenderOpen()) { }
                return;
            }

            Layout layout = CreateLayout();
            Color ink = LightTheme ? Color.FromRgb(31, 47, 65) : Color.FromRgb(225, 236, 247);
            Dictionary<int, ActiveNote> active = new Dictionary<int, ActiveNote>();
            if (Notes != null)
            {
                foreach (ActiveNote note in Notes)
                {
                    if (note.Number < layout.First || note.Number > layout.Last) continue;
                    ActiveNote previous;
                    if (!active.TryGetValue(note.Number, out previous) || note.Velocity >= previous.Velocity)
                        active[note.Number] = note;
                }
            }

            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            if (KeyboardOnly)
            {
                sustainedNotes.Effect = null;
                using (DrawingContext empty = sustainedNotes.RenderOpen()) { }
                using (DrawingContext empty = heldNotes.RenderOpen()) { }
                DrawKeyboard(dc, layout, active, ink); dc.Pop(); return;
            }
            RectangleGeometry scoreClip = new RectangleGeometry(new Rect(0, 0, ActualWidth, layout.KeyboardTop));
            MatrixTransform scoreTransform = ScoreTransform(layout);
            scoreNotes.Clip = scoreClip;
            dc.PushClip(scoreClip);
            dc.PushTransform(scoreTransform);
            DrawStaff(dc, layout, ink);
            List<NotePlacement> placements = PlaceNotes(layout, active);

            if (GhostNotes)
            {
                HashSet<int> activeSteps = new HashSet<int>();
                foreach (int number in active.Keys) activeSteps.Add(DiatonicStep(number));
                foreach (int step in layout.GhostSteps)
                {
                    if (!activeSteps.Contains(step))
                    {
                        double y = StepY(step, layout);
                        DrawHead(dc, layout.NoteX, y, layout.HeadWidth * .46,
                            Brush(ink, LightTheme ? .095 : .075), null);
                    }
                }
            }

            dc.Pop();
            dc.Pop();

            // Child visuals share the same score transform and viewport. Keeping
            // held notes above the soft sustain layer preserves immediate feedback.
            sustainBlur.Radius = Math.Max(1.4, layout.HeadWidth * scoreScale * .18);
            bool anySustained = false;
            foreach (NotePlacement placement in placements)
                if (!placement.Note.IsHeld && NoteOpacity(placement.Note) > 0) { anySustained = true; break; }
            sustainedNotes.Effect = anySustained ? sustainBlur : null;
            DrawNoteLayer(sustainedNotes, false, placements, layout, ink, scoreTransform);
            DrawNoteLayer(heldNotes, true, placements, layout, ink, scoreTransform);

            if (!ScoreOnly) DrawKeyboard(dc, layout, active, ink);
            dc.Pop();
        }

        private void DrawNoteLayer(DrawingVisual visual, bool held, IList<NotePlacement> placements,
            Layout layout, Color ink, Transform transform)
        {
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(transform);
                // Ledger lines precede all heads within each layer.
                foreach (NotePlacement placement in placements)
                    if (placement.Note.IsHeld == held)
                    { dc.PushOpacity(NoteOpacity(placement.Note)); DrawLedgerLines(dc, placement.Note.Number, placement.X, layout, ink); dc.Pop(); }
                foreach (NotePlacement placement in placements)
                    if (placement.Note.IsHeld == held) DrawNote(dc, placement, layout);
                dc.Pop();
            }
        }

        private MatrixTransform ScoreTransform(Layout layout)
        {
            return new MatrixTransform(scoreScale, 0, 0, scoreScale,
                ActualWidth * .5 * (1 - scoreScale) + scoreOffsetX,
                layout.KeyboardTop * .5 * (1 - scoreScale) + scoreOffsetY);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private Layout CreateLayout()
        {
            if (cachedLayout != null && layoutWidth == ActualWidth && layoutHeight == ActualHeight &&
                layoutKey == KeySignatureFifths && layoutFlats == Flats && layoutFullRange == FullRange &&
                layoutScoreOnly == ScoreOnly && layoutKeyboardOnly == KeyboardOnly) return cachedLayout;
            Layout layout = new Layout();
            layout.First = FullRange ? 21 : 36;
            layout.Last = FullRange ? 108 : 84;
            layout.Left = 63;
            layout.Right = ActualWidth - 19;
            layout.KeyboardHeight = Math.Min(67, Math.Max(43, ActualHeight * .17));
            layout.KeyboardTop = ActualHeight - layout.KeyboardHeight - 13;
            if (ScoreOnly) layout.KeyboardTop = ActualHeight;
            if (KeyboardOnly) { layout.KeyboardTop = 2; layout.KeyboardHeight = ActualHeight - 4; layout.Left = 1; layout.Right = ActualWidth - 1; }
            double top = 18;
            double bottom = layout.KeyboardTop - 25;
            // Enharmonic spellings can cross an octave boundary (B# / Cb), so
            // scan the whole range instead of assuming MIDI endpoints are extrema.
            int upperStep = int.MinValue;
            int lowerStep = int.MaxValue;
            for (int number = layout.First; number <= layout.Last; number++)
            {
                NotatedPitch pitch = Spell(number);
                upperStep = Math.Max(upperStep, pitch.Step);
                lowerStep = Math.Min(lowerStep, pitch.Step);
                if (pitch.InKey) layout.GhostSteps.Add(pitch.Step);
            }
            layout.Spacing = Math.Min(13, Math.Max(4, (bottom - top) * 2 / (upperStep - lowerStep)));
            double occupied = (upperStep - lowerStep) * layout.Spacing / 2;
            layout.Top = top + Math.Max(0, (bottom - top - occupied) / 2);
            layout.TopStep = upperStep;
            int whiteCount = 0;
            for (int number = layout.First; number <= layout.Last; number++)
                if (!IsBlack(number)) whiteCount++;
            if (KeyboardOnly)
            {
                double keybedWidth = Math.Min(layout.Right - layout.Left, layout.KeyboardHeight / 6.36 * whiteCount);
                layout.Left = (ActualWidth - keybedWidth) / 2; layout.Right = layout.Left + keybedWidth;
            }
            layout.WhiteWidth = (layout.Right - layout.Left) / whiteCount;
            // Score heads scale with the staff, never with the horizontal piano keys.
            layout.HeadWidth = Math.Min(14, layout.Spacing * 1.25);
            // Reserve fixed space for this key, including dense accidental columns.
            // Changing the played notes never moves the staff or the main note column.
            int keyCount = Math.Min(7, Math.Abs(KeySignatureFifths));
            double signatureAdvance = Math.Max(10, layout.Spacing * 1.1);
            double keySpace = keyCount == 0 ? 0 : keyCount * signatureAdvance + 12;
            double staffWidth = Math.Min(276 + keySpace + (keyCount == 0 ? 0 : 40), ActualWidth - 56);
            layout.StaffLeft = (ActualWidth - staffWidth) / 2;
            layout.StaffRight = layout.StaffLeft + staffWidth;
            layout.NoteX = layout.StaffLeft + 276 * .61 + keySpace;
            // Ordinary altered unisons need room for one sign between their heads,
            // not the wide corridor reserved for a full chromatic cluster.
            layout.UnisonOffset = Math.Max(29, layout.HeadWidth * 2 + 4);
            CreateSignaturePlacements(layout, keyCount, signatureAdvance);
            int whiteIndex = 0;
            for (int number = layout.First; number <= layout.Last; number++)
            {
                layout.KeyX[number] = layout.Left + layout.WhiteWidth * (IsBlack(number) ? whiteIndex : whiteIndex + .5);
                if (!IsBlack(number)) whiteIndex++;
            }
            cachedLayout = layout; layoutWidth = ActualWidth; layoutHeight = ActualHeight;
            layoutKey = KeySignatureFifths; layoutFlats = Flats; layoutFullRange = FullRange;
            layoutScoreOnly = ScoreOnly; layoutKeyboardOnly = KeyboardOnly;
            return layout;
        }

        private void DrawStaff(DrawingContext dc, Layout layout, Color ink)
        {
            if (staffDrawingLayout != layout || staffDrawingLight != LightTheme)
            {
                staffDrawing = new DrawingGroup();
                using (DrawingContext cached = staffDrawing.Open()) DrawStaffContent(cached, layout, ink);
                staffDrawing.Freeze(); staffDrawingLayout = layout; staffDrawingLight = LightTheme;
            }
            dc.DrawDrawing(staffDrawing);
        }

        private void DrawStaffContent(DrawingContext dc, Layout layout, Color ink)
        {
            Pen line = Pen(ink, LightTheme ? .26 : .29, .8);
            // E4..F5 and G2..A3: one shared diatonic coordinate system.
            for (int i = 0; i < 5; i++)
            {
                double trebleY = StepY(30 + 2 * i, layout);
                double bassY = StepY(18 + 2 * i, layout);
                dc.DrawLine(line, new Point(layout.StaffLeft, trebleY), new Point(layout.StaffRight, trebleY));
                dc.DrawLine(line, new Point(layout.StaffLeft, bassY), new Point(layout.StaffRight, bassY));
            }
            double staffTop = StepY(38, layout);
            double staffBottom = StepY(18, layout);
            dc.DrawLine(Pen(ink, .36, 1), new Point(layout.StaffLeft, staffTop), new Point(layout.StaffLeft, staffBottom));

            StreamGeometry brace = new StreamGeometry();
            double middle = (staffTop + staffBottom) / 2;
            using (StreamGeometryContext context = brace.Open())
            {
                context.BeginFigure(new Point(layout.StaffLeft - 7, staffTop), false, false);
                context.BezierTo(new Point(layout.StaffLeft - 19, staffTop + 6), new Point(layout.StaffLeft - 3, middle - 10), new Point(layout.StaffLeft - 14, middle), true, false);
                context.BezierTo(new Point(layout.StaffLeft - 3, middle + 10), new Point(layout.StaffLeft - 19, staffBottom - 6), new Point(layout.StaffLeft - 7, staffBottom), true, false);
            }
            brace.Freeze();
            dc.DrawGeometry(null, Pen(ink, .52, 1.8), brace);

            DrawClef(dc, 0xE050, "𝄞", layout.StaffLeft + 8, StepY(32, layout), layout.Spacing, ink);
            DrawClef(dc, 0xE062, "𝄢", layout.StaffLeft + 9, StepY(24, layout), layout.Spacing, ink);
            foreach (SignaturePlacement sign in layout.SignaturePlacements)
            {
                if (sign.Glyph != null) dc.DrawGlyphRun(Brush(ink, .78), sign.Glyph);
                else dc.DrawText(Text(sign.Symbol, Math.Max(11, layout.Spacing * 1.65), ink, .78, symbolFace), sign.Origin);
            }
        }

        private void EnsureMusicFont()
        {
            if (!fontLoaded)
            {
                fontLoaded = true;
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Bravura.otf");
                    if (File.Exists(path)) musicFont = new GlyphTypeface(new Uri(path, UriKind.Absolute));
                }
                catch (IOException) { }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
            }
        }

        private void CreateSignaturePlacements(Layout layout, int count, double advance)
        {
            EnsureMusicFont();
            double firstX = layout.StaffLeft + Math.Max(49, layout.Spacing * 3.3) + 7;
            int[] steps = KeySignatureFifths < 0 ? FlatSignatureSteps : SharpSignatureSteps;
            int codepoint = KeySignatureFifths < 0 ? 0xE260 : 0xE262;
            string symbol = KeySignatureFifths < 0 ? "♭" : "♯";
            layout.SignatureRight = layout.StaffLeft + layout.Spacing * 3.3;
            for (int staff = 0; staff < 2; staff++)
            for (int i = 0; i < count; i++)
            {
                int step = steps[i] - staff * 14;
                double x = firstX + i * advance;
                double y = StepY(step, layout);
                SignaturePlacement sign = new SignaturePlacement { Step = step, Symbol = symbol };
                ushort glyph;
                if (musicFont != null && musicFont.CharacterToGlyphMap.TryGetValue(codepoint, out glyph))
                {
                    double em = layout.Spacing * 4;
                    sign.Glyph = new GlyphRun(musicFont, 0, false, em, new ushort[] { glyph }, new Point(x, y),
                        new double[] { musicFont.AdvanceWidths[glyph] * em }, null, null, null, null, null, null);
                    sign.Bounds = sign.Glyph.BuildGeometry().Bounds;
                }
                else
                {
                    FormattedText text = Text(symbol, Math.Max(11, layout.Spacing * 1.65), Colors.White, 1, symbolFace);
                    sign.Origin = new Point(x, y - text.Height * .58);
                    sign.Bounds = text.BuildGeometry(sign.Origin).Bounds;
                }
                layout.SignaturePlacements.Add(sign);
                layout.SignatureRight = Math.Max(layout.SignatureRight, sign.Bounds.Right);
            }
        }

        private void DrawClef(DrawingContext dc, int codepoint, string fallback, double x, double baseline, double spacing, Color ink)
        {
            EnsureMusicFont();
            ushort glyph;
            if (musicFont != null && musicFont.CharacterToGlyphMap.TryGetValue(codepoint, out glyph))
            {
                double em = spacing * 4;
                GlyphRun run = new GlyphRun(musicFont, 0, false, em,
                    new ushort[] { glyph }, new Point(x, baseline),
                    new double[] { musicFont.AdvanceWidths[glyph] * em },
                    null, null, null, null, null, null);
                dc.DrawGlyphRun(Brush(ink, .78), run);
            }
            else
            {
                FormattedText text = Text(fallback, spacing * 4, ink, .78, symbolFace);
                dc.DrawText(text, new Point(x, baseline - spacing * (codepoint == 0xE050 ? 3.1 : 1.0)));
            }
        }

        private void DrawLedgerLines(DrawingContext dc, int number, double x, Layout layout, Color ink)
        {
            int step = DiatonicStep(number);
            int staffLow = step >= 28 ? 30 : 18;
            int staffHigh = step >= 28 ? 38 : 26;
            Pen line = Pen(ink, .52, 1);
            double halfWidth = layout.HeadWidth * .75;
            if (step < staffLow)
            {
                for (int current = staffLow - 2; current >= step; current -= 2)
                {
                    double y = StepY(current, layout);
                    dc.DrawLine(line, new Point(x - halfWidth, y), new Point(x + halfWidth, y));
                }
            }
            else if (step > staffHigh)
            {
                for (int current = staffHigh + 2; current <= step; current += 2)
                {
                    double y = StepY(current, layout);
                    dc.DrawLine(line, new Point(x - halfWidth, y), new Point(x + halfWidth, y));
                }
            }
        }

        private List<NotePlacement> PlaceNotes(Layout layout, IDictionary<int, ActiveNote> active)
        {
            // Note age and harmony tint change on every frame, while pitch geometry
            // normally stays fixed. Reuse that geometry and only refresh its data.
            bool samePitches = placementLayout == layout && cachedPlacements != null && cachedPlacements.Count == active.Count;
            if (samePitches)
                foreach (NotePlacement placement in cachedPlacements)
                    if (!active.ContainsKey(placement.Number)) { samePitches = false; break; }
            if (samePitches)
            {
                foreach (NotePlacement placement in cachedPlacements)
                {
                    placement.Note = active[placement.Number];
                    if (placement.Accidental == null) continue;
                    Brush tint = Brush(RenderNoteColor(placement.Note), NoteOpacity(placement.Note));
                    if (!ReferenceEquals(tint, placement.AccidentalBrush))
                    { placement.Accidental.SetForegroundBrush(tint); placement.AccidentalBrush = tint; }
                }
                return cachedPlacements;
            }
            var ended = new List<int>();
            foreach (int number in stableHeadSlots.Keys)
                if (!active.ContainsKey(number)) ended.Add(number);
            foreach (int number in ended) stableHeadSlots.Remove(number);

            double offset = layout.UnisonOffset;
            double maximumOffset = Math.Max(84, layout.Spacing * 8.4);
            while (true)
            {
                List<NotePlacement> placements = PlaceNotesWithOffset(layout, active, offset);
                bool collision = false;
                foreach (NotePlacement placement in placements)
                {
                    if (placement.Accidental == null) continue;
                    Rect sign = placement.Accidental.BuildGeometry(placement.AccidentalBounds.TopLeft).Bounds;
                    sign.Inflate(.8, .8);
                    foreach (NotePlacement other in placements)
                    {
                        bool sharedPitchPosition = placement.HeadGroupX > layout.NoteX &&
                            other.HeadGroupX == layout.NoteX && Math.Abs(placement.Y - other.Y) < .01;
                        if (sign.IntersectsWith(other.HeadBounds) ||
                            (sharedPitchPosition && sign.Left <= other.HeadBounds.Right + .5))
                        {
                            collision = true;
                            break;
                        }
                    }
                    if (collision) break;
                }
                if (!collision || offset >= maximumOffset)
                {
                    foreach (NotePlacement placement in placements)
                        if (!stableHeadSlots.ContainsKey(placement.Note.Number))
                            stableHeadSlots[placement.Note.Number] = new HeadSlot {
                                GroupOffset = placement.HeadGroupX - layout.NoteX, Column = placement.Column };
                    placementLayout = layout; cachedPlacements = placements;
                    return placements;
                }
                // Expand only when the actual played neighbours need extra room.
                // The staff and its main column remain stationary.
                offset = Math.Min(maximumOffset, offset + 2);
            }
        }

        private List<NotePlacement> PlaceNotesWithOffset(Layout layout, IDictionary<int, ActiveNote> active, double unisonOffset)
        {
            Dictionary<int, int> stepCounts = new Dictionary<int, int>();
            foreach (ActiveNote note in active.Values)
            {
                int step = DiatonicStep(note.Number);
                int count;
                stepCounts.TryGetValue(step, out count);
                stepCounts[step] = count + 1;
            }
            List<ActiveNote> sorted = new List<ActiveNote>(active.Values);
            sorted.Sort(delegate(ActiveNote a, ActiveNote b)
            {
                bool aStable = stableHeadSlots.ContainsKey(a.Number);
                bool bStable = stableHeadSlots.ContainsKey(b.Number);
                if (aStable != bStable) return aStable ? -1 : 1;
                int comparison = DiatonicStep(a.Number).CompareTo(DiatonicStep(b.Number));
                if (comparison != 0) return comparison;
                // Keep the signature's diatonic note on the main column when its
                // chromatic neighbour is also held, including F# beside F natural.
                comparison = Spell(b.Number).InKey.CompareTo(Spell(a.Number).InKey);
                return comparison != 0 ? comparison : a.Number.CompareTo(b.Number);
            });
            List<NotePlacement> result = new List<NotePlacement>();
            double headWidth = layout.HeadWidth;
            foreach (ActiveNote note in sorted)
            {
                NotatedPitch pitch = Spell(note.Number);
                bool chromaticUnison = KeySignatureFifths != 0 && stepCounts[pitch.Step] > 1;
                HeadSlot stable;
                bool hasStableSlot = stableHeadSlots.TryGetValue(note.Number, out stable);
                NotePlacement placement = new NotePlacement { Note = note, Number = note.Number, Y = PitchY(note.Number, layout),
                    HeadGroupX = hasStableSlot ? layout.NoteX + stable.GroupOffset :
                        layout.NoteX + (chromaticUnison && !pitch.InKey ? unisonOffset : 0),
                    AccidentalText = chromaticUnison ? (pitch.Alteration > 0 ? "♯" : pitch.Alteration < 0 ? "♭" : "♮") : pitch.Accidental };
                int column = hasStableSlot ? stable.Column : 0;
                int attempt = 0;
                while (true)
                {
                    if (!hasStableSlot)
                        column = attempt == 0 ? 0 : (attempt % 2 == 1 ? (attempt + 1) / 2 : -(attempt / 2));
                    placement.X = placement.HeadGroupX + column * (headWidth + 1.2);
                    placement.Column = column;
                    // Bounds conservatively contain the rotated filled ellipse, with a small gap.
                    placement.HeadBounds = new Rect(placement.X - headWidth * .5,
                        placement.Y - headWidth * .38, headWidth, headWidth * .76);
                    bool collision = false;
                    foreach (NotePlacement previous in result)
                        if (placement.HeadBounds.IntersectsWith(previous.HeadBounds)) { collision = true; break; }
                    if (!collision || hasStableSlot) break;
                    attempt++;
                }
                result.Add(placement);
            }

            // A same-letter chromatic pair gets explicit signs in two separate head
            // groups. For example, D-major F# + F natural cannot look like one natural
            // sign applies to both heads. Each group packs its own signs to its left.
            List<Rect> signs = new List<Rect>();
            foreach (NotePlacement placement in result)
            {
                string accidental = placement.AccidentalText;
                if (accidental.Length == 0) continue;
                Color color = RenderNoteColor(placement.Note);
                double opacity = NoteOpacity(placement.Note);
                placement.Accidental = Text(accidental, Math.Max(11, layout.Spacing * 1.65),
                    color, opacity, symbolFace);
                placement.AccidentalBrush = Brush(color, opacity);
                double width = Math.Max(placement.Accidental.WidthIncludingTrailingWhitespace, placement.Accidental.Extent * .45);
                double signRight = placement.HeadGroupX - headWidth * .5 - 5;
                double y = placement.Y - placement.Accidental.Height * .58;
                for (int column = 0; ; column++)
                {
                    Rect bounds = new Rect(signRight - width - column * (width + 3), y, width, placement.Accidental.Height);
                    Rect padded = bounds; padded.Inflate(1, 1);
                    bool collision = false;
                    foreach (NotePlacement head in result)
                        if (padded.IntersectsWith(head.HeadBounds)) { collision = true; break; }
                    if (collision) continue;
                    foreach (Rect previous in signs)
                        if (padded.IntersectsWith(previous)) { collision = true; break; }
                    if (collision) continue;
                    placement.AccidentalBounds = bounds;
                    signs.Add(padded);
                    break;
                }
            }
            return result;
        }

        private void DrawNote(DrawingContext dc, NotePlacement placement, Layout layout)
        {
            ActiveNote note = placement.Note;
            double x = placement.X;
            double y = placement.Y;
            Color color = RenderNoteColor(note);
            double opacity = NoteOpacity(note);
            double size = layout.HeadWidth;
            if (note.IsHeld)
            {
                dc.DrawEllipse(Brush(color, opacity * .07), null, new Point(x, y), size * 1.25, size * .97);
                dc.DrawEllipse(Brush(color, opacity * .13), null, new Point(x, y), size * .83, size * .67);
            }
            DrawHead(dc, x, y, size, Brush(color, opacity), null);
            if (placement.Accidental != null)
                dc.DrawText(placement.Accidental, placement.AccidentalBounds.TopLeft);
        }

        private void DrawKeyboard(DrawingContext dc, Layout layout, IDictionary<int, ActiveNote> active, Color ink)
        {
            double rasterScale = KeyboardRasterScale();
            if (keyboardDrawingLayout != layout || keyboardDrawingLight != LightTheme)
            {
                BuildKeyboardDrawings(layout, ink);
                keyboardDrawingLayout = layout; keyboardDrawingLight = LightTheme;
                whiteKeyBitmap = blackKeyBitmap = null;
            }
            if (whiteKeyBitmap == null || Math.Abs(keyboardBitmapScale - rasterScale) > .01)
            { BuildKeyboardBitmaps(rasterScale); keyboardBitmapScale = rasterScale; }
            dc.PushTransform(new MatrixTransform(keyboardScale, 0, 0, keyboardScale,
                (layout.Left + layout.Right) * .5 * (1 - keyboardScale) + keyboardOffsetX,
                (layout.KeyboardTop + layout.KeyboardHeight * .5) * (1 - keyboardScale) + keyboardOffsetY));
            // Split the static keybed at its original white/black z boundary.
            // Highlights retain their native vector edges and original clipping.
            for (int layer = 0; layer < 2; layer++)
            {
                dc.DrawImage(layer == 0 ? whiteKeyBitmap : blackKeyBitmap, new Rect(0, 0, ActualWidth, ActualHeight));
                foreach (KeyDrawing key in keyDrawings)
                {
                    if (key.Black != (layer == 1)) continue;
                    ActiveNote note;
                    if (!active.TryGetValue(key.Number, out note)) continue;
                    if (key.Clip != null) dc.PushClip(key.Clip);
                    DrawKeyHighlight(dc, key.Face, note, key.Black);
                    if (key.Clip != null) dc.Pop();
                }
            }
            dc.DrawDrawing(keyboardTopDrawing);
            dc.Pop();
        }

        private double KeyboardRasterScale()
        {
            double scale = Finite(RenderPixelScale) ? Math.Max(.5, RenderPixelScale) : 1.5;
            PresentationSource source = PresentationSource.FromVisual(this);
            Visual root = this;
            Visual parent;
            while ((parent = VisualTreeHelper.GetParent(root) as Visual) != null) root = parent;
            Rect pixel = TransformToAncestor(root).TransformBounds(new Rect(0, 0, 1, 1));
            if (source != null && source.CompositionTarget != null)
            {
                Matrix dpi = source.CompositionTarget.TransformToDevice;
                scale = Math.Max(pixel.Width * dpi.M11, pixel.Height * dpi.M22);
            }
            else scale *= Math.Max(pixel.Width, pixel.Height);
            return Math.Max(.5, Math.Min(4, scale * keyboardScale));
        }

        private void BuildKeyboardBitmaps(double scale)
        {
            for (int layer = 0; layer < 2; layer++)
            {
                var visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                    foreach (KeyDrawing key in keyDrawings)
                        if (key.Black == (layer == 1)) dc.DrawDrawing(key.Drawing);
                var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth * scale)),
                    Math.Max(1, (int)Math.Ceiling(ActualHeight * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(visual); bitmap.Freeze();
                if (layer == 0) whiteKeyBitmap = bitmap; else blackKeyBitmap = bitmap;
            }
        }

        private void BuildKeyboardDrawings(Layout layout, Color ink)
        {
            keyDrawings.Clear();
            double top = layout.KeyboardTop, length = layout.KeyboardHeight;
            double gap = Math.Max(.55, layout.WhiteWidth * .032);
            Brush whiteFace = KeyGradient("#20D9F3FF", "#08D9F3FF", "#16C7E6F4", false);
            Brush blackFace = KeyGradient("#302D4155", "#180D1B29", "#282E4357", false);
            Brush whiteEdge = KeyGradient("#12B9D8E8", "#04DCEFF8", "#18BCDCEB", false);
            Brush blackSide = KeyGradient("#283A5165", "#080A1724", "#283A5165", true);
            for (int number = layout.First; number <= layout.Last; number++)
            {
                if (IsBlack(number)) continue;
                double x = layout.KeyX[number] - layout.WhiteWidth / 2;
                Rect rect = new Rect(x + gap / 2, top, layout.WhiteWidth - gap, length);
                Geometry keyShape = new RectangleGeometry(rect, 1.4, 1.4);
                foreach (int neighbor in new[] { number - 1, number + 1 })
                {
                    if (neighbor < layout.First || neighbor > layout.Last || !IsBlack(neighbor)) continue;
                    double blackWidth = layout.WhiteWidth * .58;
                    var cutout = new RectangleGeometry(new Rect(layout.KeyX[neighbor] - blackWidth / 2 - .5, top, blackWidth + 1, length * .63 + .5));
                    keyShape = new CombinedGeometry(GeometryCombineMode.Exclude, keyShape, cutout);
                }
                // Resolve the black-key cutouts once; retaining CombinedGeometry
                // otherwise repeats its boolean clipping work on software frames.
                keyShape = keyShape.GetOutlinedPathGeometry();
                keyShape.Freeze();
                var drawing = new DrawingGroup();
                Rect face = new Rect(rect.X, rect.Y, rect.Width, Math.Max(1, rect.Height - 3));
                using (DrawingContext dc = drawing.Open())
                {
                    dc.PushClip(keyShape);
                    dc.DrawRoundedRectangle(whiteEdge, Pen(ink, .16, .65), rect, 1.4, 1.4);
                    dc.DrawRoundedRectangle(whiteFace, null, face, 1.3, 1.3);
                    dc.DrawLine(Pen(ink, .18, .65), new Point(rect.Left + .8, top + 2), new Point(rect.Left + .8, face.Bottom - 1));
                    dc.DrawLine(Pen(ink, .25, .7), new Point(rect.Left + 1, face.Bottom - .8), new Point(rect.Right - 1, face.Bottom - .8));
                    dc.Pop();
                }
                drawing.Freeze();
                keyDrawings.Add(new KeyDrawing { Number = number, Drawing = drawing, Clip = keyShape, Face = face });
            }
            for (int number = layout.First; number <= layout.Last; number++)
            {
                if (!IsBlack(number)) continue;
                double width = layout.WhiteWidth * .58;
                Rect rect = new Rect(layout.KeyX[number] - width / 2, top, width, length * .63);
                var drawing = new DrawingGroup();
                double bevel = width * .125;
                Rect face = new Rect(rect.Left + bevel, rect.Top + .7, rect.Width - bevel * 2, Math.Max(1, rect.Height - 4));
                using (DrawingContext dc = drawing.Open())
                {
                    dc.DrawRoundedRectangle(Brush(Colors.Black, .06), null,
                        new Rect(rect.X - 1, rect.Y + 2, rect.Width + 3, rect.Height + 2), 2, 2);
                    dc.DrawRoundedRectangle(blackSide, Pen(ink, .22, .7), rect, 1.4, 1.4);
                    dc.DrawRoundedRectangle(blackFace, null, face, .7, .7);
                    dc.DrawLine(Pen(ink, .24, .65), new Point(face.Left + .3, face.Top + 1), new Point(face.Left + .3, face.Bottom - 1));
                    dc.DrawLine(Pen(ink, .18, .7), new Point(face.Left, face.Bottom), new Point(face.Right, face.Bottom));
                }
                drawing.Freeze();
                keyDrawings.Add(new KeyDrawing { Number = number, Drawing = drawing, Face = face, Black = true });
            }
            keyboardTopDrawing = new DrawingGroup();
            using (DrawingContext dc = keyboardTopDrawing.Open())
            {
                dc.DrawRectangle(KeyGradient("#18000000", "#06000000", "#00000000", false), null,
                    new Rect(layout.Left, top, layout.Right - layout.Left, Math.Min(5, length * .04)));
            }
            keyboardTopDrawing.Freeze();
        }

        private static Brush KeyGradient(string first, string middle, string last, bool horizontal)
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1) };
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(first), 0));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(middle), .36));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(last), 1));
            gradient.Freeze(); return gradient;
        }

        private void DrawKeyHighlight(DrawingContext dc, Rect rect, ActiveNote note, bool black)
        {
            Color color = RenderNoteColor(note);
            double opacity = NoteOpacity(note);
            dc.DrawRoundedRectangle(Brush(color, opacity * (black ? .92 : .85)), null, rect, 2, 2);
            if (!note.IsHeld) return;
            double margin = black ? 2 : 3;
            dc.DrawRoundedRectangle(Brush(color, opacity), null,
                new Rect(rect.Left + margin, rect.Bottom - 4, Math.Max(1, rect.Width - 2 * margin), 2), 1, 1);
        }

        private static void DrawHead(DrawingContext dc, double x, double y, double width, Brush fill, Pen outline)
        {
            dc.PushTransform(new RotateTransform(-18, x, y));
            dc.DrawEllipse(fill, outline, new Point(x, y), width / 2, width * .345);
            dc.Pop();
        }

        private Color NoteColor(int velocity) { return AccentColor; }

        private Color RenderNoteColor(ActiveNote note)
        { return note.HasTint ? Color.FromRgb(note.TintR, note.TintG, note.TintB) : AccentColor; }

        private double NoteOpacity(ActiveNote note)
        {
            double velocity = Math.Max(1, Math.Min(127, note.Velocity)) / 127.0;
            return double.IsNaN(note.Brightness) ? 0 : Math.Max(0, Math.Min(1, note.Brightness)) * (.3 + .7 * velocity);
        }

        private static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * amount),
                (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
        }

        private SolidColorBrush Brush(Color color, double opacity)
        {
            byte alpha = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * color.A);
            uint key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            SolidColorBrush result;
            if (!brushes.TryGetValue(key, out result))
            {
                if (brushes.Count > 2048) brushes.Clear();
                result = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                result.Freeze();
                brushes[key] = result;
            }
            return result;
        }

        private Pen Pen(Color color, double opacity, double thickness)
        {
            var brush = Brush(color, opacity);
            Color c = brush.Color;
            uint argb = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            ulong key = ((ulong)argb << 32) | (uint)Math.Round(thickness * 10000);
            Pen pen;
            if (pens.TryGetValue(key, out pen)) return pen;
            if (pens.Count > 2048) pens.Clear();
            pen = new Pen(brush, thickness); pen.Freeze(); pens[key] = pen;
            return pen;
        }

        private FormattedText Text(string text, double size, Color color, double opacity, Typeface face)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, size, Brush(color, opacity));
        }

        private int DiatonicStep(int number)
        {
            return Spell(number).Step;
        }

        private NotatedPitch Spell(int number)
        {
            if (spellings == null || spellingKey != KeySignatureFifths || spellingFlats != Flats)
            { spellings = new NotatedPitch[128]; spellingKey = KeySignatureFifths; spellingFlats = Flats; }
            return spellings[number] ?? (spellings[number] = KeySignature.Spell(number, KeySignatureFifths, Flats));
        }

        private double PitchY(int number, Layout layout)
        {
            return StepY(DiatonicStep(number), layout);
        }

        private static double StepY(int step, Layout layout)
        {
            return layout.Top + (layout.TopStep - step) * layout.Spacing / 2;
        }

        private static bool IsBlack(int number)
        {
            int pitch = number % 12;
            return pitch == 1 || pitch == 3 || pitch == 6 || pitch == 8 || pitch == 10;
        }

        private sealed class Layout
        {
            public int First, Last, TopStep;
            public double Left, Right, Top, Spacing, WhiteWidth, HeadWidth, KeyboardTop, KeyboardHeight;
            public double StaffLeft, StaffRight, NoteX, SignatureRight, UnisonOffset;
            public readonly Dictionary<int, double> KeyX = new Dictionary<int, double>();
            public readonly HashSet<int> GhostSteps = new HashSet<int>();
            public readonly List<SignaturePlacement> SignaturePlacements = new List<SignaturePlacement>();
        }

        private sealed class SignaturePlacement
        {
            public int Step;
            public string Symbol;
            public GlyphRun Glyph;
            public Point Origin;
            public Rect Bounds;
        }

        private sealed class NotePlacement
        {
            public ActiveNote Note;
            public int Number;
            public double X, Y, HeadGroupX;
            public int Column;
            public string AccidentalText;
            public Rect HeadBounds, AccidentalBounds;
            public FormattedText Accidental;
            public Brush AccidentalBrush;
        }

        private sealed class KeyDrawing
        {
            public int Number;
            public bool Black;
            public DrawingGroup Drawing;
            public Geometry Clip;
            public Rect Face;
        }

        private sealed class HeadSlot
        {
            public double GroupOffset;
            public int Column;
        }
    }
}
