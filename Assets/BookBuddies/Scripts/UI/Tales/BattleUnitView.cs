using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// One fighter on the field: its art standing on its feet and a nameplate under it with the HP bar (a delayed
    /// damage chip and a separate shield strip), six ink pips for pets and a tidy row of status icons (a pet's row is left
    /// off a plate too narrow for it, in a crowded lane). It glides to where the stage puts it, idles in its own style
    /// (the site's tqbob, float, wobble…), glows on its turn and naps when out.
    /// </summary>
    public sealed class BattleUnitView : MonoBehaviour
    {
        // the site's status order (unitHTML), with the emoji each one shows
        static readonly (string key, string icon)[] Statuses =
        {
            ("stun", "💫"), ("bleed", "🩸"), ("poison", "🧪"), ("expose", "🔍"), ("crit", "🌀"), ("taunt", "📢"), ("wind", "⏰"),
            ("pow", "💪"), ("dodge", "🪞"), ("regen", "💚"), ("weak", "😳"),
        };
        static readonly Color PetFill = Palette.Hex("#5fd06d"), FoeFill = Palette.Hex("#ff7a8a"), LowFill = Palette.Hex("#ff5e5e"),
            RisenFill = Palette.Hex("#ff3a3a"), ShieldBlue = Palette.Hex("#8fd0ff"), Gold = Palette.Hex("#ffcf6e"), PlateInk = Palette.Hex("#140c1f");
        const float PetArtHeight = 464f / 400f, PetFeet = .05f; // PetSprites draws 400×464 with the feet 5% up

        public BattleUnit Unit { get; private set; }
        /// <summary>Lunges, hops and shakes play on this (pivot at the feet).</summary>
        public RectTransform Motion { get; private set; }
        /// <summary>Tapping a foe opens its card (null for pets).</summary>
        public Button Tap { get; private set; }
        public bool ShownKo { get; private set; }
        /// <summary>The sprite box's side in reference pixels.</summary>
        public float Size { get; private set; }
        public Vector2 Feet => root.anchoredPosition;
        /// <summary>The middle of the body, where hits land.</summary>
        public Vector2 Center => Feet + new Vector2(0, Size * .45f * scale);
        /// <summary>Just above the head, where tips and bubbles start.</summary>
        public Vector2 Top => Feet + new Vector2(0, Size * .92f * scale);
        public float Width => Size * .8f * scale;

        RectTransform root, idle, art, plate, statusRow, pips, glow, ring, zz;
        Text nameText, hpText;
        Image fill, ghost, shieldBar, turnGlow, shieldRing;
        CanvasGroup group;
        readonly List<(Graphic g, Color c)> paints = new List<(Graphic, Color)>();
        readonly List<Image> pipDots = new List<Image>(), statusIcons = new List<Image>();
        float scale = 1, phase, hpShown = 1, hpTarget = 1, ghostShown = 1, ghostHold, shieldShown, shieldTarget, turn, turnShown, tintUntil;
        Vector2 from, to;
        float sizeFrom, sizeTo, glideAt = -1, hop;
        bool boss, dense;
        string motion;

        /// <summary>Builds a fighter under the stage's unit layer.</summary>
        public static BattleUnitView Create(RectTransform parent, BattleUnit u)
        {
            var root = UiKit.Node(u.Name, parent);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(.5f, 0);
            var v = root.gameObject.AddComponent<BattleUnitView>();
            v.Build(root, u);
            return v;
        }

        void Build(RectTransform r, BattleUnit u)
        {
            root = r;
            Unit = u;
            boss = u.Boss;
            phase = Random.value * 10;
            motion = u.IsFoe ? u.Foe.Def.Mv ?? "bob" : "bob";
            group = r.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = u.IsFoe; // taps on a pet fall through to its lane

            turnGlow = Picture(r, "turn", UiKit.Glow, Gold.WithAlpha(0));
            glow = turnGlow.rectTransform;
            if (boss) Halo(r);
            if (!u.IsFoe) Stretch(Picture(r, "shadow", Art.SoftDot, new Color(0, 0, 0, .32f)).rectTransform, new Vector2(.14f, -.05f), new Vector2(.86f, .07f));

            Motion = UiKit.Node("motion", r).Fill();
            Motion.pivot = new Vector2(.5f, 0);
            idle = UiKit.Node("idle", Motion).Fill();
            idle.pivot = new Vector2(.5f, 0);
            art = UiKit.Node("art", idle).Fill();
            art.pivot = new Vector2(.5f, 0);
            if (u.IsFoe) FoeArt.MakeUi(art, FoeArt.For(u.Foe, u.Boss));
            else PetArt(art, u);
            foreach (var g in art.GetComponentsInChildren<Graphic>()) paints.Add((g, g.color));
            if (u.IsFoe) scale = FoeArt.For(u.Foe, u.Boss).Scale;

            shieldRing = Picture(Motion, "shield", BattleArt.Ring, ShieldBlue.WithAlpha(0));
            ring = shieldRing.rectTransform;
            zz = UiKit.Label(Motion, "z z", 22, Palette.Hex("#cfd8ff"), UiKit.Title, TextAnchor.MiddleCenter).rectTransform;
            zz.gameObject.SetActive(false);

            Plate(r, u);
            if (u.IsFoe) TapArea(r);
        }

        static Image Picture(Transform parent, string name, Sprite sprite, Color color)
        {
            var img = UiKit.Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 0);
            return img;
        }

        // the boss's halo: a soft glow in its aura colour behind it and a gold ring on the ground
        void Halo(RectTransform r)
        {
            var look = FoeArt.For(Unit.Foe, true);
            var tint = look.Aura.a > 0 ? look.Aura : Palette.Hex("#ff9a6a");
            var back = Picture(r, "halo", UiKit.Glow, tint.WithAlpha(.42f)).rectTransform;
            Stretch(back, new Vector2(-.25f, .05f), new Vector2(1.25f, 1.15f)).gameObject.AddComponent<Breathe>();
            Stretch(Picture(r, "halo ring", BattleArt.Ring, Gold.WithAlpha(.55f)).rectTransform, new Vector2(-.12f, -.06f), new Vector2(1.12f, .12f));
        }

        static RectTransform Stretch(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        static void PetArt(RectTransform box, BattleUnit u)
        {
            var img = UiKit.Node("pet", box).gameObject.AddComponent<Image>();
            img.sprite = PetSprites.For(u.Hero?.Look ?? Buddy.ShownLook);
            img.preserveAspect = true;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = new Vector2(0, -PetFeet * PetArtHeight);
            r.anchorMax = new Vector2(1, (1 - PetFeet) * PetArtHeight);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        // the nameplate: a soft ink plate (text over art always sits on one) with the name, the bar and a status row
        void Plate(RectTransform r, BattleUnit u)
        {
            var bg = UiKit.Panel(r, "plate", PlateInk.WithAlpha(.62f), 10);
            bg.raycastTarget = false;
            plate = bg.rectTransform;
            plate.anchorMin = plate.anchorMax = new Vector2(.5f, 0);
            plate.pivot = new Vector2(.5f, 1);
            plate.anchoredPosition = new Vector2(0, -6);
            UiKit.Column(plate, 4, new RectOffset(8, 8, 5, 6), TextAnchor.UpperCenter);
            UiKit.Hug(plate, false, true);
            if (boss) UiKit.Outline(bg, Gold.WithAlpha(.75f), 10, 2);

            var nameRow = UiKit.Node("name", plate);
            UiKit.Row(nameRow, 5, null, TextAnchor.MiddleCenter);
            if (boss) UiKit.Icon(nameRow, "👑", 18);
            else if (u.IsFoe) UiKit.Icon(nameRow, TalesData.Current.GenreIcon(u.Gen), 18);
            nameText = UiKit.Label(nameRow, "", u.IsFoe ? 15 : 17, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter);
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(nameText, -1, -1, 1);
            if (!u.IsFoe)
            {
                hpText = UiKit.Label(nameRow, "", 15, Palette.Cream.WithAlpha(.85f), UiKit.Bold, TextAnchor.MiddleRight);
                hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            var bar = UiKit.Panel(plate, "hp", new Color(0, 0, 0, .6f), 7);
            bar.raycastTarget = false;
            UiKit.Size(bar, -1, u.IsFoe ? 12 : 14);
            ghost = Strip(bar.rectTransform, "chip", Color.white.WithAlpha(.75f), 0, 1);
            fill = Strip(bar.rectTransform, "fill", u.IsFoe ? FoeFill : PetFill, 0, 1);
            Strip(fill.rectTransform, "shine", Color.white.WithAlpha(.22f), .5f, 1).rectTransform.anchorMax = new Vector2(1, 1);
            shieldBar = Strip(bar.rectTransform, "shield", ShieldBlue, 0, .38f);
            shieldBar.rectTransform.anchorMax = new Vector2(0, .38f);

            statusRow = UiKit.Node("status", plate);
            UiKit.Row(statusRow, 3, null, TextAnchor.MiddleCenter);
            UiKit.Size(statusRow, -1, 20);
            if (!u.IsFoe) Pips(statusRow);
            if (u.IsFoe)
            {
                hpText = UiKit.Label(statusRow, "", 15, Palette.Cream.WithAlpha(.85f), UiKit.Bold, TextAnchor.MiddleRight);
                hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        static Image Strip(RectTransform bar, string name, Color c, float yMin, float yMax)
        {
            var img = UiKit.Panel(bar, name, c, 6);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = new Vector2(0, yMin);
            r.anchorMax = new Vector2(1, yMax);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return img;
        }

        void Pips(Transform row)
        {
            pips = UiKit.Node("ink", row);
            UiKit.Row(pips, 3, null, TextAnchor.MiddleCenter);
            for (int i = 0; i < 6; i++)
            {
                var dot = UiKit.Node("pip", pips).gameObject.AddComponent<Image>();
                dot.sprite = Art.Disc;
                dot.raycastTarget = false;
                UiKit.Size(dot, 10, 10);
                pipDots.Add(dot);
            }
            UiKit.Spacer(row, 6, 0);
        }

        void TapArea(RectTransform r)
        {
            var hit = UiKit.Node("tap", r);
            hit.anchorMin = new Vector2(.1f, 0); hit.anchorMax = new Vector2(.9f, .95f);
            hit.offsetMin = hit.offsetMax = Vector2.zero;
            var img = hit.gameObject.AddComponent<Image>();
            img.color = Color.clear;
            Tap = hit.gameObject.AddComponent<Button>();
            Tap.transition = Selectable.Transition.None;
            Tap.targetGraphic = img;
        }

        // ---- placing ----

        /// <summary>Where the stage wants this fighter: feet point, sprite size and nameplate width. Glides there unless instant.</summary>
        public void Place(Vector2 feet, float size, float plateWidth, bool isDense, bool instant, bool jump)
        {
            plate.sizeDelta = new Vector2(plateWidth, plate.sizeDelta.y);
            if (pips) UiKit.Show(statusRow, plateWidth >= 100); // pips need ~100 px: a crowded lane's plates keep name, player and HP
            dense = isDense;
            if (instant || glideAt < 0)
            {
                root.anchoredPosition = from = to = feet;
                sizeFrom = sizeTo = size;
                SetSize(size);
                glideAt = 0;
                return;
            }
            if ((feet - to).sqrMagnitude < .25f && Mathf.Abs(size - sizeTo) < .5f) return;
            from = root.anchoredPosition;
            sizeFrom = Size;
            to = feet;
            sizeTo = size;
            glideAt = Time.time;
            hop = jump ? 1 : 0;
        }

        void SetSize(float s)
        {
            Size = s;
            root.sizeDelta = new Vector2(s, s);
            glow.sizeDelta = new Vector2(s * 1.15f, s * .3f);
            ring.anchoredPosition = new Vector2(0, s * .45f * scale);
            ring.sizeDelta = Vector2.one * (s * 1.05f * scale);
            zz.anchoredPosition = new Vector2(s * .3f, s * .7f);
            zz.sizeDelta = new Vector2(80, 30);
        }

        // ---- showing state ----

        /// <summary>The HP bar's numbers (from an event's fx entry, so the bar matches what's on screen).</summary>
        public void SetHp(double hp, double max, double shield)
        {
            hpTarget = max <= 0 ? 0 : Mathf.Clamp01((float)(hp / max));
            shieldTarget = max <= 0 ? 0 : Mathf.Clamp01((float)(shield / max));
            if (hpTarget < ghostShown) ghostHold = .4f;
            if (hpText) hpText.text = dense ? "" : $"{(int)System.Math.Max(0, hp)} / {(int)max}";
        }

        /// <summary>Name, ink pips and status icons from the engine's state.</summary>
        public void Paint()
        {
            var u = Unit;
            nameText.text = BattleText.Prose(u.IsFoe ? $"{u.Name}  <color=#ffd27a>Lv{(int)u.Lvl}</color>" : u.Name);
            nameText.supportRichText = true;
            if (pipDots.Count > 0)
            {
                bool full = u.Ink >= 6;
                for (int i = 0; i < 6; i++)
                    pipDots[i].color = full ? Gold : i < u.Ink ? ShieldBlue : Color.white.WithAlpha(.18f);
            }
            var shown = new List<string>();
            if (!ShownKo)
            {
                foreach (var (key, icon) in Statuses) if (u.S(key) > 0) shown.Add(icon);
                if (u.Hero != null && u.Hero.Dark > 0) shown.Add("🌑");
            }
            SetIcons(shown);
        }

        void SetIcons(List<string> icons)
        {
            while (statusIcons.Count < icons.Count) statusIcons.Add(UiKit.Icon(statusRow, "✨", 20));
            for (int i = 0; i < statusIcons.Count; i++)
            {
                bool on = i < icons.Count;
                if (on) UiKit.SetIcon(statusIcons[i], icons[i]);
                UiKit.Show(statusIcons[i], on && statusIcons[i].sprite != null);
            }
            if (hpText) hpText.transform.SetAsLastSibling();
        }

        /// <summary>A party fight's pet: the player behind it under its name, or "You" and an amber edge on the plate for yours.</summary>
        public void Owner(string who, bool you)
        {
            if (!you && string.IsNullOrEmpty(who)) return;
            var line = UiKit.Label(plate, you ? "You" : who, UiKit.SmallSize, you ? Palette.Amber : Palette.Cream.WithAlpha(.72f), UiKit.Bold, TextAnchor.MiddleCenter);
            line.verticalOverflow = VerticalWrapMode.Truncate; // one line, however narrow the plate
            UiKit.Size(line, -1, 20);
            line.transform.SetSiblingIndex(nameText.transform.parent.GetSiblingIndex() + 1);
            if (you) UiKit.Outline(plate, Palette.Amber, 10, 2);
        }

        /// <summary>The gold glow under the feet while it's this fighter's turn.</summary>
        public void SetTurn(bool on) => turn = on ? 1 : 0;

        /// <summary>Out (a pet lies down with "z z"; a foe fades away) or back up.</summary>
        public void KnockOut(bool ko)
        {
            ShownKo = ko;
            zz.gameObject.SetActive(ko && !Unit.IsFoe);
            if (!Unit.IsFoe)
            {
                art.localRotation = Quaternion.Euler(0, 0, ko ? 75 : 0); // UI turns counter-clockwise: the site's -75deg lies it to the left
                art.anchoredPosition = ko ? new Vector2(Size * .2f, 0) : Vector2.zero; // lying down, still over its spot
                Tint(ko ? new Color(.55f, .55f, .62f) : Color.white);
                return;
            }
            if (Tap) Tap.interactable = !ko;
            if (glideAt > 0) { glideAt = 0; root.anchoredPosition = to; SetSize(sizeTo); }
            KeyTween.Play(root, .5f, BattleEase.Ease, ko
                ? new[] { new Kf(0), new Kf(1, 0, 0, 0, .5f, 0) }
                : new[] { new Kf(0, 0, 0, 0, .5f, 0), new Kf(1) });
        }

        /// <summary>A tale boss switched to its enrage tactic: a red glow behind its sprite for the rest of the fight (the site's .rage drop-shadow).</summary>
        public void Enrage()
        {
            if (idle.Find("rage")) return;
            var red = Picture(idle, "rage", UiKit.Glow, RisenFill.WithAlpha(.6f)).rectTransform;
            red.SetAsFirstSibling();
            Stretch(red, new Vector2(-.12f, -.04f), new Vector2(1.12f, scale * .98f));
        }

        /// <summary>A quick warm flash when hit (the site's brightness pop).</summary>
        public void Flash()
        {
            if (ShownKo) return;
            Tint(new Color(1, .62f, .58f));
            tintUntil = Time.time + .2f;
        }

        void Tint(Color k)
        {
            foreach (var (g, c) in paints) if (g) g.color = c * k;
        }

        // ---- every frame ----

        void Update()
        {
            float dt = Time.deltaTime;
            Glide();
            Idle();
            Bars(dt);
            turnShown = Mathf.MoveTowards(turnShown, ShownKo ? 0 : turn, dt * 4);
            turnGlow.color = Gold.WithAlpha(turnShown * (.58f + .22f * Mathf.Sin(Time.time * Mathf.PI * 2))); // tqpulse, 1 s
            float shieldAlpha = shieldShown > .001f && !ShownKo ? .55f + .2f * Mathf.Sin(Time.time * 3.5f) : 0;
            shieldRing.color = ShieldBlue.WithAlpha(Mathf.MoveTowards(shieldRing.color.a, shieldAlpha, dt * 3));
            if (tintUntil > 0 && Time.time > tintUntil) { tintUntil = 0; Tint(ShownKo && !Unit.IsFoe ? new Color(.55f, .55f, .62f) : Color.white); }
            if (zz.gameObject.activeSelf) zz.localPosition = new Vector3(zz.localPosition.x, Size * .7f + Mathf.Sin(Time.time * Mathf.PI) * 6, 0);
        }

        // the site's FLIP glide between lanes (560 ms) with a little hop
        void Glide()
        {
            if (glideAt <= 0) return;
            float t = Mathf.Clamp01((Time.time - glideAt) / .56f), k = BattleEase.Glide.At(t);
            root.anchoredPosition = Vector2.LerpUnclamped(from, to, k);
            SetSize(Mathf.Lerp(sizeFrom, sizeTo, k));
            if (t >= 1) { glideAt = 0; hop = 0; }
        }

        // idle loops by movement style, plus the lane-change hop (tqlnhop)
        void Idle()
        {
            float jump = hop > 0 && glideAt > 0 ? Mathf.Sin(Mathf.Clamp01((Time.time - glideAt) / .56f) * Mathf.PI) * Size * .16f : 0;
            if (GameSettings.ReduceMotion || ShownKo) { idle.anchoredPosition = new Vector2(0, jump); idle.localRotation = Quaternion.identity; idle.localScale = Vector3.one; return; }
            float t = Time.time + phase, y = 0, r = 0, sx = 1, sy = 1;
            float Wave(float seconds) => (1 - Mathf.Cos(t / seconds * Mathf.PI * 2)) / 2; // 0 → 1 → 0, eased like ease-in-out
            switch (motion)
            {
                case "float": y = 6 * Wave(3.2f); r = -Wave(3.2f); break;
                case "wobble": r = 5 * Mathf.Sin(t / 2.2f * Mathf.PI * 2); break;
                case "hop": float h = Mathf.Repeat(t / 1.6f, 1); y = h < .7f ? 12 * Mathf.Sin(h / .7f * Mathf.PI) : 0; if (h > .8f) { sx = 1.04f; sy = .96f; } break;
                case "sway": r = 3 * Mathf.Sin(t / 3f * Mathf.PI * 2); break;
                case "jitter": int step = (int)(Mathf.Repeat(t / 1.8f, 1) * 4); y = step % 2; r = step == 1 ? .6f : step == 3 ? -.6f : 0; break;
                case "pulse": sx = sy = 1 + .06f * Wave(2.6f); break;
                default: y = 5 * Wave(2.4f); break;
            }
            idle.anchoredPosition = new Vector2(0, y * Size / 160f + jump);
            idle.localRotation = Quaternion.Euler(0, 0, r);
            idle.localScale = new Vector3(sx, sy, 1);
        }

        // the fill eases to its value in .25 s; the white chip waits .4 s, then follows over about .5 s
        void Bars(float dt)
        {
            hpShown = Mathf.Lerp(hpShown, hpTarget, 1 - Mathf.Exp(-dt / .07f));
            if (ghostShown < hpShown) ghostShown = hpShown;
            else if ((ghostHold -= dt) <= 0) ghostShown = Mathf.Lerp(ghostShown, hpShown, 1 - Mathf.Exp(-dt / .14f));
            shieldShown = Mathf.Lerp(shieldShown, shieldTarget, 1 - Mathf.Exp(-dt / .09f));
            SetWidth(fill.rectTransform, hpShown);
            SetWidth(ghost.rectTransform, ghostShown);
            SetWidth(shieldBar.rectTransform, shieldShown);
            fill.color = hpTarget < .3f ? LowFill : Unit.Phase > 0 ? RisenFill : Unit.IsFoe ? FoeFill : PetFill;
        }

        static void SetWidth(RectTransform r, float frac)
        {
            if (!Mathf.Approximately(r.anchorMax.x, frac)) r.anchorMax = new Vector2(frac, r.anchorMax.y);
            r.gameObject.SetActive(frac > .004f);
        }

        /// <summary>The boss halo's slow breathing (tqaura: 2.8 s).</summary>
        sealed class Breathe : MonoBehaviour
        {
            void Update()
            {
                if (GameSettings.ReduceMotion) return;
                float k = (Mathf.Sin(Time.time * Mathf.PI * 2 / 2.8f) + 1) / 2;
                transform.localScale = Vector3.one * Mathf.Lerp(.94f, 1.06f, k);
            }
        }
    }
}
