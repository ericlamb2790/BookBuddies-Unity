using System;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The battle field: the arena sky, drifting motes, three lanes as columns (left, centre, right) with the pet at the
    /// foot of its column and foes above, cover, and every fighter placed so nothing overlaps: foes in a column share it
    /// two to a row (more foes, smaller each), a boss takes the top of its column bigger, and nameplates stay inside it.
    /// Positions are reference pixels from the bottom-left of the safe area.
    /// </summary>
    public sealed class BattleStage : MonoBehaviour, IPointerClickHandler
    {
        const string LaneKeys = "lcr";
        const float PlateHeight = 96, MinPlate = 104, MaxPlate = 260, LabelRoom = 40, Gutter = 10;
        static readonly Color Rose = Palette.Hex("#ff6a5a"), Gold = Palette.Hex("#ffd27a");

        /// <summary>Effects go here (same coordinates as the fighters).</summary>
        public RectTransform Fx { get; private set; }
        /// <summary>Screen shakes play on this.</summary>
        public RectTransform Shake { get; private set; }
        public float Width => safe.rect.width;
        public float Height => safe.rect.height;
        /// <summary>A lane was clicked or tapped (on the field, not on a foe or a button).</summary>
        public event Action<char> LaneTapped;

        readonly Dictionary<string, BattleUnitView> views = new Dictionary<string, BattleUnitView>();
        readonly Dictionary<BattleUnitView, char> placedLane = new Dictionary<BattleUnitView, char>();
        readonly Image[] bands = new Image[3], stripes = new Image[3], outlines = new Image[3];
        readonly Text[] labels = new Text[3];
        RectTransform safe, lanes, units;
        RectTransform cover;
        BattleEngine engine;
        Func<BattleUnit, Action> onFoeTap;
        Vector2 measured;
        static Sprite topFade, bottomFade;

        /// <summary>Builds the field over the whole screen (root) with the play area inside the safe area.</summary>
        public static BattleStage Create(RectTransform root, BattleEngine engine, Func<BattleUnit, Action> onFoeTap)
        {
            var world = UiKit.Node("world", root).Fill();
            var s = world.gameObject.AddComponent<BattleStage>();
            s.engine = engine;
            s.onFoeTap = onFoeTap;
            s.Shake = world;
            s.Build(world);
            return s;
        }

        void Build(RectTransform world)
        {
            Sky(world);
            BattleMotes.Create(world);
            UiKit.Cover(world, "ground shade", new Color(.05f, .02f, .08f, .55f), bottomFade ? bottomFade : bottomFade = Fade(false));
            UiKit.Cover(world, "top shade", new Color(.05f, .02f, .08f, .5f), topFade ? topFade : topFade = Fade(true));

            safe = UiKit.Node("field", world).Fill();
            safe.gameObject.AddComponent<SafeArea>();
            var catcher = safe.gameObject.AddComponent<Image>();
            catcher.color = Color.clear; // the whole field takes lane taps
            lanes = UiKit.Node("lanes", safe);
            units = UiKit.Node("units", safe);
            Fx = UiKit.Node("fx", safe);
            foreach (var r in new[] { lanes, units, Fx }) { r.anchorMin = r.anchorMax = r.pivot = Vector2.zero; r.sizeDelta = Vector2.zero; }
            for (int i = 0; i < 3; i++) Band(i);
            foreach (var u in engine.Heroes) Add(u);
            foreach (var f in engine.Foes) Add(f);
            if (engine.CoverZone != null) cover = Cover(engine.CoverKind);
        }

        // the arena picture covers the screen, cropped around its focus point
        void Sky(RectTransform world)
        {
            var sky = UiKit.Cover(world, "sky", Palette.Hex("#241a33"));
            var sprite = BattleArt.Arena(engine.Setup.Tier, engine.Setup.Cave, UnityEngine.Random.Range(0, 1000));
            if (sprite == null) return;
            var pic = UiKit.Node("arena", sky.rectTransform).gameObject.AddComponent<Image>();
            pic.sprite = sprite;
            pic.raycastTarget = false;
            var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = sprite.rect.width / sprite.rect.height;
            var focus = BattleArt.ArenaFocus;
            pic.rectTransform.pivot = new Vector2(focus.x, 1 - focus.y);
        }

        // a vertical fade texture for the shades at the top and bottom of the screen
        static Sprite Fade(bool top)
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float t = y / 63f, a = top ? Mathf.SmoothStep(0, 1, (t - .78f) / .22f) : Mathf.SmoothStep(1, 0, t / .3f);
                tex.SetPixel(0, y, new Color(1, 1, 1, a));
            }
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f));
        }

        void Band(int i)
        {
            var band = UiKit.Panel(lanes, "lane " + LaneKeys[i], Color.white.WithAlpha(.05f), 18);
            band.raycastTarget = false;
            band.rectTransform.anchorMin = band.rectTransform.anchorMax = band.rectTransform.pivot = Vector2.zero;
            bands[i] = band;
            var s = UiKit.Node("stripes", band.transform).Fill().gameObject.AddComponent<Image>();
            s.sprite = BattleArt.Stripes;
            s.type = Image.Type.Tiled;
            s.raycastTarget = false;
            stripes[i] = s;
            outlines[i] = UiKit.Outline(band, Gold.WithAlpha(0), 18, 2);
            var label = UiKit.Label(band.transform, "", 15, Color.white.WithAlpha(.6f), UiKit.Bold, TextAnchor.MiddleCenter);
            var lr = label.rectTransform; // the column's head
            lr.anchorMin = new Vector2(0, 1);
            lr.anchorMax = lr.pivot = Vector2.one;
            lr.anchoredPosition = Vector2.zero;
            lr.sizeDelta = new Vector2(0, LabelRoom);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            labels[i] = label;
        }

        // the cover lane: a translucent shield dome or a row of bushes around the pet's spot, with a label
        RectTransform Cover(string kind)
        {
            var root = UiKit.Node("cover", lanes);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(.5f, 0);
            if (kind == "sh")
            {
                Piece(root, UiKit.Glow, Palette.Hex("#8cbeff").WithAlpha(.3f), new Vector2(-.1f, 0), new Vector2(1.1f, 1.25f));
                var dome = Piece(root, BattleArt.Ring, Palette.Hex("#a0cdff").WithAlpha(.6f), new Vector2(0, -.55f), new Vector2(1, 1.15f));
                dome.type = Image.Type.Filled;
                dome.fillMethod = Image.FillMethod.Vertical;
                dome.fillOrigin = (int)Image.OriginVertical.Top;
                dome.fillAmount = .7f;
            }
            else
            {
                var greens = new[] { "#3f8a3d", "#58a650", "#4f9a4a", "#3f8a3d" };
                for (int i = 0; i < 4; i++)
                    Piece(root, Art.Disc, Palette.Hex(greens[i]).WithAlpha(.9f), new Vector2(-.05f + i * .27f, -.04f), new Vector2(.25f + i * .27f, .3f + (i % 3 == 0 ? 0 : .08f)));
            }
            var pill = UiKit.Panel(root, "label", Palette.Hex("#140c1f").WithAlpha(.7f), 14);
            pill.raycastTarget = false;
            pill.rectTransform.anchorMin = pill.rectTransform.anchorMax = new Vector2(.5f, 1);
            pill.rectTransform.pivot = new Vector2(.5f, 0);
            UiKit.Row(pill.rectTransform, 6, new RectOffset(10, 12, 3, 4), TextAnchor.MiddleCenter);
            UiKit.Hug(pill.rectTransform);
            UiKit.Icon(pill.transform, kind == "sh" ? "🛡️" : "🌿", 18);
            UiKit.Label(pill.transform, kind == "sh" ? "Cover · less damage" : "Bushes · harder to hit", 15, Palette.Cream, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            return root;
        }

        static Image Piece(RectTransform parent, Sprite sprite, Color c, Vector2 min, Vector2 max)
        {
            var img = UiKit.Node("piece", parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = c;
            img.raycastTarget = false;
            img.rectTransform.anchorMin = min;
            img.rectTransform.anchorMax = max;
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            return img;
        }

        BattleUnitView Add(BattleUnit u)
        {
            var v = BattleUnitView.Create(units, u);
            views[u.Key] = v;
            if (v.Tap && onFoeTap != null) v.Tap.onClick.AddListener(() => onFoeTap(u)());
            v.SetHp(u.Hp, u.Max, u.S("shield"));
            v.Paint();
            return v;
        }

        // ---- looking things up ----

        /// <summary>The fighter drawn for a unit key, or null.</summary>
        public BattleUnitView View(string key) => key != null && views.TryGetValue(key, out var v) ? v : null;

        public IEnumerable<BattleUnitView> Views => views.Values;

        /// <summary>Lane l, c or r as a rectangle (a column) on the field.</summary>
        public Rect Band(char lane)
        {
            var (top, bottom) = Bounds();
            float w = ColumnWidth;
            int i = Mathf.Max(0, LaneKeys.IndexOf(lane));
            return new Rect(Width * .02f + i * (w + Gutter), bottom, w, top - bottom);
        }

        float ColumnWidth => (Width * .96f - Gutter * 2) / 3;

        (float top, float bottom) Bounds()
        {
            float h = Height;
            return (h - Mathf.Clamp(h * .11f, 64, 120), Mathf.Clamp(h * .15f, 96, 168));
        }

        /// <summary>The lane under a point on the field (columns; past either edge counts as the nearest).</summary>
        public char LaneAt(Vector2 at) =>
            LaneKeys[Mathf.Clamp((int)((at.x - Width * .02f) / (ColumnWidth + Gutter)), 0, 2)];

        public void OnPointerClick(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, e.position, e.pressEventCamera, out var local))
                LaneTapped?.Invoke(LaneAt(local - safe.rect.min));
        }

        /// <summary>A refused lane wiggles red (tqlno).</summary>
        public void Refuse(char lane)
        {
            var band = bands[Mathf.Max(0, LaneKeys.IndexOf(lane))];
            band.color = Rose.WithAlpha(.22f);
            KeyTween.Play(band, .42f, BattleEase.Ease, new Kf(0), new Kf(.2f, -5), new Kf(.4f, 5), new Kf(.6f, -5), new Kf(.8f, 5), new Kf(1));
        }

        // ---- every frame: lanes and places ----

        void LateUpdate()
        {
            var size = new Vector2(Width, Height);
            bool resized = (size - measured).sqrMagnitude > 1;
            measured = size;
            foreach (var u in engine.Foes) if (!views.ContainsKey(u.Key)) Add(u).KnockOut(false); // summoned mid-fight: pops in
            PaintLanes();
            Layout(resized);
        }

        void PaintLanes()
        {
            var me = engine.Heroes.Count > 0 ? engine.Heroes[0] : null;
            string slam = engine.SlamZone;
            for (int i = 0; i < 3; i++)
            {
                char z = LaneKeys[i];
                var r = Band(z);
                var band = bands[i].rectTransform;
                band.anchoredPosition = new Vector2(r.x, r.y);
                band.sizeDelta = new Vector2(r.width, r.height);
                bool mine = me != null && !me.Ko && me.Lane == z, hasFoes = FoesIn(z) > 0;
                bool slammed = slam != null && slam[0] == z, open = slam != null && !slammed && !hasFoes;
                float pulse = .5f + .5f * Mathf.Sin(Time.time * Mathf.PI / .7f);
                Color tint = slammed ? Rose.WithAlpha(.16f + .14f * pulse) : mine ? Gold.WithAlpha(.1f) : Color.white.WithAlpha(.045f);
                bands[i].color = Color.Lerp(bands[i].color, tint, Time.deltaTime * 10);
                outlines[i].color = mine ? Gold.WithAlpha(.4f) : slammed ? Rose.WithAlpha(.55f) : Color.clear;
                stripes[i].color = open ? Rose.WithAlpha(.16f) : !hasFoes && slam == null ? new Color(0, 0, 0, .2f) : Color.clear;
                labels[i].text = slammed ? "SLAM INCOMING" : open ? "DODGE HERE" : !hasFoes ? "CLEARED" : "";
                labels[i].color = slammed || open ? Palette.Hex("#ffd0c8") : Color.white.WithAlpha(.55f);
            }
        }

        int FoesIn(char lane)
        {
            int n = 0;
            foreach (var f in engine.Foes) if (f.Hp > 0 && f.Lane == lane) n++;
            return n;
        }

        // each column: the pet stands at its foot, foes fill the room above in rows of two (a boss gets the top row
        // alone and half as tall again), and every nameplate sits under its fighter inside the column
        void Layout(bool instant)
        {
            var (top, bottom) = Bounds();
            float colW = ColumnWidth, height = top - bottom;
            float heroRoom = Mathf.Clamp(height * .36f, PlateHeight + 60, PlateHeight + 250);
            float heroSize = Mathf.Max(40, Mathf.Min(heroRoom - PlateHeight - 12, colW * .62f, 230));
            float heroFeet = bottom + PlateHeight + 6;

            foreach (var h in engine.Heroes)
            {
                var r = Band(h.Lane);
                Put(View(h.Key), h.Lane, new Vector2(r.center.x, heroFeet), heroSize, Mathf.Clamp(heroSize + 24, MinPlate, Mathf.Min(MaxPlate, colW - 8)), false, instant);
            }
            if (cover != null)
            {
                float size = heroSize * 1.25f;
                cover.anchoredPosition = new Vector2(Band(engine.CoverZone[0]).center.x, heroFeet);
                cover.sizeDelta = new Vector2(size, size);
            }

            var shown = new List<BattleUnitView>();
            foreach (var f in engine.Foes) { var v = View(f.Key); if (v != null && !v.ShownKo) shown.Add(v); }
            float foeTop = top - LabelRoom, foeBottom = bottom + heroRoom;
            foreach (char z in LaneKeys)
            {
                var col = shown.FindAll(v => v.Unit.Lane == z);
                var boss = col.Find(v => v.Unit.Boss);
                if (boss != null) col.Remove(boss);
                int rows = (col.Count + 1) / 2;
                float unit = (foeTop - foeBottom) / Mathf.Max(1, rows + (boss != null ? 1.5f : 0));
                float x0 = Band(z).x, y = foeTop;
                if (boss != null)
                {
                    float room = unit * (rows > 0 ? 1.5f : 1);
                    float size = Mathf.Max(40, Mathf.Min(room - PlateHeight - 8, colW * .9f, 340));
                    y -= room;
                    Put(boss, z, new Vector2(x0 + colW / 2, y + PlateHeight + 4), size, Mathf.Clamp(size * .8f, 150, Mathf.Min(MaxPlate, colW - 8)), false, instant);
                }
                for (int k = 0; k < col.Count; k++)
                {
                    int inRow = Mathf.Min(2, col.Count - k / 2 * 2);
                    if (k % 2 == 0) y -= unit;
                    float slot = colW / inRow;
                    float size = Mathf.Max(40, Mathf.Min(unit - PlateHeight - 8, slot * .88f, 220));
                    float plate = Mathf.Min(Mathf.Clamp(size + 20, MinPlate, MaxPlate), slot - 8);
                    Put(col[k], z, new Vector2(x0 + slot * (k % 2 + .5f), y + PlateHeight + 4), size, plate, inRow > 1, instant);
                }
            }
            SortByLane();
        }

        void Put(BattleUnitView v, char lane, Vector2 feet, float size, float plate, bool dense, bool instant)
        {
            if (v == null) return;
            bool moved = placedLane.TryGetValue(v, out var was) && was != lane;
            placedLane[v] = lane;
            v.Place(feet, size, plate, dense, instant, moved);
        }

        // left to right; columns don't overlap, so this only keeps the order steady
        void SortByLane()
        {
            int n = 0;
            foreach (char z in LaneKeys)
                foreach (var v in views.Values)
                {
                    if (v.Unit.Lane != z) continue;
                    if (v.transform.GetSiblingIndex() != n) v.transform.SetSiblingIndex(n);
                    n++;
                }
        }

        /// <summary>Keeps a rect inside the screen's safe area (notches, rounded corners), updating if it changes.</summary>
        public sealed class SafeArea : MonoBehaviour
        {
            Rect applied;

            void Update()
            {
                var area = Screen.safeArea;
                if (area == applied || Screen.width <= 0 || Screen.height <= 0) return;
                applied = area;
                var r = (RectTransform)transform;
                r.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
                r.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
                r.offsetMin = r.offsetMax = Vector2.zero;
            }
        }
    }

    /// <summary>Ten soft motes drifting up through the arena (tqmote), still under reduce motion.</summary>
    public sealed class BattleMotes : MonoBehaviour
    {
        RectTransform[] motes;
        Image[] dots;
        float[] speed, start, alpha;

        public static void Create(RectTransform world)
        {
            var layer = UiKit.Node("motes", world).Fill();
            var m = layer.gameObject.AddComponent<BattleMotes>();
            int n = 10;
            m.motes = new RectTransform[n];
            m.dots = new Image[n];
            m.speed = new float[n];
            m.start = new float[n];
            m.alpha = new float[n];
            for (int i = 0; i < n; i++)
            {
                var img = UiKit.Node("mote", layer).gameObject.AddComponent<Image>();
                img.sprite = UiKit.Glow;
                img.raycastTarget = false;
                m.alpha[i] = UnityEngine.Random.Range(.3f, .8f);
                img.color = Color.white.WithAlpha(m.alpha[i]);
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(.05f, .6f));
                r.sizeDelta = Vector2.one * 14;
                m.motes[i] = r;
                m.dots[i] = img;
                m.speed[i] = UnityEngine.Random.Range(6f, 14f);
                m.start[i] = UnityEngine.Random.value * 14;
            }
        }

        void Update()
        {
            if (GameSettings.ReduceMotion) return;
            for (int i = 0; i < motes.Length; i++)
            {
                float t = Mathf.Repeat(Time.time + start[i], speed[i]) / speed[i];
                motes[i].anchoredPosition = new Vector2(20 * t, 120 * t);
                dots[i].color = Color.white.WithAlpha(alpha[i] * (1 - t));
            }
        }
    }
}
