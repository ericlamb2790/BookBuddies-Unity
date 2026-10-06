using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Saved on the device: which server to talk to and the sign-in token.
    /// The default server comes from Resources/BookBuddies/Data/config.json, so pointing a build at your own
    /// Worker is a one-line change. Players can still pick another server in Settings → Account.
    /// </summary>
    public static class Settings
    {
        const string ServerKey = "bb.server";
        const string TokenKey = "bb.token";
        static string configServer, version;

        public static string DefaultServer => configServer ?? (configServer = Config("server", "https://bookbuddies.pet").TrimEnd('/'));
        public static string Version => version ?? (version = Config("version", "0.2"));

        public static string Server
        {
            get => PlayerPrefs.GetString(ServerKey, DefaultServer).TrimEnd('/');
            set
            {
                string v = string.IsNullOrWhiteSpace(value) ? DefaultServer : value.Trim().TrimEnd('/');
                if (!v.StartsWith("http")) v = (v.StartsWith("localhost") || v.StartsWith("127.") ? "http://" : "https://") + v;
                PlayerPrefs.SetString(ServerKey, v);
                PlayerPrefs.Save();
            }
        }

        public static string Token
        {
            get => PlayerPrefs.GetString(TokenKey, "");
            set { PlayerPrefs.SetString(TokenKey, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Just the server's name, like "bookbuddies.pet".</summary>
        public static string ServerHost => System.Uri.TryCreate(Server, System.UriKind.Absolute, out var uri) ? uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) : Server;

        public static bool SignedIn => Token.Length > 0;

        public static void SignOut() => Token = "";

        static string Config(string key, string fallback)
        {
            try { return Json.ParseObject(Art.Text("Data/config")).Str(key, fallback); }
            catch (System.Exception) { return fallback; }
        }
    }
}
