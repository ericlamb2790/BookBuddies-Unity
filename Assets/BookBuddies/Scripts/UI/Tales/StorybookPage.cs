using System.Collections.Generic;
using System.Linq;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// One page of a storybook in the reader (storybook.md §2.3): the cover, a story page (an illustration with the scene,
    /// drifting motes, the cast sliding in, the villain, a clash star or a nap, then the heading and words that write
    /// themselves in gold ink) or The End, with its footer and reading bar. Also the cover art the end screen and the
    /// lobby's shelf use.
    /// </summary>
    public sealed class StorybookPage : MonoBehaviour
    {
        static readonly Color Ink = Palette.Hex("#3a2a1a"), Heading = Palette.Hex("#6a2e1c"), Foot = Palette.Hex("#8a7358"), Gold = Palette.Hex("#d8962a");
        static readonly Color PaperMid = Palette.Hex("#f8eed6"), PaperEdge = Palette.Hex("#e4d2ad"), CoverInk = Palette.Hex("#fff8ea");
        static readonly Color[] PaperStops = { Palette.Hex("#e4d2ad"), Palette.Hex("#f6ead0"), Palette.Hex("#f8eed6"), Palette.Hex("#e9d8b6") };
        static readonly float[] PaperAt = { 0, .04f, .94f, 1 };
        const int Motes = 7, Sparkles = 5;

        Image bar;
        RectTransform ill, foe, clash, glow, zzz, nib, textBox, content;
        CanvasGroup foeFade;
        Vector2 foeHome;
        Text body;
        InkReveal ink;
        bool reduce, spread;
        float shownAt;
        readonly List<(RectTransform r, CanvasGroup fade, float home)> cast = new List<(RectTransform, CanvasGroup, float)>();
        readonly List<(RectTransform r, float speed, float phase)> motes = new List<(RectTransform, float, float)>();
        readonly List<(Graphic g, float phase)> sparkles = new List<(Graphic, float)>();
        readonly List<RectTransform> bobbers = new List<RectTransform>();

        /// <summary>The page's rect (pivot on the spine, for the turn).</summary>
        public RectTransform Rect => (RectTransform)transform;

        /// <summary>
        /// Builds page index of a book over the whole of "book" (sized already). spread lays a story page out as two
        /// facing pages; rd is the pace's ms per word for the ink.
        /// </summary>
        public static StorybookPage Build(RectTransform book, Dictionary<string, object> data, int index, bool spread, double rd)
        {
            var pages = data.Arr("pages");
            var p = (Dictionary<string, object>)pages[index];
            var paper = UiKit.Panel(book, "page " + index, PaperMid, 16);
            var r = paper.rectTransform;
            r.pivot = new Vector2(0, .5f);
            r.Fill();
            paper.gameObject.AddComponent<Mask>(); // the page's round corners clip what is drawn on it
            var page = paper.gameObject.AddComponent<StorybookPage>();
            page.reduce = GameSettings.ReduceMotion;
            page.spread = spread;
            page.shownAt = Time.unscaledTime;
            string k = p.Str("k", null);
            if (k == "cover") page.BuildCover(paper, data);
            else if (k == "end") page.BuildEnd(data);
            else page.BuildStory(data, p, index, rd);
            return page;
        }

        /// <summary>How far the page's reading bar has filled (0 to 1).</summary>
        public void Progress(float k) => bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);

        // ---- the three kinds of page ----

        void BuildCover(Image paper, Dictionary<string, object> data)
        {
            var c = CoverColors(data);
            paper.color = Color.white;
            var art = Lay(Rect, "gradient", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            art.sprite = Linear(new[] { c.a, c.b }, new[] { 0f, 1f }, 150);
            art.raycastTarget = false;
            var stitch = UiKit.Outline(paper, Color.white.WithAlpha(.2f), 10, 2, -14);
            stitch.rectTransform.offsetMin = new Vector2(26, 14);
            Spine(.05f, .18f);
            float h = Rect.rect.height, w = Rect.rect.width;
            var col = Lay(Rect, "words", .08f, .12f, .92f, .9f);
            var who = data.Arr("cast").Cast<Dictionary<string, object>>().ToList();
            var faces = Lay(col, "faces", 0, .56f, 1, 1);
            int n = Mathf.Min(3, who.Count);
            float size = Mathf.Min(h * .26f, w * .2f);
            for (int i = 0; i < n; i++)
            {
                var face = Pet(faces, who[i].Str("l", null), size);
                face.anchorMin = face.anchorMax = new Vector2(.5f, .5f);
                face.anchoredPosition = new Vector2((i - (n - 1) / 2f) * size * .82f, 0);
                bobbers.Add(face);
            }
            var title = Words(col, UiKit.SplitEmoji(data.Str("title"), out _), Mathf.RoundToInt(Mathf.Clamp(w / 12, 26, 64)), CoverInk, UiKit.Title, TextAnchor.MiddleCenter);
            Lay(title.rectTransform, null, 0, .3f, 1, .56f);
            int ch = data.Int("ch"), vol = data.Int("vol", 1);
            var sub = Words(col, $"{ch} chapter{(ch > 1 ? "s" : "")}{(vol > 1 ? $" · Book {vol}" : "")}", Mathf.RoundToInt(Mathf.Clamp(w / 40, 15, 22)), CoverInk.WithAlpha(.9f), UiKit.Body, TextAnchor.MiddleCenter);
            Lay(sub.rectTransform, null, 0, .2f, 1, .3f);
            string names = StoryBook.List(who.Select(x => x.Str("n")));
            var by = Words(col, $"A Tales of Pages storybook\nstarring {names}", Mathf.RoundToInt(Mathf.Clamp(w / 44, 14, 20)), CoverInk.WithAlpha(.8f), UiKit.Title, TextAnchor.MiddleCenter);
            by.fontStyle = FontStyle.Italic;
            Lay(by.rectTransform, null, 0, 0, 1, .2f);
            Bar(new Color(1, 1, 1, .15f), Palette.Hex("#ffe7a8"));
            stitch.transform.SetAsLastSibling();
        }

        void BuildEnd(Dictionary<string, object> data)
        {
            Paper();
            float w = Rect.rect.width;
            var col = Lay(Rect, "words", .1f, .1f, .9f, .9f);
            var end = Words(col, "The End", Mathf.RoundToInt(Mathf.Clamp(w / 10, 40, 84)), Heading, UiKit.Title, TextAnchor.MiddleCenter);
            Lay(end.rectTransform, null, 0, .6f, 1, .9f);
            var s = data.Obj("stats");
            int ch = data.Int("ch"), bosses = s.Int("bosses");
            var lines = new List<string>
            {
                $"<b>{ch}</b> chapter{(ch > 1 ? "s" : "")} · <b>{s.Int("wins")}</b> battles won",
                $"<b>{bosses}</b> villain{(bosses == 1 ? "" : "s")} sent home · <b>{s.Int("foes")}</b> troublemakers",
            };
            var best = s.Obj("best");
            if (best != null) lines.Add($"Biggest hit: <b>{JsMath.Num(best.Num("d"))}</b>, {best.Str("n")}’s {BattleText.Prose(best.Str("ab"))}");
            if (s.Num("heals") > 0) lines.Add($"<b>{JsMath.Num(s.Num("heals"))}</b> HP of cozy healing");
            var stats = Words(col, string.Join("\n", lines), Mathf.RoundToInt(Mathf.Clamp(w / 40, 16, 24)), Palette.Hex("#4a3a2a"), UiKit.Title, TextAnchor.MiddleCenter);
            stats.supportRichText = true;
            stats.lineSpacing = 1.25f;
            Lay(stats.rectTransform, null, 0, .22f, 1, .6f);
            var owners = data.Arr("cast").Cast<Dictionary<string, object>>().Select(x => x.Str("o")).Where(o => o.Length > 0).Distinct().ToList();
            string made = System.DateTimeOffset.FromUnixTimeMilliseconds((long)data.Num("made")).LocalDateTime.ToShortDateString();
            var by = Words(col, $"{(owners.Count > 0 ? "Read together by " + StoryBook.List(owners) : "Read by you")}\n{made}", Mathf.RoundToInt(Mathf.Clamp(w / 46, 14, 20)), Foot, UiKit.Title, TextAnchor.MiddleCenter);
            by.fontStyle = FontStyle.Italic;
            Lay(by.rectTransform, null, 0, 0, 1, .2f);
            Bar(Heading.WithAlpha(.1f), Palette.Hex("#c8862a"));
        }

        void BuildStory(Dictionary<string, object> data, Dictionary<string, object> p, int index, double rd)
        {
            Paper();
            float w = Rect.rect.width, h = Rect.rect.height;
            string act = p.Str("act", "calm");
            ill = UiKit.Node("picture", Rect);
            ill.gameObject.AddComponent<RectMask2D>();
            textBox = UiKit.Node("words", Rect);
            var foot = UiKit.Node("footer", Rect);
            if (spread)
            {
                Lay(ill, null, 0, 0, .5f, 1, new Vector2(26, 26), new Vector2(-34, -26));
                Lay(textBox, null, .5f, 0, 1, 1, new Vector2(40, 52), new Vector2(-46, -42));
                Lay(foot, null, .5f, 0, 1, 0, new Vector2(40, 16), new Vector2(-46, 40));
                var spine = Lay(Rect, "spine", .5f, 0, .5f, 1, new Vector2(-30, 0), new Vector2(30, 0)).gameObject.AddComponent<Image>();
                spine.sprite = Linear(new[] { Color.clear, Palette.Hex("#5a3a1a24"), Palette.Hex("#3a2a1a40"), Palette.Hex("#5a3a1a24"), Color.clear }, new[] { 0, .45f, .5f, .55f, 1 }, 90);
                spine.raycastTarget = false;
            }
            else
            {
                Lay(ill, null, 0, 1, 1, 1, new Vector2(14, -14 - Mathf.Min((w - 28) * .75f, h * .44f)), new Vector2(-14, -14));
                Lay(textBox, null, 0, 0, 1, 1, new Vector2(24, 34), new Vector2(-24, ill.offsetMin.y - 14));
                Lay(foot, null, 0, 0, 1, 0, new Vector2(22, 10), new Vector2(-22, 32));
            }
            Picture(data, p, act);
            FooterOf(foot, data.Str("title"), index);
            Writing(p.Str("t"), p.Str("x"), rd);
            Bar(Heading.WithAlpha(.1f), Palette.Hex("#c8862a"));
        }

        // ---- the illustration ----

        void Picture(Dictionary<string, object> data, Dictionary<string, object> p, string act)
        {
            var scenes = TalesData.Current.Scenes;
            var sc = scenes[((p.Int("sc") % scenes.Count) + scenes.Count) % scenes.Count];
            var sky = Lay(ill, "scene", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            sky.sprite = Linear(sc.Stops.Select(s => Palette.Hex(s.color)).ToArray(), sc.Stops.Select(s => (float)s.at).ToArray(), 180);
            sky.raycastTarget = false;
            Vector2 size = ill.rect.size;
            float big = Mathf.Max(size.x, size.y);
            var light = Dot(ill, UiKit.Glow, Palette.Hex(sc.Glow), size.x * .9f);
            light.anchorMin = light.anchorMax = new Vector2((float)sc.Gx, 1 - (float)sc.Gy);
            for (int i = 0; i < Motes; i++)
            {
                var m = Dot(ill, Art.SoftDot, new Color(1, 1, 1, .6f), big * .012f);
                m.anchorMin = m.anchorMax = new Vector2(Random.value, 0);
                motes.Add((m, .04f + Random.value * .05f, Random.value));
            }
            if (act == "calm" || act == "rest")
            {
                glow = Dot(ill, UiKit.Glow, Palette.Hex("#ffe7a8"), size.x * .4f);
                glow.anchorMin = glow.anchorMax = new Vector2(.5f, .72f);
            }
            if (act == "calm" || act == "join")
                for (int i = 0; i < Sparkles; i++)
                {
                    var s = Dot(ill, Star, Palette.Hex("#fff8d0"), big * .035f);
                    s.anchorMin = s.anchorMax = new Vector2(.1f + Random.value * .8f, .42f + Random.value * .5f);
                    sparkles.Add((s.GetComponent<Image>(), Random.value * 6));
                }

            var all = data.Arr("cast").Cast<Dictionary<string, object>>().ToList();
            var who = p.Arr("h").Select(x => (int)(double)x).Where(j => j >= 0 && j < all.Count).ToList();
            bool solo = who.Count == 1 && !p.Has("f");
            float pet = size.y * (who.Count >= 3 ? .34f : .44f);
            for (int j = 0; j < who.Count; j++)
            {
                var holder = UiKit.Node("pet " + j, ill);
                holder.anchorMin = holder.anchorMax = new Vector2(solo ? .5f : .05f, .07f);
                holder.pivot = new Vector2(solo ? .5f : 0, 0);
                holder.sizeDelta = new Vector2(pet * 200 / 232f, pet);
                float home = solo ? 0 : j * (pet * 200 / 232f - 12);
                holder.anchoredPosition = new Vector2(home, 0);
                var face = Pet(holder, all[who[j]].Str("l", null), pet);
                face.anchorMin = face.anchorMax = face.pivot = new Vector2(.5f, act == "nap" ? .3f : 0);
                face.anchoredPosition = Vector2.zero;
                if (act == "nap") face.localRotation = Quaternion.Euler(0, 0, 70);
                cast.Add((holder, holder.gameObject.AddComponent<CanvasGroup>(), home));
            }
            var fv = StoryBook.FoeOf(p.Obj("f"));
            if (fv != null)
            {
                float fw = size.x * (spread ? .44f : .36f);
                foe = UiKit.Node("villain", ill);
                foe.anchorMin = foe.anchorMax = foe.pivot = new Vector2(1, 0);
                foe.anchoredPosition = foeHome = new Vector2(-size.x * .06f, size.y * .09f);
                foe.sizeDelta = new Vector2(fw, Mathf.Min(fw, size.y * .8f));
                FoeArt.MakeUi(foe, FoeArt.For(fv, p.Obj("f").Truthy("b")));
                foeFade = foe.gameObject.AddComponent<CanvasGroup>();
                if (act == "clash")
                {
                    clash = Dot(ill, Star, Palette.Hex("#fff6c8"), big * .12f);
                    clash.anchorMin = clash.anchorMax = new Vector2(.55f, .5f);
                    var halo = Dot(clash, UiKit.Glow, Palette.Hex("#ff9a3c").WithAlpha(.7f), big * .22f);
                    halo.SetAsFirstSibling();
                }
            }
            if (act == "nap")
            {
                var z = Words(ill, "z z z", Mathf.RoundToInt(size.y * .09f), Palette.Hex("#e8eeff"), UiKit.Title, TextAnchor.MiddleCenter);
                z.fontStyle = FontStyle.BoldAndItalic;
                zzz = z.rectTransform.Pin(new Vector2(.2f, .75f), Vector2.zero, new Vector2(size.x * .3f, size.y * .2f));
            }
            var vignette = Lay(ill, "vignette", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            vignette.sprite = UiKit.Vignette; vignette.color = Palette.Hex("#1a0f0a").WithAlpha(.45f); vignette.raycastTarget = false;
            UiKit.Outline(ill, Ink.WithAlpha(.16f), 10, 3).transform.SetAsLastSibling();
        }

        // ---- the words ----

        void Writing(string heading, string text, double rd)
        {
            var view = Lay(textBox, "view", 0, 0, 1, 1);
            view.gameObject.AddComponent<RectMask2D>();
            content = UiKit.Node("content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var head = Words(content, heading, 30, Heading, UiKit.Title, TextAnchor.UpperLeft);
            body = Words(content, text, 22, Ink, UiKit.Title, TextAnchor.UpperLeft);
            body.lineSpacing = 1.25f;
            var dot = Dot(body.rectTransform, Star, Gold, 18);
            dot.anchorMin = dot.anchorMax = new Vector2(0, 1);
            nib = dot;
            ink = body.gameObject.AddComponent<InkReveal>();
            ink.Begin(text, reduce ? 0 : (float)rd / 1000f, nib);
            Fit(head, view.rect.height, view.rect.width);
            if (content.sizeDelta.y > view.rect.height + 1)
            {
                var scroll = view.gameObject.AddComponent<ScrollRect>();
                scroll.content = content; scroll.viewport = view; scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
                view.gameObject.AddComponent<Image>().color = Color.clear; // something to drag on
            }
        }

        // the biggest size the words fit at, then smaller; a long page keeps a readable size and scrolls
        void Fit(Text head, float boxH, float boxW)
        {
            int max = Mathf.RoundToInt(Mathf.Min(spread ? 34 : 28, Mathf.Max(20, boxW / (spread ? 15 : 13)))), min = spread ? 18 : 17;
            head.rectTransform.Pin(new Vector2(0, 1), Vector2.zero, new Vector2(boxW, 10)); // widths first: they decide the wrapping
            body.rectTransform.Pin(new Vector2(0, 1), Vector2.zero, new Vector2(boxW, 10));
            float hh = 0, gap = 0, bh = 0;
            for (int fs = max; fs >= min; fs--)
            {
                head.fontSize = Mathf.RoundToInt(fs * 1.3f);
                body.fontSize = fs;
                hh = head.preferredHeight; gap = fs * .45f; bh = body.preferredHeight;
                if (hh + gap + bh <= boxH) break;
            }
            head.rectTransform.sizeDelta = new Vector2(boxW, hh);
            body.rectTransform.sizeDelta = new Vector2(boxW, bh);
            body.rectTransform.anchoredPosition = new Vector2(0, -hh - gap);
            content.sizeDelta = new Vector2(0, hh + gap + bh);
        }

        void FooterOf(RectTransform foot, string title, int index)
        {
            var t = Words(foot, BattleText.Prose(UiKit.SplitEmoji(title, out _)), 14, Foot, UiKit.Title, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Italic;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Lay(t.rectTransform, null, 0, 0, .8f, 1);
            var n = Words(foot, index.ToString(), 14, Foot, UiKit.Title, TextAnchor.MiddleRight);
            n.fontStyle = FontStyle.Italic;
            Lay(n.rectTransform, null, .8f, 0, 1, 1);
        }

        // ---- motion ----

        void Update()
        {
            float t = (Time.unscaledTime - shownAt) * 1000; // ms, like the site's keyframes
            for (int j = 0; j < cast.Count; j++)
            {
                var (r, fade, home) = cast[j];
                float a = reduce ? 1 : Mathf.Clamp01((t - 100 - j * 120) / 700);
                fade.alpha = a;
                float bob = reduce ? 0 : Mathf.Sin(t / 420 + j * 1.7f) * 6;
                r.anchoredPosition = new Vector2(home - 60 * (1 - UiKit.EaseOut(a)), bob);
            }
            if (foe != null)
            {
                float a = reduce ? 1 : Mathf.Clamp01((t - 250) / 700);
                foeFade.alpha = a;
                foe.anchoredPosition = foeHome + new Vector2(60 * (1 - UiKit.EaseOut(a)), reduce ? 0 : Mathf.Sin(t / 360) * 7);
            }
            if (reduce) return;
            if (clash != null) clash.localScale = Vector3.one * (.8f + .4f * Mathf.Abs(Mathf.Sin(t / 500)));
            if (clash != null) clash.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t / 800) * 20);
            if (glow != null) glow.localScale = Vector3.one * (1 + .08f * Mathf.Sin(t / 480));
            if (zzz != null) zzz.anchoredPosition = new Vector2(0, Mathf.Sin(t / 380) * 8);
            foreach (var (g, phase) in sparkles) { var c = g.color; c.a = .6f + .4f * Mathf.Sin(t / 320 + phase); g.color = c; }
            if (ill != null)
                foreach (var (r, speed, phase) in motes)
                {
                    float k = (t / 1000 * speed + phase) % 1;
                    r.anchoredPosition = new Vector2(Mathf.Sin(k * 6 + phase * 9) * 12, ill.rect.height * (.1f + k * .8f));
                    r.GetComponent<Image>().color = new Color(1, 1, 1, .6f * Mathf.Sin(k * Mathf.PI));
                }
            for (int i = 0; i < bobbers.Count; i++) bobbers[i].localPosition = new Vector3(bobbers[i].localPosition.x, Mathf.Sin(t / 420 + i * 1.6f) * 6, 0);
        }

        void LateUpdate()
        {
            if (ink == null || content == null || !ink.Writing) return;
            // the line being written stays in view: past the bottom, it scrolls to a third of the way down
            var view = (RectTransform)content.parent;
            float y = -nib.anchoredPosition.y - body.rectTransform.anchoredPosition.y; // depth of the nib in the content
            float top = content.anchoredPosition.y, h = view.rect.height;
            if (y + body.fontSize > top + h - 6) content.anchoredPosition = new Vector2(0, Mathf.Clamp(y - h * .35f, 0, content.sizeDelta.y - h));
        }

        // ---- pieces ----

        void Paper()
        {
            var tex = Lay(Rect, "paper", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            tex.sprite = Linear(PaperStops, PaperAt, 90);
            tex.raycastTarget = false;
            var hi = Dot(Rect, UiKit.Glow, Palette.Hex("#fffaf0").WithAlpha(.8f), Rect.rect.width * .9f);
            hi.anchorMin = hi.anchorMax = new Vector2(.18f, .92f);
            Spine(.03f, .2f);
        }

        // the soft shade along the spine
        void Spine(float width, float alpha)
        {
            var s = Lay(Rect, "spine shade", 0, 0, width, 1).gameObject.AddComponent<Image>();
            s.sprite = Linear(new[] { Color.black.WithAlpha(alpha), Color.clear }, new[] { 0f, 1f }, 90);
            s.raycastTarget = false;
        }

        void Bar(Color track, Color fill)
        {
            var b = UiKit.Panel(Rect, "reading bar", track, 2);
            b.raycastTarget = false;
            Lay(b.rectTransform, null, 0, 0, 1, 0, new Vector2(22, 6), new Vector2(-22, 9));
            bar = UiKit.Panel(b.transform, "fill", fill, 2);
            bar.raycastTarget = false;
            Lay(bar.rectTransform, null, 0, 0, 0, 1);
        }

        RectTransform Pet(RectTransform parent, string look, float size)
        {
            var img = UiKit.Node("pet", parent).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * 200 / 232f, size)).gameObject.AddComponent<Image>();
            img.sprite = string.IsNullOrEmpty(look) ? Art.Emote("📖") : PetSprites.For(look);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img.rectTransform;
        }

        static Text Words(Transform parent, string text, int size, Color color, Font font, TextAnchor align)
        {
            var t = UiKit.Label(parent, text, size, color, font, align);
            t.supportRichText = false;
            return t;
        }

        static RectTransform Dot(Transform parent, Sprite sprite, Color color, float size)
        {
            var img = UiKit.Node("dot", parent).Pin(new Vector2(.5f, .5f), Vector2.zero, Vector2.one * size).gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false;
            return img.rectTransform;
        }

        static RectTransform Lay(RectTransform r, string name, float x0, float y0, float x1, float y1, Vector2 min = default, Vector2 max = default)
        {
            if (name != null) r = UiKit.Node(name, r);
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = min; r.offsetMax = max;
            return r;
        }

        // ---- covers (end screen, shelf) ----

        /// <summary>A book's cover colours: TQ_COVERS by the first pet's genre.</summary>
        public static (Color a, Color b) CoverColors(Dictionary<string, object> book)
        {
            var covers = TalesData.Current.Covers;
            var first = book.Arr("cast")?.FirstOrDefault() as Dictionary<string, object>;
            var c = covers[(first?.Int("g") ?? 0) % covers.Count];
            return (Palette.Hex(c.from), Palette.Hex(c.to));
        }

        /// <summary>
        /// A shelf tile (tqSbTile): a 3:4 cover in the book's colours with its first pet and the title, and "n chapters"
        /// under it. Clicking opens the book.
        /// </summary>
        public static Button CoverTile(Transform parent, Dictionary<string, object> book, System.Action open, float width = 120)
        {
            var b = UiKit.Button(parent, "book", Color.white, open, 10);
            var r = (RectTransform)b.transform;
            UiKit.Size(b, width, width * 4 / 3f + 28);
            r.sizeDelta = new Vector2(width, width * 4 / 3f + 28); // the cover is drawn at this size before the shelf lays it out
            ((Image)b.targetGraphic).color = Color.clear;
            var cover = UiKit.Node("cover", r);
            cover.anchorMin = new Vector2(0, 1); cover.anchorMax = Vector2.one; cover.pivot = new Vector2(.5f, 1);
            cover.offsetMin = new Vector2(0, -width * 4 / 3f); cover.offsetMax = Vector2.zero;
            Cover(cover, book, false);
            int ch = book.Int("ch");
            var sub = UiKit.Label(r, $"{ch} chapter{(ch > 1 ? "s" : "")}", UiKit.SmallSize - 1, Palette.InkSoft, null, TextAnchor.MiddleLeft);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            Lay(sub.rectTransform, null, 0, 0, 1, 0, new Vector2(4, 0), new Vector2(0, 24));
            return b;
        }

        /// <summary>Paints a cover into rect: the gradient, a spine, up to 3 pets (just the first when small), and the title.</summary>
        public static void Cover(RectTransform rect, Dictionary<string, object> book, bool big)
        {
            var c = CoverColors(book);
            var face = UiKit.Panel(rect, "cover", Color.white, big ? 14 : 8);
            face.raycastTarget = false;
            Lay(face.rectTransform, null, 0, 0, 1, 1);
            var mask = face.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            var grad = Lay(face.rectTransform, "gradient", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            grad.sprite = Linear(new[] { c.a, c.b }, new[] { 0f, 1f }, big ? 150 : 160);
            grad.raycastTarget = false;
            var spine = Lay(face.rectTransform, "spine", 0, 0, .06f, 1).gameObject.AddComponent<Image>();
            spine.color = new Color(0, 0, 0, .14f); spine.raycastTarget = false;
            Vector2 size = rect.rect.size;
            var who = book.Arr("cast").Cast<Dictionary<string, object>>().Take(big ? 3 : 1).ToList();
            float pet = big ? size.y * .42f : size.x * .56f;
            for (int i = 0; i < who.Count; i++)
            {
                var img = UiKit.Node("pet", face.transform).gameObject.AddComponent<Image>();
                img.sprite = PetSprites.For(who[i].Str("l", null)) ?? Art.Emote("📖");
                img.preserveAspect = true; img.raycastTarget = false;
                img.rectTransform.Pin(new Vector2(.5f, big ? .66f : .62f), new Vector2((i - (who.Count - 1) / 2f) * pet * .8f, 0), new Vector2(pet * 200 / 232f, pet));
            }
            var t = UiKit.Label(face.transform, UiKit.SplitEmoji(book.Str("title"), out _), big ? 24 : 14, CoverInk, UiKit.Title, big ? TextAnchor.LowerCenter : TextAnchor.LowerLeft);
            t.fontStyle = big ? FontStyle.Normal : FontStyle.Italic;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Lay(t.rectTransform, null, 0, 0, 1, big ? .38f : .42f, new Vector2(big ? 18 : 10, big ? 60 : 8), new Vector2(big ? -18 : -6, 0));
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .45f);
        }

        // ---- textures made in code ----

        static readonly Dictionary<string, Sprite> linear = new Dictionary<string, Sprite>();
        static Sprite star;

        /// <summary>A linear gradient (CSS angle: 90 runs left to right, 180 top to bottom) through colours at stops 0-1.</summary>
        public static Sprite Linear(Color[] colors, float[] at, float angle)
        {
            string key = angle + "|" + string.Join(",", colors.Select(c => ColorUtility.ToHtmlStringRGBA(c))) + "|" + string.Join(",", at);
            if (linear.TryGetValue(key, out var s)) return s;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)); // y up
            float len = Mathf.Abs(dir.x) + Mathf.Abs(dir.y);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + .5f) / n - .5f, (y + .5f) / n - .5f);
                    px[y * n + x] = Sample(colors, at, Vector2.Dot(p, dir) / len + .5f);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return linear[key] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
        }

        static Color Sample(Color[] colors, float[] at, float t)
        {
            if (t <= at[0]) return colors[0];
            for (int i = 1; i < at.Length; i++)
                if (t <= at[i]) return Color.Lerp(colors[i - 1], colors[i], (t - at[i - 1]) / Mathf.Max(1e-4f, at[i] - at[i - 1]));
            return colors[colors.Length - 1];
        }

        /// <summary>A four-point sparkle (✦).</summary>
        public static Sprite Star => star ? star : star = MakeStar();

        static Sprite MakeStar()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs((x + .5f) / n * 2 - 1), dy = Mathf.Abs((y + .5f) / n * 2 - 1);
                    float a = Mathf.Clamp01((1 - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy))) * 6);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
        }
    }

    /// <summary>
    /// Magic ink: the words of a Text appear one at a time, rising a little and glowing gold before they settle into ink,
    /// with a sparkle nib after the newest word (the site's reveal). Legacy Text makes one quad per visible character.
    /// </summary>
    public sealed class InkReveal : BaseMeshEffect
    {
        const float Delay = .65f, Fade = .38f, Glow = 1f, Rise = 6;
        static readonly Color GoldInk = Palette.Hex("#d8962a");

        int[] visible, every; // word index per visible character, and per character with spaces as -1
        int words;
        float step, startedAt;
        RectTransform nib;
        Graphic nibArt;
        Vector2 tip;  // where the nib goes: after the newest word
        bool hasTip;

        /// <summary>True while words are still appearing or glowing.</summary>
        public bool Writing => step > 0 && Time.unscaledTime - startedAt < Delay + words * step + Glow;

        /// <summary>Starts writing text, one word every "step" seconds (0 shows it all at once).</summary>
        public void Begin(string text, float step, RectTransform nib)
        {
            this.step = step;
            this.nib = nib;
            nibArt = nib.GetComponent<Graphic>();
            var vis = new List<int>(); var all = new List<int>();
            int w = -1; bool inWord = false;
            foreach (char ch in text ?? "")
            {
                bool space = char.IsWhiteSpace(ch);
                if (!space && !inWord) w++;
                inWord = !space;
                all.Add(space ? -1 : w);
                if (!space) vis.Add(w);
            }
            visible = vis.ToArray(); every = all.ToArray(); words = w + 1;
            startedAt = Time.unscaledTime;
            nibArt.enabled = false;
        }

        // the nib follows the newest word (placed here, not in ModifyMesh: nothing may change during the UI rebuild)
        void Update()
        {
            if (step <= 0) return;
            bool on = Writing && hasTip;
            if (nibArt.enabled != on) nibArt.enabled = on;
            if (on) nib.anchoredPosition = tip;
            if (Writing) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || step <= 0 || visible == null) return;
            int quads = vh.currentVertCount / 4;
            var map = quads == visible.Length ? visible : quads == every.Length ? every : null;
            if (map == null) return;
            var ink = graphic.color;
            float t = Time.unscaledTime - startedAt;
            int newest = Mathf.Min(words - 1, Mathf.FloorToInt((t - Delay) / step));
            hasTip = false;
            var v = new UIVertex();
            float right = 0, top = 0;
            for (int q = 0; q < quads; q++)
            {
                int w = map[q];
                if (w < 0) continue;
                float age = t - Delay - w * step;
                float a = Mathf.Clamp01(age / Fade), rise = (1 - UiKit.EaseOut(a)) * Rise;
                var c = Color.Lerp(GoldInk, ink, Mathf.Clamp01(age / Glow));
                c.a = ink.a * a;
                for (int k = 0; k < 4; k++)
                {
                    vh.PopulateUIVertex(ref v, q * 4 + k);
                    v.color = c;
                    v.position.y -= rise;
                    vh.SetUIVertex(v, q * 4 + k);
                    if (w == newest && (!hasTip || v.position.x > right)) { right = v.position.x; top = hasTip ? Mathf.Max(top, v.position.y) : v.position.y; hasTip = true; }
                }
            }
            if (!hasTip) return;
            var rt = (RectTransform)transform;
            float size = graphic is Text text ? text.fontSize : 20;
            tip = new Vector2(right + rt.rect.width * rt.pivot.x + size * .4f, top - rt.rect.height * (1 - rt.pivot.y) - size * .35f);
        }
    }
}
