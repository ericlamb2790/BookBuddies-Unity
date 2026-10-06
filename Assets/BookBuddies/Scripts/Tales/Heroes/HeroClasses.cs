using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Classes: the seven open from the start, what opens the rest (tqClsMetrics, tqClsOpen, tqClsCheck), the ones close
    // enough to show, and switching class with each class keeping its own progress
    public static partial class HeroFactory
    {
        /// <summary>Open from the start: the site's five base classes plus Margin Rogue and Spine Mage. The rest are earned on the road.</summary>
        public static readonly string[] BaseClasses = { "knight", "scholar", "trick", "healer", "sleuth", "rogue", "mage" };

        /// <summary>Locked classes shown as silhouettes at most (the closest ones; the rest stay hidden).</summary>
        public const int NearShown = 3;

        /// <summary>Raised by CheckClassUnlocks with the classes that just opened.</summary>
        public static event Action<List<ClassDef>> ClassesUnlocked;

        /// <summary>
        /// tqClsMetrics: what class unlocks count. lv is the best Pet Lv of any class played; bosses, wins, tales, foes and
        /// n20 are lifetime counts; seen and leg come from the gear codex. books stays 0 (Unity has no reading).
        /// </summary>
        public static Dictionary<string, double> ClassMetrics(TalesSave save)
        {
            var me = save.Me;
            double rxp = me.Rxp;
            foreach (var r in me.ClassRxp.Values) rxp = Math.Max(rxp, r);
            var m = new Dictionary<string, double> { ["lv"] = Renown(rxp).lvl + 1, ["seen"] = save.Seen.Count, ["leg"] = save.Seen.FindAll(k => k.StartsWith("L")).Count, ["books"] = 0 };
            foreach (var k in new[] { "bosses", "wins", "tales", "foes", "n20" }) m[k] = me.Stat(k);
            return m;
        }

        /// <summary>True for a base class, an unlocked one, and the class the buddy is now.</summary>
        public static bool ClassOpen(TalesSave save, string cls) =>
            Array.IndexOf(BaseClasses, cls) >= 0 || save.ClassesUnlocked.Contains(cls) || cls == ClassOf(save);

        /// <summary>tqClsProg: how far a locked class's unlock has come (capped at the goal) and the goal.</summary>
        public static (double value, double goal) ClassProgress(Dictionary<string, double> metrics, ClassDef c) =>
            (Math.Min(c.UnlockAt, metrics.TryGetValue(c.UnlockMetric ?? "", out var v) ? v : 0), c.UnlockAt);

        /// <summary>
        /// tqClsCheck: every class whose unlock is now met opens (and is marked new), then ClassesUnlocked is raised.
        /// Call it after anything its metrics count (a win, a boss, a codex find). Returns the classes that opened.
        /// </summary>
        public static List<ClassDef> CheckClassUnlocks(TalesSave save)
        {
            var m = ClassMetrics(save);
            var opened = new List<ClassDef>();
            foreach (var c in TalesData.Current.Classes.Values)
            {
                if (c.UnlockMetric == null || ClassOpen(save, c.Key)) continue;
                if (ClassProgress(m, c).value >= c.UnlockAt) opened.Add(c);
            }
            if (opened.Count == 0) return opened;
            foreach (var c in opened) UnlockClass(save, c.Key);
            save.Touch();
            ClassesUnlocked?.Invoke(opened);
            return opened;
        }

        /// <summary>Opens a class (earned, bought, or bought on another device) and marks it new. The caller saves.</summary>
        public static void UnlockClass(TalesSave save, string cls)
        {
            if (!TalesData.Current.Classes.ContainsKey(cls) || save.ClassesUnlocked.Contains(cls)) return;
            save.ClassesUnlocked.Add(cls);
            save.Unseen.Add("cls:" + cls);
        }

        /// <summary>The classes you can be, in the site's order.</summary>
        public static List<ClassDef> OpenClasses(TalesSave save)
        {
            var list = new List<ClassDef>();
            foreach (var c in TalesData.Current.Classes.Values) if (ClassOpen(save, c.Key)) list.Add(c);
            return list;
        }

        /// <summary>
        /// Locked classes close enough to show as silhouettes: halfway to their unlock or more, closest first, at most
        /// NearShown, and always the closest one so there's something to chase. "hidden" counts the rest.
        /// </summary>
        public static List<ClassDef> NearClasses(TalesSave save, out int hidden)
        {
            var m = ClassMetrics(save);
            var locked = new List<ClassDef>(TalesData.Current.Classes.Values).FindAll(c => !ClassOpen(save, c.Key));
            double Share(ClassDef c) { var p = ClassProgress(m, c); return p.goal <= 0 ? 0 : p.value / p.goal; }
            locked.Sort((a, b) => Share(a) != Share(b) ? Share(b).CompareTo(Share(a))
                : a.UnlockAt != b.UnlockAt ? a.UnlockAt.CompareTo(b.UnlockAt) : string.CompareOrdinal(a.Key, b.Key));
            var near = locked.FindAll(c => Share(c) >= .5);
            if (near.Count == 0 && locked.Count > 0) near.Add(locked[0]);
            if (near.Count > NearShown) near.RemoveRange(NearShown, near.Count - NearShown);
            hidden = locked.Count - near.Count;
            return near;
        }

        /// <summary>A class's Pet Lv: the buddy's now, the one kept from when it last played that class, or 1.</summary>
        public static int PetLvIn(TalesSave save, string cls) =>
            Renown(cls == ClassOf(save) ? save.Me.Rxp : save.Me.ClassRxp.TryGetValue(cls, out var r) ? r : 0).lvl + 1;

        /// <summary>
        /// Becomes another open class, keeping progress per class: the current class's renown is put aside and the new
        /// class's comes back (one never played starts at Pet Lv 1). Moves and loadouts are kept per class already;
        /// gear, library upgrades and lifetime stats belong to the buddy. Saves. False when it isn't open or already chosen.
        /// </summary>
        public static bool SetClass(TalesSave save, string cls)
        {
            string from = ClassOf(save);
            if (cls == from || !TalesData.Current.Classes.ContainsKey(cls) || !ClassOpen(save, cls)) return false;
            var me = save.Me;
            me.ClassRxp[from] = me.Rxp;
            me.Rxp = me.ClassRxp.TryGetValue(cls, out var r) ? r : 0;
            me.ClassRxp.Remove(cls);
            me.Cls = cls;
            save.Touch();
            return true;
        }
    }
}
