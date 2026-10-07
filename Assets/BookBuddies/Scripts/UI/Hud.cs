using System.Collections.Generic;
using BookBuddies.Economy;
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
    /// Everything on screen over the town, laid out for 1920×1080 and kept tidy down to small windows: your pet's card
    /// (top left, the road's objective card under it), controls help, the menu, the minimap and where you are (top right),
    /// toasts and chapter titles (top centre), the dock of emotes, tricks, pets, bag, hero and map (bottom centre), the
    /// chat box with recent chat over it (bottom left), and the card for the place you're standing at (over the dock).
    /// Also the emote, trick, pet, coins and town menus. Photo mode (H hides it all, P takes a picture) lives here too,
    /// and so do the keyboard and gamepad shortcuts.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class Hud : MonoBehaviour
    {
        /// <summary>Space kept clear at the screen's edges, and between HUD cards (reference pixels).</summary>
        public const float Margin = 20, Gap = 12;
        const float CornerHeight = 48, TileWidth = 80, TileHeight = 66, SmallTile = 58, DockPad = 8, TileGap = 6;
        /// <summary>Where the minimap starts, under the top-right buttons.</summary>
        public const float BelowCorner = Margin + CornerHeight + Gap;
        /// <summary>Where cards under the pet card start (the road's objective card).</summary>
        public const float BelowPetCard = Margin + PetBadge.Height + Gap;
        /// <summary>Where cards that open from the dock sit (emotes, tricks, another pet's card).</summary>
        public const float AboveDock = Margin + TileHeight + DockPad * 2 + Gap;

        const float WideWidth = 1180;  // narrower: dock tiles without words
        const float TinyWidth = 600;   // narrower still: bag and hero live in the menu only
        const float ChatHeight = 54, ChatWidth = 420, ChatOpenWidth = 560, ChatMinWidth = 240;
        const float PlaceHalf = 250, PlaceHeight = 76, ObjectiveHeight = 64, MinLane = 400;
        const int ChatLines = 6;
        const float ChatFadeAfter = 12f;
        const string GiftOfferedKey = "bb.giftOffered"; // the account and day the gift last opened by itself

        /// <summary>True while the HUD is hidden (photo mode), so the road's corner hides with it.</summary>
        public static bool Hidden { get; private set; }

        /// <summary>A dock button: an icon over a word, its key or gamepad button in the corner, amber while its menu is open.</summary>
        sealed class Tile { public Button Button; public Image Back; public Text Word; public GameObject Key, Pad; public System.Func<bool> IsOpen; }

        PlazaWorld world;
        PlazaNetwork net;
        System.Action toTitle;
        RectTransform root, layer; // layer: everything except the menus
        CanvasGroup layerGroup;
        Overlays labels;
        Notices notices;

        RectTransform where, hint, dock, chatBox, logLines;
        Image liveDot;
        Text placeText, detailText, chatHint;
        Button playHere, send;
        Image hintIcon; Text hintName, hintSub, hintLater, verbLabel; Button verb; Image verbKey; Text verbKeyText;
        CanvasGroup log; float lastLineAt = -99;
        InputField chat; float chatOpenedAt = -99;
        readonly List<Tile> tiles = new List<Tile>();
        Tile chatTile, bagTile, heroTile;
        Sheet emotes, tricks, controls, pause, petCard, coins;
        Minimap minimap;
        CanvasGroup photoHint;
        Vector2 laidOut;
        float shownAt, photoHintAt = -99, dockTop, chatRest, chatOpen;
        bool narrowChat;

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
            hud.StartCoroutine(hud.OfferGift());
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
            Hidden = false;
            if (labels) Destroy(labels.gameObject);
        }

        void OnNetState(LiveState _) => Refresh();

        // ---- building ----

        void Build()
        {
            layer = UiKit.Node("hud", root).Fill();
            layerGroup = layer.gameObject.AddComponent<CanvasGroup>();
            layerGroup.alpha = 0; // floats in (see Animate)
            PetBadge.Create(layer, world, () => Toggle(coins));
            BuildCorner();
            BuildWhere();
            BuildPlaceCard();
            BuildDock();
            BuildChat();
            notices = Notices.Create(layer);
            minimap = Minimap.Create(layer, world);
            emotes = Menus.Emotes(root, world);
            tricks = Menus.Tricks(root, world);
            controls = Menus.Controls(root);
            pause = Menus.Pause(root, world, () => SetHidden(true), () => toTitle(), () => Toggle(coins));
            coins = WalletSheet.Create(root);
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

        // Top right: controls help and the town menu (the minimap sits under them).
        void BuildCorner()
        {
            var r = UiKit.Node("corner", layer).Pin(new Vector2(1, 1), new Vector2(-Margin, -Margin), new Vector2(10, CornerHeight));
            UiKit.Row(r, 10, null, TextAnchor.MiddleRight);
            UiKit.Hug(r, true, false);
            var help = Quiet(UiKit.Button(r, "Controls", Palette.Cream, () => Toggle(controls), 24));
            UiKit.Size(help, CornerHeight, CornerHeight);
            ((RectTransform)UiKit.Label(help.transform, "?", UiKit.HeadingSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).transform).Fill();
            UiKit.Shadow((RectTransform)help.transform, 24, 12, 4, .22f);
            var menu = Quiet(UiKit.TextButton(r, "Menu", null, Palette.Cream, Palette.Ink, () => Toggle(pause), (int)CornerHeight));
            UiKit.Shadow((RectTransform)menu.transform, 24, 12, 4, .22f);
        }

        // Under the minimap: the town, then quietly the area, who's here and the connection (and "Play here" when needed).
        void BuildWhere()
        {
            var card = UiKit.Panel(layer, "where", Palette.Cream);
            where = card.rectTransform.Pin(new Vector2(1, 1), Vector2.zero, new Vector2(10, 10));
            where.pivot = new Vector2(.5f, 1);
            UiKit.Column(where, 2, new RectOffset(16, 16, 8, 10), TextAnchor.MiddleCenter).childForceExpandWidth = false;
            UiKit.Hug(where);
            UiKit.Outline(card, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(where, UiKit.CardRadius, 12, 4, .2f);
            placeText = UiKit.Label(where, "", UiKit.BodySize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            placeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var status = UiKit.Node("status", where);
            UiKit.Row(status, 6, null, TextAnchor.MiddleCenter);
            liveDot = UiKit.Panel(status, "live", Palette.InkSoft, 5);
            UiKit.Size(liveDot, 10, 10);
            detailText = UiKit.Label(status, "", UiKit.SmallSize, Palette.InkSoft);
            detailText.horizontalOverflow = HorizontalWrapMode.Overflow;
            playHere = Quiet(UiKit.Primary(where, "Play here", () => world.Net.Start(), null, 44));
        }

        // Over the dock: a card for the place you're standing at, with its action when it has one.
        void BuildPlaceCard()
        {
            var h = UiKit.Panel(layer, "place", Palette.Cream);
            h.raycastTarget = false;
            hint = h.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, AboveDock), new Vector2(10, 10));
            UiKit.Row(hint, 14, new RectOffset(14, 14, 12, 12));
            UiKit.Hug(hint);
            UiKit.Outline(h, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            hintIcon = UiKit.Icon(hint, null, 48);
            var words = UiKit.Node("words", hint);
            UiKit.Column(words, 1, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, 260);
            hintName = UiKit.Label(words, "", UiKit.BodySize + 2, Palette.Ink, UiKit.Bold);
            hintSub = UiKit.Label(words, "", UiKit.SmallSize + 1, Palette.InkSoft);
            hintLater = UiKit.Label(words, "Opens in a later version", UiKit.SmallSize, UiKit.EmberInk);
            verb = Quiet(UiKit.Primary(hint, "Use", () => UseSpot(), null, 52));
            verbLabel = verb.GetComponentInChildren<Text>();
            verbKey = UiKit.PadGlyph(verb.transform, "A", 26);
            verbKey.transform.SetAsFirstSibling();
            verbKeyText = verbKey.GetComponentInChildren<Text>();
            UiKit.Shadow(hint, UiKit.CardRadius, 16, 5, .24f);
            UiKit.Show(hint, false);
        }

        // Bottom centre: the dock, the pet game's hotbar. Chat joins it on narrow screens.
        void BuildDock()
        {
            var tray = UiKit.Panel(layer, "dock", Palette.Cream, 22);
            dock = tray.rectTransform.Pin(new Vector2(.5f, 0), new Vector2(0, Margin), new Vector2(10, 10));
            UiKit.Row(dock, TileGap, new RectOffset((int)DockPad, (int)DockPad, (int)DockPad, (int)DockPad), TextAnchor.MiddleCenter);
            UiKit.Hug(dock);
            UiKit.Outline(tray, Palette.Ink.WithAlpha(.08f), 22, 1);
            UiKit.Shadow(dock, 22, 16, 5, .24f);
            chatTile = AddTile("💬", "Chat", "T", null, OpenChat);
            AddTile("❤️", "Emotes", "Q", "X", () => Toggle(emotes), () => emotes.IsOpen);
            AddTile("🌀", "Tricks", "F", "Y", () => Toggle(tricks), () => tricks.IsOpen);
            AddTile("🐾", "Pets", null, "Select", OpenPets);
            bagTile = AddTile("🎒", "Bag", "I", null, () => TalesUi.OpenBag());
            heroTile = AddTile("🦊", "Hero", "C", null, () => TalesUi.OpenHero());
            AddTile("🗺️", "Map", "M", "RS", () => minimap.Toggle(), () => minimap.IsBig);
        }

        Tile AddTile(string emoji, string word, string key, string pad, System.Action onClick, System.Func<bool> isOpen = null)
        {
            var b = Quiet(UiKit.Button(dock, word, Palette.Paper, onClick, 14));
            UiKit.Outline(b, Palette.Ink.WithAlpha(.08f), 14, 1);
            var r = (RectTransform)b.transform;
            UiKit.Column(r, 2, new RectOffset(4, 4, 6, 8), TextAnchor.MiddleCenter).childForceExpandWidth = false;
            UiKit.Icon(r, emoji, 32);
            var tile = new Tile { Button = b, Back = (Image)b.targetGraphic, IsOpen = isOpen };
            tile.Word = UiKit.Label(r, word, UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            tile.Word.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (key != null && !Application.isMobilePlatform) tile.Key = Corner(KeyCap(r, key));
            if (pad != null) tile.Pad = Corner(UiKit.PadGlyph(r, pad, 22));
            tiles.Add(tile);
            return tile;
        }

        // the keyboard key for a dock tile, like the world's "E" prompt
        static Image KeyCap(Transform parent, string key)
        {
            var cap = UiKit.Panel(parent, "key " + key, Palette.Ink.WithAlpha(.72f), 6);
            cap.raycastTarget = false;
            UiKit.Size(cap, 22, 22);
            ((RectTransform)UiKit.Label(cap.transform, key, 13, Palette.Cream, UiKit.Bold, TextAnchor.MiddleCenter).transform).Fill();
            return cap;
        }

        // a key hint worn like a badge on a tile's top-right corner, outside its layout
        static GameObject Corner(Image glyph)
        {
            var size = UiKit.Size(glyph);
            size.ignoreLayout = true;
            var r = glyph.rectTransform.Pin(Vector2.one, new Vector2(TileGap, TileGap), new Vector2(size.preferredWidth, size.preferredHeight));
            r.SetAsLastSibling();
            return glyph.gameObject;
        }

        // Bottom left: the chat box (Enter or T), with recent chat floating over it and fading.
        void BuildChat()
        {
            var bg = UiKit.Panel(layer, "chat log", Palette.Cream.WithAlpha(.92f));
            bg.raycastTarget = false;
            logLines = bg.rectTransform.Pin(Vector2.zero, new Vector2(Margin, AboveDock), new Vector2(ChatWidth, 10));
            UiKit.Column(logLines, 4, new RectOffset(16, 16, 10, 12));
            UiKit.Hug(logLines, false, true);
            log = bg.gameObject.AddComponent<CanvasGroup>();
            log.alpha = 0;
            log.blocksRaycasts = false;

            chat = Quiet(UiKit.Input(layer, "Say something…", UiKit.BodySize, (int)(ChatHeight / 2)));
            chat.characterLimit = 140;
            chat.onEndEdit.AddListener(OnChatEnd);
            chatBox = ((RectTransform)chat.transform).Pin(Vector2.zero, new Vector2(Margin, Margin), new Vector2(ChatWidth, ChatHeight));
            UiKit.Shadow(chatBox, (int)(ChatHeight / 2), 14, 4, .22f);
            ((RectTransform)UiKit.Icon(chatBox, "💬", 24).transform).Pin(new Vector2(0, .5f), new Vector2(18, 0), new Vector2(24, 24));
            chatHint = (Text)chat.placeholder;
            foreach (var t in new[] { chat.textComponent, chatHint })
            {
                t.rectTransform.offsetMin = new Vector2(52, 0);
                t.rectTransform.offsetMax = new Vector2(-58, 0);
            }
            send = Quiet(UiKit.Button(chatBox, "Send", Palette.Amber, SendChat, 22));
            ((RectTransform)send.transform).Pin(new Vector2(1, .5f), new Vector2(-5, 0), new Vector2(44, 44));
            UiKit.Arrow(send.transform, Palette.Ink);
        }

        // ---- every frame ----

        void Update()
        {
            if (root.rect.size != laidOut) Fit(root.rect.size);
            bool sheetOpen = AnySheetOpen();
            PlazaInput.MenuOpen = sheetOpen || minimap.IsBig || UiStack.Any;
            HandleKeys();
            if (!PlazaInput.MenuOpen && !chat.isFocused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null); // so A or Enter doesn't press a button you clicked earlier
            ShowVerbKey();
            ShowTiles();
            FitChat();
            FitWhere();
            Animate();
        }

        void HandleKeys()
        {
            if (PlazaInput.Down(PlazaAction.Back)) // open screens took their Back already (VirtualCursor → UiStack)
            {
                if (chat.isFocused) CloseChat();
                else if (UiStack.Any) return;
                else if (minimap.IsBig) minimap.Toggle();
                else if (Hidden) SetHidden(false);
                else if (!PlazaInput.Locked) Toggle(pause);
                return;
            }
            if (PlazaInput.Typing) return;
            if (UiStack.Any && !AnySheetOpen()) return; // a full screen (settings, the bag, admin tools) has the keys
            if (PlazaInput.Down(PlazaAction.Menu)) { if (minimap.IsBig) minimap.Toggle(); Toggle(pause); return; }
            if (PlazaInput.Down(PlazaAction.Photo)) { StartCoroutine(TakePhoto()); return; }
            if (PlazaInput.Down(PlazaAction.HideHud) || (Hidden && PlazaInput.TwoFingerTap())) { SetHidden(!Hidden); return; }
            if (PlazaInput.Down(PlazaAction.Map) && !AnyMenuOpen()) { minimap.Toggle(); return; }
            if (PlazaInput.Down(PlazaAction.Emotes)) Toggle(emotes);
            else if (PlazaInput.Down(PlazaAction.Tricks)) Toggle(tricks);
            else if (PlazaInput.MenuOpen) return;
            else if (PlazaInput.Down(PlazaAction.Bag)) TalesUi.OpenBag();
            else if (PlazaInput.Down(PlazaAction.Hero)) TalesUi.OpenHero();
            else if (PlazaInput.Down(PlazaAction.Pets)) OpenPets();
            else if (PlazaInput.Down(PlazaAction.Chat)) OpenChat();
            else if (PlazaInput.Down(PlazaAction.Use) && !UseSpot()) world.UseNearby();
        }

        // the place card's button, or E / A while standing at the place
        bool UseSpot() => SpotActions.CanUse(world.Hint) && SpotActions.Use(world.Hint);

        void Animate()
        {
            // the HUD floats in after arriving, and slips away in photo mode
            float shown = Hidden ? 0 : UiKit.Ease((Time.unscaledTime - shownAt) / .6f);
            layerGroup.alpha = Mathf.MoveTowards(layerGroup.alpha, shown, Time.unscaledDeltaTime * 4);
            layerGroup.blocksRaycasts = !Hidden;
            if (labels) labels.gameObject.SetActive(!Hidden);
            float hintAge = Time.unscaledTime - photoHintAt;
            photoHint.alpha = Hidden && hintAge < 3.5f ? Mathf.Clamp01(Mathf.Min(hintAge / .3f, (3.5f - hintAge) / .5f)) : 0;

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

        // dock tiles: amber while their menu is open; keys for the keyboard, buttons for a gamepad
        void ShowTiles()
        {
            bool pad = PlazaInput.UsingGamepad;
            foreach (var t in tiles)
            {
                var back = t.IsOpen != null && t.IsOpen() ? Palette.Amber : Palette.Paper;
                if (t.Back.color != back) t.Back.color = back;
                if (t.Key && t.Key.activeSelf == pad) t.Key.SetActive(!pad);
                if (t.Pad && t.Pad.activeSelf != pad) t.Pad.SetActive(pad);
            }
        }

        /// <summary>
        /// Lays the HUD out for a new screen size: dock tiles lose their words on narrow screens, the chat box takes the room
        /// left of the dock (or joins the dock when there's too little), and toasts and titles find their lane.
        /// </summary>
        void Fit(Vector2 screen)
        {
            laidOut = screen;
            bool wide = screen.x >= WideWidth;
            foreach (var t in tiles)
            {
                UiKit.Size(t.Button, wide ? TileWidth : SmallTile, wide ? TileHeight : SmallTile);
                UiKit.Show(t.Word, wide);
            }
            UiKit.Show(bagTile.Button, screen.x >= TinyWidth);
            UiKit.Show(heroTile.Button, screen.x >= TinyWidth);
            UiKit.Show(chatTile.Button, false);
            int count = 0;
            foreach (var t in tiles) if (t.Button.gameObject.activeSelf) count++;
            float tile = wide ? TileWidth : SmallTile;
            float dockLeft = (screen.x - (count * tile + (count - 1) * TileGap + DockPad * 2)) / 2;
            chatRest = Mathf.Min(ChatWidth, dockLeft - Margin - Gap);
            chatOpen = Mathf.Min(ChatOpenWidth, dockLeft - Margin - Gap);
            narrowChat = chatRest < ChatMinWidth;
            UiKit.Show(chatTile.Button, narrowChat);
            dockTop = Margin + (wide ? TileHeight : SmallTile) + DockPad * 2;
            hint.anchoredPosition = new Vector2(0, dockTop + Gap);
            FitLog(screen.x);
            FitNotices(screen);
        }

        // recent chat over the chat box, clear of the place card; on narrow screens over the place card instead
        void FitLog(float width)
        {
            float w = Mathf.Min(chatRest, width / 2 - PlaceHalf - Gap - Margin), y = Mathf.Max(Margin + ChatHeight, dockTop) + Gap;
            if (narrowChat || w < ChatMinWidth) { w = Mathf.Min(ChatWidth, width - Margin * 2); y = dockTop + Gap + PlaceHeight + Gap; }
            logLines.Pin(Vector2.zero, new Vector2(Margin, y), new Vector2(w, logLines.sizeDelta.y));
        }

        // toasts and chapter titles go in the lane between the pet card and the minimap, or under the pet card when it's too narrow
        void FitNotices(Vector2 screen)
        {
            int most = screen.y < 800 ? 2 : 3;
            float right = Minimap.ShowsSmall(screen.x) ? Minimap.Width : 0;
            float lane = screen.x - (Margin + Mathf.Max(PetBadge.Width, right) + Gap) * 2;
            if (lane >= MinLane)
            {
                float titleTop = Mathf.Max(screen.y * .2f, Margin + Notices.StackHeight(most) + Gap * 2);
                notices.Fit(0, Margin, Mathf.Min(lane, 760), most, titleTop, Mathf.Min(lane, 960));
                return;
            }
            float top = BelowPetCard + ObjectiveHeight + Gap, room = screen.x - (Margin + (right > 0 ? right + Gap : 0)) * 2;
            notices.Fit(0, top, room, most, top, room);
        }

        // the chat box: a pill left of the dock that widens while you type; on narrow screens it opens over the dock
        void FitChat()
        {
            bool typing = chat.isFocused || chat.text.Length > 0 || Time.unscaledTime - chatOpenedAt < .3f;
            UiKit.Show(send, typing);
            string hintText = typing || Application.isMobilePlatform ? "Say something…" : "Press Enter to chat";
            if (chatHint.text != hintText) chatHint.text = hintText;
            if (narrowChat)
            {
                UiKit.Show(chat, typing);
                chatBox.anchoredPosition = new Vector2(Margin, dockTop + Gap);
                chatBox.sizeDelta = new Vector2(laidOut.x - Margin * 2, ChatHeight);
                return;
            }
            UiKit.Show(chat, true);
            chatBox.anchoredPosition = new Vector2(Margin, Margin);
            float w = typing ? chatOpen : chatRest, glide = GameSettings.ReduceMotion ? 1 : 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14);
            chatBox.sizeDelta = new Vector2(Mathf.Lerp(chatBox.sizeDelta.x, w, glide), ChatHeight);
        }

        // the "where" card sits centred under the minimap (or right under the corner buttons without it)
        void FitWhere()
        {
            bool map = Minimap.ShowsSmall(laidOut.x);
            float half = Mathf.Max(map ? Minimap.Width : 0, where.rect.width) / 2;
            where.anchoredPosition = new Vector2(-Margin - half, -(map ? BelowCorner + Minimap.Width + Gap : BelowCorner));
        }

        // ---- menus ----

        IEnumerable<Sheet> Sheets()
        {
            yield return petCard; yield return emotes; yield return tricks; yield return controls; yield return pause; yield return coins;
        }

        bool AnyMenuOpen() => AnySheetOpen() || (minimap != null && minimap.IsBig);

        bool AnySheetOpen()
        {
            foreach (var s in Sheets()) if (s != null && s.IsOpen) return true;
            return false;
        }

        void OpenPets()
        {
            foreach (var s in Sheets()) if (s != null) s.Close();
            PetsScreen.Open(world.Me);
        }

        // ---- the daily gift ----

        /// <summary>
        /// Arriving in town loads the wallet; once a day, while the gift waits, the coins sheet opens with it on top
        /// (the site greets you with its gift card the same way).
        /// </summary>
        System.Collections.IEnumerator OfferGift()
        {
            var refresh = Wallet.Refresh();
            yield return new WaitUntil(() => refresh.IsCompleted);
            yield return new WaitForSecondsRealtime(1.2f);
            string day = Wallet.State.Day;
            if (!Wallet.Ready || Wallet.State.GiftClaimed || Hidden || UiStack.Any || PlazaInput.Locked) yield break;
            if (PlayerPrefs.GetString(GiftOfferedKey, "") == Settings.AccountId + day) yield break;
            PlayerPrefs.SetString(GiftOfferedKey, Settings.AccountId + day);
            Toggle(coins);
        }

        // ---- photo mode ----

        void SetHidden(bool hide)
        {
            Hidden = hide;
            photoHintAt = Time.unscaledTime;
            foreach (var s in Sheets()) if (s != null) s.Close();
            Sound.Play(hide ? "close" : "open");
        }

        /// <summary>Saves a picture of the town without the HUD to the Photos folder, with a flash and a click.</summary>
        System.Collections.IEnumerator TakePhoto()
        {
            bool wasHidden = Hidden;
            Hidden = true;
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
            Hidden = wasHidden;
            if (!Hidden) layerGroup.alpha = 1;
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
            UiKit.Show(chat, true);
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
            var line = UiKit.Label(logLines, "", UiKit.BodySize, Palette.Ink);
            line.supportRichText = true;
            string colour = villager ? "#2f5fb0" : "#b4521f";
            line.text = $"<color={colour}>{Escape(who)}</color>  {Escape(words)}";
            while (logLines.childCount > ChatLines) DestroyImmediate(logLines.GetChild(0).gameObject);
            lastLineAt = Time.unscaledTime;
        }

        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        // ---- notices ----

        void ShowToast(string text) => notices.Show(text);

        void ShowBanner(string title, string sub) => notices.Title(title, sub);

        /// <summary>Updates the "where" card and the place card.</summary>
        void Refresh()
        {
            if (!this || !placeText || world == null || world.Net == null) return;
            var live = world.Net;
            string area = world.Area ?? world.Map.Name;
            placeText.text = world.Map.Name;

            string detail;
            Color dot = Palette.InkSoft;
            switch (live.State)
            {
                case LiveState.Live:
                    int others = world.PeopleHere();
                    detail = others == 0 ? "Just you" : (others + 1) + " here";
                    if (live.OnThisPc) detail += " · On this PC";
                    else if (live.RoundTripMs > 0) detail += " · " + Mathf.RoundToInt(live.RoundTripMs) + " ms";
                    dot = Palette.Leaf;
                    break;
                case LiveState.Connecting: detail = "Connecting…"; dot = Palette.Amber; break;
                case LiveState.Busy: detail = "Finding a room…"; dot = Palette.Amber; break;
                case LiveState.Reconnecting: detail = "Reconnecting…"; dot = Palette.Amber; break;
                case LiveState.Elsewhere: detail = "Open on another device"; break;
                case LiveState.SentHome: detail = "Taking a short break"; break;
                default: detail = live.OnThisPc ? "On this PC" : Settings.SignedIn ? "Offline" : "Exploring offline"; break;
            }
            detailText.text = area == world.Map.Name ? detail : area + " · " + detail;
            liveDot.color = dot;
            UiKit.Show(playHere, live.State == LiveState.Elsewhere);

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
