using System;
using System.Collections.Generic;
using BookBuddies.Economy;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The hero card, which is also the Tales shop. The left side is always your buddy: gear, class and evolution, Pet Lv,
    /// renown and the next milestone, and its stats. The tabs on the right: Hero (equipped moves, tactics and what shapes
    /// it), Moves (the compendium: moves it owns, the next few sealed, the rest hidden until it grows), Class (the classes
    /// it can be, each keeping its own progress, and the ones close to opening) and Library (upgrades for every fight).
    /// LB and RB change tabs; C or B closes it; I opens the bag.
    /// </summary>
    public sealed partial class HeroCard : TalesScreen
    {
        const float Pad = 24, HeadHeight = 56, TabsHeight = 52;
        static readonly string[] TabKeys = { "hero", "moves", "class", "library" };
        static readonly string[] TabNames = { "Hero", "Moves", "Class", "Library" };
        static readonly string[] LaneNames = { "Left", "Middle", "Right" };
        static readonly string[] LaneKeys = { "l", null, "r" };

        TalesSave save;
        RectTransform left, leftContent, right, rightContent;
        Button[] tabs;
        Image movesDot, classDot; // something new on that tab
        FlashPill flash;
        int tab;
        float leftWidth;
        bool wasTop = true;
        readonly HashSet<string> shownNew = new HashSet<string>(), flipped = new HashSet<string>();

        /// <summary>
        /// Opens the hero card on a tab ("hero", "moves", "class" or "library"; null keeps it as it is), or brings it back
        /// when the bag was opened over it.
        /// </summary>
        public static void Open(string tab = null)
        {
            var open = TalesUi.Find<HeroCard>();
            if (open != null)
            {
                TalesUi.Find<BagScreen>()?.Close();
                if (tab != null) open.Show(Array.IndexOf(TabKeys, tab));
                return;
            }
            var c = Create<HeroCard>("Hero card", false);
            c.MaxSize = new Vector2(1240, 820);
            c.tab = Math.Max(0, Array.IndexOf(TabKeys, tab));
            c.Build();
        }

        void Build()
        {
            save = TalesSave.Current;
            HeroFactory.CheckClassUnlocks(save); // the site checks when its Tales shop opens: gear found since may have opened one
            var head = UiKit.Node("header", Card);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = Vector2.one; head.pivot = new Vector2(.5f, 1);
            head.offsetMin = new Vector2(Pad, -Pad - HeadHeight); head.offsetMax = new Vector2(-Pad, -Pad);
            UiKit.Row(head, 12);
            UiKit.Size(UiKit.Label(head, TalesUi.PetName, UiKit.TitleSize, Palette.Ink, UiKit.Title), -1, 44, 1);
            CoinPill.Create(head, null);
            UiKit.Secondary(head, "Bag & gear", () => TalesUi.OpenBag(), "🎒", 44);
            UiKit.CloseButton(head, Close);

            left = UiKit.Panel(Card, "buddy", Palette.Paper).rectTransform;
            leftContent = UiKit.ScrollColumn(left, 12, new RectOffset(18, 18, 14, 20), out _);
            ((RectTransform)leftContent.parent).Fill();
            right = UiKit.Node("details", Card);
            var bar = UiKit.Node("tabs", right);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(.5f, 1);
            bar.offsetMin = new Vector2(0, -TabsHeight); bar.offsetMax = Vector2.zero;
            UiKit.Row(bar, 8).childForceExpandWidth = false;
            tabs = UiKit.Tabs(bar, TabNames, Show);
            movesDot = ShopKit.Dot(tabs[1].transform);
            classDot = ShopKit.Dot(tabs[2].transform);
            rightContent = UiKit.ScrollColumn(right, 10, new RectOffset(4, 12, 4, 64), out _);
            var view = (RectTransform)rightContent.parent;
            view.Fill();
            view.offsetMax = new Vector2(0, -TabsHeight - 10);
            flash = FlashPill.Create(Card);
            UiKit.PadHints(Card, ("A", "Select"), ("LB/RB", "Tabs"), ("B", "Back"));
            Wallet.Changed += Fill;
        }

        protected override void Layout(Vector2 card)
        {
            leftWidth = card.x >= 1000 ? 380 : 320;
            float top = Pad + HeadHeight + 12;
            left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0, 1); left.pivot = new Vector2(0, .5f);
            left.offsetMin = new Vector2(Pad, Pad); left.offsetMax = new Vector2(Pad + leftWidth, -top);
            right.anchorMin = Vector2.zero; right.anchorMax = Vector2.one; right.pivot = new Vector2(.5f, .5f);
            right.offsetMin = new Vector2(Pad * 2 + leftWidth, Pad); right.offsetMax = new Vector2(-Pad, -top);
            bool first = leftContent.childCount == 0;
            if (first) Show(tab);
            else Fill();
        }

        /// <summary>Shows a tab (wrapping around), from the top. What was new on the last tab counts as seen.</summary>
        void Show(int index)
        {
            Seen();
            tab = (index % TabKeys.Length + TabKeys.Length) % TabKeys.Length;
            UiKit.SelectTab(tabs, tab);
            Fill();
            rightContent.anchoredPosition = Vector2.zero;
            Selectable first = null;
            foreach (var b in rightContent.GetComponentsInChildren<Button>()) if (b.interactable) { first = b; break; }
            VirtualCursor.FocusFirst(first ? first : tabs[tab]);
        }

        void Fill()
        {
            if (leftWidth <= 0 || !this) return; // not laid out yet, or closed
            Clear(leftContent);
            Clear(rightContent);
            var hero = HeroFactory.Build(Buddy.ShownLook, TalesUi.PetName, 1);
            FillLeft(hero);
            switch (TabKeys[tab])
            {
                case "moves": FillCompendium(hero); break;
                case "class": FillClasses(hero); break;
                case "library": FillLibrary(); break;
                default:
                    FillMoves(hero);
                    FillTactics();
                    FillMakeup(hero);
                    break;
            }
            UiKit.Show(movesDot, AnyNew($"move:{hero.Hero.Cls}:"));
            UiKit.Show(classDot, AnyNew("cls:"));
        }

        bool AnyNew(string prefix)
        {
            foreach (var k in save.Unseen) if (k.StartsWith(prefix)) return true;
            return false;
        }

        // what was shown as new on this tab is new no more
        void Seen()
        {
            if (shownNew.Count == 0) return;
            save.Unseen.ExceptWith(shownNew);
            shownNew.Clear();
            save.Touch();
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
        }

        void FillLeft(BattleUnit hero)
        {
            var h = hero.Hero;
            var d = TalesData.Current;
            var C = d.Class(h.Cls);
            var renown = HeroFactory.Renown(save.Me.Rxp);
            int petLv = renown.lvl + 1, evo = HeroFactory.Evo(renown.lvl);

            float size = Mathf.Min(220, leftWidth - 80);
            var holder = UiKit.Node("portrait", leftContent);
            UiKit.Size(holder, -1, size + 8);
            var portrait = BuddyPortrait.Create(holder, size);
            ((RectTransform)portrait.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size));
            portrait.Show(save.Me.Gear);

            var who = UiKit.Node("class", leftContent);
            UiKit.Row(who, 8, null, TextAnchor.MiddleCenter);
            UiKit.Icon(who, C.Icon, 26);
            UiKit.Label(who, HeroFactory.EvoName(h.Cls, petLv), UiKit.HeadingSize, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            var genre = d.Genres[((hero.Gen % 6) + 6) % 6];
            var tags = UiKit.Node("tags", leftContent);
            UiKit.Row(tags, 8, null, TextAnchor.MiddleCenter);
            UiKit.Badge(tags, $"Pet Lv {petLv}", UiKit.EmberInk);
            UiKit.Badge(tags, genre.label, UiKit.SkyInk);
            UiKit.Badge(tags, h.NatureName, UiKit.LeafInk);

            // renown: how far to the next Pet Lv
            var bar = UiKit.Node("renown", leftContent);
            UiKit.Column(bar, 4);
            var line = UiKit.Node("line", bar);
            UiKit.Row(line, 6);
            GearUi.IconText(line, "🌟", $"Pet Lv {petLv}", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            UiKit.Spacer(line);
            UiKit.Label(line, $"{JsMath.Num(renown.xp)} / {JsMath.Num(renown.need)} XP", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            var track = UiKit.Panel(bar, "track", Palette.Ink.WithAlpha(.1f), 6);
            UiKit.Size(track, -1, 12);
            var fill = UiKit.Panel(track.transform, "fill", Palette.Amber, 6).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)(renown.xp / renown.need)), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            int bonus = JsMath.RoundI((HeroFactory.RenownBonus(renown.lvl) + .05 * evo) * 100);
            UiKit.Label(bar, $"+{bonus}% stats from renown and evolutions", UiKit.SmallSize, Palette.InkSoft);
            Milestone();

            var c = HeroFactory.CardStats(hero);
            var stats = UiKit.Node("stats", leftContent);
            var g = stats.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2((leftWidth - 36 - 8) / 2, 60);
            g.spacing = new Vector2(8, 8);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            GearUi.StatTile(stats, "❤️", "Health", c.hp, c.hp);
            GearUi.StatTile(stats, "⚔️", "Attack", c.atk, c.atk);
            GearUi.StatTile(stats, "🛡️", "Defense", c.def, c.def);
            GearUi.StatTile(stats, "💨", "Speed", c.spd, c.spd);
            UiKit.Size(GearUi.StatTile(leftContent, "⚡", "Power in a tale", c.power, c.power), -1, 60);

            // the four slots at a glance; any of them opens the bag
            var strip = UiKit.Node("gear", leftContent);
            UiKit.Row(strip, 8, null, TextAnchor.MiddleCenter);
            foreach (var sl in LootData.SlotKeys)
            {
                var it = Loot.Equipped(save, sl);
                Button b;
                if (it != null) b = GearUi.Tile(strip, it, false, () => TalesUi.OpenBag(it.Id), 64);
                else
                {
                    b = UiKit.Button(strip, Loot.SlotName(sl), Palette.Cream, () => TalesUi.OpenBag(), 12);
                    UiKit.Size(b, 64, 64);
                    UiKit.Outline(b, Palette.Ink.WithAlpha(.18f), 12, 2);
                    var ghost = UiKit.Icon(b.transform, Loot.SlotIcon(sl), 30);
                    ghost.color = Color.white.WithAlpha(.4f);
                    ghost.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(30, 30));
                }
            }
            GearUi.IconText(leftContent, "⚡", $"{JsMath.Num(h.Gear.Power)} gear power", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold)
                .GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        }

        void Section(string name, string sub = null)
        {
            UiKit.Spacer(rightContent, 6, 0);
            UiKit.Label(rightContent, name, 26, Palette.Ink, UiKit.Title);
            if (sub != null) UiKit.Label(rightContent, sub, UiKit.SmallSize, Palette.InkSoft);
        }

        // one line in a section: an icon on a paper square, a bold title, a soft line under it, and anything on the right
        RectTransform Row(string emoji, string head, string sub)
        {
            var row = UiKit.Node(head, rightContent);
            UiKit.Row(row, 14, new RectOffset(0, 0, 4, 4));
            var plate = UiKit.Panel(row, "icon", Palette.Paper, 12);
            plate.raycastTarget = false;
            UiKit.Size(plate, 52, 52);
            UiKit.Icon(plate.transform, emoji, 32).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(32, 32));
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, head, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            if (!string.IsNullOrEmpty(sub)) UiKit.Label(words, sub, UiKit.SmallSize, Palette.InkSoft);
            return row;
        }

        // the Hero tab: the moves it fights with now
        void FillMoves(BattleUnit hero)
        {
            var h = hero.Hero;
            int slots = HeroFactory.Slots(h.Rn);
            Section("Moves", h.Rn < 12 ? $"{h.Kit.Count - 2} of {slots} move slots · next slot at Pet Lv {(h.Rn < 5 ? 6 : 13)}" : null);
            foreach (var k in h.Kit)
            {
                var m = HeroFactory.Move(h.Cls, k);
                if (m == null) continue;
                var row = Row(HeroFactory.MoveIcon(hero, k), HeroFactory.MoveName(hero, k), HeroFactory.MoveDesc(hero, k));
                UiKit.Badge(row, m.Ult ? "Ultimate" : m.Cost == 0 ? "Free" : $"{m.Cost} ink", m.Ult ? UiKit.EmberInk : UiKit.SkyInk);
            }
        }

        void FillTactics()
        {
            Section("Tactics");
            var ult = Row("💥", "Auto ultimate", "Unleash the ultimate as soon as the ink is full.");
            UiKit.Switch(ult, save.Me.AutoUlt, on => { save.Me.AutoUlt = on; save.Touch(); });
            var lane = Row("🧭", "Lane", $"Where {TalesUi.PetName} stands when a fight starts.");
            int at = System.Math.Max(0, System.Array.IndexOf(LaneKeys, save.Me.Lane));
            UiKit.Choice(lane, LaneNames, at, i => { save.Me.Lane = LaneKeys[i]; save.Touch(); }, 220);
        }

        // nature, spark, home genre, class base and loot luck: everything that adds up in a tale
        void FillMakeup(BattleUnit hero)
        {
            var h = hero.Hero;
            var d = TalesData.Current;
            var nat = HeroFactory.Nature(save.Personality);
            Section($"What makes {TalesUi.PetName}");
            Row(HeroFactory.PersonalityIcon(nat.key), $"{nat.name} nature",
                $"{Loot.StatName(nat.up)} +8% · {Loot.StatName(nat.down)} −4% · {Quirk(nat.quirk, nat.quirkValue)}");

            uint spark = JsMath.Hash("spark:" + h.PetKey);
            var sparkRow = Row("✦", "Spark", "Fixed since it hatched. Each star is +1%.");
            var stars = UiKit.Node("stars", sparkRow);
            UiKit.Column(stars, 2, null, TextAnchor.MiddleRight);
            var keys = new[] { ("hp", spark % 6), ("atk", (spark >> 3) % 6), ("def", (spark >> 6) % 6), ("spd", (spark >> 9) % 6) };
            foreach (var (k, n) in keys) Stars(stars, k, (int)n);

            var genre = d.Genres[((hero.Gen % 6) + 6) % 6];
            Row(genre.icon, $"{genre.label} at heart", $"{genre.label} gear gets +10% on {TalesUi.PetName}.");
            var C = d.Class(h.Cls);
            Row(C.Icon, C.Name, $"Class base · Health {C.Hp} · Attack {C.Atk} · Defense {C.Def} · Speed {C.Spd}");
            bool shiny = Json.ParseObject(Buddy.ShownLook)?.Truthy("sy") ?? false;
            Row("🍀", $"Loot luck +{JsMath.Num(Loot.Luck(save, shiny))}%", "From nature, shiny and gear. Better odds of rare drops.");
        }

        static void Stars(Transform parent, string stat, int n)
        {
            var row = UiKit.Node(stat, parent);
            UiKit.Row(row, 3, null, TextAnchor.MiddleRight);
            UiKit.Icon(row, Loot.StatIcon(stat), 16);
            for (int i = 0; i < 5; i++)
            {
                var dot = UiKit.Panel(row, "star", i < n ? Palette.Amber : Palette.Ink.WithAlpha(.12f), 5);
                dot.raycastTarget = false;
                UiKit.Size(dot, 10, 10);
            }
        }

        // LT_QUIRK: the nature's small gear-like bonus, in words
        static string Quirk(string k, double v)
        {
            string n = JsMath.Num(v);
            switch (k)
            {
                case "rg": return $"Heals {n}% each turn";
                case "cd": return $"+{n}% critical damage";
                case "ink": return $"+{n} starting ink";
                case "luck": return $"+{n}% loot luck";
                case "cc": return $"+{n}% critical chance";
                case "sh": return $"Starts battles with a {n}% shield";
                case "dg": return $"+{n}% dodge";
                case "ls": return $"{n}% lifesteal";
                case "th": return $"{n}% thorns";
                default: return "";
            }
        }

        protected override void Update()
        {
            base.Update();
            bool top = ReferenceEquals(UiStack.Top, this);
            if (top && !wasTop) Fill(); // back from the bag: the gear may have changed
            wasTop = top;
            if (!top) return;
            if (TalesUi.Pressed(PlazaAction.ZoomIn)) Show(tab + 1);       // RB (or +)
            else if (TalesUi.Pressed(PlazaAction.ZoomOut)) Show(tab - 1); // LB (or -)
            else if (TalesUi.Pressed(PlazaAction.Hero)) Close();
            else if (TalesUi.Pressed(PlazaAction.Bag)) TalesUi.OpenBag();
        }

        protected override void OnClosed() => Seen();

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Wallet.Changed -= Fill;
        }
    }
}
