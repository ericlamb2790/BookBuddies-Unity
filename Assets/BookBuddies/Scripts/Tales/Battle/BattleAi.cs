using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Move choice: every unit scores each usable move on each target (val) and takes the best, with a little noise.
    // Pets also follow their tactics (a move order, auto-ult off), and the Dark Passenger can take over a literary pet.
    public sealed partial class BattleEngine
    {
        sealed class Choice
        {
            public MoveDef Move;
            public BattleUnit Target;
            public double Value;
            public bool Takeover;
        }

        static readonly BattleUnit[] NoTarget = { null };

        // choose(u), wrapped by the Passenger takeover
        Choice Choose(BattleUnit u)
        {
            if (!u.IsFoe) { var forced = PassengerTakeover(u) ?? TacticsChoice(u); if (forced != null) return forced; }
            else if (u.S("wind") != 0) { var w = u.Moves.Find(m => m.Wind); if (w != null) return new Choice { Move = w }; }

            var taunts = u.IsFoe ? LiveHeroes().FindAll(h => h.S("taunt") != 0) : new List<BattleUnit>();
            Choice bestNon = null, ult = null;
            foreach (var a in Usable(u))
                foreach (var t in Candidates(u, a, taunts.Count > 0 ? taunts : LaneOpp(u)))
                {
                    var c = new Choice { Move = a, Target = t, Value = Val(u, a, t) * (.98 + rng.Next() * .04) };
                    if (a.Ult) { if (ult == null || c.Value > ult.Value) ult = c; }
                    else if (bestNon == null || c.Value > bestNon.Value) bestNon = c;
                }
            return ult != null && UltNow(u, ult, bestNon) ? ult : bestNon ?? ult;
        }

        // the ult fires when asked for, when it's clearly best, for a healer when someone is down or low, or against 3+ foes
        bool UltNow(BattleUnit u, Choice ult, Choice bestNon)
        {
            bool need = Heroes.Exists(h => h.Ko) || LiveHeroes().Exists(h => h.Hp / h.Max < .35);
            return (u.Hero?.WantUlt ?? false) || ult.Value >= (bestNon?.Value ?? 0) * 1.35 || (need && u.Hero?.Cls == "healer") || LiveFoes().Count >= 3;
        }

        // foes: moves off cooldown; pets: known moves they have the ink for
        List<MoveDef> Usable(BattleUnit u) =>
            u.IsFoe ? u.Moves.FindAll(m => !(u.Cds.TryGetValue(m.Key, out var cd) && cd > 0)) : u.Moves.FindAll(m => CostOf(u, m) <= u.Ink);

        // tacChoose: "My order" takes the first listed move worth using; with auto-ult off the pet picks the best regular move
        Choice TacticsChoice(BattleUnit u)
        {
            var h = u.Hero;
            bool ordered = h.Order != null && h.Order.Count > 0;
            if ((!ordered && h.AutoUlt) || h.WantUlt) return null;
            var list = Usable(u);
            if (ordered)
                foreach (var k in h.Order)
                {
                    var a = list.Find(m => m.Key == k);
                    if (a == null || (a.Ult && !h.AutoUlt)) continue;
                    var c = BestTarget(u, a);
                    if (c != null && c.Value > 0) return c;
                }
            if (h.AutoUlt) return null;
            Choice best = null;
            foreach (var a in list)
            {
                if (a.Ult) continue;
                var c = BestTarget(u, a);
                if (c != null && (best == null || c.Value > best.Value)) best = c;
            }
            return best;
        }

        Choice BestTarget(BattleUnit u, MoveDef a)
        {
            Choice b = null;
            foreach (var t in Candidates(u, a, LaneOpp(u)))
            {
                double v = Val(u, a, t);
                if (b == null || v > b.Value) b = new Choice { Move = a, Target = t, Value = v };
            }
            return b;
        }

        // the targets a move is scored on: each reachable foe for "one", the lowest-HP foe for "low", else none.
        // Deliberate fix: the site scored "low" moves with no target, so their damage counted as 0 and they were never picked.
        IList<BattleUnit> Candidates(BattleUnit u, MoveDef a, List<BattleUnit> reachable)
        {
            if (a.Target == "one") return reachable;
            if (a.Target == "low") { var low = Lowest(Opp(u)); if (low != null) return new[] { low }; }
            return NoTarget;
        }

        // at 3+ Passenger stacks the shadow may grab the wheel: its hardest-hitting regular move on a random foe
        Choice PassengerTakeover(BattleUnit u)
        {
            int dk = u.Hero.Dark;
            if (dk < 3 || rng.Next() >= (dk - 2) * .18) return null;
            var foes = Opp(u);
            var list = u.Moves.FindAll(a => a["p"] != 0 && !a.Ult && CostOf(u, a) <= u.Ink);
            if (foes.Count == 0 || list.Count == 0) return null;
            double Weight(MoveDef a) => a["p"] * (a["n"] != 0 ? a["n"] : 1);
            var pick = list[0];
            foreach (var a in list) if (Weight(a) > Weight(pick)) pick = a;
            return new Choice { Move = pick, Target = rng.Pick(foes), Takeover = true };
        }

        // val: how good a move is right now
        double Val(BattleUnit u, MoveDef ab, BattleUnit t)
        {
            var foes = Opp(u);
            var frs = Mates(u);
            double v = AttackValue(u, ab, t, foes, frs) + HealValue(u, ab, frs) + SupportValue(u, ab, foes, frs);
            return u.IsFoe ? v : v + LiteraryValue(u, ab, t, foes);
        }

        static double Threat(BattleUnit x) => x.Atk * (x.Boss ? 1.4 : 1);

        // exe and pe swap in a bigger multiplier against a low or exposed target
        static double PowerOn(MoveDef ab, BattleUnit t) =>
            ab["exe"] != 0 && t.Hp < t.Max * .35 ? ab["exe"] : ab["pe"] != 0 && t.S("expose") != 0 ? ab["pe"] : ab["p"];

        double AttackValue(BattleUnit u, MoveDef ab, BattleUnit t, List<BattleUnit> foes, List<BattleUnit> frs)
        {
            if (foes.Count == 0) return 0;
            if (ab.Wind && u.S("wind") == 0) return Estimate(u, t ?? foes[0], ab["p"]) * .55;
            if (ab["p"] == 0) return 0;
            double KillValue(BattleUnit x, double p)
            {
                double d = Estimate(u, x, p, ab["cc"]);
                return Math.Min(d, x.Hp) + (d >= x.Hp ? 18 + Threat(x) : 0);
            }
            double v = 0;
            if (ab.Target == "rand")
            {
                foreach (var f in foes) v += Math.Min(Estimate(u, f, ab["p"], ab["cc"]), f.Hp);
                v = v / Math.Max(1, foes.Count) * ab.Hits * 1.05;
            }
            else if (ab.Target == "all")
            {
                foreach (var f in foes)
                {
                    v += KillValue(f, ab["p"]);
                    if (ab["exp"] != 0 && f.S("expose") == 0) v += 7;
                    if (ab["poison"] != 0 && f.S("poison") == 0) v += f.Max * .05 * ab["poison"] * .7;
                }
                v += foes.Count * ab["stun"] * 9;
            }
            else if (t != null)
            {
                double p = PowerOn(ab, t), est = Estimate(u, t, p);
                bool lives = est < t.Hp;
                v += KillValue(t, p);
                if (ab["stun"] != 0 && t.S("stun") == 0 && lives) v += ab["stun"] * Threat(t) * 1.4;
                if (ab["bleed"] != 0 && t.S("bleed") == 0 && lives) v += t.Max * .06 * ab["bleed"] * .8;
                if (ab["exp"] != 0 && t.S("expose") == 0 && lives) { double sum = 0; foreach (var m in frs) sum += m.Atk; v += sum * .3 * Math.Min(ab["exp"], 2); }
                if (ab["drain"] != 0) v += Math.Min(est * ab["drain"], u.Max - u.Hp) * .8;
                if (ab["ink"] != 0 && !t.IsFoe) v += Math.Min(t.Ink, -ab["ink"]) * 6;
                if (ab["steal"] != 0) v += 4;
            }
            if (ab["inkAll"] != 0) v += Heroes.FindAll(h => h.Ink > 0).Count * 5;
            return v;
        }

        double HealValue(BattleUnit u, MoveDef ab, List<BattleUnit> frs)
        {
            double hl = ab["hl"], rv = ab["rv"];
            if (hl == 0 && rv == 0) return 0;
            var who = ab.Target == "ally" ? new List<BattleUnit> { Lowest(frs) } : rv != 0 ? (u.IsFoe ? new List<BattleUnit>() : Heroes) : frs;
            double v = 0;
            foreach (var m in who)
            {
                if (m == null) continue;
                if (!m.IsFoe && m.Ko) { if (rv != 0) v += 40 + m.Max * rv; continue; }
                double pct = m.Hp / m.Max;
                v += Math.Min(m.Max * hl, m.Max - m.Hp) * (pct < .35 ? 2.2 : pct < .6 ? 1.3 : .45);
                if (ab["clean"] != 0 && (m.S("bleed") != 0 || m.S("poison") != 0 || m.S("stun") != 0)) v += 10;
            }
            return v;
        }

        double SupportValue(BattleUnit u, MoveDef ab, List<BattleUnit> foes, List<BattleUnit> frs)
        {
            double v = 0, foeAtk = 0;
            foreach (var f in foes) foeAtk += f.Atk;
            var self = new List<BattleUnit> { u };
            if (ab["sh"] != 0)
                foreach (var m in ab.Target == "self" ? self : frs) v += Math.Max(0, Math.Min(m.Max * ab["sh"], foeAtk * .9 - m.S("shield"))) * .55;
            if (ab["shs"] != 0) v += u.S("shield") != 0 ? 0 : u.Max * ab["shs"] * .4;
            if (ab["pow"] != 0) foreach (var m in ab.Target == "self" ? self : frs) v += m.S("pow") != 0 ? 0 : m.Atk * .3 * ab["pow"];
            if (ab["dodge"] != 0) v += u.S("dodge") != 0 ? -8 : foeAtk / Math.Max(1, frs.Count) * .45 * ab["dodge"] * .7;
            if (ab["regen"] != 0) foreach (var m in frs) v += m.S("regen") != 0 ? 0 : Math.Min(m.Max * .06 * ab["regen"], m.Max - m.Hp) * .8 + 3;
            if (!u.IsFoe && ab["ink"] > 0) v += 6 * ab["ink"];
            if (ab["taunt"] != 0) v += u.Hp / u.Max > .55 ? 10 : -12;
            if (ab.Crit) v += u.S("crit") != 0 ? -99 : u.Atk * 1.1;
            if (ab["buff"] != 0) v += BuffsOn(u) >= 2 ? -99 : 14;
            if (!u.IsFoe && ab.Cost != 0 && !ab.Ult) v -= ab.Cost * u.Atk * .28;
            return v;
        }

        // the literary classes' extra terms (_va6)
        double LiteraryValue(BattleUnit u, MoveDef ab, BattleUnit t, List<BattleUnit> foes)
        {
            var f = t ?? (foes.Count > 0 ? foes[0] : null);
            int dk = u.Hero.Dark;
            double v = 0;
            if (ab["weak"] != 0 && f != null && f.S("weak") <= 0) v += f.Atk * .9 * (ab.Target == "all" ? Math.Min(3, foes.Count) : 1);
            if (ab.Pull && f != null && f.Lane != u.Lane) v += 10;
            if (ab.Gath) v += foes.FindAll(x => x.Lane != u.Lane).Count * 7;
            if (ab["dark"] != 0) v += dk < 4 ? 6 : -6;
            if (ab["calm"] != 0) v += dk * 7 - 4;
            if (ab.Leap) v += 4;
            return v;
        }

        // the unit with the lowest share of its max HP (first on ties)
        static BattleUnit Lowest(List<BattleUnit> units)
        {
            BattleUnit low = null;
            foreach (var x in units) if (low == null || x.Hp / x.Max < low.Hp / low.Max) low = x;
            return low;
        }

        int BuffsOn(BattleUnit u) => buffs.TryGetValue(u, out var n) ? n : 0;
    }
}
