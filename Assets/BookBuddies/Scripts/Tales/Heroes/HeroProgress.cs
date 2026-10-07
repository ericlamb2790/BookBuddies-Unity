using System;

namespace BookBuddies.Tales
{
    // Renown ("Pet Lv" = renown level + 1), its stat bonus, evolutions, and what a win adds (tqGain)
    public static partial class HeroFactory
    {
        /// <summary>tqRenown: renown level, xp into it and xp needed for the next.</summary>
        public static (int lvl, double xp, double need) Renown(double rxp)
        {
            int l = 0;
            double x = Math.Max(0, rxp), need = 12;
            while (x >= need && l < 100000)
            {
                x -= need;
                l++;
                need = 12 + l * 6 + (l >= 40 ? 2.0 * (l - 39) * (l - 39) : 0);
            }
            return (l, x, need);
        }

        /// <summary>tqRnBonus: +2.5% stats per renown level up to 40, then +1.2% each.</summary>
        public static double RenownBonus(int rn) => rn <= 40 ? .025 * rn : 1 + .012 * (rn - 40);

        /// <summary>tqEvo: evolutions so far (one every 10 Pet Lv).</summary>
        public static int Evo(int rn) => (rn + 1) / 10;

        /// <summary>Evolution name, e.g. "Gilt-Edge Knight", for a class at a Pet Lv.</summary>
        public static string EvoName(string cls, int petLv)
        {
            var C = TalesData.Current.Class(cls);
            var names = C.Evolutions.Length > 0 ? C.Evolutions : new[] { C.Name };
            int ev = Math.Max(0, petLv) / 10;
            return ev < names.Length ? names[ev] : names[names.Length - 1] + " " + Roman(ev - names.Length + 2);
        }

        /// <summary>tqGain after a fight: adds renown, counts life stats, opens any class now earned, returns a level-up line or null.</summary>
        /// <remarks>
        /// A wild win passes rx 2 and stat "wins" (every wild fight is the site's 'fight' kind, even against a boss), which also
        /// counts the foes beaten and the best level. A loss passes 0 and "falls". Fate rolls (natural 20s, helpers) count either way.
        /// </remarks>
        public static string GainRenown(TalesSave save, int rx, string stat, BattleOutcome outcome)
        {
            var me = save.Me;
            if (stat != null) me.Count(stat);
            if (stat == "wins" && outcome != null)
            {
                me.Count("foes", Math.Max(1, outcome.Defeated.Count));
                me.Life["best"] = Math.Max(me.Stat("best"), outcome.Lvl);
            }
            if (outcome != null)
            {
                if (outcome.Nat20s > 0) me.Count("n20", outcome.Nat20s);
                if (outcome.Helpers > 0) me.Count("helpers", outcome.Helpers);
            }
            string line = rx > 0 ? AddRenown(save, rx, outcome) : null;
            save.Touch();
            CheckClassUnlocks(save);
            return line;
        }

        /// <summary>tqTrack's win inside a tale: renown 8 for a boss, 4 for an elite, 2 for a fight, and a boss counts in life.bosses.</summary>
        public static string GainTaleWin(TalesSave save, string kind, BattleOutcome outcome)
        {
            if (kind == TaleBattle.Boss) save.Me.Count("bosses");
            return GainRenown(save, kind == TaleBattle.Boss ? 8 : kind == TaleBattle.Elite ? 4 : 2, "wins", outcome);
        }

        static string AddRenown(TalesSave save, int rx, BattleOutcome outcome)
        {
            int before = Renown(save.Me.Rxp).lvl;
            save.Me.Rxp += rx;
            int after = Renown(save.Me.Rxp).lvl;
            if (outcome != null) outcome.RenownGained += rx;
            if (after <= before) return null;

            string pet = outcome?.Hero?.Name ?? "Your pet";
            bool evolved = Evo(after) > Evo(before);
            string line = evolved
                ? $"🦋 {pet} evolved into a {EvoName(ClassOf(save), after + 1)}! Pet Lv {after + 1}, and every stat grows"
                : $"🌟 {pet} reached Pet Lv {after + 1}! {PetLvFlavor(pet, after + 1)}{(Slots(after) > Slots(before) ? " It has a new move slot." : "")}";
            if (outcome != null) { outcome.LevelUp = true; outcome.Evolved |= evolved; outcome.LevelNote = line; }
            return line;
        }

        /// <summary>petLvFlavor: a fun line for a new Pet Lv.</summary>
        public static string PetLvFlavor(string name, int lv)
        {
            var d = TalesData.Current;
            if (d.PetLvFlavor.Length == 0) return "";
            string line = ReplaceFirst(d.PetLvFlavor[lv * 17 % d.PetLvFlavor.Length], "{n}", name);
            return d.PetLvX.Length == 0 ? line : ReplaceFirst(line, "{x}", d.PetLvX[lv * 7 % d.PetLvX.Length]);
        }

        // JavaScript's String.replace with a string pattern: only the first match
        static string ReplaceFirst(string s, string find, string with)
        {
            int i = s.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + find.Length);
        }

        static string Roman(int n)
        {
            var sb = new System.Text.StringBuilder();
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] s = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }
    }
}
