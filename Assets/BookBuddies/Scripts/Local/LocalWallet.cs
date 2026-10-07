// Ports Server/src/wallet.js:1-224 (the ledgers, payload, GET /wallet, POST /wallet/earn and /wallet/spend, priceOf,
// metaPrice, stockPrice) and db.js:74-76 (etDay). Adds the offline purse's sessions: the coins found offline that
// Net/CoinBank banks into the online wallet when a session ends (POST /api/wallet/bank on the online Worker), and
// POST /wallet/carry, which hands a visitor's sessions in a hosted world to their own game to bank the same way.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BookBuddies.Economy;

namespace BookBuddies.Local
{
    /// <summary>
    /// One stretch of offline play: the finds it paid for per day, what it earned and spent, and once closed the total
    /// to bank online (Earned − Spent, never below 0 or above the balance then).
    /// </summary>
    public sealed class LocalSession
    {
        /// <summary>The session id the online wallet banks it under (32 hex digits).</summary>
        public string Id;
        /// <summary>When the first find opened it and when it was closed (ms since 1970).</summary>
        public long Started, Ended;
        /// <summary>Paid finds per day: "YYYY-MM-DD" -> {coin, bag, gift} counts.</summary>
        public Dictionary<string, Dictionary<string, int>> Days = new Dictionary<string, Dictionary<string, int>>();
        /// <summary>Coins its finds paid, coins spent offline meanwhile, and once closed what it banks.</summary>
        public int Earned, Spent, Total;
        /// <summary>Closed: play ended. Synced: banked online (or nothing to bank).</summary>
        public bool Closed, Synced;

        /// <summary>The body of POST /api/wallet/bank: {session, days, total}.</summary>
        public Dictionary<string, object> BankBody()
        {
            var days = new Dictionary<string, object>();
            foreach (var d in Days) days[d.Key] = d.Value.ToDictionary(k => k.Key, k => (object)(double)k.Value);
            return new Dictionary<string, object> { ["session"] = Id, ["days"] = days, ["total"] = (double)Total };
        }

        internal Dictionary<string, object> Write()
        {
            var o = BankBody();
            o["started"] = Started; o["ended"] = Ended; o["earned"] = Earned; o["spent"] = Spent; o["closed"] = Closed; o["synced"] = Synced;
            return o;
        }

        internal static LocalSession Read(Dictionary<string, object> o)
        {
            var s = new LocalSession
            {
                Id = o.Str("session"), Started = (long)o.Num("started"), Ended = (long)o.Num("ended"), Earned = o.Int("earned"),
                Spent = o.Int("spent"), Total = o.Int("total"), Closed = o.Truthy("closed"), Synced = o.Truthy("synced"),
            };
            var days = o.Obj("days");
            if (days != null)
                foreach (var d in days)
                    if (d.Value is Dictionary<string, object> n) s.Days[d.Key] = n.Keys.ToDictionary(k => k, k => n.Int(k));
            return s;
        }
    }

    /// <summary>
    /// The offline purse, kept like the Worker's wallet: every coin is a ledger row and a balance is their sum; the same
    /// (kind, ref) pays or charges once. Prices and rewards come from economy.json. Also the coin sessions banked online.
    /// </summary>
    public static class LocalWallet
    {
        const string ShortCoins = "Not enough coins";
        const int KeptSessions = 20; // banked sessions kept as history
        static readonly Regex RefRule = new Regex("^[A-Za-z0-9:_-]{6,60}\\z");
        static readonly Random Rng = new Random();
        static readonly string[] FindKinds = { "coin", "bag", "gift" };

        static EconomyData Econ => EconomyData.Current;

        /// <summary>Today in US Eastern time (YYYY-MM-DD), the server's day: coin caps and the daily gift turn over at its midnight.</summary>
        public static string Today() => EtDay(LocalServer.Now);

        // America/New_York by the US rule (since 2007), so it needs no time zone data: daylight time from 2:00 on the second
        // Sunday of March to 2:00 on the first Sunday of November
        static string EtDay(long ms)
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            var start = Sunday(utc.Year, 3, 2).AddHours(7);
            var end = Sunday(utc.Year, 11, 1).AddHours(6);
            return utc.AddHours(utc >= start && utc < end ? -4 : -5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        static DateTime Sunday(int year, int month, int nth)
        {
            var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            return first.AddDays((7 - (int)first.DayOfWeek) % 7 + 7 * (nth - 1));
        }

        // the day n days after a YYYY-MM-DD day (n < 0: before)
        static string ShiftDay(string day, int n) =>
            DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(n).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // ---- the ledgers ----

        /// <summary>A player's coins (0 for an unknown player).</summary>
        public static int Balance(string pid) => LocalServer.Player(pid) is LocalPlayer p ? Sum(p.Coins) : 0;

        /// <summary>Pays a whole positive amount once per (kind, ref), like wallet.js credit: true when it was paid now.</summary>
        public static bool Credit(string pid, string kind, string reference, int amount) =>
            LocalServer.Player(pid) is LocalPlayer p && Add(p.Coins, kind, reference, amount);

        /// <summary>How many coin rows of one kind today, and what they add up to. Daily caps count these.</summary>
        public static (int n, int s) TodayCount(string pid, string kind)
        {
            if (!(LocalServer.Player(pid) is LocalPlayer p)) return (0, 0);
            string today = Today();
            int n = 0, s = 0;
            foreach (var r in p.Coins) if (r.Kind == kind && r.Day == today) { n++; s += r.Amount; }
            return (n, s);
        }

        static int Sum(List<LedgerRow> rows) => rows.Sum(r => r.Amount);

        static bool Has(List<LedgerRow> rows, string kind, string reference) => rows.Exists(r => r.Kind == kind && r.Ref == reference);

        static bool Add(List<LedgerRow> rows, string kind, string reference, int amount)
        {
            if (amount <= 0 || Has(rows, kind, reference)) return false;
            Insert(rows, kind, reference, amount);
            return true;
        }

        static void Insert(List<LedgerRow> rows, string kind, string reference, int amount)
        {
            rows.Add(new LedgerRow { Kind = kind, Ref = reference, Amount = amount, Day = Today(), At = LocalServer.Now });
            LocalServer.Store.Touch();
        }

        // a debit that never goes below 0: 409 when this ref was paid for already, 402 when the balance doesn't cover it
        static (bool ok, string error, int status) Take(List<LedgerRow> rows, string kind, string reference, int amount, string shortMessage)
        {
            if (amount <= 0) return (false, "Bad amount", 400);
            if (Has(rows, kind, reference)) return (false, "Already paid for", 409);
            if (Sum(rows) < amount) return (false, shortMessage, 402);
            Insert(rows, kind, reference, -amount);
            return (true, null, 0);
        }

        static FairState Fair(LocalPlayer p)
        {
            if (p.Fair == null) { p.Fair = new FairState(); LocalServer.Store.Touch(); }
            return p.Fair;
        }

        // the latest daily gift: its day and how many days in a row it was
        static (string day, int run) LastGift(LocalPlayer p)
        {
            string last = null;
            foreach (var r in p.Coins) if (r.Kind == "gift" && (last == null || string.CompareOrdinal(r.Ref, last) > 0)) last = r.Ref;
            if (last == null) return ("", 0);
            var parts = last.Split(':');
            double run = parts.Length > 1 ? Js.ParseInt(parts[1]) : double.NaN;
            return (parts[0], double.IsNaN(run) ? 0 : (int)run);
        }

        // ---- the payload and the routes ----

        /// <summary>What every wallet reply carries: balances, today's gift and caps, the fair's counters, what's owned and the last 20 coin rows.</summary>
        internal static Dictionary<string, object> Payload(LocalPlayer p)
        {
            string today = Today(), yesterday = ShiftDay(today, -1);
            var fair = Fair(p);
            var (giftDay, run) = LastGift(p);
            var used = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var r in p.Coins) if (r.Day == today) used[r.Kind] = (used.TryGetValue(r.Kind, out var n) ? (int)n : 0) + 1;
            var recent = new List<object>();
            for (int i = p.Coins.Count - 1; i >= 0 && recent.Count < 20; i--)
            {
                var r = p.Coins[i];
                recent.Add(new Dictionary<string, object> { ["kind"] = r.Kind, ["amount"] = r.Amount, ["day"] = r.Day, ["created_at"] = r.At });
            }
            return new Dictionary<string, object>
            {
                ["balance"] = Sum(p.Coins), ["fair"] = Sum(p.Tickets), ["day"] = today, ["boost"] = 100,
                ["gift"] = new Dictionary<string, object> { ["claimed"] = giftDay == today, ["run"] = giftDay == today || giftDay == yesterday ? run : 0 },
                ["used"] = new Dictionary<string, object>(used),
                ["balls"] = fair.Balls, ["tix"] = fair.Tix, ["drops"] = fair.Drops, ["pity"] = fair.Pity, ["ups"] = fair.Ups, ["freecap"] = fair.FreeCap == today,
                ["owned"] = Owned(p, yesterday), ["recent"] = recent,
            };
        }

        // what was bought for keeps (keepsakes, class unlocks, library levels, moves) and the last two days' stock pieces
        static List<object> Owned(LocalPlayer p, string yesterday)
        {
            bool Keeps(LedgerRow r) => r.Kind == "shop" && (r.Ref.StartsWith("decor:", StringComparison.Ordinal) || r.Ref.StartsWith("unlock:", StringComparison.Ordinal) ||
                r.Ref.StartsWith("meta:", StringComparison.Ordinal) || r.Ref.StartsWith("move:", StringComparison.Ordinal) ||
                (r.Ref.StartsWith("stock:", StringComparison.Ordinal) && string.CompareOrdinal(r.Day, yesterday) >= 0));
            return p.Coins.Where(Keeps).Select(r => r.Ref).OrderBy(r => r, StringComparer.Ordinal).Cast<object>().ToList();
        }

        /// <summary>GET /wallet, POST /wallet/earn and POST /wallet/spend. Each answers with the payload plus what happened.</summary>
        internal static Dictionary<string, object> Route(string method, string path, Dictionary<string, object> body, LocalPlayer me)
        {
            if (path == "/wallet" && method == "GET")
            {
                Add(me.Coins, "starter", "once", Econ.Starter);
                return Payload(me);
            }
            if (path == "/wallet/carry" && method == "POST") return Carry(me, body);
            if (method != "POST" || (path != "/wallet/earn" && path != "/wallet/spend")) throw new LocalProblem("Not found", 404);
            Add(me.Coins, "starter", "once", Econ.Starter);
            var extra = path == "/wallet/earn" ? Earn(me, body) : Spend(me, body);
            var reply = Payload(me);
            foreach (var kv in extra) reply[kv.Key] = kv.Value;
            return reply;
        }

        // {kind: 'gift'}: the daily gift. Each day in a row pays the next step of the 7-day track, then it starts again.
        static Dictionary<string, object> Earn(LocalPlayer me, Dictionary<string, object> body)
        {
            var kind = Js.Get(body, "kind");
            if ((Js.Truthy(kind) ? Js.Str(kind) : "") != "gift") throw new LocalProblem("Unknown reward");
            string today = Today();
            var (day, last) = LastGift(me);
            if (day == today) throw new LocalProblem("Already claimed today", 409);
            int run = day == ShiftDay(today, -1) ? last + 1 : 1;
            int granted = Econ.LoginTrack[(run - 1) % Econ.LoginTrack.Length];
            if (!Add(me.Coins, "gift", today + ":" + run, granted)) throw new LocalProblem("Already claimed today", 409);
            if (run % 7 == 0) { Fair(me).FreeCap = today; LocalServer.Store.Touch(); } // day 7 also owes a free Book Fair capsule today
            return new Dictionary<string, object> { ["granted"] = granted, ["run"] = run };
        }

        // {kind: 'shop', item, ref}: a purchase priced here (any "amount" is ignored). {kind: 'fair', n, ref?}: coins for n Book Fair tickets.
        static Dictionary<string, object> Spend(LocalPlayer me, Dictionary<string, object> body)
        {
            var kindValue = Js.Get(body, "kind");
            string kind = Js.Truthy(kindValue) ? Js.Str(kindValue) : "shop";
            if (kind == "shop")
            {
                var itemValue = Js.Get(body, "item");
                string item = Js.Truthy(itemValue) ? Js.Str(itemValue) : "";
                int price = PriceOf(item, out bool once);
                string reference = once ? item : RefOf(body);
                if (!RefRule.IsMatch(reference) || (!once && !reference.StartsWith(item + ":", StringComparison.Ordinal))) throw new LocalProblem("Bad purchase");
                Paid(Take(me.Coins, "shop", reference, price, ShortCoins));
                NoteSpend(me, price);
                return new Dictionary<string, object> { ["paid"] = price };
            }
            if (kind == "fair")
            {
                double n = Js.ParseInt(Js.Get(body, "n"));
                var pack = Econ.TicketPacks.FirstOrDefault(k => k[0] == n);
                if (pack == null) throw new LocalProblem("Unknown ticket pack");
                string asked = RefOf(body);
                string reference = RefRule.IsMatch(asked) ? asked : LocalServer.Now + ":" + Rng.Next(1000000);
                var d = Take(me.Coins, "fairbuy", reference, pack[1], ShortCoins);
                if (d.status != 409) Paid(d); // already paid with this ref: make sure the tickets arrived
                Add(me.Tickets, "buy", reference, pack[0]);
                if (d.ok) NoteSpend(me, pack[1]);
                return new Dictionary<string, object> { ["paid"] = d.ok ? pack[1] : 0, ["got"] = pack[0] };
            }
            throw new LocalProblem("Unknown purchase");
        }

        static string RefOf(Dictionary<string, object> body)
        {
            var v = Js.Get(body, "ref");
            return Js.Truthy(v) ? Js.Str(v) : "";
        }

        static void Paid((bool ok, string error, int status) d)
        {
            if (!d.ok) throw new LocalProblem(d.error, d.status);
        }

        // ---- prices (wallet.js priceOf, metaPrice, stockPrice) ----

        /// <summary>
        /// A shop item's coin price, and whether it sells once per account: decor:&lt;id&gt; · move:&lt;cls&gt;:&lt;move&gt; ·
        /// unlock:&lt;cls&gt; · meta:&lt;upgrade&gt;:&lt;new level&gt; · stock:&lt;day&gt;:&lt;town&gt;:&lt;i&gt; · satchel:&lt;town&gt;.
        /// </summary>
        internal static int PriceOf(string item, out bool once)
        {
            var shop = Econ.Shop;
            var p = item.Split(':');
            bool At(string k, int n) => p[0] == k && p.Length == n;
            double price = 0;
            once = true;
            if (At("decor", 2)) price = shop.Obj("decor").Obj(p[1]).Num("p");
            else if (At("unlock", 2)) price = shop.Obj("unlock").Num(p[1]);
            else if (At("meta", 3)) price = MetaPrice(p[1], p[2]);
            else if (At("stock", 4)) price = StockPrice(p[1], p[2], p[3]);
            else if (At("move", 3)) { price = shop.Obj("move").Num(p[1] + ":" + p[2]); once = false; }
            else if (At("satchel", 2)) { price = shop.Num("satchel"); once = false; }
            if (!(price > 0)) throw new LocalProblem("Unknown purchase");
            return (int)price;
        }

        // a library upgrade's next level costs 80·lvl + 20·(lvl−1)², with lvl the level it becomes
        static double MetaPrice(string key, string level)
        {
            double lvl = Js.Number(level);
            foreach (var x in Econ.Shop.Arr("meta"))
                if (x is Dictionary<string, object> m && m.Str("k") == key)
                    return Js.IsInteger(lvl) && lvl >= 1 && lvl <= m.Num("max") ? 80 * lvl + 20 * (lvl - 1) * (lvl - 1) : 0;
            return 0;
        }

        // Today's satchel stock: 4 pieces a town a day, rolled from a stream seeded by "stock:<day>:<town>". Only the rarity
        // decides the price, so this re-rolls it and skips lootRoll's other draws. The day may be one either side of today.
        static double StockPrice(string day, string town, string index)
        {
            var stock = Econ.Shop.Obj("stock");
            double lv = stock.Obj("lv").Num(town), i = Js.Number(index);
            string today = Today();
            if (lv == 0 || !Js.IsInteger(i) || i < 0 || i > 3 || (day != ShiftDay(today, -1) && day != today && day != ShiftDay(today, 1))) return 0;
            double[] w = stock.Doubles("w"), m = stock.Doubles("m"), basePrice = stock.Doubles("base");
            var weights = w.Select((x, k) => x * m[k]).ToArray();
            double total = 0;
            foreach (double x in weights) total += x;
            uint a = Fnv("stock:" + day + ":" + town);
            for (int j = 0; ; j++)
            {
                double r = Mulberry(ref a) * total;
                int t = 0;
                while (t < 4 && r >= weights[t]) { r -= weights[t]; t++; }
                if (j == i) return Js.Round(basePrice[t] * (1 + Math.Max(1, Math.Min(60, 4 + lv * 2 + j)) / 40) / 5) * 5;
                for (int k = t >= 2 ? 4 : 3; k > 0; k--) Mulberry(ref a);
            }
        }

        // the site's ltHash (FNV-1a over the first UTF-16 unit of each code point) and ltRng (mulberry32), as in wallet.js
        static uint Fnv(string s)
        {
            uint x = 2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                x = unchecked((x ^ s[i]) * 16777619u);
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            }
            return x;
        }

        static double Mulberry(ref uint a)
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1 | a);
                t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        // ---- offline sessions, banked online by Net/CoinBank ----

        /// <summary>A town find just paid "coins" (LocalTown claims): counts it toward the open session, opening one if none is.</summary>
        public static void NoteFind(string pid, string k, int coins)
        {
            if (coins <= 0 || !(LocalServer.Player(pid) is LocalPlayer p)) return;
            var s = Open(p);
            if (s == null)
            {
                s = new LocalSession { Id = Guid.NewGuid().ToString("N"), Started = LocalServer.Now };
                p.Sessions.Add(s);
            }
            string today = Today();
            if (!s.Days.TryGetValue(today, out var finds)) s.Days[today] = finds = FindKinds.ToDictionary(x => x, x => 0);
            finds[Array.IndexOf(FindKinds, k) >= 0 ? k : "coin"]++; // other kinds pay as a coin, like town.js
            s.Earned += coins;
            LocalServer.Store.Touch();
        }

        /// <summary>Closes the open session: its Total is Earned − Spent, kept between 0 and the balance. Null when none is open.</summary>
        public static LocalSession CloseSession(string pid)
        {
            if (!(LocalServer.Player(pid) is LocalPlayer p) || !(Open(p) is LocalSession s)) return null;
            s.Closed = true;
            s.Ended = LocalServer.Now;
            s.Total = Math.Max(0, Math.Min(s.Earned - s.Spent, Sum(p.Coins)));
            s.Synced = s.Total == 0; // nothing to bank
            Prune(p);
            LocalServer.Store.Touch();
            return s;
        }

        /// <summary>The closed sessions still to bank online (Total above 0), oldest first.</summary>
        public static List<LocalSession> Unsynced(string pid) =>
            LocalServer.Player(pid) is LocalPlayer p ? p.Sessions.FindAll(s => s.Closed && !s.Synced && s.Total > 0) : new List<LocalSession>();

        /// <summary>
        /// The online wallet banked "banked" coins of a session: they leave the offline purse (one 'banked' row) and the
        /// session is done. A session carried home from a hosted world left the purse then: what wasn't banked comes back.
        /// </summary>
        public static void Banked(string pid, string sessionId, int banked)
        {
            if (!(LocalServer.Player(pid) is LocalPlayer p) || !(p.Sessions.Find(x => x.Id == sessionId) is LocalSession s) || s.Synced) return;
            if (p.Coins.Find(r => r.Kind == "carry" && r.Ref == sessionId) is LedgerRow carried) Add(p.Coins, "unbanked", sessionId, -carried.Amount - banked);
            else
            {
                int amount = Math.Min(banked, Sum(p.Coins)); // coins spent here since are gone already
                if (amount > 0 && !Has(p.Coins, "banked", sessionId)) Insert(p.Coins, "banked", sessionId, -amount);
            }
            s.Synced = true;
            Prune(p);
            LocalServer.Store.Touch();
        }

        /// <summary>
        /// POST /wallet/carry, from a visitor's game in a hosted world: first what their game banked online from sessions it
        /// carried before ({settled: [{session, banked}]}, what wasn't banked comes back to this purse), then their open
        /// session closes, and the reply is the payload plus every closed session still to bank ({sessions: [{session, days,
        /// total}]}), which their game takes home and banks with its own online sign-in, so coins found here aren't lost
        /// with the world. A session's coins leave this purse (one 'carry' row) the first time it goes, so they can't be
        /// spent here as well.
        /// </summary>
        static Dictionary<string, object> Carry(LocalPlayer me, Dictionary<string, object> body)
        {
            foreach (var o in body.Arr("settled"))
                if (o is Dictionary<string, object> d && me.Sessions.Find(x => x.Id == d.Str("session")) is LocalSession s && s.Closed)
                    Banked(me.Id, s.Id, Math.Max(0, Math.Min(d.Int("banked"), s.Total)));
            CloseSession(me.Id);
            var waiting = Unsynced(me.Id);
            foreach (var s in waiting)
            {
                int take = Math.Min(s.Total, Sum(me.Coins));
                if (take > 0 && !Has(me.Coins, "carry", s.Id)) Insert(me.Coins, "carry", s.Id, -take);
            }
            var reply = Payload(me);
            reply["sessions"] = waiting.Select(s => (object)s.BankBody()).ToList();
            return reply;
        }

        static LocalSession Open(LocalPlayer p) => p.Sessions.Count > 0 && !p.Sessions[p.Sessions.Count - 1].Closed ? p.Sessions[p.Sessions.Count - 1] : null;

        // offline shop and fair spends while a session is open come off what it banks
        static void NoteSpend(LocalPlayer p, int coins)
        {
            if (!(Open(p) is LocalSession s)) return;
            s.Spent += coins;
            LocalServer.Store.Touch();
        }

        // keeps the last few banked sessions as history and every one still to bank
        static void Prune(LocalPlayer p)
        {
            var banked = p.Sessions.FindAll(s => s.Synced);
            for (int i = 0; i < banked.Count - KeptSessions; i++) p.Sessions.Remove(banked[i]);
        }
    }
}
