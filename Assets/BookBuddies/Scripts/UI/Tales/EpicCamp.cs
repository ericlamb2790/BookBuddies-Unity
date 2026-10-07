using System;
using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The Long Read (the site's campView, tales.js T:2043-2125, solo): after a lair boss the party camps by a fire, healed
    /// to full. A night picture of the camp with the pets around the fire and their tents, then four things to do (gear up,
    /// moves, the story so far, a word by the fire) and "Turn in for the night", which brings the morning and turns the page.
    /// </summary>
    public static class EpicCamp
    {
        const float CpH = 40, CpD = 52;   // the camp's perspective (CPJ): the far edge 40% down, 52% deep
        static readonly string[] TentInks = { "#e9785a", "#5aa0e9", "#7ac46a", "#c78ae0", "#f0b84a", "#5ac8c0", "#e9708f", "#9a8af0" };

        /// <summary>Builds the camp into body; done turns the page once morning comes.</summary>
        public static void Build(RectTransform body, TaleRun run, EpicView v, Action done)
        {
            var col = EpicViews.Page(body, 780);
            var kicker = EpicViews.Centered(col);
            UiKit.Badge(kicker, "Campfire · safe zone", UiKit.EmberInk);
            EpicViews.Title(col, "The Long Read", 38);
            EpicViews.Paragraph(col, $"{(string.IsNullOrEmpty(v.Boss) ? "" : v.Boss + " is beaten. ")}Tents are pitched around the fire and everyone is rested. Gear up, then turn in for the night.", UiKit.BodySize, Palette.InkSoft);

            float w = Mathf.Min(780, body.rect.width - 32);
            var camp = Picture(col, run, Mathf.Clamp(w * .52f, 220, 340));

            var tiles = UiKit.Node("tiles", col);
            var grid = tiles.gameObject.AddComponent<GridLayoutGroup>();
            int across = w >= 560 ? 4 : 2;
            grid.cellSize = new Vector2(Mathf.Min(150, (w - 12 * (across - 1)) / across), 92);
            grid.spacing = new Vector2(12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = across;
            var first = UiKit.Tile(tiles, "🎒", "Gear up", () => TalesUi.OpenBag());
            UiKit.Tile(tiles, "🎯", "Moves", () => TalesUi.OpenHero("moves"));
            UiKit.Tile(tiles, "📖", "Story so far", () => Story(body, run));
            UiKit.Tile(tiles, "🔥", "Campfire", camp.Say);

            var note = UiKit.Label(col, "No rush. Rest when you’re ready.", UiKit.SmallSize, Palette.InkSoft, null, TextAnchor.MiddleCenter);
            var row = EpicViews.Centered(col);
            Button sleep = null;
            sleep = UiKit.Primary(row, "Turn in for the night", () =>
            {
                if (!sleep.interactable) return;
                sleep.interactable = false;
                var words = sleep.GetComponentInChildren<Text>();
                words.text = "Morning!";
                foreach (var img in sleep.GetComponentsInChildren<Image>()) if (img.sprite == Art.Emote("⛺")) UiKit.SetIcon(img, "☀️");
                note.text = "A new day. Turning the page…";
                Sound.Play("pop");
                camp.Dawn(done);
            }, "⛺", 52);
            EpicViews.Focus(sleep ? (Selectable)sleep : first);
        }

        // the camp at night: sky, stars and moon over the clearing, trees at the edge, the fire in the middle with the tents and pets around it
        static Camp Picture(RectTransform col, TaleRun run, float height)
        {
            var frame = UiKit.Panel(col, "camp", Palette.Hex("#1e2e1c"), 16);
            frame.raycastTarget = false;
            UiKit.Size(frame, -1, height);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // clips to the rounded corners
            var pic = frame.rectTransform;
            var camp = frame.gameObject.AddComponent<Camp>();
            camp.Run = run;

            var sky = UiKit.Node("sky", pic).Painted<VGradient>();
            sky.Top = Palette.Hex("#0b1028"); sky.Mid = Palette.Hex("#273257"); sky.Bottom = Palette.Hex("#3a4a5a"); sky.At = .7f;
            Stretch(sky.rectTransform, 0, 1 - CpH / 100, 1, 1);
            var ground = UiKit.Node("ground", pic).Painted<VGradient>();
            ground.Top = Palette.Hex("#3a5530"); ground.Mid = Palette.Hex("#5d7a3a"); ground.Bottom = Palette.Hex("#1e2e1c"); ground.At = .45f;
            Stretch(ground.rectTransform, 0, 0, 1, 1 - CpH / 100);
            camp.Sky = UiKit.Node("dawn sky", pic).Fill().gameObject.AddComponent<Image>();
            camp.Sky.color = new Color(1, .74f, .42f, 0);
            camp.Sky.raycastTarget = false;

            var rng = new System.Random(run.Seed + run.Ch * 31);
            for (int i = 0; i < 30; i++)
            {
                var star = UiKit.Panel(pic, "star", Color.white, 2);
                star.raycastTarget = false;
                float s = 2 + (float)rng.NextDouble() * 2;
                Point(star.rectTransform, (float)rng.NextDouble(), 1 - (float)rng.NextDouble() * .3f);
                star.rectTransform.sizeDelta = new Vector2(s, s);
                camp.Stars.Add((star, (float)rng.NextDouble() * 6, 2 + (float)rng.NextDouble() * 4));
            }
            var moonGlow = UiKit.Node("moon glow", pic);
            Point(moonGlow, .84f, .84f);
            moonGlow.sizeDelta = new Vector2(110, 110);
            var mg = moonGlow.gameObject.AddComponent<Image>();
            mg.sprite = UiKit.Glow; mg.color = new Color(1, .95f, .8f, .25f); mg.raycastTarget = false;
            var moon = UiKit.Panel(moonGlow, "moon", Palette.Hex("#fff3c4"), 17);
            moon.raycastTarget = false;
            moon.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(34, 34));

            // things stand on the ground by depth (z 0 = far, 100 = near); nearer ones are bigger and drawn later
            var things = new List<(float z, Action draw)>();
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2, x = 50 + Mathf.Cos(a) * 47, z = 50 - Mathf.Sin(a) * 49;
                if (z > 64 || x < 0 || x > 100) continue;
                int k = i;
                things.Add((z, () => Stand(pic, x, z, k % 3 == 0 ? "🌳" : "🌲", 70, .9f)));
            }
            var light = UiKit.Node("fire glow", pic);
            var fp = Project(50, 50);
            Point(light, fp.x, fp.y);
            light.sizeDelta = new Vector2(height * 1.4f, height * .8f);
            camp.Glow = light.gameObject.AddComponent<Image>();
            camp.Glow.sprite = UiKit.Glow; camp.Glow.color = new Color(1, .65f, .25f, .45f); camp.Glow.raycastTarget = false;

            int n = run.Party.Count, m = Math.Max(2, n);
            for (int i = 0; i < m; i++)
            {
                float a = (-24 + i * (228f / Math.Max(1, m - 1))) * Mathf.Deg2Rad, x = 50 + Mathf.Cos(a) * 33, z = 50 - Mathf.Sin(a) * 31;
                string ink = TentInks[i % TentInks.Length];
                things.Add((z, () => Stand(pic, x, z, "⛺", 74, 1).color = Color.Lerp(Color.white, Palette.Hex(ink), .35f)));
            }
            things.Add((50, () =>
            {
                Stand(pic, 50, 50, "🪵", 44, 1);
                camp.Flame = Stand(pic, 50, 51, "🔥", 62, 1).rectTransform;
            }));
            for (int i = 0; i < n; i++)
            {
                var h = run.Party[i];
                float a = (200 + i * (140f / Math.Max(1, n - 1 == 0 ? 1 : n - 1))) * Mathf.Deg2Rad, x = 50 + Mathf.Cos(a) * 15, z = 50 - Mathf.Sin(a) * 14;
                things.Add((z + .1f, () => camp.Pets.Add(Pet(pic, x, z, h))));
            }
            things.Sort((p, q) => p.z.CompareTo(q.z));
            foreach (var t in things) t.draw();

            var safe = UiKit.Node("safe", pic);
            Point(safe, 0, 0);
            safe.pivot = Vector2.zero;
            safe.anchoredPosition = new Vector2(12, 10);
            UiKit.Row(safe, 0);
            UiKit.Hug(safe);
            var pill = UiKit.Panel(safe, "pill", new Color(0, 0, 0, .4f), 13);
            pill.raycastTarget = false;
            UiKit.Row(pill.rectTransform, 6, new RectOffset(8, 10, 3, 4), TextAnchor.MiddleCenter);
            UiKit.Icon(pill.transform, "🛡️", 18);
            UiKit.Label(pill.transform, "Safe zone", UiKit.SmallSize - 1, Color.white, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;

            camp.Bubble = UiKit.Panel(pic, "say", Palette.Cream, 14);
            camp.Bubble.raycastTarget = false;
            Point(camp.Bubble.rectTransform, .5f, 1);
            camp.Bubble.rectTransform.pivot = new Vector2(.5f, 1);
            camp.Bubble.rectTransform.anchoredPosition = new Vector2(0, -12);
            UiKit.Row(camp.Bubble.rectTransform, 0, new RectOffset(14, 14, 8, 9), TextAnchor.MiddleCenter);
            UiKit.Hug(camp.Bubble.rectTransform);
            camp.Words = UiKit.Label(camp.Bubble.transform, "", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            camp.Words.horizontalOverflow = HorizontalWrapMode.Overflow;
            camp.Bubble.gameObject.SetActive(false);
            return camp;
        }

        // CPJ: where a spot on the ground (x across, z toward you, both 0-100) lands in the picture, as anchors from the bottom-left
        static Vector2 Project(float x, float z)
        {
            float t = Mathf.Clamp01(z / 100), w = .6f + .4f * t;
            return new Vector2((50 + (x - 50) * w) / 100, 1 - (CpH + t * CpD) / 100);
        }

        static float Depth(float z) => .56f + .44f * Mathf.Clamp01(z / 100);

        static void Stretch(RectTransform r, float x0, float y0, float x1, float y1)
        {
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void Point(RectTransform r, float x, float y)
        {
            r.anchorMin = r.anchorMax = new Vector2(x, y);
            r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = Vector2.zero;
        }

        // an emoji standing on the ground (its feet on the spot), sized by depth
        static Image Stand(RectTransform pic, float x, float z, string emoji, float size, float alpha)
        {
            var p = Project(x, z);
            float s = size * Depth(z);
            var img = UiKit.Icon(pic, emoji, s);
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = p; r.pivot = new Vector2(.5f, .08f);
            r.anchoredPosition = Vector2.zero;
            img.color = new Color(1, 1, 1, alpha);
            return img;
        }

        static RectTransform Pet(RectTransform pic, float x, float z, TaleHero h)
        {
            var p = Project(x, z);
            float s = 78 * Depth(z);
            var r = UiKit.Node(h.Seed.Name, pic);
            r.anchorMin = r.anchorMax = p; r.pivot = new Vector2(.5f, 0);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(s, s);
            var shadow = UiKit.Node("shadow", r).Pin(new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(s * .6f, s * .14f)).gameObject.AddComponent<Image>();
            shadow.sprite = Art.SoftDot; shadow.color = new Color(0, 0, 0, .45f); shadow.raycastTarget = false;
            var pet = UiKit.Node("pet", r).Fill().gameObject.AddComponent<Image>();
            pet.sprite = PetSprites.For(h.Seed.Look);
            pet.preserveAspect = true; pet.raycastTarget = false;
            if (x > 50) pet.rectTransform.localScale = new Vector3(-1, 1, 1);  // everyone faces the fire
            var name = UiKit.Label(r, BattleText.Prose(h.Seed.Name), UiKit.SmallSize - 2, Color.white, UiKit.Bold, TextAnchor.UpperCenter);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, -2), new Vector2(120, 18)).pivot = new Vector2(.5f, 1);
            name.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .7f);
            return pet.rectTransform;
        }

        // cpStory: the chapter, the run's tallies and everyone's level
        static void Story(RectTransform body, TaleRun run)
        {
            var s = Sheet.Create(body, "Story so far", new Vector2(.5f, .5f), Vector2.zero, 480, "The story so far");
            s.Dim(.35f);
            s.Closed = () => { if (s) UnityEngine.Object.Destroy(s.gameObject); };
            string beaten = string.IsNullOrEmpty(run.CampBoss) ? "" : $"{run.CampBoss} is beaten. ";
            UiKit.Label(s.Card, BattleText.Prose($"{beaten}Chapter {run.Ch} of {run.Title ?? "this tale"}."), UiKit.BodySize, Palette.InkSoft);
            var st = run.Stats;
            var chips = UiKit.Node("chips", s.Card);
            var grid = chips.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(200, 36);
            grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            Chip(chips, "⚔️", $"{st.Wins} {(st.Wins == 1 ? "win" : "wins")}");
            Chip(chips, "👑", $"{st.Bosses} {(st.Bosses == 1 ? "boss" : "bosses")}");
            Chip(chips, "👾", $"{st.Foes} foes");
            Chip(chips, "💧", $"{run.Gold} ink");
            foreach (var h in run.Party)
            {
                var row = UiKit.Node(h.Seed.Name, s.Card);
                UiKit.Row(row, 10);
                var pet = UiKit.Node("pet", row).gameObject.AddComponent<Image>();
                pet.sprite = PetSprites.For(h.Seed.Look); pet.preserveAspect = true; pet.raycastTarget = false;
                UiKit.Size(pet, 40, 40);
                UiKit.Label(row, BattleText.Prose(h.Seed.Name), UiKit.BodySize, Palette.Ink, UiKit.Bold);
                UiKit.Label(row, $"Level {h.Lvl}", UiKit.SmallSize, Palette.InkSoft);
            }
            s.First = UiKit.Secondary(s.Card, "Back to camp", s.Close);
            s.Open();
        }

        static void Chip(RectTransform parent, string icon, string text)
        {
            var c = UiKit.Panel(parent, text, Palette.Paper, 18);
            c.raycastTarget = false;
            UiKit.Row(c.rectTransform, 6, new RectOffset(10, 12, 4, 4), TextAnchor.MiddleLeft);
            UiKit.Icon(c.transform, icon, 22);
            UiKit.Label(c.transform, text, UiKit.SmallSize, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>The camp picture's life: twinkling stars, a flickering fire, pets breathing, a word by the fire, and the dawn.</summary>
        sealed class Camp : MonoBehaviour
        {
            public TaleRun Run;
            public readonly List<(Image star, float phase, float period)> Stars = new List<(Image, float, float)>();
            public readonly List<RectTransform> Pets = new List<RectTransform>();
            public RectTransform Flame;
            public Image Glow, Sky, Bubble;
            public Text Words;
            float sayUntil, dawnAt = -1;
            int said;
            Action done;

            /// <summary>A pet says something by the fire (tqFlv('camp'), {p} = the pet).</summary>
            public void Say()
            {
                var lines = EpicData.Current.CampLines;
                if (lines.Length == 0 || Run.Party.Count == 0) return;
                var h = Run.Party[said % Run.Party.Count];
                string line = lines[(int)Math.Floor(EpicRooms.Rng.Next() * lines.Length)].Replace("{p}", h.Seed.Name);
                said++;
                Words.text = BattleText.Prose(line);
                Bubble.gameObject.SetActive(true);
                FadeIn.On(Bubble, 0);
                sayUntil = Time.unscaledTime + 4.5f;
                Sound.Play("pop", .6f);
            }

            /// <summary>Morning comes (the sky warms over a second and a half), then done.</summary>
            public void Dawn(Action then)
            {
                if (dawnAt >= 0) return;
                done = then;
                dawnAt = Time.unscaledTime;
            }

            void Update()
            {
                float t = Time.unscaledTime;
                bool still = GameSettings.ReduceMotion;
                foreach (var s in Stars) s.star.color = new Color(1, 1, 1, still ? .7f : .35f + .55f * (.5f + .5f * Mathf.Sin((t + s.phase) * Mathf.PI * 2 / s.period)));
                if (Flame && !still)
                {
                    float f = Mathf.PerlinNoise(t * 3.1f, .3f);
                    Flame.localScale = new Vector3(.94f + .1f * Mathf.PerlinNoise(.7f, t * 2.3f), .9f + .2f * f, 1);
                    Glow.color = new Color(1, .65f, .25f, .36f + .16f * f);
                }
                for (int i = 0; i < Pets.Count; i++)
                    if (Pets[i]) Pets[i].localScale = new Vector3(Pets[i].localScale.x, still ? 1 : 1 + .025f * Mathf.Sin((t + i * 1.3f) * Mathf.PI * 2 / 2.6f), 1);
                if (Bubble.gameObject.activeSelf && t > sayUntil) Bubble.gameObject.SetActive(false);
                if (dawnAt < 0) return;
                float k = UiKit.EaseOut((t - dawnAt) / (still ? .4f : 1.6f));
                Sky.color = new Color(1, .74f, .42f, .5f * k);
                if (k < 1 || done == null) return;
                var then = done;
                done = null;
                then();
            }
        }
    }
}
