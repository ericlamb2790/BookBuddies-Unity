using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>Little one-off effects: dust from footsteps, poofs, sparkles, and emoji that float up (hearts, sparkles...).</summary>
    public static class Fx
    {
        static Transform root;

        static Transform Root => root ? root : root = new GameObject("Effects").transform;

        /// <summary>A little puff of dust kicked up by a footstep (size 1), or a skid when stopping (bigger).</summary>
        public static void Dust(Vector2 at, float size = 1)
        {
            var r = Draw.Blob("dust", Root, at.x, at.y, .09f * size, .07f * size, new Color32(240, 225, 190, 150), Draw.GroundFxOrder);
            var p = r.gameObject.AddComponent<Puff>();
            p.life = .45f + .1f * size; p.grow = 2.4f; p.startAlpha = .55f;
        }

        public static void Poof(Vector2 at, float seconds = .7f)
        {
            var r = Draw.Standing("poof", Root, Art.SoftDot, at.x, at.y - .4f, Draw.Order(at.y + .5f));
            r.color = new Color(1, 1, 1, .9f);
            r.transform.localScale = Vector3.one * .35f;
            var p = r.gameObject.AddComponent<Puff>();
            p.grow = 3f; p.life = seconds;
        }

        /// <summary>The site's sparkle: ten gold, white and sky dots spreading in a ring a tile above a map point.</summary>
        public static void Sparkle(Vector2 at, float delay = 0, float seconds = 1.6f)
        {
            var go = new GameObject("sparkle");
            go.transform.SetParent(Root, false);
            go.transform.SetPositionAndRotation(TownMap.ToWorld(at.x, at.y), TownCamera.Facing);
            var s = go.AddComponent<SparkleRing>();
            s.delay = delay; s.life = seconds; s.order = Draw.Order(at.y + 1.5f);
        }

        /// <summary>An emoji that rises from a map point and fades (hearts for hugs, sparkles for boops...).</summary>
        public static void Float(string emoji, Vector2 at, float delay = 0, float seconds = 1.4f)
        {
            var sprite = Art.Emote(emoji);
            if (!sprite) return;
            var r = Draw.Standing("fx " + emoji, Root, sprite, at.x, at.y, 30000);
            r.transform.localScale = Vector3.one * .45f;
            var f = r.gameObject.AddComponent<FloatUp>();
            f.delay = delay; f.life = seconds;
        }

        /// <summary>An emoji tossed in an arc from one pet to another (a ball for fetch, a cookie for snacks).</summary>
        public static void Toss(string emoji, Vector2 from, Vector2 to, float seconds = 1.5f)
        {
            var sprite = Art.Emote(emoji);
            if (!sprite) return;
            var r = Draw.Standing("toss " + emoji, Root, sprite, from.x, from.y, 30000);
            r.transform.localScale = Vector3.one * .35f;
            var f = r.gameObject.AddComponent<Arc>();
            f.from = from; f.to = to; f.life = seconds;
        }

        sealed class FloatUp : MonoBehaviour
        {
            public float delay, life = 1.4f;
            float age;
            SpriteRenderer r;
            Vector3 start;
            void Start() { r = GetComponent<SpriteRenderer>(); start = transform.position; r.enabled = false; }
            void Update()
            {
                age += Time.deltaTime;
                float q = (age - delay) / life;
                if (q < 0) return;
                if (q >= 1) { Destroy(gameObject); return; }
                r.enabled = true;
                transform.position = start + transform.up * (1.2f + q * .8f);
                r.color = new Color(1, 1, 1, q > .7f ? (1 - q) / .3f : 1);
            }
        }

        sealed class SparkleRing : MonoBehaviour
        {
            static readonly Color[] Colours = { Palette.Hex("#ffd34d"), Color.white, Palette.Hex("#7fd0ff") };
            public float delay, life = 1.6f;
            public int order;
            readonly SpriteRenderer[] dots = new SpriteRenderer[10];
            float age;

            void Start()
            {
                for (int i = 0; i < dots.Length; i++) { dots[i] = Draw.Sprite("dot", transform, Art.Disc, order, Colours[i % 3]); dots[i].enabled = false; }
            }

            void Update()
            {
                age += Time.deltaTime;
                float q = (age - delay) / life;
                if (q < 0) return;
                if (q >= 1) { Destroy(gameObject); return; }
                float r = .2f + q * .75f, size = .05f * (1 - q) + .02f;
                for (int i = 0; i < dots.Length; i++)
                {
                    float a = i / 10f * Mathf.PI * 2;
                    dots[i].enabled = true;
                    dots[i].transform.localPosition = new Vector3(Mathf.Cos(a) * r, 1 - Mathf.Sin(a) * r * .7f, 0);
                    dots[i].transform.localScale = Vector3.one * size;
                    dots[i].color = Colours[i % 3].WithAlpha(1 - q);
                }
            }
        }

        sealed class Arc : MonoBehaviour
        {
            public Vector2 from, to;
            public float life = 1.5f;
            float age;
            void Update()
            {
                age += Time.deltaTime;
                float q = age / life;
                if (q >= 1) { Destroy(gameObject); return; }
                float bounce = q < .5f ? q * 2 : (1 - q) * 2; // there and back, like fetch
                var p = Vector2.Lerp(from, to, bounce);
                transform.position = TownMap.ToWorld(p.x, p.y) + transform.up * (.6f + Mathf.Sin(bounce * Mathf.PI) * .8f);
            }
        }
    }
}
