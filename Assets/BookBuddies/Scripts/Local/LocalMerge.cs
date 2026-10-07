// The site's routes for joining accounts and uploading the game save (SITE-CONTRACT §2.4-§2.7 and §3), answered on this
// PC: GET and PUT /me/tales, POST /merge/preview and POST /merge. The tests use LocalServer as bookbuddies.pet's
// stand-in, so the replies, "why" words, coin math, pet rules, old codes, replay and limits are the contract's. A player
// with LocalPlayer.Petsave or Site set plays a website account; the offline game sets neither, and never merges.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BookBuddies.Local
{
    /// <summary>
    /// Joining a game-only account ("from") into another ("into"): every pet comes along (6 in the nest, the rest at the
    /// Pet Inn), coins join up with one welcome gift per person, tickets, keepsakes, sign-ins and the stricter mute or
    /// break come too, and from's old code opens into. Also each account's uploaded game save.
    /// </summary>
    static class LocalMerge
    {
        const int MaxSave = 64000;   // characters of a game save as JSON
        const int MaxPets = 30;      // nest and resting, both sides together
        const int MergesPerDay = 5;  // into one account, in any 24 hours
        const int Recent = 10;       // GET /me/tales lists the newest 10 merges, and an upload folds at most 10
        const long Day = 86400000;

        static LocalStore Db => LocalServer.Store;

        /// <summary>The id of the account a merged-away code (as CleanCode gives it) joined; null when it isn't one.</summary>
        internal static string AliasOf(string code) => Db.Merges.Find(m => code != null && m.Code == code)?.Into;

        /// <summary>
        /// GET /me/tales: the uploaded game save, the website's pet save only while there is none, every pet (nest and
        /// resting), what's owned for keeps, and the newest merges into this account with their saves. Writes nothing.
        /// </summary>
        internal static Dictionary<string, object> GetTales(LocalPlayer me)
        {
            var save = Parse(me.Tales);
            return new Dictionary<string, object>
            {
                ["at"] = me.TalesAt, ["save"] = save, ["petsave"] = save == null ? me.Petsave : null, ["site_at"] = SiteAt(me),
                ["pets"] = Pets(me, true), ["active"] = me.Pets.Find(p => p.Active)?.Id ?? "", ["owned"] = LocalWallet.Owned(me, null),
                ["merges"] = Db.Merges.Where(m => m.Into == me.Id).OrderBy(m => m.At).TakeLast(Recent).Select(m => (object)new Dictionary<string, object>
                {
                    ["from"] = m.From, ["at"] = m.At, ["pets"] = m.Pets, ["folded"] = m.Folded ? 1 : 0, ["save"] = Parse(m.Tales), ["save_at"] = m.TalesAt,
                }).ToList(),
            };
        }

        /// <summary>
        /// PUT /me/tales {at, save, folded?}: the game uploads its save (owned by this account, at most 64,000 characters).
        /// An "at" older than the one kept is refused as stale; "folded" marks merges whose saves the game took in.
        /// </summary>
        internal static Dictionary<string, object> PutTales(Dictionary<string, object> body, LocalPlayer me)
        {
            Fits(body, 80000);
            var (text, at) = Upload(body, me.Id);
            if (me.TalesAt > at) throw new LocalProblem("Another device saved newer progress.", 409, "stale");
            me.Tales = text;
            me.TalesAt = at;
            var folded = body.Arr("folded").OfType<string>().Take(Recent).ToList();
            foreach (var m in Db.Merges) if (m.Into == me.Id && folded.Contains(m.From)) m.Folded = true;
            Db.Touch();
            return new Dictionary<string, object> { ["ok"] = true, ["at"] = at };
        }

        /// <summary>
        /// POST /merge/preview {code}: what joining with that code would do, changing nothing but the wrong-code count:
        /// {mode: "same"}, {mode: "switch", other} for two website accounts, or {mode: "merge", you, from, into, coins, limits}.
        /// </summary>
        internal static Dictionary<string, object> Preview(Dictionary<string, object> body, LocalPlayer me)
        {
            var typed = Typed(body);
            if (typed == me) return new Dictionary<string, object> { ["mode"] = "same" };
            if (me.Website && typed.Website)
                return new Dictionary<string, object> { ["mode"] = "switch", ["other"] = new Dictionary<string, object> { ["id"] = typed.Id, ["name"] = typed.Name, ["username"] = null } };
            var (from, into) = Pair(me, typed);
            int moves = Moves(from);
            return new Dictionary<string, object>
            {
                ["mode"] = "merge", ["you"] = from == me ? "from" : "into", ["from"] = Side(from, from != me), ["into"] = Side(into, into != me),
                ["coins"] = new Dictionary<string, object> { ["starter"] = Starter(from), ["moves"] = moves, ["total"] = LocalWallet.Balance(into.Id) + moves },
                ["limits"] = new Dictionary<string, object> { ["nest"] = LocalPets.MaxPets, ["pets"] = MaxPets },
            };
        }

        /// <summary>
        /// POST /merge {code, from, nest?, active?, tales?}: joins from into into and answers {ok, merged, again, from, into,
        /// moved, coins_moved, account}. "from" is the key: the same body again (signed in as into by then) gets the same
        /// reply with "again", and a from that joined someone else is "taken".
        /// </summary>
        internal static Dictionary<string, object> Merge(Dictionary<string, object> body, LocalPlayer me)
        {
            Fits(body, 140000);
            var typed = Typed(body);
            string asked = Js.Get(body, "from") as string ?? "";
            if (Db.Merges.Find(m => m.From == asked) is LocalMergeRow done)
            {
                if (done.Into != me.Id && done.Into != typed.Id) throw new LocalProblem("This buddy already joined another account.", 409, "taken");
                return Done(done, true);
            }
            if (typed == me) throw new LocalProblem("That’s the code you’re signed in with already.", 409, "same");
            if (me.Website && typed.Website)
                throw new LocalProblem("Both are bookbuddies.pet accounts, so they can’t be merged. Sign in with that code instead.", 409, "website");
            var (from, into) = Pair(me, typed);
            if (from.Id != asked) throw new LocalProblem("Something changed since you checked. Check the code again.", 409, "changed");
            long now = LocalServer.Now;
            if (Db.Merges.Count(m => m.Into == into.Id && m.At > now - Day) >= MergesPerDay)
                throw new LocalProblem("This account joined lots of buddies today. Try again tomorrow.", 429, "busy");
            if (from.Pets.Count + into.Pets.Count > MaxPets) throw new LocalProblem($"That’s more pets than one account can keep ({MaxPets}).", 409, "pets");
            var (nest, active) = Nest(body, from, into);
            string tales = null;
            long talesAt = 0;
            if (Js.Get(body, "tales") is object sent) (tales, talesAt) = Upload(sent, into.Id);
            return Join(from, into, nest, active, tales, talesAt, now);
        }

        // gameMerge, the site's one batch. Nothing of from's is dropped: its pets, coins (less its welcome gift), tickets,
        // fair counters, offline sessions and sign-ins move to into, and only then does its row go.
        static Dictionary<string, object> Join(LocalPlayer from, LocalPlayer into, List<LocalPet> nest, LocalPet active, string tales, long talesAt, long now)
        {
            // pets: from's move over, renamed when into has that id already; the nest's are home and the rest nap
            var fromActive = from.Pets.Find(p => p.Active);
            var moved = new List<object>();
            var ids = new List<object>();
            foreach (var p in LocalPets.Sorted(from))
            {
                string was = p.Id;
                while (into.Pets.Exists(q => q.Id == p.Id)) p.Id = "p" + LocalAccounts.Hex(LocalAccounts.RandomBytes(5));
                into.Pets.Add(p);
                moved.Add(new Dictionary<string, object> { ["was"] = was, ["id"] = p.Id });
                ids.Add(p.Id);
            }
            foreach (var p in into.Pets)
            {
                p.Rest = !nest.Contains(p);
                p.Active = p == active;
            }
            if (into.Petsave == null) into.Pet = active.Look;

            // coins: from's rows into hasn't got move over (not its welcome gift), and one merge row makes into's balance grow
            // by exactly Moves(from); tickets add up in full, the rows into has already summed in one row
            int moves = Moves(from);
            var coins = Movable(from.Coins, into.Coins).Where(r => r.Kind != "starter").ToList();
            Append(into.Coins, coins, MergeRow(from, moves - coins.Sum(r => r.Amount), now));
            var tickets = Movable(from.Tickets, into.Tickets);
            int left = from.Tickets.Sum(r => r.Amount) - tickets.Sum(r => r.Amount);
            Append(into.Tickets, tickets, left != 0 ? MergeRow(from, left, now) : null);
            if (from.Fair != null && into.Fair != null)
            {
                into.Fair.Balls += from.Fair.Balls;
                into.Fair.Tix += from.Fair.Tix;
                into.Fair.Drops += from.Fair.Drops;
            }
            else into.Fair ??= from.Fair;
            LocalWallet.CloseSession(from.Id); // its offline coins still to bank come along
            into.Sessions.InsertRange(0, from.Sessions);

            // from's sign-ins now open into, the stricter mute or break stays, and the game's merged save is into's
            foreach (string hash in Db.Tokens.Where(t => t.Value == from.Id).Select(t => t.Key).ToList()) Db.Tokens[hash] = into.Id;
            into.MuteUntil = Math.Max(into.MuteUntil, from.MuteUntil);
            into.BanUntil = Math.Max(into.BanUntil, from.BanUntil);
            if (tales != null)
            {
                into.Tales = tales;
                into.TalesAt = talesAt;
            }

            // the receipt (from's old code opens into from now on); accounts merged into from earlier now point at into
            foreach (var m in Db.Merges) if (m.Into == from.Id) m.Into = into.Id;
            var row = new LocalMergeRow
            {
                From = from.Id, Into = into.Id, Code = from.Code, At = now, Moved = moved, CoinsMoved = moves,
                Pets = new Dictionary<string, object> { ["ids"] = ids, ["active"] = fromActive?.Id ?? "" },
                Tales = from.Tales, TalesAt = from.TalesAt, Folded = tales != null || from.Tales == null,
            };
            Db.Merges.Add(row);
            Db.Players.Remove(from.Id);
            Db.Touch();
            return Done(row, false);
        }

        // the done reply, the same each time that merge is asked for
        static Dictionary<string, object> Done(LocalMergeRow m, bool again) => new Dictionary<string, object>
        {
            ["ok"] = true, ["merged"] = true, ["again"] = again, ["from"] = m.From, ["into"] = m.Into, ["moved"] = m.Moved,
            ["coins_moved"] = m.CoinsMoved, ["account"] = LocalAccounts.Me(LocalServer.Player(m.Into)),
        };

        // The account a typed code opens: by its own code, or the old code of an account merged into it. A code of the wrong
        // shape, or one that opens nothing, counts as a wrong code like on /link/claim.
        static LocalPlayer Typed(Dictionary<string, object> body)
        {
            string key = LocalAccounts.Tries("tries");
            string code = LocalAccounts.CleanCode(Js.Get(body, "code"));
            var typed = code == null ? null : LocalAccounts.WithCode(code) ?? LocalServer.Player(AliasOf(code));
            if (typed != null) return typed;
            LocalAccounts.CountWrongCode(key);
            throw code == null ? new LocalProblem("Enter your recovery code, like BB-XXXXX-XXXXX", 400, "code")
                : new LocalProblem("That code doesn’t match an account. Check each letter.", 404, "code");
        }

        // Who joins whom: only a game-only account merges, into the other one (into the typed one when both are game-only).
        // Bots and sample readers never merge. Both sides' first pets are seeded here, as gamePetList does.
        static (LocalPlayer from, LocalPlayer into) Pair(LocalPlayer me, LocalPlayer typed)
        {
            if (me.Locked || typed.Locked) throw new LocalProblem("That account can’t be merged.", 403, "locked");
            LocalPets.FirstPet(me);
            LocalPets.FirstPet(typed);
            return me.Website ? (typed, me) : (me, typed);
        }

        // one side of the preview; the side that isn't the caller carries its saves too (the typed code proves access)
        static Dictionary<string, object> Side(LocalPlayer p, bool other)
        {
            var side = new Dictionary<string, object>
            {
                ["id"] = p.Id, ["name"] = p.Name, ["username"] = null, ["website"] = p.Website, ["coins"] = LocalWallet.Balance(p.Id),
                ["pets"] = Pets(p, false), ["save_at"] = p.TalesAt, ["site_at"] = SiteAt(p),
            };
            if (other)
            {
                side["save"] = Parse(p.Tales);
                side["petsave"] = p.Petsave;
            }
            return side;
        }

        // every pet, the nest first and then the resting ones, oldest first in each
        static List<object> Pets(LocalPlayer p, bool born) => LocalPets.Sorted(p).OrderBy(q => q.Rest).Select(q =>
        {
            var o = new Dictionary<string, object> { ["id"] = q.Id, ["name"] = q.Name, ["look"] = q.Look, ["active"] = q.Active, ["rest"] = q.Rest };
            if (born) o["born"] = q.Born;
            return (object)o;
        }).ToList();

        // The nest after the merge and its active pet. Asked for: 1 to 6 different pets of either side, and an active one
        // among them. Otherwise into's active pet, into's nest, from's nest, into's resting pets, then from's, the first 6;
        // the active pet is then into's when it is in the nest, else the first.
        static (List<LocalPet> nest, LocalPet active) Nest(Dictionary<string, object> body, LocalPlayer from, LocalPlayer into)
        {
            LocalPet Pick(object o) // {side, id}
            {
                var e = o as Dictionary<string, object>;
                var side = e.Str("side") == "from" ? from : e.Str("side") == "into" ? into : null;
                return side?.Pets.Find(p => p.Id == e.Str("id"));
            }
            List<LocalPet> nest;
            if (Js.Get(body, "nest") is object asked)
            {
                nest = (asked as List<object>)?.Select(Pick).ToList();
                if (nest == null || nest.Count < 1 || nest.Count > LocalPets.MaxPets || nest.Contains(null) || nest.Distinct().Count() < nest.Count)
                    throw new LocalProblem($"Pick 1 to {LocalPets.MaxPets} pets for the nest.", 400, "nest");
            }
            else
            {
                List<LocalPet> i = LocalPets.Sorted(into), f = LocalPets.Sorted(from);
                nest = i.Where(p => p.Active).Concat(i.Where(p => !p.Rest)).Concat(f.Where(p => !p.Rest))
                    .Concat(i.Where(p => p.Rest)).Concat(f.Where(p => p.Rest)).Distinct().Take(LocalPets.MaxPets).ToList();
            }
            var active = Pick(Js.Get(body, "active"));
            var home = into.Pets.Find(p => p.Active);
            return (nest, nest.Contains(active) ? active : nest.Contains(home) ? home : nest[0]);
        }

        // what from's coins add to into: its balance less its welcome gift, never below 0 (one gift per person)
        static int Moves(LocalPlayer from) => Math.Max(0, from.Coins.Sum(r => r.Amount) - Starter(from));

        static int Starter(LocalPlayer p) => p.Coins.Find(r => r.Kind == "starter" && r.Ref == "once")?.Amount ?? 0;

        // from's ledger rows that into hasn't got (the same kind and ref)
        static List<LedgerRow> Movable(List<LedgerRow> from, List<LedgerRow> into) =>
            from.Where(r => !into.Exists(x => x.Kind == r.Kind && x.Ref == r.Ref)).ToList();

        // moved rows join into's ledger in time order, then the merge row (if any) comes last
        static void Append(List<LedgerRow> ledger, List<LedgerRow> rows, LedgerRow merge)
        {
            var all = ledger.Concat(rows).OrderBy(r => r.At).ToList();
            if (merge != null) all.Add(merge);
            ledger.Clear();
            ledger.AddRange(all);
        }

        static LedgerRow MergeRow(LocalPlayer from, int amount, long now) =>
            new LedgerRow { Kind = "merge", Ref = "from:" + from.Id, Amount = amount, Day = LocalWallet.Today(), At = now };

        // an uploaded game save {at, save}, checked as the site does: its JSON text and its time
        static (string text, long at) Upload(object upload, string owner)
        {
            var o = upload as Dictionary<string, object>;
            if (!(Js.Get(o, "save") is Dictionary<string, object> save)) throw Bad();
            string text = Json.Write(save);
            if (text.Length > MaxSave) throw new LocalProblem("Your adventure save is too big.", 413, "size");
            if (save.Str("owner") != owner) throw new LocalProblem("That adventure belongs to another account.", 409, "owner");
            double at = Js.Get(o, "at") is double d ? d : 0;
            if (!Js.IsInteger(at) || at <= 0 || at > LocalServer.Now + Day) throw Bad();
            return (text, (long)at);
        }

        // readBody's limit: a longer body reads as none
        static void Fits(Dictionary<string, object> body, int limit)
        {
            if (Json.Write(body).Length > limit) throw Bad();
        }

        static LocalProblem Bad() => new LocalProblem("That request couldn’t be read.", 400, "bad");

        static long SiteAt(LocalPlayer p) => p.Petsave != null ? p.Updated : 0;

        static object Parse(string save) => save == null ? null : Json.Parse(save);
    }
}
