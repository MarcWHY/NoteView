using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    /// <summary>
    /// Renders an independent, fixed-size OBS composition. Create and call this
    /// object on the same WPF dispatcher thread; no Window or screen capture is used.
    /// </summary>
    public sealed class ObsFrameRenderer
    {
        public const int Width = 1120;
        public const int Height = 640;
        public const double RenderScale = 1.5;
        public const int PixelWidth = 1680;
        public const int PixelHeight = 960;
        private readonly Border surface;
        private readonly StaffView staff = new StaffView { ScoreOnly = true };
        private readonly StaffView keyboard = new StaffView { KeyboardOnly = true };
        private readonly LayoutBoard board;
        private readonly TextBlock symbol, detail, alternate, pedal;
        private readonly Grid harmony;

        public ObsFrameRenderer()
        {
            surface = new Border { Width = Width, Height = Height, Padding = new Thickness(0),
                SnapsToDevicePixels = true, UseLayoutRounding = true };
            harmony = new Grid { Margin = new Thickness(0) };
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            symbol = Text(36);
            symbol.FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol");
            symbol.TextTrimming = TextTrimming.None;
            symbol.TextWrapping = TextWrapping.NoWrap;
            symbol.FontWeight = FontWeights.Normal;
            symbol.VerticalAlignment = VerticalAlignment.Center;
            symbol.Margin = new Thickness(0, 0, 18, 0);
            harmony.Children.Add(symbol);

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail = Text(17);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.MaxHeight = 44;
            detail.LineHeight = 22;
            alternate = Text(12);
            alternate.Margin = new Thickness(0, 5, 0, 0);
            alternate.TextWrapping = TextWrapping.Wrap;
            alternate.MaxHeight = 32;
            alternate.LineHeight = 16;
            details.Children.Add(detail); details.Children.Add(alternate);
            details.Visibility = Visibility.Collapsed; Grid.SetColumn(details, 1); harmony.Children.Add(details);

            pedal = Text(12);
            pedal.Text = "";
            pedal.HorizontalAlignment = HorizontalAlignment.Right;
            pedal.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pedal, 2); harmony.Children.Add(pedal);
            board = new LayoutBoard(staff, keyboard, harmony); surface.Child = board;
        }

        public byte[] Render(AppSettings settings, IList<ActiveNote> notes, string chordSymbol,
            string description, string alternatives, bool sustain)
        {
            surface.VerifyAccess();
            if (settings == null) throw new ArgumentNullException("settings");
            Color background = ParseColor(settings.Background, Color.FromRgb(16, 27, 38));
            double opacity = settings.BackgroundOpacity;
            if (double.IsNaN(opacity) || double.IsInfinity(opacity)) opacity = 96;
            background.A = (byte)Math.Round(Math.Max(0, Math.Min(100, opacity)) * 2.55);
            surface.Background = Solid(background);
            staff.Notes = notes ?? new List<ActiveNote>();
            staff.Flats = settings.Flats;
            staff.KeySignatureFifths = settings.KeySignatureFifths;
            staff.FullRange = settings.FullRange;
            staff.GhostNotes = settings.GhostNotes;
            staff.LightTheme = settings.DarkInk;
            staff.IntensityMode = settings.IntensityMode;
            staff.AccentColor = ParseColor(settings.HarmonyTint, Color.FromRgb(164, 188, 203));
            keyboard.Notes = staff.Notes; keyboard.Flats = staff.Flats; keyboard.FullRange = staff.FullRange;
            keyboard.LightTheme = staff.LightTheme; keyboard.IntensityMode = staff.IntensityMode;
            keyboard.AccentColor = staff.AccentColor; keyboard.Refresh();
            board.Apply(settings);

            Brush accent = Solid(staff.AccentColor);
            symbol.Foreground = accent;
            symbol.Text = MusicTheory.DisplaySymbol(chordSymbol);
            symbol.FontSize = 36;
            detail.Foreground = Solid(settings.DarkInk ? Color.FromRgb(38, 59, 72) : Color.FromRgb(220, 233, 239));
            alternate.Foreground = Solid(settings.DarkInk ? Color.FromRgb(69, 95, 112) : Color.FromRgb(154, 174, 186));
            detail.Text = description ?? "";
            alternate.Text = alternatives ?? "";
            alternate.Visibility = string.IsNullOrWhiteSpace(alternatives) ? Visibility.Collapsed : Visibility.Visible;
            pedal.Foreground = accent;
            pedal.Visibility = sustain ? Visibility.Visible : Visibility.Hidden;

            // This tree has no PresentationSource and keeps its own dimensions,
            // so minimizing or hiding the application's window cannot blank it.
            staff.Refresh();
            surface.Measure(new Size(Width, Height));
            surface.Arrange(new Rect(0, 0, Width, Height));
            surface.UpdateLayout();
            // Render the logical 1120 x 640 layout at 1.5x pixel density. This gives
            // OBS sharper edges while preserving responsive real-time animation.
            var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight,
                96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static TextBlock Text(double size)
        {
            return new TextBlock { FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = size,
                TextTrimming = TextTrimming.CharacterEllipsis };
        }
        private static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
        }
        private static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return (Color)ColorConverter.ConvertFromString(value); }
            catch (FormatException) { return fallback; }
            catch (NotSupportedException) { return fallback; }
            catch (ArgumentException) { return fallback; }
        }
    }
}
