using System;
using System.Collections.Generic;
using System.Globalization;
using BookBuddies.Tales;

namespace BookBuddies.Economy
{
    /// <summary>A keepsake from a town's bookshop (the site's DECOR t_&lt;town&gt;_&lt;i&gt;).</summary>
    public sealed class Keepsake
    {
        public string Id, Icon, Name, Town;
        public int Price;
    }

    /// <summary>
    /// What the town bookshops sell, worked out the site's way: today's satchel stock (wwStock), a satchel's piece
    /// (lootSatchel) and the keepsakes, plus taking in what the server's ledger says you own. Prices come from
    /// WalletMath (the Worker's own rules). Pure C#, tested in test/tales/tests/ShopTests.cs.
    /// </summary>
    public static class Shop
    {
        /// <summary>The buddy's key in move receipts ("move:&lt;class&gt;:&lt;move&gt;:me"): one Tales profile per device, like the Plaza's.</summary>
        public const string PetKey = "me";

        /// <summary>The stock's day: the device's own date, like the site's WW_DAY (the server takes a day either side of its own).</summary>
        public static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>The shop item for piece i of a town's stock on a day.</summary>
        public static string StockItem(string day, string town, int i) => $"stock:{day}:{town}:{i}";

        /// <summary>
        /// wwStock: a town's four pieces on a day, rolled from "stock:{day}:{town}" like the site, so every device and the
        /// server agree on them and their prices. The seed's clock part is 0, so the pieces stay the same all day. Empty
        /// for a place without a shop.
        /// </summary>
        public static string[] Stock(EconomyData e, string day, string town)
        {
            int lv = e.Shop.Obj("stock").Obj("lv").Int(town);
            if (lv <= 0) return new string[0];
            var rng = new Mulberry32(JsMath.Hash($"stock:{day}:{town}"));
            var items = new string[4];
            for (int i = 0; i < items.Length; i++) items[i] = Loot.RollItem("satchel", WalletMath.StockLevel(lv, i), Theme(town), null, 0, rng, null, 0);
            return items;
        }

        /// <summary>lootSatchel: the satchel's piece of the town's gear (uncommon or better) at item level 8 + renown, with your loot luck, added to the bag.</summary>
        public static string Satchel(string town, TalesSave save, bool shiny, IRng rng = null)
        {
            string item = Loot.RollItem("satchel", 8 + HeroFactory.Renown(save.Me.Rxp).lvl, Theme(town), null, Loot.Luck(save, shiny), rng ?? SystemRng.Shared);
            Loot.Add(save, item);
            return item;
        }

        /// <summary>A town's six keepsakes, in the shop's order.</summary>
        public static List<Keepsake> Keepsakes(EconomyData e, string town)
        {
            var list = new List<Keepsake>();
            for (int i = 0; Find(e, $"t_{town}_{i}") is Keepsake k; i++) list.Add(k);
            return list;
        }

        /// <summary>A keepsake by id, or null.</summary>
        public static Keepsake Find(EconomyData e, string id)
        {
            var o = e.Shop.Obj("decor").Obj(id);
            return o == null ? null : new Keepsake { Id = id, Icon = o.Str("i"), Name = o.Str("n"), Town = o.Str("town"), Price = o.Int("p") };
        }

        /// <summary>
        /// Takes in what the server's ledger says you own (the wallet reply's "owned"): keepsakes, unlocked classes,
        /// library levels and the buddy's bought moves, so they follow the account to any device. Saves when anything was new.
        /// </summary>
        public static bool TakeOwned(TalesSave save, IEnumerable<string> refs)
        {
            bool changed = false;
            foreach (var r in refs)
            {
                var p = r.Split(':');
                switch (p[0])
                {
                    case "decor" when p.Length == 2 && !save.Keepsakes.Contains(p[1]):
                        save.Keepsakes.Add(p[1]);
                        changed = true;
                        break;
                    case "unlock" when p.Length == 2 && !save.ClassesUnlocked.Contains(p[1]):
                        HeroFactory.UnlockClass(save, p[1]);
                        changed = true;
                        break;
                    case "meta" when p.Length == 3 && int.TryParse(p[2], out int lv) && lv > HeroFactory.Library(save, p[1]):
                        save.Meta[p[1]] = lv;
                        changed = true;
                        break;
                    case "move" when p.Length == 4 && p[3] == PetKey:
                        changed |= HeroFactory.AddMove(save, p[1], p[2]);
                        break;
                }
            }
            if (changed) save.Touch();
            return changed;
        }

        static string Theme(string town) => Loot.Data.Towns.TryGetValue(town ?? "", out var th) ? th : "Z";
    }
}
