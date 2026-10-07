using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Tales
{
    /// <summary>
    /// epStartBattle (tales.js T:1063-1073): the fight for run.Battle, built from the room's kind, the act and what the
    /// rooms handed out: the foes with their mods, the boss's tactic changes, the twist on half the plain fights, the
    /// land's hazard (none when warded) and its arena. It reads the run but changes nothing, and draws every random pick
    /// from the fight's own seed, so a resumed tale rebuilds the very same fight; EpicFlow.Finish clears what it spent afterwards.
    /// </summary>
    public static class EpicBattle
    {
        // the land's scene (TQ_SCN 0-5: fantasy mystery romance sci-fi spooky adventure) as a Bramble Road arena pool
        static readonly int[] SceneTier = { 3, 2, 5, 3, 4, 1 };

        /// <summary>The battle setup for the run's current fight (run.Battle must be set).</summary>
        public static BattleSetup Setup(TaleRun run)
        {
            var E = run.Ep; var A = E.A; var d = TalesData.Current;
            string kind = run.Battle.Kind;
            var rng = new Mulberry32(unchecked((uint)run.Battle.Seed));
            double lvl = EpicSaga.Level(E);
            var pool = Pool(A);
            int party = run.Party.Count;
            BattleUnit Make(FoeDef def, double l, bool boss, bool elite, int k) =>
                FoeFactory.Make(FoeFactory.Variant(def, boss, elite, rng), l, boss, elite, "f" + k, 1, rng);
            BattleUnit Minion(int k) => Make(d.Minions[pool[rng.Range(pool.Count)] % d.Minions.Count], lvl, false, kind == TaleBattle.Elite, k);
            BattleUnit Extra(int k) { var f = Minion(k); f.Lvl = Math.Max(1, JsMath.Round(f.Lvl)); return f; } // a twist's extra foe: no mods

            var foes = new List<BattleUnit>();
            if (kind == TaleBattle.Boss)
            {
                var f = Make(EpicSaga.Boss(E, E.Act), lvl + (E.Act == 4 ? .6 : 0), true, false, 0);
                if (E.Act == 4) f.Max = JsMath.Round(f.Max * 1.5);
                f.Tactics = new List<(double at, string m)>(A.Ph);
                foes.Add(f);
                if (E.Act >= 2) foes.Add(Minion(1));
            }
            else if (kind == TaleBattle.Elite)
            {
                var f = Make(d.Bosses[A.Lt % d.Bosses.Count], lvl, false, true, 0);
                f.Max = JsMath.Round(f.Max * 1.9);
                f.Atk = JsMath.Round(f.Atk * 1.15);
                if (E.Rival) f.Name = "Captain of the Dog-Eared Gang";
                foes.Add(f);
                foes.Add(Minion(1));
            }
            else
            {
                int n = Math.Min(party >= 6 ? 5 : 4, 1 + (int)Math.Floor(rng.Next() * Math.Min(3, 1 + E.Done * .7 + E.At.r * .3)) + (party >= 3 ? 1 : 0) + (party >= 6 ? 1 : 0));
                for (int k = 0; k < n; k++) foes.Add(Minion(k));
                if (E.Rel.Contains("a_flute") && foes.Count > 1) foes.RemoveAt(foes.Count - 1);
            }
            if (party >= 3 && kind != TaleBattle.Fight) foes.Add(Minion(2));
            if (party >= 6 && kind != TaleBattle.Fight) foes.Add(Minion(3));
            if (kind != TaleBattle.Fight) RenownScale(run, foes[0]);
            foreach (var f in foes) Mods(run, kind, f);
            string twist = kind == TaleBattle.Fight ? BattleEngine.TqTwist(foes, Extra, rng) : null;

            var land = EpicSaga.LandOf(E);
            string hz = E.Ward ? null : E.Hz;
            var setup = new BattleSetup
            {
                Title = run.Title, Place = $"Act {EpicSaga.Roman(E.Act)} · {land.N}", Lvl = Math.Max(1, run.Ch), Tier = SceneTier[land.Sc % 6], Cave = E.Act == 4,
                Ink = E.Ink + (E.Bless ? 2 : 0) - (hz == "gloom" ? 1 : 0) + (E.Rel.Contains("a_ruby") ? 1 : 0) - (E.Cur.Contains("c_spindle") ? 1 : 0),
                Tale = new TaleBattle { Kind = kind, Hazard = hz, Twist = twist, Ally = E.Ally, Revived = run.Revived, Ch = run.Ch, Sc = land.Sc, Gold = run.Gold },
            };
            setup.Tale.Boons.AddRange(run.Boons);
            setup.Tale.ReadyFoes.AddRange(foes);
            setup.Tale.Pool.AddRange(pool);
            var h = run.Party.Find(x => !x.Ko) ?? run.Party[0];
            setup.Tale.HeroLvl = h.Lvl;
            setup.Hero = Hero(run, h);
            setup.HpFrac = h.Hp / TaleLife.MaxHp(run, h);
            setup.Look = h.Seed.Look;
            setup.PetName = h.Seed.Name;
            return setup;
        }

        /// <summary>tqPool: the act's pool minions that belong anywhere or to this land, plus the land's own minions three times; the lair (no minion of its own) falls back to every minion with no land when nothing is left (the site throws there).</summary>
        public static List<int> Pool(EpicAct A)
        {
            var mins = TalesData.Current.Minions;
            var own = Enumerable.Range(0, mins.Count).Where(i => mins[i].Rg == A.R).ToList();
            var pool = A.Pool.Where(i => { var m = mins[i % mins.Count]; return m.Rg == null || m.Rg == A.R; }).ToList();
            for (int k = 0; k < 3; k++) pool.AddRange(own);
            return pool.Count > 0 ? pool : Enumerable.Range(0, mins.Count).Where(i => mins[i].Rg == null).ToList();
        }

        // the run hero as it stands: its tale level, known moves and tactics, the library's Inkwell and Second Wind as they are
        // now (tqMeta, bought while the tale waited too), and the next fight's shields (bless 15%, Glass Slipper 10%)
        static BattleUnit Hero(TaleRun run, TaleHero h)
        {
            var u = HeroFactory.Build(h.Seed, h.Lvl);
            var info = u.Hero;
            info.Known = h.Kn.FindAll(k => HeroFactory.Move(info.Cls, k) != null);
            if (info.Known.Count > 0) { u.Moves.Clear(); foreach (var k in info.Known) u.Moves.Add(HeroFactory.Move(info.Cls, k)); }
            var save = TalesSave.Current;
            info.MetaInk = HeroFactory.Library(save, "ink");
            info.MetaRev = HeroFactory.Library(save, "rev") > 0;
            var me = save.Me;
            info.Order = me.Order == null ? null : new List<string>(me.Order);
            info.AutoUlt = me.AutoUlt;
            if (me.Lane == "l" || me.Lane == "r") u.Lane = me.Lane[0];
            HeroFactory.SetLevel(u, h.Lvl, run.Boons);
            double shield = (run.Ep.Bless ? JsMath.Round(u.Max * .15) : 0) + (run.Ep.Rel.Contains("a_slipper") ? JsMath.Round(u.Max * .1) : 0);
            if (shield > 0) u.St["shield"] = shield;
            return u;
        }

        // boss and elite scale with the party's renown: up to +45% HP and +22.5% attack
        static void RenownScale(TaleRun run, BattleUnit f)
        {
            double rn = run.Party.Sum(h => HeroFactory.Renown(h.Seed.Rxp).lvl) / (double)Math.Max(1, run.Party.Count);
            double cu = 1 + Math.Min(.45, rn * .035);
            if (cu <= 1.01) return;
            f.Max = JsMath.Round(f.Max * cu);
            f.Atk = JsMath.Round(f.Atk * (1 + (cu - 1) * .5));
        }

        // per foe (T:1069): curses c_hearts +12% HP and c_heart +10% boss attack, a cursed room +25% attack, relic a_hook −10% HP,
        // and on the boss itself relic a_sword −10% and a weakened boss −20%
        static void Mods(TaleRun run, string kind, BattleUnit f)
        {
            var E = run.Ep;
            if (E.Cur.Contains("c_hearts")) f.Max = JsMath.Round(f.Max * 1.12);
            if (f.Boss && E.Cur.Contains("c_heart")) f.Atk = JsMath.Round(f.Atk * 1.1);
            f.Hp = f.Max;
            f.Lvl = Math.Max(1, JsMath.Round(f.Lvl));
            if (E.Curse) f.Atk = JsMath.Round(f.Atk * 1.25);
            if (E.Rel.Contains("a_hook")) f.Hp = JsMath.Round(f.Hp * .9);
            if (f.Boss && kind == TaleBattle.Boss)
            {
                if (E.Rel.Contains("a_sword")) f.Hp = JsMath.Round(f.Hp * .9);
                if (E.Weak) f.Hp = JsMath.Round(f.Hp * .8);
            }
        }
    }
}
