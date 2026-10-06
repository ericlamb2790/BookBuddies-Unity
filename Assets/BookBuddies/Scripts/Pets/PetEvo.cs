using System;
using System.Collections.Generic;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Evolution, as on the website (build 532+): every pet has three forms, and its body, mane, tail, fur and personality
    /// lean it toward Might, Toughness, Vigor or Agility. Each form raises those strengths in Tales (evoPoints, evoBuffsAt)
    /// and the strongest one picks the pet's accent colour (EVO_ACC).
    /// </summary>
    public sealed class PetEvo
    {
        /// <summary>The four strengths in the site's order: key, icon, name, what it raises in battle.</summary>
        public readonly List<(string key, string icon, string name, string stat)> Attrs = new List<(string, string, string, string)>();
        public readonly Dictionary<string, string> Accent = new Dictionary<string, string>();
        public int[] Xp = new int[0];    // pet XP needed for each form
        public int Cap;                  // the most any strength gets, in %
        double[] pct = new double[0], all = new double[0];
        readonly Dictionary<string, Dictionary<string, double>> bodyAff = new Dictionary<string, Dictionary<string, double>>(),
            partAff = new Dictionary<string, Dictionary<string, double>>(), persAff = new Dictionary<string, Dictionary<string, double>>();
        readonly Dictionary<string, string[]> names = new Dictionary<string, string[]>(), feats = new Dictionary<string, string[]>();

        /// <summary>evoPoints: how many affinity points the look (and personality, if any) gives each strength.</summary>
        public Dictionary<string, double> Points(PetLook o, string personality, PetParts parts)
        {
            var p = new Dictionary<string, double>();
            foreach (var a in Attrs) p[a.key] = 0;
            string sh = parts.Shapes.ContainsKey(o.Shape) ? o.Shape : "bean";
            Add(p, bodyAff.TryGetValue(sh, out var b) ? b : bodyAff["bean"]);
            Add(p, Part("sk:" + o.Skin));
            Add(p, Part("mn:" + o.Mane));
            Add(p, Part("tl:" + (o.Tail != "none" ? o.Tail : parts.Shapes[sh].Tail ?? "undefined")));
            if (personality != null) Add(p, persAff.TryGetValue(personality, out var q) ? q : null);
            return p;
        }

        /// <summary>evoTop: the strongest strength (the first one wins ties).</summary>
        public string Top(Dictionary<string, double> points)
        {
            string best = Attrs[0].key;
            foreach (var a in Attrs) if (points[a.key] > points[best]) best = a.key;
            return best;
        }

        /// <summary>evoBuffsAt: % added to each strength at form 0-3 (0 = an egg, which gets nothing).</summary>
        public Dictionary<string, double> BuffsAt(Dictionary<string, double> points, int evo)
        {
            var r = new Dictionary<string, double>();
            foreach (var a in Attrs) r[a.key] = Math.Min(Cap, points[a.key] * pct[evo] + all[evo]);
            return r;
        }

        /// <summary>The accent colour a pet's evolution art uses (from its strongest strength, no personality).</summary>
        public string AccentOf(PetLook fitted, PetParts parts) => Accent[Top(Points(fitted, null, parts))];

        /// <summary>evoName: the body's name at form 1-3 ("Kitten", "Prowler", "Sabercat"; or "Little Bean", "Bean", "Mighty Bean").</summary>
        public string NameOf(string body, int evo, PetParts parts)
        {
            int e = Math.Max(1, Math.Min(3, evo));
            if (names.TryGetValue(body ?? "", out var n)) return n[e - 1];
            string b = parts.ShapeNames.TryGetValue(body ?? "", out var s) ? s.name : parts.ShapeNames["bean"].name;
            return e == 1 ? "Little " + b : e == 2 ? b : "Mighty " + b;
        }

        /// <summary>What the body gains at form 1-3 ("Where it all starts", "Fish scarf & tabby marks"...), or "".</summary>
        public string FeatOf(string body, int evo) => feats.TryGetValue(body ?? "", out var f) && evo >= 1 && evo <= f.Length ? f[evo - 1] : "";

        Dictionary<string, double> Part(string key) => partAff.TryGetValue(key, out var a) ? a : null;

        static void Add(Dictionary<string, double> p, Dictionary<string, double> a)
        {
            if (a == null) return;
            foreach (var kv in a) p[kv.Key] = (p.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
        }

        internal static PetEvo FromJson(Dictionary<string, object> j)
        {
            var e = new PetEvo { Xp = j.Ints("xp"), Cap = j.Int("cap"), pct = j.Doubles("pct"), all = j.Doubles("all") };
            foreach (List<object> a in j.Arr("attrs")) e.Attrs.Add(((string)a[0], (string)a[1], (string)a[2], (string)a[3]));
            foreach (var kv in j.Obj("acc")) e.Accent[kv.Key] = (string)kv.Value;
            Affinities(j.Obj("bodyAff"), e.bodyAff);
            Affinities(j.Obj("partAff"), e.partAff);
            Affinities(j.Obj("persAff"), e.persAff);
            foreach (var kv in j.Obj("names")) e.names[kv.Key] = ((List<object>)kv.Value).ConvertAll(v => (string)v).ToArray();
            foreach (var kv in j.Obj("feats")) e.feats[kv.Key] = ((List<object>)kv.Value).ConvertAll(v => (string)v).ToArray();
            return e;
        }

        static void Affinities(Dictionary<string, object> from, Dictionary<string, Dictionary<string, double>> to)
        {
            foreach (var kv in from)
            {
                var a = new Dictionary<string, double>();
                foreach (var x in (Dictionary<string, object>)kv.Value) a[x.Key] = (double)x.Value;
                to[kv.Key] = a;
            }
        }
    }
}
