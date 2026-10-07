using System.Collections;
using System.Collections.Generic;
using BookBuddies.Local;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The title screen over a slowly drifting Pawtopia: the BookBuddies name, the main menu, and your egg
    /// (or the buddy that hatched from it) sitting on a stack of books. Tap the egg and it wobbles; tap your
    /// buddy and it hops. New players hatch their egg from here. Play opens "Where to?": Continue where you are, or go
    /// somewhere else: Pawtopia online, My world on this PC (just you, or open to friends) or a friend's world (WorldsSheet; the nameplate then
    /// says how big the party there is in a friend's world).
    /// The menu works with the mouse, touch, keys and the gamepad cursor; short windows get a more compact name and menu
    /// so nothing is cut off.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        static readonly Vector2 DriftCentre = new Vector2(40, 33);
        const float CompactHeight = 760;      // shorter screens get the compact name and menu
        const float MenuWidth = 380, MenuGap = 28;
        const float StageHeight = 460;        // your buddy, its books and the nameplate, top to bottom
        const float Recheck = 30;             // seconds between quiet server checks while the title is up
        const float HostCheck = .5f;          // seconds between looks at your visitors while you host

        TownCamera cam;
        System.Action enterTown, watchIntro;
        RectTransform root, logo, menu, where, stage, footer, status;
        CanvasGroup group, menuFade, whereFade;
        Image pet, glow, statusDot, wash, hereIcon;
        Text tagline, logoName, townName, plateName, plateSub, statusText, statusHost, playLabel, hereLine, homeLine, mineLine;
        Button play, switchPet, here, home, mine, visit, recover;
        Sheet start, naming, unreachable, lost, myWorld, joinWorld;
        static bool offeredOffline; // the "can't reach the server" (or "this world") card shows once a launch
        InputField nameField;
        Text nameError, nameNote, hatchLabel;
        bool canHatch, busy, checking, hosting, picking; // picking: "Where to?" is showing
        int party;                   // members in the party of the world you're visiting (0 while unknown)
        bool? online;                // the last server check: null while unknown
        string checkedServer;        // the server that check was for
        float shownAt, lastW, lastH, idleAt, animAt, checkAt, hostAt, swapAt;
        string anim, whereShown;
        Vector2 camFrom;
        float zoomFrom, distanceFrom;

        /// <summary>Shows the title. A notice (like "please sign in again") opens the sign-in card with it.</summary>
        public static TitleScreen Show(TownCamera cam, System.Action enterTown, System.Action watchIntro, string notice = null)
        {
            var canvas = UiKit.MakeCanvas("Title", 40);
            var t = canvas.gameObject.AddComponent<TitleScreen>();
            t.cam = cam;
            t.enterTown = enterTown;
            t.watchIntro = watchIntro;
            t.root = (RectTransform)canvas.transform;
            t.Build();
            t.Refresh();
            t.CheckServer();
            t.CountParty();
            if (notice != null) t.ShowSignIn(notice);
            else VirtualCursor.FocusFirst(t.play);
            return t;
        }

        void Build()
        {
            group = root.gameObject.AddComponent<CanvasGroup>();
            UiKit.Cover(root, "vignette", new Color(.2f, .12f, .05f, .55f), UiKit.Vignette);
            wash = UiKit.Cover(root, "wash", Palette.Cream.WithAlpha(.9f), SideWash());

            // the name
            logo = UiKit.Node("logo", root);
            UiKit.Column(logo, 0, null, TextAnchor.MiddleLeft);
            UiKit.Hug(logo, false, true);
            tagline = UiKit.Label(logo, "A cozy town for readers and their pets", UiKit.BodySize + 2, UiKit.EmberInk, UiKit.Bold);
            logoName = UiKit.Label(logo, "BookBuddies", 112, Palette.Ink, UiKit.Title);
            logoName.horizontalOverflow = HorizontalWrapMode.Overflow;
            logoName.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = Palette.Cream.WithAlpha(.7f);
            townName = UiKit.Label(logo, "Pawtopia", 44, UiKit.EmberInk, UiKit.Title);

            // the main menu: one amber Play, quiet cream for the rest
            menu = UiKit.Node("menu", root);
            UiKit.Column(menu, 12);
            UiKit.Hug(menu, false, true);
            play = MenuButton(UiKit.Primary(menu, "Play", OnPlay, null, 56));
            playLabel = play.GetComponentInChildren<Text>();
            switchPet = MenuButton(UiKit.Secondary(menu, "Switch pet", () => PetsScreen.Open(null, Refresh), "🐾", 56)); // before spawning in
            MenuButton(UiKit.Secondary(menu, "Settings", () => SettingsPanel.Open(() => { if (this) { Refresh(); CheckServer(); } }), "⚙️", 56));
            if (OnPc) MenuButton(UiKit.Secondary(menu, "Quit", Application.Quit, null, 56));
            menuFade = menu.gameObject.AddComponent<CanvasGroup>();
            BuildWhere();

            // your egg or buddy on a stack of books
            stage = UiKit.Node("stage", root);
            glow = UiKit.Cover(stage, "glow", Palette.Amber.WithAlpha(.5f), UiKit.Glow);
            glow.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, 30), new Vector2(520, 520));
            var books = UiKit.Icon(stage, "📚", 150);
            ((RectTransform)books.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -95), new Vector2(150, 150));
            var shade = UiKit.Cover(stage, "shadow", Palette.Ink.WithAlpha(.3f), UiKit.Glow);
            shade.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -32), new Vector2(150, 34));
            var tap = UiKit.Button(stage, "your buddy", Color.clear, null, 0);
            tap.onClick.AddListener(OnPetTapped);
            ((RectTransform)tap.transform).Pin(new Vector2(.5f, .5f), new Vector2(0, -28), new Vector2(210, 250));
            ((RectTransform)tap.transform).pivot = new Vector2(.5f, 0);
            tap.transition = Selectable.Transition.None;
            tap.navigation = new Navigation { mode = Navigation.Mode.None };
            pet = UiKit.Node("pet", tap.transform).Fill().gameObject.AddComponent<Image>();
            pet.preserveAspect = true;
            pet.raycastTarget = false;
            ((RectTransform)pet.transform).pivot = new Vector2(.5f, 0);

            var plate = UiKit.Panel(stage, "nameplate", Palette.Cream).rectTransform;
            plate.Pin(new Vector2(.5f, .5f), new Vector2(0, -196), new Vector2(10, 10));
            UiKit.Column(plate, 0, new RectOffset(22, 22, 8, 10), TextAnchor.MiddleCenter);
            UiKit.Hug(plate);
            UiKit.Shadow(plate, UiKit.CardRadius, 12, 4, .25f);
            plateName = UiKit.Label(plate, "", UiKit.HeadingSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            plateName.horizontalOverflow = HorizontalWrapMode.Overflow;
            plateSub = UiKit.Label(plate, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            plateSub.horizontalOverflow = HorizontalWrapMode.Overflow;

            // the version, and the intro to watch again
            footer = UiKit.Node("footer", root);
            UiKit.Row(footer, 4);
            UiKit.Hug(footer);
            var version = UiKit.Label(footer, "v" + Settings.Version, UiKit.SmallSize, Palette.InkSoft, UiKit.Bold);
            version.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.TextButton(footer, "Watch the intro", null, Color.clear, UiKit.EmberInk, () => Leave(watchIntro), 32);

            // the server: a small pill in the corner, green when connected; tap it to check again
            var pill = UiKit.Button(root, "server", Palette.Cream, CheckServer, 16);
            pill.navigation = new Navigation { mode = Navigation.Mode.None }; // the gamepad stays on the menu
            status = (RectTransform)pill.transform;
            UiKit.Row(status, 8, new RectOffset(14, 16, 7, 8));
            UiKit.Hug(status);
            UiKit.Shadow(status, 16, 10, 3, .2f);
            statusDot = UiKit.Panel(status, "dot", Palette.Amber, 5);
            statusDot.raycastTarget = false;
            UiKit.Size(statusDot, 10, 10);
            statusText = UiKit.Label(status, "", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            statusHost = UiKit.Label(status, "", UiKit.SmallSize, Palette.InkSoft);
            foreach (var t in new[] { statusText, statusHost }) { t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false; }

            BuildStartCards();
            shownAt = Time.unscaledTime;
            camFrom = cam.Focus; zoomFrom = cam.zoom; distanceFrom = cam.distance;
            cam.Directed = true;
            Sound.Music("home");
        }

        static bool OnPc => !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer; // worlds are PC to PC

        // a main menu button floats over the town, so it gets a soft shadow and slightly bigger words
        static Button MenuButton(Button b)
        {
            b.GetComponentInChildren<Text>().fontSize = UiKit.BodySize + 3;
            UiKit.Shadow((RectTransform)b.transform, 28, 12, 4, .22f);
            return b;
        }

        // ---- hatching: pick a name, then the egg hatches ----

        void BuildStartCards()
        {
            start = Card("Hatch your egg", "Your egg is ready. Give it a name tag and it will hatch into a buddy that’s all yours.");
            start.First = UiKit.Primary(start.Card, "Hatch my egg", () => { start.Close(); naming.Open(); FocusName(); }, null, 52);
            UiKit.Secondary(start.Card, "I already have a buddy", () => { start.Close(); SignInOnline(); });
            UiKit.TextButton(start.Card, "Just look around", null, Palette.Cream, Palette.InkSoft, () => { start.Close(); Leave(enterTown); });

            naming = Card("What should we call you?", "This name shows above your buddy in town. Pick a nickname, not your real name.");
            nameField = UiKit.Input(naming.Card, "Your nickname");
            nameField.characterLimit = 20;
            nameField.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) Hatch(); });
            UiKit.Size(nameField, -1, 52);
            nameError = UiKit.Label(naming.Card, "", UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            UiKit.Show(nameError, false);
            nameNote = UiKit.Label(naming.Card, "Your buddy will live on this device for now. Sign in with a recovery code any time to meet readers in town.", UiKit.SmallSize, Palette.InkSoft);
            var go = UiKit.Primary(naming.Card, "Hatch!", Hatch, null, 52);
            hatchLabel = go.GetComponentInChildren<Text>();
            UiKit.Secondary(naming.Card, "Back", () => { naming.Close(); start.Open(); });
            naming.First = nameField;

            unreachable = Card("Can’t reach the server", "The online town isn’t answering right now. You can play offline on this PC instead: coins you find there go to your online wallet once you’re back online.");
            unreachable.First = UiKit.Primary(unreachable.Card, "Play offline", () => { unreachable.Close(); GoTo(Settings.Local, true, null); }, null, 52);
            UiKit.Secondary(unreachable.Card, "Try again", () => { unreachable.Close(); offeredOffline = false; CheckServer(); });
            UiKit.TextButton(unreachable.Card, "Not now", null, Palette.Cream, Palette.InkSoft, unreachable.Close);

            lost = Card("Can’t reach this world", "Its host may have closed it, or you’re not on the same network any more. Your buddy can head home any time.");
            lost.First = UiKit.Primary(lost.Card, "Leave this world", () => { lost.Close(); GoTo(Settings.OnlineServer, false, null); }, null, 52);
            UiKit.Secondary(lost.Card, "Try again", () => { lost.Close(); offeredOffline = false; CheckServer(); });
            UiKit.TextButton(lost.Card, "Not now", null, Palette.Cream, Palette.InkSoft, lost.Close);
        }

        Sheet Card(string title, string body)
        {
            var s = Sheet.Create(root, title, new Vector2(.5f, .5f), Vector2.zero, 480, title);
            s.Dim(.35f);
            UiKit.Label(s.Card, body, UiKit.BodySize, Palette.InkSoft);
            UiKit.Spacer(s.Card, 4, 0);
            return s;
        }

        // with a keyboard the name field is ready to type in; on a gamepad the cursor goes to it instead
        void FocusName()
        {
            UiKit.Show(nameNote, !canHatch);
            if (Application.isMobilePlatform || PlazaInput.UsingGamepad) return;
            nameField.Select();
            nameField.ActivateInputField();
        }

        async void Hatch()
        {
            if (busy) return;
            string name = nameField.text.Trim();
            if (name.Length < 2) { NameError("Pick a nickname with at least 2 letters."); return; }
            busy = true;
            hatchLabel.text = "Warming up…";
            UiKit.Show(nameError, false);
            int hue = Buddy.EggHue;
            string look = Buddy.NewLook(hue), recovery = null;
            if (canHatch)
            {
                try
                {
                    var account = await BBApi.Register(name, look);
                    name = account.Str("name", name);
                    look = account.Str("pet", look);
                    if (!Settings.IsLocal) recovery = account.Str("recovery"); // offline there's nothing to recover on another device
                }
                catch (BBApi.ApiError e) when (e.Status > 0)
                {
                    StopWaiting(e.Message);
                    return;
                }
                catch (System.Exception)
                {
                    canHatch = false;
                    StopWaiting("Can’t reach the server right now. Tap Hatch! again to hatch your egg on this device.");
                    return;
                }
            }
            if (!this) return;
            Buddy.Save(name, look);
            naming.Close();
            StopWaiting(null);
            pet.enabled = false;
            HatchScene.Play(hue, look, name, recovery, () => { Refresh(); Leave(enterTown); });
        }

        void StopWaiting(string error)
        {
            if (!this) return;
            busy = false;
            hatchLabel.text = "Hatch!";
            UiKit.Show(nameNote, !canHatch);
            if (error != null) NameError(error);
        }

        void NameError(string message)
        {
            nameError.text = message;
            UiKit.Show(nameError, true);
        }

        void ShowSignIn(string message)
        {
            gameObject.SetActive(false);
            SignInScreen.Show(message, signedIn =>
            {
                gameObject.SetActive(true);
                Refresh();
                if (signedIn) { _ = CoinBank.Refresh(); Leave(enterTown); } // a new sign-in: its coins come up to date
                else VirtualCursor.FocusFirst(play);
            });
        }

        // ---- the main menu ----

        // Play: hatch your egg first, or choose where to play
        void OnPlay()
        {
            if (!Buddy.Hatched && !Settings.SignedIn) { start.Open(); return; }
            ShowWhere(true);
        }

        // in you go, wherever you are now
        void Enter()
        {
            if (!Buddy.Hatched && !Settings.SignedIn) { ShowWhere(false); start.Open(); return; }
            Leave(enterTown);
        }

        // ---- "Where to?": Continue where you are, or Pawtopia, My world or a friend's world ----

        void BuildWhere()
        {
            where = UiKit.Node("where to", root);
            UiKit.Column(where, 10);
            UiKit.Hug(where, false, true);
            whereFade = where.gameObject.AddComponent<CanvasGroup>();

            var head = UiKit.Node("header", where);
            UiKit.Row(head, 12);
            var back = UiKit.Secondary(head, "‹", () => ShowWhere(false), null, 48);
            back.GetComponentInChildren<Text>().fontSize = UiKit.HeadingSize + 4;
            UiKit.Shadow((RectTransform)back.transform, 24, 12, 4, .22f);
            UiKit.Size(back, 48, 48);
            var title = UiKit.Label(head, "Where to?", UiKit.TitleSize + 4, Palette.Ink, UiKit.Title);
            title.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = Palette.Cream.WithAlpha(.7f);
            UiKit.Size(title, -1, 52, 1);

            here = Place(Palette.Amber, "🌍", "Continue", out hereIcon, out hereLine, Enter);
            var or = UiKit.Label(where, "Or go somewhere else", UiKit.SmallSize + 1, UiKit.EmberInk, UiKit.Bold);
            or.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = Palette.Cream.WithAlpha(.7f);
            UiKit.Size(or, -1, 26);
            home = Place(Palette.Cream, "🌍", "Pawtopia", out _, out homeLine, () => GoTo(Settings.OnlineServer, true, homeLine));
            mine = Place(Palette.Cream, "🏡", "My world", out _, out mineLine, OpenMyWorld);
            if (OnPc)
            {
                visit = Place(Palette.Cream, "🤝", "Join a friend", out _, out var visitLine, OpenJoin);
                visitLine.text = "Visit a friend’s world with their join code.";
            }
            recover = UiKit.TextButton(where, "I have a recovery code", null, Color.clear, UiKit.EmberInk, () => { ShowWhere(false); SignInOnline(); }, 40);
            where.gameObject.SetActive(false);
        }

        // one place to play, as one big button: its icon, its name, a line about it, and an arrow
        Button Place(Color color, string emoji, string name, out Image icon, out Text line, System.Action onClick)
        {
            bool main = color == Palette.Amber;
            var b = UiKit.Button(where, name, color, onClick, 18);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 14, new RectOffset(14, 16, 11, 12));
            UiKit.Shadow(r, 18, 12, 4, .22f);
            if (!main) UiKit.Outline(b, Palette.Ink.WithAlpha(.16f), 18);
            icon = UiKit.Icon(r, emoji, main ? 48 : 40);
            var words = UiKit.Node("words", r);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, name, UiKit.BodySize + (main ? 5 : 3), Palette.Ink, UiKit.Bold);
            line = UiKit.Label(words, "", UiKit.SmallSize + 1, main ? Palette.Ink.WithAlpha(.75f) : Palette.InkSoft);
            UiKit.Size(UiKit.Label(r, "›", UiKit.HeadingSize + 6, Palette.Ink.WithAlpha(main ? .6f : .4f), UiKit.Bold, TextAnchor.MiddleCenter), 14, -1);
            return b;
        }

        void ShowWhere(bool on)
        {
            if (picking == on) return;
            picking = on;
            swapAt = Time.unscaledTime;
            UiKit.Show(menu, !on);
            UiKit.Show(where, on);
            lastW = 0; // fit the column that's showing
            if (on)
            {
                RefreshWhere();
                UiStack.Push(where, () => ShowWhere(false));
                Sound.Play("open");
                VirtualCursor.FocusFirst(here);
            }
            else
            {
                UiStack.Remove(where);
                VirtualCursor.FocusFirst(play);
            }
        }

        // Continue says where you are; the places you're not in are the other choices
        void RefreshWhere()
        {
            bool open = LocalHost.Running, local = Settings.IsLocal, world = Settings.IsWorld;
            UiKit.SetIcon(hereIcon, local ? "🏡" : world ? "🤝" : "🌍");
            hereLine.text = open ? "My world · open, " + WorldsSheet.VisitorCount(LocalHost.Visitors)
                : local ? "My world · just you"
                : world ? Settings.WorldName + " · visiting"
                : "Pawtopia · online";
            UiKit.Show(home, local || world);
            if (!busy) homeLine.text = world ? "Leave this world and head home." : "The online town, shared with readers everywhere.";
            UiKit.Show(mine, OnPc || !local);
            if (!busy) mineLine.text = open ? "Open to friends. See your join code, or close it."
                : local ? "Open it to friends, or change who can join."
                : OnPc ? "Your world on this PC. Play alone, or open it to friends." : "Your world on this device. No network needed.";
            UiKit.Show(recover, Settings.TokenFor(Settings.OnlineServer).Length == 0);
            string shown = hereLine.text + homeLine.text + mineLine.text + home.gameObject.activeSelf + mine.gameObject.activeSelf + recover.gameObject.activeSelf;
            if (shown != whereShown) { whereShown = shown; lastW = 0; } // its height may have changed
        }

        // go online, or offline to your world, bringing your buddy and pets (the first time), then maybe in you go
        async void GoTo(string server, bool thenPlay, Text say)
        {
            if (busy) return;
            busy = true;
            if (say != null) say.text = "Getting ready…";
            await BBApi.UseServer(server);
            if (!this) return;
            busy = false;
            Refresh();
            CheckServer();
            if (thenPlay) Enter();
        }

        // My world: on a PC, a card to play alone or open your world to friends; on a phone, straight in
        void OpenMyWorld()
        {
            if (!OnPc) { GoTo(Settings.Local, true, mineLine); return; }
            if (myWorld == null) myWorld = Worlds(true);
            myWorld.Open();
        }

        void OpenJoin()
        {
            if (joinWorld == null) joinWorld = Worlds(false);
            joinWorld.Open();
        }

        Sheet Worlds(bool host)
        {
            var s = WorldsSheet.Create(root, host, Joined, () => { Refresh(); CheckServer(); });
            s.Closed += () => { if (this && picking) VirtualCursor.FocusFirst(here); };
            return s;
        }

        // into your world or a friend's from its card: show it, then walk in
        void Joined()
        {
            Refresh();
            CheckServer();
            CountParty();
            if (!gameObject.activeSelf) return;
            ShowWhere(false);
            if (!UiStack.Any) Enter();
        }

        // recovery codes belong to online accounts, so signing in offline or in a friend's world goes online first
        async void SignInOnline()
        {
            if (Settings.IsLocal || Settings.IsWorld)
            {
                if (busy) return;
                busy = true;
                await BBApi.UseServer(Settings.OnlineServer);
                if (!this) return;
                busy = false;
                Refresh();
                CheckServer();
            }
            ShowSignIn(null);
        }

        /// <summary>The title fades out, then "then" runs (into town, or the intro).</summary>
        void Leave(System.Action then)
        {
            if (busy) return;
            busy = true;
            StartCoroutine(FadeOut(then));
        }

        IEnumerator FadeOut(System.Action then)
        {
            group.interactable = false;
            for (float t = 0; t < .5f; t += Time.unscaledDeltaTime) { group.alpha = 1 - UiKit.Ease(t / .5f); yield return null; }
            UiStack.Remove(where);
            Destroy(gameObject);
            then();
        }

        /// <summary>Shows your buddy or egg, and the right words on the Play button.</summary>
        void Refresh()
        {
            pet.enabled = true;
            pet.sprite = PetSprites.For(Buddy.ShownLook);
            bool hatched = Buddy.Hatched;
            plateName.text = hatched ? (Buddy.Name.Length > 0 ? Buddy.Name : "Your buddy") : "Your egg";
            plateSub.text = Subtitle(hatched);
            playLabel.text = hatched || Settings.SignedIn ? "Play" : "Hatch your egg";
            UiKit.Show(switchPet, hatched);
            RefreshWhere();
            lastW = 0; // the menu may have changed length
        }

        // under your buddy's name: hosting (with your visitors), visiting a friend's world (and its party), offline or online
        string Subtitle(bool hatched) =>
            LocalHost.Running ? "Hosting your world · " + WorldsSheet.VisitorCount(LocalHost.Visitors)
            : !hatched ? "It’s warm. Give it a tap!"
            : Settings.IsWorld ? "Visiting " + Settings.WorldName + (party > 0 ? " · party of " + party : "")
            : Settings.IsLocal ? "In your world on this PC" : Settings.SignedIn ? "Ready for Pawtopia" : "Exploring on this device";

        // in a friend's world, how many are in its party, asked once as the title shows (no answer: the nameplate just says whose world)
        async void CountParty()
        {
            party = 0;
            if (!Settings.IsWorld || !Settings.SignedIn) return;
            string server = Settings.Server;
            Dictionary<string, object> reply;
            try { reply = await BBApi.Party(); }
            catch (BBApi.ApiError) { return; }
            if (!this || Settings.Server != server) return;
            party = reply.Truthy("party") ? reply.Arr("members").Count : 0;
            plateSub.text = Subtitle(Buddy.Hatched);
        }

        // while you host, the nameplate keeps count of your visitors, and the pill notices the world opening or closing
        void ShowHosting()
        {
            hostAt = Time.unscaledTime + HostCheck;
            plateSub.text = Subtitle(Buddy.Hatched);
            if (picking) RefreshWhere(); // Continue counts your visitors too
            if (LocalHost.Running == hosting) return;
            hosting = LocalHost.Running;
            CheckServer();
        }

        /// <summary>Asks the server if it's there, and shows the answer on the pill. Runs again every half minute.</summary>
        async void CheckServer()
        {
            if (checking) { checkAt = 0; return; } // once this check is back, check again
            checkAt = Time.unscaledTime + Recheck;
            if (Settings.IsLocal)
            {
                canHatch = true;
                online = null;
                if (LocalHost.Running) ShowStatus(Palette.Leaf, "Hosting", LocalHost.Code ?? "your world is open");
                else ShowStatus(Palette.Sky, "Offline", "playing on this PC");
                return;
            }
            string server = Settings.Server, where = Settings.IsWorld ? Settings.WorldName : Settings.ServerHost;
            if (server != checkedServer) online = null; // a new server starts from "Connecting…"
            checkedServer = server;
            if (online != true) ShowStatus(Palette.Amber, "Connecting…", where); // a quiet recheck keeps "Connected" up
            checking = true;
            bool ok;
            try { canHatch = await BBApi.CanHatch(); ok = true; }
            catch (System.Exception) { canHatch = false; ok = false; }
            checking = false;
            if (!this) return;
            if (Settings.Server != server) { checkAt = 0; return; } // switched servers meanwhile
            if (ok && online != true && server == Settings.OnlineServer) _ = CoinBank.Refresh(); // green: your coins come up to date
            online = ok;
            if (ok) ShowStatus(Palette.Leaf, "Connected", where);
            else
            {
                ShowStatus(Palette.Rose, "Can’t connect", "tap to try again");
                OfferOffline();
            }
        }

        void ShowStatus(Color dot, string text, string host)
        {
            statusDot.color = dot;
            statusText.text = text;
            statusHost.text = host;
        }

        // once a launch, when nothing else is on screen: play offline instead? (or, in a friend's world, head home?)
        void OfferOffline()
        {
            if (offeredOffline || busy || !gameObject.activeSelf || UiStack.Any) return;
            offeredOffline = true;
            (Settings.IsWorld ? lost : unreachable).Open();
        }

        // ---- every frame ----

        void Update()
        {
            PlazaInput.MenuOpen = true; // the gamepad drives the menu with the cursor
            if (!Mathf.Approximately(root.rect.width, lastW) || !Mathf.Approximately(root.rect.height, lastH)) Fit();
            Drift();
            AnimatePet();
            if (checking && online != true) statusDot.color = Palette.Amber.WithAlpha(.4f + .6f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1)); // a soft pulse while connecting
            else if (!checking && Time.unscaledTime >= checkAt) CheckServer();
            if (Time.unscaledTime >= hostAt) ShowHosting();
            (picking ? whereFade : menuFade).alpha = UiKit.EaseOut((Time.unscaledTime - swapAt) / .2f); // the column that's showing fades in
        }

        // the camera floats slowly around the town square
        void Drift()
        {
            float t = Time.unscaledTime - shownAt, blend = UiKit.Ease(t / 3f);
            float zoom = GameSettings.ReduceMotion ? 0 : 1;
            var target = DriftCentre + new Vector2(Mathf.Sin(t * .05f) * 7, Mathf.Cos(t * .037f) * 4) * zoom;
            cam.petBelowCentre = Mathf.Lerp(cam.petBelowCentre, 0, blend);
            cam.Focus = Vector2.Lerp(camFrom, target, blend);
            cam.zoom = Mathf.Lerp(zoomFrom, 2.1f, blend);
            cam.distance = Mathf.Lerp(distanceFrom, 20, blend);
            cam.Place();
        }

        void AnimatePet()
        {
            float now = Time.unscaledTime, k = (now - animAt) / .9f;
            var r = (RectTransform)pet.transform;
            float breathe = Mathf.Sin(now * 2.2f) * .02f;
            float y = 0, rot = 0, sx = 1 - breathe, sy = 1 + breathe;
            if (anim != null && k < 1)
            {
                if (anim == "hop") { y = Mathf.Sin(k * Mathf.PI) * 46; sy += k < .15f ? -.1f : .05f; }
                else if (anim == "wobble") rot = Mathf.Sin(k * Mathf.PI * 6) * 12 * (1 - k);
                else if (anim == "spin") sx = Mathf.Cos(k * Mathf.PI * 4);
            }
            else anim = null;
            r.anchoredPosition = new Vector2(0, y);
            r.localRotation = Quaternion.Euler(0, 0, rot);
            r.localScale = new Vector3(sx, sy, 1);
            glow.color = Palette.Amber.WithAlpha(.42f + Mathf.Sin(now * 1.3f) * .08f);

            if (anim == null && now > idleAt)
            {
                idleAt = now + 2.5f + Random.value * 3;
                Play(Buddy.Hatched ? (Random.value < .6f ? "hop" : "spin") : "wobble");
            }
        }

        void Play(string kind) { anim = kind; animAt = Time.unscaledTime; }

        void OnPetTapped()
        {
            if (Buddy.Hatched) { Play("hop"); Sound.Play(Random.value < .5f ? "boop" : "happy"); Float("❤️"); }
            else { Play("wobble"); Sound.Play("crack"); Float("✨"); }
        }

        void Float(string emoji)
        {
            var icon = UiKit.Icon(stage, emoji, 40);
            ((RectTransform)icon.transform).Pin(new Vector2(.5f, .5f), new Vector2(Random.Range(-40f, 40f), 140), new Vector2(40, 40));
            StartCoroutine(Rise(icon));
        }

        static IEnumerator Rise(Image icon)
        {
            var r = (RectTransform)icon.transform;
            Vector2 from = r.anchoredPosition;
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                r.anchoredPosition = from + new Vector2(0, UiKit.EaseOut(t / 1.2f) * 70);
                icon.color = new Color(1, 1, 1, t < .9f ? 1 : (1.2f - t) / .3f);
                yield return null;
            }
            Destroy(icon.gameObject);
        }

        // ---- layout: name and menu on the left, buddy on the right; stacked on tall screens ----

        void Fit()
        {
            lastW = root.rect.width; lastH = root.rect.height;
            bool portrait = lastW / Mathf.Max(1, lastH) < 1.1f;
            bool compact = lastH < CompactHeight;
            wash.enabled = !portrait;

            // the name and the menu, a little smaller on short screens
            float nameWidth = portrait ? lastW - 32 : Mathf.Min(760, lastW * .5f);
            logoName.fontSize = (int)Mathf.Min(compact ? 80 : 112, nameWidth / 5.6f);
            UiKit.Size(logoName, -1, logoName.fontSize * 1.14f);
            townName.fontSize = compact ? 34 : 44;
            UiKit.Show(tagline, !compact);
            float buttonHeight = compact ? 48 : 56;
            int buttons = 0;
            foreach (Transform b in menu)
                if (b.gameObject.activeSelf && b.TryGetComponent<Button>(out _)) { UiKit.Size(b.GetComponent<Button>(), -1, buttonHeight); buttons++; }
            menu.GetComponent<VerticalLayoutGroup>().spacing = compact ? 8 : 12;
            float menuHeight = buttons * buttonHeight + (buttons - 1) * (compact ? 8 : 12);
            if (picking) // "Where to?" is as tall as its cards are at this width
            {
                where.GetComponent<VerticalLayoutGroup>().spacing = compact ? 8 : 10;
                where.sizeDelta = new Vector2(portrait ? Mathf.Min(MenuWidth, lastW - 40) : MenuWidth, where.sizeDelta.y);
                LayoutRebuilder.ForceRebuildLayoutImmediate(where);
                menuHeight = LayoutUtility.GetPreferredHeight(where);
            }
            float logoHeight = (compact ? 0 : 30) + logoName.fontSize * 1.14f + townName.fontSize * 1.2f;

            foreach (var t in logo.GetComponentsInChildren<Text>()) t.alignment = portrait ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            logo.GetComponent<VerticalLayoutGroup>().childAlignment = portrait ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            stage.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 300));
            if (portrait) FitTall(logoHeight, menuHeight);
            else FitWide(logoHeight, menuHeight);
        }

        // name at the top, buddy in the middle, menu at the bottom
        void FitTall(float logoHeight, float menuHeight)
        {
            status.Pin(new Vector2(.5f, 1), new Vector2(0, -16), status.sizeDelta);
            logo.Pin(new Vector2(.5f, 1), new Vector2(0, -68), new Vector2(lastW - 32, 10));
            logo.localScale = Vector3.one;
            menu.Pin(new Vector2(.5f, 0), new Vector2(0, 56), new Vector2(Mathf.Min(MenuWidth, lastW - 40), 10));
            menu.localScale = Vector3.one;
            PinWhere();
            footer.Pin(new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(10, 22));
            float top = lastH - 68 - logoHeight, bottom = 56 + menuHeight;
            stage.anchorMin = stage.anchorMax = new Vector2(.5f, 0);
            stage.anchoredPosition = new Vector2(0, (top + bottom) / 2);
            stage.localScale = Vector3.one * Mathf.Clamp((top - bottom) / StageHeight, .5f, 1);
        }

        // name and menu down the left (scaled down together if the window is very short), buddy on the right
        void FitWide(float logoHeight, float menuHeight)
        {
            float left = Mathf.Max(64, lastW * .07f);
            float total = logoHeight + MenuGap + menuHeight;
            float k = Mathf.Min(1, (lastH - 112) / total);
            float top = total * k / 2 + 16;
            logo.Pin(new Vector2(0, .5f), new Vector2(left, top), new Vector2(Mathf.Min(760, lastW * .5f), 10));
            logo.pivot = new Vector2(0, 1);
            logo.localScale = Vector3.one * k;
            menu.Pin(new Vector2(0, .5f), new Vector2(left, top - (logoHeight + MenuGap) * k), new Vector2(MenuWidth, 10));
            menu.pivot = new Vector2(0, 1);
            menu.localScale = Vector3.one * k;
            PinWhere();
            footer.Pin(Vector2.zero, new Vector2(left, 18), new Vector2(10, 22));
            status.Pin(Vector2.one, new Vector2(-24, -20), status.sizeDelta);
            stage.anchorMin = stage.anchorMax = new Vector2(.7f, .5f);
            stage.anchoredPosition = new Vector2(0, 10);
            stage.localScale = Vector3.one * Mathf.Min(1.25f, (lastH - 140) / StageHeight);
        }

        // "Where to?" sits just where the main menu does
        void PinWhere()
        {
            where.anchorMin = menu.anchorMin; where.anchorMax = menu.anchorMax; where.pivot = menu.pivot;
            where.anchoredPosition = menu.anchoredPosition;
            where.sizeDelta = new Vector2(menu.sizeDelta.x, where.sizeDelta.y);
            where.localScale = menu.localScale;
        }

        /// <summary>Cream on the left fading to clear, so the name stays easy to read over the town.</summary>
        static Sprite SideWash()
        {
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Pow(1 - Mathf.Clamp01(x / 40f), 1.6f)));
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(.5f, .5f), 1);
        }

        void OnDisable() => PlazaInput.MenuOpen = false;
    }
}
