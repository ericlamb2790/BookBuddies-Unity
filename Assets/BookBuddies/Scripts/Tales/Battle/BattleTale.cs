using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // A fight inside a tale (Setup.Tale, the site's epStartBattle): starting ink, the friend who joins at the intro, the
    // boons' combat hooks (TQ_BOONS, battle.md §9), the land's hazard (TQ_HZ), the fight twist (tqTwist) and a boss's tactic
    // changes (epPhase). thick, quill, hardcover, speed and hero scale stats in HeroFactory.SetLevel; library and cocoa pay
    // out after the fight (EpicFlow). Outside a tale every hook is a no-op.
    public sealed partial class BattleEngine
    {
        static readonly string[] TwistKeys = { "ambush", "swarm", "duel", "shield", "chief", "worn" }; // TQ_TWIST key order
        static Dictionary<string, object> twistTable;

        int gold; // ink drops won and stolen in this fight (Outcome.Gold); the run had Tale.Gold going in

        /// <summary>Stacks of a boon in this fight (0 outside a tale).</summary>
        public int Boon(string key)
        {
            int n = 0;
            if (Setup.Tale != null) foreach (var b in Setup.Tale.Boons) if (b == key) n++;
            return n;
        }

        // abCost: marg makes a pet's regular moves 1 ink cheaper (never free); ults keep their price
        /// <summary>The ink a move costs this pet right now (the side panel's charge bars).</summary>
        public int InkCost(BattleUnit u, MoveDef ab) => CostOf(u, ab);

        int CostOf(BattleUnit u, MoveDef ab) => !u.IsFoe && ab.Cost != 0 && !ab.Ult && Boon("marg") > 0 ? Math.Max(1, ab.Cost - 1) : ab.Cost;

        // a tale's starting ink: 1, +2 a bookmark stack, the library's Inkwell and the dungeon's extras (Setup.Ink), kept in 0..6
        int TaleInk(HeroInfo info) => JsMath.Clamp(1 + 2 * Boon("bookmark") + info.MetaInk + Setup.Ink, 0, 6);

        // dogear: +12% crit chance a stack for pets
        double DogEar(BattleUnit a) => a.IsFoe ? 0 : .12 * Boon("dogear");

        // annot: a pet's genre edge is ×2 instead of ×1.5
        double GenreEdge(BattleUnit a) => !a.IsFoe && Boon("annot") > 0 ? 2 : 1.5;

        // cliff: a pet hits a foe under 30% HP ×1.5
        double Cliffhanger(BattleUnit a, BattleUnit d) => !a.IsFoe && d.IsFoe && d.Hp < d.Max * .3 && Boon("cliff") > 0 ? 1.5 : 1;

        // plot: once a fight, a pet that would nap hangs on at 1 HP. True if it did.
        bool PlotArmor(BattleUnit t, BattleHit f)
        {
            if (t.IsFoe || t.Hero.PlotUsed || Boon("plot") == 0) return false;
            t.Hero.PlotUsed = true;
            t.Hp = 1;
            f.Pop = "✨ Plot armor!";
            return true;
        }

        // steal: a foe's special takes up to n of the run's ink drops (a wild fight has none to take)
        void Steal(BattleUnit u, int n, BattleEvent e)
        {
            int s = Math.Min((Setup.Tale?.Gold ?? 0) + gold, n);
            gold -= s;
            if (s > 0) e.Pops.Add(Pop(u.Key, $"🪙 Stole {s} 💧"));
        }

        // sequel: a foe felled by a pet's hit gives every pet standing +1 ink
        void Sequel()
        {
            if (Boon("sequel") == 0) return;
            foreach (var h in LiveHeroes()) h.Ink = Math.Min(6, h.Ink + 1);
        }

        // foot: a pet's single-target hit strikes again for 35% (a small hit that can KO)
        void Footnote(BattleUnit a, BattleUnit t, double mult, BattleEvent e)
        {
            if (a.IsFoe || Dead(t) || Boon("foot") == 0) return;
            var r = Damage(a, t, mult * .35, 0);
            t.Hp = Math.Max(0, t.Hp - r.x);
            var f = Snap(t);
            f.Damage = r.x;
            f.Small = true;
            f.Ko = t.Hp <= 0;
            e.Fx.Add(f);
        }

        // nap: a hurt pet heals 4% on each of its turns
        void NapBoon(BattleUnit u, BattleEvent dot)
        {
            if (u.IsFoe || u.Hp <= 0 || u.Hp >= u.Max || Boon("nap") == 0) return;
            double b = u.Hp;
            u.Hp = Math.Min(u.Max, u.Hp + u.Max * .04);
            dot.Fx.Add(new BattleHit { Unit = u.Key, Heal = JsMath.Round(u.Hp - b) });
        }

        // a pet napping: out of the fight with no statuses, and named in the storybook
        void Nap(BattleUnit h)
        {
            h.Ko = true;
            h.St.Clear();
            if (!naps.Contains(h.Name)) naps.Add(h.Name);
        }

        // fateRoll(ally): the friend the dungeon sent (E.ally) acts at the intro with no die
        void AllyArrives(List<BattleEvent> evs, string key)
        {
            var npc = TalesData.Current.Npcs.Find(n => n.Key == key);
            var heroes = LiveHeroes();
            if (npc == null || heroes.Count == 0 || LiveFoes().Count == 0) return;
            var e = new BattleEvent { Kind = "fate", Actor = heroes[0].Key, Name = heroes[0].Name, Result = "ally", Line = $"{npc.Name} came to help!" };
            NpcAct(npc, e);
            Emit(evs, e);
        }

        // ---- hazards (TQ_HZ, battle.md §9): the land's weather; a warded land hands the fight no hazard at all ----

        /// <summary>True while this fight is under hazard k (rain fog frost whisper thorn gloom).</summary>
        public bool Hz(string k) => Setup.Tale != null && Setup.Tale.Hazard == k;

        /// <summary>A unit's speed as the turn order sees it: Bitter Frost slows pets ×.8.</summary>
        public double SpdOf(BattleUnit u) => u.IsFoe || !Hz("frost") ? u.Spd : u.Spd * .8;

        // whisper: foes hit ×1.15; thorn: foes take ×.85
        double HazardMul(BattleUnit a, BattleUnit d) => a.IsFoe && Hz("whisper") ? 1.15 : d.IsFoe && Hz("thorn") ? .85 : 1;

        // fog: a pet's hit on a foe misses 12%
        bool FogMiss(BattleUnit a, BattleUnit t) => t.IsFoe && !a.IsFoe && Hz("fog") && rng.Next() < .12;

        // rain: a pet loses 3% of its max each turn, never below 1 HP
        void Rain(BattleUnit u, BattleEvent dot)
        {
            if (u.IsFoe || !Hz("rain") || u.Hp <= 1) return;
            double d = Math.Max(1, JsMath.Round(u.Max * .03));
            u.Hp = Math.Max(1, u.Hp - d);
            dot.Fx.Add(new BattleHit { Unit = u.Key, Damage = d, Dot = "🌧️" });
        }

        // ---- the fight twist (tqTwist, 06-tales.js D:225-232) ----

        /// <summary>
        /// tqTwist: half of a tale's plain fights get a twist that reshapes the foes before they're handed to the engine
        /// (mk builds the k-th extra minion). Returns the TQ_TWIST key, or null for no twist.
        /// </summary>
        public static string TqTwist(List<BattleUnit> foes, Func<int, BattleUnit> mk, IRng rng)
        {
            if (rng.Next() > .5 || foes.Count == 0) return null;
            string t = TwistKeys[rng.Range(TwistKeys.Length)];
            switch (t)
            {
                case "ambush": foreach (var f in foes) f.Atk = JsMath.Round(f.Atk * 1.15); break;
                case "swarm":
                    int n = Math.Min(6, foes.Count + 2);
                    while (foes.Count < n) { var x = mk(foes.Count); x.Max = Math.Max(1, JsMath.Round(x.Max * .5)); x.Hp = x.Max; foes.Add(x); }
                    foreach (var f in foes) { f.Max = Math.Max(1, JsMath.Round(f.Max * .8)); f.Hp = f.Max; }
                    break;
                case "duel":
                    foes.RemoveRange(1, foes.Count - 1);
                    var d = foes[0];
                    d.Max = JsMath.Round(d.Max * 2.3);
                    d.Atk = JsMath.Round(d.Atk * 1.2);
                    d.Hp = d.Max;
                    break;
                case "shield": foreach (var f in foes) f.St["shield"] = JsMath.Round(f.Max * .25); break;
                case "chief":
                    var c = foes[0];
                    c.Name = "Chief " + c.Name;
                    c.Max = JsMath.Round(c.Max * 1.7);
                    c.Atk = JsMath.Round(c.Atk * 1.15);
                    c.Hp = c.Max;
                    break;
                case "worn":
                    if (foes.Count < 6) foes.Add(mk(foes.Count));
                    foreach (var f in foes) f.Hp = JsMath.Round(f.Max * .65);
                    break;
            }
            return t;
        }

        /// <summary>A twist's banner (TQ_TWIST): icon, name and what it did; null for no or an unknown twist.</summary>
        public static (string icon, string name, string desc)? TwistInfo(string key)
        {
            if (key == null) return null;
            twistTable = twistTable ?? Json.ParseObject(TalesData.TextLoader("Data/tales")).Obj("TQ_TWIST");
            if (twistTable == null || !twistTable.TryGetValue(key, out var row)) return null;
            var w = (List<object>)row;
            return ((string)w[0], (string)w[1], (string)w[2]);
        }

        // ---- boss tactics (epPhase, T:1075-1078) ----

        // after a turn, a boss at or under its next threshold (Tactics, in order) switches tactics: calls minions, rages,
        // raises a shield or patches itself up, and says so (TQ_PHASE / TQ_PHASEN)
        void EpPhase(List<BattleEvent> evs)
        {
            var data = EpicData.Current;
            foreach (var f in LiveFoes())
            {
                var P = f.Tactics;
                if (P == null || P.Count == 0 || f.Hp > f.Max * P[0].at) continue;
                string m = P[0].m;
                P.RemoveAt(0);
                var lines = data.PhaseLines.TryGetValue(m, out var own) ? own : data.PhaseLines["enrage"];
                var e = new BattleEvent
                {
                    Kind = "phase", Actor = f.Key, Icon = "⚠️", Foe = true, Result = m, Line = lines[rng.Range(lines.Length)],
                    Name = data.PhaseNames.TryGetValue(m, out var banner) ? banner : "changes tactics!",
                };
                double gained = 0, before = f.Hp;
                switch (m)
                {
                    case "summon": Summon(f); break;
                    case "enrage": f.Atk = JsMath.Round(f.Atk * 1.35); f.Spd += 3; break;
                    case "shield": gained = JsMath.Round(f.Max * .22); f.St["shield"] = f.S("shield") + gained; break;
                    case "heal": f.Hp = Math.Min(f.Max, f.Hp + f.Max * .18); break;
                }
                var s = Snap(f);
                s.ShieldGained = gained;
                s.Heal = JsMath.Round(f.Hp - before);
                e.Fx.Add(s);
                Emit(evs, e);
            }
        }

        // summon: up to two minions from the act's pool join at the boss's level with full HP, never past five foes
        void Summon(BattleUnit f)
        {
            var mins = TalesData.Current.Minions;
            var pool = Setup.Tale?.Pool;
            int n = Math.Min(2, 5 - LiveFoes().Count);
            for (int i = 0; i < n; i++)
            {
                var def = pool != null && pool.Count > 0 ? mins[pool[rng.Range(pool.Count)] % mins.Count] : rng.Pick(mins);
                var x = FoeFactory.Make(FoeFactory.Variant(def, false, false, rng), f.Lvl, false, false, "s" + Round + i, 1, rng);
                x.Hp = x.Max;
                x.Lvl = f.Lvl;
                Foes.Add(x);
            }
        }
    }
}
