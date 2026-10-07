using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Damage (dmg), single hits (hitOne) and what equipped gear does when a pet hits or gets hit
    public sealed partial class BattleEngine
    {
        // attack × move power × defence, genre ring, statuses, boons, perks, cover and the enrage timer; also the crit chance
        double BaseDamage(BattleUnit a, BattleUnit d, double mult, double moveCc, out double eff, out double cc)
        {
            mult *= LiteraryMul(a);
            double x = a.Atk * mult * Math.Max(.14, 100 / (100 + d.Def * 4));
            eff = (a.Gen + 1) % 6 == d.Gen ? GenreEdge(a) : (d.Gen + 1) % 6 == a.Gen ? .75 : 1;
            x *= eff;
            if (d.S("expose") != 0) x *= 1.35;
            if (a.S("pow") != 0) x *= 1.25;
            x *= Cliffhanger(a, d);
            double ex = Perk(a, "execute");
            if (ex != 0 && d.IsFoe && d.Hp < d.Max * .35) x *= 1 + ex / 100;
            x *= CoverMul(a, d);
            if (a.IsFoe && Round > 12) x *= 1 + .1 * (Round - 12);
            cc = Math.Min(.9, (a.IsFoe ? .06 : .1) + DogEar(a) + moveCc + (a.IsFoe ? 0 : (a.Hero?.Cc ?? 0) / 100));
            return x;
        }

        // _dm6: each Passenger stack +12%, a spiteful class below half HP ×1.3, a flustered foe ×.7
        double LiteraryMul(BattleUnit a)
        {
            if (a.IsFoe) return a.S("weak") > 0 ? .7 : 1;
            double m = 1 + .12 * a.Hero.Dark;
            if (TalesData.Current.Class(a.Hero.Cls).Spite && a.Hp < a.Max * .5) m *= 1.3;
            return m;
        }

        /// <summary>dmg with est: the expected damage the AI plans with (crit folded in, shields counted at 80%).</summary>
        double Estimate(BattleUnit a, BattleUnit d, double mult, double moveCc = 0)
        {
            double x = BaseDamage(a, d, mult, moveCc, out _, out double cc);
            x *= 1 + (a.S("crit") != 0 ? .6 : cc * .6);
            return Math.Max(1, x - d.S("shield") * .8);
        }

        // dmg: rolls ±10% and the crit, then the shield soaks what it can
        (double x, bool crit, double eff, double ab) Damage(BattleUnit a, BattleUnit d, double mult, double moveCc)
        {
            double x = BaseDamage(a, d, mult, moveCc, out double eff, out double cc);
            x *= .9 + rng.Next() * .2;
            bool crit = false;
            if (a.S("crit") != 0 || rng.Next() < cc)
            {
                crit = true;
                x *= 1.6 + (a.IsFoe ? 0 : (a.Hero?.Cd ?? 0) / 100);
                a.St["crit"] = 0;
            }
            double ab = 0, shield = d.S("shield");
            if (shield != 0)
            {
                ab = Math.Min(shield, x);
                d.St["shield"] = shield - ab;
                x -= ab;
            }
            return (Math.Max(ab != 0 ? 0 : 1, JsMath.Round(x)), crit, eff, JsMath.Round(ab));
        }

        // hitOne: dodges and misses, the hit, KOs (a pet's phoenix feather, then plot armor), gear effects, then the footnote. Returns the damage.
        double HitOne(BattleUnit a, BattleUnit t, double mult, BattleEvent e, bool noFoot = false, double moveCc = 0)
        {
            if (Dead(t) || GearDodge(a, t, e)) return 0;
            if (Misses(a, t))
            {
                var m = Snap(t);
                m.Miss = true;
                e.Fx.Add(m);
                return 0;
            }
            var r = Damage(a, t, mult, moveCc);
            t.Hp -= r.x;
            var f = new BattleHit { Unit = t.Key, Damage = r.x, Crit = r.crit, Effect = r.eff, Absorbed = r.ab };
            if (t.Hp <= 0) { Fall(t, f); if (t.IsFoe && !a.IsFoe) Sequel(); }
            f.Hp = Math.Max(0, JsMath.Round(t.Hp));
            f.Max = t.Max;
            f.Shield = JsMath.Round(t.S("shield"));
            e.Fx.Add(f);
            GearHit(a, t, r.x, r.crit, e);
            if (!a.IsFoe && (best == null || r.x > best.Value.d)) best = (a.Name, e.Name, r.x);
            if (!a.IsFoe && r.crit) crits++;
            if (!noFoot) Footnote(a, t, mult, e);
            return r.x;
        }

        // a foe with dodge evades 20%, a pet with the dodge status 45%, and bushes 25% of foe swings
        bool Misses(BattleUnit a, BattleUnit t) =>
            (t.IsFoe && t.Foe.Def.Dodge && rng.Next() < .2) || (!t.IsFoe && t.S("dodge") != 0 && rng.Next() < .45) || BushMiss(a, t);

        // a pet at 0 HP rises once with the phoenix perk, else hangs on with plot armor, otherwise naps; a foe is out
        void Fall(BattleUnit t, BattleHit f)
        {
            double phoenix = Perk(t, "phoenix");
            if (!t.IsFoe && phoenix != 0 && !t.Hero.PhoenixUsed)
            {
                t.Hero.PhoenixUsed = true;
                t.Hp = Math.Max(1, JsMath.Round(t.Max * phoenix / 100));
                f.Pop = "🪶 Back on their feet!";
                return;
            }
            if (PlotArmor(t, f)) return;
            t.Hp = 0;
            f.Ko = true;
            if (!t.IsFoe) Nap(t);
        }

        double Perk(BattleUnit u, string perk) => u.IsFoe || u.Hero?.Gear == null ? 0 : u.Hero.Gear.Perk(perk);

        // gearHit: lifesteal and perk procs when a pet hits a foe; thorns when a foe hits a pet
        void GearHit(BattleUnit a, BattleUnit t, double x, bool crit, BattleEvent e)
        {
            if (!a.IsFoe && t.IsFoe)
            {
                LifeSteal(a, x, e);
                if (t.Hp > 0) PerkProcs(a, t, x, e);
                if (crit) CritPerks(a, t, x, e);
            }
            double th = t.IsFoe ? 0 : t.Hero?.Th ?? 0;
            if (a.IsFoe && th != 0 && x > 0 && !Dead(a)) Splash(t, a, Math.Max(1, JsMath.Round(x * th / 100)), e, "🌵 ");
        }

        void LifeSteal(BattleUnit a, double x, BattleEvent e)
        {
            double ls = a.Hero?.Ls ?? 0;
            if (ls == 0 || a.Hp <= 0 || a.Hp >= a.Max) return;
            double b = a.Hp;
            a.Hp = Math.Min(a.Max, a.Hp + x * ls / 100);
            double h = JsMath.Round(a.Hp - b);
            if (h <= 0) return;
            var f = Snap(a);
            f.Heal = h;
            e.Fx.Add(f);
        }

        void PerkProcs(BattleUnit a, BattleUnit t, double x, BattleEvent e)
        {
            double ember = Perk(a, "ember"), venom = Perk(a, "venom"), frost = Perk(a, "frost"), echo = Perk(a, "echo");
            if (ember != 0 && rng.Next() < ember / 100) { t.St["bleed"] = Math.Max(t.S("bleed"), 3); PerkPop(e, a, "ember", "Burning!"); }
            if (venom != 0 && rng.Next() < venom / 100) { t.St["poison"] = Math.Max(t.S("poison"), 3); PerkPop(e, a, "venom", "Poisoned!"); }
            if (frost != 0 && rng.Next() < frost / 100 * (t.Boss ? .5 : 1)) { t.St["stun"] = 1; PerkPop(e, a, "frost", "Dizzy!"); }
            if (echo != 0 && rng.Next() < echo / 100) { Splash(a, t, Math.Max(1, JsMath.Round(x * .5)), e, "🔁 "); PerkPop(e, a, "echo", "Echo!"); }
        }

        void CritPerks(BattleUnit a, BattleUnit t, double x, BattleEvent e)
        {
            double scribe = Perk(a, "scribe"), chain = Perk(a, "chain");
            if (scribe != 0 && rng.Next() < scribe / 100) { a.Ink = Math.Min(6, a.Ink + 1); PerkPop(e, a, "scribe", "+1 ink"); }
            if (chain == 0) return;
            var others = Opp(a).FindAll(f => f != t && !Dead(f));
            if (others.Count == 0) return;
            Splash(a, rng.Pick(others), Math.Max(1, JsMath.Round(x * chain / 100)), e, "⚡ ");
            PerkPop(e, a, "chain", "Chain!");
        }

        // gearDodge: a pet's dodge stat sidesteps a foe's hit; the mirror perk hits back for 60% of the pet's attack
        bool GearDodge(BattleUnit a, BattleUnit t, BattleEvent e)
        {
            double dg = t.IsFoe ? 0 : t.Hero?.Dg ?? 0;
            if (!a.IsFoe || dg == 0 || rng.Next() >= dg / 100) return false;
            var m = Snap(t);
            m.Miss = true;
            e.Fx.Add(m);
            if (Perk(t, "mirror") != 0 && !Dead(a))
            {
                Splash(t, a, Math.Max(1, JsMath.Round(t.Atk * .6)), e, "🪞 ");
                PerkPop(e, t, "mirror", "Mirror!");
            }
            return true;
        }

        // gearSplash: small extra damage that can't crit, ignores shields and can KO
        void Splash(BattleUnit from, BattleUnit t, double x, BattleEvent e, string tag)
        {
            if (Dead(t)) return;
            t.Hp = Math.Max(0, t.Hp - x);
            var f = Snap(t);
            f.Damage = x;
            f.Small = true;
            f.Dot = tag;
            if (t.Hp <= 0) { t.Hp = 0; f.Ko = true; }
            e.Fx.Add(f);
        }

        BattleEvent poppedFor;
        readonly HashSet<string> popped = new HashSet<string>();

        // each perk's callout shows once per event: "🔥 Emberfang!" (the item's name) or "🔥 Burning!"
        void PerkPop(BattleEvent e, BattleUnit u, string perk, string text)
        {
            if (e != poppedFor) { poppedFor = e; popped.Clear(); }
            if (!popped.Add(perk)) return;
            string item = u.Hero?.Gear?.PerkFrom.TryGetValue(perk, out var n) == true ? n : null;
            e.Pops.Add(Pop(u.Key, PerkIcon(perk) + " " + (item != null ? item + "!" : text)));
        }

        static Dictionary<string, string> perkIcons;

        static string PerkIcon(string perk)
        {
            if (perkIcons == null)
            {
                perkIcons = new Dictionary<string, string>();
                var all = Json.ParseObject(TalesData.TextLoader("Data/loot")).Obj("LT_PERK");
                if (all != null) foreach (var kv in all) perkIcons[kv.Key] = ((Dictionary<string, object>)kv.Value).Str("i", "✨");
            }
            return perkIcons.TryGetValue(perk, out var i) ? i : "✨";
        }
    }
}
