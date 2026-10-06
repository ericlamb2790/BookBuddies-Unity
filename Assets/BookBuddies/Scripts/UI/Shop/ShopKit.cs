using System;
using BookBuddies.Economy;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// What the shops share (the town bookshops and the hero card's Moves, Class and Library tabs): paying through the
    /// Wallet with one clear line when it can't, a second ask for pricey things, price buttons, the "New" tag, sealed
    /// cards that flip over when revealed, and the message pill.
    /// </summary>
    public static class ShopKit
    {
        /// <summary>Things that cost this much or more ask once more before buying.</summary>
        public const int ConfirmAt = 400;

        static bool paying;

        /// <summary>
        /// Buys a shop item (the server prices it; see WalletMath.Price). Pricey items ask first. "paid" runs once it's
        /// yours (bought now, or before on any device); otherwise "say" gets the line to show, with how many more coins
        /// it takes when that's the problem. "reff" is the receipt for things each pet buys once (moves).
        /// </summary>
        public static void Buy(string item, string reff, string icon, string what, Action<WalletResult> paid, Action<string> say)
        {
            int price = Wallet.Price(item);
            if (paying || price <= 0) return;
            if (Wallet.Ready && Wallet.Coins < price) { Sound.Play("boop"); say(Short(price)); return; }
            if (price < ConfirmAt) { Pay(item, reff, price, paid, say); return; }
            ConfirmCard.Open(icon, $"Buy {what}?", $"It costs {CoinPill.Format(price)} coins. You have {CoinPill.Format(Wallet.Coins)}.",
                $"Buy for {CoinPill.Format(price)}", () => Pay(item, reff, price, paid, say));
        }

        static async void Pay(string item, string reff, int price, Action<WalletResult> paid, Action<string> say)
        {
            paying = true;
            var r = await Wallet.Spend("shop", item, reff);
            paying = false;
            if (r.Ok) { Sound.Play("coin"); paid(r); }
            else say(r.Outcome == WalletOutcome.Short ? Short(price) : r.Message);
        }

        /// <summary>"Not enough coins yet: 120 more to go."</summary>
        public static string Short(int price) =>
            $"Not enough coins yet: {CoinPill.Format(Mathf.Max(1, price - Wallet.Coins))} more to go. Reading earns coins.";

        /// <summary>A buy button: an optional verb, then 🪙 and the price. Amber when you can afford it, quiet paper when not (it still says why).</summary>
        public static Button PriceButton(Transform parent, int price, Action onClick, string verb = null)
        {
            bool can = !Wallet.Ready || Wallet.Coins >= price;
            var b = UiKit.Button(parent, "Buy", can ? Palette.Amber : Palette.Paper, onClick, 22);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 6, new RectOffset(18, 18, 0, 0), TextAnchor.MiddleCenter);
            if (verb != null) Words(r, verb + " ·");
            UiKit.Icon(r, "🪙", 22);
            Words(r, CoinPill.Format(price));
            UiKit.Size(b, -1, 44);
            if (!can) UiKit.Outline(b, Palette.Ink.WithAlpha(.16f), 22);
            return b;
        }

        static void Words(Transform parent, string text) =>
            UiKit.Label(parent, text, UiKit.BodySize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;

        /// <summary>The rose "New" tag on something just unlocked.</summary>
        public static Image NewTag(Transform parent) => UiKit.Badge(parent, "New", UiKit.RoseInk);

        /// <summary>A small rose dot in a corner (a tab with something new on it).</summary>
        public static Image Dot(Transform parent)
        {
            var dot = UiKit.Panel(parent, "new", Palette.Rose, 7);
            dot.raycastTarget = false;
            dot.rectTransform.Pin(new Vector2(1, 1), new Vector2(-8, -6), new Vector2(14, 14));
            UiKit.Size(dot).ignoreLayout = true;
            UiKit.Outline(dot, Palette.Cream, 7, 2);
            return dot;
        }

        /// <summary>
        /// The reveal moment: covers a card with its sealed face (a "?" on ink) that flips away after "delay" seconds,
        /// showing the card under it. Reduce motion shows it straight away.
        /// </summary>
        public static void Reveal(RectTransform card, float delay)
        {
            if (GameSettings.ReduceMotion) return;
            var seal = UiKit.Panel(card, "sealed", Palette.Ink, UiKit.CardRadius);
            UiKit.Size(seal).ignoreLayout = true;
            seal.rectTransform.Fill();
            UiKit.Label(seal.transform, "?", 34, Palette.Amber, UiKit.Title, TextAnchor.MiddleCenter).rectTransform.Fill();
            card.gameObject.AddComponent<CardFlip>().Begin(seal.gameObject, delay);
        }

        /// <summary>A card turning over: it narrows to an edge, the sealed face drops away, and it widens again.</summary>
        sealed class CardFlip : MonoBehaviour
        {
            const float Half = .17f;
            GameObject seal;
            float start;

            public void Begin(GameObject face, float delay)
            {
                seal = face;
                start = Time.unscaledTime + delay;
            }

            void Update()
            {
                float t = (Time.unscaledTime - start) / Half;
                if (t < 0) return;
                if (seal && t >= 1) { Destroy(seal); Sound.Play("rare"); }
                float x = t < 1 ? 1 - UiKit.Ease(t) : UiKit.EaseBack(t - 1);
                transform.localScale = new Vector3(Mathf.Max(.02f, x), 1, 1);
                if (t >= 2) { transform.localScale = Vector3.one; Destroy(this); }
            }
        }
    }

    /// <summary>A short line in an ink pill at the bottom of a card (its first emoji drawn as an icon); it fades after a moment.</summary>
    public sealed class FlashPill : MonoBehaviour
    {
        Text text;
        Image icon;
        CanvasGroup group;
        float at = -99;

        public static FlashPill Create(RectTransform card)
        {
            var pill = UiKit.Panel(card, "flash", Palette.Ink, 20);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(10, 40));
            UiKit.Size(pill).ignoreLayout = true;
            UiKit.Row(r, 8, new RectOffset(16, 18, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Hug(r, true, false);
            var f = pill.gameObject.AddComponent<FlashPill>();
            f.icon = UiKit.Icon(r, null, 22);
            f.text = UiKit.Label(r, "", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold);
            f.text.horizontalOverflow = HorizontalWrapMode.Overflow;
            f.group = pill.gameObject.AddComponent<CanvasGroup>();
            f.group.alpha = 0;
            f.group.blocksRaycasts = false;
            return f;
        }

        public void Show(string line)
        {
            text.text = UiKit.SplitEmoji(line, out var emoji);
            UiKit.SetIcon(icon, emoji);
            UiKit.Show(icon, icon.enabled);
            transform.SetAsLastSibling();
            at = Time.unscaledTime;
        }

        void Update() => group.alpha = Mathf.Clamp01(2.6f - (Time.unscaledTime - at) * 1.2f);
    }

    /// <summary>"Buy the Crystal ball?": a small card over a shop that asks once more. Its button agrees; B or the other button backs out.</summary>
    public sealed class ConfirmCard : TalesScreen
    {
        /// <summary>Asks; "yes" runs after the card closes when the player agrees.</summary>
        public static void Open(string icon, string title, string body, string ok, Action yes)
        {
            var c = Create<ConfirmCard>("Confirm", true);
            c.MaxSize = new Vector2(640, 380);
            c.Build(icon, title, body, ok, yes);
        }

        void Build(string icon, string title, string body, string ok, Action yes)
        {
            var col = UiKit.Node("content", Card).Fill(28);
            UiKit.Column(col, 12, null, TextAnchor.MiddleCenter).childForceExpandWidth = true;
            if (!string.IsNullOrEmpty(icon))
            {
                var holder = UiKit.Node("icon", col);
                UiKit.Size(holder, -1, 64);
                UiKit.Icon(holder, icon, 60).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(60, 60));
            }
            UiKit.Label(col, title, UiKit.TitleSize - 4, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            UiKit.Label(col, body, UiKit.BodySize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            UiKit.Spacer(col, 6, 0);
            var row = UiKit.Node("buttons", col);
            UiKit.Row(row, 12, null, TextAnchor.MiddleCenter);
            UiKit.Secondary(row, "Not now", Close);
            var go = UiKit.Primary(row, ok, () => { Close(); yes(); });
            UiKit.PadHints(Card, ("A", "Select"), ("B", "Back"));
            VirtualCursor.FocusFirst(go);
        }

        protected override void Layout(Vector2 card) { }
    }
}
