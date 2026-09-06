using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NoteView
{
    public static class SchedulingTests
    {
        private static int checks;
        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                CheckCoalescedMidi();
                CheckFirstClientAndReconnect();
                app.Shutdown();
                Console.WriteLine("SchedulingTests: PASS ({0} assertions)", checks);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static MainWindow Window()
        {
            var window = new MainWindow(true) { ShowInTaskbar = false, ShowActivated = false, Left = -15000, Top = -15000 };
            window.Show(); PumpQueued(); window.WindowState = WindowState.Minimized; PumpQueued();
            return window;
        }

        private static void CheckCoalescedMidi()
        {
            MainWindow main = Window();
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                try
                {
                    double time = 0; int harmonyUpdates = 0;
                    var notes = new NoteState(delegate { return time; });
                    var colors = new HarmonyColor(delegate { harmonyUpdates++; return time; });
                    Set(main, "notes", notes); Set(main, "harmonyColor", colors);
                    var output = new ObsOutputServer(); output.Start(0);
                    Set(main, "obsOutput", output); Invoke(main, "ObserveObsClients", output);
                    var worker = new ObsFrameWorker(delegate(byte[] png)
                    { output.PublishFrame(png); entered.Set(); release.WaitOne(20000); }, delegate { entered.Set(); });
                    Set(main, "obsWorker", worker);
                    Invoke(main, "PublishObsFrame", true);
                    Check(entered.WaitOne(20000), "initial worker frame reaches controlled publication gate");
                    GetFrame(output.Url); PumpQueued(); harmonyUpdates = 0;
                    object oldNotes = Field<StaffView>(main, "staff").Notes;
                    long started = Stopwatch.GetTimestamp();
                    Send(main, 0x90, 60, 110); Send(main, 0x90, 64, 80); Send(main, 0x90, 67, 95);
                    Send(main, 0xB0, 64, 127); Send(main, 0x80, 60, 0); Send(main, 0x90, 60, 127);
                    Check(ReferenceEquals(oldNotes, Field<StaffView>(main, "staff").Notes) && harmonyUpdates == 0,
                        "MIDI burst updates state in order while deferring one combined visual snapshot");
                    Check(Field<bool>(main, "noteRenderQueued"), "burst has a queued immediate visual update");
                    PumpQueued();
                    double submitMilliseconds = (Field<long>(main, "lastObsSubmit") - started) * 1000.0 / Stopwatch.Frequency;
                    Check(harmonyUpdates == 1 && !Field<bool>(main, "noteRenderQueued"), "six MIDI messages cause one visual/harmony update");
                    Check(main.WindowState == WindowState.Minimized, "event-driven delivery works on an actually minimized window");
                    Check(notes.SustainDown && notes.GetActiveNotes().Count == 3 && notes.GetActiveNotes().All(n => n.IsHeld),
                        "coalescing preserves note-off pedal and retrigger ordering");
                    object pending = Field<object>(worker, "pending");
                    Check(pending != null, "new MIDI snapshot reaches OBS worker without any periodic OBS timer");
                    var snapshot = (IList)pending.GetType().GetField("Notes").GetValue(pending);
                    ActiveNote c = snapshot.Cast<ActiveNote>().Single(n => n.Number == 60);
                    Check(c.StrikeId == 4 && c.Velocity == 127 && c.Brightness == 1,
                        "latest OBS snapshot retains the retrigger and full strike brightness");
                    Check(colors.Group == "", "immediate draw does not skip the harmonic confirmation delay");
                    time = .079; Invoke(main, "AnimateNotes");
                    Check(colors.Group == "", "harmonic foundation is not confirmed before 80 ms");
                    time = .081; Invoke(main, "AnimateNotes");
                    Check(colors.Group != "", "unchanged harmonic foundation confirms after its original delay");
                    object beforeAnimation = Field<object>(worker, "pending");
                    Set(main, "animationTick", true);
                    try { time = .2; Invoke(main, "AnimateNotes"); }
                    finally { Set(main, "animationTick", false); }
                    Check(ReferenceEquals(beforeAnimation, Field<object>(worker, "pending")) &&
                        Field<DispatcherTimer>(main, "obsAnimationTimer").IsEnabled,
                        "continuous animation queues a single bounded-rate submission instead of raising the frame rate");
                    Send(main, 0x90, 60, 119); PumpQueued();
                    Check(!Field<DispatcherTimer>(main, "obsAnimationTimer").IsEnabled &&
                        !ReferenceEquals(beforeAnimation, Field<object>(worker, "pending")),
                        "a new strike immediately replaces pending animation and cancels its timer");
                    using (var staleInput = new MidiInput())
                        main.ProcessMidiMessage(staleInput, new MidiMessageEventArgs { Status = 0x90, Data1 = 72, Data2 = 127 });
                    Check(notes.GetActiveNotes().Count == 3, "callbacks from a stale MIDI device are still ignored");
                    Console.WriteLine("Processing 6 MIDI messages -> actual OBS Submit: {0:F3} ms (one cold UI burst; excludes driver callback dispatch, PNG rendering and OBS presentation)", submitMilliseconds);
                }
                finally { main.Close(); release.Set(); }
            }
        }

        private static void CheckFirstClientAndReconnect()
        {
            MainWindow main = Window();
            try
            {
                int published = 0;
                var output = new ObsOutputServer(); output.Start(0);
                Set(main, "obsOutput", output); Invoke(main, "ObserveObsClients", output);
                Set(main, "obsWorker", new ObsFrameWorker(delegate(byte[] png)
                { output.PublishFrame(png); Interlocked.Increment(ref published); }, delegate { }));
                Invoke(main, "PublishObsFrame", true);
                PumpUntil(delegate { return Volatile.Read(ref published) == 1; });
                main.SetScoreTransform(1.2, 80, 0);
                Send(main, 0x90, 62, 90); PumpQueued();
                Check(!output.HasRecentClients && published == 1 && Field<bool>(main, "obsDirty") &&
                    !Field<DispatcherTimer>(main, "obsAnimationTimer").IsEnabled,
                    "without a browser, edits retain dirty state and do not queue frame work");
                GetFrame(output.Url);
                PumpUntil(delegate { return Volatile.Read(ref published) >= 2; });
                Check(!Field<bool>(main, "obsDirty"), "the first browser activates the latest minimized-window snapshot");
                // Server protocol tests exercise the actual three-second expiry.
                // Move that clock boundary here so this UI behavior test stays short.
                Set(output, "lastClientTimestamp", Stopwatch.GetTimestamp() - 4 * Stopwatch.Frequency);
                int beforeReconnect = Volatile.Read(ref published);
                main.SetScoreTransform(1.3, -70, 10); PumpQueued();
                Check(Field<bool>(main, "obsDirty") && published == beforeReconnect, "inactive-browser edits wait without a periodic submission timer");
                GetFrame(output.Url);
                PumpUntil(delegate { return Volatile.Read(ref published) > beforeReconnect; });
                Check(!Field<bool>(main, "obsDirty"), "a returning browser event flushes pending settings without MIDI input");
                main.Close(); main = null;
                bool closed = false;
                try { GetFrame(output.Url); } catch (WebException) { closed = true; }
                Check(closed, "closing an event-driven output still closes its HTTP server");
            }
            finally { if (main != null) main.Close(); }
        }

        private static void Send(MainWindow main, int status, int data1, int data2)
        { main.ProcessMidiMessage(null, new MidiMessageEventArgs { Status = status, Data1 = data1, Data2 = data2 }); }
        private static void GetFrame(string address)
        {
            var request = (HttpWebRequest)WebRequest.Create(address + "frame.png"); request.Proxy = null; request.Timeout = 3000;
            using (var response = request.GetResponse()) using (var stream = response.GetResponseStream())
            { var buffer = new byte[4096]; while (stream.Read(buffer, 0, buffer.Length) > 0) { } }
        }
        private static void PumpQueued()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        private static void PumpUntil(Func<bool> complete)
        {
            var deadline = Stopwatch.StartNew();
            while (!complete() && deadline.ElapsedMilliseconds < 20000)
            { PumpQueued(); Thread.Sleep(2); }
            if (!complete()) throw new Exception("Timed out waiting for event-driven output");
        }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void Set(object target, string name, object value)
        { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
        private static void Invoke(object target, string name, params object[] arguments)
        { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments); }
        private static void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("FAILED: " + label); }
    }
}
