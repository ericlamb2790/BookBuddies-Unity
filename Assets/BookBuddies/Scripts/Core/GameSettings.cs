using UnityEngine;

namespace BookBuddies
{
    /// <summary>
    /// The player's choices from the Settings menu, saved on the device. Changing one saves it and
    /// tells anyone listening (Changed), so sound, graphics and the HUD update straight away.
    /// </summary>
    public static class GameSettings
    {
        public static readonly int[] FrameRates = { 30, 60, 120, -1 };       // -1 = as fast as the screen allows
        public static readonly float[] UiSizes = { .85f, 1f, 1.15f, 1.3f };

        public static event System.Action Changed;

        public static float MusicVolume { get => F("music", .6f); set => Set("music", value); }
        public static float SoundVolume { get => F("sound", .8f); set => Set("sound", value); }
        public static bool ShowNames { get => B("names", true); set => Set("names", value); }
        public static bool ShowBubbles { get => B("bubbles", true); set => Set("bubbles", value); }
        public static bool ShowMinimap { get => B("minimap", true); set => Set("minimap", value); }
        public static bool AmbientLife { get => B("ambient", true); set => Set("ambient", value); }
        public static bool Cinematics { get => B("cinematics", true); set => Set("cinematics", value); }
        public static bool ReduceMotion { get => B("calm", false); set => Set("calm", value); }
        public static bool IntroSeen { get => B("intro", false); set { Set("intro", value); Save(); } }
        public static bool VSync { get => B("vsync", true); set => Set("vsync", value); }
        public static int FrameRate { get => I("fps", 1); set => Set("fps", Mathf.Clamp(value, 0, FrameRates.Length - 1)); }
        public static int UiSize { get => I("ui", 1); set => Set("ui", Mathf.Clamp(value, 0, UiSizes.Length - 1)); }
        public static int Quality { get => I("quality", QualitySettings.GetQualityLevel()); set => Set("quality", value); }

        public static float UiScale => UiSizes[UiSize];

        public static bool Fullscreen
        {
            get => Screen.fullScreen;
            set { Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed; Changed?.Invoke(); }
        }

        /// <summary>Applies the graphics choices. Called at start and whenever one changes.</summary>
        public static void Apply()
        {
            int quality = Quality;
            if (quality >= 0 && quality < QualitySettings.names.Length && quality != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(quality, true);
            QualitySettings.vSyncCount = VSync && !Application.isMobilePlatform ? 1 : 0;
            Application.targetFrameRate = FrameRates[FrameRate];
        }

        public static void ResetAll()
        {
            foreach (var key in new[] { "music", "sound", "names", "bubbles", "minimap", "ambient", "cinematics", "calm", "vsync", "fps", "ui", "quality" })
                PlayerPrefs.DeleteKey(Prefix + key);
            Saved();
            Save();
        }

        // ---- saving ----

        const string Prefix = "bb.set.";

        static float F(string k, float d) => PlayerPrefs.GetFloat(Prefix + k, d);
        static bool B(string k, bool d) => PlayerPrefs.GetInt(Prefix + k, d ? 1 : 0) == 1;
        static int I(string k, int d) => PlayerPrefs.GetInt(Prefix + k, d);

        static void Set(string k, float v) { PlayerPrefs.SetFloat(Prefix + k, Mathf.Clamp01(v)); Saved(); }
        static void Set(string k, bool v) { PlayerPrefs.SetInt(Prefix + k, v ? 1 : 0); Saved(); }
        static void Set(string k, int v) { PlayerPrefs.SetInt(Prefix + k, v); Saved(); }

        /// <summary>Writes the settings to disk (Settings does this when it closes; Unity also does on quit).</summary>
        public static void Save() => PlayerPrefs.Save();

        static void Saved()
        {
            Apply();
            Changed?.Invoke();
        }
    }
}
