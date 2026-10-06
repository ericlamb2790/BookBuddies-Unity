using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Pets;
using BookBuddies.Tales;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Everything on screen over the town: where you are and who's around, area banners, toasts, the place card
    /// (with its action button), the minimap, the chat log and bar, the bag and hero buttons, and the emote, trick,
    /// pet and town menus. Photo mode (H hides it all, P takes a picture) lives here too, and so do the keyboard
    /// and gamepad shortcuts.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class Hud : MonoBehaviour
    {
        const int ChatLines = 6;
        const float ChatFadeAfter = 12f;
        const float CompactWidth = 820f;  // narrower: icon-only buttons
        const float TinyWidth = 600f;     // narrower still: bag and hero live in the menu only
        const float BarHeight = 56f;

        PlazaWorld world;
        PlazaNetwork net;
        System.Action toTitle;
        RectTransform root, layer; // layer: everything except the menus
        CanvasGroup layerGroup;
        Overlays labels;

        Image liveDot;
        HorizontalLayoutGroup statusRow;
        Text placeText, detailText;
        Button playHere;
        CanvasGroup banner; Text bannerTitle, bannerSub; float bannerAt = -99;
        RectTransform toast; Text toastText; Image toastIcon; float toastUntil;
        RectTransform hint; Image hintIcon; Text hintName, hintSub, hintLater, verbLabel; Button verb; Image verbKey; Text verbKeyText;
        CanvasGroup log; RectTransform logLines; float lastLineAt = -99;
        InputField chat; float chatOpenedAt;
        readonly List<Text> wideOnly = new List<Text>(); // button words hidden on narrow screens
        Button bag, hero;                                // in the menu too, so phones can drop them from the corner
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
            hud.net = world.Net;
            hud.toTitle = toTitle;
            hud.root = (RectTransform)canvas.transform;
            hud.Build();
            hud.labels = Overlays.Create(world);
            world.Toast += hud.ShowToast;
            world.Banner += hud.ShowBanner;
            world.ChatLine += hud.AddChatLine;
            world.PetTapped += hud.OpenPet;
            world.Changed += hud.Refresh;
            hud.net.StateChanged += hud.OnNetState;
            hud.Refresh();
            hud.shownAt = Time.unscaledTime;
            return hud;
        }

        // the world and its connection can outlive the HUD (and are torn down in any order when Play mode stops)
        void OnDestroy()
        {
            if ((object)world != null)
            {
                world.Toast -= ShowToast;
                world.Banner -= ShowBanner;
                world.ChatLine -= AddChatLine;
                world.PetTapped -= OpenPet;
                world.Changed -= Refresh;
            }
            if (net != null) net.StateChanged -= OnNetState;
            PlazaInput.MenuOpen = false;
            if (labels) Destroy(labels.gameObject);
        }

        void OnNetState(LiveState _) => Refresh();

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

        // HUD buttons stay out of keyboard and d-pad navigation, so the highlight never wanders out of an open menu
        static T Quiet<T>(T control) where T : Selectable
        {
            control.navigation = new Navigation { mode = Navigation.Mode.None };
            return control;
        }

        // shown for a moment when the HUD hides
        void BuildPhotoHint()
        {
            var pill = UiKit.Panel(root, "photo hint", Palette.Ink.WithAlpha(.8f), 22);
            pill.raycastTarget = false;
            var r = pill.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, 28), new Vector2(10, 44));
            UiKit.Row(r, 0, new RectOffset(22, 22, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Hug(r, true, false);
            string keys = Application.isMobilePlatform ? "Tap the screen with two fingers to show the HUD" : "Photo mode · P takes a picture · H shows the HUD";
            UiKit.Label(r, keys, UiKit.BodySize - 1, Palette.Cream, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
            photoHint = pill.gameObject.AddComponent<CanvasGroup>();
            photoHint.alpha = 0;
        }

        // Top left: a live dot, the town and area, and how many readers are here.
        void BuildStatus()
        {
            var pill = UiKit.Panel(layer, "status", Palette.Cream, 24);
            var r = pill.rectTransform.Pin(new Vector2(0, 1), new Vector2(16, -16), new Vector2(10, 48));
            statusRow = UiKit.Row(r, 10, new RectOffset(18, 20, 0, 0));
            UiKit.Hug(r, true, false);
            UiKit.Outline(pill, Palette.Ink.WithAlpha(.08f), 24, 1);
            UiKit.Shadow(r, 24, 12, 4, .22f);
            liveDot = UiKit.Panel(r, "live", Palette.InkSoft, 6);
            UiKit.Size(liveDot, 12, 12);
            placeText = UiKit.Label(r, "", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            placeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            detailText = UiKit.Label(r, "", UiKit.SmallSize + 1, Palette.InkSoft);
            detailText.horizontalOverflow = HorizontalWrapMode.Overflow;
            playHere = Quiet(UiKit.Primary(r, "Play here", () => world.Net.Start(), null, 38));
        }

        // Top right: bag, hero, controls help and the town menu.
        void BuildCorner()
        {
            var r = UiKit.Node("corner", layer).Pin(new Vector2(1, 1), new Vector2(-16, -16), new Vector2(10, 48));
            UiKit.Row(r, 10, null, TextAnchor.MiddleRight);
            UiKit.Hug(r, true, false);
            bag = CornerButton(r, "🎒", "Bag", () => TalesUi.OpenBag());
            hero = CornerButton(r, "🦊", "Hero", () => TalesUi.OpenHero());
            var help = Quiet(UiKit.Button(r, "Controls", Palette.Cream, () => Toggle(controls), 24));
            UiKit.Size(help, 48, 48);
            ((RectTransform)UiKit.Label(help.transform, "?", UiKit.HeadingSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).transform).Fill();
            UiKit.Shadow((RectTransform)help.transform, 24, 12, 4, .22f);
            CornerButton(r, null, "Menu", () => Toggle(pause));
        }

        Button CornerButton(Transform corner, string emoji, string label, System.Action onClick)
        {
            var b = Quiet(UiKit.TextButton(corner, label, emoji, Palette.Cream, Palette.Ink, onClick, 48));
            UiKit.Shadow((RectTransform)b.transform, 24, 12, 4, .22f);
            if (emoji != null) wideOnly.Add(b.GetComponentInChildren<Text>());
            return b;
        }

        // Centre top: the area you just walked into, in the site's display face.
        void BuildBanner()
        {
            var card = UiKit.Panel(layer, "banner", Palette.Cream);
            card.raycastTarget = false;
            var r = card.rectTransform.Pin(new Vector2(.5f, 1), new Vector2(0, -84), new Vector2(10, 10));
            UiKit.Column(r, 2, new RectOffset(28, 28, 12, 14), TextAnchor.MiddleCenter);
            UiKit.Hug(r);
            UiKit.Shadow(r, UiKit.CardRadius, 18, 6, .26f);
            bannerTitle = UiKit.Label(r, "", UiKit.TitleSize + 4, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            bannerTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            bannerSub = UiKit.Label(r, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            bannerSub.horizontalOverflow = HorizontalWrapMode.Overflow;
            banner = card.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0;
            banner.blocksRaycasts = false;
        }

        // Above the chat bar: a toast, and a card for the place you're standing at (with its action, when it has one).
        void BuildNotices()
        {
            var stack = UiKit.Node("notices", layer).Pin(new Vector2(.5f, 0), new Vector2(0, BarHeight + 28), new Vector2(10, 10));
            UiKit.Column(stack, 10, null, TextAnchor.LowerCenter).childForceExpandWidth = false;
            UiKit.Hug(stack);

            var t = UiKit.Panel(stack, "toast", Palette.Ink, 22);
            t.raycastTarget = false;
            toast = t.rectTransform;
            UiKit.Row(toast, 10, new RectOffset(16, 20, 10, 10), TextAnchor.MiddleCenter);
            toastIcon = UiKit.Icon(toast, null, 24);
            toastText = UiKit.Label(toast, "", UiKit.BodySize - 1, Palette.Paper, UiKit.Bold);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Shadow(toast, 22, 12, 4, .3f);
            UiKit.Show(toast, false);

            var h = UiKit.Panel(stack, "place", Palette.Cream);
            h.raycastTarget = false;
            hint = h.rectTransform;
            UiKit.Row(hint, 14, new RectOffset(14, 14, 12, 12));
            UiKit.Outline(h, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            hintIcon = UiKit.Icon(hint, null, 48);
            var words = UiKit.Node("words", hint);
            UiKit.Column(words, 1, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, 260);
            hintName = UiKit.Label(words, "", UiKit.BodySize + 2, Palette.Ink, UiKit.Bold);
            hintSub = UiKit.Label(words, "", UiKit.SmallSize, Palette.InkSoft);
            hintLater = UiKit.Label(words, "Opens in a later version", UiKit.SmallSize, UiKit.EmberInk);
            verb = Quiet(UiKit.Primary(hint, "Use", () => UseSpot(), null, 52));
            verbLabel = verb.GetComponentInChildren<Text>();
            verbKey = UiKit.PadGlyph(verb.transform, "A", 26);
            verbKey.transform.SetAsFirstSibling();
            verbKeyText = verbKey.GetComponentInChildren<Text>();
            UiKit.Shadow(hint, UiKit.CardRadius, 16, 5, .24f);
            UiKit.Show(hint, false);
        }

        // Bottom: recent chat, the chat box, and the emote and trick buttons.
        void BuildChat()
        {
            var bg = UiKit.Panel(layer, "chat log", Palette.Cream.WithAlpha(.9f));
            bg.raycastTarget = false;
            logLines = bg.rectTransform.Pin(Vector2.zero, new Vector2(16, BarHeight + 28), new Vector2(420, 10));
            UiKit.Column(logLines, 4, new RectOffset(14, 14, 10, 12));
            UiKit.Hug(logLines, false, true);
            log = bg.gameObject.AddComponent<CanvasGroup>();
            log.alpha = 0;
            log.blocksRaycasts = false;

            var bar = UiKit.Node("chat bar", layer);
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(.5f, 0);
            bar.offsetMin = new Vector2(16, 16); bar.offsetMax = new Vector2(-16, 16 + BarHeight);
            UiKit.Row(bar, 10).childForceExpandHeight = true;

            chat = Quiet(UiKit.Input(bar, "Say something…"));
            chat.characterLimit = 140;
            chat.onEndEdit.AddListener(OnChatEnd);
            UiKit.Size(chat, 140, -1, 1);
            UiKit.Shadow((RectTransform)chat.transform, 12, 12, 4, .2f);

            var send = Quiet(UiKit.Primary(bar, "Send", SendChat, null, (int)BarHeight));
            UiKit.Shadow((RectTransform)send.transform, 28, 12, 4, .2f);
            BarButton(bar, "❤️", "Emotes", () => Toggle(emotes));
            BarButton(bar, "🐾", "Tricks", () => Toggle(tricks));
        }

        void BarButton(Transform bar, string emoji, string label, System.Action onClick)
        {
            var b = Quiet(UiKit.TextButton(bar, label, emoji, Palette.Cream, Palette.Ink, onClick, (int)BarHeight));
            UiKit.Shadow((RectTransform)b.transform, 28, 12, 4, .2f);
            wideOnly.Add(b.GetComponentInChildren<Text>());
        }

        // ---- every frame ----

        void Update()
        {
            if (!Mathf.Approximately(root.rect.width, lastWidth)) Fit(root.rect.width);
            bool sheetOpen = AnySheetOpen();
            PlazaInput.MenuOpen = sheetOpen || minimap.IsBig || UiStack.Any;
            HandleKeys();
            if (!PlazaInput.MenuOpen && !chat.isFocused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null); // so A or Enter doesn't press a button you clicked earlier
            ShowVerbKey();
            Animate();
        }

        void HandleKeys()
        {
            if (PlazaInput.Down(PlazaAction.Back)) // open screens took their Back already (VirtualCursor → UiStack)
            {
                if (chat.isFocused) CloseChat();
                else if (UiStack.Any) return;
                else if (minimap.IsBig) minimap.Toggle();
                else if (hidden) SetHidden(false);
                else if (!PlazaInput.Locked) Toggle(pause);
                return;
            }
            if (PlazaInput.Typing) return;
            if (UiStack.Any && !AnySheetOpen()) return; // a full screen (settings, the bag, admin tools) has the keys
            if (PlazaInput.Down(PlazaAction.Menu)) { if (minimap.IsBig) minimap.Toggle(); Toggle(pause); return; }
            if (PlazaInput.Down(PlazaAction.Photo)) { StartCoroutine(TakePhoto()); return; }
            if (PlazaInput.Down(PlazaAction.HideHud) || (hidden && PlazaInput.TwoFingerTap())) { SetHidden(!hidden); return; }
            if (PlazaInput.Down(PlazaAction.Map) && !AnyMenuOpen()) { minimap.Toggle(); return; }
            if (PlazaInput.Down(PlazaAction.Emotes)) Toggle(emotes);
            else if (PlazaInput.Down(PlazaAction.Tricks)) Toggle(tricks);
            else if (PlazaInput.MenuOpen) return;
            else if (PlazaInput.Down(PlazaAction.Bag)) TalesUi.OpenBag();
            else if (PlazaInput.Down(PlazaAction.Hero)) TalesUi.OpenHero();
            else if (PlazaInput.Down(PlazaAction.Chat)) OpenChat();
            else if (PlazaInput.Down(PlazaAction.Use) && !UseSpot()) world.UseNearby();
        }

        // the place card's button, or E / A while standing at the place
        bool UseSpot() => SpotActions.CanUse(world.Hint) && SpotActions.Use(world.Hint);

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
            ((RectTransform)banner.transform).anchoredPosition = new Vector2(0, -84 + (1 - show) * 10);

            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) UiKit.Show(toast, false);

            float idle = Time.unscaledTime - lastLineAt;
            float target = chat.isFocused ? 1 : idle < ChatFadeAfter ? 1 : 0;
            log.alpha = Mathf.MoveTowards(log.alpha, logLines.childCount > 0 ? target : 0, Time.unscaledDeltaTime * 3);
        }

        // the key on the place card's button: A on a gamepad, E with a keyboard, none for touch
        void ShowVerbKey()
        {
            if (!verb.gameObject.activeInHierarchy) return;
            bool keys = !Application.isMobilePlatform || PlazaInput.UsingGamepad;
            UiKit.Show(verbKey, keys);
            if (!keys) return;
            string key = PlazaInput.UsingGamepad ? "A" : "E";
            if (verbKeyText.text == key) return;
            verbKeyText.text = key;
            verbKey.color = PlazaInput.UsingGamepad ? UiKit.LeafInk : Palette.Ink;
        }

        /// <summary>Narrow screens get icon-only buttons, and the chat log moves up under the status pill.</summary>
        void Fit(float width)
        {
            lastWidth = width;
            bool compact = width < CompactWidth;
            foreach (var label in wideOnly)
            {
                UiKit.Show(label, !compact);
                label.transform.parent.GetComponent<HorizontalLayoutGroup>().padding.right = compact ? 14 : 20;
            }
            UiKit.Show(bag, width >= TinyWidth);
            UiKit.Show(hero, width >= TinyWidth);
            var size = new Vector2(Mathf.Min(420, width - 32), logLines.sizeDelta.y);
            if (compact) logLines.Pin(new Vector2(0, 1), new Vector2(16, -76), size);
            else logLines.Pin(Vector2.zero, new Vector2(16, BarHeight + 28), size);
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

        void Toggle(Sheet sheet)
        {
            bool wasOpen = sheet.IsOpen;
            foreach (var s in Sheets()) if (s != null && s != sheet) s.Close();
            if (wasOpen) sheet.Close(); else sheet.Open();
        }

        void OpenPet(PetActor pet)
        {
            foreach (var s in Sheets()) if (s != null) s.Close();
            petCard = Menus.PetCard(root, world, pet);
            petCard.Open();
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
            var line = UiKit.Label(logLines, "", UiKit.SmallSize + 1, Palette.Ink);
            line.supportRichText = true;
            string colour = villager ? "#2f5fb0" : "#b4521f";
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
            if (!this || !placeText || world == null || world.Net == null) return;
            var live = world.Net;
            string area = world.Area ?? world.Map.Name;
            placeText.text = area == world.Map.Name ? area : world.Map.Name + " · " + area;

            string detail;
            Color dot = Palette.InkSoft;
            switch (live.State)
            {
                case LiveState.Live:
                    int others = world.PeopleHere();
                    detail = others == 0 ? "Just you" : (others + 1) + " here";
                    if (live.RoundTripMs > 0) detail += " · " + Mathf.RoundToInt(live.RoundTripMs) + " ms";
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
            bool elsewhere = live.State == LiveState.Elsewhere;
            UiKit.Show(playHere, elsewhere);
            statusRow.padding.right = elsewhere ? 6 : 20;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)statusRow.transform);

            var spot = world.Hint;
            UiKit.Show(hint, spot != null);
            if (spot == null) return;
            UiKit.SetIcon(hintIcon, spot.Icon);
            hintName.text = UiKit.SplitEmoji(spot.Name, out _);
            hintSub.text = UiKit.SplitEmoji(spot.Sub, out _);
            UiKit.Show(hintSub, hintSub.text.Length > 0);
            bool usable = SpotActions.CanUse(spot);
            UiKit.Show(verb, usable);
            UiKit.Show(hintLater, !usable);
            verbLabel.text = string.IsNullOrEmpty(spot.Verb) ? "Go" : UiKit.SplitEmoji(spot.Verb, out _);
        }
    }
}
