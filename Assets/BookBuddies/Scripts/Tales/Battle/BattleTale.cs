using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // A fight inside a tale (Setup.Tale, the site's epStartBattle): starting ink, the friend who joins at the intro, and the
    // boons' combat hooks (TQ_BOONS, battle.md §9). thick, quill, hardcover, speed and hero scale stats in HeroFactory.SetLevel;
    // library and cocoa pay out after the fight (EpicFlow). Outside a tale every hook is a no-op.
    public sealed partial class BattleEngine
    {
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
    }
}
