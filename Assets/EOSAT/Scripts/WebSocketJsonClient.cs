using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Eosat
{
    /// <summary>
    /// Background WebSocket client (System.Net.WebSockets.ClientWebSocket) for the smo-vr/1 feed on
    /// ws://127.0.0.1:8101/vr. Same shape as TcpJsonClient: runs off the main thread, auto-reconnects,
    /// and pushes complete JSON strings into a ConcurrentQueue that TelemetryHub drains in Update().
    /// A WebSocket message may hold one object or several NDJSON lines; each line is queued separately.
    /// </summary>
    public sealed class WebSocketJsonClient : ITelemetryClient
    {
        const int MaxMessageBytes = 1 << 20;   // 1 MB runaway guard
        const int MaxQueued = 2000;            // drop oldest if the main thread stalls (no unbounded growth)

        public readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        public Uri Uri { get; }
        public float ReconnectDelaySeconds = 2f;
        public float ConnectTimeoutSeconds = 3f;

        volatile bool connected;
        volatile string lastError;
        CancellationTokenSource cts;
        Task loop;
        ClientWebSocket socket;               // only touched by the loop task + Disconnect()
        readonly object gate = new object();

        public bool Connected => connected;
        public string LastError => lastError;
        public WebSocketState State { get { var s = socket; try { return s?.State ?? WebSocketState.None; } catch { return WebSocketState.None; } } }
        public string Label => $"WebSocket: {Uri.Port}";

        public WebSocketJsonClient(string uri) { Uri = new Uri(uri); }

        public void Start()
        {
            lock (gate)
            {
                if (cts != null) return;
                cts = new CancellationTokenSource();
                var token = cts.Token;
                loop = Task.Run(() => RunAsync(token), token);
            }
        }

        public bool TryDequeue(out string json) => messages.TryDequeue(out json);

        /// <summary>Stops the loop, closes the socket politely (1 s max) and frees everything. Safe to call twice.</summary>
        public void Disconnect()
        {
            CancellationTokenSource c;
            ClientWebSocket s;
            lock (gate) { c = cts; cts = null; s = socket; }
            if (c == null) return;
            connected = false;
            try { c.Cancel(); } catch (ObjectDisposedException) { }

            if (s != null && s.State == WebSocketState.Open)
            {
                try
                {
                    using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                        s.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "switching source", closeCts.Token)
                         .Wait(TimeSpan.FromSeconds(1));
                }
                catch { /* already closing / server gone */ }
            }
            try { s?.Abort(); } catch { }
            // The loop disposes its own socket; dispose the CTS once the loop has seen the cancel.
            var l = loop;
            if (l != null) l.ContinueWith(_ => c.Dispose(), TaskScheduler.Default);
            else c.Dispose();
            while (messages.TryDequeue(out _)) { }   // nothing stale leaks into the next source
        }

        public void Stop() => Disconnect();

        async Task RunAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var text = new StringBuilder(1024);
            var bytes = new System.IO.MemoryStream();

            while (!token.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                {
                    lock (gate) socket = ws;
                    try
                    {
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            connectCts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                            await ws.ConnectAsync(Uri, connectCts.Token).ConfigureAwait(false);
                        }
                        connected = true;
                        lastError = null;

                        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                        {
                            bytes.SetLength(0);
                            WebSocketReceiveResult r;
                            do
                            {
                                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                                if (r.MessageType == WebSocketMessageType.Close)
                                {
                                    lastError = "server closed the connection";
                                    goto closed;
                                }
                                bytes.Write(buffer, 0, r.Count);
                                if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("message too large");
                            } while (!r.EndOfMessage);

                            if (r.MessageType != WebSocketMessageType.Text) continue;
                            Enqueue(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length), text);
                        }
                        closed:;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (OperationCanceledException) { lastError = $"no server on {Uri} (timeout)"; }
                    catch (WebSocketException e) { lastError = e.InnerException?.Message ?? e.Message; }
                    catch (Exception e) { lastError = e.Message; }
                    finally
                    {
                        connected = false;
                        lock (gate) if (socket == ws) socket = null;
                    }
                }

                if (token.IsCancellationRequested) break;
                try { await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            bytes.Dispose();
        }

        void Enqueue(string payload, StringBuilder line)
        {
            // NDJSON: split on newlines; a single object without a newline is one message.
            line.Clear();
            foreach (char ch in payload)
            {
                if (ch == '\n') { Push(line); line.Clear(); }
                else if (ch != '\r') line.Append(ch);
            }
            Push(line);
        }

        void Push(StringBuilder line)
        {
            if (line.Length == 0) return;
            string s = line.ToString().Trim();
            if (s.Length == 0) return;
            messages.Enqueue(s);
            while (messages.Count > MaxQueued && messages.TryDequeue(out _)) { }
        }
    }
}
