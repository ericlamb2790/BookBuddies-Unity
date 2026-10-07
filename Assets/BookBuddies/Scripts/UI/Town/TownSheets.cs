using System.Collections;
using BookBuddies.Live;
using BookBuddies.Road;
using BookBuddies.Tales;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Bramble Road's sheets in town (the site's gateSheet, loreSheet, stationSheet and the map menu's travel rows): the
    /// way on or back at a town gate; the lore stone with the town's story, your home stone and fast travel to towns
    /// you've walked to; the Paw Express to any town; and Recall with every town on the route. Each opens on its own
    /// canvas and goes when closed.
    /// </summary>
    public static partial class TownSheets
    {
        const float Width = 540;
        const int RowIcon = 34, MiniHeight = 44;
        const float RecallDelay = .35f;

        // where a list of route rows is shown, which decides each row's button
        enum RouteFrom { Map, Stone, Station }

        static TalesData Data => TalesData.Current;
        static TownBook Book => TownBook.Current;

        /// <summary>
        /// A town gate (gateSheet): "On to" the next town from the east gate ("w"), "Back to" the town behind from
        /// the west gate ("e"), both from a gate with no side. Walk asks first when the road is above your level.
        /// </summary>
        public static void Gate(PlazaWorld w, string side)
        {
            int i = TownBook.Index(w?.Map.Key);
            if (i < 0) return;
            var s = Open("🌾", "Bramble Road");
            Note(s.Card, "The road runs from town to town, and every stretch is tougher than the one before. Foes hide in the tall grass.");
            if (i < Data.Route.Length - 1 && side != "e") GateRow(s, i + 1, Data.Route[i + 1], true);
            if (i > 0 && side != "w") GateRow(s, i, Data.Route[i - 1], false);
            s.Open();
        }

        static void GateRow(Sheet s, int link, string to, bool onward)
        {
            var z = Data.Tiers[Data.LinkT[link] - 1];
            var t = Book.Get(to);
            int foes = TownBook.FoeLevel(link); // the site said the tier's base level, too low from link 8 on
            var row = Row(s.Card, t.Icon, (onward ? "On to " : "Back to ") + t.Name,
                $"{z.Name} · foes Lv {foes}–{foes + 2}{(z.Elites.Length > 0 ? " · villain elites" : "")}", z.Icon);
            LevelBadge(row, Data.RouteLv[link]);
            Mini(s, row, "Walk", true, () => RoadSpots.DangerAsk(Data.RouteLv[link], z.Name, foes, () => RoadSpots.WalkTheRoad(link, !onward)));
        }

        /// <summary>The lore stone in the square (loreSheet): the town's story, binding your home here, and fast travel.</summary>
        public static void Lore(PlazaWorld w)
        {
            string k = w?.Map.Key;
            var t = Book.Get(k);
            if (t == null || TownBook.Index(k) < 0) return;
            TownProgress.Touched(k);
            var s = Open(t.Icon, t.Name);

            var story = UiKit.Panel(s.Card, "lore", Palette.Paper);
            UiKit.Column(story.rectTransform, 6, new RectOffset(16, 16, 12, 14)).childForceExpandWidth = true;
            var top = UiKit.Node("stone", story.transform);
            UiKit.Row(top, 8);
            UiKit.Icon(top, "🪨", 24);
            UiKit.Size(UiKit.Label(top, t.Landmark, UiKit.BodySize, Palette.Ink, UiKit.Bold), -1, -1, 1);
            LevelBadge(top, t.Level);
            UiKit.Label(story.transform, t.Lore, UiKit.SmallSize + 1, Palette.InkSoft);

            bool home = TownProgress.Home == k;
            var homeTown = Book.Get(TownProgress.Home);
            var bind = Row(s.Card, home ? "🏠" : "🪨", home ? "This is your home stone" : "Bind your home here",
                home ? "If your pet faints on the road, it wakes up here. Recall brings you back once an hour."
                : homeTown != null ? $"Your home is {homeTown.Name}. Binding here moves it." : "Your pet wakes up here after fainting, and Recall can bring you home.");
            if (home) Chip(bind, "Home");
            else Mini(s, bind, "Bind", true, () =>
            {
                TownProgress.Bind(k);
                w.Me.ShowEmote("🏠");
                Fx.Sparkle(w.Me.Pos + new Vector2(0, -.4f));
                w.Notify($"🏠 {t.Name} is your home stone now");
            });

            Section(s.Card, "✨", "Fast travel", "Lore stones are linked. Ride to the stone in any town you’ve walked to.");
            RouteRows(s, w, RouteFrom.Stone);
            s.Open();
        }

        /// <summary>The Paw Express (stationSheet, build 539): a free ride to any town on Bramble Road, visited or not.</summary>
        public static void Station(PlazaWorld w)
        {
            if (w == null) return;
            var s = Open("🚂", "Paw Express");
            Note(s.Card, "Trains run to every town on Bramble Road. Pick a stop and hop on.");
            RouteRows(s, w, RouteFrom.Station);
            s.Open();
        }

        /// <summary>Travel from anywhere (the map menu's rows): Recall to your stones, and every town on Bramble Road.</summary>
        public static void Route(PlazaWorld w)
        {
            if (w == null) return;
            var s = Open("🗺️", w.Map.Name);
            RecallRows(s, w);
            Section(s.Card, "🛤️", "Towns on Bramble Road", "Walk the road to reach a new town. After that, the Paw Express can take you back.");
            RouteRows(s, w, RouteFrom.Map);
            s.Open();
        }

        // ---- moving between stones ----

        /// <summary>Recall (stoneRecall): once an hour, back to your home or last stone from anywhere, but not mid-fight.</summary>
        public static void Recall(PlazaWorld w, string key)
        {
            double left = TownProgress.RecallLeft;
            if (left > 0) { w.Notify("🪨 Recall is ready in " + TownProgress.Minutes(left)); return; }
            if (w.Road != null && w.Road.Fighting) { w.Notify("Finish the fight before you recall"); return; }
            var t = Book.Get(key);
            if (t == null) return;
            TownProgress.Recalled();
            w.Me.ShowEmote("🪨");
            w.Notify($"🪨 Recalling to {t.Name}…");
            w.StartCoroutine(Later(RecallDelay, () => { if (w) StoneGo(w, key); }));
        }

        /// <summary>Off to a town's lore stone (stoneGo): by train to another town, or a poof across this one.</summary>
        public static void StoneGo(PlazaWorld w, string key)
        {
            var t = Book.Get(key);
            if (t == null) return;
            TownProgress.Touched(key);
            var at = new Vector2Int(t.Stone[0], t.Stone[1]);
            if (key == w.Map.Key) w.PoofTo(at, t.Landmark);
            else Boot.Travel(key, at, null, true);
        }

        // a Paw Express ride (travelTo): off at the town's platform, or back where you were in Pawtopia
        static void Ride(string key)
        {
            var at = TownBook.ArrivalByTrain(key);
            AutoSave.Now("train");
            Boot.Travel(key, at != null ? new Vector2Int(at[0], at[1]) : (Vector2Int?)null, null, true);
        }

        static IEnumerator Later(float seconds, System.Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }

        // ---- rows ----

        // routeRow: here, Travel (only at a stone; elsewhere a "🪨 Stone" chip), Walk toward a town not yet visited, or "On foot";
        // at the station every other town has Ride, and a town you haven't seen says it's a first visit
        static void RouteRows(Sheet s, PlazaWorld w, RouteFrom from)
        {
            foreach (var k in Data.Route)
            {
                var t = Book.Get(k);
                if (t == null) continue;
                bool here = k == w.Map.Key, seen = TownProgress.Seen(k), train = from == RouteFrom.Station;
                string about = t.Genre + " · " + t.Sub;
                var row = Row(s.Card, t.Icon, t.Name, here ? "You are here" : train ? about + (seen ? "" : " · First visit") : seen ? about : "Not visited yet");
                LevelBadge(row, t.Level);
                var walk = here || seen || train ? null : WalkToward(w, k);
                if (here) Chip(row, "Here");
                else if (train) Mini(s, row, "Ride", true, () => Ride(k));
                else if (seen && from == RouteFrom.Stone) Mini(s, row, "Travel", true, () => StoneGo(w, k));
                else if (seen) Chip(row, "Stone", "🪨");
                else if (walk != null) Mini(s, row, "Walk", false, () => w.GoToSpot(walk));
                else Chip(row, "On foot");
            }
        }

        // routeWalk: on the road, its end toward that town; in town, the gate toward it (the site always picked the first gate)
        static TownMap.Spot WalkToward(PlazaWorld w, string key)
        {
            var map = w.Map;
            int j = TownBook.Index(key);
            if (map.Wild != null) return map.Wild.IsRoad ? map.Spots.Find(s => s.Kind == (j >= map.Wild.Link ? "towns" : "home")) : null;
            string side = j > TownBook.Index(map.Key) ? "w" : "e";
            return map.Spots.Find(s => s.Kind == "road" && (s.Side == null || s.Side == side));
        }

        // stoneRows: home and last stone with their Recall buttons (showing the minutes left while it cools down)
        static void RecallRows(Sheet s, PlazaWorld w)
        {
            string home = Book.Get(TownProgress.Home) != null ? TownProgress.Home : null;
            string last = Book.Get(TownProgress.Last) != null && TownProgress.Last != home ? TownProgress.Last : null;
            if (home == null && last == null)
            {
                Row(s.Card, "🪨", "Recall", "Touch a lore stone in any town to link it. Then you can recall to it once an hour.");
                return;
            }
            double left = TownProgress.RecallLeft;
            Section(s.Card, "🪨", "Recall", left > 0 ? "Ready again in " + TownProgress.Minutes(left) : "Ready. One recall per hour");
            foreach (var (key, icon, label) in new[] { (home, "🏠", "Home"), (last, "🪨", "Last stone") })
            {
                if (key == null) continue;
                var t = Book.Get(key);
                var row = Row(s.Card, icon, label + " · " + t.Name, t.Landmark);
                Mini(s, row, left > 0 ? TownProgress.Minutes(left) : "Recall", left <= 0, () => Recall(w, key));
            }
        }

        // ---- building blocks ----

        static Sheet Open(string icon, string title)
        {
            var canvas = UiKit.MakeCanvas("Town sheet", 30);
            var s = Sheet.Create(canvas.transform, title, new Vector2(.5f, .5f), Vector2.zero, Width);
            s.Dim(.35f);
            s.Closed = () => Object.Destroy(canvas.gameObject);
            var head = UiKit.Node("head", s.Card);
            UiKit.Row(head, 12);
            UiKit.Icon(head, icon, 40);
            UiKit.Size(UiKit.Label(head, title, UiKit.TitleSize - 2, Palette.Ink, UiKit.Title), -1, 44, 1);
            UiKit.CloseButton(head, s.Close);
            return s;
        }

        static void Note(Transform card, string text) => UiKit.Label(card, text, UiKit.SmallSize + 1, Palette.InkSoft);

        static void Section(Transform card, string icon, string title, string note)
        {
            UiKit.Rule(card);
            var head = UiKit.Node("section", card);
            UiKit.Row(head, 8);
            UiKit.Icon(head, icon, 24);
            UiKit.Label(head, title, UiKit.BodySize + 1, Palette.Ink, UiKit.Bold);
            Note(card, note);
        }

        // an icon, a bold name over a small line, then whatever the caller adds on the right
        static RectTransform Row(Transform card, string icon, string name, string sub, string subIcon = null)
        {
            var row = UiKit.Node("row", card);
            UiKit.Row(row, 10).childForceExpandWidth = false;
            UiKit.Size(row).minHeight = 52;
            UiKit.Icon(row, icon, RowIcon);
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, name, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            var line = UiKit.Node("sub", words);
            UiKit.Row(line, 4).childForceExpandWidth = false;
            if (subIcon != null) UiKit.Icon(line, subIcon, 16);
            UiKit.Size(UiKit.Label(line, sub, UiKit.SmallSize, Palette.InkSoft), -1, -1, 1);
            return row;
        }

        // the site's lvBadge: green at or under your level, amber within two, rose beyond
        static void LevelBadge(Transform row, int required)
        {
            int lv = RoadSpots.ReaderLevel;
            UiKit.Badge(row, "Lv " + required, required <= lv ? UiKit.LeafInk : required <= lv + 2 ? UiKit.EmberInk : UiKit.RoseInk);
        }

        // a small button; it closes the sheet first, and the first one gets the gamepad's focus
        static void Mini(Sheet s, Transform row, string label, bool main, System.Action act)
        {
            System.Action go = () => { s.Close(); act(); };
            var b = main ? UiKit.Primary(row, label, go, null, MiniHeight) : UiKit.Secondary(row, label, go, null, MiniHeight);
            if (s.First == null) s.First = b;
        }

        // a quiet word where a button would be ("Here", "Home", "🪨 Stone", "On foot")
        static void Chip(Transform row, string text, string icon = null)
        {
            var pill = UiKit.Badge(row, text, Palette.InkSoft);
            if (icon == null) return;
            pill.GetComponent<HorizontalLayoutGroup>().spacing = 4;
            UiKit.Icon(pill.transform, icon, 16).transform.SetAsFirstSibling();
        }
    }
}
