using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    public enum WildKind { Road, Cave, Indoor }

    /// <summary>
    /// What makes a map part of the wilds (Bramble Road, the Inkwell Caves): which stretch of road it is, how tough
    /// its foes are, where the tall grass grows, the lanterns and crystals, the weather and the critters.
    /// Read from the "wild" block of Data/town_&lt;key&gt;.json (made by tools/export_road.js).
    /// </summary>
    public sealed class WildInfo
    {
        public sealed class Light { public Vector2 At; public Color Color; public float Radius; }
        public sealed class End { public string Key, Name, Icon; }
        public sealed class Critter { public string Emoji; public char Kind; public int Count; } // kind: g hops, r runs, a flies, w swims

        public WildKind Kind;
        public int Tier, Link, Level;
        public string Music, Weather, GrassArt; // GrassArt + variant (0-2) names the tall-grass sprite
        public End West, East;                  // the towns at each end of a road (null in the caves)
        public readonly List<Critter> Life = new List<Critter>();
        public readonly List<Light> Lights = new List<Light>();
        public readonly Dictionary<int, int> Tall = new Dictionary<int, int>(); // tile index -> tuft variant

        public bool IsCave => Kind == WildKind.Cave;
        public bool IsRoad => Kind == WildKind.Road;

        public static WildInfo From(Dictionary<string, object> j, int width)
        {
            var w = new WildInfo
            {
                Kind = j.Str("kind") == "cave" ? WildKind.Cave : j.Str("kind") == "indoor" ? WildKind.Indoor : WildKind.Road,
                Tier = j.Int("tier", 1), Link = j.Int("link", 1), Level = j.Int("lvl", 2),
                Music = j.Str("music", "wild"), Weather = j.Str("fx"), GrassArt = j.Str("grass", null),
                West = ReadEnd(j.Obj("west")), East = ReadEnd(j.Obj("east")),
            };
            w.Life.AddRange(ReadLife(j.Arr("life")));
            foreach (List<object> l in j.Arr("lights"))
                w.Lights.Add(new Light { At = new Vector2((float)(double)l[0], (float)(double)l[1]), Color = Palette.Hex((string)l[2]), Radius = (float)(double)l[3] });
            foreach (List<object> t in j.Arr("tall"))
                w.Tall[(int)(double)t[1] * width + (int)(double)t[0]] = (int)(double)t[2];
            return w;
        }

        /// <summary>Critters as exported: [emoji, kind, count] each.</summary>
        public static List<Critter> ReadLife(List<object> rows)
        {
            var life = new List<Critter>();
            foreach (List<object> c in rows) life.Add(new Critter { Emoji = (string)c[0], Kind = ((string)c[1])[0], Count = (int)(double)c[2] });
            return life;
        }

        static End ReadEnd(Dictionary<string, object> o) => o == null ? null : new End { Key = o.Str("k"), Name = o.Str("n"), Icon = o.Str("i") };
    }
}
