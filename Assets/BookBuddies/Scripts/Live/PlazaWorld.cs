using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Live
{
    /// <summary>
    /// The live town: your pet, everyone else's, and what happens between you.
    /// Walking, sitting, chat, emotes, tricks, cute interactions and finding coins all go through the
    /// same messages the website sends, so Unity players and website players share one town.
    /// </summary>
    public sealed class PlazaWorld : MonoBehaviour
    {
        public static readonly string[] Emotes = { "❤️", "😂", "👋", "🎉", "😮", "📚", "✨", "💤" };
        public static readonly string[] Tricks = { "hop", "spin", "wave", "dance", "nap" };
        public static readonly string[] Interactions = { "hug", "boop", "play", "snack", "highpaw", "dance", "wave" };
        const float HeartbeatEvery = 1.4f;
        const int KeyboardStride = 2; // tiles ahead to aim for when walking with keys or a stick

        public TownMap Map { get; private set; }
        public PlazaNetwork Net { get; private set; }
        public PetActor Me { get; private set; }
        public IEnumerable<PetActor> Actors => actors.Values;
        public string Area { get; private set; }
        public TownMap.Spot Hint { get; private set; }
        bool Live => Net != null && Net.IsLive;
        public readonly HashSet<string> Muted = new HashSet<string>();

        // things the HUD shows
        public event System.Action<string> Toast;
        public event System.Action<string, string> Banner;       // title, subtitle
        public event System.Action<string, string, bool> ChatLine; // who, text, isVillager
        public event System.Action<PetActor> PetTapped;
        public event System.Action Changed;                       // people count, live state or hint changed

        TownView view;
        TownCamera cam;
        Transform actorRoot;
        readonly Dictionary<string, PetActor> actors = new Dictionary<string, PetActor>();
        readonly Dictionary<string, Vector2Int> items = new Dictionary<string, Vector2Int>();
        readonly HashSet<string> claimed = new HashSet<string>();
        float heartbeatAt, villagerWanderAt;
        Vector2Int keyTarget = new Vector2Int(-1, -1);

        public void Begin(TownMap map, TownView townView, TownCamera townCamera, string myName, string myLook)
        {
            Map = map; view = townView; cam = townCamera;
            actorRoot = new GameObject("Pets").transform;
            actorRoot.SetParent(transform, false);
            Me = PetActor.Spawn(actorRoot, map, "me", myName, myLook, StartPosition(), false, true);
            actors[Me.Id] = Me;
            cam.Setup(map, Me.Pos);

            Net = new PlazaNetwork(map.Key);
            Net.Welcome += OnWelcome;
            Net.Message += OnMessage;
            Net.StateChanged += OnStateChanged;
            if (Settings.SignedIn) Net.Start();
            else LocalVillagers();
        }

        public void End()
        {
            if (Net == null) return;
            SavePosition();
            Net.Stop();
            Net = null;
        }

        void OnDestroy() => End();

        void Update()
        {
            if (Me == null || Net == null) return;
            Net.Update();
            HandlePointer();
            HandleKeys();
            if (Live && Me.Walking && Time.time > heartbeatAt) { heartbeatAt = Time.time + HeartbeatEvery; SendGo(); }
            if (!Live && Time.time > villagerWanderAt) { villagerWanderAt = Time.time + 6; WanderVillagers(); }
            cam.Follow(Me.Pos, Time.deltaTime);
            CheckArea();
            PickUpItems();
        }

        // ---- input ----

        void HandlePointer()
        {
            if (!PlazaInput.Tapped(out var screen)) return;
            Vector2? hit = cam.ScreenToMap(screen);

            // pets first, by how close they look on screen
            PetActor best = null;
            float bestD = 60f * Screen.dpi / 160f + 30f;
            foreach (var a in actors.Values)
            {
                if (a.Gone || a.Hidden) continue;
                Vector2 p = cam.Cam.WorldToScreenPoint(a.transform.position + a.transform.up * .55f);
                float d = (p - screen).magnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            if (best == Me) { Trick(new[] { "hop", "twirl", "wiggle" }[Random.Range(0, 3)]); return; }
            if (best != null) { GoTo(best); return; }
            if (!hit.HasValue) return;

            int x = Mathf.FloorToInt(hit.Value.x), y = Mathf.FloorToInt(hit.Value.y);
            if (!Map.Inside(x, y)) return;
            foreach (var kv in items)
                if (Mathf.Abs(kv.Value.x - x) <= 1 && Mathf.Abs(kv.Value.y - y) <= 1) { view.TapRing(kv.Value.x, kv.Value.y); WalkTo(kv.Value.x, kv.Value.y); return; }
            var seat = Map.SeatAt(x, y);
            if (seat != null) { SitAt(seat); return; }
            var spot = Map.SpotAt(x, y);
            if (spot != null) { UseSpot(spot); return; }
            var to = PathFinder.NearestWalkable(Map, x, y);
            if (!to.HasValue) return;
            view.TapRing(to.Value.x, to.Value.y);
            WalkTo(to.Value.x, to.Value.y);
        }

        void HandleKeys()
        {
            var move = PlazaInput.Move();
            if (move != Vector2.zero) StepToward(move);
            else if (keyTarget.x >= 0)
            {
                // key released: stop on the tile we're stepping into, not the lookahead target
                keyTarget = new Vector2Int(-1, -1);
                if (Me.Path.Count > 1) { Me.Path.RemoveRange(1, Me.Path.Count - 1); SendGo(); }
            }

            if (PlazaInput.Down(PlazaAction.Hop)) Trick("hop");
            for (int i = 0; i < Emotes.Length; i++)
                if (PlazaInput.Down(PlazaAction.Emote1 + i)) Emote(Emotes[i]);
        }

        /// <summary>Walk with keys or a stick: aim a few tiles ahead and re-aim as the direction changes.</summary>
        void StepToward(Vector2 dir)
        {
            int dx = Mathf.Abs(dir.x) > .38f ? (int)Mathf.Sign(dir.x) : 0;
            int dy = Mathf.Abs(dir.y) > .38f ? (int)Mathf.Sign(dir.y) : 0;
            if (dx == 0 && dy == 0) return;
            var here = Me.Tile;
            var target = here;
            for (int i = 0; i < KeyboardStride; i++)
            {
                var next = new Vector2Int(target.x + dx, target.y + dy);
                bool cornerOk = dx == 0 || dy == 0 || (Map.Walkable(target.x + dx, target.y) && Map.Walkable(target.x, target.y + dy));
                if (!Map.Walkable(next.x, next.y) || !cornerOk) break;
                target = next;
            }
            if (target == here) { Me.Dir = dx != 0 ? dx : Me.Dir; return; }
            bool sameDirection = keyTarget.x >= 0 && (keyTarget - here).x * dx >= 0 && (keyTarget - here).y * dy >= 0 && Mathf.Sign((keyTarget - here).x) == dx && Mathf.Sign((keyTarget - here).y) == dy;
            if (sameDirection && Me.Path.Count > 1) return;
            keyTarget = target;
            WalkTo(target.x, target.y, null, true);
        }

        /// <summary>E key or gamepad A: do the obvious thing nearby.</summary>
        public void UseNearby()
        {
            PetActor near = null;
            float bestD = 1.9f;
            foreach (var a in actors.Values)
                if (a != Me && !a.Gone && Vector2.Distance(a.Pos, Me.Pos) < bestD) { bestD = Vector2.Distance(a.Pos, Me.Pos); near = a; }
            if (near != null) { PetTapped?.Invoke(near); return; }
            var t = Me.Tile;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    var seat = Map.SeatAt(t.x + i, t.y + j);
                    if (seat != null) { SitAt(seat); return; }
                }
            var spot = Map.Spots.Find(s => Mathf.Abs(s.Use.x - t.x) <= 1 && Mathf.Abs(s.Use.y - t.y) <= 1);
            if (spot != null) { SetHint(spot); return; }
            Trick("hop");
        }

        // ---- your pet ----

        public bool WalkTo(int x, int y, System.Action then = null, bool quiet = false)
        {
            var to = Map.Walkable(x, y) ? new Vector2Int(x, y) : PathFinder.NearestWalkable(Map, x, y);
            if (!to.HasValue) return false;
            var path = PathFinder.Find(Map, Me.Tile, to.Value);
            if (path == null) { if (!quiet) Toast?.Invoke("Can’t get there from here"); return false; }
            if (Me.Sitting) Send("sit", "on", 0.0);
            Me.Walk(path);
            Me.OnArrived = then ?? Arrived;
            SetHint(null);
            if (path.Count == 0) { Me.OnArrived = null; (then ?? Arrived)(); return true; }
            SendGo();
            heartbeatAt = Time.time + HeartbeatEvery;
            return true;
        }

        void Arrived()
        {
            SavePosition();
            var t = Me.Tile;
            SetHint(Map.Spots.Find(s => s.Use == t));
        }

        void SitAt(TownMap.Seat seat)
        {
            void Sit()
            {
                Me.SitHere();
                if (seat.Facing != 0) Me.Dir = seat.Facing;
                Send("sit", "on", 1.0);
                var icon = new Dictionary<string, string> { ["bench"] = "🪑", ["chair"] = "☕", ["beanbag"] = "📖", ["hammock"] = "💤", ["blanket"] = "🧺" };
                Me.ShowEmote(icon.TryGetValue(seat.Kind, out var e) ? e : "✨");
            }
            if (Me.Tile == new Vector2Int(seat.X, seat.Y)) Sit();
            else WalkTo(seat.X, seat.Y, Sit);
        }

        void UseSpot(TownMap.Spot spot)
        {
            void Show() { Me.Dir = spot.Area.center.x + .5f > Me.Pos.x ? 1 : -1; SetHint(spot); }
            if (Me.Tile == spot.Use) Show();
            else if (!WalkTo(spot.Use.x, spot.Use.y, Show)) SetHint(spot);
        }

        void GoTo(PetActor other)
        {
            var t = other.Tile;
            var near = PathFinder.NearestWalkable(Map, t.x + (Me.Pos.x < other.Pos.x ? -1 : 1), t.y) ?? PathFinder.NearestWalkable(Map, t.x, t.y + 1);
            void Face() { Me.Dir = other.Pos.x > Me.Pos.x ? 1 : -1; PetTapped?.Invoke(other); }
            if (!near.HasValue || Vector2.Distance(new Vector2(near.Value.x + .5f, near.Value.y + .5f), Me.Pos) < 1.2f) { Face(); return; }
            WalkTo(near.Value.x, near.Value.y, Face, true);
        }

        public void Say(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > 140) text = text.Substring(0, 140);
            if (Live) Send("say", "text", text);
            else { Me.Say(text); ChatLine?.Invoke("You", text, false); }
        }

        public void Emote(string emoji)
        {
            Me.ShowEmote(emoji);
            Sound.Play("pop");
            Send("emo", "e", emoji);
        }

        /// <summary>hop, spin, wave, dance or nap (twirl and wiggle are sent as spin, like the site).</summary>
        public void Trick(string kind)
        {
            if (kind == "nap") Me.Nap();
            else Me.Play(kind, kind == "dance" ? 1.8f : kind == "wave" ? 1.2f : .9f);
            Send(new Dictionary<string, object> { ["t"] = "act", ["a"] = kind == "twirl" || kind == "wiggle" ? "spin" : kind, ["to"] = "" });
        }

        /// <summary>hug, boop, play (fetch), snack, highpaw, dance or wave with another pet.</summary>
        public void Interact(PetActor other, string kind)
        {
            if (other == null || other.Gone) return;
            void Go()
            {
                Me.Dir = other.Pos.x > Me.Pos.x ? 1 : -1;
                Me.Play(kind, kind == "hug" ? 1.3f : kind == "play" || kind == "dance" ? 1.8f : kind == "snack" ? 1.5f : kind == "wave" ? 1.2f : .9f);
                Send(new Dictionary<string, object> { ["t"] = "act", ["a"] = kind, ["to"] = other.Id });
                CuteFx(Me, other, kind);
                if (other.IsBot || !Live) ReplyAsVillager(other, kind);
            }
            if (Vector2.Distance(other.Pos, Me.Pos) < 1.8f) { Go(); return; }
            var t = other.Tile;
            var near = PathFinder.NearestWalkable(Map, t.x + (Me.Pos.x < other.Pos.x ? -1 : 1), t.y) ?? PathFinder.NearestWalkable(Map, t.x, t.y + 1);
            if (!near.HasValue) Go();
            else WalkTo(near.Value.x, near.Value.y, Go, true);
        }

        public void ToggleMute(PetActor other)
        {
            string key = string.IsNullOrEmpty(other.User) ? other.Name : other.User;
            if (!Muted.Remove(key)) { Muted.Add(key); other.ClearBubble(); }
        }

        public bool IsMuted(PetActor other) => Muted.Contains(string.IsNullOrEmpty(other.User) ? other.Name : other.User);

        static readonly Dictionary<string, string[]> CuteReplies = new Dictionary<string, string[]>
        {
            ["hug"] = new[] { "Aww, squish! 🤗", "Best hug ever 💛", "Hug buddies!" },
            ["boop"] = new[] { "Boop! 👃", "Hehe, that tickles!", "Boop right back!" },
            ["play"] = new[] { "Fetch! 🎾", "Again, again!", "Got it!" },
            ["snack"] = new[] { "Yum, thank you! 🍪", "Crumbs everywhere 😋", "Sharing is caring!" },
            ["highpaw"] = new[] { "Up top! 🐾", "Paw-some!", "Yesss!" },
            ["dance"] = new[] { "Let’s boogie! 💃", "Dance party!", "Look at us go!" },
            ["wave"] = new[] { "Hi hi! 👋", "Hello friend!", "Oh, hello!" },
        };

        void ReplyAsVillager(PetActor other, string kind)
        {
            other.Dir = Me.Pos.x > other.Pos.x ? 1 : -1;
            other.Sitting = false;
            string back = kind == "wave" || kind == "dance" || kind == "snack" || kind == "hug" ? kind : "happy";
            other.Play(back, 1.3f, .35f);
            var lines = CuteReplies.TryGetValue(kind, out var l) ? l : CuteReplies["wave"];
            other.Say(lines[Random.Range(0, lines.Length)]);
        }

        static void CuteFx(PetActor a, PetActor b, string kind)
        {
            Vector2 mid = (a.Pos + b.Pos) / 2;
            if (a.IsMe || b.IsMe) Sound.Play(kind == "hug" ? "hug" : kind == "boop" ? "boop" : kind == "snack" ? "munch" : "happy");
            switch (kind)
            {
                case "hug": Fx.Float("❤️", mid, 0, 1.8f); break;
                case "boop": Fx.Float("✨", Vector2.Lerp(a.Pos, b.Pos, .7f), .22f, .7f); break;
                case "play": Fx.Toss("🎾", a.Pos, b.Pos, 1.7f); break;
                case "snack": Fx.Toss("🍪", a.Pos, b.Pos, 1.5f); Fx.Float("❤️", b.Pos, 1.1f, 1.3f); break;
                case "highpaw": Fx.Float("🐾", mid, .38f, .9f); Fx.Float("✨", mid + new Vector2(0, -.3f), .4f, .8f); break;
                case "dance": Fx.Float("✨", mid, 0, 1.8f); break;
                default: Fx.Float("❤️", b.Pos, 0, 1.2f); break;
            }
        }

        // ---- network messages ----

        void Send(string type, string key, object value) => Send(new Dictionary<string, object> { ["t"] = type, [key] = value });
        void Send(Dictionary<string, object> m) => Net?.Send(m);

        void SendGo()
        {
            if (Me.Path.Count == 0) return;
            var last = Me.Path[Me.Path.Count - 1];
            Send(new Dictionary<string, object>
            {
                ["t"] = "go",
                ["x"] = System.Math.Round(Me.Pos.x - .5, 1), ["y"] = System.Math.Round(Me.Pos.y - .5, 1),
                ["tx"] = (double)last.x, ["ty"] = (double)last.y,
            });
        }

        void OnWelcome(Dictionary<string, object> m)
        {
            foreach (var a in new List<PetActor>(actors.Values)) if (a != Me) { a.Leave(); Destroy(a.gameObject); }
            actors.Clear();
            Me.Id = Net.MyId;
            actors[Me.Id] = Me;

            foreach (Dictionary<string, object> o in m.Arr("roster")) Join(o);
            view.ClearItems(); items.Clear(); claimed.Clear();
            foreach (Dictionary<string, object> it in m.Arr("items")) AddItem(it);
            for (int i = 0; i < Map.Plots.Count; i++) view.SetPlant(i, null, 0);
            foreach (Dictionary<string, object> g in m.Arr("garden")) ShowPlant(g);

            var t = Me.Tile;
            Send(new Dictionary<string, object> { ["t"] = "hi", ["look"] = Me.Look, ["x"] = (double)t.x, ["y"] = (double)t.y });
            SendGo();
            if (Me.Sitting) Send("sit", "on", 1.0);
            string theme = m.Str("th");
            if (theme.Length > 0) Banner?.Invoke(theme, "The town is decorated for the occasion");
            Changed?.Invoke();
        }

        PetActor Join(Dictionary<string, object> o)
        {
            string id = o.Str("id");
            int x = o.Int("x", -1), y = o.Int("y", -1), tx = o.Int("tx", -1), ty = o.Int("ty", -1);
            bool placed = x >= 0 && y >= 0;
            var start = placed ? new Vector2(x + .5f, y + .5f) : new Vector2(Map.Start.x + .5f, Map.Start.y + .5f);
            var a = PetActor.Spawn(actorRoot, Map, id, o.Str("n", "Reader"), o.Str("look"), start, o.Truthy("b"), false);
            a.User = o.Str("u");
            a.Hidden = !placed && !a.IsBot;
            actors[id] = a;
            if (o.Truthy("sit") && Map.SeatAt(tx, ty) != null) { a.Pos = new Vector2(tx + .5f, ty + .5f); a.Sitting = true; }
            else if (tx >= 0 && (tx != x || ty != y))
            {
                var path = PathFinder.Find(Map, a.Tile, new Vector2Int(tx, ty));
                if (path != null) a.Walk(path); else a.Pos = new Vector2(tx + .5f, ty + .5f);
            }
            return a;
        }

        void OnMessage(Dictionary<string, object> m)
        {
            string type = m.Str("t");
            actors.TryGetValue(m.Str("id"), out var e);
            switch (type)
            {
                case "join":
                    var p = m.Obj("p");
                    if (p == null) break;
                    if (actors.TryGetValue(p.Str("id"), out var old) && old != Me) Destroy(old.gameObject);
                    Join(p).ShowEmote("👋");
                    Changed?.Invoke();
                    break;
                case "leave":
                    if (e != null && e != Me) { e.ShowEmote("👋"); e.Leave(); actors.Remove(e.Id); Changed?.Invoke(); }
                    break;
                case "bye":
                    if (e != null && e != Me) { e.Leave(); actors.Remove(e.Id); Changed?.Invoke(); }
                    break;
                case "look":
                    if (e != null) e.SetLook(m.Str("look"));
                    break;
                case "go":
                    if (e != null && e != Me) NetGo(e, m);
                    break;
                case "sit":
                    if (e == null || e == Me) break;
                    if (!m.Truthy("on")) { e.Sitting = false; break; }
                    var end = e.Path.Count > 0 ? e.Path[e.Path.Count - 1] : e.Tile;
                    if (Map.SeatAt(end.x, end.y) != null) { if (e.Path.Count > 0) e.WantSit = true; else e.Sitting = true; }
                    break;
                case "emo":
                    if (e != null) e.ShowEmote(m.Str("e"));
                    break;
                case "act":
                    if (e != null) OnAct(e, m.Str("a"), m.Str("to"));
                    break;
                case "say":
                    if (e == null || Muted.Contains(string.IsNullOrEmpty(e.User) ? e.Name : e.User)) break;
                    e.Say(m.Str("text"));
                    if (e != Me && !e.IsBot && Vector2.Distance(e.Pos, Me.Pos) < 12) Sound.Play("chat", .6f);
                    ChatLine?.Invoke(e == Me ? "You" : e.Name, m.Str("text"), e.IsBot);
                    break;
                case "item":
                    AddItem(m.Obj("it"));
                    break;
                case "ix":
                    string id = m.Str("id");
                    view.RemoveItem(id); items.Remove(id);
                    if (actors.TryGetValue(m.Str("by"), out var finder) && finder != Me) finder.ShowEmote("🪙");
                    break;
                case "got":
                    Me.ShowEmote(m.Str("k") == "gift" ? "🎁" : "🪙");
                    Sound.Play(m.Str("k") == "gift" ? "rare" : "coin");
                    int coins = m.Int("coins");
                    Toast?.Invoke(coins > 0 ? $"Found {(m.Str("k") == "bag" ? "a bag of coins" : m.Str("k") == "gift" ? "a gift box" : "a coin")}! +{coins} coins" : "Found it! You've hit today's coin limit.");
                    break;
                case "g":
                    var plot = m.Obj("p");
                    if (plot != null) ShowPlant(plot);
                    if (m.Str("k") == "plant" && actors.TryGetValue(m.Str("by"), out var planter) && planter != Me) planter.ShowEmote("🌱");
                    break;
                case "theme":
                    if (m.Str("k").Length > 0) Banner?.Invoke("The town is decorated", m.Str("by").Length > 0 ? m.Str("by") + " decorated the town" : "");
                    break;
                case "ann":
                    Banner?.Invoke("📣 " + m.Str("text"), m.Str("n").Length > 0 ? "From " + m.Str("n") + ", town admin" : "");
                    break;
                case "clear":
                    foreach (var a in actors.Values) a.ClearBubble();
                    break;
                case "err":
                case "muted":
                    Toast?.Invoke(m.Str("msg"));
                    break;
                case "kicked":
                    Toast?.Invoke(m.Str("msg", "An admin sent you home for a little while."));
                    break;
            }
        }

        /// <summary>
        /// Someone else started walking. Path from where we're drawing them now and pace their stride so they
        /// arrive when the real pet does; only a pet that's 8+ tiles off pops into place. (Website build 502.)
        /// </summary>
        void NetGo(PetActor e, Dictionary<string, object> m)
        {
            var start = new Vector2((float)m.Num("x") + .5f, (float)m.Num("y") + .5f);
            var to = new Vector2Int(m.Int("tx"), m.Int("ty"));
            double at = m.Num("at");
            float lag = at > 0 ? Mathf.Clamp((float)(Net.ServerNow - at), 0, 1500) / 1000f : .12f;
            e.Sitting = false;
            e.Sleeping = false;
            if (e.Hidden || Vector2.Distance(e.Pos, start) > 8)
            {
                if (e.Hidden) { e.Hidden = false; e.Pos = start; }
                else e.Teleport(start);
            }
            var path = PathFinder.Find(Map, e.Tile, to);
            if (path == null) { e.Path.Clear(); e.Pace = 0; e.Pos = new Vector2(to.x + .5f, to.y + .5f); return; }
            e.Walk(path);
            var real = PathFinder.Find(Map, new Vector2Int(Mathf.FloorToInt(start.x), Mathf.FloorToInt(start.y)), to);
            float left = PathFinder.Length(start, real ?? path) / PetActor.Speed - lag;
            float ours = PathFinder.Length(e.Pos, path);
            e.Pace = left > .2f ? Mathf.Clamp(ours / (PetActor.Speed * left), .7f, 2.4f) : 2.4f;
        }

        void OnAct(PetActor e, string kind, string to)
        {
            if (kind == "nap") { e.Nap(); return; }
            if (kind == "sip" || kind == "read") return; // café cups and books are website-only for now
            actors.TryGetValue(to ?? "", out var target);
            var lengths = new Dictionary<string, float> { ["dance"] = 1.8f, ["wave"] = 1.2f, ["hug"] = 1.3f, ["play"] = 1.8f, ["snack"] = 1.5f };
            e.Play(kind, lengths.TryGetValue(kind, out var s) ? s : .9f);
            if (target == null) return;
            e.Dir = target.Pos.x > e.Pos.x ? 1 : -1;
            CuteFx(e, target, kind);
            if (target != Me)
            {
                target.Play(kind == "hug" || kind == "dance" || kind == "snack" ? kind : "happy", 1.3f, .3f);
                target.Dir = e.Pos.x > target.Pos.x ? 1 : -1;
                return;
            }
            var said = new Dictionary<string, string>
            {
                ["hug"] = "🤗 {0} gave you a big hug", ["boop"] = "👉 {0} booped your nose", ["play"] = "🎾 {0} wants to play fetch",
                ["snack"] = "🍪 {0} shared a snack with you", ["highpaw"] = "🐾 {0} gave you a high paw", ["dance"] = "💃 {0} is dancing with you",
                ["wave"] = "👋 {0} waved at you", ["hop"] = "❤️ {0} sent you love",
            };
            Toast?.Invoke(string.Format(said.TryGetValue(kind, out var line) ? line : "💛 {0} said hi", e.Name));
            Me.Dir = e.Pos.x > Me.Pos.x ? 1 : -1;
            Me.Play(kind == "hug" || kind == "dance" ? kind : "happy", 1.3f, .25f);
        }

        void OnStateChanged(LiveState state)
        {
            if (state != LiveState.Live && state != LiveState.Connecting)
            {
                view.ClearItems(); items.Clear();
                if (state != LiveState.Reconnecting) LocalVillagers();
            }
            if (state == LiveState.Elsewhere) Toast?.Invoke("You're in town on another device, so this one is exploring on its own.");
            if (state == LiveState.SentHome) Toast?.Invoke("An admin asked you to take a short break from town.");
            Changed?.Invoke();
        }

        // ---- coins, gifts, garden ----

        void AddItem(Dictionary<string, object> it)
        {
            if (it == null) return;
            string id = it.Str("id");
            var at = new Vector2Int(it.Int("x"), it.Int("y"));
            items[id] = at;
            view.AddItem(id, it.Str("k"), at.x, at.y);
        }

        void PickUpItems()
        {
            foreach (var kv in items)
            {
                if (claimed.Contains(kv.Key)) continue;
                if (Mathf.Abs(kv.Value.x + .5f - Me.Pos.x) < .75f && Mathf.Abs(kv.Value.y + .5f - Me.Pos.y) < .75f)
                {
                    claimed.Add(kv.Key);
                    Send("claim", "id", kv.Key);
                    return;
                }
            }
        }

        void ShowPlant(Dictionary<string, object> g)
        {
            int plot = g.Int("plot", -1);
            string seed = g.Str("seed", null);
            if (seed == null) { view.SetPlant(plot, null, 0); return; }
            view.SetPlant(plot, seed, GrowthStage(g));
        }

        // Hours each seed takes to grow, from the site's SEEDS list.
        static readonly Dictionary<string, float> SeedHours = new Dictionary<string, float>
        { ["bluebell"] = 2, ["tulip"] = 3, ["strawberry"] = 4, ["carrot"] = 5, ["sunflower"] = 6, ["pumpkin"] = 10 };

        /// <summary>Same growth maths as the site: watering moves a plant 30 minutes ahead.</summary>
        int GrowthStage(Dictionary<string, object> g)
        {
            float need = (SeedHours.TryGetValue(g.Str("seed"), out var h) ? h : 4) * 3600e3f;
            double got = Net.ServerNow - g.Num("planted_at") + g.Num("waters") * 30 * 60e3;
            return got >= need ? 3 : got >= need * .6 ? 2 : got >= need * .2 ? 1 : 0;
        }

        // ---- villagers when you're not connected ----

        void LocalVillagers()
        {
            foreach (var a in actors.Values) if (a.IsBot) return;
            var folk = Townsfolk.Villagers;
            for (int i = 0; i < folk.Length && Map.Seats.Count > 0; i++)
            {
                var seat = Map.Seats[(i * 5 + 3) % Map.Seats.Count];
                var v = PetActor.Spawn(actorRoot, Map, "v" + i, folk[i].name, folk[i].look, new Vector2(seat.X + .5f, seat.Y + .5f), true, false);
                v.Sitting = true;
                actors[v.Id] = v;
            }
            Changed?.Invoke();
        }

        void WanderVillagers()
        {
            foreach (var v in actors.Values)
            {
                if (!v.IsBot || v.Gone || Random.value < .7f || Map.Seats.Count == 0) continue;
                var seat = Map.Seats[Random.Range(0, Map.Seats.Count)];
                bool taken = false;
                foreach (var o in actors.Values) if (o != v && o.Tile == new Vector2Int(seat.X, seat.Y)) taken = true;
                if (taken) continue;
                var path = PathFinder.Find(Map, v.Tile, new Vector2Int(seat.X, seat.Y));
                if (path == null) continue;
                v.Walk(path);
                v.WantSit = true;
            }
        }

        // ---- small helpers ----

        public int PeopleHere()
        {
            int n = 0;
            foreach (var a in actors.Values) if (a != Me && !a.IsBot && !a.Gone) n++;
            return n;
        }

        /// <summary>Shows the banner for where you are again (after arriving, once the HUD is up).</summary>
        public void AnnounceArea() => Area = null;

        void CheckArea()
        {
            var t = Me.Tile;
            string area = Map.AreaAt(t.x, t.y);
            if (area == Area) return;
            Area = area;
            Banner?.Invoke(area, area != Map.Name ? Map.Name : "");
            Changed?.Invoke();
        }

        void SetHint(TownMap.Spot spot)
        {
            if (Hint == spot) return;
            Hint = spot;
            Changed?.Invoke();
        }

        Vector2 StartPosition()
        {
            var saved = PlayerPrefs.GetString("bb.pos." + Map.Key, "");
            var xy = saved.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) && Map.Walkable(x, y)) return new Vector2(x + .5f, y + .5f);
            return new Vector2(Map.Start.x + .5f, Map.Start.y + .5f);
        }

        void SavePosition()
        {
            if (Me == null || Map == null) return;
            PlayerPrefs.SetString("bb.pos." + Map.Key, Me.Tile.x + "," + Me.Tile.y);
        }
    }
}
