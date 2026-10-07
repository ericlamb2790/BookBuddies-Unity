using System;
using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The act's map (the site's mapView, tales.js T:1178-1205): the saga's act pips, the land's name and hazard, then the
    /// map, a scrolling page of the land with its roads, places and the lair boss looming at the top, and beside it (below on
    /// narrow windows) "Where next?". Tap a glowing place to see what it is, tap it again or "Go here ›" to go; "Next"
    /// steps through the places. Places more than two rows ahead stay fogged. The lone way on goes by itself.
    /// </summary>
    public static class EpicMapView
    {
        const float RowHeight = 94, CanvasExtra = 40, Gutter = 16, Gap = 16, WideFrom = 860, MaxMapHeight = 680, NodeSize = 48, BossSize = 60;
        static readonly Color Gold = Palette.Hex("#ffd36a"), Rim = Palette.Hex("#f3dfb2"), Done = Palette.Hex("#e9c46a"), Hot = Palette.Hex("#ff9a6a"), Lair = Palette.Hex("#ff7a8a");

        // the place picked for a closer look and the map's scroll survive a redraw (a resize), until the party moves on
        static string selKey;
        static int sel = -1;
        static float fromBottom = -1;

        /// <summary>Builds the map view into body; pick(i) takes option i, autoGo(go, label) starts the lone way's countdown.</summary>
        public static void Build(RectTransform body, TaleRun run, EpicView v, Action<int> pick, Action<Action, Text> autoGo)
        {
            var E = run.Ep;
            var land = EpicSaga.LandOf(E);
            string key = $"{run.Id}:{run.Vol}:{E.Saga}:{E.Act}:{E.At.r}";
            if (key != selKey) { selKey = key; sel = v.Opts.Count == 1 ? 0 : -1; fromBottom = -1; }

            var size = body.rect.size;
            bool wide = size.x >= WideFrom;
            var root = UiKit.Node("map view", body).Fill();

            // ---- header: act pips, the land, "Act II of IV · hazard" ----
            bool shortWindow = size.y < 560;
            float headH = wide && !shortWindow ? 118 : shortWindow ? 84 : 100;
            var head = UiKit.Node("head", root);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = Vector2.one; head.pivot = new Vector2(.5f, 1);
            head.anchoredPosition = Vector2.zero; head.sizeDelta = new Vector2(0, headH);
            UiKit.Column(head, 2, new RectOffset((int)Gutter, (int)Gutter, shortWindow ? 6 : wide ? 14 : 10, 0), TextAnchor.UpperCenter).childForceExpandWidth = false;
            Pips(head, E, shortWindow);
            UiKit.Label(head, BattleText.Prose(land.N), wide && !shortWindow ? UiKit.TitleSize : UiKit.HeadingSize + 2, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            Lede(head, E, land);

            // ---- the map and the side panel: side by side on wide windows, stacked on narrow ones ----
            float room = size.y - headH - Gutter;
            float cw = Mathf.Min(size.x - Gutter * 2, 1240);
            float x0 = (size.x - cw) / 2;
            Rect mapAt, sideAt;
            if (wide)
            {
                float mapW = (cw - Gap) * 1.45f / 2.45f;
                float mapH = Mathf.Min(room, MaxMapHeight);
                mapAt = new Rect(x0, headH, mapW, mapH);
                sideAt = new Rect(x0 + mapW + Gap, headH, cw - mapW - Gap, mapH);
            }
            else
            {
                float sideH = Mathf.Clamp(room * .36f, 110, 250);
                float mapH = room - sideH - 10;
                if (mapH < 160) { mapH = Mathf.Max(120, room * .55f); sideH = Mathf.Max(80, room - mapH - 10); }
                mapAt = new Rect(x0, headH, cw, mapH);
                sideAt = new Rect(x0, headH + mapH + 10, cw, sideH);
            }

            var state = new MapState { Run = run, View = v, Pick = pick };
            Map(root, mapAt, state, land);
            Side(root, sideAt, state, wide);
            state.Paint();
            if (v.Opts.Count == 1) autoGo(() => pick(0), state.Countdown);
            EpicViews.Focus(sel >= 0 ? (Selectable)state.Go : state.First);
        }

        // what the map and side panel share: the place picked for a closer look and the buttons that show it
        sealed class MapState
        {
            public TaleRun Run;
            public EpicView View;
            public Action<int> Pick;
            public readonly List<(int opt, RectTransform disc, Image face, Image glow, Color color)> Nodes = new List<(int, RectTransform, Image, Image, Color)>();
            public Button First, Go;
            public RectTransform Panel;
            public Text Countdown;

            public void Select(int i)
            {
                if (i == sel) { Pick(i); return; }
                sel = i;
                Sound.Play("tap");
                Paint();
                if (Go) EpicViews.Focus(Go);
            }

            public void Paint()
            {
                foreach (var n in Nodes)
                {
                    bool on = n.opt == sel;
                    n.face.color = on ? Gold : n.color;
                    n.glow.enabled = on;
                    n.disc.GetComponent<Pulse>().Size = on ? 1.22f : 1;
                }
                PickPanel(this);
            }
        }

        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t).WithAlpha(1);

        // ---- header pieces ----

        // one pip per act (its land), lit up to the current act, then the Dark Author's pip
        static void Pips(RectTransform head, EpicState E, bool small)
        {
            var row = UiKit.Node("saga", head);
            UiKit.Row(row, 4, new RectOffset(0, 0, small ? 0 : 2, small ? 2 : 6), TextAnchor.MiddleCenter);
            float size = small ? 26 : 34;
            for (int i = 0; i < E.Acts.Count; i++)
            {
                if (i > 0) Dash(row);
                int act = i + 1;
                Pip(row, EpicSaga.LandOf(E, act).I, act < E.Act, act == E.Act, false, size);
            }
            Dash(row);
            Pip(row, EpicSaga.Dark(E).I, false, E.Act == 4, true, size);
        }

        static void Pip(RectTransform row, string icon, bool done, bool on, bool dark, float size)
        {
            var holder = UiKit.Node("pip", row);
            UiKit.Size(holder, size + 2, size + 2);
            int round = (int)(size / 2);
            var pip = UiKit.Panel(holder, "disc", done ? Gold.WithAlpha(.25f) : Palette.Paper, round);
            pip.raycastTarget = false;
            pip.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size));
            UiKit.Outline(pip, dark ? Lair.WithAlpha(.6f) : on ? Palette.Amber : done ? Gold.WithAlpha(.6f) : Palette.Ink.WithAlpha(.18f), round, 2);
            var ic = UiKit.Icon(pip.transform, icon, size * .56f);
            ic.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .56f, size * .56f));
            var group = pip.gameObject.AddComponent<CanvasGroup>();
            group.alpha = done || on ? 1 : .5f;
            if (on) pip.rectTransform.localScale = new Vector3(1.14f, 1.14f, 1);
        }

        static void Dash(RectTransform row)
        {
            var d = UiKit.Panel(row, "dash", Palette.Ink.WithAlpha(.18f), 0);
            d.raycastTarget = false;
            UiKit.Size(d, 14, 2);
        }

        // "Act II of IV · 🌧️ Ink Rain" (or "Warded" once a ward is up)
        static void Lede(RectTransform head, EpicState E, Land land)
        {
            var row = UiKit.Node("lede", head);
            UiKit.Row(row, 6, null, TextAnchor.MiddleCenter);
            Small(row, $"Act {EpicSaga.Roman(E.Act)} of IV");
            if (land.Hz == null || !EpicData.Current.Hazards.TryGetValue(land.Hz, out var hz)) return;
            Small(row, "·");
            UiKit.Icon(row, hz.i, 20);
            Small(row, E.Ward ? "Warded" : hz.n).color = E.Ward ? Palette.InkSoft.WithAlpha(.6f) : UiKit.RoseInk;
        }

        static Text Small(RectTransform row, string text)
        {
            var t = UiKit.Label(row, text, UiKit.SmallSize, Palette.InkSoft, UiKit.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        // ---- the map ----

        static void Map(RectTransform root, Rect at, MapState st, Land land)
        {
            var run = st.Run; var E = run.Ep; var v = st.View; var M = E.Map;
            var frame = Place(UiKit.Panel(root, "map", Palette.Hex(land.Color), 16).rectTransform, at);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // clips to the rounded corners
            UiKit.Shadow(frame, 16, 22, 10, .4f);
            var view = UiKit.Node("view", frame).Fill();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            scroll.viewport = view;

            int N = M.Count;
            float W = at.width, H = Mathf.Max(at.height, N * RowHeight + CanvasExtra), step = 100f / N;
            float Y(int r) => 100 - (r + .5f) * step;
            Vector2 P(double x, float y) => new Vector2((float)x / 100 * W, -y / 100 * H);   // from the canvas's top-left

            var can = UiKit.Node("canvas", view);
            can.anchorMin = new Vector2(0, 1); can.anchorMax = Vector2.one; can.pivot = new Vector2(.5f, 1);
            can.sizeDelta = new Vector2(0, H);
            scroll.content = can;

            // the ground: the land darker at the top, the lair's shadow over it, a soft vignette and an inked border
            Color ground = Palette.Hex(land.Color), boss = Palette.Hex(EpicSaga.Boss(E, E.Act).C ?? "#6a1f33");
            var grad = UiKit.Node("ground", can).Fill().Painted<VGradient>();
            grad.Top = Mix(ground, Color.black, .45f); grad.Mid = ground; grad.Bottom = Mix(ground, Color.black, .3f); grad.At = .42f;
            var lairGlow = UiKit.Node("lair shade", can);
            lairGlow.anchorMin = new Vector2(-.2f, 1 - .5f * at.height / H); lairGlow.anchorMax = new Vector2(1.2f, 1 + .2f * at.height / H);
            lairGlow.offsetMin = lairGlow.offsetMax = Vector2.zero;
            var shade = lairGlow.gameObject.AddComponent<Image>();
            shade.sprite = UiKit.Glow; shade.color = Mix(boss, Color.black, .45f).WithAlpha(.55f); shade.raycastTarget = false;
            var light = UiKit.Node("light", can);
            light.anchorMin = new Vector2(.1f, .55f); light.anchorMax = new Vector2(.9f, 1.05f); light.offsetMin = light.offsetMax = Vector2.zero;
            var li = light.gameObject.AddComponent<Image>();
            li.sprite = UiKit.Glow; li.color = new Color(1, .97f, .88f, .12f); li.raycastTarget = false;
            var vig = UiKit.Node("vignette", can).Fill().gameObject.AddComponent<Image>();
            vig.sprite = UiKit.Vignette; vig.color = new Color(0, 0, 0, .45f); vig.raycastTarget = false;

            // the lair boss looms at the top of the page
            var B0 = EpicSaga.Boss(E, E.Act);
            float lw = Mathf.Min(W * .62f, 300);
            var lair = UiKit.Node("lair", can).Pin(new Vector2(.5f, 1), new Vector2(0, lw * .14f), new Vector2(lw, lw));
            lair.anchorMin = lair.anchorMax = new Vector2(.5f, 1); lair.pivot = new Vector2(.5f, 1);
            lair.gameObject.AddComponent<CanvasGroup>().alpha = .16f;
            try { FoeArt.MakeUi(lair, FoeArt.For(FoeFactory.Plain(B0), true)); }
            catch (Exception e) { Debug.LogException(e); UiKit.Icon(lair, B0.I, lw * .6f); }

            // scattered deco (tqRand(seed + act·99 + saga), as the site draws it): nine dungeon things at the top, fourteen of the land's
            var r0 = new Mulberry32(unchecked((uint)((long)run.Seed + E.Act * 99L + E.Saga)));
            var dun = new[] { "🕯️", "⛓️", "🦴", "🕸️", "🗝️", "🪦", "🏚️", B0.I };
            for (int i = 0; i < 9; i++)
            {
                float l = (float)(r0.Next() * 90 + 5), t = (float)(r0.Next() * 26 + 4), s = Mathf.Round((float)(15 + r0.Next() * 12)), rot = Mathf.Round((float)((r0.Next() - .5) * 30));
                Deco(can, dun[(int)Math.Floor(r0.Next() * dun.Length)], l, t, s * 1.3f, rot, .42f, W, H);
            }
            if (land.Deco.Length > 0)
                for (int i = 0; i < 14; i++)
                {
                    float l = (float)(r0.Next() * 94 + 3), t = (float)(r0.Next() * 92 + 3), s = Mathf.Round((float)(14 + r0.Next() * 13)), rot = Mathf.Round((float)((r0.Next() - .5) * 24));
                    Deco(can, land.Deco[(int)Math.Floor(r0.Next() * land.Deco.Length)], l, t, s * 1.3f, rot, .3f, W, H);
                }
            var border = UiKit.Node("border", can).Fill(7).gameObject.AddComponent<Image>();
            border.sprite = UiKit.Ring(11, 2); border.type = Image.Type.Sliced; border.color = Palette.Hex("#f6e3b8").WithAlpha(.4f); border.raycastTarget = false;

            // the roads: walked ones in gold, the ways on from here bright and moving, the rest dotted ink
            var seen = new HashSet<(int, int)> { (0, 0) };
            foreach (var t in E.Trail) seen.Add(t);
            var roads = UiKit.Node("roads", can).Fill().Painted<Roads>();
            for (int r = 0; r + 1 < N; r++)
                for (int c = 0; c < M[r].Count; c++)
                    foreach (int j in M[r][c].To)
                    {
                        if (j >= M[r + 1].Count) continue;
                        var a = P(M[r][c].X, Y(r)); var b = P(M[r + 1][j].X, Y(r + 1));
                        float dy = step * .5f / 100 * H;
                        bool on = seen.Contains((r, c)) && seen.Contains((r + 1, j));
                        bool nx = r == E.At.r && c == E.At.c;
                        roads.Add(a, a + new Vector2(0, dy), b - new Vector2(0, dy), b, on ? Roads.Kind.On : nx ? Roads.Kind.Next : Roads.Kind.Plain);
                    }

            // the places
            var can_ = new Dictionary<int, int>();   // column on the next row -> option
            for (int i = 0; i < v.Opts.Count; i++) can_[v.Opts[i].To] = i;
            Color ink = Mix(Palette.Hex(land.Ink), Color.white, .25f);
            for (int r = 0; r < N; r++)
                for (int c = 0; c < M[r].Count; c++)
                {
                    var nd = M[r][c];
                    bool here = r == E.At.r && c == E.At.c, done = seen.Contains((r, c)) && !here, fog = EpicSaga.Fogged(E, r, nd);
                    int opt = r == E.At.r + 1 && can_.TryGetValue(c, out int oi) ? oi : -1;
                    Node(can, st, P(nd.X, Y(r)), nd, opt, here, done, fog, ink, E);
                }

            // the party's token, standing on the place it's at
            var tok = run.Party.Find(h => !h.Ko) ?? (run.Party.Count > 0 ? run.Party[0] : null);
            var hereNode = E.Here;
            var token = UiKit.Node("token", can).Pin(new Vector2(0, 1), P(hereNode.X, Y(E.At.r)) + new Vector2(0, 40), new Vector2(56, 56));
            token.anchorMin = token.anchorMax = new Vector2(0, 1); token.pivot = new Vector2(.5f, .5f);
            var drop = UiKit.Node("shadow", token).Pin(new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(30, 8)).gameObject.AddComponent<Image>();
            drop.sprite = Art.SoftDot; drop.color = new Color(0, 0, 0, .45f); drop.raycastTarget = false;
            var pet = UiKit.Node("pet", token).Fill().gameObject.AddComponent<Image>();
            pet.raycastTarget = false; pet.preserveAspect = true;
            if (tok != null) pet.sprite = PetSprites.For(tok.Seed.Look);
            if (!pet.sprite) UiKit.SetIcon(pet, "🐾");
            pet.gameObject.AddComponent<Pulse>().Bob = 4;

            // scroll: kept where it was on a redraw, else the current row sits low in the view (scrollTop = y − view + 130)
            float viewH = at.height, maxTop = Mathf.Max(0, H - viewH);
            float top = fromBottom >= 0 ? H - viewH - fromBottom : H * Y(E.At.r) / 100 - viewH + 130;
            can.anchoredPosition = new Vector2(0, Mathf.Clamp(top, 0, maxTop));
            scroll.onValueChanged.AddListener(_ => fromBottom = H - viewH - can.anchoredPosition.y);
        }

        static RectTransform Place(RectTransform r, Rect at)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(at.x, -at.y);
            r.sizeDelta = at.size;
            return r;
        }

        static void Deco(RectTransform can, string emoji, float left, float top, float size, float rot, float alpha, float W, float H)
        {
            var ic = UiKit.Icon(can, emoji, size);
            var r = ic.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(left / 100 * W, -top / 100 * H);
            r.localRotation = Quaternion.Euler(0, 0, -rot);
            ic.color = new Color(1, 1, 1, alpha);
        }

        // a place on the map: a disc (gold once walked, dashed while fogged, the boss's face at the lair); the ways on are buttons
        static void Node(RectTransform can, MapState st, Vector2 at, MapNode nd, int opt, bool here, bool done, bool fog, Color ink, EpicState E)
        {
            var data = EpicData.Current;
            bool boss = nd.T == "boss";
            float size = boss ? BossSize : NodeSize;
            var (icon, lab) = data.Nodes.TryGetValue(nd.T, out var n) ? n : ("❔", nd.T);
            string label = fog ? "Unexplored" : opt >= 0 ? st.View.Opts[opt].N : boss ? EpicSaga.Boss(E, E.Act).N : lab;

            RectTransform holder;
            Button button = null;
            if (opt >= 0)
            {
                int i = opt;
                button = UiKit.Button(can, label, Color.clear, () => st.Select(i), (int)(size / 2));
                holder = (RectTransform)button.transform;
                if (st.First == null) st.First = button;
            }
            else holder = UiKit.Node(label, can);
            holder.anchorMin = holder.anchorMax = new Vector2(0, 1); holder.pivot = new Vector2(.5f, .5f);
            holder.anchoredPosition = at; holder.sizeDelta = new Vector2(size, size);

            var disc = UiKit.Node("disc", holder).Fill();
            var glow = UiKit.Node("glow", disc).Fill(-12).gameObject.AddComponent<Image>();
            glow.sprite = UiKit.Glow; glow.color = Gold.WithAlpha(.5f); glow.raycastTarget = false; glow.enabled = false;
            Color face = boss ? Palette.Hex("#4a1424") : fog ? new Color(.1f, .07f, .04f, .35f) : done ? Done : ink;
            var faceImg = UiKit.Panel(disc, "face", face, (int)(size / 2));
            faceImg.raycastTarget = false;
            faceImg.rectTransform.Fill();
            Color rim = boss ? Lair : opt >= 0 ? Gold : here ? Color.white : done ? Palette.Hex("#fff3c4") : (nd.T == "elite" || nd.T == "danger") ? Hot : Rim.WithAlpha(fog ? .44f : .8f);
            UiKit.Outline(faceImg, rim, (int)(size / 2), fog ? 2 : 3);
            if (!fog)
            {
                var shine = UiKit.Panel(faceImg.transform, "shine", new Color(1, 1, 1, done ? .25f : .14f), (int)(size / 2 - 6));
                shine.raycastTarget = false;
                shine.rectTransform.anchorMin = new Vector2(.18f, .45f); shine.rectTransform.anchorMax = new Vector2(.62f, .86f);
                shine.rectTransform.offsetMin = shine.rectTransform.offsetMax = Vector2.zero;
            }

            if (boss)
            {
                var face2 = UiKit.Node("villain", faceImg.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -3), new Vector2(size * .78f, size * .78f));
                try { FoeArt.MakeUi(face2, FoeArt.For(FoeFactory.Plain(EpicSaga.Boss(E, E.Act)), true)); }
                catch (Exception e) { Debug.LogException(e); UiKit.Icon(face2, EpicSaga.Boss(E, E.Act).I, size * .6f); }
            }
            else if (fog)
            {
                var q = UiKit.Label(faceImg.transform, "?", 20, new Color(1, 1, 1, .65f), UiKit.Bold, TextAnchor.MiddleCenter);
                q.rectTransform.Fill();
            }
            else
            {
                var ic = UiKit.Icon(faceImg.transform, icon, size * .5f);
                ic.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .5f, size * .5f));
            }

            var pulse = disc.gameObject.AddComponent<Pulse>();
            pulse.Breathe = opt >= 0;
            if (button != null)
            {
                var t = UiKit.Label(holder, BattleText.Prose(lab), UiKit.SmallSize - 2, Color.white, UiKit.Bold, TextAnchor.UpperCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, -6), new Vector2(120, 20)).pivot = new Vector2(.5f, 1);
                t.rectTransform.anchoredPosition = new Vector2(0, -4);
                t.gameObject.AddComponent<Outline>().effectColor = new Color(.16f, .1f, .05f, .85f);
                st.Nodes.Add((opt, disc, faceImg, glow, face));
            }
        }

        // ---- the side panel: "Where next?", the place picked, Go / Next, the countdown and the party ----

        static void Side(RectTransform root, Rect at, MapState st, bool wide)
        {
            var side = Place(UiKit.Node("side", root), at);
            var col = UiKit.ScrollColumn(side, 10, new RectOffset(2, 2, 2, 8), out _);
            ((RectTransform)col.parent).Fill();
            if (wide) UiKit.Label(col, "Where next?", UiKit.HeadingSize, Palette.Ink, UiKit.Title);
            st.Panel = UiKit.Node("pick", col);
            UiKit.Column(st.Panel, 8);
            st.Countdown = UiKit.Label(col, "", UiKit.SmallSize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            EpicViews.PartyStrip(col, st.Run);
        }

        static void PickPanel(MapState st)
        {
            var p = st.Panel;
            if (!p) return;
            for (int i = p.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(p.GetChild(i).gameObject);
            st.Go = null;
            var v = st.View;
            if (sel < 0 || sel >= v.Opts.Count)
            {
                var box = UiKit.Panel(p, "tap", Palette.Paper, 14);
                box.raycastTarget = false;
                UiKit.Outline(box, Palette.Ink.WithAlpha(.18f), 14, 1);
                UiKit.Column(box.rectTransform, 4, new RectOffset(14, 14, 12, 12), TextAnchor.MiddleCenter);
                var row = EpicViews.Centered(box.rectTransform);
                UiKit.Icon(row, "👆", 24);
                UiKit.Label(row, "Tap a glowing place on the map", UiKit.BodySize - 1, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Label(box.transform, $"{v.Opts.Count} way{(v.Opts.Count == 1 ? "" : "s")} to go.", UiKit.SmallSize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
                return;
            }
            var o = v.Opts[sel];
            var card = UiKit.Panel(p, "place", Gold.WithAlpha(.12f), 14);
            card.raycastTarget = false;
            UiKit.Outline(card, Palette.Amber, 14, 2);
            UiKit.Row(card.rectTransform, 12, new RectOffset(12, 12, 10, 10));
            UiKit.Icon(card.transform, o.I, 40);
            var text = UiKit.Node("text", card.transform);
            UiKit.Column(text, 2);
            UiKit.Size(text, -1, -1, 1);
            UiKit.Label(text, BattleText.Prose(UiKit.SplitEmoji(o.N, out _)), UiKit.BodySize, Palette.Ink, UiKit.Bold);
            if (!string.IsNullOrEmpty(o.D)) UiKit.Label(text, BattleText.Prose(o.D), UiKit.SmallSize, Palette.InkSoft);
            FadeIn.On(card, 0);
            var buttons = UiKit.Node("buttons", p);
            UiKit.Row(buttons, 10, null, TextAnchor.MiddleRight);
            UiKit.Spacer(buttons);
            if (v.Opts.Count > 1) UiKit.Secondary(buttons, "Next", () => st.Select((sel + 1) % v.Opts.Count), null, 44);
            int at = sel;
            st.Go = UiKit.Primary(buttons, "Go here ›", () => st.Pick(at), null, 44);
        }
    }

    /// <summary>A gentle breath (scale) or bob (height) for a map piece; Size eases toward a picked place's larger size.</summary>
    sealed class Pulse : MonoBehaviour
    {
        public bool Breathe;
        public float Bob, Size = 1;
        float scale = 1, phase;
        Vector2 home;
        bool homed;

        void Start() => phase = UnityEngine.Random.value * 6;

        void Update()
        {
            var r = (RectTransform)transform;
            bool still = GameSettings.ReduceMotion;
            scale = Mathf.Lerp(scale, Size, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14));
            float t = Time.unscaledTime + phase;
            float s = scale * (Breathe && !still && Size <= 1 ? 1 + .06f * (.5f + .5f * Mathf.Sin(t * Mathf.PI * 2 / 1.6f)) : 1);
            r.localScale = new Vector3(s, s, 1);
            if (Bob > 0)
            {
                if (!homed) { home = r.anchoredPosition; homed = true; }
                r.anchoredPosition = home + new Vector2(0, still ? 0 : Mathf.Sin(t * Mathf.PI * 2 / 2.4f) * Bob);
            }
        }
    }

    /// <summary>A vertical gradient: Top to Mid (at At, from the top) to Bottom.</summary>
    sealed class VGradient : MaskableGraphic
    {
        public Color Top = Color.black, Mid = Color.gray, Bottom = Color.black;
        public float At = .5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float ym = r.yMax - r.height * At;
            Band(vh, r.xMin, r.xMax, r.yMax, ym, Top, Mid);
            Band(vh, r.xMin, r.xMax, ym, r.yMin, Mid, Bottom);
        }

        static void Band(VertexHelper vh, float x0, float x1, float yTop, float yBottom, Color top, Color bottom)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, yBottom), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x0, yTop), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, yTop), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, yBottom), bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    /// <summary>The map's roads as cubic curves (positions from the parent's top-left): solid gold, moving dots, or dotted ink.</summary>
    sealed class Roads : MaskableGraphic
    {
        public enum Kind { Plain, Next, On }

        const int Steps = 28;
        const float DotGap = 9;
        static readonly Kind[] Order = { Kind.Plain, Kind.On, Kind.Next }; // the ways on draw last, on top
        readonly List<(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Kind k)> roads = new List<(Vector2, Vector2, Vector2, Vector2, Kind)>();
        readonly Vector2[] pts = new Vector2[Steps + 1]; // one road's points, reused (the moving dots redraw every frame)
        static readonly Color PlainInk = new Color(.165f, .1f, .047f, .6f), NextInk = Palette.Hex("#fff3d6"), OnInk = Palette.Hex("#ffd36a");
        float flow;

        public void Add(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Kind k)
        {
            roads.Add((a, b, c, d, k));
            SetVerticesDirty();
        }

        void Update()
        {
            if (GameSettings.ReduceMotion || !roads.Exists(x => x.k == Kind.Next)) return;
            flow = Mathf.Repeat(flow + Time.unscaledDeltaTime * 14, DotGap);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var origin = new Vector2(r.xMin, r.yMax);
            foreach (var k in Order)
                foreach (var road in roads)
                {
                    if (road.k != k) continue;
                    for (int i = 0; i <= Steps; i++) pts[i] = origin + Bezier(road.a, road.b, road.c, road.d, i / (float)Steps);
                    if (k == Kind.On) Solid(vh, pts, 4.5f, OnInk);
                    else Dots(vh, pts, k == Kind.Next ? 4.2f : 3.6f, k == Kind.Next ? NextInk : PlainInk, k == Kind.Next ? flow : 0);
                }
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        static void Solid(VertexHelper vh, Vector2[] pts, float width, Color color)
        {
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                var dir = (pts[i + 1] - pts[i]).normalized;
                var n = new Vector2(-dir.y, dir.x) * width / 2;
                var ext = dir * width * .3f;   // a little overlap so the joins don't crack
                Quad(vh, pts[i] - ext - n, pts[i] - ext + n, pts[i + 1] + ext + n, pts[i + 1] + ext - n, color);
            }
        }

        static void Dots(VertexHelper vh, Vector2[] pts, float size, Color color, float offset)
        {
            float next = offset, walked = 0;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                while (next <= walked + len)
                {
                    var p = Vector2.Lerp(pts[i], pts[i + 1], len > 0 ? (next - walked) / len : 0);
                    float h = size / 2;
                    // a little diamond-cut square reads as a round dot at this size
                    Quad(vh, p + new Vector2(-h, 0), p + new Vector2(0, h), p + new Vector2(h, 0), p + new Vector2(0, -h), color);
                    Quad(vh, p + new Vector2(-h * .7f, -h * .7f), p + new Vector2(-h * .7f, h * .7f), p + new Vector2(h * .7f, h * .7f), p + new Vector2(h * .7f, -h * .7f), color);
                    next += DotGap;
                }
                walked += len;
            }
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
