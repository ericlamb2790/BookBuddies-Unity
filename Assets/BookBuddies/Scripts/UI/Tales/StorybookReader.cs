using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BookBuddies.Tales
{
    /// <summary>
    /// The storybook reader (storybook.md §2.3): a picture book on dark leather whose pages turn by themselves once there
    /// has been time to read them. Two facing pages on a wide screen, one page on a tall one. ‹ › (LB/RB, ← →, a tap on
    /// either side, a swipe) turn pages, ⏸/▶ (Space) stops or restarts the turning, the pace pill changes the reading
    /// speed. ♥ Save keeps the book, Delete (tap twice) takes a kept one off the shelf, ✕ or B closes it.
    /// </summary>
    public sealed class StorybookReader : TalesScreen
    {
        const string PaceKey = "bb.talePace";
        const float TopHeight = 56, ControlsHeight = 60, Gap = 12;
        const float TurnOut = .75f, TurnBack = .6f;

        Dictionary<string, object> book;
        List<object> pages;
        Action save, delete, closed;
        bool kept, spread, auto = true;
        int index, pace, pageMs; // pageMs: how long this page stays at this pace (tqPageMs), worked out when either changes
        float elapsed;
        Vector2 laidOut;
        RectTransform area;
        StorybookPage page;
        readonly List<(StorybookPage p, Image shade, float at, bool outward)> turning = new List<(StorybookPage, Image, float, bool)>();
        Button saveButton, deleteButton, autoButton, next;
        Image autoIcon, paceIcon;
        Text paceText, deleteText;
        float deleteArmedAt = -99;
        readonly List<Image> dots = new List<Image>();

        /// <summary>
        /// Opens a book. kept: it's on the shelf already (Delete instead of ♥ Save). save keeps it, delete takes it off the
        /// shelf (null = no Delete), closed runs when the reader closes.
        /// </summary>
        public static void Open(Dictionary<string, object> book, bool kept, Action save, Action delete, Action closed)
        {
            if (book?.Arr("pages") == null || book.Arr("pages").Count == 0) return;
            var r = Create<StorybookReader>("Storybook", false, false);
            r.MaxSize = new Vector2(4000, 4000);
            r.book = book; r.pages = book.Arr("pages");
            r.kept = kept; r.save = save; r.delete = delete; r.closed = closed;
            r.pace = Mathf.Clamp(PlayerPrefs.GetInt(PaceKey, 0), 0, TalesData.Current.Paces.Count - 1);
            r.Build();
        }

        Pace Pace => TalesData.Current.Paces[pace];

        void Build()
        {
            var leather = UiKit.Cover(Root, "leather", Color.white, Leather);
            leather.transform.SetSiblingIndex(1); // over the scrim, under the card
            var top = UiKit.Node("top", Card);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = Vector2.one; top.pivot = new Vector2(.5f, 1);
            top.offsetMin = new Vector2(0, -TopHeight); top.offsetMax = Vector2.zero;
            UiKit.Row(top, 10, new RectOffset(8, 8, 4, 4));
            var title = UiKit.Label(top, BattleText.Prose(book.Str("title")), UiKit.HeadingSize, Cream, UiKit.Title);
            title.horizontalOverflow = HorizontalWrapMode.Wrap; title.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(title, -1, 44, 1);
            saveButton = Pill(top, "Save", "❤️", Keep);
            deleteButton = Pill(top, "Delete", null, Delete);
            deleteText = deleteButton.GetComponentInChildren<Text>();
            UiKit.CloseButton(top, Close);
            PaintKept();

            area = UiKit.Node("book", Card);
            area.gameObject.AddComponent<Image>().color = Color.clear; // taps and swipes land here
            area.gameObject.AddComponent<Taps>().Reader = this;
            UiKit.Shadow(area, 16, 30, 14, .7f);

            var bar = UiKit.Node("controls", Card);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(.5f, 0);
            UiKit.Row(bar, 8, null, TextAnchor.MiddleCenter);
            var prev = Round(bar, () => Go(index - 1, true));
            UiKit.Arrow(prev.transform, Cream).localRotation = Quaternion.Euler(0, 0, 180);
            autoButton = Round(bar, () => Play(!auto));
            autoIcon = UiKit.Node("icon", autoButton.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(24, 24)).gameObject.AddComponent<Image>();
            autoIcon.raycastTarget = false;
            var paceButton = UiKit.Button(bar, "pace", Cream.WithAlpha(.1f), NextPace, 22);
            UiKit.Size(paceButton, -1, 44);
            UiKit.Row((RectTransform)paceButton.transform, 6, new RectOffset(12, 14, 0, 0), TextAnchor.MiddleCenter);
            paceIcon = UiKit.Icon(paceButton.transform, null, 24);
            paceText = UiKit.Label(paceButton.transform, "", UiKit.SmallSize, Cream, UiKit.Bold);
            paceText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var dotRow = UiKit.Node("dots", bar);
            UiKit.Row(dotRow, 4, null, TextAnchor.MiddleCenter);
            UiKit.Size(dotRow, -1, 44, 1);
            for (int i = 0; i < pages.Count; i++)
            {
                var d = UiKit.Panel(dotRow, "dot", Cream.WithAlpha(.2f), 3);
                d.raycastTarget = false;
                dots.Add(d);
            }
            next = Round(bar, () => Go(index + 1));
            UiKit.Arrow(next.transform, Cream);
            foreach (var b in Card.GetComponentsInChildren<Selectable>()) b.navigation = new Navigation { mode = Navigation.Mode.None }; // ← → turn pages
            UiKit.PadHints(Card, ("A", "Select"), ("LB/RB", "Turn"), ("B", "Close"));
            PaintAuto();
            PaintPace();
            VirtualCursor.FocusFirst(next);
        }

        protected override void Layout(Vector2 card)
        {
            var bar = (RectTransform)next.transform.parent;
            bar.sizeDelta = new Vector2(Mathf.Min(card.x, 760), ControlsHeight);
            bar.anchoredPosition = Vector2.zero;
            float w = card.x, h = card.y - TopHeight - ControlsHeight - Gap * 2;
            spread = w >= 820 && w > card.y * 1.1f;
            var size = spread ? new Vector2(Mathf.Min(w, 1480, h * 1.52f), 0) : new Vector2(Mathf.Min(w, 720), Mathf.Min(h, 1060));
            if (spread) size.y = size.x / 1.52f;
            area.anchorMin = area.anchorMax = new Vector2(.5f, 0);
            area.pivot = new Vector2(.5f, .5f);
            area.sizeDelta = size;
            area.anchoredPosition = new Vector2(0, ControlsHeight + Gap + h / 2);
            if (size == laidOut) return;
            laidOut = size;
            Show(index); // redraw the page at the new size
        }

        // ---- turning ----

        void Show(int n)
        {
            foreach (var t in turning) if (t.p) Destroy(t.p.gameObject);
            turning.Clear();
            if (page) Destroy(page.gameObject);
            page = StorybookPage.Build(area, book, n, spread, Pace.Rd);
            index = n;
            elapsed = 0;
            TimePage();
            PaintDots();
        }

        /// <summary>
        /// Turns to page n: forward, the new page waits under the old one while it swings away on the spine; back, the
        /// new page swings in on top.
        /// </summary>
        void Go(int n, bool back = false)
        {
            if (n < 0 || n >= pages.Count || n == index) return;
            var old = page;
            page = StorybookPage.Build(area, book, n, spread, Pace.Rd);
            index = n;
            elapsed = 0;
            TimePage();
            PaintDots();
            Sound.Play(n == pages.Count - 1 ? "happy" : "whoosh", .5f);
            if (GameSettings.ReduceMotion) { Destroy(old.gameObject); return; }
            if (back) Turn(page, true);
            else { old.transform.SetAsLastSibling(); Turn(old, false); }
            foreach (var t in turning) if (t.p != page && t.p != old && t.p) Destroy(t.p.gameObject, .05f); // a page mid-turn when another starts
            if (back) Destroy(old.gameObject, TurnBack);
        }

        void Turn(StorybookPage p, bool inward)
        {
            foreach (var t in turning) if (t.p == p && t.shade) Destroy(t.shade.gameObject); // turned again mid-turn
            turning.RemoveAll(t => t.p == p);
            var shade = UiKit.Cover(p.Rect, "shade", Color.black.WithAlpha(0));
            if (inward) p.Rect.localScale = new Vector3(0, 1, 1);
            turning.Add((p, shade, Time.unscaledTime, !inward));
        }

        // the turn as the site's video draws it: the page's width shrinks with cos(e·π/2) about the spine, and darkens
        void Turning()
        {
            for (int i = turning.Count - 1; i >= 0; i--)
            {
                var (p, shade, at, outward) = turning[i];
                if (!p) { turning.RemoveAt(i); continue; }
                float k = Mathf.Clamp01((Time.unscaledTime - at) / (outward ? TurnOut : TurnBack));
                float e = k < .5f ? 2 * k * k : 1 - Mathf.Pow(-2 * k + 2, 2) / 2; // ease in and out
                float open = outward ? 1 - e : e;
                p.Rect.localScale = new Vector3(Mathf.Cos((1 - open) * Mathf.PI / 2), 1, 1);
                shade.color = Color.black.WithAlpha(.45f * (1 - open));
                if (k < 1) continue;
                turning.RemoveAt(i);
                if (outward) Destroy(p.gameObject);
                else Destroy(shade.gameObject);
            }
        }

        // ---- the timer and controls ----

        bool Held => !ReferenceEquals(UiStack.Top, this) || !Application.isFocused;

        void TimePage() => pageMs = StoryBook.PageMs((Dictionary<string, object>)pages[index], Pace);

        protected override void Update()
        {
            base.Update();
            if (page == null) return;
            Turning();
            if (auto && !Held)
            {
                elapsed += Time.unscaledDeltaTime * 1000;
                if (elapsed >= pageMs)
                {
                    if (index >= pages.Count - 1) { auto = false; PaintAuto(); }
                    else Go(index + 1);
                }
            }
            page.Progress(elapsed / pageMs);
            if (deleteArmedAt > 0 && Time.unscaledTime - deleteArmedAt > 3) { deleteArmedAt = -99; deleteText.text = "Delete"; }
            if (Held) return;
            if (TalesUi.Pressed(PlazaAction.ZoomOut) || KeyDown(-1)) Go(index - 1, true);
            if (TalesUi.Pressed(PlazaAction.ZoomIn) || KeyDown(1)) Go(index + 1);
            if (KeyDown(0) && EventSystem.current?.currentSelectedGameObject == null) Play(!auto); // else Space presses the selected button
        }

        // the reading keys: ← (-1), → (1), Space (0)
        static bool KeyDown(int which)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            return which < 0 ? k.leftArrowKey.wasPressedThisFrame : which > 0 ? k.rightArrowKey.wasPressedThisFrame : k.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(which < 0 ? KeyCode.LeftArrow : which > 0 ? KeyCode.RightArrow : KeyCode.Space);
#endif
        }

        // ⏸/▶: on a page that has already run out, ▶ turns it (and from the last page starts over)
        void Play(bool on)
        {
            auto = on;
            PaintAuto();
            if (!on || elapsed < pageMs) return;
            if (index >= pages.Count - 1) Go(0, true);
            else Go(index + 1);
        }

        void NextPace()
        {
            pace = (pace + 1) % TalesData.Current.Paces.Count;
            PlayerPrefs.SetInt(PaceKey, pace);
            TimePage();
            PaintPace();
        }

        void Keep()
        {
            save?.Invoke();
            kept = true;
            PaintKept();
        }

        void Delete()
        {
            if (deleteArmedAt < 0) { deleteArmedAt = Time.unscaledTime; deleteText.text = "Tap again to delete"; return; }
            delete?.Invoke();
            Close();
        }

        protected override void OnClosed() => closed?.Invoke();

        // ---- paint ----

        void PaintAuto()
        {
            autoIcon.sprite = auto ? Art.Emote("⏸️") : PlayIcon;
            ((Image)autoButton.targetGraphic).color = auto ? Palette.Hex("#ffcf6e") : Cream.WithAlpha(.1f);
            autoIcon.color = auto ? Palette.Ink : Cream;
        }

        void PaintPace()
        {
            UiKit.SetIcon(paceIcon, Pace.I);
            paceText.text = Pace.N;
        }

        void PaintKept()
        {
            UiKit.Show(saveButton, !kept && save != null);
            UiKit.Show(deleteButton, kept && delete != null);
        }

        void PaintDots()
        {
            for (int i = 0; i < dots.Count; i++)
            {
                bool on = i == index;
                dots[i].color = on ? Palette.Hex("#ffcf6e") : Cream.WithAlpha(.2f);
                UiKit.Size(dots[i], on ? 16 : 6, 6);
            }
        }

        static readonly Color Cream = Palette.Hex("#fff5e6");

        static Button Pill(Transform parent, string label, string emoji, Action act) => UiKit.TextButton(parent, label, emoji, Cream.WithAlpha(.12f), Cream, act, 40);

        static Button Round(Transform parent, Action act)
        {
            var b = UiKit.Button(parent, "round", Cream.WithAlpha(.1f), act, 23);
            UiKit.Size(b, 46, 46);
            return b;
        }

        // ---- textures ----

        static Sprite leather, play;

        // radial-gradient(120% 90% at 50% 30%, #3a2a24, #110b09)
        static Sprite Leather => leather ? leather : leather = Make(64, (x, y) =>
            Color.Lerp(Palette.Hex("#3a2a24"), Palette.Hex("#110b09"), Mathf.Clamp01(new Vector2((x - .5f) / .6f, (y - .7f) / .45f).magnitude)));

        // ▶, drawn: the font has no glyph for it
        static Sprite PlayIcon => play ? play : play = Make(48, (x, y) =>
            new Color(1, 1, 1, Mathf.Clamp01(Mathf.Min(x - .22f, .5f - Mathf.Abs(y - .5f) - (x - .22f) * .58f) * 40)));

        static Sprite Make(int n, Func<float, float, Color> at)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) px[y * n + x] = at((x + .5f) / n, (y + .5f) / n);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
        }

        // a tap on the left third goes back, anywhere else forward; a swipe of 40 or more turns that way
        sealed class Taps : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public StorybookReader Reader;
            Vector2 from;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.dragging) return; // a swipe, not a tap
                var r = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(r, e.position, null, out var p)) return;
                if (p.x < r.rect.xMin + r.rect.width * .33f) Reader.Go(Reader.index - 1, true);
                else Reader.Go(Reader.index + 1);
            }

            public void OnBeginDrag(PointerEventData e) => from = e.position;

            public void OnDrag(PointerEventData e) { }

            public void OnEndDrag(PointerEventData e)
            {
                float dx = (e.position.x - from.x) / UiKit.ScreenScale();
                if (dx <= -40) Reader.Go(Reader.index + 1);
                else if (dx >= 40) Reader.Go(Reader.index - 1, true);
            }
        }
    }
}
