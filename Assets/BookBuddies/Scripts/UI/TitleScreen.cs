using System.Collections;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The title screen over a slowly drifting Pawtopia: the BookBuddies name, the main menu, and your egg
    /// (or the buddy that hatched from it) sitting on a stack of books. Tap the egg and it wobbles; tap your
    /// buddy and it hops. New players hatch their egg from here.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        static readonly Vector2 DriftCentre = new Vector2(40, 33);

        TownCamera cam;
        System.Action enterTown, watchIntro;
        Canvas canvas;
        RectTransform root, logo, menu, stage, footer;
        CanvasGroup group;
        Image pet, glow, serverDot, wash;
        Text logoName, plateName, plateSub, serverText, playLabel;
        Button play, signIn;
        Sheet start, naming;
        InputField nameField;
        Text nameError, nameNote, hatchLabel;
        bool canHatch, busy, portrait;
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
            t.canvas = canvas;
            t.root = (RectTransform)canvas.transform;
            t.Build();
            t.Refresh();
            t.CheckServer();
            if (notice != null) t.ShowSignIn(notice);
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
            UiKit.Label(logo, "A cozy town for readers and their pets", 18, Palette.Ember, UiKit.Bold);
            logoName = UiKit.Label(logo, "BookBuddies", 92, Palette.Ink, UiKit.Title);
            logoName.horizontalOverflow = HorizontalWrapMode.Overflow;
            logoName.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = Palette.Cream.WithAlpha(.7f);
            UiKit.Size(logoName, -1, 104);
            UiKit.Label(logo, "Pawtopia", 40, Palette.Ember, UiKit.Title);

            // the main menu
            menu = UiKit.Node("menu", root);
            UiKit.Column(menu, 10);
            play = MenuButton("Play", Palette.Amber, OnPlay);
            playLabel = play.GetComponentInChildren<Text>();
            signIn = MenuButton("I have a recovery code", Palette.Cream, () => ShowSignIn(null));
            MenuButton("Settings", Palette.Cream, () => SettingsPanel.Open(() => { Refresh(); CheckServer(); }));
            MenuButton("Watch the intro", Palette.Cream, () => Leave(watchIntro));
            if (!Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer) MenuButton("Quit", Palette.Cream, Application.Quit);
            UiKit.Hug(menu, false, true);

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

            var plate = UiKit.Panel(stage, "nameplate", Palette.Cream, 18).rectTransform;
            plate.Pin(new Vector2(.5f, .5f), new Vector2(0, -196), new Vector2(10, 10));
            UiKit.Column(plate, 0, new RectOffset(20, 20, 8, 10), TextAnchor.MiddleCenter);
            UiKit.Hug(plate);
            UiKit.Shadow(plate, 18, 12, 4, .25f);
            plateName = UiKit.Label(plate, "", 20, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            plateName.horizontalOverflow = HorizontalWrapMode.Overflow;
            plateSub = UiKit.Label(plate, "", 14, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            plateSub.horizontalOverflow = HorizontalWrapMode.Overflow;

            // version and server
            footer = UiKit.Node("footer", root);
            UiKit.Row(footer, 8);
            UiKit.Hug(footer);
            var version = UiKit.Label(footer, "v" + Settings.Version, 14, Palette.InkSoft, UiKit.Bold);
            version.horizontalOverflow = HorizontalWrapMode.Overflow;
            serverDot = UiKit.Panel(footer, "status", Palette.Amber, 4);
            UiKit.Size(serverDot, 8, 8);
            serverText = UiKit.Label(footer, "", 14, Palette.InkSoft);
            serverText.horizontalOverflow = HorizontalWrapMode.Overflow;

            BuildStartCards();
            shownAt = Time.unscaledTime;
            camFrom = cam.Focus; zoomFrom = cam.zoom; distanceFrom = cam.distance;
            cam.Directed = true;
            Sound.Music("home");
        }

        Button MenuButton(string label, Color colour, System.Action onClick)
        {
            var b = UiKit.TextButton(menu, label, null, colour, Palette.Ink, onClick, 50);
            b.GetComponentInChildren<Text>().fontSize = 20;
            UiKit.Shadow((RectTransform)b.transform, 25, 12, 4, .22f);
            return b;
        }

        // ---- hatching: pick a name, then the egg hatches ----

        void BuildStartCards()
        {
            start = Card("Hatch your egg", "Your egg is ready! Give it a name tag and it will hatch into a buddy that's all yours.");
            var hatch = UiKit.TextButton(start.Card, "Hatch my egg", null, Palette.Amber, Palette.Ink, () => { start.Close(); naming.Open(!PlazaInput.UsingPointer); FocusName(); }, 48);
            start.First = hatch;
            UiKit.TextButton(start.Card, "I already have a buddy", null, Palette.Paper, Palette.Ink, () => { start.Close(); ShowSignIn(null); }, 44);
            UiKit.TextButton(start.Card, "Just look around", null, Palette.Cream, Palette.InkSoft, () => { start.Close(); Leave(enterTown); }, 40);

            naming = Card("What should we call you?", "This name shows above your buddy in town. Pick a nickname, not your real name.");
            nameField = UiKit.Input(naming.Card, "Your nickname");
            nameField.characterLimit = 20;
            nameField.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) Hatch(); });
            UiKit.Size(nameField, -1, 48);
            nameError = UiKit.Label(naming.Card, "", 14, Palette.Rose, UiKit.Bold);
            UiKit.Show(nameError, false);
            nameNote = UiKit.Label(naming.Card, "Your buddy will live on this device for now. Sign in with a recovery code any time to meet readers in town.", 13, Palette.InkSoft);
            var go = UiKit.TextButton(naming.Card, "Hatch!", null, Palette.Amber, Palette.Ink, Hatch, 48);
            hatchLabel = go.GetComponentInChildren<Text>();
            UiKit.TextButton(naming.Card, "Back", null, Palette.Paper, Palette.Ink, () => { naming.Close(); start.Open(!PlazaInput.UsingPointer); }, 40);
            naming.First = nameField;
        }

        Sheet Card(string title, string body)
        {
            var s = Sheet.Create(root, title, new Vector2(.5f, .5f), Vector2.zero, 400);
            s.Card.pivot = new Vector2(.5f, .5f);
            var column = s.Card.GetComponent<VerticalLayoutGroup>();
            column.spacing = 12;
            column.padding = new RectOffset(26, 26, 24, 24);
            UiKit.Label(s.Card, title, 28, Palette.Ink, UiKit.Title);
            UiKit.Label(s.Card, body, 15, Palette.InkSoft);
            return s;
        }

        void FocusName()
        {
            UiKit.Show(nameNote, !canHatch);
            if (Application.isMobilePlatform) return;
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
            });
        }

        // ---- the main menu ----

        void OnPlay()
        {
            if (!Buddy.Hatched && !Settings.SignedIn) { start.Open(!PlazaInput.UsingPointer); return; }
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
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            if (!Mathf.Approximately(root.rect.width, lastW) || !Mathf.Approximately(root.rect.height, lastH)) Fit();
            Drift();
            AnimatePet();
            Keys();
        }

        void Keys()
        {
            PlazaInput.Typing = nameField.isFocused;
            if (PlazaInput.Down(PlazaAction.Back))
            {
                if (naming.IsOpen) naming.Close();
                else if (start.IsOpen) start.Close();
                return;
            }
            // a stick or arrow press with nothing highlighted picks the first menu button
            var es = EventSystem.current;
            bool sheetOpen = start.IsOpen || naming.IsOpen;
            if (es != null && es.currentSelectedGameObject == null && !sheetOpen && PlazaInput.Move() != Vector2.zero)
                es.SetSelectedGameObject(play.gameObject);
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
            portrait = lastW / Mathf.Max(1, lastH) < 1.1f;
            wash.enabled = !portrait;
            logoName.fontSize = (int)Mathf.Min(92, (lastW - 32) / 5.6f);
            if (portrait)
            {
                logo.Pin(new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(lastW - 32, 190));
                logo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                foreach (var t in logo.GetComponentsInChildren<Text>()) t.alignment = TextAnchor.MiddleCenter;
                stage.Pin(new Vector2(.5f, .55f), Vector2.zero, new Vector2(300, 300));
                stage.localScale = Vector3.one * .8f;
                menu.Pin(new Vector2(.5f, 0), new Vector2(0, 56), new Vector2(Mathf.Min(320, lastW - 40), 10));
                menu.pivot = new Vector2(.5f, 0);
                footer.Pin(new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(10, 22));
            }
            else
            {
                float left = Mathf.Max(48, lastW * .07f);
                logo.Pin(new Vector2(0, .5f), new Vector2(left, 150), new Vector2(620, 190));
                logo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                foreach (var t in logo.GetComponentsInChildren<Text>()) t.alignment = TextAnchor.MiddleLeft;
                stage.Pin(new Vector2(.7f, .5f), new Vector2(0, 10), new Vector2(300, 300));
                stage.localScale = Vector3.one;
                menu.Pin(new Vector2(0, .5f), new Vector2(left, 30), new Vector2(320, 10));
                menu.pivot = new Vector2(0, 1);
                footer.Pin(Vector2.zero, new Vector2(left, 18), new Vector2(10, 22));
            }
        }

        /// <summary>Cream on the left fading to clear, so the name stays easy to read over the town.</summary>
        static Sprite SideWash()
        {
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Pow(1 - Mathf.Clamp01(x / 40f), 1.6f)));
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(.5f, .5f), 1);
        }

        void OnDestroy() => PlazaInput.Typing = false;
    }
}
