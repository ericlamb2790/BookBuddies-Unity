using BookBuddies.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Settings, in five tabs: Game, Sound, Graphics, Controls and Account. Every change is saved and applied
    /// straight away. The ✕, tapping outside, Esc or B closes it (it registers with UiStack); LB and RB (or + and −)
    /// switch tabs.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        const float MaxWidth = 780, MaxHeight = 780;
        static readonly string[] TabNames = { "Game", "Sound", "Graphics", "Controls", "Account" };
        static readonly string[] FrameRateNames = { "30", "60", "120", "Unlimited" };
        static SettingsPanel open;

        public static bool IsOpen => open != null;

        RectTransform root, card, view, content;
        Button[] tabs;
        int tab;
        System.Action accountChanged;
        float soundPreviewAt;
        Vector2 lastSize;

        /// <summary>Opens Settings. "accountChanged" runs after signing out or deleting the account.</summary>
        public static void Open(System.Action accountChanged)
        {
            if (open) return;
            var canvas = UiKit.MakeCanvas("Settings", 70);
            open = canvas.gameObject.AddComponent<SettingsPanel>();
            open.root = (RectTransform)canvas.transform;
            open.accountChanged = accountChanged;
            open.Build();
            open.ShowTab(0);
            UiStack.Push(open, open.Close);
            VirtualCursor.FocusFirst(open.tabs[0]);
            Sound.Play("open");
        }

        void Build()
        {
            var scrim = UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.55f), null, true);
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(Close);

            var panel = UiKit.Panel(root, "card", Palette.Cream);
            card = panel.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(MaxWidth, MaxHeight));
            UiKit.Column(card, 14, new RectOffset(28, 28, 22, 30));
            UiKit.Outline(panel, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(card, UiKit.CardRadius, 30, 10, .35f);

            var head = UiKit.Node("head", card);
            UiKit.Row(head, 12);
            UiKit.Icon(head, "⚙️", 34);
            UiKit.Size(UiKit.Label(head, "Settings", UiKit.TitleSize, Palette.Ink, UiKit.Title), -1, 44, 1);
            UiKit.CloseButton(head, Close);

            tabs = UiKit.Tabs(card, TabNames, ShowTab);

            content = UiKit.ScrollColumn(card, 0, new RectOffset(4, 12, 4, 8), out _);
            view = (RectTransform)content.parent;
            UiKit.Size(view).flexibleHeight = 1;
            UiKit.PadHints(card, ("LB/RB", "Tabs"), ("A", "Select"), ("B", "Close"));
        }

        void ShowTab(int index)
        {
            tab = (index + TabNames.Length) % TabNames.Length;
            UiKit.SelectTab(tabs, tab);
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            content.anchoredPosition = Vector2.zero;
            switch (tab)
            {
                case 0: GameTab(); break;
                case 1: SoundTab(); break;
                case 2: GraphicsTab(); break;
                case 3: ControlsTab(); break;
                default: AccountTab(); break;
            }
        }

        // ---- tabs ----

        void GameTab()
        {
            Row("Names over pets", "See who’s who in town.", r => UiKit.Switch(r, GameSettings.ShowNames, v => GameSettings.ShowNames = v));
            Row("Chat bubbles", "Show what pets say above their heads.", r => UiKit.Switch(r, GameSettings.ShowBubbles, v => GameSettings.ShowBubbles = v));
            Row("Minimap", "The little map in the corner. M opens the big map.", r => UiKit.Switch(r, GameSettings.ShowMinimap, v => GameSettings.ShowMinimap = v));
            Row("Cinematics", "The camera sweeps in when you arrive in town.", r => UiKit.Switch(r, GameSettings.Cinematics, v => GameSettings.Cinematics = v));
            Row("Reduce motion", "A calmer camera and gentler menus.", r => UiKit.Switch(r, GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v));
            Row("Menu and text size", "Bigger is easier to read from the couch.", r => UiKit.Choice(r, GameSettings.UiSizeNames, GameSettings.UiSize, v => GameSettings.UiSize = v));
            Row("Reset settings", "Put every setting back the way it started.", r => UiKit.ConfirmButton(r, "Reset", "Tap again to reset", () =>
            {
                GameSettings.ResetAll();
                ShowTab(tab);
            }));
        }

        void SoundTab()
        {
            Row("Music", null, r => UiKit.Slider(r, GameSettings.MusicVolume, v => GameSettings.MusicVolume = v));
            Row("Sound effects", null, r => UiKit.Slider(r, GameSettings.SoundVolume, v =>
            {
                GameSettings.SoundVolume = v;
                if (Time.unscaledTime > soundPreviewAt) { soundPreviewAt = Time.unscaledTime + .18f; Sound.Play("pop"); }
            }));
        }

        void GraphicsTab()
        {
            var names = QualitySettings.names;
            if (names.Length > 1) Row("Quality", "Higher looks smoother; lower runs faster.", r => UiKit.Choice(r, names, QualitySettings.GetQualityLevel(), v => GameSettings.Quality = v));
            Row("Frame rate", "Frames each second.", r => UiKit.Choice(r, FrameRateNames, GameSettings.FrameRate, v => GameSettings.FrameRate = v));
            if (!Application.isMobilePlatform)
            {
                Row("VSync", "Matches your screen’s refresh to stop tearing.", r => UiKit.Switch(r, GameSettings.VSync, v => GameSettings.VSync = v));
                Row("Full screen", "F11 switches it too.", r => UiKit.Switch(r, GameSettings.Fullscreen, v => GameSettings.Fullscreen = v));
            }
            Row("Butterflies and petals", "Little bits of life drifting around town.", r => UiKit.Switch(r, GameSettings.AmbientLife, v => GameSettings.AmbientLife = v));
        }

        void ControlsTab()
        {
            var list = UiKit.Node("controls", content);
            UiKit.Column(list, 16, new RectOffset(0, 0, 10, 0));
            Menus.ControlsList(list);
        }

        void AccountTab()
        {
            bool signedIn = Settings.SignedIn;
            Note(signedIn ? $"Signed in as {(Buddy.Name.Length > 0 ? Buddy.Name : "a reader")} on {Settings.ServerHost}." : "Not signed in. Choose “I have a recovery code” on the title screen to sign in.");

            if (signedIn)
            {
                Text code = null;
                Row("Recovery code", "Brings your buddy back on another device. Keep it secret.", r =>
                {
                    var b = UiKit.Secondary(r, "Show", null);
                    b.onClick.AddListener(() => ShowRecovery(code, b));
                    return b;
                });
                code = Note("");
                UiKit.Show(code, false);
                if (Settings.IsAdmin)
                    Row("Admin tools", "Find players, mute, send home, give breaks.", r => UiKit.Secondary(r, "Open", () => AdminPanel.Open(), "🛡️"));
                Row("Sign out", "Your buddy stays safe on the server.", r => UiKit.ConfirmButton(r, "Sign out", "Tap again to sign out", () =>
                {
                    Settings.SignOut();
                    Buddy.Forget();
                    AccountChanged();
                }));
                Row("Delete account", "Removes your buddy and coins for good. A bookbuddies.pet account can only be deleted on the website.", r => UiKit.ConfirmButton(r, "Delete", "Tap again to delete forever", DeleteAccount));
            }

            Row("Server", "Where your buddy lives. Leave it empty for the game’s own server.", r =>
            {
                var field = UiKit.Input(r, Settings.DefaultServer, UiKit.SmallSize + 1);
                field.text = Settings.Server;
                UiKit.Size(field, 260, UiKit.ButtonHeight);
                field.onEndEdit.AddListener(v =>
                {
                    if (v.Trim() == Settings.Server) return;
                    Settings.Server = v;
                    field.text = Settings.Server;
                    Settings.SignOut();
                    Buddy.Forget();
                    AccountChanged();
                });
                return field;
            });
        }

        async void DeleteAccount()
        {
            try { await BBApi.DeleteAccount(); }
            catch (System.Exception e) { if (this) Note(e.Message).color = UiKit.RoseInk; return; }
            Buddy.Forget();
            if (this) AccountChanged();
        }

        async void ShowRecovery(Text code, Button button)
        {
            button.interactable = false;
            string text;
            try
            {
                var reply = await BBApi.Recovery();
                text = reply.Str("code");
                GUIUtility.systemCopyBuffer = text;
                text += "   (copied)";
            }
            catch (BBApi.ApiError e) when (e.Status == 404)
            {
                text = "This server keeps recovery codes on the website. Find yours on your profile there.";
            }
            catch (System.Exception e)
            {
                text = e.Message;
            }
            if (!this || !code) return;
            code.text = text;
            code.fontSize = UiKit.BodySize;
            code.color = Palette.Ink;
            UiKit.Show(code, true);
        }

        void AccountChanged()
        {
            Close();
            accountChanged?.Invoke();
        }

        // ---- rows ----

        /// <summary>A setting: its name and a short hint on the left, its control on the right, an ink rule underneath.</summary>
        void Row(string label, string hint, System.Func<Transform, Component> control)
        {
            var row = UiKit.Node(label, content);
            UiKit.Row(row, 16, new RectOffset(0, 0, 10, 10));
            UiKit.Size(row, -1, 72);
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, label, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            if (!string.IsNullOrEmpty(hint)) UiKit.Label(words, hint, UiKit.SmallSize, Palette.InkSoft);
            var right = UiKit.Node("control", row);
            UiKit.Row(right, 0, null, TextAnchor.MiddleRight);
            control(right);
            UiKit.Rule(content);
        }

        Text Note(string text)
        {
            var t = UiKit.Label(content, text, UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.Size(t, -1, 40);
            return t;
        }

        // ---- every frame ----

        void Update()
        {
            if (root.rect.size != lastSize) Fit();
            if (PlazaInput.Typing) return;
            if (PlazaInput.Down(PlazaAction.ZoomIn)) ShowTab(tab + 1);
            else if (PlazaInput.Down(PlazaAction.ZoomOut)) ShowTab(tab - 1);

            // keyboard and d-pad focus: start on the tabs, and keep the focused control in view
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == null && es != null && PlazaInput.ArrowPressed() && !VirtualCursor.Active) es.SetSelectedGameObject(tabs[tab].gameObject);
            else if (selected != null && !PlazaInput.UsingPointer && !VirtualCursor.Active && selected.transform.IsChildOf(content))
                KeepInView((RectTransform)selected.transform);
        }

        void Fit()
        {
            lastSize = root.rect.size;
            card.sizeDelta = new Vector2(Mathf.Min(MaxWidth, lastSize.x - 32), Mathf.Min(MaxHeight, lastSize.y - 32));
        }

        void KeepInView(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float top = view.InverseTransformPoint(corners[1]).y, bottom = view.InverseTransformPoint(corners[0]).y;
            var p = content.anchoredPosition;
            if (top > view.rect.yMax) p.y -= top - view.rect.yMax + 8;
            else if (bottom < view.rect.yMin) p.y += view.rect.yMin - bottom + 8;
            content.anchoredPosition = p;
        }

        public void Close()
        {
            if (!this) return;
            Sound.Play("close");
            GameSettings.Save();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            UiStack.Remove(this);
            open = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            UiStack.Remove(this);
            if (open == this) open = null;
        }
    }
}
