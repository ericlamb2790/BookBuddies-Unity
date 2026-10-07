using UnityEngine;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The little open book that blinks in the bottom-right corner for about a second when an autosave lands (AutoSave):
    /// over the HUD and every screen, under the virtual cursor, never in the way of a click, and not in photo mode.
    /// The picture is the 📖 emote (UI/Emotes).
    /// </summary>
    public sealed class SaveIcon : MonoBehaviour
    {
        const int Order = 31000; // over every screen, under the virtual cursor (32000)
        const float Size = 30, Margin = 16, Seconds = 1.1f, Strength = .8f;

        static SaveIcon instance;
        Image book;
        float startedAt;

        /// <summary>Blinks the book once (from the start again when it's already showing).</summary>
        public static void Blink()
        {
            if (Hud.Hidden) return; // photo mode keeps the screen clean
            if (!instance) instance = Create();
            instance.startedAt = Time.unscaledTime;
            instance.gameObject.SetActive(true);
        }

        static SaveIcon Create()
        {
            var canvas = UiKit.MakeCanvas("Save icon", Order);
            Destroy(canvas.GetComponent<GraphicRaycaster>());
            DontDestroyOnLoad(canvas.gameObject);
            var icon = canvas.gameObject.AddComponent<SaveIcon>();
            icon.book = UiKit.Icon(canvas.transform, "📖", Size);
            icon.book.rectTransform.Pin(new Vector2(1, 0), new Vector2(-Margin, Margin), new Vector2(Size, Size));
            icon.book.color = Color.clear; // shows from the next frame
            return icon;
        }

        // fades in with a small pop, holds, fades out; then the canvas sleeps until the next save
        void Update()
        {
            float t = (Time.unscaledTime - startedAt) / Seconds;
            if (t >= 1) { gameObject.SetActive(false); return; }
            book.color = Color.white.WithAlpha(Strength * Mathf.Clamp01(Mathf.Min(t / .15f, (1 - t) / .4f)));
            book.rectTransform.localScale = Vector3.one * (.85f + .15f * UiKit.EaseOut(t / .3f));
        }
    }
}
