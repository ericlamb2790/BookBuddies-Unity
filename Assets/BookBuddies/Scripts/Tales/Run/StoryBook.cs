using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The storybook (storybook.md): a finished tale's story log retold as pages (the site's makeBook, same seed and draw
    /// order), the win and lost entries the dungeon tells, and the reader's page timing. Pure C#. A book is a JSON object
    /// in the site's shape: {v, voice, title, vol, ch, reason, made, cast[{n,o,l,c,g}], stats, pages}; a story page is
    /// {sc, h[cast index], f?, act (calm, clash, rest, nap), t, x}, plus {k:"cover"} first and {k:"end"} last.
    /// </summary>
    public static class StoryBook
    {
        /// <summary>The site trims non-boss fight pages past this many pages.</summary>
        public const int MaxPages = 26;

        /// <summary>
        /// makeBook: the run's story log as a book. dark and relic are the dungeon's villain and relic (S.ep.dark, S.ep.relic),
        /// which thread through the chapters; null leaves the thread out. made is when it was made (ms since 1970).
        /// </summary>
        public static Dictionary<string, object> Make(TaleRun run, FoeDef dark, string relic, double made)
        {
            var b = new Builder(run, dark, relic);
            var pages = b.Pages();
            var cast = new List<object>();
            foreach (var c in b.Cast) cast.Add(new Dictionary<string, object> { ["n"] = c.N, ["o"] = c.O, ["l"] = c.L, ["c"] = c.C, ["g"] = (double)c.G });
            return new Dictionary<string, object>
            {
                ["v"] = 1.0, ["voice"] = b.Voice.K, ["title"] = run.Title, ["vol"] = (double)run.Vol, ["ch"] = (double)run.Ch, ["reason"] = run.Reason,
                ["made"] = made, ["cast"] = cast, ["stats"] = run.Stats.ToJson(), ["pages"] = pages,
            };
        }

        // ---- entries the dungeon tells ----

        /// <summary>
        /// The win entry (T:1385) for a won fight: rounds, party HP left, the foes, the first foe's look (f), boss/elite, the
        /// boss's line, the MVP, best hit, first ult, who napped, and the scene. kind is TaleBattle.Fight, Elite or Boss.
        /// </summary>
        public static Dictionary<string, object> WinEntry(BattleOutcome o, string kind, int sc)
        {
            var foes = new List<object>();
            foreach (var f in o.Defeated) foes.Add(f.Name);
            var f0 = o.Defeated.Count > 0 ? o.Defeated[0] : null;
            string me = o.Hero?.Name;
            return new Dictionary<string, object>
            {
                ["r"] = (double)o.Rounds, ["hpf"] = JsMath.Round(o.HpFrac * 100) / 100, ["foes"] = foes, ["f"] = FoeLook(f0),
                ["boss"] = kind == TaleBattle.Boss ? 1.0 : 0, ["elite"] = kind == TaleBattle.Elite ? 1.0 : 0,
                ["q"] = kind == TaleBattle.Boss ? f0?.Foe?.Def.Say : null, ["mvp"] = me,
                ["best"] = o.BestMove == null ? null : new Dictionary<string, object> { ["n"] = me, ["ab"] = Move(o.BestMove), ["d"] = JsMath.Round(o.BestHit) },
                ["ult"] = o.Ult.HasValue ? new Dictionary<string, object> { ["n"] = o.Ult.Value.n, ["ab"] = Move(o.Ult.Value.ab) } : null,
                ["naps"] = new List<object>(o.Naps.Distinct()), ["sc"] = (double)sc,
            };
        }

        /// <summary>The lost entry (T:1389): the foes still standing and the look of the first of them.</summary>
        public static Dictionary<string, object> LostEntry(IList<BattleUnit> foes, int sc)
        {
            var up = foes.Where(x => x.Alive).ToList();
            var by = new List<object>();
            foreach (var f in up) by.Add(f.Name);
            return new Dictionary<string, object> { ["by"] = by, ["f"] = FoeLook(up.Count > 0 ? up[0] : foes.FirstOrDefault()), ["sc"] = (double)sc };
        }

        /// <summary>A foe's look for a page (the site's i c n b sh hp atk sp, plus the base name and variant for Unity's art).</summary>
        public static Dictionary<string, object> FoeLook(BattleUnit f)
        {
            var v = f?.Foe;
            if (v == null) return null;
            return new Dictionary<string, object>
            {
                ["i"] = v.Def.I, ["c"] = v.Def.C, ["n"] = f.Name, ["bn"] = v.Def.N, ["b"] = f.Boss ? 1.0 : 0, ["sh"] = v.Def.Sh,
                ["hp"] = v.HasHp ? (object)v.Hp : null, ["atk"] = v.Atk, ["sp"] = v.Sp, ["ax"] = v.Ax, ["tc"] = v.Tc, ["au"] = v.Au, ["vs"] = v.Vs, ["xp"] = v.Xp,
            };
        }

        /// <summary>The villain a page's f draws (base villain plus its variant look); null when it can't be found.</summary>
        public static FoeVariant FoeOf(Dictionary<string, object> f)
        {
            if (f == null) return null;
            var d = TalesData.Current;
            string n = f.Str("bn", f.Str("n"));
            var def = d.Dark.Find(x => x.N == n) ?? d.Bosses.Find(x => x.N == n) ?? d.Minions.Find(x => x.N == n);
            if (def == null) return null;
            var v = FoeFactory.Plain(def);
            v.Dn = f.Str("n", null);
            v.Ax = f.Str("ax", null); v.Tc = f.Str("tc", null); v.Au = f.Str("au", null); v.Xp = f.Str("xp", null);
            v.Vs = f.Num("vs");
            if (f.Has("hp")) { v.Hp = f.Num("hp"); v.HasHp = true; }
            if (f.Has("atk")) v.Atk = f.Num("atk");
            if (f.Has("sp")) v.Sp = f.Str("sp");
            return v;
        }

        static string Move(string name) => name == null ? null : BookBuddies.UI.UiKit.SplitEmoji(name, out _);

        // ---- reading ----

        /// <summary>tqWc: words in a text.</summary>
        public static int Words(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>tqPageMs: how long a page stays before it turns by itself at a pace.</summary>
        public static int PageMs(Dictionary<string, object> page, Pace pace)
        {
            string k = page.Str("k", null);
            if (k == "cover") return JsMath.RoundI(4500 * pace.F);
            if (k == "end") return JsMath.RoundI(7000 * pace.F);
            int n = Words(page.Str("x")) + Words(page.Str("t"));
            return JsMath.RoundI(900 + Math.Max(n * pace.Rd + 1600, n * pace.Ms) + 2600);
        }

        // ---- prose helpers (D:433-435) ----

        /// <summary>tqList: "a", "a and b", "a, b and c".</summary>
        public static string List(IEnumerable<string> items)
        {
            var a = items.Where(s => !string.IsNullOrEmpty(s)).ToList();
            return a.Count < 2 ? (a.Count == 1 ? a[0] : "") : string.Join(", ", a.Take(a.Count - 1)) + " and " + a[a.Count - 1];
        }

        /// <summary>tqCap: the first letter in upper case.</summary>
        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>tqAn: "an 8-point", "an 18-point", "a 28-point".</summary>
        public static string An(double n)
        {
            string s = JsMath.Num(n);
            return s[0] == '8' || ((s.Length == 2 || s.Length == 5) && (s.StartsWith("11") || s.StartsWith("18"))) ? "an" : "a";
        }

        /// <summary>
        /// Array.prototype.sort((a, b) => r() - .5) as V8 runs it on short arrays (one counted run, then binary insertion),
        /// so a shuffled list comes out as the site's.
        /// </summary>
        public static List<T> RandomSort<T>(IList<T> items, IRng r)
        {
            var a = new List<T>(items);
            int n = a.Count;
            if (n < 2) return a;
            double Compare() => r.Next() - .5;
            int run = 2;
            bool down = Compare() < 0;
            for (int i = 2; i < n; i++, run++)
            {
                double o = Compare();
                if (down ? o >= 0 : o < 0) break;
            }
            if (down) a.Reverse(0, run);
            for (int start = run; start < n; start++)
            {
                int left = 0, right = start;
                var pivot = a[start];
                while (left < right)
                {
                    int mid = left + ((right - left) >> 1);
                    if (Compare() < 0) right = mid; else left = mid + 1;
                }
                a.RemoveAt(start);
                a.Insert(left, pivot);
            }
            return a;
        }

        // ---- makeBook ----

        sealed class Builder
        {
            readonly TaleRun run;
            readonly FoeDef dark;
            readonly string relic;
            readonly Mulberry32 r;
            readonly List<Dictionary<string, object>> st;
            public readonly List<TaleCast> Cast;
            public Voice Voice;
            List<string> chN;
            readonly string names;
            readonly List<object> all = new List<object>();

            public Builder(TaleRun run, FoeDef dark, string relic)
            {
                this.run = run; this.dark = dark; this.relic = relic;
                r = new Mulberry32(unchecked((uint)(run.Seed + Math.Max(1, run.Vol) * 7919)));
                st = run.Story;
                Cast = run.Cast.Take(6).ToList();
                names = List(Cast.Select(c => c.N));
                for (int i = 0; i < Math.Min(4, Cast.Count); i++) all.Add((double)i);
            }

            T P<T>(IList<T> a) => a[(int)Math.Floor(r.Next() * a.Count)];

            int Idx(string n) { int i = Cast.FindIndex(c => c.N == n); return i < 0 ? 0 : i; }

            static string Cn(string c) => TalesData.Current.Class(c).Name;

            public List<object> Pages()
            {
                var pages = new List<object> { new Dictionary<string, object> { ["k"] = "cover" } };
                var first = Cast.Count > 0 ? Cast[0] : new TaleCast { N = "Our hero", C = "sleuth" };
                Voice = P(TalesData.Current.Voices);
                chN = RandomSort(Voice.Ch, r);
                pages.Add(Page((uint)run.Seed % 6, all, "calm", "Once upon a time", r.Next() < .65 ? P(Voice.Open).Replace("{n}", names) : P(new[]
                {
                    $"{names} found a book with no last page. The first line read: “Turn me, if you dare.” So they did.",
                    $"Deep in the stacks, where the lamps hum and the dust sparkles, {names} opened a story that had been waiting just for them.",
                    $"It began, as the best stories do, on a quiet evening. {first.N} the {Cn(first.C)} tugged a sleeve and pointed at a glowing spine on the shelf.",
                    $"Nobody remembers who turned the first page. Maybe it was {first.N}. Maybe the book turned itself.",
                })));
                var chs = st.Select(e => e.Num("ch")).Distinct().OrderBy(c => c).ToList();
                foreach (double c in chs) Chapter(pages, c, chs);
                Ending(pages);
                pages.Add(new Dictionary<string, object> { ["k"] = "end" });
                foreach (Dictionary<string, object> p in pages)
                {
                    if (p.Has("x")) p["x"] = BattleText.Prose(Squash(p.Str("x")));
                    if (p.Has("t")) p["t"] = BattleText.Prose(p.Str("t"));
                }
                while (pages.Count > MaxPages)
                {
                    int i = pages.FindIndex(3, x => { var p = (Dictionary<string, object>)x; return p.Str("act") == "clash" && !(p.Obj("f")?.Truthy("b") ?? false); });
                    if (i < 0 || i >= pages.Count - 3) break;
                    pages.RemoveAt(i);
                }
                return pages;
            }

            void Chapter(List<object> pages, double c, List<double> chs)
            {
                var E = st.Where(e => e.Num("ch") == c).ToList();
                int ci = (int)c;
                foreach (var a in E.Where(e => e.Str("k") == "epic" || e.Str("k") == "act"))
                    pages.Add(Page(a.Has("sc") ? a.Num("sc") : (ci + 1) % 6, all, "calm", a.Str("t"), a.Str("bt")));
                var wins = E.Where(e => e.Str("k") == "win" && !e.Truthy("boss")).ToList();
                var boss = E.Find(e => e.Str("k") == "win" && e.Truthy("boss"));
                var extras = new List<string>();
                foreach (var e in E.Where(e => e.Str("k") == "boon").Take(1)) extras.Add(P(new[] { $"In the victory glow they found the {e.Str("n")}.", $"Tucked in a margin was a gift: the {e.Str("n")}." }));
                foreach (var e in E.Where(e => e.Str("k") == "rest").Take(1))
                    extras.Add(e.Truthy("study") ? "They stopped at a Reading Nook and studied until their eyes were heavy. Everyone grew stronger." : "They rested in a Reading Nook until every paw felt warm again.");
                foreach (var e in E.Where(e => e.Str("k") == "shop").Take(1))
                {
                    var items = TalesData.Strings(e.Arr("items"));
                    extras.Add(items.Length > 0 ? $"At the Bookshop they bought {List(items)}." : "They window-shopped at the Bookshop, noses pressed to the glass.");
                }
                foreach (var e in E.Where(e => e.Str("k") == "relic").Take(1)) extras.Add(P(new[] { $"From the villain’s hoard they chose the {e.Str("n")}.", $"Among the spoils glittered the {e.Str("n")}. They took it, of course." }));
                foreach (var e in E.Where(e => e.Str("k") == "roll" && e.Str("bt").Length > 0).Take(2)) extras.Add(e.Str("bt"));
                foreach (var e in E.Where(e => e.Str("k") == "npc").Take(1)) extras.Add($"Then, out of an older story, {e.Str("n")} turned up to {e.Str("act")}.");
                if (E.Exists(e => e.Str("k") == "revive")) extras.Add("When all seemed lost, a Second Wind swept through the pages and everyone stood back up.");

                if (wins.Count > 0)
                {
                    var w = wins.OrderByDescending(x => x.Obj("best")?.Num("d") ?? 0).First();
                    var h = new List<object> { (double)Idx(w.Str("mvp")) };
                    for (int i = 0; i < Cast.Count && h.Count < 3; i++) if (!h.Contains((double)i)) h.Add((double)i);
                    string name = chN.Count > 0 ? chN[ci % chN.Count] : "";
                    string x = Turn() + FightText(w) + Nap(w) + (extras.Count > 0 ? " " + string.Join(" ", extras) : "");
                    if (r.Next() < .6) x += " " + Thread(chs.Count > 1 ? chs.IndexOf(c) / (double)(chs.Count - 1) : 0);
                    pages.Add(Page(w.Has("sc") ? w.Num("sc") : ci % 6, h, "clash", $"Chapter {ci}{(name.Length > 0 ? ": " + name : "")}", x.Trim(), w.Obj("f")));
                }
                else if (extras.Count > 0) pages.Add(Page(ci % 6, all, "rest", $"Chapter {ci}", string.Join(" ", extras)));
                foreach (var d in E.Where(e => e.Str("k") == "dm").Take(2))
                    pages.Add(Page((ci + 3) % 6, all, "calm", d.Str("t"), $"{P(Voice.Twist)} {d.Str("bt")} {(d.Has("pick") ? $"The party chose: {d.Str("pick")}." : "")} {d.Str("out")}"));
                if (boss != null) pages.Add(Page(boss.Has("sc") ? boss.Num("sc") : 0, all, "clash", Foe(boss), BossText(boss, ci) + Nap(boss), boss.Obj("f")));
                foreach (var g in E.Where(e => e.Str("k") == "saga"))
                    pages.Add(Page(1, all, "rest", $"The {g.Str("relic")} is safe", $"{g.Str("dark")} fell at last, and {names} carried the {g.Str("relic")} home. In every library in the world, the stories breathed out."));
            }

            void Ending(List<object> pages)
            {
                var lost = st.Find(e => e.Str("k") == "lost");
                var mv = new Dictionary<string, int>();
                var order = new List<string>();
                foreach (var e in st)
                {
                    if (e.Str("k") != "win" || e.Str("mvp").Length == 0) continue;
                    string n = e.Str("mvp");
                    if (!mv.ContainsKey(n)) { mv[n] = 0; order.Add(n); }
                    mv[n] += e.Truthy("boss") ? 3 : 1;
                }
                string top = order.OrderByDescending(n => mv[n]).FirstOrDefault();
                string coda = Cast.Count > 1 && top != null ? $" If you ask the book who the hero was, it will say {top}. If you ask {top}, they will say it was everyone."
                    : top != null ? $" The book will tell you {top} was braver than anyone expected. {top} will tell you it was mostly snacks." : "";
                string chs = $"{run.Ch} chapter{(run.Ch > 1 ? "s" : "")}";
                if (lost != null)
                {
                    var by = TalesData.Strings(lost.Arr("by"));
                    string x = P(new[]
                    {
                        $"In chapter {JsMath.Num(lost.Num("ch"))}, {List(by)} proved too much. One by one, the heroes yawned, curled up between the pages, and fell asleep. It was a good place to stop.",
                        $"{Cap(List(by))} won this round. The party tucked themselves into the spine of the book and dreamed of the next adventure.",
                    }) + (dark != null ? $" They had come {chs} toward {dark.N}. Next time, they will know the way." : "") + coda + " " + P(Voice.Close);
                    pages.Add(Page(lost.Has("sc") ? lost.Num("sc") : 4, all, "nap", "Goodnight", x, lost.Obj("f")));
                }
                else pages.Add(Page(1, all, "rest", "Closing the book", P(new[]
                {
                    $"After {chs}, {names} decided the story had given them enough. They closed the book gently, keeping a finger between the pages, just in case.",
                    $"The candles burned low. {names} agreed: this was the perfect place to end. They set the book back on the shelf, spine out, so they could find it again.",
                }) + coda + " " + P(Voice.Close)));
            }

            // the chapter's thread: how far the heroes are from the villain and the relic (f = how far through the book)
            string Thread(double f)
            {
                if (dark == null) return "";
                string d = dark.N;
                return P(f < .34 ? new[] { $"Somewhere far ahead, {d} turned a page and frowned.", $"The {relic} was still a long way off. They kept walking.", $"Nobody said it out loud, but everyone was thinking about the {relic}." }
                    : f < .7 ? new[] { $"The {relic} felt closer now. So did {d}.", $"Word of them had reached {d}. Good.", "Halfway there. The pages were getting darker, and the heroes were getting braver." }
                    : new[] { $"Only a few pages stood between them and {d}.", $"They could almost hear the {relic} humming.", "The last chapters are always the heaviest. They turned them anyway." });
            }

            string Turn() => r.Next() < .5 ? P(Voice.Turn) + " " : "";

            // how a fight went decides how it's told: over in a breath, a steady win, or a close call
            static string Mood(Dictionary<string, object> w) =>
                w.Arr("naps")?.Count > 0 || (w.Has("hpf") && w.Num("hpf") < .4) ? "close" : w.Num("r") > 0 && w.Num("r") <= 2 ? "swift" : "steady";

            static string Hit(Dictionary<string, object> w) { var b = w.Obj("best"); return b != null ? $"{b.Str("n")}’s {b.Str("ab")}" : ""; }

            static string Foe(Dictionary<string, object> w) => w.Obj("f")?.Str("n") ?? "";

            static string Champ(Dictionary<string, object> w) => w.Truthy("elite") && w.Obj("f") != null ? $"A champion led them, {Foe(w)}, and it did not mean to lose. " : "";

            static string Naps(Dictionary<string, object> w) => List(TalesData.Strings(w.Arr("naps")));

            string Nap(Dictionary<string, object> w) =>
                w.Arr("naps")?.Count > 0 && Mood(w) != "close" ? $" {Naps(w)} needed a short nap along the way, but woke in time to cheer." : "";

            string FightText(Dictionary<string, object> w)
            {
                string champ = Champ(w), foes = List(TalesData.Strings(w.Arr("foes"))), hit = Hit(w), mvp = w.Str("mvp");
                var best = w.Obj("best"); var ult = w.Obj("ult");
                bool napped = w.Arr("naps")?.Count > 0;
                switch (Mood(w))
                {
                    case "swift":
                        return P(new Func<string>[]
                        {
                            () => $"{champ}{Cap(foes)} barely had time to say their lines. {(hit.Length > 0 ? $"{hit} ended it in a single breath." : "It was over in a page.")}",
                            () => $"{champ}{Cap(foes)} stepped out of the margins and straight back in again. {(mvp.Length > 0 ? $"{mvp} didn’t even put down their tea." : "Nobody even put down their tea.")}",
                            () => $"{champ}It was the shortest scuffle in the whole book. {Cap(foes)} took one look at {Or(mvp, "the party")} and decided to be somewhere else.",
                        })();
                    case "close":
                        return P(new Func<string>[]
                        {
                            () => $"{champ}For a moment it looked like the end of the book. {Cap(foes)} pressed hard{(napped ? $", and {Naps(w)} slumped into a nap" : "")}. Then {(hit.Length > 0 ? $"{hit} came out of nowhere" : $"{Or(mvp, "someone")} found one last scrap of courage")}, and the page held.",
                            () => $"{champ}It was close. Too close. {Cap(foes)} had them pinned against the spine{(napped ? $" while {Naps(w)} snored" : "")}. {(ult != null ? $"{ult.Str("n")} poured every drop of ink into {ult.Str("ab")}, and that was that." : "They held on, and on, until the troublemakers ran out of pages first.")}",
                            () => $"{champ}Paws trembled. The ink ran low. {Cap(foes)} were winning, right up until they weren’t{(hit.Length > 0 ? $": {hit} turned the whole chapter around" : "")}.",
                        })();
                    default:
                        return P(new Func<string>[]
                        {
                            () => $"{champ}{Cap(foes)} blocked the path. {(best != null ? $"{best.Str("n")} answered with {best.Str("ab")}, {An(best.Num("d"))} {JsMath.Num(best.Num("d"))}-point wallop," : "The party stood firm,")} and the troublemakers closed their books and wandered home.",
                            () => $"{champ}{Cap(foes)} tried to smudge the story. {Or(mvp, "The party")} would not allow it{(best != null ? $": {best.Str("ab")} rang out like a slammed dictionary" : "")}.",
                            () => $"{champ}A scuffle broke out between the shelves. {(ult != null ? $"When {ult.Str("n")} unleashed {ult.Str("ab")}, the whole aisle lit up." : $"{Or(mvp, "Everyone")} led the charge.")} Soon {foes} {(w.Arr("foes")?.Count > 1 ? "were" : "was")} tiptoeing away.",
                            () => $"{champ}The lamps flickered, and {foes} came creeping out of the margins. {(mvp.Length > 0 ? $"{mvp} stepped forward first." : "")} {(best != null ? $"{best.Str("ab")} settled it." : "It did not take long.")}",
                        })();
                }
            }

            string BossText(Dictionary<string, object> w, int ch)
            {
                string f = Foe(w), q = w.Str("q"), hit = Hit(w), mvp = w.Str("mvp");
                var best = w.Obj("best"); var ult = w.Obj("ult");
                if (Mood(w) == "close")
                    return P(new Func<string>[]
                    {
                        () => $"{f} very nearly finished the story right there. “{q}” {(w.Arr("naps")?.Count > 0 ? $"{Cap(Naps(w))} went down, and the lamps dimmed. " : "")}But {Or(hit, "one last stand")} would not let the book close, and {f} was the one who ran out of pages.",
                        () => $"“{q}” {f} had them on the very last line. Then {(ult != null ? $"{ult.Str("n")} found {ult.Str("ab")}" : $"{Or(mvp, "the smallest hero")} stood up one more time")}, and chapter {ch} was saved by a whisker.",
                    })();
                return P(new Func<string>[]
                {
                    () => $"At the end of chapter {ch} waited {f}. “{q}” {(ult != null ? $"{ult.Str("n")} answered with {ult.Str("ab")}." : best != null ? $"{best.Str("n")}’s {best.Str("ab")} struck true." : "The party held together.")} {f} slunk back into an older story, and the chapter was saved.",
                    () => $"The final page of chapter {ch} belonged to {f}, who sneered, “{q}” It was a long, loud battle. {(best != null ? $"In the end it was {best.Str("n")} and {best.Str("ab")} that turned the tide." : "In the end, friendship turned the tide.")}",
                    () => $"{f} had been waiting a very long time for readers like these. “{q}” {(ult != null ? $"But nobody expected {ult.Str("ab")} from {ult.Str("n")}." : $"But nobody expected {Or(mvp, "the party")} to be so brave.")} The villain retreated, and the page turned bright.",
                })();
            }

            static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

            static Dictionary<string, object> Page(double sc, List<object> h, string act, string t, string x, Dictionary<string, object> f = null)
            {
                var p = new Dictionary<string, object> { ["sc"] = sc, ["h"] = new List<object>(h), ["act"] = act, ["t"] = t, ["x"] = x };
                if (f != null) p["f"] = f;
                return p;
            }

            static string Squash(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
