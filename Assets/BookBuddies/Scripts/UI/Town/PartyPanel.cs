using System.Collections.Generic;
using BookBuddies.Live;
using BookBuddies.Local;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The party card in the town HUD (under the "where" card). In a hosted world everyone is in one party, led by the
    /// host: no invites. One row per member: their pet, their name (a crown for the leader), where they are ("Here", a
    /// town's name, or away), and Go to travel to them. Its header folds it to "Party · N" (remembered). It asks the world
    /// for the party on arriving, whenever the room nudges ("party"), when the connection changes, and every 15 seconds,
    /// never twice at once; then toasts who joined, who left and who just arrived here.
    /// </summary>
    public sealed class PartyPanel : MonoBehaviour
    {
        /// <summary>The card's width while unfolded (reference pixels), for the HUD's layout.</summary>
        public const float Width = 300;
        const float HeaderHeight = 44, RowHeight = 52, Picture = 40, ListPad = 8;
        const float RefetchEvery = 15, CheckEvery = .5f;
        const string FoldedKey = "bb.partyFolded";

        /// <summary>Whether you're in a party now: visiting a friend's world, or hosting your own.</summary>
        public static bool InWorld => Settings.IsWorld || (Settings.IsLocal && LocalHost.Running);

        sealed class Member { public string Id, Name, Look, Town; public bool Leader, Online; }

        // the last list, kept across towns so arriving somewhere new doesn't announce everyone again
        static readonly Dictionary<string, Member> known = new Dictionary<string, Member>();
        static string knownServer;

        PlazaWorld world;
        PlazaNetwork net;
        RectTransform card, list, view;
        LayoutElement cardSize, viewSize;
        Text title;
        RectTransform arrow;
        readonly List<Member> members = new List<Member>();
        string you, shown;
        bool folded, asking, again;
        float fetchAt, checkAt, top, floor;
        LiveState lastState;

        /// <summary>True while the card shows a party (the chat box offers party chat then).</summary>
        public bool Showing => members.Count > 0;

        public static PartyPanel Create(RectTransform parent, PlazaWorld world)
        {
            var bg = UiKit.Panel(parent, "party", Palette.Cream);
            var p = bg.gameObject.AddComponent<PartyPanel>();
            p.world = world;
            p.net = world.Net;
            p.Build(bg);
            return p;
        }

        // [🎉 Party · 3              ˅]
        // [(pet) Damp 👑           ]
        // [      Here               ]
        // [(pet) Pip         [ Go ] ]
        // [      Rosewater          ]
        void Build(Image bg)
        {
            card = bg.rectTransform.Pin(new Vector2(1, 1), Vector2.zero, new Vector2(Width, 10));
            UiKit.Column(card, 0, new RectOffset(6, 6, 0, 6));
            UiKit.Hug(card);
            cardSize = UiKit.Size(bg);
            UiKit.Outline(bg, Palette.Ink.WithAlpha(.08f), UiKit.CardRadius, 1);
            UiKit.Shadow(card, UiKit.CardRadius, 12, 4, .2f);

            var header = UiKit.Button(card, "Party", Color.clear, () => Fold(!folded), UiKit.CardRadius);
            header.navigation = new Navigation { mode = Navigation.Mode.None }; // like the HUD's other buttons
            UiKit.Row((RectTransform)header.transform, 8, new RectOffset(8, 10, 0, 0));
            UiKit.Size(header, -1, HeaderHeight);
            UiKit.Icon(header.transform, "🎉", 24);
            title = UiKit.Label(header.transform, "Party", UiKit.BodySize, Palette.Ink, UiKit.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Spacer(header.transform, 8);
            var holder = UiKit.Node("fold", header.transform);
            UiKit.Size(holder, 24, 24);
            arrow = UiKit.Arrow(holder, Palette.InkSoft);

            list = UiKit.ScrollColumn(card, 2, new RectOffset(0, 0, 0, 0), out _);
            view = (RectTransform)list.parent;
            viewSize = UiKit.Size(view, -1, 0);
            Fold(PlayerPrefs.GetInt(FoldedKey, 0) == 1);
            UiKit.Show(card, false);
        }

        void Start()
        {
            world.PartyChanged += Fetch;
            net.StateChanged += OnNetState;
            lastState = net.State;
            Fetch(); // arriving in town
        }

        // the world and its connection can go first when Play mode stops
        void OnDestroy()
        {
            if ((object)world != null) world.PartyChanged -= Fetch;
            if (net != null) net.StateChanged -= OnNetState;
        }

        // connecting, dropping or reconnecting: ask again (pongs repeat the same state, so only real changes count)
        void OnNetState(LiveState state)
        {
            if (state == lastState) return;
            lastState = state;
            Fetch();
        }

        void Fold(bool on)
        {
            folded = on;
            PlayerPrefs.SetInt(FoldedKey, on ? 1 : 0);
            UiKit.Show(view, !on);
            cardSize.preferredWidth = on ? -1 : Width; // folded, it hugs its one line
            arrow.localRotation = Quaternion.Euler(0, 0, on ? 0 : -90); // like a list's disclosure arrow: right when folded, down when open
        }

        /// <summary>Where the card goes: its top this far below the screen's top, its bottom at least this far above the screen's bottom.</summary>
        public void Fit(float top, float floor)
        {
            this.top = top;
            this.floor = floor;
        }

        void Update()
        {
            if (Time.unscaledTime >= checkAt)
            {
                checkAt = Time.unscaledTime + CheckEvery;
                if (!InWorld) { if (Showing) Show(null); } // your world closed, or you left the friend's
                else if (Time.unscaledTime >= fetchAt) Fetch();
            }
            if (!Showing) return;

            // the list scrolls when the screen is too short for everyone
            float room = ((RectTransform)card.parent).rect.height - top - floor - HeaderHeight - ListPad;
            float want = Mathf.Min(LayoutUtility.GetPreferredHeight(list), Mathf.Max(RowHeight, room));
            if (!Mathf.Approximately(viewSize.preferredHeight, want)) viewSize.preferredHeight = viewSize.minHeight = want;
            card.anchoredPosition = new Vector2(-Hud.Margin, -top);
        }

        // ---- asking the world ----

        async void Fetch()
        {
            if (!InWorld || !Settings.SignedIn) return;
            if (asking) { again = true; return; }
            asking = true;
            fetchAt = Time.unscaledTime + RefetchEvery;
            string server = Settings.Server;
            Dictionary<string, object> reply = null;
            try { reply = await BBApi.Party(); }
            catch (BBApi.ApiError) { }
            finally { asking = false; }
            if (!this) return;
            // no answer: a blip while the town is still connected keeps the card; a closed world takes it away
            if (Settings.Server == server && (reply != null || !net.IsLive)) Show(reply != null && reply.Truthy("party") ? reply : null);
            if (again) { again = false; Fetch(); }
        }

        void Show(Dictionary<string, object> party)
        {
            var now = new List<Member>();
            you = party.Str("you");
            foreach (var o in party.Arr("members"))
                if (o is Dictionary<string, object> m && m.Str("id").Length > 0)
                    now.Add(new Member
                    {
                        Id = m.Str("id"), Name = m.Str("name", "A friend"), Look = m.Str("look"), Town = m.Str("town"),
                        Leader = m.Truthy("leader"), Online = m.Truthy("online"),
                    });
            Announce(now);
            members.Clear();
            members.AddRange(now);
            Paint();
        }

        // toasts for what changed since the last list (none for the first one, or when the party ends)
        void Announce(List<Member> now)
        {
            bool first = knownServer != Settings.Server || known.Count == 0;
            if (first && now.Count > 1) // a banner the first time, so nobody wonders whether the party formed
            {
                var lead = now.Find(m => m.Leader);
                world.Announce(lead == null || lead.Id == you ? "🎉 Your party is together" : $"🎉 You’re in {lead.Name}’s party", $"{now.Count} in the party · the party card shows where everyone is");
            }
            if (!first && now.Count > 0)
            {
                foreach (var m in now)
                {
                    if (m.Id == you) continue;
                    if (!known.TryGetValue(m.Id, out var was)) world.Announce($"🎉 {m.Name} joined your party", $"{now.Count} in the party");
                    else if (Here(m) && !Here(was)) world.Notify($"🐾 {m.Name} is here");
                }
                foreach (var was in known.Values)
                    if (!now.Exists(m => m.Id == was.Id)) world.Notify($"👋 {was.Name} left the party");
            }
            known.Clear();
            foreach (var m in now) known[m.Id] = m;
            knownServer = Settings.Server;
        }

        bool Here(Member m) => m.Online && m.Town == world.Map.Key;

        // ---- the rows ----

        void Paint()
        {
            UiKit.Show(card, Showing);
            title.text = "Party · " + members.Count;
            string drawn = "";
            foreach (var m in members) drawn += $"{m.Id}|{m.Name}|{m.Look}|{m.Town}|{m.Leader}|{m.Online}\n";
            if (drawn == shown) return; // nothing to redraw
            shown = drawn;
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                var old = list.GetChild(i).gameObject;
                old.SetActive(false); // out of the layout now, gone at the end of the frame
                Destroy(old);
            }
            foreach (var m in members) Row(m);
        }

        void Row(Member m)
        {
            var row = UiKit.Node(m.Name, list);
            UiKit.Row(row, 10, new RectOffset(6, 6, 0, 0));
            UiKit.Size(row, -1, RowHeight);
            if (!m.Online) row.gameObject.AddComponent<CanvasGroup>().alpha = .55f; // away: softer

            var disc = UiKit.Panel(row, "disc", Palette.Paper, (int)(Picture / 2));
            disc.raycastTarget = false;
            UiKit.Size(disc, Picture, Picture);
            if (m.Look.Length > 0)
            {
                var art = UiKit.Node("pet", disc.transform).Fill(3).gameObject.AddComponent<Image>();
                art.sprite = PetSprites.For(m.Look);
                art.preserveAspect = true;
                art.raycastTarget = false;
            }

            var words = UiKit.Node("words", row);
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, 0, -1, 1);
            var line = UiKit.Node("name", words);
            UiKit.Row(line, 4);
            OneLine(line, m.Name, UiKit.BodySize - 1, Palette.Ink, UiKit.Bold);
            if (m.Leader) UiKit.Icon(line, "👑", 18);
            OneLine(words, Where(m), UiKit.SmallSize, Here(m) && m.Id != you ? UiKit.LeafInk : Palette.InkSoft, UiKit.Body);

            if (m.Online && m.Id != you && m.Town.Length > 0 && m.Town != world.Map.Key)
            {
                var go = UiKit.Primary(row, "Go", () => GoTo(m), null, 44);
                go.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }

        // a label kept to one line, cut short rather than pushing into the Go button
        static void OneLine(Transform parent, string text, int size, Color color, Font font)
        {
            var t = UiKit.Label(parent, text, size, color, font);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(t, -1, Mathf.Ceil(size * 1.3f));
        }

        string Where(Member m) =>
            m.Id == you ? "You" : !m.Online ? "Away" : Here(m) ? "Here" : m.Town.Length == 0 ? "Not in town" : Boot.PlaceName(m.Town);

        // off to a member's town (by train when it's a town, on foot to the road or the caves)
        void GoTo(Member m)
        {
            if (world.Road != null && world.Road.Fighting) { world.Notify("Finish the fight before you go"); return; }
            Boot.Travel(m.Town, null, $"Off to see {m.Name}", TownBook.Current.Get(m.Town) != null);
        }
    }
}
