using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Builds the town you see from a TownMap: painted ground, buildings and props with soft shadows,
    /// the garden, and coins or gifts lying around. Everything comes from named art, so it can be reskinned.
    /// </summary>
    public sealed class TownView : MonoBehaviour
    {
        const float MapEdgeSea = 40f; // tiles of open water drawn past the map edge

        public TownMap Map { get; private set; }
        readonly Dictionary<int, SpriteRenderer> plants = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<string, GameObject> items = new Dictionary<string, GameObject>();
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
            BuildGround();
            progress?.Invoke(.3f);
            yield return null;
            for (int i = 0; i < map.Objects.Count; i++)
            {
                PlaceObject(map.Objects[i]);
                if (i % 24 != 23) continue;
                progress?.Invoke(.3f + .6f * i / map.Objects.Count);
                yield return null;
            }
            gameObject.AddComponent<Ambience>().Build(map);
            progress?.Invoke(1);
        }

        void BuildGround()
        {
            var sea = Draw.Sprite("Sea", ground, Art.White, Draw.GroundOrder - 1, Palette.Sea);
            sea.transform.SetPositionAndRotation(TownMap.ToWorld(Map.Width / 2f, Map.Height / 2f, -.02f), Quaternion.Euler(90, 0, 0));
            sea.transform.localScale = new Vector3(Map.Width / 2f + MapEdgeSea, Map.Height / 2f + MapEdgeSea, 1);

            int n = Art.GroundChunk;
            for (int cy = 0; cy * n < Map.Height; cy++)
                for (int cx = 0; cx * n < Map.Width; cx++)
                {
                    var sprite = Art.GroundSprite(new Vector2Int(cx, cy));
                    if (!sprite) continue;
                    var r = Draw.Sprite($"Ground {cx},{cy}", ground, sprite, Draw.GroundOrder);
                    r.transform.SetPositionAndRotation(TownMap.ToWorld(cx * n, cy * n), Quaternion.Euler(90, 0, 0));
                }
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
            r.transform.localScale = Vector3.one * .25f;
            var puff = r.gameObject.AddComponent<Puff>();
            puff.grow = 2.8f;
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
