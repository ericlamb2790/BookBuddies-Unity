using BookBuddies.Live;
using BookBuddies.Road;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    // The Book Boss sheets: the ring (gymSheet), the sealed gate, and the win (the badge turning over, the gift and
    // your badges). They share the town sheets' look and building blocks.
    public static partial class TownSheets
    {
        const int BadgesPerRow = 9;
        const float BadgeTile = 48, BadgeFlip = .9f, WinFlip = .35f;
        static readonly string[] Roman = { "I", "II", "III", "IV" };

        static BossBook Bosses => BossBook.Current;

        /// <summary>
        /// The Book Boss ring (gymSheet): the boss and its team, its phases, what a win brings, your Book Badges, and
        /// Challenge (Rematch once you hold the badge), which asks first when the boss is above your level.
        /// </summary>
        public static void Boss(PlazaWorld w)
        {
            string k = w?.Map.Key;
            var b = Bosses.Get(k);
            if (b == null) return;
            var save = TalesSave.Current;
            bool won = BossBook.Beaten(save, k);
            int i = System.Array.IndexOf(Data.Route, k);
            var next = i >= 0 && i < Data.Route.Length - 1 ? Book.Get(Data.Route[i + 1]) : null;
            var team = BossBook.TeamOf(b);
            var s = Open("🏅", b.Hall);

            var who = Row(s.Card, b.Icon, b.Name, $"Book Boss of {Book.Get(k)?.Name} · " + (team.Length > 0 ? "with " + Names(team) : "fights alone"));
            LevelBadge(who, b.Level);
            Phases(s.Card, b);
            Quote(s.Card, won ? b.Beaten : b.Challenge);
            if (won)
            {
                Line(s.Card, b.BadgeIcon, $"You hold the {b.Badge}.");
                Line(s.Card, "🌾", next != null ? $"The road on to {next.Name} is open." : "You’ve beaten every leader on the road.");
            }
            else
            {
                Line(s.Card, b.BadgeIcon, $"Win the {b.Badge}");
                Line(s.Card, "🌾", next == null ? "The final badge on the road"
                    : i < 2 ? $"Bramble Road to {next.Name} is already open. Come back once you have leveled up"
                    : $"Opens Bramble Road on to {next.Name}");
                Line(s.Card, "🎁", $"A guaranteed piece of {Loot.ThemeName(b.Theme).ToLowerInvariant()} gear, rare or better");
            }
            BadgeRow(s.Card, save, null);
            Buttons(s, "Not yet", null, won ? "Rematch" : "Challenge",
                () => RoadSpots.DangerAsk(b.Level, b.Hall, BossBook.FightLevel(b, won), () => BossSpots.Fight(k)));
            s.Open();
        }

        /// <summary>
        /// The way on while this town's Book Boss stands (the site's sealed gateSheet): only badge holders may pass.
        /// "Take me to the Book Boss" walks to the ring and opens it; a gate with no side can still head back.
        /// False (and nothing shown) when the road on isn't sealed.
        /// </summary>
        public static bool Sealed(PlazaWorld w, string side)
        {
            string k = w?.Map.Key;
            var b = Bosses.Get(k);
            int i = System.Array.IndexOf(Data.Route, k);
            if (b == null || i < 0 || i >= Data.Route.Length - 1 || !Bosses.Seals(TalesSave.Current, k)) return false;
            var s = Open("🌾", "Bramble Road");
            var seal = UiKit.Panel(s.Card, "sealed", Palette.Hex(b.Color).WithAlpha(.12f));
            UiKit.Column(seal.rectTransform, 6, new RectOffset(16, 16, 14, 16), TextAnchor.UpperCenter);
            UiKit.Icon(seal.transform, "🔒", 40);
            UiKit.Label(seal.transform, $"The road to {Book.Get(Data.Route[i + 1]).Name} is sealed", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            UiKit.Label(seal.transform, $"Only readers who hold the {b.Badge} may pass. Beat the Book Boss, {b.Name}, at {b.Hall} first.",
                UiKit.SmallSize + 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            bool back = i > 0 && side != "w";
            Buttons(s, back ? "Head back instead" : "Okay", back ? () => Gate(w, "e") : (System.Action)null, "Take me to the Book Boss", () =>
            {
                var ring = w.Map.Spots.Find(x => x.Kind == "gym");
                if (ring != null) w.GoToSpot(ring);
            });
            s.Open();
            return true;
        }

        /// <summary>
        /// After a win (gymDone's sheet): the badge turns over, the boss's parting line, the road it opens, the leader's
        /// gift (or today's rematch gift), and your badges with the new one stamped in. Open bag goes to the gift.
        /// </summary>
        public static void BossWon(BossBook.Boss b, bool first, LootDrop gift)
        {
            int i = System.Array.IndexOf(Data.Route, b.Town);
            var next = first && i >= 0 && i < Data.Route.Length - 1 ? Book.Get(Data.Route[i + 1]) : null;
            var s = Open(b.BadgeIcon, first ? "Book Badge earned!" : "Rematch won!");

            var badge = UiKit.Panel(s.Card, "badge", Palette.Hex(b.Color));
            UiKit.Column(badge.rectTransform, 2, new RectOffset(16, 16, 12, 14), TextAnchor.MiddleCenter);
            UiKit.Icon(badge.transform, b.BadgeIcon, 72);
            UiKit.Label(badge.transform, b.BadgeName, UiKit.TitleSize - 4, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter);
            UiKit.Label(badge.transform, $"{b.Name} is defeated", UiKit.SmallSize, Palette.Cream.WithAlpha(.85f), UiKit.Bold, TextAnchor.MiddleCenter);
            ShopKit.Reveal(badge.rectTransform, WinFlip);

            Quote(s.Card, b.Beaten);
            if (next != null) Line(s.Card, "🌾", $"Bramble Road is open on to {next.Name}");
            if (gift?.Gear != null) Gift(s.Card, gift, first);
            else Note(s.Card, "Rematch gifts come once a day.");
            BadgeRow(s.Card, TalesSave.Current, b.Town);
            Buttons(s, "Open bag", () => TalesUi.OpenBag(gift?.Kept == true ? gift.Item : null), next != null ? "Onward!" : "Nice!", null, "🎒");
            s.Open();
        }

        // ---- boss building blocks ----

        static string Names(FoeVariant[] team)
        {
            var names = new string[team.Length];
            for (int j = 0; j < team.Length; j++) names[j] = team[j].Name;
            return names.Length == 1 ? names[0] : string.Join(", ", names, 0, names.Length - 1) + " and " + names[names.Length - 1];
        }

        // I the boss, II and III its rises, each with the twists it gains
        static void Phases(Transform card, BossBook.Boss b)
        {
            var box = UiKit.Panel(card, "phases", Palette.Paper);
            UiKit.Column(box.rectTransform, 6, new RectOffset(14, 14, 10, 12));
            Phase(box.transform, 0, b.Name, null);
            for (int j = 0; j < b.Phases.Length && j + 1 < Roman.Length; j++) Phase(box.transform, j + 1, b.Phases[j].Name, b.Phases[j].Twists);
        }

        static void Phase(Transform box, int n, string name, string[] twists)
        {
            var row = UiKit.Node("phase", box);
            UiKit.Row(row, 8).childForceExpandWidth = false;
            UiKit.Size(UiKit.Badge(row, Roman[n], UiKit.EmberInk), 40);
            UiKit.Size(UiKit.Label(row, name, UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold), -1, -1, 1);
            if (twists == null) return;
            var gains = UiKit.Node("twists", box);
            UiKit.Row(gains, 6, new RectOffset(48, 0, 0, 0)).childForceExpandWidth = false;
            foreach (var m in twists)
            {
                if (!Bosses.Twists.TryGetValue(m, out var t)) continue;
                UiKit.Icon(gains, t.icon, 18);
                UiKit.Label(gains, t.text, UiKit.SmallSize, Palette.InkSoft);
            }
        }

        static void Quote(Transform card, string line) =>
            UiKit.Label(card, $"“{line}”", UiKit.BodySize, Palette.InkSoft, UiKit.Title, TextAnchor.MiddleCenter);

        // a small icon and a line of what's at stake
        static void Line(Transform card, string icon, string text)
        {
            var row = UiKit.Node("line", card);
            UiKit.Row(row, 8).childForceExpandWidth = false;
            UiKit.Icon(row, icon, 22);
            UiKit.Size(UiKit.Label(row, text, UiKit.SmallSize + 1, Palette.Ink), -1, -1, 1);
        }

        static void Gift(Transform card, LootDrop gift, bool first)
        {
            var it = gift.Gear;
            Line(card, "🎁", first ? "Leader’s gift" : "Today’s rematch gift");
            var box = UiKit.Panel(card, "gift", TalesUi.RarityColor(it.Tier).WithAlpha(.16f));
            UiKit.Column(box.rectTransform, 0, new RectOffset(12, 12, 4, 4));
            var row = Row(box.transform, it.Icon, it.Name,
                $"{Loot.RarityName(it.Tier)} {Loot.SlotName(it.Slot).ToLowerInvariant()}{(gift.Kept ? "" : " · bag full, turned into dust")}");
            Chip(row, JsMath.Num(it.Power), "⚡");
        }

        // gymBadges: one tile per Book Boss on the route, in its colour once won; the new one turns over a moment later
        static void BadgeRow(Transform card, TalesSave save, string fresh)
        {
            UiKit.Rule(card);
            UiKit.Label(card, $"Your Book Badges · {Bosses.Badges(save)} of {Bosses.Route.Length}", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            var grid = UiKit.Node("badges", card).gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(BadgeTile, BadgeTile);
            grid.spacing = new Vector2(6, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = BadgesPerRow;
            grid.childAlignment = TextAnchor.UpperCenter;
            foreach (var k in Bosses.Route)
            {
                var b = Bosses.Get(k);
                bool won = BossBook.Beaten(save, k);
                var tile = UiKit.Panel(grid.transform, k, won ? Palette.Hex(b.Color) : Palette.Ink.WithAlpha(.08f), 12);
                tile.raycastTarget = false;
                var icon = UiKit.Icon(tile.transform, won ? b.BadgeIcon : "🔒", won ? 30 : 22);
                icon.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, Vector2.one * (won ? 30 : 22));
                if (!won) icon.color = Color.white.WithAlpha(.45f);
                if (k != fresh) continue;
                UiKit.Outline(tile, Palette.Amber, 12, 3, 2);
                ShopKit.Reveal(tile.rectTransform, BadgeFlip);
            }
        }

        // "no" (secondary, just closes unless given an act) and "yes" (primary, gets the gamepad's focus) on the right
        static void Buttons(Sheet s, string no, System.Action noAct, string yes, System.Action yesAct, string noIcon = null)
        {
            var row = UiKit.Node("buttons", s.Card);
            UiKit.Row(row, 10, null, TextAnchor.MiddleRight).childForceExpandWidth = false;
            UiKit.Secondary(row, no, () => { s.Close(); noAct?.Invoke(); }, noIcon);
            s.First = UiKit.Primary(row, yes, () => { s.Close(); yesAct?.Invoke(); });
        }
    }
}
