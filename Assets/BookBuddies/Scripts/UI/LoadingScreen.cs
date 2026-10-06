using System.Collections;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The loading screen: your pet (or your egg) bouncing on a stack of books, sparkles popping around it,
    /// a row of paw prints that fill in as things load, and a tip that changes every few seconds.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        static readonly string[] Tips =
        {
            "Tap another pet to hug, boop or play fetch.",
            "Coins, coin bags and gift boxes turn up around town. Whoever reaches them first keeps them!",
            "Tap any bench, chair or swing and your buddy will sit down.",
            "Emotes and tricks are on the two buttons next to the chat box.",
            "Press M for the town map, or tap the little map in the corner.",
            "Press H to hide the HUD, then P to take a photo of your buddy.",
            "Keep your recovery code somewhere safe. It brings your buddy back on any device.",
            "Be kind in chat, and never share your real name, school or address.",
            "The villagers love a hug. Try one with Mayor Biscuit!",
            "Leave your buddy alone for a while and it might take a little nap.",
        };
        const int PawCount = 10;
        const float BounceSeconds = .9f;

        Canvas canvas;
        CanvasGroup group, tipFade;
        RectTransform hopper, shadow;
        Image[] paws, sparkles, bokeh;
        Text stage, tip;
        bool egg;
        float progress, shownProgress, tipAt, sparkleAt;
        int tipIndex, nextSparkle;

        public static LoadingScreen Show(string look, string stageText)
        {
            var canvas = UiKit.MakeCanvas("Loading", 50);
            var l = canvas.gameObject.AddComponent<LoadingScreen>();
            l.canvas = canvas;
            l.egg = look != null && look.Contains("\"s\":0");
            l.Build((RectTransform)canvas.transform, look);
            l.Stage(stageText);
            l.tipIndex = Random.Range(0, Tips.Length);
            l.tip.text = Tips[l.tipIndex];
            l.tipAt = Time.unscaledTime;
            return l;
        }

        void Build(RectTransform root, string look)
        {
            group = root.gameObject.AddComponent<CanvasGroup>();
            UiKit.Cover(root, "paper", Palette.Cream, null, true);

            // big soft lights drifting behind everything
            bokeh = new Image[5];
            for (int i = 0; i < bokeh.Length; i++)
            {
                bokeh[i] = UiKit.Cover(root, "light", (i % 2 == 0 ? Palette.Amber : Palette.Rose).WithAlpha(i % 2 == 0 ? .2f : .1f), UiKit.Glow);
                bokeh[i].rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, Vector2.one * (360 + i * 90));
            }
            UiKit.Cover(root, "edges", new Color(.6f, .42f, .2f, .22f), UiKit.Vignette);

            var middle = UiKit.Node("middle", root).Pin(new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(300, 300));
            var books = UiKit.Icon(middle, "📚", 120);
            ((RectTransform)books.transform).Pin(new Vector2(.5f, 0), new Vector2(0, 0), new Vector2(120, 120));
            shadow = UiKit.Cover(middle, "shadow", Palette.Ink.WithAlpha(.28f), UiKit.Glow).rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 92), new Vector2(110, 26));
            hopper = UiKit.Node("pet", middle).Pin(new Vector2(.5f, 0), new Vector2(0, 98), new Vector2(150, 174));
            hopper.pivot = new Vector2(.5f, 0);
            var pet = hopper.gameObject.AddComponent<Image>();
            pet.sprite = PetSprites.For(look);
            pet.preserveAspect = true;
            pet.raycastTarget = false;

            sparkles = new Image[6];
            for (int i = 0; i < sparkles.Length; i++)
            {
                sparkles[i] = UiKit.Icon(middle, "✨", 30);
                sparkles[i].color = Color.clear;
                ((RectTransform)sparkles[i].transform).anchorMin = ((RectTransform)sparkles[i].transform).anchorMax = new Vector2(.5f, .5f);
            }

            stage = UiKit.Label(root, "", 28, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            ((RectTransform)stage.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -150), new Vector2(640, 44));

            var row = UiKit.Node("paws", root).Pin(new Vector2(.5f, .5f), new Vector2(0, -200), new Vector2(PawCount * 34, 30));
            UiKit.Row(row, 8, null, TextAnchor.MiddleCenter);
            paws = new Image[PawCount];
            for (int i = 0; i < PawCount; i++) paws[i] = UiKit.Icon(row, "🐾", 26);

            tip = UiKit.Label(root, "", 17, Palette.InkSoft, UiKit.Body, TextAnchor.UpperCenter);
            ((RectTransform)tip.transform).Pin(new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(560, 60));
            tipFade = tip.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>How far along loading is, from 0 to 1. The paws catch up smoothly.</summary>
        public void Progress(float p) => progress = Mathf.Max(progress, Mathf.Clamp01(p));

        public void Stage(string text) => stage.text = text ?? "";

        /// <summary>Fills the last paws, fades away and removes itself.</summary>
        public IEnumerator Hide()
        {
            Progress(1);
            while (shownProgress < .99f) yield return null;
            for (float t = 0; t < .45f; t += Time.unscaledDeltaTime) { group.alpha = 1 - UiKit.Ease(t / .45f); yield return null; }
            Destroy(gameObject);
        }

        void Update()
        {
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            float now = Time.unscaledTime;
            shownProgress = Mathf.MoveTowards(shownProgress, progress, Time.unscaledDeltaTime * 1.6f);
            Bounce(now);
            Paws(now);
            Sparkle(now);
            Lights(now);
            Tip(now);
        }

        // a hop with squash on landing and a stretch at the top; eggs wobble as they go
        void Bounce(float now)
        {
            float phase = now / BounceSeconds % 1, up = Mathf.Sin(phase * Mathf.PI);
            float squash = phase < .12f ? 1 - phase / .12f : phase > .9f ? (phase - .9f) / .1f : 0;
            hopper.anchoredPosition = new Vector2(0, 98 + up * 70);
            hopper.localScale = new Vector3(1 + squash * .12f - up * .03f, 1 - squash * .14f + up * .05f, 1);
            hopper.localRotation = Quaternion.Euler(0, 0, egg ? Mathf.Sin(now * 9) * 7 : Mathf.Sin(phase * Mathf.PI * 2) * 4);
            shadow.localScale = new Vector3(1 - up * .45f, 1 - up * .3f, 1);
        }

        void Paws(float now)
        {
            float lit = shownProgress * PawCount;
            for (int i = 0; i < PawCount; i++)
            {
                float on = Mathf.Clamp01(lit - i);
                paws[i].color = new Color(1, 1, 1, .18f + .82f * on);
                bool next = i == Mathf.FloorToInt(lit) && shownProgress < 1;
                paws[i].transform.localScale = Vector3.one * (next ? 1 + .14f * Mathf.Sin(now * 8) : .9f + .1f * on);
                paws[i].transform.localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 10 : -10);
            }
        }

        void Sparkle(float now)
        {
            if (now > sparkleAt)
            {
                sparkleAt = now + .32f;
                var s = sparkles[nextSparkle++ % sparkles.Length];
                float angle = Random.Range(20f, 160f) * Mathf.Deg2Rad, r = Random.Range(95f, 150f);
                ((RectTransform)s.transform).anchoredPosition = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * .8f);
                s.GetComponent<RectTransform>().localEulerAngles = new Vector3(0, 0, now % 1 * 360);
                s.transform.localScale = Vector3.zero;
            }
            foreach (var s in sparkles)
            {
                float k = s.transform.localScale.x < 1 ? s.transform.localScale.x + Time.unscaledDeltaTime * 1.4f : 1;
                s.transform.localScale = Vector3.one * Mathf.Min(k, 1);
                s.color = new Color(1, 1, 1, Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI));
            }
        }

        void Lights(float now)
        {
            for (int i = 0; i < bokeh.Length; i++)
            {
                float a = now * (.05f + i * .013f) + i * 2.1f;
                bokeh[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * (180 + i * 70), Mathf.Sin(a * 1.3f) * (120 + i * 30));
            }
        }

        void Tip(float now)
        {
            float age = now - tipAt;
            if (age > 5f)
            {
                tipIndex = (tipIndex + 1) % Tips.Length;
                tip.text = Tips[tipIndex];
                tipAt = now;
                age = 0;
            }
            tipFade.alpha = age < .4f ? age / .4f : age > 4.6f ? (5f - age) / .4f : 1;
        }
    }
}
