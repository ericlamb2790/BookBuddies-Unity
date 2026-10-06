using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Helpers for placing sprites the way the website layers them:
    /// the ground lies flat, everything standing faces the camera, and nearer rows draw on top.
    /// </summary>
    public static class Draw
    {
        public const int GroundOrder = -30000;
        public const int ShadowOrder = -20000;
        public const int GroundFxOrder = -19000;

        /// <summary>Draw order for something standing at map row y (bigger y = closer = on top).</summary>
        public static int Order(float mapY) => Mathf.Clamp(Mathf.RoundToInt(mapY * 100), -15000, 30000);

        /// <summary>
        /// Pets in the tile row of y: over that row's props and tall grass, and one step over foes in the
        /// same row (the site's z = floor(y) + 1.005 against the foes' 1.004).
        /// </summary>
        public static int ActorOrder(float mapY) => Order(Mathf.Floor(mapY) + 1) + 1;

        /// <summary>Foes in the tile row of y: just behind pets in the same row.</summary>
        public static int FoeOrder(float mapY) => Order(Mathf.Floor(mapY) + 1);

        public static SpriteRenderer Sprite(string name, Transform parent, Sprite sprite, int order, Color? tint = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
            if (tint.HasValue) r.color = tint.Value;
            return r;
        }

        /// <summary>Something standing on the map, facing the camera, with its base at (x, y).</summary>
        public static SpriteRenderer Standing(string name, Transform parent, Sprite sprite, float x, float y, int? order = null)
        {
            var r = Sprite(name, parent, sprite, order ?? Order(y));
            r.transform.SetPositionAndRotation(TownMap.ToWorld(x, y), TownCamera.Facing);
            return r;
        }

        /// <summary>A soft blob lying on the ground: shadows, glows, dust.</summary>
        public static SpriteRenderer Blob(string name, Transform parent, float x, float y, float rx, float ry, Color color, int order = ShadowOrder)
        {
            var r = Sprite(name, parent, Art.SoftDot, order, color);
            r.transform.SetPositionAndRotation(TownMap.ToWorld(x, y, .01f), Quaternion.Euler(90, 0, 0));
            r.transform.localScale = new Vector3(rx, ry, 1);
            return r;
        }

        /// <summary>
        /// The site drew shadows squashed on the upright sprite. Laid on the real ground they need
        /// less squash, since the camera's tilt already flattens them.
        /// </summary>
        public static void Shadows(Transform parent, ArtEntry art, float baseX, float baseY)
        {
            if (art.Shadows == null) return;
            foreach (var s in art.Shadows)
            {
                float ry = Mathf.Max(s.Radius.y / Mathf.Sin(TownCamera.Pitch * Mathf.Deg2Rad), s.Radius.x * .55f);
                Blob("shadow", parent, baseX + s.Offset.x, baseY + s.Offset.y, s.Radius.x * 1.15f, ry * 1.15f, Palette.ShadowTint.WithAlpha(Mathf.Min(.55f, s.Alpha * 1.6f)));
            }
        }

        /// <summary>The website's tile hash: the same "random" scatter of flowers, ripples and sparkles on every device.</summary>
        public static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                uint r = (uint)(h ^ (int)((uint)h >> 16));
                return r / 4294967296f;
            }
        }
    }
}
