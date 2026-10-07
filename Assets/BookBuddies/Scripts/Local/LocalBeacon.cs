// Finding open worlds on the home network without typing a code: a hosting PC says "here I am" over UDP every couple of
// seconds, and a joining PC lists whoever it heard lately.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BookBuddies.Local
{
    /// <summary>An open world as a joiner sees it: its name, its address ("http://ip:port"), its join code and how full it is.</summary>
    public sealed class WorldInfo
    {
        public string Name, Server, Code;
        public int Players, Max;
    }

    /// <summary>
    /// Worlds on the home network finding joiners. Announce broadcasts a small hello on UDP 7791 every 2 s while the
    /// beacon lives; Listen collects the hellos and lists the worlds heard in the last 6 s. Both run on a background
    /// thread that never throws; Dispose stops them.
    /// </summary>
    public sealed class LocalBeacon : IDisposable
    {
        public const int Port = 7791;
        const int EveryMs = 2000, ForgetMs = 6000, MaxWorlds = 32, MaxPacket = 512;
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        readonly UdpClient udp;
        readonly Func<WorldInfo> info; // Announce's
        readonly ManualResetEvent stopping = new ManualResetEvent(false);
        readonly Dictionary<string, Heard> heard = new Dictionary<string, Heard>(); // Listen's, by world id (by server for worlds sending none)
        readonly string id = Guid.NewGuid().ToString("N").Substring(0, 16); // Announce's: one world heard from several addresses is still one
        volatile bool disposed;

        /// <summary>The host's side: broadcasts what "info" says (read on a background thread) every 2 s until disposed.</summary>
        public static LocalBeacon Announce(Func<WorldInfo> info) => new LocalBeacon(info);

        /// <summary>The joiner's side: listens for worlds until disposed. Two copies of the game on one PC can both listen.</summary>
        public static LocalBeacon Listen() => new LocalBeacon(null);

        LocalBeacon(Func<WorldInfo> info)
        {
            this.info = info;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                if (info != null) udp.EnableBroadcast = true;
                else
                {
                    try { udp.ExclusiveAddressUse = false; }
                    catch (Exception) { } // not every platform has it
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                }
            }
            catch (Exception)
            {
                // no network to use (or the port is taken by something that won't share): a beacon that hears and says nothing
                udp?.Close();
                udp = null;
                return;
            }
            new Thread(info != null ? (ThreadStart)Announcing : Listening) { IsBackground = true, Name = "BookBuddies beacon" }.Start();
        }

        /// <summary>The worlds heard in the last 6 s, newest first (the order they were first heard in, so a list doesn't jump about), at most 32. A fresh copy each time.</summary>
        public List<WorldInfo> Worlds
        {
            get
            {
                long now = Clock.ElapsedMilliseconds;
                lock (heard)
                    return heard.Values.Where(h => now - h.Last <= ForgetMs).OrderByDescending(h => h.First).Take(MaxWorlds)
                        .Select(h => new WorldInfo { Name = h.World.Name, Server = h.World.Server, Code = h.World.Code, Players = h.World.Players, Max = h.World.Max })
                        .ToList();
            }
        }

        /// <summary>Stops announcing or listening.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping.Set();
            udp?.Close();
        }

        // ---- the host ----

        void Announcing()
        {
            do
            {
                try
                {
                    var world = info();
                    if (world == null) continue;
                    byte[] hello = Encoding.UTF8.GetBytes(Json.Write(new Dictionary<string, object>
                    {
                        ["bb"] = 1, ["id"] = id, ["name"] = LocalSafety.CleanText(world.Name, 40), ["port"] = PortOf(world.Server), ["code"] = world.Code ?? "",
                        ["players"] = world.Players, ["max"] = world.Max,
                    }));
                    foreach (var to in Destinations())
                        try { udp.Send(hello, hello.Length, new IPEndPoint(to, Port)); }
                        catch (SocketException) { } // a network that can't broadcast
                }
                catch (Exception) when (!disposed) { } // try again next time
                catch (Exception) { return; }
            } while (!stopping.WaitOne(EveryMs));
        }

        static int PortOf(string server) =>
            server != null && Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : LocalHost.DefaultPort;

        // everyone on every network this PC is on, and other copies of the game on this PC
        static List<IPAddress> Destinations()
        {
            var to = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        IPAddress mask;
                        try { mask = a.IPv4Mask; }
                        catch (Exception) { continue; } // not every platform knows
                        if (mask == null) continue;
                        byte[] ip = a.Address.GetAddressBytes(), m = mask.GetAddressBytes();
                        if (m.All(b => b == 0) || m.All(b => b == 255)) continue;
                        for (int i = 0; i < 4; i++) ip[i] |= (byte)~m[i];
                        var broadcast = new IPAddress(ip);
                        if (!to.Contains(broadcast)) to.Add(broadcast);
                    }
                }
            }
            catch (Exception) { } // some platforms can't list their networks: the plain broadcast still goes
            return to;
        }

        // ---- the joiner ----

        void Listening()
        {
            while (!disposed)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] packet = udp.Receive(ref from);
                    if (packet.Length <= MaxPacket) Hear(packet, from.Address);
                }
                catch (Exception) when (!disposed) { } // a bad packet, or Windows reporting an old send's trouble
                catch (Exception) { return; }
            }
        }

        void Hear(byte[] packet, IPAddress from)
        {
            Dictionary<string, object> o;
            try { o = Json.ParseObject(Encoding.UTF8.GetString(packet)); }
            catch (Exception) { return; }
            if (o == null || o.Num("bb") != 1 || from.AddressFamily != AddressFamily.InterNetwork) return;
            int port = o.Int("port", LocalHost.DefaultPort);
            if (port < 1 || port > 65535) port = LocalHost.DefaultPort;
            string name = LocalSafety.CleanText(o.Str("name"), 40);
            var world = new WorldInfo
            {
                Name = name.Length > 0 ? name : "A BookBuddies world", Server = "http://" + from + ":" + port, Code = JoinCode.For(from, port),
                Players = Math.Max(0, Math.Min(999, o.Int("players"))), Max = Math.Max(0, Math.Min(999, o.Int("max"))),
            };
            // a world on this PC arrives twice, from 127.0.0.1 and from its network address: one entry, at the network address
            string key = o.Str("id");
            if (key.Length < 8 || key.Length > 32 || !key.All(Uri.IsHexDigit)) key = world.Server;
            bool loopback = IPAddress.IsLoopback(from);
            long now = Clock.ElapsedMilliseconds;
            lock (heard)
            {
                if (heard.TryGetValue(key, out var h))
                {
                    if (h.Loopback && !loopback) h.Loopback = false;
                    else { world.Server = h.World.Server; world.Code = h.World.Code; } // keep the address it's listed under
                    h.World = world;
                    h.Last = now;
                    return;
                }
                if (heard.Count >= MaxWorlds)
                {
                    foreach (string stale in heard.Where(kv => now - kv.Value.Last > ForgetMs).Select(kv => kv.Key).ToList()) heard.Remove(stale);
                    if (heard.Count >= MaxWorlds) heard.Remove(heard.OrderBy(kv => kv.Value.Last).First().Key);
                }
                heard[key] = new Heard { World = world, First = now, Last = now, Loopback = loopback };
            }
        }

        sealed class Heard
        {
            public WorldInfo World;
            public long First, Last; // when it was first and last heard (Clock ms)
            public bool Loopback;    // only heard from this PC's own 127.0.0.1 so far
        }
    }
}
