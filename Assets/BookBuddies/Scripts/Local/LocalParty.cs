// The party in a hosted world: no invites, no accept buttons. While the world is open (LocalHost), its host leads one
// party and every friend who visits is in it, so they can see where the others are, land in the same room and chat
// across towns. Like the rest of Local/ it isn't thread-safe: everything here runs inside LocalHost.Pump, or on the
// main thread for the host's own game.

using System.Collections.Generic;
using System.Globalization;
using Msg = System.Collections.Generic.Dictionary<string, object>;

namespace BookBuddies.Local
{
    /// <summary>
    /// The open world's party: the host (Leader) plus every visitor who has signed in to a request or come into town
    /// since the world opened. A visitor counts as online while they're in town or for 2 minutes after their last
    /// request, and leaves the party after 10 minutes offline. GET /api/party shows it; "psay" in town is its chat, "pb"
    /// carries its shared battles (PartyBattle), and
    /// {"t":"party"} tells members' games to fetch it again whenever someone joins, leaves, goes on or offline, or moves
    /// (at most once a second, so someone hopping in and out of town can't set every member's game asking nonstop).
    /// </summary>
    public static class LocalParty
    {
        const long OnlineMs = 2 * 60000;    // a request this recent keeps a visitor online
        const long LeaveMs = 10 * 60000;    // offline this long: out of the party
        const long NudgeGapMs = 1000;       // at most one nudge a second: what changes meanwhile goes out with the next
        const int ChatKept = 30;
        const string Nudge = "{\"t\":\"party\"}";

        static readonly List<Member> members = new List<Member>(); // the leader first, then visitors in the order they came
        static readonly List<Msg> chat = new List<Msg>();           // the last ChatKept lines, oldest first
        static string leader;
        static int said;      // party chat lines so far: each line's "n"
        static bool changed;  // something members' games show changed since the last nudge
        static long nudgedAt; // when the last nudge went out (ms since 1970)

        /// <summary>A world is open, so there's a party.</summary>
        public static bool Active => LocalHost.Running;

        /// <summary>The host's account id: they lead the party (set when the world opens). Null while there's no host.</summary>
        public static string Leader
        {
            get => leader;
            set
            {
                if (string.IsNullOrEmpty(value)) value = null;
                if (leader == value) return;
                leader = value;
                members.RemoveAll(m => m.Pid == value);
                if (value != null) members.Insert(0, new Member { Pid = value });
                changed = true;
            }
        }

        /// <summary>The world closed: members still in town are told, then the party, its chat and its leader are gone.</summary>
        public static void Reset()
        {
            if (members.Count > 0) LocalTowns.Tell(InParty, Nudge);
            members.Clear();
            chat.Clear();
            leader = null;
            said = 0;
            changed = false;
            nudgedAt = 0;
        }

        // ---- hooks for LocalHost, LocalServer, LocalAccounts and LocalTown ----

        /// <summary>LocalHost: a visitor signed in to a request or came into town. They join the party if they aren't in it, and are seen now.</summary>
        internal static void Visited(string pid)
        {
            if (!Active || string.IsNullOrEmpty(pid) || LocalServer.Player(pid) == null) return;
            var m = Find(pid);
            if (m == null)
            {
                members.Add(m = new Member { Pid = pid });
                changed = true;
            }
            m.Seen = LocalServer.Now;
        }

        /// <summary>Whether a player is in the party right now.</summary>
        internal static bool Has(string pid) => Active && InParty(pid);

        /// <summary>
        /// LocalHost.Pump, every frame while the world is open: notes who's online and where, lets go of visitors offline
        /// for 10 minutes, and nudges every member in town (at most once a second) when any of that changed.
        /// </summary>
        internal static void Pump()
        {
            long now = LocalServer.Now;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                var m = members[i];
                var (online, room) = StateOf(m, now);
                if (room != null) m.Seen = now; // offline starts when they leave town, not when they came in
                if (m.Pid != leader && (LocalServer.Player(m.Pid) == null || now - m.Seen >= OnlineMs + LeaveMs))
                {
                    members.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (online == m.Online && room == m.Room) continue;
                m.Online = online;
                m.Room = room;
                changed = true;
            }
            if (!changed || (now >= nudgedAt && now - nudgedAt < NudgeGapMs)) return;
            changed = false;
            nudgedAt = now;
            LocalTowns.Tell(InParty, Nudge);
        }

        /// <summary>
        /// GET /api/party: the party as a member sees it (who, their looks, whether they're online and which town and room
        /// they're in, and the last 30 chat lines), or {party: false} when no world is open or they aren't in it.
        /// </summary>
        internal static Msg Get(LocalPlayer me)
        {
            if (!Has(me.Id)) return new Msg { ["party"] = false };
            long now = LocalServer.Now;
            var list = new List<object>();
            foreach (var m in members)
            {
                var p = LocalServer.Player(m.Pid);
                if (p == null) continue;
                var (online, room) = StateOf(m, now);
                list.Add(new Msg
                {
                    ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Pet ?? "", ["leader"] = p.Id == leader, ["online"] = online,
                    ["town"] = room?.Town ?? "", ["shard"] = room?.Shard ?? 0,
                });
            }
            return new Msg { ["party"] = true, ["leader"] = leader ?? "", ["you"] = me.Id, ["members"] = list, ["chat"] = chat.ConvertAll(line => (object)line) };
        }

        /// <summary>
        /// LocalAccounts.TownTicket: the room a member gets in a town. When another member is already in that town (the
        /// leader first) it's their room, whatever was asked, so friends land together; unless that room is full.
        /// </summary>
        internal static int Room(string pid, string town, int asked)
        {
            if (!Has(pid)) return asked;
            foreach (var m in members)
            {
                if (m.Pid == pid) continue;
                var room = LocalTowns.Where(m.Pid);
                if (room != null && room.Town == town && !room.Full) return room.Shard;
            }
            return asked;
        }

        /// <summary>A town ticket "&lt;pid&gt;|&lt;town&gt;:&lt;room&gt;" turned to the party's room in that town (see Room); others unchanged.</summary>
        internal static string Steer(string ticket)
        {
            int bar = ticket.LastIndexOf('|'), colon = ticket.LastIndexOf(':');
            if (bar <= 0 || colon < bar || !int.TryParse(ticket.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int asked)) return ticket;
            string pid = ticket.Substring(0, bar), town = ticket.Substring(bar + 1, colon - bar - 1);
            int shard = Room(pid, town, asked);
            return shard == asked ? ticket : $"{pid}|{town}:{shard}";
        }

        /// <summary>When a member last spoke to the party (ms since 1970, 0 never): one pace for all their links, whichever rooms they're in.</summary>
        internal static long SaidAt(string pid) => Find(pid)?.SaidAt ?? 0;

        /// <summary>
        /// LocalTown: a member's party chat line, already checked like "say". Every member in any town gets
        /// {t:'psay', from, name, text, n}, and it's kept for GET /api/party.
        /// </summary>
        internal static void Say(string pid, string name, string text)
        {
            var m = Find(pid);
            if (m != null) m.SaidAt = LocalServer.Now;
            int n = ++said;
            chat.Add(new Msg { ["n"] = n, ["from"] = pid, ["name"] = name, ["text"] = text, ["at"] = LocalServer.Now });
            if (chat.Count > ChatKept) chat.RemoveAt(0);
            LocalTowns.Tell(InParty, Json.Write(new Msg { ["t"] = "psay", ["from"] = pid, ["name"] = name, ["text"] = text, ["n"] = n }));
        }

        /// <summary>
        /// LocalTown: a member's party battle message ("pb": a ready check, its answers, a fight's steps). It reaches every
        /// other member in any town, marked with the sender's account id; anyone else's is ignored.
        /// </summary>
        internal static void Battle(string pid, Msg m)
        {
            if (!Has(pid)) return;
            m["id"] = pid;
            LocalTowns.Tell(to => to != pid && InParty(to), Json.Write(m));
        }

        // ---- inside ----

        static Member Find(string pid)
        {
            foreach (var m in members) if (m.Pid == pid) return m;
            return null;
        }

        static bool InParty(string pid) => Find(pid) != null;

        // where a member is and whether they count as online: the leader always (their game runs the world), others while
        // in town or for a while after their last request
        static (bool online, LocalTown room) StateOf(Member m, long now)
        {
            var room = LocalTowns.Where(m.Pid);
            return (m.Pid == leader || room != null || now - m.Seen < OnlineMs, room);
        }

        sealed class Member
        {
            public string Pid;
            public long Seen;       // their last request, or the last frame they were in town (ms since 1970)
            public long SaidAt;     // their last party chat line (ms since 1970)
            public bool Online;     // as members were last told
            public LocalTown Room;  // likewise; null when not in town
        }
    }
}
