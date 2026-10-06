using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Which parts suit which body (petFits / petFit, build 542) and the random DNA a hatch or reroll gives a pet
    /// (petRollLook), plus a name for each new pet (petNameRoll). Animals keep their own ears and coats, plants and objects
    /// stay clean, tails match the body. A saved look is never rewritten: a part that doesn't suit the body just isn't drawn.
    /// </summary>
    public sealed class PetDna
    {
        static readonly string[] FitParts = { "e", "mn", "tl", "sk" };
        readonly Dictionary<string, HashSet<string>> kinds = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, Dictionary<string, HashSet<string>>> rules = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        int[] hues = new int[0], sizes = new int[0];
        readonly List<(string face, double weight)> faceWeights = new List<(string, double)>();
        List<string> names, titles, syllableA, syllableB, syllableC; // petNameRoll's lists
        Regex rude;
        PetParts parts;

        /// <summary>"beast", "thing" or "blob" (petKind).</summary>
        public string KindOf(string body)
        {
            foreach (var k in kinds) if (k.Value.Contains(body)) return k.Key;
            return "blob";
        }

        /// <summary>petFits: whether part k ("e", "mn", "tl" or "sk") with value v suits the body. "none" always does.</summary>
        public bool Fits(string body, string k, string v)
        {
            if (string.IsNullOrEmpty(v) || v == "none") return true;
            var allowed = Rule(body, k) ?? Rule(KindOf(body), k);
            return allowed == null || allowed.Contains(v);
        }

        /// <summary>petFit: the look with parts that don't suit its body taken off (a copy; the look itself is unchanged).</summary>
        public PetLook Fit(PetLook look)
        {
            if (Array.TrueForAll(FitParts, k => Fits(look.Shape, k, look.Part(k)))) return look;
            var q = look.Copy();
            foreach (var k in FitParts) if (!Fits(look.Shape, k, look.Part(k))) q.SetPart(k, "none");
            return q;
        }

        /// <summary>
        /// petRollLook: a random body first, then only parts that suit it. Returns the site's look keys
        /// (sh, z, f, e, pt, mn, tl, sk) and the rolled hue. next() gives 0-1 like Math.random.
        /// </summary>
        public (Dictionary<string, object> look, int hue) Roll(Func<double> next)
        {
            var bodies = new List<string>(parts.Shapes.Keys);
            string sh = Pick(bodies, next);
            bool beast = KindOf(sh) == "beast";
            var look = new Dictionary<string, object> { ["sh"] = sh };
            look["z"] = (double)sizes[Index(sizes.Length, next)];
            look["f"] = Face(next);
            look["e"] = Part(sh, "e", parts.EarKeys, beast ? .2 : .65, next);
            look["pt"] = next() < .55 ? Pick(parts.PatternKeys.GetRange(1, parts.PatternKeys.Count - 1), next) : "none";
            look["mn"] = Part(sh, "mn", new List<string>(parts.ManeNames.Keys), .2, next);
            look["tl"] = Part(sh, "tl", new List<string>(parts.TailNames.Keys), .3, next);
            look["sk"] = Part(sh, "sk", new List<string>(parts.SkinNames.Keys), beast ? .4 : .3, next);
            int hue = next() < .7 ? hues[Index(hues.Length, next)] : (int)Math.Floor(next() * 360);
            return (look, hue);
        }

        /// <summary>
        /// A brand-new pet's look JSON: rolled DNA in the given hue (an egg's colour carries into its pet), stage s,
        /// form 1, and a 1 in 64 chance to be shiny, like the site's newPet.
        /// </summary>
        public string NewLook(Func<double> next, int? hue = null, int stage = 2, bool? shiny = null)
        {
            var (look, rolled) = Roll(next);
            var o = new Dictionary<string, object> { ["h"] = (double)(hue ?? rolled), ["s"] = (double)stage, ["o"] = new Dictionary<string, object>() };
            foreach (var kv in look) o[kv.Key] = kv.Value;
            o["ev"] = 1.0;
            if (shiny ?? next() < 1 / 64.0) o["sy"] = 1.0;
            return Json.Write(o);
        }

        /// <summary>The site's DNA reroll on an existing look: new body, parts and colour; outfit, stage, form and shine stay.</summary>
        public string Reroll(string lookJson, Func<double> next)
        {
            var o = Json.ParseObject(string.IsNullOrEmpty(lookJson) ? "{}" : lookJson) ?? new Dictionary<string, object>();
            var (look, hue) = Roll(next);
            foreach (var kv in look) o[kv.Key] = kv.Value;
            o["h"] = (double)hue;
            return Json.Write(o);
        }

        /// <summary>
        /// petNameRoll: a name for a new pet, usually from the site's list (sometimes with a title, "Sir Pip"), sometimes
        /// made up from three syllables. Up to 40 tries for one that isn't in taken (lower case).
        /// </summary>
        public string RollName(Func<double> next, ICollection<string> taken = null)
        {
            string name = "Pip";
            for (int t = 0; t < 40; t++)
            {
                double x = next();
                if (x < .22)
                {
                    name = Pick(syllableA, next) + Pick(syllableB, next) + Pick(syllableC, next);
                    if (rude.IsMatch(name)) continue;
                }
                else
                {
                    name = Pick(names, next);
                    if (x > .84)
                    {
                        string titled = Pick(titles, next) + " " + name;
                        if (titled.Length <= 14) name = titled;
                    }
                }
                if (taken == null || !taken.Contains(name.ToLowerInvariant())) break;
            }
            return name;
        }

        // fp(): a random part that suits the body, with chance c, else "none" (no roll is used when nothing suits)
        string Part(string body, string k, List<string> all, double chance, Func<double> next)
        {
            var ok = all.FindAll(v => v != "none" && Fits(body, k, v));
            return ok.Count > 0 && next() < chance ? Pick(ok, next) : "none";
        }

        // wpick(FACE_W)
        string Face(Func<double> next)
        {
            double total = 0;
            foreach (var f in faceWeights) total += f.weight;
            double x = next() * total;
            foreach (var f in faceWeights) { x -= f.weight; if (x < 0) return f.face; }
            return faceWeights[0].face;
        }

        HashSet<string> Rule(string key, string part) =>
            rules.TryGetValue(key, out var r) && r.TryGetValue(part, out var a) ? a : null;

        static string Pick(List<string> list, Func<double> next) => list[Index(list.Count, next)];
        static int Index(int n, Func<double> next) => (int)Math.Floor(next() * n);

        internal static PetDna FromJson(Dictionary<string, object> fit, Dictionary<string, object> roll, Dictionary<string, object> petNames, PetParts parts)
        {
            var d = new PetDna { parts = parts, hues = roll.Ints("hues"), sizes = roll.Ints("sizes") };
            List<string> Strs(string key) => petNames.Arr(key).ConvertAll(v => (string)v);
            d.names = Strs("pool");
            d.titles = Strs("titles");
            d.syllableA = Strs("a");
            d.syllableB = Strs("b");
            d.syllableC = Strs("c");
            d.rude = new Regex(petNames.Str("no", "^$"), RegexOptions.IgnoreCase);
            foreach (var kv in fit.Obj("kinds")) d.kinds[kv.Key] = Set(kv.Value);
            foreach (var kv in fit.Obj("rules"))
            {
                var r = new Dictionary<string, HashSet<string>>();
                foreach (var p in (Dictionary<string, object>)kv.Value) r[p.Key] = Set(p.Value);
                d.rules[kv.Key] = r;
            }
            foreach (var kv in roll.Obj("faceWeights"))
                if (parts.Faces.ContainsKey(kv.Key)) d.faceWeights.Add((kv.Key, (double)kv.Value));
            return d;
        }

        static HashSet<string> Set(object list) => new HashSet<string>(((List<object>)list).ConvertAll(v => (string)v));
    }
}
