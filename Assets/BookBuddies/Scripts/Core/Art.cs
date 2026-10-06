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
        public static readonly HashSet<string> Sways = new HashSet<string>();

        /// <summary>One map's painted ground: square chunks of Chunk tiles, by chunk position.</summary>
        public sealed class GroundSet
        {
            public int Chunk = 16;
            public readonly Dictionary<Vector2Int, string> Files = new Dictionary<Vector2Int, string>();
        }

        static readonly Dictionary<string, GroundSet> grounds = new Dictionary<string, GroundSet>();

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Merge(Json.ParseObject(Text("Data/art_index")));
            // features add their own indexes (Tales emoji, Bramble Road props), so each export writes only its own file
            foreach (var extra in new[] { "Data/art_tales", "Data/art_road" })
            {
                var more = TryText(extra);
                if (more != null) Merge(Json.ParseObject(more));
            }
        }

        static void Merge(Dictionary<string, object> j)
        {
            if (j == null) return;
            ReadEntries(j.Obj("objects"), Objects);
            ReadEntries(j.Obj("anim"), Anims);
            ReadEntries(j.Obj("plants"), Plants);
            ReadEntries(j.Obj("items"), Items);
            var emotes = j.Obj("emotes");
            if (emotes != null) foreach (var kv in emotes) Emotes[kv.Key] = (string)kv.Value;
            foreach (var s in j.Arr("sway")) Sways.Add((string)s);
            var ground = j.Obj("ground");
            if (ground == null) return;
            // art_index.json holds Pawtopia's ground as {chunk, files}; later indexes key theirs by town: {town: {chunk, files}}
            if (ground.Has("files")) ReadGround("pawtopia", ground);
            else foreach (var kv in ground) ReadGround(kv.Key, (Dictionary<string, object>)kv.Value);
        }

        static void ReadGround(string town, Dictionary<string, object> j)
        {
            var set = new GroundSet { Chunk = j.Int("chunk", 16) };
            foreach (var kv in j.Obj("files"))
            {
                var xy = kv.Key.Split(',');
                set.Files[new Vector2Int(int.Parse(xy[0]), int.Parse(xy[1]))] = (string)kv.Value;
            }
            grounds[town] = set;
        }

        /// <summary>A text file from Resources/BookBuddies, or null when it isn't there.</summary>
        public static string TryText(string path)
        {
            var asset = Resources.Load<TextAsset>(Root + path);
            return asset != null ? asset.text : null;
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

        /// <summary>Tiles per ground chunk for a map (16 when it has no painted ground).</summary>
        public static int GroundChunk(string town) => grounds.TryGetValue(town, out var set) ? set.Chunk : 16;

        /// <summary>One chunk of a map's painted ground, pinned at its top-left corner, or null.</summary>
        public static Sprite GroundSprite(string town, Vector2Int chunk) =>
            grounds.TryGetValue(town, out var set) && set.Files.TryGetValue(chunk, out var file) ? Cached(file, new Vector2(set.Chunk, set.Chunk), new Vector2(0, 1)) : null;

        /// <summary>Frees a map's ground pictures after leaving it (they are the biggest images in the game).</summary>
        public static void ReleaseGround(string town)
        {
            if (!grounds.TryGetValue(town, out var set)) return;
            foreach (var file in set.Files.Values)
            {
                if (!cache.TryGetValue(file, out var s)) continue;
                cache.Remove(file);
                if (s == null) continue;
                var tex = s.texture;
                Object.Destroy(s);
                Resources.UnloadAsset(tex);
            }
        }

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

        static Sprite softDot, ring, white, disc, glowRing;

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

        /// <summary>A crisp round dot (path trails, markers).</summary>
        public static Sprite Disc => disc ? disc : disc = MakeSprite(64, (x, y) => (1 - new Vector2(x, y).magnitude) * 24);

        /// <summary>A soft glowing band, brightest just inside the edge: the hover ring under things you can use.</summary>
        public static Sprite GlowRing => glowRing ? glowRing : glowRing = MakeSprite(128, (x, y) =>
        {
            float d = new Vector2(x, y).magnitude;
            float band = Mathf.Exp(-Mathf.Pow((d - .78f) / .1f, 2)), fill = d < 1 ? (1 - d) * .2f : 0;
            return band + fill;
        });

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
