using System.Collections.Generic;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Pets
{
    /// <summary>
    /// One pet walking around town: yours, another reader's, or a villager.
    /// Movement and every little animation (idle breathing, hops, naps, hugs...) follow the website's numbers.
    /// Walking is smoothed on top: a soft start and stop, corners drawn as gentle curves, a springy stride
    /// with a puff of dust on each step.
    /// </summary>
    public sealed class PetActor : MonoBehaviour
    {
        public const float Speed = 3.4f;     // tiles per second
        const float StartUp = .12f;          // seconds to get up to speed from standing
        const float SlowDown = .4f;          // tiles before the end of a walk where the pet starts to slow
        const float SoftestPace = .62f;      // the slowest the soft start and stop go (they add about 50 ms to a walk)
        const float Smoothing = .07f;        // seconds the drawn pet trails its path by, which rounds the corners
        const float Stride = .9f;            // tiles per bounce of the walk cycle

        // who
        public string Id, Name, User;
        public bool IsBot, IsMe;
        public string Look { get; private set; }

        // where (map coordinates; tile centres are at +.5)
        public Vector2 Pos;
        public readonly List<Vector2Int> Path = new List<Vector2Int>();
        public int Dir = 1;
        public float Pace;          // >0 speeds other players up or down so they arrive in sync
        public bool Sitting, Sleeping, WantSit, Hidden;
        public bool Hovered;        // the pointer is on it: lifts a little
        public bool NameShown = true; // storybook folk only show their names close by
        public System.Action OnArrived;

        // what it's showing
        public string Bubble { get; private set; }
        public float BubbleUntil { get; private set; }
        public string Emote { get; private set; }
        public float EmoteAt { get; private set; }

        public bool Gone => goneAt >= 0;
        public bool Walking => Path.Count > 0;
        public Vector2Int Tile => new Vector2Int(Mathf.FloorToInt(Pos.x), Mathf.FloorToInt(Pos.y));

        /// <summary>Where the pet is drawn: its path position, smoothed so turns curve.</summary>
        public Vector2 Shown => shown;

        TownMap map;
        Transform body;
        SpriteRenderer bodySprite, shadow, ring;
        string anim;
        float animStart, animLength, idleAt, stillAt, bornAt, goneAt = -1;
        Vector2 shown, shownVelocity;
        float walkingSince, stride, hoverLift;
        int footfalls;

        public static PetActor Spawn(Transform parent, TownMap map, string id, string name, string look, Vector2 pos, bool bot, bool me)
        {
            var go = new GameObject((me ? "Me " : bot ? "Villager " : "Reader ") + name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<PetActor>();
            a.map = map; a.Id = id; a.Name = name; a.IsBot = bot; a.IsMe = me; a.Pos = a.shown = pos;
            a.Dir = Random.value < .5f ? 1 : -1;
            a.bornAt = Time.time;
            a.stillAt = Time.time;
            a.idleAt = Time.time + Random.value * 3;
            a.Build();
            a.SetLook(look);
            return a;
        }

        void Build()
        {
            transform.rotation = TownCamera.Facing;
            body = new GameObject("body").transform;
            body.SetParent(transform, false);
            bodySprite = body.gameObject.AddComponent<SpriteRenderer>();
            shadow = Draw.Blob("shadow", transform.parent, 0, 0, .45f, .36f, Palette.ShadowTint.WithAlpha(.38f), Draw.ShadowOrder + 1);
            if (!IsBot)
            {
                ring = Draw.Sprite("ring", transform.parent, Art.DashedRing, Draw.GroundFxOrder, (IsMe ? Palette.Amber : Palette.Ember).WithAlpha(.85f));
                ring.transform.rotation = Quaternion.Euler(90, 0, 0);
                ring.transform.localScale = new Vector3(.5f, .32f, 1);
            }
        }

        public void SetLook(string look)
        {
            Look = look ?? "";
            bodySprite.sprite = PetSprites.For(Look);
        }

        // ---- things that happen to the pet ----

        public void Play(string kind, float seconds, float delay = 0)
        {
            anim = kind;
            animStart = Time.time + delay;
            animLength = seconds;
        }

        /// <summary>A speech bubble, for "seconds" (or long enough to read when 0).</summary>
        public void Say(string text, float seconds = 0)
        {
            Bubble = text;
            BubbleUntil = Time.time + (seconds > 0 ? seconds : 4.5f + text.Length * .06f);
        }

        public void ClearBubble() => Bubble = null;

        public void ShowEmote(string emoji)
        {
            Emote = emoji;
            EmoteAt = Time.time;
        }

        public void Walk(List<Vector2Int> path)
        {
            if (!Walking) walkingSince = Time.time; // a fresh start eases in; a change of course doesn't
            Path.Clear();
            if (path != null) Path.AddRange(path);
            Sitting = false;
            Sleeping = false;
            anim = null;
            stillAt = Time.time;
        }

        public void SitHere()
        {
            Path.Clear();
            Pos = new Vector2(Tile.x + .5f, Tile.y + .5f);
            Sitting = true;
        }

        public void Nap() { Sleeping = true; stillAt = 0; }

        public void Leave() { if (goneAt < 0) goneAt = Time.time; }

        /// <summary>Jump straight to a spot with a little poof (used when someone is far off).</summary>
        public void Teleport(Vector2 to)
        {
            Fx.Poof(Pos);
            Fx.Poof(to);
            Pos = to;
            bornAt = Time.time;
            Play("poofin", .6f);
        }

        // ---- every frame ----

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            if (Gone && t - goneAt > .7f) { Destroy(gameObject); return; }
            if (!Gone) Step(t, dt);
            if (!bodySprite.sprite) bodySprite.sprite = PetSprites.For(Look); // the sprite cache may have dropped it
            Render(t);
        }

        void OnDestroy()
        {
            if (shadow) Destroy(shadow.gameObject);
            if (ring) Destroy(ring.gameObject);
        }

        void Step(float t, float dt)
        {
            if (Walking)
            {
                var next = Path[0];
                var target = new Vector2(next.x + .5f, next.y + .5f);
                var delta = target - Pos;
                float move = Speed * dt * (IsMe ? 1 : Pace > 0 ? Pace : 1.05f) * Easing(t, delta.magnitude);
                if (Mathf.Abs(delta.x) > .05f) Dir = delta.x > 0 ? 1 : -1;
                if (delta.magnitude <= move)
                {
                    Pos = target;
                    Path.RemoveAt(0);
                    if (!Walking) Arrive(t);
                }
                else Pos += delta.normalized * move;

                Footsteps(move);
                anim = null;
                Sleeping = false;
            }
            else if (!Sitting && anim == null && t > idleAt)
            {
                if (t - stillAt > 50 && !Sleeping) Sleeping = true;
                else if (!Sleeping)
                {
                    string[] idles = { "look", "hop", "wiggle", "stretch", "look", "sniff", "twirl" };
                    string k = idles[Random.Range(0, idles.Length)];
                    Play(k, k == "hop" ? .65f : k == "twirl" ? .9f : k == "look" ? 1.4f : 1.1f);
                    if (k == "look") Dir = -Dir;
                }
                idleAt = t + 2.6f + Random.value * 4.5f;
            }
            else if (Sitting && anim == null && t > idleAt)
            {
                Play(Random.value < .5f ? "sway" : "stretch", 1.4f);
                idleAt = t + 4 + Random.value * 5;
            }
            if (anim != null && t - animStart > animLength) anim = null;
            if (Bubble != null && t > BubbleUntil) Bubble = null;
            if (Emote != null && t - EmoteAt > 1.8f) Emote = null;
        }

        // A soft start from standing and a soft stop at the end of the path, gentle enough that arrival times
        // stay within a few hundredths of a second of the site's.
        float Easing(float t, float toNext)
        {
            float k = Mathf.Lerp(SoftestPace, 1, Mathf.Clamp01((t - walkingSince) / StartUp));
            if (Path.Count == 1 && toNext < SlowDown) k *= Mathf.Lerp(SoftestPace, 1, toNext / SlowDown);
            return k;
        }

        // The stride follows the ground covered, so feet and dust keep time with the speed.
        void Footsteps(float moved)
        {
            stride += moved / Stride;
            if ((int)stride == footfalls) return;
            footfalls = (int)stride;
            var side = new Vector2(Dir * (footfalls % 2 == 0 ? .1f : -.06f), .32f);
            Fx.Dust(Pos + side, footfalls % 4 == 0 ? 1.3f : 1);
        }

        void Arrive(float t)
        {
            Pace = 0;
            if (t - walkingSince > .3f) Fx.Dust(Pos + new Vector2(0, .32f), 1.5f); // a little skid at the stop
            stillAt = t;
            idleAt = t + 1.5f + Random.value * 2.5f;
            if (IsMe) { var then = OnArrived; OnArrived = null; then?.Invoke(); }
            else if (WantSit) { WantSit = false; if (map.SeatAt(Tile.x, Tile.y) != null) Sitting = true; }
        }

        /// <summary>The website's drawPet(): squash, stretch, tilt and bounce for whatever the pet is doing.</summary>
        void Render(float t)
        {
            float lift = Sitting ? map.LiftAt(Tile.x, Tile.y) : 0;
            var seat = Sitting ? map.SeatAt(Tile.x, Tile.y) : null;
            float ox = 0, oy = 0, rot = 0, sx = 1, sy = 1;

            if (Gone)
            {
                float q = (t - goneAt) / .7f;
                sy = 1 + q * .3f; sx = 1 - q * .5f; oy = -q * .4f;
            }
            else if (Walking)
            {
                // up on the stride, squashed a touch at each footfall, leaning into the walk
                float calm = GameSettings.ReduceMotion ? .4f : 1;
                float phase = stride * Mathf.PI, b = Mathf.Abs(Mathf.Sin(phase));
                oy = -b * .14f * calm;
                sy = 1 + (b - .55f) * .09f * calm; sx = 1 - (b - .55f) * .06f * calm;
                rot = (Mathf.Sin(phase) * .07f - Dir * .035f) * calm;
            }
            else
            {
                sy = 1 + Mathf.Sin(t * 2.4f + Pos.x) * .018f; sx = 2 - sy;
                if (seat != null) { sy *= .9f; sx *= 1.04f; }
                if (Sleeping) { sy = .95f + Mathf.Sin(t * 1.6f) * .025f; rot = seat != null && seat.Kind == "hammock" ? .35f : .12f; }
                if (anim != null && t >= animStart) Animate((t - animStart) / animLength, ref ox, ref oy, ref rot, ref sx, ref sy);
            }

            // the drawn pet trails its path by a few hundredths of a second, which turns corners into curves
            if ((Pos - shown).sqrMagnitude > 2.25f) { shown = Pos; shownVelocity = Vector2.zero; }
            else shown = Vector2.SmoothDamp(shown, Pos, ref shownVelocity, Smoothing, Mathf.Infinity, Time.deltaTime);
            hoverLift = Mathf.MoveTowards(hoverLift, Hovered && !Gone ? .07f : 0, Time.deltaTime * .6f);
            oy -= hoverLift;
            if (Hovered) { sx *= 1.03f; sy *= 1.03f; }

            // place everything (body moves in the camera-facing plane, like the site's flat overlay)
            transform.position = TownMap.ToWorld(shown.x, shown.y);
            body.localPosition = new Vector3(ox, -(.32f + oy + lift), 0);
            body.localRotation = Quaternion.Euler(0, 0, -rot * Mathf.Rad2Deg);
            body.localScale = new Vector3(sx * (Dir < 0 ? -1 : 1), sy, 1);

            float fadeIn = IsMe ? 1 : Mathf.Clamp01((t - bornAt) / .48f);
            float fadeOut = Gone ? Mathf.Max(0, 1 - (t - goneAt) / .7f) : 1;
            bodySprite.color = new Color(1, 1, 1, fadeIn * fadeOut);
            bodySprite.sortingOrder = Draw.ActorOrder(shown.y) + (Sitting ? 30 : 0);
            bodySprite.enabled = !Hidden;

            shadow.transform.position = TownMap.ToWorld(shown.x, shown.y + .3f + lift * .2f, .01f);
            shadow.color = Palette.ShadowTint.WithAlpha(.38f * fadeOut);
            shadow.enabled = !Hidden;
            if (ring)
            {
                ring.transform.position = TownMap.ToWorld(shown.x, shown.y + .3f, .012f);
                ring.enabled = !Hidden && !Gone;
            }
        }

        void Animate(float k, ref float ox, ref float oy, ref float rot, ref float sx, ref float sy)
        {
            const float PI = Mathf.PI;
            float s;
            switch (anim)
            {
                case "hop": oy = -Mathf.Sin(k * PI) * .45f; sy = k < .15f ? .85f : 1.06f; break;
                case "wiggle": rot = Mathf.Sin(k * PI * 6) * .14f * (1 - k); break;
                case "stretch": sy = 1 + Mathf.Sin(k * PI) * .12f; sx = 1 - Mathf.Sin(k * PI) * .06f; break;
                case "sniff": rot = Dir * .15f * Mathf.Sin(k * PI); ox = Dir * .08f * Mathf.Sin(k * PI); break;
                case "twirl":
                case "spin": sx = Mathf.Cos(k * PI * 4); break;
                case "dance": rot = Mathf.Sin(k * PI * 8) * .2f; oy = -Mathf.Abs(Mathf.Sin(k * PI * 8)) * .15f; break;
                case "wave": rot = Mathf.Sin(k * PI * 6) * .1f; oy = -Mathf.Abs(Mathf.Sin(k * PI * 3)) * .08f; break;
                case "sway": rot = Mathf.Sin(k * PI * 2) * .07f; break;
                case "hug": s = Mathf.Sin(Mathf.Min(1, k * 1.4f) * PI); ox = Dir * .2f * s; rot = Dir * .16f * s; sx = 1 + .06f * s; sy = 1 - .05f * s; break;
                case "boop":
                    s = k < .35f ? Mathf.Sin(k / .35f * PI) : 0; ox = Dir * .3f * s; rot = Dir * .1f * s;
                    if (k > .35f) oy = -Mathf.Sin((k - .35f) / .65f * PI) * .12f;
                    break;
                case "highpaw": oy = -Mathf.Sin(k * PI) * .55f; rot = Dir * .2f * Mathf.Sin(k * PI); sy = k < .12f ? .86f : 1.05f; break;
                case "play": oy = -Mathf.Abs(Mathf.Sin(k * PI * 3)) * .35f; rot = Mathf.Sin(k * PI * 3) * .1f; break;
                case "snack": sy = 1 + Mathf.Sin(k * PI * 10) * .04f * (1 - k); rot = Dir * .06f; oy = -Mathf.Sin(k * PI) * .08f; break;
                case "happy": oy = -Mathf.Abs(Mathf.Sin(k * PI * 2)) * .3f; rot = Mathf.Sin(k * PI * 6) * .12f * (1 - k); break;
                case "poofin": s = Mathf.Min(1, k * 2.2f); sx = sy = .3f + .7f * s; oy = -(1 - s) * .6f; break;
            }
        }

        // ---- anchor points for name tags, bubbles and emotes ----

        float Lift => Sitting ? map.LiftAt(Tile.x, Tile.y) : 0;
        public Vector3 HeadPoint => transform.position + transform.up * (PetSprites.Height - .3f - Lift);
        public Vector3 FeetPoint => transform.position + transform.up * (-.55f - Lift);
    }
}
