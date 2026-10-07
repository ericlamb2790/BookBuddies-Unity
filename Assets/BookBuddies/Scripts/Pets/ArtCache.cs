using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Keeps every pet and villain drawing as a PNG on disk, named by a hash of its SVG and size, so each look is
    /// drawn once ever instead of on every launch (a vector draw takes a few hundred ms; a PNG loads in a few).
    /// A changed drawing has a new hash, so stale files are never used; deleting the folder is always safe.
    /// </summary>
    public static class ArtCache
    {
        static string Dir => Path.Combine(Application.temporaryCachePath, "art");

        static string PathFor(string svg, int w, int h)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(svg));
                var sb = new StringBuilder(w + "x" + h + "-");
                for (int i = 0; i < 12; i++) sb.Append(hash[i].ToString("x2"));
                return Path.Combine(Dir, sb.Append(".png").ToString());
            }
        }

        /// <summary>The saved drawing for this SVG at this size, or null.</summary>
        public static Texture2D Load(string svg, int w, int h)
        {
            try
            {
                string path = PathFor(svg, w, h);
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(File.ReadAllBytes(path)) && tex.width == w && tex.height == h) return tex;
                Object.Destroy(tex);
            }
            catch (System.Exception e) { Debug.LogWarning("BookBuddies: couldn't read a saved drawing. " + e.Message); }
            return null;
        }

        /// <summary>Saves a fresh drawing for next time (a failed write only means drawing it again later).</summary>
        public static void Store(Texture2D tex, string svg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(PathFor(svg, tex.width, tex.height), tex.EncodeToPNG());
            }
            catch (System.Exception e) { Debug.LogWarning("BookBuddies: couldn't save a drawing. " + e.Message); }
        }
    }
}
