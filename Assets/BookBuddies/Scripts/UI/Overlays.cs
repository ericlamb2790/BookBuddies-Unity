using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Road;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Name tags, chat bubbles, floating emotes and sleepy z's over each pet, drawn like the website:
    /// your tag is amber and says "You", other readers get an ink pill, villagers get plain slate text.
    /// Also the world's signposts: a little floating icon over every place you can visit, a label over
    /// whatever the mouse is on, the "E  Sit" prompt when your pet is next to something, and the villains'
    /// names with their ❗ and 💫.
    /// </summary>
    public sealed class Overlays : MonoBehaviour
    {
        const float BubbleMaxWidth = 228;
        const float EmoteSeconds = 1.8f;
        static readonly Color VillagerInk = Palette.Hex("#55657a");

        sealed class Marks
        {
            public RectTransform Tag, Bubble, Emote, Zzz;
            public Text BubbleText;
            public Image BubbleIcon, EmoteImage;
            public string ShownBubble;
        }

        sealed class SpotMark { public TownMap.Spot Spot; public Vector3 Top; public RectTransform Pin; public CanvasGroup Fade; public float Phase; }
        sealed class FoeMarks { public RectTransform Label, Marker; public Text Text; public Image Badge, MarkerImage; public string Shown; }

        PlazaWorld world;
        RectTransform root;
        Canvas canvas;
        readonly Dictionary<PetActor, Marks> marks = new Dictionary<PetActor, Marks>();
        readonly List<PetActor> gone = new List<PetActor>();
        readonly List<SpotMark> spots = new List<SpotMark>();
        readonly Dictionary<RoadFoe, FoeMarks> foeMarks = new Dictionary<RoadFoe, FoeMarks>();
        readonly List<RoadFoe> goneFoes = new List<RoadFoe>();
        RectTransform spotLayer, foeLayer, hoverTag, prompt;
        Text hoverText, promptText;
        Image hoverIcon;
        GameObject keyCap, padKey, tapKey;
        Text tapText;
        CanvasGroup promptFade;
        Usable shownPrompt;
        float promptAt;

        public static Overlays Create(PlazaWorld world)
        {
            var canvas = UiKit.MakeCanvas("Pet labels", 10);
            var o = canvas.gameObject.AddComponent<Overlays>();
            o.world = world;
            o.canvas = canvas;
            o.root = (RectTransform)canvas.transform;
            canvas.GetComponent<GraphicRaycaster>().enabled = false; // labels never block taps
            o.spotLayer = UiKit.Node("places", o.root).Fill();
            o.foeLayer = UiKit.Node("foes", o.root).Fill();
            o.BuildSpotMarks();
            o.BuildHoverTag();
            o.BuildPrompt();
            return o;
        }

        void LateUpdate()
        {
            if (world == null || world.Me == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            foreach (var a in world.Actors)
            {
                if (a == null) continue;
                if (!marks.TryGetValue(a, out var m)) marks[a] = m = Build(a);
                Place(a, m, cam);
            }
            gone.Clear();
            foreach (var a in marks.Keys) if (a == null) gone.Add(a);
            foreach (var a in gone) { Destroy(marks[a].Tag.parent.gameObject); marks.Remove(a); }
            if (marks.TryGetValue(world.Me, out var mine)) mine.Tag.parent.SetAsLastSibling(); // yours on top

            PlaceSpotMarks(cam);
            PlaceFoeMarks(cam);
            PlaceHoverTag(cam);
            PlacePrompt(cam);
        }

        Vector2 ToCanvas(Camera cam, Vector3 world, out bool visible)
        {
            Vector3 p = cam.WorldToScreenPoint(world);
            visible = p.z > 0;
            return (Vector2)p / canvas.scaleFactor;
        }

        // ---- places: a small floating icon over each, bobbing gently ----

        void BuildSpotMarks()
        {
            foreach (var spot in world.Map.Spots)
            {
                if (spot.Kind == "garden") continue;
                var disc = UiKit.Panel(spotLayer, spot.Name, Palette.Cream, 20);
                disc.raycastTarget = false;
                var r = disc.rectTransform.Pin(Vector2.zero, Vector2.zero, new Vector2(40, 40));
                r.pivot = new Vector2(.5f, 0);
                UiKit.Shadow(r, 20, 10, 2, .25f);
                ((RectTransform)UiKit.Icon(r, spot.Icon, 26).transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(26, 26)).pivot = new Vector2(.5f, .5f);
                var tail = UiKit.Panel(r, "tail", Palette.Cream, 0);
                tail.raycastTarget = false;
                ((RectTransform)tail.transform).Pin(new Vector2(.5f, 0), new Vector2(0, 1), new Vector2(10, 10)).localRotation = Quaternion.Euler(0, 0, 45);
                tail.transform.SetAsFirstSibling();
                spots.Add(new SpotMark { Spot = spot, Top = world.View.MarkerPoint(spot), Pin = r, Fade = r.gameObject.AddComponent<CanvasGroup>(), Phase = spot.Use.x * .7f });
            }
        }

        void PlaceSpotMarks(Camera cam)
        {
            var hovered = world.Hover?.Thing as TownMap.Spot;
            var prompted = world.Prompt?.Thing as TownMap.Spot;
            bool calm = GameSettings.ReduceMotion;
            foreach (var m in spots)
            {
                var at = ToCanvas(cam, m.Top, out bool visible);
                bool hide = !visible || m.Spot == world.Hint || m.Spot == prompted;
                float want = hide ? 0 : m.Spot == hovered ? 1 : .88f;
                m.Fade.alpha = Mathf.MoveTowards(m.Fade.alpha, want, Time.unscaledDeltaTime / .18f);
                UiKit.Show(m.Pin, m.Fade.alpha > 0);
                if (m.Fade.alpha <= 0) continue;
                float bob = calm ? 0 : Mathf.Sin(Time.unscaledTime * 2.1f + m.Phase) * 3.5f;
                m.Pin.anchoredPosition = at + new Vector2(0, 7 + bob);
                float grow = m.Spot == hovered ? 1.15f : 1;
                m.Pin.localScale = Vector3.one * Mathf.MoveTowards(m.Pin.localScale.x, grow, Time.unscaledDeltaTime / .15f);
            }
        }

        // ---- the label over whatever the mouse is on ----

        void BuildHoverTag()
        {
            var pill = UiKit.Panel(root, "hover", Palette.Ink, 18);
            pill.raycastTarget = false;
            hoverTag = pill.rectTransform.Pin(Vector2.zero, Vector2.zero, new Vector2(10, 36));
            hoverTag.pivot = new Vector2(.5f, 0);
            UiKit.Row(hoverTag, 7, new RectOffset(12, 16, 5, 5), TextAnchor.MiddleCenter);
            UiKit.Hug(hoverTag);
            UiKit.Shadow(hoverTag, 18, 10, 3, .3f);
            hoverIcon = UiKit.Icon(hoverTag, null, 22);
            hoverText = UiKit.Label(hoverTag, "", UiKit.BodySize, Palette.Cream, UiKit.Bold);
            hoverText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Show(hoverTag, false);
        }

        void PlaceHoverTag(Camera cam)
        {
            var h = world.Hover;
            bool show = h != null && !h.Same(world.Prompt);
            UiKit.Show(hoverTag, show);
            if (!show) return;
            if (hoverText.text != h.Label) { hoverText.text = h.Label; UiKit.SetIcon(hoverIcon, h.Icon); }
            var at = ToCanvas(cam, h.Top, out _);
            hoverTag.anchoredPosition = at + new Vector2(0, h.Kind == Usable.Kinds.Spot ? 56 : 12);
        }

        // ---- the prompt: [E] Sit, [A] Sit, or Tap Sit, over the thing your pet is next to ----

        void BuildPrompt()
        {
            var pill = UiKit.Panel(root, "prompt", Palette.Cream, 24);
            pill.raycastTarget = false;
            prompt = pill.rectTransform.Pin(Vector2.zero, Vector2.zero, new Vector2(10, 48));
            prompt.pivot = new Vector2(.5f, 0);
            UiKit.Row(prompt, 10, new RectOffset(8, 19, 8, 8), TextAnchor.MiddleCenter);
            UiKit.Hug(prompt);
            UiKit.Shadow(prompt, 24, 12, 4, .28f);
            UiKit.Outline(pill, Palette.Amber, 24, 2);
            promptFade = prompt.gameObject.AddComponent<CanvasGroup>();

            var cap = UiKit.Panel(prompt, "key", Palette.Ink, 7);
            cap.raycastTarget = false;
            UiKit.Size(cap, 32, 32);
            ((RectTransform)UiKit.Label(cap.transform, "E", UiKit.BodySize, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter).transform).Fill();
            keyCap = cap.gameObject;
            padKey = UiKit.PadGlyph(prompt, "A", 32).gameObject;
            var tap = UiKit.Panel(prompt, "tap", Palette.Amber, 16);
            tap.raycastTarget = false;
            UiKit.Row(tap.rectTransform, 0, new RectOffset(11, 11, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Size(tap, -1, 32);
            tapText = UiKit.Label(tap.transform, "Tap", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            tapText.horizontalOverflow = HorizontalWrapMode.Overflow;
            tapKey = tap.gameObject;
            promptText = UiKit.Label(prompt, "", UiKit.BodySize + 3, Palette.Ink, UiKit.Bold);
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Show(prompt, false);
        }

        void PlacePrompt(Camera cam)
        {
            var p = world.Prompt;
            if (p == null) { shownPrompt = null; UiKit.Show(prompt, false); return; }
            if (!p.Same(shownPrompt) || promptText.text != p.Verb)
            {
                shownPrompt = shownPrompt ?? new Usable();
                shownPrompt.Kind = p.Kind; shownPrompt.Thing = p.Thing;
                promptText.text = p.Verb;
                promptAt = Time.unscaledTime;
            }
            // which key to show: the gamepad's A, the E key, or a tap / click for the mouse and touch
            bool pad = PlazaInput.UsingGamepad, pointer = !pad && PlazaInput.UsingPointer;
            if (keyCap.activeSelf != (!pad && !pointer)) keyCap.SetActive(!pad && !pointer);
            if (padKey.activeSelf != pad) padKey.SetActive(pad);
            if (tapKey.activeSelf != pointer) tapKey.SetActive(pointer);
            if (pointer) tapText.text = WorldPointer.IsTouch || Application.isMobilePlatform ? "Tap" : "Click";
            UiKit.Show(prompt, true);

            float k = GameSettings.ReduceMotion ? 1 : UiKit.EaseBack((Time.unscaledTime - promptAt) / .18f);
            promptFade.alpha = Mathf.Clamp01((Time.unscaledTime - promptAt) / .12f);
            prompt.localScale = Vector3.one * Mathf.Lerp(.85f, 1, k);
            var at = ToCanvas(cam, p.Top, out _);
            prompt.anchoredPosition = at + new Vector2(0, p.Kind == Usable.Kinds.Pet ? 36 : 10);
        }

        // ---- villains: "★ Inkblot · Lv 3" under each, ❗ when it chases you, 💫 when stunned ----

        void PlaceFoeMarks(Camera cam)
        {
            var road = world.Road;
            if (road == null) return;
            float tile = TilePixels(cam) / canvas.scaleFactor;
            int size = Mathf.Clamp(Mathf.RoundToInt(.19f * tile), 18, 31);
            foreach (var f in road.All)
            {
                if (!foeMarks.TryGetValue(f, out var m)) foeMarks[f] = m = BuildFoeMarks(f);
                var at = ToCanvas(cam, f.PointAt(.6f), out bool visible);
                UiKit.Show(m.Label, visible && GameSettings.ShowNames);
                if (m.Label.gameObject.activeSelf)
                {
                    m.Label.anchoredPosition = at;
                    if (m.Shown != f.Label) { m.Shown = f.Label; m.Text.text = f.Label; }
                    m.Text.fontSize = size;
                    m.Text.color = f.LabelColour;
                    UiKit.Size(m.Badge, size, size);
                    m.Badge.color = f.Elite ? f.LabelColour : Color.white;
                }
                string marker = f.Marker(road.Clock, out float markerSize);
                UiKit.Show(m.Marker, visible && marker != null && markerSize > 0);
                if (!m.Marker.gameObject.activeSelf) continue;
                UiKit.SetIcon(m.MarkerImage, marker);
                m.Marker.sizeDelta = Vector2.one * markerSize * tile;
                m.Marker.anchoredPosition = ToCanvas(cam, f.MarkerPoint, out _);
            }
            goneFoes.Clear();
            foreach (var f in foeMarks.Keys) if (f == null) goneFoes.Add(f);
            foreach (var f in goneFoes) { Destroy(foeMarks[f].Label.gameObject); Destroy(foeMarks[f].Marker.gameObject); foeMarks.Remove(f); }
        }

        FoeMarks BuildFoeMarks(RoadFoe f)
        {
            var m = new FoeMarks();
            m.Label = UiKit.Node("foe " + f.Id, foeLayer).Pin(Vector2.zero, Vector2.zero, new Vector2(10, 24));
            m.Label.pivot = new Vector2(.5f, 1);
            UiKit.Row(m.Label, 3, null, TextAnchor.MiddleCenter);
            UiKit.Hug(m.Label);
            m.Badge = UiKit.Icon(m.Label, f.Badge, 22);
            m.Text = UiKit.Label(m.Label, f.Label, UiKit.SmallSize, f.LabelColour, UiKit.Bold, TextAnchor.MiddleCenter);
            m.Text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var halo = m.Text.gameObject.AddComponent<Outline>(); // the site's cream stroke
            halo.effectColor = new Color(1, .98f, .94f, .92f);
            halo.effectDistance = new Vector2(1.5f, -1.5f);
            m.Shown = f.Label;
            m.MarkerImage = UiKit.Icon(foeLayer, null, 40);
            m.MarkerImage.preserveAspect = true;
            m.Marker = (RectTransform)m.MarkerImage.transform;
            m.Marker.Pin(Vector2.zero, Vector2.zero, new Vector2(40, 40)).pivot = new Vector2(.5f, .5f);
            return m;
        }

        // screen pixels across one tile at your pet, for things the site sizes in tiles
        float TilePixels(Camera cam)
        {
            var p = world.Me.Pos;
            return (cam.WorldToScreenPoint(TownMap.ToWorld(p.x + 1, p.y)) - cam.WorldToScreenPoint(TownMap.ToWorld(p.x, p.y))).magnitude;
        }

        void Place(PetActor a, Marks m, Camera cam)
        {
            var holder = (RectTransform)m.Tag.parent;
            Vector3 feet = cam.WorldToScreenPoint(a.FeetPoint), head = cam.WorldToScreenPoint(a.HeadPoint);
            bool visible = !a.Gone && !a.Hidden && feet.z > 0;
            UiKit.Show(holder, visible);
            if (!visible) return;

            float k = canvas.scaleFactor;
            m.Tag.anchoredPosition = (Vector2)feet / k;
            UiKit.Show(m.Tag, a.IsMe || GameSettings.ShowNames);
            Vector2 top = (Vector2)head / k;

            // chat bubble (clamped inside the screen like the site's)
            string bubble = GameSettings.ShowBubbles && a.Bubble != null && Time.time < a.BubbleUntil && !world.IsMuted(a) ? a.Bubble : null;
            UiKit.Show(m.Bubble, bubble != null);
            if (bubble != null)
            {
                if (bubble != m.ShownBubble) FillBubble(m, bubble);
                float half = m.Bubble.sizeDelta.x / 2, w = root.rect.width;
                m.Bubble.anchoredPosition = new Vector2(Mathf.Clamp(top.x, half + 7, w - half - 7), top.y + 14);
            }

            // emote floats up and fades over 1.8 s
            float age = Time.time - a.EmoteAt;
            bool emote = a.Emote != null && age < EmoteSeconds;
            UiKit.Show(m.Emote, emote);
            if (emote)
            {
                UiKit.SetIcon(m.EmoteImage, a.Emote);
                float t = age / EmoteSeconds;
                m.Emote.anchoredPosition = top + new Vector2(0, 10 + t * 40 + (bubble != null ? m.Bubble.sizeDelta.y + 12 : 0));
                m.EmoteImage.color = new Color(1, 1, 1, t > .75f ? (1 - t) * 4 : 1);
                m.Emote.localScale = Vector3.one * (t < .12f ? Mathf.Lerp(.5f, 1, t / .12f) : 1);
            }

            // sleepy z drifts up and fades on a loop
            bool zzz = a.Sleeping && !a.Walking;
            UiKit.Show(m.Zzz, zzz);
            if (zzz)
            {
                float q = Time.time / 1.4f % 1;
                m.Zzz.anchoredPosition = top + new Vector2(17 + q * 12, -7 + q * 26);
                m.Zzz.localScale = Vector3.one * (.8f + q * .5f);
                m.Zzz.GetComponent<Text>().color = new Color(.35f, .35f, .55f, 1 - q);
            }
        }

        Marks Build(PetActor a)
        {
            var holder = UiKit.Node(a.name, root).Fill();
            var m = new Marks();

            // name tag
            m.Tag = UiKit.Node("tag", holder).Pin(Vector2.zero, Vector2.zero, Vector2.zero);
            m.Tag.pivot = new Vector2(.5f, 1);
            if (a.IsBot)
            {
                UiKit.Row(m.Tag, 3, null, TextAnchor.MiddleCenter);
                UiKit.Icon(m.Tag, "🏡", 17);
                var t = UiKit.Label(m.Tag, a.Name, 16, VillagerInk, UiKit.Bold, TextAnchor.MiddleCenter);
                t.fontStyle = FontStyle.Italic;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var halo = t.gameObject.AddComponent<Outline>();
                halo.effectColor = new Color(1, .98f, .94f, .9f);
                halo.effectDistance = new Vector2(1.4f, -1.4f);
            }
            else
            {
                var pill = UiKit.Panel(m.Tag, "pill", a.IsMe ? Palette.Amber : Palette.Ink, 13);
                pill.raycastTarget = false;
                UiKit.Row((RectTransform)pill.transform, 6, new RectOffset(a.IsMe ? 12 : 10, 12, 4, 4), TextAnchor.MiddleCenter);
                if (!a.IsMe)
                {
                    var dot = UiKit.Panel(pill.transform, "dot", Palette.Amber, 4);
                    UiKit.Size(dot, 8, 8);
                }
                var t = UiKit.Label(pill.transform, a.IsMe ? "You" : a.Name, 16, a.IsMe ? Palette.Ink : Palette.Paper, UiKit.Bold, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Row(m.Tag, 0, null, TextAnchor.UpperCenter);
                UiKit.Shadow((RectTransform)pill.transform, 13, 5, 2, .3f);
            }
            UiKit.Hug(m.Tag);

            // chat bubble with a little tail
            var bubble = UiKit.Panel(holder, "bubble", a.IsMe ? Palette.BubbleMine : a.IsBot ? Palette.BubbleBot : Palette.BubbleOther, 13);
            bubble.raycastTarget = false;
            m.Bubble = (RectTransform)bubble.transform;
            m.Bubble.Pin(Vector2.zero, Vector2.zero, new Vector2(72, 36)).pivot = new Vector2(.5f, 0);
            UiKit.Shadow(m.Bubble, 13, 12, 4, .28f);
            var tail = UiKit.Panel(m.Bubble, "tail", bubble.color, 0);
            tail.raycastTarget = false;
            ((RectTransform)tail.transform).Pin(new Vector2(.5f, 0), Vector2.zero, new Vector2(12, 12)).localRotation = Quaternion.Euler(0, 0, 45);
            m.BubbleIcon = UiKit.Icon(m.Bubble, null, 22);
            ((RectTransform)m.BubbleIcon.transform).Pin(new Vector2(0, 1), new Vector2(11, -7), new Vector2(22, 22));
            m.BubbleText = UiKit.Label(m.Bubble, "", 17, Palette.Hex("#2b2118"), UiKit.Body, TextAnchor.UpperLeft);

            // emote and z
            m.EmoteImage = UiKit.Icon(holder, null, 36);
            m.Emote = (RectTransform)m.EmoteImage.transform;
            m.Emote.Pin(Vector2.zero, Vector2.zero, new Vector2(36, 36)).pivot = new Vector2(.5f, 0);
            var z = UiKit.Label(holder, "z", 22, Color.white, UiKit.Bold, TextAnchor.MiddleCenter);
            m.Zzz = (RectTransform)z.transform;
            m.Zzz.Pin(Vector2.zero, Vector2.zero, new Vector2(24, 24));
            var zHalo = z.gameObject.AddComponent<Outline>();
            zHalo.effectColor = new Color(1, 1, 1, .7f);

            UiKit.Show(m.Bubble, false); UiKit.Show(m.Emote, false); UiKit.Show(m.Zzz, false);
            return m;
        }

        /// <summary>Wraps the text to at most 4 lines and sizes the bubble around it.</summary>
        static void FillBubble(Marks m, string text)
        {
            m.ShownBubble = text;
            string words = UiKit.SplitEmoji(text, out string icon);
            UiKit.SetIcon(m.BubbleIcon, icon);
            float indent = icon != null ? 26 : 0;
            var t = m.BubbleText;
            t.text = words.Length > 0 ? words : " ";
            var r = (RectTransform)t.transform;
            float width = Mathf.Min(t.preferredWidth, BubbleMaxWidth);
            r.Pin(new Vector2(0, 1), new Vector2(11 + indent, -7), new Vector2(width, 24));
            float lineHeight = t.fontSize * 1.25f, height = Mathf.Min(t.preferredHeight, lineHeight * 4);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            r.sizeDelta = new Vector2(width, height + 2);
            m.Bubble.sizeDelta = new Vector2(width + 22 + indent, Mathf.Max(height, icon != null ? 22 : 0) + 16);
        }
    }
}
