using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Road
{
    /// <summary>
    /// The site's fight wipe: a starburst of ink and cream wedges (9° each) bursting out from your pet in a
    /// growing circle over 0.72 s, then holding the screen until the battle takes over. With reduce motion
    /// it simply covers the screen.
    /// </summary>
    public sealed class FightWipe : MonoBehaviour
    {
        const float Seconds = .72f, FadeOutSeconds = .2f;

        Starburst burst;
        CanvasGroup group;
        float bornAt, hideAt = -1;

        /// <summary>Starts the wipe from a screen point (your pet).</summary>
        public static FightWipe Play(Vector2 screenPoint)
        {
            var canvas = UiKit.MakeCanvas("Fight wipe", 45);
            var w = canvas.gameObject.AddComponent<FightWipe>();
            w.group = canvas.gameObject.AddComponent<CanvasGroup>();
            var r = UiKit.Node("burst", canvas.transform).Fill();
            w.burst = r.Painted<Starburst>();
            w.burst.Centre = screenPoint;
            w.bornAt = Time.unscaledTime;
            w.Update();
            return w;
        }

        /// <summary>Fades the wipe away (the battle screen is up, or the fight didn't start).</summary>
        public void Hide()
        {
            if (hideAt < 0) hideAt = Time.unscaledTime;
        }

        void Update()
        {
            var size = ((RectTransform)burst.transform).rect.size;
            float k = burst.canvas ? burst.canvas.scaleFactor : 1;
            float full = 1.5f * size.magnitude / Mathf.Sqrt(2); // CSS circle(150%) on this screen
            float q = GameSettings.ReduceMotion ? 1 : Mathf.Clamp01((Time.unscaledTime - bornAt) / Seconds);
            burst.Radius = full * CubicBezier(.6f, 0, .4f, 1, q);
            burst.Pivot = burst.Centre / k;
            float fade = hideAt < 0 ? 1 : 1 - (Time.unscaledTime - hideAt) / FadeOutSeconds;
            group.alpha = Mathf.Lerp(.9f, 1, Mathf.Clamp01(q / .55f)) * Mathf.Clamp01(fade);
            burst.SetVerticesDirty();
            if (fade <= 0) Destroy(gameObject);
        }

        /// <summary>CSS cubic-bezier(x1, y1, x2, y2) at time t: solves for the curve parameter, then reads y.</summary>
        static float CubicBezier(float x1, float y1, float x2, float y2, float t)
        {
            float u = t;
            for (int i = 0; i < 8; i++)
            {
                float x = Bezier(x1, x2, u) - t, dx = BezierSlope(x1, x2, u);
                if (Mathf.Abs(x) < 1e-4f || Mathf.Abs(dx) < 1e-5f) break;
                u = Mathf.Clamp01(u - x / dx);
            }
            return Bezier(y1, y2, u);
        }

        static float Bezier(float a, float b, float u) => 3 * a * u * (1 - u) * (1 - u) + 3 * b * u * u * (1 - u) + u * u * u;
        static float BezierSlope(float a, float b, float u) => 3 * a * (1 - u) * (1 - 4 * u + 3 * u * u) + 3 * b * u * (2 - 3 * u) + 3 * u * u;

        /// <summary>Alternating wedges around a centre, clipped to a circle of the current radius.</summary>
        sealed class Starburst : MaskableGraphic
        {
            const int Wedges = 40, Steps = 3; // 9° wedges, each drawn as three slices so the edge looks round
            static readonly Color Ink = Palette.Hex("#1a1226"), Cream = Palette.Hex("#fff4e0");

            public Vector2 Centre; // screen pixels
            public Vector2 Pivot;  // canvas units, from the bottom-left
            public float Radius;   // canvas units

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (Radius <= 0) return;
                var rect = rectTransform.rect;
                var c = new Vector2(rect.xMin, rect.yMin) + Pivot;
                for (int w = 0; w < Wedges; w++)
                {
                    var col = w % 2 == 0 ? Ink : Cream;
                    for (int s = 0; s < Steps; s++)
                    {
                        float a0 = (w * Steps + s) * Mathf.PI * 2 / (Wedges * Steps), a1 = (w * Steps + s + 1) * Mathf.PI * 2 / (Wedges * Steps);
                        int i = vh.currentVertCount;
                        vh.AddVert(c, col, Vector2.zero);
                        vh.AddVert(c + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * Radius, col, Vector2.zero);
                        vh.AddVert(c + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * Radius, col, Vector2.zero);
                        vh.AddTriangle(i, i + 1, i + 2);
                    }
                }
            }
        }
    }
}
