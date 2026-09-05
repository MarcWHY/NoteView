using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NoteView
{
    public static class UiSmokeTests
    {
        private static int checks;
        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application();
                var main = new MainWindow(true) { ShowInTaskbar = false, ShowActivated = false, Left = -15000, Top = -15000 };
                main.Show(); main.UpdateLayout(); Pump();
                main.SetPreviewChord(); main.UpdateLayout();
                Check(!Field<System.Windows.Controls.TextBlock>(main, "chordDescription").IsVisible &&
                    !Field<System.Windows.Controls.TextBlock>(main, "noteList").IsVisible &&
                    !Field<System.Windows.Controls.TextBlock>(main, "chordAlternatives").IsVisible,
                    "harmony panel displays only its symbol");
                Check(Field<TextBlock>(main, "chordSymbol").Text == "C△7", "demo MIDI to chord UI");
                Check(Field<StaffView>(main, "staff").Notes.Count == 5, "demo MIDI to staff UI");
                Invoke(main, "StopDemo");
                Check(Field<StaffView>(main, "staff").Notes.Count == 0, "stop demo clears highlights");
                var state = Field<NoteState>(main, "notes");
                state.Process(0x90, 60, 30); state.Process(0x90, 64, 80); state.Process(0x90, 67, 127);
                Invoke(main, "RenderNotes", true);
                Check(Field<TextBlock>(main, "chordSymbol").Text == "C", "live state to harmony label");
                state.Process(0xB0, 64, 127); state.Process(0x80, 60, 0); Invoke(main, "RenderNotes", true);
                Check(Field<StaffView>(main, "staff").Notes.Single(n => n.Number == 60).IsHeld == false, "pedal state reaches staff");
                Check(Field<TextBlock>(main, "pedal").Text.StartsWith("●"), "pedal indicator");
                state.Process(0xB0, 64, 0); Invoke(main, "RenderNotes", true);
                Check(Field<StaffView>(main, "staff").Notes.Count == 2, "pedal release removes sustained note");
                main.Settings.Channel = 2; main.ChannelChanged();
                Check(state.GetActiveNotes().Count == 0, "channel switch clears previous input");
                main.Settings.Channel = 0;
                main.ApplySettings(); main.UpdateLayout();
                double scoreHeightBeforeLongChord = Field<StaffView>(main, "staff").ActualHeight;
                foreach (int number in new[] { 48, 61, 64, 66, 67, 68, 70 }) state.Process(0x90, number, 90);
                Invoke(main, "RenderNotes", true); main.UpdateLayout();
                var extendedLabel = Field<TextBlock>(main, "chordSymbol");
                Check(extendedLabel.Text == "C7(♭9,♯11,♭13)", "altered extended chord reaches UI");
                Check(extendedLabel.FontSize == 30 && extendedLabel.TextWrapping == TextWrapping.NoWrap && double.IsPositiveInfinity(extendedLabel.MaxWidth), "long chord symbols keep full font size on one line");
                Check(extendedLabel.TextTrimming == TextTrimming.None, "long chord symbols are not ellipsized");
                Check(Math.Abs(Field<StaffView>(main, "staff").ActualHeight - scoreHeightBeforeLongChord) < .01, "score geometry stays fixed when chord text wraps");
                Render(main, "ui-extended-chord.png");
                state.Clear();
                main.SetPreviewChord(); main.UpdateLayout();
                Render(main, "ui-default.png");
                main.ShowSettings();
                var settings = Field<SettingsWindow>(main, "settingsWindow");
                settings.ShowInTaskbar = false; settings.ShowActivated = false; settings.Left = -15000; settings.Top = -15000;
                settings.UpdateLayout(); Pump();
                var keySelector = Field<ComboBox>(main, "keySelector");
                var keyChoice = Field<ComboBox>(settings, "keyChoice");
                Check(keySelector.Items.Count == 30 && keyChoice.Items.Count == 30, "all major and minor key options available");
                Invoke(main, "StopDemo"); state.Clear();
                foreach (int number in new[] { 60, 62, 65, 69, 73, 78 }) state.Process(0x90, number, 90);
                keySelector.SelectedIndex = MainWindow.KeyOptionIndex(2, false);
                Check(main.Settings.KeySignatureFifths == 2 && !main.Settings.MinorKey, "score selector changes default to D major");
                Check(Field<StaffView>(main, "staff").KeySignatureFifths == 2, "D key signature reaches staff");
                Check(keyChoice.SelectedIndex == keySelector.SelectedIndex, "main selector synchronizes open settings");
                Check(state.GetActiveNotes().Select(n => n.Number).SequenceEqual(new[] { 60, 62, 65, 69, 73, 78 }), "changing key preserves actual sounding MIDI pitches");
                Check(Field<TextBlock>(main, "noteList").Text.Contains("F4") && Field<TextBlock>(main, "noteList").Text.Contains("F#5"), "key-aware note names reach information panel");
                Render(main, "ui-d-major.png"); Render(settings, "ui-key-settings.png");
                keyChoice.SelectedIndex = MainWindow.KeyOptionIndex(2, true);
                Check(main.Settings.MinorKey && main.Settings.KeySignatureFifths == 2, "settings selector changes to relative B minor");
                Check(keySelector.SelectedIndex == keyChoice.SelectedIndex, "settings selector synchronizes score selector");
                CheckScoreControls(main, settings, state);
                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var buffer = new StringWriter())
                {
                    serializer.Serialize(buffer, main.Settings);
                    using (var reader = new StringReader(buffer.ToString()))
                    {
                        var restored = (AppSettings)serializer.Deserialize(reader);
                        Check(restored.KeySignatureFifths == 2 && restored.MinorKey, "default key survives persisted settings roundtrip");
                        Check(Near(restored.StaffScale, 1.25) && Near(restored.StaffOffsetX, 80) && Near(restored.StaffOffsetY, 20),
                            "score size and position survive persisted settings roundtrip");
                    }
                }
                using (var reader = new StringReader("<AppSettings><Flats>true</Flats></AppSettings>"))
                {
                    var legacy = (AppSettings)serializer.Deserialize(reader);
                    Check(legacy.KeySignatureFifths == 0 && !legacy.MinorKey && legacy.Flats, "legacy settings retain C major and previous spelling preference");
                    Check(Near(legacy.StaffScale, 1) && Near(legacy.StaffOffsetX, 0) && Near(legacy.StaffOffsetY, 0),
                        "legacy settings retain original score size and centered position");
                }
                main.ResetScoreTransform();
                state.Clear(); main.SetKeySignature(0, false); main.SetPreviewChord();
                Invoke(settings, "SetBackground", "#F1F4EF", true);
                Check(Field<CheckBox>(settings, "darkInkCheckbox").IsChecked == true, "background preset checkbox sync");
                Check(Field<StaffView>(main, "staff").LightTheme, "background preset staff ink sync");
                Render(settings, "ui-settings.png");
                settings.Close();
                main.Settings.DarkInk = false;
                main.SetPreviewTransparent(); main.UpdateLayout();
                var transparent = Render(main, "ui-transparent.png");
                byte[] pixels = new byte[transparent.PixelWidth * transparent.PixelHeight * 4];
                transparent.CopyPixels(pixels, transparent.PixelWidth * 4, 0);
                int zeroAlpha = 0, partialAlpha = 0;
                for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] == 0) zeroAlpha++; else if (pixels[i] < 255) partialAlpha++; }
                Check(zeroAlpha > transparent.PixelWidth * transparent.PixelHeight * .75, "transparent background has zero-alpha pixels");
                Check(partialAlpha > 1000, "note/staff rendering retains variable opacity");
                main.Width = 880; main.Height = 640; main.Settings.FullRange = false; main.Settings.Flats = true;
                main.ApplySettings(); main.UpdateLayout(); Render(main, "ui-small.png");
                Check(Field<StaffView>(main, "staff").ActualHeight >= 150, "minimum size leaves usable score area");
                main.Close(); app.Shutdown();
                Console.WriteLine("UiSmokeTests: PASS (" + checks + " assertions); preview PNGs in artifacts/tests.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void CheckScoreControls(MainWindow main, SettingsWindow settings, NoteState state)
        {
            var staff = Field<StaffView>(main, "staff");
            var board = Field<LayoutBoard>(main, "layoutBoard");
            var scale = Field<Slider>(settings, "scoreScale");
            var offsetX = Field<Slider>(settings, "scoreOffsetX");
            var offsetY = Field<Slider>(settings, "scoreOffsetY");
            Check(Near(main.Settings.StaffScale, 1) && Near(main.Settings.StaffOffsetX, 0) && Near(main.Settings.StaffOffsetY, 0),
                "score transform starts at original size and position");
            Check(Near(scale.Value, 100) && Near(offsetX.Value, 0) && Near(offsetY.Value, 0),
                "settings show default score size and position");

            // A real held chord and quiet pedal-only notes must survive purely visual edits.
            state.Clear();
            int[] pitches = { 48, 65, 66, 69, 73 };
            int[] velocities = { 88, 24, 105, 64, 42 };
            for (int i = 0; i < pitches.Length; i++) state.Process(0x90, pitches[i], velocities[i]);
            state.Process(0xB0, 64, 127);
            state.Process(0x80, 65, 0); state.Process(0x80, 73, 0);
            Invoke(main, "RenderNotes", true);
            string before = Snapshot(state);

            scale.Value = 125; offsetX.Value = 80; offsetY.Value = 20;
            main.UpdateLayout();
            Check(Near(main.Settings.StaffScale, 1.25) && Near(main.Settings.StaffOffsetX, 80) && Near(main.Settings.StaffOffsetY, 20),
                "score settings sliders update persisted size and position");
            Check(Near(board.Bounds(0).Width / 440, 1.25) && Near(board.Bounds(0).X - 340, 80) && Near(board.Bounds(0).Y, 20),
                "score settings sliders reach renderer");
            Check(Snapshot(state) == before && state.SustainDown,
                "changing score size and position preserves held notes and pedal sustain");
            Check(staff.Notes.Count(n => n.IsHeld) == 3 && staff.Notes.Count(n => !n.IsHeld) == 2 &&
                staff.Notes.Single(n => n.Number == 65).Velocity == 24,
                "quiet sustained notes and held notes remain distinct at renderer boundary");
            Check(main.Settings.KeySignatureFifths == 2 && main.Settings.MinorKey && staff.KeySignatureFifths == 2,
                "visual score changes preserve selected key signature");

            main.SetScoreTransform(1.3, -40, 25);
            Check(Near(scale.Value, 130) && Near(offsetX.Value, -40) && Near(offsetY.Value, 25),
                "score transform synchronizes open settings controls");
            Check(Near(board.Bounds(0).Width / 440, 1.3) && Near(board.Bounds(0).X - 340, -40) && Near(board.Bounds(0).Y, 25),
                "direct score transform reaches renderer");

            main.SetScoreEditing(true); main.UpdateLayout();
            Check(Field<bool>(main, "editingScore") && Field<Border[]>(board, "boxes")[0].Visibility == Visibility.Visible,
                "score editing shows its interaction overlay");
            main.SetScoreEditing(false); main.UpdateLayout();
            Check(!Field<bool>(main, "editingScore") && Field<Border[]>(board, "boxes")[0].Visibility != Visibility.Visible,
                "leaving score editing hides its interaction overlay");
            Check(Snapshot(state) == before && state.SustainDown, "entering and leaving score editing preserves sounding notes");

            main.ResetScoreTransform();
            Check(Near(main.Settings.StaffScale, 1) && Near(main.Settings.StaffOffsetX, 0) && Near(main.Settings.StaffOffsetY, 0),
                "reset restores default score size and position");
            Check(Near(board.Bounds(0).Width / 440, 1) && Near(board.Bounds(0).X - 340, 0) && Near(board.Bounds(0).Y, 0) &&
                Near(scale.Value, 100) && Near(offsetX.Value, 0) && Near(offsetY.Value, 0),
                "reset synchronizes renderer and open settings sliders");
            Check(Snapshot(state) == before && state.SustainDown && staff.KeySignatureFifths == 2,
                "reset preserves active pitches pedal and key signature");

            main.SetScoreTransform(1.25, 80, 20); main.UpdateLayout();
            Render(main, "ui-score-adjusted-pedal.png");
            main.SetScoreEditing(true); main.UpdateLayout();
            Render(main, "ui-score-editing.png");
            main.SetScoreEditing(false);
        }
        private static string Snapshot(NoteState state)
        { return string.Join("|", state.GetActiveNotes().Select(n => n.Number + ":" + n.Velocity + ":" + n.IsHeld)); }
        private static bool Near(double actual, double expected)
        { return Math.Abs(actual - expected) < .001; }
        private static void Check(bool condition, string label)
        { checks++; if (!condition) throw new Exception("FAILED: " + label); }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static void Invoke(object target, string name, params object[] args)
        { target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        private static RenderTargetBitmap Render(Window window, string name)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(stream);
            return bitmap;
        }
    }
}
