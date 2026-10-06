using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    public enum Tile : byte { Grass, Path, Stone, Water, Sand, Soil, Wood, Prism, Carnival }

    /// <summary>
    /// A town's layout, loaded from Resources/BookBuddies/Data/town_&lt;key&gt;.json (exported from the website).
    /// Map coordinates: x to the right, y down the map (toward the viewer), one unit per tile.
    /// </summary>
    public sealed class TownMap
    {
        public sealed class Placed { public string Kind, Sprite; public int X, Y, W, H; }
        public sealed class Seat { public int X, Y; public string Kind; public int Facing; }
        public sealed class Spot { public string Kind, Icon, Name, Sub, Verb; public RectInt Area; public Vector2Int Use; }
        public sealed class Region { public RectInt Area; public string Name; }

        public string Key, Name;
        public int Width, Height;
        public Vector2Int Start;
        public Tile[] Tiles;
        public bool[] Blocked;
        public readonly List<Placed> Objects = new List<Placed>();
        public readonly List<Seat> Seats = new List<Seat>();
        public readonly List<Spot> Spots = new List<Spot>();
        public readonly List<Region> Areas = new List<Region>();
        public readonly List<Vector2Int> Plots = new List<Vector2Int>();
        public readonly Dictionary<string, float> SeatLift = new Dictionary<string, float>();

        public static TownMap Load(string key)
        {
            var j = Json.ParseObject(Art.Text("Data/town_" + key));
            var m = new TownMap { Key = j.Str("key"), Name = j.Str("name"), Width = j.Int("w"), Height = j.Int("h") };
            var start = j.Ints("start");
            m.Start = new Vector2Int(start[0], start[1]);
            m.Tiles = new Tile[m.Width * m.Height];
            m.Blocked = new bool[m.Width * m.Height];
            var ground = j.Arr("ground");
            var block = j.Arr("block");
            for (int y = 0; y < m.Height; y++)
                for (int x = 0; x < m.Width; x++)
                {
                    m.Tiles[y * m.Width + x] = (Tile)(((string)ground[y])[x] - '0');
                    m.Blocked[y * m.Width + x] = ((string)block[y])[x] != '0';
                }
            foreach (Dictionary<string, object> o in j.Arr("objects"))
                m.Objects.Add(new Placed { Kind = o.Str("k"), Sprite = o.Str("s"), X = o.Int("x"), Y = o.Int("y"), W = o.Int("w", 1), H = o.Int("h", 1) });
            foreach (Dictionary<string, object> s in j.Arr("seats"))
                m.Seats.Add(new Seat { X = s.Int("x"), Y = s.Int("y"), Kind = s.Str("k"), Facing = s.Int("f") });
            foreach (Dictionary<string, object> s in j.Arr("spots"))
            {
                var r = s.Ints("r");
                var use = s.Ints("use");
                m.Spots.Add(new Spot
                {
                    Kind = s.Str("k"), Icon = s.Str("i"), Name = s.Str("n"), Sub = s.Str("sub"), Verb = s.Str("v", "Go"),
                    Area = new RectInt(r[0], r[1], r[2] - r[0], r[3] - r[1]), Use = new Vector2Int(use[0], use[1]),
                });
            }
            foreach (List<object> a in j.Arr("areas"))
                m.Areas.Add(new Region { Area = new RectInt((int)(double)a[0], (int)(double)a[1], (int)((double)a[2] - (double)a[0]), (int)((double)a[3] - (double)a[1])), Name = (string)a[4] });
            foreach (Dictionary<string, object> p in j.Arr("plots"))
                m.Plots.Add(new Vector2Int(p.Int("x"), p.Int("y")));
            foreach (var kv in j.Obj("seatLift")) m.SeatLift[kv.Key] = (float)(double)kv.Value;
            return m;
        }

        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool Walkable(int x, int y) => Inside(x, y) && !Blocked[y * Width + x];
        public Tile TileAt(int x, int y) => Inside(x, y) ? Tiles[y * Width + x] : Tile.Water;

        public Seat SeatAt(int x, int y) => Seats.Find(s => s.X == x && s.Y == y);

        /// <summary>How far a pet sinks into this seat (negative lifts it up the screen).</summary>
        public float LiftAt(int x, int y)
        {
            var s = SeatAt(x, y);
            return s != null && SeatLift.TryGetValue(s.Kind, out var lift) ? lift : 0f;
        }

        public Spot SpotAt(int x, int y) => Spots.Find(s => s.Kind != "garden" && x >= s.Area.xMin && x <= s.Area.xMax && y >= s.Area.yMin && y <= s.Area.yMax);

        /// <summary>Name of the neighbourhood at a tile (falls back to the town name).</summary>
        public string AreaAt(int x, int y)
        {
            foreach (var a in Areas)
                if (x >= a.Area.xMin && x <= a.Area.xMax && y >= a.Area.yMin && y <= a.Area.yMax) return a.Name;
            return Name;
        }

        /// <summary>Map (x, y) to a world position on the ground. World X = map x, world Z = -map y.</summary>
        public static Vector3 ToWorld(float x, float y, float height = 0) => new Vector3(x, height, -y);

        public static Vector2 ToMap(Vector3 world) => new Vector2(world.x, -world.z);
    }
}
