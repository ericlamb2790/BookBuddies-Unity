using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Three lanes (left, center, right). Foes stand in lanes too and a boss holds the center; a pet's single-target
    // moves hit the foes in its own lane. Some fights have a lane with cover (25% less damage) or bushes (25% evade).
    public sealed partial class BattleEngine
    {
        const string Lanes = "lcr";
        readonly HashSet<BattleUnit> placed = new HashSet<BattleUnit>();

        /// <summary>The lane with cover or bushes this fight, or null.</summary>
        public string CoverZone { get; private set; }
        /// <summary>"sh" (cover: 25% less damage from foes) or "bu" (bushes: foes miss 25%), or null.</summary>
        public string CoverKind { get; private set; }

        /// <summary>"Left", "Middle" or "Right": the battle screen draws lanes l, c, r as columns.</summary>
        public static string LaneName(char lane) => lane == 'l' ? "Left" : lane == 'r' ? "Right" : "Middle";

        /// <summary>Move a hero to lane l, c or r (dodging a slam). False if not allowed.</summary>
        public bool MoveLane(BattleUnit hero, char lane)
        {
            if (Over || hero == null || hero.IsFoe || hero.Ko || Lanes.IndexOf(lane) < 0 || !LaneOK(lane)) return false;
            SetLane(hero, lane);
            return true;
        }

        /// <summary>True if a pet may stand in this lane now: it has foes, or a slam is coming (then any lane will do).</summary>
        public bool LaneOK(char lane)
        {
            if (slam != null) return true;
            var live = LiveLanes();
            return live.Count == 0 || live.Contains(lane);
        }

        // your pet's lane is kept for its next fight
        void SetLane(BattleUnit hero, char lane)
        {
            hero.Lane = lane;
            if (save != null && hero == Me) save.Me.Lane = lane.ToString();
        }

        // laneInit: new foes take the lane with the fewest live foes from a pool; a boss holds the center
        void LaneInit()
        {
            var todo = Foes.FindAll(f => !placed.Contains(f));
            if (todo.Count == 0) return;
            bool boss = Foes.Exists(f => f.Boss), fresh = placed.Count == 0;
            foreach (var f in todo) if (f.Boss) Place(f, 'c');
            todo.RemoveAll(f => f.Boss);
            int n = todo.Count;
            string pool = boss ? (rng.Next() < .5 ? "lr" : "rl")
                : !fresh ? "clr"
                : n == 1 ? "c" : n == 2 ? new[] { "lr", "lc", "cr" }[rng.Range(3)] : Lanes;
            foreach (var f in todo)
            {
                char bestLane = pool[0];
                foreach (char z in pool) if (FoesIn(z) < FoesIn(bestLane)) bestLane = z;
                Place(f, bestLane);
            }
        }

        void Place(BattleUnit f, char lane) { f.Lane = lane; placed.Add(f); }

        int FoesIn(char lane) => Foes.FindAll(f => placed.Contains(f) && !Dead(f) && f.Lane == lane).Count;

        HashSet<char> LiveLanes()
        {
            var s = new HashSet<char>();
            foreach (var f in Foes) if (!Dead(f)) s.Add(f.Lane);
            return s;
        }

        // laneNear: a lane with no foes sends a pet to the nearest lane that has some
        char LaneNear(char z)
        {
            if (LaneOK(z)) return z;
            var live = LiveLanes();
            var order = z == 'l' ? "cr" : z == 'r' ? "cl" : FoesIn('l') >= FoesIn('r') ? "lr" : "rl";
            foreach (char k in order) if (live.Contains(k)) return k;
            return z;
        }

        // laneFix: every pet in an empty lane slides over
        void LaneFix()
        {
            foreach (var h in Heroes)
            {
                if (Dead(h)) continue;
                char to = LaneNear(h.Lane);
                if (to != h.Lane) SetLane(h, to);
            }
        }

        // laneOpp: a pet aims single-target moves at foes in its lane (any foe when its lane is empty)
        List<BattleUnit> LaneOpp(BattleUnit u)
        {
            var all = Opp(u);
            if (u.IsFoe) return all;
            var mine = all.FindAll(f => f.Lane == u.Lane);
            return mine.Count > 0 ? mine : all;
        }

        // tacCv: 60% of fights get a cover or bushes lane
        void RollCover()
        {
            if (rng.Next() >= .6) return;
            CoverZone = Lanes[rng.Range(3)].ToString();
            CoverKind = rng.Next() < .5 ? "sh" : "bu";
        }

        bool InCover(BattleUnit u, string kind) => CoverKind == kind && CoverZone != null && u.Lane == CoverZone[0];

        // tacCov: a foe hitting a pet in cover does 25% less
        double CoverMul(BattleUnit a, BattleUnit d) => a.IsFoe && !d.IsFoe && InCover(d, "sh") ? .75 : 1;

        // tacBush: a foe swinging at a pet in the bushes misses 25% of the time
        bool BushMiss(BattleUnit a, BattleUnit t) => a.IsFoe && !t.IsFoe && InCover(t, "bu") && rng.Next() < .25;
    }
}
