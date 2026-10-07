using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Bag &amp; gear. Left: your buddy wearing its four slots, and its stats (with the change an item would make).
    /// Right: the bag (60) with slot tabs and a sort, and the selected item's card: stats compared with what's worn,
    /// Equip, and Salvage for dust (asks twice). A selects, X salvages, LB/RB switch tabs, B backs out one level
    /// (the item card is its own level on the UiStack). On wide screens the card sits beside the grid; on narrow
    /// ones it covers it.
    /// </summary>
    public sealed class BagScreen : TalesScreen
    {
        const float Pad = 24, HeadHeight = 60, DetailWidth = 400, TileSize = 80, TileGap = 10;
        const string SortPref = "bb.bagSort";
        static readonly string[] Filters = { null, "w", "a", "h", "c" };
        static readonly string[] SortNames = { "Newest", "Rarity", "Power" };

        TalesSave save;
        RectTransform left, right, gridView, detail, emptyNote, leftContent, gridContent, detailContent, detailBack, actions;
        GridLayoutGroup grid;
        ScrollRect gridScroll, detailScroll;
        Text countText, dustText, flashText;
        CanvasGroup flash;
        Button[] filterTabs, sortTabs;
        Button primaryAction, salvageAction;
        readonly Dictionary<string, Button> tiles = new Dictionary<string, Button>();
        int filter, sort, columns = 6;
        float leftWidth, flashAt = -99;
        string selected, pending; // pending: the item to highlight once the screen has its size
        bool docked;

        /// <summary>Opens the bag (or brings it back to the front), selecting the item with this string or seed.</summary>
        public static void Open(string highlight)
        {
            var bag = TalesUi.Find<BagScreen>();
            if (bag == null)
            {
                bag = Create<BagScreen>("Bag", false);
                bag.Build();
                bag.pending = highlight ?? "";
                AutoSave.Now("bag");
                return;
            }
            TalesUi.Find<HeroCard>()?.Close();
            if (highlight != null) bag.Highlight(highlight);
        }

        void Build()
        {
            save = TalesSave.Current;
            sort = Mathf.Clamp(PlayerPrefs.GetInt(SortPref, 0), 0, SortNames.Length - 1);
            BuildHeader();

            var leftPanel = UiKit.Panel(Card, "buddy", Palette.Paper);
            left = leftPanel.rectTransform;
            leftContent = UiKit.ScrollColumn(left, 14, new RectOffset(16, 16, 12, 20), out _);
            ((RectTransform)leftContent.parent).Fill();

            right = UiKit.Node("bag", Card);
            BuildToolbar();
            BuildGrid();
            BuildDetail();
            UiKit.PadHints(Card, ("A", "Select"), ("X", "Salvage"), ("LB/RB", "Tabs"), ("B", "Back"));

            var pill = UiKit.Panel(Card, "flash", Palette.Ink, 20);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(10, 40));
            UiKit.Row(r, 0, new RectOffset(18, 18, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Hug(r, true, false);
            flashText = UiKit.Label(r, "", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold);
            flashText.horizontalOverflow = HorizontalWrapMode.Overflow;
            flash = pill.gameObject.AddComponent<CanvasGroup>();
            flash.alpha = 0;
        }

        void BuildHeader()
        {
            var head = UiKit.Node("header", Card);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = Vector2.one; head.pivot = new Vector2(.5f, 1);
            head.offsetMin = new Vector2(Pad, -Pad - HeadHeight); head.offsetMax = new Vector2(-Pad, -Pad);
            UiKit.Row(head, 14);
            UiKit.Label(head, "Bag & gear", UiKit.TitleSize, Palette.Ink, UiKit.Title).horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Spacer(head, 4, 0);
            UiKit.Icon(head, "🎒", 24);
            countText = UiKit.Label(head, "", UiKit.BodySize, Palette.InkSoft, UiKit.Bold);
            countText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Icon(head, "✨", 24);
            dustText = UiKit.Label(head, "", UiKit.BodySize, Palette.InkSoft, UiKit.Bold);
            dustText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Spacer(head);
            UiKit.Label(head, "Sort", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            sortTabs = new Button[SortNames.Length];
            for (int i = 0; i < SortNames.Length; i++)
            {
                int k = i;
                sortTabs[i] = UiKit.TextButton(head, SortNames[i], null, Palette.Paper, Palette.Ink, () => SetSort(k), 44);
            }
            UiKit.CloseButton(head, Close);
        }

        void BuildToolbar()
        {
            var bar = UiKit.Node("tabs", right);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(.5f, 1);
            bar.offsetMin = new Vector2(0, -52); bar.offsetMax = Vector2.zero;
            UiKit.Row(bar, 8).childForceExpandWidth = false; // the track hugs its tabs (LB and RB at its ends on a gamepad)
            var names = new string[Filters.Length];
            for (int i = 0; i < Filters.Length; i++) names[i] = Filters[i] == null ? "All" : Loot.SlotPlural(Filters[i]);
            filterTabs = UiKit.Tabs(bar, names, SetFilter);
        }

        void BuildGrid()
        {
            gridContent = UiKit.ScrollColumn(right, 0, new RectOffset(4, 4, 6, 24), out gridScroll);
            gridView = (RectTransform)gridContent.parent;
            var g = UiKit.Node("grid", gridContent);
            grid = g.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(TileSize, TileSize);
            grid.spacing = new Vector2(TileGap, TileGap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.childAlignment = TextAnchor.UpperLeft;

            emptyNote = UiKit.Node("empty", right);
            UiKit.Column(emptyNote, 10, new RectOffset(40, 40, 0, 0), TextAnchor.MiddleCenter).childForceExpandWidth = false;
        }

        void BuildDetail()
        {
            var panel = UiKit.Panel(right, "item", Palette.Paper);
            detail = panel.rectTransform;
            UiKit.Outline(panel, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);

            detailBack = UiKit.Node("back row", detail);
            detailBack.anchorMin = new Vector2(0, 1); detailBack.anchorMax = Vector2.one; detailBack.pivot = new Vector2(.5f, 1);
            detailBack.offsetMin = new Vector2(16, -60); detailBack.offsetMax = new Vector2(-16, -12);
            UiKit.Row(detailBack, 8).childForceExpandWidth = false;
            UiKit.Secondary(detailBack, "Back to bag", CloseDetail, null, 44);

            detailContent = UiKit.ScrollColumn(detail, 12, new RectOffset(16, 16, 16, 16), out detailScroll);
            actions = UiKit.Node("actions", detail);
            actions.anchorMin = Vector2.zero; actions.anchorMax = new Vector2(1, 0); actions.pivot = new Vector2(.5f, 0);
            actions.offsetMin = new Vector2(16, 16); actions.offsetMax = new Vector2(-16, 16 + UiKit.ButtonHeight);
            UiKit.Row(actions, 10, null, TextAnchor.MiddleRight).childForceExpandWidth = false;
        }

        // ---- layout ----

        protected override void Layout(Vector2 card)
        {
            leftWidth = card.x >= 1200 ? 360 : 300;
            float top = Pad + HeadHeight + 12;
            left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0, 1); left.pivot = new Vector2(0, .5f);
            left.offsetMin = new Vector2(Pad, Pad); left.offsetMax = new Vector2(Pad + leftWidth, -top);
            Stretch(right, Pad * 2 + leftWidth, top, Pad, Pad + 16);

            float rightWidth = card.x - Pad * 3 - leftWidth;
            docked = rightWidth >= 860;
            float gridWidth = docked ? rightWidth - DetailWidth - 16 : rightWidth;
            Stretch(gridView, 0, 64, docked ? DetailWidth + 16 : 0, 0);
            Stretch(emptyNote, 0, 64, docked ? DetailWidth + 16 : 0, 0);
            if (docked) { detail.anchorMin = new Vector2(1, 0); detail.anchorMax = Vector2.one; detail.pivot = new Vector2(1, .5f); detail.offsetMin = new Vector2(-DetailWidth, 0); detail.offsetMax = new Vector2(0, -64); }
            else Stretch(detail, 0, 0, 0, 0);
            columns = Mathf.Max(3, Mathf.FloorToInt((gridWidth - 8 + TileGap) / (TileSize + TileGap)));
            Refresh();
            if (pending == null) return;
            if (pending.Length == 0 || !Highlight(pending)) VirtualCursor.FocusFirst(FirstTile());
            pending = null;
        }

        // stretch a rect inside its parent with these margins (left, top, right, bottom)
        static void Stretch(RectTransform r, float l, float t, float rt, float b)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t);
        }

        // ---- filling it in ----

        void Refresh()
        {
            if (leftWidth <= 0) return; // not laid out yet
            if (selected != null && !save.Bag.Contains(selected) && Loot.WornIn(save, selected) == null) selected = null;
            if (selected == null) UiStack.Remove(detail);
            countText.text = $"{save.Bag.Count} of {Loot.BagSize}";
            dustText.text = $"{save.Dust} dust";
            UiKit.SelectTab(filterTabs, filter);
            UiKit.SelectTab(sortTabs, sort);
            FillBuddy();
            FillGrid();
            FillDetail();
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false); // layout groups skip it before it's gone at the end of the frame
                Destroy(c);
            }
        }

        // the buddy with its four slots, then its stats and what gear adds (with the change the selected item would make)
        void FillBuddy()
        {
            Clear(leftContent);
            float pet = leftWidth - 32 - 2 * TileSize - 16;
            var block = UiKit.Node("wearing", leftContent);
            UiKit.Size(block, -1, Mathf.Max(2 * (TileSize + 24) + 12, pet + 40));
            var portrait = BuddyPortrait.Create(block, pet);
            ((RectTransform)portrait.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, 4), new Vector2(pet, pet));
            portrait.Show(save.Me.Gear);
            Slot(block, "h", new Vector2(0, 1));
            Slot(block, "a", new Vector2(1, 1));
            Slot(block, "w", new Vector2(0, 0));
            Slot(block, "c", new Vector2(1, 0));

            var now = Preview(null, null, false);
            var after = PreviewSelected();
            var name = UiKit.Label(leftContent, TalesUi.PetName, 26, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            var gearPower = UiKit.Node("gear power", leftContent);
            UiKit.Row(gearPower, 6, null, TextAnchor.MiddleCenter);
            UiKit.Icon(gearPower, "⚡", 20);
            UiKit.Label(gearPower, $"{JsMath.Num(now.Hero.Gear.Power)} gear power", UiKit.BodySize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            if (after != null) GearUi.Delta(gearPower, after.Hero.Gear.Power - now.Hero.Gear.Power, "", UiKit.BodySize);

            var c0 = HeroFactory.CardStats(now);
            var c1 = after != null ? HeroFactory.CardStats(after) : c0;
            var stats = UiKit.Node("stats", leftContent);
            var g = stats.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2((leftWidth - 32 - 8) / 2, 60);
            g.spacing = new Vector2(8, 8);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            GearUi.StatTile(stats, "❤️", "Health", c0.hp, c1.hp);
            GearUi.StatTile(stats, "⚔️", "Attack", c0.atk, c1.atk);
            GearUi.StatTile(stats, "🛡️", "Defense", c0.def, c1.def);
            GearUi.StatTile(stats, "💨", "Speed", c0.spd, c1.spd);
            var power = GearUi.StatTile(leftContent, "⚡", "Power in a tale", c0.power, c1.power);
            UiKit.Size(power, -1, 60);

            Extras(now, after);
            var hero = UiKit.Secondary(leftContent, "Hero card", () => TalesUi.OpenHero(), "📜");
            UiKit.Size(hero, -1, UiKit.ButtonHeight);
        }

        // one equipped slot around the buddy: the item's tile (or a ghost of the slot), and its name under it
        void Slot(RectTransform block, string sl, Vector2 corner)
        {
            var holder = UiKit.Node(sl, block).Pin(corner, Vector2.zero, new Vector2(TileSize, TileSize + 24));
            var it = Loot.Equipped(save, sl);
            Button b;
            if (it != null)
            {
                b = GearUi.Tile(holder, it, false, () => Select(it.Id), TileSize);
                if (it.Id == selected) UiKit.Outline(b, Palette.Amber, 12, 3, 4);
            }
            else
            {
                int k = System.Array.IndexOf(Filters, sl);
                b = UiKit.Button(holder, Loot.SlotName(sl), Palette.Cream, () => SetFilter(filter == k ? 0 : k), 12);
                UiKit.Outline(b, Palette.Ink.WithAlpha(.18f), 12, 2);
                var ghost = UiKit.Icon(b.transform, Loot.SlotIcon(sl), 34);
                ghost.color = Color.white.WithAlpha(.4f);
                ghost.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(34, 34));
                var empty = UiKit.Label(b.transform, "Empty", 13, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
                ((RectTransform)empty.transform).Pin(new Vector2(.5f, 0), new Vector2(0, 8), new Vector2(TileSize, 18)).pivot = new Vector2(.5f, 0);
            }
            ((RectTransform)b.transform).Pin(new Vector2(.5f, 1), Vector2.zero, new Vector2(TileSize, TileSize));
            var label = UiKit.Label(holder, Loot.SlotName(sl).ToUpperInvariant(), 13, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
            ((RectTransform)label.transform).Pin(new Vector2(.5f, 0), Vector2.zero, new Vector2(TileSize + 16, 20)).pivot = new Vector2(.5f, 0);
        }

        // what gear (and nature) adds beyond the four stats: crit, dodge, lifesteal… and legendary perks
        void Extras(BattleUnit now, BattleUnit after)
        {
            var h = now.Hero;
            var a = after?.Hero;
            var rows = new (string key, double v, double w)[]
            {
                ("cc", h.Cc, a?.Cc ?? h.Cc), ("cd", h.Cd, a?.Cd ?? h.Cd), ("ls", h.Ls, a?.Ls ?? h.Ls), ("th", h.Th, a?.Th ?? h.Th),
                ("dg", h.Dg, a?.Dg ?? h.Dg), ("rg", h.Rg, a?.Rg ?? h.Rg), ("sh", h.Sh, a?.Sh ?? h.Sh), ("ink", h.GearInk, a?.GearInk ?? h.GearInk),
            };
            var list = UiKit.Node("extras", leftContent);
            UiKit.Column(list, 2);
            foreach (var (key, v, w) in rows)
            {
                if (v == 0 && w == 0) continue;
                var row = UiKit.Node(key, list);
                UiKit.Row(row, 8);
                UiKit.Size(row, -1, 28);
                UiKit.Icon(row, Loot.StatIcon(key), 20);
                UiKit.Size(UiKit.Label(row, Loot.StatName(key), UiKit.SmallSize + 1, Palette.InkSoft), -1, -1, 1);
                UiKit.Label(row, v == 0 ? "—" : Loot.StatText(key, JsMath.Round1(v)), UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                if (w != v) UiKit.Size(GearUi.Delta(row, w - v, key == "ink" ? "" : "%"), 56);
            }
            var perks = new SortedSet<string>(h.Gear.Perks.Keys);
            if (a != null) perks.UnionWith(a.Gear.Perks.Keys);
            foreach (var k in perks)
            {
                double v = h.Gear.Perk(k), w = a?.Gear.Perk(k) ?? v;
                var row = UiKit.Node(k, list);
                UiKit.Row(row, 8);
                UiKit.Size(row, -1, 28);
                UiKit.Icon(row, Loot.PerkIcon(k), 20);
                UiKit.Size(UiKit.Label(row, Loot.PerkName(k), UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold), -1, -1, 1);
                UiKit.Label(row, v == 0 ? "—" : JsMath.Num(v) + "%", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                if (w != v) UiKit.Size(GearUi.Delta(row, w - v, "%"), 56);
            }
            if (list.childCount == 0) UiKit.Label(list, "Gear adds crit, dodge, lifesteal and more. Rare pieces and up roll extra stats.", UiKit.SmallSize, Palette.InkSoft);
        }

        // your buddy as a level-1 hero, optionally trying an item on (or taking one off) for the stat changes
        BattleUnit Preview(string slot, string item, bool change)
        {
            if (!change) return HeroFactory.Build(Buddy.ShownLook, TalesUi.PetName, 1);
            var gear = save.Me.Gear;
            gear.TryGetValue(slot, out var was);
            if (item == null) gear.Remove(slot); else gear[slot] = item;
            try { return HeroFactory.Build(Buddy.ShownLook, TalesUi.PetName, 1); }
            finally { if (was == null) gear.Remove(slot); else gear[slot] = was; }
        }

        BattleUnit PreviewSelected()
        {
            var it = Loot.Get(selected);
            if (it == null) return null;
            string worn = Loot.WornIn(save, it.Id);
            return worn != null ? Preview(worn, null, true) : Preview(it.Slot, it.Id, true);
        }

        List<GearItem> Items()
        {
            var list = new List<GearItem>();
            for (int i = save.Bag.Count - 1; i >= 0; i--) // newest first
            {
                var it = Loot.Get(save.Bag[i]);
                if (it != null && (Filters[filter] == null || it.Slot == Filters[filter])) list.Add(it);
            }
            if (sort == 1) Sorted(list, (a, b) => a.Tier != b.Tier ? b.Tier - a.Tier : a.Ancient != b.Ancient ? (a.Ancient ? -1 : 1) : b.Power.CompareTo(a.Power));
            else if (sort == 2) Sorted(list, (a, b) => b.Power.CompareTo(a.Power));
            return list;
        }

        // a stable sort, so equal items keep their newest-first order
        static void Sorted(List<GearItem> list, System.Comparison<GearItem> by)
        {
            var order = new Dictionary<GearItem, int>();
            for (int i = 0; i < list.Count; i++) order[list[i]] = i;
            list.Sort((a, b) => { int c = by(a, b); return c != 0 ? c : order[a] - order[b]; });
        }

        void FillGrid()
        {
            Clear(grid.transform);
            tiles.Clear();
            grid.constraintCount = columns;
            var items = Items();
            foreach (var it in items)
            {
                string id = it.Id;
                var b = GearUi.Tile(grid.transform, it, save.Fresh.Contains(id), () => Select(id), TileSize);
                if (id == selected) UiKit.Outline(b, Palette.Amber, 12, 3, 4);
                tiles[id] = b;
            }
            if (Filters[filter] == null)
                for (int i = items.Count; i < Loot.BagSize; i++)
                {
                    var cell = UiKit.Panel(grid.transform, "free", Palette.Paper.WithAlpha(.7f), 12);
                    cell.raycastTarget = false;
                    UiKit.Outline(cell, Palette.Ink.WithAlpha(.06f), 12, 1);
                }
            bool none = items.Count == 0;
            UiKit.Show(gridView, !none);
            UiKit.Show(emptyNote, none);
            if (none) FillEmpty();
        }

        void FillEmpty()
        {
            Clear(emptyNote);
            string sl = Filters[filter];
            bool bagEmpty = save.Bag.Count == 0;
            UiKit.Icon(emptyNote, sl == null || bagEmpty ? "🎒" : Loot.SlotIcon(sl), 56);
            UiKit.Label(emptyNote, bagEmpty ? "No gear yet" : $"No {Loot.SlotPlural(sl).ToLowerInvariant()} in your bag", 26, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var body = UiKit.Label(emptyNote, bagEmpty
                ? "Foes on Bramble Road drop it about 1 in 3 fights, and storybook villains always do. The Bramble Stash has something new every day."
                : "Foes on Bramble Road drop gear about 1 in 3 fights. Pick All to see everything you carry.", UiKit.BodySize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            UiKit.Size(body, 460);
        }

        void FillDetail()
        {
            Clear(detailContent);
            Clear(actions);
            primaryAction = salvageAction = null;
            var it = Loot.Get(selected);
            UiKit.Show(detail, docked || it != null);
            UiKit.Show(detailBack, !docked && it != null);
            var view = (RectTransform)detailContent.parent;
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(0, it != null ? 16 + UiKit.ButtonHeight + 8 : 0);
            view.offsetMax = new Vector2(0, !docked && it != null ? -60 : 0);
            detailContent.anchoredPosition = Vector2.zero;
            if (it == null)
            {
                if (!docked) return;
                UiKit.Spacer(detailContent, 120, 0);
                UiKit.Icon(detailContent, "🔍", 48);
                UiKit.Label(detailContent, $"Pick something to see its stats and how it compares with what {TalesUi.PetName} is wearing.", UiKit.BodySize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
                return;
            }

            string worn = Loot.WornIn(save, it.Id);
            GearUi.Band(detailContent, it);
            GearUi.CompareLine(detailContent, save, it);
            GearUi.StatRows(detailContent, it, worn == null ? Loot.Equipped(save, it.Slot) : null);
            if (it.Perk != null) GearUi.PerkBox(detailContent, it.Perk, it.PerkValue);
            var art = Loot.WeaponArt(it);
            if (art.perk != null)
                GearUi.IconText(detailContent, Loot.PerkIcon(art.perk), $"Weapon art: {Loot.PerkName(art.perk)} {JsMath.Num(art.value)}%", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            var look = Json.ParseObject(Buddy.ShownLook);
            var home = TalesData.Current.Genres[TalesData.GenreOfHue(look?.Num("h") ?? 0)];
            if (it.ThemeKey == home.key)
                GearUi.IconText(detailContent, home.icon, $"Home genre: +10% for {TalesUi.PetName}", UiKit.SmallSize + 1, UiKit.LeafInk, UiKit.Bold);
            if (it.Flavor.Length > 0) UiKit.Label(detailContent, $"“{it.Flavor}”", UiKit.BodySize - 1, Palette.InkSoft);
            UiKit.Label(detailContent, $"Roll quality {it.Quality}%", UiKit.SmallSize, Palette.InkSoft);

            if (worn != null) primaryAction = UiKit.Secondary(actions, "Take off", TakeOff);
            else
            {
                salvageAction = UiKit.ConfirmButton(actions, $"Salvage +{it.Dust} dust", "Yes, salvage it", Salvage);
                primaryAction = UiKit.Primary(actions, "Equip", Equip, Loot.SlotIcon(it.Slot));
            }
        }

        // ---- actions ----

        void Select(string id)
        {
            selected = id;
            save.Fresh.Remove(id);
            if (!UiStack.Contains(detail)) UiStack.Push(detail, CloseDetail);
            Refresh();
            detailScroll.verticalNormalizedPosition = 1;
            VirtualCursor.FocusFirst(primaryAction);
        }

        void CloseDetail()
        {
            string was = selected;
            selected = null;
            UiStack.Remove(detail);
            Refresh();
            if (was != null && tiles.TryGetValue(was, out var tile)) VirtualCursor.FocusFirst(tile);
        }

        // selects an item by its string or seed (in the bag or worn) and scrolls to it; false when it isn't there (it became dust)
        bool Highlight(string id)
        {
            string match = null;
            foreach (var s in save.Bag) if (s == id || Loot.Get(s)?.Seed == id) { match = s; break; }
            if (match == null && Loot.WornIn(save, id) != null) match = id;
            if (match == null) return false;
            filter = 0;
            Select(match);
            if (tiles.TryGetValue(match, out var tile)) ScrollTo((RectTransform)tile.transform);
            return true;
        }

        void ScrollTo(RectTransform tile)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            float view = gridView.rect.height, content = gridContent.rect.height;
            float y = -tile.anchoredPosition.y - view / 2 + TileSize / 2;
            gridContent.anchoredPosition = new Vector2(0, Mathf.Clamp(y, 0, Mathf.Max(0, content - view)));
        }

        void Equip()
        {
            var it = Loot.Get(selected);
            if (it == null || !Loot.Equip(save, it.Id)) return;
            Sound.Play("happy");
            Flash($"{it.Name} equipped");
            AutoSave.Now("equip");
            Refresh();
            VirtualCursor.FocusFirst(primaryAction);
        }

        void TakeOff()
        {
            string slot = Loot.WornIn(save, selected);
            if (slot == null) return;
            if (!Loot.Unequip(save, slot)) { Flash("Your bag is full. Salvage something first."); return; }
            Sound.Play("pop");
            Flash("Back in your bag");
            AutoSave.Now("equip");
            Refresh();
        }

        // the salvage button's second press (or X twice)
        void Salvage()
        {
            int dust = Loot.Salvage(save, selected);
            if (dust == 0) return;
            Sound.Play("coin");
            Flash($"+{dust} page dust");
            AutoSave.Now("salvage");
            selected = null;
            Refresh();
            VirtualCursor.FocusFirst(FirstTile());
        }

        void SetFilter(int i)
        {
            filter = (i + Filters.Length) % Filters.Length;
            if (!docked) selected = null; // the card covers the grid: show the new tab
            Refresh();
            gridContent.anchoredPosition = Vector2.zero;
        }

        void SetSort(int i)
        {
            sort = i;
            PlayerPrefs.SetInt(SortPref, i);
            Refresh();
        }

        void Flash(string text)
        {
            flashText.text = text;
            flashAt = Time.unscaledTime;
        }

        Selectable FirstTile()
        {
            foreach (var b in tiles.Values) return b;
            return filterTabs[filter];
        }

        protected override void Update()
        {
            base.Update();
            flash.alpha = Mathf.Clamp01(2.4f - (Time.unscaledTime - flashAt) * 1.2f);
            var top = UiStack.Top;
            if (!ReferenceEquals(top, this) && !ReferenceEquals(top, detail)) return;
            if (TalesUi.Pressed(PlazaAction.ZoomIn)) SetFilter(filter + 1);       // RB (or +)
            else if (TalesUi.Pressed(PlazaAction.ZoomOut)) SetFilter(filter - 1); // LB (or -)
            else if (salvageAction != null && PlazaInput.UsingGamepad && TalesUi.Pressed(PlazaAction.Emotes)) SalvageWithX();
            else if (TalesUi.Pressed(PlazaAction.Bag)) Close();
            else if (TalesUi.Pressed(PlazaAction.Hero)) TalesUi.OpenHero();
        }

        // X arms the salvage button and puts the cursor on it, so a second X (or A) confirms
        void SalvageWithX()
        {
            salvageAction.onClick.Invoke();
            VirtualCursor.FocusFirst(salvageAction);
        }

        protected override void OnClosed()
        {
            UiStack.Remove(detail);
            save.Fresh.Clear(); // like the site: closing the bag clears the "new" dots
            save.Touch();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            UiStack.Remove(detail);
        }
    }
}
