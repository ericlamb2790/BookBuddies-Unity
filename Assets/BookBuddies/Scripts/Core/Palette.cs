using UnityEngine;

namespace BookBuddies
{
    /// <summary>BookBuddies colours, taken from the website's Plaza styles.</summary>
    public static class Palette
    {
        public static readonly Color Ink = Hex("#3a2a1c");
        public static readonly Color InkSoft = Hex("#6b5440");
        public static readonly Color Cream = Hex("#fff8ea");
        public static readonly Color Paper = Hex("#fff1dc");
        public static readonly Color Amber = Hex("#ffbf47");
        public static readonly Color Ember = Hex("#e8743c");
        public static readonly Color Rose = Hex("#e2557f");
        public static readonly Color Sky = Hex("#4f7fd6");
        public static readonly Color Teal = Hex("#2fa39b");
        public static readonly Color Leaf = Hex("#5fa84e");
        public static readonly Color Sea = Hex("#5fb0d4");
        public static readonly Color BubbleMine = Hex("#fff3d6");
        public static readonly Color BubbleBot = Hex("#eef6ff");
        public static readonly Color BubbleOther = Hex("#fffdf8");
        public static readonly Color ShadowTint = new Color32(40, 60, 20, 255);

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
    }
}
