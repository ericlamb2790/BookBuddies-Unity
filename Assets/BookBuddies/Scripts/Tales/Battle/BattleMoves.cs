using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // exec: pays for and performs a move (hits, riders, heals, shields, buffs), then the literary classes' extras
    public sealed partial class BattleEngine
    {
        BattleEvent Exec(BattleUnit u, MoveDef ab, BattleUnit tk, bool takeover)
        {
            var leap = u.IsFoe ? null : Leap(u, ab, tk);
            var e = Perform(u, ab, tk);
            if (!u.IsFoe) AfterMove(u, ab, e, leap, takeover);
            return e;
        }

        BattleEvent Perform(BattleUnit u, MoveDef ab, BattleUnit tk)
        {
            var e = new BattleEvent
            {
                Kind = "atk", Actor = u.Key, Name = NameOf(u, ab), Icon = IconOf(u, ab), Ult = ab.Ult, Uv = ab.Uv ?? "cutin", Foe = u.IsFoe, Anim = AnimOf(u, ab),
            };
            Pay(u, ab);
            if (ab.Wind && u.S("wind") == 0)
            {
                u.St["wind"] = 1;
                e.Kind = "buff";
                e.Name = "Tick… tock…";
                e.Icon = "⏰";
                e.Pops.Add(Pop(u.Key, "⏰ Winding up a huge swing!"));
                return Fin(u, e);
            }
            if (ab.Wind) u.St["wind"] = 0;

            var targets = TargetsOf(u, ab, tk);
            e.Aoe = ab.Target == "all" || ab.Target == "party";
            if (ab["p"] != 0) Strike(u, ab, targets, e);
            if (ab["hl"] != 0 || ab["rv"] != 0) Heal(u, ab, targets, e);
            if (ab["sh"] != 0) ShieldMates(u, ab, e);
            if (!u.IsFoe && ab["ink"] > 0) { u.Ink = Math.Min(6, u.Ink + (int)ab["ink"]); e.Pops.Add(Pop(u.Key, "🪙 +1 ink")); }
            if (ab["pow"] != 0 || ab["regen"] != 0 || ab["dodge"] != 0) Buff(u, ab, e);
            SelfEffects(u, ab, e);
            return Fin(u, e);
        }

        // pets spend ink; a foe's special goes on cooldown (cd + 1, as it ticks down before the foe's next choice)
        void Pay(BattleUnit u, MoveDef ab)
        {
            if (!u.IsFoe)
            {
                u.Ink = Math.Max(0, u.Ink - ab.Cost);
                u.Hero.WantUlt = false;
            }
            else if (ab["cd"] != 0) u.Cds[ab.Key] = (int)ab["cd"] + 1;
        }

        // fin: an ult empties the ink well; any other move refills 1 ink (2 on a crit)
        static BattleEvent Fin(BattleUnit u, BattleEvent e)
        {
            if (!u.IsFoe) u.Ink = e.Ult ? 0 : Math.Min(6, u.Ink + 1 + (e.Fx.Exists(f => f.Crit) ? 1 : 0));
            if (e.Anim == "arc") e.Projectile = ProjectileFor(e.Name, u.IsFoe);
            return e;
        }

        /// <summary>The arc projectile a move throws: TQ_PJ[hash(name)%10] (foe moves hash name + "~").</summary>
        public static string ProjectileFor(string moveName, bool foe)
        {
            var pj = TalesData.Current.Projectiles;
            return pj.Length == 0 ? "lob" : pj[JsMath.Hash((moveName ?? "") + (foe ? "~" : "")) % (uint)pj.Length];
        }

        static string NameOf(BattleUnit u, MoveDef ab) =>
            !u.IsFoe ? HeroFactory.MoveName(u, ab.Key) : ab == FoeFactory.Hit ? u.Foe.Def.An : u.Foe.Sn;

        static string IconOf(BattleUnit u, MoveDef ab) =>
            !u.IsFoe ? HeroFactory.MoveIcon(u, ab.Key) : ab == FoeFactory.Hit ? u.Foe.Def.I : u.Foe.Si ?? u.Foe.Def.I;

        static string AnimOf(BattleUnit u, MoveDef ab)
        {
            if (ab.Fx != null) return ab.Fx;
            if (!u.IsFoe) return "arc";
            if (ab == FoeFactory.Hit) return u.Boss ? "slash" : "arc";
            return u.Foe.Sp != null && TalesData.Current.SpecialFx.TryGetValue(u.Foe.Sp, out var fx) && fx != "" ? fx : "arc";
        }

        List<BattleUnit> TargetsOf(BattleUnit u, MoveDef ab, BattleUnit tk)
        {
            var foes = Opp(u);
            switch (ab.Target)
            {
                case "one":
                    var t = tk != null && !Dead(tk) ? tk : foes.Count > 0 ? foes[0] : null;
                    return t == null ? new List<BattleUnit>() : new List<BattleUnit> { t };
                case "low": return Single(Lowest(foes));
                case "all": return foes;
                case "self": return new List<BattleUnit> { u };
                case "ally": return Single(Lowest(Mates(u)));
                case "party": return ab["rv"] != 0 ? new List<BattleUnit>(u.IsFoe ? Foes : Heroes) : Mates(u);
                default: return new List<BattleUnit>();
            }
        }

        static List<BattleUnit> Single(BattleUnit u) => u == null ? new List<BattleUnit>() : new List<BattleUnit> { u };

        void Strike(BattleUnit u, MoveDef ab, List<BattleUnit> targets, BattleEvent e)
        {
            if (ab.Target == "rand") RandomHits(u, ab, e);
            else
                foreach (var t in targets)
                {
                    double d = HitOne(u, t, PowerOn(ab, t), e, noFoot: ab.Target != "one", moveCc: ab["cc"]);
                    if (!Dead(t)) Riders(ab, t, e);
                    if (ab["drain"] != 0 && d > 0) Drain(u, d * ab["drain"], e);
                }
            if (ab["inkAll"] != 0)
            {
                foreach (var h in Heroes) h.Ink = Math.Max(0, h.Ink + (int)ab["inkAll"]);
                e.Pops.Add(Pop(null, ab["inkAll"] < 0 ? "♟️ Every pet loses 1 ink" : "💧 +1 ink for everyone"));
            }
        }

        // n hits at random foes, one after another; bleed and poison stick to whoever they hit (the site dropped them)
        void RandomHits(BattleUnit u, MoveDef ab, BattleEvent e)
        {
            e.Seq = true;
            for (int i = 0; i < ab.Hits; i++)
            {
                var opp = Opp(u);
                if (opp.Count == 0) break;
                var t = rng.Pick(opp);
                HitOne(u, t, ab["p"], e, noFoot: true, moveCc: ab["cc"]);
                if (Dead(t)) continue;
                if (ab["bleed"] != 0) Afflict(t, "bleed", ab["bleed"], "🩸 Bleeding", e);
                if (ab["poison"] != 0) Afflict(t, "poison", ab["poison"], "🧪 Poisoned", e);
            }
        }

        // what a hit leaves behind on a target that's still standing (also after a miss)
        void Riders(MoveDef ab, BattleUnit t, BattleEvent e)
        {
            if (ab["stun"] != 0 && rng.Next() < ab["stun"] && t.S("stun") == 0) { t.St["stun"] = 1; e.Pops.Add(Pop(t.Key, "💫 Dizzy!")); }
            if (ab["bleed"] != 0) Afflict(t, "bleed", ab["bleed"], "🩸 Bleeding", e);
            if (ab["exp"] != 0) Afflict(t, "expose", ab["exp"], "🔍 Exposed!", e);
            if (ab["poison"] != 0) Afflict(t, "poison", ab["poison"], "🧪 Poisoned", e);
            if (ab["ink"] != 0 && !t.IsFoe && t.Ink != 0) { t.Ink = Math.Max(0, t.Ink + (int)ab["ink"]); e.Pops.Add(Pop(t.Key, "🫙 Ink scrambled!")); }
        }

        static void Afflict(BattleUnit t, string status, double turns, string pop, BattleEvent e)
        {
            t.St[status] = turns;
            e.Pops.Add(Pop(t.Key, pop));
        }

        static void Drain(BattleUnit u, double amount, BattleEvent e)
        {
            double amt = JsMath.Round(amount);
            u.Hp = Math.Min(u.Max, u.Hp + amt);
            var f = Snap(u);
            f.Heal = amt;
            e.Fx.Add(f);
        }

        // heals (and revives) the targets; a move that hits foes heals the user's side instead (the site healed the foes)
        void Heal(BattleUnit u, MoveDef ab, List<BattleUnit> targets, BattleEvent e)
        {
            if (ab["p"] == 0) e.Kind = "heal";
            bool hitsFoes = ab.Target == "one" || ab.Target == "low" || ab.Target == "all" || ab.Target == "rand";
            foreach (var t in hitsFoes ? Mates(u) : targets)
            {
                if (Dead(t))
                {
                    if (ab["rv"] == 0) continue;
                    t.Ko = false;
                    t.Hp = JsMath.Round(t.Max * ab["rv"]);
                    t.St.Clear();
                    var r = Snap(t);
                    r.Revive = true;
                    r.Heal = t.Hp;
                    e.Fx.Add(r);
                    continue;
                }
                double b = t.Hp;
                t.Hp = Math.Min(t.Max, t.Hp + t.Max * ab["hl"]);
                if (ab["clean"] != 0) { t.St["bleed"] = 0; t.St["poison"] = 0; t.St["stun"] = 0; }
                var f = Snap(t);
                f.Heal = JsMath.Round(t.Hp - b);
                e.Fx.Add(f);
            }
        }

        // sh shields every teammate; a self move shields only the user (the site shielded the whole party)
        void ShieldMates(BattleUnit u, MoveDef ab, BattleEvent e)
        {
            if (ab["p"] == 0) e.Kind = "shield";
            foreach (var t in ab.Target == "self" ? new List<BattleUnit> { u } : Mates(u)) AddShield(t, JsMath.Round(t.Max * ab["sh"]), e);
        }

        static void AddShield(BattleUnit t, double s, BattleEvent e)
        {
            t.St["shield"] = t.S("shield") + s;
            var f = Snap(t);
            f.ShieldGained = s;
            e.Fx.Add(f);
        }

        // pow, regen and dodge: on the user for a self move, else on every teammate (the longer duration wins)
        void Buff(BattleUnit u, MoveDef ab, BattleEvent e)
        {
            if (ab["p"] == 0 && ab["hl"] == 0 && ab["sh"] == 0) e.Kind = "buff";
            bool self = ab.Target == "self";
            foreach (var t in self ? new List<BattleUnit> { u } : Mates(u))
            {
                foreach (var k in new[] { "pow", "regen", "dodge" }) if (ab[k] != 0) t.St[k] = Math.Max(t.S(k), ab[k]);
                var f = Snap(t);
                f.Buff = ab["pow"] != 0 ? "💪" : ab["dodge"] != 0 ? "🪞" : "💚";
                e.Fx.Add(f);
            }
            string text = ab["pow"] != 0 ? (self ? "💪 Powered up!" : "💪 Party powered up!") : ab["dodge"] != 0 ? "🪞 Hard to hit!" : "💚 Regen";
            e.Pops.Add(Pop(self ? u.Key : null, text));
        }

        // self shield, taunt, a sure crit next, and a foe's attack buff
        void SelfEffects(BattleUnit u, MoveDef ab, BattleEvent e)
        {
            if (ab["shs"] != 0)
            {
                if (ab["p"] == 0) e.Kind = "shield";
                AddShield(u, JsMath.Round(u.Max * ab["shs"]), e);
            }
            if (ab["taunt"] != 0) { u.St["taunt"] = ab["taunt"]; e.Pops.Add(Pop(u.Key, "📢 Over here!")); }
            if (ab.Crit) { e.Kind = "buff"; u.St["crit"] = 1; e.Pops.Add(Pop(u.Key, "🌀 Next hit crits!")); }
            if (ab["buff"] != 0)
            {
                e.Kind = "buff";
                u.Atk = JsMath.Round(u.Atk * ab["buff"]);
                buffs[u] = BuffsOn(u) + 1;
                e.Pops.Add(Pop(u.Key, "💢 Attack up!"));
            }
        }

        // leap: a steelpush into the target's lane before the hit (the lane with most foes when there's no target yet)
        KeyValuePair<string, string>? Leap(BattleUnit u, MoveDef ab, BattleUnit tk)
        {
            if (!ab.Leap) return null;
            var foes = Opp(u);
            var t = tk != null && !Dead(tk) ? tk : ab.Target == "low" ? Lowest(foes) : FirstInBusiestLane(foes);
            if (t == null || t.Lane == u.Lane) return null;
            SetLane(u, t.Lane);
            return Pop(u.Key, u.Hero.Cls == "windrunner" ? "🌬️ Lashing!" : "🪙 Steelpush!");
        }

        static BattleUnit FirstInBusiestLane(List<BattleUnit> foes)
        {
            char busiest = 'l';
            int most = -1;
            foreach (char z in Lanes)
            {
                int n = foes.FindAll(f => f.Lane == z).Count;
                if (n > most) { most = n; busiest = z; }
            }
            return foes.Find(f => f.Lane == busiest);
        }

        // _ex6: the Passenger's stacks, the Code's calm, flustered foes, and foes pulled into the pet's lane
        void AfterMove(BattleUnit u, MoveDef ab, BattleEvent e, KeyValuePair<string, string>? leap, bool takeover)
        {
            var info = u.Hero;
            if (leap != null) e.Pops.Insert(0, leap.Value);
            if (takeover) e.Pops.Insert(0, Pop(u.Key, "🌑 The Passenger takes over!"));
            if (ab["dark"] != 0) { info.Dark = Math.Min(5, info.Dark + (int)ab["dark"]); e.Pops.Add(Pop(u.Key, $"🌑 Passenger {info.Dark}/5")); }
            if (ab["calm"] != 0 && info.Dark != 0)
            {
                info.Dark = Math.Max(0, info.Dark - (int)ab["calm"]);
                e.Pops.Add(Pop(u.Key, info.Dark != 0 ? $"📏 The Code holds · {info.Dark}/5" : "📏 Back to the Code"));
            }
            if (ab["weak"] == 0 && !ab.Pull && !ab.Gath) return;
            var hit = new HashSet<string>();
            foreach (var f in e.Fx) hit.Add(f.Unit);
            foreach (var f in Opp(u).FindAll(x => hit.Contains(x.Key)))
            {
                if (ab["weak"] != 0) { f.St["weak"] = Math.Max(f.S("weak"), ab["weak"]); e.Pops.Add(Pop(f.Key, "😳 Flustered")); }
                if (ab.Pull && f.Lane != u.Lane && !f.Boss) { f.Lane = u.Lane; e.Pops.Add(Pop(f.Key, "💃 Obliged to move")); }
            }
            if (!ab.Gath) return;
            int n = 0;
            foreach (var f in Opp(u)) if (!f.Boss && f.Lane != u.Lane) { f.Lane = u.Lane; n++; }
            if (n > 0) e.Pops.Add(Pop(null, $"💃 {n} foe{(n > 1 ? "s" : "")} obliged to join your lane"));
        }
    }
}
