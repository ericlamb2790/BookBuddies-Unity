using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>Pieces the bag, the hero card and the reveal share: gear tiles, the item card, icon labels and deltas.</summary>
    public static class GearUi
    {
        /// <summary>An emoji and a word side by side (the fonts can't draw colour emoji, so the emoji is an image).</summary>
        public static RectTransform IconText(Transform parent, string emoji, string text, int size, Color color, Font font = null)
        {
            var row = UiKit.Node(text, parent);
            UiKit.Row(row, 6);
            if (!string.IsNullOrEmpty(emoji)) UiKit.Icon(row, emoji, size + 4);
            UiKit.Label(row, text, size, color, font ? font : UiKit.Body).horizontalOverflow = HorizontalWrapMode.Overflow;
            return row;
        }

        /// <summary>"+1.2%" in leaf green or "−0.8%" in rose: the sign carries the meaning, the colour backs it up.</summary>
        public static Text Delta(Transform parent, double d, string unit, int size = UiKit.SmallSize)
        {
            d = JsMath.Round1(d);
            var t = UiKit.Label(parent, d == 0 ? "" : (d > 0 ? "+" : "−") + JsMath.Num(System.Math.Abs(d)) + unit, size, d > 0 ? UiKit.LeafInk : UiKit.RoseInk, UiKit.Bold, TextAnchor.MiddleRight);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>A square bag tile: rarity frame and tint, the item's icon and power, ★ when ancient, a rose dot when new.</summary>
        public static Button Tile(Transform parent, GearItem it, bool fresh, System.Action onClick, float size = 84)
        {
            var b = UiKit.Button(parent, it.Name, Palette.Paper, onClick, 12);
            UiKit.Size(b, size, size);
            var rc = TalesUi.RarityColor(it.Tier);
            var tint = UiKit.Panel(b.transform, "tint", rc.WithAlpha(it.Tier == 0 ? .1f : .2f), 10);
            tint.raycastTarget = false;
            ((RectTransform)tint.transform).Fill(3);
            UiKit.Outline(b, rc, 12, 3);
            if (it.Tier >= 3) Glow(b.transform, rc, size * .9f, new Vector2(0, 8));
            var icon = UiKit.Icon(b.transform, it.Icon, size * .5f);
            icon.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(size * .5f, size * .5f));
            UiKit.Size(icon).ignoreLayout = true;

            var power = UiKit.Node("power", b.transform).Pin(new Vector2(.5f, 0), new Vector2(0, 6), new Vector2(size - 8, 20));
            power.pivot = new Vector2(.5f, 0);
            UiKit.Row(power, 2, null, TextAnchor.MiddleCenter);
            UiKit.Icon(power, "⚡", 14);
            UiKit.Label(power, JsMath.Num(it.Power), UiKit.SmallSize, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;

            if (it.Ancient) UiKit.Icon(b.transform, "★", 18).rectTransform.Pin(new Vector2(0, 1), new Vector2(5, -5), new Vector2(18, 18));
            if (fresh)
            {
                var dot = UiKit.Panel(b.transform, "new", Palette.Rose, 7);
                dot.raycastTarget = false;
                dot.rectTransform.Pin(new Vector2(1, 1), new Vector2(-6, -6), new Vector2(14, 14));
                UiKit.Outline(dot, Palette.Cream, 7, 2);
            }
            return b;
        }

        /// <summary>A stat on a cream tile: icon, name, value, and the change to after when it differs.</summary>
        public static RectTransform StatTile(Transform parent, string emoji, string name, int value, int after)
        {
            var tile = UiKit.Panel(parent, name, Palette.Cream, 12);
            tile.raycastTarget = false;
            var r = tile.rectTransform;
            UiKit.Row(r, 10, new RectOffset(12, 12, 6, 6));
            UiKit.Icon(r, emoji, 26);
            var words = UiKit.Node("words", r);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, name, 13, Palette.InkSoft, UiKit.Bold);
            UiKit.Label(words, value.ToString("N0"), 22, Palette.Ink, UiKit.Bold);
            if (after != value) GearUi.Delta(r, after - value, "", UiKit.BodySize);
            return r;
        }

        /// <summary>A soft glow in a colour, behind whatever comes next in parent.</summary>
        public static Image Glow(Transform parent, Color color, float size, Vector2 offset)
        {
            var g = UiKit.Node("glow", parent).Pin(new Vector2(.5f, .5f), offset, new Vector2(size, size)).gameObject.AddComponent<Image>();
            g.sprite = UiKit.Glow;
            g.color = color.WithAlpha(.55f);
            g.raycastTarget = false;
            UiKit.Size(g).ignoreLayout = true;
            return g;
        }

        /// <summary>The item's name plate: an ink band with the icon, the name in its rarity colour, rarity · slot · level · theme, and power.</summary>
        public static RectTransform Band(Transform parent, GearItem it, int iconSize = 60)
        {
            var band = UiKit.Panel(parent, "band", Palette.Ink, 12);
            band.raycastTarget = false;
            var r = band.rectTransform;
            UiKit.Row(r, 14, new RectOffset(14, 16, 12, 12));
            var rc = TalesUi.RarityColor(it.Tier);
            var holder = UiKit.Node("icon", r);
            UiKit.Size(holder, iconSize, iconSize);
            if (it.Tier >= 2) Glow(holder, rc, iconSize * 1.5f, Vector2.zero);
            UiKit.Icon(holder, it.Icon, iconSize).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(iconSize, iconSize));

            var words = UiKit.Node("words", r);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, it.Name, 22, rc, UiKit.Title);
            string rarity = (it.Ancient ? "Ancient " : "") + Loot.RarityName(it.Tier);
            UiKit.Label(words, $"{rarity} {Loot.SlotName(it.Slot).ToLowerInvariant()} · Level {it.ILvl}", UiKit.SmallSize, Palette.Cream.WithAlpha(.85f));
            IconText(words, Loot.ThemeIcon(it.Theme), Loot.ThemeName(it.Theme), UiKit.SmallSize, Palette.Cream.WithAlpha(.85f));

            var power = IconText(r, "⚡", JsMath.Num(it.Power), 22, Palette.Amber, UiKit.Bold);
            UiKit.Size(power, -1, iconSize);
            return r;
        }

        /// <summary>
        /// One line comparing an item with what's worn: "+38 power vs your Tiny Crown", "Fills your empty hat slot",
        /// or "You're wearing this".
        /// </summary>
        public static Text CompareLine(Transform parent, TalesSave save, GearItem it, int size = UiKit.BodySize)
        {
            var (delta, empty) = Loot.Compare(save, it);
            var cur = Loot.Equipped(save, it.Slot);
            string text; Color color;
            if (Loot.WornIn(save, it.Id) != null) { text = "You’re wearing this"; color = Palette.InkSoft; }
            else if (empty) { text = $"Fills your empty {Loot.SlotName(it.Slot).ToLowerInvariant()} slot"; color = UiKit.LeafInk; }
            else if (delta == 0) { text = $"Same power as your {cur.Name}"; color = Palette.InkSoft; }
            else { text = $"{(delta > 0 ? "+" : "−")}{System.Math.Abs(delta)} power vs your {cur.Name}"; color = delta > 0 ? UiKit.LeafInk : UiKit.RoseInk; }
            return UiKit.Label(parent, text, size, color, UiKit.Bold);
        }

        /// <summary>
        /// The item's stats, each with its roll-quality bar and, when compareTo is given, the change against it;
        /// stats only the worn item has are listed after, as losses.
        /// </summary>
        public static void StatRows(Transform parent, GearItem it, GearItem compareTo)
        {
            foreach (var roll in it.Rolls)
                StatRow(parent, roll.Key, roll.Value, roll.Primary, roll.Quality, compareTo == null ? (double?)null : roll.Value - compareTo.Stat(roll.Key));
            if (compareTo == null) return;
            foreach (var roll in compareTo.Rolls)
                if (!it.Stats.ContainsKey(roll.Key)) StatRow(parent, roll.Key, 0, false, -1, -roll.Value);
        }

        static void StatRow(Transform parent, string key, double value, bool primary, double quality, double? delta)
        {
            var row = UiKit.Node(key, parent);
            UiKit.Row(row, 10);
            UiKit.Size(row, -1, 30);
            UiKit.Icon(row, Loot.StatIcon(key), 22);
            bool lost = quality < 0;
            var name = UiKit.Label(row, lost ? Loot.StatName(key) : $"{Loot.StatText(key, value)} {Loot.StatName(key)}", UiKit.BodySize - 1,
                lost ? Palette.InkSoft : Palette.Ink, primary ? UiKit.Bold : UiKit.Body);
            UiKit.Size(name, -1, -1, 1);
            if (!lost) QualityBar(row, quality);
            if (delta.HasValue)
            {
                var d = Delta(row, delta.Value, Data(key).Int ? "" : "%");
                UiKit.Size(d, 64);
            }
        }

        static GearStat Data(string key) => Loot.Data.StatByKey[key];

        static void QualityBar(Transform parent, double q)
        {
            var track = UiKit.Panel(parent, "roll", Palette.Ink.WithAlpha(.1f), 3);
            track.raycastTarget = false;
            UiKit.Size(track, 46, 6);
            var fill = UiKit.Panel(track.transform, "fill", Palette.Amber, 3);
            fill.raycastTarget = false;
            var f = fill.rectTransform;
            f.anchorMin = Vector2.zero; f.anchorMax = new Vector2((float)q, 1);
            f.offsetMin = f.offsetMax = Vector2.zero;
        }

        /// <summary>The legendary perk box: icon, name and strength, and what it does.</summary>
        public static void PerkBox(Transform parent, string perk, double value, string lead = null)
        {
            var box = UiKit.Panel(parent, "perk", Palette.Amber.WithAlpha(.2f), 12);
            box.raycastTarget = false;
            UiKit.Column(box.rectTransform, 4, new RectOffset(14, 14, 10, 12));
            IconText(box.transform, Loot.PerkIcon(perk), $"{lead}{Loot.PerkName(perk)} {JsMath.Num(value)}%", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            UiKit.Label(box.transform, Loot.PerkText(perk, value), UiKit.SmallSize + 1, Palette.InkSoft);
        }
    }

    /// <summary>
    /// Your buddy wearing its gear: the hat on top, the weapon at its side, armor tucked in, the charm circling, a ★
    /// when anything is ancient, and a soft glow in the best rarity (Rare and up). Moves gently unless motion is reduced.
    /// </summary>
    public sealed class BuddyPortrait : MonoBehaviour
    {
        Image glow, hat, weapon, armor, charm, star;
        float size;

        public static BuddyPortrait Create(Transform parent, float size)
        {
            var r = UiKit.Node("buddy", parent);
            UiKit.Size(r, size, size);
            var p = r.gameObject.AddComponent<BuddyPortrait>();
            p.size = size;
            p.glow = GearUi.Glow(r, Color.white, size * 1.3f, Vector2.zero);
            var pet = UiKit.Node("pet", r).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * 200 / 232f, size)).gameObject.AddComponent<Image>();
            pet.sprite = PetSprites.For(Buddy.ShownLook);
            pet.preserveAspect = true;
            pet.raycastTarget = false;
            p.armor = p.Part("armor");
            p.weapon = p.Part("weapon");
            p.hat = p.Part("hat");
            p.charm = p.Part("charm");
            p.star = p.Part("ancient");
            return p;
        }

        Image Part(string name)
        {
            var img = UiKit.Node(name, transform).Pin(new Vector2(.5f, .5f), Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Dresses the buddy in a set of gear (slot → item string).</summary>
        public void Show(IDictionary<string, string> gear)
        {
            GearItem Of(string sl) => gear != null && gear.TryGetValue(sl, out var s) ? Loot.Get(s) : null;
            int top = -1; bool ancient = false;
            foreach (var sl in LootData.SlotKeys) { var it = Of(sl); if (it == null) continue; top = Mathf.Max(top, it.Tier); ancient |= it.Ancient; }
            glow.enabled = top >= 2;
            if (top >= 2) glow.color = TalesUi.RarityColor(top).WithAlpha(.5f);
            Put(hat, Of("h"), new Vector2(0, .4f), .3f, -8);
            Put(weapon, Of("w"), new Vector2(.36f, -.14f), .32f, 14);
            var a = Of("a");
            Put(armor, a != null && a.Tier >= 1 ? a : null, new Vector2(-.33f, -.24f), .22f, 0);
            Put(charm, Of("c"), new Vector2(.46f, 0), .2f, 0);
            star.sprite = ancient ? Art.Emote("★") : null;
            star.enabled = star.sprite != null;
            star.rectTransform.anchoredPosition = new Vector2(.36f, .4f) * size;
            star.rectTransform.sizeDelta = Vector2.one * size * .14f;
        }

        void Put(Image img, GearItem it, Vector2 at, float scale, float angle)
        {
            img.sprite = it != null ? Art.Emote(it.Icon) : null;
            img.enabled = img.sprite != null;
            img.rectTransform.anchoredPosition = at * size;
            img.rectTransform.sizeDelta = Vector2.one * size * scale;
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        }

        void Update()
        {
            if (GameSettings.ReduceMotion) return;
            float t = Time.unscaledTime;
            if (charm.enabled) charm.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(t * .9f), Mathf.Sin(t * .9f) * .55f) * size * .46f;
            if (hat.enabled) hat.rectTransform.anchoredPosition = new Vector2(0, .4f + Mathf.Sin(t * 2.1f) * .012f) * size;
            if (weapon.enabled) weapon.rectTransform.localRotation = Quaternion.Euler(0, 0, 14 + Mathf.Sin(t * 2.2f) * 5);
            if (glow.enabled) glow.rectTransform.localScale = Vector3.one * (1 + Mathf.Sin(t * 2.4f) * .04f);
        }
    }
}
