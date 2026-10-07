using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BookBuddies.Live;
using BookBuddies.Local;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;
using Msg = System.Collections.Generic.Dictionary<string, object>;

namespace BookBuddies.Road
{
    /// <summary>
    /// One battle for the whole party in a hosted world. A Book Boss or the cave guardian asks everyone online first
    /// (the ready check, PartyReadySheet) and whoever's ready is brought in beside the challenger; a road pack that
    /// catches several friends at once is one fight for all of them. Every game in the fight runs the same battle in step
    /// with the captain's (the challenger's, or the one the foes caught): it rolls each step on a fresh seed and sends it
    /// on with everyone's inputs and the state after it. If the captain goes quiet the others carry on alone, and a
    /// friend who drops out only holds the fight up for a few seconds. Each player still plays their own pet and gets
    /// their own rewards and ending. The messages are "pb" {t, k, f: the fight's id, …}, which the room passes to every
    /// member in any town, marked with the sender (LocalParty.Battle). Online nothing changes: Active is false and every
    /// fight is your own.
    /// </summary>
    public static class PartyBattle
    {
        const float Wait = 60;        // seconds a ready check waits for answers
        const float AllReady = 1.2f;  // "All ready!" for a moment, then the fight starts by itself
        const float Linger = 60;      // a member lets go of a ready check this long after its wait (its captain went quiet)
        const float GatherFor = 3;    // seconds the captain of a road fight waits for the others' pets
        const float GiveUp = 6;       // a road fighter who hears no start by then fights on its own
        const float PullIn = .9f;     // the poof and the banner before a boss fight opens
        const float Quiet = 8;        // a follower who hears nothing from the captain for this long carries on alone
        const float Again = 1;        // a follower asks for a missing step again at most this often
        const float Hold = 3;         // the longest the captain holds the fight for a friend who's behind
        const float LaneGap = .25f;   // a follower's lane moves go at most this often (a visitor's link takes 20 messages a second)
        const int MostPets = 6;       // pets in one fight
        const int MostStart = 13000;  // characters in a start (the room takes 15000; each pet is about 1500)
        const int Deciding = 0, Ready = 1, NotNow = 2, InFight = 3; // ReadyMember.State

        static Func<PlazaWorld> current;
        static Check check;      // the ready check you're asking or answering
        static Gathering road;   // a road fight waiting for its pets
        static Link fight;       // the party fight on your screen
        static int fights;       // fights you've called: each id is "<your id>:<n>"
        static float joinBy;     // a boss fight you're pulled into opens before then

        /// <summary>Hooks the party's battles up; "world" gives the town you're in when a message comes.</summary>
        public static void Register(Func<PlazaWorld> world) => current = world;

        static PlazaWorld World => current?.Invoke();
        static string You => PartyPanel.You;

        /// <summary>Fights are shared: you're connected to a hosted world with someone else in the party online.</summary>
        public static bool Active
        {
            get
            {
                var w = World;
                return w != null && w.Net != null && w.Net.IsLive && PartyPanel.InWorld && You.Length > 0 && Online().Count > 1;
            }
        }

        /// <summary>You're asking the party to fight with you (the cave guardian waits meanwhile).</summary>
        public static bool Asking => check != null && check.Captain == You;

        /// <summary>A boss fight is about to open for you (the poof and the banner): the road's foes leave you be meanwhile.</summary>
        public static bool Joining => Time.unscaledTime < joinBy;

        // ---- a boss: the ready check ----

        /// <summary>
        /// A boss fight (kind "guardian", or "book:" + town): with the party online, everyone gets the ready check with
        /// the boss's card, and whoever's ready fights it beside you. It starts by itself once everyone's ready; once
        /// someone says not now or the wait runs out you may go with whoever's ready, or call it off (done gets null).
        /// Without a party it's your own fight, as always.
        /// </summary>
        public static void AskBoss(string kind, BattleSetup setup, ReadyInfo info, Vector2 from, Action<BattleOutcome> done)
        {
            var w = World;
            if (!Active || Asking || road != null) { Run(setup, from, done); return; }
            if (check != null) // you'd answered a friend's ready check: not now after all
            {
                Send("ready", check.F, "ok", 0.0);
                PartyReadySheet.Close(null);
            }
            var who = Online();
            if (who.Count > MostPets) who.RemoveRange(MostPets, who.Count - MostPets); // only as many as can fight are asked
            int n = who.Count;
            var (hp, atk) = BattleEngine.PartyScale(n);
            if (string.IsNullOrEmpty(info.Scale)) info.Scale = $"Tougher for a party of {n}: {Times(hp)} HP, {Times(atk)} attack";
            var c = check = new Check
            {
                F = You + ":" + ++fights, Captain = You, Who = who, Info = info, Ends = Time.unscaledTime + Wait,
                Kind = kind, Setup = setup, From = from, Done = done,
            };
            c.States[You] = Ready;
            Send("ask", c.F, "who", Words(who), "info", Write(info), "wait", (double)Wait);
            Show(c);
            w.StartCoroutine(Ticking(c));
        }

        // a friend's ready check: answered "in a fight" at once when you are, else the card
        static void Asked(string from, string f, Msg m)
        {
            var w = World;
            var who = Strings(m.Arr("who"));
            if (w == null || !who.Contains(You) || !f.StartsWith(from + ":", StringComparison.Ordinal)) return;
            if (BattleScreen.Open || Joining || road != null || Asking || (w.Road != null && w.Road.Fighting)) { Send("ready", f, "ok", 0.0, "busy", 1.0); return; }
            if (!Buddy.Hatched) { Send("ready", f, "ok", 0.0); return; }
            if (check != null) // a newer ask takes the place of the one before: its captain hears you're not coming after all
            {
                Send("ready", check.F, "ok", 0.0);
                PartyReadySheet.Close(null);
            }
            var c = check = new Check { F = f, Captain = from, Who = who, Info = Read(m.Obj("info")), Ends = Time.unscaledTime + Mathf.Clamp((float)m.Num("wait", Wait), 1, Wait) };
            c.States[from] = Ready;
            Show(c);
            w.StartCoroutine(Ticking(c));
        }

        // your answer: ready sends your pet as it is right now; not now is done with the card
        static void Answer(Check c, bool yes)
        {
            if (check != c || c.Answered) return;
            c.Answered = true;
            c.States[You] = yes ? Ready : NotNow;
            if (!yes) { check = null; Send("ready", c.F, "ok", 0.0); return; }
            Send("ready", c.F, "ok", 1.0, "hero", BattleWire.Party(Mine()));
        }

        // someone's answer: the roster changes on every card, and the captain keeps their pet for the fight
        static void Answered(string from, string f, Msg m)
        {
            bool ok = m.Truthy("ok") && m.Obj("hero") != null;
            var r = road;
            if (r != null && r.F == f)
            {
                if (r.Captain && ok && r.Party.Count < MostPets && !r.Party.Exists(p => p.Pid == from)) Take(r.Party, from, m.Obj("hero"));
                return;
            }
            var c = check;
            if (c == null || c.F != f || from == c.Captain || !c.Who.Contains(from)) return;
            c.States[from] = ok ? Ready : m.Truthy("busy") ? InFight : NotNow;
            if (c.Captain == You)
            {
                if (ok) c.Heroes[from] = m.Obj("hero");
                else c.Heroes.Remove(from);
            }
            Refresh(c);
        }

        // the captain goes with whoever's ready (on Go, or a moment after the last one said so)
        static void Go(Check c)
        {
            if (check != c) return;
            check = null;
            PartyReadySheet.Close(null);
            var party = new List<PartyHero> { Mine(c.Setup.HpFrac, c.Setup.Ink) };
            foreach (var pid in c.Who)
                if (party.Count < MostPets && c.Heroes.TryGetValue(pid, out var hero)) Take(party, pid, hero);
            if (party.Count > 1) { Start(c.F, c.Kind, c.Setup, party, c.From, c.Done); return; }
            Send("cancel", c.F, "why", "alone");
            Run(c.Setup, c.From, c.Done);
        }

        static void CallOff(Check c)
        {
            if (check != c) return;
            check = null;
            Send("cancel", c.F, "why", "off");
            c.Done?.Invoke(null);
        }

        // the captain called it off, or went alone
        static void Cancelled(string from, string f, Msg m)
        {
            var c = check;
            if (c == null || c.F != f || from != c.Captain) return;
            check = null;
            PartyReadySheet.Close(m.Str("why") == "alone" ? $"{Name(from)} went on alone" : $"{Name(from)} called it off");
        }

        // a ready check while it's up: the roster and time left each second, and the captain's fight starting by itself a
        // moment after everyone's ready; a member caught by a fight of their own meanwhile can't come after all (the card
        // went with the battle), and a member lets go of a check whose captain went quiet
        static IEnumerator Ticking(Check c)
        {
            float shown = 0, go = -1;
            while (check == c)
            {
                float now = Time.unscaledTime;
                if (c.Captain != You && BattleScreen.Open)
                {
                    check = null;
                    Send("ready", c.F, "ok", 0.0, "busy", 1.0);
                    yield break;
                }
                if (c.Captain != You && now > c.Ends + Linger)
                {
                    check = null;
                    PartyReadySheet.Close("The party battle didn’t start");
                    yield break;
                }
                if (c.Captain == You)
                {
                    if (!c.Who.TrueForAll(p => c.State(p) == Ready)) go = -1;
                    else if (go < 0) go = now + AllReady;
                    else if (now >= go) { Go(c); yield break; }
                }
                if (now >= shown)
                {
                    shown = now + 1;
                    Refresh(c);
                }
                yield return null;
            }
        }

        static void Show(Check c) =>
            PartyReadySheet.Show(c.Info, Roster(c), c.Ends - Time.unscaledTime, c.Captain == You, () => Answer(c, true), () => Answer(c, false), () => Go(c), () => CallOff(c));

        // the card's roster and time left; the captain counts anyone who's gone offline (or out of every room) as not coming
        static void Refresh(Check c)
        {
            if (c.Captain == You)
            {
                var online = Online();
                foreach (var pid in c.Who) if (c.State(pid) == Deciding && !online.Contains(pid)) c.States[pid] = NotNow;
            }
            float left = c.Ends - Time.unscaledTime;
            PartyReadySheet.Refresh(Roster(c), left, left <= 0 || c.States.ContainsValue(NotNow) || c.States.ContainsValue(InFight));
        }

        // everyone asked, as the card shows them
        static List<ReadyMember> Roster(Check c)
        {
            var party = PartyPanel.Members();
            return c.Who.ConvertAll(pid =>
            {
                var m = party.Find(p => p.id == pid);
                return new ReadyMember
                {
                    Pid = pid, Name = m.id != null ? m.name : "A friend", Look = m.look ?? "", Leader = m.leader,
                    Captain = pid == c.Captain, You = pid == You, State = c.State(pid),
                };
            });
        }

        // ---- the road: a pack caught several friends ----

        /// <summary>
        /// A road pack caught several friends at once (RoadFoes, after the wipe): fid is the room's fight, captain the
        /// room id of the pet it caught, who everyone in the fight (room ids, the captain first). Everyone else sends their
        /// pet and the captain starts with whoever's came within 3 s; anyone left out gets done(null). With no start
        /// (the one caught couldn't come) or nobody else, it's a fight of your own. A pack made for friends who didn't
        /// come is cut to the foes the pets that fight would meet.
        /// </summary>
        public static void Road(int fid, string captain, List<string> who, BattleSetup setup, Vector2 from, Action<BattleOutcome> done)
        {
            var w = World;
            bool lead = captain == w.Me.Id;
            var r = road = new Gathering
            {
                F = $"r:{w.Map.Key}:{captain}:{fid}", Captain = lead, Want = Math.Min(who.Count, MostPets), Setup = setup, From = from, Done = done,
                Until = Time.unscaledTime + (lead ? GatherFor : GiveUp),
            };
            r.Party.Add(Mine(setup.HpFrac, setup.Ink));
            w.StartCoroutine(Gather(r));
        }

        // the captain waits for the pets (all of them, or 3 s); anyone else sends theirs each second until the start (the
        // captain's game may not be gathering yet, or a message went missing)
        static IEnumerator Gather(Gathering r)
        {
            var hero = r.Captain ? null : BattleWire.Party(r.Party[0]);
            float again = 0;
            while (road == r && r.Party.Count < r.Want && Time.unscaledTime < r.Until)
            {
                if (hero != null && Time.unscaledTime >= again)
                {
                    again = Time.unscaledTime + 1;
                    Send("ready", r.F, "ok", 1.0, "hero", hero);
                }
                yield return null;
            }
            if (road != r) yield break;
            road = null;
            bool together = r.Captain && r.Party.Count > 1;
            RoadFoes.ForPets(r.Setup, together ? r.Party.Count : 1);
            if (together) Start(r.F, "road", r.Setup, r.Party, r.From, r.Done);
            else Run(r.Setup, r.From, r.Done);
        }

        // ---- the fight ----

        // the captain's start: everyone named comes in, in this order, and the fight opens here too
        static void Start(string f, string kind, BattleSetup setup, List<PartyHero> party, Vector2 from, Action<BattleOutcome> done)
        {
            var w = World;
            setup.Party.Clear();
            setup.Party.AddRange(party);
            var wire = BattleWire.Setup(setup);
            while (setup.Party.Count > 2 && Json.Write(wire).Length > MostStart) // it must fit in one message: the last to answer stay out
            {
                setup.Party.RemoveAt(setup.Party.Count - 1);
                wire = BattleWire.Setup(setup);
            }
            var t = w.Me.Tile;
            Send("start", f, "who", Words(setup.Party.ConvertAll(p => p.Pid)), "setup", wire, "kind", kind,
                "at", new Msg { ["town"] = w.Map.Key, ["x"] = (double)t.x, ["y"] = (double)t.y });
            Open(w, f, You, kind, wire, from, done);
        }

        // the captain's fight starts: you're in it when it names you
        static void Started(string from, string f, Msg m)
        {
            var w = World;
            if (w == null) return;
            bool named = Strings(m.Arr("who")).Contains(You);
            var r = road;
            if (r != null && r.F == f && !r.Captain)
            {
                road = null;
                if (named) Join(w, from, m, r.From, r.Done);
                else r.Done?.Invoke(null); // the others went without you
                return;
            }
            var c = check;
            if (c == null || c.F != f || from != c.Captain) return;
            check = null;
            PartyReadySheet.Close(named ? null : "The party went without you");
            string kind = m.Str("kind");
            if (named) Join(w, from, m, Point(w.Me.Pos), o => After(kind, o));
        }

        // into the captain's fight: poofed in beside them when you're in the same place, then the battle
        static void Join(PlazaWorld w, string captain, Msg m, Vector2 from, Action<BattleOutcome> done)
        {
            string kind = m.Str("kind");
            if (Elsewhere(w, captain, kind)) { done?.Invoke(null); return; } // in another fight: not poofed away from it
            var at = m.Obj("at");
            var to = new Vector2Int(at.Int("x") + 1, at.Int("y"));
            var spot = new Vector2(to.x + .5f, to.y + .5f);
            if (at.Str("town") == w.Map.Key && Vector2.Distance(w.Me.Pos, spot) > 2)
            {
                w.PoofTo(to, null);
                from = Point(spot);
            }
            var boss = kind.StartsWith("book:", StringComparison.Ordinal) ? BossBook.Current.Get(kind.Substring(5)) : null;
            if (boss != null) Loot.SetFoeTable(boss.Name, boss.Loot); // the boss's own drops, as its challenger's game has them
            Open(w, m.Str("f"), captain, kind, m.Obj("setup"), from, done);
        }

        // the fight on your screen, built from the captain's setup with you as your own pet, after the party's banner
        static void Open(PlazaWorld w, string f, string captain, string kind, Msg wire, Vector2 from, Action<BattleOutcome> done)
        {
            BattleSetup setup = null;
            try { setup = BattleWire.Setup(wire, "h:" + You); }
            catch (Exception) { } // a setup that won't read: no fight
            // nor while you're in another one (a foe that caught you meanwhile): this one goes on without you
            if (Elsewhere(w, captain, kind) || setup == null || setup.Foes.Count == 0 || !setup.Party.Exists(p => p.Unit.Key == setup.Me)) { done?.Invoke(null); return; }
            foreach (var p in setup.Party) p.Unit.Name = Clean(p.Unit.Name, 24, "A pet"); // the same in every game: names only show
            var link = fight = new Link(f, captain, setup.Party);
            w.Announce("⚔️ Party battle!", $"{setup.Party.Count} pets vs {setup.Foes[0].v.Name}");
            if (kind == "road") { Run(setup, from, done, link); return; }
            joinBy = Time.unscaledTime + PullIn + 1;
            w.StartCoroutine(Later(PullIn, () =>
            {
                joinBy = 0;
                if (!Elsewhere(w, captain, kind)) Run(setup, from, done, link);
                else { link.Closed(); done?.Invoke(null); }
            }));
        }

        // you're in another fight: one on screen, or (pulled into a friend's boss fight) one a foe on the road started
        static bool Elsewhere(PlazaWorld w, string captain, string kind) =>
            BattleScreen.Open || kind != "road" && captain != You && w.Road != null && w.Road.Fighting;

        // a battle opens over the town: a Tales screen that's up (the bag, a tale) closes first, so the fight isn't hidden
        // under it and doesn't keep its lock on walking when it ends
        static void Run(BattleSetup setup, Vector2 from, Action<BattleOutcome> done, IBattleLink link = null)
        {
            if (!BattleScreen.Open) TalesUi.CloseAll();
            BattleScreen.Run(setup, from, done, link);
        }

        // a boss fight you joined is over: its own ending in your own save (the Book Badge, the guardian's chest)
        static void After(string kind, BattleOutcome o)
        {
            if (o == null || o.Rounds <= 0) return; // the battle never got going
            var w = World;
            if (kind.StartsWith("book:", StringComparison.Ordinal)) BossSpots.After(kind.Substring(5), o);
            else if (w == null || w.Road == null || !w.Road.GuardianFought(o)) RoadVitals.After(!o.Won, o);
        }

        /// <summary>A "pb" message from someone in the party (PlazaWorld passes them on, from any town).</summary>
        public static void Hear(Msg m)
        {
            string from = m.Str("id"), f = m.Str("f");
            if (from.Length == 0 || from == You || f.Length == 0) return;
            switch (m.Str("k"))
            {
                case "ask": Asked(from, f, m); break;
                case "ready": Answered(from, f, m); break;
                case "cancel": Cancelled(from, f, m); break;
                case "start": Started(from, f, m); break;
                default: if (fight != null && fight.F == f) fight.Hear(from, m); break; // a step, an input or a pace
            }
        }

        // ---- small helpers ----

        // a message to the party (it's handed back, so a captain's step can go again)
        static Msg Send(string k, string f, params object[] kv)
        {
            var m = new Msg { ["t"] = "pb", ["k"] = k, ["f"] = f };
            for (int i = 0; i + 1 < kv.Length; i += 2) m[(string)kv[i]] = kv[i + 1];
            Post(m);
            return m;
        }

        static void Post(Msg m)
        {
            var w = World;
            if (w != null) w.SendParty(m);
        }

        // the party members online in a room now (someone between rooms can't hear the party's battles), you first
        static List<string> Online()
        {
            var who = new List<string> { You };
            foreach (var m in PartyPanel.Members()) if (m.online && m.town.Length > 0 && m.id != You) who.Add(m.id);
            return who;
        }

        static string Name(string pid)
        {
            foreach (var m in PartyPanel.Members()) if (m.id == pid) return m.name;
            return pid == You && Buddy.Name.Length > 0 ? Buddy.Name : "A friend";
        }

        static PartyHero Mine(double hp, int ink) => BattleWire.Mine(You, Name(You), Buddy.Look, MyPets.ActiveName, hp, ink);

        // your pet as it is right now (out of town a fight starts with at least one ink, as the road's do)
        static PartyHero Mine()
        {
            var w = World;
            bool town = w == null || w.Map.IsTown;
            var (hp, ink) = RoadVitals.Now(town);
            return Mine(hp, town ? ink : Mathf.Max(1, ink));
        }

        // a friend's pet from their answer, keyed by who sent it (a pet that won't build stays out)
        static void Take(List<PartyHero> party, string pid, Msg wire)
        {
            try
            {
                var p = BattleWire.Party(wire);
                p.Pid = pid;
                p.Unit.Key = "h:" + pid;
                party.Add(p);
            }
            catch (Exception) { }
        }

        static Msg Write(ReadyInfo i) => new Msg
        {
            ["icon"] = i.Icon, ["name"] = i.Name, ["title"] = i.Title, ["place"] = i.Place, ["quote"] = i.Quote, ["level"] = (double)i.Level,
            ["rec"] = (double)i.Rec, ["lines"] = Words(i.Lines), ["scale"] = i.Scale,
        };

        // a card from a friend's game, tidied like chat (only its name goes through the word list: longer lines can trip
        // it by accident, "pages hits")
        static ReadyInfo Read(Msg m)
        {
            var i = new ReadyInfo
            {
                Icon = Tidy(m.Str("icon"), 16), Name = Clean(m.Str("name"), 60, "A boss"), Title = Tidy(m.Str("title"), 60), Place = Tidy(m.Str("place"), 60),
                Quote = Tidy(m.Str("quote"), 160), Level = m.Int("level"), Rec = m.Int("rec"), Scale = Tidy(m.Str("scale"), 120),
            };
            foreach (var line in Strings(m.Arr("lines")))
            {
                string text = Tidy(line, 120);
                if (text.Length > 0 && i.Lines.Count < 6) i.Lines.Add(text);
            }
            return i;
        }

        static string Tidy(string text, int max) => LocalSafety.CleanText(text, max);

        // a name from a friend's game, checked like a name in town
        static string Clean(string name, int max, string otherwise)
        {
            name = Tidy(name, max);
            return name.Length > 0 && LocalSafety.TextProblem(name) == null ? name : otherwise;
        }

        static Vector2 Point(Vector2 at)
        {
            var cam = Camera.main;
            return cam ? (Vector2)cam.WorldToScreenPoint(TownMap.ToWorld(at.x, at.y)) : new Vector2(Screen.width / 2f, Screen.height / 2f);
        }

        static string Times(double x) => x.ToString("0.##", CultureInfo.InvariantCulture) + "×";

        static List<object> Words(List<string> list) => list.ConvertAll(s => (object)s);

        static List<string> Strings(List<object> list)
        {
            var words = new List<string>();
            foreach (var o in list) if (o is string s) words.Add(s);
            return words;
        }

        static IEnumerator Later(float seconds, Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }

        /// <summary>A ready check: who's asked and their answers, and the captain's fight with the pets that are coming.</summary>
        sealed class Check
        {
            public string F, Captain, Kind;
            public List<string> Who;
            public ReadyInfo Info;
            public float Ends;
            public bool Answered;
            public readonly Dictionary<string, int> States = new Dictionary<string, int>();
            public readonly Dictionary<string, Msg> Heroes = new Dictionary<string, Msg>();
            public BattleSetup Setup;
            public Vector2 From;
            public Action<BattleOutcome> Done;

            public int State(string pid) => States.TryGetValue(pid, out int s) ? s : Deciding;
        }

        /// <summary>A road fight waiting for its pets (the captain's) or for its start (everyone else's).</summary>
        sealed class Gathering
        {
            public string F;
            public bool Captain;
            public int Want;
            public float Until;
            public readonly List<PartyHero> Party = new List<PartyHero>();
            public BattleSetup Setup;
            public Vector2 From;
            public Action<BattleOutcome> Done;
        }

        /// <summary>
        /// The party fight on your screen (BattleScreen plays it through this): as captain, everyone's inputs come in and
        /// each step goes out (kept, for anyone who missed it), held while a friend is 2+ steps behind (3 s at most each
        /// time); as a follower, the captain's steps come in and your inputs and pace ("at") go out. A follower plays every
        /// step in order: one that went missing (a later one is here, or the captain has been quiet a while) is asked for
        /// again ("again"), and the captain sends it, or "on" while it's still busy with the step before. Only once the
        /// captain has said nothing for 8 s with every step that came played does the follower carry on alone. An input of
        /// yours that never came back (a message went missing) stops showing as on its way after 8 s too.
        /// </summary>
        sealed class Link : IBattleLink
        {
            sealed class Step { public uint Seed; public List<BattleInput> Ins; public List<object> Snap; public bool? Over; }
            sealed class Friend { public int Played; public float HeldSince = -1; public bool Gone; }

            public readonly string F;
            readonly string lead;
            readonly List<PartyHero> party;
            readonly List<BattleInput> queued = new List<BattleInput>();                    // captain: inputs for the next step
            readonly Dictionary<string, Friend> friends = new Dictionary<string, Friend>(); // captain: how far each follower has played
            readonly Dictionary<int, Msg> sent = new Dictionary<int, Msg>();               // captain: every step sent, for anyone who missed one
            readonly Dictionary<int, Step> steps = new Dictionary<int, Step>();            // follower: steps come but not yet played
            readonly Dictionary<string, float> pending = new Dictionary<string, float>();   // your inputs not applied yet, by when you asked
            int got, played, askedFor;
            float heardAt = Time.unscaledTime, sentAt = -99, askedAt = -99;
            BattleInput? owed; // your latest lane move, waiting for its turn to go
            char lane;         // the lane you asked for last
            bool lost, closed;

            public Link(string f, string captain, List<PartyHero> pets)
            {
                F = f;
                lead = captain;
                party = pets;
                Captain = captain == You;
                if (Captain) foreach (var p in pets) if (p.Pid != You) friends[p.Pid] = new Friend();
            }

            public bool Captain { get; }

            public bool Lost
            {
                get
                {
                    if (!Captain && !lost && !closed && !steps.ContainsKey(played + 1) && Time.unscaledTime - heardAt > Quiet) lost = true;
                    return lost;
                }
            }

            public int Behind => got - played;

            public bool Waiting(int n)
            {
                float now = Time.unscaledTime;
                bool hold = false;
                foreach (var p in friends.Values)
                {
                    if (p.Gone || n - 1 - p.Played < 2) { p.HeldSince = -1; continue; }
                    if (p.HeldSince < 0) p.HeldSince = now;
                    if (now - p.HeldSince > Hold) p.Gone = true; // left or stuck: no more waiting for them until they catch up
                    else hold = true;
                }
                return hold;
            }

            public List<BattleInput> TakeInputs()
            {
                var ins = new List<BattleInput>(queued);
                queued.Clear();
                pending.Clear();
                return ins;
            }

            public uint NewSeed() => (uint)UnityEngine.Random.Range(1, int.MaxValue);

            public void Sent(int n, uint seed, List<BattleInput> ins, List<object> snap, bool? over) =>
                sent[n] = Send("step", F, "n", (double)n, "s", (double)seed, "in", BattleWire.Inputs(ins), "after", snap, "over", over == null ? -1.0 : over.Value ? 1.0 : 0.0);

            public bool Next(int n, out uint seed, out List<BattleInput> ins)
            {
                Flush();
                bool has = steps.TryGetValue(n, out var s);
                if (!has) Chase(n);
                // yours go in with this step (a lane move only once your last one has: one may still be on its way)
                else foreach (var i in s.Ins) if (i.Hero == "h:" + You && (i.Act != "lane" || owed == null && i.Lane == lane)) pending.Remove(i.Act);
                seed = has ? s.Seed : 0;
                ins = has ? s.Ins : null;
                return has;
            }

            public bool After(int n, out List<object> snap, out bool? over)
            {
                bool has = steps.TryGetValue(n, out var s);
                snap = has ? s.Snap : null;
                over = has ? s.Over : null;
                return has;
            }

            public void Played(int n)
            {
                steps.Remove(n);
                played = Math.Max(played, n);
                Send("at", F, "n", (double)played);
                Flush();
            }

            // step n isn't here: asked for again when a later one came (it went missing) or the captain has been quiet a
            // while, at once for a new one and then once a second
            void Chase(int n)
            {
                float now = Time.unscaledTime;
                if ((got <= n && now - heardAt <= Quiet / 2) || (n == askedFor && now - askedAt < Again)) return;
                askedFor = n;
                askedAt = now;
                Send("again", F, "n", (double)n);
            }

            public void Ask(BattleInput i)
            {
                pending[i.Act] = Time.unscaledTime;
                if (i.Act == "lane") lane = i.Lane;
                if (Captain) queued.Add(i);
                else if (i.Act == "lane" && Time.unscaledTime - sentAt < LaneGap) owed = i; // goes a moment later
                else
                {
                    if (i.Act == "lane") owed = null; // a newer move: one held back must not land after it
                    Tell(i);
                }
            }

            void Tell(BattleInput i)
            {
                sentAt = Time.unscaledTime;
                Send("in", F, "a", i.Act, "l", i.Lane == '\0' ? "" : i.Lane.ToString());
            }

            // a lane move held back, once it may go
            void Flush()
            {
                if (owed == null || Time.unscaledTime - sentAt < LaneGap) return;
                Tell(owed.Value);
                owed = null;
            }

            public bool Pending(string act) => pending.TryGetValue(act, out float at) && Time.unscaledTime - at < Quiet;

            public string NameOf(string heroKey)
            {
                var p = party.Find(h => h.Unit.Key == heroKey);
                return p == null ? null : Name(p.Pid);
            }

            public void Closed()
            {
                closed = true;
                if (fight == this) fight = null;
            }

            /// <summary>A step from the captain (or word that it's still busy), or a follower's input, pace or missing step.</summary>
            public void Hear(string from, Msg m)
            {
                if (closed || lost) return;
                int n = m.Int("n");
                switch (m.Str("k"))
                {
                    case "step":
                        if (Captain || from != lead) return;
                        heardAt = Time.unscaledTime;
                        if (n <= played || steps.ContainsKey(n)) return; // one asked for again that came after all
                        double over = m.Num("over", -1);
                        steps[n] = new Step { Seed = (uint)m.Num("s"), Ins = BattleWire.Inputs(m.Arr("in")), Snap = m.Arr("after"), Over = over < 0 ? (bool?)null : over > 0 };
                        got = Math.Max(got, n);
                        break;
                    case "on":
                        if (!Captain && from == lead) heardAt = Time.unscaledTime;
                        break;
                    case "again":
                        if (!Captain || !friends.ContainsKey(from)) return;
                        if (sent.TryGetValue(n, out var step)) Post(step);
                        else Send("on", F);
                        break;
                    case "in":
                        string lane = m.Str("l");
                        if (Captain && friends.ContainsKey(from)) queued.Add(new BattleInput { Hero = "h:" + from, Act = m.Str("a"), Lane = lane.Length > 0 ? lane[0] : '\0' });
                        break;
                    case "at":
                        if (Captain && friends.TryGetValue(from, out var p) && n > p.Played) { p.Played = n; p.Gone = false; }
                        break;
                }
            }
        }
    }
}
