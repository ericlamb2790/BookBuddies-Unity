using System;
using BookBuddies.Tales;

namespace BookBuddies.Road
{
    /// <summary>
    /// Your pet's HP and ink between wild fights (the site's pvNow / pvSet / pvAfter). HP comes back over time,
    /// in 100 s in town or 300 s out on the road; ink drains by one pip every 45 s. Kept in TalesSave.
    /// </summary>
    public static class RoadVitals
    {
        public const int MaxInk = 6;

        static double NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>HP (share of max, 0 to 1) and ink right now. Reading never saves, like the site.</summary>
        public static (double hp, int ink) Now(bool inTown)
        {
            var s = TalesSave.Current;
            double hp = s.HpFrac, ink = s.InkSaved;
            double seconds = Math.Max(0, (NowMs - (s.VitalsAt > 0 ? s.VitalsAt : NowMs)) / 1000);
            if (seconds > 1)
            {
                hp = Math.Min(1, hp + seconds / (inTown ? 100 : 300));
                ink = Math.Max(0, ink - Math.Floor(seconds / 45));
            }
            return (hp, (int)ink);
        }

        /// <summary>Sets HP and ink from now on (clamped to 0-1 and 0-6).</summary>
        public static void Set(double hp, int ink)
        {
            var s = TalesSave.Current;
            s.HpFrac = Math.Max(0, Math.Min(1, hp));
            s.InkSaved = Math.Max(0, Math.Min(MaxInk, ink));
            s.VitalsAt = NowMs;
            s.Touch();
        }

        /// <summary>After a fight: fainting leaves half HP and no ink; anything else keeps what the fight left (at least 5%).</summary>
        public static void After(bool lost, BattleOutcome outcome)
        {
            if (lost) Set(.5, 0);
            else if (outcome != null) Set(Math.Max(.05, outcome.HpFrac), outcome.Ink);
        }
    }
}
