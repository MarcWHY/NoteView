using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NoteView
{
    // Desktop and off-screen OBS rendering share this exact coordinate system.
    public sealed class LayoutBoard : Canvas
    {
        public const double BoardWidth = 1120, BoardHeight = 640;
        private readonly FrameworkElement[] items;
        private readonly Border[] boxes = new Border[3];
        private readonly Rect[] defaults = { new Rect(340, 0, 440, 440), new Rect(0, 400, 1120, 140), new Rect(20, 545, 1080, 90) };
        private readonly Border frame;
        private readonly Rectangle atmosphere;
        private string atmosphereKey;
        private AppSettings settings;
        public event Action Changed;
        public event Action Finished;
        public LayoutBoard(FrameworkElement score, FrameworkElement keyboard, FrameworkElement harmony)
        {
            Width = BoardWidth; Height = BoardHeight; ClipToBounds = true;
            atmosphere = new Rectangle { Width = BoardWidth, Height = BoardHeight, IsHitTestVisible = false, Focusable = false };
            RenderOptions.SetBitmapScalingMode(atmosphere, BitmapScalingMode.Linear);
            Children.Add(atmosphere);
            items = new[] { score, keyboard, harmony };
            frame = new Border { Width = Width, Height = Height, BorderBrush = Brushes.MediumAquamarine, BorderThickness = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(10, 121, 230, 194)), IsHitTestVisible = false };
            Children.Add(frame);
            string[] names = { "五线谱", "键盘", "和弦" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Rect original = defaults[i];
                items[i].Width = original.Width; items[i].Height = original.Height;
                var host = new Viewbox { Child = items[i], Stretch = Stretch.Fill };
                Children.Add(host);
                items[i] = host;
                var overlay = new Grid();
                var drag = new Thumb { Cursor = Cursors.SizeAll, Opacity = 0 };
                overlay.Children.Add(drag);
                var title = new TextBlock { Text = names[i] + " · 拖动移动", Foreground = Brushes.MediumAquamarine, Background = new SolidColorBrush(Color.FromArgb(220, 16, 27, 38)), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                overlay.Children.Add(title);
                var resize = new Thumb { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = Brushes.MediumAquamarine };
                overlay.Children.Add(resize);
                boxes[i] = new Border { Child = overlay, BorderBrush = Brushes.MediumAquamarine, BorderThickness = new Thickness(1), Background = Brushes.Transparent };
                Children.Add(boxes[i]);
                SetZIndex(boxes[i], 10 + i);
                drag.DragStarted += delegate { Select(index); };
                drag.DragDelta += delegate(object sender, DragDeltaEventArgs e) { Move(index, e.HorizontalChange, e.VerticalChange); };
                drag.DragCompleted += delegate { if (Finished != null) Finished(); };
                resize.DragStarted += delegate { Select(index); };
                resize.DragDelta += delegate(object sender, DragDeltaEventArgs e)
                { Rect r = Bounds(index); Resize(index, Math.Max((r.Width + e.HorizontalChange) / r.Width, (r.Height + e.VerticalChange) / r.Height)); };
                resize.DragCompleted += delegate { if (Finished != null) Finished(); };
                boxes[i].MouseWheel += delegate(object sender, MouseWheelEventArgs e) { Select(index); Resize(index, e.Delta > 0 ? 1.05 : 1 / 1.05); if (Finished != null) Finished(); e.Handled = true; };
            }
            SetEditing(false);
        }
        private void Select(int index) { for (int i = 0; i < 3; i++) SetZIndex(boxes[i], i == index ? 20 : 10); }
        public void SetEditing(bool enabled)
        { frame.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; foreach (Border b in boxes) b.Visibility = frame.Visibility; }
        public Rect Bounds(int index)
        {
            double scale = index == 0 ? settings.StaffScale : index == 1 ? settings.KeyboardScale : settings.HarmonyScale;
            double x = index == 0 ? settings.StaffOffsetX : index == 1 ? settings.KeyboardOffsetX : settings.HarmonyOffsetX;
            double y = index == 0 ? settings.StaffOffsetY : index == 1 ? settings.KeyboardOffsetY : settings.HarmonyOffsetY;
            Rect d = defaults[index];
            scale = Safe(scale, 1); scale = Math.Max(.2, Math.Min(Math.Min(BoardWidth / d.Width, BoardHeight / d.Height), scale));
            double w = d.Width * scale, h = d.Height * scale;
            return new Rect(Math.Max(0, Math.Min(BoardWidth - w, d.X + Safe(x, 0))), Math.Max(0, Math.Min(BoardHeight - h, d.Y + Safe(y, 0))), w, h);
        }
        private static double Safe(double x, double fallback) { return double.IsNaN(x) || double.IsInfinity(x) ? fallback : x; }
        private void Store(int index, Rect r)
        {
            double scale = r.Width / defaults[index].Width, x = r.X - defaults[index].X, y = r.Y - defaults[index].Y;
            if (index == 0) { settings.StaffScale = scale; settings.StaffOffsetX = x; settings.StaffOffsetY = y; }
            else if (index == 1) { settings.KeyboardScale = scale; settings.KeyboardOffsetX = x; settings.KeyboardOffsetY = y; }
            else { settings.HarmonyScale = scale; settings.HarmonyOffsetX = x; settings.HarmonyOffsetY = y; }
        }
        public void Apply(AppSettings value)
        {
            settings = value;
            for (int i = 0; i < 3; i++)
            {
                Rect r = Bounds(i); Store(i, r);
                foreach (FrameworkElement element in new FrameworkElement[] { items[i], boxes[i] })
                {
                    SetLeft(element, r.X); SetTop(element, r.Y); element.Width = r.Width; element.Height = r.Height;
                    element.Measure(r.Size); element.Arrange(r);
                }
            }
            RefreshAtmosphere();
        }
        public void RefreshAtmosphere()
        {
            if (settings == null) return;
            Rect score = Bounds(0), keyboard = Bounds(1), harmony = Bounds(2);
            // Reuse frozen gradients while only note brightness changes (the usual steady chord).
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3:F3}|{4:F3}|{5:F3}|{6:F3}|{7}|{8}|{9}",
                settings.HarmonyTint, settings.HarmonyMemoryTint, settings.HarmonyRelationTint,
                settings.HarmonyHistoryStrength, settings.HarmonyVariationStrength,
                settings.HarmonyAtmosphereStrength, settings.AtmosphereOpacity, score, keyboard, harmony);
            if (key == atmosphereKey) return;
            atmosphereKey = key;
            atmosphere.Fill = HarmonyVisuals.Atmosphere(settings, score, keyboard, harmony);
        }
        public void Move(int index, double dx, double dy)
        { Rect r = Bounds(index); r.X = Math.Max(0, Math.Min(BoardWidth - r.Width, r.X + dx)); r.Y = Math.Max(0, Math.Min(BoardHeight - r.Height, r.Y + dy)); Store(index, r); Apply(settings); if (Changed != null) Changed(); }
        public void Resize(int index, double ratio)
        {
            Rect r = Bounds(index); Rect d = defaults[index];
            double scale = Math.Max(.2, Math.Min(Math.Min((BoardWidth - r.X) / d.Width, (BoardHeight - r.Y) / d.Height), r.Width / d.Width * ratio));
            r.Width = d.Width * scale; r.Height = d.Height * scale;
            Store(index, r); Apply(settings); if (Changed != null) Changed();
        }
    }
}
