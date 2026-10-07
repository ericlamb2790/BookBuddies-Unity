// Join codes: the hosting PC's address on the home network as a few letters and digits a friend can read out or type
// ("K7QF-2M9A"), with a check character so a typo is caught before anyone waits on a connection that can't work.

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BookBuddies.Local
{
    /// <summary>
    /// A host's IPv4 address and port as a short code in Crockford's base 32 (digits and letters without I, L, O or U),
    /// in groups of 4. Seven characters hold the address, four more the port when it isn't 7790, and the last one checks
    /// the rest. TryRead also takes a plain address, so a friend outside the home network can type "ip:port".
    /// </summary>
    public static class JoinCode
    {
        const string Digits = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const int AddressChars = 7, PortChars = 4; // 7 × 5 bits: the address's 32 and 3 for "a port follows"

        /// <summary>The code for an IPv4 address and port, e.g. "K7QF-2M9A" (the usual 7790 adds nothing).</summary>
        public static string For(IPAddress ip, int port)
        {
            if (ip == null) throw new ArgumentNullException(nameof(ip));
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Join codes hold IPv4 addresses only.", nameof(ip));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

            byte[] b = ip.GetAddressBytes();
            ulong address = (ulong)b[0] << 24 | (ulong)b[1] << 16 | (ulong)b[2] << 8 | b[3];
            bool withPort = port != LocalHost.DefaultPort;
            var values = new int[AddressChars + (withPort ? PortChars : 0) + 1];
            Put(values, 0, AddressChars, address << 3 | (withPort ? 1UL : 0UL));
            if (withPort) Put(values, AddressChars, PortChars, (ulong)port);
            values[values.Length - 1] = Check(values, values.Length - 1);

            var code = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0 && i % 4 == 0) code.Append('-');
                code.Append(Digits[values[i]]);
            }
            return code.ToString();
        }

        /// <summary>
        /// Reads what a friend typed: a join code (any case, spaces and dashes ignored, I and L read as 1 and O as 0), or
        /// an address: an IPv4 address ("192.168.1.5", ":port" optional) or a computer's name with its port ("damp-pc:7790")
        /// or full name ("damp-pc.local"), with "http://" in front if they like. "server" is then "http://&lt;address&gt;:&lt;port&gt;"
        /// (7790 when no port is given). False when it's neither, or a code with a typo.
        /// </summary>
        public static bool TryRead(string text, out string server)
        {
            server = null;
            string s = (text ?? "").Trim();
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0 || s.IndexOfAny(new[] { '.', ':', '/' }) >= 0) return ReadAddress(s, out server);
            // a bare word is a code, so a code with a letter missing is caught rather than taken for a computer's name
            string compact = Compact(s);
            return compact != null && ReadCode(compact, out server);
        }

        // ---- codes ----

        // the code's characters as upper case without spaces or dashes, the look-alikes read as digits; null when it isn't code-shaped
        static string Compact(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char raw in s)
            {
                if (raw == '-' || raw == ' ') continue;
                char c = char.ToUpperInvariant(raw);
                if (c == 'I' || c == 'L') c = '1';
                else if (c == 'O') c = '0';
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))) return null;
                sb.Append(c);
            }
            return sb.Length == AddressChars + 1 || sb.Length == AddressChars + PortChars + 1 ? sb.ToString() : null;
        }

        static bool ReadCode(string compact, out string server)
        {
            server = null;
            var values = new int[compact.Length];
            for (int i = 0; i < compact.Length; i++)
                if ((values[i] = Digits.IndexOf(compact[i])) < 0) return false; // U isn't in the alphabet
            if (Check(values, values.Length - 1) != values[values.Length - 1]) return false;

            ulong head = Take(values, 0, AddressChars);
            bool withPort = values.Length > AddressChars + 1;
            if ((head & 7) != (withPort ? 1UL : 0UL)) return false;
            ulong port = withPort ? Take(values, AddressChars, PortChars) : LocalHost.DefaultPort;
            if (port < 1 || port > 65535 || (withPort && port == LocalHost.DefaultPort)) return false;
            ulong address = head >> 3;
            server = Server(FormattableString.Invariant($"{address >> 24}.{address >> 16 & 255}.{address >> 8 & 255}.{address & 255}"), (int)port);
            return true;
        }

        // n base-32 characters for a number, most significant first
        static void Put(int[] values, int at, int n, ulong number)
        {
            for (int i = n - 1; i >= 0; i--, number >>= 5) values[at + i] = (int)(number & 31);
        }

        static ulong Take(int[] values, int at, int n)
        {
            ulong number = 0;
            for (int i = 0; i < n; i++) number = number << 5 | (uint)values[at + i];
            return number;
        }

        // Each character times an odd weight, mod 32: an odd weight never hides a change, so any one wrong character shows.
        static int Check(int[] values, int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++) sum += (2 * i + 1) * values[i];
            return sum & 31;
        }

        // ---- addresses ----

        static bool ReadAddress(string s, out string server)
        {
            server = null;
            const string Http = "http://";
            if (s.StartsWith(Http, StringComparison.OrdinalIgnoreCase)) s = s.Substring(Http.Length);
            else if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return false; // https and the rest: a home world is plain http
            if (s.EndsWith("/", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
            if (s.Length == 0 || s.IndexOfAny(new[] { '/', '?', '#', '@', ' ', '[' }) >= 0) return false;

            int port = LocalHost.DefaultPort;
            int colon = s.IndexOf(':');
            if (colon >= 0)
            {
                string digits = s.Substring(colon + 1);
                if (digits.Length == 0 || digits.Length > 5 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                    return false;
                s = s.Substring(0, colon);
            }
            string host = IsNumeric(s) ? IPv4(s) : HostName(s);
            if (host == null) return false;
            server = Server(host, port);
            return true;
        }

        static bool IsNumeric(string s)
        {
            foreach (char c in s) if (c != '.' && (c < '0' || c > '9')) return false;
            return true;
        }

        // four numbers 0-255, written the usual way ("192.168.001.5" is 192.168.1.5); null otherwise
        static string IPv4(string s)
        {
            string[] parts = s.Split('.');
            if (parts.Length != 4) return null;
            var n = new int[4];
            for (int i = 0; i < 4; i++)
                if (parts[i].Length == 0 || parts[i].Length > 3 || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]) || n[i] > 255)
                    return null;
            return FormattableString.Invariant($"{n[0]}.{n[1]}.{n[2]}.{n[3]}");
        }

        // a computer's name ("damp-pc", "damp-pc.local") in lower case; null when it isn't one
        static string HostName(string s)
        {
            if (s.Length > 253) return null;
            foreach (string label in s.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return null;
                foreach (char c in label)
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')) return null;
            }
            return s.ToLowerInvariant();
        }

        static string Server(string host, int port) => "http://" + host + ":" + port.ToString(CultureInfo.InvariantCulture);
    }
}
