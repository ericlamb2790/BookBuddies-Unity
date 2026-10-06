using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Road
{
    /// <summary>
    /// What the places in a town on Bramble Road do (the site's wildAct, curioAct and the gag handler): the landmark
    /// and Pawtopia's fountain (a line, then the lore stone), the curios (sometimes hiding gear), the silly gags, the
    /// cottage doors and the Paw Express station (the shop is UI/Shop/TownShop, the Book Boss ring Tales/Road/BossSpots).
    /// </summary>
    public static class TownSpots
    {
        const float LineSeconds = 5.5f, CurioSeconds = 6.5f, GagSeconds = 6f, FindChance = .25f, RevealDelay = .9f;

        static System.Func<PlazaWorld> current;

        /// <summary>Registers every town place. "world" gives the town you're in when a place is used.</summary>
        public static void Register(System.Func<PlazaWorld> world)
        {
            current = world;
            SpotActions.Register("mon", Landmark);
            SpotActions.Register("fountain", Landmark);
            SpotActions.Register("curio", Curio);
            SpotActions.Register("gag", Gag);
            SpotActions.Register("door", Door);
            SpotActions.Register("station", _ => TownSheets.Station(World));
        }

        static PlazaWorld World => current?.Invoke();
        static TownBook.Town Town => TownBook.Current.Get(World?.Map.Key);
        static T Pick<T>(IList<T> list) => list[Random.Range(0, list.Count)];

        // the landmark: a hop and one of the town's lines, and the lore stone opens
        static void Landmark(TownMap.Spot spot)
        {
            var w = World;
            var t = Town;
            if (w == null || t == null) return;
            w.Trick("hop");
            if (t.Lines.Length > 0) w.Me.Say(Pick(t.Lines), LineSeconds);
            TownSheets.Lore(w);
        }

        // curioAct: happy, its emote, a sparkle and a line; a quarter of the time gear was hiding there too
        static void Curio(TownMap.Spot spot)
        {
            var w = World;
            var t = Town;
            if (w == null || t == null || spot.Index < 0 || spot.Index >= t.Curios.Count) return;
            var c = t.Curios[spot.Index];
            Poke(w, spot, c, CurioSeconds, .9f);
            if (!Buddy.Hatched || Random.value >= FindChance) return;
            var save = TalesSave.Current;
            string item = Loot.Find(t.Key, save);
            if (item == null) return;
            save.Touch();
            w.StartCoroutine(Reveal(item));
        }

        static void Gag(TownMap.Spot spot)
        {
            var w = World;
            var gags = TownBook.Current.Gags;
            if (w == null || spot.Index < 0 || spot.Index >= gags.Count) return;
            Poke(w, spot, gags[spot.Index], GagSeconds, .8f);
        }

        static void Poke(PlazaWorld w, TownMap.Spot spot, TownBook.Poke p, float seconds, float sparkle)
        {
            w.Me.Play("happy", .9f);
            w.Me.ShowEmote(p.Icon);
            Fx.Sparkle(new Vector2(spot.Area.xMin + .5f, spot.Area.yMin - .4f), 0, sparkle);
            Sound.Play("coin");
            w.Me.Say(Pick(p.Lines), seconds);
        }

        static System.Collections.IEnumerator Reveal(string item)
        {
            yield return new WaitForSecondsRealtime(RevealDelay);
            TalesUi.Reveal(new List<string> { item }, "Something else was hiding there!");
        }

        // cottages come in a later version: whoever lives here is out
        static void Door(TownMap.Spot spot)
        {
            var house = Town?.House;
            if (house == null || house.Length == 0) return;
            string who = house[Mathf.Max(0, spot.Index) % house.Length];
            RoadCard.Tell("🚪", $"{who} isn’t home right now", "Knock again another day.");
        }
    }
}
