using System;
using System.Text.RegularExpressions;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The life of a solo tale (lobby.md §2.1): start one, leave it (it keeps), end it into a storybook, begin the next
    /// book, Second Wind once per tale, and a fresh hero snapshot when the bag closes. Saves go through TaleStore. Pure C#.
    /// The dungeon itself starts from the driver: a run with no Ep (new, or a new book) gets its saga there (beginEpic).
    /// </summary>
    public static class TaleLife
    {
        /// <summary>The two ways a tale ends (S.reason).</summary>
        public const string Ended = "ended", Defeat = "defeat";
        static readonly Regex BookSuffix = new Regex(@" · Book \d+$");

        /// <summary>tqTaleName: "The {a} of {b}", "{b} and the {a}" or "Beyond the {a}", from the seed.</summary>
        public static string TaleName(int seed)
        {
            var d = TalesData.Current;
            var r = new Mulberry32(unchecked((uint)(seed ^ 0x7a1e)));
            string a = r.Pick(d.TaleNamesA), b = r.Pick(d.TaleNamesB);
            int t = (int)Math.Floor(r.Next() * 3);
            return t == 0 ? $"The {a} of {b}" : t == 1 ? $"{b} and the {a}" : $"Beyond the {a}";
        }

        /// <summary>
        /// newRun for a small read: a seed, a name, your buddy at tale level 1 with full HP, the begin entry, and
        /// life.tales + 1 (which can open a class). Saved as the solo tale.
        /// </summary>
        public static TaleRun NewSolo(TalesSave save, string look, string name, IRng rng = null)
        {
            int seed = NewSeed(rng);
            var run = new TaleRun { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Seed = seed, Title = TaleName(seed) };
            SetParty(run, save, look, name);
            run.Tell("begin");
            save.Me.Count("tales");
            save.Touch();
            HeroFactory.CheckClassUnlocks(save);
            TaleStore.Save(run);
            return run;
        }

        /// <summary>leaveTale: back to the lobby; the tale is saved and waits (Continue).</summary>
        public static void Leave(TaleRun run) => TaleStore.Save(run);

        /// <summary>
        /// endTale: the tale is over ("ended" from the menu, "defeat" when the party fell; the dungeon tells "lost" first).
        /// The story log becomes the book, and the tale is saved with it.
        /// </summary>
        public static void End(TaleRun run, string reason)
        {
            run.Battle = null;
            run.View = null;
            run.Over = true;
            run.Reason = reason;
            if (reason == Ended) run.Tell("ended");
            var (dark, relic) = Thread(run);
            run.Book = StoryBook.Make(run, dark, relic, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            TaleStore.Save(run);
        }

        /// <summary>
        /// newVolume: the same tale, book n + 1. Your buddy comes back fresh (level 1, full HP), and everything the last
        /// book gathered (chapters, drops, boons, stats, story, the dungeon) starts over under a new seed.
        /// </summary>
        public static void NewVolume(TaleRun run, TalesSave save, string look, string name, IRng rng = null)
        {
            run.Vol++;
            run.Ch = 1; run.Node = 0; run.Gold = 30;
            run.Boons.Clear();
            run.Over = false; run.Reason = null; run.Book = null; run.Revived = false;
            run.Battle = null; run.View = null; run.Ep = null; run.CampNext = false; run.CampBoss = null;
            run.Stats = new TaleStats();
            run.Title = BookSuffix.Replace(run.Title ?? "A Tale", "") + $" · Book {run.Vol}";
            SetParty(run, save, look, name);
            run.Story.Clear();
            run.Tell("begin");
            run.Seed = NewSeed(rng);
            TaleStore.Save(run);
        }

        /// <summary>True while the library's Second Wind (read live, like the fight does) is still left in this tale (it works once per tale).</summary>
        public static bool CanRevive(TaleRun run, TalesSave save) => !run.Revived && HeroFactory.Library(save, "rev") > 0;

        /// <summary>A fight used Second Wind (BattleOutcome.Revived): spent for the rest of the tale, and told once.</summary>
        public static void SecondWind(TaleRun run)
        {
            if (run.Revived) return;
            run.Revived = true;
            run.Tell("revive");
        }

        /// <summary>
        /// The bag closed mid-tale: your hero takes its new gear, class and look (a fresh snapshot) and keeps its share of
        /// HP; its tale level, xp, ink, moves and statuses stay.
        /// </summary>
        public static void Resnap(TaleRun run, TalesSave save, string look, string name)
        {
            var h = run.Party.Find(x => x.Seed.PetKey == "me");
            if (h == null) return;
            double frac = h.Hp / Math.Max(1, MaxHp(run, h));
            h.Seed = HeroFactory.SeedOf(save, look, name);
            h.Hp = Math.Max(h.Ko ? 0 : 1, JsMath.Round(MaxHp(run, h) * frac));
        }

        /// <summary>tqStats' max HP (HeroFactory.SetLevel with the run's boons): base HP grown 8% a tale level, +20% per Thick Spine, +10% with Hero of the Hour.</summary>
        public static double MaxHp(TaleRun run, TaleHero h)
        {
            var hero = HeroFactory.Build(h.Seed, h.Lvl);
            HeroFactory.SetLevel(hero, h.Lvl, run.Boons);
            return hero.Max;
        }

        /// <summary>The chapter a lobby card shows ("Chapter n", S.ch).</summary>
        public static string Progress(TaleRun run) => run.Over ? "The End" : $"Chapter {run.Ch}";

        // your buddy as it sets out: a snapshot, tale level 1, its kit, full HP
        static void SetParty(TaleRun run, TalesSave save, string look, string name)
        {
            var seed = HeroFactory.SeedOf(save, look, name);
            var built = HeroFactory.Build(seed, 1);
            var h = new TaleHero { Seed = seed };
            h.Kn.AddRange(built.Hero.Known);
            run.Party.Clear();
            run.Party.Add(h);
            h.Hp = MaxHp(run, h);
            run.Cast.Clear();
            run.Cast.Add(TaleCast.Of(seed));
        }

        static int NewSeed(IRng rng) => (int)((rng ?? SystemRng.Shared).Next() * 1e9);

        // the dungeon's villain and relic (S.ep.dark, S.ep.relic); they thread through the book
        static (FoeDef dark, string relic) Thread(TaleRun run)
        {
            if (run.Ep == null) return (null, null);
            var all = TalesData.Current.Dark;
            return (all[run.Ep.Dark % all.Count], run.Ep.Relic);
        }
    }
}
