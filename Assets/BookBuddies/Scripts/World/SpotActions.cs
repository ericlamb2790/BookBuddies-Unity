using System.Collections.Generic;

namespace BookBuddies.World
{
    /// <summary>
    /// What a place on the map does when you use it (the button on its card, or E / A next to it).
    /// Features register a handler per spot kind; places with no handler say they open in a later version.
    /// </summary>
    public static class SpotActions
    {
        static readonly Dictionary<string, System.Action<TownMap.Spot>> handlers = new Dictionary<string, System.Action<TownMap.Spot>>();

        public static void Register(string kind, System.Action<TownMap.Spot> use) => handlers[kind] = use;
        public static bool CanUse(TownMap.Spot spot) => spot != null && handlers.ContainsKey(spot.Kind);

        public static bool Use(TownMap.Spot spot)
        {
            if (spot == null || !handlers.TryGetValue(spot.Kind, out var use)) return false;
            use(spot);
            return true;
        }
    }
}
