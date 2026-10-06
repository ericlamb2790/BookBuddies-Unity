using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BookBuddies.World
{
    /// <summary>
    /// The mouse or finger as the town sees it between taps (taps themselves come from PlazaInput.Tapped):
    /// whether a press that began on the world is still held, for click-and-hold walking, and where a mouse
    /// is hovering, for the highlight ring. Works with either Unity input setting.
    /// </summary>
    public static class WorldPointer
    {
        static readonly List<RaycastResult> hits = new List<RaycastResult>();
        static bool pressedOnWorld;
        static int polledFrame = -1;

        /// <summary>Where the mouse or first finger is, in screen pixels.</summary>
        public static Vector2 Position
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
#else
                return Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
#endif
            }
        }

        /// <summary>True when the pointer is a finger, which can't hover.</summary>
        public static bool IsTouch
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Pointer.current is Touchscreen;
#else
                return Input.touchCount > 0;
#endif
            }
        }

        /// <summary>True while a press that started on the world (not on a button) is still down.</summary>
        public static bool HeldOnWorld
        {
            get
            {
                Poll();
                return pressedOnWorld;
            }
        }

        /// <summary>True while two fingers are down (pinching to zoom), which never walks.</summary>
        public static bool Pinching
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var ts = Touchscreen.current;
                return ts != null && ts.touches.Count > 1 && ts.touches[1].isInProgress;
#else
                return Input.touchCount > 1;
#endif
            }
        }

        /// <summary>True when a screen point is over a button, card or anything else on a UI canvas.</summary>
        public static bool OverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0;
        }

        static void Poll()
        {
            if (polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            if (Pressed()) pressedOnWorld = !OverUi(Position);
            else if (!Down() || Pinching) pressedOnWorld = false;
        }

        static bool Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        static bool Down()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null && Pointer.current.press.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }
    }
}
