using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NoteView
{
    public static class ObsIntegrationTests
    {
        private static int checks;
        [STAThread]
        public static int Main()
        {
            MainWindow main = null;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                main = new MainWindow(true) { ShowInTaskbar = false, ShowActivated = false, Left = -15000, Top = -15000 };
                double visualTime = 0;
                SetField(main, "harmonyColor", new HarmonyColor(delegate { return visualTime; }));
                main.Show(); Pump(60);
                main.Settings.BackgroundOpacity = 0;
                main.Settings.GhostNotes = false;
                main.ApplySettings();
                var output = new ObsOutputServer();
                output.Start(0);
                SetField(main, "obsOutput", output);
                SetField(main, "obsWorker", new ObsFrameWorker(output.PublishFrame, delegate(Exception ex) { throw new Exception("OBS worker failed", ex); }));
                Invoke(main, "PublishObsFrame", true);
                byte[] initial = WaitForFrame(output.Url, null);
                Check(initial.Length > 1000, "initial HTTP output is a rendered PNG");
                var initialImage = Decode(initial);
                Check(initialImage.PixelWidth == ObsFrameRenderer.PixelWidth && initialImage.PixelHeight == ObsFrameRenderer.PixelHeight, "1.5x OBS resolution");
                var pixels = new byte[ObsFrameRenderer.PixelWidth * ObsFrameRenderer.PixelHeight * 4]; initialImage.CopyPixels(pixels, ObsFrameRenderer.PixelWidth * 4, 0);
                Check(pixels[3] == 0 && pixels.Where((b, i) => i % 4 == 3).Any(b => b > 0), "transparent background with visible score");
                var timer = Field<DispatcherTimer>(main, "obsTimer");
                timer.Start();
                main.WindowState = WindowState.Minimized; Pump(60);
                Check(main.WindowState == WindowState.Minimized, "main window is actually minimized");
                var notes = new NoteState(delegate { return visualTime; });
                SetField(main, "notes", notes);
                notes.Process(0x90, 60, 110); notes.Process(0x90, 64, 80); notes.Process(0x90, 67, 95);
                Invoke(main, "RenderNotes", true);
                byte[] held = WaitForFrame(output.Url, initial);
                Check(!initial.SequenceEqual(held), "new MIDI notes update HTTP PNG through timer while minimized");
                File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs-minimized-held.png"), held);
                visualTime = 3;
                Invoke(main, "AnimateNotes");
                byte[] decayed = WaitForFrame(output.Url, held);
                Check(!decayed.SequenceEqual(held), "time alone updates OBS brightness while minimized");
                held = decayed;
                notes.Process(0xB0, 64, 127); notes.Process(0x80, 60, 0);
                Invoke(main, "RenderNotes", true);
                byte[] pedal = WaitForFrame(output.Url, held);
                Check(!pedal.SequenceEqual(held), "pedal changes update while minimized");
                File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs-minimized-pedal.png"), pedal);
                visualTime = 30;
                Invoke(main, "AnimateNotes");
                byte[] faded = WaitForFrame(output.Url, pedal);
                Check(notes.GetActiveNotes().Count == 3 && !Field<System.Windows.Controls.TextBlock>(main, "chordSymbol").Text.StartsWith("C", StringComparison.Ordinal),
                    "faded pedal pitch no longer contaminates harmony without discarding MIDI state");
                pedal = faded;
                main.SetKeySignature(2, false); main.SetScoreTransform(1.2, 40, -10);
                byte[] transformed = WaitForFrame(output.Url, pedal);
                Check(!pedal.SequenceEqual(transformed), "key signature and score transforms update while minimized");
                main.ResetScoreTransform(); main.SetKeySignature(0, false); notes.Clear();
                Invoke(main, "RenderNotes", true);
                byte[] cleared = WaitForFrame(output.Url, transformed);
                Check(!initial.SequenceEqual(cleared) && main.Settings.HarmonyAtmosphereStrength > 0,
                    "clearing notes preserves the atmosphere for a continuous release");
                for (int step = 0; step < 120; step++)
                { visualTime += .1; Invoke(main, "AnimateNotes"); }
                byte[] quiet = WaitForFrame(output.Url, cleared);
                Check(initial.SequenceEqual(quiet), "silence restores exact initial frame after the atmosphere fades while minimized");
                string address = output.Url;
                main.Close(); main = null;
                bool stopped = false;
                try { GetFrame(address); } catch (WebException) { stopped = true; }
                Check(stopped, "closing app stops output server");
                Console.WriteLine("ObsIntegrationTests: PASS ({0} assertions)", checks);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (main != null) main.Close(); }
        }
        private static byte[] GetFrame(string address)
        {
            var request = (HttpWebRequest)WebRequest.Create(address + "frame.png");
            request.Proxy = null; request.Timeout = 3000;
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var bytes = new MemoryStream()) { stream.CopyTo(bytes); return bytes.ToArray(); }
        }
        private static byte[] WaitForFrame(string address, byte[] previous)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                Pump(40);
                byte[] png = GetFrame(address);
                if (png.Length > 100 && (previous == null || !previous.SequenceEqual(png))) return png;
            }
            throw new Exception("No new OBS frame arrived within two seconds");
        }
        private static BitmapSource Decode(byte[] bytes)
        { using (var stream = new MemoryStream(bytes)) return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); checks++; }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static void SetField(object target, string name, object value)
        { target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value); }
        private static void Invoke(object target, string name, params object[] args)
        { target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
}
