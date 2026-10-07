// The offline server's tables as one JSON document: Server/src/db.js:27-52 (players, tokens, pets, coin_tx, fair_tx,
// econ_state, limits and meta) plus the offline coin sessions (LocalWallet). Each player's rows live under the player.
// Written atomically: a temp file, then a replace (Tales/Save/TalesSave.cs saves the same way).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BookBuddies.Local
{
    /// <summary>A reader on this PC (a players row) and everything the offline server keeps for them.</summary>
    public sealed class LocalPlayer
    {
        /// <summary>The account id (a UUID), the name, and the active pet's look JSON.</summary>
        public string Id, Name, Pet;
        /// <summary>Set by the online admin tools only, so offline these stay false and 0.</summary>
        public bool Admin;
        public long MuteUntil, BanUntil;
        /// <summary>The recovery code as stored: "BB" and 10 letters and digits, no dashes.</summary>
        public string Code;
        /// <summary>When the account was made, and when it last asked for a town ticket (ms since 1970).</summary>
        public long Created, LastSeen;
        /// <summary>The pets (pets.js), in the order they were hatched.</summary>
        public readonly List<LocalPet> Pets = new List<LocalPet>();
        /// <summary>The coin ledger (coin_tx), oldest first: the balance is always the sum.</summary>
        public readonly List<LedgerRow> Coins = new List<LedgerRow>();
        /// <summary>The Book Fair ticket ledger (fair_tx), oldest first.</summary>
        public readonly List<LedgerRow> Tickets = new List<LedgerRow>();
        /// <summary>The Book Fair counters (econ_state); null until the wallet first needs them.</summary>
        public FairState Fair;
        /// <summary>Stretches of offline play for coin banking, oldest first; at most the last one is open.</summary>
        public readonly List<LocalSession> Sessions = new List<LocalSession>();
    }

    /// <summary>One of a player's pets: its look is pet look JSON, and exactly one pet is active.</summary>
    public sealed class LocalPet
    {
        /// <summary>"p1", "p2"… or "p" and 10 hex digits; the pet's name; its look JSON.</summary>
        public string Id, Name, Look;
        public bool Active;
        /// <summary>When it hatched (ms since 1970): the list is oldest first.</summary>
        public long Born;
    }

    /// <summary>One ledger row: coins (or tickets) in, or out when negative. A (kind, ref) pair appears at most once.</summary>
    public sealed class LedgerRow
    {
        /// <summary>What it was for ("starter", "gift", "find", "shop", "fairbuy", "banked"…), its receipt, and its US Eastern day.</summary>
        public string Kind, Ref, Day;
        public int Amount;
        /// <summary>When it was written (ms since 1970).</summary>
        public long At;
    }

    /// <summary>The Book Fair counters (econ_state): pinballs, wheel tickets, prism drops, pity, fishing, upgrades, free capsule day.</summary>
    public sealed class FairState
    {
        public int Pity, Balls, Tix, Drops;
        public long FishAt;
        public Dictionary<string, object> Ups = new Dictionary<string, object>();
        public string FreeCap = "";
    }

    /// <summary>
    /// Everything the offline server keeps, as one versioned JSON document ({"v":1, players, tokens, meta}).
    /// Every change calls Touch, so a save knows there is something to write.
    /// </summary>
    public sealed class LocalStore
    {
        public const int Version = 1;

        /// <summary>The players by id.</summary>
        public readonly Dictionary<string, LocalPlayer> Players = new Dictionary<string, LocalPlayer>();
        /// <summary>Sign-ins: the SHA-256 (hex) of each token, to its player's id.</summary>
        public readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        /// <summary>Small server-wide values (today's wrong recovery codes).</summary>
        public readonly Dictionary<string, object> Meta = new Dictionary<string, object>();

        /// <summary>The file this store was read from and saves to (null: memory only).</summary>
        public string Source { get; private set; }

        /// <summary>Counts every change since the store was loaded.</summary>
        public long Changes { get; private set; }

        // the Changes count of the newest snapshot on disk (written from the save thread)
        internal long Written;
        // the file was there but couldn't be read (locked, no access): this store must never be written over it
        internal bool ReadFailed;

        /// <summary>Marks the store changed.</summary>
        public void Touch() => Changes++;

        /// <summary>
        /// Reads the save. A missing file gives an empty store; one that isn't a version 1 save is put aside as .bad and
        /// gives an empty store; one that can't be opened (locked) gives an empty store that never saves over it.
        /// </summary>
        public static LocalStore Load(string path)
        {
            var store = ReadFile(path);
            store.Source = path;
            return store;
        }

        static LocalStore ReadFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return new LocalStore();
            // a crash between delete and move leaves only the finished temp file
            string from = File.Exists(path) ? path : File.Exists(path + ".tmp") ? path + ".tmp" : null;
            if (from == null) return new LocalStore();
            string text;
            try { text = File.ReadAllText(from); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return new LocalStore { ReadFailed = true }; }
            try { return Read(Json.ParseObject(text)); }
            catch (Exception) { PutAside(from, path + ".bad"); return new LocalStore(); }
        }

        /// <summary>The whole store as JSON text.</summary>
        public string ToText()
        {
            var players = new Dictionary<string, object>();
            foreach (var p in Players.Values) players[p.Id] = Write(p);
            var tokens = new Dictionary<string, object>();
            foreach (var t in Tokens) tokens[t.Key] = t.Value;
            return Json.Write(new Dictionary<string, object> { ["v"] = Version, ["players"] = players, ["tokens"] = tokens, ["meta"] = Meta });
        }

        /// <summary>Writes "text" to a temp file, flushes it to disk, then swaps it in for the save.</summary>
        public static void WriteAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception e) when (e is IOException || e is PlatformNotSupportedException)
            {
                File.Delete(path);
                File.Move(tmp, path);
            }
        }

        static void PutAside(string from, string bad)
        {
            try
            {
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(from, bad);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        // ---- reading ----

        static LocalStore Read(Dictionary<string, object> o)
        {
            if (o == null || o.Int("v") != Version) throw new FormatException("Not a version " + Version + " offline save");
            var s = new LocalStore();
            var players = o.Obj("players");
            if (players != null)
                foreach (var kv in players)
                    if (kv.Value is Dictionary<string, object> p) s.Players[kv.Key] = ReadPlayer(kv.Key, p);
            var tokens = o.Obj("tokens");
            if (tokens != null)
                foreach (var kv in tokens)
                    if (kv.Value is string pid && s.Players.ContainsKey(pid)) s.Tokens[kv.Key] = pid;
            var meta = o.Obj("meta");
            if (meta != null) foreach (var kv in meta) s.Meta[kv.Key] = kv.Value;
            return s;
        }

        static LocalPlayer ReadPlayer(string id, Dictionary<string, object> o)
        {
            var p = new LocalPlayer
            {
                Id = id, Name = o.Str("name"), Pet = o.Str("pet"), Code = o.Str("code"), Admin = o.Truthy("admin"),
                MuteUntil = (long)o.Num("mute"), BanUntil = (long)o.Num("ban"), Created = (long)o.Num("created"), LastSeen = (long)o.Num("seen"),
            };
            foreach (var x in o.Arr("pets"))
                if (x is Dictionary<string, object> q)
                    p.Pets.Add(new LocalPet { Id = q.Str("id"), Name = q.Str("name"), Look = q.Str("look"), Active = q.Truthy("active"), Born = (long)q.Num("born") });
            ReadRows(o.Arr("coins"), p.Coins);
            ReadRows(o.Arr("tickets"), p.Tickets);
            var f = o.Obj("fair");
            if (f != null)
                p.Fair = new FairState
                {
                    Pity = f.Int("pity"), Balls = f.Int("balls"), Tix = f.Int("tix"), Drops = f.Int("drops"), FishAt = (long)f.Num("fish_at"),
                    Ups = f.Obj("ups") ?? new Dictionary<string, object>(), FreeCap = f.Str("freecap"),
                };
            foreach (var x in o.Arr("sessions"))
                if (x is Dictionary<string, object> q) p.Sessions.Add(LocalSession.Read(q));
            return p;
        }

        // rows are [kind, ref, amount, day, at] to keep a long ledger small
        static void ReadRows(List<object> rows, List<LedgerRow> into)
        {
            foreach (var x in rows)
                if (x is List<object> r && r.Count == 5)
                    into.Add(new LedgerRow { Kind = r[0] as string ?? "", Ref = r[1] as string ?? "", Amount = (int)(double)r[2], Day = r[3] as string ?? "", At = (long)(double)r[4] });
        }

        // ---- writing ----

        static Dictionary<string, object> Write(LocalPlayer p)
        {
            var o = new Dictionary<string, object>
            {
                ["name"] = p.Name, ["pet"] = p.Pet, ["code"] = p.Code, ["admin"] = p.Admin,
                ["mute"] = p.MuteUntil, ["ban"] = p.BanUntil, ["created"] = p.Created, ["seen"] = p.LastSeen,
                ["pets"] = p.Pets.ConvertAll(q => (object)new Dictionary<string, object>
                {
                    ["id"] = q.Id, ["name"] = q.Name, ["look"] = q.Look, ["active"] = q.Active, ["born"] = q.Born,
                }),
                ["coins"] = WriteRows(p.Coins),
                ["tickets"] = WriteRows(p.Tickets),
                ["sessions"] = p.Sessions.ConvertAll(s => (object)s.Write()),
            };
            if (p.Fair != null)
                o["fair"] = new Dictionary<string, object>
                {
                    ["pity"] = p.Fair.Pity, ["balls"] = p.Fair.Balls, ["tix"] = p.Fair.Tix, ["drops"] = p.Fair.Drops,
                    ["fish_at"] = p.Fair.FishAt, ["ups"] = p.Fair.Ups, ["freecap"] = p.Fair.FreeCap,
                };
            return o;
        }

        static List<object> WriteRows(List<LedgerRow> rows) =>
            rows.ConvertAll(r => (object)new List<object> { r.Kind, r.Ref, r.Amount, r.Day, r.At });
    }
}
