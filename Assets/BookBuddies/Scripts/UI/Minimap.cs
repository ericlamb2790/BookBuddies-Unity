using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The little round map in the corner, and the big town map (M, the gamepad's right stick, or a tap).
    /// It's an illustrated map made from the town's own art: the painted ground, then the trees and buildings
    /// in miniature. Dots show every pet: you in amber, other readers in orange, villagers in blue.
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        const float SmallSize = 168;
        const float SmallScale = 3.6f;   // screen units per tile in the corner map
        const float MinArt = 1.6f;       // skip props smaller than this many square tiles (fences, lamps...)

        PlazaWorld world;
        TownMap map;
        RectTransform frame, art, dotLayer, labels;
        Image window, border, scrim;
        readonly Dictionary<PetActor, Image> dots = new Dictionary<PetActor, Image>();
        readonly List<PetActor> gone = new List<PetActor>();
        bool big;
        float scale;
        Vector2 laidOutFor;

        public bool IsBig => big;

        public static Minimap Create(RectTransform parent, PlazaWorld world)
        {
            var root = UiKit.Node("map", parent).Fill();
            var m = root.gameObject.AddComponent<Minimap>();
            m.world = world;
            m.map = world.Map;
            m.Build(root);
            return m;
        }

        void Build(RectTransform root)
        {
            scrim = UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.5f), null, true);
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Toggle);

            border = UiKit.Panel(root, "border", Palette.Cream, (int)(SmallSize / 2) + 5);
            frame = UiKit.Node("frame", border.transform);
            window = frame.gameObject.AddComponent<Image>();
            window.color = Palette.Sea;
            frame.gameObject.AddComponent<Mask>();
            frame.gameObject.AddComponent<Canvas>(); // the map redraws on its own, apart from the HUD
            frame.gameObject.AddComponent<GraphicRaycaster>();
            var tap = frame.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.navigation = new Navigation { mode = Navigation.Mode.None };
            tap.onClick.AddListener(Toggle);
            UiKit.Shadow((RectTransform)border.transform, (int)(SmallSize / 2) + 5, 12, 4, .25f);

            art = UiKit.Node("town", frame);
            art.anchorMin = art.anchorMax = new Vector2(.5f, .5f);
            art.pivot = new Vector2(0, 1);
            PaintGround();
            PaintProps();
            dotLayer = UiKit.Node("pets", frame).Fill();
            labels = UiKit.Node("areas", frame).Fill();
            foreach (var a in map.Areas) AreaLabel(a);
            Layout();
        }

        void PaintGround()
        {
            int n = Art.GroundChunk;
            for (int cy = 0; cy * n < map.Height; cy++)
                for (int cx = 0; cx * n < map.Width; cx++)
                {
                    var sprite = Art.GroundSprite(new Vector2Int(cx, cy));
                    if (!sprite) continue;
                    var r = UiKit.Node("ground", art);
                    r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
                    r.anchoredPosition = new Vector2(cx * n, -cy * n);
                    r.sizeDelta = new Vector2(n, n);
                    var img = r.gameObject.AddComponent<RawImage>();
                    img.texture = sprite.texture;
                    var t = sprite.textureRect;
                    img.uvRect = new Rect(t.x / sprite.texture.width, t.y / sprite.texture.height, t.width / sprite.texture.width, t.height / sprite.texture.height);
                    img.raycastTarget = false;
                }
        }

        // trees and buildings in miniature, back to front
        void PaintProps()
        {
            var placed = new List<TownMap.Placed>(map.Objects);
            placed.Sort((a, b) => (a.Y + a.H).CompareTo(b.Y + b.H));
            foreach (var o in placed)
            {
                bool animated = Art.Anims.TryGetValue(o.Sprite, out var entry);
                if (!animated && !Art.Objects.TryGetValue(o.Sprite, out entry)) continue;
                if (entry.Size.x * entry.Size.y < MinArt) continue;
                var sprite = Art.Sprite(entry, animated ? 0 : -1);
                if (!sprite) continue;
                var r = UiKit.Node(o.Kind, art);
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = entry.Pivot;
                r.anchoredPosition = new Vector2(o.X + o.W / 2f, -(o.Y + o.H));
                r.sizeDelta = entry.Size;
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
            }
        }

        void AreaLabel(TownMap.Region a)
        {
            var t = UiKit.Label(labels, a.Name, 13, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Cream;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var r = (RectTransform)t.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.sizeDelta = new Vector2(160, 20);
            r.name = a.Name;
        }

        /// <summary>Corner map ⇄ big town map.</summary>
        public void Toggle()
        {
            big = !big;
            Sound.Play(big ? "open" : "close");
            Layout();
        }

        void Layout()
        {
            var parent = (RectTransform)transform;
            var b = (RectTransform)border.transform;
            laidOutFor = parent.rect.size;
            if (big)
            {
                scale = Mathf.Min((parent.rect.width - 60) / map.Width, (parent.rect.height - 90) / map.Height);
                var size = new Vector2(map.Width, map.Height) * scale;
                b.Pin(new Vector2(.5f, .5f), new Vector2(0, -10), size + Vector2.one * 10);
                border.sprite = UiKit.Rounded(18);
                window.sprite = UiKit.Rounded(14);
            }
            else
            {
                scale = SmallScale;
                b.Pin(new Vector2(1, 1), new Vector2(-12, -62), Vector2.one * (SmallSize + 10));
                border.sprite = UiKit.Rounded((int)(SmallSize / 2) + 5);
                window.sprite = UiKit.Rounded((int)(SmallSize / 2));
            }
            window.type = border.type = Image.Type.Sliced;
            ((RectTransform)frame.transform).Fill(5);
            art.localScale = Vector3.one * scale;
            labels.gameObject.SetActive(big);
            UiKit.Show(scrim, big);
        }

        void LateUpdate()
        {
            if (world == null || world.Me == null) return;
            bool show = big || GameSettings.ShowMinimap;
            UiKit.Show(border, show);
            if (!show) return;
            var screen = ((RectTransform)transform).rect.size;
            if (big && screen != laidOutFor) Layout(); // follows window resizes

            art.anchoredPosition = Point(Vector2.zero);
            foreach (var a in world.Actors)
            {
                if (a == null) continue;
                if (!dots.TryGetValue(a, out var dot)) dots[a] = dot = Dot(a);
                ((RectTransform)dot.transform).anchoredPosition = Point(a.Pos);
                UiKit.Show(dot, !a.Gone && !a.Hidden);
            }
            gone.Clear();
            foreach (var a in dots.Keys) if (a == null) gone.Add(a);
            foreach (var a in gone) { Destroy(dots[a].gameObject); dots.Remove(a); }

            if (dots.TryGetValue(world.Me, out var mine))
            {
                mine.transform.SetAsLastSibling();
                mine.transform.localScale = Vector3.one * (1 + Mathf.Sin(Time.unscaledTime * 4) * .12f);
            }
            if (big)
                foreach (RectTransform label in labels)
                {
                    var area = map.Areas.Find(r => r.Name == label.name);
                    if (area != null) label.anchoredPosition = Point(area.Area.center + Vector2.one * .5f);
                }
        }

        /// <summary>Where a map point sits in the window: centred on your pet in the corner, on the whole town when big.</summary>
        Vector2 Point(Vector2 mapPoint)
        {
            Vector2 focus = big ? new Vector2(map.Width, map.Height) / 2 : world.Me.Pos;
            return new Vector2(mapPoint.x - focus.x, focus.y - mapPoint.y) * scale;
        }

        Image Dot(PetActor a)
        {
            float size = a.IsMe ? 12 : 8;
            var dot = UiKit.Panel(dotLayer, a.name, a.IsMe ? Palette.Amber : a.IsBot ? Palette.Sky : Palette.Ember, (int)(size / 2));
            dot.raycastTarget = false;
            var r = dot.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.sizeDelta = Vector2.one * size;
            var ring = dot.gameObject.AddComponent<Outline>();
            ring.effectColor = a.IsMe ? Palette.Ink : Color.white;
            ring.effectDistance = new Vector2(1.5f, -1.5f);
            return dot;
        }
    }
}
