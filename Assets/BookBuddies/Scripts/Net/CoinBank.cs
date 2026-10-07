using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Local;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Banks offline coins into your online wallet. A session is one stretch of offline play; when it ends (quitting, back
    /// to the title, switching to online) its total goes to the online server once (POST /wallet/bank), which checks it
    /// against that session's finds and the daily caps. Banked coins leave the offline purse; coins the server refuses stay
    /// in it. Sessions that couldn't upload (no network, a crash) try again at the next launch online, or when the next
    /// offline session ends. Main thread only.
    /// </summary>
    public static class CoinBank
    {
        static Task<bool> syncing;

        /// <summary>Coins this launch's uploads have banked so far (the coins sheet says so).</summary>
        public static int Banked { get; private set; }

        /// <summary>Raised after an upload banked coins: how many.</summary>
        public static event Action<int> BankedCoins;

        /// <summary>There's an online account for offline coins to go to (signed in on Settings.OnlineServer).</summary>
        public static bool HasBank => Settings.TokenFor(Settings.OnlineServer).Length > 0;

        /// <summary>Ends the offline profile's open session (if there is one) and saves, so its total is ready to bank.</summary>
        public static void EndSession()
        {
            try
            {
                string pid = LocalPid();
                if (pid == null) return;
                LocalWallet.CloseSession(pid);
                LocalServer.SaveNow();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Uploads every closed session of the offline profile that isn't banked yet, with your sign-in on the online
        /// server (none there: the coins wait in the offline purse). Never throws; true when nothing is left to upload.
        /// </summary>
        public static Task<bool> SyncAll() => syncing != null && !syncing.IsCompleted ? syncing : (syncing = Upload());

        static async Task<bool> Upload()
        {
            try
            {
                string pid = LocalPid();
                if (pid == null) return true;
                string server = Settings.OnlineServer, token = Settings.TokenFor(server);
                var tried = new HashSet<string>(); // a session closed while these upload gets its turn too
                while (true)
                {
                    var waiting = LocalWallet.Unsynced(pid);
                    var next = waiting.Find(s => !tried.Contains(s.Id));
                    if (next == null) return waiting.Count == 0;
                    if (token.Length == 0) return false;
                    tried.Add(next.Id);
                    int banked = await Bank(server, token, next);
                    if (banked < 0) return false;
                    Settle(pid, next.Id, banked);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        // the coins the server banked for one session (0 when it refused the whole upload), or -1 to try again later
        static async Task<int> Bank(string server, string token, LocalSession session)
        {
            try { return (await BBApi.SendTo(server, token, "POST", "/wallet/bank", session.BankBody())).Int("banked"); }
            catch (BBApi.ApiError e) when (e.Status == 400) { return 0; } // it can never be banked: the coins stay offline
            catch (BBApi.ApiError) { return -1; }
        }

        // the banked coins leave the offline purse (the rest stay there) and the session is done
        static void Settle(string pid, string session, int banked)
        {
            LocalWallet.Banked(pid, session, banked);
            LocalServer.SaveNow();
            if (banked <= 0) return;
            Banked += banked;
            BankedCoins?.Invoke(banked);
        }

        // the offline profile signed in on this PC, or null
        static string LocalPid()
        {
            string token = Settings.TokenFor(Settings.Local);
            return token.Length > 0 ? LocalServer.PidForToken(token) : null;
        }
    }
}
