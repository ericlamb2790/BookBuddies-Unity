using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Road
{
    /// <summary>
    /// The road's own corner of the HUD: your pet's HP and ink between fights (in the wilds), and the compass
    /// that points the way on ("On to Rosewater" in town, "East to Rosewater · 42 steps" on the road).
    /// Tapping the compass walks you there. It sits under the status pill (above the chat bar on narrow screens).
    /// </summary>
    public sealed class RoadHud : MonoBehaviour
    {
        const float CompactWidth = 640, BarWidth = 150, RefreshEvery = .25f;
        static readonly Color HpGood = Palette.Hex("#4f9a48"), HpLow = Palette.Hex("#e6a23c"), HpBad = Palette.Hex("#d64545");

        PlazaWorld world;
        RectTransform root, column, arrow;
        Image hpFill, goalIcon;
        CanvasGroup group;
        Text hpText, goalTitle, goalSub;
        Image[] pips;
        Button compass;
        Goal goal;
        float refreshAt;
        string shownGoal;

        /// <summary>Where the compass points: a map point, what to call it, and what a tap does.</summary>
        sealed class Goal { public Vector2 At; public string Icon, Title, Sub; public TownMap.Spot Spot; }

        public static RoadHud Create(PlazaWorld world)
        {
            var canvas = UiKit.MakeCanvas("Road HUD", 19);
            var hud = canvas.gameObject.AddComponent<RoadHud>();
            hud.world = world;
            hud.root = (RectTransform)canvas.transform;
            hud.group = canvas.gameObject.AddComponent<CanvasGroup>();
            hud.Build();
            return hud;
        }

        void Build()
        {
            column = UiKit.Node("road", root);
            UiKit.Column(column, 8).childForceExpandWidth = false;
            UiKit.Hug(column);
            if (world.Map.Wild != null) BuildVitals();
            BuildCompass();
        }

        // ❤️ HP [bar] 86%  /  Ink ● ● ● ○ ○ ○
        void BuildVitals()
        {
            var card = UiKit.Panel(column, "vitals", Palette.Cream);
            card.raycastTarget = false;
            var r = card.rectTransform;
            UiKit.Column(r, 6, new RectOffset(14, 16, 10, 12));
            UiKit.Hug(r);
            UiKit.Shadow(r, UiKit.CardRadius, 12, 4, .22f);

            var hp = Row(r);
            UiKit.Icon(hp, "❤️", 20);
            UiKit.Size(UiKit.Label(hp, "HP", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold), 34);
            var track = UiKit.Panel(hp, "bar", Palette.Paper, 6);
            UiKit.Size(track, BarWidth, 12);
            UiKit.Outline(track, Palette.Ink.WithAlpha(.12f), 6, 1);
            hpFill = UiKit.Panel(track.transform, "fill", HpGood, 6);
            hpFill.raycastTarget = false;
            hpFill.rectTransform.Fill();
            hpText = UiKit.Label(hp, "100%", UiKit.SmallSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleRight);
            UiKit.Size(hpText, 46);

            var ink = Row(r);
            UiKit.Icon(ink, "🖋️", 20);
            UiKit.Size(UiKit.Label(ink, "Ink", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold), 34);
            pips = new Image[RoadVitals.MaxInk];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UiKit.Panel(ink, "pip", Palette.Paper, 7);
                UiKit.Size(pips[i], 14, 14);
                UiKit.Outline(pips[i], UiKit.SkyInk.WithAlpha(.45f), 7, 1);
            }
        }

        static RectTransform Row(RectTransform parent)
        {
            var row = UiKit.Node("row", parent);
            UiKit.Row(row, 8).childForceExpandWidth = false;
            UiKit.Size(row, -1, 22);
            return row;
        }

        // [➜] East to Rosewater / Bramble Road · 42 steps
        void BuildCompass()
        {
            compass = UiKit.Button(column, "compass", Palette.Cream, Follow, 24);
            var r = (RectTransform)compass.transform;
            UiKit.Row(r, 12, new RectOffset(8, 18, 6, 6)).childForceExpandWidth = false;
            UiKit.Hug(r);
            UiKit.Size(compass, -1, 56);
            UiKit.Shadow(r, 24, 12, 4, .22f);

            var disc = UiKit.Panel(r, "arrow", Palette.Amber, 20);
            disc.raycastTarget = false;
            UiKit.Size(disc, 40, 40);
            arrow = UiKit.Node("pointer", disc.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(24, 24));
            arrow.pivot = new Vector2(.5f, .5f);
            Bar(arrow, new Vector2(-2, 0), 16, 0);      // shaft
            Bar(arrow, new Vector2(4, 3.6f), 11, -40);   // head
            Bar(arrow, new Vector2(4, -3.6f), 11, 40);

            goalIcon = UiKit.Icon(r, null, 28);
            var words = UiKit.Node("words", r);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Hug(words);
            goalTitle = UiKit.Label(words, "", UiKit.BodySize - 1, Palette.Ink, UiKit.Bold);
            goalTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            goalSub = UiKit.Label(words, "", UiKit.SmallSize, Palette.InkSoft);
            goalSub.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static void Bar(RectTransform parent, Vector2 at, float length, float angle)
        {
            var bar = UiKit.Panel(parent, "bar", Palette.Ink, 2);
            bar.raycastTarget = false;
            var r = bar.rectTransform.Pin(new Vector2(.5f, .5f), at, new Vector2(length, 3.5f));
            r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = at;
            r.localRotation = Quaternion.Euler(0, 0, angle);
        }

        void Update()
        {
            var me = world ? world.Me : null;
            if (me == null) return;
            bool compact = root.rect.width < CompactWidth;
            if (compact) column.Pin(Vector2.zero, new Vector2(16, 84), column.sizeDelta);
            else column.Pin(new Vector2(0, 1), new Vector2(16, -76), column.sizeDelta);
            bool fighting = world.Road != null && world.Road.Fighting;
            group.alpha = Mathf.MoveTowards(group.alpha, PlazaInput.Locked || fighting ? 0 : 1, Time.unscaledDeltaTime / .2f);
            group.blocksRaycasts = group.alpha > .5f;

            if (Time.unscaledTime < refreshAt) { PointArrow(me); return; }
            refreshAt = Time.unscaledTime + RefreshEvery;
            if (pips != null) ShowVitals();
            goal = fighting || UiStack.Any ? null : FindGoal();
            UiKit.Show(compass, goal != null);
            if (goal != null) ShowGoal(me);
            PointArrow(me);
        }

        void ShowVitals()
        {
            var (hp, ink) = RoadVitals.Now(world.Map.Wild == null);
            hpFill.rectTransform.anchorMax = new Vector2((float)hp, 1);
            hpFill.color = hp > .55 ? HpGood : hp > .25 ? HpLow : HpBad;
            hpText.text = Mathf.RoundToInt((float)hp * 100) + "%";
            for (int i = 0; i < pips.Length; i++) pips[i].color = i < ink ? Palette.Sky : Palette.Paper;
        }

        void ShowGoal(PetActor me)
        {
            float d = Vector2.Distance(goal.At, me.Pos);
            string sub = goal.Spot != null ? goal.Sub : d < 3 ? "You’re here" : goal.Sub + " · " + Mathf.Max(1, Mathf.RoundToInt(d)) + " steps";
            string key = goal.Title + "|" + sub;
            if (key == shownGoal) return;
            shownGoal = key;
            UiKit.SetIcon(goalIcon, goal.Icon);
            goalTitle.text = goal.Title;
            goalSub.text = sub;
        }

        // the arrow turns to point from your pet toward the goal on screen
        void PointArrow(PetActor me)
        {
            var cam = Camera.main;
            if (goal == null || cam == null) return;
            Vector2 from = cam.WorldToScreenPoint(TownMap.ToWorld(me.Pos.x, me.Pos.y)), to = cam.WorldToScreenPoint(TownMap.ToWorld(goal.At.x, goal.At.y));
            arrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
        }

        // the site's goalNow: in town, the road out; on the road, the sign at the east end; nothing in the caves
        Goal FindGoal()
        {
            var map = world.Map;
            if (map.Wild == null)
            {
                var road = map.Spots.Find(s => s.Kind == "road");
                string next = NextTown();
                if (road == null || next == null) return null;
                return new Goal { At = road.Use + new Vector2(.5f, .5f), Icon = "🌾", Title = "On to " + next, Sub = "Take Bramble Road east", Spot = road };
            }
            if (!map.Wild.IsRoad || map.Wild.East == null) return null;
            TownMap.Placed sign = null;
            foreach (var o in map.Objects) if (o.Kind == "wsign" && (sign == null || o.X > sign.X)) sign = o;
            if (sign == null) return null;
            return new Goal { At = new Vector2(sign.X + .5f, sign.Y + 1.5f), Icon = map.Wild.East.Icon, Title = "East to " + map.Wild.East.Name, Sub = map.Name };
        }

        string NextTown()
        {
            var data = TalesData.Current;
            int i = System.Array.IndexOf(data.Route, world.Map.Key);
            if (i < 0 || i >= data.Route.Length - 1) return null;
            return data.Towns.TryGetValue(data.Route[i + 1], out var t) ? t.Name : null;
        }

        // a tap on the compass: off to the road's spot in town, or to the far sign on the road
        void Follow()
        {
            if (goal == null) return;
            Sound.Play("boop");
            if (goal.Spot != null) world.GoToSpot(goal.Spot);
            else world.WalkTo(Mathf.FloorToInt(goal.At.x), Mathf.FloorToInt(goal.At.y) + 1);
        }
    }
}
