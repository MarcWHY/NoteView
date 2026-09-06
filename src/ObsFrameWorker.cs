using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace NoteView
{
    /// <summary>
    /// Keeps PNG rendering off the MIDI/UI dispatcher. At most one newest snapshot
    /// waits behind an in-flight render; obsolete pending snapshots are replaced.
    /// </summary>
    public sealed class ObsFrameWorker : IDisposable
    {
        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Action<byte[]> publish;
        private readonly Action<Exception> failed;
        private readonly Thread thread;
        private Snapshot pending;
        private volatile bool stopped;

        public ObsFrameWorker(Action<byte[]> publish, Action<Exception> failed)
        {
            if (publish == null) throw new ArgumentNullException("publish");
            this.publish = publish;
            this.failed = failed;
            thread = new Thread(Run) { IsBackground = true, Name = "NoteView OBS renderer" };
            thread.SetApartmentState(ApartmentState.STA);
            try { thread.Start(); }
            catch { wake.Dispose(); throw; }
        }

        public void Submit(AppSettings settings, IList<ActiveNote> notes, string chord,
            string description, string alternatives, bool sustain)
        {
            if (stopped) return;
            if (settings == null) throw new ArgumentNullException("settings");
            var next = new Snapshot { Settings = settings.Snapshot(), Chord = chord,
                Description = description, Alternatives = alternatives, Sustain = sustain };
            if (notes != null)
                foreach (ActiveNote note in notes)
                    if (note != null) next.Notes.Add(new ActiveNote { Number = note.Number,
                        Velocity = note.Velocity, IsHeld = note.IsHeld, Brightness = note.Brightness, StrikeId = note.StrikeId,
                        HasTint = note.HasTint, TintR = note.TintR, TintG = note.TintG, TintB = note.TintB });
            lock (sync)
            {
                if (stopped) return;
                pending = next;
                wake.Set();
            }
        }

        public void Dispose()
        {
            // Rendering or PNG encoding may currently be busy. Never wait for it
            // on the UI thread; the background thread owns and closes its event.
            lock (sync)
            {
                if (stopped) return;
                stopped = true;
                pending = null;
                wake.Set();
            }
        }

        private void Run()
        {
            try
            {
                // Every WPF object is created and used only on this STA thread.
                var renderer = new ObsFrameRenderer();
                while (true)
                {
                    Snapshot next;
                    lock (sync)
                    {
                        if (stopped) break;
                        next = pending;
                        pending = null;
                    }
                    if (next == null) { wake.WaitOne(); continue; }
                    if (stopped) break;
                    try
                    {
                        byte[] png = renderer.Render(next.Settings, next.Notes, next.Chord,
                            next.Description, next.Alternatives, next.Sustain);
                        // A callback already in progress can finish during Dispose;
                        // callers must also guard their disposed publishing target.
                        if (!stopped) publish(png);
                    }
                    catch (Exception ex) { ReportFailure(ex); }
                }
            }
            catch (Exception ex) { ReportFailure(ex); }
            finally
            {
                var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    dispatcher.InvokeShutdown();
                lock (sync)
                {
                    stopped = true;
                    pending = null;
                    wake.Dispose();
                }
            }
        }

        private void ReportFailure(Exception exception)
        {
            if (stopped || failed == null) return;
            // Error callbacks are observers. A closing UI or publishing server
            // must not cause an unhandled exception on this background thread.
            try { if (!stopped) failed(exception); }
            catch { }
        }

        private sealed class Snapshot
        {
            public AppSettings Settings;
            public readonly List<ActiveNote> Notes = new List<ActiveNote>();
            public string Chord, Description, Alternatives;
            public bool Sustain;
        }
    }
}
