using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Road
{
    /// <summary>
    /// A small centred card over the town for the road's questions and notes: "Dangerous road" (Go anyway /
    /// Turn back) and "Rosewater opens in a later version". It joins the UiStack, so B, Esc or a tap on the
    /// dimmed town backs out, and the gamepad cursor starts on the safe choice.
    /// </summary>
    public sealed class RoadCard : MonoBehaviour
    {
        const float Width = 540, InSeconds = .18f, OutSeconds = .12f;

        CanvasGroup group;
        RectTransform card;
        float shownAt, closingAt = -1;

        /// <summary>A question with a safe main choice (amber) and a riskier quiet one. Backing out picks the safe one.</summary>
        public static RoadCard Ask(string icon, string title, string body, string riskyLabel, System.Action risky, string safeLabel, System.Action safe = null)
        {
            var c = Build(icon, title, body, safe);
            var row = c.Buttons();
            UiKit.Secondary(row, riskyLabel, () => c.Close(risky));
            var main = UiKit.Primary(row, safeLabel, () => c.Close(safe));
            c.Finish(main);
            return c;
        }

        /// <summary>A short note with one button.</summary>
        public static RoadCard Tell(string icon, string title, string body, string okLabel = "Okay")
        {
            var c = Build(icon, title, body, null);
            c.Finish(UiKit.Primary(c.Buttons(), okLabel, () => c.Close(null)));
            return c;
        }

        static RoadCard Build(string icon, string title, string body, System.Action back)
        {
            var canvas = UiKit.MakeCanvas("Road card", 30);
            var c = canvas.gameObject.AddComponent<RoadCard>();
            var root = (RectTransform)canvas.transform;
            c.group = canvas.gameObject.AddComponent<CanvasGroup>();

            // the dimmed town behind: tapping it backs out, like B
            var scrim = UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.42f), null, true);
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(() => c.Close(back));

            var panel = UiKit.Panel(root, "card", Palette.Cream);
            c.card = panel.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(Width, 10));
            UiKit.Column(c.card, 16, new RectOffset(28, 28, 24, 28)).childForceExpandWidth = true;
            UiKit.Hug(c.card, false, true);
            UiKit.Shadow(c.card, UiKit.CardRadius, 24, 8, .3f);

            var head = UiKit.Node("title", c.card);
            UiKit.Row(head, 12);
            if (icon != null) UiKit.Icon(head, icon, 40);
            var t = UiKit.Label(head, title, UiKit.TitleSize, Palette.Ink, UiKit.Title);
            UiKit.Size(t, -1, 44, 1);
            UiKit.Label(c.card, body, UiKit.BodySize, Palette.Ink);
            UiKit.PadHints(c.card, ("A", "Select"), ("B", "Back"));

            UiStack.Push(c, () => c.Close(back, true));
            c.shownAt = Time.unscaledTime;
            Sound.Play("open");
            return c;
        }

        RectTransform Buttons()
        {
            var row = UiKit.Node("buttons", card);
            UiKit.Row(row, 12, new RectOffset(0, 0, 8, 0), TextAnchor.MiddleRight).childForceExpandWidth = false;
            return row;
        }

        void Finish(Selectable first)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            VirtualCursor.FocusFirst(first);
            Update();
        }

        void Close(System.Action then, bool fromStack = false)
        {
            if (closingAt >= 0) return;
            closingAt = Time.unscaledTime;
            group.blocksRaycasts = false;
            if (!fromStack) UiStack.Remove(this);
            Sound.Play("close");
            then?.Invoke();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (closingAt >= 0)
            {
                float q = (now - closingAt) / OutSeconds;
                group.alpha = 1 - Mathf.Clamp01(q);
                if (q >= 1) Destroy(gameObject);
                return;
            }
            float k = UiKit.EaseOut((now - shownAt) / InSeconds);
            group.alpha = k;
            card.localScale = Vector3.one * (GameSettings.ReduceMotion ? 1 : Mathf.Lerp(.96f, 1, k));
        }

        void OnDestroy() => UiStack.Remove(this);
    }
}
