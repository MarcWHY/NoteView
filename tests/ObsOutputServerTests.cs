using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NoteView;

public static class ObsOutputServerTests
{
    private static int assertions;
    private sealed class Response
    {
        public int Status;
        public string Headers;
        public byte[] Body;
        public string Header(string name)
        {
            foreach (string line in Headers.Split(new string[] { "\r\n" }, StringSplitOptions.None))
                if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line.Substring(name.Length + 1).Trim();
            return null;
        }
    }

    public static int Main()
    {
        try
        {
            TestFramesAndProtocol();
            TestBoundedConnections();
            TestLifecycle();
            TestRestartAtSameVersion();
            TestLongPolling();
            Console.WriteLine("ObsOutputServerTests: PASS ({0} assertions)", assertions);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("ObsOutputServerTests: FAIL after {0} assertions: {1}", assertions, exception);
            return 1;
        }
    }

    private static void TestLongPolling()
    {
        using (var server = new ObsOutputServer())
        {
            int activations = 0;
            server.ClientActive += delegate { Interlocked.Increment(ref activations); };
            server.Start(0);
            Uri uri = new Uri(server.Url);
            server.PublishFrame(new byte[] { 1 });
            Response current = Request(uri, "GET", "/frame.png");
            var delays = new List<double>();
            for (int sample = 0; sample < 5; sample++)
            {
                string query = "/frame.png?after=" + current.Header("X-Frame-Version") +
                    "&session=" + current.Header("X-Frame-Session") + "&wait=1000";
                Response next = null; Exception failure = null;
                using (var completed = new ManualResetEvent(false))
                {
                    var thread = new Thread(delegate()
                    {
                        try { next = Request(uri, "GET", query); } catch (Exception ex) { failure = ex; }
                        finally { completed.Set(); }
                    }) { IsBackground = true };
                    thread.Start();
                    True(!completed.WaitOne(40), "unchanged long poll waits instead of returning a repeated frame");
                    var elapsed = Stopwatch.StartNew();
                    server.PublishFrame(new byte[] { (byte)(sample + 2) });
                    True(completed.WaitOne(1500) && thread.Join(1500) && failure == null,
                        "publishing immediately wakes the waiting browser request");
                    elapsed.Stop(); delays.Add(elapsed.Elapsed.TotalMilliseconds);
                    Equal(200, next.Status, "long poll returns newly published frame");
                    Equal((byte)(sample + 2), next.Body[0], "long poll snapshot never falls back to an earlier frame");
                    current = next;
                }
            }
            Equal(1, activations, "active long polling does not flood client-activation callbacks");
            string retained = "/frame.png?after=" + current.Header("X-Frame-Version") + "&session=" + current.Header("X-Frame-Session");
            var heartbeat = Stopwatch.StartNew();
            Equal(204, Request(uri, "GET", retained + "&wait=120").Status, "unchanged long poll ends with a heartbeat");
            True(heartbeat.ElapsedMilliseconds >= 100 && heartbeat.ElapsedMilliseconds < 1500, "long-poll waiting has a finite deadline");
            Equal(204, Request(uri, "GET", retained).Status, "legacy no-wait clients keep immediate unchanged responses");
            Equal(400, Request(uri, "GET", retained + "&wait=1001").Status, "long polling cannot request an unbounded wait");
            Equal(400, Request(uri, "GET", retained + "&wait=-1").Status, "negative waits are rejected");
            Equal(400, Request(uri, "GET", retained + "&wait=1&wait=2").Status, "duplicate wait parameters are rejected");
            using (var completed = new ManualResetEvent(false))
            {
                var thread = new Thread(delegate()
                {
                    try { Request(uri, "GET", retained + "&wait=1000"); } catch { }
                    finally { completed.Set(); }
                }) { IsBackground = true };
                thread.Start(); True(!completed.WaitOne(40), "shutdown test has a waiting long poll");
                var closing = Stopwatch.StartNew(); server.Dispose();
                True(completed.WaitOne(1000) && thread.Join(1000), "Dispose wakes long polling and closes its socket");
                True(closing.ElapsedMilliseconds < 1000, "shutdown does not wait out the long-poll deadline");
            }
            delays.Sort();
            Console.WriteLine("Long-poll publish-to-response: median={0:F3} ms, max={1:F3} ms (5 local requests; excludes PNG rendering)", delays[2], delays[4]);
        }
        using (var server = new ObsOutputServer())
        {
            server.Start(0);
            server.ClientActive += delegate { server.PublishFrame(new byte[] { 99 }); };
            Response first = Request(new Uri(server.Url), "GET", "/frame.png?after=0&session=&wait=1000");
            Equal(200, first.Status, "first client can trigger its initial frame without any submission timer");
            Equal((byte)99, first.Body[0], "client-triggered first publication is not lost before waiting begins");
        }
    }

    private static void TestFramesAndProtocol()
    {
        using (ObsOutputServer server = new ObsOutputServer())
        {
            server.Start(0);
            Uri uri = new Uri(server.Url);
            Equal("127.0.0.1", uri.Host, "Only loopback URL is advertised");
            True(uri.Port > 0, "An ephemeral port is advertised correctly");
            True(!server.HasRecentClients, "No browser client before first frame request");
            Response page = Request(uri, "GET", "/");
            Equal(200, page.Status, "Browser source page loads");
            string html = Encoding.UTF8.GetString(page.Body);
            True(html.Contains("background:transparent"), "Page is transparent");
            True(html.Contains("object-fit:contain"), "Page scales complete frames");
            True(html.Contains("AbortController") && html.Contains("URL.revokeObjectURL"), "Browser requests and object URLs are bounded");
            True(!server.HasRecentClients, "HTML route alone does not count as an active image client");
            Response empty = Request(uri, "GET", "/frame.png?after=0");
            Equal(204, empty.Status, "No frame initially");
            Equal("0", empty.Header("X-Frame-Version"), "Initial frame version");
            Equal(0, empty.Body.Length, "204 has no response body");
            True(server.HasRecentClients, "Empty polling still counts as a recent browser");

            // A valid RGBA PNG is passed through exactly, without recompression,
            // color conversion, or loss of its alpha channel.
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
            byte[] expected = (byte[])png.Clone();
            server.PublishFrame(png);
            png[0] = 0;
            Response first = Request(uri, "GET", "/frame.png?after=0");
            Equal(200, first.Status, "New frame is sent");
            Equal("image/png", first.Header("Content-Type"), "PNG content type");
            Equal("1", first.Header("X-Frame-Version"), "Version increments when published");
            Equal(32, first.Header("X-Frame-Session").Length, "Frames identify their server instance");
            Equal(expected.Length.ToString(CultureInfo.InvariantCulture), first.Header("Content-Length"), "Exact binary content length");
            EqualBytes(expected, first.Body, "Frame and alpha bytes survive a caller mutation");
            True(first.Header("Access-Control-Allow-Origin") == null, "No CORS access is enabled");
            Equal("close", first.Header("Connection"), "Each request closes its connection");
            Equal("no-store, no-cache, must-revalidate", first.Header("Cache-Control"), "Frames cannot become cached stale images");
            Response unchanged = Request(uri, "GET", "/frame.png?after=1&session=" + first.Header("X-Frame-Session"));
            Equal(204, unchanged.Status, "Unchanged frame is not resent");
            Equal("1", unchanged.Header("X-Frame-Version"), "204 retains current version");
            Equal(0, unchanged.Body.Length, "Unchanged frame has no body");
            True(unchanged.Header("Content-Length") == null, "204 omits Content-Length as required by HTTP");
            Equal(200, Request(uri, "GET", "/frame.png?after=1").Status, "Missing session cannot suppress a frame");
            Response head = Request(uri, "HEAD", "/frame.png");
            Equal(200, head.Status, "HEAD frame status");
            Equal(expected.Length.ToString(CultureInfo.InvariantCulture), head.Header("Content-Length"), "HEAD reports the representation length");
            Equal(0, head.Body.Length, "HEAD has no body");

            server.PublishFrame(expected);
            Equal("2", Request(uri, "GET", "/frame.png?after=1").Header("X-Frame-Version"), "Next published frame advances version");
            Response health = Request(uri, "GET", "/health");
            Equal("{\"version\":2,\"hasFrame\":true}", Encoding.UTF8.GetString(health.Body), "Health has no private information");
            Equal(403, Request(uri, "GET", "/", "example.com:" + uri.Port).Status, "Non-loopback Host is rejected");
            Equal(403, Request(uri, "GET", "/", "127.0.0.1.example.com:" + uri.Port).Status, "Host suffix cannot bypass loopback restriction");
            Equal(200, Request(uri, "GET", "/health", "localhost:" + uri.Port).Status, "Explicit localhost Host is accepted");
            Equal(405, Request(uri, "POST", "/frame.png").Status, "Mutation methods are rejected");
            Equal(404, Request(uri, "GET", "/../src/MainWindow.cs").Status, "No filesystem endpoint");
            Equal(404, Request(uri, "GET", "/frame.png/extra").Status, "Endpoint names match exactly");
            Equal(400, Request(uri, "GET", "/frame.png?after=bad").Status, "Malformed frame version is rejected");
            Equal(400, Request(uri, "GET", "/frame.png?after=1&extra=1").Status, "Unknown query fields are rejected");
            Equal(400, Raw(uri, "GET / HTTP/1.1\r\nHost: " + uri.Authority + "\r\nHost: localhost:" + uri.Port + "\r\n\r\n").Status, "Ambiguous duplicate Hosts are rejected");
            Equal(400, Raw(uri, "GET / HTTP/1.1\r\nHost: " + uri.Authority + "\r\nTransfer-Encoding: chunked\r\n\r\n").Status, "Request bodies are rejected");
            Equal(431, Raw(uri, "GET / HTTP/1.1\r\nHost: " + uri.Authority + "\r\nX-Long: " + new string('a', 8200) + "\r\n\r\n").Status, "Headers have a finite size limit");
            Thread.Sleep(3100);
            True(!server.HasRecentClients, "Inactive browsers expire after about three seconds");
        }
    }

    private static void TestBoundedConnections()
    {
        using (ObsOutputServer server = new ObsOutputServer())
        {
            server.Start(0);
            Uri uri = new Uri(server.Url);
            List<TcpClient> idle = new List<TcpClient>();
            try
            {
                for (int index = 0; index < 8; index++)
                {
                    TcpClient client = new TcpClient();
                    client.Connect(IPAddress.Loopback, uri.Port);
                    idle.Add(client);
                }
                Thread.Sleep(100);
                using (TcpClient excess = new TcpClient())
                {
                    excess.Connect(IPAddress.Loopback, uri.Port);
                    excess.ReceiveTimeout = 1000;
                    bool closed = false;
                    try { closed = excess.GetStream().ReadByte() == -1; }
                    catch (IOException) { closed = true; }
                    True(closed, "A ninth client cannot occupy an additional worker");
                }
            }
            finally { foreach (TcpClient client in idle) client.Close(); }
            Thread.Sleep(100);
            Equal(200, Request(uri, "GET", "/health").Status, "Service recovers when idle clients close");
            using (TcpClient incomplete = new TcpClient())
            {
                incomplete.Connect(IPAddress.Loopback, uri.Port);
                incomplete.ReceiveTimeout = 4000;
                byte[] partial = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost:");
                incomplete.GetStream().Write(partial, 0, partial.Length);
                Stopwatch elapsed = Stopwatch.StartNew();
                bool closed = false;
                try { closed = incomplete.GetStream().ReadByte() == -1; }
                catch (IOException) { closed = true; }
                True(closed && elapsed.ElapsedMilliseconds < 3500, "Incomplete headers time out within a finite deadline");
            }
        }
    }

    private static void TestLifecycle()
    {
        ObsOutputServer server = new ObsOutputServer();
        server.Start(0);
        Uri uri = new Uri(server.Url);
        bool conflict = false;
        using (ObsOutputServer conflicting = new ObsOutputServer())
        {
            try { conflicting.Start(uri.Port); }
            catch (SocketException) { conflict = true; }
            True(conflict, "Port conflicts are surfaced to the caller");
            True(conflicting.Url == null, "Failed startup does not advertise a URL");
        }
        using (TcpClient pending = new TcpClient())
        {
            pending.Connect(IPAddress.Loopback, uri.Port);
            pending.ReceiveTimeout = 1500;
            Thread.Sleep(50);
            Stopwatch elapsed = Stopwatch.StartNew();
            server.Dispose();
            True(elapsed.ElapsedMilliseconds < 1500, "Dispose closes listener and workers promptly");
            bool closed = false;
            try { closed = pending.GetStream().ReadByte() == -1; }
            catch (IOException) { closed = true; }
            True(closed, "Dispose closes already accepted sockets");
        }
        True(!server.HasRecentClients, "Disposed servers report no clients");
        server.Dispose();
        using (ObsOutputServer replacement = new ObsOutputServer())
        {
            replacement.Start(uri.Port);
            Equal(200, Request(uri, "GET", "/health").Status, "Port can be reused after disposal");
        }
        bool disposedRejected = false;
        try { server.Start(0); }
        catch (ObjectDisposedException) { disposedRejected = true; }
        True(disposedRejected, "Disposed server cannot restart");
    }

    private static void TestRestartAtSameVersion()
    {
        ObsOutputServer original = new ObsOutputServer();
        original.Start(0);
        Uri uri = new Uri(original.Url);
        original.PublishFrame(new byte[] { 137, 80, 78, 71, 1 });
        Response before = Request(uri, "GET", "/frame.png");
        string retainedQuery = "/frame.png?after=" + before.Header("X-Frame-Version") + "&session=" + before.Header("X-Frame-Session");
        original.Dispose();
        using (ObsOutputServer replacement = new ObsOutputServer())
        {
            replacement.Start(uri.Port);
            byte[] changedFrame = new byte[] { 137, 80, 78, 71, 2 };
            replacement.PublishFrame(changedFrame);
            Response after = Request(uri, "GET", retainedQuery);
            Equal("1", after.Header("X-Frame-Version"), "Replacement deliberately has the same local version");
            Equal(200, after.Status, "Quick restart sends a replacement frame despite equal versions");
            True(after.Header("X-Frame-Session") != before.Header("X-Frame-Session"), "Restart has a new session identity");
            EqualBytes(changedFrame, after.Body, "Quick restart transmits the new frame");
            string currentQuery = "/frame.png?after=1&session=" + after.Header("X-Frame-Session");
            Equal(204, Request(uri, "GET", currentQuery).Status, "Conditional requests resume within the new instance");
        }
    }

    private static Response Request(Uri uri, string method, string target, string host = null)
    {
        return Raw(uri, method + " " + target + " HTTP/1.1\r\nHost: " + (host ?? uri.Authority) + "\r\nConnection: close\r\n\r\n");
    }

    private static Response Raw(Uri uri, string request)
    {
        byte[] raw;
        using (TcpClient client = new TcpClient())
        {
            client.ReceiveTimeout = 4000;
            client.SendTimeout = 2000;
            client.Connect(IPAddress.Loopback, uri.Port);
            NetworkStream stream = client.GetStream();
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            using (MemoryStream result = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                while (true)
                {
                    int count;
                    try { count = stream.Read(buffer, 0, buffer.Length); }
                    catch (IOException) { if (result.Length == 0) throw; else break; }
                    if (count == 0) break;
                    result.Write(buffer, 0, count);
                }
                raw = result.ToArray();
            }
        }
        int boundary = -1;
        for (int index = 0; index + 3 < raw.Length; index++)
            if (raw[index] == 13 && raw[index + 1] == 10 && raw[index + 2] == 13 && raw[index + 3] == 10) { boundary = index; break; }
        if (boundary < 0) throw new Exception("No complete HTTP response was received.");
        string headers = Encoding.ASCII.GetString(raw, 0, boundary);
        byte[] body = new byte[raw.Length - boundary - 4];
        Array.Copy(raw, boundary + 4, body, 0, body.Length);
        return new Response { Status = int.Parse(headers.Split(' ')[1], CultureInfo.InvariantCulture), Headers = headers, Body = body };
    }

    private static void EqualBytes(byte[] expected, byte[] actual, string label)
    {
        Equal(expected.Length, actual.Length, label + " length");
        for (int index = 0; index < expected.Length; index++) Equal(expected[index], actual[index], label + " byte " + index);
    }

    private static void True(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!object.Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
}
