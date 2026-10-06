using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Economy;
using BookBuddies.Net;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// Admin tools for accounts (town admins only; the server checks every call). Players: search by name or id,
    /// pick one to see their account and history, then mute them, send them home, give them a break from town,
    /// rename them, set their coins, share admin rights or delete the account — anything that can't be undone asks
    /// twice. Recent actions: what every admin did lately. Opened from Settings → Account, the town menu, or a
    /// player's card in town. Wide screens show the list and the player side by side; narrow ones one at a time
    /// (B goes back to the list). Mouse, touch, keys and the gamepad cursor all work.
    /// </summary>
    public sealed class AdminPanel : MonoBehaviour
    {
        const float MaxWidth = 1180, MaxHeight = 800;
        const float SideBySide = 960;   // card width from which the list and the player sit side by side
        const float SearchDelay = .35f;
        const double Forever = 8e15;    // the server's "for good"
        static readonly string[] TabNames = { "Players", "Recent actions" };
        static AdminPanel open;

        public static bool IsOpen => open != null;

        RectTransform root, card, listPane, detailPane, logPane, results, detail, logList;
        LayoutElement listSize;
        InputField search;
        Text count, status;
        Button[] tabs;
        readonly Dictionary<string, Image> rows = new Dictionary<string, Image>();
        readonly object detailLevel = new object(); // B on a narrow screen: back from a player to the list
        int tab, searchSerial;
        bool wide, busy;
        float searchAt = -1;
        Vector2 lastSize;
        string selectedId;
        Dictionary<string, object> player;
        double clockOffset; // server time minus local time, in ms

        /// <summary>Opens the admin tools, optionally searching for a name and opening one player's card.</summary>
        public static void Open(string query = null, string playerId = null)
        {
            if (!open)
            {
                var canvas = UiKit.MakeCanvas("Admin tools", 75);
                open = canvas.gameObject.AddComponent<AdminPanel>();
                open.root = (RectTransform)canvas.transform;
                open.Build();
                UiStack.Push(open, open.Close);
                Sound.Play("open");
            }
            open.ShowTab(0);
            open.search.SetTextWithoutNotify(query ?? "");
            open.Search();
            if (!string.IsNullOrEmpty(playerId)) open.Select(playerId);
            VirtualCursor.FocusFirst(open.search);
        }

        /// <summary>
        /// A moderation action from a player's card in town (mute, kick, ban…; see BBApi.AdminAct). Finds the account by
        /// the id the town shares, or else by the exact name, and reports back in a few words. When several readers
        /// share that name, the admin tools open with it searched instead.
        /// </summary>
        public static async void Quick(string name, string id, string action, Dictionary<string, object> body, System.Action<string> report)
        {
            try
            {
                string target = await FindAccount(name, id);
                if (target == null) { report($"More than one reader is called {name}. Pick the right one here."); Open(name); return; }
                var reply = await BBApi.AdminAct(target, action, body);
                var log = reply.Arr("log");
                report(log.Count > 0 ? Capital(Describe((Dictionary<string, object>)log[0])) + "." : "Done.");
            }
            catch (BBApi.ApiError e) { report(e.Message); }
        }

        static async Task<string> FindAccount(string name, string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                try { return (await BBApi.AdminPlayer(id)).Obj("player").Str("id"); }
                catch (BBApi.ApiError e) when (e.Status == 404) { } // not an account id: fall back to the name
            }
            var found = (await BBApi.AdminSearch(name, true)).Arr("players");
            if (found.Count == 0) throw new BBApi.ApiError($"Couldn’t find {name}’s account on this server.", 404);
            return found.Count == 1 ? ((Dictionary<string, object>)found[0]).Str("id") : null;
        }

        // ---- building ----

        void Build()
        {
            var scrim = UiKit.Cover(root, "scrim", Palette.Ink.WithAlpha(.55f), null, true);
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(Close);

            var panel = UiKit.Panel(root, "card", Palette.Cream);
            card = panel.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(MaxWidth, MaxHeight));
            UiKit.Column(card, 14, new RectOffset(28, 28, 22, 30));
            UiKit.Outline(panel, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(card, UiKit.CardRadius, 30, 10, .35f);

            var head = UiKit.Node("head", card);
            UiKit.Row(head, 12);
            UiKit.Icon(head, "🛡️", 34);
            UiKit.Size(UiKit.Label(head, "Admin tools", UiKit.TitleSize, Palette.Ink, UiKit.Title), -1, 44, 1);
            count = UiKit.Label(head, "", UiKit.SmallSize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleRight);
            count.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.CloseButton(head, Close);

            tabs = UiKit.Tabs(card, TabNames, ShowTab);

            var body = UiKit.Node("body", card);
            UiKit.Row(body, 20).childForceExpandHeight = true;
            UiKit.Size(body).flexibleHeight = 1;
            BuildList(body);
            BuildDetail(body);
            BuildLog(body);

            status = UiKit.Label(card, "", UiKit.SmallSize + 1, Palette.InkSoft);
            UiKit.Size(status, -1, 24);
            UiKit.PadHints(card, ("LB/RB", "Tabs"), ("A", "Select"), ("B", "Back"));
        }

        void BuildList(Transform body)
        {
            listPane = UiKit.Node("players", body);
            UiKit.Column(listPane, 10).childForceExpandHeight = false;
            listSize = UiKit.Size(listPane, 400);
            var row = UiKit.Node("search", listPane);
            UiKit.Row(row, 8);
            search = UiKit.Input(row, "Name or id");
            search.characterLimit = 64;
            search.onValueChanged.AddListener(_ => searchAt = Time.unscaledTime + SearchDelay);
            search.onEndEdit.AddListener(_ => { if (PlazaInput.EnterPressed()) Search(); });
            UiKit.Size(search, 120, 48, 1);
            UiKit.Secondary(row, "Search", Search, "🔍");
            results = UiKit.ScrollColumn(listPane, 8, new RectOffset(2, 8, 2, 8), out _);
            UiKit.Size(results.parent).flexibleHeight = 1;
        }

        void BuildDetail(Transform body)
        {
            detailPane = UiKit.Node("player", body);
            UiKit.Column(detailPane, 0);
            UiKit.Size(detailPane, -1, -1, 1);
            var bg = UiKit.Panel(detailPane, "paper", Palette.Paper, UiKit.CardRadius);
            UiKit.Size(bg).flexibleHeight = 1;
            UiKit.Column(bg.rectTransform, 0, new RectOffset(4, 4, 4, 4));
            detail = UiKit.ScrollColumn(bg.transform, 16, new RectOffset(18, 22, 16, 22), out _);
            UiKit.Size(detail.parent).flexibleHeight = 1;
        }

        void BuildLog(Transform body)
        {
            logPane = UiKit.Node("recent actions", body);
            UiKit.Column(logPane, 0);
            UiKit.Size(logPane, -1, -1, 1);
            logList = UiKit.ScrollColumn(logPane, 8, new RectOffset(2, 8, 2, 8), out _);
            UiKit.Size(logList.parent).flexibleHeight = 1;
        }

        // ---- every frame ----

        void Update()
        {
            if (root.rect.size != lastSize) Fit();
            if (searchAt > 0 && Time.unscaledTime >= searchAt) Search();
            if (PlazaInput.Down(PlazaAction.ZoomIn)) ShowTab(tab + 1);
            else if (PlazaInput.Down(PlazaAction.ZoomOut)) ShowTab(tab - 1);
        }

        void Fit()
        {
            lastSize = root.rect.size;
            card.sizeDelta = new Vector2(Mathf.Min(MaxWidth, lastSize.x - 32), Mathf.Min(MaxHeight, lastSize.y - 32));
            wide = card.sizeDelta.x >= SideBySide;
            Arrange();
        }

        // which panes show: the list and the player side by side, or one at a time on narrow screens
        void Arrange()
        {
            bool players = tab == 0, alone = !wide && player != null;
            UiKit.Show(logPane, !players);
            UiKit.Show(listPane, players && !alone);
            UiKit.Show(detailPane, players && (wide || player != null));
            listSize.preferredWidth = listSize.minWidth = wide ? 400 : -1;
            listSize.flexibleWidth = wide ? 0 : 1;
            if (players && alone) { if (!UiStack.Contains(detailLevel)) UiStack.Push(detailLevel, BackToList); }
            else UiStack.Remove(detailLevel);
        }

        void BackToList()
        {
            player = null;
            selectedId = null;
            Highlight();
            Arrange();
        }

        void ShowTab(int index)
        {
            tab = (index + TabNames.Length) % TabNames.Length;
            UiKit.SelectTab(tabs, tab);
            Arrange();
            if (tab == 1) LoadLog();
            if (tab == 0 && player == null) ShowEmptyDetail();
        }

        // ---- players ----

        async void Search()
        {
            searchAt = -1;
            int serial = ++searchSerial;
            string query = search.text.Trim();
            Dictionary<string, object> reply;
            try { reply = await BBApi.AdminSearch(query); }
            catch (BBApi.ApiError e) { if (this) Say(e.Message, true); return; }
            if (!this || serial != searchSerial) return; // a newer search is on its way
            Clock(reply);
            Clear(results);
            rows.Clear();
            var found = reply.Arr("players");
            count.text = reply.Int("total") + " players";
            foreach (Dictionary<string, object> p in found) ResultRow(p);
            if (found.Count == 0)
                Note(results, query.Length == 0 ? "No players yet. They show up here after they hatch an egg." : $"No one matches “{query}”. Try part of a name, or paste an id.");
            Highlight();
        }

        void ResultRow(Dictionary<string, object> p)
        {
            string id = p.Str("id");
            var b = UiKit.Button(results, p.Str("name"), Palette.Cream, () => Select(id), 12);
            UiKit.Outline(b, Palette.Ink.WithAlpha(.1f), 12, 1);
            UiKit.Size(b, -1, 72);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 12, new RectOffset(12, 14, 8, 8));
            Avatar(r, p, 48);
            var words = UiKit.Node("words", r);
            UiKit.Column(words, 2, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            var name = UiKit.Label(words, p.Str("name"), UiKit.BodySize, Palette.Ink, UiKit.Bold);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Label(words, Seen(p) + " · " + p.Int("coins") + " coins", UiKit.SmallSize, Palette.InkSoft);
            var badges = UiKit.Node("badges", r);
            UiKit.Column(badges, 4, null, TextAnchor.MiddleRight).childForceExpandWidth = false;
            Badges(badges, p);
            rows[id] = (Image)b.targetGraphic;
        }

        async void Select(string id)
        {
            selectedId = id;
            Highlight();
            Dictionary<string, object> reply;
            try { reply = await BBApi.AdminPlayer(id); }
            catch (BBApi.ApiError e) { if (this) Say(e.Message, true); return; }
            if (!this || selectedId != id) return;
            ShowPlayer(reply);
        }

        void Highlight()
        {
            foreach (var kv in rows) kv.Value.color = kv.Key == selectedId ? Palette.Amber.WithAlpha(.45f) : Palette.Cream;
        }

        void ShowEmptyDetail()
        {
            Clear(detail);
            var box = UiKit.Node("empty", detail);
            UiKit.Column(box, 8, new RectOffset(0, 0, 60, 0), TextAnchor.MiddleCenter);
            UiKit.Icon(box, "🔍", 56);
            UiKit.Label(box, "Pick a player to see their account", UiKit.HeadingSize - 2, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            UiKit.Label(box, "Search by name, or paste an id from the recent actions.", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
        }

        // ---- one player's card ----

        void ShowPlayer(Dictionary<string, object> reply)
        {
            Clock(reply);
            player = reply.Obj("player");
            Arrange();
            Clear(detail);
            if (player == null) { selectedId = null; ShowEmptyDetail(); return; }
            bool self = player.Str("id") == Settings.AccountId;
            string name = player.Str("name");

            if (!wide) UiKit.Secondary(detail, "‹  All players", BackToList, null, 44);
            Head(name);
            UiKit.Rule(detail);

            var chat = Group("Chat", self ? "You can’t mute yourself." : null);
            if (!self && Muted(player)) ActButton(chat, "Unmute", "unmute", null);
            else if (!self)
            {
                ActButton(chat, "Mute 15 min", "mute", Body("minutes", 15));
                ActButton(chat, "Mute 1 hour", "mute", Body("minutes", 60));
                ActButton(chat, "Mute 1 day", "mute", Body("minutes", 1440));
            }

            var town = Group("Town", self ? null : "A break sends them home and keeps them out of town (and signed out) until it ends.");
            if (!self) ActButton(town, "Send home now", "kick", null);
            if (Banned(player)) ActButton(town, "End the break", "unban", null);
            else if (!self)
            {
                Confirm(town, "Break 1 hour", "ban", Body("hours", 1));
                Confirm(town, "Break 1 day", "ban", Body("hours", 24));
                Confirm(town, "Break 1 week", "ban", Body("hours", 168));
                Confirm(town, "Break for good", "ban", new Dictionary<string, object> { ["permanent"] = true });
            }

            AccountGroup(name, self);
            History(player.Str("id"), reply.Arr("log"));
            VirtualCursor.FocusFirst(detail.GetComponentInChildren<Selectable>());
        }

        void Head(string name)
        {
            var head = UiKit.Node("head", detail);
            UiKit.Row(head, 18);
            var portrait = UiKit.Panel(head, "portrait", Palette.Cream, 16);
            UiKit.Size(portrait, 104, 104);
            var art = UiKit.Node("pet", portrait.transform).Fill(6).gameObject.AddComponent<Image>();
            art.sprite = PetSprites.For(player.Str("pet").Length > 0 ? player.Str("pet") : Buddy.GuestLook);
            art.preserveAspect = true;
            art.raycastTarget = false;
            var words = UiKit.Node("words", head);
            UiKit.Column(words, 6, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, -1, -1, 1);
            UiKit.Label(words, name, UiKit.TitleSize, Palette.Ink, UiKit.Title);
            var badges = UiKit.Node("badges", words);
            UiKit.Row(badges, 6);
            Badges(badges, player);
            UiKit.Label(words, $"Joined {Date(player.Num("created_at"))} · {Seen(player)} · {player.Int("coins")} coins", UiKit.SmallSize + 1, Palette.InkSoft);
            var idRow = UiKit.Node("id", words);
            UiKit.Row(idRow, 8);
            var id = UiKit.Label(idRow, "id " + player.Str("id"), UiKit.SmallSize, Palette.InkSoft);
            id.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text copied = null;
            var copy = UiKit.Secondary(idRow, "Copy id", () => { GUIUtility.systemCopyBuffer = player.Str("id"); copied.text = "Copied"; }, null, 44);
            copied = copy.GetComponentInChildren<Text>();
        }

        void AccountGroup(string name, bool self)
        {
            Group("Account", null);
            Field("New name", name, InputField.ContentType.Standard, "Rename", v => Run("rename", new Dictionary<string, object> { ["name"] = v }));
            Field("Coins", player.Int("coins").ToString(), InputField.ContentType.IntegerNumber, "Set coins", v =>
            {
                if (int.TryParse(v, out int coins) && coins >= 0) Run("coins", Body("coins", coins));
                else Say("Coins must be a whole number, 0 or more.", true);
            });
            Note(detail, "Setting coins adds one entry for the difference to their coin history, so the change shows up there.");
            if (self) { Note(detail, "You can’t change your own admin rights or delete your own account here."); return; }
            var grid = Grid(detail);
            bool admin = player.Truthy("is_admin");
            UiKit.ConfirmButton(grid, admin ? "Take admin away" : "Make admin", "Sure? Tap again", () => Run("admin", new Dictionary<string, object> { ["on"] = !admin }));
            UiKit.ConfirmButton(grid, "Delete account", $"Delete {name}?", () => Run("delete", null));
        }

        // a text box with a button after it ("New name [Pip] [Rename]")
        void Field(string label, string value, InputField.ContentType type, string button, System.Action<string> act)
        {
            var row = UiKit.Node(label, detail);
            UiKit.Row(row, 10);
            UiKit.Size(UiKit.Label(row, label, UiKit.SmallSize + 1, Palette.Ink, UiKit.Bold), 110);
            var field = UiKit.Input(row, label);
            field.contentType = type;
            field.characterLimit = 20;
            field.text = value;
            UiKit.Size(field, 120, 48, 1);
            UiKit.Secondary(row, button, () => act(field.text.Trim()));
        }

        void History(string id, List<object> log)
        {
            Group("History", null);
            if (log.Count == 0) { Note(detail, "No admin actions yet."); return; }
            foreach (Dictionary<string, object> e in log) LogRow(detail, e, false);
        }

        // ---- recent actions ----

        async void LoadLog()
        {
            Dictionary<string, object> reply;
            try { reply = await BBApi.AdminLog(); }
            catch (BBApi.ApiError e) { if (this) Say(e.Message, true); return; }
            if (!this) return;
            Clock(reply);
            Clear(logList);
            var log = reply.Arr("log");
            foreach (Dictionary<string, object> e in log) LogRow(logList, e, true);
            if (log.Count == 0) Note(logList, "Nothing yet. Actions you and other admins take show up here.");
        }

        // "12 min ago · Damp muted Pip for 15 min"; in the full list a row opens that player
        void LogRow(Transform parent, Dictionary<string, object> e, bool opens)
        {
            string target = e.Str("target_id");
            Transform row;
            if (opens && target.Length > 0 && e.Str("action") != "delete")
            {
                var b = UiKit.Button(parent, "entry", Palette.Cream, () => { ShowTab(0); Select(target); }, 12);
                UiKit.Outline(b, Palette.Ink.WithAlpha(.1f), 12, 1);
                row = b.transform;
            }
            else row = UiKit.Node("entry", parent);
            UiKit.Row((RectTransform)row, 14, new RectOffset(14, 14, 10, 10));
            UiKit.Size(row, -1, 52);
            UiKit.Size(UiKit.Label(row, Ago(e.Num("at")), UiKit.SmallSize, Palette.InkSoft), 120);
            var line = UiKit.Label(row, "", UiKit.BodySize - 1, Palette.InkSoft);
            line.supportRichText = true;
            line.text = $"<color=#3a2a1c>{Escape(e.Str("admin"))}</color> {Describe(e, true)}";
            UiKit.Size(line, -1, -1, 1);
        }

        /// <summary>"muted Pip for 15 min": what an admin did, from a log entry. With ink, names stand out.</summary>
        static string Describe(Dictionary<string, object> e, bool ink = false)
        {
            string who = ink ? $"<color=#3a2a1c>{Escape(e.Str("target"))}</color>" : e.Str("target"), d = e.Str("detail");
            switch (e.Str("action"))
            {
                case "mute": return $"muted {who} {d}";
                case "unmute": return $"unmuted {who}";
                case "ban": return $"gave {who} a break from town {d}";
                case "unban": return $"ended {who}’s break";
                case "kick": return $"sent {who} home";
                case "rename": return $"renamed {who} {d}";
                case "coins": return $"set {who}’s coins {d}";
                case "admin": return d == "on" ? $"made {who} an admin" : $"took {who}’s admin rights away";
                case "delete": return $"deleted {who}’s account";
                default: return $"{e.Str("action")} {who} {d}".Trim();
            }
        }

        // ---- running an action ----

        void ActButton(Transform grid, string label, string action, Dictionary<string, object> body) =>
            UiKit.Secondary(grid, label, () => Run(action, body), null, 44);

        void Confirm(Transform grid, string label, string action, Dictionary<string, object> body) =>
            UiKit.ConfirmButton(grid, label, "Sure? Tap again", () => Run(action, body), 44);

        async void Run(string action, Dictionary<string, object> body)
        {
            if (busy || player == null) return;
            busy = true;
            Say("Working…", false);
            string id = player.Str("id");
            try
            {
                var reply = await BBApi.AdminAct(id, action, body);
                if (!this) return;
                var log = reply.Arr("log");
                Say(log.Count > 0 ? Capital(Describe((Dictionary<string, object>)log[0])) + "." : "Done.", false, true);
                Sound.Play(action == "delete" || action == "ban" ? "close" : "pop");
                if (action == "coins" && id == Settings.AccountId) _ = Wallet.Refresh(); // your own coins: the HUD counts to the new number
                ShowPlayer(reply);
                Search();
            }
            catch (BBApi.ApiError e) { if (this) Say(e.Message, true); }
            finally { busy = false; }
        }

        // ---- small pieces ----

        // a heading, an optional line under it, and a wrapping grid for its buttons
        Transform Group(string title, string hint)
        {
            UiKit.Label(detail, title, UiKit.BodySize, UiKit.EmberInk, UiKit.Bold);
            if (hint != null) UiKit.Label(detail, hint, UiKit.SmallSize, Palette.InkSoft);
            return Grid(detail);
        }

        static Transform Grid(Transform parent)
        {
            var r = UiKit.Node("buttons", parent);
            var g = r.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(176, 44);
            g.spacing = new Vector2(8, 8);
            g.constraint = GridLayoutGroup.Constraint.Flexible;
            return r;
        }

        void Badges(Transform parent, Dictionary<string, object> p)
        {
            if (p.Str("id") == Settings.AccountId) UiKit.Badge(parent, "You", UiKit.LeafInk);
            if (p.Truthy("is_admin")) UiKit.Badge(parent, "Admin", UiKit.SkyInk);
            if (Muted(p)) UiKit.Badge(parent, "Muted · " + Left(p.Num("mute_until")), UiKit.EmberInk);
            if (Banned(p)) UiKit.Badge(parent, p.Num("ban_until") >= Forever ? "Banned for good" : "On a break · " + Left(p.Num("ban_until")), UiKit.RoseInk);
        }

        // a round initial in the pet's colour (lists skip the full pet art to stay quick)
        static void Avatar(Transform parent, Dictionary<string, object> p, float size)
        {
            float hue = 30;
            try { hue = (float)Json.ParseObject(p.Str("pet")).Num("h", 30); } catch (System.FormatException) { }
            var disc = UiKit.Panel(parent, "avatar", Color.HSVToRGB(Mathf.Repeat(hue, 360) / 360f, .35f, 1f), (int)(size / 2));
            disc.raycastTarget = false;
            UiKit.Size(disc, size, size);
            string name = p.Str("name");
            var t = UiKit.Label(disc.transform, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", UiKit.HeadingSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
            ((RectTransform)t.transform).Fill();
        }

        static Dictionary<string, object> Body(string key, int value) => new Dictionary<string, object> { [key] = (double)value };

        void Note(Transform parent, string text) => UiKit.Label(parent, text, UiKit.SmallSize + 1, Palette.InkSoft);

        void Say(string text, bool problem, bool good = false)
        {
            status.text = text;
            status.color = problem ? UiKit.RoseInk : good ? UiKit.LeafInk : Palette.InkSoft;
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        // ---- times (the server's clock, so "5 min left" is right on any device) ----

        void Clock(Dictionary<string, object> reply)
        {
            if (reply.Has("now")) clockOffset = reply.Num("now") - LocalNow;
        }

        static double LocalNow => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        double Now => LocalNow + clockOffset;

        bool Muted(Dictionary<string, object> p) => p.Num("mute_until") > Now;
        bool Banned(Dictionary<string, object> p) => p.Num("ban_until") > Now;

        string Seen(Dictionary<string, object> p)
        {
            double at = p.Num("last_seen");
            return at > 0 ? "seen " + Ago(at) : "never in town";
        }

        string Ago(double at)
        {
            double mins = (Now - at) / 60000;
            if (mins < 1) return "just now";
            if (mins < 60) return (int)mins + " min ago";
            if (mins < 48 * 60) return (int)(mins / 60) + " h ago";
            if (mins < 14 * 1440) return (int)(mins / 1440) + " days ago";
            return Date(at);
        }

        string Left(double until)
        {
            double mins = System.Math.Ceiling((until - Now) / 60000);
            if (mins < 60) return mins + " min left";
            if (mins < 48 * 60) return System.Math.Ceiling(mins / 60) + " h left";
            return System.Math.Ceiling(mins / 1440) + " days left";
        }

        static string Date(double ms) =>
            ms > 0 ? System.DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime().ToString("d MMM yyyy") : "—";

        static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;

        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        // ---- closing ----

        public void Close()
        {
            if (!this) return;
            UiStack.Remove(detailLevel);
            UiStack.Remove(this);
            Sound.Play("close");
            open = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            UiStack.Remove(detailLevel);
            UiStack.Remove(this);
            if (open == this) open = null;
        }
    }
}
