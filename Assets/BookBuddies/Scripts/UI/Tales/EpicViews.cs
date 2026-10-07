using System;
using System.Collections;
using BookBuddies.Pets;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The dungeon on the tale screen (the site's renderView for S.ep, tales.js T:1178-1240, T:1514-1526): it draws the view
    /// EpicFlow says is current into TaleScreen.Body (map, scene, encounter, d20 roll, gift, relic, the Long Read), passes
    /// the player's pick or Continue back to EpicFlow, saves the run after every step, and plays fights over the tale.
    /// Everything is a normal Button, so mouse, touch and the virtual cursor work; the first choice gets the focus.
    /// </summary>
    public sealed class EpicViews : MonoBehaviour
    {
        const float SpinSeconds = 3.3f, AutoGoSeconds = 5;

        TaleRun run;
        RectTransform body;
        Vector2 size;
        Action auto;          // the map's lone way on (pre-lair), taken by itself after AutoGoSeconds
        float autoLeft;
        Text autoLabel;

        /// <summary>Shows the run's dungeon in the open tale (starting its first saga when it has none); a saved fight replays from its start.</summary>
        public static void Show(TaleRun run)
        {
            var body = TaleScreen.Body;
            if (!body || run == null || run.Over) return;
            var v = body.GetComponent<EpicViews>();
            if (!v) v = body.gameObject.AddComponent<EpicViews>();
            v.run = run;
            v.body = body;
            if (run.Ep == null) { EpicFlow.Start(run); TaleStore.Save(run); }
            v.Step();
        }

        void Step()
        {
            if (run.Battle != null) Fight();
            else Render();
        }

        void Render()
        {
            Clear();
            size = body.rect.size;
            var v = EpicFlow.Current(run);
            TaleStore.SaveSoon(run);
            AutoSave.Now("page"); // every new page: a map, a scene, an encounter, a roll, a camp, a gift
            switch (v.K)
            {
                case "map": EpicMapView.Build(body, run, v, Pick, AutoGo, Played); break;
                case "scene": Scene(v); break;
                case "enc": Encounter(v); break;
                case "roll": StartCoroutine(Roll(v)); break;
                case "camp": EpicCamp.Build(body, run, v, () => Pick(0)); break;
                default: Gift(v); break;
            }
        }

        void Pick(int i)
        {
            EpicFlow.Choose(run, i);
            After();
        }

        void Continue()
        {
            EpicFlow.Continue(run);
            After();
        }

        // a Fate card was played: the run is saved and the view redrawn with the card's line (the site's sync + renderView)
        void Played()
        {
            TaleStore.Save(run);
            Render();
        }

        // after every step the run is saved: a fight is saved with run.Battle set before its setup, so a resumed tale replays it
        void After()
        {
            TaleStore.Save(run);
            if (run.Over) return;
            Step();
        }

        void Fight()
        {
            Clear();
            BattleSetup setup;
            try { setup = EpicBattle.Setup(run); }
            catch (Exception e) { Debug.LogException(e); Hiccup(); return; }
            var tale = run;
            TaleScreen.RunBattle(setup, o =>
            {
                if (!o.Won && o.Rounds == 0) { if (this) Hiccup(); return; } // the fight never got going: it stays saved, try again
                bool over = EpicFlow.Finish(tale, setup, o);
                TaleStore.Save(tale);
                // a defeat ends the run here, as on quit the tale screen may be gone before the battle hands its outcome back
                if (over) { TaleLife.End(tale, TaleLife.Defeat); TaleScreen.End(TaleLife.Defeat); }
                else if (this) Render();
            });
        }

        // the site's "The story hiccuped": the saved fight is tried again
        void Hiccup()
        {
            Clear();
            var col = Page(body, 620);
            BigIcon(col, "📖", 72);
            Title(col, "The story hiccuped", 34);
            Paragraph(col, "The page didn’t turn properly. Your tale is saved, so the fight can start again from the top.");
            Focus(UiKit.Primary(col, "Try again ›", Fight));
        }

        // ---- scene: a few lines that fade in one after another, then Continue (epScene, sceneView T:1525) ----

        void Scene(EpicView v)
        {
            var col = Page(body, 760);
            BigIcon(col, v.I, 84);
            Title(col, v.T, 38);
            for (int i = 0; i < v.Lines.Count; i++) FadeIn.On(Line(col, v.Lines[i], UiKit.BodySize + 1, Palette.Ink, true), .3f + i * .45f);
            PartyStrip(col, run);
            Focus(UiKit.Primary(Centered(col), "Continue ›", Continue));
        }

        // ---- encounter: the scene, then 2-3 things to do with their odds, and the Fate hand (encView T:1206-1211); a Story Master twist is gold-tinted ----

        void Encounter(EpicView v)
        {
            bool twist = v.Tp == "twist";
            var col = Page(body, 780);
            if (twist) Line(col, "🪶 The Story Master turns the page…", UiKit.BodySize, UiKit.EmberInk, true);
            BigIcon(col, v.I, 84);
            Title(col, v.T, 36);
            Paragraph(col, v.Text, UiKit.BodySize + 1, Palette.Ink);
            Paragraph(col, "What do you do? The pet best suited rolls a d20.", UiKit.SmallSize, Palette.InkSoft);
            Button first = null;
            for (int i = 0; i < v.Opts.Count; i++)
            {
                var o = v.Opts[i];
                int at = i;
                string chip = o.Chk == null ? "✓ Safe choice" : $"{EpicData.Current.Checks[o.Chk].n} · {JsMath.RoundI(EpicRooms.Chance(run.Ep, o) * 100)}% chance";
                var b = Option(col, o, chip, o.Chk == null ? UiKit.LeafInk : UiKit.SkyInk, () => Pick(at));
                if (twist) b.targetGraphic.color = Color.Lerp(Palette.Paper, EpicHand.Gold, .3f);
                if (first == null) first = b;
            }
            EpicHand.Build(col, run, Played);
            PartyStrip(col, run);
            Focus(first);
        }

        // ---- roll: the d20 spins for 3.3 s, slowing, then lands; the outcome and its effects (rollView T:1231-1240) ----

        IEnumerator Roll(EpicView v)
        {
            var col = Page(body, 720);
            if (v.Dm) Line(col, $"🪶 {v.T}", UiKit.SmallSize + 1, UiKit.EmberInk, true);
            else Paragraph(col, BattleText.Prose($"{v.I} {v.T}"), UiKit.SmallSize + 1, UiKit.EmberInk);
            Title(col, v.Ch, 36);
            RectTransform result = null, outcome;
            Image die = null; Text number = null;
            bool skip = false;
            if (v.Chk != null)
            {
                var holder = Centered(col);
                var dieButton = UiKit.Button(holder, "d20", Color.clear, () => skip = true, 70);
                UiKit.Size(dieButton, 170, 170);
                die = UiKit.Node("die", dieButton.transform).Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(160, 160)).gameObject.AddComponent<Image>();
                die.sprite = BattleArt.Die("plain");
                die.preserveAspect = true;
                die.raycastTarget = false;
                number = UiKit.Label(dieButton.transform, "?", 52, Color.white, UiKit.Title, TextAnchor.MiddleCenter);
                number.rectTransform.Pin(new Vector2(.5f, .5f), new Vector2(0, -6), new Vector2(160, 160));
                number.gameObject.AddComponent<Outline>().effectColor = Palette.Ink.WithAlpha(.8f);
                Paragraph(col, $"{v.Who} rolls {EpicData.Current.Checks[v.Chk].i} {EpicData.Current.Checks[v.Chk].n}", UiKit.BodySize, Palette.InkSoft);
                result = Centered(col);
                string verdict = v.Crit == 1 ? "Natural 20!" : v.Crit == -1 ? "Natural 1…" : v.Ok ? "Success!" : "Failed";
                UiKit.Label(result, $"{v.Roll} {(v.B >= 0 ? "+" : "−")} {Math.Abs(v.B)} = {v.Roll + v.B} against {v.Dc}", UiKit.BodySize, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Badge(result, verdict, v.Ok ? UiKit.LeafInk : UiKit.RoseInk);
                result.gameObject.SetActive(false);
                Focus(dieButton);
            }
            outcome = UiKit.Node("outcome", col);
            UiKit.Column(outcome, 10);
            Paragraph(outcome, BattleText.Prose(v.Text), UiKit.BodySize + 1, Palette.Ink);
            foreach (var l in v.Lines) Line(outcome, l, UiKit.BodySize, UiKit.EmberInk, false);
            var go = UiKit.Primary(Centered(outcome), "Continue ›", Continue);
            outcome.gameObject.SetActive(false);

            if (v.Chk != null && !GameSettings.ReduceMotion)
            {
                Sound.Play("tap");
                float gap = .06f, end = Time.unscaledTime + SpinSeconds;
                while (Time.unscaledTime < end && !skip)
                {
                    number.text = UnityEngine.Random.Range(1, 21).ToString();
                    die.rectTransform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-30f, 30f));
                    Sound.Play("tap", .3f);
                    float until = Time.unscaledTime + gap;
                    while (Time.unscaledTime < until && !skip) yield return null;
                    gap = Mathf.Min(.32f, gap * 1.12f);
                }
            }
            if (die)
            {
                number.text = v.Roll.ToString();
                die.rectTransform.localRotation = Quaternion.identity;
                die.sprite = BattleArt.Die(v.Ok ? "ok" : v.Crit == -1 ? "no" : "meh");
                result.gameObject.SetActive(true);
                Burst(die.rectTransform, v.Ok, v.Crit == 1);
                Sound.Play(v.Crit == 1 ? "rare" : v.Ok ? "pop" : "boop");
            }
            outcome.gameObject.SetActive(true);
            FadeIn.On(outcome, 0);
            Focus(go);
        }

        // ten sparks (eighteen on a natural 20) fly out of the die (dieBurst T:1222)
        static void Burst(RectTransform at, bool ok, bool nat)
        {
            for (int i = 0; i < (nat ? 18 : 10); i++)
            {
                var bit = UiKit.Icon(at, nat ? "✨" : ok ? "✨" : "💫", 22);
                bit.color = ok ? Color.white : new Color(1, .6f, .66f);
                float a = UnityEngine.Random.value * Mathf.PI * 2, d = 60 + UnityEngine.Random.value * (nat ? 110 : 60);
                KeyTween.Play(bit, .9f + UnityEngine.Random.value * .4f, BattleEase.Soft, 0, true, new Kf(0, 0, 0, 0, .4f), new Kf(1, Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0, 1.1f, 0));
            }
        }

        // ---- gifts: a boon after a win, a relic from a lair boss's hoard (choiceView T:1514-1522) ----

        void Gift(EpicView v)
        {
            bool boon = v.K == "boon";
            var col = Page(body, 760);
            BigIcon(col, boon ? "🏆" : "💎", 72);
            Title(col, boon ? "Victory!" : "A relic for the road", 40);
            Paragraph(col, boon ? $"+{v.Drops} ink drops. Pick a gift." : "The boss’s hoard holds three relics. Choose one to keep for the rest of this saga.", UiKit.BodySize + 1, Palette.InkSoft);
            foreach (var l in v.Lines) Line(col, l, UiKit.BodySize, UiKit.EmberInk, true);
            var gear = Centered(col);
            UiKit.Secondary(gear, "Gear up", () => TalesUi.OpenBag(), "🎒", 44);
            UiKit.Label(gear, "Equip new finds before you pick.", UiKit.SmallSize, Palette.InkSoft).horizontalOverflow = HorizontalWrapMode.Overflow;
            Button first = null;
            for (int i = 0; i < v.Opts.Count; i++)
            {
                int at = i;
                var b = Option(col, v.Opts[i], null, Palette.Ink, () => Pick(at));
                if (first == null) first = b;
            }
            PartyStrip(col, run);
            Focus(first);
        }

        // ---- the lone way on goes by itself (choicePhase's 5 s for a single map option, T:897) ----

        void AutoGo(Action go, Text label)
        {
            auto = go;
            autoLeft = AutoGoSeconds;
            autoLabel = label;
        }

        void Update()
        {
            if (run == null || !body) return;
            if ((body.rect.size - size).sqrMagnitude > 4 && run.Battle == null && !run.Over && EpicFlow.Current(run).K != "roll") Render();
            if (auto == null || TaleScreen.Paused) return;
            autoLeft -= Time.unscaledDeltaTime;
            if (autoLabel) autoLabel.text = $"The only way on. Going there in {Mathf.CeilToInt(Mathf.Max(0, autoLeft))}s…";
            if (autoLeft > 0) return;
            var go = auto;
            auto = null;
            go();
        }

        void Clear()
        {
            StopAllCoroutines();
            auto = null;
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
        }

        // ---- shared pieces (the map and the camp use them too) ----

        /// <summary>A scrolling page over parent with a centred column at most maxWidth wide (16 px gutters on small windows).</summary>
        internal static RectTransform Page(RectTransform parent, float maxWidth)
        {
            float w = parent.rect.width;
            int side = Mathf.RoundToInt(Mathf.Max(16, (w - maxWidth) / 2));
            var col = UiKit.ScrollColumn(parent, 14, new RectOffset(side, side, 28, 36), out _);
            ((RectTransform)col.parent).Fill();
            return col;
        }

        /// <summary>A row that centres what is put in it.</summary>
        internal static RectTransform Centered(RectTransform parent)
        {
            var r = UiKit.Node("row", parent);
            UiKit.Row(r, 12, null, TextAnchor.MiddleCenter);
            return r;
        }

        internal static void BigIcon(RectTransform parent, string emoji, float size)
        {
            var row = Centered(parent);
            UiKit.Icon(row, emoji, size);
        }

        internal static Text Title(RectTransform parent, string text, int size)
        {
            var t = UiKit.Label(parent, BattleText.Prose(UiKit.SplitEmoji(text, out _)), size, Palette.Ink, UiKit.Title, TextAnchor.MiddleCenter);
            return t;
        }

        internal static Text Paragraph(RectTransform parent, string text, int size = UiKit.BodySize, Color? color = null) =>
            UiKit.Label(parent, BattleText.Prose(UiKit.SplitEmoji(text, out _)), size, color ?? Palette.InkSoft, null, TextAnchor.MiddleCenter);

        /// <summary>A line whose first emoji becomes its icon ("💧 +40 ink drops"); centred or left-aligned.</summary>
        internal static RectTransform Line(RectTransform parent, string text, int size, Color color, bool center)
        {
            var row = UiKit.Node("line", parent);
            UiKit.Row(row, 10, null, center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            string words = BattleText.Prose(UiKit.SplitEmoji(text, out var icon));
            if (icon != null) UiKit.Icon(row, icon, size + 8);
            var t = UiKit.Label(row, words, size, color, null, center && icon == null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            UiKit.Size(t, -1, -1, 1);
            return row;
        }

        /// <summary>A choice card: big icon, name, line and an optional chip (odds, "Safe choice").</summary>
        internal static Button Option(RectTransform parent, EpicOpt o, string chip, Color chipInk, Action click)
        {
            var b = UiKit.Button(parent, o.N, Palette.Paper, click, 16);
            UiKit.Outline(b, Palette.Ink.WithAlpha(.12f), 16, 1);
            var r = (RectTransform)b.transform;
            UiKit.Row(r, 16, new RectOffset(18, 18, 14, 14));
            UiKit.Icon(r, o.I, 46);
            var text = UiKit.Node("text", r);
            UiKit.Column(text, 4);
            UiKit.Size(text, -1, -1, 1);
            UiKit.Label(text, BattleText.Prose(UiKit.SplitEmoji(o.N, out _)), UiKit.HeadingSize - 1, Palette.Ink, UiKit.Bold);
            if (!string.IsNullOrEmpty(o.D)) UiKit.Label(text, BattleText.Prose(UiKit.SplitEmoji(o.D, out _)), UiKit.SmallSize, Palette.InkSoft);
            if (chip != null)
            {
                var chipRow = UiKit.Node("chip", text);
                UiKit.Row(chipRow, 0);
                UiKit.Badge(chipRow, chip, chipInk);
            }
            return b;
        }

        /// <summary>The party: each pet with its level and an HP bar (napping pets dimmed).</summary>
        internal static void PartyStrip(RectTransform parent, TaleRun run)
        {
            var row = Centered(parent);
            foreach (var h in run.Party)
            {
                var chip = UiKit.Panel(row, h.Seed.Name, Palette.Paper, 14);
                chip.raycastTarget = false;
                UiKit.Row(chip.rectTransform, 10, new RectOffset(10, 14, 6, 6));
                var pet = UiKit.Node("pet", chip.transform).gameObject.AddComponent<Image>();
                pet.sprite = PetSprites.For(h.Seed.Look);
                pet.preserveAspect = true;
                pet.raycastTarget = false;
                pet.color = h.Ko ? new Color(1, 1, 1, .45f) : Color.white;
                UiKit.Size(pet, 46, 46);
                var text = UiKit.Node("text", chip.transform);
                UiKit.Column(text, 4);
                UiKit.Label(text, $"{BattleText.Prose(h.Seed.Name)} · Lv {h.Lvl}{(h.Ko ? " · napping" : "")}", UiKit.SmallSize, Palette.Ink, UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
                var track = UiKit.Panel(text, "hp", Palette.Ink.WithAlpha(.12f), 4);
                track.raycastTarget = false;
                UiKit.Size(track, 130, 8);
                var fill = UiKit.Panel(track.transform, "fill", Palette.Leaf, 4).rectTransform;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(Mathf.Clamp01((float)(h.Hp / Math.Max(1, TaleLife.MaxHp(run, h)))), 1);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
            }
        }

        internal static void Focus(Selectable first)
        {
            if (first) VirtualCursor.FocusFirst(first);
        }
    }

    /// <summary>Fades and lifts an element in after a delay (scale only, so layout groups keep their places).</summary>
    public sealed class FadeIn : MonoBehaviour
    {
        const float Seconds = .4f;
        CanvasGroup group;
        float startsAt;

        /// <summary>Fades c in, starting delay seconds from now.</summary>
        public static void On(Component c, float delay)
        {
            var f = c.gameObject.AddComponent<FadeIn>();
            f.group = c.GetComponent<CanvasGroup>();
            if (!f.group) f.group = c.gameObject.AddComponent<CanvasGroup>();
            f.group.alpha = 0;
            f.startsAt = Time.unscaledTime + (GameSettings.ReduceMotion ? 0 : delay);
        }

        void Update()
        {
            float k = UiKit.EaseOut((Time.unscaledTime - startsAt) / Seconds);
            group.alpha = k;
            float s = GameSettings.ReduceMotion ? 1 : Mathf.Lerp(.97f, 1, k);
            transform.localScale = new Vector3(s, s, 1);
            if (k >= 1) Destroy(this);
        }
    }
}
