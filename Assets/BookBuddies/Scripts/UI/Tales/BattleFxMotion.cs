using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>A move's signature look (the site's sgOf): a shape, two colours, a spin and a style, all from a hash of its name.</summary>
    public readonly struct Sg
    {
        public readonly uint Hash;
        public readonly int Shape, Kind, Spin;
        public readonly Color Color, Color2;

        Sg(uint h, int shape, int kind, int spin, Color c, Color c2) { Hash = h; Shape = shape; Kind = kind; Spin = spin; Color = c; Color2 = c2; }

        /// <summary>sgOf with the class palette for pets (a class reads as one palette, a shade per move).</summary>
        public static Sg Of(BattleEvent e, BattleEngine engine)
        {
            uint h = JsMath.Hash((string.IsNullOrEmpty(e.Name) ? "move" : e.Name) + (e.Foe ? "~" : ""));
            int s = (int)h;
            float hue = e.Kind == "heal" ? 120 + h % 50 : e.Kind == "shield" ? 190 + h % 40 : h % 360;
            Color c = BattleArt.Hsl(hue, 92, 66), c2 = BattleArt.Hsl(hue + 40, 95, 78);
            var cls = engine?.Find(e.Actor)?.Hero?.Cls;
            if (cls != null && e.Kind != "heal" && e.Kind != "shield" && BattleFx.ClassArt.TryGetValue(cls, out var m))
            {
                hue = (m.hue + h % 36 - 18 + 360) % 360;
                c = BattleArt.Hsl(hue, 78, 62);
                c2 = BattleArt.Hsl(hue + 30, 85, 76);
            }
            return new Sg(h, (int)(h % 10), (s >> 6) % 3, (s >> 4) % 2 != 0 ? 1 : -1, c, c2);
        }
    }

    // The site's motion primitives: body moves, the ways a move travels (projectiles, beams, rain, slashes), impacts,
    // runes, signature shapes and each class's particles. Field coordinates; the site's y-down offsets are flipped.
    public sealed partial class BattleFx
    {
        /// <summary>MV_C: the particle glyph, motion and hue each class throws.</summary>
        public static readonly Dictionary<string, (string glyph, string motion, int hue)> ClassArt = new Dictionary<string, (string, string, int)>
        {
            ["knight"] = ("rune", "ring", 44), ["scholar"] = ("page", "swirl", 265), ["trick"] = ("star", "scatter", 300), ["healer"] = ("petal", "rise", 140),
            ["sleuth"] = ("key", "streak", 205), ["rogue"] = ("shard", "streak", 220), ["mage"] = ("star", "swirl", 275), ["paladin"] = ("rune", "ring", 48),
            ["druid"] = ("leaf", "swirl", 110), ["tinker"] = ("gear", "scatter", 30), ["shaman"] = ("bolt", "fall", 190), ["telepath"] = ("moon", "ring", 290),
            ["bard"] = ("note", "rise", 330), ["ranger"] = ("feather", "streak", 95), ["warlock"] = ("flame", "swirl", 285), ["monk"] = ("puff", "ring", 38),
            ["ghost"] = ("puff", "rise", 200), ["dragoon"] = ("flame", "fall", 12), ["summoner"] = ("rune", "swirl", 170), ["chrono"] = ("gear", "ring", 55),
            ["alchemist"] = ("bubble", "rise", 150), ["samurai"] = ("petal", "streak", 345), ["dancer"] = ("petal", "swirl", 320), ["mimic"] = ("key", "scatter", 35),
            ["pirate"] = ("coin", "scatter", 45), ["passenger"] = ("drop", "scatter", 352), ["regency"] = ("fan", "swirl", 325), ["coinshot"] = ("coin", "streak", 48),
            ["burner"] = ("flame", "scatter", 22), ["windrunner"] = ("wind", "streak", 195), ["dunadan"] = ("leaf", "fall", 120),
        };
        static readonly string[] FoeGlyphs = { "shard", "puff", "star", "bolt", "drop" }, FoeMotions = { "scatter", "fall", "swirl" };

        // ---- body moves (on a fighter's Motion, pivoted at the feet) ----

        /// <summary>A step toward the target and back (the default attack).</summary>
        public void Lunge(BattleUnitView a, BattleUnitView b)
        {
            if (a == null || b == null) return;
            var d = (b.Center - a.Center) * .3f;
            KeyTween.Play(a.Motion, T(460), BattleEase.Snap, new Kf(0), new Kf(.25f, -.15f * d.x, -.15f * d.y, 0, .95f), new Kf(.55f, d.x, d.y, 0, 1.12f), new Kf(1));
        }

        /// <summary>A leaning rush most of the way to the target (slashes).</summary>
        public void Dash(BattleUnitView a, BattleUnitView b)
        {
            if (a == null || b == null) return;
            var d = (b.Center - a.Center) * .55f;
            KeyTween.Play(a.Motion, T(420), BattleEase.Snap, new Kf(0), new Kf(.2f, -.08f * d.x, -.08f * d.y, 6, .92f), new Kf(.5f, d.x, d.y, -8, 1.1f), new Kf(1));
        }

        /// <summary>A heavy jump and landing (quakes).</summary>
        public void Hop(BattleUnitView a)
        {
            if (a == null) return;
            KeyTween.Play(a.Motion, T(460), BattleEase.In, new Kf(0), Kf.Squash(.4f, 0, 34, 0, .95f, 1.08f), Kf.Squash(.75f, 0, -4, 0, 1.15f, .85f), new Kf(1));
        }

        /// <summary>A small rise for heals, shields and buffs.</summary>
        public void Lift(BattleUnitView a)
        {
            if (a != null) KeyTween.Play(a.Motion, T(500), BattleEase.Linear, new Kf(0), new Kf(.5f, 0, 14, 0, 1.08f), new Kf(1));
        }

        /// <summary>Too dizzy: a rock side to side.</summary>
        public void Rock(BattleUnitView a)
        {
            if (a != null) KeyTween.Play(a.Motion, T(500), BattleEase.Linear, new Kf(0), new Kf(-1, 0, 0, 12), new Kf(-1, 0, 0, -10), new Kf(1));
        }

        /// <summary>A victory bounce, staggered by delay.</summary>
        public void Bounce(BattleUnitView a, float delay)
        {
            if (a != null) KeyTween.Play(a.Motion, T(450), BattleEase.Linear, delay, false, new Kf(0), new Kf(-1, 0, 18), new Kf(-1), new Kf(-1, 0, 10), new Kf(1));
        }

        /// <summary>A missed fighter sways out of the way.</summary>
        public void Dodge(BattleUnitView v)
        {
            if (v == null) return;
            float side = v.Unit.IsFoe ? 1 : -1;
            KeyTween.Play(v.Motion, T(460), BattleEase.Out, new Kf(0), new Kf(.35f, 16 * side, 0, -8 * side), new Kf(1));
        }

        /// <summary>foeWind: a foe's wind-up before it attacks, in its own movement style. Returns the seconds it takes.</summary>
        public float Wind(BattleUnitView v)
        {
            float d = T(320);
            if (v == null || d < .04f) return 0;
            Kf[] k;
            switch (v.Unit.Foe?.Def.Mv)
            {
                case "float": k = new[] { new Kf(0), new Kf(-1, 0, 22, 8), new Kf(1) }; break;
                case "wobble": k = new[] { new Kf(0), new Kf(-1, 0, 0, 12), new Kf(-1, 0, 0, -10), new Kf(-1, 0, 0, 6), new Kf(1) }; break;
                case "hop": k = new[] { new Kf(0), new Kf(-1, 0, 16), new Kf(-1), new Kf(-1, 0, 10), new Kf(1) }; break;
                case "sway": k = new[] { new Kf(0), new Kf(-1, 10, 0, 6), new Kf(1) }; break;
                case "jitter": k = new[] { new Kf(0), new Kf(-1, -3, -1), new Kf(-1, 3, 1), new Kf(-1, -2, 1), new Kf(-1, 2, -1), new Kf(1) }; break;
                case "pulse": k = new[] { new Kf(0), new Kf(-1, 0, 0, 0, 1.22f), new Kf(1) }; break;
                default: k = new[] { new Kf(0), Kf.Squash(-1, 0, 0, 0, 1.06f, .9f), new Kf(1) }; break;
            }
            KeyTween.Play(v.Motion, d, BattleEase.InOut, k);
            return d;
        }

        /// <summary>A hit lands: the fighter jolts and flashes, a ring spreads (gold and bigger on a crit, which also shakes the screen).</summary>
        public void Impact(BattleUnitView v, bool crit)
        {
            if (v == null) return;
            v.Flash();
            KeyTween.Play(v.Motion, T(380), BattleEase.Linear, new Kf(0), new Kf(.2f, -8, 0, 4), new Kf(.45f, 7, 0, -3), new Kf(.7f, -4), new Kf(1));
            var ring = Picture(field, BattleArt.Ring, crit ? Gold : Color.white, v.Center, new Vector2(90, 90));
            KeyTween.Play(ring, Mathf.Max(.01f, T(420)), BattleEase.Out, 0, true, new Kf(0, 0, 0, 0, .2f), new Kf(1, 0, 0, 0, crit ? 1.7f : 1.1f, 0));
            if (crit) Shake(T(320), new Vector2(-6, -3), new Vector2(5, 4), new Vector2(-3, -2));
        }

        /// <summary>Shakes the whole field through the given offsets and back.</summary>
        public void Shake(float seconds, params Vector2[] steps)
        {
            if (GameSettings.ReduceMotion || seconds < .01f) return;
            var k = new Kf[steps.Length + 2];
            k[0] = new Kf(0);
            for (int i = 0; i < steps.Length; i++) k[i + 1] = new Kf(-1, steps[i].x, steps[i].y);
            k[k.Length - 1] = new Kf(1);
            KeyTween.Play(stage.Shake, seconds, BattleEase.Linear, k);
        }

        // ---- how a move travels (deliver) ----

        /// <summary>Sends a move from a to b by its animation (beam, rain, slash, quake, burst, or an arc projectile). Returns the seconds to wait for it to land.</summary>
        public float Deliver(string anim, BattleUnitView a, BattleUnitView b, string emoji, string projectile)
        {
            if (a == null || b == null) return 0;
            switch (anim)
            {
                case "beam": return Beam(a.Center, b.Center);
                case "rain": return Rain(b, emoji);
                case "slash": return Slash(b);
                case "quake": return Wave(b);
                case "burst": return Burst(b);
                default: return Shot(projectile ?? "lob", a.Center, b.Center, emoji);
            }
        }

        // shot: ten flights, picked per move so every ability keeps its own
        float Shot(string kind, Vector2 a, Vector2 b, string emoji)
        {
            float d = T(kind == "lob" ? 360 : kind == "bolt" || kind == "disc" ? 300 : kind == "boomer" ? 560 : kind == "bounce" ? 520 : 420);
            if (d < .04f) return 0;
            if (kind != "volley") { Flight(kind, a, b, emoji, d, 1, 0, 0); return d; }
            for (int i = 0; i < 3; i++) Flight("lob", a, b, emoji, d * .85f, .7f, (i - 1) * 16, i * T(90));
            return d * .85f + 2 * T(90);
        }

        void Flight(string kind, Vector2 a, Vector2 b, string emoji, float d, float sc, float off, float delay)
        {
            Vector2 dv = b - a + new Vector2(0, -off);
            float len = Mathf.Max(1, dv.magnitude), h = Mathf.Min(90, Mathf.Abs(dv.x) * .3f + 40), ang = Mathf.Atan2(dv.y, dv.x) * Mathf.Rad2Deg;
            var n = new Vector2(-dv.y, dv.x) / len;
            PathMover.PathFn path = t =>
            {
                Vector2 p = a + dv * t;
                float r = 0, s = sc, pi = Mathf.PI;
                switch (kind)
                {
                    case "lob": p.y += h * 4 * t * (1 - t); r = -360 * t; s = sc * (.7f + .5f * Mathf.Sin(t * pi)); break;
                    case "bolt": r = ang; s = sc * 1.1f; break;
                    case "boomer": float u = t < .7f ? t / .7f * 1.15f : 1.15f - (t - .7f) / .3f * .15f; p = a + dv * u + new Vector2(0, Mathf.Sin(t * pi) * 42); r = -1080 * t; break;
                    case "wave": p += n * (Mathf.Sin(t * pi * 3) * 18 * (1 - t * .3f)); r = -Mathf.Sin(t * 12) * 20; break;
                    case "bounce": float g = t < .5f ? t / .5f : (t - .5f) / .5f; p.y += (t < .5f ? h * .9f : h * .42f) * 4 * g * (1 - g); r = -540 * t; break;
                    case "spiral": p += n * (Mathf.Sin(t * pi * 4) * 14) + new Vector2(0, Mathf.Cos(t * pi * 4) * 8); s = sc * (.8f + .3f * Mathf.Cos(t * pi * 4)); r = -720 * t; break;
                    case "comet": p.y += h * 2 * t * (1 - t); s = sc * (1.05f + .5f * t); break;
                    case "disc": r = -1440 * t; s = sc * .95f; break;
                    case "homing": p += n * (Mathf.Sin(t * pi * 2) * 36 * (1 - t)); r = ang + Mathf.Cos(t * pi * 2) * 40; break;
                }
                return (p, r, s, 1);
            };
            var ease = kind == "bolt" || kind == "disc" ? BattleEase.Shot : kind == "boomer" || kind == "wave" || kind == "spiral" ? BattleEase.Linear : BattleEase.In;
            float size = kind == "comet" ? 44 : 32;
            PathMover.Run(Glyph(emoji, size, kind == "comet" ? Gold : Color.white), d, ease, delay, path);
            if (kind != "bolt" && kind != "comet" && kind != "homing") return;
            for (int j = 1; j <= (kind == "comet" ? 4 : 3); j++)
            {
                var trail = Glyph(emoji, size, Color.clear);
                float fade = .55f - j * .12f;
                PathMover.Run(trail, d, ease, delay + j * .03f, t => { var p = path(t); return (p.at, p.turn, p.size, fade); });
            }
        }

        float Beam(Vector2 a, Vector2 b)
        {
            float d = T(320);
            if (d < .04f) return 0;
            var dv = b - a;
            var bar = Picture(field, Art.White, Palette.Hex("#d8f0ff"), a, new Vector2(dv.magnitude, 10));
            bar.preserveAspect = false;
            bar.sprite = UiKit.Rounded(5);
            bar.type = Image.Type.Sliced;
            bar.rectTransform.pivot = new Vector2(0, .5f);
            var glow = Picture(bar.rectTransform, UiKit.Glow, Palette.Hex("#8fd0ff").WithAlpha(.7f), Vector2.zero, Vector2.zero);
            glow.preserveAspect = false;
            Stretch(glow.rectTransform, new Vector2(0, -1.4f), new Vector2(1, 2.4f));
            float ang = Mathf.Atan2(dv.y, dv.x) * Mathf.Rad2Deg;
            KeyTween.Play(bar, d, BattleEase.Out, 0, true, Kf.Squash(0, 0, 0, ang, 0, 1), Kf.Squash(.45f, 0, 0, ang, 1, 1), Kf.Squash(1, 0, 0, ang, 1, 1, 0));
            return d * .5f;
        }

        float Rain(BattleUnitView b, string emoji)
        {
            float d = T(440);
            if (d < .04f) return 0;
            sound.Play("patter");
            for (int i = 0; i < 4; i++)
            {
                float x = b.Center.x + (Random.value - .5f) * b.Width * .8f, y0 = Height + 40 + Random.value * 40;
                Vector2 from = new Vector2(x, y0), to = new Vector2(x, b.Center.y);
                PathMover.Run(Glyph(emoji, 32, Color.white), d, BattleEase.Fall, i * .07f, t => (Vector2.Lerp(from, to, t), Mathf.Lerp(30, -20, t), Mathf.Lerp(.7f, 1, t), Mathf.Lerp(.2f, 1, t)));
            }
            return d + .21f;
        }

        float Slash(BattleUnitView b)
        {
            float d = T(300);
            if (d < .04f) return 0;
            float w = b.Width * 1.3f;
            var arc = Picture(field, BattleArt.Ring, Color.white, b.Center, new Vector2(w, w));
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillOrigin = (int)Image.Origin360.Top;
            arc.fillAmount = .3f;
            var glow = Picture(arc.rectTransform, BattleArt.Ring, Gold.WithAlpha(.5f), new Vector2(w / 2, w / 2), new Vector2(w * 1.08f, w * 1.08f));
            glow.type = Image.Type.Filled;
            glow.fillMethod = Image.FillMethod.Radial360;
            glow.fillOrigin = (int)Image.Origin360.Top;
            glow.fillAmount = .32f;
            glow.transform.SetAsFirstSibling();
            KeyTween.Play(arc, d, BattleEase.Out, 0, true, new Kf(0, 0, 0, 60, .6f, 0), new Kf(.5f, 0, 0, -10, 1), new Kf(1, 0, 0, -40, 1.08f, 0));
            return d;
        }

        float Wave(BattleUnitView b)
        {
            float d = T(480);
            if (d < .04f) return 0;
            var ring = Picture(field, BattleArt.Ring, Palette.Hex("#ffdca0"), new Vector2(b.Center.x, b.Feet.y + 6), new Vector2(60, 20));
            ring.preserveAspect = false;
            KeyTween.Play(ring, d, BattleEase.Out, 0, true, new Kf(0, 0, 0, 0, .3f), new Kf(1, 0, 0, 0, 3.2f, 0));
            return d;
        }

        float Burst(BattleUnitView b)
        {
            float d = T(420);
            if (d < .04f) return 0;
            for (int i = 0; i < 9; i++)
            {
                float ang = i / 9f * Mathf.PI * 2, r = b.Width * .55f + Random.value * 20;
                var bit = Picture(field, BattleArt.Particle("star"), Palette.Hex("#ffe9a8"), b.Center, new Vector2(18, 18));
                KeyTween.Play(bit, d, BattleEase.Soft, 0, true, new Kf(0, 0, 0, 0, .4f), new Kf(1, Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0, 1.2f, 0));
            }
            return d;
        }

        /// <summary>Three glyphs circle a fighter for a buff or shield (the first is the move's emoji).</summary>
        public void Orbit(BattleUnitView b, string emoji)
        {
            float d = T(900);
            if (b == null || d < .04f) return;
            var c = b.Center;
            float r = b.Width * .55f;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var bit = i == 0 ? Glyph(string.IsNullOrEmpty(emoji) ? "✨" : emoji, 22, Color.white) : Particle("star", Gold, 16);
                PathMover.Run(bit, d, BattleEase.Linear, i * .06f, t =>
                {
                    float a = t * Mathf.PI * 2.2f + k * 2.1f;
                    return (c + new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r * .45f + t * 18), 0, .6f + .5f * Mathf.Sin(t * Mathf.PI), t < .92f ? 1 : (1 - t) / .08f);
                });
            }
        }

        /// <summary>A dashed casting circle under the caster (kind heal, shield or buff adds its sparkles). Returns the seconds to wait.</summary>
        public float Rune(BattleUnitView a, string kind = null)
        {
            float d = T(360);
            if (a == null || d < .04f) return 0;
            var tint = kind == "heal" ? Palette.Hex("#7be0a1") : kind == "shield" ? Palette.Hex("#8fd0ff") : kind == "buff" ? Palette.Hex("#ff9ad1") : Gold;
            float w = a.Width * 1.15f, h = w * .34f;
            var rune = Picture(field, Art.DashedRing, tint, new Vector2(a.Center.x, a.Center.y - a.Width * .28f - h / 2), new Vector2(w, h));
            rune.preserveAspect = false;
            KeyTween.Play(rune, d * 1.6f, BattleEase.Out, 0, true, new Kf(0, 0, 0, 0, .3f, 0), new Kf(.45f, 0, 0, -90, 1.05f), new Kf(1, 0, 0, -180, 1.15f, 0));
            if (kind != null) Sparkle(a, BattleArt.Particle(kind == "heal" ? "plus" : kind == "shield" ? "hex" : "chev"), tint);
            return d * .6f;
        }

        /// <summary>Six glyphs float up from a fighter (mint ✦ by default).</summary>
        public void Sparkle(BattleUnitView v, Sprite glyph = null, Color? tint = null)
        {
            if (v == null) return;
            var c = v.Center;
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector2(c.x + (Random.value - .5f) * v.Width * .8f, c.y - v.Width * .2f);
                var bit = Picture(field, glyph ? glyph : BattleArt.Particle("star"), tint ?? Palette.Hex("#8dffa0"), at, new Vector2(20, 20));
                KeyTween.Play(bit, T(900) + Random.value * .2f, BattleEase.Linear, i * .05f, true,
                    new Kf(0, 0, 0, 0, .4f, 0), new Kf(.3f, 0, 20, 0, 1.1f), new Kf(1, (Random.value - .5f) * 20, 60 + Random.value * 30, 0, .6f, 0));
            }
        }

        /// <summary>A beaten foe bursts into pages.</summary>
        public void Poof(BattleUnitView v)
        {
            if (v == null) return;
            for (int i = 0; i < 8; i++)
            {
                float ang = i / 8f * Mathf.PI * 2, r = 50 + Random.value * 30;
                var bit = Glyph("📄", 20, Color.white);
                bit.anchoredPosition = v.Center;
                KeyTween.Play(bit, T(700), BattleEase.Soft, 0, true, new Kf(0, 0, 0, 0, .6f), new Kf(1, Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, -Random.value * 540, 1, 0));
            }
        }

        /// <summary>sgHit: the move's signature shape flares on its target (or on the caster for support moves).</summary>
        public void SgHit(BattleEvent e, BattleUnitView v, bool self)
        {
            float d = T(self ? 620 : 520);
            if (v == null || d < .04f || GameSettings.ReduceMotion) return;
            var s = Sg.Of(e, engine);
            float size = Mathf.Max(60, v.Width * (self ? 1.25f : 1.45f)), r = s.Spin * (s.Kind == 1 ? 160 : 60);
            var shape = Picture(field, BattleArt.Shape(s.Shape), s.Color, v.Center, new Vector2(size, size));
            var glow = Picture(shape.rectTransform, UiKit.Glow, s.Color.WithAlpha(.45f), new Vector2(size / 2, size / 2), new Vector2(size * 1.3f, size * 1.3f));
            glow.transform.SetAsFirstSibling();
            Kf[] k = s.Kind == 0 ? new[] { new Kf(0, 0, 0, 0, .2f, 0), new Kf(.3f, 0, 0, -r * .6f, 1.05f), new Kf(1, 0, 0, -r, 1.35f, 0) }
                : s.Kind == 1 ? new[] { new Kf(0, 0, 0, r, 1.6f, 0), new Kf(.45f, 0, 0, 0, .9f), new Kf(1, 0, 0, -20, .5f, 0) }
                : new[] { new Kf(0, 0, -6, 0, .4f, 0), new Kf(.35f, 0, 0, 0, 1.1f), new Kf(1, 0, self ? 26 : 8, -r * .3f, 1.2f, 0) };
            KeyTween.Play(shape, d, BattleEase.Soft, 0, true, k);
        }

        // ---- class particles (mvArt, mvSelf, mvThrow) ----

        /// <summary>mvArt: the particles a hit, heal, shield or status throws on its target (the attacker's class picks them for pets).</summary>
        public void HitArt(BattleHit f, BattleEvent e)
        {
            var v = stage.View(f.Unit);
            if (f.Miss || v == null) return;
            var actor = engine.Find(e.Actor);
            string cls = actor?.Hero?.Cls;
            uint h = JsMath.Hash((e.Name ?? "") + (cls ?? ""));
            var src = stage.View(e.Actor);
            if ((f.Damage > 0 || f.Absorbed > 0) && f.Dot == null)
            {
                var m = ClassArt.TryGetValue(cls ?? "", out var cm) && actor != null && !actor.IsFoe ? cm : FoeArtOf(h);
                Throw(v, m.glyph, m.motion, m.hue, h, e.Ult ? 9 : 5, e.Ult, false, src);
            }
            if (f.Heal > 0) Throw(v, "plus", "rise", 140, h + 1, 4, false, false, null);
            if (f.ShieldGained > 0) Throw(v, "hex", "ring", 195, h + 2, 5, false, false, null);
            string pops = PopsFor(e, f.Unit);
            if (pops.Length == 0) return;
            if (pops.Contains("💫")) Throw(v, "star", "ring", 50, h + 3, 3, false, true, null);
            if (pops.Contains("🩸")) Throw(v, "drop", "fall", 355, h + 4, 3, false, false, null);
            if (pops.Contains("🧪")) Throw(v, "bubble", "rise", 110, h + 5, 4, false, false, null);
            if (pops.Contains("🔍")) Throw(v, "eye", "ring", 30, h + 6, 1, false, false, null);
            if (pops.Contains("😳")) Throw(v, "fan", "rise", 330, h + 7, 3, false, false, null);
            if (pops.Contains("💃")) Throw(v, "chev", "streak", 300, h + 8, 3, false, false, src);
        }

        /// <summary>mvSelf: marks on the actor from its own callouts (the Passenger, coins and wind, class buffs). Once per event.</summary>
        public void SelfArt(BattleEvent e)
        {
            var v = stage.View(e.Actor);
            string pops = PopsFor(e, e.Actor);
            if (v == null || pops.Length == 0) return;
            uint h = JsMath.Hash(e.Name ?? "");
            if (pops.Contains("🌑")) Throw(v, "puff", "swirl", 275, h, 6, false, true, null);
            if (pops.Contains("🪙") || pops.Contains("🌬️")) Throw(v, pops.Contains("🌬️") ? "wind" : "coin", "rise", pops.Contains("🌬️") ? 195 : 48, h + 1, 5, false, false, null);
            string cls = v.Unit.Hero?.Cls;
            bool buffed = pops.Contains("💪") || pops.Contains("🪞") || pops.Contains("💚") || pops.Contains("🌀") || pops.Contains("📢");
            if (buffed && cls != null && ClassArt.TryGetValue(cls, out var m)) Throw(v, m.glyph, "ring", m.hue, h + 2, 5, false, false, null);
        }

        // a foe's throw: glyph, motion and hue from the hash (a negative JS index falls through to a streak, as on the site)
        static (string glyph, string motion, int hue) FoeArtOf(uint h)
        {
            int k = ((int)h >> 3) % 3;
            return (FoeGlyphs[h % 5], k >= 0 ? FoeMotions[k] : "streak", (int)(h % 360));
        }

        static string PopsFor(BattleEvent e, string key)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in e.Pops) if (p.Key == key) sb.Append(p.Value).Append(' ');
            return sb.ToString();
        }

        // mvThrow: up to 14 class glyphs fly around the target in one of six motions; the hash varies count, size, spin and spread
        void Throw(BattleUnitView v, string glyph, string motion, int hue, uint h, int n, bool big, bool slow, BattleUnitView src)
        {
            float d = T(slow ? 900 : 700);
            if (GameSettings.ReduceMotion || v == null || d < .04f) return;
            int s = (int)h, cnt = Mathf.Min(14, n + (int)(h % 3));
            float w = v.Width, R = w * (.55f + (s >> 3) % 4 * .08f) * (big ? 1.5f : 1);
            float size = Mathf.Round(Mathf.Max(11, Mathf.Min(26, w * .17f)) * (1 + (s >> 5) % 3 * .12f) * (big ? 1.25f : 1)) * 1.2f;
            float spin = ((s >> 7) % 2 != 0 ? 1 : -1) * (120 + (s >> 9) % 4 * 60);
            Vector2 from = motion == "streak" && src != null ? Flip(src.Center - v.Center) : new Vector2(-w * 2, 0); // site coordinates (y down)
            for (int i = 0; i < cnt; i++)
            {
                float t = i / (float)cnt, ang = t * Mathf.PI * 2 + h % 7, j = ((s >> (i % 9)) & 7) / 7f;
                var keys = ThrowKeys(motion, t, ang, j, w, R, spin, from);
                var bit = Particle(glyph, BattleArt.Hsl(hue + ((s >> (i % 11)) & 15) - 8, 90, 68), size);
                bit.anchoredPosition = v.Center;
                float delay = i * T(motion == "streak" ? 22 : 14);
                KeyTween.Play(bit, d * (.85f + j * .3f), BattleEase.Spray, delay, true, keys);
            }
        }

        // the site's keyframes for one particle, written in its y-down terms and flipped into the field's
        static Kf[] ThrowKeys(string motion, float t, float ang, float j, float w, float R, float spin, Vector2 from)
        {
            float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
            Kf K(float at, float x, float y, float s, float r, float a) => new Kf(at, x, -y, -r, s, a);
            switch (motion)
            {
                case "scatter":
                    float r0 = R * (.7f + j * .6f);
                    return new[] { K(0, 0, 0, .3f, 0, 0), K(.3f, cos * r0 * .5f, sin * r0 * .5f, 1, spin * .4f, 1), K(1, cos * r0, sin * r0 + 12, .6f, spin, 0) };
                case "swirl":
                    var k = new Kf[9];
                    for (int s = 0; s <= 8; s++)
                    {
                        float q = s / 8f, rr = R * (1.1f - q * .95f), aa = ang + q * Mathf.PI * 1.6f * (spin > 0 ? 1 : -1);
                        k[s] = K(q, Mathf.Cos(aa) * rr, Mathf.Sin(aa) * rr * .7f, .5f + q * .7f, q * spin, s == 0 || s == 8 ? 0 : 1);
                    }
                    return k;
                case "rise":
                    float xr = (t - .5f) * w * .9f;
                    return new[] { K(0, xr, w * .35f, .4f, 0, 0), K(.35f, xr + (j - .5f) * 14, 0, 1, spin * .3f, 1), K(1, xr + (j - .5f) * 26, -w * .7f, .7f, spin * .6f, 0) };
                case "fall":
                    float xf = (t - .5f) * w * 1.1f;
                    return new[] { K(0, xf, -w * .9f - j * 20, .8f, -30, 0), K(.45f, xf, -w * .3f, 1, 0, 1), K(1, xf + (j - .5f) * 20, w * .15f, .5f, spin * .4f, 0) };
                case "ring":
                    float rg = R * .75f;
                    return new[] { K(0, cos * rg * .3f, sin * rg * .3f * .6f, .4f, 0, 0), K(.4f, cos * rg, sin * rg * .6f, 1, ang * 57, 1), K(1, cos * rg * 1.35f, sin * rg * .8f, .7f, ang * 57 + spin * .3f, 0) };
                default: // streak: in from the attacker's side, through the target and out
                    float len = Mathf.Max(1, from.magnitude), px = -from.y / len, py = from.x / len, off = (t - .5f) * w * .8f;
                    return new[]
                    {
                        K(0, from.x * .55f + px * off, from.y * .55f + py * off, .7f, 0, 0), K(.5f, px * off * .4f, py * off * .4f, 1.1f, spin * .2f, 1),
                        K(1, -from.x / len * w * .8f + px * off, -from.y / len * w * .8f + py * off, .5f, spin * .5f, 0),
                    };
            }
        }

        static Vector2 Flip(Vector2 v) => new Vector2(v.x, -v.y);

        // ---- pieces ----

        // an emoji glyph with a soft white glow (the site's drop-shadow), on the field
        RectTransform Glyph(string emoji, float size, Color glow)
        {
            var r = UiKit.Node("glyph", field);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.sizeDelta = new Vector2(size, size);
            r.gameObject.AddComponent<CanvasGroup>();
            if (glow.a > 0) Picture(r, UiKit.Glow, glow.WithAlpha(.55f), new Vector2(size / 2, size / 2), Vector2.one * size * 1.7f);
            Picture(r, Art.Emote(emoji) ?? Art.Emote("✨"), Color.white, new Vector2(size / 2, size / 2), Vector2.one * size);
            return r;
        }

        // a tinted particle mask (MV_G) with a small glow in its own colour
        RectTransform Particle(string glyph, Color tint, float size)
        {
            var img = Picture(field, BattleArt.Particle(glyph), tint, Vector2.zero, new Vector2(size, size));
            var glow = Picture(img.rectTransform, UiKit.Glow, tint.WithAlpha(.4f), new Vector2(size / 2, size / 2), Vector2.one * size * 1.8f);
            glow.transform.SetAsFirstSibling();
            return img.rectTransform;
        }

        static RectTransform Stretch(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }
    }
}
