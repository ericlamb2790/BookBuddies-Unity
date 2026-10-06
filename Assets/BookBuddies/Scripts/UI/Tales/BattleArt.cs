using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The battle screen's pictures, from Resources/BookBuddies/Battle as listed in Battle/battle_art.json
    /// (tools/export_battle_art.js renders them from the site). Swap any PNG for one of any size: arenas are cropped
    /// to cover the screen around "focus"; shapes and particles are white masks tinted in code.
    /// </summary>
    public static class BattleArt
    {
        static Dictionary<string, object> index;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        static Dictionary<string, object> Index => index ?? (index = Json.ParseObject(Art.TryText("Battle/battle_art") ?? "{}") ?? new Dictionary<string, object>());

        /// <summary>The arena sky for a road tier (1 to 5) or the caves; seed picks one of the place's skies. Null if none is there.</summary>
        public static Sprite Arena(int tier, bool cave, int seed)
        {
            var pool = Index.Obj("pools")?.Arr(cave ? "cave" : "tier" + Mathf.Clamp(tier, 1, 5));
            if (pool == null || pool.Count == 0) return null;
            var arena = Index.Obj("arenas")?.Obj((string)pool[Mathf.Abs(seed) % pool.Count]);
            return arena == null ? null : Load(arena.Str("file"));
        }

        /// <summary>The point of an arena picture (0 to 1 from its top-left) that stays in place when the screen crops it.</summary>
        public static Vector2 ArenaFocus
        {
            get { var f = Index.Floats("focus"); return f.Length == 2 ? new Vector2(f[0], f[1]) : new Vector2(.5f, .3f); }
        }

        /// <summary>Move signature shape i (the site's SG_SHAPES), a white mask.</summary>
        public static Sprite Shape(int i)
        {
            var list = Index.Arr("shapes");
            return list.Count == 0 ? UI.UiKit.Glow : Load((string)list[((i % list.Count) + list.Count) % list.Count]) ?? UI.UiKit.Glow;
        }

        /// <summary>A class particle glyph (MV_G: star, petal, coin…), a white mask; a star when it's missing.</summary>
        public static Sprite Particle(string glyph)
        {
            var all = Index.Obj("particles");
            string file = all?.Str(glyph, null) ?? all?.Str("star", null);
            return (file != null ? Load(file) : null) ?? Art.Disc;
        }

        /// <summary>The fate die: plain, ok, no or meh.</summary>
        public static Sprite Die(string look) => Load(Index.Obj("die")?.Str(look, null) ?? Index.Obj("die")?.Str("plain", null));

        /// <summary>The die's size in reference pixels.</summary>
        public static float DieSize => (float)Index.Num("dieSize", 92);

        /// <summary>The starburst behind the wipe from the road.</summary>
        public static Sprite Burst => Load(Index.Str("burst", null));

        static Sprite Load(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (cache.TryGetValue(file, out var s)) return s;
            var tex = Resources.Load<Texture2D>("BookBuddies/" + file);
            if (tex == null) { Debug.LogWarning("BookBuddies: missing battle art " + file); return cache[file] = null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            s.name = file;
            return cache[file] = s;
        }

        // ---- shapes made in code ----

        static Sprite stripes, arc;

        /// <summary>Soft diagonal stripes that tile (cleared lanes, "dodge here").</summary>
        public static Sprite Stripes => stripes ? stripes : stripes = Make(32, (x, y) => Mathf.Repeat((x + y) / 32f, 1) < .5f ? 1 : .25f, TextureWrapMode.Repeat);

        /// <summary>A thick soft ring, for impacts, rune circles and arcs (use Image fill to cut an arc out of it).</summary>
        public static Sprite Ring => arc ? arc : arc = Make(128, (x, y) =>
        {
            float d = new Vector2(x - 63.5f, y - 63.5f).magnitude / 64f;
            return Mathf.Clamp01(1 - Mathf.Abs(d - .86f) / .12f);
        }, TextureWrapMode.Clamp);

        static Sprite Make(int n, System.Func<int, int, float> alpha, TextureWrapMode wrap)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>The site's hsl() for tints: hue in degrees, saturation and lightness 0 to 100.</summary>
        public static Color Hsl(float hue, float sat, float light, float alpha = 1)
        {
            float h = Mathf.Repeat(hue, 360) / 360f, s = sat / 100f, l = light / 100f;
            float q = l < .5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float C(float t)
            {
                t = Mathf.Repeat(t, 1);
                return t < 1 / 6f ? p + (q - p) * 6 * t : t < .5f ? q : t < 2 / 3f ? p + (q - p) * (2 / 3f - t) * 6 : p;
            }
            return new Color(C(h + 1 / 3f), C(h), C(h - 1 / 3f), alpha);
        }
    }
}
