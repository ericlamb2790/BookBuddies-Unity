using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Saved on the device: which server to talk to, the sign-in token, and whether that account is a town admin.
    /// The default server comes from Resources/BookBuddies/Data/config.json, so pointing a build at your own
    /// Worker is a one-line change. Players can still pick another server in Settings → Account.
    /// </summary>
    public static class Settings
    {
        const string ServerKey = "bb.server";
        const string TokenKey = "bb.token";
        const string AdminKey = "bb.admin";
        const string AccountKey = "bb.account";
        static string configServer, devServer, version;

        public static string DefaultServer => configServer ?? (configServer = Config("server", "https://bookbuddies.pet").TrimEnd('/'));
        /// <summary>The test server (config.json "devServer"; wrangler dev's address when unset).</summary>
        public static string DevServer => devServer ?? (devServer = Config("devServer", "http://localhost:8787").TrimEnd('/'));
        public static string Version => version ?? (version = Config("version", "0.3"));

        /// <summary>Which server is in use: 0 the main one, 1 the dev one, 2 an address typed in.</summary>
        public static int ServerChoice => Server == DefaultServer ? 0 : Server == DevServer ? 1 : 2;

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

        /// <summary>Whether the signed-in account is a town admin, as the server last said (the server checks again on every admin call).</summary>
        public static bool IsAdmin
        {
            get => SignedIn && PlayerPrefs.GetInt(AdminKey, 0) == 1;
            set { PlayerPrefs.SetInt(AdminKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The signed-in account's id on the server (empty when unknown).</summary>
        public static string AccountId
        {
            get => SignedIn ? PlayerPrefs.GetString(AccountKey, "") : "";
            set { PlayerPrefs.SetString(AccountKey, value ?? ""); PlayerPrefs.Save(); }
        }

        public static void SignOut()
        {
            Token = "";
            IsAdmin = false;
            AccountId = "";
        }

        static string Config(string key, string fallback)
        {
            try { return Json.ParseObject(Art.Text("Data/config")).Str(key, fallback); }
            catch (System.Exception) { return fallback; }
        }
    }
}
