using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Tales;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Your Tales save follows your account: it's uploaded to the online server (PUT /me/tales), which is how it reaches the
    /// website and your other devices, and entering town settles it (Settle, see Decide): another account's save goes
    /// aside, a newer one from another device comes down, the website's Tales progress comes in once, and the saves of
    /// buddies that joined this account are folded in, asking which adventure stays when both have one. Uploads go at most
    /// every 2 minutes when the save changed (Push, at save points), and at once before a merge, signing out or showing the
    /// recovery code; only the save the online account owns goes, with that server's sign-in, wherever you play. A server
    /// without these routes (404 with no "why") gets none of this for the run. Main thread only.
    /// </summary>
    public static partial class TalesSync
    {
        const float Every = 120;       // seconds between uploads at save points
        const int MaxText = 64000;     // the server's limit for a save

        static float pushedAt = -Every;
        static int pushes;
        static string sent, refused;   // the save text the server has; an account that said this save isn't its own
        static bool tooBig;
        static readonly List<string> folding = new List<string>(); // merges folded into the save that the server hasn't heard of yet

        /// <summary>The server has no /me/tales: no uploads, imports or folds this run (the save stays on this device as before).</summary>
        public static bool Missing { get; private set; }

        /// <summary>The adventure card is up (Boot waits on the loading screen while it is).</summary>
        public static bool Asking { get; private set; }

        /// <summary>
        /// Entering town online and signed in: settles this device's save with the account's (Decide), asking on the card when
        /// two adventures meet, then uploads the result when the server needs it. A failed read leaves it for the next entry.
        /// Never throws.
        /// </summary>
        public static async Task Settle(Func<AdventureAsk, Task<string>> ask)
        {
            string acct = Settings.AccountIdFor(Settings.OnlineServer);
            if (acct.Length == 0) return;
            try
            {
                Dictionary<string, object> reply = null;
                if (!Missing)
                {
                    try { reply = await BBApi.Tales(); }
                    catch (BBApi.ApiError e) when (e.Status == 404 && e.Why.Length == 0) { Missing = true; }
                    catch (BBApi.ApiError) { return; }
                }
                if (Settings.AccountIdFor(Settings.OnlineServer) != acct) return; // signed in as someone else meanwhile
                var settled = await Decide(TalesSave.Current, reply, acct, TalesSave.Stashed, Asked(ask), TalesSave.Clock());
                if (settled.Stash) TalesSave.Stash();
                if (settled.Save.ToText() != TalesSave.Current.ToText()) TalesSave.Replace(settled.Save);
                if (settled.Upload) await Upload(folded: settled.Folded);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // the card counts as up from the question to the answer
        static Func<AdventureAsk, Task<string>> Asked(Func<AdventureAsk, Task<string>> ask) => async q =>
        {
            Asking = true;
            try { return await ask(q); }
            finally { Asking = false; }
        };

        /// <summary>
        /// AutoSave: uploads the save if it changed since the last upload, at most every 2 minutes unless now (pausing,
        /// quitting). A save point's own time stamp is in first. When the online server can't be reached it waits for the next.
        /// </summary>
        public static void Push(bool now = false)
        {
            if (!now && Time.realtimeSinceStartup < pushedAt + Every) return;
            pushedAt = Time.realtimeSinceStartup;
            _ = Pushed(now, ++pushes);
        }

        static async Task Pushed(bool now, int n)
        {
            if (now) TalesSave.Flush(); // its time stamped first
            else await Task.Yield();    // after the save that asked has stamped the time
            if (n == pushes) await Send(BBApi.QuickTimeout); // a push asked meanwhile sends it instead
        }

        /// <summary>
        /// Writes the save and uploads it now (seconds: the wait for an answer), marking folded merges as taken in (an upload
        /// that fails leaves them to the next one, so they aren't folded twice). True once the server has it. Never throws.
        /// </summary>
        public static Task<bool> Upload(int seconds = 6, List<string> folded = null)
        {
            if (folded != null) foreach (string id in folded) if (!folding.Contains(id)) folding.Add(id);
            TalesSave.Flush();
            return Send(seconds);
        }

        /// <summary>Signing out: the save goes up (4 s at most), then aside as this account's, and an empty one starts.</summary>
        public static async Task SignedOut()
        {
            await Upload(4);
            TalesSave.Stash();
        }

        // PUT /me/tales {at, save, folded?} on the online server with its sign-in, when the save is that account's, has been
        // played, fits and changed (or merges were folded)
        static async Task<bool> Send(int seconds)
        {
            string online = Settings.OnlineServer, acct = Settings.AccountIdFor(online);
            var save = TalesSave.Current;
            if (Missing || acct.Length == 0 || save.Owner != acct || acct == refused || save.At <= 0) return false;
            var o = save.ToObject();
            string text = Json.Write(o);
            var marks = new List<string>(folding);
            if (text == sent && marks.Count == 0) return true;
            if (text.Length > MaxText)
            {
                if (!tooBig) Debug.LogWarning($"BookBuddies: the Tales save is too big to upload ({text.Length} characters)");
                tooBig = true;
                return false;
            }
            var body = new Dictionary<string, object> { ["at"] = save.At, ["save"] = o };
            if (marks.Count > 0) body["folded"] = marks.ConvertAll(id => (object)id);
            try
            {
                await BBApi.SendTo(online, Settings.TokenFor(online), "PUT", "/me/tales", body, seconds);
                sent = text;
                folding.RemoveAll(marks.Contains);
                return true;
            }
            catch (BBApi.ApiError e)
            {
                // no such route: none of this for the run; not this account's (merged elsewhere: the next /me says so);
                // stale: a newer one comes down on the next entry; no answer: the next push tries again
                if (e.Status == 404 && e.Why.Length == 0) Missing = true;
                else if (e.Why == "owner") refused = acct;
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
