using System.Collections.Generic;
using BookBuddies.Tales;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BookBuddies.Road
{
    /// <summary>
    /// One villain out on the road or in the caves (the site's mkFoe record), and its sprite: bobbing as it
    /// walks, popping in when it appears, wobbling when stunned, with a ground shadow and, for the cave
    /// guardian, a warm glow. Its name label and ❗ / 💫 markers are drawn by UI/Overlays from Label and Marker.
    /// RoadFoes moves it; times are on the road's clock (seconds).
    /// </summary>
    public sealed class RoadFoe : MonoBehaviour
    {
        public enum Mood { Idle, Chase, Home }

        public int Id, Lvl;
        public FoeVariant V;
        public bool Guardian, Elite, Passive, Ambusher;
        public Vector2 Pos;                  // map position (tile centres at +.5)
        public Vector2Int Home;
        public Mood State;
        public readonly List<Vector2Int> Path = new List<Vector2Int>();
        public float Next, Alert, Stun, Repath, Born;
        public int Dir;
        public bool Hovered;                 // the pointer is on it: lifts a little

        /// <summary>The site's size: the guardian 1.75, elites 1.2.</summary>
        public float Size => Guardian ? 1.75f : Elite ? 1.2f : 1;
        public bool Chasing => State == Mood.Chase;
        public Vector2Int Tile => new Vector2Int(Mathf.FloorToInt(Pos.x), Mathf.FloorToInt(Pos.y));

        // label colours: chasing, passive, elite, everyone else
        static readonly Color ChaseInk = Palette.Hex("#b3261e"), PassiveInk = Palette.Hex("#3f6b3a"), EliteInk = Palette.Hex("#6b3fb0"), PlainInk = Palette.Hex("#3d3150");
        static readonly Color ShadowTint = new Color32(30, 20, 40, 77), GlowTint = new Color32(255, 150, 60, 255);

        Transform art;
        SortingGroup group;
        SpriteRenderer shadow, glow;
        SpriteRenderer[] parts;
        Color[] partColours;
        float hoverLift;

        /// <summary>Builds the sprite under parent. Call once after the record is filled in.</summary>
        public void Build(Transform parent)
        {
            transform.SetParent(parent, false);
            transform.rotation = TownCamera.Facing;
            float height = 1.3f * Size; // the site draws the 100-unit box 1.3 tiles wide
            var look = FoeArt.For(V, Guardian);
            art = FoeArt.MakeWorld(transform, look, height, Draw.FoeOrder(Pos.y));
            group = art.GetComponent<SortingGroup>();
            // the drawing's own soft shadow at its feet (MakeWorld leaves it to us), unscaled by Giant or Tiny
            var feet = Draw.Sprite("feet shadow", art, Art.SoftDot, -1, new Color(0, 0, 0, .3f));
            feet.transform.localScale = new Vector3(FoeArt.ShadowSize.x * 1.25f, FoeArt.ShadowSize.y * 1.6f, 1) * height;
            parts = art.GetComponentsInChildren<SpriteRenderer>();
            partColours = new Color[parts.Length];
            for (int i = 0; i < parts.Length; i++) partColours[i] = parts[i].color;

            shadow = Draw.Blob("foe shadow", parent, Pos.x, Pos.y + .3f, .34f * Size, .15f * Size, ShadowTint, Draw.ShadowOrder + 1);
            if (Guardian) glow = Draw.Blob("guardian glow", parent, Pos.x, Pos.y + .3f, 1, .45f, GlowTint, Draw.ShadowOrder + 2);
        }

        void OnDestroy()
        {
            if (shadow) Destroy(shadow.gameObject);
            if (glow) Destroy(glow.gameObject);
        }

        /// <summary>A world point above (negative) or below (positive) its spot, measured like the site: in tiles up the screen.</summary>
        public Vector3 PointAt(float siteDy) => TownMap.ToWorld(Pos.x, Pos.y) + TownCamera.Facing * Vector3.up * -siteDy;

        /// <summary>"👑 The Unreader · Lv 5" (the guardian keeps its full name; everyone else shows the base villain's).</summary>
        public string Label => (Guardian ? V.Name : V.Def.N) + " · Lv " + Lvl;
        /// <summary>The emoji before the label: 👑 guardian, ★ elite, or none.</summary>
        public string Badge => Guardian ? "👑" : Elite ? "★" : null;
        public Color LabelColour => Chasing ? ChaseInk : Passive ? PassiveInk : Elite ? EliteInk : PlainInk;

        /// <summary>Draws the foe for road time t (seconds).</summary>
        public void Render(float t, float dt)
        {
            float s = Size;
            bool stunned = Stun > t, chase = Chasing;
            float oy = Path.Count > 0 || chase
                ? -Mathf.Abs(Mathf.Sin(t * (chase ? 13 : 8) + Id)) * .13f
                : -Mathf.Abs(Mathf.Sin(t * 2.2f + Id)) * .05f;
            if (GameSettings.ReduceMotion) oy *= .4f;
            float rot = stunned ? Mathf.Sin(t * 9) * .12f : 0;
            float pop = Mathf.Min(1, (t - Born) / .35f);
            hoverLift = Mathf.MoveTowards(hoverLift, Hovered ? .07f : 0, dt * .6f);
            float grow = pop * (Hovered ? 1.03f : 1);

            transform.position = TownMap.ToWorld(Pos.x, Pos.y);
            // the site hangs the 1.3-tile box from (x, y + .32 + bob) with the feet 3% of the box above that point
            art.localPosition = new Vector3(0, -(.32f + oy - hoverLift) + .03f * 1.3f * s, 0);
            art.localRotation = Quaternion.Euler(0, 0, -rot * Mathf.Rad2Deg);
            art.localScale = new Vector3((Dir < 0 ? -1 : 1) * grow, grow, 1);
            group.sortingOrder = Draw.FoeOrder(Pos.y);
            for (int i = 0; i < parts.Length; i++) parts[i].color = partColours[i].WithAlpha(partColours[i].a * (stunned ? .6f : 1));

            shadow.transform.position = TownMap.ToWorld(Pos.x, Pos.y + .3f, .01f);
            if (glow)
            {
                float g = (Mathf.Sin(t * 2) + 1) / 2;
                glow.transform.position = shadow.transform.position;
                glow.transform.localScale = new Vector3(.9f + g * .1f, .4f + g * .05f, 1);
                glow.color = GlowTint.WithAlpha(.12f + g * .1f);
            }
        }

        /// <summary>The marker over its head: ❗ while chasing (growing in over 0.2 s), 💫 while stunned, else none.</summary>
        public string Marker(float t, out float size)
        {
            size = 0;
            if (Chasing) { size = .4f * Mathf.Min(1, (t - Alert) / .2f); return "❗"; }
            if (Stun > t) { size = .3f; return "💫"; }
            return null;
        }

        /// <summary>Where the marker sits: just over the head (the site's y - 1.05 s - .12 while chasing).</summary>
        public Vector3 MarkerPoint => PointAt(-1.05f * Size - (Chasing ? .12f : 0));
    }
}
