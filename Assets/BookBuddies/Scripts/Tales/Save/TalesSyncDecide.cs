using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Tales;

namespace BookBuddies.Net
{
    /// <summary>
    /// One "Which adventure stays?" question (TownSheets.AskAdventure): one pet's progress on two sides that both have a hero,
    /// what each side is called, when each was last played, and the side picked to start with ("a" or "b"). The answer is
    /// the side that stays.
    /// </summary>
    public sealed class AdventureAsk { public string Lead; public TalesSave A, B; public string NameA, NameB; public double AtA, AtB; public string Preselect; }

    /// <summary>
    /// What settling decided: the save to keep (owned by the account), whether this device's save goes aside first (Stash),
    /// the merges folded in, whether it all goes up (Upload), and the steps taken in order ("claim", "restore", "stash",
    /// "server", "import", "new", "discard &lt;id&gt;", "fold &lt;id&gt;", "upload").
    /// </summary>
    public sealed class Settled
    {
        public TalesSave Save;
        public bool Stash, Upload;
        public readonly List<string> Folded = new List<string>(), Steps = new List<string>();
    }

    // The pure half of TalesSync (Net/TalesSync.cs), kept with the saves so the Tales tests run it: what entering town does
    // with the account's game save.
    public static partial class TalesSync
    {
        const string OnDevice = "On this device", OnSite = "On bookbuddies.pet";

        /// <summary>
        /// Settles this device's save (local, unchanged) against GET /me/tales (reply; null when the server has no such route)
        /// for the signed-in account (acct). stashed(acct) reads that account's save put aside on this device (in its own
        /// folder); ask is the adventure card, asked only when a pet has hero progress in both saves.
        /// </summary>
        public static async Task<Settled> Decide(TalesSave local, Dictionary<string, object> reply, string acct,
            Func<string, TalesSave> stashed, Func<AdventureAsk, Task<string>> ask, double now)
        {
            var r = new Settled();
            var merges = new List<Dictionary<string, object>>();
            foreach (var o in reply.Arr("merges")) if (o is Dictionary<string, object> m) merges.Add(m);

            // 1. whose save this is: this account's; nobody's yet (a file from before accounts, or the empty one signing out
            // leaves, which gives way to this account's put-aside save); a buddy's that joined this account elsewhere (devB,
            // folded in below); or another account's, which goes aside while this account's own comes back
            TalesSave save = local, devB = null;
            string name = OnDevice;
            if (local.Owner == acct) { }
            else if (local.Owner == "")
            {
                save = local.AnyHero ? null : stashed(acct);
                if (save != null) r.Steps.Add("restore");
                else
                {
                    save = TalesSave.FromObject(local.ToObject());
                    save.Owner = acct;
                    r.Steps.Add("claim");
                }
            }
            else if (merges.Exists(m => m.Str("from") == local.Owner)) { devB = local; save = null; }
            else
            {
                r.Stash = true;
                r.Steps.Add("stash");
                save = stashed(acct);
                if (save != null) r.Steps.Add("restore");
                else save = new TalesSave { Owner = acct };
            }

            // 2. the account's own save: the server's when another device played later (last one wins), and the website's
            // Tales progress taken in once, when there's no game save up there yet
            var server = reply.Obj("save") is Dictionary<string, object> up ? TalesSave.FromObject(up) : null;
            if (server != null && reply.Num("at") > 0) server.At = reply.Num("at");
            string petsave = reply.Str("petsave");
            var site = server == null && petsave.Length > 0 ? SiteImport.FromPetsave(petsave, acct, reply.Num("site_at")) : null;
            if (save == null)
            {
                save = server ?? site ?? new TalesSave { Owner = acct };
                name = OnSite;
                r.Steps.Add(server != null ? "server" : site != null ? "import" : "new");
            }
            else if (server != null && server.At > save.At)
            {
                save = server;
                name = OnSite;
                r.Steps.Add("server");
            }
            else if (site != null && save.SiteIn == 0)
            {
                (save, name) = await Join(save, name, site, OnSite,
                    "Your bookbuddies.pet buddy and this device both have Tales progress. Pick the one to keep playing.", acct, now, ask);
                r.Steps.Add("import");
            }

            // 3. buddies that joined this account: each one's save is folded in once, and this device's save of one too unless
            // the merge holds it already (it went up before the merge); one that never went up, as when the merge was made
            // elsewhere, is folded in
            foreach (var m in merges)
            {
                string from = m.Str("from");
                bool folded = m.Truthy("folded");
                var mine = devB != null && devB.Owner == from ? devB : null;
                if (mine != null) devB = null;
                if (mine != null && folded && mine.At <= m.Num("save_at"))
                {
                    mine = null;
                    r.Steps.Add("discard " + from);
                }
                if (folded && mine == null) continue;
                var theirs = m.Obj("save") is Dictionary<string, object> s ? TalesSave.FromObject(s) : null;
                if (theirs != null && m.Num("save_at") > 0) theirs.At = m.Num("save_at");
                var other = mine == null || (theirs != null && theirs.At > mine.At) ? theirs : mine;
                if (other != null)
                {
                    string joined = JoinedName(reply, m);
                    (save, name) = await Join(save, name, other, joined + "’s adventure",
                        $"{joined} joined this account, and both have Tales progress. Pick the one to keep playing.", acct, now, ask);
                }
                r.Folded.Add(from);
                r.Steps.Add("fold " + from);
            }

            // 4. it belongs to this account, and goes up when the server has none, has an older one, or merges were folded
            save.Owner = acct;
            r.Save = save;
            r.Upload = reply != null && (server == null || save.At > server.At || r.Folded.Count > 0);
            if (r.Upload) r.Steps.Add("upload");
            return r;
        }

        // two saves made one, each pet keeping its own progress: when a pet has a hero on both sides the player picks the
        // side that stays (shown as that pet's), else a stays unless only b has a hero; also gives the name of the side that
        // stayed, for a question after this one
        static async Task<(TalesSave, string)> Join(TalesSave a, string nameA, TalesSave b, string nameB, string lead, string acct, double now,
            Func<AdventureAsk, Task<string>> ask)
        {
            bool keepB = b.AnyHero && !a.AnyHero;
            if (TalesMerge.Clash(a, b) is (TalesSave, TalesSave) clash)
            {
                var (pa, pb) = clash;
                keepB = await ask(new AdventureAsk { Lead = lead, A = pa, B = pb, NameA = nameA, NameB = nameB, AtA = pa.At, AtB = pb.At, Preselect = Recent(pa, pb) }) == "b";
            }
            return keepB ? (TalesMerge.Merge(b, a, acct, now), nameB) : (TalesMerge.Merge(a, b, acct, now), nameA);
        }

        // the side to start with: the more recently played, else the higher Pet Lv, else a (the account's own)
        static string Recent(TalesSave a, TalesSave b) =>
            a.At != b.At ? (a.At > b.At ? "a" : "b") : Level(b) > Level(a) ? "b" : "a";

        static int Level(TalesSave s) => HeroFactory.Renown(s.Me.Rxp).lvl + 1;

        // the buddy that joined: the name of the pet it was playing (its active pet then), from the account's pets
        static string JoinedName(Dictionary<string, object> reply, Dictionary<string, object> merge)
        {
            string id = merge.Obj("pets").Str("active");
            foreach (var o in reply.Arr("pets"))
                if (o is Dictionary<string, object> p && p.Str("id") == id && p.Str("name").Length > 0) return p.Str("name");
            return "Your other buddy";
        }
    }
}
