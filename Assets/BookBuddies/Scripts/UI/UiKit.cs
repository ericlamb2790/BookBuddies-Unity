using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Builds the HUD in code, in the Plaza's look: cream cards, ink text, amber accents, soft round corners.
    /// Fonts live in Resources/BookBuddies/Fonts; swap the TTF files to change the type.
    /// </summary>
    public static class UiKit
    {
        public const int CardRadius = 14;

        static Font body, bold, title;
        public static Font Body => body ? body : body = LoadFont("Fredoka-Medium");
        public static Font Bold => bold ? bold : bold = LoadFont("Fredoka-Bold");
        public static Font Title => title ? title : title = LoadFont("Fraunces-SemiBold");

        static Font LoadFont(string name)
        {
            var f = Resources.Load<Font>("BookBuddies/Fonts/" + name);
            return f ? f : Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        /// <summary>UI size: about one website pixel per unit on phones, a little larger on big monitors, times the UI size setting.</summary>
        public static float ScreenScale()
        {
            float k = Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Max(1, Screen.dpi / 160f) : Mathf.Clamp(Screen.height / 900f, 1f, 3f);
            return k * GameSettings.UiScale;
        }

        /// <summary>Smooth start and end, for anything that glides (0 to 1 in, 0 to 1 out).</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }

        /// <summary>Quick start, soft landing.</summary>
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }

        /// <summary>Overshoots a little and settles, for things that pop in.</summary>
        public static float EaseBack(float t) { t = Mathf.Clamp01(t) - 1; return 1 + t * t * (2.7f * t + 1.7f); }

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            go.AddComponent<CanvasScaler>().scaleFactor = ScreenScale();
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Makes sure clicks, taps, keys and gamepads reach the UI, with whichever input setting the project uses.</summary>
        public static void EnsureEventSystem()
        {
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

        static readonly ColorBlock Tints = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1, .95f, .86f),
            pressedColor = new Color(.93f, .8f, .6f),
            selectedColor = new Color(1, .78f, .36f),   // gamepad and keyboard focus: amber
            disabledColor = Color.white,                 // buttons behind an open menu look unchanged
            colorMultiplier = 1,
            fadeDuration = .08f,
        };

        /// <summary>A rounded button. Add a label and icon with ButtonLabel or your own children.</summary>
        public static Button Button(Transform parent, string name, Color color, System.Action onClick, int radius = 12)
        {
            var img = Panel(parent, name, color, radius);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = Tints;
            if (onClick != null) b.onClick.AddListener(() => { Sound.Play("tap"); onClick(); });
            return b;
        }

        /// <summary>Button with an optional emoji icon and a text label, sized to fit.</summary>
        public static Button TextButton(Transform parent, string label, string emoji, Color color, Color textColor, System.Action onClick, int height = 40)
        {
            var b = Button(parent, label, color, onClick, height / 2);
            var r = (RectTransform)b.transform;
            Row(r, 6, new RectOffset(emoji != null ? 10 : 16, 16, 0, 0), TextAnchor.MiddleCenter);
            if (emoji != null) Icon(r, emoji, height * .55f);
            var t = Label(r, label, 16, textColor, Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(b, -1, height);
            return b;
        }

        /// <summary>A square-ish tile: big emoji over a small caption. Used in the emote, trick and interaction trays.</summary>
        public static Button Tile(Transform parent, string emoji, string caption, System.Action onClick)
        {
            var b = Button(parent, caption ?? emoji, Palette.Cream, onClick, 12);
            var r = (RectTransform)b.transform;
            Column(r, 2, new RectOffset(4, 4, 6, 4), TextAnchor.MiddleCenter).childForceExpandWidth = false;
            Icon(r, emoji, caption != null ? 30 : 34);
            if (caption != null)
            {
                var t = Label(r, caption, 13, Palette.Ink, Bold, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, int fontSize = 16)
        {
            var bg = Panel(parent, "input", Color.white, 12);
            var field = bg.gameObject.AddComponent<InputField>();
            field.colors = Tints;
            field.targetGraphic = bg;
            var text = Label(bg.transform, "", fontSize, Palette.Ink);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            ((RectTransform)text.transform).Fill().offsetMin = new Vector2(14, 0);
            ((RectTransform)text.transform).offsetMax = new Vector2(-14, 0);
            var hint = Label(bg.transform, placeholder, fontSize, Palette.InkSoft);
            ((RectTransform)hint.transform).Fill().offsetMin = new Vector2(14, 0);
            ((RectTransform)hint.transform).offsetMax = new Vector2(-14, 0);
            field.textComponent = text;
            field.placeholder = hint;
            field.caretColor = Palette.Ember;
            field.caretWidth = 2;
            field.selectionColor = Palette.Amber.WithAlpha(.45f);
            field.customCaretColor = true;
            return field;
        }

        // ---- settings controls ----

        /// <summary>A rounded slider from 0 to 1 with an amber fill and a round handle. Works with the d-pad too.</summary>
        public static Slider Slider(Transform parent, float value, System.Action<float> changed, float width = 220)
        {
            var root = Node("slider", parent);
            Size(root, width, 30);
            var track = Panel(root, "track", Palette.Paper, 5);
            ((RectTransform)track.transform).Fill().offsetMin = new Vector2(0, 10);
            ((RectTransform)track.transform).offsetMax = new Vector2(0, -10);
            var fillArea = Node("fill area", root).Fill();
            fillArea.offsetMin = new Vector2(0, 10); fillArea.offsetMax = new Vector2(0, -10);
            var fill = Panel(fillArea, "fill", Palette.Amber, 5);
            var handleArea = Node("handle area", root).Fill();
            handleArea.offsetMin = new Vector2(11, 4); handleArea.offsetMax = new Vector2(-11, -4);
            var handle = Panel(handleArea, "handle", Color.white, 11);
            handle.rectTransform.sizeDelta = new Vector2(22, 0);
            var ring = Panel(handle.transform, "ring", Palette.Ember, 5);
            ((RectTransform)ring.transform).Fill(6);
            ring.raycastTarget = false;

            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.colors = Tints;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(v => changed(v));
            return s;
        }

        /// <summary>An on/off switch: a pill with a knob that slides, amber when on.</summary>
        public static Button Switch(Transform parent, bool on, System.Action<bool> changed)
        {
            var b = Button(parent, "switch", Palette.Paper, null, 14);
            Size(b, 52, 28);
            var knob = Panel(b.transform, "knob", Color.white, 11).rectTransform;
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0, .5f);
            knob.sizeDelta = new Vector2(22, 22);
            var track = (Image)b.targetGraphic;
            void Paint() { track.color = on ? Palette.Amber : Palette.InkSoft.WithAlpha(.35f); knob.anchoredPosition = new Vector2(on ? 27 : 3, 0); }
            Paint();
            b.onClick.AddListener(() => { on = !on; Paint(); Sound.Play(on ? "open" : "close"); changed(on); });
            return b;
        }

        /// <summary>‹ value › — steps through a short list of choices.</summary>
        public static RectTransform Choice(Transform parent, string[] options, int index, System.Action<int> changed, float width = 220)
        {
            var row = Node("choice", parent);
            Row(row, 6, null, TextAnchor.MiddleCenter);
            Size(row, width, 34);
            Text value = null;
            void Step(int by) { index = (index + by + options.Length) % options.Length; value.text = options[index]; changed(index); }
            var prev = TextButton(row, "‹", null, Palette.Paper, Palette.Ink, () => Step(-1), 34);
            Size(prev, 34, 34);
            value = Label(row, options[Mathf.Clamp(index, 0, options.Length - 1)], 15, Palette.Ink, Bold, TextAnchor.MiddleCenter);
            Size(value, -1, -1, 1);
            var next = TextButton(row, "›", null, Palette.Paper, Palette.Ink, () => Step(1), 34);
            Size(next, 34, 34);
            return row;
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
                    else if (Art.Emote(e + "\uFE0F") != null) icon = e + "\uFE0F";
                }
                i += len - 1;
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        }

        // Length of the emoji starting at i (with variation selectors and joiners), or 0 if text[i] isn't one.
        static int EmojiLength(string s, int i)
        {
            int cp = CodePoint(s, i, out int len);
            bool emoji = cp >= 0x1F000 || (cp >= 0x2600 && cp <= 0x27BF) || (cp >= 0x2B00 && cp <= 0x2BFF) || cp == 0x2763 || (cp >= 0x2190 && cp <= 0x21FF && i + len < s.Length && s[i + len] == '\uFE0F');
            if (!emoji) return 0;
            int j = i + len;
            while (j < s.Length)
            {
                if (s[j] == '\uFE0F') { j++; continue; }
                if (s[j] == '\uD83C' && j + 1 < s.Length && s[j + 1] >= '\uDFFB' && s[j + 1] <= '\uDFFF') { j += 2; continue; } // skin tone
                if (s[j] == '\u200D' && j + 1 < s.Length) { int n = char.IsHighSurrogate(s[j + 1]) ? 2 : 1; j += 1 + n; continue; }
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

        /// <summary>A 9-sliced rounded rectangle with smooth edges.</summary>
        public static Sprite Rounded(int radius)
        {
            if (rounded.TryGetValue(radius, out var s)) return s;
            return rounded[radius] = RoundRect(radius, 0);
        }

        /// <summary>A 9-sliced rounded rectangle that fades out over "blur" pixels, for shadows.</summary>
        public static Sprite SoftRounded(int radius, int blur)
        {
            int key = radius * 1000 + blur;
            if (soft.TryGetValue(key, out var s)) return s;
            return soft[key] = RoundRect(radius, blur);
        }

        static Sprite RoundRect(int radius, int blur)
        {
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
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
        }
    }
}
