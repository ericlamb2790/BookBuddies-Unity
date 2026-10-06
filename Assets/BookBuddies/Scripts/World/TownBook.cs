using System;
using System.Collections.Generic;
using BookBuddies.Tales;

namespace BookBuddies.World
{
    /// <summary>
    /// Data/towns.json: what the game says and shows about each town on Bramble Road (names, lore, landmark lines,
    /// curios, area flavour, cottage folk, music), the silly gags and the storybook folk, exported from the website by
    /// tools/export_towns.js. The route's order and levels come from TalesData. Pure C#: TextLoader is set by TalesBoot.
    /// </summary>
    public sealed class TownBook
    {
        /// <summary>Something to poke at: a town's curio or a gag (icon, name, verb and what it might say).</summary>
        public sealed class Poke { public string Icon, Name, Verb; public string[] Lines; }

        /// <summary>A storybook character. Count &gt; 1 means a crowd of them in different colours.</summary>
        public sealed class Character
        {
            public string Name, Icon, Look;
            public string[] Lines;
            public int Count;

            /// <summary>The pet look of one copy: each copy's hue turns 17 degrees further.</summary>
            public string LookFor(int copy)
            {
                if (Count <= 1 || copy == 0) return Look;
                var look = Json.ParseObject(Look);
                look["h"] = (double)(((int)look.Num("h") + copy * 17) % 360);
                return Json.Write(look);
            }
        }

        /// <summary>One town: its names, colours, lore and lines.</summary>
        public sealed class Town
        {
            public string Key, Name, Icon, Genre, Sub, Shop, Keeper, Roof, Landmark, Square, Pond, Lore;
            public int Level, MusicRoot;
            public bool Fog, Dusk;
            public string[] Sky, Lines, House;
            public int[] Stone, Scale;
            /// <summary>Area flavour by role: shop, sq (the square), ex (the station), pond.</summary>
            public Dictionary<string, object> Role;
            public readonly List<Poke> Curios = new List<Poke>();
            /// <summary>Everything else the site keeps on the town (subj, books, items), for the shop.</summary>
            public Dictionary<string, object> Raw;
        }

        public static Func<string, string> TextLoader;
        static TownBook current;
        public static TownBook Current => current ?? (current = new TownBook(Json.ParseObject(TextLoader("Data/towns"))));

        public readonly Dictionary<string, Town> Towns = new Dictionary<string, Town>();
        public readonly List<Poke> Gags = new List<Poke>();
        public readonly List<Character> Folk = new List<Character>();
        public readonly string[] Memes;
        readonly Dictionary<string, object> places;

        public TownBook(Dictionary<string, object> j)
        {
            foreach (var kv in j.Obj("towns")) Towns[kv.Key] = ReadTown(kv.Key, (Dictionary<string, object>)kv.Value);
            foreach (Dictionary<string, object> g in j.Arr("gags")) Gags.Add(ReadPoke(g));
            foreach (Dictionary<string, object> f in j.Arr("folk"))
                Folk.Add(new Character { Name = f.Str("n"), Icon = f.Str("i"), Look = f.Str("look"), Lines = Strings(f.Arr("lines")), Count = f.Int("count", 1) });
            Memes = Strings(j.Arr("memes"));
            places = j.Obj("places") ?? new Dictionary<string, object>();
        }

        /// <summary>A town by key, or null (roads and caves aren't towns).</summary>
        public Town Get(string key) => key != null && Towns.TryGetValue(key, out var t) ? t : null;

        // ---- the route ----

        static string[] Route => TalesData.Current.Route;

        /// <summary>Where a town sits on Bramble Road (0 = Pawtopia), or -1.</summary>
        public static int Index(string key) => Array.IndexOf(Route, key);

        /// <summary>The map of road link l, which runs from Route[l-1] (west) to Route[l] (east).</summary>
        public static string LinkPlace(int link) => "road" + link;

        /// <summary>The level of the foes on road link l: its tier's base, or from link 8 on three under the link's reader level.</summary>
        public static int FoeLevel(int link)
        {
            var data = TalesData.Current;
            int t = data.LinkT[link];
            return Math.Max(data.LinkBase[t - 1], link >= 8 ? data.RouteLv[link] - 3 : 0);
        }

        /// <summary>Where you come into a town walking off the road: its west gate from the west, its east gate from the east.</summary>
        public static int[] ArrivalFromRoad(string key, bool fromWest) => key == "pawtopia" ? new[] { 77, 40 } : fromWest ? new[] { 4, 22 } : new[] { 53, 22 };

        /// <summary>The road's middle row at column x on link l (the site's rRY): where you arrive from the east.</summary>
        public static int RoadY(int x, int link)
        {
            double p = link * 1.7;
            return JsMath.Clamp(JsMath.RoundI(26 + 5 * Math.Sin(x / 9.0 + p) + 2 * Math.Sin(x / 4.3 + 1 + p * 2)), 14, 38);
        }

        // ---- flavour ----

        /// <summary>
        /// What the area banner can say under an area's name in a town (the site's wwLine): the town's own line for
        /// its shop, square, station or pond, else a line for a named place, else the brambles at a road out.
        /// Empty when there is nothing to add. (The site looked up named places first, so every station said
        /// Pawtopia's lines; the town's own come first here.)
        /// </summary>
        public string[] AreaLines(string townKey, string area)
        {
            var t = Get(townKey);
            string role = t?.Role == null ? null : area == t.Shop ? "shop" : area == t.Square ? "sq" : area == "Paw Express" ? "ex" : area == t.Pond ? "pond" : null;
            if (role != null) return new[] { t.Role.Str(role) };
            if (places.TryGetValue(area, out var lines)) return Strings((List<object>)lines);
            return t?.Role != null && area.StartsWith("Road to ") ? new[] { "Brambles creep in from the edge of town. Foes are close." } : new string[0];
        }

        // ---- reading ----

        static Town ReadTown(string key, Dictionary<string, object> o)
        {
            var music = o.Obj("music");
            var t = new Town
            {
                Key = key, Name = o.Str("n"), Icon = o.Str("i"), Genre = o.Str("g"), Sub = o.Str("sub"), Shop = o.Str("shop"), Keeper = o.Str("keeper"),
                Roof = o.Str("roof", null), Landmark = o.Str("mon"), Square = o.Str("sq", null), Pond = o.Str("pond", null), Lore = o.Str("lore"),
                Level = o.Int("lv", 1), Fog = o.Truthy("fog"), Dusk = o.Truthy("dusk"),
                Sky = Strings(o.Arr("sky")), Lines = Strings(o.Arr("lines")), House = Strings(o.Arr("house")),
                Stone = o.Ints("stone"), MusicRoot = music.Int("root", 60), Scale = music.Ints("scale"),
                Role = o.Obj("role"), Raw = o,
            };
            foreach (Dictionary<string, object> c in o.Arr("curios")) t.Curios.Add(ReadPoke(c));
            return t;
        }

        static Poke ReadPoke(Dictionary<string, object> o) => new Poke { Icon = o.Str("i"), Name = o.Str("n"), Verb = o.Str("v"), Lines = Strings(o.Arr("o")) };

        static string[] Strings(List<object> a)
        {
            var s = new string[a.Count];
            for (int i = 0; i < s.Length; i++) s[i] = (string)a[i];
            return s;
        }
    }
}
