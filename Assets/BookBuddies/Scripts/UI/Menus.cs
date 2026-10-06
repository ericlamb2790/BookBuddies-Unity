using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// A small card that opens over the town. Tapping outside it, Esc or the gamepad's B button closes it.
    /// Opened from a key or gamepad, its first button is highlighted so the d-pad and A button work right away.
    /// </summary>
    public sealed class Sheet : MonoBehaviour
    {
        public RectTransform Card { get; private set; }
        public Selectable First;
        public bool IsOpen => gameObject.activeSelf;
        public System.Action Opened, Closed;

        float width, openedAt;
        CanvasGroup fade;
        Vector2 home;

        public static Sheet Create(Transform parent, string name, Vector2 anchor, Vector2 offset, float width)
        {
            var root = UiKit.Node(name, parent).Fill();
            var s = root.gameObject.AddComponent<Sheet>();
            s.width = width;

            var scrim = UiKit.Node("tap outside to close", root).Fill();
            scrim.gameObject.AddComponent<Image>().color = Color.clear;
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(s.Close);

            var card = UiKit.Panel(root, "card", Palette.Cream);
            s.Card = card.rectTransform.Pin(anchor, offset, new Vector2(width, 10));
            s.home = offset;
            UiKit.Column(s.Card, 10, new RectOffset(14, 14, 14, 14));
            UiKit.Hug(s.Card, false, true);
            UiKit.Shadow(s.Card, UiKit.CardRadius, 18, 6, .3f);
            s.fade = s.Card.gameObject.AddComponent<CanvasGroup>();
            root.gameObject.SetActive(false);
            return s;
        }

        public void Open(bool highlightFirst)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            float room = ((RectTransform)transform).rect.width - 24;
            Card.sizeDelta = new Vector2(Mathf.Min(width, room), Card.sizeDelta.y);
            openedAt = Time.unscaledTime;
            fade.alpha = 0;
            Sound.Play("open");
            Opened?.Invoke();
            if (highlightFirst && First != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(First.gameObject);
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            Sound.Play("close");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        public void Toggle(bool highlightFirst)
        {
            if (IsOpen) Close(); else Open(highlightFirst);
        }

        // A quick rise and fade in, easing out.
        void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / .16f), ease = 1 - (1 - t) * (1 - t) * (1 - t);
            fade.alpha = ease;
            Card.anchoredPosition = home + new Vector2(0, (1 - ease) * -8);
        }
    }

    /// <summary>The cards the HUD opens: emotes, tricks, a pet's card, controls and the town menu.</summary>
    public static class Menus
    {
        static readonly Dictionary<string, (string icon, string label)> TrickLook = new Dictionary<string, (string, string)>
        {
            ["hop"] = ("🐰", "Hop"), ["spin"] = ("🌀", "Spin"), ["wave"] = ("👋", "Wave"), ["dance"] = ("💃", "Dance"), ["nap"] = ("💤", "Nap"),
        };

        static readonly Dictionary<string, (string icon, string label)> InteractLook = new Dictionary<string, (string, string)>
        {
            ["hug"] = ("🤗", "Hug"), ["boop"] = ("👃", "Boop"), ["play"] = ("🎾", "Fetch"), ["snack"] = ("🍪", "Snack"),
            ["highpaw"] = ("🐾", "High paw"), ["dance"] = ("💃", "Dance"), ["wave"] = ("👋", "Wave"),
        };

        static GridLayoutGroup Grid(Transform parent, int columns, Vector2 cell)
        {
            var r = UiKit.Node("grid", parent);
            var g = r.gameObject.AddComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.cellSize = cell;
            g.spacing = new Vector2(8, 8);
            g.childAlignment = TextAnchor.UpperCenter;
            return g;
        }

        public static Sheet Emotes(Transform parent, PlazaWorld world)
        {
            var s = Sheet.Create(parent, "Emotes", new Vector2(1, 0), new Vector2(-12, 68), 264);
            var grid = Grid(s.Card, 4, new Vector2(52, 52));
            for (int i = 0; i < PlazaWorld.Emotes.Length; i++)
            {
                string e = PlazaWorld.Emotes[i];
                var b = UiKit.Tile(grid.transform, e, null, () => { world.Emote(e); s.Close(); });
                if (!Application.isMobilePlatform)
                {
                    var key = UiKit.Label(b.transform, (i + 1).ToString(), 11, Palette.InkSoft, UiKit.Bold, TextAnchor.UpperRight);
                    UiKit.Size(key).ignoreLayout = true;
                    ((RectTransform)key.transform).Fill(4);
                }
                if (i == 0) s.First = b;
            }
            return s;
        }

        public static Sheet Tricks(Transform parent, PlazaWorld world)
        {
            var s = Sheet.Create(parent, "Tricks", new Vector2(1, 0), new Vector2(-12, 68), 264);
            var grid = Grid(s.Card, 3, new Vector2(72, 64));
            foreach (string kind in PlazaWorld.Tricks)
            {
                var look = TrickLook.TryGetValue(kind, out var l) ? l : ("✨", kind);
                var b = UiKit.Tile(grid.transform, look.Item1, look.Item2, () => { world.Trick(kind); s.Close(); });
                if (s.First == null) s.First = b;
            }
            return s;
        }

        /// <summary>The card for a pet you tapped: say hi, play together, or mute their chat.</summary>
        public static Sheet PetCard(Transform parent, PlazaWorld world, PetActor pet)
        {
            var s = Sheet.Create(parent, "Pet card", new Vector2(.5f, 0), new Vector2(0, 68), 340);

            var head = UiKit.Node("head", s.Card);
            UiKit.Row(head, 12);
            var portrait = UiKit.Panel(head, "portrait", Palette.Paper, 12);
            UiKit.Size(portrait, 64, 64);
            var art = UiKit.Node("pet", portrait.transform).Fill(4).gameObject.AddComponent<Image>();
            art.sprite = PetSprites.For(pet.Look);
            art.preserveAspect = true;
            art.raycastTarget = false;
            var names = UiKit.Node("names", head);
            UiKit.Column(names, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(names, -1, -1, 1);
            UiKit.Label(names, pet.Name, 20, Palette.Ink, UiKit.Bold);
            var sub = UiKit.Label(names, Subtitle(world, pet), 14, Palette.InkSoft);

            var grid = Grid(s.Card, 4, new Vector2(70, 62));
            foreach (string kind in PlazaWorld.Interactions)
            {
                var look = InteractLook[kind];
                var b = UiKit.Tile(grid.transform, look.icon, look.label, () => { world.Interact(pet, kind); s.Close(); });
                if (s.First == null) s.First = b;
            }

            var buttons = UiKit.Node("buttons", s.Card);
            UiKit.Row(buttons, 8, null, TextAnchor.MiddleRight);
            if (!pet.IsBot)
            {
                Text muteLabel = null;
                var mute = UiKit.TextButton(buttons, world.IsMuted(pet) ? "Unmute" : "Mute chat", null, Palette.Paper, Palette.Ink, () =>
                {
                    world.ToggleMute(pet);
                    muteLabel.text = world.IsMuted(pet) ? "Unmute" : "Mute chat";
                    sub.text = Subtitle(world, pet);
                }, 36);
                muteLabel = mute.GetComponentInChildren<Text>();
            }
            UiKit.TextButton(buttons, "Close", null, Palette.Paper, Palette.Ink, s.Close, 36);
            s.Closed = () => Object.Destroy(s.gameObject);
            return s;
        }

        static string Subtitle(PlazaWorld world, PetActor pet) =>
            pet.IsBot ? "Villager" : world.IsMuted(pet) ? "Reader · chat muted" : "Reader in town";

        /// <summary>How to play with a mouse, touch screen, keyboard or gamepad.</summary>
        public static Sheet Controls(Transform parent)
        {
            var s = Sheet.Create(parent, "Controls", new Vector2(.5f, .5f), Vector2.zero, 460);
            s.Card.pivot = new Vector2(.5f, .5f);
            UiKit.Label(s.Card, "Controls", 24, Palette.Ink, UiKit.Title);
            ControlsList(s.Card);
            s.First = UiKit.TextButton(s.Card, "Got it", null, Palette.Amber, Palette.Ink, s.Close, 40);
            return s;
        }

        /// <summary>The three lists of controls (also shown in Settings → Controls).</summary>
        public static void ControlsList(Transform parent)
        {
            Section(parent, "Mouse and touch", new[]
            {
                ("Walk", "tap the ground"), ("Say hi or play", "tap a pet"), ("Sit", "tap a bench or chair"), ("Zoom", "scroll or pinch"),
                ("Map", "tap the little map"),
            });
            Section(parent, "Keyboard", new[]
            {
                ("Walk", "WASD or arrows"), ("Use or sit", "E"), ("Hop", "Space"), ("Emotes", "Q, or 1 to 8"), ("Tricks", "F"),
                ("Chat", "Enter or T"), ("Zoom", "+ and −"), ("Map", "M"), ("Hide the HUD", "H"), ("Photo", "P or F12"), ("Menu or close", "Esc"),
            });
            Section(parent, "Gamepad", new[]
            {
                ("Walk", "left stick or d-pad"), ("Use or sit", "A"), ("Emotes", "X"), ("Tricks", "Y"), ("Chat", "Select"),
                ("Zoom", "LB and RB"), ("Map", "right stick click"), ("Menu", "Start"), ("Close", "B"),
            });
        }

        static void Section(Transform card, string title, (string what, string how)[] rows)
        {
            UiKit.Label(card, title, 15, Palette.Ember, UiKit.Bold);
            var list = UiKit.Node(title, card);
            UiKit.Column(list, 2);
            foreach (var (what, how) in rows)
            {
                var row = UiKit.Node(what, list);
                UiKit.Row(row, 8);
                UiKit.Size(UiKit.Label(row, what, 14, Palette.Ink), 130);
                UiKit.Size(UiKit.Label(row, how, 14, Palette.InkSoft), -1, -1, 1);
            }
        }

        /// <summary>The menu in town (Esc, Start or the Menu button): settings, photo mode, back to the title, quit.</summary>
        public static Sheet Pause(Transform parent, PlazaWorld world, System.Action photo, System.Action toTitle)
        {
            var s = Sheet.Create(parent, "Menu", new Vector2(.5f, .5f), Vector2.zero, 320);
            s.Card.pivot = new Vector2(.5f, .5f);
            var column = s.Card.GetComponent<VerticalLayoutGroup>();
            column.spacing = 10;
            column.padding = new RectOffset(20, 20, 20, 20);
            UiKit.Label(s.Card, world.Map.Name, 28, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var where = UiKit.Label(s.Card, "", 14, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            s.Opened = () => where.text = world.Area ?? world.Map.Name;
            s.First = UiKit.TextButton(s.Card, "Back to town", null, Palette.Amber, Palette.Ink, s.Close, 48);
            UiKit.TextButton(s.Card, "Settings", null, Palette.Paper, Palette.Ink, () => { s.Close(); SettingsPanel.Open(toTitle); }, 44);
            UiKit.TextButton(s.Card, "Photo mode", null, Palette.Paper, Palette.Ink, () => { s.Close(); photo(); }, 44);
            UiKit.TextButton(s.Card, "Back to the title screen", null, Palette.Paper, Palette.Ink, () => { s.Close(); toTitle(); }, 44);
            if (!Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer)
                UiKit.TextButton(s.Card, "Quit game", null, Palette.Paper, Palette.Ink, Application.Quit, 44);
            return s;
        }
    }
}
