using System;
using System.Threading.Tasks;
using BookBuddies.Local;
using BookBuddies.Net;
using BookBuddies.Tales;
using BookBuddies.UI;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Saves the game at good moments, so a crash or a closed window loses next to nothing: the offline world
    /// (LocalServer, when it has a file), Tales (TalesSave, TaleStore) and PlayerPrefs. Now(why) after something worth
    /// keeping (a purchase, gear, a pet, a page turned, arriving somewhere) waits for things to settle, makes the JSON on
    /// the main thread and writes the files on a background thread (a temp file, then swapped in), then a small book
    /// blinks in the corner (SaveIcon). BeforeFight saves everything at once. It also saves when the app is paused or
    /// loses focus, every few minutes, and when the game quits. Away from your online account (offline, or in a friend's
    /// world) each save also sends your coins and pet changes there (CoinBank.AtSave, PetSync) once it answers. Main thread only.
    /// </summary>
    public static class AutoSave
    {
        const float Settle = 1.5f; // seconds Now waits for more changes…
        const float MaxWait = 5;   // …but never longer than this after the first ask
        const float Every = 180;   // the safety save, without the book

        static float dueAt = -1, firstAt, nextAt = Every; // dueAt -1: no save waiting
        static string reason, lastError; // why the waiting save was asked for; the last offline save error logged

        /// <summary>Asks for a save soon ("why" names the moment in the log if the offline save fails). Cheap: call it freely.</summary>
        public static void Now(string why)
        {
            float now = Time.unscaledTime;
            if (dueAt < 0) firstAt = now;
            dueAt = Mathf.Min(now + Settle, firstAt + MaxWait);
            reason = why;
        }

        /// <summary>Saves everything right now, before a fight starts, so a fight can't lose coins or gear.</summary>
        public static void BeforeFight() => SaveAll();

        // everything at once, on this thread (a fight is starting, the app is being paused or closed)
        static void SaveAll()
        {
            Settled();
            ToAccount();
            try
            {
                if (HasOfflineFile) LocalServer.SaveNow();
                TalesSave.Flush();
                TaleStore.Flush();
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // the JSON now, the files on a background thread; the book blinks once they're written
        static async void SaveSoon(string why, bool blink)
        {
            Settled();
            ToAccount();
            try
            {
                PlayerPrefs.Save();
                await Task.WhenAll(HasOfflineFile ? LocalServer.SaveAsync() : Task.CompletedTask, TalesSave.FlushAsync(), TaleStore.FlushAsync());
            }
            catch (Exception e) { Debug.LogException(e); return; }
            Report(why);
            if (blink) SaveIcon.Blink();
        }

        // your coins and pet changes go up to your online account (each waits for it to answer its health check)
        static void ToAccount()
        {
            try
            {
                CoinBank.AtSave();
                _ = PetSync.Push();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static bool HasOfflineFile => !string.IsNullOrEmpty(LocalServer.FilePath);

        static void Settled()
        {
            dueAt = -1;
            reason = null;
            nextAt = Time.unscaledTime + Every;
        }

        // an offline save that failed is worth one line in the log (each new error once)
        static void Report(string why)
        {
            string error = HasOfflineFile ? LocalServer.SaveError : null;
            if (error != null && error != lastError) Debug.LogWarning($"BookBuddies: the offline save failed after \"{why}\": {error}");
            lastError = error;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            dueAt = -1;
            nextAt = Every;
            var go = new GameObject("autosave") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        // the saves' clock and the app's comings and goings
        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                float now = Time.unscaledTime;
                if (dueAt >= 0 && now >= dueAt) SaveSoon(reason, true);
                else if (now >= nextAt) SaveSoon("timer", false);
            }

            // a paused app (a phone put away) may never come back, so this one is written before it returns
            void OnApplicationPause(bool paused)
            {
                if (paused) SaveAll();
            }

            void OnApplicationFocus(bool focused)
            {
                if (!focused) SaveSoon("focus lost", false);
            }

            void OnApplicationQuit() => SaveAll();
        }
    }
}
