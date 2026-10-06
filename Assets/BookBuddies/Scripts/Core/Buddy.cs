using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Your buddy as saved on this device: its name tag, its look, and the colour of your egg before it hatches.
    /// The egg's colour carries into the pet that hatches from it.
    /// </summary>
    public static class Buddy
    {
        const string NameKey = "bb.name", LookKey = "bb.look", EggKey = "bb.egg";

        /// <summary>The pet used when you explore without one.</summary>
        public const string GuestLook = "{\"h\":200,\"s\":3,\"o\":{},\"sh\":\"round\",\"z\":1,\"f\":\"cute\",\"e\":\"cat\",\"pt\":\"belly\"}";

        // what a new pet can hatch as: the cutest shapes, ears, patterns and faces from the site's parts
        static readonly string[] Shapes = { "bean", "round", "mochi", "pear", "heart", "cloud", "drop", "gumdrop", "tall" };
        static readonly string[] Ears = { "cat", "bunny", "bear", "mouse", "fox", "floppy", "none" };
        static readonly string[] Patterns = { "none", "spots", "belly", "stripes", "hearts", "freckles" };
        static readonly string[] Faces = { "cute", "cute", "goofy", "shy", "starry", "dreamy" };

        public static string Name => PlayerPrefs.GetString(NameKey, "");
        public static string Look => PlayerPrefs.GetString(LookKey, "");
        public static bool Hatched => Look.Length > 0;

        public static int EggHue
        {
            get
            {
                if (!PlayerPrefs.HasKey(EggKey)) PlayerPrefs.SetInt(EggKey, Random.Range(0, 360));
                return PlayerPrefs.GetInt(EggKey);
            }
        }

        /// <summary>The site's egg (stage 0) in your egg's colour.</summary>
        public static string EggLook => "{\"h\":" + EggHue + ",\"s\":0}";

        /// <summary>Your pet, or your egg if it hasn't hatched.</summary>
        public static string ShownLook => Hatched ? Look : EggLook;

        public static void Save(string name, string look)
        {
            if (!string.IsNullOrEmpty(name)) PlayerPrefs.SetString(NameKey, name);
            if (!string.IsNullOrEmpty(look)) PlayerPrefs.SetString(LookKey, look);
            PlayerPrefs.Save();
        }

        /// <summary>Signing out forgets the pet on this device (the server keeps it), so the title shows a fresh egg.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.DeleteKey(LookKey);
            PlayerPrefs.DeleteKey(EggKey);
            PlayerPrefs.Save();
        }

        /// <summary>A new little pet in the egg's colour, as look JSON.</summary>
        public static string NewLook(int hue)
        {
            string P(string[] list) => list[Random.Range(0, list.Length)];
            return "{\"h\":" + hue + ",\"s\":2,\"o\":{},\"sh\":\"" + P(Shapes) + "\",\"z\":1,\"f\":\"" + P(Faces) + "\",\"e\":\"" + P(Ears) + "\",\"pt\":\"" + P(Patterns) + "\"}";
        }
    }
}
