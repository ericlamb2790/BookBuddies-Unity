using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Plays the engine's events on the stage like the site's play(): who moves, how the move travels, what lands and
    /// what it says, with the site's timings at the chosen speed. Bars and knock-outs follow each event's Fx entries,
    /// so what's on screen always matches the moment being shown; Sync squares everything up after a step.
    /// </summary>
    public sealed class BattleDirector
    {
        static readonly BattleEase Rise = new BattleEase(.3f, .7f, .3f, 1);
        static readonly Color Sky = Palette.Hex("#9fd8ff");

        readonly BattleEngine engine;
        readonly BattleStage stage;
        readonly BattleFx fx;
        readonly BattleSound sound;
        readonly Dictionary<string, int> tactics = new Dictionary<string, int>(); // tactic changes so far by boss key (the band's phase count)
        bool effectSaid;

        public BattleDirector(BattleEngine engine, BattleStage stage, BattleFx fx, BattleSound sound)
        {
            this.engine = engine;
            this.stage = stage;
            this.fx = fx;
            this.sound = sound;
        }

        float T(float ms) => fx.T(ms);

        // a pause in a coroutine; zero or less waits a single frame
        static object Wait(float seconds) => seconds > 0 ? new WaitForSeconds(seconds) : null;

        /// <summary>Plays one event (yield it from a coroutine).</summary>
        public IEnumerator Play(BattleEvent e)
        {
            effectSaid = false;
            if (e.Actor != null) foreach (var v in stage.Views) v.SetTurn(v.Unit.Key == e.Actor && e.Kind != "fate");
            switch (e.Kind)
            {
                case "intro": yield return Intro(e); break;
                case "talk":
                    yield return Wait(fx.Bubble(stage.View(e.Speaker ?? e.Actor), e.Line, true));
                    yield return Wait(fx.Bubble(stage.View(e.Listener), e.Reply, false));
                    break;
                case "dot":
                    foreach (var f in e.Fx) if (f.Heal > 0) sound.Play("heal", "soft"); else if (f.Damage > 0) sound.Play("hit", "small");
                    foreach (var f in e.Fx) Apply(f, e);
                    yield return Wait(T(480));
                    break;
                case "skip":
                    sound.Play("miss");
                    fx.Callout(e);
                    fx.Rock(stage.View(e.Actor));
                    yield return Wait(T(550));
                    break;
                case "cheer": Cheer(e); break;
                case "jump": // the stage hops the foe (and any pet following it) to the new lane
                    sound.Play("swish");
                    foreach (var p in e.Pops) fx.Tip(stage.View(p.Key), p.Value);
                    yield return Wait(T(450));
                    break;
                case "fate": yield return fx.Fate(e, f => Apply(f, e)); break;
                case "rise": yield return Risen(e); break;
                case "phase": yield return Phase(e); break;
                case "win": yield return Won(); break;
                case "lose":
                    sound.Play("lose");
                    fx.Banner("Everyone needs a nap…", "The story will remember this", true);
                    yield return Wait(T(1400));
                    break;
                default: yield return Act(e); break;
            }
            foreach (var v in stage.Views) v.Paint();
        }

        /// <summary>A cheer landed: +1 ink for the pet, with sparkles and its callout.</summary>
        public void Cheer(BattleEvent e)
        {
            var v = stage.View(e.Actor);
            sound.Play("pop");
            fx.Sparkle(v, Art.Emote("📣"), Color.white);
            foreach (var p in e.Pops) fx.Tip(stage.View(p.Key), p.Value);
            v?.Paint();
        }

        /// <summary>After a step: every bar, knock-out, ink pip and status matches the engine.</summary>
        public void Sync()
        {
            foreach (var v in stage.Views)
            {
                var u = v.Unit;
                v.SetHp(u.Hp, u.Max, u.S("shield"));
                bool down = u.IsFoe ? u.Hp <= 0 : u.Ko;
                if (down != v.ShownKo) v.KnockOut(down);
                v.SetTurn(false);
                v.Paint();
            }
        }

        // foes drop in, pets rise, then the "Dust Bunny +2!" callout, or a twisted fight's banner ("👀 Ambush!" and what it did)
        IEnumerator Intro(BattleEvent e)
        {
            int i = 0, k = 0;
            foreach (var v in stage.Views)
            {
                if (v.Unit.IsFoe) KeyTween.Play(v, T(600), BattleEase.Out, T(i++ * 140), false, new Kf(0, 0, 40, 0, .8f, 0), new Kf(.7f, 0, -4, 0, 1.04f), new Kf(1));
                else KeyTween.Play(v, T(500), BattleEase.Out, T(k++ * 90), false, new Kf(0, 0, -30, 0, 1, 0), new Kf(1));
            }
            sound.Play("page");
            yield return Wait(T(500));
            if (e.Name != null)
            {
                fx.Banner(e.Name, e.Sub, false);
                yield return Wait(T(1000));
                yield break;
            }
            fx.Callout(new BattleEvent { Kind = "intro", Name = e.Line, Foe = true });
            yield return Wait(T(350));
        }

        // atk, heal, shield, buff and the slam wind-up: wind-up, callout, ultimate, delivery, hits, the rest, callouts
        IEnumerator Act(BattleEvent e)
        {
            var a = stage.View(e.Actor);
            if (e.Foe && a != null && e.Kind == "atk") yield return Wait(fx.Wind(a));
            fx.Callout(e);
            if (e.Ult)
            {
                yield return Wait(fx.UltCut(e));
                fx.SgUlt(e);
            }
            var hits = e.Fx.FindAll(IsHit);
            string anim = e.Anim ?? "arc";
            if (hits.Count > 0 && a != null) yield return Strike(e, a, hits, anim);
            else if (a != null && e.Kind != "atk")
            {
                fx.Lift(a);
                sound.Play(e.Kind == "heal" ? "heal" : e.Kind == "shield" ? "shield" : "buff");
                fx.SgHit(e, a, true);
                yield return Wait(fx.Rune(a, e.Kind == "slamw" ? "buff" : e.Kind));
            }
            foreach (var f in e.Fx)
            {
                if (IsHit(f)) continue;
                if (f.Buff != null || f.ShieldGained > 0 || f.Heal > 0 && anim == "orbit") fx.Orbit(stage.View(f.Unit), f.Buff ?? e.Icon);
                Apply(f, e);
            }
            fx.SelfArt(e);
            foreach (var p in e.Pops) fx.Tip(stage.View(p.Key), p.Value);
            if (e.Kind == "slamw") WarnSlam(e.Zone);
            yield return Wait(T(e.Ult ? 750 : 480));
        }

        // the attacker moves, the move travels and lands: all at once for area moves, one by one otherwise
        IEnumerator Strike(BattleEvent e, BattleUnitView a, List<BattleHit> hits, string anim)
        {
            if (!e.Foe && (anim == "beam" || anim == "rain" || anim == "burst" || anim == "quake")) yield return Wait(fx.Rune(a));
            var first = stage.View(hits[0].Unit);
            if (anim == "slash") fx.Dash(a, first);
            else if (anim == "quake") fx.Hop(a);
            else fx.Lunge(a, first);
            sound.Play(anim == "slash" ? "swish" : anim == "quake" ? "stomp" : anim == "beam" ? "zap" : "whoosh");
            yield return Wait(T(anim == "slash" ? 150 : anim == "quake" ? 260 : 200));
            if (e.Aoe)
            {
                var targets = new List<BattleUnitView>();
                foreach (var f in hits) { var v = stage.View(f.Unit); if (v != null && !targets.Contains(v)) targets.Add(v); }
                float land = 0;
                foreach (var v in targets) land = Mathf.Max(land, fx.Deliver(anim, a, v, e.Icon, e.Projectile));
                yield return Wait(land);
                if (anim == "quake") fx.Shake(T(300), new Vector2(0, -6), new Vector2(0, 4));
                foreach (var f in hits) Apply(f, e);
                foreach (var v in targets) fx.SgHit(e, v, false);
                yield break;
            }
            foreach (var f in hits)
            {
                var v = stage.View(f.Unit);
                if (!f.Small)
                {
                    yield return Wait(fx.Deliver(anim, a, v, e.Icon, e.Projectile));
                    fx.SgHit(e, v, false);
                }
                Apply(f, e);
                if (e.Seq || f.Small) yield return Wait(T(130));
            }
        }

        // a boss gets back up: it flares and grows, the band names its new self, then it boasts
        IEnumerator Risen(BattleEvent e)
        {
            var v = stage.View(e.Actor);
            sound.Play("stomp");
            if (v != null)
            {
                if (v.ShownKo) v.KnockOut(false);
                v.Paint();
                v.Flash();
                KeyTween.Play(v.Motion, T(1300), Rise, new Kf(0, 0, 0, 0, .6f), new Kf(.35f, 0, 0, 8, .55f), new Kf(.7f, 0, 0, -4, 1.35f), new Kf(1));
            }
            fx.RiseBand(e, PhaseOf(e.Actor));
            yield return Wait(T(500));
            foreach (var f in e.Fx) Apply(f, e);
            yield return Wait(T(900));
            yield return Wait(fx.Bubble(v, e.Line, true));
            yield return Wait(T(300));
        }

        // a tale boss changes tactics: it stomps and shakes, the rise band names the change with its boast under it, then its
        // bar (and any minions it called, which the stage pops in by itself)
        IEnumerator Phase(BattleEvent e)
        {
            var v = stage.View(e.Actor);
            sound.Play("stomp");
            if (v != null)
            {
                v.Flash();
                if (e.Result == "enrage") v.Enrage();
                KeyTween.Play(v.Motion, T(700), BattleEase.Out, new Kf(0), new Kf(.33f, 0, 0, -4, 1.18f), new Kf(.66f, 0, 0, 4, 1.12f), new Kf(1));
            }
            tactics.TryGetValue(e.Actor, out int n);
            tactics[e.Actor] = n + 1;
            string who = BattleText.Prose(engine.Find(e.Actor)?.Name ?? "The villain");
            fx.RiseBand(new BattleEvent { Name = $"{who} {e.Name}", Sub = e.Line }, PhaseOf(e.Actor));
            yield return Wait(T(500));
            foreach (var f in e.Fx) Apply(f, e);
            yield return Wait(T(1400));
        }

        // rises and tactic changes count up one phase band after another
        int PhaseOf(string key)
        {
            tactics.TryGetValue(key, out int n);
            return (engine.Find(key)?.Phase ?? 1) + n;
        }

        IEnumerator Won()
        {
            bool boss = engine.Foes.Exists(f => f.Boss);
            sound.Play(boss ? "fanfare" : "win");
            fx.Banner("Victory!", null, false);
            fx.Confetti(boss ? 120 : 45);
            int i = 0;
            foreach (var v in stage.Views)
            {
                v.SetTurn(false);
                if (!v.Unit.IsFoe && !v.ShownKo) fx.Bounce(v, T(i++ * 40));
            }
            yield return Wait(T(700));
        }

        // fxApply: one Fx entry lands on its fighter: miss, damage, shield, heal, revive, callout, bar, knock-out, particles
        void Apply(BattleHit f, BattleEvent e)
        {
            var v = stage.View(f.Unit);
            if (v == null) return;
            if (f.Miss)
            {
                fx.Number(v, "miss", BattleFx.Num.Miss);
                sound.Play("miss");
                fx.Dodge(v);
            }
            else if (f.Damage > 0 || f.Absorbed > 0)
            {
                if (f.Dot == null)
                {
                    fx.Impact(v, f.Crit);
                    sound.Play(f.Crit ? "crit" : "hit", f.Small ? "small" : null);
                }
                if (f.Damage > 0) fx.Number(v, (f.Dot ?? (f.Crit ? "💥 " : "")) + Whole(f.Damage), f.Crit ? BattleFx.Num.Crit : f.Small ? BattleFx.Num.Small : BattleFx.Num.Normal);
                if (f.Absorbed > 0) fx.Number(v, "🛡️ " + Whole(f.Absorbed), BattleFx.Num.Shield);
                if (!effectSaid && f.Effect != 1)
                {
                    effectSaid = true;
                    fx.Tip(v, f.Effect > 1 ? "📖 Super effective!" : "Not very effective…");
                }
            }
            if (f.Heal > 0) { fx.Sparkle(v); fx.Number(v, "+" + Whole(f.Heal), BattleFx.Num.Heal); }
            if (f.Revive) { fx.Tip(v, "🌅 Back on their feet!"); v.KnockOut(false); }
            if (f.ShieldGained > 0) { fx.Sparkle(v, BattleArt.Particle("hex"), Sky); fx.Number(v, "🛡️ +" + Whole(f.ShieldGained), BattleFx.Num.Shield); }
            if (f.Pop != null) fx.Tip(v, f.Pop);
            v.SetHp(f.Hp, f.Max, f.Shield);
            if (f.Ko && !v.ShownKo) KnockOut(v);
            fx.HitArt(f, e);
        }

        void KnockOut(BattleUnitView v)
        {
            if (!v.Unit.IsFoe) { sound.Play("ko"); v.KnockOut(true); return; }
            sound.Play("poof");
            fx.Poof(v);
            fx.After(T(160), () => { if (v && !v.ShownKo) v.KnockOut(true); });
        }

        // a slam is coming for the pet's lane: say so at the top of the screen
        void WarnSlam(string zone)
        {
            var me = engine.Me;
            if (me != null && !me.Ko && zone != null && zone.Length > 0 && me.Lane == zone[0]) fx.Toast("⚠️ Slam on your lane. Pick another lane!");
        }

        static bool IsHit(BattleHit f) => f.Miss || f.Damage > 0 || f.Absorbed > 0;

        static string Whole(double v) => Mathf.RoundToInt((float)v).ToString();
    }
}
