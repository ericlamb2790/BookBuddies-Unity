using System;
using System.Globalization;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The website's maths, bit for bit: JavaScript rounding, its string hash and its seeded random numbers.
    /// Pure C# (no Unity types), so the rules can be checked outside the editor.
    /// </summary>
    public static class JsMath
    {
        /// <summary>JavaScript Math.round: halves round up (C#'s Math.Round would round them to even).</summary>
        public static double Round(double x) => Math.Floor(x + .5);
        public static int RoundI(double x) => (int)Math.Floor(x + .5);

        /// <summary>Math.round(v*10)/10.</summary>
        public static double Round1(double x) => Math.Floor(x * 10 + .5) / 10;

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// tqHash / ltHash: FNV-1a over code points, using the first UTF-16 unit of each one.
        /// "me:strike" gives 3803301741 and "🐰" gives 931276136.
        /// </summary>
        public static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                h = unchecked((h ^ c) * 16777619u);
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            }
            return h;
        }

        /// <summary>The Plaza's tile hash hsh(x, y), 0 to 1.</summary>
        public static double Hsh(int x, int y)
        {
            int h = unchecked(x * 374761393 + y * 668265263);
            h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
            return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
        }

        /// <summary>strSeed: h = 7, then h = (h*31 + c) % 2147483647 per UTF-16 unit; never 0.</summary>
        public static long StrSeed(string s)
        {
            long h = 7;
            foreach (char c in s) h = (h * 31 + c) % 2147483647;
            return h == 0 ? 1 : h;
        }

        /// <summary>tqShade: lighten (k &gt; 0) or darken (k &lt; 0) a #rrggbb colour. Anything else passes through.</summary>
        public static string Shade(string c, double k)
        {
            if (!IsHex6(c)) return c;
            var sb = new System.Text.StringBuilder("#");
            for (int i = 0; i < 3; i++)
            {
                int ch = int.Parse(c.Substring(1 + i * 2, 2), NumberStyles.HexNumber);
                int v = RoundI(ch + ((k < 0 ? 0 : 255) - ch) * Math.Abs(k));
                sb.Append(Clamp(v, 0, 255).ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>tqMix: blend two #rrggbb colours (t = 0 gives a, 1 gives b).</summary>
        public static string Mix(string a, string b, double t)
        {
            string A = a != null && a.StartsWith("#") ? a : "#" + a, B = b != null && b.StartsWith("#") ? b : "#" + b;
            if (!IsHex6(A) || !IsHex6(B)) return a ?? b;
            var sb = new System.Text.StringBuilder("#");
            for (int i = 0; i < 3; i++)
            {
                int x = int.Parse(A.Substring(1 + i * 2, 2), NumberStyles.HexNumber), y = int.Parse(B.Substring(1 + i * 2, 2), NumberStyles.HexNumber);
                sb.Append(Clamp(RoundI(x + (y - x) * t), 0, 255).ToString("x2"));
            }
            return sb.ToString();
        }

        static bool IsHex6(string c)
        {
            if (c == null || c.Length != 7 || c[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(c[i])) return false;
            return true;
        }

        /// <summary>Formats a number the way JavaScript prints it (12, 0.5, -3.25).</summary>
        public static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>tqRand / ltRng: mulberry32. Seed 12345 gives 0.9797282677609473, 0.3067522644996643, 0.484205421525985.</summary>
    public sealed class Mulberry32
    {
        uint a;
        public Mulberry32(uint seed) { a = seed; }
        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = a;
                t = (t ^ (t >> 15)) * (1 | t);
                t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <summary>The Plaza map generator's rnd(): Park–Miller.</summary>
    public sealed class ParkMiller
    {
        long sd;
        public ParkMiller(long seed) { sd = seed; }
        public double Next() { sd = sd * 16807 % 2147483647; return sd / 2147483647.0; }
    }

    /// <summary>Math.random for battles and drops. Swap in a seeded one for tests.</summary>
    public interface IRng { double Next(); }

    public sealed class SystemRng : IRng
    {
        readonly Random r;
        public SystemRng(int? seed = null) { r = seed.HasValue ? new Random(seed.Value) : new Random(); }
        public double Next() => r.NextDouble();
        public static readonly SystemRng Shared = new SystemRng();
    }

    public static class RngExt
    {
        public static int Range(this IRng r, int n) => n <= 0 ? 0 : Math.Min(n - 1, (int)Math.Floor(r.Next() * n));
        public static T Pick<T>(this IRng r, System.Collections.Generic.IList<T> list) => list == null || list.Count == 0 ? default : list[r.Range(list.Count)];
        public static bool Chance(this IRng r, double p) => r.Next() < p;
    }
}
