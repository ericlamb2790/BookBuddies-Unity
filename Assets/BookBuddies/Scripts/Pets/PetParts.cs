using System.Collections.Generic;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Every piece a pet is drawn from (body shapes, ears, faces, outfits...), as SVG snippets.
    /// Loaded from Resources/BookBuddies/Data/pet_parts.json, which was exported from the website,
    /// so new outfits or faces are added by editing that file, not code.
    /// Snippets use placeholders the builder fills in: {pb} body, {line} outline, {shade}, {light}, {inner} ear pink, {h} hue.
    /// </summary>
    public sealed class PetParts
    {
        public sealed class Shape
        {
            public string Path;
            public int Top;
            public int FaceOffset;
            public int[] Arms;   // left x, right x, y
            public int[] Feet;   // left x, right x, y (null = no feet)
            public string Back;  // drawn behind the body (tails, spikes)
            public string Front; // drawn on the face layer (beaks)
        }

        /// <summary>Outfit piece. Tops and dresses have a sleeve colour, a body part (clipped to the pet) and an on-top part.</summary>
        public sealed class Item
        {
            public string Svg;        // simple items (hats, glasses...)
            public string Sleeve;     // tops: sleeve colour
            public string Body;       // tops: clipped to the body
            public string Over;       // tops: drawn over the body
            public bool IsGarment => Svg == null;
        }

        public string Inner;
        public Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>();
        public double[] Sizes;
        public double[] Stages;
        public Dictionary<string, string> Ears = new Dictionary<string, string>();
        public Dictionary<string, string> Horns = new Dictionary<string, string>();
        public Dictionary<string, string> Patterns = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, string>> Faces = new Dictionary<string, Dictionary<string, string>>();
        public string[] Sprouts;
        public string Freckles, ShinyStars, LegendStars, Egg;
        public Dictionary<string, Item> Items = new Dictionary<string, Item>();
        public HashSet<string> BackItems, Sleeveless;

        public static PetParts FromJson(string json)
        {
            var j = Json.ParseObject(json);
            var p = new PetParts
            {
                Inner = j.Str("inner"),
                Sizes = j.Doubles("sizes"),
                Stages = j.Doubles("stages"),
                Freckles = j.Str("freckles"),
                ShinyStars = j.Str("shinyStars"),
                LegendStars = j.Str("legendStars"),
                Egg = j.Str("egg"),
                BackItems = Strings(j.Arr("backItems")),
                Sleeveless = Strings(j.Arr("sleeveless")),
            };
            foreach (var kv in j.Obj("shapes"))
            {
                var s = (Dictionary<string, object>)kv.Value;
                p.Shapes[kv.Key] = new Shape
                {
                    Path = s.Str("d"), Top = s.Int("top"), FaceOffset = s.Int("fy"), Arms = s.Ints("arms"),
                    Feet = s.Has("feet") ? s.Ints("feet") : null, Back = s.Str("back"), Front = s.Str("front"),
                };
            }
            ReadStrings(j.Obj("ears"), p.Ears);
            ReadStrings(j.Obj("horns"), p.Horns);
            ReadStrings(j.Obj("patterns"), p.Patterns);
            foreach (var kv in j.Obj("faces"))
            {
                var moods = new Dictionary<string, string>();
                ReadStrings((Dictionary<string, object>)kv.Value, moods);
                p.Faces[kv.Key] = moods;
            }
            var sprouts = j.Arr("sprouts");
            p.Sprouts = new string[sprouts.Count];
            for (int i = 0; i < sprouts.Count; i++) p.Sprouts[i] = (string)sprouts[i];
            foreach (var kv in j.Obj("items"))
            {
                if (kv.Value is string svg) p.Items[kv.Key] = new Item { Svg = svg };
                else if (kv.Value is Dictionary<string, object> g)
                    p.Items[kv.Key] = new Item { Sleeve = g.Str("c", null), Body = g.Str("d", null), Over = g.Str("s", null) };
            }
            return p;
        }

        static void ReadStrings(Dictionary<string, object> from, Dictionary<string, string> to)
        {
            if (from == null) return;
            foreach (var kv in from) to[kv.Key] = kv.Value as string ?? "";
        }

        static HashSet<string> Strings(List<object> a)
        {
            var set = new HashSet<string>();
            foreach (var v in a) set.Add((string)v);
            return set;
        }
    }
}
