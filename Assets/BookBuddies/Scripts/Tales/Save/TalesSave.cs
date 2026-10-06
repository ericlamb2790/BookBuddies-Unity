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
        public List<string> Order;                           // tactics: my move order (null = Smart)
        public bool AutoUlt = true;
        public string Lane;                                  // "l", "c" or "r"; null = default

        public void Count(string stat, double by = 1) => Life[stat] = (Life.TryGetValue(stat, out var v) ? v : 0) + by;
        public double Stat(string stat) => Life.TryGetValue(stat, out var v) ? v : 0;
    }

    /// <summary>
    /// Everything Tales keeps on this device: the pet profile, the gear bag, dust, the codex, HP and ink between fights,
    /// and Bramble Road's daily finds. Saved as JSON next to the game's other data (TalesSave.FilePath).
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
        public string StashDay = "", ShrineDay = "";             // daily finds (yyyy-MM-dd)

        public void Touch() { Save(); Changed?.Invoke(); }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            var o = new Dictionary<string, object>
            {
                ["v"] = (double)Version, ["pers"] = Personality, ["bag"] = Strs(Bag), ["fresh"] = Strs(Fresh), ["dust"] = (double)Dust,
                ["seen"] = Strs(Seen), ["met"] = Strs(Met), ["hp"] = HpFrac, ["ink"] = InkSaved, ["vt"] = VitalsAt, ["link"] = (double)Link,
                ["lair"] = LairAt, ["chest"] = ChestReady, ["stash"] = StashDay, ["shrine"] = ShrineDay,
                ["me"] = new Dictionary<string, object>
                {
                    ["cls"] = Me.Cls, ["rxp"] = Me.Rxp, ["own"] = Lists(Me.Own), ["kit"] = Lists(Me.Kit), ["life"] = Nums(Me.Life),
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
            s.LairAt = o.Num("lair"); s.ChestReady = o.Truthy("chest"); s.StashDay = o.Str("stash"); s.ShrineDay = o.Str("shrine");
            var me = o.Obj("me");
            if (me != null)
            {
                s.Me.Cls = me.Str("cls", null); s.Me.Rxp = me.Num("rxp");
                ReadLists(me.Obj("own"), s.Me.Own); ReadLists(me.Obj("kit"), s.Me.Kit);
                var life = me.Obj("life"); if (life != null) foreach (var kv in life) if (kv.Value is double d) s.Me.Life[kv.Key] = d;
                var gear = me.Obj("gear"); if (gear != null) foreach (var kv in gear) if (kv.Value is string g) s.Me.Gear[kv.Key] = g;
                if (me.Has("order")) { s.Me.Order = new List<string>(); foreach (var x in me.Arr("order")) if (x is string k) s.Me.Order.Add(k); }
                s.Me.AutoUlt = !me.Has("ua") || me.Truthy("ua"); s.Me.Lane = me.Str("lane", null);
            }
            return s;
        }

        static List<object> Strs(IEnumerable<string> a) { var l = new List<object>(); foreach (var x in a) l.Add(x); return l; }
        static Dictionary<string, object> Lists(Dictionary<string, List<string>> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = Strs(kv.Value); return o; }
        static Dictionary<string, object> Nums(Dictionary<string, double> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
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
