using System.Collections.Generic;
using System.Text;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Two read-only panels in the room beside the lane columns, shown only when there's space (1080p and wider):
    /// your pet on the left (stats, effects, the move list (BattleMoveList), the last turns) and the foes on the right (turn
    /// order, each foe's lane, stats, genre, special cooldown and effects). They refresh a few times a second.
    /// </summary>
    public sealed class BattleSides : MonoBehaviour
    {
        const float MinRoom = 300, Margin = 20, Refresh = .25f;
        static readonly HashSet<string> Good = new HashSet<string> { "shield", "regen", "crit", "wind", "pow", "dodge", "taunt" };
        static readonly Color Card = Palette.Hex("#15122c").WithAlpha(.88f);
        const string Dim = "#b9b1dd", Gold = "#ffd27a", Leaf = "#7be08a", Rose = "#ff8a7a", Sky = "#8fd0ff";

        BattleEngine engine;
        BattleStage stage;
        BattleLog log;
        RectTransform left, right;
        Text pet, last, order, foes;
        BattleMoveList moves;
        float nextAt;

        /// <summary>A battle event played (the move list flashes the pet's move).</summary>
        public void Played(BattleEvent e) { if (moves) moves.Played(e); }

        public static BattleSides Create(RectTransform hud, BattleEngine engine, BattleStage stage, BattleLog log)
        {
            var s = hud.gameObject.AddComponent<BattleSides>();
            s.engine = engine;
            s.stage = stage;
            s.log = log;
            s.left = s.Side(hud, "your pet");
            s.pet = s.Box(s.left);
            s.moves = BattleMoveList.Create(s.left, engine);
            s.last = s.Box(s.left);
            s.right = s.Side(hud, "foes");
            s.order = s.Box(s.right);
            s.foes = s.Box(s.right);
            return s;
        }

        RectTransform Side(RectTransform hud, string name)
        {
            var r = UiKit.Node(name, hud);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(0, 1);
            UiKit.Column(r, 10);
            r.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return r;
        }

        // one card: a single rich-text label on a soft ink plate
        Text Box(RectTransform side)
        {
            var card = UiKit.Panel(side, "card", Card, UiKit.CardRadius);
            card.raycastTarget = false;
            UiKit.Column(card.rectTransform, 0, new RectOffset(16, 16, 12, 14));
            var t = UiKit.Label(card.transform, "", UiKit.SmallSize + 1, Palette.Cream, UiKit.Bold, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.1f;
            return t;
        }

        void Update()
        {
            float room = stage.SideRoom - Margin * 2;
            bool show = room >= MinRoom;
            UiKit.Show(left, show);
            UiKit.Show(right, show);
            if (!show) return;
            var (top, _) = stage.Field;
            float w = Mathf.Min(room, 420);
            left.sizeDelta = right.sizeDelta = new Vector2(w, left.sizeDelta.y);
            left.anchoredPosition = new Vector2(stage.SideRoom - Margin - w, top);
            right.anchoredPosition = new Vector2(stage.Width - stage.SideRoom + Margin, top);
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + Refresh;
            var me = engine.Heroes.Count > 0 ? engine.Heroes[0] : null;
            Show(pet, me == null ? null : PetText(me));
            Show(last, LastText());
            Show(order, OrderText());
            Show(foes, FoesText());
        }

        static void Show(Text t, string text)
        {
            UiKit.Show(t.transform.parent, !string.IsNullOrEmpty(text));
            if (t.text != text) t.text = text ?? "";
        }

        // ---- the left panel ----

        string PetText(BattleUnit me)
        {
            var sb = new StringBuilder();
            sb.Append($"<size={UiKit.SmallSize + 7}>{BattleText.Prose(me.Name)}</size>  <color={Dim}>Lv {(int)me.Lvl} · {BattleText.Lane(me.Lane)} lane</color>\n");
            sb.Append($"Atk {Stat(me.Atk, me.S("pow") > 0, me.S("weak") > 0)}   Def {Stat(me.Def, me.S("shield") > 0, me.S("expose") > 0)}   Spd {Stat(me.Spd, me.S("wind") > 0, me.S("stun") > 0)}   <color={Sky}>Ink {me.Ink}/6</color>");
            string fx = Effects(me);
            if (fx != "") sb.Append('\n').Append(fx);
            return sb.ToString();
        }

        static string Stat(double v, bool up, bool down) => up ? $"<color={Leaf}>{(int)v}</color>" : down ? $"<color={Rose}>{(int)v}</color>" : ((int)v).ToString();

        static string Effects(BattleUnit u)
        {
            var sb = new StringBuilder();
            foreach (var kv in u.St)
            {
                if (kv.Value <= 0) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append($"<color={(Good.Contains(kv.Key) ? Sky : Rose)}>{Cap(kv.Key)} {(int)kv.Value}</color>");
            }
            return sb.ToString();
        }

        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        string LastText()
        {
            var sb = new StringBuilder();
            foreach (var line in log.Last(3)) sb.Append(sb.Length == 0 ? $"<size={UiKit.SmallSize + 4}>Last turns</size>" : "").Append('\n').Append(line);
            return sb.ToString();
        }

        // ---- the right panel ----

        string OrderText()
        {
            var sb = new StringBuilder($"<size={UiKit.SmallSize + 4}>Turn order</size>\n");
            int n = 0;
            foreach (var key in engine.TurnQueue)
            {
                var u = Find(key);
                if (u == null || !u.Alive) continue;
                if (n++ > 0) sb.Append($" <color={Dim}>›</color> ");
                sb.Append(u.IsFoe ? $"<color={Rose}>{Short(u.BaseName ?? u.Name)}</color>" : $"<color={Leaf}>{Short(u.Name)}</color>");
            }
            return n == 0 ? null : sb.ToString();
        }

        string FoesText()
        {
            var sb = new StringBuilder();
            int alive = 0;
            foreach (var f in engine.Foes)
            {
                if (!f.Alive) continue;
                alive++;
                sb.Append('\n').Append($"<b>{BattleText.Prose(f.BaseName ?? f.Name)}</b>  <color={Gold}>Lv{(int)f.Lvl}</color>{(f.Boss ? $"  <color={Rose}>Boss</color>" : "")}\n");
                sb.Append($"<color={Dim}>{BattleText.Lane(f.Lane)} · Atk {(int)f.Atk} · Def {(int)f.Def} · Spd {(int)f.Spd} · {TalesData.Current.GenreLabel(f.Gen)}</color>");
                if (f.Moves.Count > 1)
                {
                    var sp = f.Moves[1];
                    int cd = f.Cds.TryGetValue(sp.Key, out var c) ? c : 0;
                    sb.Append($"\nSpecial: {BattleText.Prose(sp.Name ?? sp.Key)}  {(cd > 0 ? $"<color={Dim}>in {cd}</color>" : $"<color={Rose}>ready</color>")}");
                }
                string fx = Effects(f);
                if (fx != "") sb.Append('\n').Append(fx);
            }
            return alive == 0 ? null : $"<size={UiKit.SmallSize + 4}>Foes</size>  <color={Dim}>{alive} left</color>" + sb;
        }

        BattleUnit Find(string key)
        {
            foreach (var u in engine.Heroes) if (u.Key == key) return u;
            foreach (var u in engine.Foes) if (u.Key == key) return u;
            return null;
        }

        // the first word of a name, for the turn order line
        static string Short(string name)
        {
            name = BattleText.Prose(name ?? "");
            int sp = name.IndexOf(' ');
            return sp > 0 ? name.Substring(0, sp) : name;
        }
    }
}
