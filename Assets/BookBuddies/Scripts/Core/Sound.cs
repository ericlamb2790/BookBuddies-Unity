using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Every sound is made in code from the website's own recipes (petSound and the Plaza music), so the game
    /// ships without audio files. To use a recording instead, put a clip in Resources/BookBuddies/Sounds named
    /// after the sound (Sounds/hatch.wav) or the music mood (Sounds/music_pawtopia.ogg). A file always wins.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        enum Wave { Sine, Triangle, Square, Saw }

        readonly struct Tone
        {
            public readonly float F1, F2, Dur, At, Vol;
            public readonly Wave Wave;
            public Tone(float f1, float f2, float dur, float at = 0, Wave wave = Wave.Sine, float vol = .12f)
            { F1 = f1; F2 = f2; Dur = dur; At = at; Wave = wave; Vol = vol; }
        }

        const int Rate = 44100;
        const int Variants = 4;          // each effect is made a few times with the site's random pitch wobble
        const float Loudness = 1.6f;     // the site's levels are gentle; games sit a little louder
        const float Beat = .4f;          // music: one step every 400 ms, like the site

        static readonly Dictionary<string, (int root, int[] scale)> Moods = new Dictionary<string, (int, int[])>
        {
            ["pawtopia"] = (60, new[] { 0, 2, 4, 7, 9 }),
            ["home"] = (64, new[] { 0, 4, 7, 11 }),
            ["wild"] = (59, new[] { 0, 3, 5, 7, 10 }),
            ["cave"] = (50, new[] { 0, 3, 5, 7 }),
        };
        static readonly int[] Chords = { 0, 3, 4, 2 };

        static Sound me;
        AudioSource sfx, music, track;
        readonly Dictionary<string, AudioClip[]> effects = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, AudioClip> notes = new Dictionary<string, AudioClip>();
        string mood, wantMood;
        float fade, nextBeat;
        int beat;

        /// <summary>Sets up the speakers once. Safe to call again.</summary>
        public static void Init()
        {
            if (me) return;
            var go = new GameObject("Sound");
            DontDestroyOnLoad(go);
            me = go.AddComponent<Sound>();
#if UNITY_2022_2_OR_NEWER
            if (FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
#else
            if (FindObjectOfType<AudioListener>() == null) go.AddComponent<AudioListener>();
#endif
            me.sfx = go.AddComponent<AudioSource>();
            me.track = go.AddComponent<AudioSource>();
            me.track.loop = true;

            // the music's own little room: a soft low-pass and the site's 420 ms echo
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(go.transform, false);
            me.music = musicGo.AddComponent<AudioSource>();
            musicGo.AddComponent<AudioLowPassFilter>().cutoffFrequency = 2200;
            var echo = musicGo.AddComponent<AudioEchoFilter>();
            echo.delay = 420; echo.decayRatio = .35f; echo.wetMix = .5f; echo.dryMix = 1;
            foreach (var s in new[] { me.sfx, me.music, me.track }) { s.playOnAwake = false; s.spatialBlend = 0; }
        }

        /// <summary>Plays an effect: boop, happy, chat, munch, hug, splash, coin, crack, hatch, yawn, pop, gacha, rare, tap, open, close, shutter, whoosh.</summary>
        public static void Play(string name, float volume = 1)
        {
            if (!me || GameSettings.SoundVolume <= 0) return;
            var clips = me.Effect(name);
            if (clips != null) me.sfx.PlayOneShot(clips[Random.Range(0, clips.Length)], volume * GameSettings.SoundVolume * Loudness);
        }

        /// <summary>Changes the music's mood (pawtopia, home, wild, cave) with a short fade, or stops it with null.</summary>
        public static void Music(string newMood)
        {
            if (me) me.wantMood = newMood;
        }

        void Update()
        {
            // fade out, swap moods, fade back in
            float target = wantMood == mood ? 1 : 0;
            fade = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime / .8f);
            if ((fade <= 0 || mood == null) && wantMood != mood) StartMood(wantMood);
            float level = GameSettings.MusicVolume * .55f * fade;
            music.volume = level * Loudness;
            track.volume = GameSettings.MusicVolume * fade;

            if (mood == null || track.isPlaying || Time.unscaledTime < nextBeat) return;
            nextBeat = Time.unscaledTime + Beat;
            if (level > 0) PlayBeat();
            beat++;
        }

        void StartMood(string next)
        {
            mood = next;
            beat = 0;
            track.Stop();
            var file = next == null ? null : Resources.Load<AudioClip>("BookBuddies/Sounds/music_" + next);
            if (file) { track.clip = file; track.Play(); }
        }

        // pwMusic: a soft chord every 16 steps, and a random melody note on about half of the even steps
        void PlayBeat()
        {
            if (!Moods.TryGetValue(mood, out var m)) return;
            if (beat % 16 == 0)
            {
                int degree = Chords[beat / 16 % Chords.Length];
                foreach (int k in new[] { 0, 2, 4 }) music.PlayOneShot(Note(m.root, m.scale, degree + k, -12, Wave.Triangle, 6f, .045f));
            }
            if (beat % 2 == 0 && Random.value < .55f)
                music.PlayOneShot(Note(m.root, m.scale, Random.Range(0, m.scale.Length * 2), 0, Wave.Sine, 1.5f, .06f));
        }

        AudioClip Note(int root, int[] scale, int degree, int shift, Wave wave, float seconds, float vol)
        {
            int n = scale.Length, octave = Mathf.FloorToInt(degree / (float)n);
            int midi = root + scale[((degree % n) + n) % n] + octave * 12 + shift;
            string key = midi + "|" + wave + "|" + seconds;
            if (notes.TryGetValue(key, out var clip)) return clip;
            float f = 440f * Mathf.Pow(2, (midi - 69) / 12f);
            return notes[key] = Make(key, new List<Tone> { new Tone(f, f, seconds, 0, wave, vol) });
        }

        // ---- effects ----

        AudioClip[] Effect(string name)
        {
            if (effects.TryGetValue(name, out var made)) return made;
            var file = Resources.Load<AudioClip>("BookBuddies/Sounds/" + name);
            if (file) return effects[name] = new[] { file };
            if (Recipe(name, () => 1) == null) return effects[name] = null;
            made = new AudioClip[Variants];
            for (int i = 0; i < Variants; i++) made[i] = Make(name, Recipe(name, () => .85f + Random.value * .3f));
            return effects[name] = made;
        }

        /// <summary>The website's petSound recipes (plus a few for menus), as tones: from, to, seconds, start, wave, volume.</summary>
        static List<Tone> Recipe(string name, System.Func<float> R)
        {
            var t = new List<Tone>();
            switch (name)
            {
                case "boop": t.Add(new Tone(520 * R(), 880 * R(), .14f)); break;
                case "happy": t.Add(new Tone(660, 990, .1f)); t.Add(new Tone(880, 1320, .12f, .09f)); break;
                case "chat":
                    int n = 2 + Random.Range(0, 3);
                    for (int i = 0; i < n; i++) t.Add(new Tone(500 * R() + i * 40, 700 * R(), .07f, i * .08f, Wave.Triangle, .07f));
                    break;
                case "munch": for (int i = 0; i < 3; i++) t.Add(new Tone(220 * R(), 140, .05f, i * .11f, Wave.Square, .05f)); break;
                case "hug": t.Add(new Tone(440, 660, .25f, 0, Wave.Sine, .1f)); t.Add(new Tone(660, 880, .25f, .12f, Wave.Sine, .08f)); break;
                case "splash": for (int i = 0; i < 5; i++) t.Add(new Tone(900 * R(), 300, .06f, i * .05f, Wave.Sine, .05f)); break;
                case "coin": t.Add(new Tone(988, 988, .06f, 0, Wave.Square, .05f)); t.Add(new Tone(1319, 1319, .18f, .06f, Wave.Square, .05f)); break;
                case "crack": t.Add(new Tone(180 * R(), 90, .08f, 0, Wave.Saw, .08f)); t.Add(new Tone(1200 * R(), 600, .05f, .02f, Wave.Triangle, .05f)); break;
                case "hatch":
                    float[] up = { 523, 659, 784, 1047 };
                    for (int i = 0; i < up.Length; i++) t.Add(new Tone(up[i], up[i] * 1.01f, .22f, i * .12f, Wave.Triangle, .1f));
                    t.Add(new Tone(1568, 2093, .4f, .5f, Wave.Sine, .07f));
                    break;
                case "yawn": t.Add(new Tone(500, 220, .6f, 0, Wave.Sine, .08f)); break;
                case "pop": t.Add(new Tone(300, 1200, .08f, 0, Wave.Sine, .1f)); break;
                case "gacha": for (int i = 0; i < 8; i++) t.Add(new Tone(600 + i * 80, 640 + i * 80, .05f, i * .06f, Wave.Square, .04f)); break;
                case "rare":
                    float[] sparkle = { 784, 988, 1175, 1568 };
                    for (int i = 0; i < sparkle.Length; i++) t.Add(new Tone(sparkle[i], sparkle[i], .18f, i * .09f, Wave.Triangle, .09f));
                    break;
                // menus and cinematics (new in Unity, in the same voice)
                case "tap": t.Add(new Tone(620 * R(), 900, .05f, 0, Wave.Sine, .05f)); break;
                case "open": t.Add(new Tone(520, 780, .09f, 0, Wave.Triangle, .06f)); break;
                case "close": t.Add(new Tone(700, 460, .08f, 0, Wave.Triangle, .05f)); break;
                case "shutter": t.Add(new Tone(1800, 500, .05f, 0, Wave.Saw, .05f)); t.Add(new Tone(900, 300, .08f, .05f, Wave.Triangle, .06f)); break;
                case "whoosh": for (int i = 0; i < 6; i++) t.Add(new Tone(240 + i * 60 * R(), 900 * R(), .5f, i * .03f, Wave.Sine, .025f)); break;
                default: return null;
            }
            return t;
        }

        // ---- the synthesiser: Web Audio's oscillator, exponential glide and envelope, sample by sample ----

        static AudioClip Make(string name, List<Tone> tones)
        {
            float length = 0;
            foreach (var t in tones) length = Mathf.Max(length, t.At + t.Dur + .03f);
            var data = new float[Mathf.CeilToInt(length * Rate)];
            foreach (var t in tones) Add(data, t);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Add(float[] data, Tone t)
        {
            const float Attack = .015f;
            int start = Mathf.RoundToInt(t.At * Rate), count = Mathf.RoundToInt(t.Dur * Rate);
            float to = Mathf.Max(40, t.F2), decay = Mathf.Max(.001f, t.Dur - Attack);
            double phase = 0;
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float time = i / (float)Rate;
                phase += t.F1 * Mathf.Pow(to / t.F1, time / t.Dur) / Rate;
                float gain = time < Attack ? t.Vol * time / Attack : t.Vol * Mathf.Pow(.0005f / t.Vol, (time - Attack) / decay);
                data[start + i] += gain * Shape(t.Wave, (float)(phase - System.Math.Floor(phase)));
            }
        }

        static float Shape(Wave w, float p)
        {
            switch (w)
            {
                case Wave.Triangle: return 1 - 4 * Mathf.Abs(p - .5f);
                case Wave.Square: return p < .5f ? .8f : -.8f;
                case Wave.Saw: return 2 * p - 1;
                default: return Mathf.Sin(p * 2 * Mathf.PI);
            }
        }
    }
}
