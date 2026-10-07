using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Two Tales saves made into one, when accounts join or the website's progress comes in. The game has one hero, so one
    /// stays whole (keep) with its road state; the other's renown is put aside for its class and its gear goes in the bag.
    /// What was bought or found joins up: bags (no cap, nothing dusted), dust, the codex, classes, keepsakes, library
    /// levels and Book Bosses. Pets aren't in here: they're rows on the server, and all of them are kept.
    /// Pure: neither save is changed.
    /// </summary>
    public static class TalesMerge
    {
        /// <summary>The merged save, owned by owner and dated now.</summary>
        public static TalesSave Merge(TalesSave keep, TalesSave other, string owner, double now)
        {
            var m = TalesSave.FromObject(keep.ToObject());
            var me = m.Me;

            // the other hero's renown waits for its class (HeroFactory.SetClass brings it back); the kept class is left alone
            string kept = HeroFactory.ClassOf(keep);
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
            m.Unseen.UnionWith(other.Unseen);
            Union(m.Seen, other.Seen);
            Union(m.Met, other.Met);
            Union(m.TownsSeen, other.TownsSeen);
            Union(m.ClassesUnlocked, other.ClassesUnlocked);
            Union(m.Keepsakes, other.Keepsakes);
            foreach (var kv in other.Gyms) m.Gyms[kv.Key] = Math.Max(kv.Value, m.Gyms.TryGetValue(kv.Key, out var t) ? t : 0);
            foreach (var kv in other.Meta) m.Meta[kv.Key] = Math.Max(kv.Value, HeroFactory.Library(m, kv.Key));

            m.Owner = owner;
            m.At = now;
            m.SiteIn = Math.Max(keep.SiteIn, other.SiteIn);
            return m;
        }

        static void Union(List<string> to, List<string> from)
        {
            foreach (var x in from) if (!to.Contains(x)) to.Add(x);
        }
    }
}
