using BookBuddies.Pets;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// Your buddy as saved on this device: its name tag, its look, and the colour of your egg before it hatches.
    /// The egg's colour carries into the pet that hatches from it. With more than one pet (Pets/MyPets.cs), Look is the
    /// active one, so the town, Tales and the title screen all show it.
    /// </summary>
    public static class Buddy
    {
        const string NameKey = "bb.name", LookKey = "bb.look", EggKey = "bb.egg";

        /// <summary>The pet used when you explore without one.</summary>
        public const string GuestLook = "{\"h\":200,\"s\":3,\"o\":{},\"sh\":\"round\",\"z\":1,\"f\":\"cute\",\"e\":\"cat\",\"pt\":\"belly\"}";

        static readonly System.Random dice = new System.Random(); // NextDouble stays below 1, like Math.random

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

        /// <summary>Signing out forgets the pets on this device (the server keeps them), so the title shows a fresh egg.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.DeleteKey(LookKey);
            PlayerPrefs.DeleteKey(EggKey);
            MyPets.Forget();
            PlayerPrefs.Save();
        }

        /// <summary>A new little pet in the egg's colour, as look JSON: random DNA like a hatch on the site (any body, and only parts that suit it).</summary>
        public static string NewLook(int hue) => PetSprites.Parts.Dna.NewLook(Roll, hue);

        /// <summary>A random number from 0 up to (not including) 1, for pet rolls.</summary>
        public static double Roll() => dice.NextDouble();
    }
}
