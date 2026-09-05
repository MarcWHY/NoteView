using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NoteView
{
    public sealed class MidiDevice
    {
        public int Id;
        public string Name;
        public override string ToString() { return Name; }
    }

    public sealed class MidiMessageEventArgs : EventArgs
    {
        public int Status;
        public int Data1;
        public int Data2;
        // Milliseconds since midiInStart, supplied by the MIDI driver.
        public long Timestamp;
    }

    /// <summary>
    /// Windows MIDI input only; never sends MIDI or produces audio.
    /// Events are serialized on a ThreadPool worker. UI consumers must dispatch
    /// asynchronously to their UI thread. Dispose the input when the app closes.
    /// </summary>
    public sealed class MidiInput : IDisposable
    {
        private const uint CallbackFunction = 0x00030000;
        private const uint MidiIoStatus = 0x00000020;
        private const uint MimClose = 0x3C2;
        private const uint MimData = 0x3C3;
        private const uint MimMoreData = 0x3CC;

        // A process-wide, permanently rooted delegate prevents unmanaged calls
        // into a collected delegate. The registry also roots every open input.
        private static readonly NativeCallback Callback = OnNativeMessage;
        private static readonly object RegistryGate = new object();
        private static readonly Dictionary<int, MidiInput> Registry = new Dictionary<int, MidiInput>();
        private static int nextSession;

        private readonly object lifecycleGate = new object();
        private readonly object queueGate = new object();
        private readonly Queue<PendingMessage> pending = new Queue<PendingMessage>();
        private IntPtr handle;
        private Timer deviceTimer;
        private volatile int activeSession;
        private volatile bool isOpen;
        private bool disposed;
        private bool workerScheduled;
        private int polling;

        public event EventHandler<MidiMessageEventArgs> MessageReceived;
        public event EventHandler Disconnected;
        public bool IsOpen { get { return isOpen; } }

        public static IList<MidiDevice> GetDevices()
        {
            List<MidiDevice> devices = new List<MidiDevice>();
            uint count = midiInGetNumDevs();
            for (uint id = 0; id < count; id++)
            {
                MidiInCaps caps;
                uint result = midiInGetDevCaps(new UIntPtr(id), out caps,
                    (uint)Marshal.SizeOf(typeof(MidiInCaps)));
                // Devices can disappear while enumeration is in progress.
                if (result == 0)
                    devices.Add(new MidiDevice { Id = (int)id, Name = caps.Name });
            }
            return devices;
        }

        public void Open(int deviceId)
        {
            lock (lifecycleGate)
            {
                if (disposed) throw new ObjectDisposedException("MidiInput");
                if (deviceId < 0) throw new ArgumentOutOfRangeException("deviceId");
                CloseCore();

                MidiInCaps caps;
                Check(midiInGetDevCaps(new UIntPtr((uint)deviceId), out caps,
                    (uint)Marshal.SizeOf(typeof(MidiInCaps))), "读取 MIDI 设备失败");

                int session;
                lock (RegistryGate)
                {
                    do { session = unchecked(++nextSession); }
                    while (session == 0 || Registry.ContainsKey(session));
                    Registry.Add(session, this);
                }
                activeSession = session;
                uint result = midiInOpen(out handle, (uint)deviceId, Callback,
                    new IntPtr(session), CallbackFunction | MidiIoStatus);
                if (result != 0)
                {
                    activeSession = 0;
                    handle = IntPtr.Zero;
                    lock (RegistryGate) { Registry.Remove(session); }
                    Check(result, "连接 MIDI 设备失败");
                }

                isOpen = true;
                result = midiInStart(handle);
                if (result != 0)
                {
                    CloseCore();
                    Check(result, "启动 MIDI 输入失败");
                }

                // WinMM does not guarantee a callback for USB removal. Polling
                // device presence also releases the app's highlighted notes.
                Connection connection = new Connection { Session = session, Name = caps.Name };
                deviceTimer = new Timer(PollDevicePresence, connection, 1000, 1000);
            }
        }

        public void Close()
        {
            lock (lifecycleGate) { CloseCore(); }
        }

        public void Dispose()
        {
            lock (lifecycleGate)
            {
                if (disposed) return;
                disposed = true;
                CloseCore();
            }
            GC.SuppressFinalize(this);
        }

        private void CloseCore()
        {
            int session = activeSession;
            // Invalidate before stopping: midiInStop/reset/close may themselves
            // synchronously invoke the native callback.
            activeSession = 0;
            isOpen = false;
            Timer timer = deviceTimer;
            deviceTimer = null;
            if (timer != null) timer.Dispose();

            IntPtr oldHandle = handle;
            handle = IntPtr.Zero;
            if (oldHandle != IntPtr.Zero)
            {
                midiInStop(oldHandle);
                midiInReset(oldHandle);
                uint result = midiInClose(oldHandle);
                if (result != 0 && result != 5) // invalid handle after removal
                    Trace.WriteLine("MIDI close: " + GetError(result));
            }
            if (session != 0)
                lock (RegistryGate) { Registry.Remove(session); }
            lock (queueGate) { pending.Clear(); }
        }

        private static void OnNativeMessage(IntPtr midiHandle, uint message,
            IntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
        {
            // No WinMM calls, synchronous UI calls, or subscriber code here:
            // Windows documents that multimedia calls in MidiInProc may deadlock.
            try
            {
                if (message != MimData && message != MimMoreData && message != MimClose) return;
                int session = unchecked((int)instance.ToInt64());
                MidiInput input;
                lock (RegistryGate)
                {
                    if (!Registry.TryGetValue(session, out input)) return;
                }
                if (input.activeSession != session) return;
                if (message == MimClose)
                {
                    input.Enqueue(new PendingMessage { Session = session, IsDisconnect = true });
                    return;
                }
                uint packed = unchecked((uint)parameter1.ToUInt64());
                int status = (int)(packed & 0xFF);
                // Forward channel messages, including note-off, velocity-zero
                // note-on, pedals, all-notes-off, pitch bend, and aftertouch.
                // Clock/active-sensing/System Exclusive do not drive notation.
                if (status < 0x80 || status >= 0xF0) return;
                input.Enqueue(new PendingMessage
                {
                    Session = session,
                    Message = new MidiMessageEventArgs
                    {
                        Status = status,
                        Data1 = (int)((packed >> 8) & 0x7F),
                        Data2 = (int)((packed >> 16) & 0x7F),
                        Timestamp = unchecked((uint)parameter2.ToUInt64())
                    }
                });
            }
            catch (Exception error)
            {
                // An exception must never unwind across the unmanaged boundary.
                Trace.WriteLine("MIDI callback: " + error.Message);
            }
        }

        private void Enqueue(PendingMessage message)
        {
            lock (queueGate)
            {
                if (activeSession != message.Session) return;
                pending.Enqueue(message);
                if (workerScheduled) return;
                workerScheduled = true;
                ThreadPool.QueueUserWorkItem(DrainMessages);
            }
        }

        private void DrainMessages(object unused)
        {
            while (true)
            {
                PendingMessage next;
                lock (queueGate)
                {
                    if (pending.Count == 0)
                    {
                        workerScheduled = false;
                        return;
                    }
                    next = pending.Dequeue();
                }
                if (activeSession != next.Session) continue;
                try
                {
                    if (next.IsDisconnect)
                    {
                        lock (lifecycleGate)
                        {
                            if (activeSession != next.Session) continue;
                            CloseCore();
                        }
                        EventHandler disconnected = Disconnected;
                        if (disconnected != null) disconnected(this, EventArgs.Empty);
                    }
                    else
                    {
                        EventHandler<MidiMessageEventArgs> received = MessageReceived;
                        if (received != null) received(this, next.Message);
                    }
                }
                catch (Exception error)
                {
                    // Keep receiving later note-offs if a consumer fails once.
                    Trace.WriteLine("MIDI event subscriber: " + error.Message);
                }
            }
        }

        private void PollDevicePresence(object state)
        {
            Connection connection = (Connection)state;
            if (activeSession != connection.Session || Interlocked.Exchange(ref polling, 1) != 0) return;
            try
            {
                bool found = false;
                foreach (MidiDevice device in GetDevices())
                    if (String.Equals(device.Name, connection.Name, StringComparison.Ordinal))
                    { found = true; break; }
                connection.MissingPolls = found ? 0 : connection.MissingPolls + 1;
                if (connection.MissingPolls >= 2)
                    Enqueue(new PendingMessage { Session = connection.Session, IsDisconnect = true });
            }
            catch (Exception error) { Trace.WriteLine("MIDI device presence: " + error.Message); }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        private static void Check(uint result, string operation)
        {
            if (result == 0) return;
            string hint = result == 4
                ? "设备可能正被其他音乐软件占用。请关闭占用它的软件后重试。"
                : result == 2 || result == 6
                    ? "请检查电钢琴的 USB 连接，然后刷新设备列表。"
                    : "请重新连接电钢琴或刷新设备后重试。";
            throw new InvalidOperationException(operation + "：" + hint + " (" + GetError(result) + ")");
        }

        private static string GetError(uint result)
        {
            StringBuilder text = new StringBuilder(256);
            return midiInGetErrorText(result, text, (uint)text.Capacity) == 0
                ? text.ToString() : "WinMM " + result;
        }

        private sealed class Connection
        {
            public int Session;
            public string Name;
            public int MissingPolls;
        }

        private sealed class PendingMessage
        {
            public int Session;
            public bool IsDisconnect;
            public MidiMessageEventArgs Message;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
            public uint Support;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void NativeCallback(IntPtr handle, uint message, IntPtr instance,
            UIntPtr parameter1, UIntPtr parameter2);

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern uint midiInGetDevCaps(UIntPtr deviceId, out MidiInCaps caps, uint capsSize);
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInOpen(out IntPtr handle, uint deviceId,
            NativeCallback callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInStart(IntPtr handle);
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInStop(IntPtr handle);
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInReset(IntPtr handle);
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint midiInClose(IntPtr handle);
        [DllImport("winmm.dll", EntryPoint = "midiInGetErrorTextW", CharSet = CharSet.Unicode)]
        private static extern uint midiInGetErrorText(uint error, StringBuilder text, uint length);
    }
}
