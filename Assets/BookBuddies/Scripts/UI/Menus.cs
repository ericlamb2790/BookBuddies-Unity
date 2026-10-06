using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// A card that opens over the screen: an optional title with a close button, content that scrolls when the
    /// screen is short, and "Ⓐ Select Ⓑ Back" hints while a gamepad is in use. Tapping outside it, the ✕, Esc
    /// or B closes it (it registers with UiStack). Opened from keys or a gamepad, its first control gets focus.
    /// </summary>
    public sealed class Sheet : MonoBehaviour
    {
        const float Margin = 20;     // kept clear at the screen's edges
        const float OpenSeconds = .18f;

        /// <summary>Where content goes: a column inside the card that scrolls when it can't all fit.</summary>
        public RectTransform Card { get; private set; }
        /// <summary>The visible card around the content.</summary>
        public RectTransform Frame { get; private set; }
        public Selectable First;
        public bool IsOpen => gameObject.activeSelf;
        public System.Action Opened, Closed;

        float width, openedAt;
        CanvasGroup fade;
        Vector2 home;
        Image scrim;
        RectTransform view;
        LayoutElement viewSize;
        ScrollRect scroll;

        /// <summary>A closed sheet pinned to an anchor of the parent (0,0 bottom-left … 1,1 top-right). Give a title for a header row.</summary>
        public static Sheet Create(Transform parent, string name, Vector2 anchor, Vector2 offset, float width, string title = null)
        {
            var root = UiKit.Node(name, parent).Fill();
            var s = root.gameObject.AddComponent<Sheet>();
            s.width = width;
            s.home = offset;

            s.scrim = UiKit.Node("tap outside to close", root).Fill().gameObject.AddComponent<Image>();
            s.scrim.color = Color.clear;
            var close = s.scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(s.Close);

            var card = UiKit.Panel(root, "card", Palette.Cream);
            s.Frame = card.rectTransform.Pin(anchor, offset, new Vector2(width, 10));
            UiKit.Column(s.Frame, 12, new RectOffset(22, 22, 18, 24));
            UiKit.Hug(s.Frame, false, true);
            UiKit.Outline(card, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(s.Frame, UiKit.CardRadius, 22, 8, .32f);
            s.fade = card.gameObject.AddComponent<CanvasGroup>();
            if (title != null) UiKit.Header(s.Frame, title, s.Close);
            s.Card = UiKit.ScrollColumn(s.Frame, 12, new RectOffset(0, 0, 2, 2), out s.scroll);
            s.view = (RectTransform)s.Card.parent;
            s.viewSize = UiKit.Size(s.view);
            UiKit.PadHints(s.Frame, ("A", "Select"), ("B", "Back"));
            root.gameObject.SetActive(false);
            return s;
        }

        /// <summary>Darkens the screen behind the card (for menus in the middle of the screen).</summary>
        public void Dim(float alpha) => scrim.color = Palette.Ink.WithAlpha(alpha);

        public void Open(bool focusFirst = true)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            UiStack.Push(this, Close);
            openedAt = Time.unscaledTime;
            fade.alpha = 0;
            Card.anchoredPosition = Vector2.zero;
            Sound.Play("open");
            Opened?.Invoke();
            if (focusFirst) VirtualCursor.FocusFirst(First);
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            UiStack.Remove(this);
            Sound.Play("close");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        public void Toggle(bool focusFirst = true)
        {
            if (IsOpen) Close(); else Open(focusFirst);
        }

        void OnDestroy() => UiStack.Remove(this);

        void LateUpdate()
        {
            Fit();
            float t = (Time.unscaledTime - openedAt) / OpenSeconds;
            if (t > 1 && fade.alpha >= 1) return;
            float ease = UiKit.EaseOut(t);
            fade.alpha = ease;
            Frame.anchoredPosition = home + new Vector2(0, GameSettings.ReduceMotion ? 0 : (1 - ease) * -10);
        }

        // keep the card inside the screen: narrower on small screens, scrolling when it's too tall
        void Fit()
        {
            var screen = ((RectTransform)transform).rect.size;
            float w = Mathf.Min(width, screen.x - Margin * 2);
            if (!Mathf.Approximately(Frame.sizeDelta.x, w)) Frame.sizeDelta = new Vector2(w, Frame.sizeDelta.y);
            float chrome = Frame.rect.height - view.rect.height;
            float room = screen.y - Margin * 2 - Mathf.Abs(home.y) - chrome;
            float h = Mathf.Max(60, Mathf.Min(LayoutUtility.GetPreferredHeight(Card), room));
            if (Mathf.Abs(viewSize.preferredHeight - h) > .5f) viewSize.preferredHeight = viewSize.minHeight = h;
            scroll.vertical = LayoutUtility.GetPreferredHeight(Card) > h + 1;
        }
    }

    /// <summary>The cards the HUD opens: emotes, tricks, a pet's card, controls and the town menu.</summary>
    public static class Menus
    {
        const float AboveBar = 92; // HUD sheets sit just above the chat bar

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
            g.spacing = new Vector2(10, 10);
            g.childAlignment = TextAnchor.UpperCenter;
            return g;
        }

        public static Sheet Emotes(Transform parent, PlazaWorld world)
        {
            var s = Sheet.Create(parent, "Emotes", new Vector2(1, 0), new Vector2(-16, AboveBar), 352, "Emotes");
            var grid = Grid(s.Card, 4, new Vector2(70, 70));
            for (int i = 0; i < PlazaWorld.Emotes.Length; i++)
            {
                string e = PlazaWorld.Emotes[i];
                var b = UiKit.Tile(grid.transform, e, null, () => { world.Emote(e); s.Close(); });
                if (!Application.isMobilePlatform)
                {
                    var key = UiKit.Label(b.transform, (i + 1).ToString(), UiKit.SmallSize, Palette.InkSoft, UiKit.Bold, TextAnchor.UpperRight);
                    UiKit.Size(key).ignoreLayout = true;
                    ((RectTransform)key.transform).Fill(5);
                }
                if (i == 0) s.First = b;
            }
            return s;
        }

        public static Sheet Tricks(Transform parent, PlazaWorld world)
        {
            var s = Sheet.Create(parent, "Tricks", new Vector2(1, 0), new Vector2(-16, AboveBar), 352, "Tricks");
            var grid = Grid(s.Card, 3, new Vector2(94, 84));
            foreach (string kind in PlazaWorld.Tricks)
            {
                var look = TrickLook.TryGetValue(kind, out var l) ? l : ("✨", kind);
                var b = UiKit.Tile(grid.transform, look.Item1, look.Item2, () => { world.Trick(kind); s.Close(); });
                if (s.First == null) s.First = b;
            }
            return s;
        }

        /// <summary>The card for a pet you tapped: say hi, play together, mute their chat, and (for admins) moderation.</summary>
        public static Sheet PetCard(Transform parent, PlazaWorld world, PetActor pet)
        {
            var s = Sheet.Create(parent, "Pet card", new Vector2(.5f, 0), new Vector2(0, AboveBar), 460);

            var head = UiKit.Node("head", s.Card);
            UiKit.Row(head, 14);
            var portrait = UiKit.Panel(head, "portrait", Palette.Paper, 14);
            UiKit.Size(portrait, 76, 76);
            var art = UiKit.Node("pet", portrait.transform).Fill(4).gameObject.AddComponent<Image>();
            art.sprite = PetSprites.For(pet.Look);
            art.preserveAspect = true;
            art.raycastTarget = false;
            var names = UiKit.Node("names", head);
            UiKit.Column(names, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(names, -1, -1, 1);
            UiKit.Label(names, pet.Name, UiKit.HeadingSize, Palette.Ink, UiKit.Bold);
            var sub = UiKit.Label(names, Subtitle(world, pet), UiKit.SmallSize, Palette.InkSoft);
            UiKit.CloseButton(head, s.Close);

            var grid = Grid(s.Card, 4, new Vector2(90, 80));
            foreach (string kind in PlazaWorld.Interactions)
            {
                var look = InteractLook[kind];
                var b = UiKit.Tile(grid.transform, look.icon, look.label, () => { world.Interact(pet, kind); s.Close(); });
                if (s.First == null) s.First = b;
            }

            if (!pet.IsBot)
            {
                var row = UiKit.Node("buttons", s.Card);
                UiKit.Row(row, 8, null, TextAnchor.MiddleLeft);
                Text muteLabel = null;
                var mute = UiKit.Secondary(row, world.IsMuted(pet) ? "Show their chat" : "Hide their chat", () =>
                {
                    world.ToggleMute(pet);
                    muteLabel.text = world.IsMuted(pet) ? "Show their chat" : "Hide their chat";
                    sub.text = Subtitle(world, pet);
                }, "🤐", 44);
                muteLabel = mute.GetComponentInChildren<Text>();
                if (Settings.IsAdmin) Moderation(s.Card, pet);
            }
            s.Closed = () => Object.Destroy(s.gameObject);
            return s;
        }

        static string Subtitle(PlazaWorld world, PetActor pet) =>
            pet.IsBot ? "Villager" : world.IsMuted(pet) ? "Reader · chat hidden" : "Reader in town";

        // quick admin actions for the player on this card; "More…" opens the full admin tools
        static void Moderation(Transform card, PetActor pet)
        {
            var box = UiKit.Panel(card, "moderation", Palette.Paper, 12);
            UiKit.Column(box.rectTransform, 8, new RectOffset(14, 14, 12, 14));
            UiKit.Outline(box, Palette.Ink.WithAlpha(.12f), 12, 1);
            var title = UiKit.Node("title", box.transform);
            UiKit.Row(title, 8);
            UiKit.Icon(title, "🛡️", 22);
            UiKit.Label(title, "Moderation", UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold);
            var status = UiKit.Label(box.transform, "Only you can see this.", UiKit.SmallSize, Palette.InkSoft);
            void Report(string text) { if (status) status.text = text; }
            void Act(string action, Dictionary<string, object> body) { Report("Working…"); AdminPanel.Quick(pet.Name, pet.User, action, body, Report); }

            var chat = Pair(box.transform);
            Even(UiKit.Secondary(chat, "Mute 15 min", () => Act("mute", new Dictionary<string, object> { ["minutes"] = 15 }), null, 44));
            Even(UiKit.Secondary(chat, "Mute 1 day", () => Act("mute", new Dictionary<string, object> { ["minutes"] = 1440 }), null, 44));
            var town = Pair(box.transform);
            Even(UiKit.ConfirmButton(town, "Send home", "Send home?", () => Act("kick", null), 44));
            Even(UiKit.ConfirmButton(town, "Break 1 hour", "Sure? Tap again", () => Act("ban", new Dictionary<string, object> { ["hours"] = 1 }), 44));
            UiKit.Secondary(box.transform, "More admin tools…", () => AdminPanel.Open(pet.Name, pet.User), "🛡️", 44);
        }

        static RectTransform Pair(Transform parent)
        {
            var row = UiKit.Node("row", parent);
            UiKit.Row(row, 8);
            return row;
        }

        static void Even(Button b) => UiKit.Size(b, -1, -1, 1);

        /// <summary>How to play with a mouse, touch screen, keyboard or gamepad, in three columns when there's room.</summary>
        public static Sheet Controls(Transform parent)
        {
            var s = Sheet.Create(parent, "Controls", new Vector2(.5f, .5f), Vector2.zero, 1120, "Controls");
            s.Dim(.35f);
            s.Opened = () =>
            {
                for (int i = s.Card.childCount - 1; i >= 0; i--) Object.Destroy(s.Card.GetChild(i).gameObject);
                ControlsList(s.Card, ((RectTransform)s.transform).rect.width >= 1100);
                s.First = UiKit.Primary(s.Card, "Got it", s.Close);
            };
            return s;
        }

        /// <summary>The three lists of controls (also shown in Settings → Controls), side by side or one under another.</summary>
        public static void ControlsList(Transform parent, bool sideBySide = false)
        {
            Transform into = parent;
            if (sideBySide)
            {
                var row = UiKit.Node("columns", parent);
                UiKit.Row(row, 28).childAlignment = TextAnchor.UpperLeft;
                into = row;
            }
            Section(into, "Mouse and touch", new[]
            {
                ("Walk", "tap the ground"), ("Say hi or play", "tap a pet"), ("Sit", "tap a bench or chair"), ("Zoom", "scroll or pinch"),
                ("Map", "tap the little map"),
            });
            Section(into, "Keyboard", new[]
            {
                ("Walk", "WASD or arrows"), ("Use or sit", "E"), ("Hop", "Space"), ("Emotes", "Q, or 1 to 8"), ("Tricks", "F"),
                ("Bag", "I"), ("Hero", "C"), ("Chat", "Enter or T"), ("Zoom", "+ and −"), ("Map", "M"), ("Hide the HUD", "H"),
                ("Photo", "P or F12"), ("Back or menu", "Esc"),
            });
            Section(into, "Gamepad", new[]
            {
                ("Walk", "left stick or d-pad"), ("Use or sit", "pad:A"), ("Emotes", "pad:X"), ("Tricks", "pad:Y"), ("Chat", "pad:Select"),
                ("Zoom", "pad:LB/RB"), ("Map", "pad:RS"), ("Menu", "pad:Start"), ("Back", "pad:B"),
                ("In menus", "left stick moves the cursor"), ("Click", "pad:A"), ("Scroll", "right stick"),
            });
        }

        static void Section(Transform parent, string title, (string what, string how)[] rows)
        {
            var section = UiKit.Node(title, parent);
            UiKit.Column(section, 6);
            UiKit.Size(section, -1, -1, 1);
            UiKit.Label(section, title, UiKit.BodySize, UiKit.EmberInk, UiKit.Bold);
            foreach (var (what, how) in rows)
            {
                var row = UiKit.Node(what, section);
                UiKit.Row(row, 10);
                UiKit.Size(row, -1, 30);
                UiKit.Size(UiKit.Label(row, what, UiKit.SmallSize + 1, Palette.Ink), 150);
                if (!how.StartsWith("pad:")) { UiKit.Size(UiKit.Label(row, how, UiKit.SmallSize + 1, Palette.InkSoft), -1, -1, 1); continue; }
                foreach (var b in how.Substring(4).Split('/')) UiKit.PadGlyph(row, b, 26);
                UiKit.Spacer(row);
            }
        }

        /// <summary>The menu in town (Esc, Start or the Menu button): bag and hero, your pets, settings, photo mode, back to the title, quit.</summary>
        public static Sheet Pause(Transform parent, PlazaWorld world, System.Action photo, System.Action toTitle)
        {
            var s = Sheet.Create(parent, "Menu", new Vector2(.5f, .5f), Vector2.zero, 460);
            s.Dim(.35f);
            var head = UiKit.Node("head", s.Card);
            UiKit.Row(head, 12);
            var words = UiKit.Node("words", head);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, world.Map.Name, UiKit.TitleSize + 4, Palette.Ink, UiKit.Title);
            var where = UiKit.Label(words, "", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.CloseButton(head, s.Close);
            s.Opened = () => where.text = world.Area ?? world.Map.Name;

            var tiles = UiKit.Node("bag and hero", s.Card);
            UiKit.Row(tiles, 10).childForceExpandWidth = true;
            BigTile(tiles, "🎒", "Bag", "Gear and finds", () => { s.Close(); TalesUi.OpenBag(); });
            BigTile(tiles, "🦊", "Hero", "Stats and moves", () => { s.Close(); TalesUi.OpenHero(); });
            BigTile(s.Card, "🐾", "Pets", "Switch, hatch or reroll", () => { s.Close(); PetsScreen.Open(world.Me); });

            s.First = UiKit.Primary(s.Card, "Back to town", s.Close, null, 52);
            UiKit.Secondary(s.Card, "Settings", () => { s.Close(); SettingsPanel.Open(toTitle); }, "⚙️");
            UiKit.Secondary(s.Card, "Photo mode", () => { s.Close(); photo(); });
            if (Settings.IsAdmin) UiKit.Secondary(s.Card, "Admin tools", () => { s.Close(); AdminPanel.Open(); }, "🛡️");
            UiKit.Secondary(s.Card, "Back to the title screen", () => { s.Close(); toTitle(); });
            if (!Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer)
                UiKit.Secondary(s.Card, "Quit game", Application.Quit);
            return s;
        }

        // a big two-line button with an emoji: the bag, hero and pets in the menu
        static void BigTile(Transform parent, string emoji, string title, string sub, System.Action onClick)
        {
            var b = UiKit.Button(parent, title, Palette.Paper, onClick, 14);
            UiKit.Outline(b, Palette.Ink.WithAlpha(.12f), 14, 1);
            UiKit.Size(b, -1, 76, 1);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 10, new RectOffset(14, 12, 0, 0));
            UiKit.Icon(r, emoji, 40);
            var words = UiKit.Node("words", r);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Label(words, title, UiKit.BodySize + 1, Palette.Ink, UiKit.Bold);
            UiKit.Label(words, sub, UiKit.SmallSize, Palette.InkSoft);
        }
    }
}
