using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Road
{
    /// <summary>
    /// What the road's places do (the site's wildAct and road extras): the ways back and on at each end of a road,
    /// the cave mouth and its exit, the trailhead, the campfire, the wayshrine, the hidden stash and the guardian's
    /// chest (the town gates are BossSpots', which checks the Book Boss's seal first). Registered once with
    /// SpotActions; each handler acts on the town you're in now.
    /// </summary>
    public static class RoadSpots
    {
        static readonly string[] CampLines = { "Toasty. Your pet is fully rested.", "You warm your paws by the fire. All better.", "A marshmallow, a short story, and a full heal." };
        static readonly string[] ShrineLines =
        {
            "You leave a pressed flower at the shrine. The emblem glows.", "A breeze turns the pages of a book left as an offering.",
            "Someone carved “almost there!” into the base.", "The shrine hums like a town full of readers.", "A little bell rings. The road ahead feels shorter.",
        };
        static readonly string[] StashLines = { "Nothing left but a note: “Back tomorrow.”", "Empty. A squirrel is looking at you very innocently.", "Just leaves now. Come back another day." };
        static readonly Vector2Int CaveMouth = new Vector2Int(48, 42);
        const int EastEnd = 109; // arriving on a road from the town ahead (RW - 3)

        static System.Func<PlazaWorld> current;

        /// <summary>Registers every road place. "world" gives the town you're in when a place is used.</summary>
        public static void Register(System.Func<PlazaWorld> world)
        {
            current = world;
            SpotActions.Register("home", _ => WalkOff(false));
            SpotActions.Register("towns", _ => WalkOff(true));
            SpotActions.Register("cave", _ => IntoTheCaves());
            SpotActions.Register("exit", _ => Boot.Travel(TownBook.LinkPlace(TalesSave.Current.Link), CaveMouth));
            SpotActions.Register("trail", Trailhead);
            SpotActions.Register("camp", Camp);
            SpotActions.Register("rshrine", Shrine);
            SpotActions.Register("stash", Stash);
            SpotActions.Register("chest", Chest);
        }

        static PlazaWorld World => current?.Invoke();
        static TalesData Data => TalesData.Current;
        static T Pick<T>(IList<T> list) => list[Random.Range(0, list.Count)];

        /// <summary>Your level for the road's warnings and level badges: your Pet Lv (the site uses your reader level from books read).</summary>
        public static int ReaderLevel => HeroFactory.Renown(TalesSave.Current.Me.Rxp).lvl + 1;

        // ---- coming and going ----

        /// <summary>Walks onto road link l (linkTo): at its west end, or at its east end when coming back from the town ahead.</summary>
        public static void WalkTheRoad(int link, bool fromEast)
        {
            TalesSave.Current.Link = link;
            TalesSave.Current.Touch();
            Boot.Travel(TownBook.LinkPlace(link), fromEast ? new Vector2Int(EastEnd, TownBook.RoadY(EastEnd, link)) : (Vector2Int?)null);
        }

        // off the end of a road into its town: the next town's west gate, or the east gate of the town behind you
        static void WalkOff(bool east)
        {
            var wild = World?.Map.Wild;
            var end = east ? wild?.East : wild?.West;
            if (end == null) return;
            var at = TownBook.ArrivalFromRoad(end.Key, east);
            Boot.Travel(end.Key, new Vector2Int(at[0], at[1]));
        }

        // the caves are as tough as the road you came in from: its tier's foes, a level up
        static void IntoTheCaves()
        {
            int link = Mathf.Clamp(TalesSave.Current.Link, 1, Data.Route.Length - 1);
            var tier = Data.Tiers[Data.LinkT[link] - 1];
            DangerAsk(Data.RouteLv[link] + 1, "The Inkwell Caves", tier.Lvl + 1, () => Boot.Travel("caves", null));
        }

        /// <summary>
        /// The site's dangerAsk: if the place is above your level, asks before you go ("Once a fight starts you
        /// can't run…").
        /// </summary>
        public static void DangerAsk(int required, string where, int foes, System.Action go)
        {
            int level = ReaderLevel;
            if (required <= level) { go(); return; }
            bool very = required >= level + 3;
            RoadCard.Ask(very ? "☠️" : "⚠️", very ? "Very dangerous road" : "Dangerous road",
                $"{where} is meant for reader level {required}, with foes around level {foes}. You’re level {level}. Once a fight starts you can’t run, and if your pet faints it wakes up back in town.",
                "Go anyway", go, "Turn back");
        }

        // ---- places to stop ----

        static void Trailhead(TownMap.Spot spot)
        {
            var w = World;
            if (w == null) return;
            w.Me.ShowEmote("📜");
            w.Notify("📜 The Tales lobby opens in a later version");
        }

        static void Camp(TownMap.Spot spot)
        {
            var w = World;
            if (w == null) return;
            RoadVitals.Set(1, RoadVitals.Now(false).ink);
            Cheer(w, "🔥", Pick(CampLines));
            Fx.Sparkle(w.Me.Pos + new Vector2(0, -.6f));
            Sound.Play("coin");
        }

        static void Shrine(TownMap.Spot spot)
        {
            var w = World;
            if (w == null) return;
            Cheer(w, spot.Icon, Pick(ShrineLines));
            Fx.Sparkle(new Vector2(spot.Use.x + .5f, spot.Use.y - 3), 0, 1.1f);
            if (Buddy.Hatched) Find(new[] { "shrine" }, "The wayshrine leaves you a gift", .5f); // once a day, and not always
        }

        static void Stash(TownMap.Spot spot)
        {
            var w = World;
            if (w == null) return;
            var save = TalesSave.Current;
            if (Loot.UsedToday(save, "stash")) { w.Me.Say(Pick(StashLines)); return; }
            Cheer(w, "🧰", null);
            Fx.Sparkle(new Vector2(spot.Use.x + .5f, spot.Use.y - 1), 0, 1.2f);
            Sound.Play("coin");
            if (Buddy.Hatched) { Find(new[] { "stash" }, "You found the Bramble Stash!", .5f); return; } // Loot.Roll marks the day
            Loot.MarkToday(save, "stash");
            save.Touch();
            w.Notify("A pile of old coins and a note: “Hatch a pet and come back.”");
        }

        static void Chest(TownMap.Spot spot)
        {
            var w = World;
            if (w == null) return;
            var save = TalesSave.Current;
            if (w.Road != null && w.Road.GuardianAwake) { w.Notify("The guardian won’t let anyone near the chest. Beat it first!"); return; }
            if (!save.ChestReady)
            {
                int left = RoadFoes.LairMinutesLeft();
                w.Notify(left > 0 ? $"The chest is empty. The guardian wakes again in {left} min." : "The guardian is back. Listen for it in the lair.");
                return;
            }
            save.ChestReady = false;
            save.Touch();
            if (!Buddy.Hatched) { w.Notify("Hatch your pet first. The chest holds gear for Tales of Pages."); return; }
            Fx.Sparkle(new Vector2(spot.Area.xMin + .5f, spot.Area.yMax + .5f));
            Find(new[] { "boss", "boss" }, "The guardian’s chest creaks open…", 0);
        }

        // happy hop, an emote and (optionally) something said
        static void Cheer(PlazaWorld w, string emote, string line)
        {
            w.Me.Play("happy", .9f);
            w.Me.ShowEmote(emote);
            if (line != null) w.Me.Say(line);
        }

        /// <summary>Rolls gear from the given sources (Loot.Roll puts it in the bag) and shows what turned up.</summary>
        static void Find(string[] sources, string title, float delay)
        {
            var found = new List<string>();
            foreach (var source in sources)
            {
                string item = Loot.Roll(source, TalesSave.Current);
                if (!string.IsNullOrEmpty(item)) found.Add(item);
            }
            if (found.Count == 0) return;
            if (delay <= 0) TalesUi.Reveal(found, title);
            else World?.StartCoroutine(RevealLater(found, title, delay));
        }

        static System.Collections.IEnumerator RevealLater(List<string> items, string title, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            TalesUi.Reveal(items, title);
        }
    }
}
