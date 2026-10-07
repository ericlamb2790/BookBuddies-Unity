using System.Collections.Generic;
using System.Text;
using BookBuddies.Local;
using BookBuddies.Net;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The title's world cards. "My world" plays in your offline world on this PC: just you, or opened to other PCs
    /// (HostRunner) for friends with your join code, or for everyone on your network too. While it's open the card shows
    /// its join code and visitors, and "Enter my world" takes you in. "Join a friend" takes a friend's code or address,
    /// or a world heard on your network (LocalBeacon), checks it's there, then switches to it with your buddy and pets
    /// (BBApi.UseServer). It all travels over your own network, or a port the host forwards: nothing goes through
    /// bookbuddies.pet.
    /// </summary>
    public sealed class WorldsSheet : MonoBehaviour
    {
        const float LookEvery = .5f;   // seconds between looks at your visitors and the worlds nearby
        const float CopiedFor = 1.5f;  // seconds "Copied!" stays on the copy button
        const string WhoKey = "bb.worldWho"; // who could join your world last time (an index into WhoNames)
        static readonly string[] WhoNames = { "Just me", "Code only", "My network" };

        Sheet sheet;
        System.Action joined, changed;
        bool host;                     // My world (or Join a friend)
        RectTransform setup, named, hosting, nearby;
        Button openWorld, enter, copy;
        Text openLabel, whoNote, hostError, code, copyLabel, visitors, portNote, joinStatus;
        InputField field, worldName;
        LocalBeacon listener;
        List<string> addresses = new List<string>(); // this PC's, looked up as the card opens and as your world opens
        string nearbyShown;
        float lookAt, copiedAt = -99;
        bool busy;
        int who;

        /// <summary>
        /// A card, closed: My world (host) or Join a friend. "joined" runs once you're in your world or a friend's (the card
        /// closes first); "changed" after your world opens or closes.
        /// </summary>
        public static Sheet Create(Transform parent, bool host, System.Action joined, System.Action changed)
        {
            string title = host ? "My world" : "Join a friend";
            var s = Sheet.Create(parent, title, new Vector2(.5f, .5f), Vector2.zero, 540, title);
            s.Dim(.35f);
            var w = s.gameObject.AddComponent<WorldsSheet>();
            w.sheet = s;
            w.host = host;
            w.joined = joined;
            w.changed = changed;
            if (host) w.BuildHost(s.Card);
            else w.BuildJoin(s.Card);
            return s;
        }

        /// <summary>"1 visitor", "3 visitors".</summary>
        public static string VisitorCount(int n) => n == 1 ? "1 visitor" : n + " visitors";

        // my world: who can join (and its name when friends can), then in; once it's open, its code and visitors
        void BuildHost(RectTransform card)
        {
            who = Mathf.Clamp(PlayerPrefs.GetInt(WhoKey, 0), 0, WhoNames.Length - 1);
            setup = Group(card);
            var row = Row(setup);
            UiKit.Size(UiKit.Label(row, "Who can join", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold), -1, -1, 1);
            UiKit.Choice(row, WhoNames, who, i => { who = i; PlayerPrefs.SetInt(WhoKey, i); ShowWho(); });
            whoNote = Line(setup, "");
            named = Group(setup);
            UiKit.Label(named, "World name", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            worldName = UiKit.Input(named, "Your world’s name");
            worldName.characterLimit = 40;
            UiKit.Size(worldName, -1, 52);
            openWorld = UiKit.Primary(setup, "Play", Open, null, 52);
            openLabel = openWorld.GetComponentInChildren<Text>();
            hostError = UiKit.Label(setup, "", UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            ShowWho();

            hosting = Group(card);
            var plate = UiKit.Panel(hosting, "join code", Palette.Paper);
            plate.raycastTarget = false;
            UiKit.Column(plate.rectTransform, 2, new RectOffset(14, 14, 10, 14), TextAnchor.MiddleCenter);
            UiKit.Outline(plate, Palette.Ink.WithAlpha(.12f), UiKit.CardRadius, 1);
            UiKit.Label(plate.transform, "Your join code", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
            code = UiKit.Label(plate.transform, "", 52, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var share = Row(hosting);
            copy = UiKit.Secondary(share, "Copy code", Copy, null, 44);
            copyLabel = copy.GetComponentInChildren<Text>();
            visitors = UiKit.Label(share, "", UiKit.BodySize, UiKit.LeafInk, UiKit.Bold, TextAnchor.MiddleRight);
            UiKit.Size(visitors, -1, -1, 1);
            UiKit.Label(hosting, "Friends type this code in Join a friend.", UiKit.BodySize, Palette.Ink);
            enter = UiKit.Primary(hosting, "Enter my world", Enter, "🏡", 52);
            portNote = Line(hosting, "");
            if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                Line(hosting, "If Windows asks, allow BookBuddies on private networks.");
            UiKit.Secondary(hosting, "Close my world", CloseWorld, null, 48);
        }

        // join a friend: a code or address, or a world heard on your network
        void BuildJoin(RectTransform card)
        {
            Line(card, "Type the join code your friend sees on their screen, or pick a world on your network.");
            var join = Row(card);
            field = UiKit.Input(join, "Join code or address");
            field.characterLimit = 64;
            field.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) JoinTyped(); });
            UiKit.Size(field, -1, 52, 1);
            UiKit.Primary(join, "Join", JoinTyped, null, 52);
            joinStatus = UiKit.Label(card, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            UiKit.Spacer(card, 2, 0);
            UiKit.Label(card, "Worlds on your network", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            nearby = Group(card);
            sheet.First = field;
        }

        // ---- while the card is open ----

        void OnEnable()
        {
            addresses = LocalHost.Addresses();
            lookAt = 0;
            if (host)
            {
                if (!busy) hostError.text = "";
                if (worldName.text.Trim().Length == 0) worldName.text = DefaultName();
                ShowHosting(); // before the sheet focuses its first control
            }
            else
            {
                listener = LocalBeacon.Listen();
                nearbyShown = null;
                if (!busy) Say("", false);
            }
        }

        void OnDisable()
        {
            listener?.Dispose();
            listener = null;
        }

        void Update()
        {
            if (Time.unscaledTime < lookAt) return;
            lookAt = Time.unscaledTime + LookEvery;
            if (host) ShowHosting();
            else ShowNearby();
        }

        // ---- my world ----

        // who can join and Play (or Open), or the open world's code and visitors
        void ShowHosting()
        {
            bool on = LocalHost.Running;
            UiKit.Show(setup, !on);
            UiKit.Show(hosting, on);
            UiKit.Show(hostError, hostError.text.Length > 0);
            sheet.First = on ? enter : openWorld;
            if (!on) return;
            // with no network the code would only lead back to this PC
            bool noNetwork = addresses.Count == 0;
            code.text = noNetwork ? "Connect this PC to Wi-Fi or a cable so friends can find your world." : LocalHost.Code;
            code.fontSize = noNetwork ? UiKit.BodySize : 52;
            code.font = noNetwork ? UiKit.Bold : UiKit.Title;
            int n = LocalHost.Visitors;
            visitors.text = n == 0 ? "No visitors yet" : VisitorCount(n);
            copyLabel.text = Time.unscaledTime - copiedAt < CopiedFor ? "Copied!" : "Copy code";
            portNote.text = $"Playing over the internet? Forward port {LocalHost.Port} on your router to this PC, then share your public address.";
        }

        void ShowWho()
        {
            whoNote.text = who == 0 ? "Just you, on this PC. No network needed."
                : who == 1 ? "Friends with your join code can come in. Your world stays off other PCs’ lists."
                : "PCs on your network see it in their list, and your join code works too.";
            UiKit.Show(named, who > 0);
            if (!busy) openLabel.text = who == 0 ? "Play" : "Open my world";
        }

        static string DefaultName() => Buddy.Name.Length > 0 ? Buddy.Name + "’s world" : "A cozy world";

        // your world is your offline one, so into offline play if needed; then in you go, or it opens to other PCs first
        async void Open()
        {
            if (busy) return;
            busy = true;
            hostError.text = "";
            openLabel.text = who == 0 ? "Getting ready…" : "Opening…";
            if (!Settings.IsLocal) await BBApi.UseServer(Settings.Local);
            else _ = CoinBank.Refresh(); // starting your world: your account's coins come up to date
            if (!this) return;
            busy = false;
            ShowWho();
            if (who == 0) { Enter(); return; }
            string name = LocalSafety.CleanText(worldName.text, 40).Trim();
            if (!HostRunner.Open(name.Length > 0 ? name : DefaultName(), who == 2))
                hostError.text = LocalHost.Error ?? "Your world couldn’t open. Try again in a moment.";
            addresses = LocalHost.Addresses();
            ShowHosting();
            changed?.Invoke();
            if (LocalHost.Running && sheet.IsOpen) VirtualCursor.FocusFirst(enter);
        }

        // in you go: you play in your own world like your visitors, and it stays open while you do
        void Enter()
        {
            sheet.Close();
            joined?.Invoke();
        }

        void Copy()
        {
            string joinCode = LocalHost.Code;
            if (joinCode == null || addresses.Count == 0) return;
            GUIUtility.systemCopyBuffer = joinCode;
            copiedAt = Time.unscaledTime;
            ShowHosting();
        }

        void CloseWorld()
        {
            HostRunner.Close();
            ShowHosting();
            changed?.Invoke();
            if (sheet.IsOpen) VirtualCursor.FocusFirst(openWorld);
        }

        // ---- joining ----

        void JoinTyped()
        {
            string text = field.text.Trim();
            if (text.Length == 0) Say("Type the join code your friend sees on their screen.", true);
            else if (!JoinCode.TryRead(text, out string server)) Say("That doesn’t look like a join code. Check it and try again.", true);
            else Join(server);
        }

        // checks the world is there and has room, switches to it (your buddy and pets come along), then in you go
        async void Join(string server)
        {
            if (busy) return;
            if (!Buddy.Hatched) { Say("Hatch your egg first, so your buddy can come along.", true); return; }
            if (LocalHost.Running) { Say(OwnWorld(server) ? "That’s your own world! Friends join it from their PCs." : "Close your world first, then you can visit a friend’s.", true); return; }
            busy = true;
            Say("Looking for that world…", false);
            Dictionary<string, object> world = null;
            bool reached = true;
            try { world = await BBApi.WorldAt(server); }
            catch (BBApi.ApiError) { reached = false; }
            if (!this) return;
            if (!reached || world == null || world.Int("players") >= world.Int("max", int.MaxValue))
            {
                busy = false;
                Say(!reached ? "Couldn’t reach that world. Check the code, and that you’re on the same network as the host."
                    : world == null ? "That address isn’t a BookBuddies world."
                    : "That world is full right now. Try again in a little while.", true);
                return;
            }

            string from = Settings.Server;
            Say("Joining " + Settings.WorldNameOf(server) + "…", false);
            await BBApi.UseServer(server);
            if (!this) return;
            if (!Settings.SignedIn)
            {
                await BBApi.UseServer(from); // the world couldn't make you a profile there: back where you were
                busy = false;
                if (this) Say("Couldn’t join that world just now. Try again in a moment.", true);
                return;
            }
            busy = false;
            Say("", false);
            field.text = "";
            sheet.Close();
            joined?.Invoke();
        }

        // your own open world, by any of this PC's addresses
        bool OwnWorld(string server) =>
            LocalHost.Running && System.Uri.TryCreate(server, System.UriKind.Absolute, out var uri) && uri.Port == LocalHost.Port
            && (uri.IsLoopback || addresses.Contains(uri.Host));

        void Say(string text, bool problem)
        {
            joinStatus.text = text;
            joinStatus.color = problem ? UiKit.RoseInk : Palette.InkSoft;
            UiKit.Show(joinStatus, text.Length > 0);
        }

        // the worlds heard on your network lately (not your own), rebuilt only when something about them changed
        void ShowNearby()
        {
            var worlds = listener.Worlds;
            worlds.RemoveAll(w => OwnWorld(w.Server));
            var sb = new StringBuilder();
            foreach (var w in worlds) sb.Append(w.Name).Append('|').Append(w.Server).Append('|').Append(w.Players).Append('/').Append(w.Max).Append('\n');
            string now = sb.ToString();
            if (now == nearbyShown) return;
            nearbyShown = now;
            for (int i = nearby.childCount - 1; i >= 0; i--)
            {
                var old = nearby.GetChild(i).gameObject;
                old.SetActive(false); // gone from the layout now, destroyed at the end of the frame
                Destroy(old);
            }
            if (worlds.Count == 0) Line(nearby, "Looking for worlds on your network…");
            foreach (var w in worlds) NearbyWorld(w);
        }

        // one world nearby: its name, who's visiting, and Join
        void NearbyWorld(WorldInfo w)
        {
            var box = UiKit.Panel(nearby, "world", Palette.Paper, 12);
            box.raycastTarget = false;
            UiKit.Row(box.rectTransform, 12, new RectOffset(12, 10, 8, 8));
            UiKit.Outline(box, Palette.Ink.WithAlpha(.1f), 12, 1);
            UiKit.Icon(box.transform, "🏡", 32);
            var words = UiKit.Node("words", box.transform);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, string.IsNullOrEmpty(w.Name) ? "A friend’s world" : w.Name, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            UiKit.Label(words, $"{w.Players} of {w.Max} visitors", UiKit.SmallSize, Palette.InkSoft);
            bool full = w.Max > 0 && w.Players >= w.Max;
            var go = UiKit.Secondary(box.transform, full ? "Full" : "Join", () => Join(w.Server), null, 44);
            go.interactable = !full && !string.IsNullOrEmpty(w.Server);
        }

        // ---- pieces ----

        static Text Line(Transform parent, string text) => UiKit.Label(parent, text, UiKit.SmallSize + 1, Palette.InkSoft);

        static RectTransform Group(Transform card)
        {
            var g = UiKit.Node("group", card);
            UiKit.Column(g, 10);
            return g;
        }

        static RectTransform Row(Transform card)
        {
            var r = UiKit.Node("row", card);
            UiKit.Row(r, 10);
            return r;
        }
    }
}
