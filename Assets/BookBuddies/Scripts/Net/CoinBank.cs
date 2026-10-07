using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Economy;
using BookBuddies.Local;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Banks offline coins into your online wallet. A session is one stretch of offline play; when it ends (each save,
    /// quitting, back to the title, switching to online) its total goes to the online server once (POST /wallet/bank),
    /// which checks it against that session's finds and the daily caps. Banked coins leave the offline purse; coins the
    /// server refuses stay in it. Sessions that couldn't upload (no network, a crash) try again at the next launch,
    /// sign-in or switch of server, or when the title's server check turns green. Nothing is sent until the online
    /// server answers its health check. In a friend's world your finds go into a session there; your game carries it home (Carry) and banks it the
    /// same way, then tells that world what was banked. Main thread only.
    /// </summary>
    public static class CoinBank
    {
        const float FreshFor = 20; // seconds a finished Refresh stays good for the same sign-in
        const float SendEvery = 120; // seconds between sends to your account at save points (saves meanwhile fold into the next)
        const string CarriedKey = "bb.carried"; // sessions carried home from worlds: [{world, body, banked}] (banked -1: not yet)

        static Task<bool> syncing;
        static Task refreshing;
        static string freshFor;    // the sign-in the last finished Refresh was for
        static float freshAt = -99, sentAt = -99;
        static Task sending, banking;
        static bool owed, hurry; // a save asked for a send (now: without waiting out SendEvery)

        /// <summary>Coins this launch's uploads have banked so far (the coins sheet says so).</summary>
        public static int Banked { get; private set; }

        /// <summary>Raised after an upload banked coins: how many.</summary>
        public static event Action<int> BankedCoins;

        /// <summary>There's an online account for offline coins to go to (signed in on Settings.OnlineServer).</summary>
        public static bool HasBank => Settings.TokenFor(Settings.OnlineServer).Length > 0;

        /// <summary>
        /// Ends the offline profile's open session (if there is one) and saves, so its total is ready to bank. Soon (a save
        /// point while playing): no write here, so the game doesn't stall; the save's own background write, or the bank's, keeps it.
        /// </summary>
        public static void EndSession(bool soon = false)
        {
            try
            {
                string pid = LocalPid();
                if (pid == null) return;
                LocalWallet.CloseSession(pid);
                if (!soon) LocalServer.SaveNow();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Uploads every closed session of the offline profile that isn't banked yet, with your sign-in on the online
        /// server (none there: the coins wait in the offline purse). Never throws; true when nothing is left to upload.
        /// </summary>
        public static Task<bool> SyncAll() => syncing != null && !syncing.IsCompleted ? syncing : (syncing = Upload(false));

        /// <summary>
        /// Brings your account's coins up to date: once the online server answers its health check, offline sessions that
        /// aren't banked yet go up first, then the fresh balance comes down (Wallet.TakeOnline). When it's down, or there's
        /// no online account, nothing is sent and the last balance stays. Runs at launch, sign-in and each switch of server
        /// (a world starting or ending); never throws.
        /// </summary>
        public static Task Refresh() => refreshing != null && !refreshing.IsCompleted ? refreshing : (refreshing = RefreshNow());

        static async Task RefreshNow()
        {
            if (!HasBank) return;
            string server = Settings.OnlineServer, token = Settings.TokenFor(server);
            if (token == freshFor && Time.realtimeSinceStartup - freshAt < FreshFor) return;
            if (!await BBApi.Up(server)) return;
            _ = PetSync.Push(true, Settings.Local); // offline play's pet changes go up too, wherever you are now
            await (syncing != null && !syncing.IsCompleted ? syncing : (syncing = Upload(true)));
            await BankCarried(true);
            try { Wallet.TakeOnline(await BBApi.SendTo(server, token, "GET", "/wallet", null)); }
            catch (BBApi.ApiError) { return; }
            freshFor = token;
            freshAt = Time.realtimeSinceStartup;
        }

        // up: the online server just answered its health check (otherwise it's asked before the first upload)
        static async Task<bool> Upload(bool up)
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
                    if (!up && !(up = await BBApi.Up(server))) return false; // down: the sessions wait, nothing is sent
                    tried.Add(next.Id);
                    int banked = await Bank(server, token, next.BankBody());
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
        static async Task<int> Bank(string server, string token, Dictionary<string, object> session)
        {
            try { return (await BBApi.SendTo(server, token, "POST", "/wallet/bank", session)).Int("banked"); }
            catch (BBApi.ApiError e) when (e.Status == 400) { return 0; } // it can never be banked: the coins stay offline
            catch (BBApi.ApiError) { return -1; }
        }

        // the banked coins leave the offline purse (the rest stay there) and the session is done (written in the
        // background: quitting writes everything once more)
        static void Settle(string pid, string session, int banked)
        {
            LocalWallet.Banked(pid, session, banked);
            _ = LocalServer.SaveAsync();
            if (banked <= 0) return;
            Banked += banked;
            BankedCoins?.Invoke(banked);
        }

        /// <summary>
        /// A save point away from your online account (AutoSave, leaving): offline the open session ends and banks; in a
        /// friend's world your coins there come home (Carry) and bank. One send at a time, at most every 2 minutes unless now:
        /// saves that come meanwhile fold into one more send, made with the newest numbers. Nothing is sent while the
        /// online server is down. Never throws.
        /// </summary>
        public static Task AtSave(bool now = false)
        {
            if (!(Settings.IsWorld ? Settings.SignedIn : Settings.IsLocal && HasBank)) return Task.CompletedTask;
            owed = true;
            hurry |= now;
            if (sending == null || sending.IsCompleted) sending = Send();
            return sending;
        }

        static async Task Send()
        {
            try
            {
                while (owed)
                {
                    while (!hurry && Time.realtimeSinceStartup < sentAt + SendEvery) await Task.Delay(250);
                    owed = hurry = false;
                    sentAt = Time.realtimeSinceStartup;
                    if (Settings.IsWorld && Settings.SignedIn) await Carry(Settings.Server, Settings.Token);
                    else if (Settings.IsLocal) { EndSession(true); await SyncAll(); }
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---- coins found in a friend's world ----

        // tells the world what your game banked from sessions carried before (those coins leave its purse), then brings
        // every session still to bank there onto this PC and banks them online; a world that can't be reached keeps them
        static async Task Carry(string world, string token)
        {
            var list = Carried();
            var settled = new List<object>();
            foreach (var c in list)
                if (c.Str("world") == world && c.Int("banked") >= 0) settled.Add(new Dictionary<string, object> { ["session"] = c.Obj("body")?.Str("session"), ["banked"] = (double)c.Int("banked") });
            Dictionary<string, object> reply;
            try { reply = await BBApi.SendTo(world, token, "POST", "/wallet/carry", new Dictionary<string, object> { ["settled"] = settled }, BBApi.QuickTimeout); }
            catch (BBApi.ApiError) { return; } // the world isn't there: its coins wait in it
            list = Carried(); // what banked meanwhile
            list.RemoveAll(c => c.Str("world") == world && c.Int("banked") >= 0 && settled.Exists(o => ((Dictionary<string, object>)o).Str("session") == c.Obj("body")?.Str("session")));
            foreach (var o in reply.Arr("sessions"))
                if (o is Dictionary<string, object> body && !list.Exists(c => c.Obj("body")?.Str("session") == body.Str("session")))
                    list.Add(new Dictionary<string, object> { ["world"] = world, ["body"] = body, ["banked"] = -1.0 });
            SaveCarried(list);
            if (Settings.Server == world) Wallet.Apply(reply);
            _ = BankCarried(false);
        }

        // banks the carried sessions online (up: the health check already passed); each one's result waits to be told to its world
        static Task BankCarried(bool up) => banking != null && !banking.IsCompleted ? banking : (banking = BankCarriedNow(up));

        static async Task BankCarriedNow(bool up)
        {
            string server = Settings.OnlineServer, token = Settings.TokenFor(server);
            if (token.Length == 0) return;
            foreach (var c in Carried())
            {
                if (c.Int("banked") >= 0) continue;
                if (!up && !(up = await BBApi.Up(server))) return; // down: they wait on this PC
                int banked = await Bank(server, token, c.Obj("body"));
                if (banked < 0) return;
                var list = Carried();
                var same = list.Find(x => x.Obj("body")?.Str("session") == c.Obj("body")?.Str("session"));
                if (same == null) continue;
                same["banked"] = (double)banked;
                SaveCarried(list);
                if (banked <= 0) continue;
                Banked += banked;
                BankedCoins?.Invoke(banked);
            }
        }

        static List<Dictionary<string, object>> Carried()
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                if (Json.Parse(PlayerPrefs.GetString(CarriedKey, "[]")) is List<object> a)
                    foreach (var o in a) if (o is Dictionary<string, object> d && d.Obj("body") != null) list.Add(d);
            }
            catch (FormatException) { }
            return list;
        }

        static void SaveCarried(List<Dictionary<string, object>> list)
        {
            PlayerPrefs.SetString(CarriedKey, Json.Write(list.ConvertAll(d => (object)d)));
            PlayerPrefs.Save();
        }

        // the offline profile signed in on this PC, or null
        static string LocalPid()
        {
            string token = Settings.TokenFor(Settings.Local);
            return token.Length > 0 ? LocalServer.PidForToken(token) : null;
        }
    }
}
