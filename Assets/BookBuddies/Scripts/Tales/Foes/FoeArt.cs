using System.Collections.Generic;
using System.Text;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if BB_VECTOR
using System.IO;
using Unity.VectorGraphics;
#endif

namespace BookBuddies.Tales
{
    /// <summary>
    /// Draws villains like the site's villainSVG: VillainSvg writes the SVG, Unity Vector Graphics turns it into a
    /// sprite once per look, and the emoji the importer can't draw sit on top as images.
    /// Swappable art: a PNG at Resources/BookBuddies/Foes/&lt;slug&gt;.png replaces the drawing. The slug is the base
    /// villain's name in lower case with every other character turned into '_' (Writer’s Block → writer_s_block).
    /// Draw it as the site's square 100×100 box with the feet 5% up from the bottom; any resolution works.
    /// Without the Vector Graphics package, villains show as a blob in their colour with their emoji.
    /// </summary>
    public static class FoeArt
    {
        /// <summary>Where the feet stand in the box, from the bottom (the site's ground point, y = 95 of 100).</summary>
        public const float FeetY = .05f;

        /// <summary>The ground shadow the SVG drew (rx 30, ry 5), as radii in box units. Callers draw it, unscaled by Look.Scale.</summary>
        public static readonly Vector2 ShadowSize = new Vector2(.3f, .05f);

        const int TextureSize = 640;          // pixels across the box (sharp up to a 1440p boss)
        const float EmojiScale = 128f / 104f; // emote PNGs hold a 104px glyph in a 128px square
        const int CacheLimit = 48;
        static readonly Vector2 ShadowDrop = new Vector2(0, -.02f);           // the emoji's drop-shadow(0 2px …)
        static readonly Vector2 AuraCenter = new Vector2(.5f, .4f), AuraSize = new Vector2(1.16f, 1.1f); // around the body
        const float AuraAlpha = .7f;

        /// <summary>A villain's sprite (pivot at its feet, 1 unit = the site's 100px box) plus emoji drawn on top.</summary>
        public sealed class Look
        {
            public Sprite Body;
            public Overlay[] Overlays = new Overlay[0];
            public Color Aura;              // clear when none
            public float Scale = 1;         // the variant's size (Giant/Tiny), applied about the feet
            public Color Tint = Color.white; // a swapped-in PNG takes the affix's colour this way
            internal Drawing Source;
        }

        /// <summary>
        /// Text the vector importer can't draw: centre in the 100×100 box (y up from the bottom), font size as a fraction
        /// of the box. Glyphs (♥ ❄ ♠ 7) are drawn as text in Color; emoji are images (text if one has no image).
        /// </summary>
        public struct Overlay
        {
            public string Text;
            public Vector2 Center;
            public float Size;
            public bool Glyph, Shadow; // Shadow: the site's soft drop shadow under the emoji
            public Color Color;
        }

        /// <summary>One cached drawing, shared by every villain that looks the same. Users = live MakeWorld/MakeUi copies.</summary>
        internal sealed class Drawing
        {
            public Sprite Body;
            public Overlay[] Overlays;
            public bool OwnsTexture;
            public int Users;
        }

        static readonly Dictionary<string, Drawing> cache = new Dictionary<string, Drawing>();
        static readonly LinkedList<string> recent = new LinkedList<string>();
        static readonly Dictionary<string, Drawing> swapped = new Dictionary<string, Drawing>(); // slug -> PNG (null = none)

        /// <summary>How a villain looks: its drawing (or swapped-in PNG), aura and size.</summary>
        public static Look For(FoeVariant v, bool boss)
        {
            var png = Swapped(v.Def.N);
            var drawing = png ?? Drawn(v, boss);
            string aura = v.Au ?? (boss ? VillainSvg.AutoAura(v) : null);
            return new Look
            {
                Body = drawing.Body,
                Overlays = drawing.Overlays,
                Aura = aura != null ? Palette.Hex(aura) : Color.clear,
                Scale = v.Vs > 0 ? (float)v.Vs : 1,
                Tint = png != null ? AffixTint(v) : Color.white,
                Source = drawing,
            };
        }

        /// <summary>The file name a swapped-in PNG uses: "Writer’s Block" → "writer_s_block".</summary>
        public static string Slug(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name.ToLowerInvariant()) sb.Append(ch < 128 && char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }

        // ---- building ----

        /// <summary>
        /// Builds the villain as world sprites under parent (feet at the parent's origin, the box "height" world units tall).
        /// The result carries a SortingGroup: change its sortingOrder as the villain walks. Flip it with a negative x scale.
        /// </summary>
        public static Transform MakeWorld(Transform parent, Look look, float height, int sortingOrder)
        {
            var root = new GameObject("foe").transform;
            root.SetParent(parent, false);
            root.gameObject.AddComponent<SortingGroup>().sortingOrder = sortingOrder;
            root.gameObject.AddComponent<Holder>().Hold(look.Source);
            var box = new GameObject("art").transform; // 1 unit = the box, feet at the origin
            box.SetParent(root, false);
            box.localScale = Vector3.one * (height * look.Scale);
            if (look.Aura.a > 0) WorldSprite("aura", box, Art.SoftDot, 0, look.Aura.WithAlpha(AuraAlpha), AuraCenter, AuraSize / 2).gameObject.AddComponent<AuraPulse>(); // the soft dot is 2 units across
            WorldSprite("body", box, look.Body, 1, look.Tint, new Vector2(.5f, FeetY), Vector2.one);
            foreach (var o in look.Overlays) WorldOverlay(box, o);
            return root;
        }

        /// <summary>
        /// Builds the villain as UI images filling rect's height (feet at the bottom centre), with the SVG's ground
        /// shadow under it. Returns the art node, pivoted at the feet, for bobs and squashes.
        /// </summary>
        public static RectTransform MakeUi(RectTransform rect, Look look)
        {
            var box = UiKit.Node("foe", rect);
            box.anchorMin = new Vector2(.5f, -FeetY / (1 - FeetY)); // the box hangs below rect by its feet margin
            box.anchorMax = new Vector2(.5f, 1);
            box.pivot = new Vector2(.5f, FeetY);
            box.sizeDelta = box.anchoredPosition = Vector2.zero;
            box.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            box.localScale = Vector3.one * look.Scale;
            box.gameObject.AddComponent<Holder>().Hold(look.Source);

            var shadow = UiImage(box, "shadow", Art.SoftDot, new Color(0, 0, 0, .3f), new Vector2(.5f, FeetY), new Vector2(ShadowSize.x * 2.5f, ShadowSize.y * 3.2f));
            shadow.rectTransform.localScale = Vector3.one / look.Scale; // the site leaves the shadow out of the size change
            if (look.Aura.a > 0) UiImage(box, "aura", Art.SoftDot, look.Aura.WithAlpha(AuraAlpha), AuraCenter, AuraSize).gameObject.AddComponent<AuraPulse>();
            UiImage(box, "body", look.Body, look.Tint, new Vector2(.5f, .5f), Vector2.one).preserveAspect = true;
            foreach (var o in look.Overlays) UiOverlay(box, o);
            return box;
        }

        static void WorldOverlay(Transform box, Overlay o)
        {
            var at = o.Center - new Vector2(.5f, FeetY);
            var emoji = o.Glyph ? null : Art.Emote(o.Text);
            if (emoji == null)
            {
                var text = new GameObject("glyph").AddComponent<TextMesh>();
                text.transform.SetParent(box, false);
                text.transform.localPosition = at;
                text.font = UiKit.Bold;
                text.fontSize = 64;
                text.characterSize = o.Size * 10 / text.fontSize; // TextMesh draws 10 font pixels per unit
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = o.Color;
                text.text = o.Text;
                var mesh = text.GetComponent<MeshRenderer>();
                mesh.sharedMaterial = text.font.material;
                mesh.sortingOrder = 3;
                return;
            }
            var size = Vector2.one * (o.Size * EmojiScale);
            if (o.Shadow) WorldSprite("emoji shadow", box, emoji, 2, new Color(0, 0, 0, .4f), o.Center + ShadowDrop, size);
            WorldSprite("emoji", box, emoji, 3, Color.white, o.Center, size);
        }

        static void UiOverlay(RectTransform box, Overlay o)
        {
            var emoji = o.Glyph ? null : Art.Emote(o.Text);
            if (emoji == null)
            {
                var area = Area(box, "glyph", o.Center, Vector2.one * (o.Size * 1.4f));
                var text = UiKit.Label(area, o.Text, 40, o.Color, UiKit.Bold, TextAnchor.MiddleCenter);
                text.rectTransform.Fill();
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 1;
                text.resizeTextMaxSize = 300;
                return;
            }
            var size = Vector2.one * (o.Size * EmojiScale);
            if (o.Shadow) UiImage(box, "emoji shadow", emoji, new Color(0, 0, 0, .4f), o.Center + ShadowDrop, size);
            UiImage(box, "emoji", emoji, Color.white, o.Center, size);
        }

        /// <summary>A sprite in the box, its pivot at a point given in box fractions (0..1, y up).</summary>
        static SpriteRenderer WorldSprite(string name, Transform box, Sprite sprite, int order, Color color, Vector2 at, Vector2 scale)
        {
            var r = new GameObject(name).AddComponent<SpriteRenderer>();
            r.transform.SetParent(box, false);
            r.transform.localPosition = at - new Vector2(.5f, FeetY);
            r.transform.localScale = new Vector3(scale.x, scale.y, 1);
            r.sprite = sprite;
            r.color = color;
            r.sortingOrder = order;
            return r;
        }

        static Image UiImage(RectTransform box, string name, Sprite sprite, Color color, Vector2 center, Vector2 size)
        {
            var img = Area(box, name, center, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform Area(RectTransform box, string name, Vector2 center, Vector2 size)
        {
            var r = UiKit.Node(name, box);
            r.anchorMin = center - size / 2;
            r.anchorMax = center + size / 2;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        // ---- drawings ----

        static Drawing Drawn(FoeVariant v, bool boss)
        {
            string key = $"{v.Def.N}|{v.Tc}|{v.Xp}|{VillainSvg.FaceArch(v)}|{(boss ? 1 : 0)}";
            if (cache.TryGetValue(key, out var hit)) { recent.Remove(key); recent.AddFirst(key); return hit; }

            string svg = VillainSvg.ForUnity(VillainSvg.Build(v, boss), out var marks);
            var body = Render(svg);
            var drawing = body != null
                ? new Drawing { Body = body, Overlays = marks.ConvertAll(ToOverlay).ToArray(), OwnsTexture = true }
                : Blob(v);
            cache[key] = drawing;
            recent.AddFirst(key);
            Trim();
            return drawing;
        }

        /// <summary>Forgets the oldest drawings nobody is showing, so a long walk doesn't fill memory.</summary>
        static void Trim()
        {
            for (var node = recent.Last; node != null && cache.Count > CacheLimit;)
            {
                var prev = node.Previous;
                var d = cache[node.Value];
                if (d.Users <= 0 && node != recent.First)
                {
                    if (d.OwnsTexture && d.Body) Object.Destroy(d.Body.texture);
                    if (d.Body) Object.Destroy(d.Body);
                    cache.Remove(node.Value);
                    recent.Remove(node);
                }
                node = prev;
            }
        }

        static Drawing Swapped(string name)
        {
            string slug = Slug(name ?? "");
            if (swapped.TryGetValue(slug, out var d)) return d;
            var tex = Resources.Load<Texture2D>("BookBuddies/Foes/" + slug);
            return swapped[slug] = tex == null ? null : new Drawing { Body = Wrap(tex), Overlays = new Overlay[0] };
        }

        /// <summary>A swapped-in PNG has its own colours, so an affix's tint is laid over it softly.</summary>
        static Color AffixTint(FoeVariant v)
        {
            var affix = v.Ax == null ? null : TalesData.Current.Affixes.Find(a => a.N == v.Ax);
            return affix?.C != null ? Color.Lerp(Color.white, Palette.Hex(affix.C), (float)affix.M) : Color.white;
        }

        static Overlay ToOverlay(VillainSvg.TextMark m) => new Overlay
        {
            Text = m.Text,
            Center = new Vector2((float)m.X, (float)m.Y),
            Size = (float)m.Size,
            Glyph = m.Color != null,
            Color = m.Color != null ? Palette.Hex(m.Color) : Color.white,
            Shadow = m.Shadow,
        };

        static Sprite Render(string svg)
        {
#if BB_VECTOR
            try
            {
                var scene = SVGParser.ImportSVG(new StringReader(svg));
                var options = new VectorUtils.TessellationOptions { StepDistance = .5f, MaxCordDeviation = .1f, MaxTanAngleDeviation = .05f, SamplingStepSize = .01f };
                var geometry = VectorUtils.TessellateScene(scene.Scene, options);
                var vector = VectorUtils.BuildSprite(geometry, scene.SceneViewport, 100f, VectorUtils.Alignment.BottomCenter, Vector2.zero, 64, true);
                var material = new Material(Shader.Find("Unlit/VectorGradient") ?? Shader.Find("Sprites/Default"));
                var tex = VectorUtils.RenderSpriteToTexture2D(vector, TextureSize, TextureSize, material, 4);
                Object.Destroy(vector);
                Object.Destroy(material);
                tex.wrapMode = TextureWrapMode.Clamp;
                return Wrap(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("BookBuddies: couldn't draw a villain, using a blob instead. " + e.Message);
            }
#else
            if (cache.Count == 0) Debug.LogWarning("BookBuddies: install com.unity.vectorgraphics to draw real villains (see SETUP.md).");
#endif
            return null;
        }

        static Sprite Wrap(Texture2D tex) =>
            Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, FeetY), tex.width, 0, SpriteMeshType.FullRect);

        /// <summary>Stand-in villain: a round body in its colour with two eyes, and its emoji on top.</summary>
        static Drawing Blob(FoeVariant v)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color body = Palette.Hex(VillainSvg.BodyColor(v)), line = Color.Lerp(body, Color.black, .42f), ink = Palette.Hex("#1b1b1b");
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) * 100 / n, w = 100 - (y + .5f) * 100 / n; // the SVG's own coordinates
                    float d = new Vector2((u - 50) / 32, (w - 60) / 33).magnitude;
                    float inside = Mathf.Clamp01((1 - d) * 24);
                    bool eye = Mathf.Min(new Vector2(u - 38, w - 60).magnitude, new Vector2(u - 62, w - 60).magnitude) < 5;
                    var c = eye ? ink : d > .93f ? line : body;
                    px[y * n + x] = new Color(c.r, c.g, c.b, inside);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var emoji = new Overlay { Text = v.Def.I, Center = new Vector2(.5f, .77f), Size = .26f, Color = Color.white, Shadow = true };
            return new Drawing { Body = Wrap(tex), Overlays = string.IsNullOrEmpty(v.Def.I) ? new Overlay[0] : new[] { emoji }, OwnsTexture = true };
        }

        /// <summary>Counts a drawing as in use while this copy of the villain exists.</summary>
        sealed class Holder : MonoBehaviour
        {
            Drawing drawing;
            public void Hold(Drawing d) { drawing = d; if (d != null) d.Users++; }
            void OnDestroy() { if (drawing != null) drawing.Users--; }
        }

        /// <summary>The boss halo's slow breathing (the site's tqaura: 2.8s, opacity .55 to 1, scale .92 to 1.06).</summary>
        sealed class AuraPulse : MonoBehaviour
        {
            SpriteRenderer sprite;
            Graphic graphic;
            Color color;
            Vector3 size;
            float phase;

            void Start()
            {
                sprite = GetComponent<SpriteRenderer>();
                graphic = GetComponent<Graphic>();
                color = sprite ? sprite.color : graphic.color;
                size = transform.localScale;
                phase = Random.value * Mathf.PI * 2;
            }

            void Update()
            {
                float k = (Mathf.Sin(Time.time * Mathf.PI * 2 / 2.8f + phase) + 1) / 2;
                transform.localScale = size * Mathf.Lerp(.92f, 1.06f, k);
                var c = color.WithAlpha(color.a * Mathf.Lerp(.55f, 1, k));
                if (sprite) sprite.color = c; else graphic.color = c;
            }
        }
    }
}
