using System.Collections.Generic;
using BookBuddies.Economy;
using BookBuddies.Tales;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.UI
{
    /// <summary>
    /// Every town's bookshop (the site's shopSheet): the keeper's welcome, the town's six keepsakes, today's satchel stock
    /// and a satchel, the keeper's book picks, and your keepsakes from every town. Moves, classes and library upgrades
    /// are sold on the hero card's tabs; the Gear tab links there. Every purchase goes through the Wallet.
    /// </summary>
    public static class TownShop
    {
        static readonly string[] Greetings =
        {
            "Welcome in! These are my favorite {0} reads right now.", "Looking for your next {0} book? Start here.", "Mind the cat on the counter. Take your time!",
        };

        static EconomyData Econ => Wallet.Economy;
        static TalesSave Save => TalesSave.Current;

        /// <summary>Makes every "shop" spot open its town's bookshop, and has newly earned classes announced in town.</summary>
        public static void Register()
        {
            SpotActions.Register("shop", _ => Open(Boot.World?.Map.Key));
            HeroFactory.ClassesUnlocked += Announce;
        }

        /// <summary>Opens a town's bookshop.</summary>
        public static void Open(string town)
        {
            var t = TownBook.Current.Get(town);
            if (t == null) return;
            string keeper = string.Format(Greetings[Random.Range(0, Greetings.Length)], t.Genre.ToLowerInvariant());
            ShopScreen.Open(town == Boot.TownKey ? "📚" : t.Icon, t.Shop, $"<b>{t.Keeper}</b> says: “{keeper}”",
                new ShopScreen.Tab { Name = "Keepsakes", Fill = s => Keepsakes(s, t) },
                new ShopScreen.Tab { Name = "Gear", Fill = s => Gear(s, t) },
                new ShopScreen.Tab { Name = "Book picks", Fill = s => Picks(s, t) },
                new ShopScreen.Tab { Name = "My keepsakes", Fill = Collection });
        }

        /// <summary>Your keepsakes from every town (for a menu button).</summary>
        public static void OpenCollection() =>
            ShopScreen.Open("🎁", "My keepsakes", null, new ShopScreen.Tab { Name = "My keepsakes", Fill = Collection });

        // "Only in {town}": the six keepsakes, each sold once
        static void Keepsakes(ShopScreen s, TownBook.Town t)
        {
            s.Section($"Only in {t.Name}", "Keepsakes for your collection. Each one sells once.");
            var grid = s.Grid();
            foreach (var k in Shop.Keepsakes(Econ, t.Key))
            {
                bool owned = Save.Keepsakes.Contains(k.Id);
                var foot = s.Item(grid, k.Icon, k.Name, owned ? "In your collection" : null);
                if (owned) UiKit.Badge(foot, "Owned", UiKit.LeafInk);
                else ShopKit.PriceButton(foot, k.Price, () => ShopKit.Buy("decor:" + k.Id, null, k.Icon, k.Name, r => Bought(s, r, $"{k.Icon} Bought {k.Name}!"), s.Flash));
            }
        }

        static void Bought(ShopScreen s, WalletResult r, string line)
        {
            if (!s) return;
            s.Flash(r.Outcome == WalletOutcome.Already ? r.Message : line);
            Boot.World?.Me.ShowEmote("🛍️");
            s.Refresh();
        }

        // today's four pieces of satchel stock, a satchel, and the way to moves and classes
        static void Gear(ShopScreen s, TownBook.Town t)
        {
            string day = Shop.Today;
            var stock = Shop.Stock(Econ, day, t.Key);
            s.Section("Today’s satchel stock", "Fresh gear every morning. Each piece sells once.");
            var grid = s.Grid();
            for (int i = 0; i < stock.Length; i++)
            {
                var it = Loot.Get(stock[i]);
                string item = Shop.StockItem(day, t.Key, i);
                string about = $"{(it.Ancient ? "Ancient " : "")}{Loot.RarityName(it.Tier)} {Loot.SlotName(it.Slot).ToLowerInvariant()} · Level {it.ILvl} · {JsMath.Num(it.Power)} power";
                var foot = s.Item(grid, null, it.Name, about, TalesUi.RarityColor(it.Tier), holder => GearArt(holder, it));
                if (Wallet.State.Owned.Contains(item)) UiKit.Badge(foot, "Sold", Palette.InkSoft);
                else ShopKit.PriceButton(foot, Wallet.Price(item), () => BuyStock(s, item, it));
            }

            string theme = Loot.ThemeName(Loot.Data.Towns.TryGetValue(t.Key, out var th) ? th : "Z").ToLowerInvariant();
            s.Section("Tales of Pages satchel", $"One piece of {theme} gear for your pet’s tales. Uncommon or better, with a shot at legendary.");
            var satchel = s.Grid();
            ShopKit.PriceButton(s.Item(satchel, "🎒", $"{t.Name} satchel", "A surprise piece of gear"), Wallet.Price("satchel:" + t.Key), () => BuySatchel(s, t));

            s.Section("Moves, classes and the library", "Learn moves, change class and buy library upgrades on your hero card.");
            var row = UiKit.Node("hero", s.Content);
            UiKit.Row(row, 0).childForceExpandWidth = false;
            UiKit.Secondary(row, "Open the hero card", () => HeroCard.Open("moves"), "✨");
        }

        // the piece on a plate in its rarity's colour, glowing from epic up
        static Component GearArt(Transform holder, GearItem it)
        {
            var rc = TalesUi.RarityColor(it.Tier);
            var plate = UiKit.Panel(holder, "plate", rc.WithAlpha(.18f), 18);
            plate.raycastTarget = false;
            UiKit.Outline(plate, rc, 18, 3);
            if (it.Tier >= 3) GearUi.Glow(plate.transform, rc, ShopScreen.ArtSize * 1.3f, Vector2.zero);
            UiKit.Icon(plate.transform, it.Icon, 54).rectTransform.Pin(new Vector2(.5f, .5f), Vector2.zero, new Vector2(54, 54));
            return plate;
        }

        static void BuyStock(ShopScreen s, string item, GearItem it)
        {
            if (!Buddy.Hatched) { s.Flash("Hatch your pet first. Gear is for Tales of Pages."); return; }
            ShopKit.Buy(item, null, it.Icon, it.Name, r =>
            {
                if (!s) return;
                if (r.Outcome == WalletOutcome.Already) { s.Flash(r.Message); return; }
                Loot.Add(Save, it.Id);
                s.Refresh();
                TalesUi.Reveal(new List<string> { it.Id }, "Added to your bag!");
            }, s.Flash);
        }

        static void BuySatchel(ShopScreen s, TownBook.Town t)
        {
            if (!Buddy.Hatched) { s.Flash("Hatch your pet first. Satchels hold gear for Tales of Pages."); return; }
            ShopKit.Buy("satchel:" + t.Key, null, "🎒", $"a {t.Name} satchel", r =>
            {
                bool shiny = Json.ParseObject(Buddy.ShownLook)?.Truthy("sy") ?? false;
                string item = Shop.Satchel(t.Key, Save, shiny);
                if (s) TalesUi.Reveal(new List<string> { item }, "You open the satchel…");
            }, s.Flash);
        }

        // "Shopkeeper's picks": the town's six books
        static void Picks(ShopScreen s, TownBook.Town t)
        {
            s.Section("Shopkeeper’s picks", $"{t.Keeper}’s favorite {t.Genre.ToLowerInvariant()} reads.");
            var grid = s.Grid();
            foreach (List<object> b in t.Raw.Arr("books")) s.Item(grid, "📖", (string)b[0], (string)b[1]);
        }

        // every keepsake you own, from every town, in route order
        static void Collection(ShopScreen s)
        {
            var all = new List<Keepsake>();
            int total = 0;
            foreach (var town in TalesData.Current.Route)
                foreach (var k in Shop.Keepsakes(Econ, town)) { total++; if (Save.Keepsakes.Contains(k.Id)) all.Add(k); }
            if (all.Count == 0) { s.Empty("🎁", "No keepsakes yet. Every town’s bookshop sells its own."); return; }
            s.Section("My keepsakes", $"{all.Count} of {total} keepsakes from the towns on Bramble Road.");
            var grid = s.Grid();
            foreach (var k in all) UiKit.Badge(s.Item(grid, k.Icon, k.Name, null), TownBook.Current.Get(k.Town)?.Name ?? "", UiKit.SkyInk);
        }

        // tqClsCheck's toast, in town
        static void Announce(List<ClassDef> opened)
        {
            var w = Boot.World;
            if (w == null) return;
            w.Notify(opened.Count == 1
                ? $"🎉 New class unlocked: {opened[0].Icon} {opened[0].Name}. Become one from your hero card."
                : $"🎉 {opened.Count} new classes unlocked! See them on your hero card.");
        }
    }
}
