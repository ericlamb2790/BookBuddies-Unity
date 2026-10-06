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
    /// The road's own corner of the HUD: the objective card under the pet card (and as wide), pointing the way on
    /// ("Beat Inspector Fogg" while a Book Boss seals the road, "On to Rosewater" in town, "East to Rosewater · 42 steps"
    /// on the road). Tapping it walks you there; its – button tucks it into a small arrow, and tapping the arrow brings
    /// the card back. HP and ink are on the pet card.
    /// </summary>
    public sealed class RoadHud : MonoBehaviour
    {
        const float Height = 64, RefreshEvery = .25f;
        const string TuckedKey = "bb.goalTucked";

        PlazaWorld world;
        RectTransform root, card, arrow;
        Image goalIcon;
        CanvasGroup group;
        Text goalTitle, goalSub;
        GameObject words;
        Button compass, tuck;
        Goal goal;
        float refreshAt;
        string shownGoal;
        bool tucked;

        /// <summary>Where the compass points: a map point, what to call it, and what a tap does.</summary>
        sealed class Goal { public Vector2 At; public string Icon, Title, Sub; public TownMap.Spot Spot; }

        public static RoadHud Create(PlazaWorld world)
        {
            var canvas = UiKit.MakeCanvas("Road HUD", 19);
            var hud = canvas.gameObject.AddComponent<RoadHud>();
            hud.world = world;
            hud.root = (RectTransform)canvas.transform;
            hud.group = canvas.gameObject.AddComponent<CanvasGroup>();
            hud.BuildCompass();
            hud.Tuck(PlayerPrefs.GetInt(TuckedKey, 0) == 1);
            return hud;
        }

        // [➜] 🌾 On to Rosewater / Take Bramble Road east  [–]
        void BuildCompass()
        {
            compass = UiKit.Button(root, "objective", Palette.Cream, Follow, UiKit.CardRadius);
            compass.navigation = new Navigation { mode = Navigation.Mode.None };
            card = ((RectTransform)compass.transform).Pin(new Vector2(0, 1), new Vector2(Hud.Margin, -Hud.BelowPetCard), new Vector2(PetBadge.Width, Height));
            UiKit.Row(card, 10, new RectOffset(12, 12, 0, 0));
            UiKit.Outline(compass, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(card, UiKit.CardRadius, 14, 4, .22f);

            var disc = UiKit.Panel(card, "arrow", Palette.Amber, 20);
            disc.raycastTarget = false;
            UiKit.Size(disc, 40, 40);
            arrow = UiKit.Arrow(disc.transform, Palette.Ink);

            goalIcon = UiKit.Icon(card, null, 28);
            var column = UiKit.Node("words", card);
            UiKit.Column(column, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(column, 0, -1, 1);
            words = column.gameObject;
            goalTitle = OneLine(column, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            goalSub = OneLine(column, UiKit.SmallSize, Palette.InkSoft, UiKit.Body);

            tuck = UiKit.Button(card, "Tuck away", Palette.Paper, () => Tuck(true), 22);
            tuck.navigation = new Navigation { mode = Navigation.Mode.None };
            UiKit.Size(tuck, 44, 44);
            var bar = UiKit.Panel(tuck.transform, "–", Palette.Ink, 1);
            bar.raycastTarget = false;
            bar.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(16, 3));
        }

        // a label that keeps to one line, cut short rather than spilling out of the card
        static Text OneLine(Transform parent, int size, Color color, Font font)
        {
            var t = UiKit.Label(parent, "", size, color, font);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(t, -1, Mathf.Ceil(size * 1.3f));
            return t;
        }

        // tucked: only the arrow shows, in a small square; untucked: the whole card
        void Tuck(bool on)
        {
            tucked = on;
            PlayerPrefs.SetInt(TuckedKey, on ? 1 : 0);
            UiKit.Show(goalIcon, !on && goalIcon.sprite != null);
            words.SetActive(!on);
            UiKit.Show(tuck, !on);
            card.sizeDelta = new Vector2(on ? Height : PetBadge.Width, Height);
        }

        void Update()
        {
            var me = world ? world.Me : null;
            if (me == null) return;
            bool fighting = world.Road != null && world.Road.Fighting;
            group.alpha = Hud.Hidden ? 0 : Mathf.MoveTowards(group.alpha, PlazaInput.Locked || fighting ? 0 : 1, Time.unscaledDeltaTime / .2f);
            group.blocksRaycasts = group.alpha > .5f;

            if (Time.unscaledTime < refreshAt) { PointArrow(me); return; }
            refreshAt = Time.unscaledTime + RefreshEvery;
            goal = fighting || UiStack.Any ? null : FindGoal();
            UiKit.Show(compass, goal != null);
            if (goal != null) ShowGoal(me);
            PointArrow(me);
        }

        void ShowGoal(PetActor me)
        {
            float d = Vector2.Distance(goal.At, me.Pos);
            string sub = goal.Spot != null ? goal.Sub : d < 3 ? "You’re here" : goal.Sub + " · " + Mathf.Max(1, Mathf.RoundToInt(d)) + " steps";
            string key = goal.Title + "|" + sub;
            if (key == shownGoal) return;
            shownGoal = key;
            UiKit.SetIcon(goalIcon, goal.Icon);
            UiKit.Show(goalIcon, !tucked && goalIcon.sprite != null);
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

        // the site's goalNow: in town, the Book Boss while it seals the road, else the road out; on the road, the sign at
        // the east end; nothing in the caves
        Goal FindGoal()
        {
            var map = world.Map;
            if (map.Wild == null)
            {
                var boss = BossBook.Current.Seals(TalesSave.Current, map.Key) ? map.Spots.Find(s => s.Kind == "gym") : null;
                if (boss != null)
                    return new Goal { At = boss.Use + new Vector2(.5f, -1), Icon = "📕", Title = "Beat " + BossBook.Current.Get(map.Key).Name, Sub = "Book Boss at " + boss.Name, Spot = boss };
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

        // a tap on the card: off to the road's spot in town, or to the far sign on the road; on the tucked arrow, the card comes back
        void Follow()
        {
            if (goal == null) return;
            if (tucked) { Tuck(false); return; }
            Sound.Play("boop");
            if (goal.Spot != null) world.GoToSpot(goal.Spot);
            else world.WalkTo(Mathf.FloorToInt(goal.At.x), Mathf.FloorToInt(goal.At.y) + 1);
        }
    }
}
