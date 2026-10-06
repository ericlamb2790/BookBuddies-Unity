using System.Collections;
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
    /// buddy and it hops. New players hatch their egg from here. The menu works with the mouse, touch, keys and the
    /// gamepad cursor; short windows get a more compact name and menu so nothing is cut off.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        static readonly Vector2 DriftCentre = new Vector2(40, 33);
        const float CompactHeight = 760;      // shorter screens get the compact name and menu
        const float MenuWidth = 380, MenuGap = 28;
        const float StageHeight = 460;        // your buddy, its books and the nameplate, top to bottom

        TownCamera cam;
        System.Action enterTown, watchIntro;
        RectTransform root, logo, menu, stage, footer;
        CanvasGroup group;
        Image pet, glow, serverDot, wash;
        Text tagline, logoName, townName, plateName, plateSub, serverText, playLabel;
        Button play, signIn;
        Sheet start, naming;
        InputField nameField;
        Text nameError, nameNote, hatchLabel;
        bool canHatch, busy;
        float shownAt, lastW, lastH, idleAt, animAt;
        string anim;
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
            signIn = MenuButton(UiKit.Secondary(menu, "I have a recovery code", () => ShowSignIn(null), null, 56));
            MenuButton(UiKit.Secondary(menu, "Settings", () => SettingsPanel.Open(() => { Refresh(); CheckServer(); }), "⚙️", 56));
            MenuButton(UiKit.Secondary(menu, "Watch the intro", () => Leave(watchIntro), null, 56));
            if (!Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer)
                MenuButton(UiKit.Secondary(menu, "Quit", Application.Quit, null, 56));

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

            // version and server
            footer = UiKit.Node("footer", root);
            UiKit.Row(footer, 8);
            UiKit.Hug(footer);
            var version = UiKit.Label(footer, "v" + Settings.Version, UiKit.SmallSize, Palette.InkSoft, UiKit.Bold);
            version.horizontalOverflow = HorizontalWrapMode.Overflow;
            serverDot = UiKit.Panel(footer, "status", Palette.Amber, 5);
            UiKit.Size(serverDot, 10, 10);
            serverText = UiKit.Label(footer, "", UiKit.SmallSize, Palette.InkSoft);
            serverText.horizontalOverflow = HorizontalWrapMode.Overflow;

            BuildStartCards();
            shownAt = Time.unscaledTime;
            camFrom = cam.Focus; zoomFrom = cam.zoom; distanceFrom = cam.distance;
            cam.Directed = true;
            Sound.Music("home");
        }

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
            UiKit.Secondary(start.Card, "I already have a buddy", () => { start.Close(); ShowSignIn(null); });
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
                    recovery = account.Str("recovery");
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
                if (signedIn) Leave(enterTown);
                else VirtualCursor.FocusFirst(play);
            });
        }

        // ---- the main menu ----

        void OnPlay()
        {
            if (!Buddy.Hatched && !Settings.SignedIn) { start.Open(); return; }
            Leave(enterTown);
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
            plateSub.text = hatched ? (Settings.SignedIn ? "Ready for Pawtopia" : "Exploring on this device") : "It’s warm. Give it a tap!";
            playLabel.text = hatched || Settings.SignedIn ? "Play" : "Hatch your egg";
            UiKit.Show(signIn, !Settings.SignedIn);
            lastW = 0; // the menu may have changed length
        }

        async void CheckServer()
        {
            serverText.text = Settings.ServerHost + " · checking…";
            serverDot.color = Palette.Amber;
            bool online;
            try { canHatch = await BBApi.CanHatch(); online = true; }
            catch (System.Exception) { canHatch = false; online = false; }
            if (!this) return;
            serverText.text = Settings.ServerHost + (online ? " · online" : " · can’t reach the server");
            serverDot.color = online ? Palette.Leaf : Palette.Rose;
        }

        // ---- every frame ----

        void Update()
        {
            PlazaInput.MenuOpen = true; // the gamepad drives the menu with the cursor
            if (!Mathf.Approximately(root.rect.width, lastW) || !Mathf.Approximately(root.rect.height, lastH)) Fit();
            Drift();
            AnimatePet();
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
            logo.Pin(new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(lastW - 32, 10));
            logo.localScale = Vector3.one;
            menu.Pin(new Vector2(.5f, 0), new Vector2(0, 56), new Vector2(Mathf.Min(MenuWidth, lastW - 40), 10));
            menu.localScale = Vector3.one;
            footer.Pin(new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(10, 22));
            float top = lastH - 40 - logoHeight, bottom = 56 + menuHeight;
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
            footer.Pin(Vector2.zero, new Vector2(left, 18), new Vector2(10, 22));
            stage.anchorMin = stage.anchorMax = new Vector2(.7f, .5f);
            stage.anchoredPosition = new Vector2(0, 10);
            stage.localScale = Vector3.one * Mathf.Min(1.25f, (lastH - 140) / StageHeight);
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
