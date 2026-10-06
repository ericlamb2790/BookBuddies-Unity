using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Little lives on the road and in the caves (the site's lifeStep): rabbits that dash away, birds that hop and
    /// fly off when you get close, butterflies and bats that flutter, ducks paddling on the ponds.
    /// Which critters live where comes from the map's Wild block.
    /// </summary>
    public sealed class Critters : MonoBehaviour
    {
        const float Comeback = 4f; // seconds before a bird that flew off is replaced, somewhere out of sight

        sealed class Critter
        {
            public WildInfo.Critter Kind;
            public Vector2 At, Home, To;
            public float Phase, Up, NextAt, Dir;
            public int State; // 0 idle, 1 running away, 2 flying off
            public SpriteRenderer Body, Shadow;
        }

        readonly List<Critter> all = new List<Critter>();
        TownMap map;
        Vector2? pet;
        float comebackAt;

        /// <summary>Where your pet is this frame, so critters can scatter (null when nobody's walking about).</summary>
        public void PetAt(Vector2? where) => pet = where;

        public void Build(TownMap town)
        {
            map = town;
            var centre = new Vector2(map.Start.x, map.Start.y);
            foreach (var kind in map.Wild.Life)
                for (int i = 0; i < kind.Count; i++)
                    Add(kind, i % 2 == 0 ? centre : (Vector2?)null, false);
        }

        void Add(WildInfo.Critter kind, Vector2? near, bool outOfSight)
        {
            var at = FindSpot(kind.Kind, near);
            if (!at.HasValue || (outOfSight && pet.HasValue && Vector2.Distance(at.Value, pet.Value) < 8)) return;
            var c = new Critter { Kind = kind, At = at.Value, Home = at.Value, To = at.Value, Phase = Random.value * 7 };
            var sprite = Art.Emote(kind.Emoji);
            if (!sprite) return;
            c.Body = Draw.Standing(kind.Emoji, transform, sprite, c.At.x, c.At.y);
            bool water = kind.Kind == 'w';
            c.Shadow = Draw.Blob("shadow", transform, c.At.x, c.At.y, .3f, .1f, water ? new Color(1, 1, 1, .35f) : new Color(.12f, .08f, .16f, .22f), Draw.GroundFxOrder);
            all.Add(c);
        }

        Vector2? FindSpot(char kind, Vector2? near)
        {
            for (int n = 0; n < 80; n++)
            {
                bool close = near.HasValue && n < 50;
                int x = close ? Mathf.FloorToInt(near.Value.x + (Random.value - .5f) * 26) : 1 + Random.Range(0, map.Width - 2);
                int y = close ? Mathf.FloorToInt(near.Value.y + (Random.value - .5f) * 20) : 1 + Random.Range(0, map.Height - 2);
                if (!map.Inside(x, y)) continue;
                if (kind == 'w' ? map.TileAt(x, y) == Tile.Water : map.Walkable(x, y)) return new Vector2(x + .5f, y + .5f);
            }
            return null;
        }

        void Update()
        {
            if (map == null) return;
            bool alive = GameSettings.AmbientLife;
            foreach (var c in all) { c.Body.enabled = alive; c.Shadow.enabled = alive; }
            if (!alive) return;
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, .1f);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (Step(c, t, dt)) { Render(c, t); continue; }
                Destroy(c.Body.gameObject); Destroy(c.Shadow.gameObject);
                all.RemoveAt(i);
                comebackAt = t + Comeback;
            }
            if (comebackAt > 0 && t > comebackAt) ReplaceBird();
        }

        // false once the critter has flown away for good
        bool Step(Critter c, float t, float dt)
        {
            float d = pet.HasValue ? Vector2.Distance(pet.Value, c.At) : 99;
            Vector2 away = pet.HasValue && d > 0 ? (c.At - pet.Value) / d : Vector2.right;
            switch (c.Kind.Kind)
            {
                case 'a': // flutters around home and drifts away from you
                    c.At = c.Home + new Vector2(Mathf.Sin(t * .7f + c.Phase) * 1.6f, Mathf.Sin(t * 1.1f + c.Phase * 2) * 1.1f);
                    if (d < 1.4f) c.Home += away * dt * 3 * d;
                    return true;
                case 'w':
                    if (t > c.NextAt)
                    {
                        c.NextAt = t + 3 + Random.value * 4;
                        var to = c.At + new Vector2((Random.value - .5f) * 4, (Random.value - .5f) * 2);
                        if (map.TileAt(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y)) == Tile.Water) c.To = to;
                    }
                    break;
                default:
                    if (c.State == 2)
                    {
                        c.Up += dt * 3.2f;
                        c.At += new Vector2(c.Dir * dt * 2.2f, -dt * .6f);
                        return c.Up <= 6;
                    }
                    if (c.State == 0 && d < (c.Kind.Kind == 'g' ? 1.9f : 2.3f))
                    {
                        if (c.Kind.Kind == 'g') { c.State = 2; c.Dir = away.x >= 0 ? 1 : -1; return true; }
                        var run = c.At + away * 4;
                        if (map.Walkable(Mathf.FloorToInt(run.x), Mathf.FloorToInt(run.y))) { c.To = run; c.State = 1; }
                    }
                    else if (t > c.NextAt)
                    {
                        c.NextAt = t + 1.5f + Random.value * 4;
                        c.State = 0;
                        bool hopper = c.Kind.Kind == 'g';
                        var to = c.At + new Vector2((Random.value - .5f) * (hopper ? 1.6f : 3.5f), (Random.value - .5f) * (hopper ? 1.2f : 2.5f));
                        if (map.Walkable(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y))) c.To = to;
                    }
                    break;
            }
            float speed = c.Kind.Kind == 'w' ? .5f : c.State == 1 ? 4.2f : c.Kind.Kind == 'g' ? 1.6f : 1.1f;
            c.At = Vector2.MoveTowards(c.At, c.To, speed * dt);
            return true;
        }

        void Render(Critter c, float t)
        {
            char k = c.Kind.Kind;
            bool air = k == 'a' || c.State == 2, moving = c.State == 1 || (c.To - c.At).sqrMagnitude > .0025f;
            float hop = air ? 0 : k == 'w' ? Mathf.Sin(t * 2 + c.Phase) * .03f
                : moving ? Mathf.Abs(Mathf.Sin(t * (c.State == 1 ? 16 : 9) + c.Phase)) * .14f
                : k == 'g' && Mathf.Sin(t * 1.3f + c.Phase) > .85f ? Mathf.Abs(Mathf.Sin(t * 12)) * .08f : 0;
            float lift = air ? (k == 'a' ? 1.1f + Mathf.Sin(t * 3 + c.Phase) * .12f : .3f + c.Up) : 0;
            float size = k == 'a' ? .5f : k == 'w' ? .66f : .6f;
            float flap = k == 'a' ? .75f + Mathf.Abs(Mathf.Sin(t * 14 + c.Phase)) * .35f : 1;

            var body = c.Body.transform;
            body.position = TownMap.ToWorld(c.At.x, c.At.y) + body.up * (size * .45f + hop + lift);
            body.localScale = new Vector3(size * flap, size, 1);
            c.Body.sortingOrder = Draw.Order(Mathf.Floor(c.At.y) + (air ? 1.9f : 1.003f));
            c.Body.color = new Color(1, 1, 1, c.State == 2 ? Mathf.Clamp01(1.5f - c.Up / 4) : 1);

            float shrink = air ? .6f : 1;
            c.Shadow.transform.position = TownMap.ToWorld(c.At.x, c.At.y + (k == 'w' ? .12f : .14f), .01f);
            c.Shadow.transform.localScale = k == 'w' ? new Vector3(.32f, .09f, 1) : new Vector3(size * .55f * shrink, size * .18f * shrink, 1);
            c.Shadow.color = k == 'w' ? new Color(1, 1, 1, .35f) : new Color(.12f, .08f, .16f, air ? .12f : .22f);
        }

        // anything that flew off comes back somewhere you can't see
        void ReplaceBird()
        {
            comebackAt = 0;
            var birds = map.Wild.Life.FindAll(k => k.Kind == 'g');
            if (birds.Count == 0) return;
            int before = all.Count;
            Add(birds[Random.Range(0, birds.Count)], null, true);
            if (all.Count == before) comebackAt = Time.time + 3;
        }
    }
}
