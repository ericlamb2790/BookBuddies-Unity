using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Road
{
    /// <summary>
    /// Foes shared in a hosted world. The room names one game to run them (LocalTown: the host's when they're here,
    /// else whoever came first; PlazaWorld.FoeLead). That game spawns, moves and fights them as usual, but around
    /// everyone in the room, and sends the list a few times a second ("wf" k "s"). The others draw that list and spawn
    /// nothing: each foe is rebuilt from its seed, so its villain, level and look match. When a foe catches anyone, the
    /// leader starts one fight for everyone close by ("fight"): each of them battles that same pack with their own pet,
    /// at the same time. The pack is gone once someone wins ("end"), or rests if everyone lost. Online nothing is
    /// shared: FoeLead is null and the road works as it always has.
    /// </summary>
    public sealed partial class RoadFoes
    {
        const float ShareEvery = .2f;   // seconds between the leader's lists
        const float JoinRange = 7;      // tiles: everyone this close to the one caught joins the fight
        const float FightTimeout = 240; // seconds before a shared fight nobody reported back on is let go
        const float Catchup = 12;       // how quickly a drawn foe closes on where the leader has it

        /// <summary>A fight the leader started for several players: its pack, who's in it, and who's reported back.</summary>
        sealed class Together { public List<int> Pack = new List<int>(), Who = new List<int>(); public int Ended; public bool Won; public float At; }

        readonly HashSet<int> beaten = new HashSet<int>();  // foes beaten here: kept away until the leader's list drops them
        readonly Dictionary<int, Together> together = new Dictionary<int, Together>(); // the leader's shared fights
        readonly List<object> pets = new List<object>();     // scratch for "who" lists
        int lastFight;
        float shareAt;

        /// <summary>The room shares its foes (a hosted world): one game runs them for everyone.</summary>
        bool Sharing => world.FoeLead != null;
        /// <summary>This game runs the foes here: online always, in a hosted world when the room says so.</summary>
        bool Leading => !Sharing || (world.Me != null && world.FoeLead == world.Me.Id);

        // ---- the leader ----

        // the pets foes go after: yours, and with foes shared everyone's in the room
        IEnumerable<PetActor> Players(PetActor me)
        {
            yield return me;
            if (!Sharing) yield break;
            foreach (var a in world.Actors)
                if (a != null && a != me && !a.IsBot && !a.Gone && !a.Hidden) yield return a;
        }

        PetActor SomePlayer(PetActor me)
        {
            var all = new List<PetActor>(Players(me));
            return all[rng.Range(all.Count)];
        }

        PetActor Nearest(RoadFoe f, PetActor me)
        {
            PetActor best = me;
            float d = Vector2.Distance(me.Pos, f.Pos);
            foreach (var a in Players(me))
            {
                float e = Vector2.Distance(a.Pos, f.Pos);
                if (e < d) { d = e; best = a; }
            }
            return best;
        }

        bool InFight(string id)
        {
            if (!int.TryParse(id, out int n)) return false;
            foreach (var t in together.Values) if (t.Who.Contains(n)) return true;
            return false;
        }

        // a foe caught someone: the pack and everyone close by go into one fight together
        void FightTogether(RoadFoe lead, PetActor caught)
        {
            var me = world.Me;
            var pack = PackOf(lead, caught.Pos);
            var t = new Together { At = Time.unscaledTime };
            var ids = new List<object>();
            foreach (var o in pack) { o.Fighting = true; o.State = RoadFoe.Mood.Idle; o.Path.Clear(); t.Pack.Add(o.Id); ids.Add((double)o.Id); }
            pets.Clear();
            foreach (var a in Players(me))
                if (int.TryParse(a.Id, out int n) && Vector2.Distance(a.Pos, caught.Pos) < JoinRange && (a == me ? fight == null && !Busy : !InFight(a.Id)))
                {
                    t.Who.Add(n);
                    pets.Add((double)n);
                }
            int id = ++lastFight;
            together[id] = t;
            ShareNow(true);
            world.ShareFoes(new Dictionary<string, object> { ["t"] = "wf", ["k"] = "fight", ["f"] = (double)id, ["pack"] = ids, ["who"] = new List<object>(pets) });
            if (int.TryParse(me.Id, out int mine) && t.Who.Contains(mine)) Engage(pack, id);
        }

        // someone in a shared fight is done (you, or a message from them): a win clears the pack for everyone
        void Ended(int id, bool won)
        {
            if (!Leading)
            {
                world.ShareFoes(new Dictionary<string, object> { ["t"] = "wf", ["k"] = "end", ["f"] = (double)id, ["won"] = won ? 1.0 : 0 });
                return;
            }
            if (!together.TryGetValue(id, out var t)) return;
            t.Ended++;
            if (won && !t.Won)
            {
                t.Won = true;
                foreach (int f in t.Pack)
                {
                    beaten.Add(f);
                    var o = foes.Find(x => x.Id == f);
                    if (o == null || fight != null && fight.Pack.Contains(o)) continue; // still in your own fight: gone when it ends
                    Fx.Sparkle(o.Pos + new Vector2(0, -.4f), 0);
                    Remove(o);
                }
            }
            if (t.Ended >= t.Who.Count) Release(id);
        }

        // a shared fight is over for everyone: whatever's left of its pack rests a moment, then roams again
        void Release(int id)
        {
            if (!together.TryGetValue(id, out var t)) return;
            together.Remove(id);
            foreach (int f in t.Pack)
            {
                var o = foes.Find(x => x.Id == f);
                if (o == null || fight != null && fight.Pack.Contains(o)) continue;
                o.Fighting = false; o.State = RoadFoe.Mood.Idle; o.Stun = clock + 3;
            }
        }

        // the list, a few times a second (or now): [id, seed, home x, home y, flags, x·100, y·100, mood, facing]
        void ShareNow(bool now)
        {
            foreach (var kv in new List<KeyValuePair<int, Together>>(together))
                if (Time.unscaledTime - kv.Value.At > FightTimeout) Release(kv.Key);
            if (!now && Time.unscaledTime < shareAt) return;
            shareAt = Time.unscaledTime + ShareEvery;
            var list = new List<object>();
            foreach (var f in foes)
                list.Add(new List<object>
                {
                    (double)f.Id, (double)f.Seed, (double)f.Home.x, (double)f.Home.y,
                    (double)((f.Guardian ? 1 : 0) | (f.Ambusher ? 2 : 0) | (f.Fighting ? 4 : 0) | (f.Stun > clock ? 8 : 0)),
                    (double)Mathf.RoundToInt(f.Pos.x * 100), (double)Mathf.RoundToInt(f.Pos.y * 100), (double)(int)f.State, (double)f.Dir,
                });
            world.ShareFoes(new Dictionary<string, object> { ["t"] = "wf", ["k"] = "s", ["tn"] = (double)tierNumber, ["lv"] = (double)level, ["f"] = list });
        }

        // ---- everyone else ----

        /// <summary>A "wf" message from someone else in the room (PlazaWorld passes them on).</summary>
        public void Hear(PetActor from, Dictionary<string, object> m)
        {
            switch (m.Str("k"))
            {
                case "s": if (!Leading && from.Id == world.FoeLead) Take(m); break;
                case "fight": if (from.Id == world.FoeLead) Join(m); break;
                case "end": if (Leading) Ended(m.Int("f"), m.Truthy("won")); break;
            }
        }

        // the leader's list: new foes are built from their seeds, the rest move toward where the leader has them
        void Take(Dictionary<string, object> m)
        {
            int tn = m.Int("tn"), lv = m.Int("lv");
            var data = TalesData.Current;
            if (tn >= 1 && tn <= data.Tiers.Count && tn != tierNumber) { tierNumber = tn; tier = data.Tiers[tn - 1]; }
            if (lv > 0) level = lv;
            var seen = new HashSet<int>();
            foreach (var o in m.Arr("f"))
            {
                if (!(o is List<object> a) || a.Count < 9) continue;
                int id = I(a, 0), flags = I(a, 4);
                seen.Add(id);
                if (beaten.Contains(id)) continue;
                var f = foes.Find(x => x.Id == id);
                var at = new Vector2(I(a, 5) / 100f, I(a, 6) / 100f);
                if (f == null) { f = Make(I(a, 2), I(a, 3), (flags & 1) != 0, (flags & 2) != 0, I(a, 1), id); f.Pos = at; }
                var mood = (RoadFoe.Mood)Mathf.Clamp(I(a, 7), 0, 2);
                if (mood == RoadFoe.Mood.Chase && !f.Chasing) f.Alert = clock;
                f.State = mood;
                f.Seen = at;
                f.Dir = I(a, 8) < 0 ? -1 : 1;
                f.Fighting = (flags & 4) != 0;
                f.Stun = (flags & 8) != 0 ? clock + ShareEvery * 2 : 0;
            }
            foreach (var f in foes.ToArray()) if (!seen.Contains(f.Id) && (fight == null || !fight.Pack.Contains(f))) Remove(f);
            beaten.RemoveWhere(id => !seen.Contains(id));
        }

        // the leader caught you or someone near you: into the fight with that pack
        void Join(Dictionary<string, object> m)
        {
            var me = world.Me;
            if (me == null || !int.TryParse(me.Id, out int mine)) return;
            bool mineToo = false;
            foreach (var o in m.Arr("who")) if (o is double d && (int)d == mine) mineToo = true;
            if (!mineToo) return;
            if (fight != null || Busy) { Ended(m.Int("f"), false); return; } // can't come right now: counted out
            var pack = new List<RoadFoe>();
            foreach (var o in m.Arr("pack"))
                if (o is double d) { var f = foes.Find(x => x.Id == (int)d); if (f != null) pack.Add(f); }
            if (pack.Count == 0) { Ended(m.Int("f"), false); return; } // nothing here to fight: count you out
            Engage(pack, m.Int("f"));
        }

        // drawing the leader's foes between lists, and the grass rustling under everyone
        void Follow(PetActor me, float dt)
        {
            if (!Frozen) clock += dt;
            float k = 1 - Mathf.Exp(-Catchup * dt);
            foreach (var f in foes)
            {
                if (f.Seen == Vector2.zero) continue;
                var to = f.Seen - f.Pos;
                if (Mathf.Abs(to.x) > .05f) f.Dir = to.x > 0 ? 1 : -1;
                f.Pos = to.magnitude > 4 ? f.Seen : f.Pos + to * k;
            }
            Grass(me, true);
        }

        static int I(List<object> a, int i) => a[i] is double d ? (int)d : 0;
    }
}
