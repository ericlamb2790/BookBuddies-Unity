using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// A hero seed as JSON (the site's myHeroSnap in a run's party), so a tale keeps the pet it set out with.
    /// Take one with HeroFactory.SeedOf; values come out in parsed-JSON shape, so ToJson → FromJson needs no Json.Write between.
    /// </summary>
    public static class HeroSnap
    {
        /// <summary>The seed as a JSON object.</summary>
        public static Dictionary<string, object> ToJson(HeroSeed s) => new Dictionary<string, object>
        {
            ["cls"] = s.Cls, ["pers"] = s.Personality, ["pet"] = s.PetKey, ["look"] = s.Look, ["n"] = s.Name,
            ["hue"] = s.Hue, ["rxp"] = s.Rxp, ["mood"] = s.Mood, ["stage"] = (double)s.Stage, ["rank"] = (double)s.Rank, ["sy"] = s.Shiny,
            ["meta"] = new Dictionary<string, object> { ["vit"] = (double)s.MetaVit, ["atk"] = (double)s.MetaAtk, ["ink"] = (double)s.MetaInk, ["rev"] = s.MetaRev },
            ["gx"] = new Dictionary<string, object>
            {
                ["st"] = TalesSave.Nums(s.Gear.Stats), ["pk"] = TalesSave.Nums(s.Gear.Perks), ["from"] = TalesSave.Texts(s.Gear.PerkFrom), ["pow"] = s.Gear.Power,
            },
            ["kit"] = s.Kit == null ? null : TalesSave.Strs(s.Kit), ["own"] = s.Own == null ? null : TalesSave.Strs(s.Own),
        };

        /// <summary>A seed from ToJson's object; null when there is none.</summary>
        public static HeroSeed FromJson(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var meta = o.Obj("meta");
            var s = new HeroSeed
            {
                Cls = o.Str("cls", "sleuth"), Personality = o.Str("pers", "sunny"), PetKey = o.Str("pet", "me"), Look = o.Str("look", null), Name = o.Str("n", "Buddy"),
                Hue = o.Num("hue"), Rxp = o.Num("rxp"), Mood = o.Num("mood", 60), Stage = o.Int("stage", 1), Rank = o.Int("rank"), Shiny = o.Truthy("sy"),
                MetaVit = meta.Int("vit"), MetaAtk = meta.Int("atk"), MetaInk = meta.Int("ink"), MetaRev = meta.Truthy("rev"),
                Kit = o.Has("kit") ? new List<string>(TalesData.Strings(o.Arr("kit"))) : null,
                Own = o.Has("own") ? new List<string>(TalesData.Strings(o.Arr("own"))) : null,
            };
            var gx = o.Obj("gx");
            ReadNums(gx.Obj("st"), s.Gear.Stats);
            ReadNums(gx.Obj("pk"), s.Gear.Perks);
            var from = gx.Obj("from");
            if (from != null) foreach (var kv in from) if (kv.Value is string n) s.Gear.PerkFrom[kv.Key] = n;
            s.Gear.Power = gx.Num("pow");
            return s;
        }

        /// <summary>Copies the numbers of a JSON object into a dictionary (other values are skipped).</summary>
        internal static void ReadNums(Dictionary<string, object> from, Dictionary<string, double> to)
        {
            if (from != null) foreach (var kv in from) if (kv.Value is double d) to[kv.Key] = d;
        }
    }
}
