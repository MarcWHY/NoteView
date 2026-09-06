using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NoteView
{
    public sealed class MainWindow : Window
    {
        internal readonly AppSettings Settings;
        private readonly bool preview;
        private readonly NoteState notes = new NoteState();
        private readonly HarmonyColor harmonyColor = new HarmonyColor();
        private readonly NoteState demoNotes = new NoteState();
        private readonly StaffView staff = new StaffView { ScoreOnly = true };
        private readonly StaffView keyboard = new StaffView { KeyboardOnly = true };
        private LayoutBoard layoutBoard;
        private MidiInput midi;
        private Border shell, chordCard;
        private Grid header, toolbar, harmony;
        private TextBlock status, chordSymbol, chordDescription, chordAlternatives, noteList, pedal, footer, rangeLabel, brand;
        private ComboBox devices;
        private ComboBox keySelector;
        private bool updatingKeySelector;
        private static readonly int[] KeyFifths = { 0, 1, 2, 3, 4, 5, 6, 7, -1, -2, -3, -4, -5, -6, -7 };
        private Button connectButton, demoButton, overlayButton;
        private Button adjustScoreButton;
        private bool editingScore;
        private readonly DispatcherTimer renderTimer = new DispatcherTimer();
        private readonly DispatcherTimer deviceTimer = new DispatcherTimer();
        private readonly DispatcherTimer demoTimer = new DispatcherTimer();
        private readonly DispatcherTimer obsAnimationTimer = new DispatcherTimer();
        private bool noteRenderQueued, obsPublishQueued;
        private bool animationTick;
        private long lastObsSubmit;
        private ObsOutputServer obsOutput;
        private ObsFrameWorker obsWorker;
        private bool obsDirty = true;
        private string obsError = "";
        private Forms.NotifyIcon tray;
        private SettingsWindow settingsWindow;
        private bool fillingDevices, manualDisconnect, closing, demo, dirty = true, clickThrough;
        private bool hotkeyRegistered;
        private int demoIndex;
        private string deviceSignature = "", connectionText = "正在查找 MIDI 设备…";
        private long receivedCount;
        private DateTime chordChangedAt = DateTime.MinValue;
        private DateTime chordRenderedAt = DateTime.MinValue;
        private IntPtr hwnd;
        private static readonly Brush Mint = BrushOf("#79E6C2");
        internal static Brush BrushOf(string hex) { return new SolidColorBrush(ParseColor(hex, Colors.White)); }
        internal static Color ParseColor(string text, Color fallback)
        { try { return (Color)ColorConverter.ConvertFromString(text); } catch { return fallback; } }

        public MainWindow(bool previewMode)
        {
            preview = previewMode;
            Settings = preview ? new AppSettings() : AppSettings.Load();
            Title = "NoteView · 钢琴桌面五线谱";
            Width = Settings.Width; Height = Settings.Height;
            MinWidth = 880; MinHeight = 640;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            FontSize = 13;
            Foreground = BrushOf("#DCE9EF");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (!preview && !double.IsNaN(Settings.Left) && !double.IsInfinity(Settings.Left) && !double.IsNaN(Settings.Top) && !double.IsInfinity(Settings.Top)
                && Settings.Left > SystemParameters.VirtualScreenLeft - Width + 100 && Settings.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                && Settings.Top >= SystemParameters.VirtualScreenTop && Settings.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = Settings.Left; Top = Settings.Top; }
            BuildLayout();
            ApplySettings();
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                if (preview) return;
                CreateTray();
                RefreshDevices(true);
                renderTimer.Interval = TimeSpan.FromMilliseconds(25);
                // The timer advances envelopes and harmonic confirmation only.
                // New MIDI notes have their own coalesced immediate render below.
                renderTimer.Tick += delegate
                {
                    animationTick = true;
                    try { if (dirty) RenderNotes(false); else AnimateNotes(); }
                    finally { animationTick = false; }
                };
                renderTimer.Start();
                deviceTimer.Interval = TimeSpan.FromSeconds(3);
                deviceTimer.Tick += delegate { RefreshDevices(false); };
                deviceTimer.Start();
                UpdateObsOutput();
            };
            demoTimer.Interval = TimeSpan.FromMilliseconds(1500);
            demoTimer.Tick += delegate { NextDemo(); };
            obsAnimationTimer.Tick += delegate { obsAnimationTimer.Stop(); PublishObsFrame(false); };
            Closed += OnClosed;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    if (editingScore) SetScoreEditing(false);
                    else { SetClickThrough(false); Settings.Overlay = false; ApplySettings(); }
                    e.Handled = true;
                }
                if (e.Key == Key.F2) { ShowSettings(); e.Handled = true; }
            };
        }

        private void BuildLayout()
        {
            shell = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(24, 0, 24, 8) };
            Content = shell;
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = grid;

            header = new Grid { Height = 65 };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += DragWindow;
            brand = Label("◉   NOTE VIEW", 16, Mint);
            brand.FontWeight = FontWeights.SemiBold;
            brand.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(brand);
            var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            adjustScoreButton = ButtonOf("调整布局", delegate { SetScoreEditing(!editingScore); });
            windowButtons.Children.Add(adjustScoreButton);
            overlayButton = ButtonOf("纯谱面", delegate { Settings.Overlay = !Settings.Overlay; ApplySettings(); });
            windowButtons.Children.Add(overlayButton);
            windowButtons.Children.Add(ButtonOf("设置", delegate { ShowSettings(); }));
            windowButtons.Children.Add(ButtonOf("—", delegate { WindowState = WindowState.Minimized; }, 34));
            var close = ButtonOf("×", delegate { Close(); }, 34); close.ToolTip = "退出 NoteView";
            windowButtons.Children.Add(close);
            Grid.SetColumn(windowButtons, 1); header.Children.Add(windowButtons);
            grid.Children.Add(header);

            toolbar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var inputGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            inputGroup.Children.Add(Label("MIDI 输入", 12, BrushOf("#94A9B8"), new Thickness(0, 0, 12, 0)));
            devices = new ComboBox { Width = 228, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Foreground = BrushOf("#152530"), FontSize = 12 };
            devices.SelectionChanged += delegate
            {
                if (fillingDevices) return;
                manualDisconnect = false;
                ConnectSelected();
            };
            inputGroup.Children.Add(devices);
            connectButton = ButtonOf("连接", delegate
            {
                if (midi != null && midi.IsOpen) { manualDisconnect = true; Disconnect(); SetStatus("已断开 · 可重新连接"); }
                else { manualDisconnect = false; ConnectSelected(); }
            });
            inputGroup.Children.Add(connectButton);
            inputGroup.Children.Add(ButtonOf("刷新", delegate { RefreshDevices(true); }));
            toolbar.Children.Add(inputGroup);
            var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            demoButton = ButtonOf("演示", delegate { ToggleDemo(); });
            modes.Children.Add(demoButton);
            modes.Children.Add(ButtonOf("清音", delegate { notes.Clear(); demoNotes.Clear(); dirty = true; RenderNotes(true); }));
            Grid.SetColumn(modes, 1); toolbar.Children.Add(modes);
            Grid.SetRow(toolbar, 1); grid.Children.Add(toolbar);

            var musicArea = new Grid();
            musicArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            musicArea.RowDefinitions.Add(new RowDefinition());
            var scoreHeading = new Grid();
            scoreHeading.ColumnDefinitions.Add(new ColumnDefinition()); scoreHeading.ColumnDefinitions.Add(new ColumnDefinition());
            rangeLabel = Label("", 12, BrushOf("#9AAEBA"));
            rangeLabel.VerticalAlignment = VerticalAlignment.Center;
            rangeLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            rangeLabel.MaxWidth = 240;
            var scoreControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            scoreControls.Children.Add(rangeLabel);
            keySelector = new ComboBox { ItemsSource = KeyChoiceNames(), Width = 106, Height = 28, Margin = new Thickness(12, 0, 0, 0), Foreground = BrushOf("#152530"), VerticalContentAlignment = VerticalAlignment.Center };
            keySelector.SelectionChanged += delegate { if (!updatingKeySelector && keySelector.SelectedIndex >= 0) SelectKeyOption(keySelector.SelectedIndex); };
            scoreControls.Children.Add(keySelector);
            scoreHeading.Children.Add(scoreControls);
            status = Label(connectionText, 12, Mint); status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.HorizontalAlignment = HorizontalAlignment.Right; status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(status, 1); scoreHeading.Children.Add(status);
            musicArea.Children.Add(scoreHeading);
            Grid.SetRow(musicArea, 2); grid.Children.Add(musicArea);

            // Keep the score's geometry fixed as chord labels and alternatives change length.
            chordCard = new Border { Height = 90, CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 4, 20, 4) };
            harmony = new Grid();
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            harmony.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            var chordLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            chordSymbol = Label("", 30, Mint, new Thickness(0, 2, 0, 0));
            chordSymbol.FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol");
            chordSymbol.FontWeight = FontWeights.Normal;
            chordSymbol.TextWrapping = TextWrapping.NoWrap;
            chordSymbol.HorizontalAlignment = HorizontalAlignment.Left;
            chordLeft.Children.Add(chordSymbol); harmony.Children.Add(chordLeft);
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            chordDescription = Label("", 14, Foreground);
            chordDescription.TextWrapping = TextWrapping.Wrap;
            noteList = Label("", 12, BrushOf("#9BB0BC"), new Thickness(0, 5, 0, 0));
            noteList.TextTrimming = TextTrimming.CharacterEllipsis;
            chordAlternatives = Label("", 11, BrushOf("#8CA4B3"), new Thickness(0, 4, 0, 0));
            chordAlternatives.TextWrapping = TextWrapping.Wrap;
            chordAlternatives.MaxHeight = 36;
            chordAlternatives.LineHeight = 16;
            chordAlternatives.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(chordDescription); details.Children.Add(noteList); details.Children.Add(chordAlternatives);
            details.Visibility = Visibility.Collapsed; Grid.SetColumn(details, 1); harmony.Children.Add(details);
            var pedalGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            pedal = Label("○  延音踏板", 12, BrushOf("#8FA6B5")); pedalGroup.Children.Add(pedal);
            pedalGroup.Visibility = Visibility.Collapsed; Grid.SetColumn(pedalGroup, 2); harmony.Children.Add(pedalGroup);
            chordCard.Child = harmony;
            layoutBoard = new LayoutBoard(staff, keyboard, chordCard);
            layoutBoard.Changed += delegate { QueueObsFrame(); if (settingsWindow != null) { settingsWindow.RefreshScoreControls(); settingsWindow.RefreshControlTransforms(); } };
            layoutBoard.Finished += SaveSettings;
            var layoutHost = new Viewbox { Child = layoutBoard, Stretch = Stretch.Uniform };
            Grid.SetRow(layoutHost, 1); musicArea.Children.Add(layoutHost);

            var footerGrid = new Grid { Height = 26 };
            footer = Label("本地实时显示  ·  F2 设置  ·  Ctrl+Alt+N 找回窗口", 11, BrushOf("#7F97A6")); footer.VerticalAlignment = VerticalAlignment.Center;
            footerGrid.Children.Add(footer);
            var grip = new Thumb { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = 0.3 };
            var gripVisual = new FrameworkElementFactory(typeof(Border));
            gripVisual.SetValue(Border.BackgroundProperty, BrushOf("#7A9BA8"));
            gripVisual.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripVisual };
            grip.DragDelta += delegate(object sender, DragDeltaEventArgs e) { Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange); Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange); };
            footerGrid.Children.Add(grip); Grid.SetRow(footerGrid, 4); grid.Children.Add(footerGrid);
            var menu = new ContextMenu();
            AddMenu(menu, "设置", ShowSettings);
            AddMenu(menu, "切换纯谱面", delegate { Settings.Overlay = !Settings.Overlay; ApplySettings(); });
            AddMenu(menu, "清除亮音", delegate { notes.Clear(); demoNotes.Clear(); dirty = true; });
            AddMenu(menu, "退出", Close);
            ContextMenu = menu;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            DependencyObject original = e.OriginalSource as DependencyObject;
            while (original != null && original != header)
            {
                if (original is Button) return;
                original = VisualTreeHelper.GetParent(original);
            }
            if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } }
        }
        internal static TextBlock Label(string text, double size, Brush color, Thickness? margin = null)
        { return new TextBlock { Text = text, FontSize = size, Foreground = color, Margin = margin ?? new Thickness(0) }; }
        internal static Button ButtonOf(string text, Action action, double width = double.NaN)
        {
            var button = new Button { Content = text, Width = width, Height = 32, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), Background = BrushOf("#273947"), Foreground = BrushOf("#DDEAF0"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 12 };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Button.BackgroundProperty, BrushOf("#3A5362"))); template.Triggers.Add(hover);
            button.Template = template;
            button.Click += delegate { action(); };
            return button;
        }
        private static void AddMenu(ContextMenu menu, string title, Action action)
        { var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); }

        internal void ApplySettings()
        {
            Topmost = Settings.Topmost;
            var background = ParseColor(Settings.Background, Color.FromRgb(16, 27, 38));
            background.A = (byte)Math.Round(Settings.BackgroundOpacity * 2.55);
            shell.Background = new SolidColorBrush(background);
            shell.BorderBrush = Settings.BackgroundOpacity == 0 ? Brushes.Transparent : BrushOf(Settings.DarkInk ? "#8E9BA3" : "#304450");
            shell.BorderThickness = new Thickness(Settings.BackgroundOpacity == 0 ? 0 : 1);
            chordCard.Background = Brushes.Transparent;
            toolbar.Visibility = Settings.Overlay ? Visibility.Collapsed : Visibility.Visible;
            header.Height = Settings.Overlay ? 45 : 65;
            brand.Text = "◉   NOTE VIEW";
            brand.FontSize = Settings.Overlay ? 12 : 16;
            brand.Opacity = Settings.Overlay ? 0.6 : 1;
            overlayButton.Content = Settings.Overlay ? "还原面板" : "纯谱面";
            footer.Visibility = Settings.Overlay ? Visibility.Hidden : Visibility.Visible;
            staff.Flats = Settings.Flats;
            staff.KeySignatureFifths = Settings.KeySignatureFifths;
            staff.FullRange = Settings.FullRange;
            staff.GhostNotes = Settings.GhostNotes;
            staff.LightTheme = Settings.DarkInk;
            staff.IntensityMode = Settings.IntensityMode;
            staff.AccentColor = ParseColor(Settings.HarmonyTint, Color.FromRgb(164, 188, 203));
            keyboard.Flats = staff.Flats; keyboard.KeySignatureFifths = staff.KeySignatureFifths;
            keyboard.FullRange = staff.FullRange; keyboard.LightTheme = staff.LightTheme;
            keyboard.IntensityMode = staff.IntensityMode; keyboard.AccentColor = staff.AccentColor;
            layoutBoard.Apply(Settings);
            var ink = BrushOf(Settings.DarkInk ? "#263B48" : "#DCE9EF");
            var muted = BrushOf(Settings.DarkInk ? "#455F70" : "#9AAEBA");
            chordDescription.Foreground = ink; noteList.Foreground = muted; rangeLabel.Foreground = muted; chordAlternatives.Foreground = muted;
            chordSymbol.Foreground = HarmonyVisuals.Accent(Settings.HarmonyTint, Settings.HarmonyMemoryTint,
                Settings.HarmonyRelationTint, Settings.HarmonyHistoryStrength, Settings.HarmonyVariationStrength);
            brand.Foreground = Settings.DarkInk ? BrushOf("#176F5D") : Mint;
            rangeLabel.Text = "";
            updatingKeySelector = true;
            keySelector.SelectedIndex = KeyOptionIndex(Settings.KeySignatureFifths, Settings.MinorKey);
            updatingKeySelector = false;
            int keyCount = Math.Abs(Settings.KeySignatureFifths);
            keySelector.ToolTip = "默认调式 · 自动保存\n" + MusicTheory.DisplaySymbol(KeySignature.DisplayName(Settings.KeySignatureFifths, Settings.MinorKey)) + " · " +
                (keyCount == 0 ? "无升降号" : keyCount + (Settings.KeySignatureFifths > 0 ? " 个升号" : " 个降号"));
            if (settingsWindow != null) { settingsWindow.RefreshKeySelection(); settingsWindow.RefreshScoreControls(); }
            if (settingsWindow != null) settingsWindow.RefreshControlTransforms();
            UpdateScoreEditor();
            obsDirty = true;
            dirty = true;
            RenderNotes(true);
        }

        private void RefreshDevices(bool force)
        {
            if (closing) return;
            try
            {
                var available = MidiInput.GetDevices();
                string signature = string.Join("|", available.Select(x => x.Id + ":" + x.Name));
                if (!force && signature == deviceSignature) return;
                bool changed = signature != deviceSignature;
                deviceSignature = signature;
                string wanted = devices.SelectedItem is MidiDevice ? ((MidiDevice)devices.SelectedItem).Name : Settings.DeviceName;
                fillingDevices = true;
                devices.ItemsSource = available;
                var selected = available.FirstOrDefault(x => x.Name == wanted) ?? available.FirstOrDefault();
                devices.SelectedItem = selected;
                fillingDevices = false;
                if (selected == null) { Disconnect(); SetStatus("未找到 MIDI 输入 · 请检查 USB 连接"); }
                else if ((midi == null || !midi.IsOpen || changed) && !manualDisconnect) ConnectSelected();
            }
            catch (Exception ex) { fillingDevices = false; SetStatus("设备刷新失败：" + ex.Message); }
        }
        private void ConnectSelected()
        {
            var device = devices.SelectedItem as MidiDevice;
            if (device == null) { SetStatus("请先连接电钢琴，再点击刷新"); return; }
            Disconnect();
            var input = new MidiInput();
            midi = input;
            input.MessageReceived += delegate(object sender, MidiMessageEventArgs e)
            {
                if (closing || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate { ProcessMidiMessage(input, e); }));
            };
            input.Disconnected += delegate
            {
                if (closing || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate { if (midi != input || closing) return; Disconnect(); SetStatus("MIDI 连接已断开 · 正在等待设备"); deviceSignature = ""; }));
            };
            try
            {
                input.Open(device.Id);
                Settings.DeviceName = device.Name;
                connectButton.Content = "断开";
                SetStatus("●  已连接 · " + device.Name);
            }
            catch (Exception ex)
            {
                Disconnect();
                SetStatus("连接失败：" + ex.Message);
                status.ToolTip = "若其他音乐软件占用了 MIDI 端口，请关闭该软件的 MIDI 输入后重试。\n" + ex.Message;
            }
        }
        internal void ProcessMidiMessage(MidiInput input, MidiMessageEventArgs e)
        {
            if (closing || midi != input) return;
            if (Settings.Channel != 0 && (e.Status & 15) != Settings.Channel - 1) return;
            if (demo && (e.Status & 240) == 144 && e.Data2 > 0) StopDemo();
            receivedCount++;
            int kind = e.Status & 240;
            if (kind != 128 && kind != 144 && !(kind == 176 && (e.Data1 == 64 || e.Data1 == 120 || e.Data1 == 121 || e.Data1 == 123))) return;
            string before = ChordPitchSet(notes);
            notes.Process(e.Status, e.Data1, e.Data2);
            dirty = true;
            if (before != ChordPitchSet(notes)) chordChangedAt = DateTime.UtcNow;
            if (kind == 144 && e.Data2 > 0)
                footer.Text = MusicTheory.DisplaySymbol(KeySignature.Spell(e.Data1, Settings.KeySignatureFifths, Settings.Flats).Name) + "  ·  力度 " + e.Data2 + " / 127  ·  通道 " + ((e.Status & 15) + 1) + "  ·  已收到 " + receivedCount + " 条 MIDI 消息";
            if (noteRenderQueued) return;
            noteRenderQueued = true;
            // All already queued MIDI callbacks retain their order at Normal
            // priority; one Render callback then consumes the complete burst.
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                noteRenderQueued = false;
                if (!closing && dirty) RenderNotes(false);
            }));
        }
        private void Disconnect()
        {
            var previous = midi; midi = null;
            if (previous != null) previous.Dispose();
            notes.Clear(); dirty = true;
            connectButton.Content = "连接";
        }
        private void SetStatus(string text)
        { connectionText = text; if (!demo) status.Text = text; status.ToolTip = text; }

        private bool UpdateHarmonyTint(IList<ActiveNote> active)
        {
            bool changed = harmonyColor.Update(active, Settings.IncludeSustainInChord, KeySignature.UsesFlats(Settings.KeySignatureFifths, Settings.Flats));
            string display = harmonyColor.Chord != null && harmonyColor.Chord.IsRecognized ? MusicTheory.DisplaySymbol(harmonyColor.Chord.Symbol) : "";
            if (chordSymbol.Text != display) { chordSymbol.Text = display; chordSymbol.ToolTip = display; QueueObsFrame(); }
            Settings.HarmonyTint = harmonyColor.Hex;
            Settings.HarmonyMemoryTint = harmonyColor.MemoryHex;
            Settings.HarmonyRelationTint = harmonyColor.RelationHex;
            Settings.HarmonyHistoryStrength = harmonyColor.HistoryStrength;
            Settings.HarmonyVariationStrength = harmonyColor.VariationStrength;
            Settings.HarmonyAtmosphereStrength = harmonyColor.AtmosphereStrength;
            // Snapshots are new objects every frame, even when the foundation color is unchanged.
            harmonyColor.ApplyNoteTints(active);
            if (changed)
            {
                var tint = ParseColor(Settings.HarmonyTint, Color.FromRgb(164, 188, 203));
                staff.AccentColor = tint; keyboard.AccentColor = tint;
                chordSymbol.Foreground = HarmonyVisuals.Accent(Settings.HarmonyTint, Settings.HarmonyMemoryTint,
                    Settings.HarmonyRelationTint, Settings.HarmonyHistoryStrength, Settings.HarmonyVariationStrength);
                layoutBoard.RefreshAtmosphere();
                staff.Refresh(); keyboard.Refresh(); QueueObsFrame();
            }
            return changed;
        }
        private void AnimateNotes()
        {
            var active = (demo ? demoNotes : notes).GetActiveNotes();
            bool colorChanged = UpdateHarmonyTint(active);
            if (active.Count == 0) { PublishObsFrame(false); return; }
            if (ChordPitchSet(demo ? demoNotes : notes) != string.Join(",", staff.Notes.Where(n => n.IsHeld || (Settings.IncludeSustainInChord && n.Brightness > 0)).Select(n => n.Number)))
            { RenderNotes(true); return; }
            if (!colorChanged && staff.Notes != null && active.Count == staff.Notes.Count &&
                active.Select((n, i) => Math.Round(n.Brightness * 255) == Math.Round(staff.Notes[i].Brightness * 255) &&
                    n.Number == staff.Notes[i].Number && n.IsHeld == staff.Notes[i].IsHeld &&
                    n.HasTint == staff.Notes[i].HasTint && n.TintR == staff.Notes[i].TintR &&
                    n.TintG == staff.Notes[i].TintG && n.TintB == staff.Notes[i].TintB).All(equal => equal)) return;
            keyboard.Notes = active; staff.Notes = active;
            keyboard.Refresh(); staff.Refresh(); obsDirty = true;
            PublishObsFrame(false);
        }
        private void RenderNotes(bool forceChord)
        {
            obsDirty = true;
            NoteState state = demo ? demoNotes : notes;
            var active = state.GetActiveNotes();
            UpdateHarmonyTint(active);
            keyboard.Notes = active; keyboard.Refresh();
            staff.Notes = active;
            staff.Refresh();
            pedal.Text = state.SustainDown ? "●  延音踏板" : "○  延音踏板";
            pedal.Foreground = state.SustainDown ? Mint : BrushOf(Settings.DarkInk ? "#455F70" : "#8FA6B5");
            noteList.Text = active.Count == 0 ? "" : string.Join("   ", active.Select(n => MusicTheory.DisplaySymbol(KeySignature.Spell(n.Number, Settings.KeySignatureFifths, Settings.Flats).Name) + " · " + n.Velocity + (n.IsHeld ? "" : " ↝")));
            noteList.ToolTip = noteList.Text;
            if (forceChord || (DateTime.UtcNow - chordChangedAt).TotalMilliseconds >= 80 || (DateTime.UtcNow - chordRenderedAt).TotalMilliseconds >= 180)
            {
                var chord = harmonyColor.Chord ?? new ChordResult();
                chordSymbol.Text = chord.IsRecognized ? MusicTheory.DisplaySymbol(chord.Symbol) : "";
                chordSymbol.ToolTip = chordSymbol.Text;
                chordSymbol.FontSize = 30;
                chordDescription.Text = active.Count == 0 ? "" : chord.Description;
                chordAlternatives.Text = string.IsNullOrWhiteSpace(chord.Alternatives) ? "" : "也可能是 " + chord.Alternatives;
                chordAlternatives.ToolTip = chordAlternatives.Text;
                chordRenderedAt = DateTime.UtcNow;
                dirty = false;
            }
            PublishObsFrame(false);
        }
        private string ChordPitchSet(NoteState state)
        { return string.Join(",", state.GetActiveNotes().Where(n => n.IsHeld || (Settings.IncludeSustainInChord && n.Brightness > 0)).Select(n => n.Number)); }
        private void ToggleDemo()
        {
            if (demo) { StopDemo(); return; }
            demo = true; demoIndex = 0;
            demoButton.Content = "停止演示";
            status.Text = "●  演示模式 · 不发声";
            NextDemo(); demoTimer.Start();
        }
        private void StopDemo()
        { demo = false; demoTimer.Stop(); demoNotes.Clear(); demoButton.Content = "演示"; status.Text = connectionText; dirty = true; RenderNotes(true); }
        private void NextDemo()
        {
            int[][] chords = { new[] { 48, 60, 64, 67, 71 }, new[] { 45, 60, 64, 67 }, new[] { 41, 57, 60, 64 }, new[] { 43, 59, 62, 65 } };
            int[] velocities = { 48, 64, 82, 104, 120 };
            demoNotes.Clear();
            int[] chord = chords[demoIndex++ % chords.Length];
            for (int i = 0; i < chord.Length; i++) demoNotes.Process(0x90, chord[i], velocities[i]);
            dirty = true; RenderNotes(true);
        }
        public void SetPreviewChord()
        {
            demo = true; demoIndex = 0; NextDemo();
            status.Text = "●  演示预览 · C△7";
            footer.Text = "本地实时显示  ·  F2 设置  ·  Ctrl+Alt+N 找回窗口";
        }
        public void SetPreviewTransparent()
        { Settings.BackgroundOpacity = 0; Settings.Overlay = true; Settings.GhostNotes = false; ApplySettings(); }

        internal void ShowSettings()
        {
            SetClickThrough(false);
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += delegate { settingsWindow = null; SaveSettings(); };
            settingsWindow.Show();
        }
        internal void ChannelChanged()
        { notes.Clear(); dirty = true; ApplySettings(); }
        internal void SetScoreTransform(double scale, double x, double y, bool save = false)
        {
            QueueObsFrame();
            Settings.StaffScale = ClampScoreValue(scale, .2, 2, 1);
            Settings.StaffOffsetX = ClampScoreValue(x, -1120, 1120, 0);
            Settings.StaffOffsetY = ClampScoreValue(y, -640, 640, 0);
            layoutBoard.Apply(Settings);
            if (settingsWindow != null) settingsWindow.RefreshScoreControls();
            UpdateScoreEditor();
            if (save) SaveSettings();
        }
        internal void SetKeyboardTransform(double scale, double x, double y, bool save = false)
        {
            Settings.KeyboardScale = ClampScoreValue(scale, .2, 1.5, 1);
            Settings.KeyboardOffsetX = ClampScoreValue(x, -1120, 1120, 0);
            Settings.KeyboardOffsetY = ClampScoreValue(y, -640, 640, 0);
            layoutBoard.Apply(Settings); QueueObsFrame();
            if (settingsWindow != null) settingsWindow.RefreshControlTransforms();
            if (save) SaveSettings();
        }
        internal void SetHarmonyTransform(double scale, double x, double y, bool save = false)
        {
            Settings.HarmonyScale = ClampScoreValue(scale, .2, 1.5, 1);
            Settings.HarmonyOffsetX = ClampScoreValue(x, -1120, 1120, 0);
            Settings.HarmonyOffsetY = ClampScoreValue(y, -640, 640, 0);
            layoutBoard.Apply(Settings);
            QueueObsFrame();
            if (settingsWindow != null) settingsWindow.RefreshControlTransforms();
            if (save) SaveSettings();
        }
        internal void ResetControlTransforms()
        { SetKeyboardTransform(1, 0, 0); SetHarmonyTransform(1, 0, 0, true); }
        private static double ClampScoreValue(double value, double min, double max, double fallback)
        { return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value)); }
        internal void ResetScoreTransform()
        { SetScoreTransform(1, 0, 0, true); }
        internal void SetScoreEditing(bool enabled)
        {
            editingScore = enabled;
            if (enabled) SetClickThrough(false); else SaveSettings();
            layoutBoard.SetEditing(enabled);
            adjustScoreButton.Content = enabled ? "完成调整" : "调整布局";
        }
        private void UpdateScoreEditor() { if (layoutBoard != null) layoutBoard.Apply(Settings); }
        internal string ObsOutputUrl
        { get { return obsOutput == null ? "http://127.0.0.1:" + ObsOutputServer.DefaultPort + "/" : obsOutput.Url; } }
        internal string ObsOutputStatus
        {
            get
            {
                if (!string.IsNullOrEmpty(obsError)) return obsError;
                if (preview) return "预览模式 · 未启动输出服务";
                return obsOutput == null ? "OBS 输出已关闭" : "正在输出 · 最小化后保持运行";
            }
        }
        internal void SetObsOutputEnabled(bool enabled)
        {
            Settings.ObsOutputEnabled = enabled;
            UpdateObsOutput();
            SaveSettings();
        }
        private void UpdateObsOutput()
        {
            if (preview || closing) return;
            obsError = "";
            if (!Settings.ObsOutputEnabled)
            {
                StopObsOutput();
            }
            else if (obsOutput == null)
            {
                try
                {
                    var output = new ObsOutputServer();
                    ObserveObsClients(output);
                    try { output.Start(); } catch { output.Dispose(); throw; }
                    obsOutput = output;
                    obsWorker = new ObsFrameWorker(
                        delegate(byte[] png) { try { output.PublishFrame(png); } catch (ObjectDisposedException) { } },
                        delegate(Exception error)
                        {
                            if (Dispatcher.HasShutdownStarted) return;
                            Dispatcher.BeginInvoke(new Action(delegate
                            { if (!closing && obsOutput == output) StopObsOutputWithError(error); }));
                        });
                    obsDirty = true;
                    PublishObsFrame(true);
                }
                catch (Exception ex) { StopObsOutputWithError(ex); }
            }
            if (settingsWindow != null) settingsWindow.RefreshObsControls();
        }
        private void ObserveObsClients(ObsOutputServer output)
        {
            output.ClientActive += delegate
            {
                if (closing || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate
                { if (!closing && obsOutput == output) QueueObsFrame(); }));
            };
        }
        private void QueueObsFrame()
        {
            obsDirty = true;
            if (animationTick) { QueueObsAnimation(); return; }
            if (obsPublishQueued || obsOutput == null || closing || Dispatcher.HasShutdownStarted) return;
            obsPublishQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                obsPublishQueued = false;
                if (closing) return;
                // A MIDI burst must update the note/tint snapshot before a
                // simultaneous settings or client-activation request submits it.
                if (noteRenderQueued) { QueueObsFrame(); return; }
                PublishObsFrame(false);
            }));
        }
        private void PublishObsFrame(bool force)
        {
            if (obsOutput == null || obsWorker == null || (!force && (!obsDirty || !obsOutput.HasRecentClients))) return;
            if (!force && animationTick) { QueueObsAnimation(); return; }
            try
            {
                obsAnimationTimer.Stop();
                NoteState current = demo ? demoNotes : notes;
                obsWorker.Submit(Settings, staff.Notes, chordSymbol.Text,
                    chordDescription.Text, chordAlternatives.Text, current.SustainDown);
                lastObsSubmit = Stopwatch.GetTimestamp();
                obsDirty = false;
            }
            catch (Exception ex) { StopObsOutputWithError(ex); }
        }
        private void QueueObsAnimation()
        {
            if (obsAnimationTimer.IsEnabled || !obsDirty || closing || obsOutput == null ||
                obsWorker == null || !obsOutput.HasRecentClients) return;
            // Preserve the former ~30/s animation ceiling without delaying a
            // strike. Only a dirty animation starts this one-shot timer; MIDI
            // and settings publish immediately and cancel it.
            double elapsed = (Stopwatch.GetTimestamp() - lastObsSubmit) * 1000.0 / Stopwatch.Frequency;
            obsAnimationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, 33 - elapsed));
            obsAnimationTimer.Start();
        }
        private void StopObsOutputWithError(Exception ex)
        {
            StopObsOutput();
            obsError = "OBS 输出未启动：" + ex.Message + "（可点击重试）";
            if (settingsWindow != null) settingsWindow.RefreshObsControls();
        }
        private void StopObsOutput()
        {
            obsAnimationTimer.Stop();
            if (obsWorker != null) { obsWorker.Dispose(); obsWorker = null; }
            if (obsOutput != null) { obsOutput.Dispose(); obsOutput = null; }
        }
        internal static string[] KeyChoiceNames()
        {
            var names = new string[30];
            for (int i = 0; i < names.Length; i++) names[i] = MusicTheory.DisplaySymbol(KeySignature.DisplayName(KeyFifths[i % 15], i >= 15));
            return names;
        }
        internal static int KeyOptionIndex(int fifths, bool minor)
        { return Math.Max(0, Array.IndexOf(KeyFifths, fifths)) + (minor ? 15 : 0); }
        internal void SelectKeyOption(int index)
        {
            if (index < 0 || index >= 30) return;
            SetKeySignature(KeyFifths[index % 15], index >= 15);
        }
        internal void SetKeySignature(int fifths, bool minor)
        {
            if (fifths < -7 || fifths > 7) fifths = 0;
            Settings.KeySignatureFifths = fifths;
            Settings.MinorKey = minor;
            ApplySettings();
            SaveSettings();
        }
        private void SaveSettings()
        {
            if (preview) return;
            Settings.Width = Width; Settings.Height = Height;
            Settings.Left = Left; Settings.Top = Top;
            try { Settings.Save(); } catch (Exception ex) { SetStatus("设置保存失败：" + ex.Message); }
        }
        private void CreateTray()
        {
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "NoteView · 钢琴五线谱", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示 / 找回窗口", null, delegate { RestoreWindow(); });
            menu.Items.Add("设置", null, delegate { RestoreWindow(); ShowSettings(); });
            menu.Items.Add("纯谱面模式", null, delegate { Settings.Overlay = !Settings.Overlay; ApplySettings(); });
            menu.Items.Add("鼠标穿透 / 恢复操作", null, delegate { SetClickThrough(!clickThrough); });
            menu.Items.Add("退出 NoteView", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreWindow(); };
        }
        private void RestoreWindow()
        {
            SetClickThrough(false); Show(); WindowState = WindowState.Normal;
            if (Left + ActualWidth < SystemParameters.VirtualScreenLeft + 80 || Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 || Top < SystemParameters.VirtualScreenTop || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            { Left = SystemParameters.WorkArea.Left + 40; Top = SystemParameters.WorkArea.Top + 40; }
            Activate();
        }
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            if (preview) return;
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(WindowProc);
            hotkeyRegistered = RegisterHotKey(hwnd, 1, 0x0001 | 0x0002 | 0x4000, 0x4E);
            if (!hotkeyRegistered) footer.Text = "Ctrl+Alt+N 已被占用 · 可从系统托盘找回窗口";
        }
        private IntPtr WindowProc(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == 1) { RestoreWindow(); handled = true; }
            return IntPtr.Zero;
        }
        internal void SetClickThrough(bool enabled)
        {
            if (hwnd == IntPtr.Zero) return;
            if (enabled && !hotkeyRegistered)
            { MessageBox.Show(this, "找回窗口的快捷键被其他程序占用，请先释放 Ctrl+Alt+N 再使用鼠标穿透。", "NoteView"); return; }
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(enabled ? style | 0x20 : style & ~0x20L));
            clickThrough = enabled;
            if (enabled && tray != null) tray.ShowBalloonTip(4000, "已开启鼠标穿透", "按 Ctrl+Alt+N 或双击托盘图标恢复操作。", Forms.ToolTipIcon.Info);
        }
        private void OnClosed(object sender, EventArgs e)
        {
            closing = true;
            renderTimer.Stop(); deviceTimer.Stop(); demoTimer.Stop();
            StopObsOutput();
            if (settingsWindow != null) settingsWindow.Close();
            Disconnect();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (hotkeyRegistered) UnregisterHotKey(hwnd, 1);
            SaveSettings();
        }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    }
}
