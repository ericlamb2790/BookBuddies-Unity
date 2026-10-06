using System.Collections.Generic;
using BookBuddies.Pets;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// The storybook characters living in a town on Bramble Road (the site's folkSpawn): they stand where the
    /// exporter placed them, a couple near you take a little stroll every 1.8 s, and now and then one mutters a
    /// line. Tap one and it turns to you and talks. Names only show close by, so thirty of them don't bury the
    /// town in labels.
    /// </summary>
    public sealed class StoryFolk : MonoBehaviour
    {
        const float Every = 1.8f, NearX = 14, NearY = 12, NameRange = 3;
        const float MutterChance = .35f, MutterSeconds = 4.2f, TalkSeconds = 6.5f, OwnLineChance = .55f;
        const int StrollersPerTick = 2, LongestStroll = 10;

        TownMap map;
        PetActor me;
        readonly Dictionary<PetActor, TownBook.Character> who = new Dictionary<PetActor, TownBook.Character>();
        float nextAt;

        /// <summary>The folk's pets (they aren't in the live room, so PlazaWorld.Actors adds them).</summary>
        public IEnumerable<PetActor> Pets => who.Keys;

        /// <summary>Puts a town's folk on the map. "me" is your pet: they stroll near it and turn to it.</summary>
        public static StoryFolk Spawn(Transform parent, TownMap map, PetActor me)
        {
            var f = new GameObject("Storybook folk").AddComponent<StoryFolk>();
            f.transform.SetParent(parent, false);
            f.map = map;
            f.me = me;
            var book = TownBook.Current.Folk;
            foreach (var r in map.Folk)
            {
                if (r.Folk < 0 || r.Folk >= book.Count) continue;
                var c = book[r.Folk];
                var a = PetActor.Spawn(f.transform, map, "sf" + f.who.Count, c.Name, c.LookFor(r.Copy), new Vector2(r.At.x + .5f, r.At.y + .5f), true, false);
                f.who[a] = c;
            }
            return f;
        }

        /// <summary>True for one of this town's storybook characters.</summary>
        public bool Has(PetActor a) => a != null && who.ContainsKey(a);

        /// <summary>folkSay: turns to you and says one of its own lines (or a silly one), with its emote and a boop.</summary>
        public void Talk(PetActor a) => Say(a, false);

        void Say(PetActor a, bool quiet)
        {
            if (!who.TryGetValue(a, out var c) || c.Lines.Length == 0) return;
            var memes = TownBook.Current.Memes;
            string line = Random.value < OwnLineChance || memes.Length == 0 ? c.Lines[Random.Range(0, c.Lines.Length)] : memes[Random.Range(0, memes.Length)];
            a.Say(line, quiet ? MutterSeconds : TalkSeconds);
            if (quiet) return;
            a.ShowEmote(c.Icon);
            a.Dir = me.Pos.x > a.Pos.x ? 1 : -1;
            Sound.Play("boop");
        }

        void Update()
        {
            if (me == null) return;
            ShowNames();
            if (Time.time < nextAt) return;
            nextAt = Time.time + Every;
            Stroll();
        }

        // a couple of idle folk near you step a few tiles; sometimes one of them mutters
        void Stroll()
        {
            var near = new List<PetActor>();
            foreach (var a in who.Keys)
                if (!a.Walking && !Talking(a) && Mathf.Abs(a.Pos.x - me.Pos.x) < NearX && Mathf.Abs(a.Pos.y - me.Pos.y) < NearY) near.Add(a);
            for (int k = 0; k < StrollersPerTick && near.Count > 0; k++)
            {
                int i = Random.Range(0, near.Count);
                var a = near[i];
                near.RemoveAt(i);
                var to = a.Tile + new Vector2Int(Random.Range(-3, 4), Random.Range(-2, 3));
                if (!map.Walkable(to.x, to.y)) continue;
                var path = PathFinder.Find(map, a.Tile, to);
                if (path != null && path.Count < LongestStroll) a.Walk(path);
            }
            if (near.Count > 0 && Random.value < MutterChance) Say(near[Random.Range(0, near.Count)], true);
        }

        static bool Talking(PetActor a) => a.Bubble != null && Time.time < a.BubbleUntil;

        // the nearest one within 3 tiles shows its name, and so does anyone talking
        void ShowNames()
        {
            PetActor nearest = null;
            float best = NameRange;
            foreach (var a in who.Keys)
            {
                float d = Vector2.Distance(a.Pos, me.Pos);
                if (d < best) { best = d; nearest = a; }
            }
            foreach (var a in who.Keys) a.NameShown = a == nearest || Talking(a);
        }
    }
}
