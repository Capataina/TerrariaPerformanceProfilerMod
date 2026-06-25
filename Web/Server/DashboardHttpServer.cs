#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PerformanceProfiler.Web.Server;

/// <summary>
/// The mod's production HTTP/1.1 server, built on raw
/// <see cref="TcpListener"/>. Serves the dashboard SPA and the JSON API
/// to a loopback browser client. Loaded from <c>Mod.Load</c>, disposed
/// from <c>Mod.Unload</c>; lives the whole mod lifetime.
///
/// <para>
/// <b>Why hand-rolled instead of <see cref="HttpListener"/>.</b> Windows'
/// <c>HttpListener</c> goes through the <c>http.sys</c> kernel driver,
/// which refuses to bind for non-admin users unless a URL ACL has been
/// configured via <c>netsh http add urlacl</c>. That breaks the
/// "load → F9 → browser" seamless contract — Workshop players will not
/// run admin commands to use a Terraria mod. <see cref="TcpListener"/>
/// is a raw socket on the userspace TCP/IP stack; no admin needed on
/// any port at-or-above 1024 on any platform we target.
/// </para>
///
/// <para>
/// <b>Scope.</b> Loopback-only (127.0.0.1), GET-only, plain HTTP/1.1,
/// connection-close per response, single-threaded accept loop with a
/// thread-per-request fan-out. Sufficient for the dashboard polling
/// case (a handful of <c>fetch</c> calls per second from one client)
/// and explicitly NOT a general-purpose server. If the design ever
/// grows (concurrent dashboards, big payloads, websockets) we revisit.
/// </para>
///
/// <para>
/// <b>Lifecycle.</b> Constructed in <c>Mod.Load</c>, disposed in
/// <c>Mod.Unload</c>. Binds to the first free port in
/// <see cref="PortRangeStart"/>..<see cref="PortRangeEnd"/>. The chosen
/// URL is exposed via <see cref="Url"/> for the keybind / chat command
/// to read.
/// </para>
///
/// <para>
/// Renamed from <c>TinyHttpServer</c> on 2026-05-21 — the original name
/// suggested a throwaway test artifact, but this is the only server we
/// ship. The "tiny" framing referred to the ~250 LOC implementation,
/// not the role in the system.
/// </para>
/// </summary>
public sealed class DashboardHttpServer : IDisposable
{
    /// <summary>First port to try. Hunts upward until one binds free.</summary>
    /// <remarks>
    /// Deliberately NOT 7777 — Terraria's multiplayer host (Host &amp; Play,
    /// dedicated server) binds 7777 by default and would collide. 27277
    /// is well outside Terraria's range, easy to remember, and unlikely
    /// to clash with anything else a typical player has running.
    /// </remarks>
    public const int PortRangeStart = 27277;
    /// <summary>Inclusive upper bound of the bind-port search.</summary>
    public const int PortRangeEnd   = 27287;

    private readonly TcpListener _listener;
    private readonly Thread _acceptThread;
    private readonly Action<string, Exception?> _log;
    private readonly Func<HttpRequest, HttpResponse> _route;

    private volatile bool _stopping;

    /// <summary>The full URL the dashboard lives at. Loopback-only.</summary>
    public string Url { get; }

    /// <summary>The bound TCP port; useful for diagnostics.</summary>
    public int Port { get; }

    public DashboardHttpServer(Func<HttpRequest, HttpResponse> route,
                               Action<string, Exception?>? log = null)
    {
        _route = route;
        _log = log ?? ((_, _) => { });

        (int port, TcpListener listener) = BindFirstFreePort();
        Port = port;
        _listener = listener;
        Url = $"http://127.0.0.1:{port}/";

        _acceptThread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "PerfProfiler/HTTP",
        };
        _acceptThread.Start();

        _log($"DashboardHttpServer listening at {Url}", null);
    }

    /// <summary>
    /// Walks <see cref="PortRangeStart"/>..<see cref="PortRangeEnd"/> and
    /// returns the first <see cref="TcpListener"/> we can bind on
    /// <see cref="IPAddress.Loopback"/>. Throws if every port in the range
    /// is occupied — at that point another process (or another tML
    /// session?) is already using all of them and the player needs to
    /// resolve it manually.
    /// </summary>
    private static (int, TcpListener) BindFirstFreePort()
    {
        for (int port = PortRangeStart; port <= PortRangeEnd; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return (port, listener);
            }
            catch (SocketException)
            {
                // Port busy — try next.
            }
        }
        throw new InvalidOperationException(
            $"No free TCP port in range {PortRangeStart}..{PortRangeEnd}; " +
            "another process is using all of them.");
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (ObjectDisposedException)
            {
                // Listener disposed during Stop(); clean exit.
                return;
            }
            catch (SocketException) when (_stopping)
            {
                return;
            }
            catch (Exception ex)
            {
                _log("DashboardHttpServer accept loop hit non-stop exception", ex);
                continue;
            }

            // Thread-per-request is fine at the scale we expect (a handful
            // of polls per second). If we ever serve more we revisit with
            // an async pump, but the dashboard's read pattern doesn't justify
            // that complexity today.
            var t = new Thread(() => HandleClient(client))
            {
                IsBackground = true,
                Name = "PerfProfiler/HTTP-req",
            };
            t.Start();
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // Write timeout only — read timeout is enforced explicitly
                // inside TryReadRequest via Socket.Poll so we never throw
                // an IOException for an idle browser preconnect. Throwing
                // would be caught here, but tModLoader hooks first-chance
                // exceptions and logs them anyway, producing noise in
                // client.log every time the browser opened a speculative
                // socket it never sent on.
                stream.WriteTimeout = 5000;

                HttpRequest? req = TryReadRequest(client, stream);
                if (req == null) return;

                HttpResponse resp;
                try
                {
                    resp = _route(req);
                }
                catch (Exception ex)
                {
                    _log($"DashboardHttpServer route threw for {req.Path}", ex);
                    resp = HttpResponse.PlainText(500, $"Internal error: {ex.GetType().Name}: {ex.Message}");
                }
                WriteResponse(stream, resp);
            }
        }
        catch (IOException) { /* client hung up */ }
        catch (SocketException) { /* same */ }
        catch (Exception ex)
        {
            _log("DashboardHttpServer request handler exception", ex);
        }
    }

    private static HttpRequest? TryReadRequest(TcpClient client, NetworkStream stream)
    {
        // Read until "\r\n\r\n" (end of headers). We never read a body —
        // GET-only for the prototype.
        //
        // Non-throwing timeout via Socket.Poll. A 5s ReadTimeout on the
        // stream would throw IOException on idle sockets (browser
        // preconnect / aborted keep-alive / closed tab), which tML's
        // first-chance exception hook then logs as "Silently Caught
        // Exception" every time — pure noise in client.log. Polling
        // returns false on timeout instead of throwing.
        var buffer = new byte[8192];
        int total = 0;
        int headerEnd = -1;
        const int pollIntervalUs = 100_000;  // 100 ms per poll
        const int maxWaitUs = 3_000_000;     // 3 s total budget per request
        int waitedUs = 0;

        while (total < buffer.Length && waitedUs < maxWaitUs)
        {
            // Poll for readable data. Returns true when data is ready OR
            // when the connection has been closed (Available will then be 0).
            if (!client.Client.Poll(pollIntervalUs, SelectMode.SelectRead))
            {
                waitedUs += pollIntervalUs;
                continue;
            }

            // Poll fired. If Available == 0 the peer closed the socket
            // without sending; return cleanly.
            if (client.Client.Available == 0) return null;

            int n;
            try { n = stream.Read(buffer, total, buffer.Length - total); }
            catch { return null; }
            if (n <= 0) break;
            total += n;
            headerEnd = IndexOfHeaderEnd(buffer, total);
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return null;

        string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        string[] lines = head.Split("\r\n");
        if (lines.Length == 0) return null;

        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3) return null;

        string method = requestLine[0];
        string target = requestLine[1];
        // Strip any query string for prototype routing simplicity.
        string path = target;
        int q = target.IndexOf('?');
        if (q >= 0) path = target.Substring(0, q);

        return new HttpRequest(method, path, target);
    }

    private static int IndexOfHeaderEnd(byte[] buf, int len)
    {
        for (int i = 0; i + 3 < len; i++)
        {
            if (buf[i] == (byte)'\r' && buf[i+1] == (byte)'\n' &&
                buf[i+2] == (byte)'\r' && buf[i+3] == (byte)'\n')
            {
                return i;
            }
        }
        return -1;
    }

    private static void WriteResponse(NetworkStream stream, HttpResponse resp)
    {
        var sb = new StringBuilder(256);
        sb.Append("HTTP/1.1 ").Append(resp.Status).Append(' ').Append(StatusText(resp.Status)).Append("\r\n");
        sb.Append("Content-Type: ").Append(resp.ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(resp.Body.Length).Append("\r\n");
        // Loopback-only so a permissive CORS header is harmless; eases dev-time
        // tinkering where the player edits the dashboard HTML and reloads.
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        // No caching — every poll should return fresh state.
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        byte[] header = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(header, 0, header.Length);
        stream.Write(resp.Body, 0, resp.Body.Length);
        stream.Flush();
    }

    private static string StatusText(int s) => s switch
    {
        200 => "OK",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _   => "OK",
    };

    public void Dispose()
    {
        _stopping = true;
        try { _listener.Stop(); } catch { /* swallow */ }
        // Give the accept loop a moment to exit cleanly.
        try { _acceptThread.Join(500); } catch { /* swallow */ }
    }
}

