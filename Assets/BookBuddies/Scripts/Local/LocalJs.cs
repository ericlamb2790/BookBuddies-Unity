// The few JavaScript conversions the Worker's routes lean on (String(v), parseInt, Number, truthiness, Math.round,
// String.prototype.trim, URLSearchParams.get), so the offline server reads a request body exactly like Server/src/*.js.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BookBuddies.Local
{
    /// <summary>JavaScript's conversions over parsed JSON values (string, double, bool, null, lists, objects).</summary>
    static class Js
    {
        static readonly Regex DecimalLiteral = new Regex(@"^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?\z");

        /// <summary>A key of a request body; null when it's missing (JS undefined and null read the same here).</summary>
        public static object Get(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) ? v : null;

        /// <summary>String(v).</summary>
        public static string Str(object v) => v switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => Num(d),
            int i => Num(i),
            long l => Num(l),
            List<object> a => string.Join(",", a.ConvertAll(x => x == null ? "" : Str(x))),
            _ => "[object Object]",
        };

        /// <summary>Whether v counts as true in an if or an ||.</summary>
        public static bool Truthy(object v) => v switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            int i => i != 0,
            long l => l != 0,
            string s => s.Length > 0,
            _ => true,
        };

        /// <summary>parseInt(v, 10): the whole number at the start of String(v), NaN when there's none.</summary>
        public static double ParseInt(object v)
        {
            string s = Str(v);
            int i = 0;
            while (i < s.Length && IsSpace(s[i])) i++;
            bool minus = i < s.Length && s[i] == '-';
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == start) return double.NaN;
            double n = Decimal(s.Substring(start, i - start));
            return minus ? -n : n;
        }

        /// <summary>Number(s) for a string: decimal, Infinity, or 0x/0o/0b; "" is 0 and anything else NaN.</summary>
        public static double Number(string s)
        {
            s = Trim(s);
            if (s.Length == 0) return 0;
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s.Length > 2 && s[0] == '0')
            {
                char p = char.ToLowerInvariant(s[1]);
                int radix = p == 'x' ? 16 : p == 'o' ? 8 : p == 'b' ? 2 : 0;
                if (radix > 0) return Digits(s.Substring(2), radix);
            }
            return DecimalLiteral.IsMatch(s) ? Decimal(s) : double.NaN;
        }

        // the nearest double to a decimal literal (older runtimes throw where JS gives Infinity)
        static double Decimal(string s)
        {
            try { return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity; }
        }

        static double Digits(string s, int radix)
        {
            double n = 0;
            foreach (char c in s)
            {
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 99;
                if (d >= radix) return double.NaN;
                n = n * radix + d;
            }
            return n;
        }

        /// <summary>Number.isInteger.</summary>
        public static bool IsInteger(double d) => !double.IsInfinity(d) && Math.Floor(d) == d;

        /// <summary>Math.round: the nearest whole number, halves up (exact, unlike floor(x + 0.5)).</summary>
        public static double Round(double x)
        {
            double f = Math.Floor(x);
            return x - f >= .5 ? f + 1 : f;
        }

        /// <summary>String(n) for a number: the shortest digits that read back the same, in JS's notation.</summary>
        public static string Num(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
            if (d == 0) return "0";
            string r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
            int e = r.IndexOf('E');
            string mant = e < 0 ? r : r.Substring(0, e);
            int dot = mant.IndexOf('.');
            string digits = dot < 0 ? mant : mant.Remove(dot, 1);
            // the value is 0.<digits> × 10^n
            int n = (dot < 0 ? mant.Length : dot) + (e < 0 ? 0 : int.Parse(r.Substring(e + 1), CultureInfo.InvariantCulture));
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
            digits = digits.Substring(lead).TrimEnd('0');
            n -= lead;
            int k = digits.Length;
            string s;
            if (k <= n && n <= 21) s = digits + new string('0', n - k);
            else if (0 < n && n <= 21) s = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) s = "0." + new string('0', -n) + digits;
            else s = digits.Substring(0, 1) + (k > 1 ? "." + digits.Substring(1) : "") + (n - 1 >= 0 ? "e+" : "e-") + Math.Abs(n - 1);
            return d < 0 ? "-" + s : s;
        }

        /// <summary>JS whitespace and line terminators (what \s, trim and parseInt skip).</summary>
        public static bool IsSpace(char c) =>
            c == ' ' || (c >= '\t' && c <= '\r') || c == '\u00a0' || c == '\u1680' || (c >= '\u2000' && c <= '\u200a') ||
            c == '\u2028' || c == '\u2029' || c == '\u202f' || c == '\u205f' || c == '\u3000' || c == '\ufeff';

        /// <summary>String.prototype.trim.</summary>
        public static string Trim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsSpace(s[a])) a++;
            while (b > a && IsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        /// <summary>URLSearchParams.get: the first value of a query parameter, decoded; null when it's missing.</summary>
        public static string Query(string query, string key)
        {
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                if (Decode(k) == key) return eq < 0 ? "" : Decode(pair.Substring(eq + 1));
            }
            return null;
        }

        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }
}
