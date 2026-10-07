using System.Collections.Generic;
using BookBuddies.Pets;
using BookBuddies.Road;
using BookBuddies.Tales;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// What a party's ready check says about the fight: the boss (icon, name, title, place, its line), the reader level
    /// it's meant for (Rec, else Level) and its foes' level, what it does (Lines, each may start with an emoji) and how
    /// much tougher it is for the party (Scale).
    /// </summary>
    public sealed class ReadyInfo { public string Icon, Name, Title, Place, Quote; public int Level, Rec; public List<string> Lines = new List<string>(); public string Scale; }

    /// <summary>One player on a ready check. State: 0 deciding, 1 ready, 2 not now, 3 in a fight.</summary>
    public sealed class ReadyMember { public string Pid, Name, Look; public bool Leader, Captain, You; public int State; }

    /// <summary>
    /// The party's ready check before a boss fight (PartyBattle): a card for everyone online with the boss, what it does,
    /// how much tougher it gets for the party, who's coming (each with a live chip: Ready, Deciding…, Not now, In a fight)
    /// and the wait draining away. Members answer Ready (A) or Not now (B, ✕ or the button); the captain waits, goes with
    /// whoever's ready once someone says not now or time runs out, or calls it off. Taps outside the card don't answer
    /// for you. It sits over every other card and goes by itself when a battle opens.
    /// </summary>
    public static class PartyReadySheet
    {
        static ReadyCard card;

        /// <summary>True while the card is up.</summary>
        public static bool Open => card != null;

        /// <summary>
        /// Opens the card (or brings an open one for the same fight up to date); seconds is the wait left. The captain gets
        /// "Call it off" and "Go with N ready" (live once anyone can't come or time ran out); everyone else "Not now" and
        /// "Ready", which give way to the roster once answered.
        /// </summary>
        public static void Show(ReadyInfo info, List<ReadyMember> members, float seconds, bool captain,
            System.Action ready, System.Action notNow, System.Action goAnyway, System.Action callOff)
        {
            if (card != null && card.Info.Name == info.Name && card.Captain == captain) { card.Refresh(members, seconds, CanGo(members, seconds)); return; }
            Close(null);
            card = TownSheets.ReadyCheck(info, members, seconds, captain, ready, notNow, goAnyway, callOff);
        }

        /// <summary>The live roster and time left, changed in place (canGoAnyway: the captain may go with whoever's ready).</summary>
        public static void Refresh(List<ReadyMember> members, float seconds, bool canGoAnyway)
        {
            if (card != null) card.Refresh(members, seconds, canGoAnyway);
        }

        /// <summary>Closes the card without answering, with a toast when given one ("Damp called it off").</summary>
        public static void Close(string toast)
        {
            if (card != null) card.Dismiss();
            card = null;
            if (!string.IsNullOrEmpty(toast)) Boot.World?.Notify(toast);
        }

        // the captain may go with whoever's ready once someone can't come or the wait is over
        internal static bool CanGo(List<ReadyMember> members, float seconds) => members.Exists(m => m.State >= 2) || seconds <= 0;

        internal static void Gone(ReadyCard c)
        {
            if (card == c) card = null;
        }
    }

    // The ready check's card, built from the town sheets' blocks (PartyReadySheet opens it, ReadyCard keeps it live).
    public static partial class TownSheets
    {
        const int ReadyOrder = 90; // over every other card (Tales screens stack up from 82), under the cursor

        // the head, who's asking, the boss as the card's hero, what it does, the party scaling and any danger; the
        // roster, the time bar and the buttons come from ReadyCard (the last two pinned under the part that scrolls)
        internal static ReadyCard ReadyCheck(ReadyInfo info, List<ReadyMember> members, float seconds, bool captain,
            System.Action ready, System.Action notNow, System.Action goAnyway, System.Action callOff)
        {
            var s = Open("⚔️", "Party battle");
            s.GetComponentInParent<Canvas>().sortingOrder = ReadyOrder;
            UiKit.Cover(s.transform, "hold", Color.clear, null, true).transform.SetSiblingIndex(1); // taps outside don't answer for you
            string lead = members.Find(m => m.Captain)?.Name ?? "Your party";
            Note(s.Card, captain ? "Asking the party. Whoever’s ready fights beside you."
                : $"{lead} is calling the party to fight. Say you’re ready and you’ll be brought in beside them.");
            var glow = ReadyBoss(s.Card, info);
            foreach (var line in info.Lines)
            {
                string text = UiKit.SplitEmoji(line, out var icon);
                Line(s.Card, icon, text);
            }
            if (!string.IsNullOrEmpty(info.Scale)) Callout(s.Card, "💪", info.Scale, UiKit.EmberInk);
            ReadyDanger(s.Card, info);
            UiKit.Rule(s.Card);
            var card = s.gameObject.AddComponent<ReadyCard>();
            card.Build(s, info, glow, members, seconds, captain, ready, notNow, goAnyway, callOff);
            s.Open();
            return card;
        }

        // the boss: its icon in a warm glow, its name in the title face, the level badge and where it waits, then its line
        static Image ReadyBoss(Transform card, ReadyInfo info)
        {
            var box = UiKit.Panel(card, "boss", Palette.Paper);
            UiKit.Column(box.rectTransform, 4, new RectOffset(18, 18, 10, 16), TextAnchor.UpperCenter);
            UiKit.Outline(box, Palette.Amber.WithAlpha(.55f), UiKit.CardRadius, 2);
            var art = UiKit.Node("art", box.transform);
            UiKit.Size(art, -1, 96);
            var glow = UiKit.Node("glow", art).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(176, 124)).gameObject.AddComponent<Image>();
            glow.sprite = UiKit.Glow;
            glow.color = Palette.Amber.WithAlpha(.55f);
            glow.raycastTarget = false;
            var icon = UiKit.Icon(art, info.Icon, 80);
            icon.rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 80));
            if (!icon.enabled) UiKit.Show(art, false); // no picture: no empty glow either
            UiKit.Label(box.transform, info.Name, UiKit.TitleSize, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            var about = UiKit.Node("about", box.transform);
            UiKit.Row(about, 8, null, TextAnchor.MiddleCenter);
            int need = info.Rec > 0 ? info.Rec : info.Level;
            if (need > 0) LevelBadge(about, need);
            string where = string.IsNullOrEmpty(info.Title) ? info.Place ?? "" : string.IsNullOrEmpty(info.Place) ? info.Title : info.Title + " · " + info.Place;
            if (where.Length > 0) UiKit.Label(about, where, UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold);
            if (!string.IsNullOrEmpty(info.Quote)) Quote(box.transform, info.Quote);
            return glow;
        }

        // the road's DangerAsk said on the card: the fight is above your reader level
        static void ReadyDanger(Transform card, ReadyInfo info)
        {
            int need = info.Rec > 0 ? info.Rec : info.Level, lv = RoadSpots.ReaderLevel;
            if (need <= lv) return;
            bool very = need >= lv + 3;
            string foes = info.Rec > 0 && info.Level > 0 ? $", with foes around level {info.Level}" : "";
            Callout(card, very ? "☠️" : "⚠️", $"Meant for reader level {need}{foes}. You’re level {lv}.", very ? UiKit.RoseInk : UiKit.EmberInk);
        }

        // a tinted strip with an icon and a bold line in its ink
        static void Callout(Transform card, string icon, string text, Color ink)
        {
            var strip = UiKit.Panel(card, "callout", ink.WithAlpha(.1f), 12);
            strip.raycastTarget = false;
            UiKit.Row(strip.rectTransform, 8, new RectOffset(12, 12, 8, 9));
            UiKit.Icon(strip.transform, icon, 22);
            UiKit.Size(UiKit.Label(strip.transform, text, UiKit.SmallSize + 1, ink, UiKit.Bold), -1, -1, 1);
        }
    }

    /// <summary>
    /// The ready check's live half: who's coming (a row each, its chip changing in place with a pop when someone's
    /// ready), the count, the time bar draining between updates (rose for the last ten seconds, full and green once
    /// everyone's ready), and the answer buttons, which give way to a line once you've said you're ready. Reduce motion
    /// keeps the colours and drops the pops, breathing and pulse.
    /// </summary>
    sealed class ReadyCard : MonoBehaviour
    {
        const float Disc = 40, RowHeight = 52, ChipWidth = 132, PopTime = .35f, Hurry = 10;
        static readonly string[] Words = { "Deciding…", "Ready", "Not now", "In a fight" };

        sealed class Member { public RectTransform Row, Tick; public CanvasGroup PetFade, NameFade; public Image Chip, Icon; public Text Label; public bool You; public int State = -1; public float Pop; }

        public ReadyInfo Info { get; private set; }
        public bool Captain { get; private set; }

        readonly Dictionary<string, Member> members = new Dictionary<string, Member>();
        Sheet sheet;
        Image glow, fill;
        Text count, time, status, goLabel;
        RectTransform rows, buttons, hints;
        Button go;
        System.Action ready, notNow, goAnyway, callOff;
        float total, left;
        int readyCount, secondsShown = -1;
        bool canGo, answered, settled, allReady;

        public void Build(Sheet s, ReadyInfo info, Image bossGlow, List<ReadyMember> list, float seconds, bool captain,
            System.Action onReady, System.Action onNotNow, System.Action onGo, System.Action onCallOff)
        {
            sheet = s;
            Info = info;
            Captain = captain;
            glow = bossGlow;
            ready = onReady;
            notNow = onNotNow;
            goAnyway = onGo;
            callOff = onCallOff;
            total = Mathf.Max(1, seconds);
            var closed = s.Closed;
            s.Closed = () => { Backed(); PartyReadySheet.Gone(this); closed?.Invoke(); };

            var head = UiKit.Node("who", s.Card);
            UiKit.Row(head, 8);
            UiKit.Size(UiKit.Label(head, "Who’s coming", UiKit.BodySize + 1, Palette.Ink, UiKit.Bold), -1, -1, 1);
            count = UiKit.Label(head, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleRight);
            count.horizontalOverflow = HorizontalWrapMode.Overflow;
            rows = UiKit.Node("roster", s.Card);
            UiKit.Column(rows, 4);
            Timer(s.Frame); // the clock and your answer stay in sight however tall the card is
            status = UiKit.Label(s.Frame, "", UiKit.SmallSize + 1, UiKit.LeafInk, UiKit.Bold, TextAnchor.MiddleRight);
            UiKit.Show(status, false);
            Buttons();
            Refresh(list, seconds, PartyReadySheet.CanGo(list, seconds));
        }

        // the wait as a bar that drains, and the seconds left beside it
        void Timer(Transform frame)
        {
            var row = UiKit.Node("time", frame);
            UiKit.Row(row, 12);
            var track = UiKit.Panel(row, "track", Palette.Ink.WithAlpha(.08f), 5);
            track.raycastTarget = false;
            UiKit.Size(track, -1, 10, 1);
            fill = UiKit.Panel(track.transform, "left", Palette.Amber, 5);
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            time = UiKit.Label(row, "", UiKit.SmallSize + 1, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleRight);
            UiKit.Size(time, 92);
        }

        // the captain: Call it off and Go; everyone else: Not now and Ready (the main button, where A lands)
        void Buttons()
        {
            buttons = UiKit.Node("buttons", sheet.Frame);
            UiKit.Row(buttons, 10, null, TextAnchor.MiddleRight);
            if (Captain)
            {
                UiKit.Secondary(buttons, "Call it off", () => Settle(callOff));
                go = UiKit.Primary(buttons, "", () => Settle(goAnyway));
                goLabel = go.GetComponentInChildren<Text>();
                sheet.First = go;
                Hints(("A", "Select"), ("B", "Call it off"));
                return;
            }
            UiKit.Secondary(buttons, "Not now", () => Answer(false));
            sheet.First = UiKit.Primary(buttons, "Ready", () => Answer(true));
            Hints(("A", "Ready"), ("B", "Not now"));
        }

        // the gamepad hints on the card's edge say what A and B do here
        void Hints(params (string button, string what)[] pairs)
        {
            var old = hints ? hints : sheet.Frame.Find("pad hints");
            if (old) Destroy(old.gameObject);
            hints = UiKit.PadHints(sheet.Frame, pairs);
        }

        // ---- answers ----

        // Ready keeps the card up to watch the others come in; Not now is done with it
        void Answer(bool yes)
        {
            if (answered) return;
            answered = true;
            if (!yes) { Settle(notNow); return; }
            ready?.Invoke();
            UiKit.Show(buttons, false);
            Hints(("B", "Close"));
            foreach (var m in members.Values) if (m.You) Set(m, 1);
            Status();
        }

        // a button's last word: done with the card
        void Settle(System.Action act)
        {
            settled = true;
            act?.Invoke();
            Dismiss();
        }

        // B, Esc or ✕: Not now (the captain calls it off), unless you've already answered
        void Backed()
        {
            if (settled) return;
            settled = true;
            if (Captain) callOff?.Invoke();
            else if (!answered) { answered = true; notNow?.Invoke(); }
        }

        /// <summary>Closes the card without answering (the fight started, was called off, or a new ask replaced it).</summary>
        public void Dismiss()
        {
            settled = true;
            if (sheet.IsOpen) sheet.Close();
        }

        void OnDestroy() => PartyReadySheet.Gone(this);

        // ---- the roster ----

        /// <summary>Brings the roster, the count, the time left and the captain's Go up to date, in place.</summary>
        public void Refresh(List<ReadyMember> list, float seconds, bool canGoAnyway)
        {
            left = Mathf.Max(0, seconds);
            canGo = canGoAnyway;
            readyCount = 0;
            var here = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (!members.TryGetValue(m.Pid, out var row)) members[m.Pid] = row = Row(m);
                row.Row.SetSiblingIndex(i);
                here.Add(m.Pid);
                Set(row, m.You && answered && m.State == 0 ? 1 : m.State); // your Ready shows before the party hears it
                if (row.State == 1) readyCount++;
            }
            foreach (var pid in new List<string>(members.Keys))
            {
                if (here.Contains(pid)) continue;
                members[pid].Row.gameObject.SetActive(false); // out of the layout now, gone at the end of the frame
                Destroy(members[pid].Row.gameObject);
                members.Remove(pid);
            }
            bool all = readyCount == list.Count && readyCount > 0;
            if (all && !allReady) Sound.Play("happy");
            allReady = all;
            count.text = all ? "All ready!" : $"{readyCount} of {list.Count} ready";
            count.color = all ? UiKit.LeafInk : Palette.InkSoft;
            secondsShown = -1;
            GoButton();
            Status();
        }

        // one member: their pet in a disc (an amber ring for you), their name (a crown for the party leader), who they
        // are to this fight, and their chip
        Member Row(ReadyMember m)
        {
            var row = UiKit.Node("member", rows);
            UiKit.Row(row, 10);
            UiKit.Size(row, -1, RowHeight);
            var r = new Member { Row = row, You = m.You };

            var disc = UiKit.Panel(row, "disc", Palette.Paper, (int)(Disc / 2));
            disc.raycastTarget = false;
            r.PetFade = disc.gameObject.AddComponent<CanvasGroup>();
            UiKit.Size(disc, Disc, Disc);
            if (m.You) UiKit.Outline(disc, Palette.Amber, (int)(Disc / 2), 2, 2);
            if (!string.IsNullOrEmpty(m.Look))
            {
                var art = UiKit.Node("pet", disc.transform).Fill(3).gameObject.AddComponent<Image>();
                art.sprite = PetSprites.For(m.Look);
                art.preserveAspect = true;
                art.raycastTarget = false;
            }

            var words = UiKit.Node("words", row);
            r.NameFade = words.gameObject.AddComponent<CanvasGroup>();
            UiKit.Column(words, 0, null, TextAnchor.MiddleLeft);
            UiKit.Size(words, 0, -1, 1);
            var line = UiKit.Node("name", words);
            UiKit.Row(line, 4);
            OneLine(line, m.Name, UiKit.BodySize, Palette.Ink, UiKit.Bold);
            if (m.Leader) UiKit.Icon(line, "👑", 18);
            string who = m.You && m.Captain ? "You called the fight" : m.You ? "You" : m.Captain ? "Called the fight" : "";
            if (who.Length > 0) OneLine(words, who, UiKit.SmallSize, m.You ? UiKit.EmberInk : Palette.InkSoft, UiKit.Body);

            r.Chip = UiKit.Panel(row, "chip", Color.clear, 15);
            r.Chip.raycastTarget = false;
            UiKit.Row(r.Chip.rectTransform, 6, new RectOffset(10, 12, 0, 0), TextAnchor.MiddleCenter);
            UiKit.Size(r.Chip, ChipWidth, 30);
            r.Tick = Tick(r.Chip.transform);
            r.Icon = UiKit.Icon(r.Chip.transform, null, 18);
            r.Label = UiKit.Label(r.Chip.transform, "", UiKit.SmallSize, Palette.InkSoft, UiKit.Bold, TextAnchor.MiddleCenter);
            r.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return r;
        }

        // a label kept to one line, cut short rather than pushing into the chip
        static void OneLine(Transform parent, string text, int size, Color color, Font font)
        {
            var t = UiKit.Label(parent, text, size, color, font);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Size(t, -1, Mathf.Ceil(size * 1.3f));
        }

        // a check mark of two rounded strokes (drawn like the close button's ✕)
        static RectTransform Tick(Transform parent)
        {
            var box = UiKit.Node("tick", parent);
            UiKit.Size(box, 14, 14);
            Stroke(box, new Vector2(-3.4f, -1.25f), 6.2f, -47.6f);
            Stroke(box, new Vector2(1.6f, .5f), 11.3f, 45.8f);
            return box;
        }

        static void Stroke(RectTransform box, Vector2 at, float length, float angle)
        {
            var bar = UiKit.Panel(box, "stroke", Palette.Cream, 2);
            bar.raycastTarget = false;
            bar.rectTransform.Pin(new Vector2(.5f, .5f), at, new Vector2(length, 3)).localRotation = Quaternion.Euler(0, 0, angle);
        }

        // a member's chip: Ready (solid leaf with a tick), Deciding… (soft, its hourglass breathing), Not now (muted, the
        // member dimmed beside it) or In a fight (ember); the chip itself always reads plainly
        void Set(Member m, int state)
        {
            if (m.State == state) return;
            bool first = m.State < 0;
            m.State = state;
            m.Chip.color = state == 1 ? UiKit.LeafInk : state == 3 ? UiKit.EmberInk.WithAlpha(.14f) : Palette.Ink.WithAlpha(state == 2 ? .05f : .08f);
            m.Label.text = Words[Mathf.Clamp(state, 0, Words.Length - 1)];
            m.Label.color = state == 1 ? Palette.Cream : state == 3 ? UiKit.EmberInk : Palette.InkSoft;
            UiKit.Show(m.Tick, state == 1);
            UiKit.SetIcon(m.Icon, state == 0 ? "⏳" : state == 3 ? "⚔️" : null);
            UiKit.Show(m.Icon, m.Icon.sprite != null);
            m.PetFade.alpha = m.NameFade.alpha = state == 2 ? .55f : 1;
            if (first || state != 1) return;
            m.Pop = 1;
            Sound.Play("pop");
        }

        // the captain's Go: waiting, then "Go with N ready" once it may go, and "All ready!" while the fight starts by itself
        void GoButton()
        {
            if (go == null) return;
            bool may = !allReady && (canGo || left <= 0);
            go.interactable = may;
            goLabel.text = allReady ? "All ready!" : !may ? "Waiting for answers" : readyCount <= 1 ? "Go alone" : $"Go with {readyCount} ready";
        }

        // after you've said ready: a line where the buttons were
        void Status()
        {
            if (!answered || Captain) return;
            status.text = allReady ? "All ready! Here we go…" : "You’re in. Waiting for the others…";
            UiKit.Show(status, true);
        }

        // ---- every frame ----

        void Update()
        {
            if (BattleScreen.Open) { Dismiss(); return; }
            bool calm = GameSettings.ReduceMotion;
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            bool waiting = left > 0;
            left = Mathf.Max(0, left - dt);
            if (waiting && left <= 0) GoButton();

            float k = allReady ? 1 : left / total;
            bool hurry = !allReady && left <= Hurry;
            fill.rectTransform.anchorMax = new Vector2(k, 1);
            UiKit.Show(fill, k > .02f);
            fill.color = (allReady ? Palette.Leaf : hurry ? Palette.Rose : Palette.Amber).WithAlpha(hurry && !calm ? .7f + .3f * (Mathf.Cos(t * Mathf.PI * 2) + 1) / 2 : 1);
            int secs = allReady ? 0 : Mathf.CeilToInt(left);
            if (secs != secondsShown)
            {
                secondsShown = secs;
                time.text = allReady ? "Starting" : secs > 0 ? secs + " s left" : "Time’s up";
                time.color = allReady ? UiKit.LeafInk : hurry ? UiKit.RoseInk : Palette.InkSoft;
            }

            // a swell when someone's ready, a slow breath on the hourglass while they decide, and the boss's glow breathing
            foreach (var m in members.Values)
            {
                m.Pop = Mathf.Max(0, m.Pop - dt / PopTime);
                float s = calm ? 1 : 1 + .12f * Mathf.Sin(m.Pop * Mathf.PI);
                m.Chip.rectTransform.localScale = new Vector3(s, s, 1);
                m.Icon.color = Color.white.WithAlpha(m.State == 0 && !calm ? .4f + .6f * (Mathf.Cos(t * Mathf.PI * 2 / 1.6f) + 1) / 2 : 1);
            }
            float b = calm ? 1 : 1 + .05f * Mathf.Sin(t * Mathf.PI * 2 / 2.8f);
            glow.rectTransform.localScale = new Vector3(b, b, 1);
        }
    }
}
