using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NoteView;

// Calls the native callback with synthetic WinMM messages. These tests never
// open, start, stop, or send data to a real MIDI device.
internal static class MidiInputTests
{
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly MethodInfo NativeCallback = typeof(MidiInput).GetMethod("OnNativeMessage", PrivateStatic);
    private static int nextSession = 100000000;

    private static int Main()
    {
        try
        {
            TestPackedMessages();
            TestArrivalOrder();
            TestSubscriberFailure();
            TestDisconnectAndReentrantClose();
            TestDisposedInput();
            Console.WriteLine("PASS: MIDI decoding, channel/velocity/sustain preservation, burst order, stale/system-message filtering, subscriber recovery, disconnect and close lifecycle.");
            Console.WriteLine("No MIDI hardware input was opened by these tests.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void TestPackedMessages()
    {
        using (TestConnection connection = new TestConnection())
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            List<MidiMessageEventArgs> received = new List<MidiMessageEventArgs>();
            connection.Input.MessageReceived += delegate(object sender, MidiMessageEventArgs message)
            {
                lock (received)
                {
                    received.Add(message);
                    if (received.Count == 5) done.Set();
                }
            };
            connection.Send(0x3C3, 0x00643C90, 7); // Middle C, velocity 100.
            connection.Send(0x3C3, 0x00003C90, 8); // A valid note-off encoding.
            connection.Send(0x3C3, 0x007F40B2, 9); // Sustain, channel 3.
            connection.Send(0x3CC, 0x00003C8F, UInt32.MaxValue); // More-data, channel 16.
            connection.Send(0x3C3, 0x000000F8, 11); // MIDI clock is ignored.
            connection.Send(0x3C3, 0x00000000, 12); // Invalid status is ignored.
            connection.SendAsSession(connection.Session - 1, 0x3C3, 0x00643C90, 13);
            connection.Send(0x3C3, 0x000005C0, 14); // Program change has one data byte.
            Wait(done, "packed MIDI messages");
            lock (received)
            {
                Require(received.Count == 5, "Unexpected event count for channel/system/stale messages.");
                Require(received[0].Status == 0x90 && received[0].Data1 == 60 && received[0].Data2 == 100, "Note-on decoding.");
                Require(received[1].Status == 0x90 && received[1].Data2 == 0, "Preserve velocity-zero note-on.");
                Require(received[2].Status == 0xB2 && received[2].Data1 == 64 && received[2].Data2 == 127, "Preserve controller and channel.");
                Require(received[3].Status == 0x8F && received[3].Timestamp == 4294967295L, "MIM_MOREDATA and unsigned driver timestamp.");
                Require(received[4].Status == 0xC0 && received[4].Data1 == 5, "One-data-byte channel message.");
            }
        }
    }

    private static void TestArrivalOrder()
    {
        const int count = 2048;
        using (TestConnection connection = new TestConnection())
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            List<long> timestamps = new List<long>();
            connection.Input.MessageReceived += delegate(object sender, MidiMessageEventArgs message)
            {
                lock (timestamps)
                {
                    timestamps.Add(message.Timestamp);
                    if (timestamps.Count == count) done.Set();
                }
            };
            for (int i = 0; i < count; i++)
                connection.Send(0x3C3, (uint)((i % 2 == 0 ? 0x90 : 0x80) | ((i % 128) << 8) | (100 << 16)), (uint)i);
            Wait(done, "MIDI message burst");
            lock (timestamps)
            {
                Require(timestamps.Count == count, "MIDI burst dropped messages.");
                for (int i = 0; i < count; i++) Require(timestamps[i] == i, "MIDI burst reordered messages.");
            }
        }
    }

    private static void TestSubscriberFailure()
    {
        using (TestConnection connection = new TestConnection())
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            int received = 0;
            int lastStatus = 0;
            connection.Input.MessageReceived += delegate(object sender, MidiMessageEventArgs message)
            {
                received++;
                if (received == 1) throw new InvalidOperationException("Intentional subscriber error");
                lastStatus = message.Status;
                done.Set();
            };
            connection.Send(0x3C3, 0x00643C90, 0);
            connection.Send(0x3C3, 0x00003C80, 1);
            Wait(done, "note-off after subscriber failure");
            Require(received == 2 && lastStatus == 0x80, "A consumer failure stopped future note-offs.");
        }
    }

    private static void TestDisconnectAndReentrantClose()
    {
        using (TestConnection connection = new TestConnection())
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            bool wasClosed = false;
            connection.Input.Disconnected += delegate
            {
                wasClosed = !connection.Input.IsOpen;
                connection.Input.Close(); // A handler must be able to close safely.
                done.Set();
            };
            connection.Send(0x3C2, 0, 0);
            Wait(done, "disconnect notification");
            Require(wasClosed, "Disconnect was delivered before input state was closed.");
            connection.Send(0x3C3, 0x00643C90, 1); // Registry no longer accepts this session.
        }
        using (TestConnection connection = new TestConnection())
        using (ManualResetEvent done = new ManualResetEvent(false))
        {
            connection.Input.MessageReceived += delegate
            {
                connection.Input.Close();
                done.Set();
            };
            connection.Send(0x3C3, 0x00643C90, 0);
            Wait(done, "close from a message handler");
            Require(!connection.Input.IsOpen, "Close from message subscriber did not close input.");
        }
    }

    private static void TestDisposedInput()
    {
        MidiInput input = new MidiInput();
        input.Close();
        input.Close();
        input.Dispose();
        input.Dispose();
        try { input.Open(0); }
        catch (ObjectDisposedException) { return; }
        throw new Exception("Disposed input accepted Open.");
    }

    private static void Wait(WaitHandle handle, string operation)
    {
        if (!handle.WaitOne(5000)) throw new Exception("Timed out waiting for " + operation + ".");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private sealed class TestConnection : IDisposable
    {
        public readonly MidiInput Input = new MidiInput();
        public readonly int Session = Interlocked.Increment(ref nextSession);

        public TestConnection()
        {
            Dictionary<int, MidiInput> registry = (Dictionary<int, MidiInput>)typeof(MidiInput).GetField("Registry", PrivateStatic).GetValue(null);
            object gate = typeof(MidiInput).GetField("RegistryGate", PrivateStatic).GetValue(null);
            lock (gate) { registry.Add(Session, Input); }
            typeof(MidiInput).GetField("activeSession", PrivateInstance).SetValue(Input, Session);
            typeof(MidiInput).GetField("isOpen", PrivateInstance).SetValue(Input, true);
        }

        public void Send(uint message, uint packed, uint timestamp)
        {
            SendAsSession(Session, message, packed, timestamp);
        }

        public void SendAsSession(int session, uint message, uint packed, uint timestamp)
        {
            NativeCallback.Invoke(null, new object[] { IntPtr.Zero, message, new IntPtr(session), new UIntPtr(packed), new UIntPtr(timestamp) });
        }

        public void Dispose() { Input.Dispose(); }
    }
}
