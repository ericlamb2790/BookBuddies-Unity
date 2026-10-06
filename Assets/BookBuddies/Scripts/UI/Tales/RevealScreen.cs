using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// "You found the Bramble Stash!": new gear turns over one card at a time in front of a glow in the best rarity's
    /// colour, each with its power against what's worn and whether it went in the bag or became dust.
    /// Nice! (A) closes it; Open bag goes straight to the first new piece.
    /// </summary>
    public sealed class RevealScreen : TalesScreen
    {
        const float FirstFlip = .3f, FlipEvery = .18f, FlipTime = .34f, MaxCardWidth = 300, Gap = 24;

        readonly List<GearItem> items = new List<GearItem>();
        readonly List<(RectTransform card, CanvasGroup fade, Image pillar)> cards = new List<(RectTransform, CanvasGroup, Image)>();
        RectTransform row;
        Image glow;
        System.Action closed;
        float openedAt;
        int flipped;

        /// <summary>Shows the items (item strings; unknown ones are skipped). Runs closed when it goes away, or at once when there's nothing to show.</summary>
        public static void Open(IList<string> list, string title, System.Action closed)
        {
            var found = new List<GearItem>();
            if (list != null) foreach (var s in list) { var it = Loot.Get(s); if (it != null) found.Add(it); }
            if (found.Count == 0) { closed?.Invoke(); return; }
            var r = Create<RevealScreen>("Loot reveal", true, false);
            r.items.AddRange(found);
            r.closed = closed;
            r.MaxSize = new Vector2(1600, 900);
            r.Build(string.IsNullOrEmpty(title) ? "You found something!" : title);
        }

        void Build(string title)
        {
            var save = TalesSave.Current;
            int top = 0;
            foreach (var it in items) top = Mathf.Max(top, it.Tier);
            glow = GearUi.Glow(Card, TalesUi.RarityColor(top), 1000, new Vector2(0, 20));
            glow.color = glow.color.WithAlpha(top >= 2 ? .5f : .3f);

            var t = UiKit.Label(Card, title, 40, Palette.Cream, UiKit.Title, TextAnchor.MiddleCenter);
            ((RectTransform)t.transform).Pin(new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(1200, 56)).pivot = new Vector2(.5f, 1);

            row = UiKit.Node("cards", Card).Pin(new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(10, 10));
            UiKit.Row(row, Gap, null, TextAnchor.MiddleCenter).childAlignment = TextAnchor.UpperCenter;
            UiKit.Hug(row);
            foreach (var it in items) cards.Add(MakeCard(save, it));

            var buttons = UiKit.Node("buttons", Card).Pin(new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(10, UiKit.ButtonHeight));
            buttons.pivot = new Vector2(.5f, 0);
            UiKit.Row(buttons, 14, null, TextAnchor.MiddleCenter);
            UiKit.Hug(buttons, true, false);
            UiKit.Secondary(buttons, "Open bag", OpenBag, "🎒");
            var nice = UiKit.Primary(buttons, "Nice!", Close);
            UiKit.PadHints(Card, ("A", "Select"), ("B", "Close"));
            openedAt = Time.unscaledTime;
            VirtualCursor.FocusFirst(nice);
        }

        // one turned-over card: the name plate, power against what's worn, and where it went
        (RectTransform, CanvasGroup, Image) MakeCard(TalesSave save, GearItem it)
        {
            var slot = UiKit.Node(it.Name, row);
            UiKit.Size(slot, MaxCardWidth);
            var pillar = UiKit.Node("pillar", slot).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(MaxCardWidth * 1.2f, 720)).gameObject.AddComponent<Image>();
            pillar.sprite = UiKit.Glow;
            pillar.color = TalesUi.RarityColor(it.Tier).WithAlpha(0);
            pillar.raycastTarget = false;
            UiKit.Size(pillar).ignoreLayout = true;

            var card = UiKit.Panel(slot, "card", Palette.Cream); // catches taps, so only the dimmed edges close the reveal
            var r = card.rectTransform;
            UiKit.Column(r, 10, new RectOffset(12, 12, 12, 16));
            UiKit.Column(slot, 0);
            UiKit.Shadow(r, UiKit.CardRadius, 20, 8, .35f);
            UiKit.Outline(card, TalesUi.RarityColor(it.Tier), UiKit.CardRadius, 3);
            GearUi.Band(r, it, 56);
            GearUi.CompareLine(r, save, it, UiKit.SmallSize + 1);
            var drop = Loot.Found(it.Id);
            if (drop != null && !drop.Kept) GearUi.IconText(r, "✨", $"Bag full, so it became {drop.Dust} dust", UiKit.SmallSize, UiKit.RoseInk, UiKit.Bold);
            if (drop != null && drop.Fresh) GearUi.IconText(r, "📖", "New in your codex", UiKit.SmallSize, UiKit.LeafInk, UiKit.Bold);
            if (drop != null && drop.Signature) GearUi.IconText(r, "✦", "Signature drop", UiKit.SmallSize, UiKit.EmberInk, UiKit.Bold);
            if (it.Perk != null) GearUi.IconText(r, Loot.PerkIcon(it.Perk), $"{Loot.PerkName(it.Perk)} {JsMath.Num(it.PerkValue)}%", UiKit.SmallSize, Palette.Ink, UiKit.Bold);

            var fade = r.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0;
            return (r, fade, pillar);
        }

        protected override void Layout(Vector2 size)
        {
            int n = Mathf.Max(1, cards.Count);
            float w = Mathf.Min(MaxCardWidth, (size.x - 64 - (n - 1) * Gap) / n);
            foreach (var (card, _, _) in cards) UiKit.Size(card.parent, w);
        }

        protected override void Update()
        {
            base.Update();
            float t = Time.unscaledTime - openedAt;
            bool calm = GameSettings.ReduceMotion;
            for (int i = 0; i < cards.Count; i++)
            {
                var (card, fade, pillar) = cards[i];
                float k = Mathf.Clamp01((t - FirstFlip - i * FlipEvery) / FlipTime);
                if (k > 0 && i >= flipped) Flip(i);
                fade.alpha = UiKit.EaseOut(k * 2);
                float x = calm ? 1 : UiKit.EaseBack(k);
                card.localScale = new Vector3(x, calm ? 1 : Mathf.Lerp(.9f, 1, UiKit.EaseOut(k)), 1);
                pillar.color = pillar.color.WithAlpha(.42f * UiKit.EaseOut(k) * (calm ? 1 : .85f + .15f * Mathf.Sin(t * 2 + i)));
            }
            if (!calm) glow.rectTransform.localScale = Vector3.one * (1 + Mathf.Sin(t * 1.6f) * .03f);
        }

        // each card that turns over makes a sound: the best rarity's for the first, a soft pop for the rest
        void Flip(int i)
        {
            flipped = i + 1;
            int top = 0;
            foreach (var it in items) top = Mathf.Max(top, it.Tier);
            if (i == 0) Sound.Play(top >= 2 ? "rare" : "coin");
            else Sound.Play("pop", .6f);
        }

        void OpenBag()
        {
            string first = null;
            foreach (var it in items) if (Loot.Found(it.Id)?.Kept ?? TalesSave.Current.Bag.Contains(it.Id)) { first = it.Id; break; }
            Close();
            TalesUi.OpenBag(first);
        }

        protected override void OnClosed() => closed?.Invoke();
    }
}
