using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Saved on the device: which server to talk to, and for each server its sign-in token, the account id and whether
    /// that account is a town admin. The default server comes from Resources/BookBuddies/Data/config.json, so pointing a
    /// build at your own Worker is a one-line change. Players can still pick another server in Settings → Account, or
    /// play offline: the "local" server is the game's own backend on this PC (Local/LocalServer.cs). Each server keeps its
    /// own sign-in, so switching to offline and back never signs you out online.
    /// </summary>
    public static class Settings
    {
        /// <summary>The server for offline play: the Local backend inside the game, saved on this PC.</summary>
        public const string Local = "local";
        const string ServerKey = "bb.server";
        const string OnlineKey = "bb.online"; // the last server that wasn't this PC
        // per server: "bb.token|https://…"; the bare keys are from before servers kept their own sign-in
        const string TokenKey = "bb.token";
        const string AdminKey = "bb.admin";
        const string AccountKey = "bb.account";
        static string configServer, devServer, version;
        static bool migrated;

        /// <summary>Raised after the server changes (to another one, or between online and offline).</summary>
        public static event System.Action ServerChanged;

        public static string DefaultServer => configServer ?? (configServer = Config("server", "https://bookbuddies.pet").TrimEnd('/'));
        /// <summary>The test server (config.json "devServer"; wrangler dev's address when unset).</summary>
        public static string DevServer => devServer ?? (devServer = Config("devServer", "http://localhost:8787").TrimEnd('/'));
        public static string Version => version ?? (version = Config("version", "0.3"));

        /// <summary>Which server is in use: 0 the main one, 1 the dev one, 2 an address typed in, 3 offline on this PC.</summary>
        public static int ServerChoice => IsLocal ? 3 : Server == DefaultServer ? 0 : Server == DevServer ? 1 : 2;

        /// <summary>The server's address, or Local while playing offline. Setting it keeps every server's sign-in.</summary>
        public static string Server
        {
            get => PlayerPrefs.GetString(ServerKey, DefaultServer).TrimEnd('/');
            set
            {
                string from = Server, to = Address(value);
                if (to == from) return;
                Migrate(); // the old keys belong to the server we're leaving
                PlayerPrefs.SetString(ServerKey, to);
                PlayerPrefs.SetString(OnlineKey, to != Local ? to : from);
                PlayerPrefs.Save();
                ServerChanged?.Invoke();
            }
        }

        /// <summary>Playing offline: every call goes to the backend on this PC and the live town runs inside the game.</summary>
        public static bool IsLocal => Server == Local;

        /// <summary>The online server: the one in use, or while offline the last one used (the main one when none was).</summary>
        public static string OnlineServer => IsLocal ? Address(PlayerPrefs.GetString(OnlineKey, DefaultServer)) : Server;

        /// <summary>The sign-in token saved for a server (empty when not signed in there).</summary>
        public static string TokenFor(string server) => PlayerPrefs.GetString(Key(TokenKey, server), "");

        /// <summary>The sign-in token for the server in use.</summary>
        public static string Token
        {
            get => TokenFor(Server);
            set { PlayerPrefs.SetString(Key(TokenKey, Server), value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Just the server's name, like "bookbuddies.pet" ("this PC" offline).</summary>
        public static string ServerHost => HostOf(Server);

        /// <summary>A server's name, like "bookbuddies.pet".</summary>
        public static string HostOf(string server) =>
            server == Local ? "this PC" : System.Uri.TryCreate(server, System.UriKind.Absolute, out var uri) ? uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) : server;

        public static bool SignedIn => Token.Length > 0;

        /// <summary>
        /// Whether the signed-in account is a town admin, as the server last said (the server checks again on every admin
        /// call). Never offline: the admin tools work on the online server only.
        /// </summary>
        public static bool IsAdmin
        {
            get => SignedIn && !IsLocal && PlayerPrefs.GetInt(Key(AdminKey, Server), 0) == 1;
            set { PlayerPrefs.SetInt(Key(AdminKey, Server), value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The signed-in account's id on the server in use (empty when unknown).</summary>
        public static string AccountId
        {
            get => SignedIn ? PlayerPrefs.GetString(Key(AccountKey, Server), "") : "";
            set { PlayerPrefs.SetString(Key(AccountKey, Server), value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Signs out of the server in use.</summary>
        public static void SignOut() => SignOut(Server);

        /// <summary>Signs out of one server; the others keep their sign-in.</summary>
        public static void SignOut(string server)
        {
            PlayerPrefs.DeleteKey(Key(TokenKey, server));
            PlayerPrefs.DeleteKey(Key(AdminKey, server));
            PlayerPrefs.DeleteKey(Key(AccountKey, server));
            PlayerPrefs.Save();
        }

        // a typed address made whole: "https://" in front (http for localhost), no slash at the end
        static string Address(string value)
        {
            string v = string.IsNullOrWhiteSpace(value) ? DefaultServer : value.Trim().TrimEnd('/');
            if (v == Local) return v;
            return v.StartsWith("http") ? v : (v.StartsWith("localhost") || v.StartsWith("127.") ? "http://" : "https://") + v;
        }

        static string Key(string key, string server)
        {
            Migrate();
            return key + "|" + server;
        }

        // once, after updating: the single sign-in from before moves to the server it was made on
        static void Migrate()
        {
            if (migrated) return;
            migrated = true;
            if (!PlayerPrefs.HasKey(TokenKey) && !PlayerPrefs.HasKey(AccountKey) && !PlayerPrefs.HasKey(AdminKey)) return;
            string server = Server, token = PlayerPrefs.GetString(TokenKey, "");
            if (token.Length > 0 && server != Local && !PlayerPrefs.HasKey(TokenKey + "|" + server))
            {
                PlayerPrefs.SetString(TokenKey + "|" + server, token);
                PlayerPrefs.SetString(AccountKey + "|" + server, PlayerPrefs.GetString(AccountKey, ""));
                PlayerPrefs.SetInt(AdminKey + "|" + server, PlayerPrefs.GetInt(AdminKey, 0));
            }
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(AccountKey);
            PlayerPrefs.DeleteKey(AdminKey);
            PlayerPrefs.Save();
        }

        static string Config(string key, string fallback)
        {
            try { return Json.ParseObject(Art.Text("Data/config")).Str(key, fallback); }
            catch (System.Exception) { return fallback; }
        }
    }
}
