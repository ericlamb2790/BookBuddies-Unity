// The offline rooms: one LocalTown per "<town>:<shard>", as the Worker keeps one Durable Object per room (db.js TOWNS).

using System.Collections.Generic;

namespace BookBuddies.Local
{
    /// <summary>
    /// The town rooms running inside the game, by name ("&lt;town&gt;:&lt;shard&gt;"). They have no timers of their own:
    /// every LocalLink.Poll calls Tick, so rooms only run while someone is in town. A room left empty for a minute is
    /// forgotten (finds and all), as the Worker's idle rooms are.
    /// </summary>
    public static class LocalTowns
    {
        const double ForgetAfterMs = 60e3;
        static readonly Dictionary<string, LocalTown> rooms = new Dictionary<string, LocalTown>();

        /// <summary>The room with this name, made on first use (an unknown town is Pawtopia, like town.js placeOf).</summary>
        public static LocalTown Room(string name)
        {
            if (!rooms.TryGetValue(name, out var room)) rooms[name] = room = new LocalTown(name, LocalServer.Now);
            return room;
        }

        /// <summary>Runs every room's turn that's due (each 3 s while someone is in it) and forgets rooms empty for a minute.</summary>
        public static void Tick(double nowMs)
        {
            List<string> idle = null;
            foreach (var kv in rooms)
            {
                kv.Value.Tick(nowMs);
                if (kv.Value.IdleFor(nowMs) > ForgetAfterMs) (idle ??= new List<string>()).Add(kv.Key);
            }
            if (idle != null) foreach (string name in idle) rooms.Remove(name);
        }

        /// <summary>Tests: forgets every room.</summary>
        public static void Reset() => rooms.Clear();
    }
}
