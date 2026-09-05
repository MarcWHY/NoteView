using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace NoteView
{
    public sealed class SettingsWindow : Window
    {
        private readonly MainWindow main;
        private readonly AppSettings settings;
        private Slider opacity;
        private TextBlock opacityValue;
        private Button backgroundButton;
        private CheckBox darkInkCheckbox;
        private ComboBox keyChoice;
        private bool refreshingKeyChoice;
        private Slider scoreScale, scoreOffsetX, scoreOffsetY;
        private TextBlock scoreScaleValue, scoreOffsetXValue, scoreOffsetYValue;
        private bool refreshingScoreControls;
        private Slider keyboardScale, keyboardOffsetX, keyboardOffsetY, harmonyScale, harmonyOffsetX, harmonyOffsetY;
        private TextBlock keyboardScaleValue, keyboardOffsetXValue, keyboardOffsetYValue, harmonyScaleValue, harmonyOffsetXValue, harmonyOffsetYValue;
        private CheckBox obsEnabled;
        private TextBlock obsStatus;
        private bool refreshingObsControls;

        public SettingsWindow(MainWindow owner)
        {
            main = owner; settings = owner.Settings;
            Owner = owner; Title = "NoteView · 设置";
            Width = 480; Height = 720; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanMinimize;
            Background = MainWindow.BrushOf("#142330");
            Foreground = MainWindow.BrushOf("#DCE9EF");
            FontFamily = owner.FontFamily; FontSize = 13;
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var content = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            scroll.Content = content; Content = scroll;
            content.Children.Add(MainWindow.Label("让音乐浮在桌面上", 23, Foreground));
            content.Children.Add(MainWindow.Label("设置即时生效，关闭后自动保存", 12, MainWindow.BrushOf("#9BAEBB"), new Thickness(0, 7, 0, 20)));

            Section(content, "OBS 输出");
            obsEnabled = new CheckBox { Content = "开启 OBS 浏览器源（最小化后继续显示）", IsChecked = settings.ObsOutputEnabled,
                Foreground = Foreground, Margin = new Thickness(0, 0, 0, 9) };
            obsEnabled.Checked += delegate { if (!refreshingObsControls) main.SetObsOutputEnabled(true); };
            obsEnabled.Unchecked += delegate { if (!refreshingObsControls) main.SetObsOutputEnabled(false); };
            content.Children.Add(obsEnabled);
            var obsAddress = new TextBox { Text = main.ObsOutputUrl, IsReadOnly = true, Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13, Background = MainWindow.BrushOf("#223845"), Foreground = Foreground, BorderThickness = new Thickness(0) };
            content.Children.Add(obsAddress);
            var obsActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 6) };
            var copyObs = MainWindow.ButtonOf("复制 OBS 地址", delegate
            {
                try { Clipboard.SetText(main.ObsOutputUrl); obsStatus.Text = "地址已复制 · 在 OBS 添加浏览器源，尺寸 1120 × 640"; }
                catch (System.Runtime.InteropServices.COMException) { obsAddress.SelectAll(); obsAddress.Focus(); obsStatus.Text = "请按 Ctrl+C 复制上方地址"; }
            });
            copyObs.Margin = new Thickness(0); obsActions.Children.Add(copyObs);
            obsActions.Children.Add(MainWindow.ButtonOf("重试输出", delegate { main.SetObsOutputEnabled(true); }));
            content.Children.Add(obsActions);
            obsStatus = MainWindow.Label("", 11, MainWindow.BrushOf("#79E6C2"));
            obsStatus.TextWrapping = TextWrapping.Wrap; content.Children.Add(obsStatus);
            var obsHelp = MainWindow.Label("OBS 添加「浏览器」源，粘贴上方地址，宽 1120、高 640。\n可最小化 NoteView，退出程序会停止输出。透明背景沿用当前设置。", 11,
                MainWindow.BrushOf("#9BAEBB"), new Thickness(0, 6, 0, 8));
            obsHelp.TextWrapping = TextWrapping.Wrap; obsHelp.LineHeight = 19; content.Children.Add(obsHelp);
            RefreshObsControls();

            Section(content, "调式与调号");
            keyChoice = Choice(content, "默认调式", MainWindow.KeyChoiceNames(), MainWindow.KeyOptionIndex(settings.KeySignatureFifths, settings.MinorKey),
                delegate(int i) { if (!refreshingKeyChoice) main.SelectKeyOption(i); }, false);
            var keyHelp = MainWindow.Label("例如 D 大调：F♯、C♯由调号表示；F、C 自然音加 ♮。\n可在谱面上方快速切换，自动记住下次启动的调式。", 11, MainWindow.BrushOf("#9BAEBB"), new Thickness(0, 0, 0, 8));
            keyHelp.TextWrapping = TextWrapping.Wrap; keyHelp.LineHeight = 19; content.Children.Add(keyHelp);

            Section(content, "五线谱位置与大小");
            scoreScale = ScoreSlider(content, "谱面缩放", 20, 200, settings.StaffScale * 100, out scoreScaleValue);
            scoreOffsetX = ScoreSlider(content, "水平位置", -1120, 1120, settings.StaffOffsetX, out scoreOffsetXValue);
            scoreOffsetY = ScoreSlider(content, "垂直位置", -640, 640, settings.StaffOffsetY, out scoreOffsetYValue);
            foreach (Slider slider in new[] { scoreScale, scoreOffsetX, scoreOffsetY })
                slider.ValueChanged += delegate
                {
                    if (!refreshingScoreControls)
                        main.SetScoreTransform(scoreScale.Value / 100, scoreOffsetX.Value, scoreOffsetY.Value);
                };
            var scoreActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var directAdjust = MainWindow.ButtonOf("调整全部布局", delegate { Close(); main.SetScoreEditing(true); });
            directAdjust.Margin = new Thickness(0, 0, 0, 0); scoreActions.Children.Add(directAdjust);
            scoreActions.Children.Add(MainWindow.ButtonOf("复位位置与大小", main.ResetScoreTransform));
            content.Children.Add(scoreActions);
            var scoreHelp = MainWindow.Label("点击“调整全部布局”，在同一画布中拖动五线谱、键盘或和弦；拖右下角或滚轮缩放。", 11, MainWindow.BrushOf("#9BAEBB"), new Thickness(0, 0, 0, 8));
            scoreHelp.TextWrapping = TextWrapping.Wrap; content.Children.Add(scoreHelp);
            RefreshScoreControls();

            Section(content, "键盘控件位置与大小");
            AddTransformSliders(content, "键盘缩放", "键盘水平位置", "键盘垂直位置", .2, 1.5, -1120, 1120, -640, 640,
                settings.KeyboardScale, settings.KeyboardOffsetX, settings.KeyboardOffsetY,
                out keyboardScale, out keyboardOffsetX, out keyboardOffsetY, out keyboardScaleValue, out keyboardOffsetXValue, out keyboardOffsetYValue,
                delegate { main.SetKeyboardTransform(keyboardScale.Value / 100, keyboardOffsetX.Value, keyboardOffsetY.Value); });
            content.Children.Add(MainWindow.ButtonOf("复位键盘位置与大小", delegate { main.SetKeyboardTransform(1, 0, 0, true); }));

            Section(content, "和弦控件位置与大小");
            AddTransformSliders(content, "和弦缩放", "和弦水平位置", "和弦垂直位置", .2, 1.5, -1120, 1120, -640, 640,
                settings.HarmonyScale, settings.HarmonyOffsetX, settings.HarmonyOffsetY,
                out harmonyScale, out harmonyOffsetX, out harmonyOffsetY, out harmonyScaleValue, out harmonyOffsetXValue, out harmonyOffsetYValue,
                delegate { main.SetHarmonyTransform(harmonyScale.Value / 100, harmonyOffsetX.Value, harmonyOffsetY.Value); });
            content.Children.Add(MainWindow.ButtonOf("复位和弦位置与大小", delegate { main.SetHarmonyTransform(1, 0, 0, true); }));
            RefreshControlTransforms();

            Section(content, "背景与谱面");
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            backgroundButton = MainWindow.ButtonOf("自选背景色", PickBackground);
            colors.Children.Add(backgroundButton);
            colors.Children.Add(MainWindow.ButtonOf("深海", delegate { SetBackground("#101B26", false); }));
            colors.Children.Add(MainWindow.ButtonOf("纸白", delegate { SetBackground("#F1F4EF", true); }));
            colors.Children.Add(MainWindow.ButtonOf("全透明", delegate { opacity.Value = 0; }));
            content.Children.Add(colors);
            opacityValue = MainWindow.Label("背景不透明度", 12, Foreground);
            content.Children.Add(opacityValue);
            opacity = new Slider { Minimum = 0, Maximum = 100, Value = settings.BackgroundOpacity, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 10, 0, 15) };
            opacity.ValueChanged += delegate { settings.BackgroundOpacity = opacity.Value; UpdateOpacityText(); main.ApplySettings(); };
            content.Children.Add(opacity); UpdateOpacityText();
            darkInkCheckbox = Check(content, "深色谱线与文字（适合浅色桌面）", settings.DarkInk, delegate(bool value) { settings.DarkInk = value; });
            Check(content, "显示未弹音符的淡色位置提示", settings.GhostNotes, delegate(bool value) { settings.GhostNotes = value; });
            Check(content, "纯谱面模式（隐藏设备工具栏）", settings.Overlay, delegate(bool value) { settings.Overlay = value; });

            Section(content, "演奏显示");
            Choice(content, "显示音域", new[] { "完整钢琴 · A0–C8（88 键）", "常用音域 · C2–C6" }, settings.FullRange ? 0 : 1, delegate(int i) { settings.FullRange = i == 0; });
            content.Children.Add(MainWindow.Label("和弦决定色彩，力度决定击键亮度。\n暖金：大和弦；冷蓝：小和弦；青绿：挂留。\n珊瑚橙：属和弦；紫／玫红：减、增及变化和弦。\n同组和声保持染色，确认换和弦后柔和换色。\n按住保留击键亮度的 14%；踏板延音逐渐淡出。", 12, Foreground, new Thickness(0, 0, 0, 10)));
            Choice(content, "无调号偏好", new[] { "非调内音用升号（C 大调 / A 小调）", "非调内音用降号（C 大调 / A 小调）" }, settings.Flats ? 1 : 0, delegate(int i) { settings.Flats = i == 1; });
            string[] channels = new string[17]; channels[0] = "全部通道";
            for (int i = 1; i < channels.Length; i++) channels[i] = "通道 " + i;
            Choice(content, "MIDI 通道", channels, settings.Channel, delegate(int i) { settings.Channel = i; main.ChannelChanged(); });
            Check(content, "和弦识别包含踏板延音", settings.IncludeSustainInChord, delegate(bool value) { settings.IncludeSustainInChord = value; });
            Check(content, "窗口始终置顶", settings.Topmost, delegate(bool value) { settings.Topmost = value; });

            Section(content, "桌面操作");
            var pass = MainWindow.ButtonOf("开启鼠标穿透", delegate { Close(); main.SetClickThrough(true); });
            pass.HorizontalAlignment = HorizontalAlignment.Left; pass.Margin = new Thickness(0, 0, 0, 10); content.Children.Add(pass);
            var help = MainWindow.Label("拖动窗口顶部移动，拖动右下角调整大小。\nCtrl+Alt+N：找回窗口并恢复鼠标操作。\nF2：设置；Esc：退出纯谱面。\n也可以双击系统托盘中的 NoteView 图标。", 12, MainWindow.BrushOf("#9BAEBB"));
            help.LineHeight = 23; content.Children.Add(help);
            var done = MainWindow.ButtonOf("完成", Close); done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 18, 0, 0); content.Children.Add(done);
        }
        private void Section(Panel parent, string text)
        {
            var label = MainWindow.Label(text, 14, MainWindow.BrushOf("#79E6C2"), new Thickness(0, 12, 0, 12));
            label.FontWeight = FontWeights.SemiBold; parent.Children.Add(label);
        }
        private CheckBox Check(Panel parent, string text, bool value, Action<bool> changed)
        {
            var checkbox = new CheckBox { Content = text, IsChecked = value, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12) };
            checkbox.Checked += delegate { changed(true); main.ApplySettings(); };
            checkbox.Unchecked += delegate { changed(false); main.ApplySettings(); };
            parent.Children.Add(checkbox);
            return checkbox;
        }
        private ComboBox Choice(Panel parent, string title, string[] options, int selected, Action<int> changed, bool applySettings = true)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var label = MainWindow.Label(title, 12, Foreground); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            var combo = new ComboBox { ItemsSource = options, SelectedIndex = selected, Height = 30, Foreground = MainWindow.BrushOf("#152530"), VerticalContentAlignment = VerticalAlignment.Center };
            combo.SelectionChanged += delegate { if (combo.SelectedIndex >= 0) { changed(combo.SelectedIndex); if (applySettings) main.ApplySettings(); } };
            Grid.SetColumn(combo, 1); row.Children.Add(combo); parent.Children.Add(row);
            return combo;
        }
        internal void RefreshKeySelection()
        {
            if (keyChoice == null) return;
            refreshingKeyChoice = true;
            keyChoice.SelectedIndex = MainWindow.KeyOptionIndex(settings.KeySignatureFifths, settings.MinorKey);
            refreshingKeyChoice = false;
        }
        private Slider ScoreSlider(Panel parent, string title, double min, double max, double value, out TextBlock valueLabel)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition());
            row.Children.Add(MainWindow.Label(title, 12, Foreground));
            valueLabel = MainWindow.Label("", 12, MainWindow.BrushOf("#79E6C2"));
            valueLabel.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(valueLabel);
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 5, IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 8, 0, 0), Height = 21 };
            Grid.SetRow(slider, 1); row.Children.Add(slider); parent.Children.Add(row);
            return slider;
        }
        internal void RefreshScoreControls()
        {
            if (scoreScale == null || scoreOffsetX == null || scoreOffsetY == null) return;
            refreshingScoreControls = true;
            scoreScale.Value = settings.StaffScale * 100;
            scoreOffsetX.Value = settings.StaffOffsetX;
            scoreOffsetY.Value = settings.StaffOffsetY;
            scoreScaleValue.Text = Math.Round(settings.StaffScale * 100) + "%";
            scoreOffsetXValue.Text = Math.Round(settings.StaffOffsetX) + " px";
            scoreOffsetYValue.Text = Math.Round(settings.StaffOffsetY) + " px";
            refreshingScoreControls = false;
        }
        private void AddTransformSliders(Panel parent, string t1, string t2, string t3, double smin, double smax, double xmin, double xmax, double ymin, double ymax,
            double sv, double xv, double yv, out Slider s, out Slider x, out Slider y, out TextBlock sl, out TextBlock xl, out TextBlock yl, Action changed)
        {
            s = ScoreSlider(parent, t1, smin * 100, smax * 100, sv * 100, out sl);
            x = ScoreSlider(parent, t2, xmin, xmax, xv, out xl);
            y = ScoreSlider(parent, t3, ymin, ymax, yv, out yl);
            foreach (Slider item in new[] { s, x, y }) item.ValueChanged += delegate { if (!refreshingScoreControls) { changed(); RefreshControlTransforms(); } };
        }
        internal void RefreshControlTransforms()
        {
            if (keyboardScale == null || harmonyScale == null) return;
            refreshingScoreControls = true;
            keyboardScale.Value = settings.KeyboardScale * 100; keyboardOffsetX.Value = settings.KeyboardOffsetX; keyboardOffsetY.Value = settings.KeyboardOffsetY;
            harmonyScale.Value = settings.HarmonyScale * 100; harmonyOffsetX.Value = settings.HarmonyOffsetX; harmonyOffsetY.Value = settings.HarmonyOffsetY;
            keyboardScaleValue.Text = Math.Round(settings.KeyboardScale * 100) + "%"; keyboardOffsetXValue.Text = Math.Round(settings.KeyboardOffsetX) + " px"; keyboardOffsetYValue.Text = Math.Round(settings.KeyboardOffsetY) + " px";
            harmonyScaleValue.Text = Math.Round(settings.HarmonyScale * 100) + "%"; harmonyOffsetXValue.Text = Math.Round(settings.HarmonyOffsetX) + " px"; harmonyOffsetYValue.Text = Math.Round(settings.HarmonyOffsetY) + " px";
            refreshingScoreControls = false;
        }
        internal void RefreshObsControls()
        {
            if (obsEnabled == null || obsStatus == null) return;
            refreshingObsControls = true;
            obsEnabled.IsChecked = settings.ObsOutputEnabled;
            obsStatus.Text = main.ObsOutputStatus;
            refreshingObsControls = false;
        }
        private void UpdateOpacityText()
        { opacityValue.Text = "背景不透明度   " + (int)opacity.Value + "%" + (opacity.Value == 0 ? "  ·  桌面完全透出" : ""); }
        private void SetBackground(string color, bool darkInk)
        { settings.Background = color; settings.DarkInk = darkInk; darkInkCheckbox.IsChecked = darkInk; opacity.Value = 96; main.ApplySettings(); }
        private void PickBackground()
        {
            string result = PickColor(MainWindow.ParseColor(settings.Background, Colors.Black));
            if (result != null)
            {
                settings.Background = result;
                Color color = MainWindow.ParseColor(result, Colors.Black);
                settings.DarkInk = (color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722) > 150;
                darkInkCheckbox.IsChecked = settings.DarkInk;
                if (opacity.Value == 0) opacity.Value = 96;
                main.ApplySettings();
            }
        }
        private string PickColor(Color initial)
        {
            using (var picker = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B) })
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                using (var owner = new DialogOwner(handle))
                {
                    if (picker.ShowDialog(owner) == Forms.DialogResult.OK)
                        return string.Format("#{0:X2}{1:X2}{2:X2}", picker.Color.R, picker.Color.G, picker.Color.B);
                }
            }
            return null;
        }
        private sealed class DialogOwner : Forms.IWin32Window, IDisposable
        {
            public IntPtr Handle { get; private set; }
            public DialogOwner(IntPtr handle) { Handle = handle; }
            public void Dispose() { }
        }
    }
}
