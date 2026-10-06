using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>Plays a looping frame sequence (fountain, Ferris wheel, carousel).</summary>
    public sealed class Flipbook : MonoBehaviour
    {
        public Sprite[] frames;
        public float seconds = 4;
        SpriteRenderer r;

        void Awake() => r = GetComponent<SpriteRenderer>();

        void Update()
        {
            if (frames == null || frames.Length == 0) return;
            int f = (int)(Mathf.Repeat(Time.time, seconds) / seconds * frames.Length) % frames.Length;
            if (frames[f]) r.sprite = frames[f];
        }
    }

    /// <summary>A gentle breeze: trees, flags and balloons rock around their base.</summary>
    public sealed class Sway : MonoBehaviour
    {
        public float degrees = 1.6f;
        public float speed = 1.1f;
        float phase;
        Quaternion rest;

        void Start()
        {
            rest = transform.rotation;
            var p = transform.position;
            phase = Draw.Hash((int)p.x, (int)p.z) * 6.28f;
        }

        void Update()
        {
            float a = Mathf.Sin(Time.time * speed + phase) * degrees + Mathf.Sin(Time.time * speed * 2.3f + phase * 2) * degrees * .35f;
            transform.rotation = rest * Quaternion.Euler(0, 0, a);
        }
    }

    /// <summary>Bobs up and down on the screen (coins and gifts waiting to be found).</summary>
    public sealed class Bob : MonoBehaviour
    {
        public float height = .08f;
        Vector3 rest;
        float phase;

        void Start() { rest = transform.localPosition; phase = rest.x; }

        void Update() => transform.localPosition = rest + transform.up * Mathf.Sin(Time.time / .35f + phase) * height;
    }

    /// <summary>Grows and fades, then removes itself (tap rings, dust puffs, poofs).</summary>
    public sealed class Puff : MonoBehaviour
    {
        public float life = .7f;
        public float grow = 2f;
        public float startAlpha = .9f;
        SpriteRenderer r;
        Vector3 size;
        float age;

        void Start() { r = GetComponent<SpriteRenderer>(); size = transform.localScale; }

        void Update()
        {
            age += Time.deltaTime;
            float q = age / life;
            if (q >= 1) { Destroy(gameObject); return; }
            transform.localScale = size * (1 + (grow - 1) * q);
            var c = r.color;
            c.a = startAlpha * (1 - q);
            r.color = c;
        }
    }
}
