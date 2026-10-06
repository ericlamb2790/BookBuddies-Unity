using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// The dotted trail you see after clicking somewhere to walk: little cream dots along the path that pop in
    /// from your pet outwards and fade as it walks over them. It reads the walker's own path list, which loses
    /// a tile from the front each time one is reached.
    /// </summary>
    public sealed class PathPreview : MonoBehaviour
    {
        const float DotSize = .085f;      // tiles
        const float Stagger = .025f;      // seconds between dots popping in
        const float FadeSeconds = .25f;
        static readonly Color DotColour = Palette.Cream.WithAlpha(.92f);

        sealed class Dot { public SpriteRenderer R; public int Reach; public float BornAt, GoneAt = -1; }

        readonly List<Dot> dots = new List<Dot>();
        List<Vector2Int> live;
        int total;
        Vector2Int end;

        public static PathPreview Create(Transform parent)
        {
            var p = new GameObject("Path preview").AddComponent<PathPreview>();
            p.transform.SetParent(parent, false);
            return p;
        }

        /// <summary>Shows the trail from a point along a path that is about to be walked (the walker's live list).</summary>
        public void Show(Vector2 from, List<Vector2Int> path)
        {
            Clear();
            if (path == null || path.Count < 2) return;
            live = path; total = path.Count; end = path[path.Count - 1];
            float now = Time.time;
            var prev = from;
            for (int i = 0; i < path.Count - 1; i++) // the last tile gets the tap ring instead
            {
                var c = new Vector2(path[i].x + .5f, path[i].y + .5f);
                Add((prev + c) / 2, total - i, now + dots.Count * Stagger);
                Add(c, total - i, now + dots.Count * Stagger);
                prev = c;
            }
        }

        public void Clear()
        {
            foreach (var d in dots) if (d.R) Destroy(d.R.gameObject);
            dots.Clear();
            live = null;
        }

        // A dot goes once the walker has fewer than "reach" tiles left (it has reached the dot's tile).
        void Add(Vector2 at, int reach, float bornAt)
        {
            var r = Draw.Sprite("dot", transform, Art.Disc, Draw.GroundFxOrder, Color.clear);
            r.transform.SetPositionAndRotation(TownMap.ToWorld(at.x, at.y, .014f), Quaternion.Euler(90, 0, 0));
            dots.Add(new Dot { R = r, Reach = reach, BornAt = GameSettings.ReduceMotion ? Time.time : bornAt });
        }

        void Update()
        {
            if (live == null) return;
            // walking somewhere else (a new path in the same list) ends this trail
            if (live.Count > total || live.Count == 0 || live[live.Count - 1] != end) { Clear(); return; }
            total = live.Count;
            float now = Time.time;
            for (int i = dots.Count - 1; i >= 0; i--)
            {
                var d = dots[i];
                if (d.GoneAt < 0 && live.Count < d.Reach) d.GoneAt = now;
                float fade = d.GoneAt < 0 ? 1 : 1 - (now - d.GoneAt) / FadeSeconds;
                if (fade <= 0) { Destroy(d.R.gameObject); dots.RemoveAt(i); continue; }
                float pop = Mathf.Clamp01((now - d.BornAt) / .16f);
                d.R.transform.localScale = Vector3.one * DotSize * UI.UiKit.EaseBack(pop) * (d.GoneAt < 0 ? 1 : fade);
                d.R.color = DotColour.WithAlpha(DotColour.a * Mathf.Min(fade, pop > 0 ? 1 : 0));
            }
        }
    }
}
