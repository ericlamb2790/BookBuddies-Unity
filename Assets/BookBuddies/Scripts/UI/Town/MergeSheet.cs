using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Economy;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    // Joining accounts: the confirm card (both sides, what joins up, whose adventure stays, the nest of 6), the adventure
    // question when entering town, and a buddy's own code. Town sheets over every other card, where taps outside don't answer.
    public static partial class TownSheets
    {
        const int MergeOrder = 92; // over the ready check (90) and the loading screen

        /// <summary>
        /// The join-accounts card for a /merge/preview plan: both sides, what joins up, and the choices when there are any
        /// (whose adventure stays, the nest of 6). Merge joins them and shows the login afterwards; Not now, B, Esc or ✕
        /// change nothing. A "switch" plan (two bookbuddies.pet accounts) offers signing in as the other one instead.
        /// done(true) once this device's account changed.
        /// </summary>
        internal static void JoinAccounts(MergePlan plan, string code, System.Action<bool> done)
        {
            bool swap = plan.Mode == "switch";
            Over(swap ? "🔁" : "🤝", swap ? "Another bookbuddies.pet account" : "Join accounts").gameObject.AddComponent<MergeCard>().Join(plan, code, done);
        }

        /// <summary>
        /// Which adventure stays, when entering town finds two (TalesSync.Settle): each with its class, Pet Lv and when it was
        /// last played, the suggested one picked. Keep this one answers "a" or "b"; B, Esc or ✕ keep the suggested one.
        /// </summary>
        internal static Task<string> AskAdventure(AdventureAsk ask) =>
            Over("📖", "Which adventure stays?").gameObject.AddComponent<MergeCard>().Adventure(ask);

        /// <summary>A buddy saved as its own account: the code that brings it back, with Copy. done runs once it's closed.</summary>
        internal static void BuddyCode(string name, string code, System.Action done)
        {
            var s = Over("🗝️", "Keep your buddy’s code");
            Note(s.Card, $"Your buddy {name} is saved with its own code. Keep it to come back to them.");
            CodeBox(s.Card, code);
            Note(s.Card, "You can find it again any time in Settings → Account.");
            var row = UiKit.Node("buttons", s.Frame);
            UiKit.Row(row, 10, null, TextAnchor.MiddleRight);
            CopyButton(row, code);
            s.First = UiKit.Primary(row, "I saved it", s.Close);
            var closed = s.Closed;
            s.Closed = () => { closed?.Invoke(); done?.Invoke(); };
            s.Open();
        }

        // a town sheet over everything else, where taps outside don't answer
        static Sheet Over(string icon, string title)
        {
            var s = Open(icon, title);
            s.GetComponentInParent<Canvas>().sortingOrder = MergeOrder;
            UiKit.Cover(s.transform, "hold", Color.clear, null, true).transform.SetSiblingIndex(1);
            return s;
        }

        // a recovery code in big letters, as the hatching card shows it
        static void CodeBox(Transform parent, string code)
        {
            var box = UiKit.Panel(parent, "code", Palette.Paper, 12);
            UiKit.Outline(box, Palette.Ink.WithAlpha(.12f), 12, 1);
            UiKit.Size(box, -1, 68);
            UiKit.Label(box.transform, code, 34, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter).rectTransform.Fill();
        }

        static Button CopyButton(Transform row, string code)
        {
            Text label = null;
            var b = UiKit.Secondary(row, "Copy", () => { GUIUtility.systemCopyBuffer = code; label.text = "Copied"; });
            label = b.GetComponentInChildren<Text>();
            return b;
        }

        // a typed code the way codes are shown: BB-XXXXX-XXXXX
        static string Pretty(string code)
        {
            var c = new System.Text.StringBuilder();
            foreach (char ch in code) if (char.IsLetterOrDigit(ch)) c.Append(char.ToUpperInvariant(ch));
            return c.Length == 12 ? $"{c.ToString(0, 2)}-{c.ToString(2, 5)}-{c.ToString(7, 5)}" : code.Trim();
        }

        static string Count(int n, string thing) => n == 1 ? "1 " + thing : $"{n} {thing}s";

        /// <summary>
        /// The join-accounts card's live half, and the adventure question's: picks change in place (whose adventure stays,
        /// like radio buttons; the nest's tiles), Merge works through its steps with the answers hidden and B
        /// waiting, and the card ends All set (with the login code when it changed) or with the problem and Try again, which
        /// sends the same request again. Pictures not drawn yet come one a frame. Reduce motion keeps the glow, the
        /// hourglass and the All set pop still.
        /// </summary>
        sealed class MergeCard : MonoBehaviour
        {
            const float Narrow = 560; // narrower screens stack the two sides
            const float SidePet = 72, RowPet = 52, TilePet = 56, PopTime = .45f;
            static readonly Vector2 TileSize = new Vector2(92, 100);

            // something to pick: a whose-adventure row ("from"/"into", or "a"/"b") or a pet's tile in the nest
            sealed class Pick { public Button Button; public Image Edge, Mark, Ring; public RectTransform Tick, Star; public string Side; public PetRow Pet; }

            Sheet sheet;
            Button close;
            MergePlan plan;
            AdventureAsk ask;
            TaskCompletionSource<string> answer;
            System.Action<bool> done;
            System.Action retry;
            Dictionary<string, object> request; // POST /merge's body: Try again sends it as it is
            string code, chosen;                // chosen: the adventure card's pick
            RectTransform body, sides, hero, status, buttons, hints, problem;
            CanvasGroup picks;
            Text say, count;
            Image glow, hourglass;
            GridLayoutGroup grid;
            readonly List<Pick> heroes = new List<Pick>(), nest = new List<Pick>();
            readonly Queue<(Image image, string look)> toDraw = new Queue<(Image, string)>();
            double now;
            float popAt = -1;
            bool narrow, working, joined, finished;

            static bool IsNarrow => Screen.width / UiKit.ScreenScale() < Narrow;
            int Columns => narrow ? 3 : 5;

            public void Join(MergePlan p, string typed, System.Action<bool> onDone)
            {
                Begin();
                plan = p;
                code = typed;
                done = onDone;
                if (plan.Mode == "switch") Switch();
                else Ready();
                Show();
            }

            public Task<string> Adventure(AdventureAsk q)
            {
                Begin();
                ask = q;
                chosen = q.Preselect;
                answer = new TaskCompletionSource<string>();
                Note(body, q.Lead);
                heroes.Add(Choose("a", q.A, q.NameA, q.AtA, q.AtB));
                heroes.Add(Choose("b", q.B, q.NameB, q.AtB, q.AtA));
                Note(body, "The other one’s renown is saved for its class, and its gear goes in your bag. Bags, keepsakes and library levels join up either way.");
                MarkHeroes();
                Buttons(("Keep this one", () => Keep(chosen), null));
                Hints(("A", "Select"), ("B", "Keep the suggested one"));
                Show();
                return answer.Task;
            }

            // the parts every state shares: the body that scrolls, and the working line over the answers
            void Begin()
            {
                sheet = GetComponent<Sheet>();
                close = sheet.Card.GetComponentInChildren<Button>(); // the head's ✕, the card's only button so far
                now = TalesSave.Clock();
                narrow = IsNarrow;
                body = UiKit.Node("body", sheet.Card);
                UiKit.Column(body, 12);
                picks = body.gameObject.AddComponent<CanvasGroup>();
                status = UiKit.Node("status", sheet.Frame);
                UiKit.Row(status, 8, null, TextAnchor.MiddleCenter);
                hourglass = UiKit.Icon(status, "⏳", 22);
                say = UiKit.Label(status, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
                UiKit.Show(status, false);
                var closed = sheet.Closed;
                sheet.Closed = () => { Closed(); closed?.Invoke(); };
            }

            void Show()
            {
                sheet.Open();
                UiStack.Push(sheet, Back); // B and Esc answer through the card instead of just closing it
            }

            // ---- the confirm card ----

            // the question over a warm glow, both sides, what joins up, and the choices when there are any
            void Ready()
            {
                glow = ReadyBoss(body, new ReadyInfo { Icon = "🤝", Name = Question() });
                hero = (RectTransform)glow.transform.parent.parent;
                UiKit.Label(hero, "Nothing changes until you tap Merge.", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
                Sides();
                Callout(body, "🐾", "Every pet comes along. None are left behind.", UiKit.LeafInk);
                Callout(body, "🪙", plan.CoinLine, UiKit.EmberInk);
                Callout(body, "🎒", "Each pet keeps its own adventure: level, class, gear, bag and road. Keepsakes, classes, library levels and the codex join up.", UiKit.SkyInk);
                Callout(body, "🗝️", plan.CodeIsLogin
                    ? $"From now on, sign in with {Pretty(code)}. Your old game code opens the same account too."
                    : $"You keep signing in with your own code. {plan.From.Name}’s old code opens this account too.", Palette.Ink);
                if (plan.AskNest) Nest();
                Buttons(("Not now", () => Finish(false), null), ("Merge", Merge, "🤝"));
                Hints(("A", "Merge"), ("B", "Not now"));
            }

            string Question() =>
                plan.Into.Website && plan.From.You ? "Merge your game buddy into your bookbuddies.pet account?"
                : plan.Into.You ? $"Bring {plan.From.Name} into your account?"
                : $"Merge this buddy into {plan.Into.Name}?";

            // from and into side by side, an arrow pointing into into (one over the other on a narrow screen)
            void Sides()
            {
                int at = -1;
                if (sides)
                {
                    at = sides.GetSiblingIndex();
                    sides.gameObject.SetActive(false);
                    Destroy(sides.gameObject);
                }
                sides = UiKit.Node("sides", body);
                if (at >= 0) sides.SetSiblingIndex(at);
                if (narrow) UiKit.Column(sides, 6, null, TextAnchor.UpperCenter);
                else UiKit.Row(sides, 8, null, TextAnchor.MiddleCenter).childForceExpandHeight = true;
                Side(plan.From);
                var way = UiKit.Node("way", sides);
                UiKit.Size(way, 28, 28);
                UiKit.Arrow(way, UiKit.EmberInk).localRotation = Quaternion.Euler(0, 0, narrow ? -90 : 0);
                Side(plan.Into);
            }

            // one account: where it is, its active pet, its name, then its pets, coins, adventure and when it was last played
            void Side(MergeSide side)
            {
                bool into = side == plan.Into;
                var panel = UiKit.Panel(sides, "side", into ? Palette.Amber.WithAlpha(.12f) : Palette.Paper);
                panel.raycastTarget = false;
                UiKit.Outline(panel, into ? Palette.Amber : Palette.Ink.WithAlpha(.12f), UiKit.CardRadius, into ? 2 : 1);
                UiKit.Column(panel.rectTransform, 4, new RectOffset(14, 14, 12, 14), TextAnchor.UpperCenter).childForceExpandWidth = false;
                if (!narrow) UiKit.Size(panel, 0, -1, 1);
                Chip(panel.transform, Where(side), WhereIcon(side));
                Picture(panel.transform, LookOf(side), SidePet);
                UiKit.Label(panel.transform, side.Name, UiKit.HeadingSize, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
                if (side.Website && side.Username.Length > 0) UiKit.Label(panel.transform, "@" + side.Username, UiKit.SmallSize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
                var facts = UiKit.Node("facts", panel.transform);
                UiKit.Column(facts, 2);
                Line(facts, "🐾", Count(side.Pets.Count, "pet"));
                Line(facts, "🪙", CoinPill.Format(side.Coins) + " coins");
                Line(facts, "⚔️", side.Save.HasHero ? $"Pet Lv {side.PetLv} · {Data.Class(side.ClassKey).Name}" : "No adventures yet");
                string ago = MergePlan.Ago(side.SaveAt, now);
                if (ago.Length > 0) Line(facts, "⏰", "Played " + ago);
            }

            static string Where(MergeSide side) => side.You ? "This game" : side.Website ? "bookbuddies.pet" : "Code you typed";

            static string WhereIcon(MergeSide side) => side.You ? "🎮" : side.Website ? "🌍" : "🗝️";

            static string LookOf(MergeSide side) => (side.Pets.Find(p => p.Active) ?? (side.Pets.Count > 0 ? side.Pets[0] : null))?.Look ?? Buddy.GuestLook;

            // the nest of 6: a tile per pet, picked ones ticked, the active one starred, the count on the right
            void Nest()
            {
                UiKit.Rule(body);
                var head = UiKit.Node("section", body);
                UiKit.Row(head, 8);
                UiKit.Icon(head, "🏡", 24);
                UiKit.Size(UiKit.Label(head, "Your nest", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold), -1, -1, 1);
                count = UiKit.Label(head, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleRight);
                count.horizontalOverflow = HorizontalWrapMode.Overflow;
                Note(body, $"{plan.From.Pets.Count + plan.Into.Pets.Count} pets in all. Pick up to {MergePlan.NestSize} for your nest. The rest nap at the Pet Inn, and you can swap them any time in Pets.");
                grid = UiKit.Node("nest", body).gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = TileSize;
                grid.spacing = new Vector2(8, 8);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = Columns;
                grid.childAlignment = TextAnchor.UpperCenter;
                foreach (var side in new[] { plan.From, plan.Into })
                    foreach (var pet in side.Pets) nest.Add(Tile(pet, side));
                MarkNest();
            }

            // a pet's tile: where it comes from in one corner, its pick in the other, its picture (starred when it'll be
            // active) and its name
            Pick Tile(PetRow pet, MergeSide side)
            {
                var b = UiKit.Button(grid.transform, pet.Name, Palette.Paper, () => Toggle(pet), 14);
                var t = new Pick { Button = b, Pet = pet, Edge = UiKit.Outline(b, Palette.Ink.WithAlpha(.12f), 14, 1) };
                var r = (RectTransform)b.transform;
                UiKit.Column(r, 2, new RectOffset(6, 6, 10, 6), TextAnchor.UpperCenter).childForceExpandWidth = false;
                var art = Picture(r, pet.Look, TilePet);
                t.Star = UiKit.Icon(art.transform, "⭐", 20).rectTransform.Pin(new Vector2(1, 0), new Vector2(6, -2), new Vector2(20, 20));
                var name = UiKit.Label(r, pet.Name, UiKit.SmallSize, Palette.Ink, UiKit.Bold, TextAnchor.MiddleCenter);
                name.verticalOverflow = VerticalWrapMode.Truncate;
                UiKit.Size(name, TileSize.x - 12, 20);
                var from = UiKit.Icon(r, WhereIcon(side), 18);
                UiKit.Size(from).ignoreLayout = true;
                from.rectTransform.Pin(new Vector2(0, 1), new Vector2(6, -6), new Vector2(18, 18));
                Mark(t, r, 20);
                UiKit.Size(t.Mark).ignoreLayout = true;
                t.Mark.rectTransform.Pin(new Vector2(1, 1), new Vector2(-6, -6), new Vector2(20, 20));
                return t;
            }

            void Toggle(PetRow pet)
            {
                if (!plan.Toggle(pet)) { Sound.Play("boop"); return; } // the last pet in the nest stays
                MarkNest();
            }

            // the picks in place: ticked and amber when in, the active one starred, the others dimmed once 6 are in
            void MarkNest()
            {
                bool full = plan.Nest.Count >= MergePlan.NestSize;
                foreach (var t in nest)
                {
                    bool on = plan.Nest.Contains(t.Pet);
                    Set(t, on);
                    UiKit.Show(t.Star, plan.Active == t.Pet);
                    t.Button.interactable = on || !full;
                }
                count.text = $"{plan.Nest.Count} of {MergePlan.NestSize}";
                count.color = full ? UiKit.LeafInk : Palette.InkSoft;
            }

            // Merge: coins banked and pet changes sent (4 s at most), this device's adventure up when it's from's (6 s), then
            // the join with the merged adventure; Try again comes back here and sends the same request
            async void Merge()
            {
                Work("Saving your coins…", Merge);
                CoinBank.EndSession();
                await Task.WhenAny(Task.WhenAll(CoinBank.SyncAll(), PetSync.Push(true, Settings.Local)), Task.Delay(4000));
                if (plan.From.You)
                {
                    Say("Saving your adventure…");
                    await TalesSync.Upload();
                }
                Say("Joining your accounts…");
                Dictionary<string, object> reply;
                try
                {
                    request ??= plan.Body(code, TalesSave.Clock());
                    reply = await BBApi.Merge(request);
                }
                catch (BBApi.ApiError e) { Failed(e); return; }
                catch (System.Exception e) { Debug.LogException(e); Failed(null); return; }
                Joined(reply);
                if (this) AllSet(reply.Obj("account"));
            }

            // this device follows the joined account (its sign-in already does, BBApi.Merge): its buddy and pets, the merged
            // adventure with from's pets under their ids here (it goes up again with them), a fresh offline profile next
            // time, and its coins
            void Joined(Dictionary<string, object> reply)
            {
                joined = true;
                var account = reply.Obj("account");
                Buddy.Save(account.Str("name"), account.Str("pet"));
                MyPets.Apply(account);
                TalesSave.Replace(MergePlan.Landed(request.Obj("tales").Obj("save"), reply.Arr("moved")));
                _ = TalesSync.Upload();
                Settings.SignOut(Settings.Local);
                _ = Wallet.Refresh();
                _ = CoinBank.Refresh();
            }

            // All set: who you are now, and the code to sign in with when that changed
            void AllSet(Dictionary<string, object> account)
            {
                Settle();
                Clear();
                glow = ReadyBoss(body, new ReadyInfo { Icon = "🎉", Name = "All set!" });
                hero = (RectTransform)glow.transform.parent.parent;
                popAt = Time.unscaledTime;
                int pets = plan.From.Pets.Count + plan.Into.Pets.Count;
                UiKit.Label(hero, $"You’re {account.Str("name", plan.Into.Name)} now, with {Count(pets, "pet")} and {CoinPill.Format(account.Int("coins", plan.CoinsTotal))} coins.",
                    UiKit.BodySize, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
                if (plan.CodeIsLogin)
                {
                    CodeBox(body, Pretty(code));
                    UiKit.Label(body, "Sign in with this code on any device, in the game or on bookbuddies.pet.", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Body, TextAnchor.MiddleCenter);
                }
                Buttons(("Done", () => Finish(true), null));
                if (plan.CodeIsLogin) CopyButton(buttons, Pretty(code)).transform.SetAsFirstSibling();
                Hints(("A", "Done"), ("B", "Done"));
                Sound.Play("happy");
                VirtualCursor.FocusFirst(sheet.First);
            }

            // ---- the switch card: two bookbuddies.pet accounts can't merge ----

            void Switch()
            {
                string name = plan.OtherName;
                UiKit.Label(body, $"{name} is a bookbuddies.pet account too, and two of those can’t be merged. You can sign in as {name} instead. This account stays as it is, with its own code.",
                    UiKit.BodySize, Palette.InkSoft);
                Buttons(("Not now", () => Finish(false), null), ($"Sign in as {name}", SignInInstead, null));
                Hints(("A", "Sign in"), ("B", "Not now"));
            }

            // signing out's two halves (TalesSync.SignedOut) around the sign-in: this account's adventure goes up while it's
            // still signed in, and aside once the other one is
            async void SignInInstead()
            {
                Work($"Signing in as {plan.OtherName}…", SignInInstead);
                await TalesSync.Upload(4);
                try { await BBApi.SignIn(code); }
                catch (BBApi.ApiError e) { Failed(e); return; }
                TalesSave.Stash();
                Buddy.Forget(); // your buddy comes from the server from now on
                joined = true;
                Finish(true);
            }

            // ---- the adventure card ----

            Pick Choose(string side, TalesSave save, string name, double at, double otherAt)
            {
                var cls = Data.Class(HeroFactory.ClassOf(save));
                string ago = MergePlan.Ago(at, now);
                var row = Choice(null, cls.Icon, name, $"Pet Lv {HeroFactory.Renown(save.Me.Rxp).lvl + 1} {cls.Name}" + (ago.Length > 0 ? " · played " + ago : ""),
                    at > otherAt, () => { chosen = side; MarkHeroes(); });
                row.Side = side;
                return row;
            }

            void Keep(string side)
            {
                answer.TrySetResult(side);
                Finish(false);
            }

            // ---- shared pieces ----

            // a row picked like a radio button: a pet (or an icon), a name over a line, "Most recent" when it is, and a ring
            // that fills with a tick once it's the pick
            Pick Choice(string look, string icon, string name, string sub, bool recent, System.Action act)
            {
                var b = UiKit.Button(body, name, Palette.Paper, act, 14);
                var p = new Pick { Button = b, Edge = UiKit.Outline(b, Palette.Ink.WithAlpha(.12f), 14, 1) };
                var r = (RectTransform)b.transform;
                UiKit.Row(r, 12, new RectOffset(12, 14, 8, 8));
                UiKit.Size(b, -1, RowPet + 16);
                if (look != null) Picture(r, look, RowPet);
                else UiKit.Icon(r, icon, 40);
                var words = UiKit.Node("words", r);
                UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
                UiKit.Size(words, 0, -1, 1);
                UiKit.Label(words, name, UiKit.BodySize, Palette.Ink, UiKit.Bold);
                UiKit.Label(words, sub, UiKit.SmallSize, Palette.InkSoft);
                if (recent) UiKit.Badge(r, "Most recent", UiKit.LeafInk);
                Mark(p, r, 26);
                return p;
            }

            void MarkHeroes()
            {
                foreach (var p in heroes) Set(p, p.Side == chosen);
            }

            // a pick's ring: empty, or amber with a tick when on
            static void Mark(Pick p, Transform parent, float size)
            {
                p.Mark = UiKit.Panel(parent, "pick", Color.clear, (int)(size / 2));
                p.Mark.raycastTarget = false;
                UiKit.Size(p.Mark, size, size);
                p.Ring = UiKit.Outline(p.Mark, Palette.Ink.WithAlpha(.3f), (int)(size / 2), 2);
                p.Tick = UiKit.Node("tick", p.Mark.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(14, 14));
                Stroke(p.Tick, new Vector2(-3.4f, -1.25f), 6.2f, -47.6f);
                Stroke(p.Tick, new Vector2(1.6f, .5f), 11.3f, 45.8f);
            }

            static void Stroke(RectTransform box, Vector2 at, float length, float angle)
            {
                var bar = UiKit.Panel(box, "stroke", Palette.Ink, 2);
                bar.raycastTarget = false;
                bar.rectTransform.Pin(new Vector2(.5f, .5f), at, new Vector2(length, 3)).localRotation = Quaternion.Euler(0, 0, angle);
            }

            // a pick shown on (amber, an amber edge, the ring ticked) or off (paper)
            static void Set(Pick p, bool on)
            {
                ((Image)p.Button.targetGraphic).color = on ? Palette.Amber.WithAlpha(.16f) : Palette.Paper;
                p.Edge.sprite = UiKit.Ring(14, on ? 2 : 1);
                p.Edge.color = on ? Palette.Amber : Palette.Ink.WithAlpha(.12f);
                p.Mark.color = on ? Palette.Amber : Color.clear;
                p.Ring.enabled = !on;
                UiKit.Show(p.Tick, on);
            }

            // a pet's picture: at once when it's drawn already, else next in line (one a frame)
            Image Picture(Transform parent, string look, float size)
            {
                var img = UiKit.Node("pet", parent).gameObject.AddComponent<Image>();
                UiKit.Size(img, size, size);
                img.preserveAspect = true;
                img.raycastTarget = false;
                if (PetSprites.Has(look)) img.sprite = PetSprites.For(look);
                else { img.enabled = false; toDraw.Enqueue((img, look)); }
                return img;
            }

            // the answers on the card's edge, under the part that scrolls: quiet ones first, the main one (where A lands) last
            void Buttons(params (string label, System.Action act, string icon)[] list)
            {
                if (buttons) { buttons.gameObject.SetActive(false); Destroy(buttons.gameObject); }
                buttons = UiKit.Node("buttons", sheet.Frame);
                UiKit.Row(buttons, 10, null, TextAnchor.MiddleRight);
                for (int i = 0; i < list.Length; i++)
                {
                    var (label, act, icon) = list[i];
                    if (i < list.Length - 1) UiKit.Secondary(buttons, label, act, icon);
                    else sheet.First = UiKit.Primary(buttons, label, act, icon);
                }
            }

            // what A and B do here, on the card's edge while a gamepad is in use
            void Hints(params (string button, string what)[] pairs)
            {
                var old = hints ? hints : sheet.Frame.Find("pad hints");
                if (old) Destroy(old.gameObject);
                hints = pairs.Length > 0 ? UiKit.PadHints(sheet.Frame, pairs) : null;
            }

            // working: the answers give way to a line saying what's happening, the picks hold still, and B waits
            void Work(string text, System.Action again)
            {
                working = true;
                retry = again;
                picks.interactable = false;
                Problem(null);
                UiKit.Show(buttons, false);
                UiKit.Show(close, false);
                Hints();
                Say(text);
            }

            void Say(string text)
            {
                say.text = text;
                UiKit.Show(status, true);
            }

            // done working: the line goes and the ✕ is back
            void Settle()
            {
                working = false;
                UiKit.Show(status, false);
                UiKit.Show(close, true);
            }

            // it didn't go through: what the server said, then Not now or Try again (the same request, safe to send again).
            // When the accounts changed since the preview, it looks again instead. A buddy that already joined someone else
            // leaves this device signed in there.
            void Failed(BBApi.ApiError e)
            {
                if (!this) return;
                if (e != null && (e.Why == "changed" || e.Why == "website" || e.Why == "same")) { Recheck(); return; }
                Settle();
                Problem(e == null ? "Something went wrong. Try again in a little while."
                    : e.Status == 404 && e.Why.Length == 0 ? "Joining accounts isn’t available yet. Try again after the next update."
                    : e.Message);
                if (e?.Why == "taken")
                {
                    joined = true;
                    Buttons(("Okay", () => Finish(true), null));
                    Hints(("A", "Okay"), ("B", "Okay"));
                }
                else
                {
                    Buttons(("Not now", () => Finish(false), null), ("Try again", retry, null));
                    Hints(("A", "Try again"), ("B", "Not now"));
                }
                VirtualCursor.FocusFirst(sheet.First);
            }

            // the accounts changed since the code was checked: check it again and start over on a fresh card
            async void Recheck()
            {
                Say("Something changed. Checking the code again…");
                MergePlan fresh;
                try { fresh = MergePlan.Read(await BBApi.MergePreview(code), TalesSave.Current, TalesSave.Clock()); }
                catch (BBApi.ApiError e) { request = null; Failed(e); return; }
                if (!this) return;
                if (fresh.Mode == "same")
                {
                    Settle();
                    Problem("That’s the code you’re signed in with already.");
                    Buttons(("Okay", () => Finish(false), null));
                    Hints(("A", "Okay"), ("B", "Okay"));
                    return;
                }
                var then = done;
                done = null;
                Finish(false);
                JoinAccounts(fresh, code, then);
            }

            // the problem in a rose strip over the answers (null: it goes)
            void Problem(string text)
            {
                if (problem) { problem.gameObject.SetActive(false); Destroy(problem.gameObject); problem = null; }
                if (text == null) return;
                Callout(sheet.Frame, "⚠️", text, UiKit.RoseInk);
                problem = (RectTransform)sheet.Frame.GetChild(sheet.Frame.childCount - 1);
                problem.SetSiblingIndex(status.GetSiblingIndex());
            }

            // a new body for the next state
            void Clear()
            {
                for (int i = body.childCount - 1; i >= 0; i--) { var c = body.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
                heroes.Clear();
                nest.Clear();
                toDraw.Clear();
                sides = null;
                grid = null;
                picks.interactable = true;
                Problem(null);
            }

            // ---- leaving ----

            // B or Esc: nothing while it works; the suggested adventure on the adventure card; otherwise done with the card
            void Back()
            {
                if (working) { UiStack.Push(sheet, Back); return; }
                if (ask != null) Keep(ask.Preselect);
                else Finish(joined);
            }

            // closed any way (the ✕ too): the adventure card keeps the suggested one if nothing was picked
            void Closed()
            {
                answer?.TrySetResult(ask.Preselect);
                Finish(joined);
            }

            void Finish(bool changed)
            {
                if (finished) return;
                finished = true;
                var then = done;
                done = null;
                if (sheet.IsOpen) sheet.Close();
                then?.Invoke(changed);
            }

            void OnDestroy() => answer?.TrySetResult(ask.Preselect); // the town never waits on a card that's gone

            // ---- every frame: one new picture, the layout when the screen narrows, the glow and the hourglass breathing ----

            void Update()
            {
                while (toDraw.Count > 0)
                {
                    var (img, look) = toDraw.Dequeue();
                    if (!img) continue;
                    img.sprite = PetSprites.For(look);
                    img.enabled = true;
                    break;
                }
                if (IsNarrow != narrow)
                {
                    narrow = !narrow;
                    if (sides) Sides();
                    if (grid) grid.constraintCount = Columns;
                }
                bool calm = GameSettings.ReduceMotion;
                float t = Time.unscaledTime;
                if (glow)
                {
                    float b = calm ? 1 : 1 + .05f * Mathf.Sin(t * Mathf.PI * 2 / 2.8f);
                    glow.rectTransform.localScale = new Vector3(b, b, 1);
                }
                if (working) hourglass.color = Color.white.WithAlpha(calm ? 1 : .4f + .6f * (Mathf.Cos(t * Mathf.PI * 2 / 1.6f) + 1) / 2);
                if (popAt < 0) return;
                float k = (t - popAt) / PopTime, s = calm || k >= 1 ? 1 : .9f + .1f * UiKit.EaseBack(k);
                hero.localScale = new Vector3(s, s, 1);
                if (k >= 1) popAt = -1;
            }
        }
    }
}
