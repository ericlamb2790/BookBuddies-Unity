using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // One unit's turn: damage over time and regen, stun, status decay, then a move; foes may also jump lanes, wind up or land a zone slam
    public sealed partial class BattleEngine
    {
        (string foe, char zone, int round)? slam;

        static readonly string[] Decays = { "expose", "taunt", "pow", "dodge", "weak" };

        List<BattleEvent> Turn(BattleUnit u)
        {
            if (!u.IsFoe) return BaseTurn(u, out _);
            if (slam != null && FoeGone(slam.Value.foe)) slam = null;
            var evs = BaseTurn(u, out var action);
            if (Dead(u) || u.S("stun") != 0 || action == null || action.Kind == "shield") return evs;
            // the site swaps the foe's action event for the slam one although the action still happened; we show both
            if (slam != null && slam.Value.foe == u.Key) evs.Add(GroundSlam(u));
            else if (slam == null && Round >= 2 && LiveHeroes().Count > 0 && rng.Next() < (u.Boss ? .3 : .14)) evs.Add(WindUpSlam(u));
            return evs;
        }

        bool FoeGone(string key) { var f = Find(key); return f == null || Dead(f); }

        // foes move too (not the site's): one with no pet in its lane may jump to the lane with the most pets, and now
        // and then one hops a lane over; a boss holds the center and nobody jumps while a slam is coming. Pets left with
        // no foe in their lane follow it. Decided just before the foe's move, so one its bleed KOs or a stun holds stays put.
        BattleEvent Jump(BattleUnit u)
        {
            if (u.Boss || slam != null) return null;
            var pets = LiveHeroes();
            char from = u.Lane;
            bool alone = !pets.Exists(h => h.Lane == from);
            if (pets.Count == 0 || rng.Next() >= (alone ? .2 : .05)) return null;
            char to = from;
            if (alone)
            {
                int most = 0;
                foreach (char z in Lanes) { int n = pets.FindAll(h => h.Lane == z).Count; if (n > most) { most = n; to = z; } }
            }
            else to = from != 'c' ? 'c' : rng.Next() < .5 ? 'l' : 'r';
            if (to == from) return null;
            u.Lane = to;
            var e = new BattleEvent { Kind = "jump", Actor = u.Key, Name = "Jumps lanes", Icon = "↔️", Foe = true, Zone = to.ToString() };
            e.Pops.Add(Pop(u.Key, "↔️ Jumps lanes"));
            if (FoesIn(from) == 0)
                foreach (var h in pets) if (h.Lane == from) { SetLane(h, to); e.Pops.Add(Pop(h.Key, "🐾 Follows")); }
            return e;
        }

        // the site's turn(u): returns the events, and the move's own event (or null) as action
        List<BattleEvent> BaseTurn(BattleUnit u, out BattleEvent action)
        {
            action = null;
            var evs = new List<BattleEvent>();
            var dot = TickStatuses(u);
            if (dot.Fx.Count > 0) evs.Add(dot);
            if (Dead(u)) return evs;
            var bloom = Bloom(u);
            if (bloom != null) evs.Add(bloom);
            if (u.IsFoe) foreach (var k in new List<string>(u.Cds.Keys)) if (u.Cds[k] > 0) u.Cds[k]--;
            if (u.S("stun") != 0)
            {
                u.St["stun"] = 0;
                evs.Add(new BattleEvent { Kind = "skip", Actor = u.Key, Name = "Too dizzy!", Icon = "💫", Foe = u.IsFoe });
                Decay(u);
                return evs;
            }
            Decay(u);
            if (u.IsFoe && Jump(u) is BattleEvent jump) evs.Insert(0, jump); // shown as the turn starts
            var c = Choose(u);
            if (c != null) evs.Add(action = Exec(u, c.Move, c.Target, c.Takeover));
            return evs;
        }

        // bleed 6% and poison 5% of max, Ink Rain, regen 6%, the nap boon, then gear regen; a pet that drops to 0 here naps (no phoenix)
        BattleEvent TickStatuses(BattleUnit u)
        {
            var dot = new BattleEvent { Kind = "dot", Actor = u.Key, Foe = u.IsFoe };
            if (u.S("bleed") != 0) Lose(u, dot, .06, "bleed", "🩸");
            if (u.S("poison") != 0) Lose(u, dot, .05, "poison", "🧪");
            Rain(u, dot);
            if (u.S("regen") != 0 && u.Hp > 0)
            {
                double b = u.Hp;
                u.Hp = Math.Min(u.Max, u.Hp + u.Max * .06);
                u.St["regen"]--;
                if (u.Hp > b) dot.Fx.Add(new BattleHit { Unit = u.Key, Heal = JsMath.Round(u.Hp - b) });
            }
            NapBoon(u, dot);
            double rg = u.Hero?.Rg ?? 0;
            if (rg != 0 && u.Hp > 0 && u.Hp < u.Max)
            {
                double b = u.Hp;
                u.Hp = Math.Min(u.Max, u.Hp + u.Max * rg / 100);
                dot.Fx.Add(new BattleHit { Unit = u.Key, Heal = JsMath.Round(u.Hp - b) });
            }
            if (u.Hp <= 0)
            {
                u.Hp = 0;
                if (!u.IsFoe) Nap(u);
                dot.Fx.Add(new BattleHit { Unit = u.Key, Ko = true });
            }
            foreach (var f in dot.Fx) { f.Hp = Math.Max(0, JsMath.Round(u.Hp)); f.Max = u.Max; }
            return dot;
        }

        static void Lose(BattleUnit u, BattleEvent dot, double share, string status, string tag)
        {
            double d = Math.Max(1, JsMath.Round(u.Max * share));
            u.Hp -= d;
            u.St[status]--;
            dot.Fx.Add(new BattleHit { Unit = u.Key, Damage = d, Dot = tag });
        }

        // the bloom perk: every hurt teammate heals a little on this pet's turn
        BattleEvent Bloom(BattleUnit u)
        {
            double bl = Perk(u, "bloom");
            if (bl == 0) return null;
            var e = new BattleEvent { Kind = "dot", Actor = u.Key };
            foreach (var m in Mates(u))
            {
                if (Dead(m) || m.Hp >= m.Max) continue;
                double b = m.Hp;
                m.Hp = Math.Min(m.Max, m.Hp + m.Max * bl / 100);
                var f = Snap(m);
                f.Heal = JsMath.Round(m.Hp - b);
                e.Fx.Add(f);
            }
            return e.Fx.Count > 0 ? e : null;
        }

        // expose, taunt, pow, dodge and flustered lose a turn before the unit acts, so pow N powers N-1 attacks
        static void Decay(BattleUnit u)
        {
            foreach (var k in Decays) if (u.S(k) != 0) u.St[k]--;
        }

        // a slam lands on whoever still stands in its lane
        BattleEvent GroundSlam(BattleUnit u)
        {
            char z = slam.Value.zone;
            slam = null;
            var e = new BattleEvent { Kind = "atk", Actor = u.Key, Name = "Ground Slam", Icon = "💥", Uv = "cutin", Anim = "slash", Aoe = true, Foe = true, Zone = z.ToString() };
            var hit = LiveHeroes().FindAll(h => h.Lane == z);
            foreach (var h in hit) HitOne(u, h, 1.45, e, noFoot: true);
            if (hit.Count == 0)
            {
                e.Kind = "buff";
                e.Pops.Add(Pop(u.Key, "💨 Everyone dodged!"));
            }
            else if (LiveHeroes().Exists(h => h.Lane != z)) e.Pops.Add(Pop(null, $"💥 {LaneName(z)} lane slammed"));
            return e;
        }

        // the foe picks the lane with the most pets and warns everyone
        BattleEvent WindUpSlam(BattleUnit u)
        {
            var count = new Dictionary<char, int> { ['l'] = 0, ['c'] = 0, ['r'] = 0 };
            foreach (var h in LiveHeroes()) count[h.Lane]++;
            int top = Math.Max(count['l'], Math.Max(count['c'], count['r']));
            var tied = new List<char>();
            foreach (char l in Lanes) if (count[l] == top) tied.Add(l);
            char z = tied[rng.Range(tied.Count)];
            slam = (u.Key, z, Round);
            var e = new BattleEvent { Kind = "slamw", Actor = u.Key, Name = "Winding up a slam", Icon = "⚠️", Foe = true, Zone = z.ToString() };
            e.Pops.Add(Pop(u.Key, $"⚠️ Slam on {LaneName(z)}!"));
            return e;
        }
    }
}
