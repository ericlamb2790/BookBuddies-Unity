using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>One move in the compendium: owned (revealed), or sealed with what reveals it.</summary>
    public sealed class MovePage
    {
        public string Key;
        public MoveDef Move;
        public bool Owned, Tome;
        /// <summary>The Pet Lv it can be learned at (class moves past the starting kit); 0 for the rest.</summary>
        public int PetLv;
    }

    // The move compendium the hero card shows (owned moves, the next few sealed, the rest hidden), learning and equipping
    // moves (the Tales shop's buy and equip), and the next renown milestone
    public static partial class HeroFactory
    {
        /// <summary>Sealed pages shown past the moves you own; the rest stay hidden.</summary>
        public const int SealedShown = 3;

        /// <summary>
        /// The Pet Lv each class move past the starting kit can be learned at, in the class's order. The site sells them all
        /// from the start; Unity paces them so the compendium opens as the buddy grows.
        /// </summary>
        static readonly int[] LearnAt = { 3, 6, 10 };

        /// <summary>
        /// The Moves tab for a class: every move owned, then the next SealedShown in the order they open (the class's own
        /// moves, then the genre tomes), and how many pages stay hidden after those.
        /// </summary>
        public static (List<MovePage> owned, List<MovePage> seals, int hidden) Compendium(TalesSave save, string cls)
        {
            var own = Owned(save, cls);
            var start = BaseOwn(cls);
            var owned = new List<MovePage>();
            var locked = new List<MovePage>();
            int extra = 0;
            foreach (var m in TalesData.Current.Class(cls).Moves)
            {
                var page = new MovePage { Key = m.Key, Move = m, Owned = own.Contains(m.Key) };
                if (!start.Contains(m.Key)) page.PetLv = LearnAt[Math.Min(extra++, LearnAt.Length - 1)];
                (page.Owned ? owned : locked).Add(page);
            }
            foreach (var t in TalesData.Current.Tomes) locked.Add(new MovePage { Key = t.Key, Move = t, Tome = true });
            int shown = Math.Min(SealedShown, locked.Count);
            return (owned, locked.GetRange(0, shown), locked.Count - shown);
        }

        /// <summary>True when a sealed class move can be bought now (the buddy has reached its Pet Lv). Tomes come from reading.</summary>
        public static bool CanLearn(TalesSave save, MovePage page) => !page.Owned && !page.Tome && Renown(save.Me.Rxp).lvl + 1 >= page.PetLv;

        /// <summary>
        /// A move was bought (tqShop buy): it joins the class's moves, a regular move goes straight into a free slot, and
        /// it's marked new. Saves.
        /// </summary>
        public static void Learn(TalesSave save, string cls, string key)
        {
            AddMove(save, cls, key);
            var kit = KitOf(save, cls);
            var regs = kit.GetRange(1, kit.Count - 2);
            var m = Move(cls, key);
            if (m != null && !m.Ult && !regs.Contains(key) && regs.Count < Slots(Renown(save.Me.Rxp).lvl)) regs.Add(key);
            SetKit(save, cls, regs, kit[kit.Count - 1]);
            save.Unseen.Add($"move:{cls}:{key}");
            save.Touch();
        }

        /// <summary>Adds a bought move to a class (no equipping, no save). True when it's new.</summary>
        public static bool AddMove(TalesSave save, string cls, string key)
        {
            if (Move(cls, key) == null) return false;
            if (!save.Me.Own.TryGetValue(cls, out var bought)) save.Me.Own[cls] = bought = new List<string>();
            if (bought.Contains(key)) return false;
            bought.Add(key);
            return true;
        }

        /// <summary>
        /// tqShop equip: an ultimate replaces the equipped one; an equipped move comes off; any other goes into a free slot.
        /// Saves. Returns null, or why it couldn't ("You can equip 3 moves. Unequip one first").
        /// </summary>
        public static string ToggleEquip(TalesSave save, string cls, string key)
        {
            var kit = KitOf(save, cls);
            var regs = kit.GetRange(1, kit.Count - 2);
            string ult = kit[kit.Count - 1];
            int slots = Slots(Renown(save.Me.Rxp).lvl);
            if (key.StartsWith("ult")) ult = key;
            else if (regs.Contains(key)) regs.Remove(key);
            else if (regs.Count >= slots) return $"You can equip {slots} moves. Unequip one first";
            else regs.Add(key);
            SetKit(save, cls, regs, ult);
            save.Touch();
            return null;
        }

        /// <summary>↺ Reset to starting moves: forgets the class's loadout (bought moves stay). Saves.</summary>
        public static void ResetKit(TalesSave save, string cls)
        {
            save.Me.Kit.Remove(cls);
            save.Touch();
        }

        static void SetKit(TalesSave save, string cls, List<string> regs, string ult)
        {
            var kit = new List<string> { "strike" };
            kit.AddRange(regs);
            kit.Add(ult);
            save.Me.Kit[cls] = kit;
        }

        /// <summary>
        /// The next Pet Lv that opens something for the buddy's class and what it opens: a move slot, an evolution, a move
        /// to learn, a class to discover. There's always one (every tenth Pet Lv evolves).
        /// </summary>
        public static (int petLv, List<string> opens) NextMilestone(TalesSave save)
        {
            string cls = ClassOf(save);
            int now = Renown(save.Me.Rxp).lvl + 1;
            var gates = new List<int>();
            foreach (var p in Compendium(save, cls).seals) if (!p.Tome) gates.Add(p.PetLv);
            var classes = new List<int>();
            double best = ClassMetrics(save)["lv"];
            foreach (var c in TalesData.Current.Classes.Values)
                if (c.UnlockMetric == "lv" && c.UnlockAt > best && !ClassOpen(save, c.Key)) classes.Add((int)c.UnlockAt);
            for (int lv = now + 1; ; lv++)
            {
                var opens = new List<string>();
                if (Slots(lv - 1) > Slots(lv - 2)) opens.Add($"a {Slots(lv - 1)}th move slot");
                if (Evo(lv - 1) > Evo(lv - 2)) opens.Add($"evolves into a {EvoName(cls, lv)}");
                if (gates.Contains(lv)) opens.Add("a new move to learn");
                if (classes.Contains(lv)) opens.Add("a new class to discover");
                if (opens.Count > 0) return (lv, opens);
            }
        }
    }
}
