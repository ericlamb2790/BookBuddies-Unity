using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;
using BookBuddies.Tales;
using UnityEngine;

namespace BookBuddies.Economy
{
    /// <summary>How a purchase or a reward went.</summary>
    public enum WalletOutcome
    {
        /// <summary>Paid or granted just now.</summary>
        Done,
        /// <summary>Paid for or claimed before (maybe on another device): it's yours, nothing was charged.</summary>
        Already,
        /// <summary>Not enough coins (or Book Fair tickets).</summary>
        Short,
        /// <summary>The server couldn't be reached, or nobody is signed in: nothing changed.</summary>
        Offline,
        /// <summary>The server said no; the message says why.</summary>
        Refused,
    }

    /// <summary>What Spend and Earn hand back: the outcome, the coins paid or granted, and a line the UI can show.</summary>
    public sealed class WalletResult
    {
        public readonly WalletOutcome Outcome;
        public readonly int Amount;
        public readonly string Message;

        public WalletResult(WalletOutcome outcome, int amount, string message)
        {
            Outcome = outcome;
            Amount = amount;
            Message = message ?? "";
        }

        /// <summary>The item is yours or the reward arrived, now or before.</summary>
        public bool Ok => Outcome == WalletOutcome.Done || Outcome == WalletOutcome.Already;
    }

    /// <summary>One row of the coin ledger: what it was for, how many coins (spent rows are negative) and the day.</summary>
    public sealed class CoinEntry
    {
        public string Kind, Day;
        public int Amount;
    }

    /// <summary>Today's numbers and the Book Fair counters from the last wallet reply.</summary>
    public sealed class WalletState
    {
        /// <summary>The server's day (US Eastern, YYYY-MM-DD): gifts and daily limits turn over at its midnight.</summary>
        public string Day = "";
        public bool GiftClaimed;
        /// <summary>Days in a row the gift has been claimed (today included once it's claimed; 0 after a missed day).</summary>
        public int GiftRun;
        /// <summary>Book Fair counters: pinballs, wheel tickets, prism drops and capsule pity.</summary>
        public int Balls, WheelTickets, Drops, Pity;
        /// <summary>The day-7 gift's free Book Fair capsule is waiting today.</summary>
        public bool FreeCapsule;
        public readonly Dictionary<string, int> Upgrades = new Dictionary<string, int>();
        /// <summary>Coin rows per kind today ("find", "shop"…): daily limits count these.</summary>
        public readonly Dictionary<string, int> Used = new Dictionary<string, int>();
        /// <summary>The latest coin rows, newest first.</summary>
        public readonly List<CoinEntry> Recent = new List<CoinEntry>();
        /// <summary>What the ledger says you bought for keeps: decor, unlock, meta and move receipts, and the last two days' stock.</summary>
        public readonly HashSet<string> Owned = new HashSet<string>();

        public int UsedToday(string kind) => Used.TryGetValue(kind, out int n) ? n : 0;

        /// <summary>Which day in a row the next gift is (today's, while it's still to claim).</summary>
        public int GiftDay => GiftClaimed ? GiftRun : GiftRun + 1;
    }

    /// <summary>
    /// The one wallet: coins, Book Fair tickets and the fair's counters, always the server's numbers (the Worker keeps
    /// them as a ledger and prices every purchase itself, see Server/src/wallet.js). Towns, shops, bosses and the
    /// Book Fair earn and spend through here. When the server can't be reached it shows the last balance the server sent
    /// and refuses to spend, so nothing is ever made up on the device. Playing offline the server is the backend on this
    /// PC with the same rules: those coins are the offline purse, banked into the online wallet when offline play ends
    /// (Net/CoinBank). Online and offline each keep their own numbers.
    /// </summary>
    public static class Wallet
    {
        const string SavedKey = "bb.wallet"; // "account|coins|tickets": the last numbers the server sent (".local" offline)
        const string ShortCoins = "Not enough coins yet! 🪙";
        const string ShortTickets = "Not enough 🎟️ tickets yet.";

        static int coins, tickets;
        static bool fresh;
        static string account; // whose numbers these are

        /// <summary>Raised whenever the numbers change (a reply arrived, a find paid, the connection dropped).</summary>
        public static event Action Changed;
        /// <summary>Coins arrived: how many, and why ("daily gift", "town find"). The HUD counter shows a "+n" pop.</summary>
        public static event Action<int, string> Popped;

        /// <summary>Coins: the server's balance (the last one it sent while offline; 0 when nobody is signed in).</summary>
        public static int Coins => Mine ? coins : 0;
        /// <summary>Book Fair tickets.</summary>
        public static int Tickets => Mine ? tickets : 0;
        public static WalletState State { get; private set; } = new WalletState();
        /// <summary>The numbers came from the server this session and it's reachable: spending and claiming can go ahead.</summary>
        public static bool Ready => fresh && Mine;
        /// <summary>There's a balance to show: fresh, or the last one the server sent (show it as offline when not Ready).</summary>
        public static bool HasBalance => Mine;
        /// <summary>The prices and rewards (Data/economy.json).</summary>
        public static EconomyData Economy => EconomyData.Current;
        /// <summary>Playing offline: these are the offline purse's coins, on this PC until they're banked.</summary>
        public static bool OfflinePurse => Settings.IsLocal;

        static bool Mine => Settings.SignedIn && account == Settings.AccountId;
        static string Key => Settings.IsLocal ? SavedKey + ".local" : SavedKey;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            EconomyData.TextLoader = Art.Text;
            Settings.ServerChanged -= Load;
            Settings.ServerChanged += Load;
            CoinBank.BankedCoins -= OnBanked;
            CoinBank.BankedCoins += OnBanked;
            Load();
        }

        // the last numbers saved for the server in use (switching between online and offline shows the other's)
        static void Load()
        {
            fresh = false;
            account = null;
            coins = tickets = 0;
            State = new WalletState();
            var saved = PlayerPrefs.GetString(Key, "").Split('|');
            if (saved.Length == 3)
            {
                account = saved[0];
                int.TryParse(saved[1], out coins);
                int.TryParse(saved[2], out tickets);
            }
            Changed?.Invoke();
        }

        // offline coins went to the online wallet: both purses have new numbers
        static void OnBanked(int banked) => _ = Refresh();

        /// <summary>Asks the server for the wallet (the first time on an account, that adds its starter coins). False when it couldn't.</summary>
        public static async Task<bool> Refresh()
        {
            if (!Settings.SignedIn) return false;
            string server = Settings.Server;
            try
            {
                var reply = await BBApi.Wallet();
                if (Settings.Server == server) Apply(reply); // not another server's numbers
                return true;
            }
            catch (BBApi.ApiError) { GoneOffline(); return false; }
        }

        /// <summary>Takes in a wallet reply (any server reply that carries "balance").</summary>
        public static void Apply(Dictionary<string, object> reply)
        {
            if (!reply.Has("balance")) return;
            account = Settings.AccountId;
            fresh = true;
            coins = reply.Int("balance");
            tickets = reply.Int("fair");
            State = Read(reply);
            Save();
            Shop.TakeOwned(TalesSave.Current, State.Owned);
            Changed?.Invoke();
        }

        /// <summary>An item's price (see WalletMath.Price), 0 when there's no such item.</summary>
        public static int Price(string item) => WalletMath.Price(Economy, item);

        public static bool CanAfford(int price) => Ready && Coins >= price;

        /// <summary>
        /// Buys something. kind "shop": item as in WalletMath.Price ("decor:t_fantasy_0", "satchel:pawtopia",
        /// "stock:2026-10-06:romance:2"…), priced by the server. kind "fair": item "tickets:&lt;n&gt;" trades coins for a Book
        /// Fair ticket pack. "reff" is the receipt: leave it out and items that sell once use the item, the rest a fresh
        /// nonce; give "item:&lt;pet key&gt;" for something each pet buys once (a move). Already = it was yours already.
        /// </summary>
        public static async Task<WalletResult> Spend(string kind, string item, string reff = null)
        {
            if (!Settings.SignedIn) return SignedOut();
            int price = Price(item);
            if (Ready && price > 0 && Coins < price) return new WalletResult(WalletOutcome.Short, 0, ShortCoins);
            var body = new Dictionary<string, object>
            {
                ["kind"] = kind, ["item"] = item, ["ref"] = reff ?? (WalletMath.Once(item) ? item : item + ":" + Nonce()),
            };
            if (item.StartsWith("tickets:") && int.TryParse(item.Substring(8), out int n)) body["n"] = (double)n;
            try
            {
                var reply = await BBApi.WalletSpend(body);
                Apply(reply);
                return new WalletResult(WalletOutcome.Done, reply.Int("paid"), "");
            }
            catch (BBApi.ApiError e) { return Failed(e); }
        }

        /// <summary>
        /// Claims a reward the server pays from its own table: "gift" (the daily gift). The coins pop on the HUD with
        /// "why" (default "daily gift"). Already = claimed before, today.
        /// </summary>
        public static async Task<WalletResult> Earn(string kind, string reff = null, string why = null)
        {
            if (!Settings.SignedIn) return SignedOut();
            try
            {
                var reply = await BBApi.WalletEarn(new Dictionary<string, object> { ["kind"] = kind, ["ref"] = reff ?? "" });
                Apply(reply);
                int granted = reply.Int("granted");
                if (granted > 0) Popped?.Invoke(granted, why ?? (kind == "gift" ? "daily gift" : ""));
                return new WalletResult(WalletOutcome.Done, granted, granted > 0 ? "" : "🪙 Daily limit reached for that reward");
            }
            catch (BBApi.ApiError e) { return Failed(e, kind == "gift" ? "Today’s gift was already claimed" : null); }
        }

        /// <summary>The server already paid (a town find): shows the coins arriving. "balance" is the new total when the server sent it.</summary>
        public static void Credited(int amount, string why, int balance = -1)
        {
            if (amount <= 0 || !Settings.SignedIn) return;
            if (balance >= 0) { account = Settings.AccountId; coins = balance; }
            else if (Mine) coins += amount;
            else return;
            Save();
            Changed?.Invoke();
            Popped?.Invoke(amount, why);
        }

        // ---- replies ----

        static WalletResult Failed(BBApi.ApiError e, string already = null)
        {
            if (e.Status == 0)
            {
                GoneOffline();
                return new WalletResult(WalletOutcome.Offline, 0, "Can’t reach the server, so nothing changed. Try again in a moment.");
            }
            if (e.Status == 409 || e.Status == 402) _ = Refresh();
            if (e.Status == 409) return new WalletResult(WalletOutcome.Already, 0, already ?? "That’s already yours.");
            if (e.Status == 402) return new WalletResult(WalletOutcome.Short, 0, e.Message.Contains("ticket") ? ShortTickets : ShortCoins);
            return new WalletResult(WalletOutcome.Refused, 0, e.Message);
        }

        static WalletResult SignedOut() => new WalletResult(WalletOutcome.Offline, 0, Settings.IsLocal
            ? "Offline coins need a buddy. Hatch your egg on the title screen to use them."
            : "Coins live on your account. Sign in from the title screen to use them.");

        static void GoneOffline()
        {
            fresh = false;
            Changed?.Invoke();
        }

        static void Save()
        {
            PlayerPrefs.SetString(Key, $"{account}|{coins}|{tickets}");
            PlayerPrefs.Save();
        }

        // a receipt for something you can buy again (a satchel): unique per tap, the same if a retry resends it
        static string Nonce() => Convert.ToString(DateTime.UtcNow.Ticks / 10000, 16) + UnityEngine.Random.Range(0x100000, 0xffffff).ToString("x");

        static WalletState Read(Dictionary<string, object> r)
        {
            var gift = r.Obj("gift");
            var s = new WalletState
            {
                Day = r.Str("day"), GiftClaimed = gift.Truthy("claimed"), GiftRun = gift.Int("run"),
                Balls = r.Int("balls"), WheelTickets = r.Int("tix"), Drops = r.Int("drops"), Pity = r.Int("pity"), FreeCapsule = r.Truthy("freecap"),
            };
            Counts(r.Obj("used"), s.Used);
            Counts(r.Obj("ups"), s.Upgrades);
            foreach (Dictionary<string, object> e in r.Arr("recent"))
                s.Recent.Add(new CoinEntry { Kind = e.Str("kind"), Amount = e.Int("amount"), Day = e.Str("day") });
            foreach (var o in r.Arr("owned")) if (o is string item) s.Owned.Add(item);
            return s;
        }

        static void Counts(Dictionary<string, object> from, Dictionary<string, int> into)
        {
            if (from == null) return;
            foreach (var kv in from) if (kv.Value is double d) into[kv.Key] = (int)d;
        }
    }
}
