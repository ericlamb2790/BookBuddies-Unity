using System.Collections;
using BookBuddies.Live;
using BookBuddies.Net;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Starts BookBuddies when you press Play and moves between the screens:
    /// loading → the intro (first launch only) → the title → hatching or signing in → loading → arriving in town,
    /// and back to the title from the town menu. It adds itself to whatever scene is open, so an empty scene works.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const string TownKey = "pawtopia";
        const float RoomWait = 2.5f; // seconds the loading screen waits for the live room, so you arrive to company

        static Boot running;
        TownMap map;
        TownView view;
        TownCamera cam;
        Townsfolk folk;
        PlazaWorld world;
        Hud hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (running == null) new GameObject("BookBuddies").AddComponent<Boot>();
        }

        void Awake() => running = this;

        IEnumerator Start()
        {
            GameSettings.Apply();
            Sound.Init();
            UiKit.EnsureEventSystem();
            Art.Load();
            var loading = LoadingScreen.Show(Buddy.ShownLook, "Opening the storybook…");
            Sound.Music("home");
            yield return null;

            map = TownMap.Load(TownKey);
            loading.Stage("Painting Pawtopia…");
            loading.Progress(.1f);
            view = new GameObject("Town").AddComponent<TownView>();
            yield return view.BuildGradually(map, p => loading.Progress(.1f + p * .75f));
            cam = SetUpCamera();

            loading.Stage("Waking the villagers…");
            folk = Townsfolk.Spawn(map);
            yield return new WaitForSecondsRealtime(.5f);

            // the next screen waits underneath, so the loading screen fades straight into it
            if (GameSettings.IntroSeen)
            {
                ShowTitle(null);
                yield return loading.Hide();
                yield break;
            }
            var intro = StartCoroutine(Cinema.Intro(cam));
            yield return loading.Hide();
            yield return intro;
            ShowTitle(null);
        }

        TownCamera SetUpCamera()
        {
            var c = Camera.main;
            if (c == null)
            {
                c = new GameObject("Main Camera").AddComponent<Camera>();
                c.tag = "MainCamera";
            }
            var town = c.GetComponent<TownCamera>();
            if (!town) town = c.gameObject.AddComponent<TownCamera>();
            town.Setup(map, new Vector2(map.Start.x + .5f, map.Start.y + .5f));
            return town;
        }

        void ShowTitle(string notice) =>
            TitleScreen.Show(cam, () => StartCoroutine(EnterTown()), () => StartCoroutine(WatchIntro()), notice);

        IEnumerator WatchIntro()
        {
            yield return Cinema.Intro(cam);
            ShowTitle(null);
        }

        /// <summary>Loads your buddy (from the server when signed in), opens the live room and cranes down to you.</summary>
        IEnumerator EnterTown()
        {
            string name = Buddy.Name, look = Buddy.Hatched ? Buddy.Look : Buddy.GuestLook;
            var loading = LoadingScreen.Show(look, "Fluffing your buddy…");
            loading.Progress(.15f);

            if (Settings.SignedIn)
            {
                var me = BBApi.Me();
                yield return new WaitUntil(() => me.IsCompleted);
                if (me.Exception?.InnerException is BBApi.ApiError e && e.Status == 401)
                {
                    Buddy.Forget();
                    yield return loading.Hide();
                    ShowTitle("Your sign-in has run out. Please sign in again.");
                    yield break;
                }
                if (me.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    name = me.Result.Str("name", name);
                    if (me.Result.Str("pet").Length > 0) look = me.Result.Str("pet");
                    Buddy.Save(name, look);
                }
                // no connection: use the buddy saved last time, and the town keeps trying to connect
            }
            if (string.IsNullOrEmpty(name)) name = "You";

            loading.Stage("Finding a cozy room…");
            loading.Progress(.5f);
            if (folk) Destroy(folk.gameObject);
            world = new GameObject("Plaza").AddComponent<PlazaWorld>();
            world.Begin(map, view, cam, name, look);
            cam.Directed = true; // stays put until the arrival shot starts
            for (float t = 0; t < RoomWait && world.Net.State == LiveState.Connecting; t += Time.unscaledDeltaTime)
            {
                loading.Progress(.5f + .45f * t / RoomWait);
                yield return null;
            }
            Sound.Music("pawtopia");
            var arrival = StartCoroutine(Cinema.Arrival(cam, world.Me));
            yield return loading.Hide();
            yield return arrival;
            hud = Hud.Create(world, () => StartCoroutine(BackToTitle()));
            world.AnnounceArea();
        }

        /// <summary>From the town menu, or after signing out: fade out of town and back to the title.</summary>
        IEnumerator BackToTitle()
        {
            var fade = Cinema.Create(cam, false);
            yield return fade.Fade(1, .45f);
            if (hud) Destroy(hud.gameObject);
            if (world) { world.End(); Destroy(world.gameObject); }
            view.ClearItems();
            folk = Townsfolk.Spawn(map);
            ShowTitle(null);
            yield return fade.Fade(0, .6f);
            Destroy(fade.gameObject);
        }
    }
}
