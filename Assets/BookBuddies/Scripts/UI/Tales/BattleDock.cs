using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The battle's controls, kept to the edges: the fight's title top-left, controller hints top-right, and along the
    /// bottom the lane switcher, a hint, Cheer, speed, Bag and Log (quiet until the pointer comes near), with the big
    /// Ultimate button in the corner: a charging ring of ink that pulses gold when it's ready.
    /// Wide screens get one row; narrow ones stack the lanes over the actions.
    /// </summary>
    public sealed class BattleDock : MonoBehaviour
    {
        const string LaneKeys = "lcr";
        const float Margin = 16, Gap = 8, Quiet = .62f;
        static readonly Color Ink = Palette.Hex("#1d1529").WithAlpha(.9f), Gold = Palette.Hex("#ffcf6e"), Blue = Palette.Hex("#8fd0ff"), Rose = Palette.Hex("#d8392b");

        /// <summary>What the controls do (set by the battle screen).</summary>
        public Action<char> Lane;
        public Action Ult, Cheer, Speed, Bag, Log;

        BattleEngine engine;
        RectTransform root, bar, lanes, actions, ultRoot;
        Text title, subtitle, hint, speedText, ultLine;
        Image hintPill, ultRing, ultGlow, bagDot;
        Button ultButton, cheerButton;
        readonly Button[] laneButtons = new Button[3];
        readonly List<Image> actionIcons = new List<Image>();
        CanvasGroup quiet;
        BattleHover hover;
        Vector2 measured;
        bool narrow, roomyHint;

        /// <summary>The control a controller or keyboard starts on (the Ultimate).</summary>
        public Selectable First => ultButton;

        BattleUnit Hero => engine.Heroes.Count > 0 ? engine.Heroes[0] : null;

        /// <summary>Builds the controls on the HUD layer (a safe-area rect over the field).</summary>
        public static BattleDock Create(RectTransform hud, BattleEngine engine)
        {
            var d = hud.gameObject.AddComponent<BattleDock>();
            d.engine = engine;
            d.root = hud;
            d.Build();
            return d;
        }

        void Build()
        {
            Title();
            var hints = UiKit.Node("pad hints", root).Pin(new Vector2(1, 1), new Vector2(-Margin, -44), Vector2.zero);
            UiKit.PadHints(hints, ("A", "Ultimate"), ("LB/RB", "Lane"), ("X", "Cheer"), ("Y", "Speed"));

            var scrim = UiKit.Panel(root, "dock", Color.clear, 0);
            bar = scrim.rectTransform;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(.5f, 0);
            hover = scrim.gameObject.AddComponent<BattleHover>(); // the scrim catches the pointer, so the row wakes up
            quiet = UiKit.Node("quiet", bar).Fill().gameObject.AddComponent<CanvasGroup>();
            quiet.alpha = Quiet;

            lanes = Group("lanes", (RectTransform)quiet.transform);
            for (int i = 0; i < 3; i++)
            {
                char z = LaneKeys[i];
                laneButtons[i] = Dark(lanes, BattleText.Lane(z), null, () => Lane?.Invoke(z), 104);
            }
            hintPill = UiKit.Panel(quiet.transform, "hint", Ink, 18);
            hintPill.raycastTarget = false;
            hint = UiKit.Label(hintPill.transform, "", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter);
            hint.rectTransform.Fill();

            actions = Group("actions", (RectTransform)quiet.transform);
            cheerButton = Dark(actions, "Cheer", "📣", () => Cheer?.Invoke());
            speedText = Dark(actions, "1×", "⏩", () => Speed?.Invoke(), 92).GetComponentInChildren<Text>();
            var bag = Dark(actions, "Bag", "🎒", () => Bag?.Invoke());
            bagDot = UiKit.Panel(bag.transform, "new", Palette.Rose, 6);
            bagDot.raycastTarget = false;
            bagDot.rectTransform.Pin(new Vector2(1, 1), new Vector2(-6, -6), new Vector2(12, 12));
            UiKit.Size(bagDot).ignoreLayout = true;
            Dark(actions, "Log", "📜", () => Log?.Invoke());

            UltButton();
        }

        // the fight's name and where it is, on a soft ink plate
        void Title()
        {
            var plate = UiKit.Panel(root, "title", Ink.WithAlpha(.7f), UiKit.CardRadius);
            plate.raycastTarget = false;
            var r = plate.rectTransform.Pin(new Vector2(0, 1), new Vector2(Margin, -Margin), new Vector2(10, 10));
            UiKit.Column(r, 2, new RectOffset(18, 18, 10, 12));
            UiKit.Hug(r);
            title = UiKit.Label(r, BattleText.Prose(engine.Setup.Title), 26, Palette.Cream, UiKit.Title);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            subtitle = UiKit.Label(r, "", UiKit.SmallSize, Palette.Cream.WithAlpha(.75f), UiKit.Bold);
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        RectTransform Group(string name, RectTransform parent)
        {
            var g = UiKit.Node(name, parent);
            g.anchorMin = g.anchorMax = Vector2.zero;
            UiKit.Row(g, Gap, null, TextAnchor.MiddleCenter);
            UiKit.Hug(g);
            return g;
        }

        // a quiet ink button with an optional icon and words
        Button Dark(Transform parent, string label, string emoji, Action act, float width = -1)
        {
            var b = UiKit.TextButton(parent, label, emoji, Ink, Palette.Cream, act);
            UiKit.Outline(b, Color.white.WithAlpha(.12f), UiKit.ButtonHeight / 2);
            UiKit.Size(b, width, UiKit.ButtonHeight);
            foreach (var img in b.GetComponentsInChildren<Image>()) if (img.name == "icon") actionIcons.Add(img);
            return b;
        }

        // the hero moment: a big round button inside a ring that fills with ink
        void UltButton()
        {
            ultRoot = UiKit.Node("ultimate", root);
            ultRoot.anchorMin = ultRoot.anchorMax = ultRoot.pivot = new Vector2(1, 0);
            ultGlow = Fill(ultRoot, "glow", UiKit.Glow, Gold.WithAlpha(0), -.35f);
            Fill(ultRoot, "track", BattleArt.Ring, Color.white.WithAlpha(.16f), 0);
            ultRing = Fill(ultRoot, "ink", BattleArt.Ring, Gold, 0);
            ultRing.type = Image.Type.Filled;
            ultRing.fillMethod = Image.FillMethod.Radial360;
            ultRing.fillOrigin = (int)Image.Origin360.Top;
            ultRing.fillClockwise = true;

            ultButton = UiKit.Button(ultRoot, "Ultimate button", Ink, () => Ult?.Invoke(), 60);
            ((RectTransform)ultButton.transform).Fill(14);
            var face = (RectTransform)ultButton.transform;
            var icon = UiKit.Icon(face, "✨", 34);
            icon.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 22), new Vector2(34, 34));
            var name = UiKit.Label(face, "Ultimate", UiKit.SmallSize + 2, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter);
            name.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -6), new Vector2(160, 24));
            ultLine = UiKit.Label(face, "", UiKit.SmallSize, Palette.Cream.WithAlpha(.75f), UiKit.Bold, TextAnchor.MiddleCenter);
            ultLine.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -28), new Vector2(160, 20));
            var pad = UiKit.Node("pad", ultRoot).Pin(new Vector2(0, 1), new Vector2(10, -10), new Vector2(24, 24));
            ((RectTransform)UiKit.PadGlyph(pad, "A").transform).Fill();
            pad.gameObject.AddComponent<GamepadOnly>();
        }

        static Image Fill(RectTransform parent, string name, Sprite sprite, Color color, float outset)
        {
            var img = UiKit.Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = new Vector2(-outset, -outset);
            r.anchorMax = new Vector2(1 + outset, 1 + outset);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return img;
        }

        // ---- every frame ----

        /// <summary>Shows the fight as it is: paused or not, the speed, whether a cheer is cooling down.</summary>
        public void Refresh(bool paused, int speed, bool cheerCooling)
        {
            Layout();
            var me = Hero;
            var setup = engine.Setup;
            subtitle.text = paused ? "Paused" : $"{(setup.BookBoss ? "Book Boss" : setup.Guardian ? "Guardian battle" : "Wild battle")} · {BattleText.Prose(setup.Place)}";
            subtitle.color = paused ? Gold : Palette.Cream.WithAlpha(.75f);
            speedText.text = speed + "×";
            cheerButton.interactable = !cheerCooling;
            UiKit.Show(bagDot, TalesSave.Current.Fresh.Count > 0);
            Lanes(me);
            UltState(me);
            if (roomyHint) hintPill.rectTransform.anchoredPosition = new Vector2(Margin + lanes.rect.width + Gap * 2, hintPill.rectTransform.anchoredPosition.y);
            bool alert = me != null && !me.Ko && engine.SlamZone != null && engine.SlamZone[0] == me.Lane;
            quiet.alpha = Mathf.MoveTowards(quiet.alpha, hover.Over || alert ? 1 : Quiet, Time.unscaledDeltaTime * 5);
        }

        // your lane in amber, a slammed lane in red, lanes you can't enter faded; the hint says what to do
        void Lanes(BattleUnit me)
        {
            string slam = engine.SlamZone;
            for (int i = 0; i < 3; i++)
            {
                char z = LaneKeys[i];
                bool mine = me != null && me.Lane == z, slammed = slam != null && slam[0] == z;
                laneButtons[i].interactable = me != null && !me.Ko && (mine || engine.LaneOK(z));
                ((Image)laneButtons[i].targetGraphic).color = slammed ? Rose : mine ? Palette.Amber : Ink;
                laneButtons[i].GetComponentInChildren<Text>().color = mine && !slammed ? Palette.Ink : Palette.Cream;
            }
            bool inSlam = me != null && slam != null && slam[0] == me.Lane;
            hint.text = HintText(me, slam);
            hintPill.color = inSlam && !me.Ko ? Rose : Ink;
        }

        string HintText(BattleUnit me, string slam)
        {
            if (me == null) return "";
            if (me.Ko) return $"{BattleText.Prose(me.Name)} is napping";
            if (slam != null) return $"Slam on the {BattleText.Lane(slam[0])} lane. Pick another lane";
            var live = new HashSet<char>();
            foreach (var f in engine.Foes) if (f.Hp > 0) live.Add(f.Lane);
            if (live.Count == 1) foreach (char z in live) return $"All foes are in the {BattleText.Lane(z)} lane";
            return "Pick a lane with foes to move";
        }

        void UltState(BattleUnit me)
        {
            bool ko = me == null || me.Ko, queued = !ko && me.Hero != null && me.Hero.WantUlt, ready = !ko && engine.UltReady(me);
            int ink = me?.Ink ?? 0;
            ultRing.fillAmount = Mathf.MoveTowards(ultRing.fillAmount, Mathf.Clamp01(ink / 6f), Time.unscaledDeltaTime * 2);
            ultRing.color = queued ? Blue : ready ? Gold : Gold.WithAlpha(.8f);
            ultLine.text = ko ? "Napping" : queued ? "Next turn" : ready ? (me.Hero != null && !me.Hero.AutoUlt ? "Ready! Tap" : "Ready!") : $"{ink} / 6 ink";
            ultButton.interactable = !ko;
            float pulse = ready && !queued ? (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / 1.4f) + 1) / 2 : 0;
            ultGlow.color = (queued ? Blue : Gold).WithAlpha(queued ? .35f : pulse * .55f);
            float s = GameSettings.ReduceMotion ? 1 : 1 + pulse * .05f;
            ultRoot.localScale = new Vector3(s, s, 1);
        }

        // one row when it fits; on narrow screens the lanes sit over the actions and the hint steps aside
        void Layout()
        {
            var size = root.rect.size;
            if ((size - measured).sqrMagnitude < 1) return;
            measured = size;
            narrow = size.x < 900;
            float ult = Mathf.Clamp(size.y * .12f, 96, 136), row = UiKit.ButtonHeight;
            ultRoot.anchoredPosition = new Vector2(-Margin, 10);
            ultRoot.sizeDelta = new Vector2(ult, ult);
            foreach (var icon in actionIcons) UiKit.Show(icon, !narrow);

            float bottom = Margin - 4;
            actions.pivot = narrow ? Vector2.zero : new Vector2(1, 0);
            actions.anchorMin = actions.anchorMax = narrow ? Vector2.zero : new Vector2(1, 0);
            actions.anchoredPosition = narrow ? new Vector2(Margin, bottom) : new Vector2(-(Margin * 2 + ult), bottom);
            lanes.pivot = lanes.anchorMin = lanes.anchorMax = Vector2.zero;
            lanes.anchoredPosition = new Vector2(Margin, narrow ? bottom + row + Gap : bottom);

            bool roomy = size.x >= 1500;
            UiKit.Show(hintPill, !narrow);
            var h = hintPill.rectTransform;
            h.anchorMin = h.anchorMax = h.pivot = Vector2.zero;
            h.sizeDelta = new Vector2(roomy ? Mathf.Min(460, size.x - 1240) : 420, roomy ? row : 36);
            h.anchoredPosition = new Vector2(Margin, roomy ? bottom : bottom + row + Gap);
            roomyHint = roomy;
            bar.sizeDelta = new Vector2(0, narrow ? bottom + row * 2 + Gap + 8 : roomy ? bottom + row + 8 : bottom + row + Gap + 44);
        }
    }

    /// <summary>Notices the pointer (mouse, touch or the gamepad cursor) over a rect: for "quiet until hovered" and "hover pauses".</summary>
    public sealed class BattleHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        public bool Over { get; private set; }
        /// <summary>A press anywhere on the rect (its buttons included).</summary>
        public event Action Pressed;

        public void OnPointerEnter(PointerEventData e) => Over = true;
        public void OnPointerExit(PointerEventData e) => Over = false;
        public void OnPointerDown(PointerEventData e) => Pressed?.Invoke();
        void OnDisable() => Over = false;
    }
}
