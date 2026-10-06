using System;
using BookBuddies.Economy;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// A shop: its name and the keeper's line, your coins (counting up and down as they change), tabs (LB and RB on a
    /// gamepad) and a scrolling page of item cards. Whoever opens it says what each tab shows (see TownShop); a tab
    /// fills itself with Section, Grid and Item, and the page refills whenever the wallet changes.
    /// </summary>
    public sealed class ShopScreen : TalesScreen
    {
        const float Pad = 24, HeadHeight = 56, KeeperHeight = 40, TabsHeight = 52, CardWidth = 240, CardHeight = 290, Gap = 14;
        /// <summary>The size of an item card's picture.</summary>
        public const float ArtSize = 84;

        /// <summary>A tab: its name and what fills it.</summary>
        public sealed class Tab
        {
            public string Name;
            public Action<ShopScreen> Fill;
        }

        Tab[] tabs;
        Button[] tabButtons;
        int tab;
        RectTransform content;
        FlashPill flash;
        float width;

        /// <summary>Where a tab adds its rows.</summary>
        public RectTransform Content => content;

        /// <summary>Opens a shop on its first tab. "keeper" is the keeper's line (rich text), or null.</summary>
        public static ShopScreen Open(string icon, string title, string keeper, params Tab[] tabs)
        {
            var s = Create<ShopScreen>(title, false);
            s.MaxSize = new Vector2(1240, 820);
            s.tabs = tabs;
            s.Build(icon, title, keeper);
            return s;
        }

        void Build(string icon, string title, string keeper)
        {
            var top = UiKit.Node("top", Card);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = Vector2.one; top.pivot = new Vector2(.5f, 1);
            UiKit.Column(top, 8);

            var head = UiKit.Node("header", top);
            UiKit.Row(head, 12);
            UiKit.Size(head, -1, HeadHeight);
            UiKit.Icon(head, icon, 40);
            UiKit.Size(UiKit.Label(head, title, UiKit.TitleSize, Palette.Ink, UiKit.Title), -1, 44, 1);
            CoinPill.Create(head, null);
            UiKit.CloseButton(head, Close);

            if (keeper != null)
            {
                var line = UiKit.Label(top, keeper, UiKit.BodySize, Palette.InkSoft);
                UiKit.Size(line, -1, KeeperHeight);
            }
            if (tabs.Length > 1)
            {
                var bar = UiKit.Node("tabs", top);
                UiKit.Row(bar, 8).childForceExpandWidth = false;
                UiKit.Size(bar, -1, TabsHeight);
                var names = Array.ConvertAll(tabs, t => t.Name);
                tabButtons = UiKit.Tabs(bar, names, Show);
            }
            float topHeight = HeadHeight + (keeper != null ? KeeperHeight + 8 : 0) + (tabs.Length > 1 ? TabsHeight + 8 : 0);
            top.offsetMin = new Vector2(Pad, -Pad - topHeight); top.offsetMax = new Vector2(-Pad, -Pad);

            var body = UiKit.Node("page", Card);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(Pad, Pad); body.offsetMax = new Vector2(-Pad, -Pad - topHeight - 10);
            content = UiKit.ScrollColumn(body, 12, new RectOffset(2, 2, 4, 64), out _);
            ((RectTransform)content.parent).Fill();

            flash = FlashPill.Create(Card);
            UiKit.PadHints(Card, tabs.Length > 1 ? new[] { ("A", "Buy"), ("LB/RB", "Tabs"), ("B", "Back") } : new[] { ("A", "Select"), ("B", "Back") });
            Wallet.Changed += Refresh;
        }

        protected override void Layout(Vector2 card)
        {
            bool first = width <= 0;
            width = card.x - Pad * 2 - 4;
            if (first) Show(0);
            else Refresh();
        }

        /// <summary>Shows a tab (wrapping around), from the top.</summary>
        public void Show(int index)
        {
            tab = (index % tabs.Length + tabs.Length) % tabs.Length;
            if (tabButtons != null) UiKit.SelectTab(tabButtons, tab);
            Refresh();
            content.anchoredPosition = Vector2.zero;
            VirtualCursor.FocusFirst(FirstButton());
        }

        /// <summary>Fills the tab again (after a purchase, or when coins change), keeping the scroll.</summary>
        public void Refresh()
        {
            if (width <= 0 || !content) return;
            for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
            tabs[tab].Fill(this);
        }

        /// <summary>A line in the message pill at the bottom.</summary>
        public void Flash(string line) => flash.Show(line);

        /// <summary>A heading and an optional note under it.</summary>
        public void Section(string title, string note = null)
        {
            if (content.childCount > 0) UiKit.Spacer(content, 6, 0);
            UiKit.Label(content, title, 26, Palette.Ink, UiKit.Title);
            if (note != null) UiKit.Label(content, note, UiKit.SmallSize + 1, Palette.InkSoft);
        }

        /// <summary>A grid of item cards as wide as the page.</summary>
        public RectTransform Grid()
        {
            var r = UiKit.Node("grid", content);
            var g = r.gameObject.AddComponent<GridLayoutGroup>();
            int columns = Mathf.Max(2, Mathf.FloorToInt((width + Gap) / (CardWidth + Gap)));
            g.cellSize = new Vector2((width - Gap * (columns - 1)) / columns, CardHeight);
            g.spacing = new Vector2(Gap, Gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            return r;
        }

        /// <summary>
        /// An item card in a grid: an emoji on a round plate (or the caller's "art", ArtSize square), the name, a soft
        /// line under it, and a foot for its price button or tag (returned).
        /// </summary>
        public RectTransform Item(Transform grid, string emoji, string name, string sub, Color? nameColor = null, Func<Transform, Component> art = null)
        {
            var card = UiKit.Panel(grid, name, Palette.Paper, 16);
            card.raycastTarget = false;
            UiKit.Outline(card, Palette.Ink.WithAlpha(.08f), 16, 1);
            UiKit.Column(card.rectTransform, 6, new RectOffset(14, 14, 16, 14), TextAnchor.UpperCenter);
            var holder = UiKit.Node("art", card.transform);
            UiKit.Size(holder, -1, 88);
            var shown = art != null ? art(holder) : Plate(holder, emoji);
            ((RectTransform)shown.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(ArtSize, ArtSize));
            UiKit.Label(card.transform, name, UiKit.BodySize, nameColor ?? Palette.Ink, UiKit.Bold, TextAnchor.UpperCenter);
            if (!string.IsNullOrEmpty(sub)) UiKit.Label(card.transform, sub, UiKit.SmallSize, Palette.InkSoft, null, TextAnchor.UpperCenter);
            UiKit.Size(UiKit.Node("space", card.transform)).flexibleHeight = 1;
            var foot = UiKit.Node("foot", card.transform);
            UiKit.Row(foot, 8, null, TextAnchor.MiddleCenter);
            UiKit.Size(foot, -1, 44);
            return foot;
        }

        static Image Plate(Transform holder, string emoji)
        {
            var plate = UiKit.Panel(holder, "plate", Palette.Cream, 42);
            plate.raycastTarget = false;
            UiKit.Icon(plate.transform, emoji, 56).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(56, 56));
            return plate;
        }

        /// <summary>A quiet note with an emoji, for an empty tab.</summary>
        public void Empty(string emoji, string text)
        {
            var row = UiKit.Node("empty", content);
            UiKit.Column(row, 10, new RectOffset(0, 0, 40, 0), TextAnchor.MiddleCenter).childForceExpandWidth = false;
            UiKit.Icon(row, emoji, 64);
            UiKit.Label(row, text, UiKit.BodySize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
        }

        Selectable FirstButton()
        {
            foreach (var b in content.GetComponentsInChildren<Button>()) if (b.interactable) return b;
            return tabButtons != null ? tabButtons[tab] : null;
        }

        protected override void Update()
        {
            base.Update();
            if (!ReferenceEquals(UiStack.Top, this) || tabs.Length < 2) return;
            if (TalesUi.Pressed(PlazaAction.ZoomIn)) Show(tab + 1);       // RB (or +)
            else if (TalesUi.Pressed(PlazaAction.ZoomOut)) Show(tab - 1); // LB (or -)
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Wallet.Changed -= Refresh;
        }
    }
}
