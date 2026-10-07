// Ports Server/src/index.js:18-55 (the routes and the replies to a refusal or a crash) and db.js:97-106 (the towns and
// rooms). The game's offline server: BBApi.Send hands it the same requests it would send the Worker. Admin (/admin/*)
// and Tales (/quest/*, /notes) stay online, so offline they answer 404 like any unknown route.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BookBuddies.Local
{
    /// <summary>
    /// The Unity Worker's account, pet, wallet and town-ticket routes, answered on this PC from one JSON file
    /// (LocalStore). Replies, status codes and messages match the online server's. Pure C#: Boot sets FilePath.
    /// GET /party is offline only: the party in a hosted world (LocalParty).
    /// </summary>
    public static class LocalServer
    {
        /// <summary>The save file (&lt;persistentDataPath&gt;/offline/world.json); none keeps the store in memory only.</summary>
        public static string FilePath;
        /// <summary>The time every route uses (tests move it).</summary>
        public static Func<DateTimeOffset> Clock = () => DateTimeOffset.UtcNow;
        /// <summary>Why the last save failed (disk full, file locked…); null after a save that worked.</summary>
        public static string SaveError { get; private set; }

        /// <summary>The towns along Bramble Road after Pawtopia, in order. Road link l runs from the town before it to the l-th of these.</summary>
        public static readonly string[] GenreTowns =
        {
            "romance", "classics", "adventure", "mystery", "fantasy", "scifi", "horror", "cozy", "fairytale",
            "poetry", "western", "ocean", "historical", "thriller", "dystopia", "myth", "gothic",
        };
        /// <summary>The live places: Pawtopia, the 17 towns, the 17 road links ("road1" … "road17") and the Inkwell Caves.</summary>
        public static readonly string[] Towns =
            new[] { "pawtopia" }.Concat(GenreTowns).Concat(GenreTowns.Select((_, i) => "road" + (i + 1))).Concat(new[] { "caves" }).ToArray();
        /// <summary>Each place has rooms 1 to Rooms.</summary>
        public const int Rooms = 6;

        const string Version = "0.3";
        static LocalStore store;
        static readonly object writing = new object();

        /// <summary>Everything kept offline, loaded from FilePath on first use (and again if FilePath changes).</summary>
        public static LocalStore Store => store != null && store.Source == FilePath ? store : (store = LocalStore.Load(FilePath));

        /// <summary>Clock's time in ms since 1970, as JS Date.now().</summary>
        public static long Now => Clock().ToUnixTimeMilliseconds();

        /// <summary>
        /// Answers one API request like the Worker: "pathAndQuery" as after /api ("/me", "/plaza/world/ticket?s=1"), the
        /// JSON body (or null) and the sign-in token (or null). The reply is shaped like a parsed HTTP reply (numbers are
        /// doubles); a refusal fails the task with a LocalProblem carrying the Worker's message and status.
        /// </summary>
        public static Task<Dictionary<string, object>> Handle(string method, string pathAndQuery, Dictionary<string, object> body, string token)
        {
            try { return Task.FromResult(Copy(Route(method, pathAndQuery ?? "", Copy(body), token))); }
            catch (LocalProblem p) { return Task.FromException<Dictionary<string, object>>(p); }
            catch (Exception e)
            {
                return Task.FromException<Dictionary<string, object>>(new LocalProblem("Something went wrong on the server. Please try again.", 500, e));
            }
        }

        static Dictionary<string, object> Route(string method, string pathAndQuery, Dictionary<string, object> body, string token)
        {
            int q = pathAndQuery.IndexOf('?');
            string path = q < 0 ? pathAndQuery : pathAndQuery.Substring(0, q), query = q < 0 ? "" : pathAndQuery.Substring(q + 1);
            if (path.StartsWith("/api", StringComparison.Ordinal)) path = path.Substring(4);

            if (path == "/health") return new Dictionary<string, object> { ["ok"] = true, ["server"] = "bookbuddies-unity", ["version"] = Version, ["accounts"] = true };
            if (path == "/register" && method == "POST") return LocalAccounts.Register(body);
            if (path == "/link/claim" && method == "POST") return LocalAccounts.SignIn(body);

            var me = LocalAccounts.SignedIn(token);
            if (path == "/me" && method == "GET") return LocalAccounts.Me(me);
            if (path == "/me" && method == "PATCH") return LocalAccounts.UpdateMe(body, me);
            if (path == "/me/recovery" && method == "GET") return LocalAccounts.Recovery(me);
            if (path == "/me/delete" && method == "POST") return LocalAccounts.DeleteMe(me);
            if (path == "/me/pets" || path.StartsWith("/me/pets/", StringComparison.Ordinal)) return LocalPets.Route(method, path, body, me);
            if (path == "/plaza/world/ticket" && method == "GET") return LocalAccounts.TownTicket(me, query);
            if (path == "/party" && method == "GET") return LocalParty.Get(me);
            if (path == "/wallet" || path.StartsWith("/wallet/", StringComparison.Ordinal)) return LocalWallet.Route(method, path, body, me);
            throw new LocalProblem("Not found", 404);
        }

        // a fresh copy through JSON text: the body reads like the Worker's readJson, the reply like an HTTP reply
        static Dictionary<string, object> Copy(Dictionary<string, object> o)
        {
            if (o == null) return new Dictionary<string, object>();
            try { return Json.ParseObject(Json.Write(o)) ?? new Dictionary<string, object>(); }
            catch (FormatException) { return new Dictionary<string, object>(); }
        }

        /// <summary>A player on this PC by id; null when unknown.</summary>
        public static LocalPlayer Player(string pid) => pid != null && Store.Players.TryGetValue(pid, out var p) ? p : null;

        /// <summary>The id of the player a sign-in token belongs to; null when unknown.</summary>
        public static string PidForToken(string token) =>
            token != null && Store.Tokens.TryGetValue(LocalAccounts.Hash(token.ToLowerInvariant()), out var pid) ? pid : null;

        // ---- saving ----

        /// <summary>Writes the store now (temp file, then replace) if anything changed since the last save.</summary>
        public static void SaveNow() => Snapshot()?.Invoke();

        /// <summary>Serializes the store now on the caller's thread and writes it on a background thread, atomically.</summary>
        public static Task SaveAsync() => Snapshot() is Action write ? Task.Run(write) : Task.CompletedTask;

        /// <summary>Tests: forgets the loaded store, so the next use reads FilePath again.</summary>
        public static void Reset()
        {
            store = null;
            SaveError = null;
        }

        // the store as text now, and the write that puts it on disk; null when nothing changed since the last save
        static Action Snapshot()
        {
            var s = store;
            if (s == null || string.IsNullOrEmpty(s.Source) || s.Changes == Interlocked.Read(ref s.Written)) return null;
            if (s.ReadFailed)
            {
                SaveError = "The offline save couldn’t be opened, so this session isn’t saved.";
                return null;
            }
            long seq = s.Changes;
            string text = s.ToText();
            return () => Write(s, text, seq);
        }

        // one write at a time; a snapshot older than the one on disk is dropped
        static void Write(LocalStore s, string text, long seq)
        {
            lock (writing)
            {
                if (seq <= Interlocked.Read(ref s.Written)) return;
                try
                {
                    LocalStore.WriteAtomic(s.Source, text);
                    Interlocked.Exchange(ref s.Written, seq);
                    SaveError = null;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { SaveError = e.Message; }
            }
        }
    }
}
