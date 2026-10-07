using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Two Tales saves made into one, when accounts join or the website's progress comes in. Each pet keeps its own
    /// progress; a pet both saves have becomes one (JoinPet), keep's staying whole unless only the other's has a hero. A
    /// save from before pets had their own counts as the other side's active pet. The account's parts join up (the codex,
    /// classes, keepsakes, library levels) and keep's daily limits stay. Pets themselves aren't in here: they're rows on
    /// the server, and all of them are kept.
    /// Pure: neither save is changed.
    /// </summary>
    public static class TalesMerge
    {
        /// <summary>The merged save, owned by owner and dated now.</summary>
        public static TalesSave Merge(TalesSave keep, TalesSave other, string owner, double now)
        {
            var (m, o) = Keyed(keep, other);
            foreach (string k in o.Pets)
                m.PutPet(k, m.PetOf(k) is Dictionary<string, object> mine ? JoinPet(mine, o.PetOf(k), true) : o.PetOf(k));

            m.Unseen.UnionWith(o.Unseen);
            Union(m.Seen, o.Seen);
            Union(m.Met, o.Met);
            Union(m.ClassesUnlocked, o.ClassesUnlocked);
            Union(m.Keepsakes, o.Keepsakes);
            foreach (var kv in o.Meta) m.Meta[kv.Key] = Math.Max(kv.Value, HeroFactory.Library(m, kv.Key));

            m.Owner = owner;
            m.At = now;
            m.SiteIn = Math.Max(keep.SiteIn, other.SiteIn);
            return m;
        }

        /// <summary>The pet both saves have a hero for, as a save of its own from each side (a's active pet first); null when there's none.</summary>
        public static (TalesSave a, TalesSave b)? Clash(TalesSave a, TalesSave b)
        {
            (a, b) = Keyed(a, b);
            foreach (string k in a.Pets)
                if (TalesSave.IsHero(a.PetOf(k)) && TalesSave.IsHero(b.PetOf(k))) return (a.PetSave(k), b.PetSave(k));
            return null;
        }

        // copies of both, a save from before pets had their own taking the other side's active pet
        static (TalesSave, TalesSave) Keyed(TalesSave a, TalesSave b)
        {
            a = TalesSave.FromObject(a.ToObject());
            b = TalesSave.FromObject(b.ToObject());
            if (b.Pet == TalesSave.Unkeyed) b.UsePet(a.Pet);
            if (a.Pet == TalesSave.Unkeyed) a.UsePet(b.Pet);
            return (a, b);
        }

        /// <summary>
        /// One pet's progress from two (PetObject's shape): the one with a hero stays whole with its road, else a when preferA.
        /// The other's renown waits for its class, its bought moves stay, its bag and what it wore go in the bag, and its
        /// dust, towns walked to, Book Bosses and new-item dots join up. Dated by the later of the two.
        /// </summary>
        public static Dictionary<string, object> JoinPet(Dictionary<string, object> a, Dictionary<string, object> b, bool preferA)
        {
            bool heroA = TalesSave.IsHero(a), heroB = TalesSave.IsHero(b);
            bool keepA = heroA != heroB ? heroA : preferA;
            var m = TalesSave.FromObject(keepA ? a : b);
            var other = TalesSave.FromObject(keepA ? b : a);
            var me = m.Me;

            // the other hero's renown waits for its class (HeroFactory.SetClass brings it back); the kept class is left alone
            string kept = HeroFactory.ClassOf(m);
            void Aside(string cls, double rxp)
            {
                if (cls != kept && rxp > (me.ClassRxp.TryGetValue(cls, out var r) ? r : 0)) me.ClassRxp[cls] = rxp;
            }
            foreach (var kv in other.Me.ClassRxp) Aside(kv.Key, kv.Value);
            Aside(HeroFactory.ClassOf(other), other.Me.Rxp);

            // bought moves were paid for, so they all stay
            foreach (var kv in other.Me.Own)
                foreach (var k in kv.Value) HeroFactory.AddMove(m, kv.Key, k);

            // the other's bag, then what it wore; exact copies of what's here already are dropped
            void Bag(string item)
            {
                if (!m.Bag.Contains(item) && !me.Gear.ContainsValue(item)) m.Bag.Add(item);
            }
            foreach (var item in other.Bag) Bag(item);
            foreach (var item in other.Me.Gear.Values) Bag(item);
            m.Dust += other.Dust;

            m.Fresh.UnionWith(other.Fresh);
            Union(m.TownsSeen, other.TownsSeen);
            foreach (var kv in other.Gyms) m.Gyms[kv.Key] = Math.Max(kv.Value, m.Gyms.TryGetValue(kv.Key, out var t) ? t : 0);

            var pet = m.PetObject();
            pet["at"] = Math.Max(a.Num("at"), b.Num("at"));
            return pet;
        }

        static void Union(List<string> to, List<string> from)
        {
            foreach (var x in from) if (!to.Contains(x)) to.Add(x);
        }
    }
}
