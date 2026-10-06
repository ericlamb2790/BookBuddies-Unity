using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>Everything tqPetHero and lootHero read from a pet, so a hero can be built from the save or by hand (tests, previews).</summary>
    public sealed class HeroSeed
    {
        public string Cls = "sleuth", Personality = "sunny", PetKey = "me", Look, Name = "Buddy";
        public double Hue, Rxp, Mood = 60;
        public int Stage = 1, Rank, MetaVit, MetaAtk;
        public bool Shiny;
        public GearSum Gear = new GearSum();
        public List<string> Kit, Own;         // null = the class's starting kit and owned moves
    }

    /// <summary>tqPetHero + lootHero + tqStats, renown and move names for your buddy.</summary>
    public static partial class HeroFactory
    {
        /// <summary>Your buddy as a battle hero at tale level lvl, from TalesSave.Current and the pet's look.</summary>
        public static BattleUnit Build(string lookJson, string petName, int lvl)
        {
            var save = TalesSave.Current;
            var look = Json.ParseObject(string.IsNullOrEmpty(lookJson) ? "{}" : lookJson) ?? new Dictionary<string, object>();
            string cls = ClassOf(save, lookJson);
            var seed = new HeroSeed
            {
                Cls = cls, Personality = save.Personality, Look = lookJson, Name = petName,
                Hue = look.Num("h"), Stage = JsMath.Clamp(look.Int("s"), 0, 5), Rank = Math.Max(0, look.Int("r")), Shiny = look.Truthy("sy"),
                Rxp = save.Me.Rxp, Gear = Loot.Sum(save.Me.Gear, TalesData.GenreOfHue(look.Num("h"))),
                Kit = KitOf(save, cls), Own = Owned(save, cls),
            };
            var hero = Build(seed, lvl);
            var info = hero.Hero;
            info.Order = save.Me.Order == null ? null : new List<string>(save.Me.Order);
            info.AutoUlt = save.Me.AutoUlt;
            if (save.Me.Lane == "l" || save.Me.Lane == "r") hero.Lane = save.Me.Lane[0];
            return hero;
        }

        /// <summary>A hero from a seed at tale level lvl: base stats, nature, spark, mood and gear (lootHero), then tqStats.</summary>
        public static BattleUnit Build(HeroSeed s, int lvl)
        {
            var d = TalesData.Current;
            var C = d.Class(s.Cls);
            int rn = Renown(s.Rxp).lvl;
            var info = new HeroInfo
            {
                Cls = C.Key, Look = s.Look, PetKey = s.PetKey, Rank = s.Rank, Stage = s.Stage, Shiny = s.Shiny, Rn = rn, Lvl = Math.Max(1, lvl), Gear = s.Gear,
                Kit = s.Kit ?? KitOf(C.Key, rn, null, null), Own = s.Own ?? BaseOwn(C.Key),
            };
            double mult = (1 + .1 * s.Stage) * (1 + .22 * s.Rank) * (s.Shiny ? 1.1 : 1) * (1 + RenownBonus(rn)) * (1 + .05 * Evo(rn));
            HeroEvo.SetBase(info, C, mult, s);
            ApplyNatureAndGear(info, s);
            info.Known = info.Kit.FindAll(k => Move(C.Key, k) != null);

            var hero = new BattleUnit { Key = "h:" + s.PetKey, Name = s.Name, Gen = TalesData.GenreOfHue(s.Hue), Hero = info };
            foreach (var k in info.Known) hero.Moves.Add(Move(C.Key, k));
            SetLevel(hero, info.Lvl);
            hero.Hp = hero.Max;
            return hero;
        }

        /// <summary>tqStats with no boons: hp, atk and def grow 8% per tale level, speed doesn't.</summary>
        public static void SetLevel(BattleUnit hero, int lvl)
        {
            var h = hero.Hero;
            h.Lvl = lvl;
            hero.Lvl = lvl;
            double lv = 1 + .08 * (lvl - 1);
            hero.Max = JsMath.Round(h.BaseHp * lv);
            hero.Atk = JsMath.Round(h.BaseAtk * lv);
            hero.Def = JsMath.Round(h.BaseDef * lv);
            hero.Spd = h.BaseSpd;
        }

        /// <summary>Stats shown on cards (tqStats with no boons at level 1): hp, atk, def, spd and power.</summary>
        public static (int hp, int atk, int def, int spd, int power) CardStats(BattleUnit hero)
        {
            var h = hero.Hero;
            int power = JsMath.RoundI(h.BaseHp / 4.0 + h.BaseAtk * 3 + h.BaseDef * 2 + h.BaseSpd * 2);
            return (h.BaseHp, h.BaseAtk, h.BaseDef, h.BaseSpd, power);
        }

        /// <summary>Class key for the buddy (chosen or natural from personality).</summary>
        public static string ClassOf(TalesSave save)
        {
            var d = TalesData.Current;
            if (save.Me.Cls != null && d.Classes.ContainsKey(save.Me.Cls)) return save.Me.Cls;
            return d.PersonalityClass.TryGetValue(save.Personality ?? "sunny", out var c) && d.Classes.ContainsKey(c) ? c : "sleuth";
        }

        /// <summary>ClassOf after giving a Unity-hatched buddy its personality (picked once from its look, then saved).</summary>
        public static string ClassOf(TalesSave save, string lookJson)
        {
            EnsurePersonality(save, lookJson);
            return ClassOf(save);
        }

        /// <summary>The buddy's personality: the saved one, or one of LT_NAT's 26 picked by the look's hash and saved.</summary>
        public static string EnsurePersonality(TalesSave save, string lookJson)
        {
            if (save.Personality != null) return save.Personality;
            var keys = new List<string>(Natures.Keys);
            save.Personality = keys[(int)(JsMath.Hash(lookJson ?? "") % (uint)keys.Count)];
            save.Touch();
            return save.Personality;
        }

        // lootHero: nature up/down, the quirk, spark, mood and gear multiply the base stats; gear numbers go to gx
        static void ApplyNatureAndGear(HeroInfo h, HeroSeed s)
        {
            var nat = Nature(s.Personality);
            var g = s.Gear ?? new GearSum();
            var sum = new Dictionary<string, double>(g.Stats);
            if (nat.quirk != "luck") sum[nat.quirk] = Get(sum, nat.quirk) + nat.quirkValue;
            uint spark = JsMath.Hash("spark:" + s.PetKey);
            var sp = new Dictionary<string, double> { ["hp"] = spark % 6, ["atk"] = (spark >> 3) % 6, ["def"] = (spark >> 6) % 6, ["spd"] = (spark >> 9) % 6 };
            double mood = JsMath.Clamp(JsMath.Round((s.Mood - 50) / 10), -5, 5);
            double F(string k) => 1 + Get(sum, k) / 100 + (nat.up == k ? .08 : 0) - (nat.down == k ? .04 : 0) + sp[k] * .01 + (k == "atk" || k == "spd" ? mood / 100 : 0);

            double hp = F("hp"), df = F("def"), spd = F("spd");
            double giant = g.Perk("giant"), haste = g.Perk("haste");
            if (giant != 0) { hp *= 1 + giant / 100; df *= 1 + giant / 100; }
            if (haste != 0) spd *= 1 + haste / 100;
            h.BaseHp = JsMath.RoundI(h.BaseHp * hp);
            h.BaseAtk = JsMath.RoundI(h.BaseAtk * F("atk"));
            h.BaseDef = JsMath.RoundI(h.BaseDef * df);
            h.BaseSpd = Math.Max(1, JsMath.RoundI(h.BaseSpd * spd));

            h.Cc = JsMath.Round1(Get(sum, "cc"));
            h.Cd = JsMath.Round1(Get(sum, "cd"));
            h.Ls = JsMath.Round1(Get(sum, "ls") + g.Perk("vamp"));
            h.Th = JsMath.Round1(Get(sum, "th") + g.Perk("thornmail"));
            h.Dg = JsMath.Round1(Get(sum, "dg") + g.Perk("mirror"));
            h.GearInk = JsMath.RoundI(Get(sum, "ink")) + (haste != 0 ? 2 : 0);
            h.Rg = JsMath.Round1(Get(sum, "rg"));
            h.Sh = JsMath.Round1(Get(sum, "sh") + g.Perk("aegis"));
            h.NatureName = nat.name;
            h.NatureIcon = PersonalityIcon(nat.key);
        }

        static double Get(Dictionary<string, double> d, string k) => d.TryGetValue(k, out var v) ? v : 0;
    }
}
