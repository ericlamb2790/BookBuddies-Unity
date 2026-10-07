using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Tales
{
    /// <summary>
    /// What happens in the dungeon's rooms (tales.js T:912-1037): scenes and their d20 rolls with the effects they hand
    /// out, relics, curses and the side quest, the campfire, the wandering bookseller, and the gifts after a win.
    /// Seeded parts use mulberry32 from the run; the rest (rolls, picks) uses Rng.
    /// </summary>
    public static class EpicRooms
    {
        /// <summary>Math.random for rolls and random picks; tests swap in a seeded one.</summary>
        public static IRng Rng = SystemRng.Shared;

        // ---- the party ----

        /// <summary>Heroes still standing (none napping).</summary>
        public static List<TaleHero> Live(TaleRun run) => run.Party.FindAll(h => !h.Ko);

        /// <summary>avgHp: the party's mean HP fraction, napping heroes counting 0.</summary>
        public static double AvgHp(TaleRun run) => run.Party.Count == 0 ? 1 : run.Party.Sum(h => h.Ko ? 0 : h.Hp / TaleLife.MaxHp(run, h)) / run.Party.Count;

        /// <summary>Heals every hero by a fraction of max HP; wake = napping heroes get up too (from 0).</summary>
        public static void Heal(TaleRun run, double frac, bool wake)
        {
            foreach (var h in run.Party)
                if (wake || !h.Ko) { double m = TaleLife.MaxHp(run, h); h.Hp = Math.Min(m, Math.Max(0, h.Hp) + m * frac); }
        }

        /// <summary>Every hero gains a level; returns the "✨ {pet} learned {move}{end}" lines.</summary>
        public static List<string> LevelUp(TaleRun run, string end = "")
        {
            var lines = new List<string>();
            foreach (var h in run.Party) { h.Lvl++; foreach (var n in HeroFactory.TaleLearn(h, Rng)) lines.Add($"✨ {h.Seed.Name} learned {n}{end}"); }
            return lines;
        }

        /// <summary>epSpot: whose turn it is in the spotlight (live heroes take turns).</summary>
        public static TaleHero Spot(TaleRun run)
        {
            var live = Live(run);
            var l = live.Count > 0 ? live : run.Party;
            return l.Count == 0 ? null : l[run.Ep.Spot % l.Count];
        }

        static string SpotName(TaleRun run) => Spot(run)?.Seed.Name ?? "the party";

        // ---- narration ----

        /// <summary>epCtx: the words a scene can use ({a} roller, {p} party, {place}, {r} land, {relic}, {dark}, {boss}, {hz}), place seeded by act and node.</summary>
        public static Dictionary<string, string> Ctx(TaleRun run, string a = null)
        {
            var E = run.Ep; var R = EpicSaga.LandOf(E);
            var r = new Mulberry32(unchecked((uint)(run.Seed + E.Act * 7L + E.At.r * 31L + E.At.c + E.Saga)));
            return new Dictionary<string, string>
            {
                ["a"] = a ?? "the party", ["p"] = EpicSaga.List(run.Party.ConvertAll(h => h.Seed.Name)), ["place"] = R.Places[(int)Math.Floor(r.Next() * R.Places.Length)],
                ["r"] = R.N, ["relic"] = E.Relic, ["dark"] = EpicSaga.Dark(E).N, ["boss"] = EpicSaga.Boss(E, E.Act).N,
                ["hz"] = R.Hz != null ? EpicData.Current.Hazards[R.Hz].n : "the road",
            };
        }

        /// <summary>epFill: {key} → ctx[key]; unknown keys stay as they are.</summary>
        public static string Fill(string t, Dictionary<string, string> c) =>
            System.Text.RegularExpressions.Regex.Replace(t ?? "", @"\{(\w+)\}", m => c.TryGetValue(m.Groups[1].Value, out var v) && v != null ? v : m.Value);

        // ---- scenes and rolls ----

        /// <summary>epTpl: the scene a view plays (event, danger or chest table); a bad index gives the first encounter.</summary>
        public static EncTpl Template(EpicView v)
        {
            var d = EpicData.Current;
            var L = v.Tp == "danger" ? d.Dangers : v.Tp == "chest" ? d.Chests : d.Encounters;
            return v.E >= 0 && v.E < L.Count ? L[v.E] : d.Encounters[0];
        }

        /// <summary>epFillEnc: the scene's narration and options, each with who rolls and their bonus.</summary>
        public static void FillEncounter(TaleRun run, EpicView v)
        {
            var T = Template(v); var c = Ctx(run, SpotName(run)); var data = EpicData.Current;
            v.I = T.I; v.T = T.T; v.Text = Fill(T.Text, c);
            v.Opts = new List<EpicOpt>();
            for (int j = 0; j < T.Opts.Count; j++)
            {
                var o = T.Opts[j];
                var x = new EpicOpt { Id = j.ToString(), I = o.I, N = Fill(o.N, c), Chk = o.Chk, Dc = o.Dc, D = "No roll needed" };
                if (o.Chk != null)
                {
                    var (h, b) = Roller(run, o.Chk);
                    x.Who = h.Seed.PetKey; x.B = b;
                    x.D = $"{h.Seed.Name} rolls {data.Checks[o.Chk].i} {data.Checks[o.Chk].n} {(b >= 0 ? "+" : "−")}{Math.Abs(b)} against {o.Dc}";
                }
                v.Opts.Add(x);
            }
        }

        /// <summary>epRoller: the live hero best at a check: class bonus + level/3, +1 in the spotlight, +2 Pocket Watch, −2 Rain Cloud curse.</summary>
        public static (TaleHero h, int b) Roller(TaleRun run, string chk)
        {
            var E = run.Ep; var live = Live(run); var l = live.Count > 0 ? live : run.Party; var spot = Spot(run);
            TaleHero best = null; int bb = -99;
            foreach (var h in l)
            {
                int b = EpicData.Current.Bonus(h.Seed.Cls, chk) + h.Lvl / 3 + (h == spot ? 1 : 0) + (E.Rel.Contains("a_watch") ? 2 : 0) - (E.Cur.Contains("c_rain") ? 2 : 0);
                if (b > bb) { bb = b; best = h; }
            }
            return (best ?? l[0], bb);
        }

        /// <summary>epChance: the odds an option succeeds (a natural 20 always does, a natural 1 never); 1 for a safe choice.</summary>
        public static double Chance(EpicState E, EpicOpt o)
        {
            if (o.Chk == null) return 1;
            int n = 1;
            for (int d = 2; d <= 19; d++) if (d + o.B + E.Insp >= o.Dc) n++;
            return n / 20.0;
        }

        /// <summary>epResolve: rolls the d20 for option i (Genie's Lamp rerolls a miss once per land), applies its effects and shows the roll.</summary>
        public static void Resolve(TaleRun run, EpicView v, int i)
        {
            var E = run.Ep; var T = Template(v);
            var opt = i >= 0 && i < T.Opts.Count ? T.Opts[i] : T.Opts[0];
            var o = v.Opts[Math.Min(i, v.Opts.Count - 1)];
            string name = run.Party.Find(h => h.Seed.PetKey == o.Who)?.Seed.Name ?? SpotName(run);
            int roll = 0, ins = o.Chk != null ? E.Insp : 0, bb = o.B + ins;
            bool ok = true;
            var pre = new List<string>();
            if (o.Chk != null)
            {
                E.Insp = 0;
                if (ins != 0) pre.Add($"✨ Inspiration: +{ins} on this roll");
                roll = 1 + Rng.Range(20);
                ok = roll == 20 || roll != 1 && roll + bb >= o.Dc;
                if (!ok && E.Rel.Contains("a_lamp") && E.Lamp != E.Act)
                {
                    E.Lamp = E.Act;
                    roll = 1 + Rng.Range(20);
                    ok = roll == 20 || roll != 1 && roll + bb >= o.Dc;
                    pre.Add("🪔 The Genie’s Lamp grants a second roll");
                }
            }
            string txt = Fill(ok ? opt.OkText : string.IsNullOrEmpty(opt.FailText) ? opt.OkText : opt.FailText, Ctx(run, name));
            var (lines, then) = Fx(run, ok ? opt.OkFx : opt.FailFx);
            lines.InsertRange(0, pre);
            if (ok && o.Chk != null && E.Th != null && Rng.Next() < .35) lines.AddRange(Page(run));
            E.Spot++;
            E.Used.Add(v.Tp[0].ToString() + v.E);
            if (E.Used.Count > 40) E.Used.RemoveRange(0, E.Used.Count - 40);
            var roll1 = new EpicView
            {
                K = "roll", I = v.I, T = v.T, Ch = o.N, Chk = o.Chk, Roll = roll, B = bb, Dc = o.Dc, Ok = ok, Who = name,
                Crit = roll == 20 ? 1 : roll == 1 ? -1 : 0, Text = txt, Then = then,
            };
            roll1.Lines.AddRange(lines);
            run.View = roll1.ToJson();
            run.Tell("roll", new Dictionary<string, object> { ["bt"] = txt, ["ok"] = ok ? 1.0 : 0.0, ["a"] = name, ["t"] = v.T });
        }

        /// <summary>epFx: applies a comma list of effects ("gold:15,heal:15", "fight", …); returns the lines to show (no repeats) and a fight it starts.</summary>
        public static (List<string> lines, string then) Fx(TaleRun run, string s)
        {
            var E = run.Ep; var data = EpicData.Current; var d = TalesData.Current;
            var lines = new List<string>();
            string then = null;
            foreach (var tok in (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = tok.Split(':');
                string k = kv[0], vv = kv.Length > 1 ? kv[1] : null;
                int n = vv != null && int.TryParse(vv, out int x) ? x : 0;
                switch (k)
                {
                    case "gold": run.Gold += n; lines.Add($"💧 +{n} ink drops"); break;
                    case "pay": { int p = Math.Min(run.Gold, n); run.Gold -= p; if (p > 0) lines.Add($"💧 −{p} ink drops"); break; }
                    case "heal": foreach (var h in Live(run)) h.Hp = Math.Min(TaleLife.MaxHp(run, h), h.Hp + TaleLife.MaxHp(run, h) * n / 100); lines.Add($"💚 Everyone heals {n}%"); break;
                    case "hurt": foreach (var h in Live(run)) h.Hp = Math.Max(1, h.Hp - TaleLife.MaxHp(run, h) * n / 100); lines.Add($"💔 Everyone loses {n}% health"); break;
                    case "boon":
                        {
                            var ok = d.Boons.FindAll(b => BoonOk(run, b.key));
                            if (ok.Count > 0) { var b = ok[Rng.Range(ok.Count)]; run.Boons.Add(b.key); lines.Add($"{b.icon} {b.name}: {b.desc}"); }
                            else { run.Gold += 20; lines.Add("💧 +20 ink drops"); }
                            break;
                        }
                    case "lvl": { var nl = LevelUp(run); lines.Add("⬆️ Every pet gains a level"); lines.AddRange(nl); break; }
                    case "ward":
                        if (E.Hz != null && !E.Ward) { E.Ward = true; lines.Add($"🛡️ The {data.Hazards[E.Hz].n} can’t touch you in this land"); }
                        else { E.Bless = true; lines.Add("✨ Next battle: everyone starts shielded, with extra ink"); }
                        break;
                    case "bless": E.Bless = true; lines.Add("✨ Next battle: everyone starts shielded, with extra ink"); break;
                    case "curse": E.Curse = true; lines.Add("🌑 Next battle: the foes hit harder"); break;
                    case "ink": E.Ink = 2; lines.Add("💧 Next battle: +2 ink for every pet"); break;
                    case "npc":
                        {
                            var npc = d.Npcs.Find(c => c.Key == vv);
                            if (npc != null) { E.Ally = vv; lines.Add($"{npc.Icon} {npc.Name} will help in your next battle"); }
                            break;
                        }
                    case "relic": lines.Add(Gain(run)); break;
                    case "cursed": lines.Add(Curse(run)); break;
                    case "lift": { string l = Lift(run); lines.Add(l.Length > 0 ? l : "✨ No curses to lift. You feel lighter anyway"); break; }
                    case "swap":
                        if (E.Act < 4)
                        {
                            var A = E.A; int j = A.Boss;
                            for (int g = 0; g < 12 && j == A.Boss; g++) j = Rng.Range(d.Bosses.Count);
                            A.Boss = j;
                            lines.Add($"👑 The villain of this land is now {d.Bosses[j].N}");
                        }
                        else { E.Weak = true; lines.Add($"👑 {EpicSaga.Dark(E).N} is rattled and starts the final battle hurt"); }
                        break;
                    case "weak": E.Weak = true; lines.Add($"🎯 {EpicSaga.Boss(E, E.Act).N} will start its battle hurt"); break;
                    case "fog": E.Clear = E.Act; lines.Add("🗺️ The fog lifts from this land’s map"); break;
                    case "page": lines.AddRange(Page(run)); break;
                    case "tone": E.Tone += n; break;
                    case "rival": E.Rival = true; then = "elite"; break;
                    case "ally":
                        {
                            var npc = d.Npcs[Rng.Range(d.Npcs.Count)];
                            E.Ally = npc.Key;
                            lines.Add($"{npc.Icon} {npc.Name} will help in your next battle");
                            break;
                        }
                    case "dm":
                        {
                            double r = Rng.Next();
                            var good = new[] { "relic", "gold:40", "bless,heal:25", "lvl", "ally" };
                            var bad = new[] { "cursed", "hurt:15", "curse", "pay:30", "cursed,gold:40" };
                            lines.AddRange(Fx(run, r < .5 ? good[(int)Math.Floor(r * 10)] : bad[(int)Math.Floor((r - .5) * 10)]).lines);
                            break;
                        }
                    case "buy":
                        if (run.Gold >= 60) { run.Gold -= 60; lines.Add("💧 −60 ink drops"); lines.Add(Gain(run)); }
                        else lines.Add("💧 Not enough ink drops for a relic yet");
                        break;
                    case "buyl":
                        if (E.Cur.Count == 0) lines.Add("🍭 You have no curses, so the stallholder gives you a lollipop");
                        else if (run.Gold >= 25) { run.Gold -= 25; lines.Add("💧 −25 ink drops"); lines.Add(Lift(run)); }
                        else lines.Add("💧 Not enough ink drops yet");
                        break;
                    case "fight": case "elite": then = k; break;
                }
            }
            return (lines.Distinct().ToList(), then);
        }

        // ---- relics, curses, the side quest ----

        /// <summary>epGain: a relic (a random one not yet held when k is null; +40 ink drops once every relic is held); returns its line.</summary>
        public static string Gain(TaleRun run, string k = null)
        {
            var E = run.Ep; var data = EpicData.Current;
            if (k == null)
            {
                var left = data.Relics.FindAll(a => !E.Rel.Contains(a.Key));
                if (left.Count == 0) { run.Gold += 40; return "💧 +40 ink drops (you own every relic)"; }
                k = left[Rng.Range(left.Count)].Key;
            }
            var row = data.Relic(k);
            if (row == null) return "";
            if (!E.Rel.Contains(k)) E.Rel.Add(k);
            if (k == "a_map") E.Clear = E.Act;
            return $"{row.I} Relic: {row.N}. {row.D}";
        }

        /// <summary>epCurse: a random curse not yet held (all held: the next fight's foes hit harder); returns its line.</summary>
        public static string Curse(TaleRun run)
        {
            var E = run.Ep;
            var left = EpicData.Current.Curses.FindAll(c => !E.Cur.Contains(c.Key));
            if (left.Count == 0) { E.Curse = true; return "🌑 Next battle: the foes hit harder"; }
            var c = left[Rng.Range(left.Count)];
            E.Cur.Add(c.Key);
            return $"{c.I} Curse: {c.N}. {c.D}";
        }

        /// <summary>epLift: lifts the oldest curse; "" when there is none.</summary>
        public static string Lift(TaleRun run)
        {
            var E = run.Ep;
            if (E.Cur.Count == 0) return "";
            var c = EpicData.Current.Curse(E.Cur[0]);
            E.Cur.RemoveAt(0);
            return $"🔥 The {c?.N} curse is lifted";
        }

        /// <summary>epPage: one more piece of the side quest; the third levels everyone up and gives a relic.</summary>
        public static List<string> Page(TaleRun run)
        {
            var th = run.Ep.Th;
            var lines = new List<string>();
            if (th == null || th.Got >= th.Need) return lines;
            th.Got++;
            lines.Add($"{th.I} Found one of {th.N} ({th.Got} of {th.Need})");
            if (th.Got >= th.Need)
            {
                foreach (var h in run.Party) { h.Lvl++; HeroFactory.TaleLearn(h, Rng); }
                lines.Add("📜 Side quest complete!");
                lines.Add(Gain(run));
                lines.Add("⬆️ Every pet gains a level");
            }
            return lines;
        }

        // ---- rooms ----

        /// <summary>campScene: a campfire on the map: a told tale, maybe a storybook visitor (ally next fight), a curse burnt away, then heal or study.</summary>
        public static void Camp(TaleRun run)
        {
            var E = run.Ep; var d = TalesData.Current; var data = EpicData.Current;
            var c = Ctx(run, SpotName(run));
            string tale = data.CampTales[Mod(run.Seed + E.Act * 3 + E.At.r + E.Saga, data.CampTales.Length)];
            bool study = AvgHp(run) > .72 && !run.Party.Exists(h => h.Ko);
            var lines = new List<string> { $"The fire crackles in {c["place"]}. {c["a"]} tells the story of {tale}." };
            double vr = new Mulberry32(unchecked((uint)(run.Seed + E.Act * 13L + E.At.r * 7L + E.Saga))).Next();
            if (vr < .6 && d.Npcs.Count > 0)
            {
                var npc = d.Npcs[(int)Math.Floor(vr / .6 * d.Npcs.Count) % d.Npcs.Count];
                E.Ally = npc.Key;
                lines.Add(npc.Camp);
                lines.Add($"{npc.Icon} {npc.Name} will help in your next battle.");
            }
            string lift = Lift(run);
            if (lift.Length > 0) lines.Add(lift);
            double hv = E.Rel.Contains("a_bowl") ? 1 : E.Cur.Contains("c_mirror") ? .22 : .45;
            if (study)
            {
                lines.Add("Everyone feels strong, so they practise until the stars come out.");
                lines.Add("⬆️ Every pet gains a level.");
                lines.AddRange(LevelUp(run, "!"));
            }
            else
            {
                Heal(run, hv, true);
                lines.Add("Somewhere in the middle, everyone falls asleep.");
                lines.Add($"💚 Everyone heals {(hv >= 1 ? "fully" : JsMath.RoundI(hv * 100) + "%")}. Nappers wake up.");
            }
            EpicFlow.Scene(run, "🏕️", "Campfire", lines);
            run.Tell("rest", new Dictionary<string, object> { ["study"] = study ? 1.0 : 0.0 });
            E.Spot++;
        }

        /// <summary>shopScene: a wandering bookseller; the pets spend ink drops by themselves (alarm clock, cocoa, a boon, else an ink pot).</summary>
        public static void Shop(TaleRun run)
        {
            var d = TalesData.Current;
            var r = new Mulberry32(unchecked((uint)(run.Seed + run.Ch * 5L + run.Node + run.Vol)));
            var ok = d.Boons.FindAll(b => BoonOk(run, b.key));
            var boon = ok.Count > 0 ? ok[(int)Math.Floor(r.Next() * ok.Count)] : d.Boons[0];
            var got = new List<object>();
            var lines = new List<string> { "A cart creaks out of the mist, piled high with books and lanterns." };
            bool Buy(int cost, Action act, string line, string item)
            {
                if (run.Gold < cost) return false;
                run.Gold -= cost; act(); lines.Add(line); got.Add(item);
                return true;
            }
            if (run.Party.Exists(h => h.Ko))
                Buy(18, () => { foreach (var h in run.Party) if (h.Ko) h.Hp = TaleLife.MaxHp(run, h) * .3; }, "⏰ An alarm clock woke the nappers (−18 ink drops)", "an alarm clock");
            if (AvgHp(run) < .65) Buy(25, () => Heal(run, .5, false), "☕ Hot cocoa for everyone (−25 ink drops)", "hot cocoa");
            if (BoonOk(run, boon.key)) Buy(55, () => run.Boons.Add(boon.key), $"{boon.icon} {boon.name}: {boon.desc} (−55 ink drops)", boon.name);
            if (got.Count == 0 && BoonOk(run, "bookmark")) Buy(20, () => run.Boons.Add("bookmark"), "🫙 An ink pot for the road (−20 ink drops)", "an ink pot");
            if (lines.Count == 1) lines.Add("Your pets pressed their noses to the glass. Nothing they could afford today.");
            EpicFlow.Scene(run, "🏪", "A Wandering Bookseller", lines);
            run.Tell("shop", new Dictionary<string, object> { ["items"] = got });
        }

        /// <summary>The gift after a win: 3 boons the party has fewer than 2 of, seeded by chapter and row; a pile of ink drops when none are left.</summary>
        public static List<EpicOpt> BoonOffer(TaleRun run)
        {
            var r = new Mulberry32(unchecked((uint)(run.Seed + run.Ch * 131L + run.Node * 17L + run.Vol)));
            var opts = TalesData.Current.Boons.Where(b => BoonOk(run, b.key)).Select(b => (k: r.Next(), b)).ToList()
                .OrderBy(p => p.k).Take(3).Select(p => new EpicOpt { Id = p.b.key, I = p.b.icon, N = p.b.name, D = p.b.desc }).ToList();
            if (opts.Count == 0) opts.Add(new EpicOpt { Id = "ink", I = "💧", N = "A pile of ink drops", D = "Your party already has every gift twice. +25 ink drops instead." });
            return opts;
        }

        /// <summary>The boss's hoard: 3 random relics not yet held; a pile of ink drops when every relic is held.</summary>
        public static List<EpicOpt> RelicOffer(TaleRun run)
        {
            var left = EpicData.Current.Relics.FindAll(a => !run.Ep.Rel.Contains(a.Key));
            var opts = left.Select(a => (k: Rng.Next(), a)).ToList().OrderBy(p => p.k).Take(3)
                .Select(p => new EpicOpt { Id = p.a.Key, I = p.a.I, N = p.a.N, D = p.a.D }).ToList();
            if (opts.Count == 0) opts.Add(new EpicOpt { Id = "ink", I = "💧", N = "A pile of ink drops", D = "You already own every relic. +40 ink drops instead." });
            return opts;
        }

        /// <summary>boonOK: each boon stacks at most twice.</summary>
        public static bool BoonOk(TaleRun run, string k) => run.Boons.Count(b => b == k) < 2;

        static int Mod(long x, int n) => (int)(((x % n) + n) % n);
    }
}
