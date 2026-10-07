using System.Collections.Generic;
using System.Text;
using BookBuddies.Local;
using BookBuddies.Net;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The title's "Host or join a world" card. Hosting takes two steps: set your world up (its name, and whether PCs on
    /// your network see it in their list), then open your offline world to other PCs (HostRunner); while it's open the
    /// card shows its join code and visitors, and "Enter my world" takes you in. Joining takes a friend's code or address, or a world heard on your network
    /// (LocalBeacon), checks it's there, then switches to it with your buddy and pets (BBApi.UseServer). It all travels
    /// over your own network, or a port the host forwards: nothing goes through bookbuddies.pet.
    /// </summary>
    public sealed class WorldsSheet : MonoBehaviour
    {
        const float LookEvery = .5f;   // seconds between looks at your visitors and the worlds nearby
        const float CopiedFor = 1.5f;  // seconds "Copied!" stays on the copy button
        static readonly string[] FindNames = { "Your network", "Code only" };

        Sheet sheet;
        System.Action joined, changed;
        RectTransform idle, setup, hosting, nearby;
        Button setUp, openWorld, enter, copy;
        Text openLabel, findNote, hostError, code, copyLabel, visitors, portNote, joinStatus;
        InputField field, worldName;
        LocalBeacon listener;
        List<string> addresses = new List<string>(); // this PC's, looked up as the card opens and as your world opens
        string nearbyShown;
        float lookAt, copiedAt = -99;
        bool busy, settingUp, listed = true;

        /// <summary>
        /// The card, closed. "joined" runs once you've switched to a friend's world (the card closes first); "changed"
        /// after your world opens or closes.
        /// </summary>
        public static Sheet Create(Transform parent, System.Action joined, System.Action changed)
        {
            var s = Sheet.Create(parent, "Worlds", new Vector2(.5f, .5f), Vector2.zero, 540, "Host or join a world");
            s.Dim(.35f);
            var w = s.gameObject.AddComponent<WorldsSheet>();
            w.sheet = s;
            w.joined = joined;
            w.changed = changed;
            w.Build(s.Card);
            return s;
        }

        /// <summary>"1 visitor", "3 visitors".</summary>
        public static string VisitorCount(int n) => n == 1 ? "1 visitor" : n + " visitors";

        void Build(RectTransform card)
        {
            // host: what it does, then setting it up and opening it, then the open world's code and visitors
            Heading(card, "Host your world");
            idle = Group(card);
            Line(idle, "Friends play in your offline world on this PC. Your buddy, pets and coins stay yours.");
            setUp = UiKit.Primary(idle, "Set up my world", () => SetUp(true), "🏡", 52);

            setup = Group(card);
            UiKit.Label(setup, "World name", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            worldName = UiKit.Input(setup, "Your world’s name");
            worldName.characterLimit = 40;
            UiKit.Size(worldName, -1, 52);
            var who = Row(setup);
            UiKit.Size(UiKit.Label(who, "Who can find it", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold), -1, -1, 1);
            UiKit.Choice(who, FindNames, 0, i => { listed = i == 0; ShowFindNote(); });
            findNote = Line(setup, "");
            var go = Row(setup);
            UiKit.Size(UiKit.Secondary(go, "Back", () => SetUp(false), null, 52), -1, -1, 1);
            openWorld = UiKit.Primary(go, "Open my world", Host, null, 52);
            UiKit.Size(openWorld, -1, -1, 2);
            openLabel = openWorld.GetComponentInChildren<Text>();
            hostError = UiKit.Label(setup, "", UiKit.SmallSize + 1, UiKit.RoseInk, UiKit.Bold);
            ShowFindNote();

            hosting = Group(card);
            var plate = UiKit.Panel(hosting, "join code", Palette.Paper);
            plate.raycastTarget = false;
            UiKit.Column(plate.rectTransform, 2, new RectOffset(14, 14, 10, 14), TextAnchor.MiddleCenter);
            UiKit.Outline(plate, Palette.Ink.WithAlpha(.12f), UiKit.CardRadius, 1);
            UiKit.Label(plate.transform, "Your join code", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
            code = UiKit.Label(plate.transform, "", 52, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var row = Row(hosting);
            copy = UiKit.Secondary(row, "Copy code", Copy, null, 44);
            copyLabel = copy.GetComponentInChildren<Text>();
            visitors = UiKit.Label(row, "", UiKit.BodySize, UiKit.LeafInk, UiKit.Bold, TextAnchor.MiddleRight);
            UiKit.Size(visitors, -1, -1, 1);
            UiKit.Label(hosting, "Friends on your network type this code to join.", UiKit.BodySize, Palette.Ink);
            enter = UiKit.Primary(hosting, "Enter my world", Enter, "🏡", 52);
            portNote = Line(hosting, "");
            if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                Line(hosting, "If Windows asks, allow BookBuddies on private networks.");
            UiKit.Secondary(hosting, "Close my world", CloseWorld, null, 48);

            // join: a code or address, or a world heard on your network
            UiKit.Spacer(card, 2, 0);
            UiKit.Rule(card);
            Heading(card, "Join a world");
            var join = Row(card);
            field = UiKit.Input(join, "Join code or address");
            field.characterLimit = 64;
            field.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) JoinTyped(); });
            UiKit.Size(field, -1, 52, 1);
            UiKit.Primary(join, "Join", JoinTyped, null, 52);
            joinStatus = UiKit.Label(card, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            UiKit.Label(card, "Worlds on your network", UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold);
            nearby = Group(card);
        }

        // ---- while the card is open ----

        void OnEnable()
        {
            listener = LocalBeacon.Listen();
            addresses = LocalHost.Addresses();
            nearbyShown = null;
            lookAt = 0;
            if (!busy) { Say("", false); hostError.text = ""; settingUp = false; }
            ShowHosting(); // before the sheet focuses its first control
        }

        void OnDisable() => listener.Dispose();

        void Update()
        {
            if (Time.unscaledTime < lookAt) return;
            lookAt = Time.unscaledTime + LookEvery;
            ShowHosting();
            ShowNearby();
        }

        // ---- hosting ----

        // the first step, the setup, or the open world's code and visitors
        void ShowHosting()
        {
            bool on = LocalHost.Running;
            UiKit.Show(idle, !on && !settingUp);
            UiKit.Show(setup, !on && settingUp);
            UiKit.Show(hosting, on);
            UiKit.Show(hostError, hostError.text.Length > 0);
            sheet.First = on ? enter : settingUp ? openWorld : setUp;
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

        // step one: name your world and choose who can find it (or back out)
        void SetUp(bool show)
        {
            settingUp = show;
            hostError.text = "";
            if (show && worldName.text.Trim().Length == 0) worldName.text = DefaultName();
            ShowHosting();
            if (sheet.IsOpen) VirtualCursor.FocusFirst(show ? openWorld : setUp);
        }

        void ShowFindNote() => findNote.text = listed
            ? "It shows in the list on PCs on your network, and your join code works too."
            : "It stays off other PCs’ lists. Only friends with your join code can find it.";

        static string DefaultName() => Buddy.Name.Length > 0 ? Buddy.Name + "’s world" : "A cozy world";

        // step two: your world is your offline one, so into offline play if needed, then open it to the PCs on your network
        async void Host()
        {
            if (busy) return;
            busy = true;
            hostError.text = "";
            openLabel.text = "Opening…";
            if (!Settings.IsLocal) await BBApi.UseServer(Settings.Local);
            if (!this) return;
            busy = false;
            openLabel.text = "Open my world";
            string name = LocalSafety.CleanText(worldName.text, 40).Trim();
            if (HostRunner.Open(name.Length > 0 ? name : DefaultName(), listed)) settingUp = false;
            else hostError.text = LocalHost.Error ?? "Your world couldn’t open. Try again in a moment.";
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
            if (sheet.IsOpen) VirtualCursor.FocusFirst(setUp);
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

        static void Heading(Transform card, string title) => UiKit.Label(card, title, UiKit.BodySize, UiKit.EmberInk, UiKit.Bold);

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
