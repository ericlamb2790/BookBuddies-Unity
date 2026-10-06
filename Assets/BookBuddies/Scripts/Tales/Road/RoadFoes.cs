using System.Collections;
using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Road
{
    /// <summary>
    /// The villains of Bramble Road and the Inkwell Caves (the site's wildStep): they appear out of sight, mostly
    /// in tall grass, wander near home, chase you when you come close (passive ones don't) and start a fight on
    /// contact. Tall grass rustles as you push through it, and now and then something jumps out. The cave
    /// guardian waits in its lair and comes back 30 minutes after it's beaten.
    /// Foes are local to this player, like the site's. The road's clock stands still while a fight, the bag or a
    /// reward is open, so nothing moves or jumps when you come back.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class RoadFoes : MonoBehaviour
    {
        // the site's numbers (tiles, tiles per second, seconds)
        const float Despawn = 34, Contact = .78f, Repath = .45f, Leash = 2.4f, PackRange = 3.4f;
        const float RoadAggro = 4.4f, CaveAggro = 5.2f, GuardianAggro = 3.4f;
        const float GuardianSpeed = 3.2f, CaveSpeed = 4.05f, RoadSpeed = 4.25f, HomeSpeed = 2.4f, IdleSpeed = 1.3f;
        const float AmbushChance = .07f, LairMinutes = 30, WipeSeconds = .72f;
        static readonly Vector2Int GuardianHome = new Vector2Int(48, 20);
        static readonly Vector2Int[] AmbushSpots = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, -1) };

        /// <summary>A fight from the moment a foe touches you until the battle screen hands back the outcome.</summary>
        sealed class Fight { public List<RoadFoe> Pack; public Vector2 Screen; public float StartedAt, ClosedAt = -1; public bool SawScreen; }

        PlazaWorld world;
        TownMap map;
        WildInfo wild;
        WildTier tier;
        int tierNumber, level; // the caves take both from the road you came in from
        TownView view;
        TownCamera cam;
        Transform root;
        readonly List<RoadFoe> foes = new List<RoadFoe>();
        readonly Dictionary<PetActor, int> grassTiles = new Dictionary<PetActor, int>();
        readonly IRng rng = SystemRng.Shared;
        float clock, spawnAt, grace = 3.5f, cryAt = -99;
        int lastId;
        bool fresh = true;
        Fight fight;
        FightWipe wipe;

        /// <summary>Starts the foes for a wild map (the town's PlazaWorld calls this when the map has a Wild block).</summary>
        public static RoadFoes Attach(PlazaWorld world, TownView view, TownCamera cam)
        {
            var r = world.gameObject.AddComponent<RoadFoes>();
            r.world = world; r.map = world.Map; r.wild = world.Map.Wild; r.view = view; r.cam = cam;
            var data = TalesData.Current;
            int link = Mathf.Clamp(TalesSave.Current.Link, 1, data.LinkT.Length - 1);
            r.tierNumber = Mathf.Clamp(r.wild.IsCave ? data.LinkT[link] : r.wild.Tier, 1, data.Tiers.Count);
            r.tier = data.Tiers[r.tierNumber - 1];
            r.level = r.wild.IsCave ? r.tier.Lvl + 1 : r.wild.Level;
            r.root = new GameObject("Foes").transform;
            r.root.SetParent(world.transform, false);
            return r;
        }

        public IReadOnlyList<RoadFoe> All => foes;
        /// <summary>The road's clock in seconds (it stops while a fight or reward screen is open).</summary>
        public float Clock => clock;
        public bool Fighting => fight != null;
        public bool GuardianAwake => foes.Exists(f => f.Guardian);

        // ---- every frame ----

        void Update()
        {
            var me = world.Me;
            if (me == null) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (fight == null && !Frozen)
            {
                clock += dt;
                Step(me, dt);
            }
            foreach (var f in foes) f.Render(clock, dt);
        }

        // while a fight, the bag or a reward reveal is up, the road waits
        static bool Frozen => BattleScreen.Open || TalesUi.AnyOpen;

        static bool Busy => UiStack.Any || PlazaInput.MenuOpen || PlazaInput.Typing || PlazaInput.Locked;

        void Step(PetActor me, float dt)
        {
            int cap = (wild.IsCave ? 9 : 6 + wild.Tier) + Mathf.Min(6, OthersHere() * 2);
            if (fresh) { fresh = false; for (int i = 0; i < cap - 2; i++) Spawn(me); }
            if (clock > spawnAt)
            {
                spawnAt = clock + (foes.Count < cap ? 2.6f : 6f);
                if (foes.FindAll(f => !f.Guardian).Count < cap) Spawn(me);
            }
            if (wild.IsCave && !TalesSave.Current.ChestReady && !GuardianAwake && LairReady()) Make(GuardianHome.x, GuardianHome.y, true, false);

            if (Busy) grace = Mathf.Max(grace, clock + 1.5f);
            bool safe = Busy || clock < grace || SafeAt(me.Pos.x, me.Pos.y);
            foreach (var f in foes.ToArray())
                if (Think(f, me, safe, dt)) return; // a fight started
            Grass(me, safe);
        }

        // one foe's turn; true when it started a fight
        bool Think(RoadFoe f, PetActor me, bool safe, float dt)
        {
            float d = Vector2.Distance(me.Pos, f.Pos);
            if (!f.Guardian && d > Despawn) { Remove(f); return false; }
            if (f.Stun > clock) return false;
            float aggro = f.Guardian ? GuardianAggro : wild.IsCave ? CaveAggro : RoadAggro;

            if (f.Chasing)
            {
                if (safe || d > aggro * Leash) { f.State = RoadFoe.Mood.Home; SetPath(f, PathFinder.Find(map, f.Tile, f.Home)); return false; }
                if (clock > f.Repath)
                {
                    f.Repath = clock + Repath;
                    var path = PathFinder.Find(map, f.Tile, me.Tile);
                    if (path != null) SetPath(f, path);
                }
                float speed = f.Guardian ? GuardianSpeed : wild.IsCave ? CaveSpeed : RoadSpeed;
                if (f.Path.Count > 1 || d > 1.2f) Walk(f, speed, dt);
                else
                {
                    f.Dir = me.Pos.x > f.Pos.x ? 1 : -1;
                    f.Pos += (me.Pos - f.Pos) / (d > 0 ? d : 1) * Mathf.Min(speed * dt, d);
                }
                if (d < Contact && clock - f.Alert > .38f) { Engage(f); return true; }
                return false;
            }

            if (!safe && d < Contact && clock - f.Born > .6f) { Engage(f); return true; }
            if (!safe && !f.Passive && d < aggro)
            {
                f.State = RoadFoe.Mood.Chase; f.Alert = clock; f.Path.Clear(); f.Repath = 0;
                if (clock - cryAt > 1.5f) { cryAt = clock; Sound.Play("boop"); }
                return false;
            }
            if (f.State == RoadFoe.Mood.Home && f.Path.Count == 0) f.State = RoadFoe.Mood.Idle;
            if (f.Path.Count == 0 && clock > f.Next && !f.Guardian) Wander(f);
            Walk(f, f.State == RoadFoe.Mood.Home ? HomeSpeed : IdleSpeed, dt);
            return false;
        }

        void Wander(RoadFoe f)
        {
            f.Next = clock + 2.2f + (float)rng.Next() * 3.5f;
            int x = f.Home.x + JsMath.RoundI((rng.Next() - .5) * 6), y = f.Home.y + JsMath.RoundI((rng.Next() - .5) * 6);
            if (map.Walkable(x, y) && !SafeAt(x, y)) SetPath(f, PathFinder.Find(map, f.Tile, new Vector2Int(x, y)));
        }

        // the site's fmove: head for the next tile's centre, snapping onto it
        static void Walk(RoadFoe f, float speed, float dt)
        {
            if (f.Path.Count == 0) return;
            var target = new Vector2(f.Path[0].x + .5f, f.Path[0].y + .5f);
            var delta = target - f.Pos;
            float step = speed * dt;
            if (Mathf.Abs(delta.x) > .05f) f.Dir = delta.x > 0 ? 1 : -1;
            if (delta.magnitude <= step) { f.Pos = target; f.Path.RemoveAt(0); }
            else f.Pos += delta.normalized * step;
        }

        static void SetPath(RoadFoe f, List<Vector2Int> path)
        {
            f.Path.Clear();
            if (path != null) f.Path.AddRange(path);
        }

        // ---- tall grass: rustles under anyone walking through, and sometimes hides an ambush ----

        void Grass(PetActor me, bool safe)
        {
            foreach (var a in world.Actors)
            {
                if (a == null || a.Gone || !a.Walking) continue;
                var t = a.Tile;
                int index = t.y * map.Width + t.x;
                if (grassTiles.TryGetValue(a, out int last) && last == index) continue;
                grassTiles[a] = index;
                if (!map.TallAt(t.x, t.y)) continue;
                view.RustleAt(t.x, t.y);
                if (a != me) continue;
                Fx.Poof(me.Pos + new Vector2(0, .1f), .5f);
                if (!safe && rng.Chance(AmbushChance)) Ambush(me);
            }
        }

        void Ambush(PetActor me)
        {
            var t = me.Tile;
            foreach (var d in AmbushSpots)
            {
                if (!map.Walkable(t.x + d.x, t.y + d.y)) continue;
                var f = Make(t.x + d.x, t.y + d.y, false, true);
                f.Passive = false; f.State = RoadFoe.Mood.Chase; f.Alert = clock;
                world.Halt();
                me.ShowEmote("❗");
                return;
            }
        }

        // ---- who appears where ----

        void Spawn(PetActor me)
        {
            for (int k = 0; k < 60; k++)
            {
                int x = 2 + rng.Range(map.Width - 4), y = 2 + rng.Range(map.Height - 4);
                if (!map.Walkable(x, y) || SafeAt(x, y)) continue;
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), me.Pos);
                if (d < 7 || d > 18) continue;
                if (wild.IsCave && x > 42) continue; // the lair is the guardian's
                if (!wild.IsCave && !map.TallAt(x, y) && rng.Chance(.6)) continue;
                Make(x, y, false, false);
                return;
            }
        }

        /// <summary>The site's mkFoe: elite and villain-elite rolls, a variant, a level, and on the road a local villain from the nearer town's genre.</summary>
        RoadFoe Make(int x, int y, bool guardian, bool ambush)
        {
            var data = TalesData.Current;
            bool elite = !guardian && rng.Chance(wild.IsCave ? tier.EliteChance + .12 : tier.EliteChance);
            bool villainElite = elite && tier.Elites.Length > 0 && rng.Chance(.6);
            var def = guardian ? rng.Pick(data.Bosses)
                : villainElite ? data.Bosses[rng.Pick(tier.Elites) % data.Bosses.Count]
                : data.Minions[rng.Pick(tier.Foes) % data.Minions.Count];
            var f = new GameObject("Foe").AddComponent<RoadFoe>();
            f.V = FoeFactory.Variant(def, guardian, elite, rng);
            f.Id = ++lastId;
            f.Guardian = guardian; f.Elite = elite; f.Ambusher = ambush;
            f.Lvl = level + (guardian ? 2 : elite ? 1 : 0) + (rng.Chance(.25) ? 1 : 0);
            f.Pos = new Vector2(x + .5f, y + .5f); f.Home = new Vector2Int(x, y);
            f.Dir = rng.Chance(.5) ? 1 : -1;
            f.Born = clock;
            f.Passive = !guardian && !ambush && rng.Chance(.45);
            if (!guardian && !elite && wild.IsRoad && !rng.Chance(.4)) LocalGenre(f, x);
            f.name = "Foe " + f.V.Name;
            f.Build(root);
            foes.Add(f);
            return f;
        }

        // foes near each end of the road come from that town's genre
        void LocalGenre(RoadFoe f, int x)
        {
            var data = TalesData.Current;
            int link = Mathf.Clamp(wild.Link, 1, data.Route.Length - 1);
            string town = x < map.Width * .5f ? data.Route[link - 1] : data.Route[link];
            if (!data.TownGenre.TryGetValue(town, out int genre)) return;
            var pool = data.Minions.FindAll(m => m.G == genre && string.IsNullOrEmpty(m.Rg));
            if (pool.Count > 0) f.V = FoeFactory.Variant(rng.Pick(pool), false, false, rng);
        }

        void Remove(RoadFoe f)
        {
            foes.Remove(f);
            Destroy(f.gameObject);
        }

        /// <summary>Near the start or a place you can use (4 tiles around the trailhead), foes leave you be.</summary>
        bool SafeAt(float x, float y)
        {
            if (Vector2.Distance(new Vector2(x, y), map.Start) < 6) return true;
            foreach (var s in map.Spots)
                if (s.Kind != "chest" && Vector2.Distance(new Vector2(x, y), s.Use) < (s.Kind == "trail" ? 4 : 3)) return true;
            return false;
        }

        static bool LairReady() => NowMs - TalesSave.Current.LairAt > LairMinutes * 60e3;

        /// <summary>Minutes until the guardian is back (0 when it's ready).</summary>
        public static int LairMinutesLeft() => Mathf.Max(0, Mathf.CeilToInt((float)((LairMinutes * 60e3 - (NowMs - TalesSave.Current.LairAt)) / 60e3)));

        static double NowMs => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int OthersHere()
        {
            int n = 0;
            foreach (var a in world.Actors) if (a != null && !a.IsMe && !a.IsBot) n++;
            return n;
        }

        // ---- tapping a foe ----

        /// <summary>The foe under a screen point (within 0.7 of a tile of its middle), or null.</summary>
        public RoadFoe FoeAt(Vector2 screen)
        {
            RoadFoe hit = null;
            float best = float.MaxValue;
            foreach (var f in foes)
            {
                Vector3 p = cam.Cam.WorldToScreenPoint(f.PointAt(-.5f));
                if (p.z <= 0) continue;
                float tile = (cam.Cam.WorldToScreenPoint(TownMap.ToWorld(f.Pos.x + 1, f.Pos.y)) - cam.Cam.WorldToScreenPoint(TownMap.ToWorld(f.Pos.x, f.Pos.y))).magnitude;
                float d = ((Vector2)p - screen).magnitude;
                if (d < tile * .7f && d < best) { best = d; hit = f; }
            }
            return hit;
        }

        /// <summary>A tap on a foe: it notices you, any grace ends, and your pet walks over to fight. False when no foe was tapped.</summary>
        public bool Tap(Vector2 screen)
        {
            var f = FoeAt(screen);
            if (f == null) return false;
            if (fight != null) return true;
            if (!f.Chasing) { f.State = RoadFoe.Mood.Chase; f.Alert = clock; f.Repath = 0; }
            grace = 0;
            var to = PathFinder.NearestWalkable(map, f.Tile.x, f.Tile.y);
            if (to.HasValue) world.WalkTo(to.Value.x, to.Value.y, null, true);
            return true;
        }

        // ---- fights ----

        void Engage(RoadFoe lead)
        {
            if (fight != null) return;
            var me = world.Me;
            var pack = new List<RoadFoe> { lead };
            if (!lead.Guardian)
                foreach (var o in foes)
                    if (pack.Count < 3 && o != lead && !o.Guardian && o.Chasing && Vector2.Distance(o.Pos, me.Pos) < PackRange) pack.Add(o);
            foreach (var o in pack) { o.Path.Clear(); o.Dir = me.Pos.x > o.Pos.x ? 1 : -1; }
            me.Dir = lead.Pos.x > me.Pos.x ? 1 : -1;
            world.Halt();
            world.ClearHint();
            me.ShowEmote("⚔️");

            Vector2 screen = cam.Cam.WorldToScreenPoint(TownMap.ToWorld(me.Pos.x, me.Pos.y) + TownCamera.Facing * Vector3.up * .6f);
            fight = new Fight { Pack = pack, Screen = screen };
            PlazaInput.Locked = true;
            wipe = FightWipe.Play(screen);
            cam.Shake();
            Sound.Play("whoosh");
            StartCoroutine(Begin(fight));
        }

        IEnumerator Begin(Fight current)
        {
            yield return new WaitForSecondsRealtime(GameSettings.ReduceMotion ? .2f : WipeSeconds);
            PlazaInput.Locked = false;
            if (fight != current) yield break;
            if (!Buddy.Hatched) { world.Notify("Hatch your pet to battle in the wilds"); Finish(current, null); yield break; }
            current.StartedAt = Time.unscaledTime;
            try { BattleScreen.Run(Setup(current.Pack), current.Screen, o => Finish(current, o)); }
            catch (System.Exception e) { Debug.LogException(e); Finish(current, null); yield break; }
            yield return Watch(current);
        }

        // If the battle never opens, or closes without an outcome, the fight ends as the site's 'none'.
        IEnumerator Watch(Fight current)
        {
            float wipeGoesAt = -1;
            while (fight == current)
            {
                float now = Time.unscaledTime;
                if (BattleScreen.Open)
                {
                    current.SawScreen = true;
                    current.ClosedAt = -1;
                    if (wipeGoesAt < 0) wipeGoesAt = now + .4f;
                    if (wipe && now > wipeGoesAt) { wipe.Hide(); wipe = null; }
                }
                else if (!current.SawScreen && now - current.StartedAt > 3) Finish(current, null);
                else if (current.SawScreen && !TalesUi.AnyOpen && !UiStack.Any)
                {
                    if (current.ClosedAt < 0) current.ClosedAt = now;
                    else if (now - current.ClosedAt > 2) Finish(current, null);
                }
                yield return null;
            }
        }

        BattleSetup Setup(List<RoadFoe> pack)
        {
            string name = pack[0].V.Name;
            var (hp, ink) = RoadVitals.Now(false);
            int lvl = 0;
            foreach (var o in pack) lvl = Mathf.Max(lvl, o.Lvl);
            var me = world.Me.Tile;
            var setup = new BattleSetup
            {
                Title = pack.Count == 1 ? name : name + (pack.Count == 2 ? " and a friend" : " and friends"),
                Place = map.AreaAt(me.x, me.y) ?? map.Name,
                Lvl = lvl,
                Mul = tier.Mul * (wild.IsCave ? 1.08 : 1),
                Guardian = pack[0].Guardian,
                HpFrac = hp,
                Ink = Mathf.Max(1, ink), // the site starts every fight with at least one ink
                Tier = tierNumber,
                Cave = wild.IsCave,
                Look = Buddy.Look,
                PetName = MyPets.ActiveName,
            };
            foreach (var o in pack) setup.Foes.Add((o.V, o.Guardian, o.Elite));
            return setup;
        }

        void Finish(Fight current, BattleOutcome outcome)
        {
            if (fight != current) return; // already over
            fight = null;
            PlazaInput.Locked = false;
            if (wipe) { wipe.Hide(); wipe = null; }
            if (outcome == null || outcome.Rounds <= 0) { Abandoned(current.Pack); return; } // the battle never got going
            RoadVitals.After(!outcome.Won, outcome);
            if (outcome.Won) Won(current.Pack);
            else Lost(current.Pack);
        }

        void Won(List<RoadFoe> pack)
        {
            var me = world.Me;
            for (int i = 0; i < pack.Count; i++) { Fx.Sparkle(pack[i].Pos + new Vector2(0, -.4f), i * .12f); Remove(pack[i]); }
            me.Play("happy", .9f, .2f);
            me.ShowEmote("🏆");
            grace = clock + 3.5f;
            if (!pack.Exists(o => o.Guardian)) return;
            var save = TalesSave.Current;
            save.LairAt = NowMs;
            save.ChestReady = true; // kept in the save, so leaving the cave doesn't lose it (the site's chest did)
            save.Touch();
            StartCoroutine(Later(.7f, () =>
            {
                world.Announce("The guardian’s chest is open!", "");
                world.Notify("🧰 The guardian’s chest unlocked. Walk over to open it.");
            }));
        }

        void Lost(List<RoadFoe> pack)
        {
            grace = clock + 7;
            foreach (var o in pack) { o.State = RoadFoe.Mood.Idle; o.Path.Clear(); o.Stun = clock + 3; }
            world.Halt();
            world.Me.ShowEmote("💫");
            // stoneWake: at your home stone, or the stone of the town behind you
            var home = TownBook.Current.Get(TownProgress.WakePlace(map.Key, true, TalesSave.Current.Link));
            string note = $"Your pet fainted and woke up at the {home.Landmark} in {home.Name}.";
            StartCoroutine(Later(.4f, () => Boot.Travel(home.Key, new Vector2Int(home.Stone[0], home.Stone[1]), note)));
        }

        // the fight didn't happen (no pet, or the battle couldn't start): everyone takes a breather
        void Abandoned(List<RoadFoe> pack)
        {
            grace = clock + 6;
            foreach (var o in pack) { o.State = RoadFoe.Mood.Idle; o.Stun = clock + 5; }
        }

        static IEnumerator Later(float seconds, System.Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }

        void OnDestroy()
        {
            if (wipe) wipe.Hide();
            if (fight != null) PlazaInput.Locked = false;
            if (root) Destroy(root.gameObject);
        }
    }
}
