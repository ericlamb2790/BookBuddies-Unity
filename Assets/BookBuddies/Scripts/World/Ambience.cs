using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Life around town: glints on the water, sparkles in the prism pool, butterflies (ported from the site)
    /// and a few petals drifting through the air near the camera (new in Unity).
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        const int ButterflyCount = 9;
        const int PetalCount = 22;
        static readonly Color[] ButterflyColours = { Palette.Hex("#ffd34d"), Palette.Hex("#ff8ab4"), Palette.Hex("#b18cff"), Palette.Hex("#7fd0ff"), Color.white };
        static readonly Color[] PetalColours = { Palette.Hex("#ffc6dd"), Palette.Hex("#fff1f6"), Palette.Hex("#ffe08a") };

        struct Glint { public SpriteRenderer r; public float seed; public bool prism; }
        sealed class Butterfly { public Transform t; public Transform wings; public Vector2 at, to; public float phase; }
        sealed class Petal { public Transform t; public Vector3 drift; public float spin; }

        readonly List<Glint> glints = new List<Glint>();
        readonly List<Butterfly> butterflies = new List<Butterfly>();
        readonly List<Petal> petals = new List<Petal>();
        TownMap map;
        Sprite wingSprite;

        public void Build(TownMap town)
        {
            map = town;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = map.TileAt(x, y);
                    float h = Draw.Hash(x, y);
                    if (tile == Tile.Water && h < .4f) AddGlint(x + h * .6f + .15f, y + .3f + h * .4f, h, false);
                    else if (tile == Tile.Prism && h < .7f) AddGlint(x + Draw.Hash(x, y + 1), y + Draw.Hash(x + 2, y), Draw.Hash(y, x), true);
                }

            wingSprite = MakeWing();
            for (int i = 0; i < ButterflyCount; i++)
            {
                var root = new GameObject("butterfly").transform;
                root.SetParent(transform, false);
                root.rotation = TownCamera.Facing;
                var wings = Draw.Sprite("wings", root, wingSprite, 0, ButterflyColours[i % ButterflyColours.Length]).transform;
                wings.localScale = new Vector3(.2f, .14f, 1);
                butterflies.Add(new Butterfly { t = root, wings = wings, at = new Vector2(6 + Random.value * (map.Width - 12), 4 + Random.value * (map.Height - 16)), phase = Random.value * 7 });
            }

            for (int i = 0; i < PetalCount; i++)
            {
                var r = Draw.Sprite("petal", transform, Art.SoftDot, 30000, PetalColours[i % PetalColours.Length].WithAlpha(.85f));
                r.transform.localScale = new Vector3(.07f, .045f, 1);
                petals.Add(new Petal { t = r.transform, drift = new Vector3(.35f + Random.value * .4f, -.12f - Random.value * .2f, -.1f), spin = Random.Range(-90f, 90f) });
            }
        }

        void AddGlint(float x, float y, float seed, bool prism)
        {
            var r = Draw.Sprite(prism ? "sparkle" : "glint", transform, prism ? Art.SoftDot : Art.White, Draw.GroundFxOrder, new Color(1, 1, 1, 0));
            r.transform.SetPositionAndRotation(TownMap.ToWorld(x, y, .015f), Quaternion.Euler(90, 0, 0));
            r.transform.localScale = prism ? Vector3.one * .07f : new Vector3(.15f, .025f, 1);
            glints.Add(new Glint { r = r, seed = seed, prism = prism });
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            bool alive = GameSettings.AmbientLife;
            if (butterflies.Count > 0 && butterflies[0].t.gameObject.activeSelf != alive)
            {
                foreach (var b in butterflies) b.t.gameObject.SetActive(alive);
                foreach (var p in petals) p.t.gameObject.SetActive(alive);
            }

            foreach (var g in glints)
            {
                float p = Mathf.Repeat(t / (g.prism ? 1.8f : 2.4f) + g.seed * 5, 1);
                float a = Mathf.Sin(p * Mathf.PI);
                g.r.color = new Color(1, 1, 1, g.prism ? .9f * a : (a > .12f ? .55f * a : 0));
                if (g.prism) g.r.transform.localScale = Vector3.one * (.02f + .06f * a);
            }

            if (!alive) return; // butterflies and petals are off in Settings

            foreach (var b in butterflies)
            {
                if (b.to == Vector2.zero || Vector2.Distance(b.at, b.to) < .3f)
                    b.to = new Vector2(Mathf.Clamp(b.at.x + (Random.value - .5f) * 8, 3, map.Width - 4), Mathf.Clamp(b.at.y + (Random.value - .5f) * 6, 3, map.Height - 8));
                var dir = (b.to - b.at).normalized;
                b.at += dir * dt * .9f + new Vector2(Mathf.Sin(t / .3f + b.phase), Mathf.Cos(t / .25f + b.phase)) * dt * .4f;
                float lift = .9f + Mathf.Sin(t / .5f + b.phase) * .15f;
                b.t.position = TownMap.ToWorld(b.at.x, b.at.y) + b.t.up * lift;
                b.wings.localScale = new Vector3(.2f * (.15f + Mathf.Abs(Mathf.Sin(t / .09f + b.phase))), .14f, 1);
                b.wings.GetComponent<SpriteRenderer>().sortingOrder = Draw.Order(b.at.y + .2f);
            }

            var cam = Camera.main;
            if (!cam) return;
            foreach (var p in petals)
            {
                Vector3 local = cam.transform.InverseTransformPoint(p.t.position);
                if (local.z < 2 || local.z > 9 || Mathf.Abs(local.x) > 5 || Mathf.Abs(local.y) > 4)
                    local = new Vector3(Random.Range(-5f, -2f), Random.Range(-3f, 4f), Random.Range(3f, 8f));
                local += p.drift * dt + new Vector3(0, Mathf.Sin(t + local.x) * .1f * dt, 0);
                p.t.position = cam.transform.TransformPoint(local);
                p.t.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, t * p.spin);
            }
        }

        static Sprite MakeWing()
        {
            const int w = 64, h = 40;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1;
                    float left = Ellipse(u + .45f, v, .5f, .75f, -.4f), right = Ellipse(u - .45f, v, .5f, .75f, .4f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(left, right)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), w / 2f, 0, SpriteMeshType.FullRect);
        }

        static float Ellipse(float x, float y, float rx, float ry, float tilt)
        {
            float c = Mathf.Cos(tilt), s = Mathf.Sin(tilt);
            float a = (x * c + y * s) / rx, b = (-x * s + y * c) / ry;
            return (1 - Mathf.Sqrt(a * a + b * b)) * 8;
        }
    }
}
