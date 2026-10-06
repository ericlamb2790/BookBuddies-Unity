using System;
using System.Collections.Generic;
using System.Globalization;

namespace BookBuddies.Tales
{
    /// <summary>One rolled stat on an item: its value, how good the roll was (Quality .55 to 1) and whether it's a primary.</summary>
    public sealed class GearRoll { public string Key; public double Value, Quality; public bool Primary; }

    /// <summary>One piece of gear, built from its item string (base~tier~ilvl~seed~ancient) exactly like ltBuild.</summary>
    public sealed class GearItem
    {
        public string Id, BaseKey, Name, Icon, Slot, Theme, Flavor, Perk; // Id is the item string itself; Theme the letter ("F")
        public int Tier, ILvl;               // Tier: 0 Common … 4 Legendary
        public bool Ancient, Legendary;
        public double PerkValue, Power;
        public readonly Dictionary<string, double> Stats = new Dictionary<string, double>();
        public string Seed, ThemeKey;        // ThemeKey: the genre word ("fantasy"), matched against the pet's home genre
        public int Quality;                  // roll quality, 0-100
        public readonly List<GearRoll> Rolls = new List<GearRoll>(); // primaries first, then affixes

        /// <summary>Page dust this item salvages for (double for ancient).</summary>
        public int Dust => Loot.Data.Rarities[Tier].Dust * (Ancient ? 2 : 1);
        public double Stat(string k) => Stats.TryGetValue(k, out var v) ? v : 0;
    }

    /// <summary>
    /// Gear: building items from their strings, the stat totals a hero wears (lootSum), the bag (60), equipping,
    /// salvaging for dust, and the drops (LootDrops.cs). Ported from the site's js/14-extras.js; pure C#.
    /// Every change is saved through TalesSave.Touch().
    /// </summary>
    public static partial class Loot
    {
        static readonly int[] AffixCount = { 0, 1, 2, 3, 3 };
        static readonly Dictionary<string, GearItem> cache = new Dictionary<string, GearItem>();

        /// <summary>The loot tables.</summary>
        public static LootData Data => LootData.Current;

        /// <summary>LT_BAG: how many items the bag holds.</summary>
        public static int BagSize => Data.BagSize;

        /// <summary>lootGet: the item for a string (cached), or null when the string names no known base.</summary>
        public static GearItem Get(string itemString)
        {
            if (string.IsNullOrEmpty(itemString)) return null;
            if (cache.TryGetValue(itemString, out var hit)) return hit;
            var it = Build(itemString);
            if (cache.Count > 800) cache.Clear();
            cache[itemString] = it;
            return it;
        }

        // ltBuild: the base, then a mulberry32 stream seeded by the whole string rolls every number
        static GearItem Build(string s)
        {
            var d = Data;
            var p = s.Split('~');
            string b = p[0];
            int t = JsMath.Clamp(JsInt(Part(p, 1)), 0, 4), il = JsMath.Clamp(JsInt(Part(p, 2)), 1, 60);
            bool A = Part(p, 4) == "1";
            var it = new GearItem { Id = s, BaseKey = b, Tier = t, ILvl = il, Ancient = A, Seed = Part(p, 3) };
            Legend leg = null;
            if (b.StartsWith("L"))
            {
                double i = JsNum(b.Substring(1));
                if (i < 0 || i >= d.Legends.Count || i != Math.Floor(i)) return null;
                leg = d.Legends[(int)i];
                (it.Theme, it.Slot, it.Name, it.Icon, it.Flavor) = (leg.Theme, leg.Slot, leg.Name, leg.Icon, leg.Flavor);
                it.Legendary = true;
            }
            else
            {
                double i = b.Length >= 2 ? JsNum(b.Substring(2)) : double.NaN;
                var B = b.Length >= 2 && i == Math.Floor(i) ? d.BaseAt(b.Substring(0, 1), b.Substring(1, 1), (int)i) : null;
                if (B == null) return null;
                (it.Theme, it.Slot, it.Name, it.Icon, it.Flavor) = (b.Substring(0, 1), b.Substring(1, 1), B.Name, B.Icon, B.Flavor);
            }
            if (!d.Themes.TryGetValue(it.Theme, out var th) || !d.Primary.ContainsKey(it.Slot)) return null;
            it.ThemeKey = th.key;

            var r = new Mulberry32(JsMath.Hash(s));
            var R = d.Rarities[t];
            double f = (.35 + .65 * il / 60) * R.Mul * (A ? 1.3 : 1);
            void Add(string k, double mx, bool pri)
            {
                double q = .55 + .45 * r.Next();
                double v = d.StatByKey[k].Int ? (t >= 3 && q > .75 ? 2 : 1) : JsMath.Round1(mx * f * q);
                it.Stats[k] = it.Stat(k) + v;
                it.Rolls.Add(new GearRoll { Key = k, Value = v, Quality = q, Primary = pri });
            }
            var primaries = d.Primary[it.Slot];
            foreach (var (k, mx) in primaries) Add(k, mx, true);
            var pool = new List<string>();
            foreach (var st in d.Stats) if (!primaries.Exists(x => x.key == st.Key)) pool.Add(st.Key);
            var picked = new List<string>();
            for (int i = 0; i < AffixCount[t] && pool.Count > 0; i++)
            {
                int at = (int)Math.Floor(r.Next() * pool.Count);
                picked.Add(pool[at]);
                pool.RemoveAt(at);
                Add(picked[i], d.StatByKey[picked[i]].Max, false);
            }

            double perkQ = 0;
            if (leg != null && d.Perks.TryGetValue(leg.Perk, out var P))
            {
                perkQ = .55 + .45 * r.Next();
                it.Perk = leg.Perk;
                it.PerkValue = JsMath.Round(P.Lo + (P.Hi - P.Lo) * perkQ * (A ? 1.15 : 1));
            }
            if (leg == null) it.Name = AffixName(it.Name, t, picked);

            double sum = 0;
            foreach (var x in it.Rolls) sum += x.Quality;
            double avg = sum / Math.Max(1, it.Rolls.Count) * .8 + (it.Perk != null ? perkQ * .2 : .2 * (it.Rolls.Count > 0 ? it.Rolls[0].Quality : 1));
            it.Power = JsMath.Round((40 + il * 6) * R.Mul * (A ? 1.3 : 1) * (.8 + .2 * avg) + (it.Perk != null ? 60 : 0));
            it.Quality = JsMath.RoundI(avg * 100);
            return it;
        }

        // "Sharp Longsword", "Longsword of Fury", "Keen Longsword of the Cat"; "… of …" and "The …" names only take a prefix
        static string AffixName(string name, int t, List<string> picked)
        {
            var st = Data.StatByKey;
            bool of = name.Contains(" of ") || name.StartsWith("The ");
            if (t == 1 && picked.Count > 0) return st[picked[0]].Prefix + " " + name;
            if (t == 2 && picked.Count > 0) return of ? st[picked[0]].Prefix + " " + name : name + " " + st[picked[0]].Suffix;
            if (t >= 3 && picked.Count > 1) return of ? st[picked[0]].Prefix + " " + name : st[picked[0]].Prefix + " " + name + " " + st[picked[1]].Suffix;
            return name;
        }

        static string Part(string[] p, int i) => i < p.Length ? p[i] : null;

        // JavaScript's +s: "" is 0, junk is NaN
        static double JsNum(string s)
        {
            if (s == null) return double.NaN;
            if (s.Trim().Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }

        // +s|0: NaN and infinities become 0, the rest truncate toward zero
        static int JsInt(string s)
        {
            double v = JsNum(s);
            return double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > int.MaxValue ? 0 : (int)Math.Truncate(v);
        }

        // ---- what a hero wears ----

        /// <summary>lootSum: totals for the equipped slots (w a h c), without the home-genre bonus.</summary>
        public static GearSum Sum(IDictionary<string, string> gear) => Sum(gear, -1);

        /// <summary>
        /// lootSum: totals for the equipped slots (w a h c). Items of the pet's home genre (homeGenre 0-5, from the
        /// hue; -1 for none) give 10% more of every stat except ink. Perks keep the strongest of each kind, and a
        /// weapon of the first eight themes adds its weapon art when that beats a perk of the same kind.
        /// </summary>
        public static GearSum Sum(IDictionary<string, string> gear, int homeGenre)
        {
            var sum = new GearSum();
            if (gear == null) return sum;
            var genres = TalesData.Current.Genres;
            string home = homeGenre >= 0 && homeGenre < genres.Count ? genres[homeGenre].key : null;
            foreach (var sl in LootData.SlotKeys)
            {
                var it = gear.TryGetValue(sl, out var s) ? Get(s) : null;
                if (it == null) continue;
                double hb = it.ThemeKey == home ? 1.1 : 1;
                foreach (var kv in it.Stats)
                    sum.Stats[kv.Key] = sum[kv.Key] + kv.Value * (Data.StatByKey[kv.Key].Int ? 1 : hb);
                if (it.Perk != null) KeepPerk(sum, it.Perk, it.PerkValue, it.Name);
                sum.Power += it.Power;
            }
            var weapon = gear.TryGetValue("w", out var w) ? Get(w) : null;
            var art = WeaponArt(weapon);
            if (art.perk != null) KeepPerk(sum, art.perk, art.value, weapon.Name);
            return sum;
        }

        static void KeepPerk(GearSum sum, string perk, double v, string itemName)
        {
            if (sum.Perk(perk) >= v) return;
            sum.Perks[perk] = v;
            sum.PerkFrom[perk] = itemName.Split(',')[0];
        }

        /// <summary>LT_ART: the weak perk every weapon of the first eight themes carries, stronger with rarity. Null perk when none.</summary>
        public static (string perk, double value) WeaponArt(GearItem weapon)
        {
            if (weapon == null || weapon.Slot != "w" || !Data.Arts.TryGetValue(weapon.Theme, out var k) || !Data.Perks.TryGetValue(k, out var P)) return (null, 0);
            return (k, JsMath.Round(P.Lo * (.35 + .15 * weapon.Tier)));
        }

        /// <summary>The item worn in a slot, or null.</summary>
        public static GearItem Equipped(TalesSave save, string slot) => save.Me.Gear.TryGetValue(slot, out var s) ? Get(s) : null;

        /// <summary>The slot an item string is worn in, or null when it isn't worn.</summary>
        public static string WornIn(TalesSave save, string itemString)
        {
            foreach (var kv in save.Me.Gear) if (kv.Value == itemString) return kv.Key;
            return null;
        }

        // ---- the bag ----

        /// <summary>Puts on an item from the bag; whatever was in its slot goes to the end of the bag.</summary>
        public static bool Equip(TalesSave save, string itemString)
        {
            int i = save.Bag.IndexOf(itemString);
            var it = Get(itemString);
            if (i < 0 || it == null) return false;
            save.Bag.RemoveAt(i);
            if (save.Me.Gear.TryGetValue(it.Slot, out var old) && !string.IsNullOrEmpty(old)) save.Bag.Add(old);
            save.Me.Gear[it.Slot] = itemString;
            save.Fresh.Remove(itemString);
            save.Touch();
            return true;
        }

        /// <summary>Takes off the item in a slot and puts it back in the bag. False when the slot is empty or the bag is full.</summary>
        public static bool Unequip(TalesSave save, string slot)
        {
            if (!save.Me.Gear.TryGetValue(slot, out var s) || save.Bag.Count >= BagSize) return false;
            save.Me.Gear.Remove(slot);
            if (!string.IsNullOrEmpty(s)) save.Bag.Add(s);
            save.Touch();
            return true;
        }

        /// <summary>Breaks an item into dust. Returns the dust gained.</summary>
        public static int Salvage(TalesSave save, string itemString)
        {
            int i = save.Bag.IndexOf(itemString);
            var it = Get(itemString);
            if (i < 0 || it == null) return 0;
            save.Bag.RemoveAt(i);
            save.Fresh.Remove(itemString);
            save.Dust += it.Dust;
            save.Touch();
            return it.Dust;
        }

        /// <summary>
        /// Power gained by wearing this item instead of what's in its slot (negative when worse), and whether
        /// that slot is empty. Zero for an item that's already worn.
        /// </summary>
        public static (int delta, bool emptySlot) Compare(TalesSave save, GearItem it)
        {
            if (it == null || WornIn(save, it.Id) != null) return (0, false);
            var cur = Equipped(save, it.Slot);
            return cur == null ? ((int)it.Power, true) : ((int)(it.Power - cur.Power), false);
        }

        // ---- names, icons and colours ----

        public static string RarityName(int tier) => Data.Rarities[JsMath.Clamp(tier, 0, 4)].Name;
        public static string RarityColor(int tier) => Data.Rarities[JsMath.Clamp(tier, 0, 4)].Color;
        public static string StatName(string key) => Data.StatByKey.TryGetValue(key, out var s) ? s.Name : key;
        public static string StatIcon(string key) => Data.StatByKey.TryGetValue(key, out var s) ? s.Icon : "";
        public static string SlotName(string slot) => Data.Slots.TryGetValue(slot, out var s) ? s.name : slot;
        public static string SlotIcon(string slot) => Data.Slots.TryGetValue(slot, out var s) ? s.icon : "";
        /// <summary>"Weapons", "Armor", "Hats", "Charms".</summary>
        public static string SlotPlural(string slot) => Data.Slots.TryGetValue(slot, out var s) ? s.plural : slot;
        public static string ThemeName(string theme) => Data.Themes.TryGetValue(theme ?? "", out var t) ? t.name : "";
        public static string ThemeIcon(string theme) => Data.Themes.TryGetValue(theme ?? "", out var t) ? t.icon : "";
        public static string PerkName(string perk) => Data.Perks.TryGetValue(perk ?? "", out var p) ? p.Name : perk;
        public static string PerkIcon(string perk) => Data.Perks.TryGetValue(perk ?? "", out var p) ? p.Icon : "";

        /// <summary>A stat as the site shows it: "+6.9%", or "+1" for ink.</summary>
        public static string StatText(string key, double v) =>
            "+" + JsMath.Num(v) + (Data.StatByKey.TryGetValue(key, out var s) && s.Int ? "" : "%");

        /// <summary>The perk's description at a strength (the site's LT_PERK[k].d).</summary>
        public static string PerkText(string perk, double v)
        {
            string n = JsMath.Num(v);
            switch (perk)
            {
                case "ember": return $"Hits have a {n}% chance to set foes burning for 3 turns.";
                case "venom": return $"Hits have a {n}% chance to poison foes for 3 turns.";
                case "frost": return $"Hits have a {n}% chance to leave a foe too dizzy to act.";
                case "vamp": return $"Heal {n}% of the damage you deal.";
                case "chain": return $"Critical hits leap to another foe for {n}% damage.";
                case "echo": return $"{n}% chance to strike the same foe again at half power.";
                case "execute": return $"Deal {n}% more damage to foes under 35% health.";
                case "aegis": return $"Start every battle with a shield worth {n}% of your health.";
                case "phoenix": return $"Once per battle, get back up with {n}% health.";
                case "bloom": return $"Heal the whole party for {n}% each of your turns.";
                case "scribe": return $"Critical hits have a {n}% chance to give you 1 ink.";
                case "haste": return $"{n}% more speed and 2 extra ink when a battle starts.";
                case "lucky": return $"{n}% better odds of rare loot.";
                case "thornmail": return $"Foes that hit you take {n}% of the damage back.";
                case "giant": return $"{n}% more health and defense.";
                case "mirror": return $"{n}% chance to dodge a hit and strike back.";
                default: return "";
            }
        }
    }
}
