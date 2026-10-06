using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Builds the HUD in code, in the Plaza's look: cream cards, ink text, amber accents, soft round corners.
    /// Sizes are in reference pixels at 1920×1080 (see ScreenScale). Fonts live in Resources/BookBuddies/Fonts;
    /// swap the TTF files to change the type.
    /// </summary>
    public static class UiKit
    {
        public const int CardRadius = 14;
        public const int ButtonHeight = 48;  // every button and field is at least 44 reference pixels tall

        // type sizes in reference pixels
        public const int TitleSize = 30, HeadingSize = 22, BodySize = 18, SmallSize = 15;

        // deeper shades of the palette for text and thin lines on cream (4.5:1 or better)
        public static readonly Color EmberInk = Palette.Hex("#b4521f");
        public static readonly Color RoseInk = Palette.Hex("#b8325c");
        public static readonly Color LeafInk = Palette.Hex("#3d7a30");
        public static readonly Color SkyInk = Palette.Hex("#2f5fb0");

        const float ReferenceWidth = 1920, ReferenceHeight = 1080;
        const float SmallestDesktopScale = .8f; // short windows (1267×619) keep text comfortable instead of shrinking with them

        static Font body, bold, title;
        public static Font Body => body ? body : body = LoadFont("Fredoka-Medium");
        public static Font Bold => bold ? bold : bold = LoadFont("Fredoka-Bold");
        public static Font Title => title ? title : title = LoadFont("Fraunces-SemiBold");

        static Font LoadFont(string name)
        {
            var f = Resources.Load<Font>("BookBuddies/Fonts/" + name);
            return f ? f : Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        static float scale = 1;
        static int scaleFrame = -1;

        /// <summary>
        /// Screen pixels per reference pixel: 1 at 1920×1080, 1.33 at 2560×1440, never below 0.8 on short windows;
        /// phones use about one density-independent pixel per unit. Times the UI size setting.
        /// </summary>
        public static float ScreenScale()
        {
            if (scaleFrame == Time.frameCount) return scale;
            scaleFrame = Time.frameCount;
            float k = Application.isMobilePlatform && Screen.dpi > 0
                ? Mathf.Max(1, Screen.dpi / 160f) * .9f
                : Mathf.Max(Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight), SmallestDesktopScale);
            return scale = k * GameSettings.UiScale;
        }

        /// <summary>Smooth start and end, for anything that glides (0 to 1 in, 0 to 1 out).</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }

        /// <summary>Quick start, soft landing.</summary>
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }

        /// <summary>Overshoots a little and settles, for things that pop in.</summary>
        public static float EaseBack(float t) { t = Mathf.Clamp01(t) - 1; return 1 + t * t * (2.7f * t + 1.7f); }

        /// <summary>A screen-space canvas that keeps itself at the right UI size as the window or the UI size setting changes.</summary>
        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            go.AddComponent<CanvasScaler>().scaleFactor = ScreenScale();
            go.AddComponent<UiScaler>();
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Makes sure clicks, taps, keys and gamepads reach the UI, with whichever input setting the project uses.</summary>
        public static void EnsureEventSystem()
        {
            VirtualCursor.Ensure();
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ---- layout ----

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Pins a rect to an anchor point of its parent (0,0 = bottom-left, 1,1 = top-right).</summary>
        public static RectTransform Pin(this RectTransform r, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = offset;
            r.sizeDelta = size;
            return r;
        }

        /// <summary>Fills the parent, inset by the given margin.</summary>
        /// <summary>
        /// Adds a custom-drawn Graphic that clicks pass through. Unity doesn't add the CanvasRenderer for
        /// Graphics declared as nested classes, so it is added first (without it uGUI throws every frame).
        /// </summary>
        public static T Painted<T>(this RectTransform r) where T : Graphic
        {
            r.gameObject.AddComponent<CanvasRenderer>();
            var g = r.gameObject.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        public static RectTransform Fill(this RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        public static HorizontalLayoutGroup Row(RectTransform r, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.padding = padding ?? new RectOffset(); g.childAlignment = align;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            return g;
        }

        public static VerticalLayoutGroup Column(RectTransform r, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var g = r.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.padding = padding ?? new RectOffset(); g.childAlignment = align;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = true; g.childForceExpandHeight = false;
            return g;
        }

        /// <summary>Lets a layout group size this rect to its content.</summary>
        public static void Hug(RectTransform r, bool width = true, bool height = true)
        {
            var f = r.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = width ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = height ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexibleWidth = -1)
        {
            var e = c.GetComponent<LayoutElement>();
            if (!e) e = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) e.preferredWidth = e.minWidth = width;
            if (height >= 0) e.preferredHeight = e.minHeight = height;
            if (flexibleWidth >= 0) e.flexibleWidth = flexibleWidth;
            return e;
        }

        /// <summary>An empty stretch of a row or column, for pushing things apart.</summary>
        public static RectTransform Spacer(Transform parent, float size = 0, float flexible = 1)
        {
            var r = Node("space", parent);
            var e = r.gameObject.AddComponent<LayoutElement>();
            e.preferredWidth = e.preferredHeight = size;
            e.flexibleWidth = flexible;
            return r;
        }

        /// <summary>
        /// A scrolling column: a masked viewport with a content column that hugs its children. Put things in the
        /// returned content. The mouse wheel, dragging and the gamepad's right stick scroll it.
        /// </summary>
        public static RectTransform ScrollColumn(Transform parent, float spacing, RectOffset padding, out ScrollRect scroll)
        {
            var view = Node("view", parent);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            var content = Node("content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = Vector2.zero;
            Column(content, spacing, padding);
            Hug(content, false, true);
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return content;
        }

        // ---- pieces ----

        /// <summary>A rounded panel. radius 0 gives a plain rectangle; a radius of half the height gives a pill.</summary>
        public static Image Panel(Transform parent, string name, Color color, int radius = CardRadius)
        {
            var r = Node(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            if (radius > 0) { img.sprite = Rounded(radius); img.type = Image.Type.Sliced; }
            return img;
        }

        /// <summary>A thin rounded line around a panel (an ink rule, a quiet button's edge, a focus ring).</summary>
        public static Image Outline(Component around, Color color, int radius, int width = 2, float outset = 0)
        {
            var r = Node("outline", around.transform).Fill(-outset);
            r.SetAsFirstSibling();
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Ring(radius + Mathf.RoundToInt(outset), width);
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            Size(img).ignoreLayout = true;
            return img;
        }

        /// <summary>
        /// A soft drop shadow under a panel: offset down, blurred, warm brown like the site's.
        /// It sits just behind the panel (UI children draw on top of their parent) and follows it around.
        /// </summary>
        public static Image Shadow(RectTransform under, int radius = CardRadius, float blur = 14, float drop = 5, float alpha = .26f)
        {
            var r = Node(under.name + " shadow", under.parent);
            r.SetSiblingIndex(under.GetSiblingIndex());
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = SoftRounded(radius, (int)blur);
            img.type = Image.Type.Sliced;
            img.color = new Color(.24f, .16f, .04f, alpha);
            img.raycastTarget = false;
            Size(img).ignoreLayout = true;
            var follow = r.gameObject.AddComponent<ShadowFollow>();
            follow.Target = under; follow.Blur = blur; follow.Drop = drop;
            return img;
        }

        sealed class ShadowFollow : MonoBehaviour
        {
            public RectTransform Target;
            public float Blur, Drop;
            readonly Vector3[] corners = new Vector3[4];
            Image image;

            void LateUpdate()
            {
                if (!Target) { Destroy(gameObject); return; }
                if (!image) image = GetComponent<Image>();
                image.enabled = Target.gameObject.activeInHierarchy;
                if (!image.enabled) return;
                var me = (RectTransform)transform;
                var parent = (RectTransform)me.parent;
                Target.GetWorldCorners(corners);
                Vector2 min = parent.InverseTransformPoint(corners[0]), max = parent.InverseTransformPoint(corners[2]);
                me.anchorMin = me.anchorMax = parent.pivot;
                me.pivot = Vector2.zero;
                me.anchoredPosition = min - new Vector2(Blur, Blur + Drop);
                me.sizeDelta = max - min + new Vector2(Blur * 2, Blur * 2);
                me.localRotation = Quaternion.identity;
            }
        }

        public static Text Label(Transform parent, string text, int size, Color color, Font font = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var r = Node("text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font ? font : Body;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f;
            return t;
        }

        /// <summary>A small coloured pill with a word in it ("Admin", "Muted").</summary>
        public static Image Badge(Transform parent, string text, Color ink)
        {
            var pill = Panel(parent, text, ink.WithAlpha(.14f), 13);
            pill.raycastTarget = false;
            Row(pill.rectTransform, 0, new RectOffset(10, 10, 3, 4), TextAnchor.MiddleCenter);
            Size(pill, -1, 26);
            Label(pill.transform, text, SmallSize, ink, Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            return pill;
        }

        /// <summary>An emoji drawn as an image (from UI/Emotes). Hidden when there is no art for it.</summary>
        public static Image Icon(Transform parent, string emoji, float size)
        {
            var r = Node("icon", parent);
            var img = r.gameObject.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            SetIcon(img, emoji);
            Size(img, size, size);
            r.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static void SetIcon(Image img, string emoji)
        {
            img.sprite = Art.Emote(emoji);
            img.enabled = img.sprite != null;
        }

        // hover darkens warmly, pressing darker still; focus is the amber ring (ControlFeel), disabled fades (ControlFeel)
        static readonly ColorBlock Tints = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(.96f, .92f, .85f),
            pressedColor = new Color(.88f, .8f, .68f),
            selectedColor = Color.white,
            disabledColor = Color.white,
            colorMultiplier = 1,
            fadeDuration = .08f,
        };

        /// <summary>A rounded button with the shared feel. Give it your own children, or use TextButton for an icon and words.</summary>
        public static Button Button(Transform parent, string name, Color color, System.Action onClick, int radius = 12)
        {
            var img = Panel(parent, name, color, radius);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = Tints;
            Feel(b, radius);
            if (onClick != null) b.onClick.AddListener(() => { Sound.Play("tap"); onClick(); });
            return b;
        }

        /// <summary>Gives a control the shared hover, press, focus and disabled behaviour.</summary>
        public static ControlFeel Feel(Selectable control, int radius, bool lift = true)
        {
            var feel = control.gameObject.AddComponent<ControlFeel>();
            feel.Ring = Outline(control, Palette.Amber, radius, 3, 5);
            feel.Ring.enabled = false;
            feel.Lift = lift;
            return feel;
        }

        /// <summary>Button with an optional emoji icon and a text label, sized to fit.</summary>
        public static Button TextButton(Transform parent, string label, string emoji, Color color, Color textColor, System.Action onClick, int height = ButtonHeight)
        {
            var b = Button(parent, label, color, onClick, height / 2);
            var r = (RectTransform)b.transform;
            bool icon = emoji != null && Art.Emote(emoji) != null;
            Row(r, 8, new RectOffset(icon ? 14 : 20, 20, 0, 0), TextAnchor.MiddleCenter);
            if (icon) Icon(r, emoji, height * .55f);
            var t = Label(r, label, height >= 44 ? BodySize : SmallSize + 1, textColor, Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(b, -1, height);
            return b;
        }

        /// <summary>The one main action on a card: amber, ink text.</summary>
        public static Button Primary(Transform parent, string label, System.Action onClick, string emoji = null, int height = ButtonHeight) =>
            TextButton(parent, label, emoji, Palette.Amber, Palette.Ink, onClick, height);

        /// <summary>A quiet action: cream with a soft ink edge.</summary>
        public static Button Secondary(Transform parent, string label, System.Action onClick, string emoji = null, int height = ButtonHeight)
        {
            var b = TextButton(parent, label, emoji, Palette.Cream, Palette.Ink, onClick, height);
            Outline(b, Palette.Ink.WithAlpha(.16f), height / 2);
            return b;
        }

        /// <summary>Something that can't be undone: rose edge and words. Pair it with a confirm step.</summary>
        public static Button Danger(Transform parent, string label, System.Action onClick, string emoji = null, int height = ButtonHeight)
        {
            var b = TextButton(parent, label, emoji, Palette.Cream, RoseInk, onClick, height);
            Outline(b, Palette.Rose.WithAlpha(.55f), height / 2);
            return b;
        }

        /// <summary>A rose button that asks once more ("sure" replaces its words) before doing something that can't be undone.</summary>
        public static Button ConfirmButton(Transform parent, string label, string sure, System.Action act, int height = ButtonHeight)
        {
            var b = Danger(parent, label, null, null, height);
            var text = b.GetComponentInChildren<Text>();
            bool armed = false;
            b.onClick.AddListener(() =>
            {
                Sound.Play("tap");
                if (armed) { act(); return; }
                armed = true;
                text.text = sure;
                text.color = Palette.Cream;
                ((Image)b.targetGraphic).color = RoseInk;
            });
            return b;
        }

        /// <summary>An arrow drawn with three rounded bars, pointing right (rotate the returned rect to aim it), 24 units across.</summary>
        public static RectTransform Arrow(Transform parent, Color color)
        {
            var arrow = Node("arrow", parent).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(24, 24));
            ArrowBar(arrow, color, new Vector2(-2, 0), 16, 0);     // shaft
            ArrowBar(arrow, color, new Vector2(4, 3.6f), 11, -40); // head
            ArrowBar(arrow, color, new Vector2(4, -3.6f), 11, 40);
            return arrow;
        }

        static void ArrowBar(RectTransform arrow, Color color, Vector2 at, float length, float angle)
        {
            var bar = Panel(arrow, "bar", color, 2);
            bar.raycastTarget = false;
            bar.rectTransform.Pin(new Vector2(.5f, .5f), at, new Vector2(length, 3.5f)).localRotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>A thin ink line between groups of rows.</summary>
        public static Image Rule(Transform parent)
        {
            var line = Panel(parent, "rule", Palette.Ink.WithAlpha(.1f), 0);
            line.raycastTarget = false;
            Size(line, -1, 1);
            return line;
        }

        /// <summary>The round ✕ in a card's corner.</summary>
        public static Button CloseButton(Transform parent, System.Action onClick)
        {
            var b = Button(parent, "Close", Palette.Paper, onClick, 22);
            Size(b, 44, 44);
            foreach (float angle in new[] { 45f, -45f })
            {
                var bar = Panel(b.transform, "x", Palette.Ink, 1).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 3));
                bar.localRotation = Quaternion.Euler(0, 0, angle);
                bar.GetComponent<Image>().raycastTarget = false;
            }
            return b;
        }

        /// <summary>A card's title row: the title on the left and a close button on the right (when onClose is given).</summary>
        public static RectTransform Header(Transform parent, string title, System.Action onClose, int size = TitleSize - 2)
        {
            var row = Node("header", parent);
            Row(row, 12);
            var t = Label(row, title, size, Palette.Ink, Title);
            Size(t, -1, 44, 1);
            if (onClose != null) CloseButton(row, onClose);
            return row;
        }

        /// <summary>A square-ish tile: big emoji over a small caption. Used in the emote, trick and interaction trays.</summary>
        public static Button Tile(Transform parent, string emoji, string caption, System.Action onClick)
        {
            var b = Button(parent, caption ?? emoji, Palette.Paper, onClick, 12);
            var r = (RectTransform)b.transform;
            Column(r, 2, new RectOffset(4, 4, 8, 6), TextAnchor.MiddleCenter).childForceExpandWidth = false;
            Icon(r, emoji, caption != null ? 36 : 42);
            if (caption != null)
            {
                var t = Label(r, caption, SmallSize, Palette.Ink, Bold, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            return b;
        }

        /// <summary>A white text box with an ink edge (a pill when radius is half its height).</summary>
        public static InputField Input(Transform parent, string placeholder, int fontSize = BodySize, int radius = 12)
        {
            var bg = Panel(parent, "input", Color.white, radius);
            var field = bg.gameObject.AddComponent<InputField>();
            field.colors = Tints;
            field.targetGraphic = bg;
            Outline(bg, Palette.Ink.WithAlpha(.18f), radius);
            Feel(field, radius, false);
            var text = Label(bg.transform, "", fontSize, Palette.Ink);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            ((RectTransform)text.transform).Fill().offsetMin = new Vector2(16, 0);
            ((RectTransform)text.transform).offsetMax = new Vector2(-16, 0);
            var hint = Label(bg.transform, placeholder, fontSize, Palette.InkSoft);
            ((RectTransform)hint.transform).Fill().offsetMin = new Vector2(16, 0);
            ((RectTransform)hint.transform).offsetMax = new Vector2(-16, 0);
            field.textComponent = text;
            field.placeholder = hint;
            field.caretColor = Palette.Ember;
            field.caretWidth = 2;
            field.selectionColor = Palette.Amber.WithAlpha(.45f);
            field.customCaretColor = true;
            return field;
        }

        // ---- settings controls ----

        /// <summary>A rounded slider from 0 to 1 with an amber fill and a round handle. Works with the d-pad and the cursor too.</summary>
        public static Slider Slider(Transform parent, float value, System.Action<float> changed, float width = 240)
        {
            var root = Node("slider", parent);
            Size(root, width, 44);
            root.gameObject.AddComponent<Image>().color = Color.clear; // the whole row is the hit area
            var track = Panel(root, "track", Palette.Paper, 6);
            ((RectTransform)track.transform).Fill().offsetMin = new Vector2(0, 16);
            ((RectTransform)track.transform).offsetMax = new Vector2(0, -16);
            Outline(track, Palette.Ink.WithAlpha(.12f), 6, 1);
            var fillArea = Node("fill area", root).Fill();
            fillArea.offsetMin = new Vector2(0, 16); fillArea.offsetMax = new Vector2(0, -16);
            var fill = Panel(fillArea, "fill", Palette.Amber, 6);
            var handleArea = Node("handle area", root).Fill();
            handleArea.offsetMin = new Vector2(14, 8); handleArea.offsetMax = new Vector2(-14, -8);
            var handle = Panel(handleArea, "handle", Color.white, 14);
            handle.rectTransform.sizeDelta = new Vector2(28, 0);
            Outline(handle, Palette.Ink.WithAlpha(.2f), 14, 1);
            var ring = Panel(handle.transform, "dot", Palette.Ember, 5);
            ((RectTransform)ring.transform).Fill(9);
            ring.raycastTarget = false;

            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.colors = Tints;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            Feel(s, 22, false);
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(v => changed(v));
            return s;
        }

        /// <summary>An on/off switch: a pill with a knob that slides, amber when on. The hit area is a full 44 pixels tall.</summary>
        public static Button Switch(Transform parent, bool on, System.Action<bool> changed)
        {
            var b = Button(parent, "switch", Color.clear, null, 16);
            Size(b, 76, 44);
            var track = Panel(b.transform, "track", Palette.Paper, 16);
            track.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(64, 32));
            track.raycastTarget = false;
            b.targetGraphic = track;
            var knob = Panel(track.transform, "knob", Color.white, 13).rectTransform;
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0, .5f);
            knob.sizeDelta = new Vector2(26, 26);
            Outline(knob.GetComponent<Image>(), Palette.Ink.WithAlpha(.15f), 13, 1);
            void Paint() { track.color = on ? Palette.Amber : Palette.InkSoft.WithAlpha(.3f); knob.anchoredPosition = new Vector2(on ? 35 : 3, 0); }
            Paint();
            b.onClick.AddListener(() => { on = !on; Paint(); Sound.Play(on ? "open" : "close"); changed(on); });
            return b;
        }

        /// <summary>‹ value › — steps through a short list of choices.</summary>
        public static RectTransform Choice(Transform parent, string[] options, int index, System.Action<int> changed, float width = 260)
        {
            var row = Node("choice", parent);
            Row(row, 6, null, TextAnchor.MiddleCenter);
            Size(row, width, 44);
            Text value = null;
            void Step(int by) { index = (index + by + options.Length) % options.Length; value.text = options[index]; changed(index); }
            var prev = Secondary(row, "‹", () => Step(-1), null, 44);
            Size(prev, 44, 44);
            value = Label(row, options[Mathf.Clamp(index, 0, options.Length - 1)], BodySize - 1, Palette.Ink, Bold, TextAnchor.MiddleCenter);
            Size(value, -1, -1, 1);
            var next = Secondary(row, "›", () => Step(1), null, 44);
            Size(next, 44, 44);
            return row;
        }

        /// <summary>
        /// Tabs that share a paper track, with LB and RB at its ends while a gamepad is in use. "pick" runs with the
        /// tab's index; mark the chosen one with SelectTab.
        /// </summary>
        public static Button[] Tabs(Transform parent, string[] names, System.Action<int> pick)
        {
            var track = Panel(parent, "tabs", Palette.Paper, 26);
            Row(track.rectTransform, 4, new RectOffset(6, 6, 4, 4));
            Size(track, -1, 52);
            PadGlyph(track.transform, "LB").gameObject.AddComponent<GamepadOnly>();
            var tabs = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                tabs[i] = TextButton(track.transform, names[i], null, Palette.Paper, Palette.Ink, () => pick(index), 44);
                Size(tabs[i], -1, 44, 1);
            }
            PadGlyph(track.transform, "RB").gameObject.AddComponent<GamepadOnly>();
            return tabs;
        }

        /// <summary>Marks the chosen tab: amber and full ink; the others stay quiet paper with softer words.</summary>
        public static void SelectTab(Button[] tabs, int index)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                ((Image)tabs[i].targetGraphic).color = i == index ? Palette.Amber : Palette.Paper;
                tabs[i].GetComponentInChildren<Text>().color = i == index ? Palette.Ink : Palette.InkSoft;
            }
        }

        // ---- controller hints ----

        /// <summary>A gamepad button drawn as a small coloured disc: A, B, X, Y, or a pill for LB, RB and the like.</summary>
        public static Image PadGlyph(Transform parent, string button, int size = 24)
        {
            bool round = button.Length == 1;
            var fill = button == "A" ? LeafInk : button == "B" ? RoseInk : button == "X" ? SkyInk : button == "Y" ? Palette.Amber : Palette.Cream;
            var disc = Panel(parent, button, fill, size / 2);
            disc.raycastTarget = false;
            Size(disc, round ? size : size + 14, size);
            var t = Label(disc.transform, button, size - 10, button == "Y" || !round ? Palette.Ink : Palette.Cream, Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            ((RectTransform)t.transform).Fill();
            return disc;
        }

        /// <summary>
        /// "Ⓐ Select  Ⓑ Back": a little ink tab on the bottom edge of a card that shows only while the player is on a gamepad.
        /// Pairs are a button ("A", "B", "LB/RB"…) and what it does.
        /// </summary>
        public static RectTransform PadHints(RectTransform card, params (string button, string what)[] hints)
        {
            var pill = Panel(card, "pad hints", Palette.Ink, 18);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(1, 0), new Vector2(-20, 0), new Vector2(10, 36));
            r.pivot = new Vector2(1, .5f);
            Size(pill).ignoreLayout = true;
            Row(r, 8, new RectOffset(8, 16, 0, 0), TextAnchor.MiddleCenter);
            Hug(r, true, false);
            for (int i = 0; i < hints.Length; i++)
            {
                if (i > 0) Spacer(r, 6, 0);
                foreach (var b in hints[i].button.Split('/')) PadGlyph(r, b);
                Label(r, hints[i].what, SmallSize, Palette.Cream, Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            r.gameObject.AddComponent<GamepadOnly>();
            return r;
        }

        public static void Show(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>
        /// Splits a message for display: the font can't draw colour emoji, so the first emoji with art becomes an
        /// icon and the rest are dropped. "💛 Pip said hi" gives icon 💛 and text "Pip said hi".
        /// </summary>
        public static string SplitEmoji(string text, out string icon)
        {
            icon = null;
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int len = EmojiLength(text, i);
                if (len == 0) { sb.Append(text[i]); continue; }
                if (icon == null)
                {
                    string e = text.Substring(i, len);
                    if (Art.Emote(e) != null) icon = e;
                    else if (Art.Emote(e + "️") != null) icon = e + "️";
                }
                i += len - 1;
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        }

        // Length of the emoji starting at i (with variation selectors and joiners), or 0 if text[i] isn't one.
        static int EmojiLength(string s, int i)
        {
            int cp = CodePoint(s, i, out int len);
            bool emoji = cp >= 0x1F000 || (cp >= 0x2600 && cp <= 0x27BF) || (cp >= 0x2B00 && cp <= 0x2BFF) || cp == 0x2763 || (cp >= 0x2190 && cp <= 0x21FF && i + len < s.Length && s[i + len] == '️');
            if (!emoji) return 0;
            int j = i + len;
            while (j < s.Length)
            {
                if (s[j] == '️') { j++; continue; }
                if (s[j] == '\uD83C' && j + 1 < s.Length && s[j + 1] >= '\uDFFB' && s[j + 1] <= '\uDFFF') { j += 2; continue; } // skin tone
                if (s[j] == '‍' && j + 1 < s.Length) { int n = char.IsHighSurrogate(s[j + 1]) ? 2 : 1; j += 1 + n; continue; }
                break;
            }
            return j - i;
        }

        static int CodePoint(string s, int i, out int len)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { len = 2; return char.ConvertToUtf32(s[i], s[i + 1]); }
            len = 1; return s[i];
        }

        // ---- shapes made in code ----

        static Sprite vignette, glow;

        /// <summary>Clear in the middle, warm and dark at the edges. Stretch it over the screen.</summary>
        public static Sprite Vignette => vignette ? vignette : vignette = Radial(128, (d) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - .45f) / .6f)));

        /// <summary>A soft round glow, bright in the middle.</summary>
        public static Sprite Glow => glow ? glow : glow = Radial(128, (d) => Mathf.Pow(Mathf.Clamp01(1 - d), 2));

        static Sprite Radial(int size, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha(Mathf.Sqrt(dx * dx + dy * dy)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
        }

        /// <summary>A full-screen image (a veil, a flash, a vignette) that never blocks taps unless asked.</summary>
        public static Image Cover(Transform parent, string name, Color color, Sprite sprite = null, bool blocks = false)
        {
            var img = Node(name, parent).Fill().gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = blocks;
            return img;
        }

        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Sprite> soft = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();

        /// <summary>A 9-sliced rounded rectangle with smooth edges.</summary>
        public static Sprite Rounded(int radius)
        {
            if (rounded.TryGetValue(radius, out var s)) return s;
            return rounded[radius] = RoundRect(radius, 0, 0);
        }

        /// <summary>A 9-sliced rounded rectangle that fades out over "blur" pixels, for shadows.</summary>
        public static Sprite SoftRounded(int radius, int blur)
        {
            int key = radius * 1000 + blur;
            if (soft.TryGetValue(key, out var s)) return s;
            return soft[key] = RoundRect(radius, blur, 0);
        }

        /// <summary>The outline of a rounded rectangle, "width" pixels thick, 9-sliced.</summary>
        public static Sprite Ring(int radius, int width)
        {
            int key = radius * 1000 + width;
            if (rings.TryGetValue(key, out var s)) return s;
            return rings[key] = RoundRect(radius, 0, width);
        }

        static Sprite RoundRect(int radius, int blur, int stroke)
        {
            radius = Mathf.Max(radius, 1);
            int edge = radius + blur, size = edge * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size / 2f, inner = c - edge; // half-size of the straight middle
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + .5f - c) - inner, 0), dy = Mathf.Max(Mathf.Abs(y + .5f - c) - inner, 0);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy); // from the nearest corner's centre
                    float a = blur > 0
                        ? 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((dist - radius + blur * .5f) / (blur * 1.5f)))
                        : Mathf.Clamp01(radius - dist + .5f);
                    if (stroke > 0) a *= Mathf.Clamp01(dist - (radius - stroke) + .5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
        }
    }

    /// <summary>Keeps its canvas at UiKit.ScreenScale, so the UI follows window resizes, F11 and the UI size setting as they happen.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class UiScaler : MonoBehaviour
    {
        CanvasScaler scaler;

        void Awake() => scaler = GetComponent<CanvasScaler>();

        void Update()
        {
            float k = UiKit.ScreenScale();
            if (!Mathf.Approximately(scaler.scaleFactor, k)) scaler.scaleFactor = k;
        }
    }

    /// <summary>
    /// How every control answers the player: it lifts a touch under the mouse or cursor, dips when pressed,
    /// wears an amber ring while it has keyboard focus, and fades while it can't be used.
    /// </summary>
    public sealed class ControlFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Image Ring;
        public bool Lift = true;

        Selectable control;
        CanvasGroup group;
        bool over, pressed, focused;
        float scale = 1;

        void Awake()
        {
            control = GetComponent<Selectable>();
            group = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnPointerEnter(PointerEventData e) => over = true;
        public void OnPointerExit(PointerEventData e) => over = pressed = false;
        public void OnPointerDown(PointerEventData e) => pressed = true;
        public void OnPointerUp(PointerEventData e) => pressed = false;
        public void OnSelect(BaseEventData e) => focused = true;
        public void OnDeselect(BaseEventData e) => focused = false;

        void OnDisable()
        {
            over = pressed = false;
            scale = 1;
            transform.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            bool usable = control && control.IsInteractable();
            bool ring = usable && focused && !PlazaInput.UsingPointer && !VirtualCursor.Active;
            if (Ring && Ring.enabled != ring) Ring.enabled = ring;

            float alpha = usable ? 1 : .45f;
            if (!Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;

            var size = ((RectTransform)transform).rect.size;
            float grow = Lift ? Mathf.Min(.05f, 5f / Mathf.Max(40, Mathf.Max(size.x, size.y))) : 0;
            float target = !usable ? 1 : pressed ? 1 - grow : over || ring ? 1 + grow : 1;
            scale = Mathf.MoveTowards(scale, target, Time.unscaledDeltaTime * .6f);
            if (!Mathf.Approximately(transform.localScale.x, scale)) transform.localScale = new Vector3(scale, scale, 1);
        }
    }

    /// <summary>Shows its children only while the player is on a gamepad (button hints), fading in and out.</summary>
    public sealed class GamepadOnly : MonoBehaviour
    {
        CanvasGroup group;

        void Awake()
        {
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            group.alpha = PlazaInput.UsingGamepad ? 1 : 0;
        }

        void Update()
        {
            float a = Mathf.MoveTowards(group.alpha, PlazaInput.UsingGamepad ? 1 : 0, Time.unscaledDeltaTime * 6);
            if (!Mathf.Approximately(a, group.alpha)) group.alpha = a;
        }
    }
}
