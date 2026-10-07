using System;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The Fate hand and the Story Master's bar (the site's handHTML/wireHand, tales.js T:1213-1217, and dmBar/dmSheet,
    /// T:1218-1221). The hand sits under the map's "Where next?" and under an encounter's choices: tap a card to arm it and
    /// read what it does, tap it again to play it (EpicRooms.PlayCard); the view then saves and redraws with the card's
    /// line. The bar shows the side quest's count and the relic and curse icons and opens "The story so far". Cards and
    /// the bar are Buttons, so mouse, touch and the virtual cursor all reach them.
    /// </summary>
    public static class EpicHand
    {
        internal static readonly Color Gold = Palette.Hex("#ffd36a");
        static int arm = -1;      // the armed card (fcArm); survives the redraw that arming causes
        static string toast;      // the last card's "You played …" line, shown once on the next hand

        /// <summary>The hand: "🃏 Your Fate cards" with a Button per card, then the armed card's text or the last play's line; played runs after a card is played.</summary>
        public static RectTransform Build(RectTransform parent, TaleRun run, Action played, bool focus = false)
        {
            var hand = run.Ep.Hand;
            if (arm >= hand.Count) arm = -1;
            var box = UiKit.Panel(parent, "fate hand", Palette.Paper, 14);
            box.raycastTarget = false;
            UiKit.Outline(box, Palette.Ink.WithAlpha(.12f), 14, 1);
            var col = box.rectTransform;
            UiKit.Column(col, 8, new RectOffset(12, 12, 10, 12), TextAnchor.UpperCenter);
            var head = EpicViews.Centered(col);
            UiKit.Icon(head, "🃏", 22);
            UiKit.Label(head, "Your Fate cards", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            if (hand.Count == 0) UiKit.Label(col, "You draw one in each new land and at every campfire.", UiKit.SmallSize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            else
            {
                var row = EpicViews.Centered(col);
                for (int i = 0; i < hand.Count; i++)
                {
                    bool on = i == arm;
                    var c = EpicData.Current.Fate(hand[i]) ?? EpicData.Current.Fates[0];
                    int at = i;
                    var card = UiKit.Button(row, c.N, on ? Color.Lerp(Palette.Paper, Gold, .45f) : Palette.Cream, () => Tap(col, run, at, played), 12);
                    UiKit.Outline(card, on ? Palette.Amber : Palette.Ink.WithAlpha(.15f), 12, on ? 2 : 1);
                    var r = (RectTransform)card.transform;
                    UiKit.Column(r, 2, new RectOffset(6, 6, 8, 6), TextAnchor.MiddleCenter).childForceExpandWidth = false;
                    UiKit.Size(card, 98, 86);
                    UiKit.Icon(r, c.I, 32);
                    UiKit.Label(r, c.N, UiKit.SmallSize - 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
                    if (on && focus) EpicViews.Focus(card);
                }
            }
            string line = toast ?? (arm >= 0 ? $"{EpicData.Current.Fate(hand[arm])?.D}. Tap it again to play." : null);
            toast = null;
            if (line != null) EpicViews.Line(col, line, UiKit.SmallSize, arm >= 0 ? Palette.Ink : UiKit.EmberInk, true);
            return col;
        }

        // the first tap arms a card (the hand redraws in place to show it), the second plays it
        static void Tap(RectTransform box, TaleRun run, int i, Action played)
        {
            if (arm != i)
            {
                arm = i;
                var parent = (RectTransform)box.parent;
                int at = box.GetSiblingIndex();
                UnityEngine.Object.Destroy(box.gameObject);
                Build(parent, run, played, true).SetSiblingIndex(at);
                return;
            }
            arm = -1;
            toast = EpicRooms.PlayCard(run, i);
            if (toast != null) Sound.Play("rare");
            played();
        }

        /// <summary>True when the bar has something to show: a side quest, relics or curses.</summary>
        public static bool HasBar(TaleRun run) => run.Ep != null && (run.Ep.Th != null || run.Ep.Rel.Count > 0 || run.Ep.Cur.Count > 0);

        /// <summary>dmBar: a centred pill with the side quest's count and the relic and curse icons; tap opens the story so far. Null when there is nothing to show.</summary>
        public static Button Bar(RectTransform parent, TaleRun run)
        {
            if (!HasBar(run)) return null;
            var E = run.Ep; var data = EpicData.Current;
            var b = UiKit.Button(EpicViews.Centered(parent), "story so far", Palette.Paper, () => Story(run), 15);
            UiKit.Outline(b, Palette.Ink.WithAlpha(.15f), 15, 1);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 6, new RectOffset(12, 10, 3, 3), TextAnchor.MiddleCenter);
            UiKit.Size(b, -1, 30);
            if (E.Th != null)
            {
                UiKit.Icon(r, E.Th.I, 18);
                Small(r, $"{Math.Min(E.Th.Got, E.Th.Need)}/{E.Th.Need}");
            }
            foreach (var k in E.Rel) { var a = data.Relic(k); if (a != null) UiKit.Icon(r, a.I, 18); }
            foreach (var k in E.Cur) { var c = data.Curse(k); if (c != null) UiKit.Icon(r, c.I, 18).color = new Color(1, .72f, .78f); }
            Small(r, E.Rel.Count > 0 || E.Cur.Count > 0 ? "›" : "Relics ›");
            return b;
        }

        /// <summary>dmSheet, "🪶 The story so far": the side quest, then the relics and curses held this saga.</summary>
        public static void Story(TaleRun run)
        {
            var body = TaleScreen.Body;
            if (!body || run.Ep == null) return;
            var E = run.Ep; var data = EpicData.Current;
            var s = Sheet.Create(body.parent, "story so far", new Vector2(.5f, .5f), Vector2.zero, 480, "The story so far");
            s.Dim(.35f);
            s.Closed = () => { if (s) UnityEngine.Object.Destroy(s.gameObject); };
            EpicViews.BigIcon(s.Card, "🪶", 40);
            if (E.Th != null)
            {
                string got = E.Th.Got >= E.Th.Need ? "Complete!" : $"{E.Th.Got} of {E.Th.Need} found. Good dice rolls and the Story Master turn them up.";
                EpicViews.Line(s.Card, $"{E.Th.I} Side quest: find {E.Th.N}. {got}", UiKit.BodySize - 1, Palette.Ink, false);
            }
            Heading(s.Card, "Relics");
            if (E.Rel.Count == 0) Note(s.Card, "None yet. Bosses, treasure and the Story Master hand them out.");
            foreach (var k in E.Rel) Row(s.Card, data.Relic(k), false);
            Heading(s.Card, "Curses");
            if (E.Cur.Count == 0) Note(s.Card, "None. Lucky you.");
            foreach (var k in E.Cur) Row(s.Card, data.Curse(k), true);
            Note(s.Card, "Each campfire burns away one curse. Relics and curses last until this saga ends.");
            s.First = UiKit.Secondary(s.Card, "Close", s.Close);
            s.Open();
        }

        static void Heading(RectTransform card, string text) => UiKit.Label(card, text, UiKit.HeadingSize - 2, Palette.Ink, UiKit.Title);

        static void Note(RectTransform card, string text) => UiKit.Label(card, text, UiKit.SmallSize, Palette.InkSoft);

        // one relic or curse: its icon, name and what it does (curses in rose)
        static void Row(RectTransform card, EpicRow x, bool curse)
        {
            if (x == null) return;
            var row = UiKit.Node(x.Key, card);
            UiKit.Row(row, 10, null, TextAnchor.UpperLeft);
            UiKit.Icon(row, x.I, 30);
            var text = UiKit.Node("text", row);
            UiKit.Column(text, 1);
            UiKit.Size(text, -1, -1, 1);
            UiKit.Label(text, x.N, UiKit.BodySize - 1, curse ? UiKit.RoseInk : Palette.Ink, UiKit.Bold);
            UiKit.Label(text, BattleText.Prose(x.D), UiKit.SmallSize, Palette.InkSoft);
        }

        static void Small(RectTransform row, string text) =>
            UiKit.Label(row, text, UiKit.SmallSize, Palette.InkSoft, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
    }
}
