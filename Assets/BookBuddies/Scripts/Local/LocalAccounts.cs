// Ports Server/src/index.js:57-150 (register, signIn, updateMe, deleteMe, account, signedIn, newToken, the recovery
// codes) and 152-166 (townTicket). Offline there are no per-IP sign-up limits, no admins and nobody on a break, and the
// town ticket needs no signature: it is "<pid>|<town>:<room>", read by LocalLink in the same process.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Local
{
    /// <summary>Accounts on this PC: hatching, signing in with a recovery code, your profile and the town ticket.</summary>
    static class LocalAccounts
    {
        const int WrongCodesPerDay = 15;
        const int PassMinutes = 15; // a pass lets you rejoin quickly without asking for a new ticket
        // "BB" plus 10 letters and digits that are hard to mix up (no 0/O, 1/I/L)
        const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        const string WrongCodes = "wrong_codes:"; // + UTC day, in the store's meta
        static readonly Regex TokenRule = new Regex("^[A-Fa-f0-9]{64}\\z");

        static LocalStore Db => LocalServer.Store;

        /// <summary>POST /register {name, pet, pets?}: a new reader hatches their egg (offline, "pets" seeds the profile with online pets).</summary>
        internal static Dictionary<string, object> Register(Dictionary<string, object> body)
        {
            string name = PlayerName(Js.Get(body, "name"));
            string pet = LocalPets.CleanPet(Js.Get(body, "pet")) ?? throw new LocalProblem("Your pet’s look is missing.");
            long now = LocalServer.Now;
            var me = new LocalPlayer { Id = Guid.NewGuid().ToString(), Name = name, Pet = pet, Code = NewRecoveryCode(), Created = now, LastSeen = now };
            me.Pet = LocalPets.Seed(me, Js.Get(body, "pets")) ?? pet;
            Db.Players[me.Id] = me;
            Db.Touch();
            return new Dictionary<string, object>
            {
                ["token"] = NewToken(me.Id), ["recovery"] = FormatCode(me.Code), ["id"] = me.Id, ["name"] = me.Name, ["pet"] = me.Pet, ["is_admin"] = false,
            };
        }

        /// <summary>POST /link/claim {code}: signs in with a recovery code (BB-XXXXX-XXXXX). Wrong guesses are limited per day.</summary>
        internal static Dictionary<string, object> SignIn(Dictionary<string, object> body)
        {
            string key = WrongCodes + DateTimeOffset.FromUnixTimeMilliseconds(LocalServer.Now).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (Db.Meta.Int(key) >= WrongCodesPerDay) throw new LocalProblem("Too many wrong codes today. Try again tomorrow.", 429);
            string code = CleanCode(Js.Get(body, "code"));
            var player = code == null ? null : Db.Players.Values.FirstOrDefault(p => p.Code == code);
            if (player == null)
            {
                CountWrongCode(key);
                throw new LocalProblem("That code didn’t match an account. Check it and try again.", 404);
            }
            var reply = new Dictionary<string, object> { ["token"] = NewToken(player.Id) };
            foreach (var kv in Account(player)) reply[kv.Key] = kv.Value;
            return reply;
        }

        /// <summary>The player a token belongs to; 401 like the Worker when it's missing or unknown.</summary>
        internal static LocalPlayer SignedIn(string token)
        {
            if (token == null || !TokenRule.IsMatch(token)) throw new LocalProblem("Please sign in.", 401);
            return LocalServer.Player(LocalServer.PidForToken(token)) ?? throw new LocalProblem("Please sign in again.", 401);
        }

        /// <summary>GET /me: the account, its coins and its pets.</summary>
        internal static Dictionary<string, object> Me(LocalPlayer me)
        {
            var reply = Account(me);
            reply["coins"] = LocalWallet.Balance(me.Id);
            foreach (var kv in LocalPets.List(me)) reply[kv.Key] = kv.Value;
            return reply;
        }

        /// <summary>PATCH /me {name?, pet?}: a new name or a new look for the active pet.</summary>
        internal static Dictionary<string, object> UpdateMe(Dictionary<string, object> body, LocalPlayer me)
        {
            string name = body.ContainsKey("name") ? PlayerName(body["name"]) : me.Name;
            bool newLook = body.ContainsKey("pet");
            string pet = newLook ? (LocalPets.CleanPet(body["pet"]) ?? throw new LocalProblem("That pet look couldn’t be read.")) : me.Pet;
            me.Name = name;
            me.Pet = pet;
            if (newLook) LocalPets.ActiveLook(me, pet);
            Db.Touch();
            return Account(me);
        }

        /// <summary>POST /me/delete: the account and everything kept with it (pets, both ledgers, the fair's counters, sessions).</summary>
        internal static Dictionary<string, object> DeleteMe(LocalPlayer me)
        {
            foreach (string hash in Db.Tokens.Where(t => t.Value == me.Id).Select(t => t.Key).ToList()) Db.Tokens.Remove(hash);
            Db.Players.Remove(me.Id);
            Db.Touch();
            return new Dictionary<string, object> { ["ok"] = true };
        }

        /// <summary>GET /me/recovery: the recovery code again.</summary>
        internal static Dictionary<string, object> Recovery(LocalPlayer me) => new Dictionary<string, object> { ["code"] = FormatCode(me.Code) };

        /// <summary>GET /plaza/world/ticket?s=&amp;town=: a ticket for room 1-6 of a place, and a pass for quick rejoins.</summary>
        internal static Dictionary<string, object> TownTicket(LocalPlayer me, string query)
        {
            double asked = Js.ParseInt(Js.Query(query, "s"));
            int shard = (int)Math.Max(1, Math.Min(LocalServer.Rooms, double.IsNaN(asked) || asked == 0 ? 1 : asked));
            string place = Js.Query(query, "town");
            string town = Array.IndexOf(LocalServer.Towns, place) >= 0 ? place : "pawtopia";
            string ticket = $"{me.Id}|{town}:{shard}";
            me.LastSeen = LocalServer.Now;
            Db.Touch();
            return new Dictionary<string, object>
            {
                ["live"] = true, ["shard"] = shard, ["town"] = town, ["ticket"] = ticket, ["pass"] = ticket, ["passExp"] = me.LastSeen + PassMinutes * 60000L,
            };
        }

        /// <summary>{id, name, pet, is_admin}, the account part of a reply.</summary>
        internal static Dictionary<string, object> Account(LocalPlayer p) =>
            new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["pet"] = p.Pet ?? "", ["is_admin"] = p.Admin };

        /// <summary>The SHA-256 of a token as lower-case hex: what the store keeps instead of the token.</summary>
        internal static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        static string PlayerName(object raw)
        {
            string name = LocalSafety.CleanText(raw, 20);
            if (name.Length < 2) throw new LocalProblem("Pick a name with at least 2 letters.");
            if (LocalSafety.TextProblem(name) != null) throw new LocalProblem("Please pick a kinder name.");
            return name;
        }

        static void CountWrongCode(string key)
        {
            int n = Db.Meta.Int(key);
            foreach (string old in Db.Meta.Keys.Where(k => k.StartsWith(WrongCodes, StringComparison.Ordinal)).ToList()) Db.Meta.Remove(old);
            Db.Meta[key] = (double)(n + 1);
            Db.Touch();
        }

        static string NewToken(string pid)
        {
            string token = Hex(RandomBytes(32));
            Db.Tokens[Hash(token)] = pid;
            Db.Touch();
            return token;
        }

        static string NewRecoveryCode() => "BB" + new string(RandomBytes(10).Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());

        static string CleanCode(object c)
        {
            string s = new string((Js.Truthy(c) ? Js.Str(c) : "").ToUpperInvariant().Where(x => (x >= 'A' && x <= 'Z') || (x >= '0' && x <= '9')).ToArray());
            return s.Length == 12 && s.StartsWith("BB", StringComparison.Ordinal) ? s : null;
        }

        static string FormatCode(string c) => string.IsNullOrEmpty(c) ? "" : $"{c.Substring(0, 2)}-{c.Substring(2, 5)}-{c.Substring(7)}";

        /// <summary>n bytes from the system's secure random generator.</summary>
        internal static byte[] RandomBytes(int n)
        {
            var bytes = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>Bytes as lower-case hex.</summary>
        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
