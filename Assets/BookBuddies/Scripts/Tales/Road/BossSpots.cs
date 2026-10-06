using System.Collections;
using System.Globalization;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Road
{
    /// <summary>
    /// Town Book Bosses (the site's gym): the ring opens the boss's sheet, Challenge starts a wild battle against the
    /// boss and its team, a win brings the Book Badge, a gift and its moment (the chapter title, then the badge turning
    /// over), and from the third town on the east gate stays sealed until the boss is beaten.
    /// </summary>
    public static class BossSpots
    {
        const float FightDelay = .9f, TitleDelay = .9f, SheetDelay = 2.6f, RematchSheetDelay = 1.6f, LostDelay = .6f;
        const int Floaters = 6;

        static System.Func<PlazaWorld> current;

        /// <summary>Registers the ring and the town gates. "world" gives the town you're in when a place is used.</summary>
        public static void Register(System.Func<PlazaWorld> world)
        {
            current = world;
            SpotActions.Register("gym", _ => TownSheets.Boss(World));
            SpotActions.Register("road", Gate);
        }

        static PlazaWorld World => current?.Invoke();

        // a town gate: the way on (side "w", or a gate with no side) is sealed while this town's boss stands
        static void Gate(TownMap.Spot spot)
        {
            var w = World;
            if (spot.Side == "e" || !TownSheets.Sealed(w, spot.Side)) TownSheets.Gate(w, spot.Side);
        }

        /// <summary>gymFight: the boss says its line, and a moment later the battle opens from your pet.</summary>
        public static void Fight(string town)
        {
            var w = World;
            var b = BossBook.Current.Get(town);
            if (w == null || b == null || BattleScreen.Open) return;
            if (!Buddy.Hatched) { w.Notify("Hatch your pet to battle in the wilds"); return; }
            w.Halt();
            w.ClearHint();
            w.Me.ShowEmote("⚔️");
            w.Notify($"{b.Icon} {b.Name}: “{b.Challenge}”");
            w.StartCoroutine(Begin(w, b));
        }

        static IEnumerator Begin(PlazaWorld w, BossBook.Boss b)
        {
            yield return new WaitForSecondsRealtime(FightDelay);
            var (hp, ink) = RoadVitals.Now(true);
            var setup = BossBook.Setup(b, BossBook.Beaten(TalesSave.Current, b.Town), hp, ink);
            var cam = Camera.main;
            Vector2 from = cam ? (Vector2)cam.WorldToScreenPoint(TownMap.ToWorld(w.Me.Pos.x, w.Me.Pos.y)) : new Vector2(Screen.width / 2f, Screen.height / 2f);
            BattleScreen.Run(setup, from, o => Done(w, b, o));
        }

        // gymDone: HP and ink as the fight left them, then the win's moment or the boss's parting shot
        static void Done(PlazaWorld w, BossBook.Boss b, BattleOutcome o)
        {
            if (!w || o == null || o.Rounds <= 0) return; // the battle never got going
            RoadVitals.After(!o.Won, o);
            if (o.Won) Won(w, b);
            else Lost(w, b);
        }

        static void Won(PlazaWorld w, BossBook.Boss b)
        {
            string today = System.DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var (first, gift) = BossBook.Win(TalesSave.Current, b, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), today, SystemRng.Shared);
            w.Me.Play("happy", 1.1f, .2f);
            w.Me.ShowEmote(b.BadgeIcon);
            Fx.Sparkle(w.Me.Pos + new Vector2(0, -.6f), 0, 1.2f);
            Sound.Play("coin");
            w.StartCoroutine(Celebrate(w, b, first, gift));
        }

        // the chapter title (with badges floating up the first time), then the sheet where the badge turns over
        static IEnumerator Celebrate(PlazaWorld w, BossBook.Boss b, bool first, LootDrop gift)
        {
            yield return new WaitForSecondsRealtime(TitleDelay);
            w.Announce(first ? b.Badge : "Rematch won", $"{b.Name} is defeated");
            if (first)
                for (int i = 0; i < Floaters; i++) Fx.Float(b.BadgeIcon, w.Me.Pos + new Vector2(Random.Range(-1.2f, 1.2f), -.8f), i * .15f);
            yield return new WaitForSecondsRealtime(first ? SheetDelay : RematchSheetDelay);
            TownSheets.BossWon(b, first, gift);
        }

        static void Lost(PlazaWorld w, BossBook.Boss b)
        {
            w.Me.ShowEmote("💤");
            w.StartCoroutine(Later(LostDelay, () => w.Notify($"{b.Icon} “{b.Lost}” Your pet rests up. Challenge again any time.")));
        }

        static IEnumerator Later(float seconds, System.Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }
    }
}
