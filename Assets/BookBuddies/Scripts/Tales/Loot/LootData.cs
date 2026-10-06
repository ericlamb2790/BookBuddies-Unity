using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>A rarity tier (LT_RAR): name, colour, stat multiplier and the dust it salvages for.</summary>
    public sealed class Rarity { public string Name, Color; public double Mul; public int Dust; }

    /// <summary>A stat gear can roll (LT_ST). Max is the best roll on a level-60 common; Int stats roll whole numbers.</summary>
    public sealed class GearStat { public string Key, Name, Icon, Prefix, Suffix; public double Max; public bool Int; }

    /// <summary>A legendary perk (LT_PERK) and its strength range.</summary>
    public sealed class GearPerk { public string Key, Name, Icon; public double Lo, Hi; }

    /// <summary>A legendary unique (LT_LEG).</summary>
    public sealed class Legend { public string Theme, Slot, Name, Icon, Perk, Flavor; }

    /// <summary>A base item (LT_BASE[theme][slot][i]).</summary>
    public sealed class BaseItem { public string Name, Icon, Flavor; }

    /// <summary>
    /// The gear tables from Resources/BookBuddies/Data/loot.json (exported from the site's final LT_* tables).
    /// List order is the save format: item strings index into it, so the file must only ever be appended to.
    /// </summary>
    public sealed class LootData
    {
        /// <summary>Legendaries from this index on are the build-364 foe signatures (LT_UNQ0).</summary>
        public const int BossLegendsFrom = 48;

        static LootData current;
        public static LootData Current => current ?? (current = Load(TalesData.TextLoader("Data/loot")));

        public static readonly string[] SlotKeys = { "w", "a", "h", "c" };

        public readonly List<Rarity> Rarities = new List<Rarity>();
        public readonly Dictionary<string, (string name, string plural, string icon)> Slots = new Dictionary<string, (string, string, string)>();
        public readonly Dictionary<string, (string key, string icon, string name)> Themes = new Dictionary<string, (string, string, string)>();
        public readonly List<string> ThemeKeys = new List<string>();
        public readonly Dictionary<string, Dictionary<string, List<BaseItem>>> Base = new Dictionary<string, Dictionary<string, List<BaseItem>>>();
        public readonly List<Legend> Legends = new List<Legend>();
        public readonly Dictionary<string, GearPerk> Perks = new Dictionary<string, GearPerk>();
        public readonly List<GearStat> Stats = new List<GearStat>();             // LT_ST declaration order (the affix pool order)
        public readonly Dictionary<string, GearStat> StatByKey = new Dictionary<string, GearStat>();
        public readonly Dictionary<string, List<(string key, double max)>> Primary = new Dictionary<string, List<(string, double)>>();
        public readonly Dictionary<string, string> Arts = new Dictionary<string, string>();  // theme -> weapon art perk
        public readonly Dictionary<string, string> Towns = new Dictionary<string, string>(); // town or genre key -> theme
        public string[] GenreThemes = new string[0];                                          // foe genre 0-5 -> theme (LT_GENTH)
        public int BagSize = 60;

        /// <summary>The base item for a {theme}{slot}{index} key part, or null.</summary>
        public BaseItem BaseAt(string theme, string slot, int index) =>
            Base.TryGetValue(theme, out var bySlot) && bySlot.TryGetValue(slot, out var list) && index >= 0 && index < list.Count ? list[index] : null;

        public static LootData Load(string json)
        {
            var j = Json.ParseObject(json);
            var d = new LootData();
            foreach (Dictionary<string, object> r in j.Arr("LT_RAR"))
                d.Rarities.Add(new Rarity { Name = r.Str("n"), Color = r.Str("c"), Mul = r.Num("m", 1), Dust = r.Int("dust") });
            foreach (var kv in j.Obj("LT_SL"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                d.Slots[kv.Key] = (o.Str("n"), o.Str("p"), o.Str("i"));
            }
            foreach (var kv in j.Obj("LT_TH"))
            {
                var a = (List<object>)kv.Value;
                d.Themes[kv.Key] = ((string)a[0], (string)a[1], (string)a[2]);
            }
            d.ThemeKeys.AddRange(TalesData.Strings(j.Arr("LT_THK")));
            foreach (var th in j.Obj("LT_BASE"))
            {
                var bySlot = new Dictionary<string, List<BaseItem>>();
                foreach (var sl in (Dictionary<string, object>)th.Value)
                {
                    var list = new List<BaseItem>();
                    foreach (List<object> b in (List<object>)sl.Value)
                        list.Add(new BaseItem { Name = (string)b[0], Icon = (string)b[1], Flavor = b.Count > 2 ? b[2] as string ?? "" : "" });
                    bySlot[sl.Key] = list;
                }
                d.Base[th.Key] = bySlot;
            }
            foreach (List<object> l in j.Arr("LT_LEG"))
                d.Legends.Add(new Legend { Theme = (string)l[0], Slot = (string)l[1], Name = (string)l[2], Icon = (string)l[3], Perk = (string)l[4], Flavor = l.Count > 5 ? l[5] as string ?? "" : "" });
            foreach (var kv in j.Obj("LT_PERK"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                d.Perks[kv.Key] = new GearPerk { Key = kv.Key, Name = o.Str("n"), Icon = o.Str("i"), Lo = o.Num("lo"), Hi = o.Num("hi") };
            }
            foreach (var kv in j.Obj("LT_ST"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                var s = new GearStat { Key = kv.Key, Name = o.Str("n"), Icon = o.Str("i"), Max = o.Num("mx"), Int = o.Truthy("int"), Prefix = o.Str("p"), Suffix = o.Str("s") };
                d.Stats.Add(s);
                d.StatByKey[kv.Key] = s;
            }
            foreach (var kv in j.Obj("LT_PRI"))
            {
                var list = new List<(string, double)>();
                foreach (List<object> p in (List<object>)kv.Value) list.Add(((string)p[0], (double)p[1]));
                d.Primary[kv.Key] = list;
            }
            foreach (var kv in j.Obj("LT_ART")) d.Arts[kv.Key] = (string)kv.Value;
            foreach (var kv in j.Obj("LT_TOWN")) d.Towns[kv.Key] = (string)kv.Value;
            d.GenreThemes = TalesData.Strings(j.Arr("LT_GENTH"));
            d.BagSize = j.Int("LT_BAG", 60);
            return d;
        }
    }
}
