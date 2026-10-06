using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>tqVariant and makeFoe: rolls a villain's affix and epithet, and turns a villain into a battle unit.</summary>
    public static class FoeFactory
    {
        /// <summary>A foe's basic hit (every villain has it next to its special).</summary>
        public static readonly MoveDef Hit = MakeHit();

        /// <summary>tqVariant: roll an affix and epithet for a base villain.</summary>
        public static FoeVariant Variant(FoeDef def, bool boss, bool elite, IRng rng = null)
        {
            rng = rng ?? SystemRng.Shared;
            var d = TalesData.Current;
            Affix a = rng.Next() < (boss ? .5 : elite ? .9 : .6) ? d.Affixes[rng.Range(d.Affixes.Count)] : null;
            (string text, double hp, double atk)? e = null;
            if (rng.Next() < (boss ? 1 : elite ? .75 : .25)) e = d.Epithets[rng.Range(d.Epithets.Count)];
            if (a == null && e == null) return Plain(def);

            // "X of Y" names only take "the …" epithets, so nobody ends up "of the Torn Index of Oz"
            if (e != null && def.N.Contains(" of ") && !e.Value.text.StartsWith("the "))
                e = rng.Pick(d.Epithets.FindAll(x => x.text.StartsWith("the ")));

            var v = Plain(def);
            v.Dn = AffixName(def.N, a) + (e == null ? "" : (e.Value.text.StartsWith("the ") ? ", " : " ") + e.Value.text);
            v.Hp = (def.Hp ?? 1) * (a?.Hp ?? 1) * (e?.hp ?? 1);
            v.Atk = (def.Atk ?? 1) * (a?.Atk ?? 1) * (e?.atk ?? 1);
            v.HasHp = true;
            if (a != null) ApplyAffix(v, a, boss, rng);
            if (boss && v.Au == null) v.Au = JsMath.Shade(Dress.Colour(def.N) ?? def.C ?? "#ff6a6a", .25);
            return v;
        }

        /// <summary>A plain variant with no roll (drawing a base villain).</summary>
        public static FoeVariant Plain(FoeDef def) => new FoeVariant
        {
            Def = def, Sp = def.Sp, Sn = def.Sn, Si = def.Si, Hp = def.Hp ?? 1, Atk = def.Atk ?? 1, HasHp = def.Hp.HasValue,
        };

        /// <summary>makeFoe for a wild fight: level, boss/elite scaling and the road's multiplier.</summary>
        public static BattleUnit Make(FoeVariant v, int lvl, bool boss, bool elite, int index, double wildMul, IRng rng = null) =>
            Make(v, lvl, boss, elite, "f" + index, wildMul, rng);

        /// <summary>makeFoe with any unit key (boss summons use "fr" + phase + i). Hp starts full.</summary>
        public static BattleUnit Make(FoeVariant v, double lvl, bool boss, bool elite, string key, double wildMul, IRng rng = null)
        {
            rng = rng ?? SystemRng.Shared;
            double sc = Math.Pow(1.17, lvl - 1) * (boss ? 1 : elite ? 1.35 : 1);
            const double partyScale = 1;   // .75 + .25 * max(1, party size), solo
            var u = new BattleUnit
            {
                Key = key, Name = v.Name, IsFoe = true, Boss = boss, Elite = elite, Gen = v.Def.G, Lvl = lvl, Foe = v,
                Max = JsMath.Round((boss ? 175 : 48) * v.Hp * sc * partyScale),
                Atk = JsMath.Round((boss ? 11 : 8) * v.Atk * sc),
                Def = JsMath.Round((boss ? 5 : 3) * sc * (v.Vdf == 0 ? 1 : v.Vdf)),
                Spd = JsMath.Round((boss ? 11 : 9) + lvl * .2 + rng.Next() * 4 + v.Vsx),
            };
            u.Max = JsMath.Round(u.Max * wildMul);
            u.Atk = JsMath.Round(u.Atk * (1 + (wildMul - 1) * .6));
            u.Hp = u.Max;
            u.Moves.Add(Hit);
            if (v.Sp != null && TalesData.Current.Specials.TryGetValue(v.Sp, out var sp))
            {
                u.Moves.Add(sp);
                u.Cds[sp.Key] = 1;
            }
            return u;
        }

        // "Gilded Dust Bunny", "The Gilded Big Bad Wolf"
        static string AffixName(string n, Affix a) =>
            a == null ? n : n.StartsWith("The ") ? "The " + a.N + " " + n.Substring(4) : a.N + " " + n;

        static void ApplyAffix(FoeVariant v, Affix a, bool boss, IRng rng)
        {
            var d = TalesData.Current;
            v.Ax = a.N;
            if (a.C != null) v.Tc = JsMath.Mix(Dress.Colour(v.Def.N) ?? v.Def.C ?? "#888888", a.C, a.M);
            if (a.Vs != 0) v.Vs = a.Vs;
            if (a.Au != null) v.Au = a.Au;
            if (a.Df != 0) v.Vdf = a.Df;
            if (a.Sx != 0) v.Vsx = a.Sx;
            if (a.Sp != null && d.Specials.ContainsKey(a.Sp) && (!boss || rng.Next() < .5)) { v.Sp = a.Sp; v.Sn = a.Sn; v.Si = a.Si; }
            var extra = Array.FindAll(a.Parts, p => Dress.PartExists(p) && !Dress.Wears(v.Def.N, p));
            if (extra.Length > 0) v.Xp = extra[rng.Range(extra.Length)];
        }

        static MoveDef MakeHit()
        {
            var m = new MoveDef { Key = "hit", Target = "one" };
            m.Num["p"] = 1;
            return m;
        }

        /// <summary>The costume table from villains.json: each dressed villain's colour and parts, and which parts exist.</summary>
        static class Dress
        {
            static Dictionary<string, object> dress;
            static HashSet<string> parts;

            public static string Colour(string name) => Of(name).Str("c", null);
            public static bool PartExists(string part) { Load(); return parts.Contains(part); }
            public static bool Wears(string name, string part) => Of(name).Arr("b").Contains(part) || Of(name).Arr("f").Contains(part);

            static Dictionary<string, object> Of(string name) { Load(); return dress.Obj(name); }

            static void Load()
            {
                if (dress != null) return;
                var j = Json.ParseObject(TalesData.TextLoader("Data/villains"));
                dress = j.Obj("dress") ?? new Dictionary<string, object>();
                parts = new HashSet<string>((j.Obj("parts") ?? new Dictionary<string, object>()).Keys);
            }
        }
    }
}
