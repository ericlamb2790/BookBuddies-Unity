// Ports Server/src/town.js:15-314 (TownRoom: joining, leaving, the 3-second turn, villagers, finds and every player
// message) for offline play, plus LocalBots' wandering readers. The admin tools (POST /admin) and shared Tales
// (tales_room.js) stay online. The messages are town.js's JSON, so Live/PlazaWorld can't tell this room from the Worker's.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BookBuddies.Economy;
using Msg = System.Collections.Generic.Dictionary<string, object>;

namespace BookBuddies.Local
{
    /// <summary>
    /// One live room ("&lt;town&gt;:&lt;shard&gt;") running inside the game: the players' pets, Pawtopia's villagers, the
    /// offline readers, and the coins, bags and gifts that turn up in every town (paid into the offline purse). Players
    /// reach it through LocalLink; LocalTowns.Tick runs its turns while someone is in it.
    /// </summary>
    public sealed class LocalTown
    {
        const int Cap = 300;
        const double TickMs = 3000;
        const string Unkind = "Please keep it kind. That word or phrase isn’t allowed on BookBuddies.";
        static readonly string[] Emotes = { "❤️", "😂", "👋", "🎉", "😮", "📚", "✨", "💤" };
        static readonly string[] Acts = { "hop", "spin", "wave", "dance", "nap", "sip", "read", "hug", "boop", "play", "snack", "highpaw" };
        static readonly string[] FolkActs = { "hop", "spin", "dance", "wave" };
        static readonly Regex TaleId = new Regex("^[A-Za-z0-9_-]{8}\\z");
        static readonly Random Rng = new Random();

        // each place's size in tiles; finds: coins and gifts turn up there (every town); life: villagers wander there (Pawtopia)
        static readonly Dictionary<string, (int w, int h, bool life, bool finds)> Places = MakePlaces();

        // spots the villagers walk between: [x, y, 1 if it's a seat]. Same as the website's Pawtopia.
        static readonly int[][] Spots =
        {
            new[] { 35, 30, 0 }, new[] { 44, 31, 0 }, new[] { 39, 37, 0 }, new[] { 32, 28, 1 }, new[] { 33, 28, 1 }, new[] { 46, 28, 1 }, new[] { 47, 28, 1 },
            new[] { 32, 35, 1 }, new[] { 46, 35, 1 }, new[] { 43, 51, 1 }, new[] { 45, 51, 1 }, new[] { 47, 51, 1 }, new[] { 49, 51, 1 }, new[] { 51, 51, 1 },
            new[] { 53, 51, 1 }, new[] { 63, 47, 1 }, new[] { 69, 47, 1 }, new[] { 63, 50, 1 }, new[] { 69, 50, 1 }, new[] { 66, 45, 1 }, new[] { 66, 52, 1 },
            new[] { 73, 45, 1 }, new[] { 14, 30, 0 }, new[] { 20, 29, 0 }, new[] { 11, 36, 0 }, new[] { 14, 19, 0 }, new[] { 60, 19, 0 }, new[] { 35, 57, 0 },
            new[] { 45, 57, 0 }, new[] { 24, 57, 1 }, new[] { 25, 57, 1 }, new[] { 16, 50, 0 }, new[] { 71, 36, 0 }, new[] { 33, 15, 0 }, new[] { 48, 40, 0 },
            new[] { 28, 40, 0 },
        };

        static readonly (string name, string look)[] Villagers =
        {
            ("Mayor Biscuit", "{\"h\":32,\"s\":3,\"o\":{},\"sh\":\"round\",\"z\":1,\"f\":\"cute\",\"e\":\"bear\",\"pt\":\"belly\",\"bg\":\"meadow\"}"),
            ("Professor Inkwell", "{\"h\":262,\"s\":3,\"o\":{},\"sh\":\"tall\",\"z\":1,\"f\":\"cool\",\"e\":\"cat\",\"pt\":\"none\",\"bg\":\"library\"}"),
            ("Pip the Postpet", "{\"h\":200,\"s\":2,\"o\":{},\"sh\":\"bean\",\"z\":0,\"f\":\"goofy\",\"e\":\"bunny\",\"pt\":\"spots\",\"bg\":\"meadow\"}"),
            ("Marigold", "{\"h\":48,\"s\":3,\"o\":{},\"sh\":\"mochi\",\"z\":1,\"f\":\"cute\",\"e\":\"mouse\",\"pt\":\"stripes\",\"bg\":\"meadow\"}"),
            ("Barista Beans", "{\"h\":18,\"s\":3,\"o\":{},\"sh\":\"pear\",\"z\":1,\"f\":\"cute\",\"e\":\"bear\",\"pt\":\"belly\",\"bg\":\"meadow\"}"),
            ("Captain Pages", "{\"h\":190,\"s\":3,\"o\":{},\"sh\":\"dino\",\"z\":2,\"f\":\"derp\",\"e\":\"none\",\"pt\":\"spots\",\"bg\":\"beach\"}"),
        };

        static readonly string[] VillagerLines =
        {
            "Anyone reading something good? 📖", "The café has fresh cocoa today ☕", "I found a coin by the pond earlier! 🪙", "Who wants to start a Tale with me?",
            "The garden looks lovely today 🌷", "Just one more chapter… 🌙", "Have you tried the Prism Pool? 🌈", "I love a cozy reading corner", "Spoilers are against town rules 🤫",
            "The Wheel of Wonder is spinning! 🎡", "Hello neighbor! 👋", "The beach is perfect for reading ⛱️",
        };

        readonly List<Player> players = new List<Player>();          // in the order they joined, like the room's Map
        readonly Dictionary<string, (int x, int y)> lastSpot = new Dictionary<string, (int x, int y)>(); // player id -> where they left
        readonly List<Folk> folk = new List<Folk>();                  // villagers and readers
        readonly List<Item> items = new List<Item>();
        readonly (int w, int h, bool life, bool finds) place;
        int seq;
        double tickAt;    // when the next turn is due; 0 while the room sleeps
        double idleSince; // when the last player left

        /// <summary>Which place the room is for ("pawtopia", a genre town, "road1"… or "caves").</summary>
        public string Town { get; }

        /// <summary>Which of the place's rooms it is, 1 to LocalServer.Rooms.</summary>
        public int Shard { get; }

        /// <summary>How many players are in the room (villagers and readers aren't counted).</summary>
        public int PlayerCount => players.Count;

        /// <summary>No room for another player.</summary>
        internal bool Full => players.Count >= Cap;

        internal LocalTown(string name, double now)
        {
            string[] parts = name.Split(':');
            string town = parts[0];
            Town = Places.ContainsKey(town) ? town : "pawtopia";
            Shard = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int shard) && shard > 0 ? shard : 1;
            place = Places[Town];
            idleSince = now;
        }

        static Dictionary<string, (int w, int h, bool life, bool finds)> MakePlaces()
        {
            var places = new Dictionary<string, (int w, int h, bool life, bool finds)> { ["pawtopia"] = (80, 64, true, true), ["caves"] = (56, 40, false, false) };
            for (int i = 0; i < LocalServer.GenreTowns.Length; i++)
            {
                places[LocalServer.GenreTowns[i]] = (56, 44, false, true);
                places["road" + (i + 1)] = (112, 52, false, false);
            }
            return places;
        }

        int ClampX(object v) => Math.Max(0, Math.Min(place.w - 1, ToInt32(v)));
        int ClampY(object v) => Math.Max(0, Math.Min(place.h - 1, ToInt32(v)));

        // ---- joining ----

        /// <summary>
        /// A player's link joins (town.js fetch): their connection id, or null when the room won't have them (an unknown
        /// player or a full room; a player on a break gets "kicked" and the link closes with 4003).
        /// </summary>
        internal string Join(LocalLink link, string pid)
        {
            var me = LocalServer.Player(pid);
            if (me == null) return null;
            foreach (var other in players.FindAll(o => o.Pid == pid)) DropOldConnection(other);
            if (players.Count >= Cap) return null;

            double now = LocalServer.Now;
            if (me.BanUntil > now)
            {
                link.Deliver(Json.Write(new Msg { ["t"] = "kicked", ["msg"] = "An admin asked you to take a short break from town." }));
                link.Drop(4003);
                return null;
            }

            if (tickAt <= 0) WakeUp(now);
            var last = lastSpot.TryGetValue(pid, out var spot) ? spot : (x: -1, y: -1);
            var pl = new Player
            {
                Id = Base36(++seq), Pid = pid, Link = link, Name = Cut(me.Name ?? "", 20), X = last.x, Y = last.y, Tx = last.x, Ty = last.y,
                Admin = me.Admin ? 1 : 0, Mute = me.MuteUntil,
            };
            players.Add(pl);
            Send(pl, Welcome(pl, now));
            return pl.Id;
        }

        Msg Welcome(Player pl, double now)
        {
            var roster = new List<object>();
            foreach (var o in players) if (o != pl && o.Look.Length > 0) roster.Add(Info(o, pl.Admin == 1));
            foreach (var v in folk)
            {
                var bot = Info(v, false);
                bot["b"] = 1;
                roster.Add(bot);
            }
            return new Msg
            {
                ["t"] = "w", ["id"] = pl.Id, ["now"] = now, ["cap"] = Cap, ["town"] = Town, ["adm"] = pl.Admin, ["theme"] = "", ["th"] = "",
                ["roster"] = roster, ["items"] = items.ConvertAll(it => (object)it.Write()), ["garden"] = new List<object>(), ["notes"] = new List<object>(),
                ["me"] = new Msg { ["x"] = pl.X, ["y"] = pl.Y },
            };
        }

        void DropOldConnection(Player other)
        {
            other.Link.Drop(4000);
            players.Remove(other);
            Broadcast(new Msg { ["t"] = "bye", ["id"] = other.Id });
        }

        /// <summary>A player's link closed (town.js leave): the others see them go, and the room sleeps once it's empty.</summary>
        internal void Leave(string id)
        {
            var pl = players.Find(p => p.Id == id);
            if (pl == null) return;
            players.Remove(pl);
            lastSpot[pl.Pid] = (pl.Tx, pl.Ty);
            Broadcast(new Msg { ["t"] = "bye", ["id"] = pl.Id });
            if (players.Count > 0) return;
            tickAt = 0;
            folk.Clear();
            idleSince = LocalServer.Now;
        }

        /// <summary>Whether this player has a link in the room.</summary>
        internal bool Has(string pid)
        {
            foreach (var p in players) if (p.Pid == pid) return true;
            return false;
        }

        /// <summary>Sends a message (JSON text) to the players here that "to" picks by account id: the party's chat and nudges.</summary>
        internal void Tell(Predicate<string> to, string json)
        {
            foreach (var p in players) if (to(p.Pid)) p.Link.Deliver(json);
        }

        /// <summary>How long the room has been empty, in ms (0 while someone is in it).</summary>
        internal double IdleFor(double now) => players.Count > 0 ? 0 : now - idleSince;

        // The room only ticks while someone is in it, and only in towns (finds); only Pawtopia has villagers, every town has readers.
        void WakeUp(double now)
        {
            folk.Clear();
            if (!place.finds) return;
            if (place.life)
                for (int i = 0; i < Villagers.Length; i++)
                {
                    var at = Spots[(i * 7 + Rng.Next(Spots.Length)) % Spots.Length];
                    folk.Add(new Folk
                    {
                        Id = "b" + i, Name = Villagers[i].name, Look = Villagers[i].look, X = at[0], Y = at[1], Tx = at[0], Ty = at[1],
                        Points = Spots, Lines = VillagerLines,
                    });
                }
            foreach (var (name, look, at) in LocalBots.Arrive(Town, Rng))
                folk.Add(new Folk
                {
                    Id = "b" + folk.Count, Name = name, Look = look, X = at[0], Y = at[1], Tx = at[0], Ty = at[1], Sit = at[2],
                    Points = LocalBots.Points(Town), Lines = LocalBots.Lines,
                });
            foreach (var v in folk) v.Next = now + 2000 + Rng.NextDouble() * 8000;
            for (int k = 0; k < 10; k++) DropItem(now);   // a few finds are waiting when the first pet arrives
            tickAt = now + TickMs;
        }

        // ---- every few seconds: villagers and readers wander and chat, finds appear and expire ----

        /// <summary>Runs the room's turn when it's due (every 3 s while someone is in it).</summary>
        internal void Tick(double now)
        {
            if (tickAt <= 0 || now < tickAt) return;
            tickAt = now + TickMs;
            foreach (var v in folk) Turn(v, now);
            foreach (var it in items.FindAll(i => now > i.Exp))
            {
                items.Remove(it);
                Broadcast(new Msg { ["t"] = "ix", ["id"] = it.Id });
            }
            int want = Math.Min(30, 10 + players.Count * 2);
            for (int k = 0; k < 2 && items.Count < want; k++) DropItem(now);
        }

        void Turn(Folk v, double now)
        {
            if (v.SitAt > 0 && now >= v.SitAt)
            {
                v.SitAt = 0;
                v.Sit = 1;
                Broadcast(new Msg { ["t"] = "sit", ["id"] = v.Id, ["on"] = 1 });
            }
            if (now < v.Next) return;
            v.Next = now + 7000 + Rng.NextDouble() * 14000;
            double r = Rng.NextDouble();
            if (r < .55)
            {
                var to = Pick(v.Points);
                if ((to[0] == v.Tx && to[1] == v.Ty) || folk.Exists(o => o != v && o.Tx == to[0] && o.Ty == to[1])) return;
                v.X = v.Tx; v.Y = v.Ty; v.Tx = to[0]; v.Ty = to[1]; v.Sit = 0;
                double dx = v.Tx - v.X, dy = v.Ty - v.Y;
                v.SitAt = to[2] == 1 ? now + Math.Sqrt(dx * dx + dy * dy) / 3.2 * 1000 + 900 : 0;
                Broadcast(Go(v, now));
            }
            else if (r < .72) Broadcast(new Msg { ["t"] = "emo", ["id"] = v.Id, ["e"] = Pick(Emotes) });
            else if (r < .84) Broadcast(new Msg { ["t"] = "say", ["id"] = v.Id, ["text"] = Pick(v.Lines) });
            else Broadcast(new Msg { ["t"] = "act", ["id"] = v.Id, ["a"] = Pick(FolkActs), ["to"] = "" });
        }

        void DropItem(double now)
        {
            double r = Rng.NextDouble();
            var it = new Item
            {
                Id = "i" + Base36(++seq), K = r < .7 ? "coin" : r < .93 ? "bag" : "gift",
                X = 3 + Rng.Next(place.w - 6), Y = 4 + Rng.Next(place.h - 10), Exp = now + (8 + Rng.NextDouble() * 6) * 60e3,
            };
            items.Add(it);
            Broadcast(new Msg { ["t"] = "item", ["it"] = it.Write() });
        }

        // ---- messages from players ----

        /// <summary>One message (JSON text) from a player's link (town.js onMessage).</summary>
        internal void Receive(string id, string raw)
        {
            var pl = players.Find(p => p.Id == id);
            if (pl == null || raw == null || raw.Length > 4000) return;
            object parsed;
            try { parsed = Json.Parse(raw); }
            catch (Exception) { return; } // not JSON: ignored, like the Worker's JSON.parse failing
            if (!(parsed is Msg m)) return;
            double now = LocalServer.Now;
            string t = m.Str("t");

            if (t == "ping") { Send(pl, new Msg { ["t"] = "pong", ["c"] = Js.Get(m, "c") is double c ? c : 0, ["now"] = now }); return; }
            if (t == "leave") { Broadcast(new Msg { ["t"] = "leave", ["id"] = pl.Id }, pl); return; }
            if (t == "hi") { Hello(pl, m); return; }
            if (pl.Look.Length == 0) return;

            pl.Burst.RemoveAll(at => now - at >= 10e3);   // at most 40 messages in 10 seconds
            if (pl.Burst.Count > 40) return;
            pl.Burst.Add(now);

            switch (t)
            {
                case "go": Walk(pl, m, now); break;
                case "sit":
                    pl.Sit = m.Truthy("on") ? 1 : 0;
                    Broadcast(new Msg { ["t"] = "sit", ["id"] = pl.Id, ["on"] = pl.Sit, ["f"] = m.Truthy("f") ? 1 : 0 }, pl);
                    break;
                case "emo":
                    if (Array.IndexOf(Emotes, Js.Get(m, "e") as string) >= 0 && !IsMuted(pl, now))
                        Broadcast(new Msg { ["t"] = "emo", ["id"] = pl.Id, ["e"] = m["e"] }, pl);
                    break;
                case "act": Act(pl, m, now); break;
                case "say": Say(pl, m, now, false); break;
                case "psay": Say(pl, m, now, true); break;
                case "claim": Claim(pl, m); break;
                case "tale": Invite(pl, m, now); break;
            }
        }

        void Hello(Player pl, Msg m)
        {
            bool first = pl.Look.Length == 0;
            pl.Look = Cut(Field(m, "look"), 1500);
            pl.X = pl.Tx = ClampX(Js.Get(m, "x"));
            pl.Y = pl.Ty = ClampY(Js.Get(m, "y"));
            if (!first) { Broadcast(new Msg { ["t"] = "look", ["id"] = pl.Id, ["look"] = pl.Look }, pl); return; }
            foreach (var p in players) if (p != pl) Send(p, new Msg { ["t"] = "join", ["p"] = Info(pl, p.Admin == 1) });
        }

        void Walk(Player pl, Msg m, double now)
        {
            pl.X = Tenth(Js.Get(m, "x"), place.w);
            pl.Y = Tenth(Js.Get(m, "y"), place.h);
            pl.Tx = ClampX(Js.Get(m, "tx"));
            pl.Ty = ClampY(Js.Get(m, "ty"));
            pl.Sit = 0;
            Broadcast(Go(pl, now), pl);
        }

        void Act(Player pl, Msg m, double now)
        {
            if (!(Js.Get(m, "a") is string a) || Array.IndexOf(Acts, a) < 0) return;
            string to = Js.Get(m, "to") is string s && s.Length < 13 ? s : "";
            if (to.Length > 0)   // hugs and boops: one every 1.5 seconds
            {
                if (IsMuted(pl, now) || now - pl.CuteAt < 1500) return;
                pl.CuteAt = now;
            }
            Broadcast(new Msg { ["t"] = "act", ["id"] = pl.Id, ["a"] = a, ["to"] = to }, pl);
        }

        // "say" is heard in this room; "psay" is party chat (LocalParty), heard by the party in every town and ignored from
        // anyone else. Party chat keeps one pace per member, so links in several rooms can't talk faster together.
        void Say(Player pl, Msg m, double now, bool party)
        {
            if (party && !LocalParty.Has(pl.Pid)) return;
            if (IsMuted(pl, now)) return;
            double last = party ? Math.Max(pl.SaidAt, LocalParty.SaidAt(pl.Pid)) : pl.SaidAt;
            if (now - last < 1200) { Error(pl, "Slow down a little 🐢"); return; }
            string text = LocalSafety.CleanText(Js.Get(m, "text"), 140);
            if (text.Length == 0) return;
            if (LocalSafety.TextProblem(text) != null) { Error(pl, Unkind); return; }
            if (LocalSafety.PersonalDetails(text)) { Error(pl, "Keep phone numbers and e-mail addresses private 💛"); return; }
            pl.SaidAt = now;
            if (party) LocalParty.Say(pl.Pid, pl.Name, text);
            else Broadcast(new Msg { ["t"] = "say", ["id"] = pl.Id, ["text"] = text });
        }

        // Whoever reaches a coin, bag or gift first keeps it: paid into the offline purse (and the open coin session), up
        // to the day's finds cap. "bal" is the new balance.
        void Claim(Player pl, Msg m)
        {
            string id = Field(m, "id");
            var it = items.Find(i => i.Id == id);
            if (it == null || Math.Abs(pl.Tx - it.X) > 1.5 || Math.Abs(pl.Ty - it.Y) > 1.5) return;
            items.Remove(it);
            Broadcast(new Msg { ["t"] = "ix", ["id"] = it.Id, ["by"] = pl.Id });
            int coins = 0;
            if (LocalWallet.TodayCount(pl.Pid, "find").n < EconomyData.Current.FindsPerDay)
            {
                coins = FindPays(it.K);
                if (LocalWallet.Credit(pl.Pid, "find", FormattableString.Invariant($"{LocalWallet.Today()}:{it.Id}:{(long)Math.Floor(it.Exp)}"), coins))
                    LocalWallet.NoteFind(pl.Pid, it.K, coins);
                else coins = 0;
            }
            Send(pl, new Msg { ["t"] = "got", ["k"] = it.K, ["coins"] = coins, ["bal"] = LocalWallet.Balance(pl.Pid) });
        }

        // finds.coins[k] || finds.coins.coin
        static int FindPays(string k)
        {
            var pays = EconomyData.Current.FindCoins;
            return pays.TryGetValue(k, out int coins) && coins > 0 ? coins : pays.TryGetValue("coin", out coins) ? coins : 0;
        }

        // "Invite to my tale": {t:'tale', to: connection id, id, title} reaches only that player as {t:'tale', from, id, title}; one every 10 s.
        void Invite(Player pl, Msg m, double now)
        {
            string toId = Field(m, "to"), id = Field(m, "id"), title = LocalSafety.CleanText(Js.Get(m, "title"), 60);
            var to = players.Find(p => p.Id == toId);
            if (to == null || to == pl || !TaleId.IsMatch(id) || now - pl.TaleAt < 10e3 || IsMuted(pl, now)) return;
            if (LocalSafety.TextProblem(title) != null) { Error(pl, Unkind); return; }
            pl.TaleAt = now;
            Send(to, new Msg { ["t"] = "tale", ["from"] = pl.Name, ["id"] = id, ["title"] = title });
        }

        bool IsMuted(Player pl, double now)
        {
            if (pl.Mute <= now) return false;
            long mins = (long)Math.Ceiling((pl.Mute - now) / 60e3);
            Error(pl, $"An admin paused your chat for {(mins < 60 ? mins + " more min" : (long)Math.Ceiling(mins / 60.0) + " more hours")}.");
            return true;
        }

        // ---- sending ----

        static void Send(Player pl, Msg o) => pl.Link.Deliver(Json.Write(o));
        static void Error(Player pl, string msg) => Send(pl, new Msg { ["t"] = "err", ["msg"] = msg });

        void Broadcast(Msg o, Player skip = null)
        {
            string text = Json.Write(o);
            foreach (var p in players) if (p != skip) p.Link.Deliver(text);
        }

        static Msg Go(Pet p, double now) => new Msg { ["t"] = "go", ["id"] = p.Id, ["x"] = p.X, ["y"] = p.Y, ["tx"] = p.Tx, ["ty"] = p.Ty, ["at"] = now };

        // What others see of a pet. Only admins get the account id (u), so the town card's admin tools reach the right account.
        static Msg Info(Pet o, bool forAdmin) => new Msg
        {
            ["id"] = o.Id, ["n"] = o.Name, ["u"] = forAdmin && o is Player p && !string.IsNullOrEmpty(p.Pid) ? p.Pid : "", ["c"] = "", ["look"] = o.Look,
            ["x"] = o.X, ["y"] = o.Y, ["tx"] = o.Tx, ["ty"] = o.Ty, ["sit"] = o.Sit,
        };

        // ---- JavaScript's conversions ----

        static T Pick<T>(T[] list) => list[Rng.Next(list.Length)];

        // String(m[key] || '')
        static string Field(Msg m, string key) => m.Truthy(key) ? Js.Str(m[key]) : "";

        static string Cut(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

        // +v
        static double ToNumber(object v) => v switch
        {
            null => 0, // null is 0; a missing key (undefined) is NaN, which every use here turns into 0 as well
            double d => d,
            bool b => b ? 1 : 0,
            string s => Js.Number(s),
            List<object> a => Js.Number(Js.Str(a)),
            _ => double.NaN,
        };

        // v | 0
        static int ToInt32(object v)
        {
            double d = ToNumber(v);
            return double.IsNaN(d) || double.IsInfinity(d) ? 0 : unchecked((int)(long)(Math.Truncate(d) % 4294967296.0));
        }

        // a position to a tenth of a tile, inside the map
        static double Tenth(object v, int max)
        {
            double d = ToNumber(v);
            if (double.IsNaN(d) || d == 0) d = 0; // +v || 0
            return Math.Max(0, Math.Min(max - 1, Js.Round(d * 10) / 10));
        }

        // (n).toString(36)
        static string Base36(int n)
        {
            const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            string s = "";
            do { s = Digits[n % 36] + s; n /= 36; } while (n > 0);
            return s;
        }

        // ---- who's in the room ----

        // a pet in the room: a player's, or a villager's or reader's
        abstract class Pet
        {
            public string Id, Name, Look = "";
            public double X, Y;
            public int Tx, Ty, Sit;
        }

        sealed class Player : Pet
        {
            public string Pid;
            public LocalLink Link;
            public int Admin;
            public double Mute, SaidAt, CuteAt, TaleAt;
            public readonly List<double> Burst = new List<double>();
        }

        sealed class Folk : Pet
        {
            public double Next, SitAt;
            public int[][] Points; // where they walk: [x, y, 1 if it's a seat]
            public string[] Lines;
        }

        sealed class Item
        {
            public string Id, K;
            public int X, Y;
            public double Exp;
            public Msg Write() => new Msg { ["id"] = Id, ["k"] = K, ["x"] = X, ["y"] = Y, ["exp"] = Exp };
        }
    }
}
