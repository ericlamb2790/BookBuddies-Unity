using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Road;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The HUD's top-left card, so the screen reads as "my pet" first: the active pet's portrait with its ink pips under
    /// it, its name and coins, its form and Pet Lv, the HP bar (HP comes back over time between wild fights) and the
    /// renown XP bar toward the next Pet Lv. Clicking the card opens the pets screen; clicking the coins opens the
    /// coins sheet. It reads the save a few times a second, so fights, finds and pet switches show up by themselves.
    /// </summary>
    public sealed class PetBadge : MonoBehaviour
    {
        /// <summary>The card's size in reference pixels (the road's objective card lines up under it, just as wide).</summary>
        public const float Width = 392, Height = 118;
        const float Portrait = 76, Pip = 10, RefreshEvery = .25f, BarSpeed = 1.5f;
        static readonly Color HpGood = Palette.Hex("#4f9a48"), HpLow = Palette.Hex("#e6a23c"), HpBad = Palette.Hex("#d64545");

        PlazaWorld world;
        RectTransform picture;
        Image art, hpFill, xpFill;
        Image[] pips;
        Text nameText, formText, hpText, xpText;
        string shownLook, form;
        int shownLevel = -1;
        float hp = -1, xp = -1, hpTarget, xpTarget, refreshAt;

        /// <summary>Adds the card to the HUD's top-left corner; "openCoins" runs when its coin counter is clicked.</summary>
        public static PetBadge Create(RectTransform parent, PlazaWorld world, System.Action openCoins)
        {
            var b = UiKit.Button(parent, "pet card", Palette.Cream, () => PetsScreen.Open(world.Me), UiKit.CardRadius);
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // like the other HUD buttons: the cursor reaches it, the d-pad doesn't
            var r = ((RectTransform)b.transform).Pin(new Vector2(0, 1), new Vector2(Hud.Margin, -Hud.Margin), new Vector2(Width, Height));
            UiKit.Outline(b, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(r, UiKit.CardRadius, 16, 5, .24f);
            var badge = b.gameObject.AddComponent<PetBadge>();
            badge.world = world;
            badge.Build(r, openCoins);
            return badge;
        }

        // [portrait]  Name ············ [🪙 1,250]
        // [ ● ● ○ ]   Little Bean · Pet Lv 5
        //             HP ▓▓▓▓▓▓▓▓▓▓░░  72%
        //             XP ▓▓▓▓░░░░░░░░  40/72
        void Build(RectTransform card, System.Action openCoins)
        {
            UiKit.Row(card, 14, new RectOffset(12, 14, 12, 12)).childAlignment = TextAnchor.UpperLeft;

            var left = UiKit.Node("portrait", card);
            UiKit.Column(left, 6, null, TextAnchor.UpperCenter).childForceExpandWidth = false;
            UiKit.Size(left, Portrait);
            var disc = UiKit.Panel(left, "disc", Palette.Paper, (int)(Portrait / 2));
            UiKit.Size(disc, Portrait, Portrait);
            UiKit.Outline(disc, Palette.Amber, (int)(Portrait / 2), 2);
            art = UiKit.Node("pet", disc.transform).Fill(5).gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            picture = art.rectTransform;
            picture.pivot = new Vector2(.5f, 0); // breathes from its feet

            var ink = UiKit.Node("ink", left);
            UiKit.Row(ink, 3, null, TextAnchor.MiddleCenter);
            UiKit.Size(ink, -1, Pip);
            pips = new Image[RoadVitals.MaxInk];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UiKit.Panel(ink, "pip", Palette.Paper, (int)(Pip / 2));
                UiKit.Size(pips[i], Pip, Pip);
                UiKit.Outline(pips[i], UiKit.SkyInk.WithAlpha(.45f), (int)(Pip / 2), 1);
            }

            var words = UiKit.Node("words", card);
            UiKit.Column(words, 4);
            UiKit.Size(words, -1, -1, 1);
            var top = UiKit.Node("name and coins", words);
            UiKit.Row(top, 8);
            UiKit.Size(top, -1, 30);
            nameText = UiKit.Label(top, "", UiKit.HeadingSize, Palette.Ink, UiKit.Bold);
            nameText.resizeTextForBestFit = true; // a long name shrinks instead of pushing the coins out
            nameText.resizeTextMinSize = UiKit.SmallSize + 1;
            nameText.resizeTextMaxSize = UiKit.HeadingSize;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(nameText, 0, 30, 1); // takes the room the coins leave
            UiKit.Size(CoinPill.Create(top, openCoins), -1, 34);

            formText = UiKit.Label(words, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            formText.supportRichText = true;
            formText.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(formText, -1, 20);
            (hpFill, hpText) = Meter(words, "HP", HpGood);
            (xpFill, xpText) = Meter(words, "XP", Palette.Amber);
        }

        // "HP ▓▓▓▓▓▓░░ 72%": a word, a rounded bar and its number
        static (Image fill, Text value) Meter(Transform parent, string word, Color color)
        {
            var row = UiKit.Node(word, parent);
            UiKit.Row(row, 8);
            UiKit.Size(row, -1, 16);
            UiKit.Size(UiKit.Label(row, word, UiKit.SmallSize, Palette.InkSoft, UiKit.Bold), 24);
            var track = UiKit.Panel(row, "bar", Palette.Ink.WithAlpha(.1f), 5);
            UiKit.Size(track, -1, 10, 1);
            var fill = UiKit.Panel(track.transform, "fill", color, 5);
            fill.rectTransform.Fill();
            var value = UiKit.Label(row, "", UiKit.SmallSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleRight);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Size(value, 64);
            return (fill, value);
        }

        void Update()
        {
            if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + RefreshEvery; Paint(); }
            float step = Time.unscaledDeltaTime * BarSpeed;
            hp = Mathf.MoveTowards(hp, hpTarget, step);
            xp = Mathf.MoveTowards(xp, xpTarget, step);
            hpFill.rectTransform.anchorMax = new Vector2(hp, 1);
            xpFill.rectTransform.anchorMax = new Vector2(xp, 1);
            float breath = GameSettings.ReduceMotion ? 1 : 1 + .025f * Mathf.Sin(Time.unscaledTime * 2.4f);
            picture.localScale = new Vector3(2 - breath, breath, 1);
        }

        // the pet, its form and level, HP, XP and ink as the save has them now
        void Paint()
        {
            string look = Buddy.ShownLook;
            if (look != shownLook)
            {
                shownLook = look;
                art.sprite = PetSprites.For(look);
                var parts = PetSprites.Parts;
                var parsed = PetLook.Parse(look, parts);
                form = parsed == null || parsed.Stage == 0 ? "Still an egg" : parts.Evolution.NameOf(parsed.Shape, parsed.Evo, parts);
            }
            nameText.text = Buddy.Hatched ? MyPets.ActiveName : "Your egg";

            var renown = HeroFactory.Renown(TalesSave.Current.Me.Rxp);
            formText.text = $"{form} · <color=#b4521f>Pet Lv {renown.lvl + 1}</color>";
            xpTarget = Mathf.Clamp01((float)(renown.xp / renown.need));
            xpText.text = $"{(int)renown.xp}/{(int)renown.need}";
            if (renown.lvl != shownLevel) { xp = xpTarget; shownLevel = renown.lvl; } // a new level starts the bar afresh

            var (health, ink) = RoadVitals.Now(world.Map.Wild == null);
            hpTarget = (float)health;
            if (hp < 0) hp = hpTarget;
            hpFill.color = health > .55 ? HpGood : health > .25 ? HpLow : HpBad;
            hpText.text = Mathf.RoundToInt(hpTarget * 100) + "%";
            for (int i = 0; i < pips.Length; i++) pips[i].color = i < ink ? Palette.Sky : Palette.Paper;
        }
    }
}
