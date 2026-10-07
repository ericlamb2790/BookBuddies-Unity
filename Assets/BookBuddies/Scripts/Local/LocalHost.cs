// Hosting a world: friends' games play in this PC's offline world, on the home network or through a forwarded port.
// A small HTTP/1.1 and WebSocket server on TCP 7790 answers the Worker's /api routes from LocalServer and lets visitors
// into LocalTowns' rooms, so a friend's game is just the online client pointed at "http://<this PC>:7790". Sockets are
// read on background tasks; everything that touches the world waits in a queue for Pump on the main thread.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookBuddies.Net;

namespace BookBuddies.Local
{
    /// <summary>
    /// Opens this PC's offline world to friends' games. They sign up here (their own account, pets and coins in this
    /// world), use the same /api routes as the online server, and join the live rooms over a WebSocket. Start, Stop
    /// and Pump belong to the main thread, and Pump must run every frame while the world is open: requests and town
    /// messages are only ever handled inside it, because LocalServer and LocalTowns aren't thread-safe.
    /// </summary>
    public static class LocalHost
    {
        /// <summary>The TCP port a world opens on unless told otherwise.</summary>
        public const int DefaultPort = 7790;
        /// <summary>At most this many live-town connections from other PCs at once.</summary>
        public const int MaxVisitors = 16;

        const int MaxConnections = 24;      // open TCP connections: requests and live towns together
        const int MaxHead = 8 * 1024;       // the request line and headers
        const int MaxBody = 64 * 1024;
        const int ReadTimeoutMs = 10000;    // to read a whole request, or to write a reply
        const int LingerMs = 2000;          // after a reply, how long the rest of a refused request is read and dropped
        const int SignupsPerAddress = 10;   // new accounts from one PC while the world is open (the Worker allows 10 a day per IP)
        const int MaxDepth = 16;            // how deep a visitor's JSON may nest (the JSON reader is recursive)
        const int PassMinutes = 15;
        const string LivePath = "/api/world/live";
        const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const string NoName = "A BookBuddies world";
        static readonly Regex Bearer = new Regex("^Bearer\\s+([A-Fa-f0-9]{64})\\z", RegexOptions.IgnoreCase);

        static Run run;
        static int lastPort = DefaultPort;
        static string name = NoName;

        /// <summary>Open to other PCs right now.</summary>
        public static bool Running => run != null;

        /// <summary>The port the world is open on (while closed, the last one it opened on).</summary>
        public static int Port => run?.Port ?? lastPort;

        /// <summary>Live-town connections from other PCs right now.</summary>
        public static int Visitors
        {
            get
            {
                var r = run;
                return r == null ? 0 : Volatile.Read(ref r.Visitors);
            }
        }

        /// <summary>The world's name as friends see it ("Damp’s world"): tidied, at most 40 characters.</summary>
        public static string Name
        {
            get => name;
            set
            {
                string s = LocalSafety.CleanText(value, 40);
                name = s.Length > 0 ? s : NoName;
            }
        }

        /// <summary>Why the last Start failed, in plain words; null after one that worked.</summary>
        public static string Error { get; private set; }

        /// <summary>Raised from Pump when a request trips over a bug on the host: worth a line in the log.</summary>
        public static event Action<Exception> Failed;

        /// <summary>
        /// Opens the world on this port on every network the PC is on (0 picks any free port). False, with Error saying
        /// why, when it can't; true straight away when it's already open.
        /// </summary>
        public static bool Start(int port = DefaultPort)
        {
            if (run != null) return true;
            if (port < 0 || port > 65535)
            {
                Error = $"{port} isn’t a port number. Pick one from 1024 to 65535.";
                return false;
            }
            var listener = new TcpListener(IPAddress.Any, port);
            // Linux and macOS hold a port for a minute after a world closes unless asked not to; on Windows that ask would let two copies share it
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                try { listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
                catch (SocketException) { }
            try { listener.Start(); }
            catch (SocketException e)
            {
                listener.Stop();
                Error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {port} is already in use on this PC. Close the other copy of the game, or pick another port."
                    : e.SocketErrorCode == SocketError.AccessDenied
                        ? $"This PC didn’t let the game use port {port}. Try another port."
                        : "Your world couldn’t be opened to friends: " + e.Message;
                return false;
            }
            var r = new Run(listener);
            lastPort = r.Port;
            Error = null;
            run = r;
            Task.Run(() => Accept(r));
            return true;
        }

        /// <summary>
        /// Closes the world to other PCs: visitors leave their rooms (their games see the town close), waiting requests
        /// are dropped and every connection closes. Start opens it again.
        /// </summary>
        public static void Stop()
        {
            var r = run;
            if (r == null) return;
            run = null;
            r.Stopping.Cancel(); // requests stop waiting for Pump, live towns close with 1001
            try { r.Listener.Stop(); }
            catch (SocketException) { }
            foreach (var v in r.Here) v.Link.Dispose();
            r.Here.Clear();
        }

        /// <summary>
        /// Main thread, every frame while open: answers the requests that came in, lets new visitors into their rooms,
        /// runs the rooms' turns (so they go on while the host sits on the title screen) and passes town messages both ways.
        /// </summary>
        public static void Pump()
        {
            var r = run;
            if (r == null) return;
            for (int n = r.Work.Count; n > 0 && r.Work.TryDequeue(out var job); n--) job();
            try
            {
                while (r.Arrived.TryDequeue(out var v)) Welcome(r, v);
                LocalTowns.Tick(LocalServer.Now);
                foreach (var v in r.Here)
                {
                    while (v.Socket.Inbox.TryDequeue(out string text))
                        if (Shallow(text)) v.Link.Send(text);
                    v.Link.Poll(); // the room's messages go out as frames; its close closes the socket
                }
            }
            catch (Exception e) { Report(e); } // a bug in a room: the next frame carries on
            r.Here.RemoveAll(Gone);
        }

        /// <summary>
        /// This PC's IPv4 addresses, the likeliest for friends first: home-network (private) addresses on a network with a
        /// router, then other private ones, then the rest. Never loopback or 169.254 (no network found).
        /// </summary>
        public static List<string> Addresses()
        {
            var found = new List<(IPAddress ip, int rank)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.OperationalStatus != OperationalStatus.Up && nic.OperationalStatus != OperationalStatus.Unknown) continue;
                    var props = nic.GetIPProperties();
                    int router = HasRouter(props) ? 0 : 1;
                    foreach (var a in props.UnicastAddresses) found.Add((a.Address, router));
                }
            }
            catch (Exception) { } // some platforms can't list their networks
            if (found.Count == 0)
                try { found.AddRange(Dns.GetHostAddresses(Dns.GetHostName()).Select(ip => (ip, 1))); }
                catch (Exception) { }
            return found.Where(f => Usable(f.ip)).OrderBy(f => (IsPrivate(f.ip) ? 0 : 2) + f.rank).Select(f => f.ip.ToString()).Distinct().ToList();
        }

        /// <summary>
        /// The join code for the likeliest of this PC's addresses and Port; null while closed. A PC with no network gets
        /// 127.0.0.1's, which only another copy of the game on this PC can use.
        /// </summary>
        public static string Code
        {
            get
            {
                var r = run;
                return r == null ? null : JoinCode.For(IPAddress.Parse(r.Address()), r.Port);
            }
        }

        /// <summary>The open world as LocalBeacon announces it; null while closed. Safe from any thread.</summary>
        public static WorldInfo Info()
        {
            var r = run;
            if (r == null) return null;
            string address = r.Address();
            return new WorldInfo
            {
                Name = name, Server = "http://" + address + ":" + r.Port.ToString(CultureInfo.InvariantCulture), Code = JoinCode.For(IPAddress.Parse(address), r.Port),
                Players = Volatile.Read(ref r.Visitors), Max = MaxVisitors,
            };
        }

        /// <summary>Whether JSON text nests at most 16 deep (the JSON reader is recursive: 10,000 brackets would end the game).</summary>
        internal static bool Shallow(string json)
        {
            int depth = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (c == '\\') i++;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '[' || c == '{') { if (++depth > MaxDepth) return false; }
                else if (c == ']' || c == '}') depth--;
            }
            return true;
        }

        // ---- the main thread's part ----

        // a visitor's socket is open: their link joins the room the ticket names
        static void Welcome(Run r, Visitor v)
        {
            var socket = v.Socket;
            v.Link = new LocalLink();
            v.Link.OnMessage += socket.SendText;
            v.Link.OnClosed += code => socket.Close(HostSocket.Sendable(code) ? code : 1013); // "couldn't join" (1006) can't be sent: "try again later"
            v.Link.Connect(WorldLinks.LocalScheme + Uri.EscapeDataString(v.Ticket));
            r.Here.Add(v);
        }

        static bool Gone(Visitor v)
        {
            if (!v.Socket.Ended) return false;
            v.Link.Dispose(); // the others see them leave
            return true;
        }

        // one request through LocalServer, the reply as JSON text
        static void Handle(Run r, Request q, TaskCompletionSource<(int, string)> reply)
        {
            try
            {
                if (q.Path == "/api/health")
                {
                    reply.TrySetResult((200, Json.Write(Health(r))));
                    return;
                }
                if (q.Path == "/api/register" && r.Signups.TryGetValue(q.Address, out int made) && made >= SignupsPerAddress)
                {
                    reply.TrySetResult((429, Problem("Lots of new pets from here today. Try again tomorrow.")));
                    return;
                }
                var task = LocalServer.Handle(q.Method, q.Target, q.Body, q.Token);
                // Handle answers at once (nothing in it awaits); if that ever changes, the reply is still made inside Pump
                if (task.IsCompleted) Finish(r, q, task, reply);
                else task.ContinueWith(t => r.Work.Enqueue(() => Finish(r, q, t, reply)), TaskScheduler.Default);
            }
            catch (Exception e) { Fail(e, reply); }
        }

        static void Finish(Run r, Request q, Task<Dictionary<string, object>> task, TaskCompletionSource<(int, string)> reply)
        {
            try
            {
                var o = task.GetAwaiter().GetResult();
                if (q.Path == "/api/register") r.Signups[q.Address] = (r.Signups.TryGetValue(q.Address, out int n) ? n : 0) + 1;
                if (q.Path == "/api/plaza/world/ticket") Hide(r, o);
                reply.TrySetResult((200, Json.Write(o)));
            }
            catch (LocalProblem p)
            {
                reply.TrySetResult((p.Status, Problem(p.Message)));
                if (p.InnerException != null) Report(p.InnerException);
            }
            catch (Exception e) { Fail(e, reply); }
        }

        static void Fail(Exception e, TaskCompletionSource<(int, string)> reply)
        {
            reply.TrySetResult((500, Problem("Something went wrong on the host.")));
            Report(e);
        }

        static void Report(Exception e)
        {
            try { Failed?.Invoke(e); }
            catch (Exception) { }
        }

        // GET /api/health: the server's own health, and that this is an open world: its name and how full it is
        static Dictionary<string, object> Health(Run r)
        {
            var health = LocalServer.Handle("GET", "/health", null, null).GetAwaiter().GetResult();
            health["world"] = true;
            health["name"] = name;
            health["players"] = Volatile.Read(ref r.Visitors);
            health["max"] = MaxVisitors;
            return health;
        }

        // A town ticket is "<pid>|<town>:<room>", unsigned, so a visitor could write anyone's. Over the network the ticket
        // and pass become random ids that only this host can turn back into the real ticket.
        static void Hide(Run r, Dictionary<string, object> reply)
        {
            string ticket = reply.Str("ticket");
            if (ticket.Length == 0) return;
            double exp = reply.Num("passExp");
            long expires = exp > 0 ? (long)exp : LocalServer.Now + PassMinutes * 60000L;
            reply["ticket"] = r.Passes.Issue(ticket, expires, false);
            if (reply.ContainsKey("pass")) reply["pass"] = r.Passes.Issue(reply.Str("pass"), expires, true);
        }

        static string Problem(string message) => Json.Write(new Dictionary<string, object> { ["error"] = message });

        // ---- the background tasks' part ----

        static async Task Accept(Run r)
        {
            while (!r.Stopping.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await r.Listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception)
                {
                    if (r.Stopping.IsCancellationRequested) return;
                    await Task.Delay(100).ConfigureAwait(false); // a hiccup: try again shortly
                    continue;
                }
                if (Interlocked.Increment(ref r.Connections) > MaxConnections || r.Stopping.IsCancellationRequested)
                {
                    Interlocked.Decrement(ref r.Connections);
                    tcp.Close();
                    continue;
                }
                _ = Task.Run(() => Serve(r, tcp));
            }
        }

        // one connection: an API request answered through Pump, or a visitor's live town
        static async Task Serve(Run r, TcpClient tcp)
        {
            var c = new Conn(tcp);
            try
            {
                c.Stream = tcp.GetStream();
                c.Address = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString();
                tcp.NoDelay = true;
                Request q;
                using (var timeout = new CancellationTokenSource(ReadTimeoutMs))
                using (timeout.Token.Register(c.Close))
                using (r.Stopping.Token.Register(c.Close))
                    q = await Read(c).ConfigureAwait(false);
                if (q == null) return;
                if (q.Path == LivePath) await Visit(r, c, q).ConfigureAwait(false);
                else await Respond(c, await Answer(r, q).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (LocalProblem p) { await Respond(c, (p.Status, Problem(p.Message))).ConfigureAwait(false); }
            catch (Exception) { } // they hung up, took too long, or the world closed
            finally
            {
                c.Close();
                Interlocked.Decrement(ref r.Connections);
            }
        }

        // the request line, headers and body; null when the connection closes first. A refusal throws a LocalProblem.
        static async Task<Request> Read(Conn c)
        {
            var buffer = new byte[MaxHead];
            int n = 0, end;
            while ((end = HeadEnd(buffer, n)) < 0)
            {
                if (n == buffer.Length) throw new LocalProblem("That request’s headers are too long.", 431);
                int got = await c.Stream.ReadAsync(buffer, n, buffer.Length - n).ConfigureAwait(false);
                if (got == 0) return null;
                n += got;
            }
            var q = Parse(Encoding.ASCII.GetString(buffer, 0, end));
            q.Address = c.Address;
            if (!q.Path.StartsWith("/api/", StringComparison.Ordinal)) throw new LocalProblem("Not found", 404);
            if (q.Header("transfer-encoding").Length > 0) throw new LocalProblem("Please send the request with a Content-Length.", 411);

            string length = q.Header("content-length");
            long size = 0;
            if (length.Length > 0 && !long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out size))
                throw new LocalProblem("That request couldn’t be read.", 400);
            if (size > MaxBody) throw new LocalProblem("That’s too much to send at once.", 413);
            if (size > 0 && q.Header("expect").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                await Write(c, Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n")).ConfigureAwait(false);

            var body = new byte[size];
            int have = Math.Min(n - end, body.Length);
            Buffer.BlockCopy(buffer, end, body, 0, have);
            q.Early = new byte[n - end - have];
            Buffer.BlockCopy(buffer, end + have, q.Early, 0, q.Early.Length);
            while (have < body.Length)
            {
                int got = await c.Stream.ReadAsync(body, have, body.Length - have).ConfigureAwait(false);
                if (got == 0) return null;
                have += got;
            }
            q.Body = body.Length > 0 ? ReadJson(Encoding.UTF8.GetString(body)) : null;
            return q;
        }

        // where "\r\n\r\n" ends the head; -1 until it has arrived
        static int HeadEnd(byte[] buffer, int n)
        {
            for (int i = 3; i < n; i++)
                if (buffer[i] == '\n' && buffer[i - 1] == '\r' && buffer[i - 2] == '\n' && buffer[i - 3] == '\r') return i + 1;
            return -1;
        }

        static Request Parse(string head)
        {
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length != 3 || first[0].Length == 0 || !first[1].StartsWith("/", StringComparison.Ordinal) || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                throw new LocalProblem("That request couldn’t be read.", 400);
            var q = new Request { Method = first[0], Target = first[1] };
            int mark = q.Target.IndexOf('?');
            q.Path = mark < 0 ? q.Target : q.Target.Substring(0, mark);
            q.Query = mark < 0 ? "" : q.Target.Substring(mark + 1);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) q.Headers[lines[i].Substring(0, colon).Trim().ToLowerInvariant()] = lines[i].Substring(colon + 1).Trim();
            }
            return q;
        }

        // the body as the Worker's readJson reads it: an object, or nothing when it isn't one
        static Dictionary<string, object> ReadJson(string text)
        {
            if (!Shallow(text)) return null;
            try { return Json.Parse(text) as Dictionary<string, object>; }
            catch (Exception) { return null; }
        }

        // an API request: Pump answers it on the main thread
        static async Task<(int status, string json)> Answer(Run r, Request q)
        {
            var reply = new TaskCompletionSource<(int, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            r.Work.Enqueue(() => Handle(r, q, reply));
            using (r.Stopping.Token.Register(() => reply.TrySetCanceled()))
                return await reply.Task.ConfigureAwait(false);
        }

        // GET /api/world/live: a ticket id from this host becomes a WebSocket into the room it names
        static async Task Visit(Run r, Conn c, Request q)
        {
            if (q.Method != "GET" || !q.Header("upgrade").Equals("websocket", StringComparison.OrdinalIgnoreCase))
                throw new LocalProblem("Expected a WebSocket", 426);
            string key = q.Header("sec-websocket-key");
            if (q.Header("sec-websocket-version") != "13" || !IsKey(key)) throw new LocalProblem("That WebSocket request couldn’t be read.", 400);
            string ticket = r.Passes.Redeem(Js.Query(q.Query, "ticket"), Js.Query(q.Query, "s"))
                ?? throw new LocalProblem("That town ticket isn’t valid here any more.", 403);

            string accept;
            using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            await Write(c, Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"))
                .ConfigureAwait(false);

            var socket = new HostSocket(c.Stream, c.Close, q.Early);
            if (!TakeSeat(r))
            {
                socket.Close(1013); // the world is full: try again later
                await socket.Run().ConfigureAwait(false);
                return;
            }
            try
            {
                r.Arrived.Enqueue(new Visitor { Socket = socket, Ticket = ticket });
                using (r.Stopping.Token.Register(() => socket.Close(1001)))
                    await socket.Run().ConfigureAwait(false);
            }
            finally { Interlocked.Decrement(ref r.Visitors); }
        }

        static bool IsKey(string key)
        {
            try { return Convert.FromBase64String(key).Length == 16; }
            catch (FormatException) { return false; }
        }

        // a place among the MaxVisitors, if one is free
        static bool TakeSeat(Run r)
        {
            while (true)
            {
                int n = Volatile.Read(ref r.Visitors);
                if (n >= MaxVisitors) return false;
                if (Interlocked.CompareExchange(ref r.Visitors, n + 1, n) == n) return true;
            }
        }

        static async Task Write(Conn c, byte[] bytes)
        {
            using (var timeout = new CancellationTokenSource(ReadTimeoutMs))
            using (timeout.Token.Register(c.Close))
                await c.Stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        // The reply, then a polite hang-up: stop sending and read whatever the client still sends for a moment, so a
        // refusal before the body (413) reaches them instead of a reset connection. Never throws.
        static async Task Respond(Conn c, (int status, string json) reply)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(reply.json);
                string head = "HTTP/1.1 " + reply.status.ToString(CultureInfo.InvariantCulture) + " " + Reason(reply.status)
                    + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length.ToString(CultureInfo.InvariantCulture)
                    + "\r\nConnection: close\r\n\r\n";
                await Write(c, Encoding.ASCII.GetBytes(head).Concat(body).ToArray()).ConfigureAwait(false);
                c.Tcp.Client.Shutdown(SocketShutdown.Send);
                using (var linger = new CancellationTokenSource(LingerMs))
                using (linger.Token.Register(c.Close))
                {
                    var scratch = new byte[4096];
                    for (int total = 0; total < 4 * MaxBody;)
                    {
                        int got = await c.Stream.ReadAsync(scratch, 0, scratch.Length).ConfigureAwait(false);
                        if (got == 0) break;
                        total += got;
                    }
                }
            }
            catch (Exception) { } // they've gone
        }

        static string Reason(int status) => status switch
        {
            200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 409 => "Conflict",
            411 => "Length Required", 413 => "Payload Too Large", 426 => "Upgrade Required", 429 => "Too Many Requests",
            431 => "Request Header Fields Too Large", 500 => "Internal Server Error", _ => "Error",
        };

        // ---- addresses ----

        static bool HasRouter(IPInterfaceProperties props)
        {
            try { return props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)); }
            catch (Exception) { return false; } // not every platform knows
        }

        static bool Usable(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] != 0 && b[0] != 127 && b[0] < 224 && !(b[0] == 169 && b[1] == 254); // not loopback, "no network" or multicast
        }

        static bool IsPrivate(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168);
        }

        // ---- what a world keeps while it's open ----

        // One opening of the world: Stop ends it and Start makes a new one, so nothing from before carries over.
        sealed class Run
        {
            public readonly TcpListener Listener;
            public readonly int Port;
            public readonly CancellationTokenSource Stopping = new CancellationTokenSource();
            public readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();      // requests for Pump
            public readonly ConcurrentQueue<Visitor> Arrived = new ConcurrentQueue<Visitor>(); // new sockets waiting for Pump to give them a link
            public readonly List<Visitor> Here = new List<Visitor>();                          // Pump's: visitors and their links
            public readonly Dictionary<string, int> Signups = new Dictionary<string, int>();   // Pump's: accounts made, by address
            public readonly Passes Passes = new Passes();
            public int Connections, Visitors;
            Tuple<string, int> best; // the likeliest address, and when it was looked up

            public Run(TcpListener listener)
            {
                Listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }

            // this PC's likeliest address for friends, looked up at most every 5 s (Code may be read every frame)
            public string Address()
            {
                var known = best;
                if (known != null && unchecked(Environment.TickCount - known.Item2) < 5000) return known.Item1;
                var list = Addresses();
                best = known = Tuple.Create(list.Count > 0 ? list[0] : "127.0.0.1", Environment.TickCount);
                return known.Item1;
            }
        }

        // The ids handed out for town tickets: each stands for a real ticket until it expires. A player's newer ticket
        // (or pass) replaces their older one. Issued inside Pump, redeemed by the socket tasks.
        sealed class Passes
        {
            readonly Dictionary<string, (string ticket, long expires, bool anyRoom)> ids = new Dictionary<string, (string, long, bool)>();

            public string Issue(string ticket, long expires, bool anyRoom)
            {
                string id = LocalAccounts.Hex(LocalAccounts.RandomBytes(16)), pid = Pid(ticket);
                long now = LocalServer.Now;
                lock (ids)
                {
                    foreach (string old in ids.Where(kv => kv.Value.expires <= now || (kv.Value.anyRoom == anyRoom && Pid(kv.Value.ticket) == pid)).Select(kv => kv.Key).ToList())
                        ids.Remove(old);
                    ids[id] = (ticket, expires, anyRoom);
                }
                return id;
            }

            // the real ticket for an id, or null; a pass goes to room "s" of its town (1-6, else 1), like the Worker's "town:*" pass
            public string Redeem(string id, string s)
            {
                (string ticket, long expires, bool anyRoom) p;
                lock (ids)
                    if (id == null || !ids.TryGetValue(id, out p)) return null;
                if (p.expires <= LocalServer.Now) return null;
                if (!p.anyRoom) return p.ticket;
                double asked = Js.ParseInt(s);
                int room = (int)Math.Max(1, Math.Min(LocalServer.Rooms, double.IsNaN(asked) || asked == 0 ? 1 : asked));
                return p.ticket.Substring(0, p.ticket.LastIndexOf(':') + 1) + room.ToString(CultureInfo.InvariantCulture);
            }

            static string Pid(string ticket) => ticket.Substring(0, Math.Max(0, ticket.LastIndexOf('|')));
        }

        sealed class Conn
        {
            public readonly TcpClient Tcp;
            public NetworkStream Stream;
            public string Address = "";
            int closed;

            public Conn(TcpClient tcp) { Tcp = tcp; }

            public void Close()
            {
                if (Interlocked.Exchange(ref closed, 1) == 1) return;
                try { Tcp.Close(); }
                catch (Exception) { }
            }
        }

        sealed class Request
        {
            public string Method, Target, Path, Query, Address = "";
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
            public Dictionary<string, object> Body;
            public byte[] Early; // what came after the head and body (a WebSocket's first frames)

            public string Header(string key) => Headers.TryGetValue(key, out var v) ? v : "";

            // "Authorization: Bearer <64 hex>", as the Worker reads it
            public string Token => Bearer.Match(Header("authorization")) is var m && m.Success ? m.Groups[1].Value : null;
        }

        sealed class Visitor
        {
            public HostSocket Socket;
            public string Ticket;  // the real one: "<pid>|<town>:<room>"
            public LocalLink Link; // Pump's
        }
    }
}
