using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>One drawable in Resources/BookBuddies, as listed in Data/art_index.json.</summary>
    public sealed class ArtEntry
    {
        public string File;            // path under Resources/BookBuddies, no extension (animations: frame prefix)
        public Vector2 Size;           // in tiles, so any PNG resolution works
        public Vector2 Pivot;          // 0..1 from bottom-left: where the sprite touches the ground
        public ShadowSpot[] Shadows;   // soft shadows laid on the ground around the base
        public int Frames;             // animations only
        public float Seconds;          // animations only: length of one loop
    }

    public struct ShadowSpot
    {
        public Vector2 Offset;  // from the base point, in tiles (y = toward the viewer)
        public Vector2 Radius;
        public float Alpha;
    }

    /// <summary>
    /// Loads every sprite by name. To reskin something, drop a new PNG over the old one in
    /// Assets/BookBuddies/Resources/BookBuddies (same name). Size and pivot come from art_index.json,
    /// so a bigger or smaller PNG still lands in the same place.
    /// </summary>
    public static class Art
    {
        const string Root = "BookBuddies/";

        public static readonly Dictionary<string, ArtEntry> Objects = new Dictionary<string, ArtEntry>();
        public static readonly Dictionary<string, ArtEntry> Anims = new Dictionary<string, ArtEntry>();
        public static readonly Dictionary<string, ArtEntry> Plants = new Dictionary<string, ArtEntry>();
        public static readonly Dictionary<string, ArtEntry> Items = new Dictionary<string, ArtEntry>();
        public static readonly Dictionary<string, string> Emotes = new Dictionary<string, string>();
        public static readonly Dictionary<Vector2Int, string> Ground = new Dictionary<Vector2Int, string>();
        public static readonly HashSet<string> Sways = new HashSet<string>();
        public static int GroundChunk = 16;

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            var j = Json.ParseObject(Text("Data/art_index"));
            ReadEntries(j.Obj("objects"), Objects);
            ReadEntries(j.Obj("anim"), Anims);
            ReadEntries(j.Obj("plants"), Plants);
            ReadEntries(j.Obj("items"), Items);
            foreach (var kv in j.Obj("emotes")) Emotes[kv.Key] = (string)kv.Value;
            foreach (var s in j.Arr("sway")) Sways.Add((string)s);
            var ground = j.Obj("ground");
            GroundChunk = ground.Int("chunk", 16);
            foreach (var kv in ground.Obj("files"))
            {
                var xy = kv.Key.Split(',');
                Ground[new Vector2Int(int.Parse(xy[0]), int.Parse(xy[1]))] = (string)kv.Value;
            }
        }

        public static string Text(string path)
        {
            var asset = Resources.Load<TextAsset>(Root + path);
            if (asset == null) throw new System.IO.FileNotFoundException("Missing Resources/" + Root + path + ".json");
            return asset.text;
        }

        /// <summary>The sprite for an entry (frame &gt;= 0 picks an animation frame), pivoted at its base.</summary>
        public static Sprite Sprite(ArtEntry e, int frame = -1)
        {
            string file = frame >= 0 ? e.File + frame.ToString("00") : e.File;
            return Cached(file, e.Size, e.Pivot);
        }

        public static Sprite GroundSprite(Vector2Int chunk) =>
            Ground.TryGetValue(chunk, out var file) ? Cached(file, new Vector2(GroundChunk, GroundChunk), new Vector2(0, 1)) : null;

        /// <summary>Emoji drawn as images, so they show on every device. Null when there is no art for it.</summary>
        public static Sprite Emote(string emoji) =>
            emoji != null && Emotes.TryGetValue(emoji, out var file) ? Cached(file, Vector2.one, new Vector2(.5f, .5f)) : null;

        static Sprite Cached(string file, Vector2 size, Vector2 pivot)
        {
            if (cache.TryGetValue(file, out var s)) return s;
            var tex = Resources.Load<Texture2D>(Root + file);
            if (tex == null) { Debug.LogWarning("BookBuddies: missing art " + file); cache[file] = null; return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, tex.width / size.x, 0, SpriteMeshType.FullRect);
            s.name = file;
            cache[file] = s;
            return s;
        }

        static void ReadEntries(Dictionary<string, object> from, Dictionary<string, ArtEntry> to)
        {
            if (from == null) return;
            foreach (var kv in from)
            {
                var o = (Dictionary<string, object>)kv.Value;
                var size = o.Floats("size");
                var pivot = o.Floats("pivot");
                var shadows = new List<ShadowSpot>();
                foreach (Dictionary<string, object> sh in o.Arr("shadows"))
                    shadows.Add(new ShadowSpot
                    {
                        Offset = new Vector2((float)sh.Num("dx"), (float)sh.Num("dy")),
                        Radius = new Vector2((float)sh.Num("rx"), (float)sh.Num("ry")),
                        Alpha = (float)sh.Num("a", .18),
                    });
                to[kv.Key] = new ArtEntry
                {
                    File = o.Str("file"),
                    Size = new Vector2(size[0], size[1]),
                    Pivot = new Vector2(pivot[0], pivot[1]),
                    Shadows = shadows.ToArray(),
                    Frames = o.Int("frames"),
                    Seconds = (float)o.Num("seconds", 1),
                };
            }
        }

        // ---- shapes made in code (soft shadow, glow, dashed ring, plain white) ----

        static Sprite softDot, ring, white;

        /// <summary>A round blob that fades to nothing at the edge. Tint it for shadows and glows.</summary>
        public static Sprite SoftDot => softDot ? softDot : softDot = MakeSprite(64, (x, y) =>
        {
            float d = Mathf.Clamp01(new Vector2(x, y).magnitude);
            return Mathf.SmoothStep(1, 0, d) * Mathf.SmoothStep(1, .55f, d);
        });

        /// <summary>A dashed ellipse outline for the "this is a player" ring under pets.</summary>
        public static Sprite DashedRing => ring ? ring : ring = MakeSprite(128, (x, y) =>
        {
            float r = new Vector2(x, y).magnitude;
            float band = 1 - Mathf.Clamp01(Mathf.Abs(r - .86f) / .09f);
            float angle = Mathf.Atan2(y, x) / (2 * Mathf.PI) + .5f;
            float dash = Mathf.Repeat(angle * 22, 1) < .62f ? 1 : 0;
            return band * dash;
        });

        public static Sprite White => white ? white : white = MakeSprite(4, (x, y) => 1);

        static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return UnityEngine.Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size / 2f, 0, SpriteMeshType.FullRect);
        }
    }
}
