using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The side panel's move list: one live row per move, in the order the pet tries them. Each row charges a bar
    /// with ink toward the move's cost (or counts down a cooldown), pops when the move comes ready, and flashes gold
    /// when the pet uses it. Drag a row by its grip to reorder: the order is the pet's tactics ("My order"), it takes
    /// effect from the next turn and is saved for later fights. Reset goes back to Smart. In a party fight the order stays
    /// as it came in (every game plays the pet the same way), so the list only shows it. Reduce motion keeps the
    /// colours and drops the slides, pops and punches.
    /// </summary>
    public sealed class BattleMoveList : MonoBehaviour
    {
        const float RowH = 44, Gap = 6, Slide = 14, FlashTime = .7f, PopTime = .45f;
        static readonly Color RowBase = Color.white.WithAlpha(.06f), RowHover = Color.white.WithAlpha(.1f), Track = new Color(0, 0, 0, .35f);
        static readonly Color Gold = Palette.Hex("#ffd27a"), Leaf = Palette.Hex("#7be08a"), Dim = Palette.Hex("#b9b1dd"), Charge = Palette.Hex("#8fd0ff");

        BattleEngine engine;
        RectTransform rows;
        Text header;
        Button reset;
        readonly List<Row> list = new List<Row>(); // in the shown order
        Row dragging;
        float grab;

        BattleUnit Me => engine.Me;
        bool Calm => GameSettings.ReduceMotion;
        bool Locked => engine.Setup.Party.Count > 0;

        /// <summary>Builds the card on a side panel column; null when the pet has no moves.</summary>
        public static BattleMoveList Create(Transform side, BattleEngine engine)
        {
            var me = engine.Me;
            if (me?.Hero == null || me.Moves.Count == 0) return null;
            var card = UiKit.Panel(side, "moves", Palette.Hex("#15122c").WithAlpha(.88f), UiKit.CardRadius);
            card.raycastTarget = false;
            var l = card.gameObject.AddComponent<BattleMoveList>();
            l.engine = engine;
            l.Build(card.rectTransform, me);
            return l;
        }

        void Build(RectTransform card, BattleUnit me)
        {
            UiKit.Column(card, 8, new RectOffset(14, 14, 12, 14));
            var top = UiKit.Node("header", card);
            UiKit.Row(top, 8, null, TextAnchor.MiddleLeft);
            UiKit.Size(top, -1, 26);
            header = UiKit.Label(top, "", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold);
            UiKit.Size(header, -1, -1, 1);
            reset = UiKit.TextButton(top, "Reset", null, Color.white.WithAlpha(.08f), Dim, ResetOrder, 26);
            UiKit.Size(reset, 64, 26);

            rows = UiKit.Node("rows", card);
            foreach (var key in Ordered(me)) list.Add(Row.Make(this, rows, me.Moves.Find(m => m.Key == key)));
            UiKit.Size(rows, -1, list.Count * (RowH + Gap) - Gap);
            for (int i = 0; i < list.Count; i++) list[i].Y = Target(i);
        }

        // the saved order first (the moves the pet still knows), then the rest as the kit lists them
        static List<string> Ordered(BattleUnit me)
        {
            var keys = new List<string>();
            if (me.Hero.Order != null) foreach (var k in me.Hero.Order) if (!keys.Contains(k) && me.Moves.Exists(m => m.Key == k)) keys.Add(k);
            foreach (var m in me.Moves) if (!keys.Contains(m.Key)) keys.Add(m.Key);
            return keys;
        }

        static float Target(int i) => -i * (RowH + Gap);

        /// <summary>A battle event played: the pet's move flashes its row.</summary>
        public void Played(BattleEvent e)
        {
            var me = Me;
            if (me == null || e.Actor != me.Key || e.Move == null) return;
            var r = list.Find(x => x.Move.Key == e.Move);
            if (r != null) r.Flash = 1;
        }

        void Update()
        {
            var me = Me;
            if (me?.Hero == null) return;
            bool mine = me.Hero.Order != null && me.Hero.Order.Count > 0;
            header.text = Locked ? (mine ? "Moves  <color=#b9b1dd>your order · party fight</color>" : "Moves  <color=#b9b1dd>smart · party fight</color>")
                : mine ? $"Moves  <color=#b9b1dd>your order · drag to change</color>" : $"Moves  <color=#b9b1dd>smart · drag to set an order</color>";
            UiKit.Show(reset, mine && !Locked);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != dragging) r.Y = Calm ? Target(i) : Mathf.Lerp(r.Y, Target(i), 1 - Mathf.Exp(-Slide * dt));
                r.Tick(me, dt);
            }
        }

        // ---- reordering ----

        void BeginDrag(Row r, PointerEventData e)
        {
            if (Locked) return;
            dragging = r;
            r.transform.SetAsLastSibling();
            grab = r.Y - Local(e).y;
        }

        void Drag(Row r, PointerEventData e)
        {
            if (dragging != r) return;
            float max = Target(list.Count - 1);
            r.Y = Mathf.Clamp(Local(e).y + grab, max - RowH / 2, RowH / 2);
            int to = Mathf.Clamp(Mathf.RoundToInt(-r.Y / (RowH + Gap)), 0, list.Count - 1), from = list.IndexOf(r);
            if (to == from) return;
            list.RemoveAt(from);
            list.Insert(to, r);
        }

        void EndDrag(Row r)
        {
            if (dragging != r) return;
            dragging = null;
            var keys = list.ConvertAll(x => x.Move.Key);
            var h = Me?.Hero;
            if (h == null || h.Order != null && h.Order.Count == keys.Count && h.Order.TrueForAll(k => keys.IndexOf(k) == h.Order.IndexOf(k))) return;
            Save(keys);
        }

        void ResetOrder()
        {
            Save(null);
            var me = Me;
            var keys = Ordered(me);
            list.Sort((a, b) => keys.IndexOf(a.Move.Key).CompareTo(keys.IndexOf(b.Move.Key)));
        }

        // the pet follows it from its next turn; the save keeps it for every later fight
        void Save(List<string> keys)
        {
            var h = Me?.Hero;
            if (h != null) h.Order = keys;
            var save = TalesSave.Current;
            save.Me.Order = keys == null ? null : new List<string>(keys);
            save.Touch();
            AutoSave.Now("move order");
        }

        Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rows, e.position, e.pressEventCamera, out var p);
            return p - new Vector2(0, rows.rect.height * (1 - rows.pivot.y)); // from the rows' top edge
        }

        /// <summary>One move: grip, icon, name, a charge bar and a badge (ready, N ink, N turns, next turn).</summary>
        sealed class Row : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public MoveDef Move;
            public float Flash;
            BattleMoveList owner;
            RectTransform r, fill;
            Image bg, fillImg, ring;
            Text badge, title;
            float shown = -1, pop;
            bool ready, over;

            /// <summary>The row's top edge, down from the list's top (the pivot is its centre, so punches grow evenly).</summary>
            public float Y
            {
                get => r.anchoredPosition.y + RowH / 2;
                set => r.anchoredPosition = new Vector2(0, value - RowH / 2);
            }

            public static Row Make(BattleMoveList owner, RectTransform parent, MoveDef m)
            {
                var bg = UiKit.Panel(parent, m.Key, RowBase, 12);
                var row = bg.gameObject.AddComponent<Row>();
                row.owner = owner;
                row.Move = m;
                row.bg = bg;
                var r = row.r = bg.rectTransform;
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = Vector2.one;
                r.sizeDelta = new Vector2(0, RowH);

                row.ring = UiKit.Outline(bg, Gold.WithAlpha(0), 12, 2);
                row.ring.raycastTarget = false;
                if (!owner.Locked) Grip(r);
                var icon = UiKit.Icon(r, m.Icon ?? (m.Ult ? "✨" : "📖"), 24);
                icon.raycastTarget = false;
                icon.rectTransform.Pin(new Vector2(0, .5f), new Vector2(42, 2), new Vector2(24, 24));
                row.title = UiKit.Label(r, BattleText.Prose(m.Name ?? m.Key), UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold);
                row.title.raycastTarget = false;
                Stretch(row.title.rectTransform, 60, 92, 4);
                row.badge = UiKit.Label(r, "", UiKit.SmallSize, Dim, UiKit.Bold, TextAnchor.MiddleRight);
                row.badge.raycastTarget = false;
                row.badge.rectTransform.anchorMin = new Vector2(1, 0);
                row.badge.rectTransform.anchorMax = Vector2.one;
                row.badge.rectTransform.pivot = new Vector2(1, .5f);
                row.badge.rectTransform.offsetMin = new Vector2(-88, 4);
                row.badge.rectTransform.offsetMax = new Vector2(-12, 0);

                var track = UiKit.Panel(r, "track", Track, 2);
                track.raycastTarget = false;
                var t = track.rectTransform;
                t.anchorMin = Vector2.zero;
                t.anchorMax = new Vector2(1, 0);
                t.pivot = new Vector2(.5f, 0);
                t.offsetMin = new Vector2(60, 6);
                t.offsetMax = new Vector2(-12, 10);
                row.fillImg = UiKit.Panel(t, "fill", Charge, 2);
                row.fillImg.raycastTarget = false;
                row.fill = row.fillImg.rectTransform;
                row.fill.anchorMin = Vector2.zero;
                row.fill.anchorMax = new Vector2(0, 1);
                row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;
                return row;
            }

            // six dots on the left: the place to grab
            static void Grip(RectTransform r)
            {
                for (int i = 0; i < 6; i++)
                {
                    var d = UiKit.Node("grip", r).gameObject.AddComponent<Image>();
                    d.sprite = Art.Disc;
                    d.color = Dim.WithAlpha(.7f);
                    d.raycastTarget = false;
                    d.rectTransform.Pin(new Vector2(0, .5f), new Vector2(14 + i % 2 * 7, 7 - i / 2 * 7), new Vector2(4, 4));
                }
            }

            static void Stretch(RectTransform t, float left, float right, float lift)
            {
                t.anchorMin = Vector2.zero;
                t.anchorMax = Vector2.one;
                t.offsetMin = new Vector2(left, lift);
                t.offsetMax = new Vector2(-right, 0);
            }

            public void Tick(BattleUnit me, float dt)
            {
                bool calm = owner.Calm;
                int cd = me.Cds.TryGetValue(Move.Key, out var c) ? c : 0;
                int cost = Move.Ult ? 6 : owner.engine.InkCost(me, Move);
                bool queued = Move.Ult && me.Hero.WantUlt;
                float target = cd > 0 ? 0 : cost <= 0 ? 1 : Mathf.Clamp01(me.Ink / (float)cost);
                bool nowReady = !me.Ko && cd == 0 && target >= 1;

                shown = shown < 0 || calm ? target : Mathf.MoveTowards(shown, target, dt * 2.5f);
                fill.anchorMax = new Vector2(shown, 1);
                fillImg.color = nowReady ? (Move.Ult ? Gold : Leaf) : Charge;
                if (nowReady && !ready) pop = 1;
                ready = nowReady;

                badge.text = me.Ko ? "napping" : queued ? "next turn" : cd > 0 ? $"{cd} turn{(cd == 1 ? "" : "s")}"
                    : nowReady ? "ready" : $"{me.Ink}/{cost} ink";
                badge.color = queued || nowReady && Move.Ult ? Gold : nowReady ? Leaf : Dim;
                title.color = nowReady || queued ? Palette.Cream : Palette.Cream.WithAlpha(.6f);

                // pop: a quick swell when the move comes ready; flash: gold wash and a punch when the pet uses it
                pop = Mathf.Max(0, pop - dt / PopTime);
                Flash = Mathf.Max(0, Flash - dt / FlashTime);
                float f = Flash * Flash;
                bg.color = Color.Lerp(over ? RowHover : RowBase, Gold.WithAlpha(.35f), f);
                ring.color = Gold.WithAlpha(Mathf.Max(f, queued ? .55f + .25f * Mathf.Sin(Time.unscaledTime * 5) : 0));
                float s = calm ? 1 : 1 + .06f * Mathf.Sin(pop * Mathf.PI) + .08f * f + (owner.dragging == this ? .03f : 0);
                r.localScale = new Vector3(s, s, 1);
            }

            public void OnBeginDrag(PointerEventData e) => owner.BeginDrag(this, e);
            public void OnDrag(PointerEventData e) => owner.Drag(this, e);
            public void OnEndDrag(PointerEventData e) => owner.EndDrag(this);
            public void OnPointerEnter(PointerEventData e) => over = true;
            public void OnPointerExit(PointerEventData e) => over = false;
            void OnDisable() { over = false; owner?.EndDrag(this); }
        }
    }
}
