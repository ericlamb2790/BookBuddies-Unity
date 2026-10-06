using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Builds a pet's SVG exactly the way the website's petSVG() does, layer by layer.
    /// Pure C#: no Unity types, so it can be checked against the site's own output outside Unity.
    /// </summary>
    public static class PetSvg
    {
        const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";
        const string ViewBox = "0 -10 200 232";
        const string Id = "pcX";
        const string Gap = "\n    ";

        /// <summary>mood: "joyful" (default in town), "content" or "sleeping".</summary>
        public static string Build(PetLook pet, PetParts parts, string mood = "joyful")
        {
            string h = Num(pet.Hue);
            int stage = Math.Max(0, Math.Min(parts.Stages.Length - 1, pet.Stage));
            if (stage == 0) return parts.Egg.Replace("{h}", h);

            var shape = parts.Shapes.TryGetValue(pet.Shape, out var sh) ? sh : parts.Shapes["bean"];
            double size = pet.Size >= 0 && pet.Size < parts.Sizes.Length ? parts.Sizes[pet.Size] : parts.Sizes[1];
            var c = new Colors(h, parts.Inner);

            var top = Garment(pet.Wear("dress") ?? pet.Wear("top"), parts);
            var bottom = pet.Wear("dress") != null ? null : Garment(pet.Wear("bottom"), parts);
            string sleeve = top?.Sleeve != null && !parts.Sleeveless.Contains(pet.Wear("top") ?? "") ? top.Sleeve : c.Body;

            int dy = shape.Top - 46, fy = shape.FaceOffset;
            string scale = Num(parts.Stages[stage] * size);
            string neck = pet.Wear("neck");

            var g = new StringBuilder("<g>");
            Line(g, neck != null && parts.BackItems.Contains(neck) ? ItemSvg(neck, parts) : "");
            Line(g, $"<g transform=\"translate(0 {dy})\">{c.Fill(parts.Ears[pet.Ears])}</g>");
            Line(g, c.Fill(shape.Back));
            Line(g, Feet(shape, c));
            Line(g, $"<g class=\"arm-l\">{Arm(shape.Arms[0], shape.Arms[2], 25, sleeve, c)}</g><g class=\"arm-r\">{Arm(shape.Arms[1], shape.Arms[2], -25, sleeve, c)}</g>");
            Line(g, $"<path d=\"{shape.Path}\" fill=\"{c.Body}\"/>");
            Line(g, $"<g clip-path=\"url(#{Id})\">{c.Fill(parts.Patterns[pet.Pattern])}"
                    + (pet.Shiny ? $"<rect class=\"shinyfx\" x=\"0\" y=\"0\" width=\"200\" height=\"220\" fill=\"url(#{Id}g)\" opacity=\".42\"/>" : "")
                    + (bottom?.Body ?? "") + (top?.Body ?? "")
                    + $"<ellipse cx=\"78\" cy=\"{76 + dy}\" rx=\"22\" ry=\"12\" fill=\"#fff\" opacity=\".22\"/></g>");
            Line(g, (bottom?.Over ?? "") + (top?.Over ?? ""));
            Line(g, $"<path d=\"{shape.Path}\" fill=\"none\" stroke=\"{c.Line}\" stroke-width=\"3\"/>");
            string shoes = pet.Wear("shoes");
            Line(g, shape.Feet != null && shoes != null && parts.Items.ContainsKey(shoes) ? $"<g transform=\"translate(0 {shape.Feet[2] - 201})\">{ItemSvg(shoes, parts)}</g>" : "");
            Line(g, $"<g transform=\"translate(0 {dy})\">{(parts.Horns.TryGetValue(pet.Ears, out var horns) ? horns : "")}{(pet.Wear("hat") != null ? "" : parts.Sprouts[stage])}</g>");
            Line(g, $"<g transform=\"translate(0 {fy})\">{Face(pet, mood, parts, h)}{shape.Front}{(pet.Pattern == "freckles" ? c.Fill(parts.Freckles) : "")}{ItemSvg(pet.Wear("eye"), parts)}</g>");
            Line(g, neck != null && !parts.BackItems.Contains(neck) ? ItemSvg(neck, parts) : "");
            string hand = ItemSvg(pet.Wear("hand"), parts);
            Line(g, hand.Length > 0 ? $"<g class=\"held\">{hand}</g>" : "");
            Line(g, $"<g transform=\"translate(0 {dy})\">{ItemSvg(pet.Wear("hat"), parts)}</g>");
            Line(g, ""); // the site's "dirty" stink cloud never shows in town
            Line(g, pet.Shiny ? parts.ShinyStars : "");
            Line(g, stage == 5 ? parts.LegendStars : "");
            g.Append("\n  </g>");
            string body = g.ToString().Replace("var(--pb)", c.Body);

            string shinyGradient = pet.Shiny
                ? $"<linearGradient id=\"{Id}g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#ff8cc6\"/><stop offset=\".25\" stop-color=\"#ffd43b\"/><stop offset=\".5\" stop-color=\"#69db7c\"/><stop offset=\".75\" stop-color=\"#4dabf7\"/><stop offset=\"1\" stop-color=\"#b197fc\"/></linearGradient>"
                : "";
            return $"<svg {Ns} class=\"petsvg {mood} f-{pet.Face} \" viewBox=\"{ViewBox}\" aria-hidden=\"true\"><defs><clipPath id=\"{Id}\"><path d=\"{shape.Path}\"/></clipPath>{shinyGradient}</defs>"
                 + $"{Gap}<g transform=\"translate(100 212) scale({scale}) translate(-100 -212)\"><g class=\"pb\">{body}</g></g></svg>";
        }

        /// <summary>
        /// Makes the SVG friendly to Unity's vector importer: hsl() colours become hex and text is dropped.
        /// </summary>
        public static string ForUnity(string svg)
        {
            svg = Regex.Replace(svg, @"hsl\(([-\d.]+) ([\d.]+)% ([\d.]+)%\)", m =>
                HslToHex(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                         double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100,
                         double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 100));
            svg = Regex.Replace(svg, @"<text[^>]*>.*?</text>", "");
            return svg.Replace("fill=\"#0003\"", "fill=\"#000\" fill-opacity=\".2\"");
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

        // ---- pieces ----

        sealed class Colors
        {
            public readonly string Body, Shade, Line, Light, Inner;
            public Colors(string h, string inner)
            {
                Body = $"hsl({h} 72% 74%)"; Shade = $"hsl({h} 55% 60%)"; Line = $"hsl({h} 38% 36%)"; Light = $"hsl({h} 85% 88%)"; Inner = inner;
            }
            public string Fill(string template) => string.IsNullOrEmpty(template) ? "" :
                template.Replace("{pb}", Body).Replace("{shade}", Shade).Replace("{line}", Line).Replace("{light}", Light).Replace("{inner}", Inner);
        }

        static void Line(StringBuilder g, string content) => g.Append(Gap).Append(content);

        static PetParts.Item Garment(string key, PetParts parts) =>
            key != null && parts.Items.TryGetValue(key, out var item) ? item : null;

        static string ItemSvg(string key, PetParts parts) =>
            key != null && parts.Items.TryGetValue(key, out var item) ? item.Svg ?? "[object Object]" : "";

        static string Arm(int x, int y, int angle, string fill, Colors c) =>
            $"<ellipse cx=\"{x}\" cy=\"{y}\" rx=\"11\" ry=\"17\" fill=\"{fill}\" stroke=\"{c.Line}\" stroke-width=\"2.5\" transform=\"rotate({angle} {x} {y})\"/>";

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

        /// <summary>Formats numbers the way JavaScript prints them (32, 0.62, -4).</summary>
        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
