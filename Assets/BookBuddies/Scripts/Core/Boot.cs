using System.Collections;
using BookBuddies.Live;
using BookBuddies.Net;
using BookBuddies.Road;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Starts BookBuddies when you press Play and moves between the screens:
    /// loading → the intro (first launch only) → the title → hatching or signing in → loading → arriving in town,
    /// and back to the title from the town menu. Travel moves you between Pawtopia, Bramble Road and the
    /// Inkwell Caves, and the place you were last in is where you come back to.
    /// It adds itself to whatever scene is open, so an empty scene works.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const string TownKey = "pawtopia";
        const string PlaceKey = "bb.place"; // the place you were last in
        const float RoomWait = 2.5f;   // seconds the loading screen waits for the live room, so you arrive to company
        const float TravelWait = 1.8f; // the same wait when travelling (the site's 1800 ms)

        static Boot running;
        TownMap map;
        TownView view;
        TownCamera cam;
        Townsfolk folk;
        PlazaWorld world;
        Hud hud;
        RoadHud roadHud;
        RoadSky sky;
        bool travelling;

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
            RoadSpots.Register(() => world);
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

            if (folk) Destroy(folk.gameObject);
            string place = SavedPlace();
            if (place != map.Key)
            {
                loading.Stage("Following the path…");
                yield return BuildPlace(place, loading, .2f, .3f);
            }

            loading.Stage("Finding a cozy room…");
            loading.Progress(.5f);
            world = new GameObject("Plaza").AddComponent<PlazaWorld>();
            world.Begin(map, view, cam, name, look);
            cam.Directed = true; // stays put until the arrival shot starts
            for (float t = 0; t < RoomWait && world.Net.State == LiveState.Connecting; t += Time.unscaledDeltaTime)
            {
                loading.Progress(.5f + .45f * t / RoomWait);
                yield return null;
            }
            Sound.Music(MoodOf(map));
            var arrival = StartCoroutine(Cinema.Arrival(cam, world.Me));
            yield return loading.Hide();
            yield return arrival;
            ShowTown();
        }

        /// <summary>
        /// Takes you to another place: Pawtopia, Bramble Road ("road1") or the Inkwell Caves ("caves"), arriving at
        /// a tile or at the place's own start, with an optional note shown once you're there.
        /// Ignored while already on the way, or when you're already there.
        /// </summary>
        public static void Travel(string place, Vector2Int? arrive, string note = null)
        {
            if (running == null || running.travelling || running.world == null || place == running.map.Key) return;
            running.StartCoroutine(running.TravelTo(place, arrive, note));
        }

        IEnumerator TravelTo(string place, Vector2Int? arrive, string note)
        {
            travelling = true;
            PlazaInput.Locked = true;
            string name = world.Me.Name, look = world.Me.Look;
            var fade = Cinema.Create(cam, false);
            yield return fade.Fade(1, .35f);
            var loading = LoadingScreen.Show(look, TravelTitle(place));
            Destroy(fade.gameObject);
            loading.Progress(.1f);
            LeaveTown();
            yield return new WaitForSecondsRealtime(.75f); // the site's wave goodbye

            loading.Stage("Following the path…");
            yield return BuildPlace(place, loading, .15f, .55f);
            world = new GameObject("Plaza").AddComponent<PlazaWorld>();
            world.Begin(map, view, cam, name, look, arrive);
            for (float t = 0; t < TravelWait && world.Net.State == LiveState.Connecting; t += Time.unscaledDeltaTime)
            {
                loading.Progress(.7f + .25f * t / TravelWait);
                yield return null;
            }
            loading.Stage("Almost there…");
            yield return new WaitForSecondsRealtime(.25f);
            Sound.Music(MoodOf(map));
            PlayerPrefs.SetString(PlaceKey, map.Key);
            yield return loading.Hide();

            ShowTown();
            world.Me.Play("hop", .65f);
            world.Me.ShowEmote(map.Wild == null ? "🏡" : map.Wild.IsCave ? "🕯️" : "🌾");
            Sound.Play("coin");
            if (note != null) world.Notify(note);
            PlazaInput.Locked = false;
            travelling = false;
        }

        static string TravelTitle(string place) =>
            place == TownKey ? "Back to Pawtopia" : place == "caves" ? "Into the Inkwell Caves" : place == "road1" ? "On to Bramble Road" : "On the way…";

        static string MoodOf(TownMap place) => place.Wild?.Music ?? "pawtopia";

        // the place you were last in, if it still exists
        static string SavedPlace()
        {
            string place = PlayerPrefs.GetString(PlaceKey, TownKey);
            return place == TownKey || Art.TryText("Data/town_" + place) != null ? place : TownKey;
        }

        /// <summary>Replaces the map and its view with another place, a little each frame under the loading screen.</summary>
        IEnumerator BuildPlace(string place, LoadingScreen loading, float from, float span)
        {
            if (view) Destroy(view.gameObject);
            map = TownMap.Load(place);
            view = new GameObject(map.Name).AddComponent<TownView>();
            yield return view.BuildGradually(map, p => loading.Progress(from + p * span));
            cam.Setup(map, new Vector2(map.Start.x + .5f, map.Start.y + .5f));
        }

        // the HUD, the road's own corner of it, and the cave darkness or weather
        void ShowTown()
        {
            hud = Hud.Create(world, () => StartCoroutine(BackToTitle()));
            roadHud = RoadHud.Create(world);
            if (map.Wild != null) sky = RoadSky.Create(world);
            world.HudReady();
        }

        void LeaveTown()
        {
            if (hud) Destroy(hud.gameObject);
            if (roadHud) Destroy(roadHud.gameObject);
            if (sky) Destroy(sky.gameObject);
            if (world) { world.End(); Destroy(world.gameObject); }
            world = null;
        }

        /// <summary>From the town menu, or after signing out: fade out of town and back to the title.</summary>
        IEnumerator BackToTitle()
        {
            var fade = Cinema.Create(cam, false);
            yield return fade.Fade(1, .45f);
            LeaveTown();
            view.ClearItems();
            if (map.Key != TownKey)
            {
                var loading = LoadingScreen.Show(Buddy.ShownLook, "Back to Pawtopia");
                yield return BuildPlace(TownKey, loading, .1f, .85f);
                yield return loading.Hide();
            }
            folk = Townsfolk.Spawn(map);
            ShowTitle(null);
            yield return fade.Fade(0, .6f);
            Destroy(fade.gameObject);
        }
    }
}
