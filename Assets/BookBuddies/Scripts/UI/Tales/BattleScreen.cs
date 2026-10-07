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
    /// The full-screen battle, wild or inside a tale (Setup.Tale: it draws over the tale and its subtitle is Setup.Place,
    /// "Act II · land"). It opens as a circle growing from where the pet met the foe, plays BattleEngine's
    /// steps through the stage (BattleStage, BattleUnitView), the effects (BattleFx) and the director (BattleDirector),
    /// and takes the player's say: lanes, the Ultimate, cheers, speed (1×, 2×, 4×, remembered), the bag and the fight log
    /// (both pause the fight between events). At the end it grants renown and loot, shows the end card (which carries
    /// on by itself), fades out and hands the outcome to the road.
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
        BattleFoeCard foeCard;
        int speed;
        float cheerReadyAt;
        bool lockedBefore, ending, finished;

        BattleUnit Hero => engine.Heroes.Count > 0 ? engine.Heroes[0] : null;
        string HeroName => BattleText.Prose(Hero?.Name ?? TalesUi.PetName);

        // the fight waits between events while a sheet, the bag or anything else is over it (a tale's own screen is under it)
        bool Paused => !ReferenceEquals(UiStack.Top, this) || TalesUi.AnyOpen && engine.Setup.Tale == null;

        /// <summary>
        /// Starts a fight from the road; from is the screen point the battle opens from. done gets the outcome once, after
        /// the end card closes (Rounds 0 means the fight never got going, e.g. it couldn't be set up).
        /// </summary>
        public static void Run(BattleSetup setup, Vector2 from, Action<BattleOutcome> done)
        {
            if (Open) { done?.Invoke(Unstarted(setup)); return; } // one fight at a time
            if (string.IsNullOrEmpty(setup.Look)) setup.Look = Buddy.Look;
            if (string.IsNullOrEmpty(setup.PetName)) setup.PetName = MyPets.ActiveName;
            BattleEngine engine;
            try { engine = new BattleEngine(setup); }
            catch (Exception e) { Debug.LogException(e); done?.Invoke(Unstarted(setup)); return; }
            UiKit.EnsureEventSystem();
            var canvas = UiKit.MakeCanvas("Battle", OrderFor(setup));
            canvas.gameObject.AddComponent<BattleScreen>().Begin(engine, from, done);
        }

        // a tale's fight goes just over the tale screen, so a bag opened from it still comes on top
        static int OrderFor(BattleSetup setup)
        {
            var tale = setup.Tale != null && TaleScreen.Body ? TaleScreen.Body.GetComponentInParent<Canvas>() : null;
            return tale ? tale.rootCanvas.sortingOrder + 1 : CanvasOrder;
        }

        static BattleOutcome Unstarted(BattleSetup s) => new BattleOutcome { HpFrac = s.HpFrac, Ink = s.Ink };

        void Begin(BattleEngine e, Vector2 point, Action<BattleOutcome> callback)
        {
            engine = e;
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
            stage = BattleStage.Create(content, engine, u => () => ToggleFoeCard(u));
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
            top = UiKit.Node("sheets", content).Fill();
            log = BattleLog.Create(gameObject, engine);
        }

        // ---- the fight ----

        IEnumerator Flow()
        {
            yield return StartCoroutine(Reveal());
            while (!engine.Over)
            {
                while (Paused) yield return null;
                List<BattleEvent> step;
                try { step = engine.Next(); }
                catch (Exception x) { Debug.LogException(x); Close(Unstarted(engine.Setup), null); yield break; }
                foreach (var e in step)
                {
                    while (Paused) yield return null;
                    log.Add(e);
                    yield return director.Play(e);
                }
                director.Sync();
                yield return new WaitForSeconds(.21f / (2 * speed)); // the site's gap between turns
            }
            End(engine.Outcome);
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

        // hands the outcome back exactly once, with walking given back
        void Finish(BattleOutcome o)
        {
            if (finished) return;
            finished = true;
            Open = false;
            PlazaInput.Locked = lockedBefore;
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
            dock.Refresh(Paused && !engine.Over, speed, Time.unscaledTime < cheerReadyAt);
            if (!engine.Over && !Paused) Shortcuts();
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

        // laneTap: napping pets can't move, and only lanes with foes (or any lane while a slam is coming) are open
        void ChooseLane(char lane)
        {
            var me = Hero;
            if (me == null || engine.Over || me.Lane == lane) return;
            if (me.Ko) { fx.Toast($"{HeroName} is napping"); return; }
            if (!engine.MoveLane(me, lane))
            {
                stage.Refuse(lane);
                sound.Play("tap");
                fx.Toast($"No foes left in the {BattleText.Lane(lane)} lane");
                return;
            }
            sound.Play("swish");
        }

        void StepLane(int by)
        {
            int i = Hero == null ? -1 : LaneKeys.IndexOf(Hero.Lane) + by;
            if (i >= 0 && i < 3) ChooseLane(LaneKeys[i]);
        }

        void PressUlt()
        {
            var me = Hero;
            if (me == null || engine.Over || TryUlt()) return;
            if (me.Ko) fx.Toast($"{HeroName} is napping");
            else if (me.Hero?.WantUlt != true) fx.Toast($"The Ultimate needs 6 ink ({me.Ink} so far). It fills as {HeroName} fights");
        }

        // queues the Ultimate for the pet's next turn when it's charged
        bool TryUlt()
        {
            var me = Hero;
            if (me?.Hero == null || me.Hero.WantUlt || !engine.RequestUlt(me)) return false;
            sound.Play("chime");
            return true;
        }

        // a cheer floats up every 1.5 s at most; the first one in a fight gives the pet +1 ink
        void Cheer()
        {
            if (engine.Over || Time.unscaledTime < cheerReadyAt) return;
            cheerReadyAt = Time.unscaledTime + 1.5f;
            fx.Cheer(stage.View(Hero?.Key), CheerEmoji[UnityEngine.Random.Range(0, CheerEmoji.Length)]);
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
