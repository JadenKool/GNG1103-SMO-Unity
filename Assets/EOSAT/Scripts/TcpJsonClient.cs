using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Eosat
{
    /// <summary>
    /// Background TCP client that splits the incoming stream into complete JSON objects
    /// (works whether or not the sender puts newlines between messages) and auto-reconnects.
    /// </summary>
    public class TcpJsonClient
    {
        readonly string host;
        readonly int port;
        readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        Thread thread;
        volatile bool running;
        volatile bool connected;
        TcpClient client;

        public bool Connected => connected;
        public string LastError { get; private set; }

        public TcpJsonClient(string host, int port) { this.host = host; this.port = port; }

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "EOSAT telemetry" };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            try { client?.Close(); } catch { }
            thread = null;
        }

        public bool TryDequeue(out string json) => messages.TryDequeue(out json);

        void Loop()
        {
            while (running)
            {
                try
                {
                    using (client = new TcpClient())
                    {
                        client.NoDelay = true;
                        var ar = client.BeginConnect(host, port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(2000)) throw new IOException($"no server on {host}:{port}");
                        client.EndConnect(ar);
                        connected = true;
                        LastError = null;
                        using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
                            ReadObjects(reader);
                    }
                }
                catch (Exception e) { LastError = e.Message; }
                connected = false;
                if (running) Thread.Sleep(2000);
            }
        }

        void ReadObjects(StreamReader reader)
        {
            var sb = new StringBuilder();
            int depth = 0;
            bool inString = false, escape = false;
            int c;
            while (running && (c = reader.Read()) != -1)
            {
                char ch = (char)c;
                if (depth == 0 && ch != '{') continue; // skip whitespace/newlines between objects
                sb.Append(ch);
                if (inString)
                {
                    if (escape) escape = false;
                    else if (ch == '\\') escape = true;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (ch == '"') inString = true;
                else if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0)
                {
                    messages.Enqueue(sb.ToString());
                    sb.Clear();
                }
                if (sb.Length > 65536) { sb.Clear(); depth = 0; } // runaway garbage guard
            }
        }
    }
}
