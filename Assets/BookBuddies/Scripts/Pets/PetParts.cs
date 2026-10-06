using System;
using System.Collections.Generic;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Every piece a pet is drawn from (bodies, ears, manes, tails, fur, evolution add-ons, outfits...), as SVG snippets.
    /// Loaded from Resources/BookBuddies/Data/pet_parts.json, which was exported from the website (tools/export_pets.js),
    /// so new outfits or bodies are added by editing that file, not code.
    /// Snippets use placeholders the builder fills in: {pb} body, {line} outline, {shade}, {light}, {inner} ear pink,
    /// {h} hue, {acc} evolution accent and {mc} mane colour.
    /// </summary>
    public sealed class PetParts
    {
        /// <summary>Reads a Resources text file ("Data/pet_parts"). Set by PetSprites in Unity, or by tests.</summary>
        public static Func<string, string> TextLoader;
        static PetParts current;

        /// <summary>The shared parts, loaded once.</summary>
        public static PetParts Current => current ?? (current = FromJson(TextLoader("Data/pet_parts")));

        /// <summary>A body. Layers that change as the pet evolves hold one snippet per form (1-3).</summary>
        public sealed class Shape
        {
            public string Path;
            public int Top;
            public int FaceOffset;
            public int[] Arms;        // left x, right x, y
            public int[] Feet;        // left x, right x, y (null = no feet)
            public string Back;       // drawn behind the body (tails, spikes)
            public string[] Front;    // with the face (noses, beaks)
            public string[] Under;    // under the face (muzzles, bellies)
            public string[] Over;     // over the body (pots, shells, caps)
            public string[] ArmLeft, ArmRight; // the body's own arms (null = round arms or leaves)
            public string Tail;       // its natural tail, or null
            public int Fang;          // 1 or 2 fangs at form 3
            public bool Leaf, NoSprout;

            /// <summary>The snippet for evolution form 1-3, or "".</summary>
            public static string ForForm(string[] perForm, int evo) => perForm == null ? "" : perForm[Math.Max(1, Math.Min(3, evo)) - 1];
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

        /// <summary>
        /// What a body grows at one evolution form: layers (back, behind, mark, over, head, face, front) and flags
        /// (noCrest, noAura, noSprout...). A few layers change when the pet wears a hat or has a tail.
        /// </summary>
        public sealed class EvoForm
        {
            public static readonly EvoForm None = new EvoForm();
            readonly Dictionary<string, string> layers = new Dictionary<string, string>(), hat = new Dictionary<string, string>(), tail = new Dictionary<string, string>();
            readonly HashSet<string> flags = new HashSet<string>();

            public string Layer(string name, bool wearsHat, bool hasTail) =>
                hasTail && tail.TryGetValue(name, out var t) ? t : wearsHat && hat.TryGetValue(name, out var h) ? h : layers.TryGetValue(name, out var l) ? l : "";

            public bool Has(string flag) => flags.Contains(flag);

            internal static EvoForm FromJson(Dictionary<string, object> o)
            {
                var f = new EvoForm();
                foreach (var kv in o)
                {
                    if (kv.Value is string s) f.layers[kv.Key] = s;
                    else if (kv.Key == "hat" || kv.Key == "tail") ReadStrings((Dictionary<string, object>)kv.Value, kv.Key == "hat" ? f.hat : f.tail);
                    else f.flags.Add(kv.Key);
                }
                return f;
            }
        }

        /// <summary>A body's fur: the coat round its outline and the tufts painted inside, [furry, fluffy].</summary>
        public sealed class Fur { public string[] Coat, Inner; }

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

        public Dictionary<string, Dictionary<int, string[]>> Manes = new Dictionary<string, Dictionary<int, string[]>>(); // mane -> head y -> [behind, in front]
        public Dictionary<string, string> Tails = new Dictionary<string, string>();
        public Dictionary<string, int> TailReach = new Dictionary<string, int>();
        public Dictionary<string, Fur> Furs = new Dictionary<string, Fur>();
        public Dictionary<string, EvoForm[]> Evo = new Dictionary<string, EvoForm[]>();
        public Dictionary<string, string[][]> BodyLines = new Dictionary<string, string[][]>();

        /// <summary>Display names and icons, in the site's order: shapes, manes, tails and skins.</summary>
        public Dictionary<string, (string name, string icon)> ShapeNames, ManeNames, TailNames, SkinNames;
        public List<string> EarKeys, PatternKeys, FaceKeys;

        public PetEvo Evolution;
        public PetDna Dna;

        public Shape ShapeOf(string key) => key != null && Shapes.TryGetValue(key, out var s) ? s : Shapes["bean"];

        /// <summary>The evolution add-ons for a body at form 1-3.</summary>
        public EvoForm EvoOf(string body, int evo) => Evo.TryGetValue(body, out var forms) ? forms[Math.Max(1, Math.Min(3, evo)) - 1] : EvoForm.None;

        /// <summary>The idle lines a body says at its form (bodyLines), or none.</summary>
        public string[] LinesOf(string body, int evo) =>
            BodyLines.TryGetValue(body ?? "", out var l) ? (evo >= 1 && evo <= l.Length ? l[evo - 1] : l[0]) : new string[0];

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
                Sprouts = List(j.Arr("sprouts")).ToArray(),
            };
            foreach (var kv in j.Obj("shapes")) p.Shapes[kv.Key] = ReadShape((Dictionary<string, object>)kv.Value);
            ReadStrings(j.Obj("ears"), p.Ears);
            ReadStrings(j.Obj("horns"), p.Horns);
            ReadStrings(j.Obj("patterns"), p.Patterns);
            ReadStrings(j.Obj("tails"), p.Tails);
            foreach (var kv in j.Obj("faces"))
            {
                var moods = new Dictionary<string, string>();
                ReadStrings((Dictionary<string, object>)kv.Value, moods);
                p.Faces[kv.Key] = moods;
            }
            foreach (var kv in j.Obj("items"))
            {
                if (kv.Value is string svg) p.Items[kv.Key] = new Item { Svg = svg };
                else if (kv.Value is Dictionary<string, object> g)
                    p.Items[kv.Key] = new Item { Sleeve = g.Str("c", null), Body = g.Str("d", null), Over = g.Str("s", null) };
            }
            ReadArt(j, p);
            ReadNames(j.Obj("names"), p);
            p.Evolution = PetEvo.FromJson(j.Obj("evoStats"));
            p.Dna = PetDna.FromJson(j.Obj("fit"), j.Obj("roll"), j.Obj("petNames"), p);
            return p;
        }

        // ---- reading ----

        static Shape ReadShape(Dictionary<string, object> s) => new Shape
        {
            Path = s.Str("d"), Top = s.Int("top"), FaceOffset = s.Int("fy"), Arms = s.Ints("arms"),
            Feet = s.Has("feet") ? s.Ints("feet") : null, Back = s.Str("back"),
            Front = PerForm(s, "front"), Under = PerForm(s, "under"), Over = PerForm(s, "over"),
            ArmLeft = PerForm(s, "armL"), ArmRight = PerForm(s, "armR"),
            Tail = s.Str("tl", null), Fang = s.Int("fang"), Leaf = s.Truthy("leaf"), NoSprout = s.Truthy("nosprout"),
        };

        // manes per head height, fur, evolution forms and idle lines
        static void ReadArt(Dictionary<string, object> j, PetParts p)
        {
            foreach (var kv in j.Obj("manes"))
            {
                var byHeight = new Dictionary<int, string[]>();
                foreach (var h in (Dictionary<string, object>)kv.Value) byHeight[int.Parse(h.Key)] = List((List<object>)h.Value).ToArray();
                p.Manes[kv.Key] = byHeight;
            }
            foreach (var kv in j.Obj("tailReach")) p.TailReach[kv.Key] = (int)(double)kv.Value;
            foreach (var kv in j.Obj("fur"))
            {
                var f = (Dictionary<string, object>)kv.Value;
                p.Furs[kv.Key] = new Fur { Coat = List(f.Arr("coat")).ToArray(), Inner = List(f.Arr("inner")).ToArray() };
            }
            foreach (var kv in j.Obj("evo"))
            {
                var forms = (List<object>)kv.Value;
                p.Evo[kv.Key] = forms.ConvertAll(f => EvoForm.FromJson((Dictionary<string, object>)f)).ToArray();
            }
            foreach (var kv in j.Obj("bodyLines"))
                p.BodyLines[kv.Key] = ((List<object>)kv.Value).ConvertAll(l => List((List<object>)l).ToArray()).ToArray();
        }

        static void ReadNames(Dictionary<string, object> n, PetParts p)
        {
            p.ShapeNames = Named(n.Obj("shapes"));
            p.ManeNames = Named(n.Obj("manes"));
            p.TailNames = Named(n.Obj("tails"));
            p.SkinNames = Named(n.Obj("skins"));
            p.EarKeys = List(n.Arr("ears"));
            p.PatternKeys = List(n.Arr("patterns"));
            p.FaceKeys = List(n.Arr("faces"));
        }

        static Dictionary<string, (string, string)> Named(Dictionary<string, object> o)
        {
            var d = new Dictionary<string, (string, string)>();
            foreach (var kv in o) { var a = (List<object>)kv.Value; d[kv.Key] = ((string)a[0], (string)a[1]); }
            return d;
        }

        // a layer that is the same for every form, or one per form
        static string[] PerForm(Dictionary<string, object> o, string key)
        {
            if (!o.TryGetValue(key, out var v) || v == null) return null;
            return v is string s ? new[] { s, s, s } : List((List<object>)v).ToArray();
        }

        static void ReadStrings(Dictionary<string, object> from, Dictionary<string, string> to)
        {
            if (from == null) return;
            foreach (var kv in from) to[kv.Key] = kv.Value as string ?? "";
        }

        static List<string> List(List<object> a) => a.ConvertAll(v => v as string ?? "");

        static HashSet<string> Strings(List<object> a) => new HashSet<string>(List(a));
    }
}
