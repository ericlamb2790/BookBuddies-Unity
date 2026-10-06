using System;
using System.Collections.Generic;
using System.Globalization;
using BookBuddies.Tales;

namespace BookBuddies.World
{
    /// <summary>
    /// Paints a map's ground the way the website's paintGround does: grass with tufts and flowers, sandy pond banks,
    /// paths with pebbles, cobbles, planks, the town square's outline and lily pads, the wilds' wash and cave rock.
    /// Towns and roads ship only their tiles and palette; this draws them into an RGB buffer, one square chunk at a time.
    /// Pure C# (no Unity types), so it can be checked against the site's own pixels. A baked ground PNG still wins.
    /// </summary>
    public sealed class GroundPainter
    {
        const int Grass = 0, Path = 1, Stone = 2, Water = 3, Sand = 4, Wood = 6, Rock = 9;

        /// <summary>A colour as the site writes it: channels 0-255 and alpha 0-1.</summary>
        public readonly struct Paint
        {
            public readonly float R, G, B, A;
            public Paint(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }

            /// <summary>Reads "#rgb", "#rrggbb", "#rrggbbaa" or "rgba(r,g,b,a)".</summary>
            public static Paint Parse(string css)
            {
                if (css.StartsWith("rgba("))
                {
                    var p = css.Substring(5, css.Length - 6).Split(',');
                    float N(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                    return new Paint(N(0), N(1), N(2), N(3));
                }
                string h = css.Substring(1);
                if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
                int Hex(int i) => Convert.ToInt32(h.Substring(i, 2), 16);
                return new Paint(Hex(0), Hex(2), Hex(4), h.Length >= 8 ? Hex(6) / 255f : 1);
            }
        }

        static readonly Paint LightGrass = Paint.Parse("rgba(120,180,80,.35)"), PaleGrass = Paint.Parse("rgba(180,225,130,.28)"),
            Blade = Paint.Parse("rgba(70,130,50,.35)"), Bank = Paint.Parse("#f1dca8"), SandDot = Paint.Parse("#d9bf85"),
            Shore = Paint.Parse("#f6e7bd"), Pond = Paint.Parse("#6cc0df"), Deep = Paint.Parse("rgba(40,120,170,.16)"),
            Pebble = Paint.Parse("rgba(150,110,60,.25)"), Shine = Paint.Parse("rgba(255,255,255,.35)"),
            Kerb = Paint.Parse("rgba(120,100,80,.3)"), Ring = Paint.Parse("rgba(120,100,80,.25)"), Pad = Paint.Parse("#5fa84e"),
            Plank = Paint.Parse("#c4925c"), Seam = Paint.Parse("#9b6c3e"), CaveFloor = Paint.Parse("rgba(18,12,28,.42)"),
            CaveRock = Paint.Parse("#17121f"), Fleck = Paint.Parse("rgba(120,100,150,.12)");
        static readonly Paint[] Flowers = { Paint.Parse("#fff"), Paint.Parse("#ffe066"), Paint.Parse("#ff9ec4"), Paint.Parse("#b8a4ff") };
        static readonly float[,] LilyPads = { { 42, 33 }, { 44.5f, 37.6f }, { 49, 33.4f }, { 50.5f, 37 } };

        readonly int width, height;
        readonly byte[] tiles;
        readonly HashSet<int> seats;
        readonly Paint grass, path, stone, stone2, wash;

        /// <summary>Genre towns: the square's outline, the fountain ring and the pond's lily pads.</summary>
        public bool Square;
        /// <summary>Bramble Road's tier (2 and up wash the grass in the tier's colour); 0 off the road.</summary>
        public int Tier;
        /// <summary>The caves: darkened floor and black rock.</summary>
        public bool Cave;

        byte[] buf;
        int size, ppu;
        float left, top;
        bool upward;

        /// <param name="tiles">One tile code per cell, row by row (the map's ground rows).</param>
        /// <param name="seats">Seat cells (y * width + x): no flowers grow under them.</param>
        /// <param name="palette">grass, path, stone and stone2 colours.</param>
        public GroundPainter(int width, int height, byte[] tiles, HashSet<int> seats, string[] palette)
        {
            this.width = width; this.height = height; this.tiles = tiles; this.seats = seats ?? new HashSet<int>();
            grass = Paint.Parse(palette[0]); path = Paint.Parse(palette[1]); stone = Paint.Parse(palette[2]); stone2 = Paint.Parse(palette[3]);
            wash = Paint.Parse(palette[0] + "8c");
        }

        /// <summary>
        /// Paints chunk (cx, cy) of `chunk` tiles at `pixelsPerTile` into rgb (3 bytes per pixel, chunk*ppu square).
        /// Rows run top to bottom, or bottom to top when upward (Unity's texture order).
        /// </summary>
        public void PaintChunk(int cx, int cy, int chunk, int pixelsPerTile, byte[] rgb, bool upward)
        {
            buf = rgb; ppu = pixelsPerTile; size = chunk * ppu; this.upward = upward;
            int X0 = cx * chunk, Y0 = cy * chunk;
            left = X0; top = Y0;
            int x0 = Math.Max(0, X0 - 1), y0 = Math.Max(0, Y0 - 1), x1 = Math.Min(width - 1, X0 + chunk), y1 = Math.Min(height - 1, Y0 + chunk);
            FillRect(X0, Y0, chunk, chunk, grass);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (At(x, y) == Grass) GrassTile(x, y);
            Blobs(x0, y0, x1, y1, Sand, Water, Bank, .06f, .25f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (At(x, y) == Sand && JsMath.Hsh(x, y) > .5) Ellipse(x + (float)JsMath.Hsh(y, x), y + (float)JsMath.Hsh(x + 1, y), .03f, .03f, SandDot);
            Blobs(x0, y0, x1, y1, Water, Water, Shore, .16f, .45f);
            Blobs(x0, y0, x1, y1, Water, Water, Pond, .03f, .38f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (At(x, y) == Water && OpenWater(x, y)) FillRect(x, y, 1, 1, Deep);
            Blobs(x0, y0, x1, y1, Path, Path, path, .08f, .32f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (At(x, y) == Path)
                    for (int k = 0; k < 2; k++) Ellipse(x + (float)JsMath.Hsh(x + k, y) * .9f + .05f, y + (float)JsMath.Hsh(y, x + k) * .9f + .05f, .05f, .035f, Pebble);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (At(x, y) == Stone) Cobbles(x, y);
            if (Square) TownSquare(x0, y0, x1, y1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (At(x, y) == Wood) Planks(x, y);
            if (Cave) CaveRocks(x0, y0, x1, y1);
            else if (Tier > 1)
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (At(x, y) == Grass) FillRect(x, y, 1, 1, wash);
        }

        int At(int x, int y) => tiles[y * width + x];

        // ---- what each tile gets ----

        void GrassTile(int x, int y)
        {
            double r = JsMath.Hsh(x, y);
            if (r < .35) FillRect(x, y, 1, 1, LightGrass);
            else if (r > .8) FillRect(x, y, 1, 1, PaleGrass);
            for (int k = 0; k < 3; k++)
            {
                float a = (float)JsMath.Hsh(x * 3 + k, y * 7), b = (float)JsMath.Hsh(x * 5, y * 3 + k);
                Line(x + a, y + b + .1f, x + a + .04f, y + b - .02f, .035f, Blade);
            }
            if (r <= .9 || seats.Contains(y * width + x)) return;
            var flower = Flowers[(int)Math.Floor(JsMath.Hsh(y, x) * 4)];
            for (int k = 0; k < 3; k++) Ellipse(x + .25f + (float)JsMath.Hsh(k, x) * .5f, y + .25f + (float)JsMath.Hsh(y, k) * .5f, .06f, .06f, flower);
        }

        // Rounded squares a little bigger than each tile of a kind, so ponds, banks and paths flow into each other.
        void Blobs(int x0, int y0, int x1, int y1, int kindA, int kindB, Paint c, float grow, float radius)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int t = At(x, y);
                    if (t == kindA || t == kindB) RoundRect(x - grow, y - grow, 1 + 2 * grow, 1 + 2 * grow, radius, c, 0);
                }
        }

        bool OpenWater(int x, int y)
        {
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int a = x + i, b = y + j;
                    if (a < 0 || b < 0 || a >= width || b >= height) continue;
                    int t = At(a, b);
                    if (t != Water && t != Wood) return false;
                }
            return true;
        }

        void Cobbles(int x, int y)
        {
            FillRect(x, y, 1, 1, stone2);
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    double h = JsMath.Hsh(x * 2 + i, y * 2 + j);
                    RoundRect(x + i * .5f + .04f, y + j * .5f + .04f, .42f, .42f, .08f, h > .7 ? Shine : h < .25 ? stone2 : stone, 0);
                }
        }

        void TownSquare(int x0, int y0, int x1, int y1)
        {
            RoundRect(21, 17, 14, 12, .3f, Kerb, .08f);
            EllipseRing(28, 22.6f, 4.4f, 3.3f, .12f, Ring);
            for (int i = 0; i < LilyPads.GetLength(0); i++)
            {
                float lx = LilyPads[i, 0], ly = LilyPads[i, 1];
                if (lx < x0 || lx > x1 + 1 || ly < y0 || ly > y1 + 1) continue;
                Ellipse(lx, ly, .32f, .22f, Pad);
                Pie(lx, ly, .33f, .3f, Pond);
            }
        }

        void Planks(int x, int y)
        {
            FillRect(x, y, 1, 1, Plank);
            for (int k = 1; k < 3; k++) Line(x + k / 3f, y, x + k / 3f, y + 1, .035f, Seam);
        }

        void CaveRocks(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (At(x, y) != Rock) { FillRect(x, y, 1, 1, CaveFloor); continue; }
                    FillRect(x - .02f, y - .02f, 1.04f, 1.04f, CaveRock);
                    float r = (float)JsMath.Hsh(x, y);
                    if (r > .6) FillRect(x + r * .5f, y + (1 - r) * .6f, .3f, .2f, Fleck);
                }
        }

        // ---- shapes, anti-aliased like a canvas: coverage from each pixel's distance to the edge ----

        void FillRect(float x, float y, float w, float h, Paint c)
        {
            float px0 = (x - left) * ppu, py0 = (y - top) * ppu, px1 = px0 + w * ppu, py1 = py0 + h * ppu;
            int i0 = Math.Max(0, (int)Math.Floor(px0)), i1 = Math.Min(size, (int)Math.Ceiling(px1));
            int j0 = Math.Max(0, (int)Math.Floor(py0)), j1 = Math.Min(size, (int)Math.Ceiling(py1));
            for (int j = j0; j < j1; j++)
            {
                float cy = Math.Min(j + 1, py1) - Math.Max(j, py0);
                for (int i = i0; i < i1; i++) Blend(i, j, c, cy * (Math.Min(i + 1, px1) - Math.Max(i, px0)));
            }
        }

        /// <summary>A rounded rectangle, filled (line 0) or outlined with a line that wide.</summary>
        void RoundRect(float x, float y, float w, float h, float r, Paint c, float line)
        {
            float hx = w / 2, hy = h / 2, mx = x + hx, my = y + hy, pad = line / 2;
            Shape(x - pad, y - pad, x + w + pad, y + h + pad, c, (px, py) =>
            {
                float qx = Math.Abs(px - mx) - hx + r, qy = Math.Abs(py - my) - hy + r;
                float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                float d = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
                return line > 0 ? Math.Abs(d) - pad : d;
            });
        }

        void Ellipse(float cx, float cy, float rx, float ry, Paint c) =>
            Shape(cx - rx, cy - ry, cx + rx, cy + ry, c, (px, py) => EllipseDistance(px - cx, py - cy, rx, ry));

        void EllipseRing(float cx, float cy, float rx, float ry, float line, Paint c) =>
            Shape(cx - rx - line, cy - ry - line, cx + rx + line, cy + ry + line, c, (px, py) => Math.Abs(EllipseDistance(px - cx, py - cy, rx, ry)) - line / 2);

        // f / |grad f| for f = (x/a)^2 + (y/b)^2 - 1: close to the true distance near the outline, which is all AA needs
        static float EllipseDistance(float x, float y, float a, float b)
        {
            float f = x * x / (a * a) + y * y / (b * b) - 1, gx = 2 * x / (a * a), gy = 2 * y / (b * b);
            float g = (float)Math.Sqrt(gx * gx + gy * gy);
            return g < 1e-6f ? -Math.Min(a, b) : f / g;
        }

        /// <summary>A straight stroke with flat ends.</summary>
        void Line(float ax, float ay, float bx, float by, float width, Paint c)
        {
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len, mx = (ax + bx) / 2, my = (ay + by) / 2;
            float pad = width / 2;
            Shape(Math.Min(ax, bx) - pad, Math.Min(ay, by) - pad, Math.Max(ax, bx) + pad, Math.Max(ay, by) + pad, c, (px, py) =>
            {
                float vx = px - mx, vy = py - my;
                return Math.Max(Math.Abs(vx * ux + vy * uy) - len / 2, Math.Abs(vx * -uy + vy * ux) - pad);
            });
        }

        /// <summary>A wedge of a circle pointing right (+x), spread ±half radians.</summary>
        void Pie(float cx, float cy, float r, float half, Paint c)
        {
            float s = (float)Math.Sin(half), k = (float)Math.Cos(half);
            Shape(cx, cy - r, cx + r, cy + r, c, (px, py) =>
            {
                float x = px - cx, y = Math.Abs(py - cy);
                float edge = x * -s + y * k; // distance past the wedge's slanted side
                return Math.Max((float)Math.Sqrt(x * x + y * y) - r, Math.Max(edge, -x));
            });
        }

        void Shape(float x0, float y0, float x1, float y1, Paint c, Func<float, float, float> distance)
        {
            int i0 = Math.Max(0, (int)Math.Floor((x0 - left) * ppu) - 1), i1 = Math.Min(size, (int)Math.Ceiling((x1 - left) * ppu) + 1);
            int j0 = Math.Max(0, (int)Math.Floor((y0 - top) * ppu) - 1), j1 = Math.Min(size, (int)Math.Ceiling((y1 - top) * ppu) + 1);
            for (int j = j0; j < j1; j++)
            {
                float py = top + (j + .5f) / ppu;
                for (int i = i0; i < i1; i++)
                {
                    float cover = .5f - distance(left + (i + .5f) / ppu, py) * ppu;
                    if (cover > 0) Blend(i, j, c, cover > 1 ? 1 : cover);
                }
            }
        }

        void Blend(int i, int j, Paint c, float cover)
        {
            float a = c.A * cover;
            if (a <= 0) return;
            int p = ((upward ? size - 1 - j : j) * size + i) * 3;
            buf[p] = (byte)(buf[p] + (c.R - buf[p]) * a + .5f);
            buf[p + 1] = (byte)(buf[p + 1] + (c.G - buf[p + 1]) * a + .5f);
            buf[p + 2] = (byte)(buf[p + 2] + (c.B - buf[p + 2]) * a + .5f);
        }
    }
}
