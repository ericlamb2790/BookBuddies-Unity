using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The battle's sounds, made in code from the site's BBS synth (js/10-social.js) the way Sound makes the Plaza's:
    /// tones that glide and fade exponentially, plus filtered noise for swishes, pages and thumps, through the site's
    /// master chain (gain .3, low-pass 4200 Hz, a soft limiter). Same per-sound gaps and 16-voice cap as the site.
    /// A clip at Resources/BookBuddies/Battle/Sounds/&lt;name&gt; replaces a sound. Follows the sound volume setting.
    /// </summary>
    public sealed class BattleSound : MonoBehaviour
    {
        enum Wave { Sine, Triangle }
        enum Filter { Band, High, Low }

        // tone(f, {at, dur, type, vol, to, att}) and hiss(f, {at, dur, vol, to, q, kind}) from BBS
        readonly struct Voice
        {
            public readonly bool Noise;
            public readonly float F, To, At, Dur, Vol, Att, Q;
            public readonly Wave Wave;
            public readonly Filter Kind;
            public Voice(bool noise, float f, float to, float at, float dur, float vol, float att, float q, Wave wave, Filter kind)
            { Noise = noise; F = f; To = to; At = at; Dur = dur; Vol = vol; Att = att; Q = q; Wave = wave; Kind = kind; }
        }

        const int Rate = 44100, Variants = 3, MaxVoices = 16;
        const float Master = .3f * 2.4f; // the site's .3 master, lifted to sit with the game's other sounds
        static readonly Dictionary<string, float> Gaps = new Dictionary<string, float>
        { ["tap"] = .045f, ["pop"] = .07f, ["hit"] = .055f, ["crit"] = .08f, ["patter"] = .15f, ["heal"] = .09f, ["page"] = .12f, ["whoosh"] = .06f, ["swish"] = .05f };

        AudioSource source;
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> last = new Dictionary<string, float>();
        readonly List<float> playing = new List<float>(); // when each live voice ends

        /// <summary>Adds the battle's speaker to a game object (it goes away with it).</summary>
        public static BattleSound Create(GameObject host)
        {
            var s = host.AddComponent<BattleSound>();
            s.source = host.AddComponent<AudioSource>();
            s.source.playOnAwake = false;
            s.source.spatialBlend = 0;
            return s;
        }

        /// <summary>
        /// Plays a sound: page whoosh swish zap stomp patter hit crit miss heal shield buff poof ko sparkle ult win fanfare lose pop chime tap coin.
        /// Variants: hit "small", heal and pop "soft", ult "cutin" "eclipse" "pages" "sunrise".
        /// </summary>
        public void Play(string name, string variant = null)
        {
            if (!source || GameSettings.SoundVolume <= 0) return;
            float now = Time.unscaledTime;
            if (last.TryGetValue(name, out var at) && now - at < (Gaps.TryGetValue(name, out var gap) ? gap : .07f)) return;
            playing.RemoveAll(end => end <= now);
            if (playing.Count >= MaxVoices) return;
            var set = Clips(name, variant);
            if (set == null) return;
            last[name] = now;
            var clip = set[Random.Range(0, set.Length)];
            playing.Add(now + clip.length);
            source.PlayOneShot(clip, GameSettings.SoundVolume);
        }

        AudioClip[] Clips(string name, string variant)
        {
            string key = variant == null ? name : name + "." + variant;
            if (clips.TryGetValue(key, out var made)) return made;
            var file = Resources.Load<AudioClip>("BookBuddies/Battle/Sounds/" + key);
            if (!file) file = Resources.Load<AudioClip>("BookBuddies/Battle/Sounds/" + name);
            if (file) return clips[key] = new[] { file };
            if (Recipe(name, variant, () => 1) == null) return clips[key] = null;
            made = new AudioClip[Variants];
            for (int i = 0; i < Variants; i++) made[i] = Render(key, Recipe(name, variant, () => .97f + Random.value * .06f));
            return clips[key] = made;
        }

        static Voice Tone(float f, float dur, float vol, float to = 0, float at = 0, Wave wave = Wave.Sine, float att = .008f) =>
            new Voice(false, f, to, at, dur, vol, att, 0, wave, Filter.Band);

        static Voice Hiss(float f, float dur, float vol, float to = 0, float at = 0, float q = 1.2f, Filter kind = Filter.Band) =>
            new Voice(true, f, to, at, dur, vol, .01f, q, Wave.Sine, kind);

        // BBS's sound table, verbatim numbers; drift() is the site's ±3% wobble
        static List<Voice> Recipe(string name, string variant, System.Func<float> drift)
        {
            var v = new List<Voice>();
            switch (name)
            {
                case "tap": v.Add(Tone(640 * drift(), .05f, .09f, 560, 0, Wave.Triangle)); break;
                case "pop": v.Add(Tone(480 * drift(), .09f, variant == "soft" ? .08f : .14f, 880)); break;
                case "chime": v.Add(Tone(880, .5f, .09f)); v.Add(Tone(1318.5f, .55f, .06f, 0, .05f)); break;
                case "page": v.Add(Hiss(1700, .17f, .1f, 800, 0, .8f)); v.Add(Tone(190, .07f, .06f)); break;
                case "whoosh": v.Add(Hiss(500, .22f, .09f, 1700, 0, .9f)); break;
                case "swish": v.Add(Hiss(1600, .12f, .08f, 3400, 0, .7f, Filter.High)); break;
                case "zap": v.Add(Tone(620 * drift(), .18f, .07f, 1400)); v.Add(Tone(1240, .14f, .025f, 2400, 0, Wave.Triangle)); break;
                case "stomp": v.Add(Tone(150, .3f, .3f, 55)); v.Add(Hiss(320, .18f, .08f, 0, 0, 1.2f, Filter.Low)); break;
                case "patter": for (int i = 0; i < 4; i++) v.Add(Hiss(1300 * drift(), .05f, .05f, 0, i * .07f, 2)); break;
                case "hit":
                    float s = variant == "small" ? .5f : 1;
                    v.Add(Tone(230 * drift(), .12f, .24f * s, 105)); v.Add(Hiss(900, .05f, .07f * s));
                    break;
                case "crit": v.Add(Tone(240, .16f, .28f, 95)); v.Add(Hiss(1000, .06f, .08f)); v.Add(Tone(1318.5f, .3f, .06f, 0, .04f)); break;
                case "miss": v.Add(Tone(420, .12f, .06f, 320, 0, Wave.Triangle)); break;
                case "heal":
                    if (variant == "soft") { v.Add(Tone(988, .18f, .04f)); break; }
                    float[] up = { 659.25f, 783.99f, 987.77f };
                    for (int i = 0; i < 3; i++) v.Add(Tone(up[i], .22f, .06f, 0, i * .06f));
                    break;
                case "shield": v.Add(Tone(523.25f, .35f, .07f)); v.Add(Tone(783.99f, .4f, .05f)); v.Add(Tone(1567.98f, .25f, .02f, 0, 0, Wave.Triangle)); break;
                case "buff": v.Add(Tone(523.25f, .25f, .08f, 783.99f)); v.Add(Tone(1046.5f, .2f, .04f, 0, .1f)); break;
                case "poof": v.Add(Hiss(900, .25f, .1f, 300)); v.Add(Tone(700, .1f, .08f, 1100)); break;
                case "ko": v.Add(Tone(330, .32f, .09f, 247, 0, Wave.Triangle)); v.Add(Tone(247, .36f, .08f, 185, .14f, Wave.Triangle)); break;
                case "sparkle": float[] sp = { 1568, 1976, 2349 }; for (int i = 0; i < 3; i++) v.Add(Tone(sp[i], .12f, .04f, 0, i * .05f)); break;
                case "ult":
                    v.Add(Hiss(300, .7f, .11f, 2400, 0, .7f));
                    float[] ch = variant == "eclipse" ? new[] { 392f, 466.16f, 587.33f } : variant == "sunrise" ? new[] { 523.25f, 659.25f, 783.99f }
                        : variant == "pages" ? new[] { 587.33f, 739.99f, 880f } : new[] { 440f, 554.37f, 659.25f };
                    for (int i = 0; i < 3; i++) v.Add(Tone(ch[i], .9f, .05f, 0, .35f + i * .04f, Wave.Sine, .08f));
                    if (variant == "pages") v.Add(Hiss(1800, .4f, .06f, 0, .1f, .6f));
                    break;
                case "win": Arpeggio(v); break;
                case "fanfare": Arpeggio(v); foreach (float f in new[] { 523.25f, 659.25f, 783.99f }) v.Add(Tone(f, .8f, .05f, 0, .42f, Wave.Sine, .04f)); break;
                case "lose": v.Add(Tone(392, .3f, .08f, 0, 0, Wave.Triangle)); v.Add(Tone(329.63f, .45f, .07f, 0, .2f, Wave.Triangle)); break;
                case "coin": v.Add(Tone(988, .1f, .06f)); v.Add(Tone(1318.5f, .16f, .05f, 0, .07f)); break;
                default: return null;
            }
            return v;
        }

        static void Arpeggio(List<Voice> v)
        {
            float[] up = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int i = 0; i < 4; i++) v.Add(Tone(up[i], .3f, .08f, 0, i * .09f, Wave.Triangle));
        }

        // ---- the synthesiser ----

        static AudioClip Render(string name, List<Voice> voices)
        {
            float length = 0;
            foreach (var v in voices) length = Mathf.Max(length, v.At + v.Dur + .04f);
            var data = new float[Mathf.CeilToInt(length * Rate)];
            foreach (var v in voices) Add(data, v);
            Finish(data);
            var clip = AudioClip.Create("battle " + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // linear attack, exponential fall to .0005, optional exponential glide of the pitch (or the filter for noise)
        static void Add(float[] data, Voice v)
        {
            int start = Mathf.RoundToInt(v.At * Rate), count = Mathf.RoundToInt(v.Dur * Rate);
            float to = v.To > 0 ? Mathf.Max(30, v.To) : v.F;
            double phase = 0;
            var filter = v.Noise ? new Biquad() : null;
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float f = v.F * Mathf.Pow(to / v.F, t / v.Dur);
                float gain = t < v.Att ? v.Vol * t / v.Att : v.Vol * Mathf.Pow(.0005f / v.Vol, (t - v.Att) / Mathf.Max(.001f, v.Dur - v.Att));
                float s;
                if (v.Noise)
                {
                    if (i % 32 == 0) filter.Tune(v.Kind, f, v.Q);
                    s = filter.Step(Random.value * 2 - 1);
                }
                else
                {
                    phase += f / Rate;
                    float p = (float)(phase - System.Math.Floor(phase));
                    s = v.Wave == Wave.Triangle ? 1 - 4 * Mathf.Abs(p - .5f) : Mathf.Sin(p * 2 * Mathf.PI);
                }
                data[start + i] += gain * s;
            }
        }

        // master: gain, a gentle 4200 Hz low-pass and a soft limiter in place of the compressor
        static void Finish(float[] data)
        {
            float k = 1 - Mathf.Exp(-2 * Mathf.PI * 4200f / Rate), y = 0;
            for (int i = 0; i < data.Length; i++)
            {
                y += (data[i] * Master - y) * k;
                data[i] = (float)System.Math.Tanh(y * 1.4f) / 1.4f;
            }
        }

        /// <summary>The RBJ cookbook biquad (Web Audio's BiquadFilterNode).</summary>
        sealed class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public void Tune(Filter kind, float f, float q)
            {
                float w = 2 * Mathf.PI * Mathf.Clamp(f, 20, Rate * .45f) / Rate, cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2 * Mathf.Max(.05f, q));
                float a0 = 1 + alpha;
                switch (kind)
                {
                    case Filter.High: b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; break;
                    case Filter.Low: b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; break;
                    default: b0 = alpha; b1 = 0; b2 = -alpha; break;
                }
                b0 /= a0; b1 /= a0; b2 /= a0;
                a1 = -2 * cos / a0;
                a2 = (1 - alpha) / a0;
            }

            public float Step(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
