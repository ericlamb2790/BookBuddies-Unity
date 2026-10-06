using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The battle's words and marks, ported from the site's fx layer: floating numbers (crit, heal, shield, miss), tips
    /// beside or above whoever they're about, the move callout, speech bubbles, banners and toasts. Everything plays and
    /// removes itself; at most 16 numbers and tips live at once. Field effects use the stage's coordinates (reference
    /// pixels from the bottom-left of the safe area); banners and cinematics go on the layer over the whole screen.
    /// Motion and particles are in BattleFxMotion.cs, the big moments in BattleFxCinema.cs.
    /// </summary>
    public sealed partial class BattleFx : MonoBehaviour
    {
        /// <summary>The site's number styles.</summary>
        public enum Num { Normal, Crit, Heal, Shield, Small, Miss }

        static readonly Color Ink = Palette.Hex("#140c1f"), Gold = Palette.Hex("#ffcf6e"), Mint = Palette.Hex("#8ff0b4"), Coral = Palette.Hex("#ff9d8a");
        static readonly float[] SlotX = { 0, -.42f, .42f, -.21f, .21f, 0 };
        const int MaxLive = 16;

        BattleStage stage;
        BattleEngine engine;
        BattleSound sound;
        RectTransform field, over;
        readonly List<GameObject> live = new List<GameObject>();
        readonly Dictionary<object, List<(int i, float end)>> slots = new Dictionary<object, List<(int, float)>>();
        readonly List<(BattleUnitView v, string text)> tips = new List<(BattleUnitView, string)>();
        RectTransform callout, toast;

        /// <summary>1, 2 or 4: how fast the fight plays.</summary>
        public int Speed { get; set; } = 1;

        /// <summary>The site's T(ms): a step of the fight in seconds at the current speed.</summary>
        public float T(float ms) => ms * 2.25f / (2 * Speed) / 1000f;

        float Width => stage.Width;
        float Height => stage.Height;

        /// <summary>Adds the effects player to the stage; over is a full-screen layer above it (cinematics, banners).</summary>
        public static BattleFx Create(BattleStage stage, BattleEngine engine, BattleSound sound, RectTransform over)
        {
            var fx = stage.Fx.gameObject.AddComponent<BattleFx>();
            fx.stage = stage;
            fx.engine = engine;
            fx.sound = sound;
            fx.field = stage.Fx;
            fx.over = over;
            return fx;
        }

        // ---- numbers ----

        /// <summary>A floating number over a fighter ("💥 123" crit, "+40" heal, "🛡️ 25" shield, "miss").</summary>
        public void Number(BattleUnitView v, string text, Num style)
        {
            if (v == null) return;
            float dur = Mathf.Max(.72f, T(1050));
            int i = Slot(("num", v), dur * .5f);
            int size = style == Num.Crit ? 40 : style == Num.Normal || style == Num.Heal ? 30 : 22;
            var color = style == Num.Crit ? Palette.Hex("#ffd66b") : style == Num.Heal ? Mint : style == Num.Shield ? Palette.Hex("#9fd8ff")
                : style == Num.Miss ? Palette.Hex("#d6dcff") : Color.white;
            var n = Words(field, text, size, color, UiKit.Bold, false);
            Stroke(n.GetComponentInChildren<Text>(), 4);
            var c = v.Center;
            float ox = SlotX[i % 6] * Mathf.Min(v.Width, 110), w = n.sizeDelta.x;
            n.anchoredPosition = new Vector2(Mathf.Clamp(c.x + ox, w / 2 + 4, Width - w / 2 - 4), Mathf.Clamp(c.y + v.Width * .3f + i % 6 * 22, 4, Height - 44));
            float pop = style == Num.Crit ? 1.22f : 1.1f;
            KeyTween.Play(n, dur, BattleEase.Soft, 0, true, new Kf(0, 0, -12, 0, .55f, 0), new Kf(.16f, 0, 8, 0, pop), new Kf(.62f, 0, 16), new Kf(1, 0, 34, 0, .94f, 0));
            Keep(n.gameObject);
        }

        // ---- tips ----

        /// <summary>A short pill about a fighter (or the whole fight when v is null). The same words on several fighters at once merge into "… ×N".</summary>
        public void Tip(BattleUnitView v, string text)
        {
            if (!string.IsNullOrEmpty(text)) tips.Add((v, BattleText.Prose(text)));
        }

        void LateUpdate()
        {
            if (tips.Count == 0) return;
            var batch = new List<(BattleUnitView v, string text)>(tips);
            tips.Clear();
            var groups = new Dictionary<string, List<BattleUnitView>>();
            var order = new List<string>();
            var alone = new HashSet<string>();
            foreach (var (v, text) in batch)
            {
                if (!groups.TryGetValue(text, out var list)) { groups[text] = list = new List<BattleUnitView>(); order.Add(text); }
                if (v == null) alone.Add(text);
                else if (!list.Contains(v)) list.Add(v);
            }
            foreach (var text in order)
            {
                var all = groups[text];
                if (all.Count == 0 && alone.Contains(text)) TipAt(all, text);
                foreach (var side in new[] { all.FindAll(v => v.Unit.IsFoe), all.FindAll(v => !v.Unit.IsFoe) })
                    if (side.Count > 0) TipAt(side, side.Count > 1 ? $"{text} ×{side.Count}" : text);
            }
        }

        // tip1: above foes, beside a single pet, below a group of pets, or centred high up
        void TipAt(List<BattleUnitView> who, string text)
        {
            bool foe = who.Count > 0 && who[0].Unit.IsFoe, mid = who.Count == 0;
            float dur = Mathf.Max(.95f, T(1350));
            int i = Slot(("tip", mid ? null : who.Count == 1 ? who[0] : (object)(foe ? "foes" : "pets")), dur * .7f);
            var pill = Pill(field, text, mid ? 18 : 16, mid ? Gold : foe ? Coral : Mint);
            Vector2 size = pill.sizeDelta;
            float x, top;
            if (mid) { x = (Width - size.x) / 2; top = Height * .7f - i * (size.y + 6); }
            else if (foe)
            {
                float highest = 0, cx = 0;
                foreach (var v in who) { highest = Mathf.Max(highest, v.Top.y); cx += v.Center.x / who.Count; }
                x = cx - size.x / 2;
                top = highest + 8 + size.y + i * (size.y + 5);
            }
            else if (who.Count == 1)
            {
                var v = who[0];
                bool right = v.Center.x < Width / 2;
                x = right ? v.Center.x + v.Size * .45f : v.Center.x - v.Size * .45f - size.x;
                top = v.Center.y + v.Width * .42f - i * (size.y + 5);
            }
            else
            {
                float lowest = float.MaxValue, cx = 0;
                foreach (var v in who) { lowest = Mathf.Min(lowest, v.Feet.y); cx += v.Center.x / who.Count; }
                x = cx - size.x / 2;
                top = lowest - 6 - i * (size.y + 5);
            }
            pill.pivot = new Vector2(0, 1);
            pill.anchoredPosition = new Vector2(Mathf.Clamp(x, 6, Width - size.x - 6), Mathf.Clamp(top, size.y + 6, Height - 6));
            KeyTween.Play(pill, dur, BattleEase.Out, 0, true, new Kf(0, 0, -6, 0, .92f, 0), new Kf(.12f), new Kf(.82f, 0, 3), new Kf(1, 0, 10, 0, 1, 0));
            Keep(pill.gameObject);
        }

        // flSlot: the first free row for this key; rows free up after hold seconds
        int Slot(object key, float hold)
        {
            float now = Time.time;
            if (!slots.TryGetValue(key, out var used)) slots[key] = used = new List<(int, float)>();
            used.RemoveAll(s => s.end <= now);
            int i = 0;
            while (used.Exists(s => s.i == i)) i++;
            used.Add((i, now + hold));
            if (slots.Count > 40) foreach (var k in new List<object>(slots.Keys)) if (slots[k].Count == 0) slots.Remove(k);
            return i;
        }

        void Keep(GameObject go)
        {
            live.RemoveAll(g => !g);
            live.Add(go);
            while (live.Count > MaxLive) { Destroy(live[0]); live.RemoveAt(0); }
        }

        // ---- callout, bubble, banner, toast ----

        /// <summary>The move's pill at the top of the field: its signature shape, who did it and the move name. Rose for foes, mint for support.</summary>
        public void Callout(BattleEvent e, string line = null)
        {
            if (callout) { var old = callout; KeyTween.Play(old, .14f, BattleEase.Out, 0, true, new Kf(0), new Kf(1, 0, 14, 0, 1, 0)); }
            var actor = engine.Find(e.Actor);
            bool support = e.Kind == "heal" || e.Kind == "shield" || e.Kind == "fate" && e.Result != "fumble";
            var edge = e.Foe || e.Result == "fumble" ? Coral : support ? Mint : Gold;
            var pill = UiKit.Panel(field, "callout", Ink.WithAlpha(.92f), 30);
            pill.raycastTarget = false;
            UiKit.Outline(pill, edge, 30, 2);
            var r = pill.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(.5f, .5f);

            var sg = Sg.Of(e, engine);
            var disc = Picture(r, Art.Disc, Color.white.WithAlpha(.08f), new Vector2(30, 30), new Vector2(40, 40));
            Picture(disc.rectTransform, BattleArt.Shape(sg.Shape), sg.Color, new Vector2(20, 20), new Vector2(28, 28));
            string who = actor != null && e.Kind != "intro" ? BattleText.Prose(UiKit.SplitEmoji(actor.Name, out _)) : "";
            string move = BattleText.Prose(UiKit.SplitEmoji(line ?? e.Name, out _));
            var big = Text(r, move, 20, Palette.Cream, UiKit.Bold);
            var small = who.Length > 0 ? Text(r, who, 15, Palette.Cream.WithAlpha(.7f), UiKit.Bold) : null;
            float w = Mathf.Min(Mathf.Max(big.preferredWidth, small ? small.preferredWidth : 0), Width * .6f);
            r.sizeDelta = new Vector2(58 + w + 22, 60);
            Place(big.rectTransform, new Vector2(58, small ? 8 : 18), w, 26);
            if (small) Place(small.rectTransform, new Vector2(58, 33), w, 20);

            r.anchoredPosition = new Vector2(Width / 2, Height - (Width < 900 ? 112 : 58));
            float hold = Mathf.Max(.8f, T(1000));
            KeyTween.Play(r, hold, BattleEase.Out, 0, true, new Kf(0, 0, -8, 0, .6f, 0), new Kf(.12f, 0, 0, 0, 1.06f), new Kf(.2f), new Kf(.85f), new Kf(1, 0, 10, 0, 1, 0));
            callout = r;
        }

        /// <summary>A speech bubble over a fighter (foes speak in rose italics). Returns how long to wait before the next line.</summary>
        public float Bubble(BattleUnitView v, string text, bool foe)
        {
            if (v == null || string.IsNullOrEmpty(text)) return 0;
            text = BattleText.Prose(UiKit.SplitEmoji(text, out _));
            float dur = Mathf.Clamp(T(700 + text.Length * 24), 1.1f, 2.8f);
            var bg = UiKit.Panel(field, "bubble", foe ? Palette.Hex("#2a0f1c") : Palette.Hex("#fff6e4"), 16);
            bg.raycastTarget = false;
            if (foe) UiKit.Outline(bg, Palette.Hex("#ff8aa0").WithAlpha(.8f), 16, 2);
            var r = bg.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            var t = Text(r, text, 19, foe ? Palette.Hex("#ffe3ec") : Palette.Hex("#2a1c10"), UiKit.Title);
            t.fontStyle = foe ? FontStyle.Italic : FontStyle.Normal;
            float maxW = Mathf.Min(380, Width * .6f), w = Mathf.Min(maxW, t.preferredWidth + 2);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(t.rectTransform, new Vector2(16, 11), w, 0);
            float h = t.preferredHeight;
            t.rectTransform.sizeDelta = new Vector2(w, h);
            r.sizeDelta = new Vector2(w + 32, h + 22);

            var c = v.Center;
            float x = Mathf.Clamp(c.x - r.sizeDelta.x / 2, 6, Width - r.sizeDelta.x - 6), y = Mathf.Min(v.Top.y + 14, Height - r.sizeDelta.y - 6);
            r.anchoredPosition = new Vector2(x, y);
            var tail = UiKit.Panel(r, "tail", bg.color, 3).rectTransform;
            tail.GetComponent<Image>().raycastTarget = false;
            tail.anchorMin = tail.anchorMax = Vector2.zero;
            tail.sizeDelta = new Vector2(14, 14);
            tail.anchoredPosition = new Vector2(Mathf.Clamp(c.x - x, 16, r.sizeDelta.x - 16), 1);
            tail.localRotation = Quaternion.Euler(0, 0, 45);
            tail.SetAsFirstSibling();

            KeyTween.Play(r, dur, BattleEase.Out, 0, true, new Kf(0, 0, -10, 0, .85f, 0), new Kf(Mathf.Min(.15f, .25f / dur)), new Kf(.9f), new Kf(1, 0, 8, 0, 1, 0));
            if (foe) KeyTween.Play(v.Motion, T(420), BattleEase.Linear, new Kf(0), Kf.Squash(.5f, 0, 0, 0, 1.08f, .94f), new Kf(1));
            return dur * .72f;
        }

        /// <summary>The big line over the field ("Victory!"), with an optional small line under it; sad is the cool blue of a loss.</summary>
        public void Banner(string big, string small, bool sad)
        {
            var b = UiKit.Node("banner", over);
            b.anchorMin = b.anchorMax = new Vector2(.5f, .88f);
            b.pivot = new Vector2(.5f, 1);
            b.sizeDelta = new Vector2(1000, 150);
            int size = Mathf.RoundToInt(Mathf.Clamp(over.rect.width / 14, 36, 64));
            var t = Text(b, BattleText.Prose(UiKit.SplitEmoji(big, out _)), size, sad ? Palette.Hex("#cfd8ff") : Gold, UiKit.Title);
            t.alignment = TextAnchor.UpperCenter;
            t.rectTransform.Pin(new Vector2(.5f, 1), Vector2.zero, new Vector2(1000, size * 1.3f));
            var shade = t.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(0, 0, 0, .45f);
            shade.effectDistance = new Vector2(0, -4);
            if (!string.IsNullOrEmpty(small))
            {
                var pill = Pill(b, small, 18, Color.clear);
                pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(.5f, 1);
                pill.anchoredPosition = new Vector2(0, -size * 1.3f - 8);
            }
            KeyTween.Play(b, T(1700), BattleEase.Out, 0, true, new Kf(0, 0, 0, 0, .4f, 0), new Kf(.15f, 0, 0, 0, 1.1f), new Kf(.25f), new Kf(.85f), new Kf(1, 0, 14, 0, 1, 0));
        }

        /// <summary>A note at the top of the screen for a few seconds (a slam on your lane, a refused move). One at a time.</summary>
        public void Toast(string text)
        {
            if (toast) Destroy(toast.gameObject);
            toast = Pill(over, text, 18, Gold);
            toast.anchorMin = toast.anchorMax = toast.pivot = new Vector2(.5f, 1);
            toast.anchoredPosition = new Vector2(0, -110); // under the title, clear of the notch
            KeyTween.Play(toast, 3.2f, BattleEase.Linear, 0, true, new Kf(0, 0, -6, 0, 1, 0), new Kf(.08f), new Kf(.85f), new Kf(1, 0, 0, 0, 1, 0));
        }

        // ---- building blocks ----

        // an ink pill with the first emoji as an icon and the words, sized to fit; edge is its thin border colour
        RectTransform Pill(RectTransform parent, string text, int size, Color edge)
        {
            string words = UiKit.SplitEmoji(text, out var icon);
            var bg = UiKit.Panel(parent, "pill", Ink.WithAlpha(.9f), 18);
            bg.raycastTarget = false;
            if (edge.a > 0) UiKit.Outline(bg, edge.WithAlpha(.45f), 18, 2);
            var r = bg.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            float x = 14, h = size + 18;
            if (icon != null)
            {
                Picture(r, Art.Emote(icon), Color.white, new Vector2(x + size * .6f, h / 2), Vector2.one * (size + 4));
                x += size + 10;
            }
            var t = Text(r, words, size, Palette.Cream, UiKit.Bold);
            float w = Mathf.Min(t.preferredWidth, Mathf.Max(200, Width * .7f));
            Place(t.rectTransform, new Vector2(x, (h - size - 6) / 2), w, size + 6);
            r.sizeDelta = new Vector2(x + w + 14, h);
            return r;
        }

        // a number or short word with an emoji icon in front, centred on its anchor point
        RectTransform Words(RectTransform parent, string text, int size, Color color, Font font, bool wrap)
        {
            string words = UiKit.SplitEmoji(text, out var icon);
            var r = UiKit.Node("words", parent);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(.5f, 0);
            float x = 0;
            if (icon != null)
            {
                Picture(r, Art.Emote(icon), Color.white, new Vector2(size * .45f, size * .6f), Vector2.one * size * .9f);
                x = size * .95f;
            }
            var t = Text(r, words, size, color, font);
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            float w = t.preferredWidth;
            Place(t.rectTransform, new Vector2(x, 0), w, size * 1.25f);
            r.sizeDelta = new Vector2(x + w, size * 1.25f);
            return r;
        }

        static Text Text(RectTransform parent, string text, int size, Color color, Font font)
        {
            var t = UiKit.Label(parent, text, size, color, font);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        // pins r at a bottom-left offset inside its parent with a fixed size
        static void Place(RectTransform r, Vector2 at, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            r.anchoredPosition = at;
            r.sizeDelta = new Vector2(w, h);
        }

        // the site's dark text stroke (paint-order stroke) and soft drop shadow, for numbers over busy art
        static void Stroke(Text t, float width)
        {
            var dark = Palette.Hex("#1a0f22");
            var a = t.gameObject.AddComponent<Outline>();
            a.effectColor = dark;
            a.effectDistance = new Vector2(width * .7f, width * .7f);
            var b = t.gameObject.AddComponent<Outline>();
            b.effectColor = dark;
            b.effectDistance = new Vector2(width, 0);
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, .55f);
            s.effectDistance = new Vector2(0, -width - 2);
        }

        /// <summary>An image centred at a point of its parent's bottom-left space.</summary>
        static Image Picture(RectTransform parent, Sprite sprite, Color color, Vector2 at, Vector2 size)
        {
            var img = UiKit.Node("fx", parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = at;
            r.sizeDelta = size;
            return img;
        }
    }
}
