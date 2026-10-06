using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// A* over the town grid: 8 directions, never cutting a corner past a wall.
    /// Same rules as the website, so a path here matches what other players see.
    /// </summary>
    public static class PathFinder
    {
        const int SearchLimit = 9000;
        const float Diagonal = 1.414f;

        // reused between searches (all on the main thread), so walking, folk and foes don't make garbage each call
        static float[] cost = new float[0];
        static int[] came = new int[0];
        static bool[] closed = new bool[0];

        /// <summary>Tiles to walk through, not including the start. Empty when already there, null when unreachable.</summary>
        public static List<Vector2Int> Find(TownMap map, Vector2Int from, Vector2Int to)
        {
            if (!map.Walkable(to.x, to.y)) return null;
            if (from == to) return new List<Vector2Int>();

            int w = map.Width, n = w * map.Height;
            if (cost.Length < n) { cost = new float[n]; came = new int[n]; closed = new bool[n]; }
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; came[i] = -1; closed[i] = false; }

            int start = from.y * w + from.x, goal = to.y * w + to.x;
            if (start < 0 || start >= n) return null;
            var open = new MinHeap();
            cost[start] = 0;
            open.Push(start, Estimate(from.x, from.y, to));

            for (int steps = 0; open.Count > 0 && steps < SearchLimit; steps++)
            {
                int cur = open.Pop();
                if (closed[cur]) continue;
                closed[cur] = true;
                if (cur == goal) break;
                int cx = cur % w, cy = cur / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!map.Walkable(nx, ny)) continue;
                        bool diagonal = dx != 0 && dy != 0;
                        if (diagonal && (!map.Walkable(cx + dx, cy) || !map.Walkable(cx, cy + dy))) continue;
                        int next = ny * w + nx;
                        float c = cost[cur] + (diagonal ? Diagonal : 1);
                        if (c < cost[next]) { cost[next] = c; came[next] = cur; open.Push(next, c + Estimate(nx, ny, to)); }
                    }
            }
            if (came[goal] < 0) return null;
            var path = new List<Vector2Int>();
            for (int c = goal; c != start; c = came[c]) path.Add(new Vector2Int(c % w, c / w));
            path.Reverse();
            return path;
        }

        /// <summary>The closest walkable tile within 5 steps, or null.</summary>
        public static Vector2Int? NearestWalkable(TownMap map, int x, int y)
        {
            if (map.Walkable(x, y)) return new Vector2Int(x, y);
            for (int r = 1; r < 6; r++)
            {
                Vector2Int? best = null;
                int bestD = int.MaxValue;
                for (int j = -r; j <= r; j++)
                    for (int i = -r; i <= r; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(i), Mathf.Abs(j)) != r || !map.Walkable(x + i, y + j)) continue;
                        int d = i * i + j * j;
                        if (d < bestD) { bestD = d; best = new Vector2Int(x + i, y + j); }
                    }
                if (best.HasValue) return best;
            }
            return null;
        }

        /// <summary>Walking distance from a point along a path of tile centres.</summary>
        public static float Length(Vector2 from, List<Vector2Int> path)
        {
            if (path == null) return 0;
            float total = 0;
            var p = from;
            foreach (var t in path)
            {
                var c = new Vector2(t.x + .5f, t.y + .5f);
                total += Vector2.Distance(p, c);
                p = c;
            }
            return total;
        }

        static float Estimate(int x, int y, Vector2Int to)
        {
            int dx = Mathf.Abs(x - to.x), dy = Mathf.Abs(y - to.y);
            return Mathf.Max(dx, dy) + .414f * Mathf.Min(dx, dy);
        }

        sealed class MinHeap
        {
            readonly List<(float f, int i)> items = new List<(float, int)>();
            public int Count => items.Count;

            public void Push(int i, float f)
            {
                items.Add((f, i));
                for (int k = items.Count - 1; k > 0;)
                {
                    int p = (k - 1) / 2;
                    if (items[p].f <= items[k].f) break;
                    (items[p], items[k]) = (items[k], items[p]);
                    k = p;
                }
            }

            public int Pop()
            {
                var top = items[0];
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count > 0)
                {
                    items[0] = last;
                    for (int k = 0; ;)
                    {
                        int l = 2 * k + 1, r = l + 1, m = k;
                        if (l < items.Count && items[l].f < items[m].f) m = l;
                        if (r < items.Count && items[r].f < items[m].f) m = r;
                        if (m == k) break;
                        (items[m], items[k]) = (items[k], items[m]);
                        k = m;
                    }
                }
                return top.i;
            }
        }
    }
}
