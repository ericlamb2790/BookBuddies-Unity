// Ports Server/src/safety.js:1-28 (the website's word lists, cleanText, textProblem, personalDetails). Offline there is no
// blocked_words table, so textProblem uses the built-in lists only (what the Worker does with an empty table).

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Local
{
    /// <summary>Keeps names and chat kind for young readers, with the same rules as the online server.</summary>
    public static class LocalSafety
    {
        // the website's lists, stored in rot13 like safety.js so the source doesn't spell them out
        static readonly string[] BadWords = Rot13("shpx", "shpxre", "shpxva", "shpxvat", "zbgureshpxre", "fuvg", "fuvggl", "ohyyfuvg", "ovgpu", "ovgpurf", "phag", "gjng", "jnaxre", "onfgneq", "nffubyr", "nefrubyr", "qvpxurnq", "cevpx", "chffl", "fyhg", "juber", "qvyqb", "wvmm", "cbea", "xxx", "xlf", "ergneq", "ergneqrq", "fcvp", "puvax", "tbbx", "xvxr", "jrgonpx", "pbba", "cnxv", "enturnq", "gbjryurnq", "genaal", "qlxr", "snt", "snttbg", "avttre", "avttn", "avttref", "avttnf");
        // also caught inside other letters ("xxfuckxx", "f u c k")
        static readonly string[] BadInside = Rot13("zbgureshpxre", "snttbg", "avttre", "avttn", "jrgonpx", "enturnq", "gbjryurnq", "shpx", "fuvg");
        static readonly string[] BadPhrases = Rot13("urvy uvgyre", "juvgr cbjre", "xvyy nyy", "fvrt urvy", "tb xvyy lbhefrys", "xvyy lbhefrys", "juvgr cevqr", "enpr jne").Append("1488").ToArray();

        // JS's \s, \S, \w and \b spelled out (.NET's versions take in more letters, such as İ); Link runs on ASCII-lower-cased
        // text, which is what JS's /i does with these patterns
        const string Space = @"\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
        const string Word = "A-Za-z0-9_";
        const string Edge = "(?:(?<![" + Word + "])(?=[" + Word + "])|(?<=[" + Word + "])(?![" + Word + "]))";
        static readonly Regex Link = new Regex(Edge + "(https?://|www\\.)[^" + Space + "]+|" + Edge + "[" + Word + "-]+\\.(com|net|org|io|gg|co|me|app|tv|xyz|ly|gl)" + Edge + "[^" + Space + "]*");
        static readonly Regex Email = new Regex("[" + Word + ".+-]+@[" + Word + "-]+\\.[" + Word + ".]+");
        static readonly Regex Phone = new Regex("([0-9]([" + Space + "().-])?){7,}");
        static readonly Regex Triples = new Regex(@"(.)\1{2,}");
        static readonly Regex Repeats = new Regex(@"(.)\1+");

        static string[] Rot13(params string[] words) =>
            words.Select(w => new string(w.Select(c => c >= 'a' && c <= 'z' ? (char)((c - 'a' + 13) % 26 + 'a') : c).ToArray())).ToArray();

        /// <summary>Tidies a name or chat line: no control characters or angle brackets, single spaces, at most "max" characters, and no links.</summary>
        public static string CleanText(object v, int max)
        {
            string s = v == null ? "" : Js.Str(v);
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                bool blank = c < ' ' || c == '<' || c == '>' || Js.IsSpace(c);
                if (blank && !space) sb.Append(' ');
                else if (!blank) sb.Append(c);
                space = blank;
            }
            string t = sb.ToString().Trim(' ');
            return RemoveLinks(t.Length > max ? t.Substring(0, max) : t);
        }

        static string RemoveLinks(string t)
        {
            var lower = t.ToCharArray();
            for (int i = 0; i < lower.Length; i++) if (lower[i] >= 'A' && lower[i] <= 'Z') lower[i] = (char)(lower[i] + 32);
            var sb = new StringBuilder();
            int at = 0;
            foreach (Match m in Link.Matches(new string(lower)))
            {
                sb.Append(t, at, m.Index - at).Append("[link removed]");
                at = m.Index + m.Length;
            }
            return sb.Append(t, at, t.Length - at).ToString();
        }

        /// <summary>The first word or phrase that isn't allowed, or null when the text is fine.</summary>
        public static string TextProblem(string txt)
        {
            string raw = (txt ?? "").ToLowerInvariant();
            if (Js.Trim(raw).Length == 0) return null;
            string n = " " + NormText(txt) + " ", squashed = n.Replace(" ", ""), squashedOnce = Dedup(squashed);
            string nd = " " + string.Join(" ", n.Trim(' ').Split(' ').Select(Dedup)) + " ";
            foreach (string w in BadWords)
            {
                string d = Dedup(w);
                if (n.Contains(" " + w + " ") || nd.Contains(" " + d + " ") || nd.Contains(" " + d + "s ") || nd.Contains(" " + d + "es ") || nd.Contains(" " + d + "ed ")) return w;
            }
            foreach (string w in BadInside)
                if (squashed.Contains(w) || (w.Length > 4 && squashedOnce.Contains(Dedup(w)))) return w;
            foreach (string p in BadPhrases)
                if (raw.Contains(p) || n.Contains(" " + p + " ")) return p;
            return null;
        }

        /// <summary>Phone numbers and e-mail addresses are personal details kids shouldn't share in town chat.</summary>
        public static bool PersonalDetails(string t) => Email.IsMatch(t) || Phone.IsMatch(t);

        static string Dedup(string w) => Repeats.Replace(w, "$1");

        // lower-case letters only, accents and look-alike digits folded in ("h3ll0" -> "hello"), runs of 3+ cut to 2
        static string NormText(string x)
        {
            var sb = new StringBuilder();
            foreach (char c in Decompose(x ?? "").ToLowerInvariant())
            {
                if (c >= '\u0300' && c <= '\u036f') continue;
                char l = Leet(c);
                sb.Append(l >= 'a' && l <= 'z' ? l : ' ');
            }
            string letters = Regex.Replace(sb.ToString(), " +", " ");
            return Triples.Replace(letters, "$1$1").Trim(' ');
        }

        static char Leet(char c) => c switch
        {
            '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '8' => 'b', '9' => 'g',
            '@' => 'a', '$' => 's', '!' => 'i', '|' => 'i', '+' => 't', '€' => 'e', _ => c,
        };

        // NFKD like JS normalize(); .NET refuses lone surrogates, which end up as spaces here anyway
        static string Decompose(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool pair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                if (pair) sb.Append(c).Append(s[++i]);
                else sb.Append(char.IsSurrogate(c) ? ' ' : c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormKD);
        }
    }
}
