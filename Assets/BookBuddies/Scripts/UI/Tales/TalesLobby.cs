using System;
using System.Collections.Generic;
using System.Globalization;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The Tales of Pages library (lobby.md §1.2), opened from Pawtopia's Tale Hall, the road's trailheads and the menu.
    /// A storybook banner, your buddy on the left (gear, class, Pet Lv, stats, Hero & moves, Bag & gear), and on the
    /// right the Small read (start one, or continue the one waiting; close it to start over), the daily tale and the shelf
    /// of kept storybooks. Narrow windows stack it into one column.
    /// </summary>
    public sealed class TalesLobby : TalesScreen
    {
        const float Pad = 24, BannerHeight = 128, Narrow = 900;

        RectTransform banner, left, leftContent, right, rightContent;
        Text countdown; // the open daily sheet's "A new daily tale in …"
        float leftWidth = -1;
        bool built;

        /// <summary>Opens the library (or brings it to the front).</summary>
        public static void Open()
        {
            if (TalesUi.Find<TalesLobby>() != null) return;
            var l = Create<TalesLobby>("Tales library", false);
            l.MaxSize = new Vector2(1360, 880);
            l.Build();
        }

        void Build()
        {
            banner = Banner(Card);
            left = UiKit.Panel(Card, "buddy", Palette.Paper).rectTransform;
            leftContent = UiKit.ScrollColumn(left, 12, new RectOffset(18, 18, 16, 20), out _);
            ((RectTransform)leftContent.parent).Fill();
            right = UiKit.Node("tales", Card);
            rightContent = UiKit.ScrollColumn(right, 14, new RectOffset(4, 12, 0, 64), out _);
            ((RectTransform)rightContent.parent).Fill();
            UiKit.PadHints(Card, ("A", "Select"), ("B", "Back"));
            built = true;
        }

        protected override void Layout(Vector2 card)
        {
            bool narrow = card.x < Narrow;
            float lw = narrow ? 0 : card.x >= 1200 ? 360 : 320;
            banner.offsetMin = new Vector2(Pad, -Pad - BannerHeight); banner.offsetMax = new Vector2(-Pad, -Pad);
            float top = Pad + BannerHeight + 16;
            left.gameObject.SetActive(!narrow);
            left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0, 1); left.pivot = new Vector2(0, .5f);
            left.offsetMin = new Vector2(Pad, Pad); left.offsetMax = new Vector2(Pad + lw, -top);
            right.anchorMin = Vector2.zero; right.anchorMax = Vector2.one;
            right.offsetMin = new Vector2(narrow ? Pad : Pad * 2 + lw, Pad); right.offsetMax = new Vector2(-Pad, -top);
            if (lw == leftWidth) return;
            leftWidth = lw;
            Fill();
        }

        void Fill()
        {
            if (!built || leftWidth < 0) return;
            Clear(leftContent);
            Clear(rightContent);
            bool hatched = Buddy.Hatched;
            if (leftWidth > 0) Hero(leftContent, hatched, Mathf.Min(200, leftWidth - 120));
            else Hero(rightContent, hatched, 110);
            var go = SmallRead(hatched);
            DailyCard();
            Shelf();
            VirtualCursor.FocusFirst(go);
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
        }

        // ---- the banner ----

        // a storybook cover across the top: the open book, the name and what a tale is, with a few sparkles
        static RectTransform Banner(RectTransform card)
        {
            var band = UiKit.Panel(card, "banner", Color.white, 16);
            band.raycastTarget = false;
            var r = band.rectTransform;
            r.anchorMin = new Vector2(0, 1); r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, 1);
            band.gameObject.AddComponent<Mask>();
            var c = TalesData.Current.Covers[0];
            var art = UiKit.Node("gradient", r).Fill().gameObject.AddComponent<Image>();
            art.sprite = StorybookPage.Linear(new[] { Palette.Hex(c.from), Palette.Hex(c.to) }, new[] { 0f, 1f }, 110);
            art.raycastTarget = false;
            var glow = UiKit.Node("glow", r).Pin(new Vector2(.12f, .5f), Vector2.zero, new Vector2(320, 320)).gameObject.AddComponent<Image>();
            glow.sprite = UiKit.Glow; glow.color = Palette.Hex("#ffe7a8").WithAlpha(.45f); glow.raycastTarget = false;
            var rnd = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                var s = UiKit.Node("sparkle", r).Pin(new Vector2(.35f + (float)rnd.NextDouble() * .62f, .15f + (float)rnd.NextDouble() * .7f), Vector2.zero, Vector2.one * (8 + (float)rnd.NextDouble() * 14)).gameObject.AddComponent<Image>();
                s.sprite = StorybookPage.Star; s.color = Palette.Hex("#fff8d0").WithAlpha(.35f + (float)rnd.NextDouble() * .4f); s.raycastTarget = false;
            }
            var row = UiKit.Node("words", r).Fill();
            UiKit.Row(row, 18, new RectOffset(28, 20, 0, 0));
            var book = UiKit.Icon(row, "📖", 76);
            book.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .3f);
            var words = UiKit.Node("text", row);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            var cream = Palette.Hex("#fff8ea");
            UiKit.Label(words, "Tales of Pages", UiKit.TitleSize + 10, cream, UiKit.Title).gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .35f);
            UiKit.Label(words, "Open a book with no last page. Your pet adventures through four lands, and every tale becomes a storybook you keep.", UiKit.BodySize, cream.WithAlpha(.88f));
            return r;
        }

        // ---- your buddy ----

        void Hero(RectTransform col, bool hatched, float size)
        {
            if (!hatched)
            {
                var egg = UiKit.Node("egg", col);
                UiKit.Size(egg, -1, 96);
                UiKit.Icon(egg, "🥚", 80).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 80));
                UiKit.Label(col, "Eggs stay home on adventures.", UiKit.HeadingSize, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
                UiKit.Label(col, "Log a little reading to hatch your egg, then bring your pet here.", UiKit.BodySize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
                return;
            }
            var save = TalesSave.Current;
            var hero = HeroFactory.Build(Buddy.ShownLook, TalesUi.PetName, 1);
            var h = hero.Hero;
            var C = TalesData.Current.Class(h.Cls);
            var renown = HeroFactory.Renown(save.Me.Rxp);
            int petLv = renown.lvl + 1;

            var holder = UiKit.Node("portrait", col);
            UiKit.Size(holder, -1, size + 8);
            var portrait = BuddyPortrait.Create(holder, size);
            ((RectTransform)portrait.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size));
            portrait.Show(save.Me.Gear);
            UiKit.Label(col, TalesUi.PetName, UiKit.TitleSize - 2, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var who = UiKit.Node("class", col);
            UiKit.Row(who, 8, null, TextAnchor.MiddleCenter);
            UiKit.Icon(who, C.Icon, 26);
            UiKit.Label(who, HeroFactory.EvoName(h.Cls, petLv), UiKit.BodySize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            var tags = UiKit.Node("tags", col);
            UiKit.Row(tags, 8, null, TextAnchor.MiddleCenter);
            UiKit.Badge(tags, $"Pet Lv {petLv}", UiKit.EmberInk);
            UiKit.Badge(tags, TalesData.Current.GenreLabel(hero.Gen), UiKit.SkyInk);

            var track = UiKit.Panel(col, "renown", Palette.Ink.WithAlpha(.1f), 6);
            UiKit.Size(track, -1, 10);
            var fill = UiKit.Panel(track.transform, "fill", Palette.Amber, 6).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)(renown.xp / renown.need)), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;

            var c = HeroFactory.CardStats(hero);
            var stats = UiKit.Node("stats", col);
            UiKit.Row(stats, 12, null, TextAnchor.MiddleCenter);
            GearUi.IconText(stats, "❤️", c.hp.ToString(), UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            GearUi.IconText(stats, "⚔️", c.atk.ToString(), UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            GearUi.IconText(stats, "🛡️", c.def.ToString(), UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            GearUi.IconText(stats, "💨", c.spd.ToString(), UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);

            var buttons = UiKit.Node("buttons", col);
            UiKit.Row(buttons, 10, new RectOffset(0, 0, 6, 0), TextAnchor.MiddleCenter).childForceExpandWidth = true;
            UiKit.Secondary(buttons, "Hero & moves", () => TalesUi.OpenHero(), "🦊", 46);
            UiKit.Secondary(buttons, "Bag & gear", () => TalesUi.OpenBag(), "🎒", 46);
        }

        // ---- the small read ----

        Selectable SmallRead(bool hatched)
        {
            var run = TaleStore.Load();
            var card = UiKit.Panel(rightContent, "small read", Palette.Paper);
            UiKit.Outline(card, Palette.Ink.WithAlpha(.1f), UiKit.CardRadius, 1);
            UiKit.Column(card.rectTransform, 12, new RectOffset(18, 18, 18, 18));
            var row = UiKit.Node("row", card.transform);
            UiKit.Row(row, 18);
            var cover = UiKit.Node("cover", row);
            UiKit.Size(cover, 96, 128);
            cover.sizeDelta = new Vector2(96, 128);
            StorybookPage.Cover(cover, CoverOf(run), false);
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 4, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, "Small read", UiKit.TitleSize - 2, Palette.Ink, UiKit.Title);
            string going = run == null ? null : run.Over ? "The End · its storybook is waiting" : $"{TaleLife.Progress(run)} · {TaleScreen.Where(run)}";
            if (run != null) UiKit.Label(words, BattleText.Prose(run.Title), UiKit.BodySize + 1, UiKit.EmberInk, UiKit.Bold);
            UiKit.Label(words, going ?? "Your own endless dungeon: four lands, a villain at the top, and a storybook at the end. Stop any time and pick it up later.",
                UiKit.BodySize, Palette.InkSoft);
            var go = UiKit.Primary(row, run == null ? "Start" : "Continue", () => Play(run), run == null ? "✨" : "📖", 56);
            UiKit.Size(go, 180, 56);
            go.interactable = hatched;
            if (run != null)
            {
                UiKit.Rule(card.transform);
                var close = UiKit.Node("close", card.transform);
                UiKit.Row(close, 12);
                UiKit.Size(UiKit.Label(close, run.Over ? "Close it to start a new tale." : "Closing it skips the storybook. To keep one, open the tale and choose End this tale from the ⋯ menu.",
                    UiKit.SmallSize, Palette.InkSoft), -1, -1, 1);
                UiKit.ConfirmButton(close, "Close this tale", "Tap again to close it", () => { TaleStore.Clear(); Fill(); }, 44);
            }
            return go;
        }

        // a running tale's cover: its pet in its colours; none yet, an empty book
        static Dictionary<string, object> CoverOf(TaleRun run)
        {
            var cast = new List<object>();
            if (run != null && run.Cast.Count > 0) cast.Add(new Dictionary<string, object> { ["l"] = run.Cast[0].L, ["g"] = (double)run.Cast[0].G });
            return new Dictionary<string, object> { ["title"] = run == null ? "" : BattleText.Prose(run.Title), ["cast"] = cast };
        }

        void Play(TaleRun run)
        {
            run = run ?? TaleLife.NewSolo(TalesSave.Current, Buddy.ShownLook, TalesUi.PetName);
            Close();
            TaleScreen.Open(run);
        }

        // ---- the daily tale ----

        // paintDaily (T:751-753): one card with how today's daily stands and Play / Continue / Board
        void DailyCard()
        {
            var mine = TaleStore.LoadDaily(TaleLife.Today());
            bool done = mine != null && mine.Done;
            var card = UiKit.Panel(rightContent, "daily", Palette.Paper);
            UiKit.Outline(card, Palette.Ink.WithAlpha(.1f), UiKit.CardRadius, 1);
            UiKit.Row(card.rectTransform, 16, new RectOffset(18, 18, 14, 14));
            UiKit.Icon(card.transform, "📆", 56);
            var words = UiKit.Node("words", card.transform);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, "Daily tale", UiKit.TitleSize - 4, Palette.Ink, UiKit.Title);
            UiKit.Label(words, done ? $"You reached {mine.Label}" : mine != null ? $"Your run is waiting · {mine.Label}" : "Same dungeon for everyone, once a day",
                UiKit.BodySize, Palette.InkSoft);
            UiKit.Size(UiKit.Secondary(card.transform, done ? "Board ›" : mine != null ? "Continue ›" : "Play ›", DailySheet, null, 52), 180, 52);
        }

        // dailySheet (T:732-746): the date, the tale's name, its boon, relic and curse, how your try stands, the board, the countdown
        void DailySheet()
        {
            string day = TaleLife.Today();
            var P = TaleLife.Daily(day);
            var mine = TaleStore.LoadDaily(day);
            var s = Sheet.Create(Root, "Daily tale", new Vector2(.5f, .5f), Vector2.zero, 620, "Daily tale");
            s.Dim(.4f);
            s.Closed = () => { countdown = null; Destroy(s.gameObject); };
            var col = s.Card;
            UiKit.Label(col, $"{DateTime.Now.ToString("dddd, MMM d", CultureInfo.InvariantCulture)} · the same dungeon for every reader", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.Label(col, BattleText.Prose(P.Name), UiKit.TitleSize - 2, UiKit.EmberInk, UiKit.Title);
            var rules = UiKit.Node("rules", col);
            UiKit.Row(rules, 10).childForceExpandWidth = true;
            var B = TalesData.Current.Boons.Find(b => b.key == P.Boon);
            var A = EpicData.Current.Relic(P.Relic);
            var C = EpicData.Current.Curse(P.Curse);
            Rule(rules, B.icon ?? "✨", B.name, B.desc, Palette.Paper);
            Rule(rules, A?.I ?? "💎", A?.N, A?.D, Palette.Paper);
            Rule(rules, C?.I ?? "🌑", C?.N, C?.D, UiKit.RoseInk.WithAlpha(.1f));

            // the status (T:741-745): finished, an egg, Continue, or Start
            if (mine != null && mine.Done)
            {
                UiKit.Label(col, $"<b>{mine.Label}</b> · Finished", UiKit.BodySize + 1, Palette.Ink);
                UiKit.Label(col, "Your pet has had today’s adventure. Come back tomorrow for a new dungeon.", UiKit.SmallSize + 1, Palette.InkSoft);
            }
            else if (!Buddy.Hatched) IconLine(col, "🥚", "Hatch your egg to take on the daily tale.", UiKit.BodySize, null);
            else
            {
                s.First = UiKit.Primary(col, mine != null ? $"Continue · {mine.Label}" : "Start today’s tale", () => { s.Close(); PlayDaily(mine, day); }, mine != null ? "📖" : "✨", 52);
                UiKit.Label(col, mine != null ? "Your run is saved where you left it." : "Your pet goes until the whole party falls.", UiKit.SmallSize + 1, Palette.InkSoft);
            }
            DailyBoard(col, mine);
            countdown = UiKit.Label(col, "", UiKit.SmallSize + 1, Palette.InkSoft);
            Countdown();
            s.Open();
        }

        // a rule chip: icon, name and what it does (the curse's is tinted)
        static void Rule(Transform row, string icon, string name, string desc, Color color)
        {
            var chip = UiKit.Panel(row, name ?? "rule", color);
            UiKit.Outline(chip, Palette.Ink.WithAlpha(.1f), UiKit.CardRadius, 1);
            UiKit.Column(chip.rectTransform, 2, new RectOffset(10, 10, 10, 12), TextAnchor.UpperCenter).childForceExpandWidth = true;
            UiKit.Size(chip, 0, -1, 1);
            UiKit.Icon(chip.transform, icon, 32);
            UiKit.Label(chip.transform, name ?? "", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            UiKit.Label(chip.transform, desc ?? "", UiKit.SmallSize - 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// "🏆 Today's deepest pets" (dBoard, T:730) as far as this device knows it: your own result for the day. The
        /// server board (GET /quest/daily, top 10 with medals and "#rank of n") replaces the body here in stage 3.
        /// </summary>
        internal static void DailyBoard(Transform parent, DailySave mine)
        {
            IconLine(parent, "🏆", "Today’s deepest pets", UiKit.HeadingSize, UiKit.Title);
            if (mine == null) UiKit.Label(parent, "Nobody has set off yet today. The first pet in sets the mark.", UiKit.BodySize, Palette.InkSoft);
            else IconLine(parent, "🐾", $"<b>{TalesUi.PetName}</b> · {mine.Label}{(mine.Over ? "" : " · still going")}", UiKit.BodySize, null);
        }

        static void IconLine(Transform parent, string emoji, string text, int size, Font font)
        {
            var row = UiKit.Node("line", parent);
            UiKit.Row(row, 8);
            UiKit.Icon(row, emoji, size + 6);
            UiKit.Size(UiKit.Label(row, text, size, Palette.Ink, font), -1, -1, 1);
        }

        void Countdown()
        {
            if (countdown) countdown.text = $"A new daily tale in <b>{TaleLife.Countdown(DateTime.Now)}</b> · one try a day";
        }

        protected override void Update()
        {
            base.Update();
            if (countdown && Time.frameCount % 60 == 0) Countdown();
        }

        // Continue today's run, or begin it (dailyNew); null from NewDaily means today's try is already spent
        void PlayDaily(DailySave mine, string day)
        {
            var run = mine?.Run ?? TaleLife.NewDaily(TalesSave.Current, Buddy.ShownLook, TalesUi.PetName, day);
            if (run == null) { Fill(); return; }
            Close();
            TaleScreen.Open(run);
        }

        // ---- the shelf ----

        void Shelf()
        {
            var books = TaleStore.Books;
            var head = UiKit.Node("storybooks", rightContent);
            UiKit.Row(head, 10);
            UiKit.Icon(head, "📕", 30);
            UiKit.Label(head, "Your storybooks", UiKit.TitleSize - 4, Palette.Ink, UiKit.Title).horizontalOverflow = HorizontalWrapMode.Overflow;
            if (books.Count > 0) UiKit.Label(head, $"{books.Count} kept", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            if (books.Count == 0)
            {
                var empty = UiKit.Panel(rightContent, "empty", Palette.Cream);
                UiKit.Outline(empty, Palette.Ink.WithAlpha(.12f), UiKit.CardRadius, 1);
                UiKit.Column(empty.rectTransform, 0, new RectOffset(18, 18, 16, 16));
                UiKit.Label(empty.transform, "When a tale ends, it becomes an animated storybook and lands on this shelf.", UiKit.BodySize, Palette.InkSoft);
                return;
            }
            var shelf = UiKit.Node("shelf", rightContent);
            var grid = shelf.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(120, 120 * 4 / 3f + 28);
            grid.spacing = new Vector2(16, 18);
            grid.childAlignment = TextAnchor.UpperLeft;
            foreach (var b in books)
            {
                var book = b;
                StorybookPage.CoverTile(shelf, book, () => StorybookReader.Open(book, true, null, () => { TaleStore.Delete(book); Fill(); }, null));
            }
        }
    }
}
