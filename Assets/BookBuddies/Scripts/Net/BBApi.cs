using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace BookBuddies.Net
{
    /// <summary>
    /// The BookBuddies Cloudflare Worker's HTTP API (the same /api routes the website uses).
    /// Signed-in calls send "Authorization: Bearer &lt;token&gt;".
    /// </summary>
    public static class BBApi
    {
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
        /// the code that brings the pet back on another device.
        /// </summary>
        public static async Task<Dictionary<string, object>> Register(string name, string petLook)
        {
            var account = await Send("POST", "/register", new Dictionary<string, object> { ["name"] = name.Trim(), ["pet"] = petLook }, false);
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
            var me = await Send("GET", "/me", null, true);
            Remember(me, Settings.Token);
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

        /// <summary>Deletes the account and everything saved with it (Unity server only), then signs out.</summary>
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

        /// <summary>The WebSocket address for the live town.</summary>
        public static string LiveUrl(string ticket, string town, int shard, bool viaPass)
        {
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

        static async Task<Dictionary<string, object>> Send(string method, string path, Dictionary<string, object> body, bool signedIn)
        {
            using (var req = new UnityWebRequest(Settings.Server + "/api" + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Json.Write(body)));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                if (signedIn && Settings.SignedIn) req.SetRequestHeader("Authorization", "Bearer " + Settings.Token);
                req.timeout = 20;
                await req.SendWebRequest();

                string text = req.downloadHandler.text;
                Dictionary<string, object> json = null;
                try { json = Json.ParseObject(text); } catch (FormatException) { }

                if (req.result != UnityWebRequest.Result.Success || req.responseCode >= 400)
                {
                    if (req.responseCode == 0) throw new ApiError("Can’t reach " + Settings.ServerHost + ". Check your connection and try again.", 0);
                    if (req.responseCode == 401) Settings.SignOut();
                    if (req.responseCode == 403 && path.StartsWith("/admin")) Settings.IsAdmin = false;
                    string why = json.Str("error", req.error ?? "Something went wrong");
                    throw new ApiError(why, req.responseCode);
                }
                return json ?? new Dictionary<string, object>();
            }
        }
    }
}
