using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The HUD's messages. Toasts pop in at the top (up to three, each kept as long as it takes to read, the same
    /// message twice just stays longer), and walking into a place shows its chapter title: the name in big Fraunces
    /// over a soft haze, a short gold ornament, and a line under it. Hud says where they go as the screen changes size;
    /// reduce motion keeps only the fades.
    /// </summary>
    public sealed class Notices : MonoBehaviour
    {
        const int BigToast = 26, SmallToast = 22;
        const float ToastGap = 8, PopSeconds = .28f, LeaveSeconds = .3f;
        const float TitleIn = .55f, TitleOut = .6f; // seconds to rise in and to fade out (it holds as long as it takes to read)
        const float HazeAlpha = .42f;

        sealed class Toast { public RectTransform Rect, Pill; public CanvasGroup Fade; public string Message; public float BornAt, Until, LeftAt = -1, Y = -1; }

        readonly List<Toast> toasts = new List<Toast>();
        RectTransform root, title, leftRule, rightRule;
        CanvasGroup titleFade, subFade;
        Text titleText, subText;
        Image haze;
        float centerX, toastTop, toastWidth = 640, titleTop, titleWidth = 900, titleAt = -99, titleHold;
        int most = 3;

        /// <summary>How tall a stack of this many one-line toasts is, so the chapter title can start under it.</summary>
        public static float StackHeight(int most) => most * (BigToast * 1.3f + 28) + (most - 1) * ToastGap;

        public static Notices Create(RectTransform parent)
        {
            var root = UiKit.Node("notices", parent).Fill();
            var n = root.gameObject.AddComponent<Notices>();
            n.root = root;
            n.BuildTitle();
            return n;
        }

        /// <summary>
        /// Where things go, in reference pixels from the top centre: toasts start at toastTop, centred at x, at most
        /// width wide and "most" at once; the chapter title starts at titleTop, at most titleWidth wide. When the title
        /// starts where the toasts do (short screens), toasts move under it while it shows.
        /// </summary>
        public void Fit(float x, float toastTop, float width, int most, float titleTop, float titleWidth)
        {
            centerX = x;
            this.toastTop = toastTop;
            toastWidth = width;
            this.most = most;
            this.titleTop = titleTop;
            this.titleWidth = titleWidth;
            title.anchoredPosition = new Vector2(x, -titleTop);
        }

        // ---- toasts ----

        /// <summary>A short message at the top: an icon from its first emoji and the words in big type.</summary>
        public void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            float now = Time.unscaledTime;
            var same = toasts.Find(t => t.Message == message && t.LeftAt < 0);
            if (same != null) { same.Until = now + ReadingTime(message); return; }
            toasts.Add(BuildToast(message, now));
            int showing = 0;
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (toasts[i].LeftAt < 0 && ++showing > most) toasts[i].LeftAt = now; // the oldest make room
        }

        // about a second per 20 letters on top of a short glance, within 3 to 7 seconds
        static float ReadingTime(string message) => Mathf.Clamp(2.4f + message.Length * .05f, 3, 7);

        // the pill and its shadow share a holder, so they fade and pop together
        Toast BuildToast(string message, float now)
        {
            var holder = UiKit.Node("toast", root).Pin(new Vector2(.5f, 1), Vector2.zero, Vector2.zero);
            holder.pivot = new Vector2(.5f, .5f);
            var pill = UiKit.Panel(holder, "pill", Palette.Ink.WithAlpha(.94f), 28);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(10, 10));
            UiKit.Row(r, 12, new RectOffset(22, 26, 13, 15), TextAnchor.MiddleCenter);
            UiKit.Hug(r);
            UiKit.Shadow(r, 28, 16, 6, .3f);
            string words = UiKit.SplitEmoji(message, out string icon);
            int size = toastWidth >= 560 ? BigToast : SmallToast;
            var image = UiKit.Icon(r, icon, size + 8);
            UiKit.Show(image, image.enabled);
            var text = UiKit.Label(r, words, size, Palette.Paper, UiKit.Bold, TextAnchor.MiddleCenter);
            float room = toastWidth - 48 - (image.enabled ? size + 20 : 0);
            UiKit.Size(text, Mathf.Min(text.preferredWidth + 2, room)); // one line when it fits, wrapped when it doesn't
            LayoutRebuilder.ForceRebuildLayoutImmediate(r);
            var fade = holder.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0;
            return new Toast { Rect = holder, Pill = r, Fade = fade, Message = message, BornAt = now, Until = now + ReadingTime(message) };
        }

        void Update()
        {
            float now = Time.unscaledTime;
            bool calm = GameSettings.ReduceMotion;
            bool shared = title.gameObject.activeSelf && titleTop <= toastTop;
            float y = shared ? titleTop + title.rect.height + ToastGap : toastTop, glide = calm ? 1 : 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14);
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                if (t.LeftAt < 0 && now >= t.Until) t.LeftAt = now;
                float pop = (now - t.BornAt) / PopSeconds, gone = t.LeftAt < 0 ? 0 : (now - t.LeftAt) / LeaveSeconds;
                if (gone >= 1) { Destroy(t.Rect.gameObject); toasts.RemoveAt(i--); continue; }
                float h = t.Pill.rect.height;
                t.Y = t.Y < 0 ? y : Mathf.Lerp(t.Y, y, glide); // the rest slide up as one leaves
                t.Fade.alpha = Mathf.Clamp01(pop * 1.6f) * (1 - UiKit.EaseOut(gone));
                float scale = calm ? 1 : Mathf.LerpUnclamped(.82f, 1, UiKit.EaseBack(pop)) - .05f * gone;
                t.Rect.localScale = new Vector3(scale, scale, 1);
                t.Rect.anchoredPosition = new Vector2(centerX, -t.Y - h / 2 + (calm ? 0 : gone * 10));
                y += h + ToastGap;
            }
            AnimateTitle(now, calm);
        }

        // ---- the chapter title ----

        void BuildTitle()
        {
            title = UiKit.Node("chapter title", root).Pin(new Vector2(.5f, 1), Vector2.zero, new Vector2(10, 10));
            UiKit.Column(title, 6, new RectOffset(28, 28, 14, 16), TextAnchor.UpperCenter).childForceExpandWidth = false;
            UiKit.Hug(title);
            haze = UiKit.Shadow(title, 60, 56, 0, 0);
            titleFade = title.gameObject.AddComponent<CanvasGroup>();
            titleFade.blocksRaycasts = false;

            titleText = Inked(UiKit.Label(title, "", 56, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter));
            var ornament = UiKit.Node("ornament", title);
            UiKit.Row(ornament, 8, null, TextAnchor.MiddleCenter);
            UiKit.Size(ornament, -1, 14);
            leftRule = Rule(ornament, Color.clear, Palette.Amber, 1);
            var gem = UiKit.Panel(ornament, "gem", Palette.Amber, 2);
            gem.raycastTarget = false;
            UiKit.Size(gem, 9, 9);
            gem.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            rightRule = Rule(ornament, Palette.Amber, Color.clear, 0);
            subText = Inked(UiKit.Label(title, "", UiKit.BodySize + 2, Palette.Cream, UiKit.Body, TextAnchor.MiddleCenter));
            subFade = subText.gameObject.AddComponent<CanvasGroup>();
            title.gameObject.SetActive(false);
        }

        // a soft drop under light words, so they read over grass, water or snow
        static Text Inked(Text t)
        {
            var shade = t.gameObject.AddComponent<Shadow>();
            shade.effectColor = Palette.Ink.WithAlpha(.75f);
            shade.effectDistance = new Vector2(0, -2.5f);
            return t;
        }

        // a gold line that fades out toward its far end; its pivot sits by the gem, so it grows out from there
        static RectTransform Rule(Transform row, Color from, Color to, float pivot)
        {
            var line = UiKit.Panel(row, "rule", Color.white, 1);
            line.raycastTarget = false;
            line.gameObject.AddComponent<SideGradient>().Set(from, to);
            UiKit.Size(line, 72, 2);
            line.rectTransform.pivot = new Vector2(pivot, .5f);
            return line.rectTransform;
        }

        /// <summary>The chapter title for a place: its name, and a line under it when there is one.</summary>
        public void Title(string name, string line)
        {
            string words = UiKit.SplitEmoji(name, out _);
            if (words.Length == 0) return;
            titleText.text = words;
            subText.text = UiKit.SplitEmoji(line, out _);
            titleText.fontSize = words.Length > 36 ? 34 : titleWidth >= 700 ? 56 : 44; // an announcement is a sentence, not a name
            Wrap(titleText);
            Wrap(subText);
            UiKit.Show(subText, subText.text.Length > 0);
            titleHold = Mathf.Clamp(1.6f + (words.Length + subText.text.Length) * .04f, 2.6f, 6);
            titleAt = Time.unscaledTime;
            title.gameObject.SetActive(true);
        }

        // one line when it fits the title's width, wrapped when it doesn't
        void Wrap(Text t) => UiKit.Size(t, Mathf.Min(t.preferredWidth + 2, titleWidth));

        // rises in with a small overshoot while the rules grow out from the gem, holds, then fades up and away
        void AnimateTitle(float now, bool calm)
        {
            if (!title.gameObject.activeSelf) return;
            float age = now - titleAt, leaving = (age - TitleIn - titleHold) / TitleOut;
            if (leaving >= 1) { title.gameObject.SetActive(false); return; }
            float arrive = age / TitleIn;
            float shown = leaving > 0 ? 1 - UiKit.EaseOut(leaving) : UiKit.EaseOut(arrive * 1.4f);
            titleFade.alpha = shown;
            haze.color = haze.color.WithAlpha(HazeAlpha * shown);
            subFade.alpha = Mathf.Clamp01((age - .2f) / .3f);
            float grow = calm ? 1 : UiKit.EaseOut((age - .12f) / .45f);
            leftRule.localScale = rightRule.localScale = new Vector3(grow, 1, 1);
            float pop = calm ? 1 : Mathf.LerpUnclamped(.9f, 1, UiKit.EaseBack(arrive));
            titleText.rectTransform.localScale = new Vector3(pop, pop, 1);
            float lift = calm ? 0 : (1 - UiKit.EaseOut(arrive)) * -12 + Mathf.Max(0, leaving) * 8;
            title.anchoredPosition = new Vector2(centerX, lift - titleTop);
        }
    }
}
