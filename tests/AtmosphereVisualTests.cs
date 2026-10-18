using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class AtmosphereVisualTests
    {
        private const int Width = ObsFrameRenderer.PixelWidth, Height = ObsFrameRenderer.PixelHeight;
        private const string Gold = "#EDBC70", Teal = "#4FD9C7", Rose = "#E273BB", Blue = "#709BDD";
        private static int checks;
        private static readonly List<string> failures = new List<string>();

        private sealed class Scene
        {
            public string Name, Symbol, Secondary;
            public AppSettings Settings;
            public IList<ActiveNote> Notes;
        }
        private sealed class Regions
        {
            public int Eligible, Primary, Secondary;
            public double PrimaryX, PrimaryY, SecondaryX, SecondaryY;
        }
        private struct Direction
        {
            public double R, G, B;
            public Direction(double r, double g, double b)
            {
                double mean = (r + g + b) / 3;
                r -= mean; g -= mean; b -= mean;
                double norm = Math.Sqrt(r * r + g * g + b * b);
                R = norm > 0 ? r / norm : 0; G = norm > 0 ? g / norm : 0; B = norm > 0 ? b / norm : 0;
            }
            public double Dot(Direction other) { return R * other.R + G * other.G + B * other.B; }
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application();
                Scene[] scenes = Scenes();
                CheckDisableAndSilence(scenes[1]);
                CheckTransforms(scenes[1]);
                CheckActualFrameBackground(scenes[1]);
                CheckTextureSize(scenes[1].Settings);
                foreach (Scene scene in scenes) SavePreviews(scene);
                foreach (Scene scene in scenes)
                {
                    foreach (bool black in new[] { false, true })
                    {
                        var settings = scene.Settings.Snapshot();
                        settings.Background = "#000000"; settings.BackgroundOpacity = black ? 100 : 0;
                        settings.AtmosphereOpacity = black ? 10 : 12;
                        byte[] background = Background(settings);
                        if (!black) CheckCorners(background, scene.Name);
                        Regions regions = Classify(background, scene.Settings.HarmonyTint, scene.Secondary ?? Teal);
                        Console.WriteLine(scene.Name + " " + (black ? "black10" : "transparent12") +
                            ": eligible=" + regions.Eligible + ", primary=" + regions.Primary + ", secondary=" + regions.Secondary);
                        if (scene.Secondary == null)
                        {
                            Check(regions.Eligible >= 12000, "pure triad has a substantial visible atmosphere");
                            Check(regions.Primary >= regions.Eligible * .92 && regions.Secondary < regions.Eligible * .01,
                                "pure triad remains one coherent hue despite unused relation/history colors");
                        }
                        else
                        {
                            int required = Math.Max(12000, (int)Math.Ceiling(regions.Eligible * .12));
                            Check(regions.Primary >= required, scene.Name + " preserves a sizeable base-color region (required=" + required + ")");
                            Check(regions.Secondary >= required, scene.Name + " has a sizeable distinct secondary-color region (required=" + required + ")");
                            double dx = regions.PrimaryX / regions.Primary - regions.SecondaryX / regions.Secondary;
                            double dy = regions.PrimaryY / regions.Primary - regions.SecondaryY / regions.Secondary;
                            Check(Math.Sqrt(dx * dx + dy * dy) >= 60, scene.Name + " distinct colors occupy separate spatial regions");
                        }
                        Check(Same(background, Background(settings)), scene.Name + " identical parameters give stable pixels");
                    }
                }
                app.Shutdown();
                if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
                Console.WriteLine("AtmosphereVisualTests: PASS (" + checks + " assertions); previews in artifacts/atmosphere-visual-tests.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static Scene[] Scenes()
        {
            var pure = Settings(Teal, Blue, 0, 0);
            var extended = Settings(Teal, Gold, .85, 0);
            var passing = Settings(Rose, Gold, .85, 0);
            var history = Settings(Gold, Blue, 0, .9);
            return new[] {
                new Scene { Name = "triad-gold", Symbol = "C", Settings = pure, Notes = Notes(Gold, 48, 60, 64, 67) },
                new Scene { Name = "maj9-gold-teal", Symbol = "Cmaj9", Secondary = Teal, Settings = extended,
                    Notes = Add(Notes(Gold, 48, 60, 64, 67), Notes(Teal, 71, 74)) },
                new Scene { Name = "passing-rose", Symbol = "C", Secondary = Rose, Settings = passing,
                    Notes = Add(Notes(Gold, 48, 60, 64, 67), Notes(Rose, 78)) },
                EngineScene(),
                new Scene { Name = "history-blue", Symbol = "C", Secondary = Blue, Settings = history,
                    Notes = Add(Notes(Gold, 48, 60, 64, 67), Notes(Blue, 69)) }
            };
        }
        private static Scene EngineScene()
        {
            double now = 0;
            var engine = new HarmonyColor(delegate { return now; });
            var notes = Notes(Gold, 48, 60, 64, 67, 71, 74);
            for (int frame = 0; frame <= 220; frame++)
            { now = frame * .025; engine.Update(notes, true); }
            engine.ApplyNoteTints(notes);
            Check(engine.Chord != null && engine.Chord.IsRecognized && engine.Chord.Symbol == "Cmaj9",
                "preview uses an actually recognized Cmaj9 held for 5.5 seconds");
            Check(engine.HistoryStrength == 0 && engine.VariationStrength > 0,
                "long-held Cmaj9 retains an extension color after history is gone");
            Console.WriteLine("engine-major9-held5s: primary=" + engine.Hex + ", relation=" + engine.RelationHex +
                ", variation=" + engine.VariationStrength.ToString("F3") + ", history=" + engine.HistoryStrength.ToString("F3"));
            return new Scene { Name = "engine-major9-held5s", Symbol = engine.Chord.Symbol, Secondary = engine.RelationHex,
                Notes = notes, Settings = new AppSettings { BackgroundOpacity = 0, GhostNotes = false, AtmosphereOpacity = 12,
                    HarmonyTint = engine.Hex, HarmonyMemoryTint = engine.MemoryHex, HarmonyRelationTint = engine.RelationHex,
                    HarmonyHistoryStrength = engine.HistoryStrength, HarmonyVariationStrength = engine.VariationStrength,
                    HarmonyAtmosphereStrength = engine.AtmosphereStrength } };
        }
        private static AppSettings Settings(string relation, string memory, double variation, double history)
        {
            return new AppSettings { BackgroundOpacity = 0, GhostNotes = false, AtmosphereOpacity = 12,
                HarmonyTint = Gold, HarmonyRelationTint = relation, HarmonyMemoryTint = memory,
                HarmonyVariationStrength = variation, HarmonyHistoryStrength = history, HarmonyAtmosphereStrength = 1 };
        }
        private static IList<ActiveNote> Notes(string hex, params int[] pitches)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            var notes = new List<ActiveNote>();
            foreach (int pitch in pitches) notes.Add(new ActiveNote { Number = pitch, Velocity = 106, IsHeld = true,
                Brightness = .8, HasTint = true, TintR = color.R, TintG = color.G, TintB = color.B });
            return notes;
        }
        private static IList<ActiveNote> Add(IList<ActiveNote> first, IList<ActiveNote> second)
        { var result = new List<ActiveNote>(first); foreach (ActiveNote note in second) result.Add(note); return result; }
        private static LayoutBoard Board(AppSettings settings)
        { var board = new LayoutBoard(new Border(), new Border(), new Border()) { DrawAtmosphere = false }; board.Apply(settings); return board; }
        private static byte[] Background(AppSettings settings)
        {
            LayoutBoard board = Board(settings);
            var composer = new ObsBackgroundComposer(Width, Height);
            composer.Prepare(settings, board.Bounds(0), board.Bounds(1), board.Bounds(2));
            var pixels = new byte[Width * Height * 4]; composer.Composite(pixels); return pixels;
        }
        private static Regions Classify(byte[] pixels, string primaryHex, string secondaryHex)
        {
            Direction primary = ParseDirection(primaryHex), secondary = ParseDirection(secondaryHex);
            var result = new Regions();
            // Ignore near-zero alpha/color quantization and classify the actual background
            // output by hue direction after removing gray. A uniform mixed-color wash
            // cannot pass by merely differing from a previous screenshot.
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int offset = (y * Width + x) * 4;
                    double r = pixels[offset + 2], g = pixels[offset + 1], b = pixels[offset];
                    double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    if (max < 8 || max - min < 4) continue;
                    result.Eligible++;
                    var direction = new Direction(r, g, b);
                    double p = direction.Dot(primary), s = direction.Dot(secondary);
                    if (p >= .8 && p > s) { result.Primary++; result.PrimaryX += x; result.PrimaryY += y; }
                    if (s >= .707106781 && s > p) { result.Secondary++; result.SecondaryX += x; result.SecondaryY += y; }
                }
            return result;
        }
        private static Direction ParseDirection(string value)
        { Color color = (Color)ColorConverter.ConvertFromString(value); return new Direction(color.R, color.G, color.B); }
        private static void CheckCorners(byte[] pixels, string label)
        {
            int[] indices = { 3, (Width - 1) * 4 + 3, (Height - 1) * Width * 4 + 3, pixels.Length - 1 };
            foreach (int index in indices) Check(pixels[index] == 0, label + " corner stays alpha zero");
        }
        private static void CheckDisableAndSilence(Scene scene)
        {
            foreach (double opacity in new[] { 0.0, 100.0 })
            {
                var settings = scene.Settings.Snapshot(); settings.Background = "#000000"; settings.BackgroundOpacity = opacity;
                settings.AtmosphereOpacity = 0; byte[] disabled = Background(settings);
                settings.AtmosphereOpacity = 12; settings.HarmonyAtmosphereStrength = 0; byte[] silent = Background(settings);
                Check(Same(disabled, silent), "silent atmosphere equals disabled atmosphere exactly");
                bool clean = true;
                for (int i = 0; i < disabled.Length; i += 4)
                    if (disabled[i] != 0 || disabled[i + 1] != 0 || disabled[i + 2] != 0 || disabled[i + 3] != (opacity == 0 ? 0 : 255)) { clean = false; break; }
                Check(clean, "disable restores the requested transparent or solid background without residual color");
            }
        }
        private static void CheckTransforms(Scene scene)
        {
            var settings = scene.Settings.Snapshot();
            settings.StaffScale = .65; settings.StaffOffsetX = -80; settings.StaffOffsetY = 90;
            settings.KeyboardScale = .55; settings.KeyboardOffsetX = 200; settings.KeyboardOffsetY = -50;
            settings.HarmonyScale = .5; settings.HarmonyOffsetX = 220; settings.HarmonyOffsetY = -45;
            byte[] original = Background(settings); Point first = Center(original);
            settings.StaffOffsetX += 80; settings.KeyboardOffsetX += 80; settings.HarmonyOffsetX += 80;
            settings.StaffOffsetY += 24; settings.KeyboardOffsetY += 24; settings.HarmonyOffsetY += 24;
            byte[] moved = Background(settings); Point second = Center(moved);
            Check(Math.Abs(second.X - first.X - 80 * ObsFrameRenderer.RenderScale) < 8 &&
                Math.Abs(second.Y - first.Y - 24 * ObsFrameRenderer.RenderScale) < 8,
                "moving controls moves their atmosphere by the same board displacement");
            CheckCorners(moved, "moved atmosphere");
            double originalArea = AlphaMass(moved);
            settings.StaffScale *= .7; settings.KeyboardScale *= .7; settings.HarmonyScale *= .7;
            byte[] smaller = Background(settings);
            Check(AlphaMass(smaller) < originalArea * .82, "scaling controls down reduces the atmosphere footprint");
            CheckCorners(smaller, "scaled atmosphere");
        }
        private static Point Center(byte[] pixels)
        {
            double mass = 0, xSum = 0, ySum = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                { double a = pixels[(y * Width + x) * 4 + 3]; mass += a; xSum += a * x; ySum += a * y; }
            return new Point(xSum / mass, ySum / mass);
        }
        private static double AlphaMass(byte[] pixels)
        { double sum = 0; for (int i = 3; i < pixels.Length; i += 4) sum += pixels[i]; return sum; }
        private static void CheckActualFrameBackground(Scene scene)
        {
            var renderer = new ObsFrameRenderer(); var settings = scene.Settings.Snapshot();
            settings.AtmosphereOpacity = 0;
            byte[] foreground = Decode(renderer.Render(settings, scene.Notes, scene.Symbol, "", "", false));
            settings.AtmosphereOpacity = 12;
            byte[] background = Background(settings);
            byte[] rendered = Decode(renderer.Render(settings, scene.Notes, scene.Symbol, "", "", false));
            int compared = 0; bool identical = true;
            for (int i = 0; i < rendered.Length; i += 4)
                if (foreground[i + 3] == 0)
                {
                    compared++;
                    for (int c = 0; c < 4; c++) if (rendered[i + c] != background[i + c]) identical = false;
                }
            Check(compared > Width * Height / 2 && identical, "full OBS composition uses the tested two-color background pixels");
            Check(Same(rendered, Decode(renderer.Render(settings, scene.Notes, scene.Symbol, "", "", false))),
                "repeated full OBS rendering keeps stable foreground and atmosphere pixels");
        }
        private static void CheckTextureSize(AppSettings settings)
        {
            LayoutBoard board = Board(settings);
            var brush = HarmonyVisuals.Atmosphere(settings, board.Bounds(0), board.Bounds(1), board.Bounds(2)) as ImageBrush;
            var image = brush == null ? null : brush.ImageSource as BitmapSource;
            Check(image != null && image.PixelWidth == 280 && image.PixelHeight == 160 && image.IsFrozen,
                "multicolor atmosphere keeps the frozen 280 by 160 texture path");
        }
        private static void SavePreviews(Scene scene)
        {
            foreach (string mode in new[] { "dark", "transparent", "black10" })
            {
                var settings = scene.Settings.Snapshot();
                settings.Background = mode == "black10" ? "#000000" : "#101B26";
                settings.BackgroundOpacity = mode == "transparent" ? 0 : 100;
                settings.AtmosphereOpacity = mode == "black10" ? 10 : 12;
                byte[] png = new ObsFrameRenderer().Render(settings, scene.Notes, scene.Symbol, "", "", false);
                File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, scene.Name + "-" + mode + ".png"), png);
            }
        }
        private static byte[] Decode(byte[] png)
        {
            using (var stream = new MemoryStream(png))
            {
                BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[Width * Height * 4]; bitmap.CopyPixels(pixels, Width * 4, 0); return pixels;
            }
        }
        private static bool Same(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static void Check(bool value, string label)
        { checks++; if (!value) failures.Add("FAILED: " + label); }
    }
}
