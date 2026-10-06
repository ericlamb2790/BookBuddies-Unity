using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Name tags, chat bubbles, floating emotes and sleepy z's over each pet, drawn like the website:
    /// your tag is amber and says "You", other readers get an ink pill, villagers get plain slate text.
    /// </summary>
    public sealed class Overlays : MonoBehaviour
    {
        const float BubbleMaxWidth = 190;
        const float EmoteSeconds = 1.8f;
        static readonly Color VillagerInk = Palette.Hex("#55657a");

        sealed class Marks
        {
            public RectTransform Tag, Bubble, Emote, Zzz;
            public Text BubbleText;
            public Image BubbleIcon, EmoteImage;
            public string ShownBubble;
        }

        PlazaWorld world;
        RectTransform root;
        Canvas canvas;
        readonly Dictionary<PetActor, Marks> marks = new Dictionary<PetActor, Marks>();
        readonly List<PetActor> gone = new List<PetActor>();

        public static Overlays Create(PlazaWorld world)
        {
            var canvas = UiKit.MakeCanvas("Pet labels", 10);
            var o = canvas.gameObject.AddComponent<Overlays>();
            o.world = world;
            o.canvas = canvas;
            o.root = (RectTransform)canvas.transform;
            canvas.GetComponent<GraphicRaycaster>().enabled = false; // labels never block taps
            return o;
        }

        void LateUpdate()
        {
            if (world == null || world.Me == null) return;
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
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
                m.Bubble.anchoredPosition = new Vector2(Mathf.Clamp(top.x, half + 6, w - half - 6), top.y + 12);
            }

            // emote floats up and fades over 1.8 s
            float age = Time.time - a.EmoteAt;
            bool emote = a.Emote != null && age < EmoteSeconds;
            UiKit.Show(m.Emote, emote);
            if (emote)
            {
                UiKit.SetIcon(m.EmoteImage, a.Emote);
                float t = age / EmoteSeconds;
                m.Emote.anchoredPosition = top + new Vector2(0, 8 + t * 34 + (bubble != null ? m.Bubble.sizeDelta.y + 10 : 0));
                m.EmoteImage.color = new Color(1, 1, 1, t > .75f ? (1 - t) * 4 : 1);
                m.Emote.localScale = Vector3.one * (t < .12f ? Mathf.Lerp(.5f, 1, t / .12f) : 1);
            }

            // sleepy z drifts up and fades on a loop
            bool zzz = a.Sleeping && !a.Walking;
            UiKit.Show(m.Zzz, zzz);
            if (zzz)
            {
                float q = Time.time / 1.4f % 1;
                m.Zzz.anchoredPosition = top + new Vector2(14 + q * 10, -6 + q * 22);
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
                UiKit.Icon(m.Tag, "🏡", 14);
                var t = UiKit.Label(m.Tag, a.Name, 13, VillagerInk, UiKit.Bold, TextAnchor.MiddleCenter);
                t.fontStyle = FontStyle.Italic;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var halo = t.gameObject.AddComponent<Outline>();
                halo.effectColor = new Color(1, .98f, .94f, .9f);
                halo.effectDistance = new Vector2(1.2f, -1.2f);
            }
            else
            {
                var pill = UiKit.Panel(m.Tag, "pill", a.IsMe ? Palette.Amber : Palette.Ink, 11);
                pill.raycastTarget = false;
                UiKit.Row((RectTransform)pill.transform, 5, new RectOffset(a.IsMe ? 10 : 8, 10, 3, 3), TextAnchor.MiddleCenter);
                if (!a.IsMe)
                {
                    var dot = UiKit.Panel(pill.transform, "dot", Palette.Amber, 4);
                    UiKit.Size(dot, 7, 7);
                }
                var t = UiKit.Label(pill.transform, a.IsMe ? "You" : a.Name, 13, a.IsMe ? Palette.Ink : Palette.Paper, UiKit.Bold, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Row(m.Tag, 0, null, TextAnchor.UpperCenter);
                UiKit.Shadow((RectTransform)pill.transform, 11, 4, 1.5f, .3f);
            }
            UiKit.Hug(m.Tag);

            // chat bubble with a little tail
            var bubble = UiKit.Panel(holder, "bubble", a.IsMe ? Palette.BubbleMine : a.IsBot ? Palette.BubbleBot : Palette.BubbleOther, 11);
            bubble.raycastTarget = false;
            m.Bubble = (RectTransform)bubble.transform;
            m.Bubble.Pin(Vector2.zero, Vector2.zero, new Vector2(60, 30)).pivot = new Vector2(.5f, 0);
            UiKit.Shadow(m.Bubble, 11, 10, 3, .28f);
            var tail = UiKit.Panel(m.Bubble, "tail", bubble.color, 0);
            tail.raycastTarget = false;
            ((RectTransform)tail.transform).Pin(new Vector2(.5f, 0), Vector2.zero, new Vector2(10, 10)).localRotation = Quaternion.Euler(0, 0, 45);
            m.BubbleIcon = UiKit.Icon(m.Bubble, null, 18);
            ((RectTransform)m.BubbleIcon.transform).Pin(new Vector2(0, 1), new Vector2(9, -6), new Vector2(18, 18));
            m.BubbleText = UiKit.Label(m.Bubble, "", 14, Palette.Hex("#2b2118"), UiKit.Body, TextAnchor.UpperLeft);

            // emote and z
            m.EmoteImage = UiKit.Icon(holder, null, 30);
            m.Emote = (RectTransform)m.EmoteImage.transform;
            m.Emote.Pin(Vector2.zero, Vector2.zero, new Vector2(30, 30)).pivot = new Vector2(.5f, 0);
            var z = UiKit.Label(holder, "z", 18, Color.white, UiKit.Bold, TextAnchor.MiddleCenter);
            m.Zzz = (RectTransform)z.transform;
            m.Zzz.Pin(Vector2.zero, Vector2.zero, new Vector2(20, 20));
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
            float indent = icon != null ? 22 : 0;
            var t = m.BubbleText;
            t.text = words.Length > 0 ? words : " ";
            var r = (RectTransform)t.transform;
            float width = Mathf.Min(t.preferredWidth, BubbleMaxWidth);
            r.Pin(new Vector2(0, 1), new Vector2(9 + indent, -6), new Vector2(width, 20));
            float lineHeight = t.fontSize * 1.25f, height = Mathf.Min(t.preferredHeight, lineHeight * 4);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            r.sizeDelta = new Vector2(width, height + 2);
            m.Bubble.sizeDelta = new Vector2(width + 18 + indent, Mathf.Max(height, icon != null ? 18 : 0) + 13);
        }
    }
}
