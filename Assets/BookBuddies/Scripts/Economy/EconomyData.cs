using System.Collections.Generic;

namespace BookBuddies.Economy
{
    /// <summary>
    /// Data/economy.json: the coin prices and rewards, exported from the website by tools/export_econ.js. The Worker
    /// prices every purchase from its own copy of the same file, so what the game shows is what the server charges.
    /// Pure C#: TextLoader is pointed at Resources by Wallet (and at the file by the tests).
    /// </summary>
    public sealed class EconomyData
    {
        public static System.Func<string, string> TextLoader;
        static EconomyData current;

        public static EconomyData Current => current ?? (current = new EconomyData(Json.ParseObject(TextLoader("Data/economy"))));

        /// <summary>Coins a new account starts with.</summary>
        public readonly int Starter;
        /// <summary>The daily gift for days 1 to 7 in a row.</summary>
        public readonly int[] LoginTrack;
        /// <summary>How many town finds pay coins each day.</summary>
        public readonly int FindsPerDay;
        /// <summary>What each kind of town find pays ("coin", "bag", "gift").</summary>
        public readonly Dictionary<string, int> FindCoins = new Dictionary<string, int>();
        /// <summary>Book Fair ticket packs: [tickets, coin price].</summary>
        public readonly List<int[]> TicketPacks = new List<int[]>();
        /// <summary>The shop prices (decor, move, unlock, meta, satchel, stock); read them with WalletMath.Price.</summary>
        public readonly Dictionary<string, object> Shop;

        public EconomyData(Dictionary<string, object> o)
        {
            Starter = o.Int("starter");
            LoginTrack = o.Ints("loginTrack");
            FindsPerDay = o.Obj("finds").Int("perDay");
            var finds = o.Obj("finds").Obj("coins");
            if (finds != null) foreach (var kv in finds) FindCoins[kv.Key] = finds.Int(kv.Key);
            foreach (List<object> pack in o.Obj("fair").Arr("packs")) TicketPacks.Add(new[] { (int)(double)pack[0], (int)(double)pack[1] });
            Shop = o.Obj("shop") ?? new Dictionary<string, object>();
        }
    }
}
