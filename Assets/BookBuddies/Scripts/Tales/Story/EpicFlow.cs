using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The dungeon's driver (the site's drive, choicePhase, applyChoice, nextNode and epNext; tales.js T:860-906, T:1053-1062,
    /// victory T:1382-1386, defeat T:1387-1389) as plain steps the views call: Current shows what is on screen (filled
    /// once, so a resumed tale shows the same choices), Choose picks an option, Continue turns the page after a scene or
    /// a roll, and Finish books a fight's outcome. A step that starts a fight sets run.Battle; the views then save the run
    /// and play the fight (EpicBattle.Setup + TaleScreen.RunBattle). After an ordinary room the Story Master may turn the
    /// page (EpicRooms.Twist); a Fate card is drawn in every new land and the hand carries into the next saga.
    /// </summary>
    public static class EpicFlow
    {
        /// <summary>Starts the dungeon on a run with no saga yet (newRun and newVolume both end here): the prologue scene.</summary>
        public static void Start(TaleRun run) => EpicSaga.Begin(run, "dungeon");

        /// <summary>The view on screen now (the map when there is none), its choices filled on first sight and kept in run.View.</summary>
        public static EpicView Current(TaleRun run)
        {
            var v = EpicView.FromJson(run.View) ?? EpicView.Of("map");
            if (v.Opts == null && v.K != "scene" && v.K != "roll")
            {
                Fill(run, v);
                run.View = v.ToJson();
            }
            return v;
        }

        /// <summary>applyChoice: option i of the current map, scene, gift, relic or camp view.</summary>
        public static void Choose(TaleRun run, int i)
        {
            var v = Current(run);
            if (v.Opts.Count == 0) { Next(run); return; }
            var o = v.Opts[Math.Max(0, Math.Min(i, v.Opts.Count - 1))];
            run.View = null;
            switch (v.K)
            {
                case "map": Go(run, o); break;
                case "enc": EpicRooms.Resolve(run, v, i); break;
                case "boon":
                    if (o.Id == "ink") run.Gold += 25; else run.Boons.Add(o.Id);
                    foreach (var h in run.Party) h.Hp = Math.Min(h.Hp, TaleLife.MaxHp(run, h));
                    run.Tell("boon", new Dictionary<string, object> { ["n"] = o.N, ["i"] = o.I });
                    Next(run);
                    break;
                case "relic":
                    if (o.Id == "ink") run.Gold += 40;
                    else { run.Tell("relic", new Dictionary<string, object> { ["i"] = o.I, ["n"] = o.N }); EpicRooms.Gain(run, o.Id); }
                    Next(run);
                    break;
                default: Next(run); break;   // camp: turn the page
            }
        }

        /// <summary>Continue on a scene or a roll: a roll that starts a fight starts it, anything else turns the page.</summary>
        public static void Continue(TaleRun run)
        {
            var v = Current(run);
            run.View = null;
            if (v.K == "roll" && v.Then != null) Fight(run, v.Then);
            else Next(run);
        }

        /// <summary>nextNode: the Long Read camp first when a lair boss just fell, else epNext (after an ordinary room, the Story Master may turn the page).</summary>
        public static void Next(TaleRun run)
        {
            run.View = null;
            if (run.CampNext) { run.CampNext = false; run.View = EpicView.Of("camp").ToJson(); return; }
            var E = run.Ep;
            if (E.TwOn) { E.TwOn = false; return; }
            if (E.Next) { E.Next = false; EpicSaga.Begin(run, E.Kind); run.Ep.Hand.AddRange(E.Hand); return; }
            if (E.Intro) { E.Intro = false; ActIntro(run); return; }
            if (E.Here.T != "boss")
            {
                run.Node = E.At.r;
                if (E.Cur.Contains("c_overdue") && run.Gold > 0) run.Gold = Math.Max(0, run.Gold - 6);
                EpicRooms.Twist(run);
                return;
            }
            if (E.Act < 4 && !E.Rpick) { E.Rpick = true; run.View = EpicView.Of("relic").ToJson(); return; }
            E.Rpick = false;
            E.Done++;
            run.Ch++;
            foreach (var h in run.Party)
            {
                double m = TaleLife.MaxHp(run, h);
                h.Hp = h.Ko ? JsMath.Round(m * .4) : Math.Min(m, h.Hp + m * .35);
            }
            if (E.Act >= 4) { SagaEnd(run); return; }
            E.Act++;
            E.Ward = false;
            E.At = (0, 0);
            E.Trail.Clear();
            E.Map = EpicSaga.Map(run, E.Act);
            E.Hz = EpicSaga.LandOf(E).Hz;
            run.Node = 0;
            ActIntro(run);
        }

        /// <summary>Starts a fight of a kind (fight, elite, boss): run.Battle is set with its own seed; the views save the run, then play it.</summary>
        public static void Fight(TaleRun run, string kind)
        {
            run.View = null;
            run.Battle = new TaleFight { Id = run.Stats.Wins + 1, Seed = (int)(EpicRooms.Rng.Next() * 1e9), Kind = kind };
        }

        /// <summary>
        /// Books a fight's outcome on the run (setup = the fight EpicBattle.Setup made): HP (as a share of max HP), ink, the ink
        /// drops won or stolen mid-fight and the run stats either way. A win pays ink drops and xp, heals with relics and boons,
        /// writes the storybook and offers the gift; a loss writes "lost". True when the tale is over
        /// (then end it: TaleLife.End and TaleScreen.End with "defeat"). Either way the things the setup spent (bless, curse, ink, ally, weak, rival) are cleared.
        /// </summary>
        public static bool Finish(TaleRun run, BattleSetup setup, BattleOutcome o)
        {
            var E = run.Ep; string kind = run.Battle?.Kind ?? TaleBattle.Fight;
            E.Bless = E.Curse = false; E.Ink = 0; E.Ally = null;
            if (kind == TaleBattle.Boss) E.Weak = false;
            if (kind == TaleBattle.Elite) E.Rival = false;
            run.Battle = null;
            if (o.Revived) TaleLife.SecondWind(run);
            var h0 = run.Party.Count > 0 ? run.Party[0] : null;
            if (h0 != null && o.Hero != null)
            {
                // HP comes back as a share of max HP: gear changed in the bag mid-fight changes the run hero's max, not the fight's
                h0.Hp = o.Hero.Ko ? 0 : Math.Max(0, o.Hero.Hp) * (TaleLife.MaxHp(run, h0) / Math.Max(1, o.Hero.Max));
                h0.Ink = o.Hero.Ink;
            }
            run.Gold += o.Gold;
            foreach (var k in o.HelperKeys)
            {
                var npc = TalesData.Current.Npcs.Find(n => n.Key == k);
                if (npc != null) run.Tell("npc", new Dictionary<string, object> { ["n"] = npc.Name, ["i"] = npc.Icon, ["act"] = npc.Act });
            }
            // the site counts crits, healing and the biggest hit as they happen, so a lost fight counts too
            var s = run.Stats;
            s.Crits += o.Crits;
            s.Heals += o.Heals;
            if (o.BestMove != null && (s.Best == null || o.BestHit > s.Best.Value.d)) s.Best = (h0?.Seed.Name, o.BestMove, o.BestHit);
            int sc = setup.Tale?.Sc ?? EpicSaga.LandOf(E).Sc;
            if (!o.Won)
            {
                run.Tell("lost", StoryBook.LostEntry(setup.Tale?.ReadyFoes ?? new List<BattleUnit>(), sc));
                return true;
            }
            Won(run, kind, o, sc);
            return false;
        }

        // victory: ink drops, relic and boon heals, xp and new moves, run stats, the storybook's win page, then the gift
        static void Won(TaleRun run, string kind, BattleOutcome o, int sc)
        {
            var E = run.Ep;
            bool boss = kind == TaleBattle.Boss, elite = kind == TaleBattle.Elite;
            int drops = (boss ? 45 : elite ? 28 : 16) + run.Ch * 2;
            if (run.Boons.Contains("library")) drops = JsMath.RoundI(drops * 1.3);
            if (E.Rel.Contains("a_goose")) drops = JsMath.RoundI(drops * 1.25);
            if (E.Rel.Contains("a_beans")) drops += 12;
            if (E.Rel.Contains("a_tinder")) EpicRooms.Heal(run, .12, false);
            run.Gold += drops;

            var view = EpicView.Of("boon");
            view.Drops = drops;
            foreach (var h in run.Party)
            {
                h.Xp += boss ? 3 : elite ? 2 : 1;
                bool up = false;
                while (h.Xp >= h.Lvl + 1) { h.Xp -= h.Lvl + 1; h.Lvl++; up = true; }
                if (up) view.Lines.Add($"⬆️ {h.Seed.Name} reached level {h.Lvl}");
                foreach (var n in HeroFactory.TaleLearn(h, EpicRooms.Rng)) view.Lines.Add($"✨ {h.Seed.Name} learned {n}");
                if (run.Boons.Contains("cocoa") && !h.Ko) h.Hp = Math.Min(TaleLife.MaxHp(run, h), h.Hp + TaleLife.MaxHp(run, h) * .12);
                h.St.Clear();
            }

            var s = run.Stats;
            s.Wins++;
            if (boss) s.Bosses++;
            s.Foes += o.Defeated.Count;

            run.Tell("win", StoryBook.WinEntry(o, kind, sc));
            if (boss) { run.CampNext = true; run.CampBoss = o.Defeated.FirstOrDefault()?.Name; }
            run.View = view.ToJson();
        }

        /// <summary>epScene: a scene view (icon, title, lines) that waits for Continue.</summary>
        internal static void Scene(TaleRun run, string icon, string title, List<string> lines)
        {
            var v = new EpicView { K = "scene", I = icon, T = title };
            v.Lines.AddRange(lines);
            run.View = v.ToJson();
        }

        // actIntro, then the land's Fate card: its line goes before the side quest's, as on the site (T:955)
        static void ActIntro(TaleRun run)
        {
            EpicSaga.ActIntro(run);
            string drew = EpicRooms.Draw(run);
            if (drew == null) return;
            var v = EpicView.FromJson(run.View);
            int at = v.Lines.FindIndex(l => l.StartsWith("📜 Side quest"));
            v.Lines.Insert(at < 0 ? v.Lines.Count : at, drew);
            run.View = v.ToJson();
        }

        // fillView: a view's choices, made once
        static void Fill(TaleRun run, EpicView v)
        {
            switch (v.K)
            {
                case "map": v.Opts = EpicSaga.Options(run); break;
                case "enc": EpicRooms.FillEncounter(run, v); break;
                case "boon": v.Opts = EpicRooms.BoonOffer(run); break;
                case "relic": v.Opts = EpicRooms.RelicOffer(run); break;
                case "camp":
                    foreach (var h in run.Party) { h.Hp = TaleLife.MaxHp(run, h); h.St.Clear(); }
                    v.Boss = run.CampBoss;
                    v.Opts = new List<EpicOpt> { new EpicOpt { Id = "go", I = "📖", N = "Turn the page", D = "" } };
                    break;
                default: v.Opts = new List<EpicOpt>(); break;
            }
        }

        // epApply on the map: walk to the node and play its room
        static void Go(TaleRun run, EpicOpt o)
        {
            var E = run.Ep;
            E.At = (E.At.r + 1, o.To);
            run.Node = E.At.r;
            E.Trail.Add(E.At);
            var nd = E.Here;
            switch (nd.T)
            {
                case "fight": case "elite": case "boss": Fight(run, nd.T); break;
                case "rest": EpicRooms.Camp(run); break;
                case "shop": EpicRooms.Shop(run); break;
                default: run.View = new EpicView { K = "enc", Tp = nd.T, E = nd.E }.ToJson(); break;
            }
        }

        // saga end (act IV boss): ink drops and a level for everyone, the storybook's saga page, and a new saga on the next page
        static void SagaEnd(TaleRun run)
        {
            var E = run.Ep;
            int bonus = 120 + 40 * E.Saga;
            run.Gold += bonus;
            var learned = EpicRooms.LevelUp(run);
            var d = EpicSaga.Dark(E);
            string hero = (run.Party.Find(h => !h.Ko) ?? run.Party.FirstOrDefault())?.Seed.Name ?? "the party";
            run.Tell("saga", new Dictionary<string, object> { ["t"] = E.Name, ["relic"] = E.Relic, ["dark"] = d.N });
            E.Next = true;
            var lines = new List<string>
            {
                $"{d.N} falls with a sound like a book slammed shut.",
                $"The {E.Relic} glows in {hero}’s paws. Across every library, the stories feel whole again.",
                $"💧 +{bonus} ink drops · ⬆️ Every pet gains a level",
            };
            lines.AddRange(learned);
            if (E.Tone >= 2) lines.Add("Bards will sing about how kind this fellowship was.");
            else if (E.Tone <= -2) lines.Add("Some say this fellowship was a little too clever for its own good. They are probably right.");
            lines.Add("But every ending is the start of another story…");
            Scene(run, "🏆", $"The {E.Relic} is safe", lines);
        }
    }
}
