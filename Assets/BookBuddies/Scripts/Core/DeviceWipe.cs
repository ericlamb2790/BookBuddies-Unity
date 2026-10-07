using System.IO;
using System.Threading.Tasks;
using BookBuddies.Local;
using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.Tales;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// "Sign out and clear this device" (Settings → Account): your world closes, what's still on its way to your account
    /// gets one last try of a few seconds (coins to carry home and bank, pet changes, your adventure), then every sign-in,
    /// the offline world, the Tales saves, the saved drawings and every setting go. Photos stay. The game carries on as new.
    /// </summary>
    public static class DeviceWipe
    {
        const int LastTryMs = 6000;

        public static async Task Run()
        {
            if (LocalHost.Running) HostRunner.Close();
            await Task.WhenAny(SendHome(), Task.Delay(LastTryMs));
            TaleStore.Forget();
            TalesSave.Forget();
            LocalServer.Forget();
            DeleteSaves();
            ArtCache.Clear();
            Buddy.Forget();
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            GameSettings.ResetAll();
        }

        // what this device holds for your account goes there first (nothing to send when you're signed out online)
        static async Task SendHome()
        {
            try
            {
                if (Settings.IsWorld && Settings.SignedIn) await CoinBank.AtSave(true); // coins found in a friend's world come home
                if (!CoinBank.HasBank) return;
                CoinBank.EndSession();
                await Task.WhenAll(CoinBank.SyncAll(), PetSync.Push(true), TalesSync.Upload(4));
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // the offline world (offline/) and the Tales saves (saves/, and tale*.json from before it)
        static void DeleteSaves()
        {
            string dir = Application.persistentDataPath;
            try
            {
                string offline = Path.Combine(dir, "offline");
                if (Directory.Exists(offline)) Directory.Delete(offline, true);
                string saves = Path.Combine(dir, "saves");
                if (Directory.Exists(saves)) Directory.Delete(saves, true);
                foreach (string file in Directory.GetFiles(dir, "tale*")) File.Delete(file);
            }
            catch (System.Exception e) { Debug.LogWarning("BookBuddies: couldn't delete every save. " + e.Message); }
        }
    }
}
