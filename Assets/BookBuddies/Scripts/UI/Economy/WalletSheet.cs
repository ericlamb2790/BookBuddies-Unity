using BookBuddies.Economy;
using BookBuddies.Net;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The coins sheet (the site's coins dialog, for Unity): your balance, the daily login gift with its 7-day track,
    /// today's town finds, and your latest coins. It opens from the HUD's coin counter and the town menu, and by
    /// itself once a day while the gift waits. It always shows the server's numbers, refreshed as it opens. Playing
    /// offline it's the offline purse, and it says when offline coins were banked into the online wallet.
    /// </summary>
    public sealed class WalletSheet : MonoBehaviour
    {
        const int TrackTile = 52;
        static readonly Color GiftLeft = Palette.Hex("#fff1c2"), GiftRight = Palette.Hex("#ffe0f0"); // the site's .giftcard

        Sheet sheet;
        string painted, notice = "";
        bool claiming;

        /// <summary>The sheet, closed, pinned under the HUD's pet card (where the coin counter is).</summary>
        public static Sheet Create(Transform parent)
        {
            var s = Sheet.Create(parent, "Coins", new Vector2(0, 1), new Vector2(Hud.Margin, -Hud.BelowPetCard), 480);
            s.gameObject.AddComponent<WalletSheet>().sheet = s;
            s.Opened = () => _ = Wallet.Refresh();
            return s;
        }

        void OnEnable()
        {
            painted = null;
            notice = "";
            Wallet.Changed += Fill;
            Fill();
        }

        void OnDisable() => Wallet.Changed -= Fill;

        // rebuilt when the numbers change (and only then, so a refresh that changes nothing doesn't flicker)
        void Fill()
        {
            var st = Wallet.State;
            string now = $"{Settings.Server}|{Settings.SignedIn}|{Wallet.Ready}|{CoinBank.Banked}|{Wallet.Coins}|{Wallet.Tickets}|{st.Day}|{st.GiftClaimed}|{st.GiftRun}|{st.UsedToday("find")}|{st.Recent.Count}|{notice}";
            if (now == painted) return;
            painted = now;
            var card = sheet.Card;
            for (int i = card.childCount - 1; i >= 0; i--)
            {
                var old = card.GetChild(i).gameObject;
                old.SetActive(false); // gone from the layout now, destroyed at the end of the frame
                Destroy(old);
            }
            sheet.First = null;

            Head(card);
            if (CoinBank.Banked > 0) UiKit.Label(card, $"Banked {CoinPill.Format(CoinBank.Banked)} offline coin{(CoinBank.Banked == 1 ? "" : "s")} to your online wallet", UiKit.SmallSize + 1, UiKit.LeafInk, UiKit.Bold);
            if (!Settings.SignedIn)
            {
                Line(card, Wallet.OfflinePurse
                    ? $"Offline coins need a buddy. Hatch your egg on the title screen (you start with {CoinPill.Format(Wallet.Economy.Starter)} coins)."
                    : $"Coins live on your account. Hatch a pet (you start with {CoinPill.Format(Wallet.Economy.Starter)} coins) or sign in from the title screen to earn and keep them.");
                return;
            }
            if (Wallet.OfflinePurse) Line(card, CoinBank.HasBank
                ? "Kept on this PC. What you find goes to your online wallet when you stop playing offline."
                : "Kept on this PC.");
            if (notice.Length > 0) UiKit.Label(card, notice, UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            if (!Wallet.Ready) Offline(card);
            else if (st.GiftClaimed) GiftDone(card, st.GiftRun);
            else GiftCard(card, st.GiftDay);
            Today(card, st);
            Recent(card, st);
        }

        // "🪙 1,250 coins" and the tickets, with the close button
        void Head(Transform card)
        {
            var head = UiKit.Node("head", card);
            UiKit.Row(head, 12);
            UiKit.Icon(head, "🪙", 44);
            var words = UiKit.Node("words", head);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            string kind = Wallet.OfflinePurse ? " offline coin" : " coin";
            string coins = Wallet.HasBalance ? CoinPill.Format(Wallet.Coins) + kind + (Wallet.Coins == 1 ? "" : "s") : Wallet.OfflinePurse ? "Offline coins" : "Coins";
            UiKit.Label(words, coins, UiKit.TitleSize, Palette.Ink, UiKit.Title);
            if (Wallet.Tickets > 0) UiKit.Label(words, $"and {CoinPill.Format(Wallet.Tickets)} Book Fair ticket{(Wallet.Tickets == 1 ? "" : "s")}", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.CloseButton(head, sheet.Close);
        }

        void Offline(Transform card)
        {
            var box = Box(card, Palette.Paper);
            if (Wallet.OfflinePurse) { Line(box, "Counting your coins…"); return; } // the purse on this PC answers straight away
            Line(box, Wallet.HasBalance
                ? "You’re offline, so this is the last balance the server sent. The daily gift and shopping come back when you reconnect."
                : "You’re offline. Your coins are safe on the server and show up here when you reconnect.");
            sheet.First = UiKit.Secondary(box, "Try again", () => _ = Wallet.Refresh(), null, 44);
        }

        // ---- the daily login gift (the site's gift card and its 7-day track) ----

        void GiftCard(Transform card, int day)
        {
            var box = Box(card, Color.white);
            box.gameObject.AddComponent<SideGradient>().Set(GiftLeft, GiftRight);
            var top = UiKit.Node("top", box);
            UiKit.Row(top, 12);
            UiKit.Icon(top, "🎁", 44);
            var words = UiKit.Node("words", top);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, "Daily login gift!", UiKit.BodySize + 2, Palette.Ink, UiKit.Bold);
            UiKit.Label(words, $"Day {day} in a row · claim +{CoinPill.Format(WalletMath.GiftAmount(Wallet.Economy, day))} coins", UiKit.SmallSize + 1, Palette.InkSoft);
            var claim = UiKit.Primary(top, claiming ? "Claiming…" : "Claim", Claim, "🪙");
            claim.interactable = !claiming;
            sheet.First = claim;
            Track(box, day);
        }

        // seven tiles: days already claimed fade, today's has the amber edge
        static void Track(Transform box, int day)
        {
            var row = UiKit.Node("track", box);
            UiKit.Row(row, 4, null, TextAnchor.MiddleCenter).childForceExpandWidth = true;
            var track = Wallet.Economy.LoginTrack;
            int today = (day - 1) % track.Length;
            for (int i = 0; i < track.Length; i++)
            {
                var tile = UiKit.Panel(row, "day " + (i + 1), Color.white.WithAlpha(i == today ? 1 : .6f), 10);
                tile.raycastTarget = false;
                UiKit.Size(tile, TrackTile, 78, 1);
                UiKit.Column(tile.rectTransform, 0, new RectOffset(2, 2, 5, 6), TextAnchor.MiddleCenter).childForceExpandWidth = false;
                if (i == today) UiKit.Outline(tile, Palette.Ember, 10, 2);
                if (i < today) tile.gameObject.AddComponent<CanvasGroup>().alpha = .5f;
                UiKit.Label(tile.transform, "Day " + (i + 1), UiKit.SmallSize, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Icon(tile.transform, i == track.Length - 1 ? "🎁" : "🪙", 22);
                UiKit.Label(tile.transform, CoinPill.Format(track[i]), UiKit.SmallSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        async void Claim()
        {
            if (claiming) return;
            claiming = true;
            painted = null;
            Fill();
            var result = await Wallet.Earn("gift");
            if (result.Outcome == WalletOutcome.Done) AutoSave.Now("gift");
            if (!this) return;
            claiming = false;
            if (result.Outcome == WalletOutcome.Done) Sound.Play("rare");
            notice = result.Outcome == WalletOutcome.Done ? "" : UiKit.SplitEmoji(result.Message, out _);
            painted = null;
            Fill();
        }

        // the site's line once today's gift is in
        void GiftDone(Transform card, int run)
        {
            var box = Box(card, Palette.Paper);
            var row = UiKit.Node("claimed", box);
            UiKit.Row(row, 12);
            UiKit.Icon(row, "🎁", 32);
            UiKit.Size(Line(row, $"Today’s gift is claimed ({run}-day login streak). Come back tomorrow for more!"), -1, -1, 1);
        }

        // ---- today and lately ----

        void Today(Transform card, WalletState st)
        {
            Heading(card, "Today");
            int found = Mathf.Min(st.UsedToday("find"), Wallet.Economy.FindsPerDay), max = Wallet.Economy.FindsPerDay;
            var row = UiKit.Node("finds", card);
            UiKit.Row(row, 12);
            UiKit.Icon(row, "🪙", 32);
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 4, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, "Town finds", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            UiKit.Label(words, found < max ? "Coins (5), coin bags (15) and gift boxes (30) turn up around Pawtopia." : "That’s every coin you can find today. More turn up tomorrow.", UiKit.SmallSize, Palette.InkSoft);
            Bar(words, (float)found / max);
            var count = UiKit.Label(row, $"{found} / {max}", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleRight);
            count.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static void Bar(Transform parent, float fill)
        {
            var track = UiKit.Panel(parent, "bar", Palette.Paper, 4);
            track.raycastTarget = false;
            UiKit.Size(track, -1, 8);
            UiKit.Outline(track, Palette.Ink.WithAlpha(.1f), 4, 1);
            var bar = UiKit.Panel(track.transform, "fill", Palette.Amber, 4).rectTransform;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(Mathf.Clamp01(fill), 1);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            UiKit.Show(bar, fill > 0);
        }

        void Recent(Transform card, WalletState st)
        {
            Heading(card, "Latest coins");
            int shown = 0;
            foreach (var e in st.Recent)
            {
                if (e.Amount == 0) continue; // Book Fair counters (a free spin) move no coins
                var row = UiKit.Node(e.Kind, card);
                UiKit.Row(row, 12);
                UiKit.Size(row, -1, 30);
                UiKit.Size(UiKit.Label(row, KindName(e.Kind), UiKit.SmallSize + 1, Palette.Ink), -1, -1, 1);
                UiKit.Label(row, DayName(e.Day, st.Day), UiKit.SmallSize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleRight).horizontalOverflow = HorizontalWrapMode.Overflow;
                var amount = UiKit.Label(row, (e.Amount > 0 ? "+" : "−") + CoinPill.Format(Mathf.Abs(e.Amount)), UiKit.SmallSize + 1, e.Amount > 0 ? UiKit.LeafInk : Palette.Ink, UiKit.Bold, TextAnchor.MiddleRight);
                UiKit.Size(amount, 76);
                shown++;
            }
            if (shown == 0) Line(card, Wallet.Ready ? "Nothing yet. Claim the daily gift, or pick up a coin in town." : "Shows up when you’re back online.");
        }

        static string KindName(string kind)
        {
            switch (kind)
            {
                case "starter": return "Welcome coins";
                case "gift": return "Daily gift";
                case "find": return "Town find";
                case "shop": return "Shopping";
                case "fairbuy": return "Book Fair tickets";
                case "admin": return "From a town admin";
                case "legacy": return "Coins from before";
                case "garden": return "Garden";
                case "bank": return "Banked from offline play";
                case "banked": return "Sent to your online wallet";
                default: return kind.Length > 0 ? char.ToUpperInvariant(kind[0]) + kind.Substring(1) : "Coins";
            }
        }

        // "Today", "Yesterday" or "6 Oct", on the server's calendar
        static string DayName(string day, string today)
        {
            if (day == today) return "Today";
            if (!System.DateTime.TryParse(day, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return day;
            if (System.DateTime.TryParse(today, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) && (t - d).TotalDays == 1) return "Yesterday";
            return d.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---- small pieces ----

        static RectTransform Box(Transform card, Color color)
        {
            var box = UiKit.Panel(card, "box", color, UiKit.CardRadius);
            box.raycastTarget = false;
            UiKit.Column(box.rectTransform, 10, new RectOffset(14, 14, 12, 14));
            UiKit.Outline(box, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            return box.rectTransform;
        }

        static void Heading(Transform card, string title) => UiKit.Label(card, title, UiKit.BodySize, UiKit.EmberInk, UiKit.Bold);

        static Text Line(Transform parent, string text) => UiKit.Label(parent, text, UiKit.SmallSize + 1, Palette.InkSoft);
    }
}
