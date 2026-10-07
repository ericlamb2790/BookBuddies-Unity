using System;
using System.Collections.Generic;
using System.Globalization;

namespace BookBuddies.Tales
{
    /// <summary>What happened to one found item: kept in the bag or turned into dust, new in the codex, a foe's signature piece.</summary>
    public sealed class LootDrop
    {
        public string Item;     // the item string
        public GearItem Gear;
        public bool Kept;       // false: the bag was full, so it became Dust
        public bool Fresh;      // its base is new in the codex
        public bool Signature;  // the foe's own signature piece
        public string From;     // the foe that dropped it, or null
        public int Dust;
    }

    /// <summary>A foe's loot table (ltFoeTab): the theme it drops, two favourite slots, a signature piece and its odds.</summary>
    public sealed class FoeLoot
    {
        public string Theme, Signature;
        public string[] Slots;
        public bool Boss;
        public double Rate, SignatureChance;
    }

    // Drops: lootRoll, ltDropFrom (wild and tale wins), the road's stash, shrine and guardian chest, and lootAdd
    public static partial class Loot
    {
        static readonly double[] Weights = { 50, 30, 14, 5, 1 };
        static readonly Dictionary<string, double[]> SourceWeights = new Dictionary<string, double[]>
        {
            ["fight"] = new double[] { 1, 1, 1, 1, 1 }, ["elite"] = new[] { .6, 1, 1.6, 2, 2 }, ["boss"] = new[] { 0, 1, 2.5, 3.5, 2.5 },
            ["find"] = new[] { 1, 1, 1.2, 1.2, 1.2 }, ["satchel"] = new[] { 0, 1, 1.8, 2.2, 2.6 },
        };
        static readonly Dictionary<string, FoeLoot> foeTables = new Dictionary<string, FoeLoot>();
        static readonly Dictionary<string, LootDrop> found = new Dictionary<string, LootDrop>();

        /// <summary>
        /// lootRoll: a new item string. src (fight, elite, boss, find, satchel) shapes the rarity odds and luck lifts
        /// rare and better; theme and slot are random when null; tier forces the rarity; clock (ms) replaces the time in
        /// the item's seed (a shop's stock passes 0, so its pieces are the same all day).
        /// </summary>
        public static string RollItem(string src, double il, string theme, string slot, double luck, IRng rng, int? tier = null, long? clock = null)
        {
            var d = Data;
            var sm = SourceWeights.TryGetValue(src ?? "", out var m) ? m : SourceWeights["fight"];
            double L = 1 + luck / 100, total = 0;
            var w = new double[5];
            for (int i = 0; i < 5; i++) total += w[i] = Weights[i] * sm[i] * (i >= 2 ? L : 1);
            int t = tier ?? -1;
            if (t < 0)
            {
                double r = rng.Next() * total;
                t = 0;
                while (t < 4 && r >= w[t]) { r -= w[t]; t++; }
            }
            int level = JsMath.Clamp(JsMath.RoundI(il == 0 ? 5 : il), 1, 60);
            string th = theme != null && d.Themes.ContainsKey(theme) ? theme : d.ThemeKeys[rng.Range(d.ThemeKeys.Count)];
            string b;
            if (t == 4)
            {
                var all = new List<int>();
                for (int i = 0; i < d.Legends.Count; i++) if (slot == null || d.Legends[i].Slot == slot) all.Add(i);
                var mine = all.FindAll(i => d.Legends[i].Theme == th);
                if (mine.Count > 0 && rng.Next() < .6) all = mine;
                b = "L" + all[(int)Math.Floor(rng.Next() * all.Count)];
            }
            else
            {
                string sl = slot ?? LootData.SlotKeys[(int)Math.Floor(rng.Next() * 4)];
                b = th + sl + (int)Math.Floor(rng.Next() * d.Base[th][sl].Count);
            }
            int an = t >= 2 && rng.Next() < .06 + luck / 1000 ? 1 : 0;
            return $"{b}~{t}~{level}~{Seed(rng, clock)}~{an}";
        }

        // ltSeed: the clock (or the one given) and a random number, in base 36
        static string Seed(IRng rng, long? clock = null) =>
            Base36((clock ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) % 10000000) + Base36((long)Math.Floor(rng.Next() * 1e6));

        static string Base36(long n)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (n == 0) return "0";
            var s = new System.Text.StringBuilder();
            for (; n > 0; n /= 36) s.Insert(0, digits[(int)(n % 36)]);
            return s.ToString();
        }

        /// <summary>lootAdd: puts an item in the bag (or turns it into dust when the bag is full), marks it new and adds it to the codex.</summary>
        public static LootDrop Add(TalesSave save, string itemString, string from = null, bool signature = false)
        {
            var it = Get(itemString);
            if (it == null) return null;
            var drop = new LootDrop { Item = itemString, Gear = it, From = from, Signature = signature, Fresh = !save.Seen.Contains(it.BaseKey), Kept = save.Bag.Count < BagSize };
            if (drop.Fresh) save.Seen.Add(it.BaseKey);
            if (drop.Kept) { save.Bag.Add(itemString); save.Fresh.Add(itemString); }
            else { drop.Dust = it.Dust; save.Dust += it.Dust; }
            if (found.Count > 64) found.Clear();
            found[itemString] = drop;
            save.Touch();
            return drop;
        }

        /// <summary>How an item found this session was handled (bag or dust, codex, signature, from which foe), or null.</summary>
        public static LootDrop Found(string itemString) => itemString != null && found.TryGetValue(itemString, out var d) ? d : null;

        /// <summary>
        /// A random item for a road find, added to the bag (see Found for what happened to it), or null when there's
        /// nothing today. "stash" (the Bramble Stash: once a UTC day per road link), "shrine" (the Wayshrine: once a UTC
        /// day per link, then a town find), "chest" or "boss" (the guardian's chest: call it twice, the site rolls two).
        /// Item level follows the buddy's Pet Lv; the stash and shrine use the road's next town for their theme.
        /// </summary>
        public static string Roll(string source, TalesSave save, IRng rng = null)
        {
            rng = rng ?? SystemRng.Shared;
            save = save ?? TalesSave.Current;
            int rn = HeroFactory.Renown(save.Me.Rxp).lvl;
            double luck = Luck(save);
            string town = RoadTown(save), theme = Data.Towns.TryGetValue(town, out var th) ? th : null;
            string item;
            switch (source)
            {
                case "stash":
                    if (UsedToday(save, source)) return null;
                    MarkToday(save, source);
                    int lv = TalesData.Current.Towns.TryGetValue(town, out var info) ? info.Lv : 1;
                    item = RollItem("satchel", 4 + lv * 2 + rn, theme, null, luck, rng);
                    break;
                case "shrine":
                    if (UsedToday(save, source)) return null;
                    MarkToday(save, source);
                    item = Find(town, save, rng);
                    if (item == null) save.Touch();
                    return item;
                case "chest":
                case "boss":
                    item = RollItem("boss", 10 + rn, "Z", null, luck + 15, rng);
                    break;
                default:
                    item = RollItem(source, 5 + rn, null, null, luck, rng);
                    break;
            }
            Add(save, item);
            return item;
        }

        /// <summary>True when this road link's Bramble Stash or Wayshrine ("stash" or "shrine") has given its find today (UTC).</summary>
        public static bool UsedToday(TalesSave save, string source) => save.RoadDays.TryGetValue(DayKey(save, source), out var day) && day == Today;

        /// <summary>Marks this link's stash or shrine as used today (the caller saves).</summary>
        public static void MarkToday(TalesSave save, string source) => save.RoadDays[DayKey(save, source)] = Today;

        static string DayKey(TalesSave save, string source) => (source == "stash" ? "g" : "s") + save.Link;

        /// <summary>
        /// lootFind: gear hiding in a town's curio or at a wayshrine. At most 5 finds a UTC day, each 40% of the time,
        /// themed by the town. Added to the bag (the caller saves and shows it), or null.
        /// </summary>
        public static string Find(string town, TalesSave save, IRng rng = null)
        {
            rng = rng ?? SystemRng.Shared;
            if (save.FindDay != Today) { save.FindDay = Today; save.FindCount = 0; }
            if (save.FindCount >= 5 || rng.Next() > .4) return null;
            save.FindCount++;
            string theme = town != null && Data.Towns.TryGetValue(town, out var th) ? th : "Z";
            string item = RollItem("find", 5 + HeroFactory.Renown(save.Me.Rxp).lvl, theme, null, Luck(save), rng);
            Add(save, item);
            return item;
        }

        static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // the town at the east end of the current road link (ROUTE[link])
        static string RoadTown(TalesSave save)
        {
            var route = TalesData.Current.Route;
            return save.Link >= 0 && save.Link < route.Length ? route[save.Link] : "pawtopia";
        }

        /// <summary>ltLuck: the nature's luck quirk, +10 when shiny, plus the Fortune perk.</summary>
        public static double Luck(TalesSave save, bool shiny = false)
        {
            var nat = HeroFactory.Nature(save.Personality);
            return (nat.quirk == "luck" ? nat.quirkValue : 0) + (shiny ? 10 : 0) + Sum(save.Me.Gear).Perk("lucky");
        }

        /// <summary>
        /// After a wild win: the lead foe's loot table rolls (always for a storybook villain, else its drop rate), a
        /// pack adds an extra chance, and the drops go in the bag and outcome.Drops. Every foe joins the bestiary.
        /// </summary>
        public static void ForWin(BattleOutcome outcome, BattleSetup setup, TalesSave save, IRng rng = null)
        {
            save = save ?? TalesSave.Current;
            var foes = new List<FoeVariant>();
            if (setup != null) foreach (var f in setup.Foes) if (f.v?.Def != null) foes.Add(f.v);
            if (foes.Count == 0 && outcome != null) foreach (var u in outcome.Defeated) if (u.Foe?.Def != null) foes.Add(u.Foe);
            DropFrom(TaleBattle.Fight, JsMath.Clamp(setup?.Lvl ?? 1, 1, 12), false, foes, outcome, save, Shiny(setup?.Look), rng);
        }

        /// <summary>
        /// After a tale win (lootTaleDrop): the lead foe's table rolls by the fight's kind (a boss always, plus 40% a second;
        /// an elite at least 65% with twice the signature chance), at item level 2 + ch·3 + PetLv/2 + 5.
        /// </summary>
        public static void ForTale(BattleOutcome outcome, string kind, int ch, TalesSave save, IRng rng = null)
        {
            save = save ?? TalesSave.Current;
            var foes = new List<FoeVariant>();
            foreach (var u in outcome.Defeated) if (u.Foe?.Def != null) foes.Add(u.Foe);
            DropFrom(kind, ch, true, foes, outcome, save, Shiny(outcome.Hero?.Hero?.Look), rng);
        }

        // ltDropFrom: every foe joins the bestiary, then the lead foe's table rolls; drops go in the bag and outcome.Drops
        static void DropFrom(string kind, int ch, bool epic, List<FoeVariant> foes, BattleOutcome outcome, TalesSave save, bool shiny, IRng rng)
        {
            rng = rng ?? SystemRng.Shared;
            if (foes.Count == 0) return;
            foreach (var v in foes) if (!save.Met.Contains(v.Def.N)) save.Met.Add(v.Def.N);

            var lead = foes.Find(IsVillain) ?? foes.Find(v => v.Dn != null && v.Dn != v.Def.N) ?? foes[0];
            var T = FoeTable(lead);
            int rn = HeroFactory.Renown(save.Me.Rxp).lvl;
            double luck = Luck(save, shiny);
            double il = 2 + ch * 3 + rn * .5 + (epic ? 5 : 0);
            string who = lead.Dn ?? lead.Def.N;
            bool boss = kind == TaleBattle.Boss, elite = kind == TaleBattle.Elite;

            void Drop()
            {
                bool sig = rng.Next() < T.SignatureChance * (elite && !T.Boss ? 2 : 1) * (1 + luck / 100);
                string s = sig ? SignatureItem(T, kind, il, luck, rng)
                    : RollItem(kind, il, rng.Next() < .75 ? T.Theme : null, rng.Next() < .7 ? T.Slots[(int)Math.Floor(rng.Next() * 2)] : null, luck, rng);
                var d = Add(save, s, who, sig);
                if (d != null) outcome?.Drops.Add(s);
            }
            if (rng.Next() < (boss ? 1 : elite ? Math.Max(Data.EliteRate, T.Rate + .3) : T.Rate)) Drop();
            if (boss && rng.Next() < .4) Drop();
            if (!boss && foes.Count > 1 && rng.Next() < .12 * (foes.Count - 1)) Drop();
            save.Touch();
        }

        // a legendary signature drops as itself; a crafted one keeps the roll's tier (at least Uncommon) unless it rolled a legendary
        static string SignatureItem(FoeLoot T, string src, double il, double luck, IRng rng)
        {
            if (T.Signature.StartsWith("L"))
            {
                string seed = Seed(rng);
                return $"{T.Signature}~4~{JsMath.RoundI(il)}~{seed}~{(rng.Next() < .06 ? 1 : 0)}";
            }
            var p = RollItem(src, il, T.Theme, T.Signature.Substring(1, 1), luck, rng).Split('~');
            if (!p[0].StartsWith("L")) { p[1] = Math.Max(1, int.Parse(p[1], CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); p[0] = T.Signature; }
            return string.Join("~", p);
        }

        static bool IsVillain(FoeVariant v) => TalesData.Current.Bosses.Exists(b => b.N == v.Def.N);

        static bool Shiny(string lookJson) => !string.IsNullOrEmpty(lookJson) && (Json.ParseObject(lookJson)?.Truthy("sy") ?? false);

        /// <summary>
        /// ltFoeTab: a foe's loot table, from a hash of its name. Cached by base name, so the first variant seen sets
        /// the drop rate (tougher variants drop more, up to 45%; storybook villains always drop).
        /// </summary>
        public static FoeLoot FoeTable(FoeVariant v)
        {
            string n = v.Def.N;
            if (foeTables.TryGetValue(n, out var hit)) return hit;
            var d = Data;
            bool boss = IsVillain(v);
            uint h = JsMath.Hash("ft" + n);
            var r = new Mulberry32(h);
            string th = ThemeOfGenre(v.Def.G) ?? d.ThemeKeys[(int)(h % (uint)d.ThemeKeys.Count)];
            var slots = LootData.SlotKeys;
            string s1 = slots[(int)Math.Floor(r.Next() * 4)], s2 = slots[(int)Math.Floor(r.Next() * 4)];
            if (s2 == s1) s2 = slots[(Array.IndexOf(slots, s1) + 2) % 4];
            string sig;
            if (boss)
            {
                var own = new List<int>();
                for (int i = LootData.BossLegendsFrom; i < d.Legends.Count; i++) if (d.Legends[i].Theme == th) own.Add(i);
                int bi = TalesData.Current.Bosses.FindAll(b => ThemeOfGenre(b.G) == th).FindIndex(b => b.N == n);
                sig = own.Count > 0 ? "L" + own[(int)((bi >= 0 ? (uint)bi : h) % (uint)own.Count)] : th + s1 + 6;
            }
            else sig = th + s1 + (6 + (int)Math.Floor(r.Next() * 32));
            double hp = v.Hp == 0 ? 1 : v.Hp;
            return foeTables[n] = new FoeLoot
            {
                Theme = th, Slots = new[] { s1, s2 }, Signature = sig, Boss = boss,
                Rate = boss ? 1 : Math.Min(d.DropMax, d.DropRate + d.DropPerHp * (hp - .8)), SignatureChance = boss ? d.BossSigChance : d.SigChance,
            };
        }

        /// <summary>gymTab: gives a foe its own loot table (a Book Boss: its town's theme, two slots and a legendary signature).</summary>
        public static void SetFoeTable(string name, FoeLoot table) => foeTables[name] = table;

        static string ThemeOfGenre(int g) => g >= 0 && g < Data.GenreThemes.Length ? Data.GenreThemes[g] : null;
    }
}
