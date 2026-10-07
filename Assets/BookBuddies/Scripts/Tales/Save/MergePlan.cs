using System;
using System.Collections.Generic;
using System.Globalization;

namespace BookBuddies.Tales
{
    /// <summary>One pet on the join-accounts card: the side it comes from ("from" or "into"), its id there, active or resting.</summary>
    public sealed class PetRow { public string Side, Id, Name, Look; public bool Active, Rest; }

    /// <summary>One account on the join-accounts card, and the Tales save that stands for it.</summary>
    public sealed class MergeSide
    {
        public string Id, Name, Username;
        public bool Website, You;            // a bookbuddies.pet account; the one signed in here
        public int Coins;
        public List<PetRow> Pets;            // nest first, then resting
        public TalesSave Save;               // yours: this device's; the other: its game save, the website's progress, or an empty one
        public double SaveAt;                // when it was last played (ms, 0 = unknown)
        public int PetLv;
        public string ClassKey;
    }

    /// <summary>
    /// Everything the join-accounts card decides, read from the POST /merge/preview reply: both sides, the coins, the
    /// nest of 6, and the merged save and POST /merge body it sends. Every pet keeps its own adventure, so nothing is
    /// asked about them; the leading side (Hero: the most recently played) starts the nest and keeps its daily limits.
    /// Pure, so it's tested without Unity.
    /// </summary>
    public sealed class MergePlan
    {
        public const int NestSize = 6;

        public string Mode;                  // "same" | "switch" | "merge"
        public string OtherName;             // switch: the other account's name
        public MergeSide From, Into;
        public int Starter, CoinsMoved, CoinsTotal;
        public string Hero;                  // "from" | "into": the leading side
        public List<PetRow> Nest; public PetRow Active;
        public bool AskNest => From.Pets.Count + Into.Pets.Count > NestSize;
        public bool CodeIsLogin => !Into.You; // the typed code is the login afterwards

        /// <summary>The plan for a preview reply; your side uses this device's save (local).</summary>
        public static MergePlan Read(Dictionary<string, object> preview, TalesSave local, double now)
        {
            var plan = new MergePlan { Mode = preview.Str("mode"), OtherName = preview.Obj("other").Str("name") };
            if (plan.Mode != "merge") return plan;
            string you = preview.Str("you");
            var from = plan.From = Side(preview.Obj("from"), "from", you == "from", local);
            var into = plan.Into = Side(preview.Obj("into"), "into", you == "into", local);
            var coins = preview.Obj("coins");
            plan.Starter = coins.Int("starter"); plan.CoinsMoved = coins.Int("moves"); plan.CoinsTotal = coins.Int("total");

            // the side that leads: the only one with a hero, else the most recently played, else the higher Pet Lv, else into
            bool f = from.Save.AnyHero, i = into.Save.AnyHero;
            plan.Hero = !f ? "into" : !i ? "from"
                : from.SaveAt != into.SaveAt ? (from.SaveAt > into.SaveAt ? "from" : "into")
                : from.PetLv > into.PetLv ? "from" : "into";
            plan.DefaultNest();
            return plan;
        }

        static MergeSide Side(Dictionary<string, object> o, string side, bool you, TalesSave local)
        {
            var s = new MergeSide
            {
                Id = o.Str("id"), Name = o.Str("name"), Username = o.Str("username"), Website = o.Truthy("website"), You = you,
                Coins = o.Int("coins"), Pets = new List<PetRow>(),
            };
            foreach (var x in o.Arr("pets"))
                if (x is Dictionary<string, object> p)
                    s.Pets.Add(new PetRow { Side = side, Id = p.Str("id"), Name = p.Str("name"), Look = p.Str("look"), Active = p.Truthy("active"), Rest = p.Truthy("rest") });
            if (you) (s.Save, s.SaveAt) = (TalesSave.FromObject(local?.ToObject()), local?.At ?? 0);
            else if (o.Obj("save") is Dictionary<string, object> save) (s.Save, s.SaveAt) = (TalesSave.FromObject(save), o.Num("save_at"));
            else if (s.Website) (s.Save, s.SaveAt) = (SiteImport.FromPetsave(o.Str("petsave", null), s.Id, o.Num("site_at")) ?? new TalesSave(), o.Num("site_at"));
            else s.Save = new TalesSave();
            if (s.Pets.Find(p => p.Active) is PetRow on) s.Save.UsePet(on.Id); // a save from before pets had their own is the active pet's
            s.PetLv = HeroFactory.Renown(s.Save.Me.Rxp).lvl + 1;
            s.ClassKey = HeroFactory.ClassOf(s.Save);
            return s;
        }

        /// <summary>
        /// The nest before the player changes it: the staying hero's active pet, that side's nest, the other side's nest, then
        /// that side's resting pets and the other's; the first 6. The active pet is the first one.
        /// </summary>
        public void DefaultNest()
        {
            var (h, o) = Hero == "from" ? (From, Into) : (Into, From);
            var order = h.Pets.FindAll(p => p.Active);
            order.AddRange(h.Pets.FindAll(p => !p.Rest));
            order.AddRange(o.Pets.FindAll(p => !p.Rest));
            order.AddRange(h.Pets.FindAll(p => p.Rest));
            order.AddRange(o.Pets.FindAll(p => p.Rest));
            Nest = new List<PetRow>();
            foreach (var p in order) if (Nest.Count < NestSize && !Nest.Contains(p)) Nest.Add(p);
            Active = Nest.Count > 0 ? Nest[0] : null;
        }

        /// <summary>
        /// Picks a pet for the nest or unpicks it (6 at most, 1 at least). Unpicking the active pet makes the first picked one
        /// active. False when nothing changed.
        /// </summary>
        public bool Toggle(PetRow p)
        {
            if (!Nest.Contains(p))
            {
                if (Nest.Count >= NestSize) return false;
                Nest.Add(p);
                return true;
            }
            if (Nest.Count == 1) return false;
            Nest.Remove(p);
            if (Active == p) Active = Nest[0];
            return true;
        }

        /// <summary>The coin callout: both sides added up, or how much comes along when from's welcome gift stays behind.</summary>
        public string CoinLine => CoinsMoved < From.Coins
            ? $"Coins join up: {N(CoinsTotal)} in all. The {N(Starter)}-coin welcome gift counts once per person, so {N(CoinsMoved)} of {From.Name}’s {N(From.Coins)} come along."
            : $"Coins join up: {N(From.Coins)} + {N(Into.Coins)} = {N(CoinsTotal)}.";

        /// <summary>The key from's pets have in the merged save until the reply gives their ids on into's account (Landed).</summary>
        public const string Moving = "from:";

        /// <summary>The save into keeps: both sides' pets, the leading side's daily limits, playing the active pet.</summary>
        public TalesSave MergedSave(double now)
        {
            var from = TalesSave.FromObject(From.Save.ToObject());
            foreach (string k in from.Pets) from.Rekey(k, Moving + k);
            var m = Hero == "from" ? TalesMerge.Merge(from, Into.Save, Into.Id, now) : TalesMerge.Merge(Into.Save, from, Into.Id, now);
            if (Active != null) m.UsePet(Active.Side == "from" ? Moving + Active.Id : Active.Id);
            return m;
        }

        /// <summary>The merged save sent (Body's tales.save) with from's pets under their ids on into's account (the reply's moved: [{was, id}]).</summary>
        public static TalesSave Landed(Dictionary<string, object> sent, List<object> moved)
        {
            var s = TalesSave.FromObject(sent);
            foreach (var o in moved)
                if (o is Dictionary<string, object> m) s.Rekey(Moving + m.Str("was"), m.Str("id"));
            foreach (string k in s.Pets)
                if (k.StartsWith(Moving, StringComparison.Ordinal)) s.Rekey(k, k.Substring(Moving.Length)); // a site with no moved: kept their ids
            return s;
        }

        /// <summary>The POST /merge body: the code, from's id (the retry key), the nest, the active pet and the merged save.</summary>
        public Dictionary<string, object> Body(string code, double now)
        {
            var save = MergedSave(now);
            return new Dictionary<string, object>
            {
                ["code"] = code, ["from"] = From.Id,
                ["nest"] = Nest.ConvertAll(p => (object)Ref(p)), ["active"] = Ref(Active),
                ["tales"] = new Dictionary<string, object> { ["at"] = save.At, ["save"] = save.ToObject() },
            };
        }

        static Dictionary<string, object> Ref(PetRow p) => p == null ? null : new Dictionary<string, object> { ["side"] = p.Side, ["id"] = p.Id };

        /// <summary>"today", "yesterday", "3 days ago", "2 weeks ago" or "4 months ago"; "" when at is unknown (0).</summary>
        public static string Ago(double at, double now)
        {
            if (at <= 0) return "";
            int days = (int)Math.Floor(Math.Max(0, now - at) / 86400000);
            return days < 1 ? "today" : days < 2 ? "yesterday" : days < 14 ? days + " days ago" : days < 60 ? days / 7 + " weeks ago" : days / 30 + " months ago";
        }

        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    }
}
