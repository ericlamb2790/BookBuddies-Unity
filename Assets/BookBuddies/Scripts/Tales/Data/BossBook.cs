using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Data/bosses.json: every town's Book Boss (the site's GYM with bbPhz, gymTeam, gymLvl and gymTab), exported by
    /// tools/export_bosses.js, and their rules: the badge you hold once a boss is beaten (TalesSave.Gyms), the seal on
    /// the road on from the third town, the fight, and the gift a win brings. Pure C#.
    /// </summary>
    public sealed class BossBook
    {
        /// <summary>One town's Book Boss: a storybook villain's body recoloured, with a small team of the town's foes.</summary>
        public sealed class Boss
        {
            public string Town, Name, Icon, Color, Body, Theme, Hall, Badge, BadgeIcon;
            public string Challenge, Beaten, Lost;  // what it says before the fight, when it loses, when you lose
            public string[] Team;                   // foe names (TQ_MIN or TQ_BOSS)
            public BossPhase[] Phases;
            public int Level;                       // gymLvl: the level badge, and the fight's level less one
            public double Mul;                      // the fight's multiplier: 1.15, and .07 more for each town along the route
            public FoeLoot Loot;                    // gymTab

            /// <summary>"Heartstring" for the Heartstring Badge.</summary>
            public string BadgeName => Badge.EndsWith(" Badge", StringComparison.Ordinal) ? Badge.Substring(0, Badge.Length - 6) : Badge;
        }

        static BossBook current;
        public static BossBook Current => current ?? (current = new BossBook(Json.ParseObject(TalesData.TextLoader("Data/bosses"))));

        /// <summary>The towns with a Book Boss, in route order (the badge row).</summary>
        public readonly string[] Route;
        /// <summary>BB_TW: each twist's icon and what it does ("💢", "Hits harder").</summary>
        public readonly Dictionary<string, (string icon, string text)> Twists = new Dictionary<string, (string, string)>();
        readonly Dictionary<string, Boss> bosses = new Dictionary<string, Boss>();

        public BossBook(Dictionary<string, object> j)
        {
            Route = TalesData.Strings(j.Arr("route"));
            var tw = j.Obj("twists");
            if (tw != null) foreach (var kv in tw) if (kv.Value is List<object> t && t.Count > 1) Twists[kv.Key] = ((string)t[0], (string)t[1]);
            var all = j.Obj("bosses");
            if (all != null) foreach (var kv in all) bosses[kv.Key] = Read(kv.Key, kv.Value as Dictionary<string, object>);
        }

        static Boss Read(string town, Dictionary<string, object> o)
        {
            var says = TalesData.Strings(o.Arr("say"));
            var badge = TalesData.Strings(o.Arr("bd"));
            var loot = o.Obj("loot");
            var phases = new List<BossPhase>();
            foreach (var x in o.Arr("phz"))
                if (x is Dictionary<string, object> p)
                    phases.Add(new BossPhase { Name = p.Str("n"), Say = p.Str("say"), Twists = TalesData.Strings(p.Arr("m")), Hp = p.Num("hp", 1), Atk = p.Num("atk", 1) });
            return new Boss
            {
                Town = town, Name = o.Str("n"), Icon = o.Str("i"), Color = o.Str("c"), Body = o.Str("b"), Theme = o.Str("th"), Hall = o.Str("hall"),
                Badge = badge[0], BadgeIcon = badge[1], Challenge = says[0], Beaten = says[1], Lost = says[2],
                Team = TalesData.Strings(o.Arr("team")), Phases = phases.ToArray(), Level = o.Int("lvl", 1), Mul = o.Num("mul", 1),
                Loot = new FoeLoot
                {
                    Theme = loot.Str("th"), Slots = TalesData.Strings(loot.Arr("sl")), Signature = loot.Str("sig"), Boss = true,
                    Rate = loot.Num("rate", 1), SignatureChance = loot.Num("sc", .15),
                },
            };
        }

        /// <summary>This town's boss, or null (the road's maps and the caves have none).</summary>
        public Boss Get(string town) => town != null && bosses.TryGetValue(town, out var b) ? b : null;

        /// <summary>gymBeat: you hold this town's badge.</summary>
        public static bool Beaten(TalesSave save, string town) => town != null && save.Gyms.ContainsKey(town);

        /// <summary>gymSeals: the road on from this town is sealed until its boss is beaten; the first two towns never seal it.</summary>
        public bool Seals(TalesSave save, string town) =>
            Array.IndexOf(TalesData.Current.Route, town) >= 2 && Get(town) != null && !Beaten(save, town);

        /// <summary>How many Book Badges you hold.</summary>
        public int Badges(TalesSave save)
        {
            int n = 0;
            foreach (var k in Route) if (Beaten(save, k)) n++;
            return n;
        }

        /// <summary>The fight's level: one over the boss's, and two more for a rematch (the battle caps it at 12).</summary>
        public static int FightLevel(Boss b, bool rematch) => b.Level + 1 + (rematch ? 2 : 0);

        /// <summary>gymTeam: the boss's team as plain villains (names the data doesn't have are left out, like the site).</summary>
        public static FoeVariant[] TeamOf(Boss b)
        {
            var d = TalesData.Current;
            var team = new List<FoeVariant>();
            foreach (var n in b.Team)
            {
                var def = d.Minions.Find(x => x.N == n) ?? d.Bosses.Find(x => x.N == n);
                if (def != null) team.Add(FoeFactory.Plain(def));
            }
            return team.ToArray();
        }

        /// <summary>gymFoe: the boss as a villain to fight: its body's villain renamed and recoloured, with its phases and team.</summary>
        public static FoeVariant Variant(Boss b)
        {
            var d = TalesData.Current;
            var body = d.Bosses.Find(x => x.N == b.Body) ?? d.Bosses[0];
            var def = new FoeDef
            {
                N = b.Name, I = b.Icon, C = b.Color, An = body.An, Sp = body.Sp, Sn = body.Sn, Si = body.Si, Rg = body.Rg, Sh = body.Sh, Mv = body.Mv,
                Say = b.Challenge, Says = new[] { b.Challenge }, G = body.G, Index = body.Index, Hp = body.Hp, Atk = body.Atk, Dodge = body.Dodge, IsBoss = true,
            };
            var v = FoeFactory.Plain(def);
            v.Phases = b.Phases;
            v.Team = TeamOf(b);
            return v;
        }

        /// <summary>
        /// gymFight's battle: the boss in the middle and its team (elite past Pawtopia), at the fight's level and
        /// multiplier, with your pet's HP and ink. Also gives the boss its own loot table for the win's drop.
        /// </summary>
        public static BattleSetup Setup(Boss b, bool rematch, double hp, int ink)
        {
            Loot.SetFoeTable(b.Name, b.Loot);
            bool elite = Array.IndexOf(TalesData.Current.Route, b.Town) >= 1;
            var s = new BattleSetup { Title = $"{b.Hall}: {b.Name}", Place = b.Hall, Lvl = FightLevel(b, rematch), Mul = b.Mul, BookBoss = true, HpFrac = hp, Ink = ink };
            s.Foes.Add((Variant(b), true, false));
            foreach (var v in TeamOf(b)) s.Foes.Add((v, false, elite));
            return s;
        }

        /// <summary>
        /// gymDone after a win: you hold the badge, and the first win (or the first rematch of a UTC day) brings a gift of
        /// the town's gear (rare or better the first time). A first win also counts as a boss beaten, which opens the
        /// boss classes (druid, paladin, warlock, dragoon). Saves; returns whether it was the first win and the gift.
        /// </summary>
        public static (bool first, LootDrop gift) Win(TalesSave save, Boss b, double nowMs, string today, IRng rng)
        {
            bool first = !Beaten(save, b.Town);
            save.Gyms[b.Town] = nowMs;
            LootDrop gift = null;
            string day = "b" + b.Town;
            if (first || !save.RoadDays.TryGetValue(day, out var last) || last != today)
            {
                save.RoadDays[day] = today;
                int? tier = first ? Math.Max(2, rng.Next() < .25 ? 3 : 2) : (int?)null;
                string item = Loot.RollItem("satchel", 4 + b.Level * 3 + HeroFactory.Renown(save.Me.Rxp).lvl, b.Theme, null, Loot.Luck(save), rng, tier);
                gift = Loot.Add(save, item);
            }
            if (first) HeroFactory.GainRenown(save, 0, "bosses", null);
            save.Touch();
            return (first, gift);
        }
    }
}
