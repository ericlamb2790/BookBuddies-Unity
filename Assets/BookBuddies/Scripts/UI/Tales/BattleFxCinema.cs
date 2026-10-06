using System;
using System.Collections;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    // The big moments: the ultimate cut-in and its signature wash, the fate d20 and the friend it may bring,
    // the boss's rise band, screen flashes, confetti and cheers.
    public sealed partial class BattleFx
    {
        static readonly string[] Roman = { "", "I", "II", "III", "IV", "V" };

        /// <summary>ultCut: the full-screen ultimate card (portrait, name, move) in its style (cutin, eclipse, pages, sunrise), then a flash. Returns the seconds it holds the fight.</summary>
        public float UltCut(BattleEvent e)
        {
            var u = engine.Find(e.Actor);
            string uv = e.Uv ?? "cutin";
            float dur = T(1050), hold = T(1180);
            sound.Play("ult", uv);
            var root = UiKit.Node("ultimate", over).Fill();
            root.gameObject.AddComponent<CanvasGroup>();
            UiKit.Cover(root, "dark", Palette.Hex("#0b0614").WithAlpha(.9f));
            var tint = uv == "eclipse" ? Palette.Hex("#9a6bff") : uv == "pages" ? Palette.Hex("#ffb98a") : Gold;
            var glow = UiKit.Cover(root, "glow", tint.WithAlpha(.45f), UiKit.Glow);
            glow.rectTransform.anchorMin = new Vector2(-.1f, uv == "sunrise" ? -.6f : -.2f);
            glow.rectTransform.anchorMax = new Vector2(1.1f, uv == "sunrise" ? .9f : 1.2f);
            UltExtras(root, uv, e.Icon);

            float side = Mathf.Min(over.rect.width * .44f, 280);
            var portrait = UiKit.Node("portrait", root).Pin(new Vector2(.5f, .56f), Vector2.zero, new Vector2(side, side));
            portrait.pivot = new Vector2(.5f, 0);
            if (u != null && u.IsFoe) FoeArt.MakeUi(portrait, FoeArt.For(u.Foe, u.Boss));
            else if (u != null)
            {
                var pic = UiKit.Node("pet", portrait).Fill().gameObject.AddComponent<Image>();
                pic.sprite = PetSprites.For(u.Hero?.Look ?? Buddy.ShownLook);
                pic.preserveAspect = true;
                pic.raycastTarget = false;
            }
            var who = UiKit.Label(root, BattleText.Prose($"{u?.Name} · ULTIMATE"), 16, Gold, UiKit.Bold, TextAnchor.MiddleCenter);
            who.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -6), new Vector2(900, 24));
            var title = UiKit.Label(root, BattleText.Prose(UiKit.SplitEmoji(e.Name, out _)), 58, Palette.Hex("#ffe6a8"), UiKit.Title, TextAnchor.MiddleCenter);
            title.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -60), new Vector2(1400, 80));
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .5f);

            KeyTween.Play(root, dur, BattleEase.Linear, new Kf(0, 0, 0, 0, 1, 0), new Kf(.12f), new Kf(.85f), new Kf(1, 0, 0, 0, 1, 0));
            float sw = over.rect.width, sh = over.rect.height;
            Kf[] pose = uv == "sunrise" ? new[] { new Kf(0, 0, -.4f * sh, 0, .8f), new Kf(.45f, 0, 6, 0, 1.04f), new Kf(1) }
                : uv == "eclipse" ? new[] { new Kf(0, 0, 0, 90, .2f, 0), new Kf(.4f, 0, 0, -4, 1.06f), new Kf(1) }
                : uv == "pages" ? new[] { new Kf(0, 0, .3f * sh, -12, 1, 0), new Kf(.4f, 0, -4, 3), new Kf(1) }
                : new[] { new Kf(0, -.6f * sw, 0, 20), new Kf(.3f, 6, 0, -3), new Kf(.4f), new Kf(1, 0, 0, 0, 1.05f) };
            KeyTween.Play(portrait, dur, BattleEase.Soft, pose);
            KeyTween.Play(title, dur, BattleEase.Out, new Kf(0, 0, 0, 0, 2.2f, 0), new Kf(.35f, 0, 0, 0, .95f), new Kf(1));
            After(hold, () =>
            {
                Destroy(root.gameObject);
                Flash(uv == "eclipse" ? Palette.Hex("#cdb8ff") : uv == "sunrise" ? Palette.Hex("#ffe1a0") : Color.white, .8f, T(280));
            });
            return hold;
        }

        // the eclipse's spinning ring with the move at its top, the sunrise's rays, the pages flying past
        void UltExtras(RectTransform root, string uv, string icon)
        {
            if (uv == "eclipse")
            {
                float size = Mathf.Min(over.rect.width * .7f, 460);
                var ring = UiKit.Node("eclipse", root).Pin(new Vector2(.5f, .62f), Vector2.zero, new Vector2(size, size));
                var dashes = UiKit.Node("ring", ring).Fill().gameObject.AddComponent<Image>();
                dashes.sprite = Art.DashedRing;
                dashes.color = Palette.Hex("#cdb8ff");
                dashes.raycastTarget = false;
                var top = UiKit.Icon(ring, string.IsNullOrEmpty(icon) ? "✨" : icon, 44);
                top.rectTransform.Pin(new Vector2(.5f, 1), Vector2.zero, new Vector2(44, 44));
                ring.gameObject.AddComponent<Spin>().DegreesPerSecond = -120;
            }
            else if (uv == "sunrise" && BattleArt.Burst)
            {
                float size = Mathf.Max(over.rect.width, over.rect.height) * 1.8f;
                var rays = UiKit.Node("rays", root).Pin(new Vector2(.5f, .2f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
                rays.sprite = BattleArt.Burst;
                rays.color = Palette.Hex("#ffe7a8").WithAlpha(.18f);
                rays.raycastTarget = false;
                rays.gameObject.AddComponent<Spin>().DegreesPerSecond = -45;
            }
            else if (uv == "pages")
                for (int i = 0; i < 16; i++)
                {
                    var page = UiKit.Icon(root, "📄", 40);
                    var r = page.rectTransform.Pin(new Vector2(0, UnityEngine.Random.Range(.14f, .94f)), new Vector2(-60, 0), new Vector2(40, 40));
                    float w = over.rect.width * 1.25f, d = UnityEngine.Random.Range(.7f, 1.2f);
                    KeyTween.Play(r, d, BattleEase.Linear, i * .05f, true, new Kf(0), new Kf(1, w, 0, -540));
                }
        }

        /// <summary>sgUlt: the ultimate's signature washes the screen: a glow, fourteen turning lines and the big shape.</summary>
        public void SgUlt(BattleEvent e)
        {
            float d = T(900);
            if (d < .04f || GameSettings.ReduceMotion) return;
            var s = Sg.Of(e, engine);
            float size = Mathf.Min(over.rect.width, over.rect.height) * 1.1f;
            var root = UiKit.Node("signature", over).Fill();
            root.gameObject.AddComponent<CanvasGroup>();
            var wash = UiKit.Cover(root, "wash", s.Color2.WithAlpha(.4f), UiKit.Glow);
            wash.rectTransform.anchorMin = new Vector2(-.2f, -.3f);
            wash.rectTransform.anchorMax = new Vector2(1.2f, 1.3f);
            var lines = UiKit.Node("lines", root).Pin(new Vector2(.5f, .5f), Vector2.zero, Vector2.one * size * 1.6f);
            for (int i = 0; i < 14; i++)
            {
                var line = Picture(lines, UiKit.Rounded(2), s.Color2.WithAlpha(.75f), new Vector2(size * .8f, size * .8f), new Vector2(size * .5f, 4));
                line.preserveAspect = false;
                line.type = Image.Type.Sliced;
                line.rectTransform.pivot = new Vector2(-.6f, .5f);
                line.rectTransform.localRotation = Quaternion.Euler(0, 0, -(i / 14f * 360 + s.Hash % 25));
            }
            var shape = Picture(root, BattleArt.Shape(s.Shape), s.Color.WithAlpha(.85f), Vector2.zero, Vector2.one * size);
            shape.rectTransform.anchorMin = shape.rectTransform.anchorMax = new Vector2(.5f, .5f);
            shape.rectTransform.anchoredPosition = Vector2.zero;
            KeyTween.Play(root, d, BattleEase.Linear, 0, true, new Kf(0, 0, 0, 0, 1, 0), new Kf(.12f), new Kf(.7f), new Kf(1, 0, 0, 0, 1, 0));
            KeyTween.Play(shape, d, BattleEase.Soft, new Kf(0, 0, 0, 0, .2f), new Kf(.5f, 0, 0, -s.Spin * 90, 1), new Kf(1, 0, 0, -s.Spin * 140, 1.25f));
            KeyTween.Play(lines, d, BattleEase.Out, new Kf(0, 0, 0, 0, .4f), new Kf(1, 0, 0, -s.Spin * 25, 1.3f));
            Flash(Color.white, .22f, Mathf.Min(d, .6f));
        }

        /// <summary>
        /// playFate: the d20 tumbles in, flickers and lands on the roll; then the callout, or a storybook friend slides in
        /// and does its thing (apply plays each of its hits).
        /// </summary>
        public IEnumerator Fate(BattleEvent e, Action<BattleHit> apply)
        {
            float size = BattleArt.DieSize * 1.3f;
            var group = UiKit.Node("fate", field);
            group.anchorMin = group.anchorMax = Vector2.zero;
            group.anchoredPosition = new Vector2(Width / 2, Height * .56f);
            group.sizeDelta = Vector2.zero;
            group.gameObject.AddComponent<CanvasGroup>();
            var die = Picture(group, BattleArt.Die("plain"), Color.white, Vector2.zero, Vector2.one * size);
            var number = UiKit.Label(group, "?", 44, Color.white, UiKit.Title, TextAnchor.MiddleCenter);
            number.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -4), new Vector2(size, size));
            Stroke(number, 3);
            var caption = Pill(group, $"{BattleText.Prose(e.Name)} rolls the d20", 16, Gold);
            caption.anchoredPosition = new Vector2(-caption.sizeDelta.x / 2, -size / 2 - 44);

            sound.Play("tap");
            KeyTween.Play(die, T(700), BattleEase.Soft, new Kf(0, -120, -90, 0, .4f, 0), new Kf(.55f, 16, 40, -420, 1.05f), new Kf(.8f, -4, -4, -520, .96f), new Kf(1, 0, 0, -540));
            for (float until = Time.time + T(720); Time.time < until; )
            {
                number.text = UnityEngine.Random.Range(1, 21).ToString();
                yield return new WaitForSeconds(.07f);
            }
            number.text = e.Roll.ToString();
            die.sprite = BattleArt.Die(e.Result == "fumble" ? "no" : e.Result == "meh" ? "meh" : "ok");
            KeyTween.Play(group, T(300), BattleEase.Out, new Kf(0, 0, 0, 0, 1.25f), new Kf(1));
            sound.Play(e.Result == "fumble" ? "miss" : e.Result == "nat" ? "fanfare" : e.Result == "great" ? "sparkle" : "pop");
            DieBurst(group.anchoredPosition, e.Result != "fumble", e.Result == "nat");
            if (e.Result == "nat") Tip(null, "✨ Natural 20!");
            yield return new WaitForSeconds(T(420));
            KeyTween.Play(group, T(300), BattleEase.Linear, 0, true, new Kf(0), new Kf(1, 0, 20, 0, .8f, 0));

            if (e.Helper == null) { Callout(e, "🎲 " + e.Line); yield break; }
            yield return Friend(e, apply);
        }

        // ten sparks (eighteen and confetti on a natural 20) burst from the die
        void DieBurst(Vector2 at, bool good, bool nat)
        {
            int n = nat ? 18 : 10;
            for (int i = 0; i < n; i++)
            {
                float ang = UnityEngine.Random.value * Mathf.PI * 2, r = 70 + UnityEngine.Random.value * 70;
                var bit = Picture(field, BattleArt.Particle("star"), good ? Gold : Coral, at, new Vector2(18, 18));
                KeyTween.Play(bit, UnityEngine.Random.Range(.9f, 1.3f), BattleEase.Soft, 0, true, new Kf(0, 0, 0, 0, .5f), new Kf(1, Mathf.Cos(ang) * r, Mathf.Sin(ang) * r - 30, -360, 1, 0));
            }
            if (nat) Confetti(60);
        }

        // the friend slides in from the left with its line, hits land, then it slides away
        IEnumerator Friend(BattleEvent e, Action<BattleHit> apply)
        {
            var npc = TalesData.Current.Npcs.Find(n => n.Key == e.Helper);
            if (npc == null) { foreach (var f in e.Fx) apply(f); yield break; }
            float size = Mathf.Clamp(Width * .17f, 62, 124) * 1.25f;
            var group = UiKit.Node("friend", field);
            group.anchorMin = group.anchorMax = group.pivot = Vector2.zero;
            group.anchoredPosition = new Vector2(Width * .04f, Height * .3f);
            group.sizeDelta = new Vector2(size * (1 + (npc.Count - 1) * .75f), size);
            group.gameObject.AddComponent<CanvasGroup>();
            var sprite = PetSprites.For(npc.Look);
            for (int i = 0; i < Mathf.Max(1, npc.Count); i++)
                Picture(group, sprite, Color.white, new Vector2(size / 2 + i * size * .75f, size / 2), Vector2.one * size);
            var line = Pill(group, npc.Say, 17, Palette.Cream);
            line.anchoredPosition = new Vector2(0, size + 8);
            var tag = Pill(group, $"{npc.Icon} {npc.Name}", 16, Gold);
            tag.anchoredPosition = new Vector2(0, -tag.sizeDelta.y - 4);

            sound.Play("chime");
            float slide = group.sizeDelta.x * 1.2f + Width * .04f;
            KeyTween.Play(group, T(480), BattleEase.Out, new Kf(0, -slide, 0, 0, 1, 0), new Kf(.7f, group.sizeDelta.x * .06f, 0), new Kf(1));
            yield return new WaitForSeconds(T(600));
            bool hits = e.Fx.Exists(f => f.Damage > 0);
            if (hits) sound.Play("whoosh");
            foreach (var f in e.Fx)
            {
                var v = stage.View(f.Unit);
                if (f.Damage > 0 && v != null) KeyTween.Play(v.Motion, T(320), BattleEase.Linear, new Kf(0), new Kf(.5f, 0, 10, 6), new Kf(1));
                apply(f);
            }
            foreach (var p in e.Pops) Tip(stage.View(p.Key), p.Value);
            yield return new WaitForSeconds(T(800));
            KeyTween.Play(group, T(420), BattleEase.In, 0, true, new Kf(0), new Kf(1, -slide, 0, 0, 1, 0));
        }

        /// <summary>The boss's rise: a band across the screen with its new name and what it gained.</summary>
        public void RiseBand(BattleEvent e, int phase)
        {
            var band = UiKit.Panel(over, "rise", new Color(0, 0, 0, .72f), 0);
            band.raycastTarget = false;
            var r = band.rectTransform;
            r.anchorMin = new Vector2(0, .7f);
            r.anchorMax = new Vector2(1, .7f);
            r.pivot = new Vector2(.5f, .5f);
            r.sizeDelta = new Vector2(0, 170);
            var small = UiKit.Label(r, "P H A S E   " + string.Join(" ", Roman[Mathf.Clamp(phase + 1, 1, 5)].ToCharArray()), 16, Palette.Hex("#ff8a7a"), UiKit.Bold, TextAnchor.MiddleCenter);
            small.rectTransform.Pin(new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(800, 22));
            var big = UiKit.Label(r, BattleText.Prose(e.Name), 52, Palette.Hex("#fff1dc"), UiKit.Title, TextAnchor.MiddleCenter);
            big.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 4), new Vector2(1600, 70));
            var sub = UiKit.Label(r, e.Sub ?? "", 19, Gold, UiKit.Bold, TextAnchor.MiddleCenter);
            sub.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(1200, 26));
            KeyTween.Play(r, T(2300), BattleEase.Linear, 0, true, Kf.Squash(0, 0, 0, 0, 1, .2f, 0), new Kf(.1f), new Kf(.82f), new Kf(1, 0, 10, 0, 1, 0));
        }

        /// <summary>A full-screen flash that fades from alpha to nothing.</summary>
        public void Flash(Color color, float alpha, float seconds)
        {
            if (seconds < .01f) return;
            var img = UiKit.Cover(over, "flash", color);
            KeyTween.Play(img, seconds, BattleEase.Linear, 0, true, new Kf(0, 0, 0, 0, 1, alpha), new Kf(1, 0, 0, 0, 1, 0));
        }

        /// <summary>The site's confetti: paper bits thrown up from high in the middle, falling and spinning for about two seconds.</summary>
        public void Confetti(int n)
        {
            if (!GameSettings.ReduceMotion) BattleConfetti.Throw(over, n);
        }

        /// <summary>floatEmo: a cheer emoji rises from the pet (at most six at once).</summary>
        public void Cheer(BattleUnitView v, string emoji)
        {
            live.RemoveAll(g => !g);
            int floating = live.FindAll(g => g.name == "cheer").Count;
            if (floating >= 6) return;
            sound.Play("pop", "soft");
            Vector2 at = v != null ? v.Center : new Vector2(UnityEngine.Random.Range(40, Width - 40), 40);
            var bit = Glyph(emoji, 44, Color.clear);
            bit.name = "cheer";
            bit.anchoredPosition = at;
            float dx = (UnityEngine.Random.value - .5f) * 60;
            KeyTween.Play(bit, 1.6f, BattleEase.Out, 0, true, new Kf(0, 0, 0, 0, .4f, 0), new Kf(.2f, 0, 30, 0, 1.3f), new Kf(1, dx, 160, 0, 1, 0));
            live.Add(bit.gameObject);
        }

        /// <summary>Runs act after a delay (gone with the battle if it closes first).</summary>
        public void After(float seconds, Action act) => StartCoroutine(Later(seconds, act));

        static IEnumerator Later(float seconds, Action act)
        {
            yield return new WaitForSeconds(seconds);
            act();
        }

        /// <summary>Turns an element at a steady rate (rays, rings).</summary>
        sealed class Spin : MonoBehaviour
        {
            public float DegreesPerSecond;
            void Update() { if (!GameSettings.ReduceMotion) transform.Rotate(0, 0, DegreesPerSecond * Time.deltaTime); }
        }
    }

    /// <summary>Confetti pieces (35% dots, the rest strips) with the site's launch, gravity, drag, spin and fade.</summary>
    public sealed class BattleConfetti : MonoBehaviour
    {
        static readonly string[] Colors = { "#ffb938", "#ff5fa2", "#4fd18b", "#5aa9ff", "#a77bff", "#ff7a45" };
        const float Life = 1.9f;

        RectTransform[] bits;
        Vector2[] velocity;
        float[] spin;
        CanvasGroup group;
        float age;

        /// <summary>Throws n pieces from (50%, 65%) of the layer.</summary>
        public static void Throw(RectTransform layer, int n)
        {
            var root = UiKit.Node("confetti", layer).Fill();
            var c = root.gameObject.AddComponent<BattleConfetti>();
            c.group = root.gameObject.AddComponent<CanvasGroup>();
            c.bits = new RectTransform[n];
            c.velocity = new Vector2[n];
            c.spin = new float[n];
            var from = new Vector2(layer.rect.width * .5f, layer.rect.height * .65f);
            for (int i = 0; i < n; i++)
            {
                bool dot = UnityEngine.Random.value < .35f;
                float s = UnityEngine.Random.Range(5f, 11f) * 1.4f;
                var img = UiKit.Node("bit", root).gameObject.AddComponent<Image>();
                img.sprite = dot ? Art.Disc : Art.White;
                img.color = Palette.Hex(Colors[UnityEngine.Random.Range(0, Colors.Length)]);
                img.raycastTarget = false;
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.sizeDelta = dot ? new Vector2(s, s) : new Vector2(s, s * .66f);
                r.anchoredPosition = from;
                r.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.value * 360);
                c.bits[i] = r;
                c.velocity[i] = new Vector2(UnityEngine.Random.Range(-5.5f, 5.5f), 4 + UnityEngine.Random.value * 11) * 60 * 1.4f; // px per frame at 60 fps
                c.spin[i] = UnityEngine.Random.Range(-.175f, .175f) * Mathf.Rad2Deg * 60;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            group.alpha = Mathf.Max(0, 1 - age / Life);
            if (age >= Life) { Destroy(gameObject); return; }
            float drag = Mathf.Pow(.99f, dt * 60);
            for (int i = 0; i < bits.Length; i++)
            {
                velocity[i].y -= .32f * 3600 * 1.4f * dt;
                velocity[i].x *= drag;
                bits[i].anchoredPosition += velocity[i] * dt;
                bits[i].Rotate(0, 0, spin[i] * dt);
            }
        }
    }
}
