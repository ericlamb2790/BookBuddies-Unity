using BookBuddies.Net;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Sign in with your BookBuddies recovery code (or a 6-letter link code), or go back.
    /// The town stays visible behind the card.
    /// </summary>
    public sealed class SignInScreen : MonoBehaviour
    {
        System.Action<bool> done;
        Canvas canvas;
        InputField code, server;
        Text error, signInLabel, serverText;
        RectTransform serverRow;
        bool busy;

        /// <summary>Shows the card. "done" runs with true once you've signed in, or false if you went back.</summary>
        public static SignInScreen Show(string message, System.Action<bool> done)
        {
            var canvas = UiKit.MakeCanvas("Sign in", 30);
            var s = canvas.gameObject.AddComponent<SignInScreen>();
            s.canvas = canvas;
            s.done = done;
            s.Build((RectTransform)canvas.transform);
            if (!string.IsNullOrEmpty(message)) s.Error(message);
            return s;
        }

        void Build(RectTransform root)
        {
            var veil = UiKit.Node("veil", root).Fill().gameObject.AddComponent<Image>();
            veil.color = Palette.Paper.WithAlpha(.55f);

            var card = UiKit.Panel(root, "card", Palette.Cream, 18).rectTransform;
            card.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(380, 10));
            UiKit.Column(card, 10, new RectOffset(26, 26, 22, 24));
            UiKit.Hug(card, false, true);
            UiKit.Shadow(card, 18, 24, 8, .3f);

            var pet = UiKit.Node("pet", card).gameObject.AddComponent<Image>();
            pet.sprite = PetSprites.For(Buddy.ShownLook);
            pet.preserveAspect = true;
            pet.raycastTarget = false;
            UiKit.Size(pet, -1, 92);

            UiKit.Label(card, "BookBuddies", 34, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            UiKit.Label(card, "Welcome back! Sign in to bring your buddy to town.", 16, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            Gap(card, 4);

            UiKit.Label(card, "Recovery code", 14, Palette.Ink, UiKit.Bold);
            code = UiKit.Input(card, "BB-XXXXX-XXXXX");
            code.characterLimit = 24;
            code.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) SignIn(); });
            UiKit.Size(code, -1, 46);
            UiKit.Label(card, "Use the recovery code you saved when your egg hatched (or from the website), or a 6-letter link code.", 13, Palette.InkSoft);

            error = UiKit.Label(card, "", 14, Palette.Rose, UiKit.Bold);
            UiKit.Show(error, false);

            var signIn = UiKit.TextButton(card, "Sign in", null, Palette.Amber, Palette.Ink, SignIn, 46);
            signInLabel = signIn.GetComponentInChildren<Text>();
            UiKit.TextButton(card, "Back", null, Palette.Paper, Palette.Ink, () => Finish(false), 42);

            // which server: bookbuddies.pet unless you run your own copy of the Worker
            var row = UiKit.Node("server", card);
            UiKit.Row(row, 6, null, TextAnchor.MiddleCenter);
            serverText = UiKit.Label(row, "", 13, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleRight);
            serverText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var change = UiKit.Button(row, "Change", Color.clear, () => UiKit.Show(serverRow, !serverRow.gameObject.activeSelf), 8);
            UiKit.Row((RectTransform)change.transform, 0, new RectOffset(4, 4, 2, 2));
            UiKit.Label(change.transform, "Change", 13, Palette.Ember, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;

            serverRow = UiKit.Node("server address", card);
            UiKit.Row(serverRow, 8);
            server = UiKit.Input(serverRow, Settings.DefaultServer, 14);
            server.text = Settings.Server;
            UiKit.Size(server, 100, 38, 1);
            UiKit.TextButton(serverRow, "Save", null, Palette.Paper, Palette.Ink, SaveServer, 38);
            UiKit.Show(serverRow, false);
            ShowServer();

            if (!Application.isMobilePlatform) { code.Select(); code.ActivateInputField(); }
        }

        static void Gap(Transform parent, float height) => UiKit.Size(UiKit.Node("gap", parent), -1, height);

        void Update()
        {
            canvas.GetComponent<CanvasScaler>().scaleFactor = UiKit.ScreenScale();
            PlazaInput.Typing = code.isFocused || server.isFocused; // so "-" in a code doesn't zoom the camera
            if (PlazaInput.Down(PlazaAction.Back) && !busy) Finish(false);
        }

        void OnDestroy() => PlazaInput.Typing = false;

        void SaveServer()
        {
            Settings.Server = server.text;
            server.text = Settings.Server;
            ShowServer();
            UiKit.Show(serverRow, false);
        }

        void ShowServer() => serverText.text = "Server: " + Settings.ServerHost;

        async void SignIn()
        {
            if (busy) return;
            string text = code.text.Trim();
            if (text.Length < 6) { Error("Type your recovery code (BB-XXXXX-XXXXX) or a 6-letter link code."); return; }
            busy = true;
            signInLabel.text = "Signing in…";
            UiKit.Show(error, false);
            try
            {
                await BBApi.SignIn(text);
                Buddy.Forget(); // your buddy comes from the server from now on
                Finish(true);
                return;
            }
            catch (BBApi.ApiError e)
            {
                Error(e.Status == 0 ? $"Can’t reach {Settings.ServerHost}. Check your connection and try again." : e.Message);
            }
            catch (System.Exception)
            {
                Error($"Can’t reach {Settings.ServerHost}. Check your connection and try again.");
            }
            if (!this) return;
            busy = false;
            signInLabel.text = "Sign in";
        }

        void Error(string message)
        {
            error.text = message;
            UiKit.Show(error, true);
        }

        void Finish(bool signedIn)
        {
            Destroy(gameObject);
            done?.Invoke(signedIn);
        }
    }
}
