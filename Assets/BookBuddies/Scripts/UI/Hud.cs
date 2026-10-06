using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Everything on screen over the town: where you are and who's around, area banners, toasts, the minimap,
    /// the chat log and bar, and the emote, trick, pet and town menus. Photo mode (H hides it all, P takes a
    /// picture) lives here too, and so do the keyboard and gamepad shortcuts.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class Hud : MonoBehaviour
    {
        const int ChatLines = 6;
        const float ChatFadeAfter = 12f;
        const float CompactWidth = 640f;

        PlazaWorld world;
        System.Action toTitle;
        Canvas canvas;
        RectTransform root, layer; // layer: everything except the menus
        CanvasGroup layerGroup;
        Overlays labels;

        Image liveDot;
        HorizontalLayoutGroup statusRow;
        Text placeText, detailText;
        Button playHere;
        CanvasGroup banner; Text bannerTitle, bannerSub; float bannerAt = -99;
        RectTransform toast; Text toastText; Image toastIcon; float toastUntil;
        RectTransform hint; Image hintIcon; Text hintName, hintSub;
        CanvasGroup log; RectTransform logLines; float lastLineAt = -99;
        InputField chat; float chatOpenedAt;
        Text emotesLabel, tricksLabel;
        Sheet emotes, tricks, controls, pause, petCard;
        Minimap minimap;
        CanvasGroup photoHint;
        bool hidden;
        float lastWidth, shownAt, photoHintAt = -99;

        /// <summary>Builds the HUD over the town. "toTitle" goes back to the title screen (from the menu, or after signing out).</summary>
        public static Hud Create(PlazaWorld world, System.Action toTitle)
        {
            var canvas = UiKit.MakeCanvas("HUD", 20);
            var hud = canvas.gameObject.AddComponent<Hud>();
            hud.world = world;
            hud.toTitle = toTitle;
            hud.canvas = canvas;
            hud.root = (RectTransform)canvas.transform;
            hud.Build();
            hud.labels = Overlays.Create(world);
            world.Toast += hud.ShowToast;
            world.Banner += hud.ShowBanner;
            world.ChatLine += hud.AddChatLine;
            world.PetTapped += hud.OpenPet;
            world.Changed += hud.Refresh;
            world.Net.StateChanged += _ => hud.Refresh();
            hud.Refresh();
            hud.shownAt = Time.unscaledTime;
            return hud;
        }

        void OnDestroy()
        {
            PlazaInput.Typing = PlazaInput.MenuOpen = false;
            if (labels) Destroy(labels.gameObject);
        }

        // ---- building ----

        void Build()
        {
            layer = UiKit.Node("hud", root).Fill();
            layerGroup = layer.gameObject.AddComponent<CanvasGroup>();
            layerGroup.alpha = 0; // floats in (see Animate)
            BuildStatus();
            BuildCorner();
            BuildBanner();
            BuildNotices();
            BuildChat();
            minimap = Minimap.Create(layer, world);
            emotes = Menus.Emotes(root, world);
            tricks = Menus.Tricks(root, world);
            controls = Menus.Controls(root);
            pause = Menus.Pause(root, world, () => SetHidden(true), () => toTitle());
            BuildPhotoHint();
        }

        // shown for a moment when the HUD hides
        void BuildPhotoHint()
        {
            var pill = UiKit.Panel(root, "photo hint", Palette.Ink.WithAlpha(.75f), 18);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(10, 36));
            UiKit.Row(r, 0, new RectOffset(18, 18, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Hug(r, true, false);
            string keys = Application.isMobilePlatform ? "Tap the screen with two fingers to show the HUD" : "Photo mode · P takes a picture · H shows the HUD";
            UiKit.Label(r, keys, 15, Palette.Cream, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            photoHint = pill.gameObject.AddComponent<CanvasGroup>();
            photoHint.alpha = 0;
        }

        // Top left: a live dot, the town and area, and how many readers are here.
        void BuildStatus()
        {
            var pill = UiKit.Panel(layer, "status", Palette.Cream, 20);
            var r = pill.rectTransform.Pin(new Vector2(0, 1), new Vector2(12, -12), new Vector2(10, 40));
            statusRow = UiKit.Row(r, 8, new RectOffset(14, 16, 0, 0));
            UiKit.Hug(r, true, false);
            UiKit.Shadow(r, 20, 10, 3, .22f);
            liveDot = UiKit.Panel(r, "live", Palette.InkSoft, 5);
            UiKit.Size(liveDot, 10, 10);
            placeText = UiKit.Label(r, "", 16, Palette.Ink, UiKit.Bold);
            placeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            detailText = UiKit.Label(r, "", 14, Palette.InkSoft);
            detailText.horizontalOverflow = HorizontalWrapMode.Overflow;
            playHere = UiKit.TextButton(r, "Play here", null, Palette.Amber, Palette.Ink, () => world.Net.Start(), 30);
        }

        // Top right: controls help and the town menu.
        void BuildCorner()
        {
            var r = UiKit.Node("corner", layer).Pin(new Vector2(1, 1), new Vector2(-12, -12), new Vector2(10, 40));
            UiKit.Row(r, 8, null, TextAnchor.MiddleRight);
            UiKit.Hug(r, true, false);
            var help = UiKit.Button(r, "Controls", Palette.Cream, () => controls.Toggle(false), 20);
            UiKit.Size(help, 40, 40);
            ((RectTransform)UiKit.Label(help.transform, "?", 20, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).transform).Fill();
            UiKit.Shadow((RectTransform)help.transform, 20, 10, 3, .22f);
            var menu = UiKit.TextButton(r, "Menu", null, Palette.Cream, Palette.Ink, () => Toggle(pause, false), 40);
            UiKit.Shadow((RectTransform)menu.transform, 20, 10, 3, .22f);
        }

        // Centre top: the area you just walked into, in the site's display face.
        void BuildBanner()
        {
            var card = UiKit.Panel(layer, "banner", Palette.Cream);
            card.raycastTarget = false;
            var r = card.rectTransform.Pin(new Vector2(.5f, 1), new Vector2(0, -64), new Vector2(10, 10));
            UiKit.Column(r, 0, new RectOffset(22, 22, 10, 12), TextAnchor.MiddleCenter);
            UiKit.Hug(r);
            UiKit.Shadow(r, UiKit.CardRadius, 16, 5, .26f);
            bannerTitle = UiKit.Label(r, "", 26, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            bannerTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            bannerSub = UiKit.Label(r, "", 14, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            bannerSub.horizontalOverflow = HorizontalWrapMode.Overflow;
            banner = card.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0;
            banner.blocksRaycasts = false;
        }

        // Above the chat bar: a toast, and a card for the place you're standing at.
        void BuildNotices()
        {
            var stack = UiKit.Node("notices", layer).Pin(new Vector2(.5f, 0), new Vector2(0, 68), new Vector2(10, 10));
            UiKit.Column(stack, 8, null, TextAnchor.LowerCenter).childForceExpandWidth = false;
            UiKit.Hug(stack);

            var t = UiKit.Panel(stack, "toast", Palette.Ink, 20);
            t.raycastTarget = false;
            toast = t.rectTransform;
            UiKit.Row(toast, 8, new RectOffset(14, 16, 8, 8), TextAnchor.MiddleCenter);
            toastIcon = UiKit.Icon(toast, null, 20);
            toastText = UiKit.Label(toast, "", 15, Palette.Paper, UiKit.Bold);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Shadow(toast, 20, 12, 4, .3f);
            UiKit.Show(toast, false);

            var h = UiKit.Panel(stack, "place", Palette.Cream);
            h.raycastTarget = false;
            hint = h.rectTransform;
            UiKit.Row(hint, 12, new RectOffset(12, 18, 10, 10));
            hintIcon = UiKit.Icon(hint, null, 36);
            var words = UiKit.Node("words", hint);
            UiKit.Column(words, 1, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, 230);
            hintName = UiKit.Label(words, "", 17, Palette.Ink, UiKit.Bold);
            hintSub = UiKit.Label(words, "", 14, Palette.InkSoft);
            UiKit.Label(words, "Opens in a later build", 13, Palette.Ember);
            UiKit.Shadow(hint, UiKit.CardRadius, 14, 4, .24f);
            UiKit.Show(hint, false);
        }

        // Bottom: recent chat, the chat box, and the emote and trick buttons.
        void BuildChat()
        {
            var bg = UiKit.Panel(layer, "chat log", Palette.Cream.WithAlpha(.86f));
            bg.raycastTarget = false;
            logLines = bg.rectTransform.Pin(Vector2.zero, new Vector2(12, 68), new Vector2(340, 10));
            UiKit.Column(logLines, 3, new RectOffset(12, 12, 8, 9));
            UiKit.Hug(logLines, false, true);
            log = bg.gameObject.AddComponent<CanvasGroup>();
            log.alpha = 0;
            log.blocksRaycasts = false;

            var bar = UiKit.Node("chat bar", layer);
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(.5f, 0);
            bar.offsetMin = new Vector2(12, 12); bar.offsetMax = new Vector2(-12, 56);
            UiKit.Row(bar, 8).childForceExpandHeight = true;

            chat = UiKit.Input(bar, "Say something…");
            chat.characterLimit = 140;
            chat.onEndEdit.AddListener(OnChatEnd);
            UiKit.Size(chat, 120, -1, 1);
            UiKit.Shadow((RectTransform)chat.transform, 12, 10, 3, .2f);

            var send = UiKit.TextButton(bar, "Send", null, Palette.Amber, Palette.Ink, SendChat, 44);
            UiKit.Shadow((RectTransform)send.transform, 22, 10, 3, .2f);
            emotesLabel = BarButton(bar, "❤️", "Emotes", () => Toggle(emotes, false));
            tricksLabel = BarButton(bar, "🐾", "Tricks", () => Toggle(tricks, false));
        }

        Text BarButton(Transform bar, string emoji, string label, System.Action onClick)
        {
            var b = UiKit.TextButton(bar, label, emoji, Palette.Cream, Palette.Ink, onClick, 44);
            UiKit.Shadow((RectTransform)b.transform, 22, 10, 3, .2f);
            return b.GetComponentInChildren<Text>();
        }

        // ---- every frame ----

        void Update()
        {
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            if (!Mathf.Approximately(root.rect.width, lastWidth)) Fit(root.rect.width);

            if (SettingsPanel.IsOpen) { PlazaInput.MenuOpen = true; Animate(); return; } // Settings handles its own keys
            PlazaInput.Typing = chat.isFocused;
            bool sheetOpen = AnySheetOpen();
            PlazaInput.MenuOpen = sheetOpen || minimap.IsBig;
            layerGroup.interactable = !sheetOpen && !hidden; // keeps the d-pad inside an open menu
            HandleKeys();
            if (!PlazaInput.MenuOpen && !chat.isFocused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null); // so A or Enter doesn't press a button you clicked earlier

            Animate();
        }

        void HandleKeys()
        {
            bool highlight = !PlazaInput.UsingPointer;
            if (PlazaInput.Down(PlazaAction.Back))
            {
                if (chat.isFocused) CloseChat();
                else if (minimap.IsBig) minimap.Toggle();
                else if (AnySheetOpen()) CloseTopMenu();
                else if (hidden) SetHidden(false);
                else if (!PlazaInput.Locked) Toggle(pause, highlight);
                return;
            }
            if (PlazaInput.Typing) return;
            if (PlazaInput.Down(PlazaAction.Menu)) { if (minimap.IsBig) minimap.Toggle(); Toggle(pause, highlight); return; }
            if (PlazaInput.Down(PlazaAction.Photo)) { StartCoroutine(TakePhoto()); return; }
            if (PlazaInput.Down(PlazaAction.HideHud) || (hidden && PlazaInput.TwoFingerTap())) { SetHidden(!hidden); return; }
            if (PlazaInput.Down(PlazaAction.Map) && !AnyMenuOpen()) { minimap.Toggle(); return; }
            if (PlazaInput.Down(PlazaAction.Emotes)) Toggle(emotes, highlight);
            else if (PlazaInput.Down(PlazaAction.Tricks)) Toggle(tricks, highlight);
            else if (PlazaInput.MenuOpen) return;
            else if (PlazaInput.Down(PlazaAction.Chat)) OpenChat();
            else if (PlazaInput.Down(PlazaAction.Use)) world.UseNearby();
        }

        void Animate()
        {
            // the HUD floats in after arriving, and slips away in photo mode
            float shown = hidden ? 0 : UiKit.Ease((Time.unscaledTime - shownAt) / .6f);
            layerGroup.alpha = Mathf.MoveTowards(layerGroup.alpha, shown, Time.unscaledDeltaTime * 4);
            layerGroup.blocksRaycasts = !hidden;
            if (labels) labels.gameObject.SetActive(!hidden);
            float hintAge = Time.unscaledTime - photoHintAt;
            photoHint.alpha = hidden && hintAge < 3.5f ? Mathf.Clamp01(Mathf.Min(hintAge / .3f, (3.5f - hintAge) / .5f)) : 0;

            // banner: rise in quickly, hold, fade away
            float age = Time.unscaledTime - bannerAt;
            float show = age < .35f ? 1 - Mathf.Pow(1 - age / .35f, 3) : age < 3f ? 1 : Mathf.Clamp01(1 - (age - 3f) / .6f);
            banner.alpha = show;
            ((RectTransform)banner.transform).anchoredPosition = new Vector2(0, -64 + (1 - show) * 10);

            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) UiKit.Show(toast, false);

            float idle = Time.unscaledTime - lastLineAt;
            float target = chat.isFocused ? 1 : idle < ChatFadeAfter ? 1 : 0;
            log.alpha = Mathf.MoveTowards(log.alpha, logLines.childCount > 0 ? target : 0, Time.unscaledDeltaTime * 3);
        }

        /// <summary>Phones get icon-only buttons, and the chat log moves up under the status pill.</summary>
        void Fit(float width)
        {
            lastWidth = width;
            bool compact = width < CompactWidth;
            foreach (var label in new[] { emotesLabel, tricksLabel })
            {
                UiKit.Show(label, !compact);
                label.transform.parent.GetComponent<HorizontalLayoutGroup>().padding.right = compact ? 10 : 16;
            }
            var size = new Vector2(Mathf.Min(340, width - 24), logLines.sizeDelta.y);
            if (compact) logLines.Pin(new Vector2(0, 1), new Vector2(12, -62), size);
            else logLines.Pin(Vector2.zero, new Vector2(12, 68), size);
        }

        // ---- menus ----

        IEnumerable<Sheet> Sheets()
        {
            yield return petCard; yield return emotes; yield return tricks; yield return controls; yield return pause;
        }

        bool AnyMenuOpen() => AnySheetOpen() || (minimap != null && minimap.IsBig);

        bool AnySheetOpen()
        {
            foreach (var s in Sheets()) if (s != null && s.IsOpen) return true;
            return false;
        }

        // ---- photo mode ----

        void SetHidden(bool hide)
        {
            hidden = hide;
            photoHintAt = Time.unscaledTime;
            foreach (var s in Sheets()) if (s != null) s.Close();
            Sound.Play(hide ? "close" : "open");
        }

        /// <summary>Saves a picture of the town without the HUD to the Photos folder, with a flash and a click.</summary>
        System.Collections.IEnumerator TakePhoto()
        {
            bool wasHidden = hidden;
            hidden = true;
            layerGroup.alpha = 0;
            photoHint.alpha = 0;
            if (labels) labels.gameObject.SetActive(false);
            yield return null;
            string folder = System.IO.Path.Combine(Application.persistentDataPath, "Photos");
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, "Pawtopia " + System.DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            yield return null;
            yield return null;
            Sound.Play("shutter");
            var flash = UiKit.Cover(root, "flash", Color.white);
            for (float t = 0; t < .35f; t += Time.unscaledDeltaTime) { flash.color = new Color(1, 1, 1, 1 - t / .35f); yield return null; }
            Destroy(flash.gameObject);
            hidden = wasHidden;
            if (!hidden) layerGroup.alpha = 1;
            ShowToast("✨ Photo saved to " + folder);
        }

        void CloseTopMenu()
        {
            Sheet top = null;
            foreach (var s in Sheets())
                if (s != null && s.IsOpen && (top == null || s.transform.GetSiblingIndex() > top.transform.GetSiblingIndex())) top = s;
            if (top != null) top.Close();
        }

        void Toggle(Sheet sheet, bool highlight)
        {
            bool wasOpen = sheet.IsOpen;
            foreach (var s in Sheets()) if (s != null && s != sheet) s.Close();
            if (wasOpen) sheet.Close(); else sheet.Open(highlight);
        }

        void OpenPet(PetActor pet)
        {
            foreach (var s in Sheets()) if (s != null) s.Close();
            petCard = Menus.PetCard(root, world, pet);
            petCard.Open(!PlazaInput.UsingPointer);
        }

        // ---- chat ----

        void OpenChat()
        {
            chatOpenedAt = Time.unscaledTime;
            chat.Select();
            chat.ActivateInputField();
        }

        void CloseChat()
        {
            chat.DeactivateInputField();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void OnChatEnd(string _)
        {
            var kb = chat.touchScreenKeyboard;
            bool submitted = PlazaInput.EnterPressed() || (kb != null && kb.status == TouchScreenKeyboard.Status.Done);
            if (!submitted) return;
            if (Time.unscaledTime - chatOpenedAt < .15f) { chat.ActivateInputField(); return; } // the Enter that opened the box
            SendChat();
        }

        void SendChat()
        {
            world.Say(chat.text);
            chat.text = "";
            CloseChat();
        }

        void AddChatLine(string who, string text, bool villager)
        {
            string words = UiKit.SplitEmoji(text, out _);
            if (words.Length == 0) return;
            var line = UiKit.Label(logLines, "", 14, Palette.Ink);
            line.supportRichText = true;
            string colour = villager ? "#4f7fd6" : "#c4532a";
            line.text = $"<color={colour}>{Escape(who)}</color>  {Escape(words)}";
            while (logLines.childCount > ChatLines) DestroyImmediate(logLines.GetChild(0).gameObject);
            lastLineAt = Time.unscaledTime;
        }

        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        // ---- notices ----

        void ShowToast(string text)
        {
            toastText.text = UiKit.SplitEmoji(text, out string icon);
            UiKit.SetIcon(toastIcon, icon);
            UiKit.Show(toast, true);
            toastUntil = Time.unscaledTime + 2.8f;
        }

        void ShowBanner(string title, string sub)
        {
            if (string.IsNullOrEmpty(title)) return;
            bannerTitle.text = UiKit.SplitEmoji(title, out _);
            bannerSub.text = UiKit.SplitEmoji(sub, out _);
            UiKit.Show(bannerSub, bannerSub.text.Length > 0);
            bannerAt = Time.unscaledTime;
        }

        /// <summary>Updates the status pill and the place card.</summary>
        void Refresh()
        {
            if (world == null || world.Net == null) return;
            var net = world.Net;
            string area = world.Area ?? world.Map.Name;
            placeText.text = area == world.Map.Name ? area : world.Map.Name + " · " + area;

            string detail;
            Color dot = Palette.InkSoft;
            switch (net.State)
            {
                case LiveState.Live:
                    int others = world.PeopleHere();
                    detail = others == 0 ? "Just you" : (others + 1) + " here";
                    if (net.RoundTripMs > 0) detail += " · " + Mathf.RoundToInt(net.RoundTripMs) + " ms";
                    dot = Palette.Leaf;
                    break;
                case LiveState.Connecting: detail = "Connecting…"; dot = Palette.Amber; break;
                case LiveState.Busy: detail = "Finding a room…"; dot = Palette.Amber; break;
                case LiveState.Reconnecting: detail = "Reconnecting…"; dot = Palette.Amber; break;
                case LiveState.Elsewhere: detail = "Open on another device"; break;
                case LiveState.SentHome: detail = "Taking a short break"; break;
                default: detail = Settings.SignedIn ? "Offline" : "Exploring offline"; break;
            }
            detailText.text = detail;
            liveDot.color = dot;
            bool elsewhere = net.State == LiveState.Elsewhere;
            UiKit.Show(playHere, elsewhere);
            statusRow.padding.right = elsewhere ? 5 : 16;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)statusRow.transform);

            var spot = world.Hint;
            UiKit.Show(hint, spot != null);
            if (spot != null)
            {
                UiKit.SetIcon(hintIcon, spot.Icon);
                hintName.text = UiKit.SplitEmoji(spot.Name, out _);
                hintSub.text = UiKit.SplitEmoji(spot.Sub, out _);
                UiKit.Show(hintSub, hintSub.text.Length > 0);
            }
        }
    }
}
