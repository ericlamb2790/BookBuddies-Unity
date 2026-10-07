using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The running tale (lobby.md §1.6): a top bar (‹ back to the library, the title, "Act II · land · 💧 gold", 🎒, ⋯)
    /// over Body, where the dungeon views build. The ⋯ menu has battle speed, End this tale and Back to the library; the
    /// end screen shows the storybook cover, ♥ Save, Begin the next tale (a daily shows how deep it got instead) and the
    /// way back. One tale is open at a time, so its surface is static. B or Esc goes back to the library (the tale waits there).
    /// </summary>
    public sealed class TaleScreen : TalesScreen
    {
        const float Pad = 24, TopHeight = 60;
        const string SpeedKey = "bb.set.bspd"; // the battle screen's remembered speed (1, 2 or 4)

        static TaleScreen current;
        TaleRun run;
        RectTransform body, over, overColumn;
        Text title, sub, gold, idle;
        Image subIcon, drop, bagDot;
        Sheet menu;
        bool bagWasOpen;
        (string title, bool over, bool paused, int saga, int act, int ch, int gold) shown;

        /// <summary>Opens the screen for a run (new, resumed, or a new book of it), closing a tale already open, and starts or resumes its dungeon.</summary>
        public static void Open(TaleRun run)
        {
            if (current) current.Close();
            current = Create<TaleScreen>("Tale", false);
            current.MaxSize = new Vector2(1640, 980);
            current.run = run;
            current.Build();
            EpicViews.Show(run);
        }

        /// <summary>The open tale's run; null while no tale is open.</summary>
        public static TaleRun Tale => current ? current.run : null;

        /// <summary>Where the dungeon views build (under the top bar); null while no tale is open.</summary>
        public static RectTransform Body => current ? current.body : null;

        /// <summary>True while anything is over the tale (a menu, a sheet, the bag, a fight) or no tale is open: the story loop waits.</summary>
        public static bool Paused => !current || !ReferenceEquals(UiStack.Top, current);

        /// <summary>
        /// Plays one fight over the tale; done gets the outcome once the battle's end card closes. The tale stays drawn under
        /// it but can't be used meanwhile: the battle draws just over it and pauses only while something (the bag, a sheet) is on top of it.
        /// </summary>
        public static void RunBattle(BattleSetup setup, Action<BattleOutcome> done)
        {
            var s = current;
            if (s) s.StepAside(true);
            BattleScreen.Run(setup, new Vector2(Screen.width / 2f, Screen.height / 2f), o =>
            {
                if (s) s.StepAside(false);
                done(o);
            });
        }

        /// <summary>
        /// Ends the tale ("ended" or "defeat"; the dungeon tells "lost" before a defeat): the story becomes its book, the
        /// tale is saved, the end screen shows, and the storybook opens by itself a moment later (overView).
        /// </summary>
        public static void End(string reason)
        {
            if (!current) return;
            if (!current.run.Over) TaleLife.End(current.run, reason);
            current.ShowEnd();
            if (current.run.Book != null) current.Invoke(nameof(AutoRead), .9f);
        }

        // ---- building ----

        void Build()
        {
            UiStack.Remove(this);
            UiStack.Push(this, Back); // B and Esc go back to the library, asking first when a storybook would be lost
            var top = UiKit.Node("top bar", Card);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = Vector2.one; top.pivot = new Vector2(.5f, 1);
            top.offsetMin = new Vector2(Pad, -Pad - TopHeight); top.offsetMax = new Vector2(-Pad, -Pad);
            UiKit.Row(top, 14);
            var back = Round(top, Back);
            UiKit.Arrow(back.transform, Palette.Ink).localRotation = Quaternion.Euler(0, 0, 180);
            var words = UiKit.Node("title", top);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            title = UiKit.Label(words, "", UiKit.TitleSize, Palette.Ink, UiKit.Title);
            title.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(title, -1, 38);
            var line = UiKit.Node("sub", words);
            UiKit.Row(line, 6);
            subIcon = UiKit.Icon(line, null, 20);
            sub = UiKit.Label(line, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            drop = UiKit.Icon(line, "💧", 20);
            gold = UiKit.Label(line, "", UiKit.SmallSize + 1, UiKit.SkyInk, UiKit.Bold);
            gold.horizontalOverflow = HorizontalWrapMode.Overflow;
            var bag = UiKit.Secondary(top, "", () => TalesUi.OpenBag(), "🎒", 52);
            UiKit.Size(bag, 64, 52);
            bagDot = ShopKit.Dot(bag.transform);
            var more = Round(top, OpenMenu);
            for (int i = -1; i <= 1; i++)
                UiKit.Panel(more.transform, "dot", Palette.Ink, 3).rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(i * 9, 0), new Vector2(6, 6)).GetComponent<Image>().raycastTarget = false;
            var rule = UiKit.Rule(Card);
            rule.rectTransform.anchorMin = new Vector2(0, 1); rule.rectTransform.anchorMax = Vector2.one;
            rule.rectTransform.offsetMin = new Vector2(Pad, -Pad - TopHeight - 9); rule.rectTransform.offsetMax = new Vector2(-Pad, -Pad - TopHeight - 8);

            idle = UiKit.Label(Card, "Turning the page…", UiKit.HeadingSize, Palette.InkSoft, UiKit.Title, TextAnchor.MiddleCenter);
            Below((RectTransform)idle.transform);
            body = Below(UiKit.Node("body", Card));
            over = Below(UiKit.Node("end", Card));
            overColumn = UiKit.ScrollColumn(over, 16, new RectOffset(), out _);
            ((RectTransform)overColumn.parent).Fill();
            overColumn.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            over.gameObject.SetActive(false);
            menu = Sheet.Create(Root, "Tale menu", new Vector2(1, 1), new Vector2(-Pad - 8, -Pad - TopHeight - 16), 380);
            UiKit.PadHints(Card, ("A", "Select"), ("B", "Library"));
            Paint();
            if (run.Over) ShowEnd();
            else VirtualCursor.FocusFirst(back);
        }

        // the space under the top bar
        RectTransform Below(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(Pad, Pad); r.offsetMax = new Vector2(-Pad, -Pad - TopHeight - 16);
            return r;
        }

        protected override void Layout(Vector2 card)
        {
            int side = Mathf.RoundToInt(Mathf.Max(8, (card.x - Pad * 2 - 760) / 2));
            overColumn.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(side, side, 18, 40);
        }

        protected override void Update()
        {
            base.Update();
            if (run == null) return;
            TaleStore.Tick();
            Paint();
            idle.enabled = !run.Over && body.childCount == 0;
            // the bag closed mid-tale: your hero takes its new gear along (keeping its share of HP)
            bool bag = TalesUi.Find<BagScreen>() != null;
            if (bagWasOpen && !bag && !run.Over)
            {
                TaleLife.Resnap(run, TalesSave.Current, Buddy.ShownLook, TalesUi.PetName);
                TaleStore.SaveSoon(run);
            }
            bagWasOpen = bag;
        }

        // the top bar: title, "Act II · land", 💧 drops, the bag's "new" dot; the words are rebuilt only when what they show changes
        void Paint()
        {
            bool paused = !run.Over && Paused && !BattleScreen.Open;
            var now = (run.Title, run.Over, paused, run.Ep?.Saga ?? 0, run.Ep?.Act ?? 0, run.Ch, run.Gold);
            if (!now.Equals(shown))
            {
                shown = now;
                title.text = BattleText.Prose(UiKit.SplitEmoji(run.Title ?? "A tale", out _)); // the daily's 📅
                sub.text = run.Over ? "The End" : paused ? "Paused" : Where(run);
                UiKit.SetIcon(subIcon, paused ? "⏸️" : null);
                UiKit.Show(drop, !run.Over);
                gold.text = run.Over ? "" : run.Gold.ToString();
            }
            UiKit.Show(bagDot, TalesSave.Current.Fresh.Count > 0);
        }

        /// <summary>Where the tale is: "Act II · Mossy Margins" in the dungeon, "Chapter n" before it begins.</summary>
        public static string Where(TaleRun run)
        {
            var E = run.Ep;
            if (E == null || E.Acts.Count < E.Act) return $"Chapter {run.Ch}";
            return $"Act {EpicSaga.Roman(E.Act)} · {EpicSaga.LandOf(E).N}";
        }

        // ---- the ⋯ menu ----

        void OpenMenu()
        {
            var col = menu.Card;
            for (int i = col.childCount - 1; i >= 0; i--) Destroy(col.GetChild(i).gameObject);
            Text speed = null;
            var fast = UiKit.Secondary(col, "", () => { PlayerPrefs.SetInt(SpeedKey, NextSpeed()); speed.text = SpeedText(); }, "⏩");
            speed = fast.GetComponentInChildren<Text>();
            speed.text = SpeedText();
            menu.First = fast;
            if (!run.Over) UiKit.Secondary(col, "End this tale", () => { menu.Close(); AskEnd(); }, "📕");
            UiKit.Secondary(col, run.Over ? "Back to the library" : "Back to the library (it waits for you)", () => { menu.Close(); Back(); }, "🚪");
            menu.Open();
        }

        static int NextSpeed() { int s = PlayerPrefs.GetInt(SpeedKey, 1); return s == 1 ? 2 : s == 2 ? 4 : 1; }

        static string SpeedText() => $"Battle speed {PlayerPrefs.GetInt(SpeedKey, 1)}×";

        void AskEnd() => ConfirmCard.Open("📕", "End this tale?", "Everything your party did becomes an animated storybook you can keep.",
            "End it and make the storybook", () => End(TaleLife.Ended));

        // ---- leaving ----

        // ‹, B, Esc and the menu: a tale going waits in the lobby; an ended one asks to keep its storybook first
        void Back()
        {
            if (!run.Over) { ToLobby(); return; }
            if (run.Book == null || TaleStore.IsKept(run)) { Finish(); return; }
            UiStack.Push(this, Back); // B and Esc took the tale off the stack: it stays under the question
            var ask = Sheet.Create(Root, "Save first", new Vector2(.5f, .5f), Vector2.zero, 520, "Save the storybook first?");
            ask.Dim(.4f);
            var why = UiKit.Label(ask.Card, $"{BattleText.Prose(UiKit.SplitEmoji(run.Title, out _))} isn’t saved yet. If you leave now, it won’t be in your storybooks.", UiKit.BodySize, Palette.InkSoft);
            why.horizontalOverflow = HorizontalWrapMode.Wrap;
            ask.First = UiKit.Primary(ask.Card, "Save and leave", () => { ask.Close(); Keep(); Finish(); }, "❤️");
            UiKit.Secondary(ask.Card, "Leave without saving", () => { ask.Close(); Finish(); });
            UiKit.Secondary(ask.Card, "Stay here", ask.Close);
            ask.Closed = () => Destroy(ask.gameObject);
            ask.Open();
        }

        // an ended tale leaves Your journeys (a daily keeps its result: that day is done)
        void Finish()
        {
            if (run.Daily == null) TaleStore.Clear();
            ToLobby();
        }

        void ToLobby()
        {
            Close();
            TalesLobby.Open();
        }

        protected override void OnClosed()
        {
            if (!run.Over) TaleLife.Leave(run); // the tale waits (Continue in the lobby)
            if (current == this) current = null;
        }

        // the tale stays open and drawn under the fight (so a bag opened mid-fight gets an order above the battle, tale + 1, and
        // the town never shows around the battle's opening circle) but its buttons are out of reach, keys and pad included
        void StepAside(bool aside) => Group.interactable = !aside;

        // ---- the end screen (overView) ----

        void ShowEnd()
        {
            menu.Close();
            body.gameObject.SetActive(false);
            over.gameObject.SetActive(true);
            for (int i = overColumn.childCount - 1; i >= 0; i--) Destroy(overColumn.GetChild(i).gameObject);
            bool sad = run.Reason == TaleLife.Defeat, daily = run.Daily != null;
            var s = run.Stats;
            UiKit.Label(overColumn, sad ? daily ? "The daily tale is over" : "The party needs a nap" : "The End", UiKit.TitleSize + 14, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            if (daily)
            {
                // the daily's end (T:1532-1533): how deep your pet got, then today's board
                string name = BattleText.Prose(UiKit.SplitEmoji(run.Title, out _));
                UiKit.Label(overColumn, $"Your pet reached <b>{TaleLife.Label(run)}</b> in {name}. A new daily tale arrives tomorrow.", UiKit.BodySize + 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
                TalesLobby.DailyBoard(overColumn, TaleStore.LoadDaily(run.Daily));
            }
            else
            {
                string reached = run.Ep != null ? $"Your fellowship reached Act {EpicSaga.Roman(run.Ep.Act)} of {BattleText.Prose(run.Ep.Name)}." : $"Your party reached chapter {run.Ch}.";
                UiKit.Label(overColumn, sad ? $"{reached} Your pets keep the renown they earned. Train in a small read or visit the Tales shop, then start a fresh tale from the library."
                    : $"{run.Ch} chapter{(run.Ch > 1 ? "s" : "")} of adventure, all written down.", UiKit.BodySize + 1, Palette.InkSoft, null, TextAnchor.MiddleCenter);
                UiKit.Label(overColumn, $"{s.Wins} battle{(s.Wins == 1 ? "" : "s")} won · {s.Bosses} villain{(s.Bosses == 1 ? "" : "s")} sent home · {s.Foes} troublemakers",
                    UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold, TextAnchor.MiddleCenter);
            }
            Selectable first = null;
            if (run.Book != null)
            {
                first = CoverButton(overColumn, run.Book, ReadBook);
                var keep = UiKit.Node("keep", overColumn);
                UiKit.Row(keep, 12, null, TextAnchor.MiddleCenter);
                if (TaleStore.IsKept(run)) UiKit.Label(keep, "✓ Saved to your storybooks", UiKit.BodySize, UiKit.LeafInk, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                else
                {
                    UiKit.Label(keep, "Keep this storybook?", UiKit.BodySize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                    UiKit.Primary(keep, "Save storybook", Keep, "❤️", 44);
                }
            }
            var buttons = UiKit.Node("buttons", overColumn);
            UiKit.Row(buttons, 12, new RectOffset(0, 0, 8, 0), TextAnchor.MiddleCenter);
            if (run.Reason == TaleLife.Ended && !daily)
            {
                var again = UiKit.Primary(buttons, "Begin the next tale", NextBook, "✨", 52);
                if (first == null) first = again;
            }
            var lib = UiKit.Secondary(buttons, "Back to the Tales library", Back, "📚", 52);
            if (!Paused) VirtualCursor.FocusFirst(first ?? lib); // not under the open storybook (♥ Save there rebuilds this)
        }

        // the storybook opens on its own when the tale ends, unless something (the reader, the menu) is already over the tale
        void AutoRead()
        {
            if (run.Over && !Paused) ReadBook();
        }

        void ReadBook() => StorybookReader.Open(run.Book, TaleStore.IsKept(run), Keep, null, () => { if (this && run.Over) ShowEnd(); });

        void Keep()
        {
            TaleStore.Keep(run);
            Sound.Play("happy");
            if (run.Over && over.gameObject.activeSelf) ShowEnd();
        }

        // newVolume: the same tale, the next book, your buddy fresh; the screen opens again on it
        void NextBook()
        {
            TaleLife.NewVolume(run, TalesSave.Current, Buddy.ShownLook, TalesUi.PetName);
            Open(run);
        }

        /// <summary>The storybook's cover as a big button with "▶ Read the storybook" on it (the end screen's bookbtn).</summary>
        static Button CoverButton(Transform parent, Dictionary<string, object> book, Action read)
        {
            var holder = UiKit.Node("storybook", parent);
            UiKit.Size(holder, -1, 290);
            var b = UiKit.Button(holder, "Read the storybook", Color.white, read, 14);
            var r = ((RectTransform)b.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(420, 270));
            UiKit.Shadow(r, 14, 22, 10, .4f);
            StorybookPage.Cover(r, book, true);
            var pill = UiKit.Panel(r, "read", Palette.Ink.WithAlpha(.72f), 18);
            pill.raycastTarget = false;
            pill.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(240, 36));
            UiKit.Label(pill.transform, "Read the storybook  ›", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter).rectTransform.Fill();
            return b;
        }

        static Button Round(Transform parent, Action act)
        {
            var b = UiKit.Button(parent, "round", Palette.Paper, act, 26);
            UiKit.Outline(b, Palette.Ink.WithAlpha(.14f), 26, 2);
            UiKit.Size(b, 52, 52);
            return b;
        }
    }
}
