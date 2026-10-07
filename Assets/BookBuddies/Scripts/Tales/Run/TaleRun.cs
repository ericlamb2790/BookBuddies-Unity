using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// One tale in progress or finished (the site's run state S, lobby.md §2.2): the party, the dungeon (Ep), the story
    /// log the storybook is made from, and the run stats. Pure C#; TaleStore saves it as JSON. Story entries, View and
    /// Book keep the site's JSON shape (double, string, bool, List&lt;object&gt;, Dictionary&lt;string, object&gt;).
    /// </summary>
    public sealed class TaleRun
    {
        /// <summary>Save format; a run saved with another version loses its in-flight Battle and View (migrate).</summary>
        public const int Version = 1;
        /// <summary>The story log's size; past it the oldest entry that isn't a key event goes (tell).</summary>
        public const int StoryCap = 220;
        static readonly HashSet<string> KeyEvents = new HashSet<string> { "begin", "join", "lost", "ended", "epic", "act", "saga", "dm" };

        public string Id, Title;
        public int Seed;                     // the run seed (mulberry32 for the saga, map, book…)
        public int Vol = 1, Ch = 1, Node;    // book number (newVolume), chapter (+1 per act boss), map row
        public int Gold = 30;                // 💧 ink drops, a run-only currency
        public readonly List<string> Boons = new List<string>();     // boon keys, one per stack
        public readonly List<TaleHero> Party = new List<TaleHero>();
        public readonly List<TaleCast> Cast = new List<TaleCast>();  // everyone who was in the tale (storybook)
        public readonly List<Dictionary<string, object>> Story = new List<Dictionary<string, object>>(); // tell's entries
        public TaleStats Stats = new TaleStats();
        public EpicState Ep;                 // the dungeon (S.ep); null before beginEpic
        public Dictionary<string, object> View; // the view on screen (EpicViews' shape), resumed as saved; null = none
        public TaleFight Battle;             // the fight under way; null between fights
        public bool Revived;                 // Second Wind used (once per tale)
        public bool CampNext;                // the Long Read camp comes before the next room (after a lair boss)
        public string CampBoss;              // the lair boss just beaten, for the camp's "story so far"
        public bool Over;
        public string Reason;                // "ended" or "defeat" once Over
        public Dictionary<string, object> Book; // StoryBook.Make's book once Over
        public string Daily;                 // "yyyy-MM-dd" for the daily tale; null for a small read

        /// <summary>Adds a story entry {k, ch, node, …d} (d's keys win) and keeps the log under StoryCap; returns the entry.</summary>
        public Dictionary<string, object> Tell(string k, Dictionary<string, object> d = null)
        {
            var e = new Dictionary<string, object> { ["k"] = k, ["ch"] = (double)Ch, ["node"] = (double)Node };
            if (d != null) foreach (var kv in d) e[kv.Key] = kv.Value;
            Story.Add(e);
            if (Story.Count > StoryCap)
            {
                int i = Story.FindIndex(1, x => !KeyEvents.Contains(x.Str("k")) && !x.Truthy("boss"));
                if (i > 0) Story.RemoveAt(i);
            }
            return e;
        }

        /// <summary>The run as a JSON object.</summary>
        public Dictionary<string, object> ToJson()
        {
            var party = new List<object>(); foreach (var h in Party) party.Add(h.ToJson());
            var cast = new List<object>(); foreach (var c in Cast) cast.Add(c.ToJson());
            return new Dictionary<string, object>
            {
                ["v"] = (double)Version, ["id"] = Id, ["title"] = Title, ["seed"] = (double)Seed, ["vol"] = (double)Vol, ["ch"] = (double)Ch, ["node"] = (double)Node,
                ["gold"] = (double)Gold, ["boons"] = TalesSave.Strs(Boons), ["party"] = party, ["cast"] = cast, ["story"] = new List<object>(Story),
                ["stats"] = Stats.ToJson(), ["ep"] = Ep?.ToJson(), ["view"] = View, ["battle"] = Battle?.ToJson(), ["revived"] = Revived,
                ["campNext"] = CampNext, ["campBoss"] = CampBoss, ["over"] = Over, ["reason"] = Reason, ["book"] = Book, ["daily"] = Daily,
            };
        }

        /// <summary>A run from ToJson's object (migrate: heroes with no snapshot are dropped; another Version drops Battle and View); null for none.</summary>
        public static TaleRun FromJson(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var r = new TaleRun
            {
                Id = o.Str("id", null), Title = o.Str("title", null), Seed = o.Int("seed"), Vol = o.Int("vol", 1), Ch = o.Int("ch", 1), Node = o.Int("node"),
                Gold = o.Int("gold"), Stats = TaleStats.FromJson(o.Obj("stats")), Ep = o.Has("ep") ? EpicState.FromJson(o.Obj("ep")) : null,
                View = o.Obj("view"), Battle = TaleFight.FromJson(o.Obj("battle")), Revived = o.Truthy("revived"),
                CampNext = o.Truthy("campNext"), CampBoss = o.Str("campBoss", null), Over = o.Truthy("over"), Reason = o.Str("reason", null),
                Book = o.Obj("book"), Daily = o.Str("daily", null),
            };
            r.Boons.AddRange(TalesData.Strings(o.Arr("boons")));
            foreach (var x in o.Arr("party")) { var h = TaleHero.FromJson(x as Dictionary<string, object>); if (h != null) r.Party.Add(h); }
            foreach (var x in o.Arr("cast")) if (x is Dictionary<string, object> c) r.Cast.Add(TaleCast.FromJson(c));
            foreach (var x in o.Arr("story")) if (x is Dictionary<string, object> e) r.Story.Add(e);
            if (o.Int("v") != Version) { r.Battle = null; r.View = null; }
            return r;
        }
    }

    /// <summary>A pet in a tale's party: its snapshot plus the run-local level, xp, HP, ink, known moves and statuses.</summary>
    public sealed class TaleHero
    {
        public HeroSeed Seed;                // the pet as it set out (HeroSnap); re-snapped when the bag closes mid-tale
        public int Lvl = 1, Xp, Ink;         // tale level (uncapped), xp toward Lvl + 1, ink carried between rooms
        public double Hp;                    // HP now; 0 = napping until a campfire, the alarm clock or Second Wind
        public readonly List<string> Kn = new List<string>(); // known moves (tqKit), grows with level-ups
        public readonly Dictionary<string, double> St = new Dictionary<string, double>(); // statuses carried between rooms
        public bool Ko => Hp <= 0;

        /// <summary>The hero as a JSON object.</summary>
        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["seed"] = HeroSnap.ToJson(Seed), ["lvl"] = (double)Lvl, ["xp"] = (double)Xp, ["hp"] = Hp, ["ink"] = (double)Ink,
            ["kn"] = TalesSave.Strs(Kn), ["st"] = TalesSave.Nums(St),
        };

        /// <summary>A hero from ToJson's object; null when it has no snapshot.</summary>
        public static TaleHero FromJson(Dictionary<string, object> o)
        {
            var seed = HeroSnap.FromJson(o.Obj("seed"));
            if (seed == null) return null;
            var h = new TaleHero { Seed = seed, Lvl = o.Int("lvl", 1), Xp = o.Int("xp"), Hp = o.Num("hp"), Ink = o.Int("ink") };
            h.Kn.AddRange(TalesData.Strings(o.Arr("kn")));
            HeroSnap.ReadNums(o.Obj("st"), h.St);
            return h;
        }
    }

    /// <summary>Someone in the storybook's cast (snapCast): id, name, owner, look, class, genre.</summary>
    public sealed class TaleCast
    {
        public string Uid, N, O = "", L, C;
        public int G;

        /// <summary>The cast entry for a hero seed (genre from its hue).</summary>
        public static TaleCast Of(HeroSeed s) => new TaleCast { Uid = s.PetKey, N = s.Name, L = s.Look, C = s.Cls, G = TalesData.GenreOfHue(s.Hue) };

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["uid"] = Uid, ["n"] = N, ["o"] = O, ["l"] = L, ["c"] = C, ["g"] = (double)G };

        public static TaleCast FromJson(Dictionary<string, object> o) =>
            new TaleCast { Uid = o.Str("uid", null), N = o.Str("n"), O = o.Str("o"), L = o.Str("l", null), C = o.Str("c", null), G = o.Int("g") };
    }

    /// <summary>The run's totals (freshStats): wins, bosses, foes beaten, hero crits, HP healed and the biggest hero hit.</summary>
    public sealed class TaleStats
    {
        public int Wins, Bosses, Foes, Crits, Heals;
        public (string n, string ab, double d)? Best; // pet name, move name, damage; null until a hero hits

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["wins"] = (double)Wins, ["bosses"] = (double)Bosses, ["foes"] = (double)Foes, ["crits"] = (double)Crits, ["heals"] = (double)Heals,
            ["best"] = Best.HasValue ? new Dictionary<string, object> { ["n"] = Best.Value.n, ["ab"] = Best.Value.ab, ["d"] = Best.Value.d } : null,
        };

        public static TaleStats FromJson(Dictionary<string, object> o)
        {
            var s = new TaleStats { Wins = o.Int("wins"), Bosses = o.Int("bosses"), Foes = o.Int("foes"), Crits = o.Int("crits"), Heals = o.Int("heals") };
            var b = o.Obj("best");
            if (b != null) s.Best = (b.Str("n"), b.Str("ab"), b.Num("d"));
            return s;
        }
    }

    /// <summary>
    /// The fight under way (S.battle). Set and saved before EpicBattle.Setup; whatever the setup spends (bless, ink, ally,
    /// weak) is cleared only once the fight is over, so a resumed tale rebuilds the same fight from the room's start.
    /// </summary>
    public sealed class TaleFight
    {
        public int Id;                       // counts fights in the run
        public int Seed;                     // the fight's own seed (co-op replays it)
        public string Kind = TaleBattle.Fight; // fight, elite or boss

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["id"] = (double)Id, ["seed"] = (double)Seed, ["kind"] = Kind };

        /// <summary>The fight from ToJson's object; null for none.</summary>
        public static TaleFight FromJson(Dictionary<string, object> o) =>
            o == null ? null : new TaleFight { Id = o.Int("id"), Seed = o.Int("seed"), Kind = o.Str("kind", TaleBattle.Fight) };
    }
}
