using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The dungeon's state (the site's S.ep, epic.md §2.2 and §2.13), held by TaleRun.Ep: the saga's roll (Dark Author,
    /// relic, four acts), where the party stands on this act's map, and what the rooms have handed out (relics, curses,
    /// blessings for the next fight, the side quest). EpicSaga makes it, EpicRooms and EpicFlow change it.
    /// </summary>
    public sealed class EpicState
    {
        public string Kind = "dungeon", Name, Relic, Want;
        public int Saga = 1, Done, Lv0 = 1, Dark;     // saga number, lair bosses beaten (all sagas), start level, TQ_DARK index
        public readonly List<EpicAct> Acts = new List<EpicAct>();
        public int Act = 1, Spot, Tone;               // act 1-4, whose turn in the spotlight, kindness (+) or cleverness (−)
        public (int r, int c) At;                     // the node the party stands on
        public readonly List<(int r, int c)> Trail = new List<(int, int)>(); // nodes visited this act (after the start)
        public readonly List<string> Used = new List<string>();  // scenes already met ("e12", "d3", "c0"), so maps avoid them
        public readonly List<string> Rel = new List<string>(), Cur = new List<string>(); // relics and curses held this saga
        public EpicThread Th;                         // the side quest
        public List<List<MapNode>> Map = new List<List<MapNode>>();
        public string Hz;                             // this land's hazard (TQ_HZ key), null for none
        public string Ally;                           // TQ_NPC key joining the next fight
        public bool Ward, Bless, Curse, Weak, Rival;  // next fight: hazard off for the land, shields + ink, foes hit harder, boss hurt, rival captain
        public int Ink;                               // next fight: extra ink for every pet
        public int Insp;                              // added to the next roll
        public int Clear, Lamp;                       // act whose fog is lifted, act whose lamp reroll is spent (0 = none)
        public bool Intro = true, Rpick, Next, TwOn;  // act intro due, relic pick shown, new saga due, a twist room is open
        public int Dmg2;                              // rooms since the last Story Master twist

        /// <summary>This act's row.</summary>
        public EpicAct A => Acts[Act - 1];
        /// <summary>The node the party stands on.</summary>
        public MapNode Here => Map[At.r][At.c];

        /// <summary>The state as a JSON object.</summary>
        public Dictionary<string, object> ToJson()
        {
            var acts = new List<object>(); foreach (var a in Acts) acts.Add(a.ToJson());
            var map = new List<object>();
            foreach (var row in Map) { var r = new List<object>(); foreach (var n in row) r.Add(n.ToJson()); map.Add(r); }
            var trail = new List<object>(); foreach (var t in Trail) trail.Add(new List<object> { (double)t.r, (double)t.c });
            return new Dictionary<string, object>
            {
                ["kind"] = Kind, ["name"] = Name, ["relic"] = Relic, ["want"] = Want, ["saga"] = (double)Saga, ["done"] = (double)Done, ["lv0"] = (double)Lv0,
                ["dark"] = (double)Dark, ["acts"] = acts, ["act"] = (double)Act, ["spot"] = (double)Spot, ["tone"] = (double)Tone,
                ["at"] = new Dictionary<string, object> { ["r"] = (double)At.r, ["c"] = (double)At.c }, ["trail"] = trail, ["used"] = TalesSave.Strs(Used),
                ["rel"] = TalesSave.Strs(Rel), ["cur"] = TalesSave.Strs(Cur), ["th"] = Th?.ToJson(), ["map"] = map, ["hz"] = Hz, ["ally"] = Ally,
                ["ward"] = Ward, ["bless"] = Bless, ["curse"] = Curse, ["weak"] = Weak, ["rival"] = Rival, ["ink"] = (double)Ink, ["insp"] = (double)Insp,
                ["clear"] = (double)Clear, ["lamp"] = (double)Lamp, ["intro"] = Intro, ["rpick"] = Rpick, ["next"] = Next, ["twOn"] = TwOn, ["dmg2"] = (double)Dmg2,
            };
        }

        /// <summary>A state from ToJson's object; null for none.</summary>
        public static EpicState FromJson(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var at = o.Obj("at");
            var e = new EpicState
            {
                Kind = o.Str("kind", "dungeon"), Name = o.Str("name", null), Relic = o.Str("relic", null), Want = o.Str("want", null),
                Saga = o.Int("saga", 1), Done = o.Int("done"), Lv0 = o.Int("lv0", 1), Dark = o.Int("dark"), Act = o.Int("act", 1), Spot = o.Int("spot"), Tone = o.Int("tone"),
                At = (at.Int("r"), at.Int("c")), Th = EpicThread.FromJson(o.Obj("th")), Hz = o.Str("hz", null), Ally = o.Str("ally", null),
                Ward = o.Truthy("ward"), Bless = o.Truthy("bless"), Curse = o.Truthy("curse"), Weak = o.Truthy("weak"), Rival = o.Truthy("rival"),
                Ink = o.Int("ink"), Insp = o.Int("insp"), Clear = o.Int("clear"), Lamp = o.Int("lamp"),
                Intro = o.Truthy("intro"), Rpick = o.Truthy("rpick"), Next = o.Truthy("next"), TwOn = o.Truthy("twOn"), Dmg2 = o.Int("dmg2"),
            };
            foreach (var a in o.Arr("acts")) e.Acts.Add(EpicAct.FromJson((Dictionary<string, object>)a));
            foreach (List<object> t in o.Arr("trail")) e.Trail.Add(((int)(double)t[0], (int)(double)t[1]));
            e.Used.AddRange(TalesData.Strings(o.Arr("used")));
            e.Rel.AddRange(TalesData.Strings(o.Arr("rel")));
            e.Cur.AddRange(TalesData.Strings(o.Arr("cur")));
            foreach (List<object> row in o.Arr("map"))
            {
                var r = new List<MapNode>();
                foreach (Dictionary<string, object> n in row) r.Add(MapNode.FromJson(n));
                e.Map.Add(r);
            }
            return e;
        }
    }

    /// <summary>One act of a saga: its land, its boss (TQ_BOSS index, −1 = the Dark Author), the boss's tactics, the minion pool (5 TQ_MIN indices) and the lieutenant.</summary>
    public sealed class EpicAct
    {
        public string R;
        public int Boss, Lt;
        public readonly List<(double at, string m)> Ph = new List<(double, string)>();
        public int[] Pool = new int[0];

        public Dictionary<string, object> ToJson()
        {
            var ph = new List<object>(); foreach (var p in Ph) ph.Add(new List<object> { p.at, p.m });
            var pool = new List<object>(); foreach (int i in Pool) pool.Add((double)i);
            return new Dictionary<string, object> { ["r"] = R, ["boss"] = (double)Boss, ["ph"] = ph, ["pool"] = pool, ["lt"] = (double)Lt };
        }

        public static EpicAct FromJson(Dictionary<string, object> o)
        {
            var a = new EpicAct { R = o.Str("r"), Boss = o.Int("boss"), Lt = o.Int("lt"), Pool = o.Ints("pool") };
            foreach (List<object> p in o.Arr("ph")) a.Ph.Add(((double)p[0], (string)p[1]));
            return a;
        }
    }

    /// <summary>A place on the act's map: its type (start fight elite event danger chest shop rest boss), x in % across, its scene (−1 = none) and the next row's nodes it leads to.</summary>
    public sealed class MapNode
    {
        public string T;
        public int X, E = -1;
        public readonly List<int> To = new List<int>();

        public Dictionary<string, object> ToJson()
        {
            var to = new List<object>(); foreach (int j in To) to.Add((double)j);
            return new Dictionary<string, object> { ["t"] = T, ["x"] = (double)X, ["e"] = (double)E, ["to"] = to };
        }

        public static MapNode FromJson(Dictionary<string, object> o)
        {
            var n = new MapNode { T = o.Str("t"), X = o.Int("x"), E = o.Int("e", -1) };
            n.To.AddRange(o.Ints("to"));
            return n;
        }
    }

    /// <summary>The saga's side quest: find three of something (TQ_THR).</summary>
    public sealed class EpicThread
    {
        public string I, N;
        public int Need = 3, Got;

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["i"] = I, ["n"] = N, ["need"] = (double)Need, ["got"] = (double)Got };

        public static EpicThread FromJson(Dictionary<string, object> o) =>
            o == null ? null : new EpicThread { I = o.Str("i"), N = o.Str("n"), Need = o.Int("need", 3), Got = o.Int("got") };
    }

    /// <summary>
    /// What the tale shows between fights (the site's S.view; saved as TaleRun.View): k = map, enc, roll, scene, boon,
    /// relic or camp. Opts stay null until the view is filled; then a resumed tale shows the same choices.
    /// </summary>
    public sealed class EpicView
    {
        public string K, I, T;                        // kind, icon, title
        public string Text;                           // narration (enc), outcome text (roll)
        public readonly List<string> Lines = new List<string>(); // scene lines, roll effect lines
        public List<EpicOpt> Opts;
        public string Tp;                             // enc: event, danger or chest
        public int E = -1;                            // enc: the template's index
        public string Ch, Chk, Who, Then;             // roll: the choice, its check, the roller, a fight it starts
        public int Roll, B, Dc, Crit;                 // roll: d20, bonus, DC, 1 = natural 20, −1 = natural 1
        public bool Ok, Dm;                           // roll: success, a Story Master twist
        public int Drops;                             // boon: ink drops just won
        public string Boss;                           // camp: the lair boss just beaten

        public static EpicView Of(string k) => new EpicView { K = k };

        public Dictionary<string, object> ToJson()
        {
            List<object> opts = null;
            if (Opts != null) { opts = new List<object>(); foreach (var o in Opts) opts.Add(o.ToJson()); }
            return new Dictionary<string, object>
            {
                ["k"] = K, ["i"] = I, ["t"] = T, ["text"] = Text, ["lines"] = TalesSave.Strs(Lines), ["opts"] = opts, ["tp"] = Tp, ["e"] = (double)E,
                ["ch"] = Ch, ["chk"] = Chk, ["who"] = Who, ["then"] = Then, ["roll"] = (double)Roll, ["b"] = (double)B, ["dc"] = (double)Dc, ["crit"] = (double)Crit,
                ["ok"] = Ok, ["dm"] = Dm, ["drops"] = (double)Drops, ["boss"] = Boss,
            };
        }

        /// <summary>A view from ToJson's object; null for none.</summary>
        public static EpicView FromJson(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var v = new EpicView
            {
                K = o.Str("k", "map"), I = o.Str("i", null), T = o.Str("t", null), Text = o.Str("text", null), Tp = o.Str("tp", null), E = o.Int("e", -1),
                Ch = o.Str("ch", null), Chk = o.Str("chk", null), Who = o.Str("who", null), Then = o.Str("then", null),
                Roll = o.Int("roll"), B = o.Int("b"), Dc = o.Int("dc"), Crit = o.Int("crit"), Ok = o.Truthy("ok"), Dm = o.Truthy("dm"), Drops = o.Int("drops"), Boss = o.Str("boss", null),
            };
            v.Lines.AddRange(TalesData.Strings(o.Arr("lines")));
            if (o.Has("opts")) { v.Opts = new List<EpicOpt>(); foreach (Dictionary<string, object> x in o.Arr("opts")) v.Opts.Add(EpicOpt.FromJson(x)); }
            return v;
        }
    }

    /// <summary>One choice on a view: id (node type, boon or relic key), icon, name, description; map: the node it goes to; enc: the check, DC, roller and bonus.</summary>
    public sealed class EpicOpt
    {
        public string Id, I, N, D, Chk, Who;
        public int To, Dc, B;

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["id"] = Id, ["i"] = I, ["n"] = N, ["d"] = D, ["chk"] = Chk, ["who"] = Who, ["to"] = (double)To, ["dc"] = (double)Dc, ["b"] = (double)B,
        };

        public static EpicOpt FromJson(Dictionary<string, object> o) => new EpicOpt
        {
            Id = o.Str("id"), I = o.Str("i"), N = o.Str("n"), D = o.Str("d"), Chk = o.Str("chk", null), Who = o.Str("who", null), To = o.Int("to"), Dc = o.Int("dc"), B = o.Int("b"),
        };
    }
}
