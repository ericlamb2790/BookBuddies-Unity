using System.Collections;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Film moments over the town: black bars, captions, fades and slow camera glides between places.
    /// The camera's tilt never changes, so pets and props always face it; shots only move where it looks,
    /// how far away it is (perspective) and how much it shows (zoom). Esc, Space, Enter, A, B, Start or the
    /// Skip button skips.
    /// </summary>
    public sealed class Cinema : MonoBehaviour
    {
        /// <summary>One camera move: from one map point to another over a few seconds, with a caption.</summary>
        public struct Shot
        {
            public Vector2 From, To;
            public float Zoom, ZoomTo, Distance, DistanceTo, Seconds;
            public string Caption;

            public Shot(Vector2 from, Vector2 to, float zoom, float distance, float seconds, string caption)
            {
                From = from; To = to; Zoom = ZoomTo = zoom; Distance = DistanceTo = distance; Seconds = seconds; Caption = caption;
            }
        }

        // The intro: a slow tour of Pawtopia. Edit the places, framing or words here.
        static readonly Shot[] IntroShots =
        {
            new Shot(new Vector2(10, 57), new Vector2(27, 57), 1.7f, 16, 6.5f, "In a little town by the sea…"),
            new Shot(new Vector2(59, 47), new Vector2(70, 46), 1.3f, 12, 6.5f, "…every page you read helps a little buddy grow."),
            new Shot(new Vector2(8, 31), new Vector2(18, 29), 1.5f, 14, 5.5f, "There are games to play,"),
            new Shot(new Vector2(35, 35), new Vector2(43, 32), 1.2f, 11, 5.5f, "friends to meet,"),
            new Shot(new Vector2(33, 16), new Vector2(34, 11), 1.4f, 13, 5.5f, "and tales to tell together."),
        };

        const float BarShare = .11f; // each black bar is this much of the screen's height

        TownCamera cam;
        Canvas canvas;
        RectTransform top, bottom, card;
        Text caption, cardTitle, cardSub;
        CanvasGroup captionFade, cardFade;
        Image black, vignette;
        float bars;
        Coroutine swapping;
        public bool Skipped { get; private set; }

        public static Cinema Create(TownCamera cam, bool skippable)
        {
            var canvas = UiKit.MakeCanvas("Cinema", 45);
            var c = canvas.gameObject.AddComponent<Cinema>();
            c.cam = cam;
            c.canvas = canvas;
            c.Build((RectTransform)canvas.transform, skippable);
            return c;
        }

        void Build(RectTransform root, bool skippable)
        {
            vignette = UiKit.Cover(root, "vignette", new Color(.16f, .1f, .05f, 0), UiKit.Vignette);
            top = Bar(root, 1);
            bottom = Bar(root, 0);

            caption = UiKit.Label(bottom, "", 26, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter);
            ((RectTransform)caption.transform).Fill(8);
            captionFade = caption.gameObject.AddComponent<CanvasGroup>();
            captionFade.alpha = 0;

            // the title card at the end of the intro
            var glow = UiKit.Cover(root, "title glow", Palette.Amber.WithAlpha(.28f), UiKit.Glow);
            card = glow.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(760, 360));
            cardTitle = UiKit.Label(card, "BookBuddies", 88, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter);
            ((RectTransform)cardTitle.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(760, 110));
            cardTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lift = cardTitle.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            lift.effectColor = new Color(.16f, .09f, .03f, .55f);
            lift.effectDistance = new Vector2(0, -4);
            cardSub = UiKit.Label(card, "Welcome to Pawtopia", 24, Palette.Amber, UiKit.Bold, TextAnchor.MiddleCenter);
            ((RectTransform)cardSub.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -52), new Vector2(760, 40));
            cardSub.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = new Color(.16f, .09f, .03f, .5f);
            cardFade = card.gameObject.AddComponent<CanvasGroup>();
            cardFade.alpha = 0;

            black = UiKit.Cover(root, "fade", Color.black);
            black.color = Color.clear;

            if (skippable)
            {
                var skip = UiKit.Button(top, "Skip", Color.clear, () => Skipped = true, 8);
                ((RectTransform)skip.transform).Pin(new Vector2(1, .5f), new Vector2(-18, 0), new Vector2(96, 34));
                var label = UiKit.Label(skip.transform, "Skip  ›", 16, Palette.Cream.WithAlpha(.8f), UiKit.Bold, TextAnchor.MiddleRight);
                ((RectTransform)label.transform).Fill();
            }
        }

        static RectTransform Bar(RectTransform root, float edge)
        {
            var bar = UiKit.Panel(root, edge > 0 ? "top bar" : "bottom bar", Color.black, 0).rectTransform;
            bar.anchorMin = new Vector2(0, edge); bar.anchorMax = new Vector2(1, edge); bar.pivot = new Vector2(.5f, edge);
            bar.sizeDelta = Vector2.zero;
            return bar;
        }

        void Update()
        {
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            if (PlazaInput.SkipPressed()) Skipped = true;
            float h = ((RectTransform)canvas.transform).rect.height * BarShare * bars;
            top.sizeDelta = bottom.sizeDelta = new Vector2(0, h);
            vignette.color = vignette.color.WithAlpha(.5f * bars);
            cardTitle.fontSize = (int)Mathf.Min(88, ((RectTransform)canvas.transform).rect.width / 8.5f);
        }

        // ---- building blocks ----

        public IEnumerator Bars(bool on, float seconds = .6f)
        {
            float from = bars, to = on ? 1 : 0;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) { bars = Mathf.Lerp(from, to, UiKit.Ease(t / seconds)); yield return null; }
            bars = to;
        }

        public IEnumerator Fade(float toBlack, float seconds)
        {
            float from = black.color.a;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) { black.color = new Color(0, 0, 0, Mathf.Lerp(from, toBlack, UiKit.Ease(t / seconds))); yield return null; }
            black.color = new Color(0, 0, 0, toBlack);
        }

        public void SetBlack(float alpha) => black.color = new Color(0, 0, 0, alpha);

        /// <summary>Glides the camera through a shot. With dip, it fades up from black at the start and down at the end.</summary>
        public IEnumerator Play(Shot s, bool dip)
        {
            const float Dip = .7f;
            Caption(s.Caption);
            for (float t = 0; t < s.Seconds && !Skipped; t += Time.unscaledDeltaTime)
            {
                float k = UiKit.Ease(t / s.Seconds), drift = t / s.Seconds; // a gentle constant drift reads more like film than an ease
                cam.Focus = Vector2.Lerp(s.From, s.To, Mathf.Lerp(drift, k, .35f));
                cam.zoom = Mathf.Lerp(s.Zoom, s.ZoomTo, k);
                cam.distance = Mathf.Lerp(s.Distance, s.DistanceTo, k);
                cam.Place();
                if (dip) SetBlack(Mathf.Max(1 - t / Dip, (t - (s.Seconds - Dip)) / Dip));
                yield return null;
            }
        }

        public void Caption(string text)
        {
            if (swapping != null) StopCoroutine(swapping);
            swapping = StartCoroutine(SwapCaption(text ?? ""));
        }

        IEnumerator SwapCaption(string text)
        {
            while (captionFade.alpha > 0) { captionFade.alpha -= Time.unscaledDeltaTime * 4; yield return null; }
            caption.text = text;
            if (text.Length == 0) yield break;
            yield return new WaitForSecondsRealtime(.35f);
            while (captionFade.alpha < 1) { captionFade.alpha += Time.unscaledDeltaTime * 1.6f; yield return null; }
        }

        // ---- the intro (first launch, or Watch the intro on the title screen) ----

        public static IEnumerator Intro(TownCamera cam)
        {
            var c = Create(cam, true);
            cam.Directed = true;
            cam.petBelowCentre = 0;
            Sound.Music("home");
            c.SetBlack(1);
            c.bars = 1;
            foreach (var shot in IntroShots)
            {
                yield return c.Play(shot, true);
                if (c.Skipped) break;
            }

            // finale: rise high over the whole town while the name comes up
            c.Caption("");
            if (!c.Skipped)
            {
                var rise = new Shot(new Vector2(40, 34), new Vector2(39, 31), 1.3f, 12, 6.5f, "") { ZoomTo = 3.3f, DistanceTo = 28 };
                c.StartCoroutine(c.TitleCard(.9f));
                yield return c.Play(rise, false);
            }
            if (c.Skipped)
            {
                yield return c.Fade(1, .35f);
                c.cardFade.alpha = 0;
            }
            GameSettings.IntroSeen = true;
            c.StartCoroutine(c.FinishIntro());
        }

        IEnumerator TitleCard(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            SetBlack(0);
            Sound.Play("rare");
            for (float t = 0; t < 1.6f && !Skipped; t += Time.unscaledDeltaTime)
            {
                cardFade.alpha = UiKit.EaseOut(t / 1.6f);
                card.localScale = Vector3.one * (.94f + .06f * UiKit.EaseOut(t / 1.6f));
                yield return null;
            }
        }

        // hands over to the title screen: the card and bars slip away and the picture clears
        IEnumerator FinishIntro()
        {
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                float k = UiKit.Ease(t / 1.2f);
                cardFade.alpha = Mathf.Min(cardFade.alpha, 1 - k);
                bars = 1 - k;
                SetBlack(Mathf.Min(black.color.a, 1 - k));
                yield return null;
            }
            cam.Release();
            Destroy(gameObject);
        }

        // ---- arriving in town: the camera cranes down from high above to your pet ----

        public static IEnumerator Arrival(TownCamera cam, PetActor me)
        {
            float zoom = cam.PlayerZoom, distance = cam.BaseDistance;
            var c = Create(cam, true);
            PlazaInput.Locked = true;
            cam.Directed = true;
            bool full = GameSettings.Cinematics && !GameSettings.ReduceMotion;

            if (full)
            {
                c.bars = 1;
                Sound.Play("whoosh", .8f);
                var start = me.Pos + new Vector2(-4, -10);
                const float Seconds = 3.6f;
                for (float t = 0; t < Seconds && !c.Skipped; t += Time.unscaledDeltaTime)
                {
                    float k = UiKit.Ease(t / Seconds);
                    cam.Focus = Vector2.Lerp(start, me.Pos, k);
                    cam.zoom = Mathf.Lerp(3f, zoom, k);
                    cam.distance = Mathf.Lerp(30, distance, k);
                    cam.Place();
                    yield return null;
                }
            }
            cam.Focus = me.Pos;
            cam.Release();
            me.Play("happy", 1.3f);
            Sound.Play("happy");
            Fx.Float("✨", me.Pos + new Vector2(0, -.4f), 0, 1.4f);
            PlazaInput.Locked = false;
            yield return c.Bars(false, full ? .6f : .01f);
            Destroy(c.gameObject);
        }
    }
}
