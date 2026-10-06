using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if BB_VECTOR
using Unity.VectorGraphics;
#endif

namespace BookBuddies.Pets
{
    /// <summary>
    /// Turns a pet's look into a crisp, anti-aliased sprite, once per look and mood, using Unity's Vector Graphics package.
    /// Sprites are cached (the most recent 192, by look and mood), so menus, the town and battles never tessellate the same
    /// pet twice; screens with many pets can ask Has() and draw the missing ones a frame apart.
    /// Without that package installed, pets show as simple coloured blobs (see SETUP.md).
    /// </summary>
    public static class PetSprites
    {
        public const float Height = 1.5f;          // tiles tall, same as the website
        const int TextureWidth = 400, TextureHeight = 464; // 2x the site's 200x232 art
        const float BaseInset = .04f;              // the site lifts the art .04 tiles off its base point
        const int CacheLimit = 192;

        static readonly Dictionary<string, LinkedListNode<(string key, Sprite sprite)>> cache = new Dictionary<string, LinkedListNode<(string, Sprite)>>();
        static readonly LinkedList<(string key, Sprite sprite)> recent = new LinkedList<(string, Sprite)>(); // most recently used first

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => PetParts.TextLoader = Art.Text;

        public static PetParts Parts
        {
            get
            {
                if (PetParts.TextLoader == null) PetParts.TextLoader = Art.Text;
                return PetParts.Current;
            }
        }

        /// <summary>What a pet of this look says now and then: its body's lines for its evolution form (BODY_LINES), or none.</summary>
        public static string[] LinesFor(string lookJson)
        {
            var look = PetLook.Parse(lookJson, Parts);
            return look == null || look.Stage == 0 ? new string[0] : Parts.LinesOf(look.Shape, look.Evo);
        }

        /// <summary>Whether this look and mood is already drawn (For returns it at once).</summary>
        public static bool Has(string lookJson, string mood = "joyful") => cache.ContainsKey(mood + "|" + lookJson);

        /// <summary>A pet's sprite. For an egg, mood "crack1" to "crack3" draws it cracking.</summary>
        public static Sprite For(string lookJson, string mood = "joyful")
        {
            string key = mood + "|" + lookJson;
            if (cache.TryGetValue(key, out var hit))
            {
                recent.Remove(hit);
                recent.AddFirst(hit);
                return hit.Value.sprite;
            }

            var look = PetLook.Parse(lookJson, Parts) ?? PetLook.Parse("{\"h\":32,\"s\":3}", Parts);
            string svg = PetSvg.Build(look, Parts, mood);
            if (look.Stage == 0) svg = PetSvg.EggCracks(svg, mood.StartsWith("crack") ? mood[mood.Length - 1] - '0' : 0);
            var sprite = Render(PetSvg.ForUnity(svg), (float)look.Hue);
            cache[key] = recent.AddFirst((key, sprite));
            while (recent.Count > CacheLimit)
            {
                var (old, s) = recent.Last.Value;
                recent.RemoveLast();
                cache.Remove(old);
                if (s) { Object.Destroy(s.texture); Object.Destroy(s); }
            }
            return sprite;
        }

        static Sprite Render(string svg, float hue)
        {
#if BB_VECTOR
            try
            {
                var scene = SVGParser.ImportSVG(new StringReader(svg));
                var options = new VectorUtils.TessellationOptions { StepDistance = 1f, MaxCordDeviation = .25f, MaxTanAngleDeviation = .05f, SamplingStepSize = .01f };
                var geometry = VectorUtils.TessellateScene(scene.Scene, options, scene.NodeOpacity); // keeps opacity="…" (shading, shine, auras)
                var vector = VectorUtils.BuildSprite(geometry, scene.SceneViewport, 100f, VectorUtils.Alignment.BottomCenter, Vector2.zero, 64, true);
                var shader = Shader.Find(svg.Contains("Gradient") ? "Unlit/VectorGradient" : "Unlit/Vector") ?? Shader.Find("Sprites/Default");
                var material = new Material(shader);
                var tex = VectorUtils.RenderSpriteToTexture2D(vector, TextureWidth, TextureHeight, material, 4);
                Object.Destroy(vector);
                Object.Destroy(material);
                tex.wrapMode = TextureWrapMode.Clamp;
                return Wrap(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("BookBuddies: couldn't draw a pet, using a blob instead. " + e.Message);
            }
#else
            if (cache.Count == 0) Debug.LogWarning("BookBuddies: install com.unity.vectorgraphics to draw real pets (see SETUP.md).");
#endif
            return Blob(hue);
        }

        static Sprite Wrap(Texture2D tex) =>
            Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, BaseInset / Height), tex.height / Height, 0, SpriteMeshType.FullRect);

        /// <summary>Stand-in pet: a soft round body in the pet's colour with two eyes.</summary>
        static Sprite Blob(float hue)
        {
            const int w = 100, h = 116;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color body = Color.HSVToRGB(hue / 360f, .45f, .95f), ink = Palette.Hex("#2b2230");
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - w / 2f) / 40f, dy = (y - 46f) / 42f;
                    float inside = Mathf.Clamp01((1 - Mathf.Sqrt(dx * dx + dy * dy)) * 20);
                    bool eye = (Mathf.Abs(x - 38) < 4 || Mathf.Abs(x - 62) < 4) && Mathf.Abs(y - 56) < 5;
                    px[y * w + x] = eye && inside > .5f ? ink : new Color(body.r, body.g, body.b, inside);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Wrap(tex);
        }
    }
}
