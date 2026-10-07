using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The solo wild auto-battle from the site's lazy/tales.js (build 530), pure C#.
    /// Call Next() repeatedly; each call returns the events of one step (the intro, a boss rising, a fate roll, one unit's turn, the end).
    /// A step's state is fully resolved before its events come back, like the site, so each Fx entry carries the numbers to show at that moment.
    /// </summary>
    /// <remarks>
    /// Event kinds and the fields they set:
    /// <list type="bullet">
    /// <item>intro: Line is the callout ("Dust Bunny +2!"), Icon ⚔️.</item>
    /// <item>atk, heal, shield, buff: Actor, Name, Icon, Ult, Uv, Anim (arc beam rain slash quake orbit burst), Projectile (arc only: lob bolt boomer
    /// volley wave bounce spiral comet disc homing), Aoe, Seq (random hits one after another), Foe, Fx, Pops. A Ground Slam is an atk with Zone set.</item>
    /// <item>slamw: a foe winds up a slam on lane Zone (Actor, Name, Icon ⚠️, Pops). SlamZone stays set until it lands; MoveLane may then pick any lane.</item>
    /// <item>dot: Actor; Fx with Damage and Dot (🩸 bleed, 🧪 poison), Heal (regen, gear regen, bloom) or Ko.</item>
    /// <item>skip: Actor was stunned (Name "Too dizzy!", Icon 💫).</item>
    /// <item>talk: Speaker (a foe) says Line, Listener (the pet) answers Reply.</item>
    /// <item>fate: Actor rolled a d20: Name (who), Roll, Bonus, Result (fumble meh good great nat), Line, Helper (a TalesData.Npcs key, or null), Fx, Pops.</item>
    /// <item>rise: Actor (a boss) rises with a new Name, Line (its boast), Sub (what it gained), Fx. Wild bosses rise once ("…, Second Edition"), a Book Boss once per phase (it may summon its team).</item>
    /// <item>cheer: Actor got +1 ink (returned by Cheer, not Next).</item>
    /// <item>win, lose: the end; Over is true and Outcome is set.</item>
    /// </list>
    /// A Fx entry is a hit when Damage &gt; 0, Absorbed &gt; 0 or Miss; Small marks splash and gear damage. Hp, Max and Shield are the unit's values right after it.
    /// Pops are short callouts: (unit key, or null for the whole arena, text).
    /// The library's Second Wind (defeat's meta revive) is a heal event: every pet back up at half HP, once a battle (once a tale in a tale).
    /// With Setup.Tale the fight follows the tale's rules (BattleTale.cs): the run hero's level and boons, ready-made foes,
    /// starting ink clamped to 0..6, a friend at the intro (a fate event with Result "ally" and no roll), and the run's ink
    /// drops won or stolen mid-fight (Outcome.Gold).
    /// Not yet: twists, hazards, tactic changes, intro banter.
    /// </remarks>
    public sealed partial class BattleEngine
    {
        public readonly List<BattleUnit> Heroes = new List<BattleUnit>(), Foes = new List<BattleUnit>();
        public readonly BattleSetup Setup;
        public int Round { get; private set; }
        public bool Over { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        /// <summary>Lane a pending slam will hit, or null.</summary>
        public string SlamZone => slam == null ? null : slam.Value.zone.ToString();
        /// <summary>The battle level: the road's (1 to 12; the hero fights at tale level min(6, Lvl)), or a tale's chapter.</summary>
        public readonly int Lvl;

        readonly IRng rng;
        readonly TalesSave save;                 // where your lane choice is kept (null for a ready-made hero)
        readonly List<string> queue = new List<string>();
        readonly Dictionary<BattleUnit, int> buffs = new Dictionary<BattleUnit, int>();
        bool intro, mid, cheered, revived;
        int nextFate = 2, nat20s, helpers, crits, heals;
        (string name, string move, double d)? best;
        (string n, string ab)? ult;
        readonly List<string> naps = new List<string>();

        public BattleEngine(BattleSetup setup, IRng rng = null)
        {
            Setup = setup;
            this.rng = rng ?? SystemRng.Shared;
            var tale = setup.Tale;
            Lvl = tale != null ? Math.Max(1, tale.Ch) : JsMath.Clamp(setup.Lvl == 0 ? 2 : setup.Lvl, 1, 12);
            int heroLvl = tale != null ? Math.Max(1, tale.HeroLvl) : Math.Min(6, Lvl);
            var hero = setup.Hero;
            if (hero == null)
            {
                save = TalesSave.Current;
                hero = HeroFactory.Build(setup.Look, string.IsNullOrEmpty(setup.PetName) ? "Your pet" : setup.PetName, heroLvl);
            }
            HeroFactory.SetLevel(hero, heroLvl, tale?.Boons);
            hero.Ko = false;
            hero.Hp = Math.Max(1, Math.Min(hero.Max, JsMath.Round(setup.HpFrac * hero.Max)));
            Heroes.Add(hero);
            if (tale != null) Foes.AddRange(tale.ReadyFoes);
            else
                for (int i = 0; i < setup.Foes.Count; i++)
                {
                    var f = setup.Foes[i];
                    Foes.Add(FoeFactory.Make(f.v, Lvl, f.boss, f.elite, "f" + i, setup.Mul, this.rng));
                }
            foreach (var h in Heroes) StartHero(h);
            LaneInit();
            LaneFix();
            RollCover();
        }

        /// <summary>The events of the next step, or an empty list once the fight is over.</summary>
        public List<BattleEvent> Next()
        {
            var evs = new List<BattleEvent>();
            if (Over) return evs;
            if (!intro)
            {
                intro = true;
                Emit(evs, IntroEvent());
                if (Setup.Tale?.Ally != null) AllyArrives(evs, Setup.Tale.Ally);
                return evs;
            }
            while (evs.Count == 0 && !Over) Step(evs);
            return evs;
        }

        public BattleUnit Find(string key) => Heroes.Find(u => u.Key == key) ?? Foes.Find(u => u.Key == key);
        public bool UltReady(BattleUnit hero) => hero != null && !hero.Ko && hero.Ink >= 6;

        /// <summary>The Ultimate button: fires on the hero's next turn. False if it isn't charged.</summary>
        public bool RequestUlt(BattleUnit hero)
        {
            if (Over || hero?.Hero == null || !UltReady(hero)) return false;
            hero.Hero.WantUlt = true;
            return true;
        }

        /// <summary>A cheer from the player; the first one in a fight gives +1 ink. Returns its event, or null.</summary>
        public BattleEvent Cheer(BattleUnit hero)
        {
            if (Over || cheered) return null;
            cheered = true;
            var live = LiveHeroes();
            var h = hero != null && !hero.Ko ? hero : live.Count > 0 ? live[0] : null;
            if (h == null) return null;
            h.Ink = Math.Min(6, h.Ink + 1);
            var e = new BattleEvent { Kind = "cheer", Actor = h.Key };
            e.Pops.Add(Pop(h.Key, "📣 Cheer! +1 ink"));
            return e;
        }

        // one pass of the site's battle loop
        void Step(List<BattleEvent> evs)
        {
            BossRise(evs);
            if (evs.Count > 0) return;
            if (LiveFoes().Count == 0) { End(evs, true); return; }
            if (LiveHeroes().Count == 0) { if (!SecondWind(evs)) End(evs, false); return; }
            if (queue.Count == 0 && NewRound(evs)) return;

            var u = Find(queue[0]);
            queue.RemoveAt(0);
            if (u == null || Dead(u)) return;
            foreach (var e in Turn(u))
            {
                Emit(evs, e);
                BossRise(evs);
                if (LiveFoes().Count == 0 || LiveHeroes().Count == 0) break;
            }
            if (Round == 3 && !mid && LiveFoes().Count > 0 && LiveHeroes().Count > 0)
            {
                mid = true;
                if (rng.Next() < .25) Banter(evs);
            }
        }

        // fills the turn order (speed plus up to 2 at random); a fate roll takes the step every 3-4 rounds from round 2. True if it did.
        bool NewRound(List<BattleEvent> evs)
        {
            Round++;
            var order = new List<(string key, double at)>();
            foreach (var u in LiveHeroes()) order.Add((u.Key, u.Spd + rng.Next() * 2));
            foreach (var u in LiveFoes()) order.Add((u.Key, u.Spd + rng.Next() * 2));
            order.Sort((a, b) => b.at.CompareTo(a.at));
            foreach (var o in order) queue.Add(o.key);
            if (Round < nextFate) return false;
            nextFate = Round + 3 + (int)Math.Floor(rng.Next() * 2);
            FateRoll(evs);
            return true;
        }

        // startBattle and gearStart: fresh statuses and the gear shield. Ink is the ink carried in from the road plus gear ink and
        // the library's Inkwell (deliberate fix: the site's startBattle reset the carried ink to 1 right after wildRun copied it in);
        // a tale starts from its own formula (TaleInk) and keeps the statuses its setup gave (epStartBattle's bless and Glass Slipper shields)
        void StartHero(BattleUnit h)
        {
            var info = h.Hero;
            if (Setup.Tale == null) h.St.Clear();
            info.PlotUsed = false;
            info.WantUlt = false;
            info.Dark = 0;
            info.PhoenixUsed = false;
            h.Ink = Math.Min(6, (Setup.Tale != null ? TaleInk(info) : Setup.Ink + info.MetaInk) + info.GearInk);
            if (info.Sh != 0) h.St["shield"] = h.S("shield") + JsMath.Round(h.Max * info.Sh / 100);
        }

        // defeat with the library's Second Wind: the party is back up at half HP with fresh statuses, once (a tale's once is spent
        // when Tale.Revived). True if it happened.
        bool SecondWind(List<BattleEvent> evs)
        {
            if (revived || Setup.Tale?.Revived == true || !Heroes.Exists(h => h.Hero.MetaRev)) return false;
            revived = true;
            var e = new BattleEvent { Kind = "heal", Actor = Heroes[0].Key, Name = "Second Wind", Icon = "🪽" };
            foreach (var h in Heroes) { h.St.Clear(); Revive(h, .5, e); }
            e.Pops.Add(Pop(null, "🪽 Second Wind!"));
            Emit(evs, e);
            return true;
        }

        BattleEvent IntroEvent()
        {
            var first = Foes[0].Name;
            foreach (var cut in new[] { " of ", " from " })
            {
                int i = first.IndexOf(cut, StringComparison.Ordinal);
                if (i >= 0) first = first.Substring(0, i);
            }
            return new BattleEvent { Kind = "intro", Icon = "⚔️", Line = first + (Foes.Count > 1 ? " +" + (Foes.Count - 1) : "") + "!" };
        }

        void End(List<BattleEvent> evs, bool won)
        {
            var hero = Heroes[0];
            if (won) foreach (var h in Heroes) h.St.Clear();
            Emit(evs, new BattleEvent { Kind = won ? "win" : "lose" });
            Over = true;
            slam = null;
            Outcome = new BattleOutcome
            {
                Won = won, HpFrac = hero.Max <= 0 ? 0 : Math.Max(0, JsMath.Round(hero.Hp)) / hero.Max, Ink = hero.Ink, Rounds = Round,
                BestMove = best?.move, BestHit = best?.d ?? 0, Hero = hero, Lvl = Lvl, Nat20s = nat20s, Helpers = helpers,
                Revived = revived, Ult = ult, Crits = crits, Heals = heals, Gold = gold,
            };
            foreach (var f in Foes) if (Dead(f)) Outcome.Defeated.Add(f);
            Outcome.Naps.AddRange(naps);
            Outcome.HelperKeys.AddRange(usedNpcs);
        }

        // the site's emit: lanes settle before every event
        void Emit(List<BattleEvent> evs, BattleEvent e)
        {
            LaneInit();
            LaneFix();
            evs.Add(e);
        }

        static bool Dead(BattleUnit u) => u.IsFoe ? u.Hp <= 0 : u.Ko;
        List<BattleUnit> LiveFoes() => Foes.FindAll(f => f.Hp > 0);
        List<BattleUnit> LiveHeroes() => Heroes.FindAll(h => !h.Ko);
        List<BattleUnit> Opp(BattleUnit u) => u.IsFoe ? LiveHeroes() : LiveFoes();
        List<BattleUnit> Mates(BattleUnit u) => u.IsFoe ? LiveFoes() : LiveHeroes();
        static KeyValuePair<string, string> Pop(string key, string text) => new KeyValuePair<string, string>(key, text);

        // a Fx entry with the unit's numbers as they are now
        static BattleHit Snap(BattleUnit u) => new BattleHit
        {
            Unit = u.Key, Hp = Math.Max(0, JsMath.Round(u.Hp)), Max = u.Max, Shield = JsMath.Round(u.S("shield")),
        };
    }
}
