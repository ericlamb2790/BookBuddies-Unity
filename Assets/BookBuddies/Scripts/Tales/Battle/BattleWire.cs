using System;
using System.Collections.Generic;
using System.Globalization;

namespace BookBuddies.Tales
{
    /// <summary>
    /// A shared party fight on the wire: the pets, the fight's setup and the players' inputs as JSON-ready values
    /// (dictionaries, lists, doubles, strings, bools). Every game in the fight builds its engine from the same wire, so a
    /// trip through Json.Write and Json.Parse gives back exactly the same numbers. Pure C#.
    /// </summary>
    public static class BattleWire
    {
        /// <summary>
        /// A number, exactly: a whole number as itself, any other as its 64 bits in hex (Json.Write prints doubles in the
        /// runtime's default format, which keeps only 15 digits on Unity's Mono).
        /// </summary>
        public static object Num(double v) => v == Math.Floor(v) && Math.Abs(v) < 1e15 ? (object)v : BitConverter.DoubleToInt64Bits(v).ToString("x16");

        /// <summary>A number written by Num (a plain JSON number reads as itself; anything else is 0).</summary>
        public static double Dbl(object o) =>
            o is double d ? d
            : o is string s && long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long bits) ? BitConverter.Int64BitsToDouble(bits)
            : 0;

        /// <summary>A pet in battle: its unit, HeroInfo and gear totals, and its moves by key.</summary>
        public static Dictionary<string, object> Hero(BattleUnit h)
        {
            var i = h.Hero;
            return new Dictionary<string, object>
            {
                ["key"] = h.Key, ["n"] = h.Name, ["gen"] = Num(h.Gen), ["lvl"] = Num(h.Lvl), ["max"] = Num(h.Max), ["hp"] = Num(h.Hp),
                ["atk"] = Num(h.Atk), ["def"] = Num(h.Def), ["spd"] = Num(h.Spd), ["ink"] = Num(h.Ink), ["lane"] = h.Lane.ToString(), ["ko"] = h.Ko,
                ["st"] = Nums(h.St), ["mv"] = All(h.Moves, mv => mv.Key),
                ["cls"] = i.Cls, ["look"] = i.Look, ["pet"] = i.PetKey, ["sig"] = i.Sig, ["nat"] = i.NatureName, ["nati"] = i.NatureIcon,
                ["rank"] = Num(i.Rank), ["stage"] = Num(i.Stage), ["rn"] = Num(i.Rn), ["shiny"] = i.Shiny, ["tl"] = Num(i.Lvl), ["xp"] = Num(i.Xp),
                ["bhp"] = Num(i.BaseHp), ["batk"] = Num(i.BaseAtk), ["bdef"] = Num(i.BaseDef), ["bspd"] = Num(i.BaseSpd), ["gear"] = Gear(i.Gear),
                ["cc"] = Num(i.Cc), ["cd"] = Num(i.Cd), ["ls"] = Num(i.Ls), ["th"] = Num(i.Th), ["dg"] = Num(i.Dg), ["rg"] = Num(i.Rg), ["sh"] = Num(i.Sh),
                ["gink"] = Num(i.GearInk), ["kit"] = All(i.Kit, k => k), ["own"] = All(i.Own, k => k), ["known"] = All(i.Known, k => k), ["order"] = All(i.Order, k => k),
                ["auto"] = i.AutoUlt, ["want"] = i.WantUlt, ["phx"] = i.PhoenixUsed, ["plot"] = i.PlotUsed, ["dk"] = Num(i.Dark), ["wk"] = Num(i.Weak),
                ["mink"] = Num(i.MetaInk), ["mrev"] = i.MetaRev,
            };
        }

        /// <summary>The pet back from the wire.</summary>
        public static BattleUnit Hero(Dictionary<string, object> m)
        {
            var i = new HeroInfo
            {
                Cls = m.Str("cls", null), Look = m.Str("look", null), PetKey = m.Str("pet", null), Sig = m.Str("sig", null),
                NatureName = m.Str("nat", null), NatureIcon = m.Str("nati", null),
                Rank = I(m, "rank"), Stage = I(m, "stage"), Rn = I(m, "rn"), Shiny = m.Truthy("shiny"), Lvl = I(m, "tl", 1), Xp = I(m, "xp"),
                BaseHp = I(m, "bhp"), BaseAtk = I(m, "batk"), BaseDef = I(m, "bdef"), BaseSpd = I(m, "bspd"), Gear = Gear(m.Obj("gear")),
                Cc = D(m, "cc"), Cd = D(m, "cd"), Ls = D(m, "ls"), Th = D(m, "th"), Dg = D(m, "dg"), Rg = D(m, "rg"), Sh = D(m, "sh"),
                GearInk = I(m, "gink"), Kit = Strs(m, "kit"), Own = Strs(m, "own"), Known = Strs(m, "known"), Order = Strs(m, "order"),
                AutoUlt = m.Truthy("auto"), WantUlt = m.Truthy("want"), PhoenixUsed = m.Truthy("phx"), PlotUsed = m.Truthy("plot"),
                Dark = I(m, "dk"), Weak = I(m, "wk"), MetaInk = I(m, "mink"), MetaRev = m.Truthy("mrev"),
            };
            var h = new BattleUnit
            {
                Key = m.Str("key", null), Name = m.Str("n", null), Gen = I(m, "gen"), Lvl = D(m, "lvl"), Max = D(m, "max"), Hp = D(m, "hp"),
                Atk = D(m, "atk"), Def = D(m, "def"), Spd = D(m, "spd"), Ink = I(m, "ink"), Lane = Lane(m.Str("lane")), Ko = m.Truthy("ko"), Hero = i,
            };
            Fill(h.St, m.Obj("st"));
            foreach (var k in m.Arr("mv")) if (k is string key && HeroFactory.Move(i.Cls, key) is MoveDef mv) h.Moves.Add(mv);
            return h;
        }

        /// <summary>A pet in a party fight: {pid, owner, hp, ink, unit} (a "ready" answer, and each of the setup's party).</summary>
        public static Dictionary<string, object> Party(PartyHero p) => new Dictionary<string, object>
        {
            ["pid"] = p.Pid, ["owner"] = p.Owner, ["hp"] = Num(p.HpFrac), ["ink"] = Num(p.Ink), ["unit"] = Hero(p.Unit),
        };

        /// <summary>A party pet back from the wire.</summary>
        public static PartyHero Party(Dictionary<string, object> m) => new PartyHero
        {
            Pid = m.Str("pid", null), Owner = m.Str("owner", null), HpFrac = D(m, "hp", 1), Ink = I(m, "ink"), Unit = Hero(m.Obj("unit")),
        };

        /// <summary>A road or Book Boss fight's setup: its numbers, its foes (each villain with its rolls, phases and team) and the party.</summary>
        public static Dictionary<string, object> Setup(BattleSetup s) => new Dictionary<string, object>
        {
            ["title"] = s.Title, ["place"] = s.Place, ["lvl"] = Num(s.Lvl), ["mul"] = Num(s.Mul), ["guardian"] = s.Guardian, ["bb"] = s.BookBoss,
            ["tier"] = Num(s.Tier), ["cave"] = s.Cave, ["hp"] = Num(s.HpFrac), ["ink"] = Num(s.Ink),
            ["foes"] = All(s.Foes, f => new Dictionary<string, object> { ["v"] = Variant(f.v), ["boss"] = f.boss, ["elite"] = f.elite }),
            ["party"] = All(s.Party, p => Party(p)),
        };

        /// <summary>The setup back from the wire, played by this game as the pet keyed me.</summary>
        public static BattleSetup Setup(Dictionary<string, object> m, string me)
        {
            var s = new BattleSetup
            {
                Title = m.Str("title", null), Place = m.Str("place", null), Lvl = I(m, "lvl", 2), Mul = D(m, "mul", 1), Guardian = m.Truthy("guardian"),
                BookBoss = m.Truthy("bb"), Tier = I(m, "tier", 1), Cave = m.Truthy("cave"), HpFrac = D(m, "hp", 1), Ink = I(m, "ink"), Me = me,
            };
            foreach (var o in m.Arr("foes"))
                if (o is Dictionary<string, object> f) s.Foes.Add((Variant(f.Obj("v")), f.Truthy("boss"), f.Truthy("elite")));
            foreach (var o in m.Arr("party"))
                if (o is Dictionary<string, object> p) s.Party.Add(Party(p));
            return s;
        }

        /// <summary>Your pet for a party fight, built from your save like a fight of your own (with your lane), keyed "h:" + pid.</summary>
        public static PartyHero Mine(string pid, string owner, string look, string petName, double hpFrac, int ink)
        {
            var unit = HeroFactory.Build(look, string.IsNullOrEmpty(petName) ? "Your pet" : petName, 1);
            unit.Key = "h:" + pid;
            return new PartyHero { Pid = pid, Owner = owner, Unit = unit, HpFrac = hpFrac, Ink = ink };
        }

        /// <summary>Players' inputs: {h: hero key, a: act, l: lane or ""}.</summary>
        public static List<object> Inputs(List<BattleInput> ins) =>
            All(ins, i => new Dictionary<string, object> { ["h"] = i.Hero, ["a"] = i.Act, ["l"] = i.Lane == '\0' ? "" : i.Lane.ToString() });

        /// <summary>Inputs back from the wire, in order.</summary>
        public static List<BattleInput> Inputs(List<object> l)
        {
            var ins = new List<BattleInput>();
            if (l != null)
                foreach (var o in l)
                    if (o is Dictionary<string, object> m)
                    {
                        string lane = m.Str("l");
                        ins.Add(new BattleInput { Hero = m.Str("h", null), Act = m.Str("a", null), Lane = lane.Length > 0 ? lane[0] : '\0' });
                    }
            return ins;
        }

        // a villain with its tqVariant rolls, a Book Boss's phases and its team
        static Dictionary<string, object> Variant(FoeVariant v) => new Dictionary<string, object>
        {
            ["def"] = Def(v.Def), ["dn"] = v.Dn, ["hp"] = Num(v.Hp), ["atk"] = Num(v.Atk), ["hashp"] = v.HasHp, ["ax"] = v.Ax, ["tc"] = v.Tc, ["au"] = v.Au,
            ["xp"] = v.Xp, ["vs"] = Num(v.Vs), ["vdf"] = Num(v.Vdf), ["vsx"] = Num(v.Vsx), ["sp"] = v.Sp, ["sn"] = v.Sn, ["si"] = v.Si,
            ["phases"] = All(v.Phases, p => Phase(p)), ["team"] = All(v.Team, t => Variant(t)),
        };

        static FoeVariant Variant(Dictionary<string, object> m) => new FoeVariant
        {
            Def = Def(m.TryGetValue("def", out var d) ? d : null), Dn = m.Str("dn", null), Hp = D(m, "hp", 1), Atk = D(m, "atk", 1), HasHp = m.Truthy("hashp"),
            Ax = m.Str("ax", null), Tc = m.Str("tc", null), Au = m.Str("au", null), Xp = m.Str("xp", null), Vs = D(m, "vs"), Vdf = D(m, "vdf"), Vsx = D(m, "vsx"),
            Sp = m.Str("sp", null), Sn = m.Str("sn", null), Si = m.Str("si", null),
            Phases = m.Has("phases") ? m.Arr("phases").ConvertAll(p => Phase(p as Dictionary<string, object>)).ToArray() : null,
            Team = m.Has("team") ? m.Arr("team").ConvertAll(t => Variant(t as Dictionary<string, object>)).ToArray() : null,
        };

        static Dictionary<string, object> Phase(BossPhase p) => new Dictionary<string, object>
        {
            ["n"] = p.Name, ["say"] = p.Say, ["tw"] = All(p.Twists, k => k), ["hp"] = Num(p.Hp), ["atk"] = Num(p.Atk),
        };

        static BossPhase Phase(Dictionary<string, object> m) => new BossPhase
        {
            Name = m.Str("n", null), Say = m.Str("say", null), Twists = TalesData.Strings(m.Arr("tw")), Hp = D(m, "hp"), Atk = D(m, "atk"),
        };

        // a villain from the tables goes by its name (names are unique across TQ_MIN, TQ_BOSS and TQ_DARK); any other
        // (a Book Boss wearing its body) goes whole
        static object Def(FoeDef d)
        {
            var t = TalesData.Current;
            if (t.Minions.Contains(d) || t.Bosses.Contains(d) || t.Dark.Contains(d)) return d.N;
            return new Dictionary<string, object>
            {
                ["n"] = d.N, ["i"] = d.I, ["c"] = d.C, ["an"] = d.An, ["sp"] = d.Sp, ["sn"] = d.Sn, ["si"] = d.Si, ["rg"] = d.Rg, ["sh"] = d.Sh,
                ["mv"] = d.Mv, ["say"] = d.Say, ["lair"] = d.Lair, ["want"] = d.Want, ["g"] = Num(d.G), ["index"] = Num(d.Index),
                ["hp"] = d.Hp.HasValue ? Num(d.Hp.Value) : null, ["atk"] = d.Atk.HasValue ? Num(d.Atk.Value) : null,
                ["dodge"] = d.Dodge, ["boss"] = d.IsBoss, ["dark"] = d.IsDark, ["says"] = All(d.Says, k => k),
            };
        }

        static FoeDef Def(object o)
        {
            var t = TalesData.Current;
            if (o is string n) return t.Minions.Find(x => x.N == n) ?? t.Bosses.Find(x => x.N == n) ?? t.Dark.Find(x => x.N == n);
            var m = o as Dictionary<string, object>;
            return new FoeDef
            {
                N = m.Str("n", null), I = m.Str("i", null), C = m.Str("c", null), An = m.Str("an", null), Sp = m.Str("sp", null), Sn = m.Str("sn", null),
                Si = m.Str("si", null), Rg = m.Str("rg", null), Sh = m.Str("sh", null), Mv = m.Str("mv", null), Say = m.Str("say", null),
                Lair = m.Str("lair", null), Want = m.Str("want", null), G = I(m, "g"), Index = I(m, "index"),
                Hp = m.Has("hp") ? D(m, "hp") : (double?)null, Atk = m.Has("atk") ? D(m, "atk") : (double?)null,
                Dodge = m.Truthy("dodge"), IsBoss = m.Truthy("boss"), IsDark = m.Truthy("dark"), Says = TalesData.Strings(m.Arr("says")),
            };
        }

        // gear totals, like HeroSnap's gx but with exact numbers
        static Dictionary<string, object> Gear(GearSum g) => g == null ? null : new Dictionary<string, object>
        {
            ["st"] = Nums(g.Stats), ["pk"] = Nums(g.Perks), ["from"] = TalesSave.Texts(g.PerkFrom), ["pow"] = Num(g.Power),
        };

        static GearSum Gear(Dictionary<string, object> m)
        {
            if (m == null) return null;
            var g = new GearSum { Power = D(m, "pow") };
            Fill(g.Stats, m.Obj("st"));
            Fill(g.Perks, m.Obj("pk"));
            var from = m.Obj("from");
            if (from != null) foreach (var kv in from) if (kv.Value is string n) g.PerkFrom[kv.Key] = n;
            return g;
        }

        /// <summary>Named numbers (statuses, gear stats) for the wire.</summary>
        internal static Dictionary<string, object> Nums(Dictionary<string, double> d)
        {
            var m = new Dictionary<string, object>();
            foreach (var kv in d) m[kv.Key] = Num(kv.Value);
            return m;
        }

        /// <summary>Named numbers back from the wire into d (nothing when m is null).</summary>
        internal static void Fill(Dictionary<string, double> d, Dictionary<string, object> m)
        {
            if (m != null) foreach (var kv in m) d[kv.Key] = Dbl(kv.Value);
        }

        // a list for the wire (null stays null)
        static List<object> All<T>(IEnumerable<T> xs, Func<T, object> f)
        {
            if (xs == null) return null;
            var l = new List<object>();
            foreach (var x in xs) l.Add(f(x));
            return l;
        }

        static List<string> Strs(Dictionary<string, object> m, string k) => m.Has(k) ? new List<string>(TalesData.Strings(m.Arr(k))) : null;
        static char Lane(string l) => l.Length > 0 ? l[0] : 'c';
        static double D(Dictionary<string, object> m, string k, double fallback = 0) => m != null && m.TryGetValue(k, out var o) && o != null ? Dbl(o) : fallback;
        static int I(Dictionary<string, object> m, string k, int fallback = 0) => (int)D(m, k, fallback);
    }
}
