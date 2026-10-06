using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Moves: what a buddy owns and equips (tqBaseOwn, tqOwned, tqKitOf) and what each move is called (tqAbName, tqAbIcon, tqAbDesc)
    public static partial class HeroFactory
    {
        /// <summary>The seed used for personal move names. The site's Plaza uses 'me', so Unity does too.</summary>
        public const string NameSeed = "me";

        /// <summary>tqSlots: regular move slots, 3 at first, 4 from Pet Lv 6 and 5 from Pet Lv 13.</summary>
        public static int Slots(int rn) => 3 + (rn >= 5 ? 1 : 0) + (rn >= 12 ? 1 : 0);

        /// <summary>tqAb: a class move or a genre tome by key, or null.</summary>
        public static MoveDef Move(string cls, string key)
        {
            var d = TalesData.Current;
            return d.Class(cls).Move(key) ?? d.Tomes.Find(t => t.Key == key);
        }

        /// <summary>tqBaseOwn: strike, the first three regular moves and the ult.</summary>
        public static List<string> BaseOwn(string cls)
        {
            var own = new List<string> { "strike" };
            foreach (var m in TalesData.Current.Class(cls).Moves)
                if (!m.Ult && m.Key != "strike" && own.Count < 4) own.Add(m.Key);
            own.Add("ult");
            return own;
        }

        /// <summary>tqOwned: the starting moves plus the ones bought for this class (Unity has no read books, so no tomes).</summary>
        public static List<string> Owned(TalesSave save, string cls)
        {
            var own = BaseOwn(cls);
            if (save.Me.Own.TryGetValue(cls, out var bought))
                foreach (var k in bought) if (!own.Contains(k)) own.Add(k);
            return own;
        }

        /// <summary>tqKitOf: strike, the equipped regular moves (as many as the slots allow) and the equipped ult.</summary>
        public static List<string> KitOf(TalesSave save, string cls)
        {
            save.Me.Kit.TryGetValue(cls, out var saved);
            return KitOf(cls, Renown(save.Me.Rxp).lvl, saved, Owned(save, cls));
        }

        internal static List<string> KitOf(string cls, int rn, List<string> saved, List<string> own)
        {
            own = own ?? BaseOwn(cls);
            var regs = saved != null
                ? saved.FindAll(k => own.Contains(k) && k != "strike" && !k.StartsWith("ult"))
                : BaseOwn(cls).FindAll(k => k != "strike" && !k.StartsWith("ult"));
            string ult = saved?.Find(k => k.StartsWith("ult") && own.Contains(k)) ?? "ult";
            var kit = new List<string> { "strike" };
            kit.AddRange(regs.GetRange(0, System.Math.Min(regs.Count, Slots(rn))));
            kit.Add(ult);
            return kit;
        }

        /// <summary>tqAbName: the move's display name for this hero.</summary>
        public static string MoveName(BattleUnit hero, string moveKey) =>
            MoveName(hero.Hero?.Cls, hero.Gen, hero.Hero?.Lvl ?? 1, NameSeed, moveKey);

        /// <summary>tqAbName for any class, genre, tale level and name seed (the site's uid).</summary>
        public static string MoveName(string cls, int gen, int lvl, string uid, string k)
        {
            var d = TalesData.Current;
            string prefix = lvl >= 12 ? "Legendary " : lvl >= 6 ? "Greater " : "";
            var tome = d.Tomes.Find(t => t.Key == k);
            if (tome != null) return prefix + tome.Name;

            var C = d.Class(cls);
            string name = C.Names.TryGetValue(k, out var byGenre) && gen >= 0 && gen < byGenre.Length && byGenre[gen] != "" ? byGenre[gen] : k;
            if (k.StartsWith("ult")) return name;

            uint s = JsMath.Hash(uid + ":" + k);
            if (s % 5 < 2 && d.Nouns.TryGetValue(k, out var nouns))
            {
                var adj = d.Adjectives[((gen % 6) + 6) % 6];
                name = adj[(s >> 3) % adj.Length] + " " + nouns[(s >> 7) % nouns.Length];
            }
            else if (s % 5 == 2 && d.BookNames.TryGetValue(k, out var books)) name = books[(s >> 5) % books.Length];
            return prefix + name;
        }

        /// <summary>tqAbIcon: the tome's icon, or the class's icon for that move, or ✨.</summary>
        public static string MoveIcon(BattleUnit hero, string moveKey)
        {
            var d = TalesData.Current;
            var tome = d.Tomes.Find(t => t.Key == moveKey);
            if (tome != null) return tome.Icon;
            var C = d.Class(hero.Hero?.Cls);
            int i = C.Moves.FindIndex(m => m.Key == moveKey);
            return i >= 0 && i < C.Icons.Length && C.Icons[i] != "" ? C.Icons[i] : "✨";
        }

        /// <summary>tqAbDesc: what the move does, in words.</summary>
        public static string MoveDesc(BattleUnit hero, string moveKey)
        {
            var d = TalesData.Current;
            var tome = d.Tomes.Find(t => t.Key == moveKey);
            if (tome != null) return tome.Desc ?? "";
            string key = moveKey.StartsWith("ult") ? moveKey + "_" + d.Class(hero.Hero?.Cls).Key : moveKey;
            return d.MoveDesc.TryGetValue(key, out var text) ? text : "";
        }
    }
}
