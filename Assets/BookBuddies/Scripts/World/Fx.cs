using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>Little one-off effects: dust from footsteps, poofs, and emoji that float up (hearts, sparkles...).</summary>
    public static class Fx
    {
        static Transform root;

        static Transform Root => root ? root : root = new GameObject("Effects").transform;

        public static void Dust(Vector2 at)
        {
            var r = Draw.Blob("dust", Root, at.x, at.y, .09f, .07f, new Color32(240, 225, 190, 150), Draw.GroundFxOrder);
            var p = r.gameObject.AddComponent<Puff>();
            p.life = .5f; p.grow = 2.4f; p.startAlpha = .6f;
        }

        public static void Poof(Vector2 at)
        {
            var r = Draw.Standing("poof", Root, Art.SoftDot, at.x, at.y - .4f, Draw.Order(at.y + .5f));
            r.color = new Color(1, 1, 1, .9f);
            r.transform.localScale = Vector3.one * .35f;
            var p = r.gameObject.AddComponent<Puff>();
            p.grow = 3f;
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
