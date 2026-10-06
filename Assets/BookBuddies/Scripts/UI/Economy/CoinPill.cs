using System.Globalization;
using BookBuddies.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The coin counter on the HUD's pet card: 🪙 and your balance on the site's dark pill. The number counts up or
    /// down to each new balance, and coins that arrive pop out under it ("+15 🪙 / town find", the site's coin pop).
    /// A gift wiggles beside it while today's gift waits, and it says "offline" when the number is only the last one
    /// the server sent. Clicking it (mouse, touch, or A with the cursor) opens the coins sheet.
    /// </summary>
    public sealed class CoinPill : MonoBehaviour
    {
        const float CountSeconds = .6f, BumpSeconds = .35f;
        static readonly Color Gold = Palette.Hex("#ffe7a3");      // the site's .pwcoins
        static readonly Color PopInk = Palette.Hex("#3b2a00");    // the site's .coinpop

        Text number, offline;
        Image coin, gift;
        float shown, from, to, countAt = -99, bumpAt = -99;

        /// <summary>Adds the counter to a row of HUD buttons; "open" runs when it's clicked.</summary>
        public static CoinPill Create(Transform row, System.Action open)
        {
            var b = UiKit.Button(row, "Coins", Palette.Ink.WithAlpha(.8f), open, 24);
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // like the other HUD buttons: the cursor reaches it, the d-pad doesn't
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 8, new RectOffset(12, 18, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Size(b, -1, 48);
            UiKit.Shadow(r, 24, 12, 4, .22f);
            var pill = b.gameObject.AddComponent<CoinPill>();
            pill.coin = UiKit.Icon(r, "🪙", 28);
            pill.number = UiKit.Label(r, "—", UiKit.BodySize + 2, Gold, UiKit.Bold);
            pill.number.horizontalOverflow = HorizontalWrapMode.Overflow;
            pill.offline = UiKit.Label(r, "offline", UiKit.SmallSize, Palette.Cream.WithAlpha(.75f));
            pill.offline.horizontalOverflow = HorizontalWrapMode.Overflow;
            pill.gift = UiKit.Icon(r, "🎁", 26);
            pill.Paint();
            pill.shown = pill.from = pill.to;
            return pill;
        }

        /// <summary>"1,250": coins the way the site writes them.</summary>
        public static string Format(int coins) => coins.ToString("N0", CultureInfo.InvariantCulture);

        void OnEnable()
        {
            Wallet.Changed += Paint;
            Wallet.Popped += Pop;
        }

        void OnDisable()
        {
            Wallet.Changed -= Paint;
            Wallet.Popped -= Pop;
        }

        // a new balance: count from what's shown now to it, with a swell when it went up
        void Paint()
        {
            if (!number) return;
            from = shown;
            to = Wallet.Coins;
            countAt = Time.unscaledTime;
            if (to > from) bumpAt = Time.unscaledTime;
            UiKit.Show(offline, Settings.SignedIn && !Wallet.Ready);
            UiKit.Show(gift, Wallet.Ready && !Wallet.State.GiftClaimed);
        }

        void Update()
        {
            bool calm = GameSettings.ReduceMotion;
            float t = (Time.unscaledTime - countAt) / CountSeconds;
            shown = t >= 1 || calm ? to : Mathf.Lerp(from, to, UiKit.EaseOut(t));
            string text = Wallet.HasBalance ? Format(Mathf.RoundToInt(shown)) : "—";
            if (number.text != text) number.text = text;

            float bump = (Time.unscaledTime - bumpAt) / BumpSeconds;
            float swell = bump < 1 && !calm ? 1 + .22f * Mathf.Sin(bump * Mathf.PI) : 1;
            coin.transform.localScale = new Vector3(swell, swell, 1);

            // the site's giftwig: still most of the time, then a quick wiggle every 2.4 seconds
            float w = Mathf.Repeat(Time.unscaledTime / 2.4f, 1);
            float angle = calm || w < .88f ? 0 : Mathf.Sin((w - .88f) / .12f * Mathf.PI * 3) * 9;
            gift.transform.localRotation = Quaternion.Euler(0, 0, angle);
        }

        // ---- the coin pop: "+n 🪙" with why under it, in the site's gold pill, popping out under the counter ----

        void Pop(int coins, string why)
        {
            if (!isActiveAndEnabled) return;
            var box = UiKit.Node("coin pop", transform);
            box.anchorMin = box.anchorMax = new Vector2(.5f, 0);
            box.pivot = new Vector2(.5f, 1);
            UiKit.Size(box).ignoreLayout = true;
            var above = box.gameObject.AddComponent<Canvas>(); // over the HUD's sheets too (the coins sheet opens right under the counter)
            above.overrideSorting = true;
            above.sortingOrder = GetComponentInParent<Canvas>().sortingOrder + 1;
            UiKit.Column(box, 0);
            UiKit.Hug(box);
            var pill = UiKit.Panel(box, "pill", Color.white, 30);
            pill.raycastTarget = false;
            pill.gameObject.AddComponent<SideGradient>().Set(Palette.Hex("#ffe07a"), Palette.Hex("#ffb938"));
            var r = pill.rectTransform;
            UiKit.Column(r, 0, new RectOffset(22, 22, 8, 10), TextAnchor.MiddleCenter);
            UiKit.Shadow(r, 30, 16, 6, .2f);
            var line = UiKit.Node("amount", r);
            UiKit.Row(line, 6, null, TextAnchor.MiddleCenter);
            UiKit.Label(line, "+" + Format(coins), UiKit.HeadingSize, PopInk, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Icon(line, "🪙", 24);
            if (!string.IsNullOrEmpty(why))
                UiKit.Label(r, why, UiKit.SmallSize, PopInk.WithAlpha(.8f), UiKit.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            box.gameObject.AddComponent<PopMotion>().Burst = coins >= 100;
        }

        /// <summary>
        /// The site's cpop (1.9 s): grows from .4 to 1.1 and settles, holds, then rises 40 pixels and fades. Big rewards
        /// throw a few coins too. Reduce motion keeps only the fade.
        /// </summary>
        sealed class PopMotion : MonoBehaviour
        {
            const float Seconds = 1.9f, Below = -10;
            public bool Burst;
            CanvasGroup group;
            RectTransform r;
            RectTransform[] coins;
            Vector2[] flight;
            float bornAt;

            void Start()
            {
                r = (RectTransform)transform;
                group = gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                bornAt = Time.unscaledTime;
                if (Burst && !GameSettings.ReduceMotion) ThrowCoins();
                Step();
            }

            void Update() => Step();

            void Step()
            {
                float p = (Time.unscaledTime - bornAt) / Seconds;
                if (p >= 1) { Destroy(gameObject); return; }
                bool calm = GameSettings.ReduceMotion;
                float scale = calm ? 1 : p < .15f ? Mathf.Lerp(.4f, 1.1f, p / .15f) : p < .25f ? Mathf.Lerp(1.1f, 1, (p - .15f) / .1f) : 1;
                float y = calm ? 0 : p < .15f ? Mathf.Lerp(-20, 0, p / .15f) : p > .8f ? (p - .8f) / .2f * 40 : 0;
                group.alpha = p < .15f ? p / .15f : p > .8f ? 1 - (p - .8f) / .2f : 1;
                r.localScale = new Vector3(scale, scale, 1);
                r.anchoredPosition = new Vector2(0, Below + y);
                if (coins != null) Fly(p * Seconds);
            }

            void ThrowCoins()
            {
                coins = new RectTransform[8];
                flight = new Vector2[coins.Length];
                for (int i = 0; i < coins.Length; i++)
                {
                    var c = UiKit.Icon(transform, "🪙", 22);
                    UiKit.Size(c).ignoreLayout = true;
                    coins[i] = c.rectTransform;
                    coins[i].anchorMin = coins[i].anchorMax = new Vector2(.5f, .5f);
                    float a = Mathf.PI * (.15f + .7f * i / (coins.Length - 1)) + Random.Range(-.12f, .12f);
                    flight[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(220f, 320f);
                }
            }

            // coins fly out, fall and fade within the first second
            void Fly(float age)
            {
                for (int i = 0; i < coins.Length; i++)
                {
                    coins[i].anchoredPosition = flight[i] * age + new Vector2(0, -420) * age * age;
                    coins[i].localRotation = Quaternion.Euler(0, 0, flight[i].x * age);
                    coins[i].GetComponent<Image>().color = new Color(1, 1, 1, Mathf.Clamp01(1.1f - age));
                }
            }
        }
    }

    /// <summary>Colours a UI graphic from left to right (the site's gold coin pop and gift card are gradients).</summary>
    public sealed class SideGradient : BaseMeshEffect
    {
        Color left = Color.white, right = Color.white;

        public void Set(Color from, Color to)
        {
            left = from;
            right = to;
            if (graphic) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            var rect = graphic.rectTransform.rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.color = Color.Lerp(left, right, Mathf.InverseLerp(rect.xMin, rect.xMax, v.position.x)) * graphic.color;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
