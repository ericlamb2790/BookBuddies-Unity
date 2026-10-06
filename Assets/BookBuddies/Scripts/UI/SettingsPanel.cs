using BookBuddies.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Settings, in five tabs: Game, Sound, Graphics, Controls and Account. Every change is saved and applied
    /// straight away. Esc or B closes it; LB and RB (or + and −) switch tabs.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        static readonly string[] TabNames = { "Game", "Sound", "Graphics", "Controls", "Account" };
        static readonly string[] UiSizeNames = { "Small", "Normal", "Large", "Extra large" };
        static readonly string[] FrameRateNames = { "30", "60", "120", "Unlimited" };
        static SettingsPanel open;

        public static bool IsOpen => open != null;

        Canvas canvas;
        RectTransform card, view, content;
        Button[] tabs;
        int tab;
        System.Action accountChanged;
        float soundPreviewAt;
        bool confirming;

        /// <summary>Opens Settings. "accountChanged" runs after signing out or deleting the account.</summary>
        public static void Open(System.Action accountChanged)
        {
            if (open) return;
            var canvas = UiKit.MakeCanvas("Settings", 70);
            open = canvas.gameObject.AddComponent<SettingsPanel>();
            open.canvas = canvas;
            open.accountChanged = accountChanged;
            open.Build((RectTransform)canvas.transform);
            open.ShowTab(0);
            Sound.Play("open");
        }

        void Build(RectTransform root)
        {
            var scrim = UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.55f), null, true);
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(Close);

            card = UiKit.Panel(root, "card", Palette.Cream, 18).rectTransform;
            card.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(620, 600));
            UiKit.Column(card, 12, new RectOffset(26, 26, 22, 22));
            UiKit.Shadow(card, 18, 28, 10, .35f);

            var head = UiKit.Node("head", card);
            UiKit.Row(head, 8);
            UiKit.Size(UiKit.Label(head, "Settings", 30, Palette.Ink, UiKit.Title), -1, 40, 1);
            UiKit.TextButton(head, "Close", null, Palette.Paper, Palette.Ink, Close, 38);

            var row = UiKit.Node("tabs", card);
            UiKit.Row(row, 6);
            tabs = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                tabs[i] = UiKit.TextButton(row, TabNames[i], null, Palette.Paper, Palette.Ink, () => ShowTab(index), 38);
            }

            view = UiKit.Node("view", card);
            UiKit.Size(view).flexibleHeight = 1;
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            content = UiKit.Node("content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = Vector2.zero;
            UiKit.Column(content, 14, new RectOffset(2, 8, 6, 12));
            UiKit.Hug(content, false, true);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
        }

        void ShowTab(int index)
        {
            tab = (index + TabNames.Length) % TabNames.Length;
            confirming = false;
            for (int i = 0; i < tabs.Length; i++) ((Image)tabs[i].targetGraphic).color = i == tab ? Palette.Amber : Palette.Paper;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            content.anchoredPosition = Vector2.zero;
            switch (tab)
            {
                case 0: GameTab(); break;
                case 1: SoundTab(); break;
                case 2: GraphicsTab(); break;
                case 3: Menus.ControlsList(content); break;
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
            Row("Reduce motion", "A calmer camera with fewer big moves.", r => UiKit.Switch(r, GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v));
            Row("Menu and text size", null, r => UiKit.Choice(r, UiSizeNames, GameSettings.UiSize, v => GameSettings.UiSize = v));
            Row("Reset settings", "Put every setting back the way it started.", r => UiKit.TextButton(r, "Reset", null, Palette.Paper, Palette.Ink, () => { GameSettings.ResetAll(); ShowTab(tab); }, 38));
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
            Row("Frame rate", null, r => UiKit.Choice(r, FrameRateNames, GameSettings.FrameRate, v => GameSettings.FrameRate = v));
            if (!Application.isMobilePlatform)
            {
                Row("VSync", "Matches your screen’s refresh to stop tearing.", r => UiKit.Switch(r, GameSettings.VSync, v => GameSettings.VSync = v));
                Row("Full screen", null, r => UiKit.Switch(r, GameSettings.Fullscreen, v => GameSettings.Fullscreen = v));
            }
            Row("Butterflies and petals", "Little bits of life drifting around town.", r => UiKit.Switch(r, GameSettings.AmbientLife, v => GameSettings.AmbientLife = v));
        }

        void AccountTab()
        {
            bool signedIn = Settings.SignedIn;
            Note(signedIn ? $"Signed in as {(Buddy.Name.Length > 0 ? Buddy.Name : "a reader")} on {Settings.ServerHost}." : "Not signed in. Use “I have a recovery code” on the title screen to sign in.");

            if (signedIn)
            {
                Text code = null;
                Row("Recovery code", "Brings your buddy back on another device. Keep it secret.", r =>
                {
                    var b = UiKit.TextButton(r, "Show", null, Palette.Paper, Palette.Ink, null, 38);
                    b.onClick.AddListener(() => ShowRecovery(code, b));
                    return b;
                });
                code = Note("");
                UiKit.Show(code, false);
                Row("Sign out", "Your buddy stays safe on the server.", r => Confirm(r, "Sign out", "Tap again to sign out", () =>
                {
                    Settings.SignOut();
                    Buddy.Forget();
                    AccountChanged();
                }));
                Row("Delete account", "Removes your buddy and coins for good.", r => Confirm(r, "Delete", "Tap again to delete forever", async () =>
                {
                    try { await BBApi.DeleteAccount(); }
                    catch (System.Exception e) { Note(e.Message); return; }
                    Buddy.Forget();
                    AccountChanged();
                }));
            }

            Row("Server", "Where your buddy lives. Leave it empty for the game’s own server.", r =>
            {
                var field = UiKit.Input(r, Settings.DefaultServer, 14);
                field.text = Settings.Server;
                UiKit.Size(field, 230, 38);
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

        async void ShowRecovery(Text code, Button button)
        {
            button.interactable = false;
            try
            {
                var reply = await BBApi.Recovery();
                code.text = reply.Str("code");
                GUIUtility.systemCopyBuffer = code.text;
                code.text += "   (copied)";
            }
            catch (BBApi.ApiError e) when (e.Status == 404)
            {
                code.text = "This server keeps recovery codes on the website. Find yours on your profile there.";
            }
            catch (System.Exception e)
            {
                code.text = e.Message;
            }
            if (!this) return;
            code.fontSize = 18;
            UiKit.Show(code, true);
        }

        /// <summary>A button that asks once more before doing something you can't undo.</summary>
        Button Confirm(Transform parent, string label, string sure, System.Action act)
        {
            Text text = null;
            var b = UiKit.TextButton(parent, label, null, Palette.Paper, Palette.Ink, null, 38);
            text = b.GetComponentInChildren<Text>();
            b.onClick.AddListener(() =>
            {
                if (text.text == sure) { act(); return; }
                text.text = sure;
                ((Image)b.targetGraphic).color = Palette.Rose.WithAlpha(.35f);
            });
            return b;
        }

        void AccountChanged()
        {
            Close();
            accountChanged?.Invoke();
        }

        // ---- rows ----

        /// <summary>A setting: name and a short hint on the left, its control on the right.</summary>
        void Row(string label, string hint, System.Func<Transform, Component> control)
        {
            var row = UiKit.Node(label, content);
            UiKit.Row(row, 14);
            var words = UiKit.Node("words", row);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, label, 17, Palette.Ink, UiKit.Bold);
            if (!string.IsNullOrEmpty(hint)) UiKit.Label(words, hint, 13, Palette.InkSoft);
            var right = UiKit.Node("control", row);
            UiKit.Row(right, 0, null, TextAnchor.MiddleRight);
            control(right);
        }

        Text Note(string text) => UiKit.Label(content, text, 15, Palette.InkSoft);

        // ---- every frame ----

        void Update()
        {
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            float room = ((RectTransform)canvas.transform).rect.height - 40;
            card.sizeDelta = new Vector2(Mathf.Min(620, ((RectTransform)canvas.transform).rect.width - 24), Mathf.Min(600, room));

            bool typing = false;
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected != null && selected.TryGetComponent<InputField>(out var field)) typing = field.isFocused;
            PlazaInput.Typing = typing;

            if (PlazaInput.Down(PlazaAction.Back) && !typing) { Close(); return; }
            if (typing) return;
            if (PlazaInput.Down(PlazaAction.ZoomIn)) ShowTab(tab + 1);
            else if (PlazaInput.Down(PlazaAction.ZoomOut)) ShowTab(tab - 1);
            if (selected == null && es != null && PlazaInput.Move() != Vector2.zero) es.SetSelectedGameObject(tabs[tab].gameObject);
            if (selected != null && selected.transform.IsChildOf(content)) KeepInView((RectTransform)selected.transform);
        }

        // with a gamepad, scroll so the highlighted control is always visible
        void KeepInView(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float top = view.InverseTransformPoint(corners[1]).y, bottom = view.InverseTransformPoint(corners[0]).y;
            float viewTop = view.rect.yMax, viewBottom = view.rect.yMin;
            var p = content.anchoredPosition;
            if (top > viewTop) p.y -= top - viewTop + 8;
            else if (bottom < viewBottom) p.y += viewBottom - bottom + 8;
            content.anchoredPosition = p;
        }

        public void Close()
        {
            if (!this) return;
            Sound.Play("close");
            GameSettings.Save();
            PlazaInput.Typing = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            open = null;
            Destroy(gameObject);
        }
    }
}
