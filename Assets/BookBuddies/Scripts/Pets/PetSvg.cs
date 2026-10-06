using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Builds a pet's SVG exactly the way the website's petSVG() does (build 551), layer by layer: aura, mane, ears, tail,
    /// fur, arms, body, clipped marks, outfit, evolution add-ons, face and sparkles.
    /// Pure C#: no Unity types, so it can be checked against the site's own output outside Unity.
    /// </summary>
    public static class PetSvg
    {
        const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";
        const string ViewBox = "0 -10 200 232";
        const string Id = "pcX";
        const string Gap = "\n    ";
        const string Ink = "#2b2230", Leaf = "#59c26a", LeafLine = "#2f7d3b";
        static readonly double[] Grow = { 1, 1, 1.1, 1.18 };       // mane and tail growth per form
        static readonly double[] TailFrame = { 1, 1, 1.04, 1.08 }; // how much each form enlarges the whole pet (for the tail's reach)
        static readonly string[] OwnBrows = { "angry", "cool", "creepy", "derp" };

        /// <summary>mood: "joyful" (default in town), "content" or "sleeping".</summary>
        public static string Build(PetLook look, PetParts parts, string mood = "joyful")
        {
            string h = Num(look.Hue);
            int stage = Math.Max(0, Math.Min(parts.Stages.Length - 1, look.Stage));
            if (stage == 0) return parts.Egg.Replace("{h}", h);

            var x = new Pet(parts.Dna.Fit(look), parts, stage, h);
            string body = Body(x, mood).Replace("var(--pb)", x.C.Body);
            string scale = Fixed(parts.Stages[stage] * x.Size, 3);
            return $"<svg {Ns} class=\"petsvg {mood} f-{x.Look.Face} \" viewBox=\"{ViewBox}\" aria-hidden=\"true\"><defs><clipPath id=\"{Id}\"><path d=\"{x.Sh.Path}\"/></clipPath>{Gradients(x)}</defs>"
                 + $"{Gap}<g transform=\"translate(100 212) scale({scale} {scale}) translate(-100 -212)\"><g class=\"pb\">{Grown(x, body)}</g></g></svg>";
        }

        // ---- the pet being drawn ----

        /// <summary>One pet's fitted look, body, form, colours and evolution add-ons.</summary>
        sealed class Pet
        {
            public readonly PetLook Look;
            public readonly PetParts Parts;
            public readonly PetParts.Shape Sh;
            public readonly PetParts.EvoForm Art;
            public readonly Colors C;
            public readonly int Stage, Evo, Dy;
            public readonly double Size;
            public readonly bool Hat, Fur, Fluffy;

            public Pet(PetLook look, PetParts parts, int stage, string h)
            {
                Look = look;
                Parts = parts;
                Stage = stage;
                Sh = parts.ShapeOf(look.Shape);
                Evo = look.Evo;
                Size = look.Size >= 0 && look.Size < parts.Sizes.Length ? parts.Sizes[look.Size] : parts.Sizes[1];
                Dy = Sh.Top - 46;
                Hat = look.Wear("hat") != null;
                Fur = look.Skin == "furry" || look.Skin == "fluffy";
                Fluffy = look.Skin == "fluffy";
                Art = parts.EvoOf(look.Shape, Evo);
                C = new Colors(h, look.Hue, parts.Inner, parts.Evolution.AccentOf(look, parts));
            }

            /// <summary>An evolution layer (back, behind, mark, over, head, face, front) in this pet's colours.</summary>
            public string Layer(string name) => C.Fill(Art.Layer(name, Hat, Look.Tail != "none"));

            public string ForForm(string[] perForm) => C.Fill(PetParts.Shape.ForForm(perForm, Evo));

            public string Item(string slot) => ItemSvg(Look.Wear(slot), Parts);
        }

        // the <g> holding every layer, one line each like the site's template
        static string Body(Pet x, string mood)
        {
            var sh = x.Sh;
            var c = x.C;
            var parts = x.Parts;
            string neck = x.Look.Wear("neck");
            bool backNeck = neck != null && parts.BackItems.Contains(neck);
            var top = Garment(x.Look.Wear("dress") ?? x.Look.Wear("top"), parts);
            var bottom = x.Look.Wear("dress") != null ? null : Garment(x.Look.Wear("bottom"), parts);

            var g = new StringBuilder("<g>");
            Line(g, Aura(x) + x.Layer("back"));
            Line(g, (backNeck ? x.Item("neck") : "") + Mane(x, 0) + Crest(x));
            Line(g, $"<g transform=\"translate(0 {x.Dy})\">{c.Fill(parts.Ears[x.Look.Ears])}</g>");
            Line(g, (x.Art.Has("noBack") ? "" : c.Fill(sh.Back)) + x.Layer("behind") + Tail(x));
            Line(g, FurCoat(x));
            Line(g, Feet(sh, c));
            Line(g, $"<g class=\"arm-l\">{Arm(x, top, 0)}</g><g class=\"arm-r\">{Arm(x, top, 1)}</g>");
            Line(g, $"<path d=\"{sh.Path}\" fill=\"{c.Body}\"/>");
            Line(g, Clipped(x, top, bottom));
            Line(g, (bottom?.Over ?? "") + (top?.Over ?? ""));
            Line(g, (x.Fur ? "" : $"<path d=\"{sh.Path}\" fill=\"none\" stroke=\"{c.Line}\" stroke-width=\"3\"/>") + (x.Art.Has("noOver") ? "" : x.ForForm(sh.Over)) + x.Layer("over"));
            string shoes = x.Look.Wear("shoes");
            Line(g, sh.Feet != null && shoes != null && parts.Items.ContainsKey(shoes) ? $"<g transform=\"translate(0 {sh.Feet[2] - 201})\">{x.Item("shoes")}</g>" : "");
            Line(g, $"<g transform=\"translate(0 {x.Dy})\">{(parts.Horns.TryGetValue(x.Look.Ears, out var horns) ? horns : "")}{Sprout(x)}</g>{x.Layer("head")}");
            Line(g, Mane(x, 1));
            Line(g, $"<g transform=\"translate(0 {sh.FaceOffset})\">{x.ForForm(sh.Under)}{Face(x.Look, mood, parts, c.H)}{x.ForForm(sh.Front)}{EvoFace(x)}{x.Layer("face")}"
                    + $"{(x.Look.Pattern == "freckles" ? c.Fill(parts.Freckles) : "")}{x.Item("eye")}</g>");
            Line(g, neck != null && !backNeck ? x.Item("neck") : "");
            string hand = x.Item("hand");
            Line(g, hand.Length > 0 ? $"<g class=\"held\">{hand}</g>" : "");
            Line(g, $"<g transform=\"translate(0 {x.Dy})\">{x.Item("hat")}</g>");
            Line(g, x.Layer("front")); // the site's "dirty" stink cloud never shows in Unity
            Line(g, x.Look.Shiny ? parts.ShinyStars : "");
            Line(g, x.Stage == 5 ? parts.LegendStars : "");
            g.Append("\n  </g>");
            return g.ToString();
        }

        // the body's own colour, clipped to its outline: pattern, marks, fur tufts, shine, clothes and soft shading
        static string Clipped(Pet x, PetParts.Item top, PetParts.Item bottom) =>
            $"<g clip-path=\"url(#{Id})\">{x.C.Fill(x.Parts.Patterns[x.Look.Pattern])}{x.Layer("mark")}{FurTufts(x)}"
            + (x.Look.Shiny ? $"<rect class=\"shinyfx\" x=\"0\" y=\"0\" width=\"200\" height=\"220\" fill=\"url(#{Id}g)\" opacity=\".42\"/>" : "")
            + (bottom?.Body ?? "") + (top?.Body ?? "")
            + $"<rect x=\"0\" y=\"{x.Sh.Top - 24}\" width=\"200\" height=\"{240 - x.Sh.Top}\" fill=\"url(#{Id}s)\"/>"
            + $"<ellipse cx=\"78\" cy=\"{76 + x.Dy}\" rx=\"22\" ry=\"12\" fill=\"#fff\" opacity=\".22\"/><ellipse cx=\"72\" cy=\"{80 + x.Dy}\" rx=\"9\" ry=\"5\" fill=\"#fff\" opacity=\".3\"/></g>";

        // forms 2 and 3 stand a little taller and wider
        static string Grown(Pet x, string body) =>
            x.Evo < 2 ? body : $"<g transform=\"translate(100 204) scale({(x.Evo >= 3 ? "1.08 1.02" : "1.04 1")}) translate(-100 -204)\">{body}</g>";

        static string Gradients(Pet x) =>
            $"<radialGradient id=\"{Id}s\" cx=\".38\" cy=\".26\" r=\".82\"><stop offset=\".55\" stop-color=\"{x.C.Line}\" stop-opacity=\"0\"/><stop offset=\".86\" stop-color=\"{x.C.Line}\" stop-opacity=\".13\"/><stop offset=\"1\" stop-color=\"{x.C.Line}\" stop-opacity=\".3\"/></radialGradient>"
            + (x.Look.Shiny ? $"<linearGradient id=\"{Id}g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#ff8cc6\"/><stop offset=\".25\" stop-color=\"#ffd43b\"/><stop offset=\".5\" stop-color=\"#69db7c\"/><stop offset=\".75\" stop-color=\"#4dabf7\"/><stop offset=\"1\" stop-color=\"#b197fc\"/></linearGradient>" : "");

        // ---- evolution extras shared by every body ----

        // form 3: a soft glow round the pet
        static string Aura(Pet x)
        {
            if (x.Evo < 3 || x.Art.Has("noAura")) return "";
            string cy = Num((x.Sh.Top + 200) / 2.0 + 4), ry = Num((200 - x.Sh.Top) / 2.0 + 30);
            return $"<ellipse class=\"evoaura\" cx=\"100\" cy=\"{cy}\" rx=\"94\" ry=\"{ry}\" fill=\"{x.C.Acc}\" opacity=\".13\"/>"
                 + $"<ellipse class=\"evoaura\" cx=\"100\" cy=\"{cy}\" rx=\"94\" ry=\"{ry}\" fill=\"none\" stroke=\"{x.C.Acc}\" stroke-width=\"4\" opacity=\".4\"/>";
        }

        // form 3: a three-flame crest behind the head (crestSVG)
        static string Crest(Pet x)
        {
            if (x.Evo < 3 || x.Art.Has("noCrest")) return "";
            int t = x.Sh.Top;
            var s = new StringBuilder();
            foreach (int r in new[] { -26, 0, 26 })
                s.Append($"<path d=\"M100 {t - 30}Q112 {t - 8} 100 {t + 10}Q88 {t - 8} 100 {t - 30}Z\" fill=\"{x.C.Acc}\" stroke=\"{x.C.Line}\" stroke-width=\"2\" transform=\"rotate({r} 100 {t + 16})\"/>");
            return s.ToString();
        }

        // forms 2 and 3: brows, power marks on the cheeks, and fangs (evoFaceSVG)
        static string EvoFace(Pet x)
        {
            int e = x.Evo;
            bool noMarks = x.Art.Has("noMarks"), noFang = x.Art.Has("noFang");
            if (e < 2 || noMarks && noFang) return "";
            var s = new StringBuilder();
            if (!noMarks && Array.IndexOf(OwnBrows, x.Look.Face) < 0)
                s.Append($"<path d=\"M67 {(e >= 3 ? 89 : 91)}L88 {(e >= 3 ? 95 : 96)}M133 {(e >= 3 ? 89 : 91)}L112 {(e >= 3 ? 95 : 96)}\" stroke=\"{Ink}\" stroke-width=\"3.5\" stroke-linecap=\"round\"/>");
            if (!noMarks)
                for (int i = 0; i < (e >= 3 ? 3 : 2); i++)
                    s.Append($"<path d=\"M{46 + i} {113 + i * 7}l11 3M{154 - i} {113 + i * 7}l-11 3\" stroke=\"{x.C.Acc}\" stroke-width=\"3.5\" stroke-linecap=\"round\"/>");
            if (e >= 3 && x.Sh.Fang > 0 && !noFang)
                s.Append(x.Sh.Fang == 2
                    ? $"<path d=\"M91 127L94 137L97 128ZM103 128L106 137L109 127Z\" fill=\"#fff\" stroke=\"{Ink}\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/>"
                    : $"<path d=\"M105 128L108 135L111 128Z\" fill=\"#fff\" stroke=\"{Ink}\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/>");
            return s.ToString();
        }

        // ---- parts ----

        // a mane behind the head (layer 0) or in front of it (layer 1), grown with the form
        static string Mane(Pet x, int layer)
        {
            int hy = x.Sh.Top + 50;
            string art = x.Parts.Manes.TryGetValue(x.Look.Mane, out var byHeight) && byHeight.TryGetValue(hy, out var m) ? x.C.Fill(m[layer]) : "";
            return $"<g transform=\"translate(100 {hy}) scale({Num(Grow[x.Evo])}) translate(-100 {-hy})\">{art}</g>";
        }

        // the chosen tail, or the body's own, wagging, sized so it never reaches past the frame
        static string Tail(Pet x)
        {
            string tk = x.Look.Tail != "none" ? x.Look.Tail : x.Sh.Tail;
            if (tk == null) return "";
            string one = x.C.Fill(x.Parts.Tails.TryGetValue(tk, out var t) ? t : "");
            string wag = $"<animateTransform attributeName=\"transform\" type=\"rotate\" values=\"-7;7;-7\" dur=\"{(tk == "pup" ? "1.1" : "2.6")}s\" repeatCount=\"indefinite\"/>";
            int ax = x.Sh.Arms[1] - 14;
            double frame = x.Parts.Stages[x.Stage] * x.Size * TailFrame[x.Evo];
            int reach = x.Parts.TailReach.TryGetValue(tk, out var r) && r != 0 ? r : 60;
            double k = Math.Max(.5, Math.Min(Grow[x.Evo], (96 / frame + 100 - ax) / (reach + 6)));
            var s = new StringBuilder($"<g class=\"ptail\" transform=\"translate({ax} {(x.Sh.Feet != null ? x.Sh.Feet[2] - 26 : 176)}) scale({Fixed(k, 3)})\">");
            foreach (int turn in x.Evo >= 3 && x.Look.Shape == "fox" && tk == "fox" ? new[] { -36, -14, 8 } : new[] { 0 })
                s.Append($"<g transform=\"rotate({turn})\"><g>{wag}{one}</g></g>");
            return s.Append("</g>").ToString();
        }

        // furry and fluffy skins grow a coat round the outline (or a dotted edge if the body has none)
        static string FurCoat(Pet x)
        {
            if (!x.Fur) return "";
            string coat = x.Parts.Furs.TryGetValue(x.Look.Shape, out var fur) ? fur.Coat[x.Fluffy ? 1 : 0] : "";
            return coat.Length > 0
                ? $"<path d=\"{coat}\" fill=\"{x.C.Body}\" stroke=\"{x.C.Line}\" stroke-width=\"2.5\" stroke-linejoin=\"round\"/>"
                : $"<path d=\"{x.Sh.Path}\" fill=\"none\" stroke=\"{x.C.Line}\" stroke-width=\"{(x.Fluffy ? 18 : 14)}\" stroke-dasharray=\"0 {(x.Fluffy ? 11 : 8)}\" stroke-linecap=\"round\"/>";
        }

        static string FurTufts(Pet x) =>
            x.Fur && x.Parts.Furs.TryGetValue(x.Look.Shape, out var fur) ? x.C.Fill(fur.Inner[x.Fluffy ? 1 : 0]) : "";

        // the body's own arms, leaves for plants, or round arms in the top's sleeve colour (with wristbands at form 3)
        static string Arm(Pet x, PetParts.Item top, int side)
        {
            var sh = x.Sh;
            if (sh.ArmLeft != null) return x.ForForm(side == 0 ? sh.ArmLeft : sh.ArmRight);
            int ax = sh.Arms[side], ay = sh.Arms[2], r = side == 0 ? 25 : -25;
            string turn = $"transform=\"rotate({r} {ax} {ay})\"";
            if (sh.Leaf) return $"<path d=\"M{ax} {ay - 17}Q{ax + 15} {ay} {ax} {ay + 19}Q{ax - 15} {ay} {ax} {ay - 17}Z\" fill=\"{Leaf}\" stroke=\"{LeafLine}\" stroke-width=\"2.5\" {turn}/>";
            string sleeve = top?.Sleeve != null && !x.Parts.Sleeveless.Contains(x.Look.Wear("top") ?? "") ? top.Sleeve : x.C.Body;
            string arm = $"<ellipse cx=\"{ax}\" cy=\"{ay}\" rx=\"11\" ry=\"17\" fill=\"{sleeve}\" stroke=\"{x.C.Line}\" stroke-width=\"2.5\" {turn}/>";
            return x.Evo >= 3 && !x.Art.Has("noBands")
                ? arm + $"<rect x=\"{ax - 11}\" y=\"{ay + 3}\" width=\"22\" height=\"6\" rx=\"2\" fill=\"{x.C.Acc}\" stroke=\"{x.C.Line}\" stroke-width=\"1.5\" {turn}/>"
                : arm;
        }

        static string Sprout(Pet x) => x.Hat || x.Art.Has("noSprout") || x.Sh.NoSprout ? "" : x.Parts.Sprouts[x.Stage];

        static string Feet(PetParts.Shape s, Colors c)
        {
            if (s.Feet == null) return "";
            string Foot(int x) => $"<ellipse cx=\"{x}\" cy=\"{s.Feet[2]}\" rx=\"16\" ry=\"8\" fill=\"{c.Shade}\" stroke=\"{c.Line}\" stroke-width=\"2.5\"/>";
            return Foot(s.Feet[0]) + Foot(s.Feet[1]);
        }

        static string Face(PetLook pet, string mood, PetParts parts, string h)
        {
            var moods = parts.Faces.TryGetValue(pet.Face, out var m) ? m : parts.Faces["cute"];
            return (moods.TryGetValue(mood, out var face) ? face : moods["joyful"]).Replace("{h}", h);
        }

        static PetParts.Item Garment(string key, PetParts parts) =>
            key != null && parts.Items.TryGetValue(key, out var item) ? item : null;

        static string ItemSvg(string key, PetParts parts) =>
            key != null && parts.Items.TryGetValue(key, out var item) ? item.Svg ?? "[object Object]" : "";

        static void Line(StringBuilder g, string content) => g.Append(Gap).Append(content);

        sealed class Colors
        {
            public readonly string H, Body, Shade, Line, Light, Inner, Acc, Mane;
            public Colors(string h, double hue, string inner, string acc)
            {
                H = h;
                Body = $"hsl({h} 72% 74%)"; Shade = $"hsl({h} 55% 60%)"; Line = $"hsl({h} 38% 36%)"; Light = $"hsl({h} 85% 88%)";
                Mane = $"hsl({Num((hue + 25) % 360)} 62% 60%)";
                Inner = inner; Acc = acc;
            }

            public string Fill(string template) => string.IsNullOrEmpty(template) ? "" :
                template.Replace("{pb}", Body).Replace("{shade}", Shade).Replace("{line}", Line).Replace("{light}", Light)
                        .Replace("{inner}", Inner).Replace("{acc}", Acc).Replace("{mc}", Mane).Replace("{h}", H);
        }

        // ---- numbers, the way JavaScript prints them ----

        /// <summary>Formats numbers the way JavaScript prints them (32, 0.62, -4).</summary>
        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// JavaScript's +v.toFixed(digits): rounds the exact binary value half away from zero, then prints it
        /// without trailing zeros (0.5208 → "0.521", 0.98 → "0.98", 1 → "1").
        /// </summary>
        public static string Fixed(double v, int digits)
        {
            bool negative = v < 0;
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(v));
            int exp = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mantissa |= 1L << 52;
            exp -= 1075; // |v| = mantissa * 2^exp
            BigInteger scaled = mantissa * BigInteger.Pow(10, digits), q;
            if (exp >= 0) q = scaled << exp;
            else
            {
                var den = BigInteger.One << -exp;
                q = BigInteger.DivRem(scaled, den, out var rem);
                if (rem * 2 >= den) q += 1;
            }
            string s = q.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
            string whole = s.Substring(0, s.Length - digits), frac = s.Substring(s.Length - digits).TrimEnd('0');
            string text = frac.Length > 0 ? whole + "." + frac : whole;
            return negative && text != "0" ? "-" + text : text;
        }

        // ---- Unity ----

        /// <summary>
        /// Makes the SVG friendly to Unity's vector importer: hsl() colours become hex, colours with alpha (#fff6, #4dabf799)
        /// become a colour plus an opacity, repeated attributes keep the first one (as browsers do), and text and
        /// SMIL animations (tail wags, flickers) are dropped.
        /// </summary>
        public static string ForUnity(string svg)
        {
            svg = Regex.Replace(svg, @"hsl\(([-\d.]+) ([\d.]+)% ([\d.]+)%\)", m =>
                HslToHex(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                         double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100,
                         double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 100));
            svg = Regex.Replace(svg, @"<text[^>]*>.*?</text>", "");
            svg = Regex.Replace(svg, @"<animate(Transform)?\b[^>]*/>", "");
            svg = Regex.Replace(svg, @"(fill|stroke|stop-color)=""#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})([0-9a-fA-F]{1,2})""", m =>
            {
                string a = m.Groups[3].Value, attr = m.Groups[1].Value;
                double alpha = a.Length == 1 ? Convert.ToInt32(a, 16) / 15.0 : Convert.ToInt32(a, 16) / 255.0;
                string opacity = attr == "stop-color" ? "stop-opacity" : attr + "-opacity";
                return $"{attr}=\"#{m.Groups[2].Value}\" {opacity}=\"{alpha.ToString("0.###", CultureInfo.InvariantCulture)}\"";
            });
            return Regex.Replace(svg, @"<[a-zA-Z]+(\s[^>]*?)?/?>", m => DropRepeats(m.Value));
        }

        // an element's attributes with any repeat of an earlier name removed
        static string DropRepeats(string tag)
        {
            var seen = new HashSet<string>();
            return Regex.Replace(tag, @"\s([a-zA-Z:-]+)=""[^""]*""", a => seen.Add(a.Groups[1].Value) ? a.Value : "");
        }

        // More cracks for the hatching cinematic, drawn like the site's own crack line.
        static readonly string[] MoreCracks =
        {
            "M84 138L80 156L90 166M110 140L116 158L108 172M74 124L70 108",
            "M98 126L102 104L94 88L104 70M124 128L134 112M142 136L156 146M58 132L46 140",
        };

        /// <summary>
        /// The site hides the egg's crack line until you tap it (CSS), so Unity drops it for level 0,
        /// shows it for level 1, and adds more cracks for levels 2 and 3 while the egg hatches.
        /// </summary>
        public static string EggCracks(string eggSvg, int level)
        {
            if (level <= 0) return Regex.Replace(eggSvg, @"<path class=""crack""[^>]*/>", "");
            var extra = new StringBuilder();
            for (int i = 0; i < Math.Min(level - 1, MoreCracks.Length); i++)
                extra.Append($"<path d=\"{MoreCracks[i]}\" stroke=\"#8a6d4a\" stroke-width=\"3\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            int end = eggSvg.LastIndexOf("</g>", StringComparison.Ordinal);
            return end < 0 ? eggSvg : eggSvg.Insert(end, extra.ToString());
        }

        public static string HslToHex(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double Channel(double t)
            {
                t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < .5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            int R = (int)Math.Round(Channel(h + 1.0 / 3) * 255), G = (int)Math.Round(Channel(h) * 255), B = (int)Math.Round(Channel(h - 1.0 / 3) * 255);
            return $"#{R:x2}{G:x2}{B:x2}";
        }
    }
}
