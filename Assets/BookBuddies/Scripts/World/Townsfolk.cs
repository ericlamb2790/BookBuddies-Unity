using System.Collections.Generic;
using BookBuddies.Pets;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Pawtopia's villagers going about their day: behind the title screen, in the intro, and in town when
    /// you're offline. They stroll around the square, sit on benches and hop now and then.
    /// </summary>
    public sealed class Townsfolk : MonoBehaviour
    {
        /// <summary>The website's villagers (name and pet look).</summary>
        public static readonly (string name, string look)[] Villagers =
        {
            ("Mayor Biscuit", "{\"h\":32,\"s\":3,\"o\":{},\"sh\":\"round\",\"z\":1,\"f\":\"cute\",\"e\":\"bear\",\"pt\":\"belly\"}"),
            ("Professor Inkwell", "{\"h\":262,\"s\":3,\"o\":{},\"sh\":\"tall\",\"z\":1,\"f\":\"cool\",\"e\":\"cat\",\"pt\":\"none\"}"),
            ("Marigold", "{\"h\":48,\"s\":3,\"o\":{},\"sh\":\"mochi\",\"z\":1,\"f\":\"cute\",\"e\":\"mouse\",\"pt\":\"stripes\"}"),
            ("Barista Beans", "{\"h\":18,\"s\":3,\"o\":{},\"sh\":\"pear\",\"z\":1,\"f\":\"cute\",\"e\":\"bear\",\"pt\":\"belly\"}"),
            ("Pip the Postpet", "{\"h\":200,\"s\":2,\"o\":{},\"sh\":\"bean\",\"z\":0,\"f\":\"goofy\",\"e\":\"bunny\",\"pt\":\"spots\"}"),
            ("Captain Pages", "{\"h\":190,\"s\":3,\"o\":{},\"sh\":\"dino\",\"z\":2,\"f\":\"derp\",\"e\":\"none\",\"pt\":\"spots\"}"),
        };

        static readonly string[] Moves = { "hop", "wave", "dance", "spin", "happy" };
        const float Roam = 9f; // tiles from the town's start point they wander

        TownMap map;
        readonly List<PetActor> folk = new List<PetActor>();
        float nextAt;

        public IReadOnlyList<PetActor> Pets => folk;

        public static Townsfolk Spawn(TownMap map)
        {
            var t = new GameObject("Townsfolk").AddComponent<Townsfolk>();
            t.map = map;
            for (int i = 0; i < Villagers.Length; i++)
            {
                bool sit = i % 2 == 0 && map.Seats.Count > 0;
                Vector2 at = sit ? SeatPoint(map.Seats[(i * 5 + 3) % map.Seats.Count]) : t.RandomSpot();
                var v = PetActor.Spawn(t.transform, map, "v" + i, Villagers[i].name, Villagers[i].look, at, true, false);
                v.Sitting = sit;
                t.folk.Add(v);
            }
            return t;
        }

        void Update()
        {
            if (Time.time < nextAt || folk.Count == 0) return;
            nextAt = Time.time + 1.6f + Random.value * 2.4f;
            var v = folk[Random.Range(0, folk.Count)];
            if (!v || v.Walking) return;
            float r = Random.value;
            if (r < .45f) Stroll(v, RandomSpot());
            else if (r < .7f && map.Seats.Count > 0) { Stroll(v, SeatPoint(map.Seats[Random.Range(0, map.Seats.Count)])); v.WantSit = true; }
            else if (!v.Sitting) v.Play(Moves[Random.Range(0, Moves.Length)], 1.1f);
        }

        void Stroll(PetActor v, Vector2 to)
        {
            var target = new Vector2Int(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y));
            foreach (var other in folk) if (other != v && other.Tile == target) return;
            var path = PathFinder.Find(map, v.Tile, target);
            if (path != null) v.Walk(path);
        }

        Vector2 RandomSpot()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                int x = map.Start.x + Mathf.RoundToInt((Random.value - .5f) * Roam * 2), y = map.Start.y + Mathf.RoundToInt((Random.value - .5f) * Roam * 1.4f);
                if (map.Walkable(x, y)) return new Vector2(x + .5f, y + .5f);
            }
            return new Vector2(map.Start.x + .5f, map.Start.y + .5f);
        }

        static Vector2 SeatPoint(TownMap.Seat s) => new Vector2(s.X + .5f, s.Y + .5f);
    }
}
