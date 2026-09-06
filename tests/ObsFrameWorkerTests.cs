using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NoteView
{
    public static class ObsFrameWorkerTests
    {
        private static int checks;

        [STAThread]
        public static int Main()
        {
            try
            {
                var app = new Application();
                TestSnapshotAndNewestRequest();
                TestDisposeDuringCallback();
                TestFailureObserverAndIdleDisposal();
                app.Shutdown();
                Console.WriteLine("ObsFrameWorkerTests: PASS (" + checks + " assertions).");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void TestSnapshotAndNewestRequest()
        {
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var ready = new ManualResetEvent(false))
            {
                int callbacks = 0, failures = 0, workerId = 0;
                bool background = false;
                ApartmentState apartment = ApartmentState.Unknown;
                Dispatcher dispatcher = null;
                byte[] finalPng = null;
                var worker = new ObsFrameWorker(delegate(byte[] png)
                {
                    int index = Interlocked.Increment(ref callbacks);
                    workerId = Thread.CurrentThread.ManagedThreadId;
                    background = Thread.CurrentThread.IsBackground;
                    apartment = Thread.CurrentThread.GetApartmentState();
                    dispatcher = Dispatcher.CurrentDispatcher;
                    if (index == 1)
                    {
                        entered.Set();
                        if (!release.WaitOne(20000)) throw new TimeoutException("Test callback gate was not released.");
                    }
                    else { finalPng = png; ready.Set(); }
                }, delegate { Interlocked.Increment(ref failures); ready.Set(); });
                Thread thread = WorkerThread(worker);
                try
                {
                    worker.Submit(new AppSettings(), null, "FIRST", "", "", false);
                    Check(entered.WaitOne(20000), "first background frame reaches the publication gate");
                    Check(workerId != Thread.CurrentThread.ManagedThreadId && background && apartment == ApartmentState.STA,
                        "render publication runs on a dedicated background STA thread");

                    // Block the current callback so all submissions below compete
                    // for exactly one pending slot, independent of render speed.
                    for (int index = 0; index < 20; index++)
                        worker.Submit(new AppSettings { Background = "#CC2233" },
                            new[] { new ActiveNote { Number = 30 + index, Velocity = 1, IsHeld = true } },
                            "OBSOLETE " + index, "", "", false);
                    var settings = new AppSettings { Background = "#13579B", BackgroundOpacity = 47, HarmonyTint = "#E9B471",
                        GhostNotes = false, KeySignatureFifths = 2, StaffScale = 1.25,
                        StaffOffsetX = 64, StaffOffsetY = -12 };
                    var notes = new List<ActiveNote> { new ActiveNote { Number = 65, Velocity = 91, IsHeld = true, Brightness = .37 },
                        new ActiveNote { Number = 66, Velocity = 39, IsHeld = false } };
                    var expectedSettings = settings.Snapshot();
                    var expectedNotes = new[] { new ActiveNote { Number = 65, Velocity = 91, IsHeld = true, Brightness = .37 },
                        new ActiveNote { Number = 66, Velocity = 39, IsHeld = false } };
                    worker.Submit(settings, notes, "FINAL", "snapshot", "held + pedal", true);
                    settings.HarmonyTint = "#FF0011"; settings.Background = "#FF0000"; settings.BackgroundOpacity = 100;
                    settings.GhostNotes = true; settings.KeySignatureFifths = -7;
                    settings.StaffScale = 2; settings.StaffOffsetX = -600;
                    notes[0].Brightness = .99; notes[0].Number = 108; notes[0].Velocity = 1; notes[0].IsHeld = false;
                    notes[1].IsHeld = true; notes.Clear();
                    release.Set();
                    Check(ready.WaitOne(20000) && finalPng != null, "latest pending frame is published");
                    worker.Dispose();
                    Check(thread.Join(10000), "render worker terminates after latest frame and disposal");
                    Check(callbacks == 2 && failures == 0, "twenty obsolete pending frames are replaced by the final request");
                    byte[] expected = new ObsFrameRenderer().Render(expectedSettings, expectedNotes,
                        "FINAL", "snapshot", "held + pedal", true);
                    File.WriteAllBytes("artifacts/worker-expected.png", expected);
                    File.WriteAllBytes("artifacts/worker-actual.png", finalPng);
                    Check(SamePixels(expected, finalPng), "Submit snapshots mutable settings list and individual note objects");
                    CheckDisposed(worker, thread);
                    Check(dispatcher != null && dispatcher.HasShutdownFinished, "worker shuts down its owned WPF dispatcher");
                }
                finally { release.Set(); worker.Dispose(); thread.Join(10000); }
            }
        }

        private static void TestDisposeDuringCallback()
        {
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                int callbacks = 0, finished = 0, failures = 0;
                var worker = new ObsFrameWorker(delegate
                {
                    Interlocked.Increment(ref callbacks);
                    entered.Set();
                    if (!release.WaitOne(20000)) throw new TimeoutException("Test callback gate was not released.");
                    Interlocked.Increment(ref finished);
                }, delegate { Interlocked.Increment(ref failures); });
                Thread thread = WorkerThread(worker);
                try
                {
                    worker.Submit(new AppSettings(), null, "FIRST", "", "", false);
                    Check(entered.WaitOne(20000), "disposal test has a callback already in progress");
                    worker.Submit(new AppSettings(), null, "MUST NOT PUBLISH", "", "", false);
                    Stopwatch elapsed = Stopwatch.StartNew();
                    worker.Dispose(); elapsed.Stop();
                    Check(elapsed.ElapsedMilliseconds < 1000, "Dispose does not wait for a blocked publication callback");
                    // Disposed submissions must return before dereferencing their
                    // inputs, including the null settings used here deliberately.
                    worker.Submit(null, null, null, null, null, false);
                    worker.Dispose();
                    release.Set();
                    Check(thread.Join(10000), "blocked callback can finish and then its worker exits");
                    Check(callbacks == 1 && finished == 1 && failures == 0,
                        "only the already-entered callback finishes; pending and post-stop requests never publish");
                    CheckDisposed(worker, thread);
                }
                finally { release.Set(); worker.Dispose(); thread.Join(10000); }
            }
        }

        private static void TestFailureObserverAndIdleDisposal()
        {
            using (var failed = new ManualResetEvent(false))
            using (var recovered = new ManualResetEvent(false))
            {
                int callbacks = 0, failureCount = 0;
                var worker = new ObsFrameWorker(delegate
                {
                    if (Interlocked.Increment(ref callbacks) == 1) throw new IOException("Deliberate publishing failure.");
                    recovered.Set();
                }, delegate
                {
                    Interlocked.Increment(ref failureCount); failed.Set();
                    throw new InvalidOperationException("A failure observer must not kill the worker.");
                });
                Thread thread = WorkerThread(worker);
                try
                {
                    worker.Submit(new AppSettings(), null, "FIRST", "", "", false);
                    Check(failed.WaitOne(20000), "publication failure reaches its observer");
                    worker.Submit(new AppSettings(), null, "RECOVERED", "", "", false);
                    Check(recovered.WaitOne(20000), "worker survives an exception thrown by the failure observer");
                    worker.Dispose();
                    Check(thread.Join(10000) && callbacks == 2 && failureCount == 1, "failure recovery leaves no extra callbacks");
                    CheckDisposed(worker, thread);
                }
                finally { worker.Dispose(); thread.Join(10000); }
            }
            for (int index = 0; index < 3; index++)
            {
                var worker = new ObsFrameWorker(delegate { throw new Exception("Idle worker must never publish."); }, null);
                Thread thread = WorkerThread(worker);
                worker.Dispose();
                Check(thread.Join(10000), "an idle worker stops without a submitted render");
                CheckDisposed(worker, thread);
            }
        }

        private static Thread WorkerThread(ObsFrameWorker worker)
        { return (Thread)typeof(ObsFrameWorker).GetField("thread", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(worker); }
        private static void CheckDisposed(ObsFrameWorker worker, Thread thread)
        {
            Check(!thread.IsAlive, "disposed worker has no live background thread");
            var wake = (AutoResetEvent)typeof(ObsFrameWorker).GetField("wake", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(worker);
            bool disposed = false;
            try { wake.WaitOne(0); } catch (ObjectDisposedException) { disposed = true; }
            Check(disposed, "worker closes its native wait handle");
            worker.Submit(null, null, null, null, null, false);
        }
        private static bool SamePixels(byte[] expected, byte[] actual)
        {
            byte[] a = Decode(expected), b = Decode(actual);
            if (a.Length != b.Length) return false;
            for (int index = 0; index < a.Length; index++) if (a[index] != b[index]) return false;
            return true;
        }
        private static byte[] Decode(byte[] png)
        {
            using (var stream = new MemoryStream(png))
            {
                BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (frame.PixelWidth != ObsFrameRenderer.PixelWidth || frame.PixelHeight != ObsFrameRenderer.PixelHeight) throw new Exception("Unexpected worker frame dimensions.");
                var source = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                source.CopyPixels(pixels, frame.PixelWidth * 4, 0); return pixels;
            }
        }
        private static void Check(bool value, string message)
        { checks++; if (!value) throw new Exception("FAILED: " + message); }
    }
}
