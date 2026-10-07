using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The fight log: every event as a line (the site's flogAdd), kept as the fight goes, shown newest first in a paper
    /// sheet on the right with round headers and rows tinted by side. The fight waits while it's open; B closes it.
    /// </summary>
    public sealed class BattleLog : MonoBehaviour
    {
        const int MaxRows = 400;
        static readonly Color PetTint = Palette.Leaf.WithAlpha(.14f), FoeTint = Palette.Rose.WithAlpha(.12f), StoryTint = Palette.Amber.WithAlpha(.2f);

        readonly List<(int round, string icon, string text, BattleText.Side side)> rows = new List<(int, string, string, BattleText.Side)>();
        BattleEngine engine;
        RectTransform sheet;

        /// <summary>True while the sheet is showing.</summary>
        public bool IsOpen => sheet != null;

        /// <summary>The log for a fight, living on the battle screen's object.</summary>
        public static BattleLog Create(GameObject host, BattleEngine engine)
        {
            var log = host.AddComponent<BattleLog>();
            log.engine = engine;
            return log;
        }

        /// <summary>Writes an event down.</summary>
        public void Add(BattleEvent e)
        {
            var (icon, text, side) = BattleText.LogLine(e, engine);
            rows.Add((engine.Round, icon, text, side));
            if (rows.Count > MaxRows) rows.RemoveAt(0);
        }

        /// <summary>The newest n rows as rich text, newest first (the side panel's "last turns").</summary>
        public IEnumerable<string> Last(int n)
        {
            for (int i = rows.Count - 1; i >= 0 && n-- > 0; i--) yield return rows[i].text;
        }

        /// <summary>Opens the sheet over the battle (layer is the full-screen layer it goes on).</summary>
        public void Open(RectTransform layer)
        {
            if (IsOpen) return;
            sheet = UiKit.Node("fight log", layer).Fill();
            var scrim = UiKit.Cover(sheet, "scrim", Palette.Ink.WithAlpha(.45f), null, true);
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(Close);

            var card = UiKit.Panel(sheet, "card", Palette.Cream, 18).rectTransform;
            card.anchorMin = new Vector2(1, 0);
            card.anchorMax = Vector2.one;
            card.pivot = new Vector2(1, .5f);
            card.sizeDelta = new Vector2(Mathf.Min(560, layer.rect.width - 32), -32);
            card.anchoredPosition = new Vector2(-16, 0);
            UiKit.Shadow(card, 18, 30, 10, .35f);
            UiKit.Column(card, 12, new RectOffset(24, 24, 18, 22));
            UiKit.Header(card, "Fight log", Close);
            UiKit.Label(card, $"Round {Mathf.Max(1, engine.Round)} · newest first. The fight waits while this is open.", UiKit.SmallSize, Palette.InkSoft);
            var list = UiKit.ScrollColumn(card, 6, new RectOffset(0, 6, 0, 0), out _);
            UiKit.Size(list.parent, -1, 0).flexibleHeight = 1;
            Rows(list);
            var back = UiKit.Primary(card, "Back to the fight", Close);
            UiKit.PadHints(card, ("B", "Back"));

            UiStack.Push(this, Close);
            VirtualCursor.FocusFirst(back);
            KeyTween.Play(card, .22f, BattleEase.Out, new Kf(0, 24, 0, 0, 1, 0), new Kf(1));
        }

        // newest first, with a "Round N" header wherever the round changes
        void Rows(RectTransform list)
        {
            int round = -1;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                var (r, icon, text, side) = rows[i];
                if (r != round)
                {
                    round = r;
                    var head = UiKit.Node("round", list);
                    UiKit.Row(head, 0);
                    UiKit.Badge(head, r <= 0 ? "Start" : $"Round {r}", Palette.InkSoft);
                }
                var row = UiKit.Panel(list, "row", side == BattleText.Side.Pet ? PetTint : side == BattleText.Side.Foe ? FoeTint : StoryTint, 10);
                row.raycastTarget = false;
                UiKit.Row(row.rectTransform, 10, new RectOffset(10, 12, 7, 8), TextAnchor.UpperLeft);
                UiKit.Icon(row.transform, icon, 22);
                var t = UiKit.Label(row.transform, text, UiKit.SmallSize + 2, Palette.Ink);
                UiKit.Size(t, -1, -1, 1);
            }
            if (rows.Count == 0) UiKit.Label(list, "Nothing yet. The fight is just starting.", UiKit.BodySize, Palette.InkSoft);
        }

        /// <summary>Closes the sheet; the fight carries on.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            UiStack.Remove(this);
            Destroy(sheet.gameObject);
            sheet = null;
        }

        void OnDestroy() => UiStack.Remove(this);
    }
}
