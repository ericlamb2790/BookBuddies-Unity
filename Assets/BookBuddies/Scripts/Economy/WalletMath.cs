using BookBuddies.Tales;

namespace BookBuddies.Economy
{
    /// <summary>
    /// Prices and the daily gift, the same rules the Worker uses (Server/src/wallet.js priceOf), so the game can show
    /// a price and check it can afford it before asking. Pure C#, tested in test/tales/tests/WalletTests.cs.
    /// </summary>
    public static class WalletMath
    {
        /// <summary>
        /// The coin price of a shop item, or 0 when there's no such item: decor:&lt;id&gt; · move:&lt;class&gt;:&lt;move&gt; ·
        /// unlock:&lt;class&gt; · meta:&lt;upgrade&gt;:&lt;new level&gt; · stock:&lt;day&gt;:&lt;town&gt;:&lt;i&gt; (today's satchel stock) ·
        /// satchel:&lt;town&gt; · tickets:&lt;n&gt; (a Book Fair ticket pack).
        /// </summary>
        public static int Price(EconomyData e, string item)
        {
            var p = item.Split(':');
            var shop = e.Shop;
            bool At(string kind, int parts) => p[0] == kind && p.Length == parts;
            if (At("decor", 2)) return shop.Obj("decor").Obj(p[1]).Int("p");
            if (At("unlock", 2)) return shop.Obj("unlock").Int(p[1]);
            if (At("meta", 3)) return MetaPrice(e, p[1], p[2]);
            if (At("stock", 4)) return int.TryParse(p[3], out int i) ? StockPrice(e, p[1], p[2], i) : 0;
            if (At("move", 3)) return shop.Obj("move").Int(p[1] + ":" + p[2]);
            if (At("satchel", 2)) return shop.Int("satchel");
            if (At("tickets", 2)) foreach (var pack in e.TicketPacks) if (pack[0].ToString() == p[1]) return pack[1];
            return 0;
        }

        /// <summary>Items that sell once per account (the server uses the item itself as the receipt). The rest need "item:&lt;nonce or pet id&gt;".</summary>
        public static bool Once(string item) =>
            item.StartsWith("decor:") || item.StartsWith("unlock:") || item.StartsWith("meta:") || item.StartsWith("stock:");

        /// <summary>A library upgrade reaching "level" costs 80·level + 20·(level−1)² (the site's tqMetaCost: 80, 180, 320, 500…).</summary>
        public static int MetaCost(int level) => 80 * level + 20 * (level - 1) * (level - 1);

        static int MetaPrice(EconomyData e, string key, string level)
        {
            if (!int.TryParse(level, out int lvl) || lvl < 1) return 0;
            foreach (System.Collections.Generic.Dictionary<string, object> m in e.Shop.Arr("meta"))
                if (m.Str("k") == key) return lvl <= m.Int("max") ? MetaCost(lvl) : 0;
            return 0;
        }

        /// <summary>A piece of gear at a rarity and item level in a town's daily stock: round(base·(1 + level/40)/5)·5 (11-plaza.js:1378).</summary>
        public static int StockPrice(EconomyData e, int tier, int level) => JsMath.RoundI(e.Shop.Obj("stock").Ints("base")[tier] * (1 + level / 40.0) / 5) * 5;

        /// <summary>
        /// The price of piece i (0-3) of a town's stock on a day, the way the Worker works it out: the stock's roll stream
        /// gives each piece's rarity, and the draws for the rest of the piece are skipped (see ShopStock for the items).
        /// 0 for a town without a shop or a piece that isn't there.
        /// </summary>
        public static int StockPrice(EconomyData e, string day, string town, int i)
        {
            var stock = e.Shop.Obj("stock");
            int lv = stock.Obj("lv").Int(town);
            if (lv <= 0 || i < 0 || i > 3) return 0;
            var rng = new Mulberry32(JsMath.Hash($"stock:{day}:{town}"));
            for (int j = 0; ; j++)
            {
                int t = StockTier(stock, rng.Next());
                if (j == i) return StockPrice(e, t, StockLevel(lv, j));
                for (int k = t >= 2 ? 4 : 3; k > 0; k--) rng.Next();
            }
        }

        /// <summary>The item level of piece i in the stock of a town at reader level lv: 4 + 2·lv + i (1 to 60).</summary>
        public static int StockLevel(int lv, int i) => JsMath.Clamp(4 + lv * 2 + i, 1, 60);

        // lootRoll's rarity pick with the satchel's weights and no luck
        static int StockTier(System.Collections.Generic.Dictionary<string, object> stock, double roll)
        {
            double[] w = stock.Doubles("w"), m = stock.Doubles("m"), weights = new double[w.Length];
            double total = 0;
            for (int k = 0; k < weights.Length; k++) total += weights[k] = w[k] * m[k];
            double r = roll * total;
            int t = 0;
            while (t < 4 && r >= weights[t]) { r -= weights[t]; t++; }
            return t;
        }

        /// <summary>The daily gift on day "run" in a row (1-based): the 7-day track, then it starts again.</summary>
        public static int GiftAmount(EconomyData e, int run) => e.LoginTrack[(System.Math.Max(run, 1) - 1) % e.LoginTrack.Length];
    }
}
