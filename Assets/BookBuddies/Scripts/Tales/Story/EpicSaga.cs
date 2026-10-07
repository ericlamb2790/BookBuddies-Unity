using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The saga and its maps (beginEpic, actIntro, epMap, epOpts; tales.js T:930-978): who stole what, the four lands,
    /// each act's branching map, and where the party can go next. Everything here is seeded from the run, so a seed and
    /// volume always give the same saga and maps.
    /// </summary>
    public static class EpicSaga
    {
        const int Rows = 7;                           // trailhead, five rows of places, the lair
        static readonly string[] Tactics = { "summon", "enrage", "shield", "heal" };
        static readonly string[] Verbs = { "has stolen", "has hidden", "has swallowed", "has locked away" };

        /// <summary>beginEpic: rolls a new saga (the next one when the run already has one), its first map, and shows the prologue.</summary>
        public static void Begin(TaleRun run, string kind = null)
        {
            var d = TalesData.Current; var data = EpicData.Current;
            var old = run.Ep;
            var r = new Mulberry32(unchecked((uint)((long)(run.Seed ^ 0x5eed) + (old != null ? old.Saga * 7919L : 0) + Math.Max(1, run.Vol) * 31L)));
            int dark = (int)Math.Floor(r.Next() * d.Dark.Count);
            if (old != null && dark == old.Dark) dark = (dark + 1) % d.Dark.Count;
            var D = d.Dark[dark];
            string relic = Pick(r, data.RelicNames.Where(x => old == null || x != old.Relic).ToList());
            var soft = Shuffle(r, data.Lands.Where(x => x.Soft).ToList());
            var hard = Shuffle(r, data.Lands.Where(x => !x.Soft && (old == null || !old.Acts.Exists(a => a.R == x.K))).ToList());
            var bo = Shuffle(r, Enumerable.Range(0, d.Bosses.Count).ToList());
            var me = Shuffle(r, Tactics.ToList());
            int Bf(Land l, int i) { int j = l.Bn == null ? -1 : d.Bosses.FindIndex(b => b.N == l.Bn); return j >= 0 ? j : bo[i]; }
            EpicAct Act(string land, int boss, int lt, params (double, string)[] ph)
            {
                var a = new EpicAct { R = land, Boss = boss, Lt = lt, Pool = Shuffle(r, Enumerable.Range(0, d.Minions.Count).ToList()).Take(5).ToArray() };
                a.Ph.AddRange(ph);
                return a;
            }
            var E = new EpicState
            {
                Kind = kind ?? old?.Kind ?? "dungeon", Saga = old != null ? old.Saga + 1 : 1, Done = old?.Done ?? 0, Lv0 = old?.Lv0 ?? Math.Max(1, run.Ch),
                Dark = dark, Relic = relic, Want = D.Want,
            };
            E.Acts.Add(Act(soft[0].K, Bf(soft[0], 0), bo[3], (.5, me[0])));
            E.Acts.Add(Act(hard[0].K, Bf(hard[0], 1), bo[4], (.5, me[1])));
            E.Acts.Add(Act(hard[1].K, Bf(hard[1], 2), bo[5], (.6, me[2]), (.3, me[3])));
            E.Acts.Add(Act("lair", -1, bo[6], (.66, "summon"), (.33, "enrage")));
            if (old != null) E.Used.AddRange(old.Used.Skip(Math.Max(0, old.Used.Count - 20)));
            var th = data.Threads[(int)Math.Floor(r.Next() * data.Threads.Count)];
            E.Th = new EpicThread { I = th.i, N = th.n };
            E.Name = Pick(r, data.Titles).Replace("{relic}", relic).Replace("{dark}", D.N).Replace("{lair}", Cap(D.Lair));
            run.Ep = E;
            run.Title = E.Name + (E.Saga > 1 ? " · Saga " + Roman(E.Saga) : "");
            E.Map = Map(run, 1);
            E.Hz = LandOf(E).Hz;
            run.Node = 0;

            string names = List(run.Party.ConvertAll(h => h.Seed.Name));
            var lands = E.Acts.Take(3).Select(a => data.Land(a.R).N).ToList();
            bool party = run.Party.Count > 1;
            var lines = new List<string>
            {
                old != null ? $"The {old.Relic} is safe, but the story isn’t finished with {names}."
                    : party ? $"{names} are no longer reading alone. The book grows heavier in their paws, and the next page is a map."
                    : $"{names} opens a book that is much heavier than it looks. The first page is a map.",
                $"{D.N} {Pick(r, Verbs)} the {relic}. Without it, {D.Want}.",
                $"Three lands stand between you and {D.Lair}: {List(lands)}.",
                party ? "The journey only ends if the whole party falls." : $"The journey only ends if {names} falls.",
            };
            run.Tell("epic", new Dictionary<string, object> { ["t"] = E.Name, ["bt"] = lines[1] + " " + lines[2] });
            EpicFlow.Scene(run, "🗺️", E.Name, lines);
        }

        /// <summary>actIntro: the land's lines, its hazard and boss, the side quest in act I; tells the storybook and shows the scene.</summary>
        public static void ActIntro(TaleRun run)
        {
            var E = run.Ep; var R = LandOf(E); var B = Boss(E, E.Act);
            var c = EpicRooms.Ctx(run);
            var lines = R.Lines.Select(x => EpicRooms.Fill(x, c)).ToList();
            if (R.Hz != null) { var h = EpicData.Current.Hazards[R.Hz]; lines.Add($"{h.i} Beware the {h.n}. {h.d}"); }
            lines.Add(E.Act == 4 ? $"{B.N} waits at the very top. This is the last land." : $"Somewhere ahead waits {B.N}.");
            if (E.Act == 1 && E.Th != null && E.Th.Got == 0) lines.Add($"📜 Side quest: find {E.Th.N}.");
            run.Tell("act", new Dictionary<string, object> { ["t"] = $"Act {Roman(E.Act)}: {R.N}", ["bt"] = string.Join(" ", lines.Take(2)), ["sc"] = (double)R.Sc });
            EpicFlow.Scene(run, R.I, $"Act {Roman(E.Act)} · {R.N}", lines);
        }

        /// <summary>epMap: the act's map, bottom to top: the trailhead, five rows of two or three places, the lair; seeded per saga and act.</summary>
        public static List<List<MapNode>> Map(TaleRun run, int act)
        {
            var E = run.Ep; var data = EpicData.Current;
            var r = new Mulberry32(unchecked((uint)(run.Seed + E.Saga * 977L + act * 131L + Math.Max(1, run.Vol) * 17L)));
            var rows = new List<List<MapNode>> { new List<MapNode> { new MapNode { T = "start", X = 50 } } };
            for (int i = 1; i < Rows - 1; i++)
            {
                int n = i == 1 ? 2 + (r.Next() < .5 ? 1 : 0) : 2 + (int)Math.Floor(r.Next() * 2);
                var row = new List<MapNode>();
                for (int c = 0; c < n; c++)
                {
                    string t = PickNode(r, i);
                    for (int g = 0; row.Exists(x => x.T == t) && g < 5; g++) t = PickNode(r, i);
                    row.Add(new MapNode { T = t, X = -1 });
                }
                rows.Add(row);
            }
            rows[Rows - 2][(int)Math.Floor(r.Next() * rows[Rows - 2].Count)].T = "rest";
            if (!rows.Skip(2).Take(3).Any(w => w.Exists(x => x.T == "shop" || x.T == "rest"))) rows[3][0].T = "shop";
            rows.Add(new List<MapNode> { new MapNode { T = "boss", X = 50 } });

            var used = new HashSet<string>(E.Used);
            int Draw(string t, int count)
            {
                int e = (int)Math.Floor(r.Next() * count);
                for (int g = 0; used.Contains(t + e) && g < count; g++) e = (e + 1) % count;
                used.Add(t + e);
                return e;
            }
            foreach (var row in rows)
                for (int c = 0; c < row.Count; c++)
                {
                    var nd = row[c];
                    if (nd.X < 0) nd.X = Math.Max(12, Math.Min(88, JsMath.RoundI((c + 1) / (double)(row.Count + 1) * 100 + (r.Next() - .5) * 12)));
                    if (nd.T == "event") nd.E = Draw("e", data.Encounters.Count);
                    if (nd.T == "danger") nd.E = Draw("d", data.Dangers.Count);
                    if (nd.T == "chest") nd.E = Draw("c", data.Chests.Count);
                }

            for (int i = 0; i < Rows - 1; i++)
            {
                var A = rows[i]; var B = rows[i + 1];
                foreach (var a in A)
                {
                    var near = Enumerable.Range(0, B.Count).OrderBy(j => Math.Abs(B[j].X - a.X)).ToList();
                    a.To.Add(near[0]);
                    if (near.Count > 1 && Math.Abs(B[near[1]].X - a.X) < 40 && r.Next() < .65) a.To.Add(near[1]);
                }
                for (int j = 0; j < B.Count; j++)
                    if (!A.Exists(a => a.To.Contains(j))) A[Enumerable.Range(0, A.Count).OrderBy(q => Math.Abs(A[q].X - B[j].X)).First()].To.Add(j);
                foreach (var a in A) a.To.Sort();
            }
            return rows;
        }

        /// <summary>epOpts: where the party can go from here, with each place's name and line.</summary>
        public static List<EpicOpt> Options(TaleRun run)
        {
            var E = run.Ep; var d = TalesData.Current; var data = EpicData.Current;
            var dark = d.Dark[E.Dark % d.Dark.Count];
            var next = E.At.r + 1 < E.Map.Count ? E.Map[E.At.r + 1] : new List<MapNode>();
            var opts = new List<EpicOpt>();
            foreach (int j in E.Here.To)
            {
                if (j >= next.Count) continue;
                var nd = next[j];
                var (i, n) = data.Nodes[nd.T];
                var o = new EpicOpt { Id = nd.T, I = i, N = n, D = "", To = j };
                switch (nd.T)
                {
                    case "fight": o.D = $"Minions of {dark.N} are on the road."; break;
                    case "elite": o.N = d.Bosses[E.A.Lt % d.Bosses.Count].N; o.D = $"A lieutenant of {dark.N}. A tough fight with rich rewards."; break;
                    case "event": o.N = data.Encounters[nd.E].T; o.D = "A scene to play out together. Someone rolls the dice."; break;
                    case "danger": o.N = data.Dangers[nd.E].T; o.D = "Pass a check, or everyone gets hurt."; break;
                    case "chest": o.N = data.Chests[nd.E].T; o.D = "Loot, if you’re careful."; break;
                    case "rest": o.D = "Rest and heal, or train if everyone feels fine."; break;
                    case "shop": o.D = "A wandering bookseller. Spend your ink drops."; break;
                    case "boss":
                        var b = Boss(E, E.Act);
                        o.N = b.N; o.I = b.I;
                        o.D = E.Act == 4 ? $"The final battle. {b.N} changes tactics twice." : $"The ruler of {LandOf(E).N}. It changes tactics when hurt.";
                        break;
                }
                opts.Add(o);
            }
            return opts;
        }

        /// <summary>Fog (T:1183): a place more than two rows ahead stays "Unexplored" unless it is the lair or the fog is lifted.</summary>
        public static bool Fogged(EpicState E, int row, MapNode nd) =>
            row > E.At.r + 2 && nd.T != "boss" && E.Clear != E.Act && !E.Rel.Contains("a_map");

        /// <summary>epReg: the act's land (the lair in act IV).</summary>
        public static Land LandOf(EpicState E, int act = 0)
        {
            var x = E.Acts[(act > 0 ? act : E.Act) - 1];
            return x.R == "lair" ? Lair(E) : EpicData.Current.Land(x.R) ?? EpicData.Current.Lands[0];
        }

        /// <summary>epBoss: the act's boss (TQ_BOSS), or the Dark Author in act IV.</summary>
        public static FoeDef Boss(EpicState E, int act)
        {
            var d = TalesData.Current;
            return act == 4 ? Dark(E) : d.Bosses[E.Acts[act - 1].Boss % d.Bosses.Count];
        }

        /// <summary>epDark: the Dark Author behind the saga.</summary>
        public static FoeDef Dark(EpicState E) => TalesData.Current.Dark[E.Dark % TalesData.Current.Dark.Count];

        /// <summary>epLair: the Dark Author's lair as a land.</summary>
        public static Land Lair(EpicState E)
        {
            var d = Dark(E);
            return new Land
            {
                K = "lair", N = Cap(d.Lair), I = "🏰", Sc = 4, Hz = "whisper", Color = "#4e3658", Ink = "#160c1e",
                Deco = new[] { "🕯️", "🦇", "⛓️", "🪦" },
                Places = new[] { "the gatehouse", "the throne of blank pages", "the stair of erased names", "the final door" },
                Lines = new[] { $"At last: {Cap(d.Lair)}. The air tastes like an ending.", $"{d.N} has been waiting for you. It has always been waiting." },
            };
        }

        /// <summary>epLvl: the foes' level here (fractional): the saga's start level, 1.6 per lair boss beaten, .22 per map row.</summary>
        public static double Level(EpicState E) => E.Lv0 + E.Done * 1.6 + E.At.r * .22;

        static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>tqRoman: 1-10 as Roman numerals.</summary>
        public static string Roman(int n) => n >= 1 && n <= 10 ? Numerals[n - 1] : n.ToString();

        /// <summary>tqList: "a, b and c".</summary>
        public static string List(IList<string> a)
        {
            var l = a.Where(x => !string.IsNullOrEmpty(x)).ToList();
            return l.Count < 2 ? (l.Count == 1 ? l[0] : "") : string.Join(", ", l.Take(l.Count - 1)) + " and " + l[l.Count - 1];
        }

        /// <summary>tqCap: first letter upper case.</summary>
        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // the room type for a map row, by the site's weights (one draw, walking the list)
        static string PickNode(IRng r, int row)
        {
            var w = row == 1 ? new[] { ("fight", 4), ("event", 4), ("chest", 2) }
                : row == Rows - 2 ? new[] { ("fight", 3), ("event", 3), ("shop", 2), ("elite", 2) }
                : new[] { ("fight", 28), ("event", 30), ("elite", 12), ("danger", 10), ("shop", 8), ("chest", 7), ("rest", 5) };
            double v = r.Next() * w.Sum(x => x.Item2);
            foreach (var (t, n) in w) if ((v -= n) < 0) return t;
            return "fight";
        }

        static T Pick<T>(IRng r, IList<T> a) => a[(int)Math.Floor(r.Next() * a.Count)];

        // sh: each item gets one draw in array order, then a stable sort by it
        static List<T> Shuffle<T>(IRng r, List<T> a) => a.Select(x => (k: r.Next(), x)).ToList().OrderBy(p => p.k).Select(p => p.x).ToList();
    }
}
