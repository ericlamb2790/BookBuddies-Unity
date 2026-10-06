using System;
using BookBuddies.Tales;

namespace BookBuddies.World
{
    /// <summary>
    /// Where you've been on Bramble Road and your lore stones (the site's pw_seen and pg().stone): towns you've walked
    /// into, the stone you bound as home, the last stone you touched, and the once-an-hour Recall. Kept in TalesSave.
    /// Pure C#.
    /// </summary>
    public static class TownProgress
    {
        /// <summary>Recall works once an hour (STONE_CD).</summary>
        public const double RecallCooldownMs = 3600e3;

        static TalesSave Save => TalesSave.Current;
        static double NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>True for Pawtopia and every town you've walked into.</summary>
        public static bool Seen(string key) => key == "pawtopia" || Save.TownsSeen.Contains(key);

        /// <summary>Notes a town on the route as visited. True the first time (it's now a stop on the Paw Express).</summary>
        public static bool MarkSeen(string key)
        {
            if (Seen(key) || TownBook.Index(key) < 0) return false;
            Save.TownsSeen.Add(key);
            Save.Touch();
            return true;
        }

        /// <summary>Your home stone's town, or null.</summary>
        public static string Home => string.IsNullOrEmpty(Save.HomeStone) ? null : Save.HomeStone;
        /// <summary>The last stone you touched or rode to, or null.</summary>
        public static string Last => string.IsNullOrEmpty(Save.LastStone) ? null : Save.LastStone;

        /// <summary>Makes a town's stone your home stone.</summary>
        public static void Bind(string key) { Save.HomeStone = key; Save.Touch(); }

        /// <summary>Notes the stone you just touched or rode to (Recall's "Last stone").</summary>
        public static void Touched(string key) { Save.LastStone = key; Save.Touch(); }

        /// <summary>Milliseconds until Recall is ready (0 when it is).</summary>
        public static double RecallLeft => Math.Max(0, Save.RecallAt + RecallCooldownMs - NowMs);

        /// <summary>Starts Recall's hour-long cool-down.</summary>
        public static void Recalled() { Save.RecallAt = NowMs; Save.Touch(); }

        /// <summary>"12 min", or "1 hour" from 60 minutes up (stoneMins).</summary>
        public static string Minutes(double ms)
        {
            int m = (int)Math.Ceiling(ms / 60000);
            return m >= 60 ? "1 hour" : m + " min";
        }

        /// <summary>
        /// Where a fainted pet wakes up (stoneWake): at your home stone unless you're in that town, else at the stone of
        /// the town behind you on the road (Pawtopia when you're not on the road).
        /// </summary>
        public static string WakePlace(string here, bool wild, int link)
        {
            var home = Home;
            if (home != null && TownBook.Index(home) >= 0 && home != here) return home;
            var route = TalesData.Current.Route;
            return wild ? route[Math.Max(0, Math.Min(route.Length - 1, link - 1))] : "pawtopia";
        }
    }
}
