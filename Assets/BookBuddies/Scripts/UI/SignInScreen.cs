using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Sign in with your BookBuddies recovery code, or go back (Back, Esc or B). A buddy only this device has is saved as an
    /// account of its own first, so a code never costs it. In join mode (Settings → Join accounts) the code is checked
    /// instead, and the join-accounts card takes over. The town stays visible behind the card; on short screens the card
    /// drops your buddy's picture and shrinks to fit.
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
        string keep, keepName; // the code of the buddy saved here as its own account, shown before leaving
        bool busy, finished, join;

        /// <summary>
        /// Shows the card. "done" runs with true once this device's account changed (signed in, or joined up), or false if
        /// you went back. join: the code joins the account you're signed in to instead of signing in.
        /// </summary>
        public static SignInScreen Show(string message, System.Action<bool> done, bool join = false)
        {
            var canvas = UiKit.MakeCanvas("Sign in", 30);
            var s = canvas.gameObject.AddComponent<SignInScreen>();
            s.root = (RectTransform)canvas.transform;
            s.done = done;
            s.join = join;
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

            UiKit.Label(card, join ? "Join your accounts" : "Welcome back", UiKit.TitleSize + 4, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            UiKit.Label(card, join
                ? "Type the code from bookbuddies.pet (Settings → Recovery code) or from your other buddy. Nothing changes until you say yes."
                : "Sign in to bring your buddy to town.", UiKit.BodySize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
            UiKit.Spacer(card, 4, 0);

            UiKit.Label(card, "Recovery code", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            code = UiKit.Input(card, "BB-XXXXX-XXXXX");
            code.characterLimit = 24;
            code.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) SignIn(); });
            UiKit.Size(code, -1, 52);
            UiKit.Label(card, "The code you saved when your egg hatched, or the one on bookbuddies.pet.", UiKit.SmallSize, Palette.InkSoft);

            error = UiKit.Label(card, "", UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            UiKit.Show(error, false);

            var signIn = UiKit.Primary(card, Action, SignIn, null, 52);
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
            UiKit.Show(row, !join); // joining is always with the online server
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

        string Action => join ? "Check code" : "Sign in";

        async void SignIn()
        {
            if (busy) return;
            string text = code.text.Trim();
            int letters = 0;
            foreach (char c in text) if (char.IsLetterOrDigit(c)) letters++;
            if (letters != 12) { Error("Type your whole recovery code, like BB-XXXXX-XXXXX."); return; }
            busy = true;
            signInLabel.text = join ? "Checking…" : "Signing in…";
            UiKit.Show(error, false);
            string problem = await (join ? Check(text) : Buddy.Hatched && !Settings.SignedIn ? SaveBuddyFirst(text) : Claim(text));
            if (!this || problem == null) return;
            busy = false;
            signInLabel.text = Action;
            Error(problem);
        }

        // signing in as the code's account; your buddy comes from the server from now on. The problem, or null once done
        async Task<string> Claim(string text)
        {
            try { await BBApi.SignIn(text); }
            catch (System.Exception e) { return Problem(e); }
            Buddy.Forget();
            if (this) Finish(true);
            return null;
        }

        // join mode: what the code would join (nothing changes yet); the join-accounts card takes over from this card, and
        // answers for it. The problem, or null once handed over
        async Task<string> Check(string text)
        {
            MergePlan plan;
            try { plan = MergePlan.Read(await BBApi.MergePreview(text), TalesSave.Current, TalesSave.Clock()); }
            catch (System.Exception e) { return Problem(e); }
            if (plan.Mode == "same") return "That’s the code you’re signed in with already.";
            if (!this) return null;
            var (then, name, kept) = (done, keepName, keep);
            (done, keep) = (null, null); // closing this card answers nothing
            Finish(false);
            TownSheets.JoinAccounts(plan, text, changed => Done(then, changed, name, changed ? null : kept)); // joined: one login, no code to keep
            return null;
        }

        // a buddy only this device has: saved as its own account first, then the code joins it where the online server can
        // join accounts, or signs in as before (its own code is shown on the way out either way, unless it joined)
        async Task<string> SaveBuddyFirst(string text)
        {
            bool merge;
            try { merge = Settings.Server == Settings.OnlineServer && (await BBApi.Features(Settings.Server)).Merge; }
            catch (System.Exception e) { return Problem(e); }
            if (!await RegisterDevice()) return "We couldn’t save your buddy before signing in, so nothing changed. Try again in a little while.";
            join = merge; // signed in to the buddy's account now: a second try goes the same way
            return await (join ? Check(text) : Claim(text));
        }

        /// <summary>
        /// Saves the buddy hatched on this device (while the server was away) as an account of its own: its pets come along
        /// (6 at most, the active one still active), and its adventure is this account's and goes up. False when it
        /// couldn't be done: then nothing changed, the device keeps its buddy, and nobody is signed in.
        /// </summary>
        async Task<bool> RegisterDevice()
        {
            var pets = new List<MyPets.Pet>(MyPets.All); // before signing in, which gives MyPets the new account's list
            var first = MyPets.Active;
            Dictionary<string, object> account, list;
            try
            {
                account = await BBApi.Register(first.Name, first.Look);
                list = await BBApi.Pets();
                string id = list.Str("active");
                int n = 1;
                foreach (var p in pets)
                    if (p != first && n++ < MyPets.Max) list = await BBApi.HatchPet(p.Name, p.Look);
                if (n > 1) list = await BBApi.SetActivePet(id);
            }
            catch (System.Exception)
            {
                // a half-made account is left as it is; this device stays as it was, its pets too (in case anything looked
                // at the list while it belonged to the new account)
                Settings.SignOut();
                MyPets.Apply(new Dictionary<string, object>
                {
                    ["pets"] = pets.ConvertAll(p => (object)new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Look }),
                    ["active"] = first.Id,
                });
                return false;
            }
            keep = account.Str("recovery");
            keepName = first.Name;
            MyPets.Apply(list);
            TalesSave.Current.Owner = Settings.AccountId;
            await TalesSync.Upload();
            return true;
        }

        string Problem(System.Exception e) =>
            !(e is BBApi.ApiError a) || a.Status == 0 ? $"Can’t reach {Settings.ServerHost}. Check your connection and try again."
            : join && a.Status == 404 && a.Why.Length == 0 ? "Joining accounts isn’t available yet. Try again after the next update."
            : a.Message;

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
            Done(done, signedIn, keepName, keep);
        }

        // done, after the code card when a buddy was saved here as its own account (the account changed then, either way)
        static void Done(System.Action<bool> done, bool changed, string name, string kept)
        {
            if (string.IsNullOrEmpty(kept)) done?.Invoke(changed);
            else TownSheets.BuddyCode(name, kept, () => done?.Invoke(true));
        }
    }
}
