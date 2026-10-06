using System;
using System.Collections.Generic;
using System.IO;

namespace BookBuddies.Tales
{
    /// <summary>Your buddy's Tales profile, like the site's pg().quest.pets[key].</summary>
    public sealed class PetProfile
    {
        public string Cls;                                   // chosen class; null = natural class from personality
        public double Rxp;                                   // renown XP (Pet Lv = renown level + 1)
        public readonly Dictionary<string, List<string>> Own = new Dictionary<string, List<string>>(); // bought moves per class
        public readonly Dictionary<string, List<string>> Kit = new Dictionary<string, List<string>>(); // equipped moves per class
        public readonly Dictionary<string, double> Life = new Dictionary<string, double>();         // tales, wins, bosses, foes, best, n20, helpers, falls
        public readonly Dictionary<string, string> Gear = new Dictionary<string, string>();          // slot w/a/h/c -> item string
        public readonly Dictionary<string, double> ClassRxp = new Dictionary<string, double>();     // renown put aside for each other class played
        public List<string> Order;                           // tactics: my move order (null = Smart)
        public bool AutoUlt = true;
        public string Lane;                                  // "l", "c" or "r"; null = default

        public void Count(string stat, double by = 1) => Life[stat] = (Life.TryGetValue(stat, out var v) ? v : 0) + by;
        public double Stat(string stat) => Life.TryGetValue(stat, out var v) ? v : 0;
    }

    /// <summary>
    /// Everything Tales keeps on this device: the pet profile, the gear bag, dust, the codex, HP and ink between fights,
    /// Bramble Road's daily finds, the towns you've walked to, your lore stones, the Book Bosses you've beaten, and what
    /// the shops sold you (library upgrades, unlocked classes, keepsakes; the server's ledger keeps those too, see
    /// Economy/Shop.TakeOwned).
    /// Saved as JSON next to the game's other data (TalesSave.FilePath).
    /// Pure C#: Unity sets FilePath at startup.
    /// </summary>
    public sealed class TalesSave
    {
        public const int Version = 1;
        public static string FilePath;
        static TalesSave current;
        public static TalesSave Current => current ?? (current = Load());
        public static event Action Changed;

        public string Personality;                           // the site's pers; picked once for a Unity-hatched buddy
        public readonly PetProfile Me = new PetProfile();
        public readonly List<string> Bag = new List<string>(); // item strings, newest last (LT_BAG cap)
        public readonly HashSet<string> Fresh = new HashSet<string>(); // items not looked at yet (the "new" dot)
        public int Dust;
        public readonly List<string> Seen = new List<string>(); // gear codex: base keys seen
        public readonly List<string> Met = new List<string>();  // bestiary: foe base names met
        public double HpFrac = 1, InkSaved, VitalsAt;           // carried between wild fights (pw_vit)
        public int Link = 1;                                     // last Bramble Road link
        public double LairAt;                                    // when the cave guardian was last beaten (ms)
        public bool ChestReady;                                  // the Guardian's Chest is waiting
        public readonly Dictionary<string, string> RoadDays = new Dictionary<string, string>(); // daily finds: "g"/"s" + link (pw_rd), "b" + town (a Book Boss's gift) -> yyyy-MM-dd
        public string FindDay = "";                              // gear found in towns and at shrines: the day and how many (5 a day)
        public int FindCount;
        public readonly List<string> TownsSeen = new List<string>(); // towns walked into (pw_seen)
        public string HomeStone = "", LastStone = "";            // lore stones (pg().stone home and last)
        public double RecallAt;                                  // when Recall was last used (ms)
        public readonly Dictionary<string, double> Gyms = new Dictionary<string, double>(); // Book Bosses beaten: town -> last win (ms) (pg().gyms)
        public readonly Dictionary<string, int> Meta = new Dictionary<string, int>(); // library upgrade levels (quest.meta)
        public readonly List<string> ClassesUnlocked = new List<string>();  // classes opened past the base seven (quest.clsU)
        public readonly List<string> Keepsakes = new List<string>();        // town keepsakes bought (decor ids)
        public readonly HashSet<string> Unseen = new HashSet<string>();     // unlocks not looked at yet ("move:<cls>:<k>", "cls:<k>")

        public void Touch() { Save(); Changed?.Invoke(); }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            var o = new Dictionary<string, object>
            {
                ["v"] = (double)Version, ["pers"] = Personality, ["bag"] = Strs(Bag), ["fresh"] = Strs(Fresh), ["dust"] = (double)Dust,
                ["seen"] = Strs(Seen), ["met"] = Strs(Met), ["hp"] = HpFrac, ["ink"] = InkSaved, ["vt"] = VitalsAt, ["link"] = (double)Link,
                ["lair"] = LairAt, ["chest"] = ChestReady, ["rd"] = Texts(RoadDays), ["fd"] = FindDay, ["fn"] = (double)FindCount,
                ["towns"] = Strs(TownsSeen), ["gyms"] = Nums(Gyms), ["stone"] = new Dictionary<string, object> { ["home"] = HomeStone, ["last"] = LastStone, ["rt"] = RecallAt },
                ["meta"] = Ints(Meta), ["clsU"] = Strs(ClassesUnlocked), ["keep"] = Strs(Keepsakes), ["new"] = Strs(Unseen),
                ["me"] = new Dictionary<string, object>
                {
                    ["cls"] = Me.Cls, ["rxp"] = Me.Rxp, ["own"] = Lists(Me.Own), ["kit"] = Lists(Me.Kit), ["life"] = Nums(Me.Life), ["crxp"] = Nums(Me.ClassRxp),
                    ["gear"] = Texts(Me.Gear), ["order"] = Me.Order == null ? null : Strs(Me.Order), ["ua"] = Me.AutoUlt, ["lane"] = Me.Lane,
                },
            };
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(o));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception) { /* a full disk shouldn't stop the game */ }
        }

        public static TalesSave Load()
        {
            var s = new TalesSave();
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return s;
            Dictionary<string, object> o;
            try { o = Json.ParseObject(File.ReadAllText(FilePath)); } catch (Exception) { return s; }
            if (o == null) return s;
            s.Personality = o.Str("pers", null);
            foreach (var x in o.Arr("bag")) if (x is string b) s.Bag.Add(b);
            foreach (var x in o.Arr("fresh")) if (x is string b) s.Fresh.Add(b);
            s.Dust = o.Int("dust");
            foreach (var x in o.Arr("seen")) if (x is string b) s.Seen.Add(b);
            foreach (var x in o.Arr("met")) if (x is string b) s.Met.Add(b);
            s.HpFrac = o.Num("hp", 1); s.InkSaved = o.Num("ink"); s.VitalsAt = o.Num("vt"); s.Link = Math.Max(1, o.Int("link", 1));
            s.LairAt = o.Num("lair"); s.ChestReady = o.Truthy("chest");
            var rd = o.Obj("rd");
            if (rd == null) { s.RoadDays["g1"] = o.Str("stash"); s.RoadDays["s1"] = o.Str("shrine"); } // older saves: one stash and one shrine
            else foreach (var kv in rd) if (kv.Value is string day) s.RoadDays[kv.Key] = day;
            s.FindDay = o.Str("fd"); s.FindCount = o.Int("fn");
            foreach (var x in o.Arr("towns")) if (x is string t) s.TownsSeen.Add(t);
            var gyms = o.Obj("gyms"); if (gyms != null) foreach (var kv in gyms) if (kv.Value is double d) s.Gyms[kv.Key] = d;
            var stone = o.Obj("stone");
            s.HomeStone = stone.Str("home"); s.LastStone = stone.Str("last"); s.RecallAt = stone.Num("rt");
            var meta = o.Obj("meta"); if (meta != null) foreach (var kv in meta) if (kv.Value is double d) s.Meta[kv.Key] = (int)d;
            foreach (var x in o.Arr("clsU")) if (x is string c) s.ClassesUnlocked.Add(c);
            foreach (var x in o.Arr("keep")) if (x is string k) s.Keepsakes.Add(k);
            foreach (var x in o.Arr("new")) if (x is string n) s.Unseen.Add(n);
            var me = o.Obj("me");
            if (me != null)
            {
                s.Me.Cls = me.Str("cls", null); s.Me.Rxp = me.Num("rxp");
                ReadLists(me.Obj("own"), s.Me.Own); ReadLists(me.Obj("kit"), s.Me.Kit);
                var life = me.Obj("life"); if (life != null) foreach (var kv in life) if (kv.Value is double d) s.Me.Life[kv.Key] = d;
                var crxp = me.Obj("crxp"); if (crxp != null) foreach (var kv in crxp) if (kv.Value is double d) s.Me.ClassRxp[kv.Key] = d;
                var gear = me.Obj("gear"); if (gear != null) foreach (var kv in gear) if (kv.Value is string g) s.Me.Gear[kv.Key] = g;
                if (me.Has("order")) { s.Me.Order = new List<string>(); foreach (var x in me.Arr("order")) if (x is string k) s.Me.Order.Add(k); }
                s.Me.AutoUlt = !me.Has("ua") || me.Truthy("ua"); s.Me.Lane = me.Str("lane", null);
            }
            return s;
        }

        static List<object> Strs(IEnumerable<string> a) { var l = new List<object>(); foreach (var x in a) l.Add(x); return l; }
        static Dictionary<string, object> Lists(Dictionary<string, List<string>> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = Strs(kv.Value); return o; }
        static Dictionary<string, object> Nums(Dictionary<string, double> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
        static Dictionary<string, object> Ints(Dictionary<string, int> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = (double)kv.Value; return o; }
        static Dictionary<string, object> Texts(Dictionary<string, string> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
        static void ReadLists(Dictionary<string, object> from, Dictionary<string, List<string>> to)
        {
            if (from == null) return;
            foreach (var kv in from)
            {
                var l = new List<string>();
                if (kv.Value is List<object> a) foreach (var x in a) if (x is string s) l.Add(s);
                to[kv.Key] = l;
            }
        }
    }
}
