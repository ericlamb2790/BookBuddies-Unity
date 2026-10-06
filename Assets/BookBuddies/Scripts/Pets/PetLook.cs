using System;
using System.Collections.Generic;

namespace BookBuddies.Pets
{
    /// <summary>
    /// What a pet looks like, as sent over the network ("look" JSON, e.g. {"h":32,"s":3,"sh":"cat","e":"none","mn":"ruff","ev":2,...}).
    /// Mirrors parsePet() on the website: unknown bodies, faces, ears, patterns, manes, tails or skins fall back to the defaults,
    /// and the evolution form is kept between 1 and 3.
    /// </summary>
    public sealed class PetLook
    {
        public double Hue;
        public int Stage;
        public Dictionary<string, string> Outfit = new Dictionary<string, string>();
        public string Shape = "bean";
        public int Size = 1;
        public string Face = "cute";
        public string Ears = "none";
        public string Pattern = "none";
        public string Mane = "none";
        public string Tail = "none";
        public string Skin = "none";
        public int Evo = 1;
        public int Rank;
        public bool Shiny;
        public string Name = "";

        public string Wear(string slot) => Outfit.TryGetValue(slot, out var v) ? v : null;

        /// <summary>A fitting part by its look key: "e" ears, "mn" mane, "tl" tail or "sk" skin.</summary>
        public string Part(string key) => key == "e" ? Ears : key == "mn" ? Mane : key == "tl" ? Tail : key == "sk" ? Skin : null;

        public void SetPart(string key, string value)
        {
            if (key == "e") Ears = value;
            else if (key == "mn") Mane = value;
            else if (key == "tl") Tail = value;
            else if (key == "sk") Skin = value;
        }

        public PetLook Copy() => (PetLook)MemberwiseClone();

        public static PetLook Parse(string json, PetParts parts)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Dictionary<string, object> j;
            try { j = Json.ParseObject(json); }
            catch (FormatException) { return null; }
            if (j == null) return null;

            var look = new PetLook
            {
                Hue = j.Num("h"),
                Stage = j.Int("s"),
                Size = j.Has("z") ? j.Int("z", 1) : 1,
                Shiny = j.Truthy("sy"),
                Rank = j.Int("r"),
                Shape = Known(j.Str("sh"), parts.Shapes, "bean"),
                Face = Known(j.Str("f"), parts.Faces, "cute"),
                Ears = Known(j.Str("e"), parts.Ears, "none"),
                Pattern = Known(j.Str("pt"), parts.Patterns, "none"),
                Mane = Known(j.Str("mn"), parts.ManeNames, "none"),
                Tail = Known(j.Str("tl"), parts.TailNames, "none"),
                Skin = Known(j.Str("sk"), parts.SkinNames, "none"),
                Evo = Math.Max(1, Math.Min(3, j.Int("ev", 1))),
                Name = j.Str("n"),
            };
            if (look.Name.Length > 14) look.Name = look.Name.Substring(0, 14);
            var outfit = j.Obj("o");
            if (outfit != null)
                foreach (var kv in outfit)
                    if (kv.Value is string item && item.Length > 0) look.Outfit[kv.Key] = item;
            return look;
        }

        static string Known<T>(string key, Dictionary<string, T> table, string fallback) =>
            key != null && table.ContainsKey(key) ? key : fallback;
    }
}
