using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // Natures (LT_NAT in loot.json): each personality nudges one stat up, one down, and adds a small gear-like quirk
    public static partial class HeroFactory
    {
        static Dictionary<string, (string name, string up, string down, string quirk, double quirkValue)> natures;

        /// <summary>The 26 personalities in the site's order, with their nature.</summary>
        public static IReadOnlyDictionary<string, (string name, string up, string down, string quirk, double quirkValue)> Natures => natures ?? (natures = LoadNatures());

        /// <summary>lootNat: the nature for a personality (unknown ones count as sunny).</summary>
        public static (string key, string name, string up, string down, string quirk, double quirkValue) Nature(string personality)
        {
            string k = personality != null && Natures.ContainsKey(personality) ? personality : "sunny";
            var n = Natures[k];
            return (k, n.name, n.up, n.down, n.quirk, n.quirkValue);
        }

        /// <summary>The personality's icon (PERSONALITIES[k].icon on the site), or 🐾.</summary>
        public static string PersonalityIcon(string personality) =>
            personality != null && Icons.TryGetValue(personality, out var i) ? i : "🐾";

        static Dictionary<string, (string, string, string, string, double)> LoadNatures()
        {
            var nat = Json.ParseObject(TalesData.TextLoader("Data/loot")).Obj("LT_NAT");
            var d = new Dictionary<string, (string, string, string, string, double)>();
            foreach (var kv in nat)
            {
                var a = (List<object>)kv.Value;
                d[kv.Key] = ((string)a[0], (string)a[1], (string)a[2], (string)a[3], (double)a[4]);
            }
            return d;
        }

        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["sunny"] = "🌞", ["sassy"] = "💅", ["sage"] = "🦉", ["pirate"] = "🏴‍☠️", ["robot"] = "🤖", ["royal"] = "👑", ["surfer"] = "🏄",
            ["nerd"] = "🤓", ["granny"] = "👵", ["coach"] = "📣", ["sleuth"] = "🕵️", ["drama"] = "🎭", ["sleepy"] = "😴", ["foodie"] = "🍜",
            ["space"] = "🚀", ["gamer"] = "🎮", ["poet"] = "🪶", ["cowboy"] = "🤠", ["wizard"] = "🧙", ["ninja"] = "🥷", ["rockstar"] = "🎸",
            ["scientist"] = "🧪", ["hippie"] = "🌼", ["knight"] = "🛡️", ["grumpy"] = "😤", ["shyguy"] = "🙈",
        };
    }
}
