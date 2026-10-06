using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BookBuddies
{
    /// <summary>
    /// Small JSON reader/writer with no package needed.
    /// Objects come back as Dictionary&lt;string, object&gt;, arrays as List&lt;object&gt;, numbers as double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text) => new Reader(text).ReadDocument();

        public static Dictionary<string, object> ParseObject(string text) => Parse(text) as Dictionary<string, object>;

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary d:
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry e in d)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, e.Key.ToString());
                        sb.Append(':');
                        WriteValue(sb, e.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                case IFormattable n: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text) { s = text ?? ""; }

            public object ReadDocument()
            {
                SkipSpace();
                var v = ReadValue();
                SkipSpace();
                if (i != s.Length) throw Error("extra text after the value");
                return v;
            }

            object ReadValue()
            {
                if (i >= s.Length) throw Error("unexpected end");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') return Word("true", true);
                if (c == 'f') return Word("false", false);
                if (c == 'n') return Word("null", null);
                return ReadNumber();
            }

            Dictionary<string, object> ReadObject()
            {
                var o = new Dictionary<string, object>();
                i++;
                SkipSpace();
                if (Peek('}')) { i++; return o; }
                while (true)
                {
                    SkipSpace();
                    string key = ReadString();
                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    o[key] = ReadValue();
                    SkipSpace();
                    if (Peek(',')) { i++; continue; }
                    Expect('}');
                    return o;
                }
            }

            List<object> ReadArray()
            {
                var a = new List<object>();
                i++;
                SkipSpace();
                if (Peek(']')) { i++; return a; }
                while (true)
                {
                    SkipSpace();
                    a.Add(ReadValue());
                    SkipSpace();
                    if (Peek(',')) { i++; continue; }
                    Expect(']');
                    return a;
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                throw Error("unclosed string");
            }

            double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw Error("unexpected character '" + s[i] + "'");
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            object Word(string word, object value)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("expected " + word);
                i += word.Length;
                return value;
            }

            void SkipSpace() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            bool Peek(char c) => i < s.Length && s[i] == c;
            void Expect(char c) { if (!Peek(c)) throw Error("expected '" + c + "'"); i++; }
            FormatException Error(string what) => new FormatException("JSON: " + what + " at " + i);
        }
    }

    /// <summary>Forgiving typed reads from parsed JSON objects (missing or wrong-typed keys give the fallback).</summary>
    public static class JsonRead
    {
        public static string Str(this Dictionary<string, object> o, string key, string fallback = "") =>
            o != null && o.TryGetValue(key, out var v) && v != null ? (v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture)) : fallback;

        public static double Num(this Dictionary<string, object> o, string key, double fallback = 0) =>
            o != null && o.TryGetValue(key, out var v) && v is double d ? d : fallback;

        public static int Int(this Dictionary<string, object> o, string key, int fallback = 0) =>
            o != null && o.TryGetValue(key, out var v) && v is double d ? (int)d : fallback;

        public static bool Has(this Dictionary<string, object> o, string key) => o != null && o.ContainsKey(key) && o[key] != null;

        public static bool Truthy(this Dictionary<string, object> o, string key)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) return false;
            if (v is bool b) return b;
            if (v is double d) return d != 0;
            if (v is string s) return s.Length > 0;
            return true;
        }

        public static Dictionary<string, object> Obj(this Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(this Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v is List<object> a ? a : new List<object>();

        public static int[] Ints(this Dictionary<string, object> o, string key)
        {
            var a = o.Arr(key);
            var r = new int[a.Count];
            for (int k = 0; k < a.Count; k++) r[k] = a[k] is double d ? (int)d : 0;
            return r;
        }

        public static double[] Doubles(this Dictionary<string, object> o, string key)
        {
            var a = o.Arr(key);
            var r = new double[a.Count];
            for (int k = 0; k < a.Count; k++) r[k] = a[k] is double d ? d : 0;
            return r;
        }

        public static float[] Floats(this Dictionary<string, object> o, string key)
        {
            var a = o.Arr(key);
            var r = new float[a.Count];
            for (int k = 0; k < a.Count; k++) r[k] = a[k] is double d ? (float)d : 0f;
            return r;
        }
    }
}
