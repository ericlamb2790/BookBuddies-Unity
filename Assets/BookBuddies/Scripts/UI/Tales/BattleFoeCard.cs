using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The card a tapped foe shows beside it (the site's foeCard): name, rank and lane, stats, what it's weak and strong
    /// against, its statuses, moves and a line it likes to say. Tapping it (or the field) closes it; it closes itself after 9 s.
    /// </summary>
    public sealed class BattleFoeCard : MonoBehaviour
    {
        const float Life = 9, Width = 360;
        static readonly Color Ink = Palette.Hex("#1d1529").WithAlpha(.96f);
        static readonly (string key, string name)[] Statuses =
            { ("stun", "Dizzy"), ("bleed", "Bleeding"), ("poison", "Poisoned"), ("expose", "Exposed"), ("wind", "Winding up"), ("taunt", "Taunting"), ("pow", "Powered up") };

        float shownAt;

        /// <summary>The foe this card is about.</summary>
        public BattleUnit Unit { get; private set; }

        /// <summary>Shows the card on the field layer beside the foe's view.</summary>
        public static BattleFoeCard Show(RectTransform layer, BattleUnitView v)
        {
            var bg = UiKit.Panel(layer, "foe card", Ink, UiKit.CardRadius);
            UiKit.Outline(bg, Color.white.WithAlpha(.12f), UiKit.CardRadius, 2);
            var r = bg.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            UiKit.Column(r, 10, new RectOffset(18, 18, 16, 18));
            UiKit.Hug(r, false, true);
            r.sizeDelta = new Vector2(Width, 0);
            var card = bg.gameObject.AddComponent<BattleFoeCard>();
            card.Unit = v.Unit;
            card.shownAt = Time.unscaledTime;
            card.Build(r);
            var tap = bg.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(card.Close);
            LayoutRebuilder.ForceRebuildLayoutImmediate(r);
            card.Place(r, v, layer);
            KeyTween.Play(r, .18f, BattleEase.Out, new Kf(0, 0, 6, 0, .97f, 0), new Kf(1));
            Sound.Play("tap");
            return card;
        }

        void Build(RectTransform r)
        {
            var u = Unit;
            var def = u.Foe.Def;
            var data = TalesData.Current;
            var head = UiKit.Node("head", r);
            UiKit.Row(head, 12);
            var tile = UiKit.Panel(head, "icon", Palette.Hex(u.Foe.Tc ?? def.C ?? "#7a6a8a"), 12);
            tile.raycastTarget = false;
            UiKit.Size(tile, 48, 48);
            UiKit.Icon(tile.transform, def.I, 32).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(32, 32));
            var name = UiKit.Label(head, BattleText.Prose(u.Name), 22, Palette.Cream, UiKit.Title);
            UiKit.Size(name, -1, -1, 1);

            string rank = u.Boss ? "Boss" : u.Elite ? "Elite" : "Foe";
            Line(r, $"{rank} · Lv {(int)u.Lvl} · {data.GenreLabel(u.Gen)} · {BattleText.Lane(u.Lane)} lane", Palette.Cream.WithAlpha(.75f));

            var stats = Chips(r);
            Chip(stats, "❤️", $"{(int)Math.Max(0, u.Hp)} / {(int)u.Max}", Palette.Cream);
            Chip(stats, "⚔️", ((int)u.Atk).ToString(), Palette.Cream);
            Chip(stats, "🛡️", ((int)u.Def).ToString(), Palette.Cream);
            Chip(stats, "💨", ((int)u.Spd).ToString(), Palette.Cream);
            var matchup = Chips(r);
            Chip(matchup, null, "Weak to " + data.GenreLabel(u.Gen + 5), Palette.Hex("#8ff0b4"));
            Chip(matchup, null, "Strong vs " + data.GenreLabel(u.Gen + 1), Palette.Hex("#ff9d8a"));

            var now = new List<string>();
            foreach (var (key, label) in Statuses) if (u.S(key) > 0) now.Add(label);
            if (now.Count > 0) Line(r, string.Join(" · ", now), Palette.Hex("#ffd27a"));
            Line(r, $"Moves: {def.An ?? "Basic hit"}, {u.Foe.Sn ?? "Special move"}", Palette.Cream.WithAlpha(.85f));
            string say = def.Say ?? (def.Says.Length > 0 ? def.Says[JsMath.Hash(u.Key + u.Name) % (uint)def.Says.Length] : null);
            if (say != null)
            {
                var quote = UiKit.Label(r, $"“{BattleText.Prose(say)}”", UiKit.SmallSize + 2, Palette.Hex("#ffd9a8"), UiKit.Title);
                quote.fontStyle = FontStyle.Italic;
            }
        }

        static void Line(RectTransform r, string text, Color color) => UiKit.Label(r, text, UiKit.SmallSize + 1, color, UiKit.Bold);

        static RectTransform Chips(RectTransform r)
        {
            var row = UiKit.Node("chips", r);
            UiKit.Row(row, 6);
            return row;
        }

        static void Chip(RectTransform row, string emoji, string text, Color ink)
        {
            var pill = UiKit.Panel(row, "chip", Color.white.WithAlpha(.08f), 14);
            pill.raycastTarget = false;
            UiKit.Row(pill.rectTransform, 5, new RectOffset(9, 10, 4, 5), TextAnchor.MiddleCenter);
            UiKit.Size(pill, -1, 30);
            if (emoji != null) UiKit.Icon(pill.transform, emoji, 18);
            UiKit.Label(pill.transform, text, UiKit.SmallSize, ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // beside the foe on the side toward the middle of the field, kept on screen
        void Place(RectTransform r, BattleUnitView v, RectTransform layer)
        {
            var field = new Vector2(((RectTransform)layer.parent).rect.width, ((RectTransform)layer.parent).rect.height);
            float h = r.rect.height;
            r.pivot = Vector2.zero;
            float x = v.Center.x - v.Size * .45f - Width - 12;
            if (x < 12) x = v.Center.x + v.Size * .45f + 12;
            r.anchoredPosition = new Vector2(Mathf.Clamp(x, 12, field.x - Width - 12), Mathf.Clamp(v.Center.y - h / 2, 12, field.y - h - 12));
        }

        void Update()
        {
            if (Unit.Hp <= 0 || Time.unscaledTime - shownAt > Life) Close();
        }

        /// <summary>Closes the card.</summary>
        public void Close()
        {
            if (this) Destroy(gameObject);
        }
    }
}
