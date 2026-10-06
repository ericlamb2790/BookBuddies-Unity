using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// The soft ring of light on the ground under whatever the pointer is on (a pet, a seat, a place or a foe),
    /// so you can see what a click would do before you click. One ring, moved each frame; it breathes gently
    /// and fades in and out quickly.
    /// </summary>
    public sealed class Highlight : MonoBehaviour
    {
        const float FadeSeconds = .12f;

        SpriteRenderer ring, glow;
        Vector2 at, size;
        Color tint;
        float shown;
        bool wanted;

        public static Highlight Create(Transform parent)
        {
            var h = new GameObject("Hover ring").AddComponent<Highlight>();
            h.transform.SetParent(parent, false);
            h.glow = Draw.Blob("glow", h.transform, 0, 0, 1, 1, Color.clear, Draw.GroundFxOrder);
            h.ring = Draw.Blob("ring", h.transform, 0, 0, 1, 1, Color.clear, Draw.GroundFxOrder + 1);
            h.ring.sprite = Art.GlowRing;
            return h;
        }

        /// <summary>Keeps the ring on a map point this frame; radius in tiles (x across, y deep).</summary>
        public void Show(Vector2 mapPoint, Vector2 radius, Color color)
        {
            at = mapPoint; size = radius; tint = color;
            wanted = true;
        }

        public void Hide() => wanted = false;

        void LateUpdate()
        {
            shown = Mathf.MoveTowards(shown, wanted ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
            ring.enabled = glow.enabled = shown > 0;
            if (shown <= 0) return;
            float breathe = GameSettings.ReduceMotion ? 1 : 1 + Mathf.Sin(Time.unscaledTime * 4.2f) * .05f;
            float grow = Mathf.Lerp(.8f, 1, UI.UiKit.EaseOut(shown));
            ring.transform.position = TownMap.ToWorld(at.x, at.y, .013f);
            glow.transform.position = ring.transform.position;
            ring.transform.localScale = new Vector3(size.x, size.y, 1) * grow * breathe;
            glow.transform.localScale = new Vector3(size.x, size.y, 1) * .9f * grow;
            ring.color = tint.WithAlpha(.95f * shown);
            glow.color = tint.WithAlpha(.28f * shown);
            wanted = false; // Show() is called every frame while hovering
        }
    }
}
