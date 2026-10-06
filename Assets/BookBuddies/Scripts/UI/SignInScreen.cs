using BookBuddies.Net;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Sign in with your BookBuddies recovery code (or a 6-letter link code), or go back (Back, Esc or B).
    /// The town stays visible behind the card; on short screens the card drops your buddy's picture and shrinks to fit.
    /// </summary>
    public sealed class SignInScreen : MonoBehaviour
    {
        const float Width = 480;
        const float ShortScreen = 700; // below this the buddy picture steps aside

        System.Action<bool> done;
        RectTransform root, card;
        Image pet;
        InputField code, server;
        Text error, signInLabel, serverText;
        RectTransform serverRow;
        Vector2 lastSize;
        bool busy, finished;

        /// <summary>Shows the card. "done" runs with true once you've signed in, or false if you went back.</summary>
        public static SignInScreen Show(string message, System.Action<bool> done)
        {
            var canvas = UiKit.MakeCanvas("Sign in", 30);
            var s = canvas.gameObject.AddComponent<SignInScreen>();
            s.root = (RectTransform)canvas.transform;
            s.done = done;
            s.Build();
            if (!string.IsNullOrEmpty(message)) s.Error(message);
            UiStack.Push(s, s.OnBack);
            return s;
        }

        void Build()
        {
            var veil = UiKit.Node("veil", root).Fill().gameObject.AddComponent<Image>();
            veil.color = Palette.Paper.WithAlpha(.55f);

            var panel = UiKit.Panel(root, "card", Palette.Cream);
            card = panel.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(Width, 10));
            UiKit.Column(card, 12, new RectOffset(28, 28, 24, 30));
            UiKit.Hug(card, false, true);
            UiKit.Outline(panel, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(card, UiKit.CardRadius, 24, 8, .3f);

            pet = UiKit.Node("pet", card).gameObject.AddComponent<Image>();
            pet.sprite = PetSprites.For(Buddy.ShownLook);
            pet.preserveAspect = true;
            pet.raycastTarget = false;
            UiKit.Size(pet, -1, 96);

            UiKit.Label(card, "Welcome back", UiKit.TitleSize + 4, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            UiKit.Label(card, "Sign in to bring your buddy to town.", UiKit.BodySize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            UiKit.Spacer(card, 4, 0);

            UiKit.Label(card, "Recovery code", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            code = UiKit.Input(card, "BB-XXXXX-XXXXX");
            code.characterLimit = 24;
            code.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) SignIn(); });
            UiKit.Size(code, -1, 52);
            UiKit.Label(card, "The code you saved when your egg hatched (or from the website), or a 6-letter link code.", UiKit.SmallSize, Palette.InkSoft);

            error = UiKit.Label(card, "", UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            UiKit.Show(error, false);

            var signIn = UiKit.Primary(card, "Sign in", SignIn, null, 52);
            signInLabel = signIn.GetComponentInChildren<Text>();
            UiKit.Secondary(card, "Back", OnBack);

            // which server: bookbuddies.pet unless you run your own copy of the Worker
            var row = UiKit.Node("server", card);
            UiKit.Row(row, 8, null, TextAnchor.MiddleCenter);
            serverText = UiKit.Label(row, "", UiKit.SmallSize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleRight);
            serverText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.TextButton(row, "Change", null, Palette.Cream, UiKit.EmberInk, () => UiKit.Show(serverRow, !serverRow.gameObject.activeSelf), 44);

            serverRow = UiKit.Node("server address", card);
            UiKit.Row(serverRow, 8);
            server = UiKit.Input(serverRow, Settings.DefaultServer, UiKit.SmallSize + 1);
            server.text = Settings.Server;
            UiKit.Size(server, 100, UiKit.ButtonHeight, 1);
            UiKit.Secondary(serverRow, "Save", SaveServer);
            UiKit.Show(serverRow, false);
            ShowServer();

            UiKit.PadHints(card, ("A", "Select"), ("B", "Back"));
            if (!Application.isMobilePlatform && !PlazaInput.UsingGamepad) { code.Select(); code.ActivateInputField(); }
            else VirtualCursor.FocusFirst(code);
        }

        void Update()
        {
            if (root.rect.size != lastSize) Fit();
        }

        // narrower on small screens; on short ones the picture goes and the card shrinks until it fits
        void Fit()
        {
            lastSize = root.rect.size;
            card.sizeDelta = new Vector2(Mathf.Min(Width, lastSize.x - 32), card.sizeDelta.y);
            UiKit.Show(pet, lastSize.y >= ShortScreen);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            float k = Mathf.Min(1, (lastSize.y - 32) / Mathf.Max(1, LayoutUtility.GetPreferredHeight(card)));
            card.localScale = new Vector3(k, k, 1);
        }

        void OnBack()
        {
            if (busy) { UiStack.Push(this, OnBack); return; } // wait for the server's answer
            Finish(false);
        }

        void OnDestroy() => UiStack.Remove(this);

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
            string problem;
            try
            {
                await BBApi.SignIn(text);
                Buddy.Forget(); // your buddy comes from the server from now on
                if (this) Finish(true);
                return;
            }
            catch (BBApi.ApiError e)
            {
                problem = e.Status == 0 ? $"Can’t reach {Settings.ServerHost}. Check your connection and try again." : e.Message;
            }
            catch (System.Exception)
            {
                problem = $"Can’t reach {Settings.ServerHost}. Check your connection and try again.";
            }
            if (!this) return;
            busy = false;
            signInLabel.text = "Sign in";
            Error(problem);
        }

        void Error(string message)
        {
            error.text = message;
            UiKit.Show(error, true);
        }

        void Finish(bool signedIn)
        {
            if (finished) return;
            finished = true;
            UiStack.Remove(this);
            Destroy(gameObject);
            done?.Invoke(signedIn);
        }
    }
}
