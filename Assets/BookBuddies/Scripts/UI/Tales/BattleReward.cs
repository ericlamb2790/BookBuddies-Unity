using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The card at the end of a road fight: "Victory!" with the renown gained and any level-up, the gear found as
    /// rarity-framed tiles that flip in one by one, then it carries on by itself after a few seconds: a ring on
    /// "Keep going" counts down. A pointer resting on the card holds the count; any other input (the stick, the d-pad,
    /// arrows, a press on the card) stops it, and "Open bag" goes straight to the new gear. A loss gets the same card
    /// with a gentler line. B keeps going.
    /// </summary>
    public sealed class BattleReward : MonoBehaviour
    {
        const float Countdown = 4, FlipDelay = .35f, FlipGap = .28f, TileSize = 112;

        Action keepGoing;
        Action<string> openBag;
        BattleHover hover;
        Image ring;
        float left = Countdown, startsAt;
        bool stopped, finished;

        /// <summary>Shows the card on the battle's top layer. keepGoing closes the battle; openBag closes it and opens the bag on that item.</summary>
        public static BattleReward Show(RectTransform layer, BattleOutcome o, BattleSound sound, Action keepGoing, Action<string> openBag)
        {
            var root = UiKit.Node("end card", layer).Fill();
            UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.5f), null, true);
            var r = root.gameObject.AddComponent<BattleReward>();
            r.keepGoing = keepGoing;
            r.openBag = openBag;
            r.Build(root, layer.rect.width, o, sound);
            UiStack.Push(r, r.KeepGoing);
            return r;
        }

        void Build(RectTransform root, float width, BattleOutcome o, BattleSound sound)
        {
            var bg = UiKit.Panel(root, "card", Palette.Cream, 18);
            var card = bg.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(Mathf.Min(680, width - 32), 10));
            UiKit.Shadow(card, 18, 30, 10, .35f);
            UiKit.Column(card, 14, new RectOffset(32, 32, 26, 28));
            UiKit.Hug(card, false, true);
            hover = bg.gameObject.AddComponent<BattleHover>();
            hover.Pressed += Stop;

            string pet = BattleText.Prose(o.Hero?.Name ?? TalesUi.PetName);
            var head = UiKit.Node("title", card);
            UiKit.Row(head, 14);
            UiKit.Icon(head, o.Won ? "🏆" : "💤", 48);
            UiKit.Label(head, o.Won ? "Victory!" : "Time for a nap", 44, Palette.Ink, UiKit.Title);
            UiKit.Label(card, o.Won
                ? $"{pet} won in {o.Rounds} round{(o.Rounds == 1 ? "" : "s")}."
                : $"{pet} ran out of steam and will wake up back in Pawtopia.", UiKit.BodySize, Palette.InkSoft);
            startsAt = Time.unscaledTime + FlipDelay;
            if (o.Won) Renown(card, o);
            if (o.Won && o.BestMove != null)
                UiKit.Label(card, $"Best hit: {BattleText.Prose(UiKit.SplitEmoji(o.BestMove, out _))} for {Mathf.RoundToInt((float)o.BestHit)}", UiKit.SmallSize, Palette.InkSoft);
            if (o.Won) Drops(card, o, sound);
            Buttons(card, o);
            UiKit.PadHints(card, ("A", "Select"), ("B", "Keep going"));
            KeyTween.Play(card, .24f, BattleEase.Out, new Kf(0, 0, -16, 0, .97f, 0), new Kf(1));
        }

        // "+2 renown · Pet Lv 5" over a bar of the way to the next level, and the level-up line when there is one
        static void Renown(RectTransform card, BattleOutcome o)
        {
            if (o.RenownGained <= 0) return;
            var (lvl, xp, need) = HeroFactory.Renown(TalesSave.Current.Me.Rxp);
            var row = UiKit.Node("renown", card);
            UiKit.Row(row, 12);
            UiKit.Icon(row, "✨", 24);
            UiKit.Label(row, $"+{o.RenownGained} renown · Pet Lv {lvl + 1}", UiKit.BodySize, UiKit.EmberInk, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            var track = UiKit.Panel(row, "bar", Palette.Ink.WithAlpha(.1f), 6);
            track.raycastTarget = false;
            UiKit.Size(track, -1, 12, 1);
            var fill = UiKit.Panel(track.transform, "xp", Palette.Amber, 6).rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01((float)(xp / Math.Max(1, need))), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            if (string.IsNullOrEmpty(o.LevelNote)) return;
            var note = UiKit.Panel(card, "level up", Palette.Amber.WithAlpha(.22f), 12);
            note.raycastTarget = false;
            UiKit.Row(note.rectTransform, 10, new RectOffset(14, 14, 10, 10), TextAnchor.MiddleLeft);
            string words = UiKit.SplitEmoji(o.LevelNote, out var icon);
            if (icon != null) UiKit.Icon(note.transform, icon, 28);
            UiKit.Size(UiKit.Label(note.transform, words, UiKit.SmallSize + 2, Palette.Ink, UiKit.Bold), -1, -1, 1);
        }

        // the gear found, flipping in one by one; or what to expect when nothing dropped
        void Drops(RectTransform card, BattleOutcome o, BattleSound sound)
        {
            if (o.Drops.Count == 0)
            {
                UiKit.Label(card, "No gear this time. Foes on Bramble Road drop it about one fight in three.", UiKit.SmallSize + 1, Palette.InkSoft);
                return;
            }
            UiKit.Label(card, "Found", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold);
            var row = UiKit.Node("drops", card);
            UiKit.Row(row, 16, null, TextAnchor.UpperCenter);
            int n = 0;
            foreach (var s in o.Drops)
            {
                var it = Loot.Get(s);
                if (it == null) continue;
                var face = Tile(row, it, Loot.Found(s));
                float at = FlipDelay + n++ * FlipGap;
                KeyTween.Play(face, .26f, BattleEase.Swing, at, false, Kf.Squash(0, 0, 0, 0, 0, 1, 0), new Kf(1));
                StartCoroutine(Later(at + .1f, () => sound.Play(it.Tier >= 2 ? "sparkle" : "pop")));
            }
            startsAt = Time.unscaledTime + FlipDelay + n * FlipGap + .2f;
        }

        static System.Collections.IEnumerator Later(float seconds, Action act)
        {
            yield return new WaitForSecondsRealtime(seconds);
            act();
        }

        // a rarity-framed tile: the item's icon, a "New" or "Signature" mark, its name and what it means for you.
        // Returns its face (the part that flips; the row lays out the tile around it).
        static RectTransform Tile(RectTransform row, GearItem it, LootDrop found)
        {
            var slot = UiKit.Node(it.Name, row);
            UiKit.Size(slot, TileSize + 24, TileSize + 76);
            var tile = UiKit.Node("face", slot).Fill();
            UiKit.Column(tile, 6, null, TextAnchor.UpperCenter);
            var rarity = TalesUi.RarityColor(it.Tier);
            var frame = UiKit.Panel(tile, "frame", Palette.Paper, UiKit.CardRadius);
            frame.raycastTarget = false;
            UiKit.Size(frame, TileSize, TileSize);
            UiKit.Outline(frame, rarity, UiKit.CardRadius, 3);
            if (it.Tier >= 3) UiKit.Cover(frame.transform, "glow", rarity.WithAlpha(.35f), UiKit.Glow).rectTransform.Fill(-14);
            UiKit.Icon(frame.transform, it.Icon, 64).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(64, 64));
            string mark = found?.Signature == true ? "Signature" : found?.Fresh == true ? "New" : null;
            if (mark != null)
            {
                var badge = UiKit.Badge(frame.transform, mark, UiKit.EmberInk);
                ((RectTransform)badge.transform).Pin(new Vector2(.5f, 1), new Vector2(0, 12), new Vector2(10, 26));
                UiKit.Hug((RectTransform)badge.transform, true, false);
            }
            var name = UiKit.Label(tile, BattleText.Prose(it.Name), UiKit.SmallSize, Palette.Ink, UiKit.Bold, TextAnchor.UpperCenter);
            name.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(name, -1, 40);
            UiKit.Label(tile, Note(it, found), UiKit.SmallSize, found != null && !found.Kept ? UiKit.RoseInk : UiKit.LeafInk, UiKit.Bold, TextAnchor.UpperCenter);
            return tile;
        }

        static string Note(GearItem it, LootDrop found)
        {
            if (found != null && !found.Kept) return $"Bag full: +{found.Dust} dust";
            var (delta, empty) = Loot.Compare(TalesSave.Current, it);
            return empty ? "Fills a slot" : delta > 0 ? $"+{delta} power" : Loot.RarityName(it.Tier);
        }

        void Buttons(RectTransform card, BattleOutcome o)
        {
            var row = UiKit.Node("buttons", card);
            UiKit.Row(row, 12, new RectOffset(0, 0, 6, 0), TextAnchor.MiddleRight);
            string first = o.Drops.Find(s => Loot.Found(s)?.Kept != false);
            if (first != null) UiKit.Secondary(row, "Open bag", () => OpenBag(first), "🎒");
            var keep = UiKit.Primary(row, "Keep going", KeepGoing);
            var box = UiKit.Node("countdown", keep.transform);
            box.SetAsFirstSibling();
            UiKit.Size(box, 26, 26);
            var track = UiKit.Node("track", box).Fill().gameObject.AddComponent<Image>();
            track.sprite = UiKit.Ring(13, 4);
            track.color = Palette.Ink.WithAlpha(.2f);
            track.raycastTarget = false;
            ring = UiKit.Node("time", box).Fill().gameObject.AddComponent<Image>();
            ring.sprite = UiKit.Ring(13, 4);
            ring.color = Palette.Ink;
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = false;
            ring.raycastTarget = false;
            VirtualCursor.FocusFirst(keep);
        }

        void Update()
        {
            if (finished) return;
            if (PlazaInput.ArrowPressed() || PlazaInput.DpadStep() != Vector2Int.zero || PlazaInput.CursorStick() != Vector2.zero) Stop();
            bool held = stopped || Time.unscaledTime < startsAt || hover.Over && PlazaInput.UsingPointer;
            if (!held) left -= Time.unscaledDeltaTime;
            ring.fillAmount = left / Countdown;
            ring.color = Palette.Ink.WithAlpha(held ? .4f : 1);
            if (left <= 0) KeepGoing();
        }

        // the player is doing something here: no more counting down
        void Stop()
        {
            stopped = true;
            UiKit.Show(ring.transform.parent, false);
        }

        /// <summary>Closes the card and the battle (the countdown, the button or B).</summary>
        public void KeepGoing()
        {
            if (finished) return;
            finished = true;
            UiStack.Remove(this);
            keepGoing?.Invoke();
        }

        void OpenBag(string item)
        {
            if (finished) return;
            finished = true;
            UiStack.Remove(this);
            openBag?.Invoke(item);
        }

        void OnDestroy() => UiStack.Remove(this);
    }
}
