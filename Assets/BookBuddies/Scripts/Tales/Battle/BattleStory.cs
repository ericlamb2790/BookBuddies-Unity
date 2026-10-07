using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // The storybook side of a fight: fate rolls and the friends they bring, bosses rising again, and villains' banter
    public sealed partial class BattleEngine
    {
        // a boss with no phase list (every wild boss) rises once, as an enraged "Second Edition"
        static readonly BossPhase[] NoPhases = new BossPhase[0];
        static readonly string[] Enrage = { "enrage" };
        static readonly (string edition, string boast)[] Editions =
        {
            ("Second Edition", "You thought that was the ending? That was only the first draft!"),
            ("Final Chapter", "Every story has a last chapter. This one is MINE!"),
        };
        static readonly Dictionary<string, string> Risen = new Dictionary<string, string>
        {
            ["enrage"] = "hits much harder", ["shield"] = "raises a hardcover shield", ["summon"] = "calls its team back in",
            ["regen"] = "starts mending its pages", ["haste"] = "speeds up",
        };

        readonly List<string> usedNpcs = new List<string>();
        readonly List<string> seenLines = new List<string>();

        // a random pet rolls d20 + level/4: a 1 fumbles, under 8 finds ink, under 15 lines up a crit, else a friend helps
        void FateRoll(List<BattleEvent> evs)
        {
            var heroes = LiveHeroes();
            if (heroes.Count == 0 || LiveFoes().Count == 0) return;
            var h = rng.Pick(heroes);
            int bonus = h.Hero.Lvl / 4, roll = 1 + (int)Math.Floor(rng.Next() * 20), total = roll + bonus;
            var e = new BattleEvent { Kind = "fate", Actor = h.Key, Name = h.Name, Roll = roll, Bonus = bonus };
            if (roll == 1)
            {
                h.Ink = Math.Max(0, h.Ink - 1);
                double d = Math.Max(1, JsMath.Round(h.Max * .06));
                h.Hp = Math.Max(1, h.Hp - d);
                var f = Snap(h);
                f.Damage = d;
                e.Fx.Add(f);
                (e.Result, e.Line) = ("fumble", $"{h.Name} trips over a footnote!");
            }
            else if (total < 8)
            {
                h.Ink = Math.Min(6, h.Ink + 1);
                (e.Result, e.Line) = ("meh", $"{h.Name} finds a spare drop of ink");
            }
            else if (total < 15)
            {
                h.St["crit"] = 1;
                h.Ink = Math.Min(6, h.Ink + 1);
                (e.Result, e.Line) = ("good", $"{h.Name} spots an opening. Next hit crits!");
            }
            else Helper(e, roll == 20, heroes);
            if (roll == 20) nat20s++;
            Emit(evs, e);
        }

        // a storybook friend bursts in; a natural 20 also heals every pet that was up 10%
        void Helper(BattleEvent e, bool nat, List<BattleUnit> heroes)
        {
            var npc = PickNpc();
            e.Result = nat ? "nat" : "great";
            NpcAct(npc, e);
            e.Line = $"{npc.Name} {(nat ? "bursts in!" : "came to help!")}";
            if (nat) foreach (var x in heroes) if (!x.Ko) x.Hp = Math.Min(x.Max, x.Hp + x.Max * .1);
        }

        // each friend comes once per fight (after all twenty, the pigs come back)
        NpcDef PickNpc()
        {
            var all = TalesData.Current.Npcs;
            var left = all.FindAll(n => !usedNpcs.Contains(n.Key));
            return left.Count > 0 ? rng.Pick(left) : all[0];
        }

        // npcAct: what each friend does
        void NpcAct(NpcDef npc, BattleEvent e)
        {
            usedNpcs.Add(npc.Key);
            helpers++;
            e.Helper = npc.Key;
            var hs = LiveHeroes();
            var fs = LiveFoes();
            switch (npc.Fx)
            {
                case "shield": foreach (var h in hs) AddShield(h, JsMath.Round(h.Max * .2), e); break;
                case "aoe": foreach (var f in fs) e.Fx.Add(NpcHit(f, 1.15)); break;
                case "one":
                    if (fs.Count == 0) break;
                    var big = fs[0];
                    foreach (var f in fs) if (f.Hp > big.Hp) big = f;
                    var o = NpcHit(big, 2.6);
                    o.Crit = true;
                    e.Fx.Add(o);
                    break;
                case "heal": foreach (var h in hs) HealBy(h, .25, e); break;
                case "regen": foreach (var h in hs) { h.St["regen"] = 3; HealBy(h, .08, e); } break;
                case "stun": foreach (var f in fs) if (rng.Next() < .6) { f.St["stun"] = 1; e.Pops.Add(Pop(f.Key, "💫 Dizzy!")); } break;
                case "expose": foreach (var f in fs) { f.St["expose"] = 3; e.Pops.Add(Pop(f.Key, "🎯 Off guard")); } break;
                case "ink": foreach (var h in hs) h.Ink = Math.Min(6, h.Ink + 2); e.Pops.Add(Pop(null, "💧 +2 ink for everyone")); break;
                case "bless": foreach (var h in hs) { AddShield(h, JsMath.Round(h.Max * .12), e); h.Ink = Math.Min(6, h.Ink + 1); } break;
                case "revive": foreach (var h in Heroes) if (h.Ko) Revive(h, .4, e); else HealBy(h, .12, e); break;
                case "gold": gold += 20; foreach (var f in fs) e.Fx.Add(NpcHit(f, .6)); e.Pops.Add(Pop(null, "💧 +20 ink drops")); break;
            }
        }

        static void HealBy(BattleUnit h, double share, BattleEvent e)
        {
            double b = h.Hp;
            h.Hp = Math.Min(h.Max, h.Hp + h.Max * share);
            var f = Snap(h);
            f.Heal = JsMath.Round(h.Hp - b);
            e.Fx.Add(f);
        }

        static void Revive(BattleUnit h, double share, BattleEvent e)
        {
            h.Ko = false;
            h.Hp = JsMath.Round(h.Max * share);
            var f = Snap(h);
            f.Revive = true;
            e.Fx.Add(f);
        }

        // npcHit: the party's average attack, no crit and no genre; shields still soak it
        BattleHit NpcHit(BattleUnit f, double mult)
        {
            var hs = LiveHeroes();
            double atk = 12;
            if (hs.Count > 0) { atk = 0; foreach (var h in hs) atk += h.Atk; atk /= hs.Count; }
            double x = atk * mult * Math.Max(.14, 100 / (100 + f.Def * 4)) * (.9 + rng.Next() * .2);
            double ab = Math.Min(f.S("shield"), x);
            if (ab > 0) { f.St["shield"] = f.S("shield") - ab; x -= ab; }
            x = Math.Max(1, JsMath.Round(x));
            f.Hp = Math.Max(0, f.Hp - x);
            var o = Snap(f);
            o.Damage = x;
            o.Absorbed = JsMath.Round(ab);
            o.Ko = f.Hp <= 0;
            return o;
        }

        // bossRise: a boss at 0 HP comes back bigger and meaner with a full bar: a wild boss once ("…, Second Edition"),
        // a Book Boss once for each phase of its own (a new name, its boast and its twists)
        void BossRise(List<BattleEvent> evs)
        {
            for (int i = 0; i < Foes.Count; i++)
            {
                var f = Foes[i];
                var phases = f.Foe?.Phases ?? NoPhases;
                int ph = f.Phase;
                if (!f.Boss || f.Hp > 0 || ph >= Math.Max(2, phases.Length + 1) - 1) continue;
                var q = ph < phases.Length ? phases[ph] : null;
                var ed = Editions[Math.Min(ph, 1)];
                f.Phase = ph + 1;
                f.BaseName = f.BaseName ?? f.Name;
                f.Max = JsMath.Round(f.Max * (q?.Hp ?? 1.2 + ph * .1));
                f.Hp = f.Max;
                f.Atk = JsMath.Round(f.Atk * (q?.Atk ?? 1.3));
                f.Def = JsMath.Round(f.Def * 1.15);
                f.Spd += 2 + ph;
                f.St.Clear();
                f.Name = q?.Name ?? $"{f.BaseName}, {ed.edition}";
                var twists = q?.Twists ?? Enrage;
                var gained = new List<string>();
                foreach (var m in twists)
                {
                    Twist(f, m);
                    if (Risen.TryGetValue(m, out var what)) gained.Add(what);
                }
                var e = new BattleEvent { Kind = "rise", Actor = f.Key, Name = f.Name, Line = q?.Say ?? ed.boast, Sub = string.Join(" · ", gained), Foe = true };
                e.Fx.Add(Snap(f));
                Emit(evs, e);
            }
        }

        // what a rising boss gains; a summon calls in up to two of its team (or random minions), never past five foes
        void Twist(BattleUnit f, string m)
        {
            switch (m)
            {
                case "shield": f.St["shield"] = JsMath.Round(f.Max * .25); break;
                case "regen": f.St["regen"] = 8; break;
                case "haste": f.Spd += 6; break;
                case "enrage": f.Atk = JsMath.Round(f.Atk * 1.15); break;
                case "summon":
                    var team = f.Foe?.Team;
                    int n = Math.Min(2, 5 - LiveFoes().Count);
                    for (int j = 0; j < n; j++)
                    {
                        var v = team != null && team.Length > 0 ? team[j % team.Length] : FoeFactory.Plain(rng.Pick(TalesData.Current.Minions));
                        Foes.Add(FoeFactory.Make(v, f.Lvl, false, true, "fr" + f.Phase + j, 1, rng));
                    }
                    break;
            }
        }

        // a villain needles the pet about its class and the pet answers (only mid-fight in the wild)
        void Banter(List<BattleEvent> evs)
        {
            var fs = LiveFoes();
            var hs = LiveHeroes();
            if (fs.Count == 0 || hs.Count == 0) return;
            var f = rng.Pick(fs);
            var h = rng.Pick(hs);
            var ctx = (pet: h.Name, foe: f.Name, genre: TalesData.Current.GenreLabel(h.Gen).ToLowerInvariant());
            var says = f.Foe.Def.Says;
            int skip = f.Boss ? 1 : 0;
            string line = mid && says.Length > skip && rng.Next() < .45 ? says[skip + rng.Range(says.Length - skip)] : Line(true, h.Hero.Cls, ctx);
            Emit(evs, new BattleEvent { Kind = "talk", Actor = f.Key, Speaker = f.Key, Listener = h.Key, Line = line, Reply = Line(false, h.Hero.Cls, ctx), Foe = true });
        }

        // tqLine: two halves from the class's taunt (or retort) pool, avoiding lines already used this fight
        string Line(bool taunt, string cls, (string pet, string foe, string genre) ctx)
        {
            var d = TalesData.Current;
            var c = cls != null && d.Classes.TryGetValue(cls, out var found) ? found : d.Classes["sleuth"];
            string[] a = taunt ? c.Taunts : c.Retorts, b = taunt ? c.TauntEnds : c.RetortEnds;
            int n = a.Length * b.Length;
            if (n == 0) return "";
            int i = rng.Range(n);
            for (int g = 0; g < n && seenLines.Contains(cls + i); g++) i = (i + 1) % n;
            seenLines.Add(cls + i);
            if (seenLines.Count > 160) seenLines.RemoveRange(0, 40);
            return (a[i % a.Length] + " " + b[i / a.Length]).Replace("{p}", ctx.pet ?? "friend").Replace("{f}", ctx.foe ?? "villain").Replace("{g}", ctx.genre ?? "cozy");
        }
    }
}
