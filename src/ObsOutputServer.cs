using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NoteView
{
    /// <summary>
    /// An IPv4-loopback-only OBS Browser Source. Frames are immutable snapshots;
    /// the listener and client workers never access WPF or the file system.
    /// </summary>
    public sealed class ObsOutputServer : IDisposable
    {
        public const int DefaultPort = 18765;
        private const int MaxClients = 8;
        private const int MaxHeaderBytes = 8192;
        private const int IoTimeout = 2000;
        private readonly object sync = new object();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private readonly string session = Guid.NewGuid().ToString("N");
        private TcpListener listener;
        private Thread acceptThread;
        private byte[] latestFrame;
        private long frameVersion;
        private long lastClientTimestamp;
        private bool hasClientTimestamp;
        private bool disposed;
        private int port;
        private string url;

        public string Url { get { lock (sync) return url; } }

        public bool HasRecentClients
        {
            get
            {
                lock (sync)
                    return !disposed && hasClientTimestamp &&
                        (Stopwatch.GetTimestamp() - lastClientTimestamp) / (double)Stopwatch.Frequency < 3.0;
            }
        }

        public void Start(int port = DefaultPort)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            lock (sync)
            {
                ThrowIfDisposed();
                if (listener != null) throw new InvalidOperationException("OBS output is already running.");
                TcpListener pending = new TcpListener(IPAddress.Loopback, port);
                // Do not share the port with another service on Windows.
                pending.ExclusiveAddressUse = true;
                try
                {
                    pending.Start(MaxClients);
                    this.port = ((IPEndPoint)pending.LocalEndpoint).Port;
                    url = "http://127.0.0.1:" + this.port.ToString(CultureInfo.InvariantCulture) + "/";
                    listener = pending;
                    acceptThread = new Thread(delegate() { AcceptClients(pending); });
                    acceptThread.IsBackground = true;
                    acceptThread.Name = "NoteView OBS listener";
                    acceptThread.Start();
                }
                catch
                {
                    pending.Stop();
                    listener = null;
                    acceptThread = null;
                    url = null;
                    throw;
                }
            }
        }

        public void PublishFrame(byte[] png)
        {
            if (png == null) throw new ArgumentNullException("png");
            if (png.Length == 0) throw new ArgumentException("A PNG frame cannot be empty.", "png");
            // The caller can reuse its byte array immediately after publishing.
            byte[] snapshot = (byte[])png.Clone();
            lock (sync)
            {
                ThrowIfDisposed();
                latestFrame = snapshot;
                frameVersion = frameVersion == long.MaxValue ? 1 : frameVersion + 1;
            }
        }

        private void AcceptClients(TcpListener source)
        {
            while (true)
            {
                TcpClient client = null;
                try
                {
                    client = source.AcceptTcpClient();
                    client.NoDelay = true;
                    client.ReceiveTimeout = IoTimeout;
                    client.SendTimeout = IoTimeout;
                    bool accepted;
                    lock (sync)
                    {
                        accepted = !disposed && clients.Count < MaxClients;
                        if (accepted) clients.Add(client);
                    }
                    if (!accepted) { CloseClient(client); continue; }
                    // Only the bounded set above can occupy workers or wait in
                    // the pool. A worker handles one request and closes the socket.
                    TcpClient queued = client;
                    if (!ThreadPool.QueueUserWorkItem(delegate { HandleClient(queued); }))
                        ReleaseClient(client);
                }
                catch (SocketException)
                {
                    if (client != null) ReleaseClient(client);
                    lock (sync) { if (disposed) return; }
                    Thread.Sleep(20);
                }
                catch (ObjectDisposedException)
                {
                    if (client != null) ReleaseClient(client);
                    return;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = IoTimeout;
                    stream.WriteTimeout = IoTimeout;
                    string headers = ReadHeaders(stream);
                    if (headers == null) return;
                    string[] lines = headers.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    string[] request = lines[0].Split(' ');
                    if (request.Length != 3 || (request[2] != "HTTP/1.1" && request[2] != "HTTP/1.0"))
                    { SendError(stream, 400, "Bad Request", false); return; }
                    bool head = request[0] == "HEAD";
                    if (request[0] != "GET" && !head)
                    { SendError(stream, 405, "Method Not Allowed", false); return; }

                    string host = null;
                    for (int index = 1; index < lines.Length; index++)
                    {
                        if (lines[index].Length == 0) continue;
                        int colon = lines[index].IndexOf(':');
                        if (colon <= 0 || char.IsWhiteSpace(lines[index][0]))
                        { SendError(stream, 400, "Bad Request", head); return; }
                        string name = lines[index].Substring(0, colon);
                        string value = lines[index].Substring(colon + 1).Trim();
                        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            if (host != null) { SendError(stream, 400, "Bad Request", head); return; }
                            host = value;
                        }
                        if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                            (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && value != "0"))
                        { SendError(stream, 400, "Bad Request", head); return; }
                    }
                    if (!ValidHost(host)) { SendError(stream, 403, "Forbidden", head); return; }

                    string target = request[1];
                    if (target == "/")
                        WriteResponse(stream, 200, "OK", "text/html; charset=utf-8", PageBytes, head, null);
                    else if (target == "/health")
                    {
                        long version;
                        bool hasFrame;
                        lock (sync) { version = frameVersion; hasFrame = latestFrame != null; }
                        byte[] body = Encoding.UTF8.GetBytes("{\"version\":" + version.ToString(CultureInfo.InvariantCulture) +
                            ",\"hasFrame\":" + (hasFrame ? "true" : "false") + "}");
                        WriteResponse(stream, 200, "OK", "application/json; charset=utf-8", body, head, null);
                    }
                    else if (target == "/frame.png" || target.StartsWith("/frame.png?", StringComparison.Ordinal))
                        ServeFrame(stream, target, head);
                    else SendError(stream, 404, "Not Found", head);
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { ReleaseClient(client); }
        }

        private static string ReadHeaders(NetworkStream stream)
        {
            byte[] bytes = new byte[MaxHeaderBytes];
            Stopwatch elapsed = Stopwatch.StartNew();
            for (int count = 0; count < bytes.Length; count++)
            {
                int remaining = IoTimeout - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0) return null;
                stream.ReadTimeout = remaining;
                int value = stream.ReadByte();
                if (value < 0) return null;
                if (value != 9 && value != 10 && value != 13 && (value < 32 || value > 126))
                { SendError(stream, 400, "Bad Request", false); return null; }
                bytes[count] = (byte)value;
                if (count >= 3 && bytes[count - 3] == 13 && bytes[count - 2] == 10 && bytes[count - 1] == 13 && value == 10)
                    return Encoding.ASCII.GetString(bytes, 0, count + 1);
            }
            SendError(stream, 431, "Request Header Fields Too Large", false);
            return null;
        }

        private bool ValidHost(string host)
        {
            if (host == null) return false;
            string suffix = ":" + port.ToString(CultureInfo.InvariantCulture);
            foreach (string name in new string[] { "127.0.0.1", "localhost", "[::1]" })
                if (host.Equals(name + suffix, StringComparison.OrdinalIgnoreCase) ||
                    (port == 80 && host.Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        private void ServeFrame(NetworkStream stream, string target, bool head)
        {
            long after = -1;
            string requestedSession = null;
            if (target.Length > "/frame.png".Length)
            {
                const string prefix = "/frame.png?after=";
                if (!target.StartsWith(prefix, StringComparison.Ordinal))
                { SendError(stream, 400, "Bad Request", head); return; }
                string versionText = target.Substring(prefix.Length);
                int separator = versionText.IndexOf('&');
                if (separator >= 0)
                {
                    string sessionText = versionText.Substring(separator);
                    versionText = versionText.Substring(0, separator);
                    if (!sessionText.StartsWith("&session=", StringComparison.Ordinal))
                    { SendError(stream, 400, "Bad Request", head); return; }
                    requestedSession = sessionText.Substring("&session=".Length);
                    if (requestedSession.Length != 0 && requestedSession.Length != 32)
                    { SendError(stream, 400, "Bad Request", head); return; }
                    foreach (char value in requestedSession)
                        if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f'))
                        { SendError(stream, 400, "Bad Request", head); return; }
                }
                if (!long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out after))
                { SendError(stream, 400, "Bad Request", head); return; }
            }
            byte[] frame;
            long version;
            lock (sync)
            {
                lastClientTimestamp = Stopwatch.GetTimestamp();
                hasClientTimestamp = true;
                frame = latestFrame;
                version = frameVersion;
            }
            string extra = "X-Frame-Version: " + version.ToString(CultureInfo.InvariantCulture) +
                "\r\nX-Frame-Session: " + session + "\r\n";
            // A version is conditional only within its server instance. A
            // browser reconnecting quickly must receive the replacement frame
            // even when a newly started instance has reached the same version.
            if (frame == null || (after == version && requestedSession == session))
                WriteResponse(stream, 204, "No Content", "image/png", null, head, extra);
            else WriteResponse(stream, 200, "OK", "image/png", frame, head, extra);
        }

        private static void SendError(NetworkStream stream, int status, string reason, bool head)
        {
            WriteResponse(stream, status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(reason), head,
                status == 405 ? "Allow: GET, HEAD\r\n" : null);
        }

        private static void WriteResponse(NetworkStream stream, int status, string reason, string type, byte[] body, bool head, string extra)
        {
            string response = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + reason + "\r\n" +
                "Content-Type: " + type + "\r\n" +
                (status == 204 ? "" : "Content-Length: " + (body == null ? 0 : body.Length).ToString(CultureInfo.InvariantCulture) + "\r\n") +
                "Cache-Control: no-store, no-cache, must-revalidate\r\nPragma: no-cache\r\n" +
                "Connection: close\r\nX-Content-Type-Options: nosniff\r\n" +
                "Content-Security-Policy: default-src 'none'; img-src 'self' blob:; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'\r\n" +
                (extra ?? "") + "\r\n";
            byte[] header = Encoding.ASCII.GetBytes(response);
            stream.Write(header, 0, header.Length);
            if (!head && body != null && body.Length > 0) stream.Write(body, 0, body.Length);
        }

        public void Dispose()
        {
            TcpListener source;
            Thread thread;
            TcpClient[] pending;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                source = listener;
                thread = acceptThread;
                pending = new TcpClient[clients.Count];
                clients.CopyTo(pending);
                latestFrame = null;
            }
            // Stop() only closes the listening socket; accepted sockets need
            // their own close to release workers blocked in reads or writes.
            if (source != null) source.Stop();
            foreach (TcpClient client in pending) CloseClient(client);
            if (thread != null && thread != Thread.CurrentThread) thread.Join(1000);
        }

        private void ReleaseClient(TcpClient client)
        {
            CloseClient(client);
            lock (sync) clients.Remove(client);
        }

        private static void CloseClient(TcpClient client)
        {
            try { client.Close(); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("ObsOutputServer");
        }

        // No audio, external assets, frame queue, or dependency on the desktop
        // window. A disconnected/restarting app leaves a transparent source.
        private static readonly byte[] PageBytes = Encoding.UTF8.GetBytes(@"<!doctype html>
<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>NoteView OBS</title><style>
html,body{width:100%;height:100%;margin:0;padding:0;overflow:hidden;background:transparent}
#frame{display:block;width:100%;height:100%;object-fit:contain;background:transparent}
</style></head><body><img id='frame' alt=''><script>
(() => {
  const frame = document.getElementById('frame');
  let version = '0', session = '', currentUrl = null, stopped = false, controller = null;
  let lastGood = performance.now(), pollTimer = null;
  function clearFrame() {
    frame.removeAttribute('src');
    if (currentUrl) URL.revokeObjectURL(currentUrl);
    currentUrl = null;
    version = '0';
    session = '';
  }
  const watchdog = setInterval(() => {
    if (performance.now() - lastGood >= 2000) clearFrame();
  }, 100);
  async function poll() {
    if (stopped) return;
    const started = performance.now();
    let retry = 33, pendingUrl = null;
    controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 2000);
    try {
      const response = await fetch('/frame.png?after=' + version + '&session=' + session, {cache:'no-store', signal:controller.signal});
      const nextVersion = response.headers.get('X-Frame-Version');
      const nextSession = response.headers.get('X-Frame-Session');
      if (!nextVersion || !/^[0-9]+$/.test(nextVersion)) throw new Error('Invalid frame version');
      if (!nextSession || !/^[0-9a-f]{32}$/.test(nextSession)) throw new Error('Invalid frame session');
      if (response.status === 204) {
        if (nextVersion === '0') clearFrame();
      } else if (response.status === 200) {
        const blob = await response.blob();
        pendingUrl = URL.createObjectURL(blob);
        const probe = new Image();
        await new Promise((resolve, reject) => {
          const onAbort = () => { cleanup(); reject(new Error('Frame timeout')); };
          const cleanup = () => { probe.onload = probe.onerror = null; controller.signal.removeEventListener('abort', onAbort); };
          probe.onload = () => { cleanup(); resolve(); };
          probe.onerror = () => { cleanup(); reject(new Error('Invalid PNG')); };
          controller.signal.addEventListener('abort', onAbort);
          if (controller.signal.aborted) { onAbort(); return; }
          probe.src = pendingUrl;
        });
        if (stopped) throw new Error('Page closed');
        const previousUrl = currentUrl;
        frame.src = pendingUrl;
        currentUrl = pendingUrl;
        pendingUrl = null;
        version = nextVersion;
        if (previousUrl) URL.revokeObjectURL(previousUrl);
      } else throw new Error('Frame unavailable');
      session = nextSession;
      lastGood = performance.now();
    } catch (error) {
      retry = 250;
      if (performance.now() - lastGood >= 2000) clearFrame();
    } finally {
      clearTimeout(timeout);
      if (pendingUrl) URL.revokeObjectURL(pendingUrl);
      controller = null;
      if (!stopped) pollTimer = setTimeout(poll, Math.max(0, retry - (performance.now() - started)));
    }
  }
  window.addEventListener('pagehide', () => {
    stopped = true;
    clearInterval(watchdog);
    clearTimeout(pollTimer);
    if (controller) controller.abort();
    clearFrame();
  });
  poll();
})();
</script></body></html>");
    }
}
