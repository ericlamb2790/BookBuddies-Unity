using System.Collections.Generic;

namespace BookBuddies.Pets
{
    /// <summary>
    /// What a pet looks like, as sent over the network ("look" JSON, e.g. {"h":32,"s":3,"sh":"round","e":"bear",...}).
    /// Mirrors parsePet() on the website: unknown shapes, faces, ears or patterns fall back to the defaults.
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
        public bool Shiny;
        public string Name = "";

        public string Wear(string slot) => Outfit.TryGetValue(slot, out var v) ? v : null;

        public static PetLook Parse(string json, PetParts parts)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Dictionary<string, object> j;
            try { j = Json.ParseObject(json); }
            catch (System.FormatException) { return null; }
            if (j == null) return null;

            var look = new PetLook
            {
                Hue = j.Num("h"),
                Stage = j.Int("s"),
                Size = j.Has("z") ? j.Int("z", 1) : 1,
                Shiny = j.Truthy("sy"),
                Shape = Known(j.Str("sh"), parts.Shapes, "bean"),
                Face = Known(j.Str("f"), parts.Faces, "cute"),
                Ears = Known(j.Str("e"), parts.Ears, "none"),
                Pattern = Known(j.Str("pt"), parts.Patterns, "none"),
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
