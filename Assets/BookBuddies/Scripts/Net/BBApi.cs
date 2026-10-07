using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BookBuddies.Local;
using BookBuddies.Pets;
using UnityEngine;
using UnityEngine.Networking;

namespace BookBuddies.Net
{
    /// <summary>
    /// The BookBuddies Cloudflare Worker's HTTP API (the same /api routes the website uses).
    /// Signed-in calls send "Authorization: Bearer &lt;token&gt;". Playing offline (Settings.IsLocal) the same calls go to
    /// the backend inside the game (Local/LocalServer.cs) instead, with the same replies and errors, and no network.
    /// </summary>
    public static class BBApi
    {
        const int TimeoutSeconds = 20;
        const int QuickTimeout = 6; // seconds for a look at your account while switching servers

        public sealed class ApiError : Exception
        {
            public readonly long Status;
            public ApiError(string message, long status) : base(message) { Status = status; }
        }

        /// <summary>
        /// Signs in with a recovery code (BB-XXXXX-XXXXX, from the website's profile) or a 6-letter link code.
        /// Saves the token and returns the account.
        /// </summary>
        public static async Task<Dictionary<string, object>> SignIn(string code)
        {
            var account = await Send("POST", "/link/claim", new Dictionary<string, object> { ["code"] = code.Trim() }, false);
            string token = account.Str("token");
            if (token.Length == 0) throw new ApiError("The server didn't send a sign-in token.", 0);
            Remember(account, token);
            return account;
        }

        /// <summary>
        /// Hatches a new pet: makes an account on the Unity server and signs in. The reply carries "recovery",
        /// the code that brings the pet back on another device. "pets" ([{name, look, active}]) brings pets along
        /// (the offline profile starts with your online pets).
        /// </summary>
        public static async Task<Dictionary<string, object>> Register(string name, string petLook, List<object> pets = null)
        {
            var body = new Dictionary<string, object> { ["name"] = name.Trim(), ["pet"] = petLook };
            if (pets != null) body["pets"] = pets;
            var account = await Send("POST", "/register", body, false);
            string token = account.Str("token");
            if (token.Length == 0) throw new ApiError("The server didn't send a sign-in token.", 0);
            Remember(account, token);
            return account;
        }

        /// <summary>
        /// Whether the server can hatch new pets (the Unity Worker can; the website's Worker signs in existing
        /// readers only). Throws when the server can't be reached.
        /// </summary>
        public static async Task<bool> CanHatch()
        {
            try { return (await Send("GET", "/health", null, false)).Truthy("accounts"); }
            catch (ApiError e) when (e.Status >= 400) { return false; }
        }

        /// <summary>Your account, including "pet" (your active pet's look as JSON text), "name", "is_admin" (remembered in Settings.IsAdmin) and, on the Unity server, "pets" and "active".</summary>
        public static async Task<Dictionary<string, object>> Me()
        {
            string server = Settings.Server;
            var me = await Send("GET", "/me", null, true);
            if (Settings.Server == server) Remember(me, Settings.Token);
            return me;
        }

        // the token, the account id and whether it's an admin, for the next launch
        static void Remember(Dictionary<string, object> account, string token)
        {
            Settings.Token = token;
            Settings.AccountId = account.Str("id");
            Settings.IsAdmin = account.Truthy("is_admin");
        }

        // ---- your pets (Unity server only; every reply is the whole list: "pets" [{id, name, look}], "active" id and "pet") ----

        /// <summary>Your pets, oldest first, and which one is active.</summary>
        public static Task<Dictionary<string, object>> Pets() => Send("GET", "/me/pets", null, true);

        /// <summary>A new pet from an egg (name 2-14 letters, look JSON). It becomes the active pet.</summary>
        public static Task<Dictionary<string, object>> HatchPet(string name, string look) =>
            Send("POST", "/me/pets", new Dictionary<string, object> { ["name"] = name, ["look"] = look }, true);

        /// <summary>A pet's new look (a DNA reroll) or name; null leaves that one as it is.</summary>
        public static Task<Dictionary<string, object>> UpdatePet(string id, string look, string name)
        {
            var body = new Dictionary<string, object>();
            if (look != null) body["look"] = look;
            if (name != null) body["name"] = name;
            return Send("PATCH", "/me/pets/" + Uri.EscapeDataString(id), body, true);
        }

        /// <summary>Makes a pet the active one: its look becomes your "pet" in town and everywhere else.</summary>
        public static Task<Dictionary<string, object>> SetActivePet(string id) =>
            Send("POST", $"/me/pets/{Uri.EscapeDataString(id)}/active", new Dictionary<string, object>(), true);

        /// <summary>Your recovery code again, as "code" (Unity server only).</summary>
        public static Task<Dictionary<string, object>> Recovery() => Send("GET", "/me/recovery", null, true);

        /// <summary>Deletes the account and everything saved with it (Unity server only; offline, the profile on this PC), then signs out.</summary>
        public static async Task DeleteAccount()
        {
            // the website's server deletes the reader's whole site account on this route, so only the game's own server may get it
            if (!await CanHatch()) throw new ApiError("This is your bookbuddies.pet account. To delete it, use the website.", 403);
            await Send("POST", "/me/delete", new Dictionary<string, object>(), true);
            Settings.SignOut();
        }

        /// <summary>A short-lived ticket for the live town (room "shard" 1-6). Comes with a 15-minute pass for quick rejoins.</summary>
        public static Task<Dictionary<string, object>> WorldTicket(int shard, string town) =>
            Send("GET", $"/plaza/world/ticket?s={shard}&town={Uri.EscapeDataString(town)}", null, true);

        /// <summary>The WebSocket address for the live town ("local:" and the ticket for the room inside the game offline).</summary>
        public static string LiveUrl(string ticket, string town, int shard, bool viaPass)
        {
            if (Settings.IsLocal) return WorldLinks.LocalScheme + Uri.EscapeDataString(ticket);
            string ws = Settings.Server.StartsWith("https") ? "wss" + Settings.Server.Substring(5) : "ws" + Settings.Server.Substring(4);
            string url = $"{ws}/api/world/live?ticket={Uri.EscapeDataString(ticket)}";
            return viaPass ? $"{url}&town={Uri.EscapeDataString(town)}&s={shard}" : url;
        }

        // ---- the wallet (every reply is the whole wallet: "balance", "fair", "gift", "used", "recent"…; see Economy/Wallet) ----

        /// <summary>Your coins, Book Fair tickets, today's gift and limits. The first call on an account adds its starter coins.</summary>
        public static Task<Dictionary<string, object>> Wallet() => Send("GET", "/wallet", null, true);

        /// <summary>A reward the server pays from its own table: {kind: "gift"} (the daily gift). Adds "granted" and "run".</summary>
        public static Task<Dictionary<string, object>> WalletEarn(Dictionary<string, object> body) => Send("POST", "/wallet/earn", body, true);

        /// <summary>A purchase the server prices: {kind: "shop", item, ref, amount} or {kind: "fair", n, ref}. Adds "paid".</summary>
        public static Task<Dictionary<string, object>> WalletSpend(Dictionary<string, object> body) => Send("POST", "/wallet/spend", body, true);

        // ---- admin tools (the server answers 403 unless you're a town admin) ----

        /// <summary>Players whose name contains the query (or whose id is it), newest visitors first; exact matches the whole name. Reply: "players", "total", "now".</summary>
        public static Task<Dictionary<string, object>> AdminSearch(string query, bool exact = false) =>
            Send("GET", $"/admin/players?q={Uri.EscapeDataString(query ?? "")}{(exact ? "&exact=1" : "")}", null, true);

        /// <summary>One player ("player") and the admin actions taken on them ("log").</summary>
        public static Task<Dictionary<string, object>> AdminPlayer(string id) =>
            Send("GET", "/admin/players/" + Uri.EscapeDataString(id), null, true);

        /// <summary>
        /// Changes a player. action: mute {minutes}, unmute, ban {hours} or {permanent: true}, unban, kick, rename {name},
        /// coins {coins} (sets the balance with one entry in their coin ledger), admin {on}, delete. Reply: the updated
        /// "player" (null after delete) and their "log".
        /// </summary>
        public static Task<Dictionary<string, object>> AdminAct(string id, string action, Dictionary<string, object> body = null) =>
            Send("POST", $"/admin/players/{Uri.EscapeDataString(id)}/{action}", body ?? new Dictionary<string, object>(), true);

        /// <summary>The latest admin actions by everyone ("log", newest first).</summary>
        public static Task<Dictionary<string, object>> AdminLog() => Send("GET", "/admin/log", null, true);

        // ---- switching servers ----

        /// <summary>
        /// Switches to another server, or to offline play on this PC (Settings.Local). Leaving offline play ends its session
        /// and banks the coins (CoinBank). The first time offline, the offline profile is made from your online account:
        /// its name and pets (or, when it can't be reached, the buddy and pets this device remembers); with neither, the
        /// egg hatches as usual. Then your buddy comes from the new server (kept as it is when that can't be reached).
        /// </summary>
        public static async Task UseServer(string server)
        {
            string from = Settings.Server;
            var remembered = server == Settings.Local ? DeviceAccount() : null; // before the pets list follows the new account
            Settings.Server = server;
            server = Settings.Server;
            if (server == from) return;
            if (from == Settings.Local)
            {
                CoinBank.EndSession();
                _ = CoinBank.SyncAll();
            }
            try
            {
                var me = Settings.IsLocal ? await LocalProfile(from, remembered) : await OnlineProfile();
                if (Settings.Server != server) return;
                Buddy.Forget();
                if (me != null) Buddy.Save(me.Str("name"), me.Str("pet"));
            }
            catch (ApiError) { } // no answer: keep the buddy this device shows
        }

        // your account on the online server (null when signed out there); throws when it can't be reached
        static async Task<Dictionary<string, object>> OnlineProfile()
        {
            if (!Settings.SignedIn) return null;
            try { return await SendTo(Settings.Server, Settings.Token, "GET", "/me", null, QuickTimeout); }
            catch (ApiError e) when (e.Status == 401) { return null; }
        }

        // your offline profile (null when there's none), made the first time from your online account or what this device remembers of it
        static async Task<Dictionary<string, object>> LocalProfile(string online, Dictionary<string, object> remembered)
        {
            if (Settings.SignedIn)
            {
                try { return await Me(); }
                catch (ApiError e) when (e.Status == 401) { } // the profile is gone: make it again
            }
            string token = Settings.TokenFor(online);
            if (token.Length > 0)
            {
                try { remembered = await SendTo(online, token, "GET", "/me", null, QuickTimeout); }
                catch (ApiError) { } // no network: what this device remembers will do
            }
            if (remembered == null) return null;
            try { return await Register(remembered.Str("name"), remembered.Str("pet"), SeedPets(remembered)); }
            catch (ApiError) { return null; }
        }

        // your buddy and pets as this device knows them, shaped like /me (null before your egg hatches)
        static Dictionary<string, object> DeviceAccount()
        {
            if (!Buddy.Hatched) return null;
            var pets = new List<object>();
            foreach (var p in MyPets.All) pets.Add(new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Look });
            return new Dictionary<string, object> { ["name"] = Buddy.Name, ["pet"] = Buddy.Look, ["pets"] = pets, ["active"] = MyPets.Active?.Id ?? "" };
        }

        // /me's pets as /register's seeds: [{name, look, active}]
        static List<object> SeedPets(Dictionary<string, object> account)
        {
            string active = account.Str("active");
            var pets = new List<object>();
            foreach (var o in account.Arr("pets"))
                if (o is Dictionary<string, object> p && p.Str("look").Length > 0)
                    pets.Add(new Dictionary<string, object> { ["name"] = p.Str("name"), ["look"] = p.Str("look"), ["active"] = p.Str("id") == active });
            return pets.Count > 0 ? pets : null;
        }

        // ---- sending ----

        static Task<Dictionary<string, object>> Send(string method, string path, Dictionary<string, object> body, bool signedIn)
        {
            string token = signedIn && Settings.SignedIn ? Settings.Token : null;
            return Settings.IsLocal ? SendLocal(method, path, body, token) : SendTo(Settings.Server, token, method, path, body);
        }

        // the backend on this PC (it reads the body and shapes the reply exactly as they'd travel over HTTP)
        static async Task<Dictionary<string, object>> SendLocal(string method, string path, Dictionary<string, object> body, string token)
        {
            try { return await LocalServer.Handle(method, path, body, token); }
            catch (LocalProblem p)
            {
                if (p.Status == 401 && token != null) Settings.SignOut(Settings.Local);
                if (p.InnerException != null) Debug.LogException(p.InnerException); // the offline server tripped: worth a look
                throw new ApiError(p.Message, p.Status);
            }
        }

        /// <summary>
        /// A request to a given online server with a given token (null for none), whichever server is in use; CoinBank
        /// banks offline coins with it. Throws ApiError (status 0 when the server can't be reached); a 401 signs out of that server.
        /// </summary>
        public static async Task<Dictionary<string, object>> SendTo(string server, string token, string method, string path, Dictionary<string, object> body, int seconds = TimeoutSeconds)
        {
            using (var req = new UnityWebRequest(server + "/api" + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Json.Write(body)));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
                req.timeout = seconds;
                await req.SendWebRequest();

                string text = req.downloadHandler.text;
                Dictionary<string, object> json = null;
                try { json = Json.ParseObject(text); } catch (FormatException) { }

                if (req.result != UnityWebRequest.Result.Success || req.responseCode >= 400)
                {
                    if (req.responseCode == 0) throw new ApiError("Can’t reach " + Settings.HostOf(server) + ". Check your connection and try again.", 0);
                    if (req.responseCode == 401 && !string.IsNullOrEmpty(token)) Settings.SignOut(server);
                    if (req.responseCode == 403 && path.StartsWith("/admin") && server == Settings.Server) Settings.IsAdmin = false;
                    string why = json.Str("error", req.error ?? "Something went wrong");
                    throw new ApiError(why, req.responseCode);
                }
                return json ?? new Dictionary<string, object>();
            }
        }
    }
}
