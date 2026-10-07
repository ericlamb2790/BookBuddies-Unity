using BookBuddies.Pets;
using System;
using System.Collections;
using System.Collections.Generic;
using BookBuddies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The full-screen battle, wild or inside a tale (Setup.Tale: it draws over the tale, its subtitle is Setup.Place,
    /// "Act II · land", and the land's hazard sits under the title). It opens as a circle growing from where the pet met the foe, plays BattleEngine's
    /// steps through the stage (BattleStage, BattleUnitView), the effects (BattleFx) and the director (BattleDirector),
    /// and takes the player's say: lanes, the Ultimate, cheers, speed (1×, 2×, 4×, remembered), the bag and the fight log
    /// (both pause a fight of your own between events). At the end it grants renown and loot, shows the end card (which
    /// carries on by itself), fades out and hands the outcome to the road.
    /// A party fight (a link) is one fight on every player's screen: each game runs the same engine in step with the
    /// captain's game (its seeds, everyone's inputs, its state after each step), nothing pauses it, and each player
    /// still plays their own pet and gets their own rewards.
    /// Keys: 1 2 3 lanes, E Ultimate, F speed, Q cheer, I bag. Gamepad: A Ultimate (on the field), LB/RB lanes, Y speed,
    /// X cheer. B does nothing mid-fight (there's no fleeing) and keeps going on the end card.
    /// </summary>
    public sealed class BattleScreen : MonoBehaviour
    {
        const int CanvasOrder = 48;            // over the HUD and the road's wipe (45), under settings (70) and Tales screens (82+)
        const string SpeedKey = "bb.set.bspd"; // until GameSettings has a BattleSpeed
        const string LaneKeys = "lcr";
        static readonly string[] CheerEmoji = { "🔥", "💪", "👏", "😮", "😂" };

        /// <summary>True from Run until the outcome has been handed back (the road freezes its villains meanwhile).</summary>
        public static bool Open { get; private set; }

        BattleEngine engine;
        IBattleLink link;
        StepRng rng;
        Action<BattleOutcome> done;
        Vector2 from;
        RectTransform root, content, top;
        CanvasGroup group;
        BattleStage stage;
        BattleFx fx;
        BattleSound sound;
        BattleDirector director;
        BattleDock dock;
        BattleLog log;
        BattleSides sides;
        BattleFoeCard foeCard;
        int speed;
        float cheerReadyAt;
        char asked; // the lane you last asked a party fight's captain for
        bool lockedBefore, ending, finished, alone;

        BattleUnit Hero => engine.Me;
        string HeroName => BattleText.Prose(Hero?.Name ?? TalesUi.PetName);

        // a sheet, the bag or anything else is over the fight (a tale's own screen is under it)
        bool Covered => !ReferenceEquals(UiStack.Top, this) || TalesUi.AnyOpen && engine.Setup.Tale == null;

        // a fight of your own waits between events while it's covered; a party fight never waits
        bool Paused => link == null && Covered;

        // your inputs apply at once in a fight of your own (or one whose captain went quiet); otherwise they go to the captain
        bool Direct => link == null || alone;

        // a follower more than a step behind the captain plays at 4× until it has caught up
        int Pace => link != null && !link.Captain && !alone && link.Behind > 1 ? 4 : speed;

        // your lane as you've picked it: in a party fight, the one you asked for until the captain's step moves you there
        char LaneNow => !Direct && asked != '\0' && link.Pending("lane") ? asked : Hero?.Lane ?? 'c';

        /// <summary>
        /// Starts a fight from the road; from is the screen point the battle opens from. done gets the outcome once, after
        /// the end card closes (Rounds 0 means the fight never got going, e.g. it couldn't be set up). With a link it's a
        /// party fight (Setup.Party is set): the engine rolls on the captain's seeds and the link hears when the screen goes.
        /// </summary>
        public static void Run(BattleSetup setup, Vector2 from, Action<BattleOutcome> done, IBattleLink link = null)
        {
            if (Open) { link?.Closed(); done?.Invoke(Unstarted(setup)); return; } // one fight at a time
            AutoSave.BeforeFight();
            if (string.IsNullOrEmpty(setup.Look)) setup.Look = Buddy.Look;
            if (string.IsNullOrEmpty(setup.PetName)) setup.PetName = MyPets.ActiveName;
            var rng = link != null ? new StepRng(0) : null;
            BattleEngine engine;
            try { engine = new BattleEngine(setup, rng); }
            catch (Exception e) { Debug.LogException(e); link?.Closed(); done?.Invoke(Unstarted(setup)); return; }
            UiKit.EnsureEventSystem();
            var canvas = UiKit.MakeCanvas("Battle", OrderFor(setup));
            canvas.gameObject.AddComponent<BattleScreen>().Begin(engine, link, rng, from, done);
        }

        // a tale's fight goes just over the tale screen, so a bag opened from it still comes on top
        static int OrderFor(BattleSetup setup)
        {
            var tale = setup.Tale != null && TaleScreen.Body ? TaleScreen.Body.GetComponentInParent<Canvas>() : null;
            return tale ? tale.rootCanvas.sortingOrder + 1 : CanvasOrder;
        }

        static BattleOutcome Unstarted(BattleSetup s) => new BattleOutcome { HpFrac = s.HpFrac, Ink = s.Ink };

        void Begin(BattleEngine e, IBattleLink shared, StepRng steps, Vector2 point, Action<BattleOutcome> callback)
        {
            engine = e;
            link = shared;
            rng = steps;
            done = callback;
            from = point;
            Open = true;
            lockedBefore = PlazaInput.Locked;
            PlazaInput.Locked = true;
            speed = SavedSpeed();
            Build();
            UiStack.Push(this, Back);
            VirtualCursor.FocusFirst(dock.First);
            StartCoroutine(Flow());
        }

        void Build()
        {
            root = (RectTransform)transform;
            group = gameObject.AddComponent<CanvasGroup>();
            content = UiKit.Node("battle", root).Fill();
            sound = BattleSound.Create(gameObject);
            stage = BattleStage.Create(content, engine, u => () => ToggleFoeCard(u), link);
            stage.LaneTapped += LaneTapped;
            var over = UiKit.Node("over", content).Fill();
            fx = BattleFx.Create(stage, engine, sound, over);
            fx.Speed = speed;
            director = new BattleDirector(engine, stage, fx, sound);

            var hud = UiKit.Node("hud", content).Fill();
            hud.gameObject.AddComponent<BattleStage.SafeArea>();
            dock = BattleDock.Create(hud, engine);
            dock.Lane = ChooseLane;
            dock.Ult = PressUlt;
            dock.Cheer = Cheer;
            dock.Speed = NextSpeed;
            dock.Bag = OpenBag;
            dock.Log = () => log.Open(top);
            HazardChip(hud);
            top = UiKit.Node("sheets", content).Fill();
            log = BattleLog.Create(gameObject, engine, link != null);
            sides = BattleSides.Create(hud, engine, stage, log);
        }

        // a tale's land hazard (TQ_HZ) as a chip under the fight's title, like the site's hzb in the arena's corner
        void HazardChip(RectTransform hud)
        {
            string hz = engine.Setup.Tale?.Hazard;
            var plate = hud.Find("title") as RectTransform; // the dock's title block (title, subtitle)
            if (hz == null || plate == null || !EpicData.Current.Hazards.TryGetValue(hz, out var h)) return;
            var row = UiKit.Node("hazard", plate);
            UiKit.Row(row, 6);
            UiKit.Icon(row, h.i, 18);
            UiKit.Label(row, h.n, UiKit.SmallSize, Palette.Hex("#ffdcc8"), UiKit.Bold).horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // ---- the fight ----

        IEnumerator Flow()
        {
            yield return StartCoroutine(Reveal());
            for (int n = 1; !engine.Over; n++)
            {
                yield return link == null || alone ? Alone() : link.Captain ? Lead(n) : Follow(n);
                if (ending) yield break;
            }
            End(engine.Outcome);
        }

        // a fight of your own (or one whose captain went quiet): the next step whenever nothing is over it
        IEnumerator Alone()
        {
            while (Paused) yield return null;
            rng?.Seed((uint)UnityEngine.Random.Range(1, int.MaxValue));
            yield return Play(Step());
        }

        // the captain: holds for anyone lagging, applies everyone's inputs, rolls the step on a fresh seed and sends it on
        IEnumerator Lead(int n)
        {
            while (link.Waiting(n)) yield return null;
            var ins = link.TakeInputs();
            Inputs(ins);
            uint seed = link.NewSeed();
            rng.Seed(seed);
            var step = Step();
            if (step == null) yield break;
            link.Sent(n, seed, ins, engine.Snapshot(), engine.Over ? engine.Outcome.Won : (bool?)null);
            yield return Play(step);
        }

        // a follower: plays the captain's step n with the same inputs and seed, then takes on the captain's state after it
        IEnumerator Follow(int n)
        {
            uint seed;
            List<BattleInput> ins;
            while (!link.Next(n, out seed, out ins)) { if (alone) yield break; yield return null; }
            Inputs(ins);
            rng.Seed(seed);
            yield return Play(Step());
            List<object> snap;
            bool? over;
            while (!link.After(n, out snap, out over)) { if (alone || ending) yield break; yield return null; }
            engine.Apply(snap);
            if (over.HasValue && !engine.Over) engine.ForceEnd(over.Value);
            director.Sync();
            link.Played(n);
        }

        // the engine's next step; a fight that breaks closes as if it never started
        List<BattleEvent> Step()
        {
            try { return engine.Next(); }
            catch (Exception x) { Debug.LogException(x); Close(Unstarted(engine.Setup), null); return null; }
        }

        // a step's events on the stage, everything squared up with the engine, then the site's gap between turns
        IEnumerator Play(List<BattleEvent> step)
        {
            if (step == null) yield break;
            fx.Speed = Pace;
            foreach (var e in step)
            {
                while (Paused) yield return null;
                log.Add(e);
                sides.Played(e);
                yield return director.Play(e);
            }
            director.Sync();
            yield return new WaitForSeconds(.21f / (2 * fx.Speed));
        }

        // the inputs before a party fight's step, in the captain's order (a friend's cheer floats up over their pet)
        void Inputs(List<BattleInput> ins)
        {
            if (ins == null) return;
            foreach (var i in ins)
            {
                if (i.Act == "cheer" && i.Hero != Hero?.Key) fx.Cheer(stage.View(i.Hero), CheerEmoji[UnityEngine.Random.Range(0, CheerEmoji.Length)]);
                var e = engine.Input(i);
                if (e == null) continue;
                log.Add(e);
                director.Cheer(e);
            }
        }

        // the battle grows as a circle from the meeting point over .45 s (a quick fade with reduce motion)
        IEnumerator Reveal()
        {
            const float Seconds = .45f;
            if (GameSettings.ReduceMotion)
            {
                for (float t = 0; t < .2f; t += Time.unscaledDeltaTime) { group.alpha = t / .2f; yield return null; }
                group.alpha = 1;
                yield break;
            }
            var mask = UiKit.Node("reveal", root);
            mask.anchorMin = mask.anchorMax = new Vector2(.5f, .5f);
            var disc = mask.gameObject.AddComponent<Image>();
            disc.sprite = Art.Disc;
            disc.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            content.SetParent(mask, false);
            content.anchorMin = content.anchorMax = new Vector2(.5f, .5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, from, null, out var at);
            for (float t = 0; t < Seconds; t += Time.unscaledDeltaTime)
            {
                var size = root.rect.size;
                float reach = (new Vector2(Mathf.Abs(at.x) + size.x / 2, Mathf.Abs(at.y) + size.y / 2)).magnitude;
                mask.anchoredPosition = at;
                mask.sizeDelta = Vector2.one * 2 * reach * BattleEase.Open.At(t / Seconds);
                content.sizeDelta = size;
                content.anchoredPosition = -at;
                yield return null;
            }
            content.SetParent(root, false);
            content.Fill();
            Destroy(mask.gameObject);
        }

        void End(BattleOutcome o)
        {
            o.Rounds = Math.Max(1, o.Rounds);
            Grant(o);
            AutoSave.Now("fight");
            if (foeCard) foeCard.Close();
            log.Close();
            BattleReward.Show(top, o, sound, () => Close(o, null), item => Close(o, item), engine.Setup.BookBoss);
        }

        // renown and loot before the card shows them (a ready-made test hero earns nothing outside a tale); the save also keeps
        // the lane picked. A tale's win pays by its kind: loot by kind and renown 8/4/2 (tqTrack)
        void Grant(BattleOutcome o)
        {
            var tale = engine.Setup.Tale;
            if (engine.Setup.Hero != null && tale == null) return;
            var save = TalesSave.Current;
            try
            {
                if (!o.Won) HeroFactory.GainRenown(save, 0, "falls", o);
                else if (tale != null)
                {
                    Loot.ForTale(o, tale.Kind, tale.Ch, save);
                    HeroFactory.GainTaleWin(save, tale.Kind, o);
                }
                else
                {
                    HeroFactory.GainRenown(save, 2, "wins", o);
                    Loot.ForWin(o, engine.Setup, save);
                }
            }
            catch (Exception x) { Debug.LogException(x); }
            save.Touch();
        }

        // ---- closing ----

        void Close(BattleOutcome o, string bagItem)
        {
            if (ending) return;
            ending = true;
            UiStack.Remove(this);
            StopAllCoroutines();
            StartCoroutine(FadeOut(o, bagItem));
        }

        // the site's exit: fade and grow a touch over .3 s, then the road takes over (and the bag opens if asked)
        IEnumerator FadeOut(BattleOutcome o, string bagItem)
        {
            group.blocksRaycasts = false;
            for (float t = 0; t < .3f; t += Time.unscaledDeltaTime)
            {
                float k = BattleEase.Ease.At(t / .3f);
                group.alpha = 1 - k;
                if (!GameSettings.ReduceMotion) content.localScale = Vector3.one * (1 + .04f * k);
                yield return null;
            }
            Finish(o);
            if (bagItem != null) TalesUi.OpenBag(bagItem);
            Destroy(gameObject);
        }

        // hands the outcome back exactly once, with walking given back (and a party fight's link let go)
        void Finish(BattleOutcome o)
        {
            if (finished) return;
            finished = true;
            Open = false;
            PlazaInput.Locked = lockedBefore;
            link?.Closed();
            done?.Invoke(o);
        }

        void OnDestroy()
        {
            UiStack.Remove(this);
            if (engine != null) Finish(engine.Over ? engine.Outcome : Unstarted(engine.Setup));
        }

        // B mid-fight: closes a foe card if one is up; there's no fleeing, so the battle stays open
        void Back()
        {
            if (ending) return;
            UiStack.Push(this, Back);
            if (foeCard) foeCard.Close();
        }

        // ---- the player's say ----

        void Update()
        {
            if (ending) return;
            if (link != null && !alone && !link.Captain && !engine.Over && link.Lost) GoAlone();
            char lane = stage.YourLane = LaneNow;
            dock.Refresh(Paused && !engine.Over, speed, Time.unscaledTime < cheerReadyAt, !Direct && link.Pending("ult"), lane);
            if (!engine.Over && !Covered) Shortcuts();
        }

        // the captain went quiet: from here this game rolls its own steps and takes your inputs at once
        void GoAlone()
        {
            alone = true;
            fx.Toast("Lost touch with the party. Your pet fights on");
        }

        void Shortcuts()
        {
            if (TalesUi.Pressed(PlazaAction.Use)) PressUlt();
            if (TalesUi.Pressed(PlazaAction.ZoomOut)) StepLane(-1);
            if (TalesUi.Pressed(PlazaAction.ZoomIn)) StepLane(1);
            for (int i = 0; i < 3; i++) if (TalesUi.Pressed(PlazaAction.Emote1 + i)) ChooseLane(LaneKeys[i]);
            if (TalesUi.Pressed(PlazaAction.Tricks)) NextSpeed();
            if (TalesUi.Pressed(PlazaAction.Emotes)) Cheer();
            if (TalesUi.Pressed(PlazaAction.Bag)) OpenBag();
        }

        // a tap on the field: closes a foe card and picks that lane; the gamepad's A there fires a ready Ultimate
        void LaneTapped(char lane)
        {
            if (foeCard) foeCard.Close();
            if (engine.Over || VirtualCursor.Active && TryUlt()) return;
            ChooseLane(lane);
        }

        // laneTap: napping pets can't move, and only lanes with foes (or any lane while a slam is coming) are open; in a
        // party fight the move goes to the captain and the pet moves with the next step (picking its own lane again
        // takes back a move on its way)
        void ChooseLane(char lane)
        {
            var me = Hero;
            if (me == null || engine.Over || LaneNow == lane) return;
            if (me.Ko) { fx.Toast($"{HeroName} is napping"); return; }
            if (Direct ? !engine.MoveLane(me, lane) : lane != me.Lane && !engine.LaneOK(lane))
            {
                stage.Refuse(lane);
                sound.Play("tap");
                fx.Toast($"No foes left in the {BattleText.Lane(lane)} lane");
                return;
            }
            if (!Direct)
            {
                asked = lane;
                link.Ask(new BattleInput { Hero = me.Key, Act = "lane", Lane = lane });
            }
            sound.Play("swish");
        }

        // LB/RB: the next open lane that way, so from the left past a cleared middle to the right; none open, the usual refusal
        void StepLane(int by)
        {
            if (Hero == null) return;
            int from = LaneKeys.IndexOf(LaneNow), i = from + by;
            while (i >= 0 && i < 3 && !engine.LaneOK(LaneKeys[i])) i += by;
            if (i < 0 || i > 2) i = from + by;
            if (i >= 0 && i < 3) ChooseLane(LaneKeys[i]);
        }

        void PressUlt()
        {
            var me = Hero;
            if (me == null || engine.Over || TryUlt()) return;
            if (me.Ko) fx.Toast($"{HeroName} is napping");
            else if (me.Hero?.WantUlt != true && (Direct || !link.Pending("ult"))) fx.Toast($"The Ultimate needs 6 ink ({me.Ink} so far). It fills as {HeroName} fights");
        }

        // queues the Ultimate for the pet's next turn when it's charged (in a party fight, asks the captain to)
        bool TryUlt()
        {
            var me = Hero;
            if (me?.Hero == null || me.Hero.WantUlt) return false;
            if (Direct ? !engine.RequestUlt(me) : link.Pending("ult") || !engine.UltReady(me)) return false;
            if (!Direct) link.Ask(new BattleInput { Hero = me.Key, Act = "ult" });
            sound.Play("chime");
            return true;
        }

        // a cheer floats up every 1.5 s at most; the first one in a fight gives the pet +1 ink (in a party fight, once
        // the captain has it)
        void Cheer()
        {
            if (engine.Over || Time.unscaledTime < cheerReadyAt) return;
            cheerReadyAt = Time.unscaledTime + 1.5f;
            fx.Cheer(stage.View(Hero?.Key), CheerEmoji[UnityEngine.Random.Range(0, CheerEmoji.Length)]);
            if (!Direct) { if (Hero != null) link.Ask(new BattleInput { Hero = Hero.Key, Act = "cheer" }); return; }
            var e = engine.Cheer(Hero);
            if (e == null) return;
            log.Add(e);
            director.Cheer(e);
        }

        void NextSpeed()
        {
            speed = speed == 1 ? 2 : speed == 2 ? 4 : 1;
            fx.Speed = speed;
            PlayerPrefs.SetInt(SpeedKey, speed);
        }

        static int SavedSpeed()
        {
            int s = PlayerPrefs.GetInt(SpeedKey, 1);
            return s == 2 || s == 4 ? s : 1;
        }

        void OpenBag()
        {
            if (!engine.Over) TalesUi.OpenBag();
        }

        void ToggleFoeCard(BattleUnit u)
        {
            bool same = foeCard && foeCard.Unit == u;
            if (foeCard) foeCard.Close();
            var v = stage.View(u.Key);
            if (!same && !ending && v != null && !v.ShownKo) foeCard = BattleFoeCard.Show(stage.Fx, v);
        }
    }
}
