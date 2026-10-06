using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Builds the town you see from a TownMap: painted ground, buildings and props with soft shadows,
    /// tall grass on the road, the garden, and coins or gifts lying around. Everything comes from named art,
    /// so it can be reskinned.
    /// </summary>
    public sealed class TownView : MonoBehaviour
    {
        const float MapEdge = 40f; // tiles of sea, meadow or rock drawn past the map edge
        const int PaintPpu = 64;   // pixels per tile for ground painted in game (the baked pictures' size)

        public TownMap Map { get; private set; }
        /// <summary>The map's little animals (null in Pawtopia, which has its own ambience).</summary>
        public Critters Life { get; private set; }
        readonly Dictionary<int, SpriteRenderer> plants = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<string, GameObject> items = new Dictionary<string, GameObject>();
        readonly Dictionary<int, Rustle> tufts = new Dictionary<int, Rustle>();
        Transform ground, props, garden, pickups;

        public void Build(TownMap map)
        {
            var steps = BuildGradually(map, null);
            while (steps.MoveNext()) { }
        }

        /// <summary>Builds the town a little each frame, so the loading screen keeps animating. Reports 0 to 1.</summary>
        public System.Collections.IEnumerator BuildGradually(TownMap map, System.Action<float> progress)
        {
            Map = map;
            ground = Group("Ground");
            props = Group("Props");
            garden = Group("Garden");
            pickups = Group("Pickups");
            if (!Art.HasBakedGround(map.Key))
            {
                var paint = PaintGround(progress);
                while (paint.MoveNext()) yield return null;
            }
            BuildGround();
            progress?.Invoke(.3f);
            yield return null;
            int steps = map.Objects.Count + (map.Wild?.Tall.Count ?? 0), done = 0;
            foreach (var o in map.Objects)
            {
                PlaceObject(o);
                if (++done % 24 != 0) continue;
                progress?.Invoke(.3f + .6f * done / steps);
                yield return null;
            }
            if (map.Wild != null)
                foreach (var kv in map.Wild.Tall)
                {
                    PlaceTuft(kv.Key % map.Width, kv.Key / map.Width, kv.Value);
                    if (++done % 48 != 0) continue;
                    progress?.Invoke(.3f + .6f * done / steps);
                    yield return null;
                }
            if (map.Key == "pawtopia") gameObject.AddComponent<Ambience>().Build(map);
            else (Life = gameObject.AddComponent<Critters>()).Build(map);
            progress?.Invoke(1);
        }

        void BuildGround()
        {
            var edge = Draw.Sprite("Backdrop", ground, Art.White, Draw.GroundOrder - 1, Map.Backdrop);
            edge.transform.SetPositionAndRotation(TownMap.ToWorld(Map.Width / 2f, Map.Height / 2f, -.02f), Quaternion.Euler(90, 0, 0));
            edge.transform.localScale = new Vector3(Map.Width / 2f + MapEdge, Map.Height / 2f + MapEdge, 1);

            int n = Art.GroundChunk(Map.Key);
            for (int cy = 0; cy * n < Map.Height; cy++)
                for (int cx = 0; cx * n < Map.Width; cx++)
                {
                    var sprite = Art.GroundSprite(Map.Key, new Vector2Int(cx, cy));
                    if (!sprite) continue;
                    var r = Draw.Sprite($"Ground {cx},{cy}", ground, sprite, Draw.GroundOrder);
                    r.transform.SetPositionAndRotation(TownMap.ToWorld(cx * n, cy * n), Quaternion.Euler(90, 0, 0));
                }
        }

        // Paints the ground chunk by chunk (one per frame), then compresses each and frees its pixels.
        System.Collections.IEnumerator PaintGround(System.Action<float> progress)
        {
            const int n = 16;
            var painter = Map.Painter();
            int across = (Map.Width + n - 1) / n, down = (Map.Height + n - 1) / n, size = n * PaintPpu;
            var rgb = new byte[size * size * 3];
            for (int cy = 0; cy < down; cy++)
                for (int cx = 0; cx < across; cx++)
                {
                    painter.PaintChunk(cx, cy, n, PaintPpu, rgb, true);
                    var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, name = $"{Map.Key} ground {cx},{cy}" };
                    tex.SetPixelData(rgb, 0);
                    tex.Apply(true);
                    tex.Compress(false);
                    tex.Apply(false, true);
                    Art.AddPaintedGround(Map.Key, new Vector2Int(cx, cy), tex, n);
                    progress?.Invoke(.3f * (cy * across + cx + 1) / (across * down));
                    yield return null;
                }
        }

        void OnDestroy()
        {
            if (Map != null) Art.ReleaseGround(Map.Key);
        }

        void PlaceObject(TownMap.Placed o)
        {
            bool animated = Art.Anims.TryGetValue(o.Sprite, out var art);
            if (!animated && !Art.Objects.TryGetValue(o.Sprite, out art)) return;
            float baseX = o.X + o.W / 2f, baseY = o.Y + o.H;
            var r = Draw.Standing(o.Sprite, props, Art.Sprite(art, animated ? 0 : -1), baseX, baseY, Draw.Order(baseY - .01f));
            Draw.Shadows(props, art, baseX, baseY);
            if (animated)
            {
                var book = r.gameObject.AddComponent<Flipbook>();
                book.frames = new Sprite[art.Frames];
                for (int f = 0; f < art.Frames; f++) book.frames[f] = Art.Sprite(art, f);
                book.seconds = art.Seconds;
            }
            if (Art.Sways.Contains(o.Kind)) r.gameObject.AddComponent<Sway>();
        }

        /// <summary>
        /// The world point just above whatever stands at a place (the top of the café, the fountain, a sign),
        /// for its floating marker. Places with nothing drawn get a marker at about pet height.
        /// </summary>
        public Vector3 MarkerPoint(TownMap.Spot spot)
        {
            var a = spot.Area;
            float x = a.xMin + (a.width + 1) / 2f, baseY = a.yMax + 1, height = 1.2f;
            foreach (var o in Map.Objects)
            {
                if (o.X + o.W <= a.xMin || o.X > a.xMax || o.Y + o.H <= a.yMin || o.Y > a.yMax) continue;
                if (!Art.Objects.TryGetValue(o.Sprite, out var art) && !Art.Anims.TryGetValue(o.Sprite, out art)) continue;
                float top = art.Size.y * (1 - art.Pivot.y);
                if (top > height) { height = top; baseY = o.Y + o.H; }
            }
            return TownMap.ToWorld(x, baseY) + TownCamera.Facing * Vector3.up * (height + .2f);
        }

        // ---- tall grass ----

        // A tuft draws just under anyone standing in its row, like the site's z = y + .99.
        void PlaceTuft(int x, int y, int variant)
        {
            if (!Art.Objects.TryGetValue(Map.Wild.GrassArt + variant, out var art)) return;
            var r = Draw.Standing("grass", props, Art.Sprite(art), x + .5f, y + 1, Draw.Order(y + .99f));
            tufts[y * Map.Width + x] = r.gameObject.AddComponent<Rustle>();
        }

        /// <summary>Shakes the tall grass at a tile (someone pushed through it).</summary>
        public void RustleAt(int x, int y)
        {
            if (Map.Inside(x, y) && tufts.TryGetValue(y * Map.Width + x, out var tuft)) tuft.Poke();
        }

        // ---- garden ----

        /// <summary>Shows a plot's plant (stage 0-3), or clears it when seed is null.</summary>
        public void SetPlant(int plot, string seed, int stage)
        {
            if (plants.TryGetValue(plot, out var old)) { Destroy(old.gameObject); plants.Remove(plot); }
            if (seed == null || plot < 0 || plot >= Map.Plots.Count) return;
            if (!Art.Plants.TryGetValue(seed + "_" + Mathf.Clamp(stage, 0, 3), out var art)) return;
            var p = Map.Plots[plot];
            plants[plot] = Draw.Standing("plant " + seed, garden, Art.Sprite(art), p.x + .5f, p.y + .7f);
        }

        // ---- coins, coin bags and gift boxes ----

        public void AddItem(string id, string kind, int x, int y)
        {
            RemoveItem(id);
            if (!Map.Walkable(x, y) || !Art.Items.TryGetValue(kind, out var art)) return;
            var root = new GameObject(kind + " " + id);
            root.transform.SetParent(pickups, false);
            var glow = Draw.Blob("glow", root.transform, x + .5f, y + .55f, .42f, .42f, new Color(1, .93f, .6f, .35f), Draw.GroundFxOrder);
            glow.gameObject.AddComponent<Pulse>();
            var r = Draw.Standing("item", root.transform, Art.Sprite(art), x + .5f, y + .6f);
            r.gameObject.AddComponent<Bob>();
            items[id] = root;
        }

        public void RemoveItem(string id)
        {
            if (items.TryGetValue(id, out var go)) { Destroy(go); items.Remove(id); }
        }

        public void ClearItems()
        {
            foreach (var go in items.Values) Destroy(go);
            items.Clear();
        }

        // ---- feedback on the ground ----

        /// <summary>The white ring that spreads where you tapped.</summary>
        public void TapRing(int x, int y)
        {
            var r = Draw.Sprite("tap", transform, Art.DashedRing, Draw.GroundFxOrder, Color.white);
            r.transform.SetPositionAndRotation(TownMap.ToWorld(x + .5f, y + .5f, .02f), Quaternion.Euler(90, 0, 0));
            r.transform.localScale = Vector3.one * .23f; // the ring sits at .86 of the sprite: radius .2 → .65 tiles over .7 s
            var puff = r.gameObject.AddComponent<Puff>();
            puff.grow = 3.25f;
        }

        Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }
    }

    /// <summary>A soft breathing glow (under things waiting to be picked up).</summary>
    public sealed class Pulse : MonoBehaviour
    {
        SpriteRenderer r;
        Vector3 size;
        void Start() { r = GetComponent<SpriteRenderer>(); size = transform.localScale; }
        void Update()
        {
            float g = (Mathf.Sin(Time.time / .4f + transform.position.x) + 1) / 2;
            transform.localScale = size * (1 + g * .2f);
            var c = r.color; c.a = .25f + g * .2f; r.color = c;
        }
    }
}
