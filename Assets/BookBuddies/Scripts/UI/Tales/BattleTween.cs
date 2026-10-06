using System;
using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>CSS timing curves (cubic-bezier), so the battle's motion keeps the site's feel.</summary>
    public sealed class BattleEase
    {
        public static readonly BattleEase
            Linear = new BattleEase(0, 0, 1, 1),
            Ease = new BattleEase(.25f, .1f, .25f, 1),
            In = new BattleEase(.42f, 0, 1, 1),
            Out = new BattleEase(0, 0, .58f, 1),
            InOut = new BattleEase(.42f, 0, .58f, 1),
            Snap = new BattleEase(.3f, .7f, .4f, 1),      // lunges, hops, lane jumps
            Soft = new BattleEase(.2f, .8f, .3f, 1),      // numbers, shapes, cut-ins
            Glide = new BattleEase(.3f, .85f, .3f, 1),    // lane changes
            Wipe = new BattleEase(.6f, 0, .4f, 1),        // the road's burst wipe
            Open = new BattleEase(.2f, 1, .3f, 1),        // the battle's circle reveal
            Shot = new BattleEase(.5f, 0, .9f, .6f),      // bolts and discs
            Fall = new BattleEase(.5f, 0, .9f, .5f),      // rain
            Spray = new BattleEase(.2f, .75f, .35f, 1),   // class particles
            Swing = new BattleEase(.3f, 1.4f, .5f, 1);    // overshooting pops

        readonly float x1, y1, x2, y2;

        public BattleEase(float x1, float y1, float x2, float y2) { this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2; }

        /// <summary>Progress at time x (both 0 to 1).</summary>
        public float At(float x)
        {
            x = Mathf.Clamp01(x);
            if (x1 == y1 && x2 == y2) return x;
            float t = x;
            for (int i = 0; i < 6; i++) // Newton's method on the curve's x(t)
            {
                float dx = Curve(t, x1, x2) - x, slope = Slope(t, x1, x2);
                if (Mathf.Abs(dx) < 1e-4f) break;
                if (Mathf.Abs(slope) < 1e-5f) { t = Bisect(x); break; }
                t = Mathf.Clamp01(t - dx / slope);
            }
            return Curve(t, y1, y2);
        }

        float Bisect(float x)
        {
            float lo = 0, hi = 1, t = x;
            for (int i = 0; i < 20; i++)
            {
                t = (lo + hi) / 2;
                if (Curve(t, x1, x2) < x) lo = t; else hi = t;
            }
            return t;
        }

        static float Curve(float t, float a, float b) { float u = 1 - t; return 3 * u * u * t * a + 3 * u * t * t * b + t * t * t; }
        static float Slope(float t, float a, float b) { float u = 1 - t; return 3 * u * u * a + 6 * u * t * (b - a) + 3 * t * t * (1 - b); }
    }

    /// <summary>One keyframe at time T (0 to 1): offset from home in reference pixels, turn in degrees, size and opacity.</summary>
    public struct Kf
    {
        public float T, X, Y, R, Sx, Sy, A;

        public Kf(float t, float x = 0, float y = 0, float r = 0, float s = 1, float a = 1) { T = t; X = x; Y = y; R = r; Sx = Sy = s; A = a; }

        /// <summary>A keyframe with a squash or stretch (different x and y sizes).</summary>
        public static Kf Squash(float t, float x, float y, float r, float sx, float sy, float a = 1) => new Kf(t, x, y, r, 1, a) { Sx = sx, Sy = sy };

        public static Kf Lerp(Kf a, Kf b, float k) => new Kf
        {
            T = a.T + (b.T - a.T) * k, X = a.X + (b.X - a.X) * k, Y = a.Y + (b.Y - a.Y) * k, R = a.R + (b.R - a.R) * k,
            Sx = a.Sx + (b.Sx - a.Sx) * k, Sy = a.Sy + (b.Sy - a.Sy) * k, A = a.A + (b.A - a.A) * k,
        };
    }

    /// <summary>
    /// Plays keyframes on a UI element like the site's el.animate(): the curve warps the whole timeline and frames blend
    /// linearly in between; the first frame holds through the delay and the last one stays (or the element is destroyed).
    /// Position is an offset from where the element stood when its first tween began.
    /// </summary>
    public sealed class KeyTween : MonoBehaviour
    {
        Kf[] keys;
        BattleEase ease;
        float seconds, delay, age;
        bool destroy, running;
        Vector2 home;
        CanvasGroup group;
        RectTransform rect;

        /// <summary>Plays now on c's object, replacing any tween it's already playing.</summary>
        public static KeyTween Play(Component c, float seconds, BattleEase ease, params Kf[] keys) => Play(c, seconds, ease, 0, false, keys);

        /// <summary>Plays after delay seconds; destroyAtEnd removes the element when it's done (fire and forget effects).</summary>
        public static KeyTween Play(Component c, float seconds, BattleEase ease, float delay, bool destroyAtEnd, params Kf[] keys)
        {
            var tw = c.GetComponent<KeyTween>();
            if (!tw) tw = c.gameObject.AddComponent<KeyTween>();
            tw.Begin(seconds, ease, delay, destroyAtEnd, keys);
            return tw;
        }

        /// <summary>Stops a tween on c and puts the element back where it started.</summary>
        public static void Stop(Component c)
        {
            var tw = c ? c.GetComponent<KeyTween>() : null;
            if (tw && tw.running) { tw.Apply(new Kf(1)); tw.running = false; }
        }

        void Begin(float s, BattleEase e, float d, bool kill, Kf[] k)
        {
            rect = (RectTransform)transform;
            if (!running) home = rect.anchoredPosition;
            keys = Spread(k);
            ease = e ?? BattleEase.Linear;
            seconds = Mathf.Max(.001f, s);
            delay = d;
            destroy = kill;
            age = 0;
            running = true;
            bool fades = false;
            foreach (var x in keys) fades |= x.A < 1;
            if (fades && !group && !TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            Apply(keys[0]);
        }

        // keyframes without a time are spaced evenly, like WAAPI's missing offsets
        static Kf[] Spread(Kf[] k)
        {
            var o = (Kf[])k.Clone();
            for (int i = 0; i < o.Length; i++) if (o[i].T < 0) o[i].T = o.Length == 1 ? 1 : i / (float)(o.Length - 1);
            return o;
        }

        void Update()
        {
            if (!running) return;
            age += Time.deltaTime;
            float t = (age - delay) / seconds;
            if (t < 0) return;
            if (t >= 1)
            {
                running = false;
                if (destroy) { Destroy(gameObject); return; }
                Apply(keys[keys.Length - 1]);
                return;
            }
            Apply(Sample(ease.At(t)));
        }

        Kf Sample(float t)
        {
            if (t <= keys[0].T) return keys[0];
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].T) return Kf.Lerp(keys[i - 1], keys[i], (t - keys[i - 1].T) / Mathf.Max(1e-5f, keys[i].T - keys[i - 1].T));
            return keys[keys.Length - 1];
        }

        void Apply(Kf k)
        {
            rect.anchoredPosition = home + new Vector2(k.X, k.Y);
            rect.localRotation = Quaternion.Euler(0, 0, k.R);
            rect.localScale = new Vector3(k.Sx, k.Sy, 1);
            if (group) group.alpha = k.A;
        }

        /// <summary>Keyframes with times: K(0, …), K(.3, …). Use -1 for "spread evenly".</summary>
        public static Kf K(float t, float x = 0, float y = 0, float r = 0, float s = 1, float a = 1) => new Kf(t, x, y, r, s, a);
    }

    /// <summary>Moves a UI element along a path function of time (projectiles and particles), then removes it.</summary>
    public sealed class PathMover : MonoBehaviour
    {
        /// <summary>Position, turn (degrees), size and opacity at progress t.</summary>
        public delegate (Vector2 at, float turn, float size, float alpha) PathFn(float t);

        PathFn path;
        BattleEase ease;
        float seconds, delay, age;
        RectTransform rect;
        CanvasGroup group;

        public static PathMover Run(RectTransform r, float seconds, BattleEase ease, float delay, PathFn path)
        {
            var m = r.gameObject.AddComponent<PathMover>();
            m.rect = r;
            m.path = path;
            m.ease = ease ?? BattleEase.Linear;
            m.seconds = Mathf.Max(.001f, seconds);
            m.delay = delay;
            if (!r.TryGetComponent(out m.group)) m.group = r.gameObject.AddComponent<CanvasGroup>();
            m.Apply(0);
            return m;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = (age - delay) / seconds;
            if (t < 0) return;
            if (t >= 1) { Destroy(gameObject); return; }
            Apply(ease.At(t));
        }

        void Apply(float t)
        {
            var p = path(t);
            rect.anchoredPosition = p.at;
            rect.localRotation = Quaternion.Euler(0, 0, p.turn);
            rect.localScale = Vector3.one * p.size;
            group.alpha = p.alpha;
        }
    }
}
