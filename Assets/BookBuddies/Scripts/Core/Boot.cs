using System.Collections;
using System.IO;
using System.Threading.Tasks;
using BookBuddies.Live;
using BookBuddies.Local;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.Road;
using BookBuddies.Tales;
using BookBuddies.UI;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Starts BookBuddies when you press Play and moves between the screens:
    /// loading → the intro (first launch only) → the title → hatching or signing in → loading → arriving in town,
    /// and back to the title from the town menu. Travel moves you between the towns on Bramble Road, the road's
    /// links and the Inkwell Caves, and the place you were last in is where you come back to.
    /// Offline play ends its session (and banks its coins, see CoinBank) back on the title and when the game quits.
    /// It adds itself to whatever scene is open, so an empty scene works.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const string TownKey = "pawtopia";
        const string PlaceKey = "bb.place"; // the place you were last in
        const float RoomWait = 2.5f;   // seconds the loading screen waits for the live room, so you arrive to company
        const float TravelWait = 1.8f; // the same wait when travelling (the site's 1800 ms)
        const float NewStopToast = 1.8f; // "… is now a stop on the Paw Express", after arriving somewhere new
        const float QuitWait = 4;        // seconds quitting waits for offline coins to reach the bank

        static Boot running;
        TownMap map;
        TownView view;
        TownCamera cam;
        Townsfolk folk;
        PlazaWorld world;
        Hud hud;
        RoadHud roadHud;
        RoadSky sky;
        bool travelling, quitting, quitReady;

        // the offline world's save file, before anything can ask the backend on this PC
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OfflineFile() => LocalServer.FilePath = Path.Combine(Application.persistentDataPath, "offline", "world.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (running == null) new GameObject("BookBuddies").AddComponent<Boot>();
        }

        void Awake()
        {
            running = this;
            Application.wantsToQuit += WantsToQuit;
        }

        void OnDestroy() => Application.wantsToQuit -= WantsToQuit;

        IEnumerator Start()
        {
            GameSettings.Apply();
            Sound.Init();
            UiKit.EnsureEventSystem();
            Art.Load();
            RoadSpots.Register(() => world);
            TownSpots.Register(() => world);
            BossSpots.Register(() => world);
            TownShop.Register();
            SpotActions.Register("tales", _ => TalesUi.OpenTales());
            var loading = LoadingScreen.Show(Buddy.ShownLook, "Opening the storybook…");
            Sound.Music("home");
            yield return null;
            CoinBank.EndSession(); // a session a crash left open ends now
            if (!Settings.IsLocal) _ = CoinBank.SyncAll(); // and sessions that couldn't upload try again (offline boots stay off the network)

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
                    ShowTitle(Settings.IsLocal ? null : "Your sign-in has run out. Please sign in again."); // offline, the egg hatches again
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
            AutoSave.Now("arrive");
        }

        /// <summary>The place you're in (null on the title screen).</summary>
        public static PlazaWorld World => running != null ? running.world : null;

        /// <summary>
        /// Takes you to another place: a town on Bramble Road (its key, like "romance"), a road link ("road" + link)
        /// or the Inkwell Caves ("caves"), arriving at a tile or at the place's own start, with an optional note
        /// shown once you're there. "train" is a Paw Express ride (or a lore stone's); otherwise you walked.
        /// Ignored while already on the way, or when you're already there.
        /// </summary>
        public static void Travel(string place, Vector2Int? arrive, string note = null, bool train = false)
        {
            if (running == null || running.travelling || running.world == null || place == running.map.Key) return;
            running.StartCoroutine(running.TravelTo(place, arrive, note, train));
        }

        // the site's travelTo: "All aboard! Next stop: Rosewater" or "On the road to Rosewater", then the step lines
        IEnumerator TravelTo(string place, Vector2Int? arrive, string note, bool train)
        {
            travelling = true;
            PlazaInput.Locked = true;
            var about = About(place);
            string name = world.Me.Name, look = world.Me.Look;
            var fade = Cinema.Create(cam, false);
            yield return fade.Fade(1, .35f);
            var loading = LoadingScreen.Show(look, train ? "All aboard! Next stop: " + about.name : (place == "caves" ? "Into " : "On the road to ") + about.name);
            Destroy(fade.gameObject);
            loading.Progress(.1f);
            LeaveTown();
            yield return new WaitForSecondsRealtime(.75f); // the site's wave goodbye

            loading.Stage(about.step);
            yield return BuildPlace(place, loading, .15f, .55f, train ? "Pulling into the station…" : "Following the path…");
            world = new GameObject("Plaza").AddComponent<PlazaWorld>();
            world.Begin(map, view, cam, name, look, arrive);
            for (float t = 0; t < TravelWait && world.Net.State == LiveState.Connecting; t += Time.unscaledDeltaTime)
            {
                loading.Progress(.7f + .25f * t / TravelWait);
                yield return null;
            }
            loading.Stage(train ? "Doors opening…" : "Almost there…");
            yield return new WaitForSecondsRealtime(.25f);
            Sound.Music(MoodOf(map));
            PlayerPrefs.SetString(PlaceKey, map.Key);
            yield return loading.Hide();

            ShowTown();
            world.Me.Play("hop", .65f);
            world.Me.ShowEmote(about.icon);
            Sound.Play("coin");
            if (note != null) world.Notify(note);
            if (TownProgress.MarkSeen(map.Key)) StartCoroutine(NewStop(TownBook.Current.Get(map.Key)));
            PlazaInput.Locked = false;
            travelling = false;
            AutoSave.Now("arrive");
        }

        // what the loading card says about a place: its icon, its name and the step line ("Romance town", "Tier I · the meadows")
        static (string icon, string name, string step) About(string place)
        {
            var town = TownBook.Current.Get(place);
            if (town != null) return (town.Icon, town.Name, town.Genre + " town");
            if (place == "caves") return ("🕯️", "Inkwell Caves", "The caves");
            var data = TalesData.Current;
            int link = int.TryParse(place.Replace("road", ""), out var l) ? Mathf.Clamp(l, 1, data.LinkT.Length - 1) : 1;
            var tier = data.Tiers[data.LinkT[link] - 1];
            return (tier.Icon, tier.Name, tier.Genre);
        }

        IEnumerator NewStop(TownBook.Town town)
        {
            yield return new WaitForSecondsRealtime(NewStopToast);
            if (world && town != null) world.Notify($"{town.Icon} {town.Name} is now a stop on the Paw Express");
        }

        // the wilds' own music, or the town's tune (its root note and scale)
        static string MoodOf(TownMap place)
        {
            if (place.Wild != null) return place.Wild.Music;
            var town = TownBook.Current.Get(place.Key);
            if (town != null) Sound.AddMood(place.Key, town.MusicRoot, town.Scale);
            return place.Key;
        }

        // the place you were last in, if it still exists
        static string SavedPlace()
        {
            string place = PlayerPrefs.GetString(PlaceKey, TownKey);
            return place == TownKey || Art.TryText("Data/town_" + place) != null ? place : TownKey;
        }

        /// <summary>
        /// Replaces the map and its view with another place, a little each frame under the loading screen
        /// ("halfway" becomes the loading line once it's half built).
        /// </summary>
        IEnumerator BuildPlace(string place, LoadingScreen loading, float from, float span, string halfway = null)
        {
            if (view) Destroy(view.gameObject);
            map = TownMap.Load(place);
            view = new GameObject(map.Name).AddComponent<TownView>();
            yield return view.BuildGradually(map, p =>
            {
                loading.Progress(from + p * span);
                if (halfway != null && p >= .5f) loading.Stage(halfway);
            });
            cam.Setup(map, new Vector2(map.Start.x + .5f, map.Start.y + .5f));

            // draw the storybook folk now, one a frame, so arriving doesn't stall on drawing them all at once
            var book = TownBook.Current.Folk;
            foreach (var r in map.Folk)
            {
                if (r.Folk < 0 || r.Folk >= book.Count) continue;
                string look = book[r.Folk].LookFor(r.Copy);
                if (PetSprites.Has(look)) continue;
                PetSprites.For(look);
                yield return null;
            }
        }

        // the HUD, the road's own corner of it, and the cave darkness, the weather or a town's fog
        void ShowTown()
        {
            hud = Hud.Create(world, () => StartCoroutine(BackToTitle()));
            roadHud = RoadHud.Create(world);
            if (map.Wild != null || TownBook.Current.Get(map.Key)?.Fog == true) sky = RoadSky.Create(world);
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
            BankOfflineCoins();
            AutoSave.Now("title");
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

        // ---- offline coins ----

        // ends the offline session (if one is open) and banks every finished one in the background
        static void BankOfflineCoins()
        {
            CoinBank.EndSession();
            _ = CoinBank.SyncAll();
        }

        // quitting ends the offline session and waits a moment for its coins to reach the bank (next launch tries again)
        bool WantsToQuit()
        {
            if (quitReady) return true;
            if (quitting) return false;
            CoinBank.EndSession();
            var sync = CoinBank.SyncAll();
            if (sync.IsCompleted || Application.isEditor) return true;
            quitting = true;
            StartCoroutine(QuitAfter(sync));
            return false;
        }

        IEnumerator QuitAfter(Task<bool> sync)
        {
            float until = Time.realtimeSinceStartup + QuitWait;
            while (!sync.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            quitReady = true;
            Application.Quit();
        }
    }
}
