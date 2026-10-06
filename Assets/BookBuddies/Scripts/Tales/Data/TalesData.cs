using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>A move (hero move, genre tome or foe special). Numbers live in Num: p, n, hits, cc, stun, bleed, poison, exp, pe, exe, drain, ink, inkAll, hl, rv, sh, shs, pow, regen, dodge, taunt, cd, buff, steal, dark, calm, weak, …</summary>
    public sealed class MoveDef
    {
        public string Key, Target = "one", Fx, Uv, Icon, Name, Desc;
        public int Cost;
        public bool Ult, Clean, Crit, Pull, Gath, Leap, Wind;
        public readonly Dictionary<string, double> Num = new Dictionary<string, double>();
        public double this[string key] => Num.TryGetValue(key, out var v) ? v : 0;
        public bool Has(string key) => Num.ContainsKey(key);
        public int Hits => (int)(Num.TryGetValue("hits", out var h) ? h : Num.TryGetValue("n", out var n) ? n : 1);

        internal static MoveDef From(string key, Dictionary<string, object> o)
        {
            var m = new MoveDef { Key = o.Str("k", key), Target = o.Str("t", "one"), Fx = o.Str("fx", null), Uv = o.Str("uv", null), Icon = o.Str("i", null), Name = o.Str("n", null), Desc = o.Str("d", null) };
            m.Cost = o.Int("c");
            m.Ult = o.Truthy("u");
            foreach (var kv in o)
            {
                switch (kv.Value)
                {
                    case double d: if (kv.Key != "c" && kv.Key != "u") m.Num[kv.Key] = d; break;
                    case bool b: if (b) m.Num[kv.Key] = 1; break;
                }
            }
            m.Clean = m.Has("clean"); m.Crit = m.Has("crit"); m.Pull = m.Has("pull"); m.Gath = m.Has("gath"); m.Leap = m.Has("leap"); m.Wind = m.Has("wind");
            return m;
        }
    }

    public sealed class ClassDef
    {
        public string Key, Name, Icon, Role, Mech, Book, Sig;
        public int Hp, Atk, Def, Spd;
        public bool Advanced, Literary, Spite;
        public string[] Fav = new string[0];
        public string UnlockMetric, UnlockLabel;
        public double UnlockAt;
        public readonly List<MoveDef> Moves = new List<MoveDef>();        // TQ_AB order: strike, 5 regulars, ult, ult2
        public readonly Dictionary<string, string[]> Names = new Dictionary<string, string[]>(); // move key -> name per genre (6)
        public string[] Icons = new string[0];                            // by move index
        public string[] Evolutions = new string[0];                       // TQ_EVO, 7 names
        public string[] Taunts = new string[0], TauntEnds = new string[0], Retorts = new string[0], RetortEnds = new string[0];
        public MoveDef Move(string key) => Moves.Find(m => m.Key == key);
    }

    /// <summary>A villain from TQ_MIN, TQ_BOSS or TQ_DARK. Hp and Atk are multipliers (null means 1, and "no archetype" for drawing).</summary>
    public sealed class FoeDef
    {
        public string N, I, C, An, Sp, Sn, Si, Rg, Sh, Mv, Say;
        public int G, Index;
        public double? Hp, Atk;
        public bool Dodge, IsBoss, IsDark;
        public string[] Says = new string[0];
    }

    public sealed class Affix
    {
        public string N, C, Au, Sp, Sn, Si;
        public double M = .42, Hp = 1, Atk = 1, Vs, Df, Sx;
        public string[] Parts = new string[0];
    }

    public sealed class WildTier
    {
        public string Name, Icon, Sub, Fx, Hill;
        public int Lvl;
        public double Mul, EliteChance, TreeDensity;
        public int[] Foes = new int[0], Elites = new int[0];
        public string[] Sky = new string[0], Trees = new string[0];
        public Dictionary<string, string> Pal = new Dictionary<string, string>();
    }

    public sealed class TownInfo { public string Key, Name, Icon, Genre, Roof; public int Lv; public string[] Trees = new string[0], Deco = new string[0]; }

    /// <summary>
    /// Every Tales of Pages table, exported from the website (Resources/BookBuddies/Data/tales.json, made by tools/export_tales.js).
    /// Pure C#: set TextLoader before first use (Unity sets it to Art.Text; tests read the file).
    /// </summary>
    public sealed class TalesData
    {
        public static Func<string, string> TextLoader;
        static TalesData current;
        public static TalesData Current => current ?? (current = Load(TextLoader("Data/tales")));

        public readonly List<(string key, string icon, string label)> Genres = new List<(string, string, string)>();
        public readonly Dictionary<string, ClassDef> Classes = new Dictionary<string, ClassDef>();
        public readonly Dictionary<string, string> PersonalityClass = new Dictionary<string, string>();
        public readonly Dictionary<string, string> MoveDesc = new Dictionary<string, string>();
        public readonly List<MoveDef> Tomes = new List<MoveDef>();
        public readonly Dictionary<string, string> TomeLabel = new Dictionary<string, string>();
        public string[][] Adjectives = new string[0][];
        public readonly Dictionary<string, string[]> Nouns = new Dictionary<string, string[]>(), BookNames = new Dictionary<string, string[]>();
        public string[] EvoIcons = new string[0], Projectiles = new string[0], PetLvFlavor = new string[0], PetLvX = new string[0];

        public readonly List<FoeDef> Minions = new List<FoeDef>(), Bosses = new List<FoeDef>(), Dark = new List<FoeDef>();
        public readonly Dictionary<string, MoveDef> Specials = new Dictionary<string, MoveDef>();
        public readonly Dictionary<string, string> SpecialFx = new Dictionary<string, string>();
        public readonly List<Affix> Affixes = new List<Affix>();
        public readonly List<(string text, double hp, double atk)> Epithets = new List<(string, double, double)>();

        public readonly List<WildTier> Tiers = new List<WildTier>();
        public string[] Route = new string[0];
        public int[] RouteLv = new int[0], LinkT = new int[0], LinkBase = new int[0];
        public readonly Dictionary<string, int> TownGenre = new Dictionary<string, int>();
        public readonly Dictionary<string, TownInfo> Towns = new Dictionary<string, TownInfo>();
        public readonly Dictionary<string, List<(string emoji, string kind, int count)>> LifeSets = new Dictionary<string, List<(string, string, int)>>();

        public ClassDef Class(string key) => key != null && Classes.TryGetValue(key, out var c) ? c : Classes["sleuth"];
        public string GenreLabel(int g) => Genres[((g % 6) + 6) % 6].label;
        public string GenreIcon(int g) => Genres[((g % 6) + 6) % 6].icon;

        /// <summary>tqGenOf: hue to genre 0-5.</summary>
        public static int GenreOfHue(double hue) => (int)Math.Floor(((((int)hue % 360) + 360) % 360 + 30) / 60.0) % 6;

        public static TalesData Load(string json)
        {
            var j = Json.ParseObject(json);
            var d = new TalesData();
            foreach (List<object> g in j.Arr("TQ_GEN")) d.Genres.Add(((string)g[0], (string)g[1], (string)g[2]));

            var abn = j.Obj("TQ_ABN"); var abi = j.Obj("TQ_ABI"); var sig = j.Obj("TQ_SIG"); var evo = j.Obj("TQ_EVO");
            var taunt = j.Obj("TQ_TAUNT"); var retort = j.Obj("TQ_RETORT"); var ab = j.Obj("TQ_AB");
            foreach (var kv in j.Obj("TQ_CLS"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                var c = new ClassDef
                {
                    Key = kv.Key, Name = o.Str("n"), Icon = o.Str("i"), Role = o.Str("role"), Mech = o.Str("mech", null), Book = o.Str("book", null),
                    Hp = o.Int("hp"), Atk = o.Int("atk"), Def = o.Int("def"), Spd = o.Int("spd"),
                    Advanced = o.Truthy("adv"), Literary = o.Truthy("lit"), Spite = o.Truthy("spite"), Fav = Strings(o.Arr("fav")),
                    Sig = sig.Str(kv.Key, "Finale"),
                };
                var un = o.Arr("un");
                if (un != null && un.Count >= 3) { c.UnlockMetric = (string)un[0]; c.UnlockAt = (double)un[1]; c.UnlockLabel = (string)un[2]; }
                if (ab.Arr(kv.Key) != null)
                    foreach (Dictionary<string, object> m in ab.Arr(kv.Key)) c.Moves.Add(MoveDef.From(null, m));
                var names = abn.Obj(kv.Key);
                if (names != null) foreach (var n in names) c.Names[n.Key] = Strings((List<object>)n.Value);
                c.Icons = Strings(abi.Arr(kv.Key));
                c.Evolutions = Strings(evo.Arr(kv.Key));
                var t = taunt.Arr(kv.Key); var r = retort.Arr(kv.Key);
                if (t != null && t.Count == 2) { c.Taunts = Strings((List<object>)t[0]); c.TauntEnds = Strings((List<object>)t[1]); }
                if (r != null && r.Count == 2) { c.Retorts = Strings((List<object>)r[0]); c.RetortEnds = Strings((List<object>)r[1]); }
                for (int i = 0; i < c.Moves.Count; i++) if (i < c.Icons.Length) c.Moves[i].Icon = c.Icons[i];
                d.Classes[kv.Key] = c;
            }
            foreach (var kv in j.Obj("TQ_PERS_CLS")) d.PersonalityClass[kv.Key] = (string)kv.Value;
            foreach (var kv in j.Obj("TQ_ABD")) d.MoveDesc[kv.Key] = (string)kv.Value;
            foreach (Dictionary<string, object> g in j.Arr("TQ_GAB")) d.Tomes.Add(MoveDef.From(null, g));
            foreach (var kv in j.Obj("TQ_GLABEL")) d.TomeLabel[kv.Key] = (string)kv.Value;
            var adj = j.Arr("TQ_ADJ"); d.Adjectives = new string[adj.Count][];
            for (int i = 0; i < adj.Count; i++) d.Adjectives[i] = Strings((List<object>)adj[i]);
            foreach (var kv in j.Obj("TQ_NOUN")) d.Nouns[kv.Key] = Strings((List<object>)kv.Value);
            foreach (var kv in j.Obj("TQ_BOOKN")) d.BookNames[kv.Key] = Strings((List<object>)kv.Value);
            d.EvoIcons = Strings(j.Arr("TQ_EVO_I")); d.Projectiles = Strings(j.Arr("TQ_PJ"));
            d.PetLvFlavor = Strings(j.Arr("PL_FLAV")); d.PetLvX = Strings(j.Arr("PL_X"));

            ReadFoes(j.Arr("TQ_MIN"), d.Minions, false, false);
            ReadFoes(j.Arr("TQ_BOSS"), d.Bosses, true, false);
            ReadFoes(j.Arr("TQ_DARK"), d.Dark, true, true);
            foreach (var kv in j.Obj("TQ_FSP")) d.Specials[kv.Key] = MoveDef.From(kv.Key, (Dictionary<string, object>)kv.Value);
            foreach (var kv in j.Obj("TQ_FOEFX")) d.SpecialFx[kv.Key] = (string)kv.Value;
            foreach (Dictionary<string, object> a in j.Arr("TQ_AFX"))
                d.Affixes.Add(new Affix
                {
                    N = a.Str("n"), C = a.Str("c", null), Au = a.Str("au", null), Sp = a.Str("sp", null), Sn = a.Str("sn", null), Si = a.Str("si", null),
                    M = a.Num("m", .42), Hp = a.Num("hp", 1), Atk = a.Num("atk", 1), Vs = a.Num("vs"), Df = a.Num("df"), Sx = a.Num("sx"), Parts = Strings(a.Arr("pt")),
                });
            foreach (List<object> e in j.Arr("TQ_EPI")) d.Epithets.Add(((string)e[0], (double)e[1], (double)e[2]));

            foreach (Dictionary<string, object> t in j.Arr("WILD_TIERS"))
            {
                var w = new WildTier
                {
                    Name = t.Str("n"), Icon = t.Str("i"), Sub = t.Str("sub"), Fx = t.Str("fx"), Hill = t.Str("hill"), Lvl = t.Int("lvl"), Mul = t.Num("mul", 1),
                    EliteChance = t.Num("ec"), TreeDensity = t.Num("td"), Foes = t.Ints("foes"), Elites = t.Ints("el"), Sky = Strings(t.Arr("sky")), Trees = Strings(t.Arr("trees")),
                };
                var pal = t.Obj("pal");
                if (pal != null) foreach (var kv in pal) w.Pal[kv.Key] = kv.Value as string;
                d.Tiers.Add(w);
            }
            d.Route = Strings(j.Arr("ROUTE")); d.RouteLv = j.Ints("ROUTE_LV"); d.LinkT = j.Ints("LINK_T"); d.LinkBase = j.Ints("LINK_BASE");
            foreach (var kv in j.Obj("TOWN_FG")) d.TownGenre[kv.Key] = (int)(double)kv.Value;
            foreach (var kv in j.Obj("TOWNS"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                d.Towns[kv.Key] = new TownInfo { Key = kv.Key, Name = o.Str("n"), Icon = o.Str("i"), Genre = o.Str("g"), Roof = o.Str("roof", null), Lv = o.Int("lv"), Trees = Strings(o.Arr("trees")), Deco = Strings(o.Arr("deco")) };
            }
            foreach (var kv in j.Obj("LIFE_SETS"))
            {
                var list = new List<(string, string, int)>();
                foreach (List<object> e in (List<object>)kv.Value) list.Add(((string)e[0], (string)e[1], (int)(double)e[2]));
                d.LifeSets[kv.Key] = list;
            }
            return d;
        }

        static void ReadFoes(List<object> from, List<FoeDef> to, bool boss, bool dark)
        {
            if (from == null) return;
            foreach (Dictionary<string, object> o in from)
                to.Add(new FoeDef
                {
                    Index = to.Count, N = o.Str("n"), I = o.Str("i"), C = o.Str("c", null), G = o.Int("g"), An = o.Str("an"), Sp = o.Str("sp"), Sn = o.Str("sn"), Si = o.Str("si"),
                    Rg = o.Str("rg", null), Sh = o.Str("sh", "blob"), Mv = o.Str("mv", null), Say = o.Str("say", null), Says = Strings(o.Arr("says")),
                    Hp = o.Has("hp") ? o.Num("hp") : (double?)null, Atk = o.Has("atk") ? o.Num("atk") : (double?)null, Dodge = o.Truthy("dodge"), IsBoss = boss, IsDark = dark,
                });
        }

        internal static string[] Strings(List<object> list)
        {
            if (list == null) return new string[0];
            var a = new string[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i] as string ?? (list[i] is double n ? JsMath.Num(n) : "");
            return a;
        }

        List<NpcDef> npcs;

        /// <summary>TQ_NPC in TQ_NPCK order: the storybook friends a lucky fate roll brings. Read from tales.json on first use.</summary>
        public List<NpcDef> Npcs => npcs ?? (npcs = ReadNpcs(Json.ParseObject(TextLoader("Data/tales"))));

        static List<NpcDef> ReadNpcs(Dictionary<string, object> j)
        {
            var list = new List<NpcDef>();
            var all = j.Obj("TQ_NPC");
            foreach (var key in Strings(j.Arr("TQ_NPCK")))
            {
                var o = all.Obj(key);
                if (o == null) continue;
                list.Add(new NpcDef
                {
                    Key = key, Name = o.Str("n"), Icon = o.Str("i"), Count = o.Int("cnt", 1), Fx = o.Str("fx"),
                    Act = o.Str("act"), Say = o.Str("say"), Camp = o.Str("camp"), Look = NpcLook(o.Obj("look")),
                });
            }
            return list;
        }

        // tqNpcLook: the friend's look over the site's defaults (meadow, cute face, no pattern)
        static string NpcLook(Dictionary<string, object> look)
        {
            var o = new Dictionary<string, object> { ["bg"] = "meadow", ["z"] = 1.0, ["f"] = "cute", ["e"] = "none", ["pt"] = "none" };
            if (look != null) foreach (var kv in look) o[kv.Key] = kv.Value;
            if (!(o.TryGetValue("o", out var outfit) && outfit is Dictionary<string, object>)) o["o"] = new Dictionary<string, object>();
            return Json.Write(o);
        }
    }

    /// <summary>A storybook friend (TQ_NPC). Look is pet look JSON, so it draws with the pet system; Count is how many come.</summary>
    public sealed class NpcDef
    {
        public string Key, Name, Icon, Fx, Act, Say, Camp, Look;
        public int Count = 1;
    }
}
