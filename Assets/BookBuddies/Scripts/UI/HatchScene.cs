using System.Collections;
using System.Collections.Generic;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The egg hatches: the room dims, the egg wobbles and cracks three times, bursts open in a flash of
    /// sparkles and shell, and your new buddy pops out and hops. Then a card shows your recovery code
    /// (when the server made you an account) so you can keep it safe, or says hello to a pet from the pets screen. Esc, Space, Enter or A skips ahead; once the
    /// card is up, B (or Esc) is the same as "Let's go". Short screens get a smaller stage and card.
    /// </summary>
    public sealed class HatchScene : MonoBehaviour
    {
        static readonly string[] Confetti = { "✨", "💛", "🎉", "❤️", "✨", "📚" };
        const float EggSize = 320;
        const float TallEnough = 900; // shorter screens shrink the stage
        const float CardWidth = 520;

        sealed class Bit { public RectTransform r; public Image img; public Vector2 v; public float spin, age, life; }

        RectTransform root, stage, words, egg, rays, cardRect, shellTop, shellBottom;
        Image veil, glow, eggImage, petImage, flash;
        CanvasGroup raysFade, captionFade, cardFade;
        Text caption, sub;
        readonly List<Bit> bits = new List<Bit>();
        string eggLook, petLook, reader, recovery, newPet;
        System.Action done;
        Selectable first;
        bool skipped, finished;
        float cardRise = 1; // 0 while the card slides up, 1 once it's in place

        /// <summary>Runs the hatching over everything else; "done" runs after the player taps Let's go.</summary>
        public static void Play(int hue, string look, string name, string recovery, System.Action done) => Show(hue, look, name, recovery, null, done);

        /// <summary>Another egg hatches (from the pets screen): the same show, then "Meet {petName}!".</summary>
        public static void Meet(int hue, string look, string petName, System.Action done) => Show(hue, look, null, null, petName, done);

        static void Show(int hue, string look, string name, string recovery, string newPet, System.Action done)
        {
            var canvas = UiKit.MakeCanvas("Hatching", 60);
            var h = canvas.gameObject.AddComponent<HatchScene>();
            h.root = (RectTransform)canvas.transform;
            h.eggLook = "{\"h\":" + hue + ",\"s\":0}";
            h.petLook = look;
            h.reader = name;
            h.recovery = recovery;
            h.newPet = newPet;
            h.done = done;
            h.Build();
            h.StartCoroutine(h.Run());
            AutoSave.Now("hatch");
        }

        void Build()
        {
            veil = UiKit.Cover(root, "veil", new Color(.14f, .09f, .05f, 0), null, true);
            UiKit.Cover(root, "edges", new Color(0, 0, 0, .5f), UiKit.Vignette);
            stage = UiKit.Node("stage", root).Pin(new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(EggSize, EggSize));

            rays = UiKit.Node("rays", stage).Pin(new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(900, 900));
            var raysImage = rays.gameObject.AddComponent<Image>();
            raysImage.sprite = Rays();
            raysImage.color = Palette.Amber.WithAlpha(.35f);
            raysImage.raycastTarget = false;
            raysFade = rays.gameObject.AddComponent<CanvasGroup>();
            raysFade.alpha = 0;
            glow = UiKit.Cover(stage, "glow", Palette.Amber.WithAlpha(0), UiKit.Glow);
            glow.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(560, 560));

            egg = Picture("egg", eggLook, out eggImage);
            shellTop = Shell("shell top", Image.OriginVertical.Top);
            shellBottom = Shell("shell bottom", Image.OriginVertical.Bottom);
            var petRect = Picture("buddy", petLook, out petImage);
            petRect.localScale = Vector3.zero;

            words = UiKit.Node("caption", root).Fill();
            captionFade = words.gameObject.AddComponent<CanvasGroup>();
            captionFade.alpha = 0;
            caption = UiKit.Label(words, newPet != null ? $"{newPet} hatched!" : "Your buddy hatched!", 52, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter);
            ((RectTransform)caption.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -172), new Vector2(1000, 68));
            caption.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = new Color(0, 0, 0, .5f);
            sub = UiKit.Label(words, newPet != null ? "Every hatch is a surprise!" : $"Say hello, {reader}!", UiKit.HeadingSize + 2, Palette.Amber, UiKit.Bold, TextAnchor.MiddleCenter);
            ((RectTransform)sub.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -224), new Vector2(1000, 34));

            BuildCard();
            flash = UiKit.Cover(root, "flash", new Color(1, 1, 1, 0));
        }

        RectTransform Picture(string label, string look, out Image image)
        {
            var r = UiKit.Node(label, stage).Pin(new Vector2(.5f, 0), Vector2.zero, new Vector2(EggSize * 200 / 232f, EggSize));
            r.pivot = new Vector2(.5f, 0);
            image = r.gameObject.AddComponent<Image>();
            image.sprite = PetSprites.For(look);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return r;
        }

        // half of the egg, for the burst
        RectTransform Shell(string label, Image.OriginVertical side)
        {
            var r = Picture(label, eggLook, out var img);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Vertical;
            img.fillOrigin = (int)side;
            img.fillAmount = side == Image.OriginVertical.Top ? .55f : .45f;
            r.pivot = new Vector2(.5f, .45f);
            r.anchoredPosition = new Vector2(0, EggSize * .45f);
            r.gameObject.SetActive(false);
            return r;
        }

        void BuildCard()
        {
            var card = UiKit.Panel(root, "card", Palette.Cream);
            cardRect = card.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 28), new Vector2(CardWidth, 10));
            UiKit.Column(cardRect, 12, new RectOffset(28, 28, 24, 28));
            UiKit.Hug(cardRect, false, true);
            UiKit.Outline(card, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(cardRect, UiKit.CardRadius, 24, 8, .35f);
            cardFade = card.gameObject.AddComponent<CanvasGroup>();
            cardFade.alpha = 0;
            cardFade.interactable = cardFade.blocksRaycasts = false;

            Button go;
            if (!string.IsNullOrEmpty(recovery))
            {
                UiKit.Label(cardRect, "Keep your recovery code safe", UiKit.TitleSize, Palette.Ink, UiKit.Title);
                var box = UiKit.Panel(cardRect, "code", Palette.Paper, 12);
                UiKit.Outline(box, Palette.Ink.WithAlpha(.12f), 12, 1);
                UiKit.Size(box, -1, 68);
                var code = UiKit.Label(box.transform, recovery, 34, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
                ((RectTransform)code.transform).Fill();
                UiKit.Label(cardRect, "It’s the only way to bring your buddy back on a new device. Write it down or take a photo, and don’t share it with anyone.", UiKit.BodySize, Palette.InkSoft);
                var row = UiKit.Node("buttons", cardRect);
                UiKit.Row(row, 8, null, TextAnchor.MiddleRight);
                Text copyLabel = null;
                var copy = UiKit.Secondary(row, "Copy", () => { GUIUtility.systemCopyBuffer = recovery; copyLabel.text = "Copied"; });
                copyLabel = copy.GetComponentInChildren<Text>();
                go = UiKit.Primary(row, "I saved it, let’s go!", Finish);
            }
            else if (newPet != null)
            {
                UiKit.Label(cardRect, $"Meet {newPet}!", UiKit.TitleSize, Palette.Ink, UiKit.Title);
                UiKit.Label(cardRect, "Every egg hatches with its own random DNA. No two pets are alike!", UiKit.BodySize, Palette.InkSoft);
                go = UiKit.Primary(cardRect, "Let’s go!", Finish, null, 52);
            }
            else
            {
                UiKit.Label(cardRect, "Ready to explore?", UiKit.TitleSize, Palette.Ink, UiKit.Title);
                UiKit.Label(cardRect, Settings.IsLocal
                    ? "Your buddy lives on this PC, for playing offline. Choose “Play online” on the title screen any time to meet other readers in town."
                    : "Your buddy lives on this device. Sign in with a recovery code any time to meet other readers in town.", UiKit.BodySize, Palette.InkSoft);
                go = UiKit.Primary(cardRect, "Let’s go!", Finish, null, 52);
            }
            UiKit.PadHints(cardRect, ("A", "Select"));
            first = go;
        }

        // ---- the sequence ----

        IEnumerator Run()
        {
            if (newPet == null) Sound.Music(null); // a hatch in town keeps the town's music
            yield return Over(.7f, k => { veil.color = veil.color.WithAlpha(.84f * k); glow.color = Palette.Amber.WithAlpha(.25f * k); }, true);
            for (int crack = 1; crack <= 3 && !skipped; crack++)
            {
                yield return Wait(crack == 1 ? .5f : .35f);
                eggImage.sprite = PetSprites.For(eggLook, "crack" + crack);
                Sound.Play("crack");
                float strength = 6 + crack * 5, seconds = crack == 3 ? .8f : .55f;
                if (crack == 3) StartCoroutine(Over(.8f, k => raysFade.alpha = k, true));
                yield return Over(seconds, k =>
                {
                    float shake = Mathf.Sin(k * Mathf.PI * (crack == 3 ? 14 : 6)) * (1 - k * .6f);
                    egg.localRotation = Quaternion.Euler(0, 0, shake * strength);
                    egg.anchoredPosition = new Vector2(0, crack == 3 ? Mathf.Abs(shake) * 10 : 0);
                    glow.color = Palette.Amber.WithAlpha(.25f + crack * .12f + Mathf.Abs(shake) * .1f);
                }, true);
            }
            Burst();
            yield return Over(.6f, k => PopPet(k));
            yield return Over(.5f, k => captionFade.alpha = k, true);
            StartCoroutine(Hops());
            yield return Wait(1.1f);
            yield return ShowCard();
        }

        void Burst()
        {
            egg.gameObject.SetActive(false);
            flash.color = Color.white;
            StartCoroutine(Over(.7f, k => flash.color = new Color(1, 1, 1, 1 - k)));
            Sound.Play("hatch");
            raysFade.alpha = 1;
            foreach (var shell in new[] { shellTop, shellBottom }) shell.gameObject.SetActive(true);
            for (int i = 0; i < 26; i++) AddBit(Confetti[i % Confetti.Length], Random.Range(0, Mathf.PI * 2), Random.Range(260f, 620f));
        }

        void PopPet(float k)
        {
            var r = (RectTransform)petImage.transform;
            float s = UiKit.EaseBack(k);
            r.localScale = new Vector3(s * (1 + (1 - k) * .2f), s * (1 - (1 - k) * .15f), 1);
        }

        IEnumerator Hops()
        {
            var r = (RectTransform)petImage.transform;
            for (int i = 0; !finished; i++)
            {
                if (i < 2 || Random.value < .4f) Sound.Play(i == 0 ? "happy" : "boop", .7f);
                yield return Over(.6f, k => { r.anchoredPosition = new Vector2(0, Mathf.Sin(k * Mathf.PI) * 40); r.localScale = new Vector3(1, k < .12f ? .9f : 1.04f, 1); });
                r.localScale = Vector3.one;
                yield return new WaitForSecondsRealtime(i < 1 ? .1f : 1.4f);
            }
        }

        IEnumerator ShowCard()
        {
            cardFade.interactable = cardFade.blocksRaycasts = true;
            UiStack.Push(this, Finish);
            VirtualCursor.FocusFirst(first);
            Sound.Play("open");
            if (newPet == null) Sound.Music("home");
            // the buddy steps up and the caption makes way for the card
            yield return Over(.5f, k =>
            {
                cardFade.alpha = k;
                cardRise = UiKit.EaseOut(k);
                captionFade.alpha = 1 - k;
                stage.anchoredPosition = new Vector2(0, 40 + 80 * UiKit.Ease(k));
            });
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            UiStack.Remove(this);
            StartCoroutine(FadeAway());
        }

        void OnDestroy() => UiStack.Remove(this);

        IEnumerator FadeAway()
        {
            var all = root.gameObject.AddComponent<CanvasGroup>();
            yield return Over(.5f, k => all.alpha = 1 - k);
            Destroy(gameObject);
            done?.Invoke();
        }

        // ---- every frame: canvas size, skipping, shell halves and confetti ----

        void Update()
        {
            Fit();
            if (!skipped && PlazaInput.SkipPressed()) skipped = true;
            rays.localRotation = Quaternion.Euler(0, 0, Time.unscaledTime * 8);
            float dt = Time.unscaledDeltaTime;
            Fling(shellTop, new Vector2(-1, 1.3f), -1, dt);
            Fling(shellBottom, new Vector2(1, .4f), 1, dt);
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.age += dt;
                b.v += new Vector2(0, -700) * dt;
                b.r.anchoredPosition += b.v * dt;
                b.r.localRotation = Quaternion.Euler(0, 0, b.r.localEulerAngles.z + b.spin * dt);
                b.img.color = new Color(1, 1, 1, Mathf.Clamp01((b.life - b.age) / .4f));
                if (b.age < b.life) continue;
                Destroy(b.r.gameObject);
                bits.RemoveAt(i);
            }
        }

        // short windows: the stage, the caption and the card shrink together so nothing overlaps
        void Fit()
        {
            float k = Mathf.Min(1, root.rect.height / TallEnough);
            stage.localScale = words.localScale = new Vector3(k, k, 1);
            cardRect.sizeDelta = new Vector2(Mathf.Min(CardWidth, root.rect.width - 32), cardRect.sizeDelta.y);
            float card = Mathf.Min(1, root.rect.height * .45f / Mathf.Max(1, cardRect.rect.height));
            cardRect.localScale = new Vector3(card, card, 1);
            cardRect.anchoredPosition = new Vector2(0, 28 - (GameSettings.ReduceMotion ? 0 : 30 * (1 - cardRise)));
        }

        // the shell halves tumble away and fade
        static void Fling(RectTransform shell, Vector2 direction, float turn, float dt)
        {
            if (!shell.gameObject.activeSelf) return;
            var img = shell.GetComponent<Image>();
            float a = img.color.a - dt * 1.2f;
            if (a <= 0) { shell.gameObject.SetActive(false); return; }
            img.color = new Color(1, 1, 1, a);
            shell.anchoredPosition += direction * 260 * dt + new Vector2(0, -(1 - a) * 420 * dt);
            shell.localRotation = Quaternion.Euler(0, 0, shell.localEulerAngles.z + turn * 140 * dt);
        }

        void AddBit(string emoji, float angle, float speed)
        {
            var img = UiKit.Icon(stage, emoji, Random.Range(26f, 44f));
            var r = (RectTransform)img.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(0, -10);
            bits.Add(new Bit { r = r, img = img, v = new Vector2(Mathf.Cos(angle), Mathf.Abs(Mathf.Sin(angle)) * 1.3f + .2f) * speed, spin = Random.Range(-260f, 260f), life = Random.Range(1.1f, 1.8f) });
        }

        // ---- timing helpers (skipping fast-forwards the build-up) ----

        IEnumerator Over(float seconds, System.Action<float> step, bool skippable = false)
        {
            for (float t = 0; t < seconds && !(skippable && skipped); t += Time.unscaledDeltaTime)
            {
                step(Mathf.Clamp01(t / seconds));
                yield return null;
            }
            step(1);
        }

        IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds && !skipped; t += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>Soft sun rays behind the egg.</summary>
        static Sprite Rays()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ray = Mathf.Pow(Mathf.Max(0, Mathf.Cos(Mathf.Atan2(dy, dx) * 9)), 6);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(ray * Mathf.Clamp01(1 - d) * Mathf.Clamp01(d * 4) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
        }
    }
}
