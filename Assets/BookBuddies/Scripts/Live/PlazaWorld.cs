using System.Collections.Generic;
using BookBuddies.Economy;
using BookBuddies.Pets;
using BookBuddies.Road;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Live
{
    /// <summary>
    /// The live town: your pet, everyone else's, and what happens between you.
    /// Walking, sitting, chat, emotes, tricks, cute interactions and finding coins all go through the
    /// same messages the website sends, so Unity players and website players share one town.
    /// Walking: tap to walk (with a dotted trail), hold to keep walking toward the pointer, or step tile by tile
    /// with keys or a stick. Whatever the pointer is on glows, and whatever your pet stands next to gets a prompt.
    /// On the road and in the caves, RoadFoes adds the villains.
    /// </summary>
    public sealed class PlazaWorld : MonoBehaviour
    {
        public static readonly string[] Emotes = { "❤️", "😂", "👋", "🎉", "😮", "📚", "✨", "💤" };
        public static readonly string[] Tricks = { "hop", "spin", "wave", "dance", "nap" };
        public static readonly string[] Interactions = { "hug", "boop", "play", "snack", "highpaw", "dance", "wave" };
        const float HeartbeatEvery = 1.4f;
        const int KeyboardStride = 2; // tiles ahead to aim for when walking with keys or a stick
        const float HoldDelay = .3f;      // seconds a press must last before it becomes hold-to-walk
        const float HoldRepath = .2f;     // seconds between re-aims while holding
        const float HoldSendEvery = .5f;  // the room allows 40 messages per 10 s, so held walks send "go" at most twice a second

        public TownMap Map { get; private set; }
        public PlazaNetwork Net { get; private set; }
        public PetActor Me { get; private set; }
        /// <summary>Every pet in town: yours, other readers', villagers and the storybook folk.</summary>
        public IEnumerable<PetActor> Actors
        {
            get
            {
                foreach (var a in actors.Values) yield return a;
                if (storyFolk) foreach (var a in storyFolk.Pets) yield return a;
            }
        }
        public string Area { get; private set; }
        public TownMap.Spot Hint { get; private set; }
        /// <summary>The villains, on the road and in the caves (null in town).</summary>
        public RoadFoes Road { get; private set; }
        /// <summary>What the mouse is on (a pet, seat, place or foe), or null.</summary>
        public Usable Hover { get; private set; }
        /// <summary>What E or A would use right now (the thing your pet stands next to), or null.</summary>
        public Usable Prompt { get; private set; }
        public TownView View => view;
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
        StoryFolk storyFolk; // a town's storybook characters (not part of the live room)
        readonly Dictionary<string, Vector2Int> items = new Dictionary<string, Vector2Int>();
        readonly HashSet<string> claimed = new HashSet<string>();
        readonly List<string> unseenNotes = new List<string>(); // messages that came while no HUD was up
        float heartbeatAt, villagerWanderAt;
        // walking with keys or a stick: a tap moves exactly one tile, holding keeps going, letting go stops on the next tile
        Vector2Int keyDir, keyStop;
        bool keyHeld, keyOwnsPath;
        // holding the pointer down on the world keeps walking toward it
        float pressAt = -1, holdRepathAt, goSentAt = -99;
        Vector2 pressFrom;
        Vector2Int holdTarget;
        bool holdWalking, goOwed;
        // hover and prompt
        Highlight ring;
        PathPreview trail;
        readonly Usable hoverSlot = new Usable(), promptSlot = new Usable();
        PetActor hoveredPet;
        RoadFoe hoveredFoe;

        /// <summary>Opens the town: your pet appears at "arrive" (or where you were, or the start) and the live room connects.</summary>
        public void Begin(TownMap map, TownView townView, TownCamera townCamera, string myName, string myLook, Vector2Int? arrive = null)
        {
            Map = map; view = townView; cam = townCamera;
            actorRoot = new GameObject("Pets").transform;
            actorRoot.SetParent(transform, false);
            Me = PetActor.Spawn(actorRoot, map, "me", myName, myLook, arrive.HasValue ? Arrival(arrive.Value) : StartPosition(), false, true);
            actors[Me.Id] = Me;
            if (map.Folk.Count > 0) storyFolk = StoryFolk.Spawn(actorRoot, map, Me);
            cam.Setup(map, Me.Pos);
            ring = Highlight.Create(transform);
            trail = PathPreview.Create(transform);
            if (map.Wild != null) Road = RoadFoes.Attach(this, view, cam);

            Net = new PlazaNetwork(map.Key);
            Net.Welcome += OnWelcome;
            Net.Message += OnMessage;
            Net.StateChanged += OnStateChanged;
            MyPets.ActiveChanged += OnPetChanged;
            if (Settings.SignedIn) Net.Start();
            else LocalVillagers();
        }

        public void End()
        {
            MyPets.ActiveChanged -= OnPetChanged;
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
            HandleHold();
            HandleKeys();
            if (goOwed && Time.time - goSentAt >= HoldSendEvery) SendGo();
            if (Live && Me.Walking && Time.time > heartbeatAt) { heartbeatAt = Time.time + HeartbeatEvery; SendGo(); }
            if (!Live && Time.time > villagerWanderAt) { villagerWanderAt = Time.time + 6; WanderVillagers(); }
            cam.Follow(Me.Pos, Time.deltaTime);
            CheckArea();
            PickUpItems();
            if (view.Life) view.Life.PetAt(Me.Pos);
            UpdateTargets();
        }

        // ---- input ----

        void HandlePointer()
        {
            if (!PlazaInput.Tapped(out var screen)) return;
            if (holdWalking || UiStack.Any) return; // the press turned into a held walk, or a card is up
            if (Road != null && Road.Tap(screen)) return; // foes first, like the site's wildTap

            var best = PetAt(screen, true);
            if (best == Me) { Trick(new[] { "hop", "twirl", "wiggle" }[Random.Range(0, 3)]); return; }
            if (best != null) { GoTo(best); ShowTrail(); return; }
            Vector2? hit = cam.ScreenToMap(screen);
            if (!hit.HasValue) return;

            int x = Mathf.FloorToInt(hit.Value.x), y = Mathf.FloorToInt(hit.Value.y);
            if (!Map.Inside(x, y)) return;
            foreach (var kv in items)
                if (Mathf.Abs(kv.Value.x - x) <= 1 && Mathf.Abs(kv.Value.y - y) <= 1) { view.TapRing(kv.Value.x, kv.Value.y); WalkTo(kv.Value.x, kv.Value.y); ShowTrail(); return; }
            var seat = Map.SeatAt(x, y);
            if (seat != null) { SitAt(seat); ShowTrail(); return; }
            var spot = Map.SpotAt(x, y);
            if (spot != null) { UseSpot(spot); ShowTrail(); return; }
            var to = PathFinder.NearestWalkable(Map, x, y);
            if (!to.HasValue) return;
            view.TapRing(to.Value.x, to.Value.y);
            if (WalkTo(to.Value.x, to.Value.y)) ShowTrail();
        }

        /// <summary>The pet nearest a screen point (within about a finger's width of its middle), or null.</summary>
        PetActor PetAt(Vector2 screen, bool includeMe)
        {
            PetActor best = null;
            float bestD = 60f * Screen.dpi / 160f + 30f;
            foreach (var a in Actors)
            {
                if (a.Gone || a.Hidden || (!includeMe && a == Me)) continue;
                Vector2 p = cam.Cam.WorldToScreenPoint(a.transform.position + a.transform.up * .55f);
                float d = (p - screen).magnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            return best;
        }

        void ShowTrail()
        {
            if (Me.Walking) trail.Show(Me.Shown, Me.Path);
        }

        /// <summary>
        /// Click-and-hold (or touch-and-hold) on the world: after a moment your pet keeps walking toward the
        /// pointer, re-aiming as it moves, and carries on to the last spot when you let go.
        /// </summary>
        void HandleHold()
        {
            bool held = WorldPointer.HeldOnWorld && !PlazaInput.Locked && !PlazaInput.MenuOpen && !UiStack.Any && (Road == null || !Road.Fighting);
            if (!held)
            {
                if (holdWalking) EndHold();
                pressAt = -1;
                return;
            }
            var at = WorldPointer.Position;
            if (pressAt < 0) { pressAt = Time.unscaledTime; pressFrom = at; return; }
            if (!holdWalking && Time.unscaledTime - pressAt < HoldDelay && (at - pressFrom).magnitude < 14) return;
            if (!holdWalking) { holdWalking = true; trail.Clear(); }
            if (Time.unscaledTime < holdRepathAt) return;
            var hit = cam.ScreenToMap(at);
            if (!hit.HasValue) return;
            var to = PathFinder.NearestWalkable(Map, Mathf.FloorToInt(hit.Value.x), Mathf.FloorToInt(hit.Value.y));
            if (!to.HasValue || (to.Value == holdTarget && Me.Walking)) return;
            holdRepathAt = Time.unscaledTime + HoldRepath;
            holdTarget = to.Value;
            WalkTo(to.Value.x, to.Value.y, null, true);
        }

        void EndHold()
        {
            holdWalking = false;
            if (goOwed) SendGo();
            if (Me.Walking) view.TapRing(holdTarget.x, holdTarget.y);
        }

        void HandleKeys()
        {
            var move = PlazaInput.Move();
            if (move != Vector2.zero) StepToward(move);
            else if (keyHeld) StopKeyWalk();

            if (PlazaInput.MenuOpen || UiStack.Any) return; // the shortcuts belong to the menu while it's up
            if (PlazaInput.Down(PlazaAction.Hop)) Trick("hop");
            for (int i = 0; i < Emotes.Length; i++)
                if (PlazaInput.Down(PlazaAction.Emote1 + i)) Emote(Emotes[i]);
        }

        /// <summary>
        /// Walk with keys or a stick. Each press promises exactly one more tile (so two quick taps move two tiles);
        /// holding keeps aiming a couple of tiles ahead, and only re-aims when the direction changes or the path runs short.
        /// </summary>
        void StepToward(Vector2 dir)
        {
            var d = new Vector2Int(Mathf.Abs(dir.x) > .38f ? (dir.x > 0 ? 1 : -1) : 0, Mathf.Abs(dir.y) > .38f ? (dir.y > 0 ? 1 : -1) : 0);
            if (d == Vector2Int.zero) return;
            bool fresh = !keyHeld || d != keyDir;
            if (!fresh && keyOwnsPath && Me.Path.Count > 1) return; // still on course
            var from = !fresh || (d == keyDir && keyOwnsPath && Me.Walking) ? (Me.Walking ? Me.Path[0] : Me.Tile) : NearestTile();
            var target = from;
            for (int i = 0; i < KeyboardStride; i++)
            {
                if (!CanStep(target, d)) break;
                target += d;
            }
            if (fresh) keyStop = CanStep(from, d) ? from + d : from;
            keyHeld = true;
            keyDir = d;
            if (target == from && !Me.Walking) { if (d.x != 0) Me.Dir = d.x; return; }
            if (target == from) return;
            WalkTo(target.x, target.y, null, true);
            keyOwnsPath = true;
        }

        /// <summary>Letting go: finish the promised tile, or the tile already being stepped into, and stop there.</summary>
        void StopKeyWalk()
        {
            keyHeld = false;
            if (!keyOwnsPath) return;
            int keep = Mathf.Max(Me.Path.IndexOf(keyStop), 0) + 1;
            if (Me.Path.Count > keep) { Me.Path.RemoveRange(keep, Me.Path.Count - keep); SendGo(); }
        }

        bool CanStep(Vector2Int at, Vector2Int d) =>
            Map.Walkable(at.x + d.x, at.y + d.y) && (d.x == 0 || d.y == 0 || (Map.Walkable(at.x + d.x, at.y) && Map.Walkable(at.x, at.y + d.y)));

        Vector2Int NearestTile() => new Vector2Int(Mathf.RoundToInt(Me.Pos.x - .5f), Mathf.RoundToInt(Me.Pos.y - .5f));

        /// <summary>E key or gamepad A: do the obvious thing nearby (what the prompt shows), or hop.</summary>
        public void UseNearby()
        {
            var near = Nearby(promptSlot);
            if (near == null) { Trick("hop"); return; }
            switch (near.Thing)
            {
                case PetActor pet: Greet(pet); break;
                case TownMap.Seat seat: SitAt(seat); break;
                case TownMap.Spot spot: if (!SpotActions.Use(spot)) SetHint(spot); break;
            }
        }

        // ---- what you could use: under the pointer, and next to your pet ----

        void UpdateTargets()
        {
            bool calm = !PlazaInput.Locked && !PlazaInput.MenuOpen && !PlazaInput.Typing && !UiStack.Any && (Road == null || !Road.Fighting);
            Hover = calm && PlazaInput.UsingPointer && !WorldPointer.IsTouch && !holdWalking ? Hovered(WorldPointer.Position) : null;
            var near = calm && !Me.Walking && !Me.Sitting ? Nearby(promptSlot) : null;
            Prompt = near != null && !(near.Thing is TownMap.Spot s && s == Hint) ? near : null; // the place card already says it

            var newPet = Hover?.Thing as PetActor;
            var newFoe = Hover?.Thing as RoadFoe;
            if (hoveredPet != newPet) { if (hoveredPet) hoveredPet.Hovered = false; hoveredPet = newPet; if (newPet) newPet.Hovered = true; }
            if (hoveredFoe != newFoe) { if (hoveredFoe) hoveredFoe.Hovered = false; hoveredFoe = newFoe; if (newFoe) newFoe.Hovered = true; }

            var glow = Hover ?? Prompt;
            if (glow != null) ring.Show(glow.At, glow.Ring, glow.Danger ? Palette.Ember : Palette.Amber);
            else ring.Hide();
        }

        Usable Hovered(Vector2 screen)
        {
            if (WorldPointer.OverUi(screen)) return null;
            var foe = Road != null ? Road.FoeAt(screen) : null;
            if (foe != null) return Describe(hoverSlot, foe);
            var pet = PetAt(screen, false);
            if (pet != null) return Describe(hoverSlot, pet);
            var hit = cam.ScreenToMap(screen);
            if (!hit.HasValue) return null;
            int x = Mathf.FloorToInt(hit.Value.x), y = Mathf.FloorToInt(hit.Value.y);
            var seat = Map.SeatAt(x, y);
            if (seat != null) return Describe(hoverSlot, seat);
            var spot = Map.SpotAt(x, y);
            return spot != null ? Describe(hoverSlot, spot) : null;
        }

        // the site's E: a pet within 1.9 tiles, else a seat next to you, else a place whose door you're beside
        Usable Nearby(Usable into)
        {
            PetActor near = null;
            float bestD = 1.9f;
            foreach (var a in Actors)
                if (a != Me && !a.Gone && !a.Hidden && Vector2.Distance(a.Pos, Me.Pos) < bestD) { bestD = Vector2.Distance(a.Pos, Me.Pos); near = a; }
            if (near != null) return Describe(into, near);
            var t = Me.Tile;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    var seat = Map.SeatAt(t.x + i, t.y + j);
                    if (seat != null) return Describe(into, seat);
                }
            var spot = Map.Spots.Find(s => Mathf.Abs(s.Use.x - t.x) <= 1 && Mathf.Abs(s.Use.y - t.y) <= 1);
            return spot != null ? Describe(into, spot) : null;
        }

        static readonly Dictionary<string, string> SeatIcons = new Dictionary<string, string> { ["bench"] = "🪑", ["chair"] = "☕", ["beanbag"] = "📖", ["hammock"] = "💤", ["blanket"] = "🧺" };

        static Usable Describe(Usable u, PetActor pet)
        {
            u.Kind = Usable.Kinds.Pet; u.Thing = pet;
            u.At = pet.Shown + new Vector2(0, .3f); u.Ring = new Vector2(.6f, .45f); u.Top = pet.HeadPoint;
            u.Icon = "👋"; u.Label = u.Verb = "Say hi";
            return u;
        }

        static Usable Describe(Usable u, TownMap.Seat seat)
        {
            u.Kind = Usable.Kinds.Seat; u.Thing = seat;
            u.At = new Vector2(seat.X + .5f, seat.Y + .5f); u.Ring = new Vector2(.62f, .5f);
            u.Top = TownMap.ToWorld(seat.X + .5f, seat.Y + .5f) + TownCamera.Facing * Vector3.up * 1.1f;
            u.Icon = SeatIcons.TryGetValue(seat.Kind, out var icon) ? icon : "🪑"; u.Label = u.Verb = "Sit";
            return u;
        }

        Usable Describe(Usable u, TownMap.Spot spot)
        {
            var a = spot.Area;
            u.Kind = Usable.Kinds.Spot; u.Thing = spot;
            u.At = new Vector2(a.xMin + (a.width + 1) / 2f, a.yMin + (a.height + 1) / 2f);
            u.Ring = new Vector2(Mathf.Min((a.width + 1) / 2f + .3f, 3), Mathf.Min((a.height + 1) / 2f + .3f, 2.2f));
            u.Top = view.MarkerPoint(spot);
            u.Icon = spot.Icon; u.Label = spot.Name;
            u.Verb = SpotActions.CanUse(spot) && !string.IsNullOrEmpty(spot.Verb) ? spot.Verb : "Look";
            return u;
        }

        static Usable Describe(Usable u, RoadFoe foe)
        {
            u.Kind = Usable.Kinds.Foe; u.Thing = foe;
            u.At = foe.Pos + new Vector2(0, .3f); u.Ring = new Vector2(.55f, .4f) * foe.Size;
            u.Top = foe.PointAt(-1.05f * foe.Size - .6f);
            u.Icon = "⚔️"; u.Label = u.Verb = "Fight";
            return u;
        }

        // ---- your pet ----

        /// <summary>
        /// Walks your pet to a tile (or the nearest walkable one). A step already under way is finished first,
        /// so turns happen on tile centres like the site's. "then" runs on arrival; "quiet" skips the can't-get-there toast.
        /// </summary>
        public bool WalkTo(int x, int y, System.Action then = null, bool quiet = false)
        {
            keyOwnsPath = false;
            var to = Map.Walkable(x, y) ? new Vector2Int(x, y) : PathFinder.NearestWalkable(Map, x, y);
            if (!to.HasValue) return false;
            var from = Me.Walking ? Me.Path[0] : Me.Tile;
            var path = PathFinder.Find(Map, from, to.Value);
            if (path == null) { if (!quiet) Notify("Can’t get there from here"); return false; }
            if (Me.Walking) path.Insert(0, from);
            if (Me.Sitting) Send("sit", "on", 0.0);
            Me.Walk(path);
            Me.OnArrived = then ?? Arrived;
            SetHint(null);
            if (path.Count == 0) { Me.OnArrived = null; (then ?? Arrived)(); return true; }
            if (holdWalking && Time.time - goSentAt < HoldSendEvery) goOwed = true;
            else SendGo();
            heartbeatAt = Time.time + HeartbeatEvery;
            return true;
        }

        /// <summary>Stops your pet on the tile it's stepping into (a fight or an ambush) and tells the room.</summary>
        public void Halt()
        {
            keyHeld = false;
            holdWalking = false;
            if (Me.Path.Count > 1) Me.Path.RemoveRange(1, Me.Path.Count - 1);
            Me.OnArrived = null;
            if (Me.Sitting) { Me.Sitting = false; Send("sit", "on", 0.0); }
            trail.Clear();
            SendGo();
        }

        /// <summary>
        /// Vanishes in a puff and reappears at a tile in this town (poofTo: riding to the lore stone of the town
        /// you're in), with the place's name as a banner.
        /// </summary>
        public void PoofTo(Vector2Int at, string label)
        {
            var to = PathFinder.NearestWalkable(Map, at.x, at.y);
            if (!to.HasValue) return;
            keyHeld = holdWalking = false;
            Me.Path.Clear();
            Me.OnArrived = null;
            if (Me.Sitting) { Me.Sitting = false; Send("sit", "on", 0.0); }
            trail.Clear();
            StartCoroutine(Reappear(to.Value, label));
        }

        System.Collections.IEnumerator Reappear(Vector2Int to, string label)
        {
            Fx.Poof(Me.Pos);
            yield return new WaitForSeconds(.26f);
            Me.Pos = new Vector2(to.x + .5f, to.y + .5f);
            Fx.Poof(Me.Pos);
            Me.Play("poofin", .6f);
            Send(new Dictionary<string, object> { ["t"] = "go", ["x"] = (double)to.x, ["y"] = (double)to.y, ["tx"] = (double)to.x, ["ty"] = (double)to.y });
            if (label != null) Banner?.Invoke(label, "");
            Arrived();
        }

        /// <summary>Walks to a place and shows its card (the compass uses this to lead you to the road).</summary>
        public void GoToSpot(TownMap.Spot spot) => UseSpot(spot);

        /// <summary>A short message over the chat bar (kept until the HUD is up when it comes during loading).</summary>
        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Toast != null) Toast(text);
            else if (!unseenNotes.Contains(text)) unseenNotes.Add(text);
        }

        /// <summary>The big title at the top of the screen.</summary>
        public void Announce(string title, string subtitle) => Banner?.Invoke(title, subtitle);

        public void ClearHint() => SetHint(null);

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

        // walk to a place and show its card; tapping it again while you're there uses it
        void UseSpot(TownMap.Spot spot)
        {
            void Show() { Me.Dir = spot.Area.center.x + .5f > Me.Pos.x ? 1 : -1; SetHint(spot); }
            if (Me.Tile == spot.Use && !Me.Walking) { if (Hint != spot || !SpotActions.Use(spot)) Show(); }
            else if (!WalkTo(spot.Use.x, spot.Use.y, Show)) SetHint(spot);
        }

        void GoTo(PetActor other)
        {
            var t = other.Tile;
            var near = PathFinder.NearestWalkable(Map, t.x + (Me.Pos.x < other.Pos.x ? -1 : 1), t.y) ?? PathFinder.NearestWalkable(Map, t.x, t.y + 1);
            void Face() { Me.Dir = other.Pos.x > Me.Pos.x ? 1 : -1; Greet(other); }
            if (!near.HasValue || Vector2.Distance(new Vector2(near.Value.x + .5f, near.Value.y + .5f), Me.Pos) < 1.2f) { Face(); return; }
            WalkTo(near.Value.x, near.Value.y, Face, true);
        }

        // a storybook character talks to you and you wave; anyone else opens their pet card
        void Greet(PetActor other)
        {
            if (!storyFolk || !storyFolk.Has(other)) { PetTapped?.Invoke(other); return; }
            storyFolk.Talk(other);
            Me.ShowEmote("👋");
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

        /// <summary>The player switched pets in the Pets screen: show the new one here and to everyone in town.</summary>
        void OnPetChanged(string look)
        {
            if (Me == null) return;
            Me.SetLook(look);
            var t = Me.Tile;
            Send(new Dictionary<string, object> { ["t"] = "hi", ["look"] = look, ["x"] = (double)t.x, ["y"] = (double)t.y });
        }

        void Send(string type, string key, object value) => Send(new Dictionary<string, object> { ["t"] = type, [key] = value });
        void Send(Dictionary<string, object> m) => Net?.Send(m);

        void SendGo()
        {
            goOwed = false;
            if (Me.Path.Count == 0) return;
            goSentAt = Time.time;
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
                    Wallet.Credited(coins, "town find", m.Int("bal", -1));
                    Notify(coins > 0 ? $"Found {(m.Str("k") == "bag" ? "a bag of coins" : m.Str("k") == "gift" ? "a gift box" : "a coin")}! +{coins} coins" : "Found it! You've hit today's coin limit.");
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
                    Notify(m.Str("msg"));
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
            // keep the step they're mid-way through, so a new course never yanks them back to a tile centre
            var from = e.Walking ? e.Path[0] : e.Tile;
            var path = PathFinder.Find(Map, from, to) ?? new List<Vector2Int> { to }; // nowhere to path: walk straight there rather than pop
            if (e.Walking) path.Insert(0, from);
            e.Walk(path);
            var real = PathFinder.Find(Map, new Vector2Int(Mathf.FloorToInt(start.x), Mathf.FloorToInt(start.y)), to);
            float left = PathFinder.Length(start, real ?? path) / PetActor.Speed - lag;
            float ours = PathFinder.Length(e.Pos, path);
            // catch up or hang back gently; the next message corrects what's left rather than a dash doing it
            e.Pace = left > .2f ? Mathf.Clamp(ours / (PetActor.Speed * left), .85f, 1.5f) : 1.5f;
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
            Notify(string.Format(said.TryGetValue(kind, out var line) ? line : "💛 {0} said hi", e.Name));
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
            if (state == LiveState.Elsewhere) Notify("You're in town on another device, so this one is exploring on its own.");
            if (state == LiveState.SentHome) Notify(string.IsNullOrEmpty(Net.Notice) ? "An admin asked you to take a short break from town." : Net.Notice);
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
            var folk = Townsfolk.For(Map.Key);
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

        /// <summary>Once the HUD is up after arriving: shows where you are, and any messages that came while loading.</summary>
        public void HudReady()
        {
            Area = null;
            var notes = unseenNotes.ToArray();
            unseenNotes.Clear();
            foreach (var note in notes) Notify(note);
        }

        // a new area shows its name and one of its lines; arriving (the first area after HudReady) shows the place's own title
        void CheckArea()
        {
            var t = Me.Tile;
            string area = Map.AreaAt(t.x, t.y);
            if (area == Area) return;
            bool arriving = Area == null;
            Area = area;
            var lines = Map.IsTown ? TownBook.Current.AreaLines(Map.Key, area) : new string[0];
            string line = lines.Length > 0 ? lines[Random.Range(0, lines.Length)] : area != Map.Name ? Map.Name : "";
            if (arriving) Banner?.Invoke(Map.Name, Tagline(area, line));
            else Banner?.Invoke(area, line);
            Changed?.Invoke();
        }

        // under a place's name as you arrive: a town's tagline, the two towns a road joins, or where in the caves you are
        string Tagline(string area, string line)
        {
            var town = TownBook.Current.Get(Map.Key);
            if (town != null && town.Sub.Length > 0) return town.Sub;
            var wild = Map.Wild;
            if (wild != null && wild.West != null && wild.East != null) return $"The road from {wild.West.Name} to {wild.East.Name}";
            return area != Map.Name ? area : line;
        }

        void SetHint(TownMap.Spot spot)
        {
            if (Hint == spot) return;
            Hint = spot;
            Changed?.Invoke();
        }

        // where you were last time in town; the road and caves always start at their entrance, out of harm's way
        Vector2 StartPosition()
        {
            if (Map.Wild != null) return Arrival(Map.Start);
            var saved = PlayerPrefs.GetString("bb.pos." + Map.Key, "");
            var xy = saved.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) && Map.Walkable(x, y)) return new Vector2(x + .5f, y + .5f);
            return new Vector2(Map.Start.x + .5f, Map.Start.y + .5f);
        }

        Vector2 Arrival(Vector2Int at)
        {
            var t = PathFinder.NearestWalkable(Map, at.x, at.y) ?? Map.Start;
            return new Vector2(t.x + .5f, t.y + .5f);
        }

        void SavePosition()
        {
            if (Me == null || Map == null || Map.Wild != null) return;
            PlayerPrefs.SetString("bb.pos." + Map.Key, Me.Tile.x + "," + Me.Tile.y);
        }
    }
}
