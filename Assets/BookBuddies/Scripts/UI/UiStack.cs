using System.Collections.Generic;

namespace BookBuddies.UI
{
    /// <summary>
    /// The open menus and screens, newest on top. B, Esc or the controller's back button closes the top one,
    /// and while anything is open the controller drives the on-screen cursor instead of walking.
    /// Screens Push themselves when they open and Remove themselves when they close.
    /// </summary>
    public static class UiStack
    {
        sealed class Entry { public object Owner; public System.Action Back; }
        static readonly List<Entry> open = new List<Entry>();

        public static bool Any => open.Count > 0;
        public static int Count => open.Count;
        public static object Top => open.Count > 0 ? open[open.Count - 1].Owner : null;

        /// <summary>Adds (or moves to the top) a screen; back runs when the player backs out of it.</summary>
        public static void Push(object owner, System.Action back)
        {
            Remove(owner);
            open.Add(new Entry { Owner = owner, Back = back });
        }

        public static bool Contains(object owner) => open.Exists(e => e.Owner == owner);

        public static void Remove(object owner) => open.RemoveAll(e => e.Owner == owner || e.Owner == null || e.Owner.Equals(null));

        /// <summary>Backs out of the top screen. False when nothing is open.</summary>
        public static bool Back()
        {
            open.RemoveAll(e => e.Owner == null || e.Owner.Equals(null));
            if (open.Count == 0) return false;
            var top = open[open.Count - 1];
            open.RemoveAt(open.Count - 1);
            top.Back?.Invoke();
            return true;
        }

        public static void Clear() => open.Clear();
    }
}
