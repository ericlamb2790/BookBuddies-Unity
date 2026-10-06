using BookBuddies.Live;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Road
{
    /// <summary>
    /// What hangs in the air over the wilds (the site's wildOverlay), drawn flat over the world and under the
    /// labels: the tier's weather on the road (snow, embers, fireflies or falling leaves), in the caves
    /// darkness that closes in around your pet, lit by lanterns and crystals, and in the foggy towns
    /// (Foghollow, Shadowmark, Ravenmoor) a pale mist along the top of the screen.
    /// The site adds the light glows; here they're blended softly over the dark, which looks much the same.
    /// </summary>
    public sealed class RoadSky : MonoBehaviour
    {
        PlazaWorld world;
        Canvas canvas;
        Darkness dark;
        Weather weather;
        Image[] lights = new Image[0];

        const float FogShare = .22f; // of the screen's height, from the top
        static readonly Color Fog = new Color32(235, 238, 242, 191); // rgba(235,238,242,.75)

        public static RoadSky Create(PlazaWorld world)
        {
            var canvas = UiKit.MakeCanvas("Road sky", 5);
            Destroy(canvas.GetComponent<GraphicRaycaster>()); // never catches taps
            var sky = canvas.gameObject.AddComponent<RoadSky>();
            sky.world = world;
            sky.canvas = canvas;
            var root = (RectTransform)canvas.transform;
            var wild = world.Map.Wild;
            if (wild == null)
            {
                var mist = UiKit.Node("fog", root).Painted<Haze>();
                mist.rectTransform.anchorMin = new Vector2(0, 1 - FogShare);
                mist.rectTransform.anchorMax = Vector2.one;
                mist.rectTransform.offsetMin = mist.rectTransform.offsetMax = Vector2.zero;
            }
            else if (wild.IsCave)
            {
                sky.dark = UiKit.Node("darkness", root).Fill().Painted<Darkness>();
                sky.lights = new Image[wild.Lights.Count];
                for (int i = 0; i < sky.lights.Length; i++)
                {
                    sky.lights[i] = UiKit.Cover(root, "light", wild.Lights[i].Color.WithAlpha(.33f), UiKit.Glow);
                    sky.lights[i].raycastTarget = false;
                }
            }
            else if (!string.IsNullOrEmpty(wild.Weather))
            {
                sky.weather = UiKit.Node("weather", root).Fill().Painted<Weather>();
                sky.weather.Kind = wild.Weather;
            }
            return sky;
        }

        void LateUpdate()
        {
            var me = world ? world.Me : null;
            var cam = Camera.main;
            if (me == null || cam == null) return;
            float k = canvas.scaleFactor;
            if (weather) weather.gameObject.SetActive(GameSettings.AmbientLife && !GameSettings.ReduceMotion);
            if (!dark) return;

            // pixels per tile around your pet sets the size of the clear circle
            Vector2 at = cam.WorldToScreenPoint(TownMap.ToWorld(me.Shown.x, me.Shown.y - .5f));
            float tile = (cam.WorldToScreenPoint(TownMap.ToWorld(me.Shown.x + 1, me.Shown.y)) - cam.WorldToScreenPoint(TownMap.ToWorld(me.Shown.x, me.Shown.y))).magnitude;
            dark.Centre = at / k;
            dark.Tile = tile / k;
            dark.SetVerticesDirty();

            var wild = world.Map.Wild;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = wild.Lights[i];
                Vector3 p = cam.WorldToScreenPoint(TownMap.ToWorld(l.At.x, l.At.y));
                float flicker = .85f + Mathf.Sin(Time.time * 1000 / 180 + l.At.x * 3) * .08f;
                float r = tile * l.Radius * flicker / k;
                var rt = lights[i].rectTransform;
                lights[i].enabled = p.z > 0;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = (Vector2)p / k;
                rt.sizeDelta = new Vector2(r * 2, r * 2);
            }
        }

        /// <summary>The cave's darkness: clear within 1.6 tiles of the centre, deepening to 86% at 6.5 tiles and beyond.</summary>
        sealed class Darkness : MaskableGraphic
        {
            const int Segments = 72;
            static readonly Color Night = new Color32(10, 6, 18, 219); // rgba(10,6,18,.86)

            public Vector2 Centre; // canvas units from the bottom-left
            public float Tile = 64; // canvas units per tile

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                var rect = rectTransform.rect;
                var c = new Vector2(rect.xMin, rect.yMin) + Centre;
                float inner = Tile * 1.6f, outer = Tile * 6.5f, far = outer + rect.size.magnitude * 2;
                Ring(vh, c, inner, outer, Night.WithAlpha(0), Night);
                Ring(vh, c, outer, far, Night, Night);
            }

            static void Ring(VertexHelper vh, Vector2 c, float r0, float r1, Color in0, Color in1)
            {
                int start = vh.currentVertCount;
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i * Mathf.PI * 2 / Segments;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vh.AddVert(c + dir * r0, in0, Vector2.zero);
                    vh.AddVert(c + dir * r1, in1, Vector2.zero);
                }
                for (int i = 0; i < Segments; i++)
                {
                    int v = start + i * 2;
                    vh.AddTriangle(v, v + 1, v + 3);
                    vh.AddTriangle(v, v + 3, v + 2);
                }
            }
        }

        /// <summary>The foggy towns' top haze: the fog colour at the top edge, fading to nothing below.</summary>
        sealed class Haze : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                var r = rectTransform.rect;
                var clear = Fog.WithAlpha(0);
                vh.AddVert(new Vector2(r.xMin, r.yMin), clear, Vector2.zero);
                vh.AddVert(new Vector2(r.xMin, r.yMax), Fog, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMax), Fog, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMin), clear, Vector2.zero);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
        }

        /// <summary>The site's wildWeather in screen space: drifting snow, rising embers, blinking fireflies or tumbling leaves.</summary>
        sealed class Weather : MaskableGraphic
        {
            public string Kind;
            static readonly Color[] LeafColours = { new Color32(214, 120, 48, 191), new Color32(196, 72, 40, 179), new Color32(232, 170, 60, 191) };

            public override Texture mainTexture => Art.Disc.texture;

            void Update() => SetVerticesDirty();

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                var rect = rectTransform.rect;
                float w = rect.width, h = rect.height, s = Time.time;
                int n = Kind == "fly" ? 18 : Kind == "snow" ? 46 : Kind == "ember" ? 28 : 16;
                for (int i = 0; i < n; i++)
                {
                    float a = (float)JsMath.Hsh(i, 7), b = (float)JsMath.Hsh(3, i), size = .6f + (float)JsMath.Hsh(i, i) * .8f;
                    float x, y, r, turn = 0;
                    Color col;
                    switch (Kind)
                    {
                        case "snow":
                            y = Mod(b * h + s * (30 + a * 30), h + 20) - 10; x = Mod(a * w + Mathf.Sin(s * .8f + i) * 14, w);
                            r = size * 2.2f; col = new Color(1, 1, 1, .85f);
                            break;
                        case "ember":
                            y = h - Mod(b * h + s * (22 + a * 26), h + 20); x = Mod(a * w + Mathf.Sin(s * 1.3f + i) * 18, w);
                            r = size * 1.8f; col = new Color(1, (140 + i % 4 * 20) / 255f, 60 / 255f, .45f + .4f * Mathf.Abs(Mathf.Sin(s * 3 + i)));
                            break;
                        case "fly":
                            x = Mod(a * w + Mathf.Sin(s * .5f + i * 2) * 40, w); y = Mod(b * h + Mathf.Cos(s * .4f + i) * 30, h);
                            r = size * 2.6f; col = new Color(1, 240 / 255f, 140 / 255f, Mathf.Max(0, Mathf.Sin(s * 1.7f + i * 1.3f)) * .8f);
                            break;
                        default: // leaves: little ellipses that turn as they fall
                            y = Mod(b * h + s * (18 + a * 14), h + 20) - 10; x = Mod(a * w + Mathf.Sin(s * .9f + i) * 30, w);
                            r = size * 2.6f; col = LeafColours[i % 3]; turn = s * 2 + i;
                            break;
                    }
                    // the site measures y down from the top
                    Dot(vh, new Vector2(rect.xMin + x, rect.yMax - y), Kind == "leaf" ? new Vector2(r * 1.4f, r * .7f) : new Vector2(r, r), -turn, col);
                }
            }

            static float Mod(float v, float m) => ((v % m) + m) % m;

            static void Dot(VertexHelper vh, Vector2 c, Vector2 radius, float turn, Color col)
            {
                int i = vh.currentVertCount;
                float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                Vector2 Corner(float u, float v) { var p = new Vector2(u * radius.x, v * radius.y); return c + new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos); }
                vh.AddVert(Corner(-1, -1), col, new Vector2(0, 0));
                vh.AddVert(Corner(-1, 1), col, new Vector2(0, 1));
                vh.AddVert(Corner(1, 1), col, new Vector2(1, 1));
                vh.AddVert(Corner(1, -1), col, new Vector2(1, 0));
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }
}
