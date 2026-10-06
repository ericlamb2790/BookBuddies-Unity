using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Builds a villain's SVG exactly the way the website's villainSVG() does (js/06-tales.js), from the
    /// bodies, costumes and face templates in Data/villains.json. Pure C#: no Unity types, so every
    /// drawing can be checked byte for byte against the site's own output outside Unity.
    /// </summary>
    public static class VillainSvg
    {
        /// <summary>
        /// Text the vector importer can't draw (the foe's emoji, ♥ ❄ ♠ 7). X and Y are its centre as a fraction
        /// of the 100×100 box (Y up from the bottom), Size the font size as a fraction of the box. Color is
        /// the glyph's own fill, or null for an emoji in its own colours. Shadow: the site's soft drop shadow.
        /// </summary>
        public struct TextMark
        {
            public string Text, Color;
            public double X, Y, Size;
            public bool Shadow;
        }

        const string GradientId = "vg1";
        const string GroundShadow = "<ellipse cx=\"50\" cy=\"95\" rx=\"30\" ry=\"5\" fill=\"#0003\"/>";
        const double GlyphRise = .34; // a glyph's visual centre sits about a third of its size above the baseline

        sealed class Dress
        {
            public string[] Back, Front;
            public string Color;
            public bool NoFace;
            public double Opacity;
        }

        sealed class Tables
        {
            public readonly Dictionary<string, string> Bodies = new Dictionary<string, string>(), Faces = new Dictionary<string, string>();
            public readonly Dictionary<string, int> Tops = new Dictionary<string, int>();
            public readonly Dictionary<string, Dress> Dresses = new Dictionary<string, Dress>();
            public readonly Dictionary<string, Dictionary<string, string>> Parts = new Dictionary<string, Dictionary<string, string>>();
            public readonly HashSet<string> BackParts = new HashSet<string>();
            public readonly List<(int lo, int hi)> FaceEmoji = new List<(int, int)>();
        }

        static Tables tables;
        static Tables Data => tables ?? (tables = Load(TalesData.TextLoader("Data/villains")));

        /// <summary>The site's villainSVG(v, boss), character for character (its gradient id is always "vg1").</summary>
        public static string Build(FoeVariant v, bool boss)
        {
            var f = v.Def;
            var dress = DressOf(f.N);
            string c = BodyColor(v), sh = Shape(f), ol = JsMath.Shade(c, -.42);
            int ey = sh == "tall" ? 52 : 60, t = Data.Tops.TryGetValue(sh, out var top) ? top : 27;
            string Fill(string tpl) => tpl.Replace("{c45}", JsMath.Shade(c, .45)).Replace("{d}", JsMath.Shade(c, -.35)).Replace("{c}", c);
            string Part(string k) => k != null && Data.Parts.TryGetValue(k, out var p) && p.TryGetValue(ey + "," + t, out var s) ? Fill(s) : "";
            string Parts(string[] keys) { var sb = new StringBuilder(); foreach (var k in keys) sb.Append(Part(k)); return sb.ToString(); }

            bool book = sh == "book";
            string path = book ? null : Data.Bodies[sh];
            string body = book
                ? $"<rect x=\"20\" y=\"24\" width=\"60\" height=\"68\" rx=\"7\" fill=\"{c}\" stroke=\"{ol}\" stroke-width=\"1.6\"/><rect x=\"20\" y=\"24\" width=\"11\" height=\"68\" rx=\"5\" fill=\"#0003\"/><rect x=\"74\" y=\"28\" width=\"5\" height=\"60\" rx=\"2\" fill=\"#fff9\"/><path d=\"M36 34 h32 M36 40 h24\" stroke=\"#ffffff55\" stroke-width=\"3\" stroke-linecap=\"round\"/>"
                : $"<path d=\"{path}\" fill=\"{c}\" stroke=\"{ol}\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>";
            string shade = book ? $"<rect x=\"20\" y=\"24\" width=\"60\" height=\"68\" rx=\"7\" fill=\"url(#{GradientId})\"/>" : $"<path d=\"{path}\" fill=\"url(#{GradientId})\"/>";
            string wings = sh == "bat"
                ? $"<g class=\"wg\"><path d=\"M24 58 Q2 38 3 66 Q10 60 13 71 Q17 62 23 69Z\" fill=\"{c}\" opacity=\".85\"/></g><g class=\"wg r\"><path d=\"M76 58 Q98 38 97 66 Q90 60 87 71 Q83 62 77 69Z\" fill=\"{c}\" opacity=\".85\"/></g>"
                : "";
            string face = dress != null && dress.NoFace ? "" : Fill(Data.Faces[$"{FaceArch(v)},{ey},{(boss ? 1 : 0)},{(dress != null ? 1 : 0)}"]);
            bool xpBack = v.Xp != null && Data.BackParts.Contains(v.Xp);
            string front = dress != null ? Parts(dress.Front)
                : IsFaceEmoji(f.I) ? ""
                : $"<text x=\"50\" y=\"{(sh == "tall" ? 24 : boss ? 30 : 32)}\" font-size=\"{(boss ? 30 : 26)}\" text-anchor=\"middle\" style=\"filter:drop-shadow(0 2px 1.5px #0006)\">{f.I}</text>";

            return "<svg viewBox=\"0 0 100 100\" xmlns=\"http://www.w3.org/2000/svg\"><defs>"
                 + $"<radialGradient id=\"{GradientId}\" cx=\"34%\" cy=\"22%\" r=\"88%\"><stop offset=\"0\" stop-color=\"#fff\" stop-opacity=\".34\"/><stop offset=\".42\" stop-color=\"#fff\" stop-opacity=\"0\"/><stop offset=\"1\" stop-color=\"#000\" stop-opacity=\".3\"/></radialGradient></defs>"
                 + GroundShadow
                 + (v.Vs != 0 ? $"<g transform=\"translate(50 95) scale({JsMath.Num(v.Vs)}) translate(-50 -95)\">" : "<g>")
                 + (xpBack ? Part(v.Xp) : "") + (dress != null ? Parts(dress.Back) : "") + wings
                 + (dress != null && dress.Opacity != 0 ? $"<g opacity=\"{JsMath.Num(dress.Opacity)}\">" : "<g>")
                 + body + shade + $"<ellipse cx=\"50\" cy=\"{ey + 14}\" rx=\"20\" ry=\"11\" fill=\"#ffffff22\"/></g>\n  "
                 + face + front + (v.Xp != null && !xpBack ? Part(v.Xp) : "") + "</g></svg>";
        }

        /// <summary>tqArch: the face that fits how a villain fights (brute, sly, caster, wee or grump).</summary>
        public static string Arch(FoeVariant v)
        {
            if (!v.HasHp && !v.Def.Hp.HasValue) return "grump"; // the site's v.hp == null
            double hp = v.HasHp ? v.Hp : v.Def.Hp.Value, atk = v.HasHp ? v.Atk : v.Def.Atk ?? 1;
            TalesData.Current.SpecialFx.TryGetValue(v.Sp ?? v.Def.Sp ?? "", out var fx);
            if (fx == "beam" || fx == "rain" || fx == "orbit") return "caster";
            if (hp >= 1.4) return "brute";
            if (hp <= .85) return "wee";
            return (atk == 0 ? 1 : atk) >= 1.1 ? "sly" : "grump";
        }

        /// <summary>The face actually drawn: costumed villains always keep the classic grump.</summary>
        public static string FaceArch(FoeVariant v) => DressOf(v.Def.N) != null ? "grump" : Arch(v);

        /// <summary>The body colour: the variant's tint, else the costume's colour, else the villain's own.</summary>
        public static string BodyColor(FoeVariant v) => v.Tc ?? DressOf(v.Def.N)?.Color ?? v.Def.C ?? "#888";

        /// <summary>The glow a boss gets when its affix brings none (tqVariant's last step).</summary>
        public static string AutoAura(FoeVariant v) => JsMath.Shade(DressOf(v.Def.N)?.Color ?? v.Def.C ?? "#ff6a6a", .25);

        static string Shape(FoeDef f) => f.Sh != null && (Data.Bodies.ContainsKey(f.Sh) || f.Sh == "book") ? f.Sh : "blob";

        static Dress DressOf(string name) => name != null && Data.Dresses.TryGetValue(name, out var d) ? d : null;

        /// <summary>TQ_FACEMOJI: emoji that are faces themselves (the villain would get two faces), so they aren't drawn.</summary>
        static bool IsFaceEmoji(string s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s[i], s[++i]) : s[i];
                foreach (var (lo, hi) in Data.FaceEmoji) if (cp >= lo && cp <= hi) return true;
            }
            return false;
        }

        // ---- making the SVG friendly to Unity's vector importer ----

        static readonly Regex Token = new Regex(@"<[^>]+>|[^<]+");
        static readonly Regex Attribute = new Regex(@"([\w:-]+)=""([^""]*)""");
        static readonly Regex ScaleGroup = new Regex(@"<g transform=""translate\(50 95\) scale\([^)]*\) translate\(-50 -95\)"">");
        static readonly HashSet<string> Shapes = new HashSet<string> { "path", "rect", "circle", "ellipse" };
        static readonly HashSet<string> Dropped = new HashSet<string> { "opacity", "class", "style", "font-size", "text-anchor" };

        /// <summary>What a group passes down to what's inside it (opacity and paint alphas are folded into each shape).</summary>
        sealed class Inherited
        {
            public double Opacity = 1, FillAlpha = 1, StrokeAlpha = 1;
            public string Fill, FontSize, Anchor;
        }

        /// <summary>
        /// Makes a villain SVG drawable by Unity's vector importer: drops the ground shadow (the caller draws one)
        /// and the Giant/Tiny scale (applied at runtime about the feet), turns #rgba/#rrggbbaa colours and opacity
        /// into plain colours with fill/stroke opacity, removes CSS-only bits, and lifts every text element out
        /// as a mark to draw on top.
        /// </summary>
        public static string ForUnity(string svg, out List<TextMark> marks)
        {
            marks = new List<TextMark>();
            svg = ScaleGroup.Replace(svg.Replace(GroundShadow, ""), "<g>");
            var output = new StringBuilder(svg.Length);
            var scopes = new Stack<Inherited>();
            scopes.Push(new Inherited());
            Dictionary<string, string> text = null;
            string content = "";

            foreach (Match m in Token.Matches(svg))
            {
                string tok = m.Value;
                if (tok[0] != '<') { if (text != null) content += tok; else output.Append(tok); continue; }
                if (tok.StartsWith("</"))
                {
                    if (tok == "</text>") { if (text != null) marks.Add(Mark(text, content, scopes.Peek())); text = null; continue; }
                    if (tok == "</g>" && scopes.Count > 1) scopes.Pop();
                    output.Append(tok);
                    continue;
                }
                string name = Regex.Match(tok, @"^<([\w:-]+)").Groups[1].Value;
                bool closed = tok.EndsWith("/>");
                var attrs = new Dictionary<string, string>();
                foreach (Match a in Attribute.Matches(tok)) attrs[a.Groups[1].Value] = a.Groups[2].Value;
                if (name == "text") { text = attrs; content = ""; continue; }
                if (name == "g" && !closed) scopes.Push(Inherit(scopes.Peek(), attrs));
                output.Append(Rewrite(name, attrs, closed, scopes.Peek()));
            }
            return output.ToString();
        }

        static Inherited Inherit(Inherited parent, Dictionary<string, string> attrs) => new Inherited
        {
            Opacity = parent.Opacity * (attrs.TryGetValue("opacity", out var o) ? Number(o) : 1),
            FillAlpha = attrs.ContainsKey("fill") ? Alpha(attrs["fill"]) : parent.FillAlpha,
            StrokeAlpha = attrs.ContainsKey("stroke") ? Alpha(attrs["stroke"]) : parent.StrokeAlpha,
            Fill = attrs.TryGetValue("fill", out var f) ? f : parent.Fill,
            FontSize = attrs.TryGetValue("font-size", out var s) ? s : parent.FontSize,
            Anchor = attrs.TryGetValue("text-anchor", out var a) ? a : parent.Anchor,
        };

        /// <summary>One element, rewritten: plain colours, with the alpha and every opacity above it multiplied into each shape.</summary>
        static string Rewrite(string name, Dictionary<string, string> attrs, bool closed, Inherited scope)
        {
            if (Shapes.Contains(name))
            {
                double fade = scope.Opacity * (attrs.TryGetValue("opacity", out var o) ? Number(o) : 1);
                Paint(attrs, "fill", "fill-opacity", fade * (attrs.ContainsKey("fill") ? Alpha(attrs["fill"]) : scope.FillAlpha));
                Paint(attrs, "stroke", "stroke-opacity", fade * (attrs.ContainsKey("stroke") ? Alpha(attrs["stroke"]) : scope.StrokeAlpha));
            }
            if (name == "stop") Paint(attrs, "stop-color", "stop-opacity", attrs.TryGetValue("stop-color", out var c) ? Alpha(c) : 1);
            if (name.EndsWith("Gradient")) // percentages of the bounding box, written as the plain fractions they mean
                foreach (var key in new List<string>(attrs.Keys))
                    if (attrs[key].EndsWith("%")) attrs[key] = JsMath.Num(Number(attrs[key].TrimEnd('%')) / 100);
            foreach (var key in new[] { "fill", "stroke", "stop-color" }) if (attrs.TryGetValue(key, out var paint)) attrs[key] = Solid(paint);
            var sb = new StringBuilder("<").Append(name);
            foreach (var kv in attrs) if (!Dropped.Contains(kv.Key)) sb.Append(' ').Append(kv.Key).Append("=\"").Append(kv.Value).Append('"');
            return sb.Append(closed ? "/>" : ">").ToString();
        }

        /// <summary>Multiplies a factor into an element's fill/stroke/stop opacity (written only when it isn't 1).</summary>
        static void Paint(Dictionary<string, string> attrs, string colorKey, string opacityKey, double factor)
        {
            if (attrs.TryGetValue(colorKey, out var color) && color == "none") return;
            if (attrs.TryGetValue(opacityKey, out var o)) factor *= Number(o);
            if (factor < .999) attrs[opacityKey] = Math.Round(factor, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }

        static bool HasAlpha(string c) => c.StartsWith("#") && (c.Length == 5 || c.Length == 9);

        /// <summary>The alpha of a #rgba or #rrggbbaa colour, 0..1 (1 for anything else).</summary>
        static double Alpha(string c)
        {
            if (!HasAlpha(c)) return 1;
            string a = c.Length == 5 ? new string(c[4], 2) : c.Substring(7);
            return int.Parse(a, NumberStyles.HexNumber) / 255.0;
        }

        /// <summary>The colour without its alpha digits.</summary>
        static string Solid(string c) => HasAlpha(c) ? c.Substring(0, c.Length == 5 ? 4 : 7) : c;

        /// <summary>A text element as a mark: its visual centre, size, colour and whether it casts the soft shadow.</summary>
        static TextMark Mark(Dictionary<string, string> attrs, string content, Inherited scope)
        {
            double size = Number(attrs.TryGetValue("font-size", out var s) ? s : scope.FontSize ?? "16");
            double x = Number(attrs.TryGetValue("x", out var xs) ? xs : "0"), y = Number(attrs.TryGetValue("y", out var ys) ? ys : "0");
            string anchor = attrs.TryGetValue("text-anchor", out var a) ? a : scope.Anchor ?? "start";
            double half = size * (IsAscii(content) ? .31 : .42); // about half the glyph's width
            double cx = anchor == "middle" ? x : anchor == "end" ? x - half : x + half;
            return new TextMark
            {
                Text = content,
                Color = attrs.TryGetValue("fill", out var fill) ? fill : scope.Fill,
                X = cx / 100,
                Y = 1 - (y - size * GlyphRise) / 100,
                Size = size / 100,
                Shadow = attrs.TryGetValue("style", out var style) && style.Contains("drop-shadow"),
            };
        }

        static bool IsAscii(string s) { foreach (char ch in s) if (ch > 127) return false; return true; }

        static double Number(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // ---- data ----

        static Tables Load(string json)
        {
            var j = Json.ParseObject(json);
            var d = new Tables();
            foreach (var kv in j.Obj("body")) d.Bodies[kv.Key] = (string)kv.Value;
            foreach (var kv in j.Obj("top")) d.Tops[kv.Key] = (int)(double)kv.Value;
            foreach (var kv in j.Obj("faces")) d.Faces[kv.Key] = (string)kv.Value;
            foreach (var p in j.Arr("xback")) d.BackParts.Add((string)p);
            foreach (var kv in j.Obj("parts"))
            {
                var byPlace = new Dictionary<string, string>();
                foreach (var t in (Dictionary<string, object>)kv.Value) byPlace[t.Key] = (string)t.Value;
                d.Parts[kv.Key] = byPlace;
            }
            foreach (var kv in j.Obj("dress"))
            {
                var o = (Dictionary<string, object>)kv.Value;
                d.Dresses[kv.Key] = new Dress
                {
                    Back = TalesData.Strings(o.Arr("b")), Front = TalesData.Strings(o.Arr("f")),
                    Color = o.Str("c", null), NoFace = o.Truthy("nf"), Opacity = o.Num("op"),
                };
            }
            foreach (Match m in Regex.Matches(j.Str("faceEmoji"), @"\\u\{([0-9A-Fa-f]+)\}-\\u\{([0-9A-Fa-f]+)\}"))
                d.FaceEmoji.Add((int.Parse(m.Groups[1].Value, NumberStyles.HexNumber), int.Parse(m.Groups[2].Value, NumberStyles.HexNumber)));
            return d;
        }
    }
}
