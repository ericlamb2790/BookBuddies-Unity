// Offline only (the Worker has none): a few readers wandering the towns, so a town you play in on your own doesn't
// feel empty. LocalTown runs them like its villagers.

using System;
using System.Collections.Generic;
using System.IO;
using BookBuddies.Economy;

namespace BookBuddies.Local
{
    /// <summary>
    /// The readers who wander the towns offline: four in Pawtopia, two in every other town, none on Bramble Road or in the
    /// caves. They're made like Pawtopia's villagers (a cozy name, a preset look in the website's look JSON, b:1 in the
    /// roster), stroll between the town's seats and the places you can use, sit, chat and emote, and never pick up finds.
    /// </summary>
    static class LocalBots
    {
        static readonly (string name, string look)[] Readers =
        {
            ("Hazel Nutmeg", "{\"h\":28,\"s\":3,\"o\":{\"hat\":\"beret\"},\"sh\":\"round\",\"z\":1,\"f\":\"cute\",\"e\":\"fox\",\"pt\":\"belly\",\"bg\":\"library\"}"),
            ("Clover Puddle", "{\"h\":110,\"s\":3,\"o\":{\"neck\":\"scarf\"},\"sh\":\"mochi\",\"z\":1,\"f\":\"shy\",\"e\":\"bunny\",\"pt\":\"spots\",\"bg\":\"meadow\"}"),
            ("Juniper Quill", "{\"h\":270,\"s\":3,\"o\":{},\"sh\":\"cloud\",\"z\":0,\"f\":\"dreamy\",\"e\":\"cat\",\"pt\":\"none\",\"bg\":\"library\"}"),
            ("Toffee Bramble", "{\"h\":35,\"s\":3,\"o\":{\"hat\":\"beanie\"},\"sh\":\"bean\",\"z\":1,\"f\":\"goofy\",\"e\":\"bear\",\"pt\":\"patch\",\"bg\":\"meadow\"}"),
            ("Willow Fern", "{\"h\":150,\"s\":3,\"o\":{},\"sh\":\"drop\",\"z\":1,\"f\":\"cute\",\"e\":\"elf\",\"pt\":\"freckles\",\"bg\":\"meadow\"}"),
            ("Biscuit Maple", "{\"h\":22,\"s\":3,\"o\":{\"neck\":\"bowtie\"},\"sh\":\"pear\",\"z\":2,\"f\":\"cool\",\"e\":\"floppy\",\"pt\":\"belly\",\"bg\":\"beach\"}"),
            ("Pebble Inkpot", "{\"h\":215,\"s\":3,\"o\":{},\"sh\":\"gumdrop\",\"z\":1,\"f\":\"starry\",\"e\":\"mouse\",\"pt\":\"sparkle\",\"bg\":\"library\"}"),
            ("Rosie Thimble", "{\"h\":340,\"s\":3,\"o\":{\"hat\":\"flowers\"},\"sh\":\"heart\",\"z\":0,\"f\":\"cute\",\"e\":\"none\",\"pt\":\"hearts\",\"bg\":\"meadow\"}"),
            ("Moss Lanternby", "{\"h\":95,\"s\":3,\"o\":{},\"sh\":\"tall\",\"z\":1,\"f\":\"shy\",\"e\":\"antenna\",\"pt\":\"stripes\",\"bg\":\"meadow\"}"),
            ("Sunny Crumpet", "{\"h\":45,\"s\":3,\"o\":{\"neck\":\"scarf\"},\"sh\":\"square\",\"z\":1,\"f\":\"goofy\",\"e\":\"bat\",\"pt\":\"dip\",\"bg\":\"beach\"}"),
        };

        /// <summary>What readers say now and then (nothing that only fits Pawtopia).</summary>
        internal static readonly string[] Lines =
        {
            "Just one more chapter… 📖", "Anyone reading something good?", "This is the perfect reading spot ✨", "Who wants to start a Tale with me? 📜",
            "I spotted a coin around here earlier 🪙", "Hello neighbor! 👋", "My bookmark keeps wandering off 🔖", "Has anyone ridden the Paw Express? 🚂",
            "The shop here has such lovely books 📚", "Saving up for something cozy 🧸",
        };

        static readonly Dictionary<string, int[][]> points = new Dictionary<string, int[][]>();

        /// <summary>The readers in a town's room as it wakes up, and where each starts ([x, y, 1 if it's a seat]); none when the town's map can't be read.</summary>
        internal static IEnumerable<(string name, string look, int[] at)> Arrive(string town, Random rng)
        {
            var spots = Points(town);
            if (spots.Length == 0) yield break;
            int n = town == "pawtopia" ? 4 : 2, first = rng.Next(Readers.Length), start = rng.Next(spots.Length);
            for (int i = 0; i < n; i++)
            {
                var (name, look) = Readers[(first + i) % Readers.Length];
                yield return (name, look, spots[(start + i * Math.Max(1, spots.Length / n)) % spots.Length]); // spread out
            }
        }

        /// <summary>Where readers walk in a town, [x, y, 1 if it's a seat]: its seats and where you stand to use its places (not the road gates).</summary>
        internal static int[][] Points(string town)
        {
            if (!points.TryGetValue(town, out var list)) points[town] = list = Load(town);
            return list;
        }

        // from the town's map file, Data/town_<key> (it loads the way economy.json does)
        static int[][] Load(string town)
        {
            var found = new List<int[]>();
            Dictionary<string, object> map;
            try { map = EconomyData.TextLoader == null ? null : Json.ParseObject(EconomyData.TextLoader("Data/town_" + town)); }
            catch (Exception e) when (e is IOException || e is FormatException) { map = null; }
            foreach (Dictionary<string, object> s in map.Arr("seats")) found.Add(new[] { s.Int("x"), s.Int("y"), 1 });
            foreach (Dictionary<string, object> s in map.Arr("spots"))
            {
                var use = s.Ints("use");
                if (use.Length == 2 && s.Str("k") != "road") found.Add(new[] { use[0], use[1], 0 });
            }
            return found.ToArray();
        }
    }
}
