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
            Settings.Token = token;
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
            Settings.Token = token;
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

        /// <summary>Your account, including "pet" (your pet's look as JSON text) and "name".</summary>
        public static Task<Dictionary<string, object>> Me() => Send("GET", "/me", null, true);

        /// <summary>Your recovery code again, as "code" (Unity server only).</summary>
        public static Task<Dictionary<string, object>> Recovery() => Send("GET", "/me/recovery", null, true);

        /// <summary>Deletes the account and everything saved with it (Unity server only), then signs out.</summary>
        public static async Task DeleteAccount()
        {
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
                    string why = json.Str("error", req.error ?? "Something went wrong");
                    throw new ApiError(why, req.responseCode);
                }
                return json ?? new Dictionary<string, object>();
            }
        }
    }
}
