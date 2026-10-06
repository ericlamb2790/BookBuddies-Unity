using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BookBuddies
{
    /// <summary>Things you can do from a keyboard or gamepad. See PlazaInput.Down for the bindings.</summary>
    public enum PlazaAction { Use, Back, Menu, Emotes, Tricks, Chat, Hop, ZoomIn, ZoomOut, Map, HideHud, Photo, Emote1, Emote2, Emote3, Emote4, Emote5, Emote6, Emote7, Emote8 }

    /// <summary>
    /// Mouse, touch, keyboard and gamepad in one place. A tap on a touch screen works exactly like a mouse click.
    /// Works with either Unity input setting (old Input Manager or the Input System package).
    ///
    /// Keyboard: WASD or arrows walk, E use or sit, Space hop, Q emotes, F tricks, Enter or T chat,
    ///           1-8 quick emotes, + and - zoom, M map, H hide the HUD, P or F12 photo, Esc close or menu.
    /// Gamepad:  left stick or d-pad walk, A use, B close, X emotes, Y tricks, LB and RB zoom,
    ///           Start menu, Select chat, right stick click map.
    /// </summary>
    public static class PlazaInput
    {
        const float TapSlop = 14f;     // pixels a finger may slide and still count as a tap
        const float TapTime = .45f;    // seconds
        const float StickDeadZone = .35f;

        /// <summary>Set while a text box has focus, so typing doesn't walk the pet or fire shortcuts.</summary>
        public static bool Typing;

        /// <summary>Set while a menu is open, so the stick and arrow keys move through it instead of walking.</summary>
        public static bool MenuOpen;

        /// <summary>Set during cinematics and full-screen menus: the town ignores walking, taps and shortcuts.</summary>
        public static bool Locked;

        /// <summary>True when the last thing the player touched was the mouse or screen (menus then skip the gamepad highlight).</summary>
        public static bool UsingPointer { get; private set; } = true;

        static Vector2 downAt;
        static float downTime;
        static bool down, downOnUi;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>True on the frame a short click or tap ends on the world (not on a button).</summary>
        public static bool Tapped(out Vector2 screen)
        {
            screen = PointerPosition();
            if (PointerPressed()) { UsingPointer = true; down = true; downAt = screen; downTime = Time.unscaledTime; downOnUi = OverUi(screen); }
            if (!PointerReleased() || !down) return false;
            if (Locked) { down = false; return false; }
            down = false;
            return !downOnUi && (screen - downAt).magnitude < TapSlop && Time.unscaledTime - downTime < TapTime;
        }

        /// <summary>Walking direction from keys or stick, in map terms (x right, y down the map). Zero when idle.</summary>
        public static Vector2 Move()
        {
            if (Typing || MenuOpen || Locked) return Vector2.zero;
            Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
            }
            var g = Gamepad.current;
            if (g != null) v += g.leftStick.ReadValue() + g.dpad.ReadValue();
#else
            v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            if (v.magnitude < StickDeadZone) return Vector2.zero;
            UsingPointer = false;
            return new Vector2(v.x, -v.y).normalized; // screen up = toward the top of the map
        }

        public static bool Down(PlazaAction a)
        {
            if ((Typing || Locked) && a != PlazaAction.Back) return false;
            if (!Pressed(a)) return false;
            UsingPointer = false;
            return true;
        }

        /// <summary>Enter, even while typing (it sends the chat message).</summary>
        public static bool EnterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        }

        static bool Pressed(PlazaAction a)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var g = Gamepad.current;
            bool K(Key key) => k != null && k[key].wasPressedThisFrame;
            bool P(System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> b) => g != null && b(g).wasPressedThisFrame;
            switch (a)
            {
                case PlazaAction.Use: return K(Key.E) || P(p => p.buttonSouth);
                case PlazaAction.Back: return K(Key.Escape) || P(p => p.buttonEast);
                case PlazaAction.Menu: return P(p => p.startButton);
                case PlazaAction.Map: return K(Key.M) || P(p => p.rightStickButton);
                case PlazaAction.HideHud: return K(Key.H);
                case PlazaAction.Photo: return K(Key.P) || K(Key.F12);
                case PlazaAction.Emotes: return K(Key.Q) || P(p => p.buttonWest);
                case PlazaAction.Tricks: return K(Key.F) || P(p => p.buttonNorth);
                case PlazaAction.Chat: return K(Key.Enter) || K(Key.T) || P(p => p.selectButton);
                case PlazaAction.Hop: return K(Key.Space);
                case PlazaAction.ZoomIn: return K(Key.Equals) || K(Key.NumpadPlus) || P(p => p.rightShoulder);
                case PlazaAction.ZoomOut: return K(Key.Minus) || K(Key.NumpadMinus) || P(p => p.leftShoulder);
                default: return K(Key.Digit1 + (a - PlazaAction.Emote1));
            }
#else
            switch (a)
            {
                case PlazaAction.Use: return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton0);
                case PlazaAction.Back: return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
                case PlazaAction.Menu: return Input.GetKeyDown(KeyCode.JoystickButton7);
                case PlazaAction.Map: return Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.JoystickButton9);
                case PlazaAction.HideHud: return Input.GetKeyDown(KeyCode.H);
                case PlazaAction.Photo: return Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.F12);
                case PlazaAction.Emotes: return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton2);
                case PlazaAction.Tricks: return Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.JoystickButton3);
                case PlazaAction.Chat: return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.JoystickButton6);
                case PlazaAction.Hop: return Input.GetKeyDown(KeyCode.Space);
                case PlazaAction.ZoomIn: return Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.JoystickButton5);
                case PlazaAction.ZoomOut: return Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.JoystickButton4);
                default: return Input.GetKeyDown(KeyCode.Alpha1 + (a - PlazaAction.Emote1));
            }
#endif
        }

        /// <summary>Positive to zoom in: scroll up, pinch out, + key or right bumper.</summary>
        public static float ZoomDelta()
        {
            if (Locked) return 0;
            float buttons = (Down(PlazaAction.ZoomIn) ? 2 : 0) - (Down(PlazaAction.ZoomOut) ? 2 : 0);
            if (MenuOpen) return buttons;
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null && ts.touches.Count > 1 && ts.touches[0].isInProgress && ts.touches[1].isInProgress)
            {
                Vector2 a = ts.touches[0].position.ReadValue(), b = ts.touches[1].position.ReadValue();
                Vector2 da = ts.touches[0].delta.ReadValue(), db = ts.touches[1].delta.ReadValue();
                return buttons + ((a - b).magnitude - ((a - da) - (b - db)).magnitude) / 60f;
            }
            return buttons + (Mouse.current != null && !Typing ? Mouse.current.scroll.ReadValue().y / 120f : 0f);
#else
            if (Input.touchCount == 2)
            {
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                return buttons + ((a.position - b.position).magnitude - ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude) / 60f;
            }
            return buttons + Input.mouseScrollDelta.y;
#endif
        }

        /// <summary>Skips a cinematic: Esc, Space, Enter, or the gamepad's A, B or Start. Works while Locked.</summary>
        public static bool SkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var g = Gamepad.current;
            return (k != null && (k.escapeKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame))
                || (g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame));
#else
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.JoystickButton7);
#endif
        }

        /// <summary>A second finger touching down (shows the HUD again in photo mode on phones).</summary>
        public static bool TwoFingerTap()
        {
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            return ts != null && ts.touches.Count > 1 && ts.touches[1].press.wasPressedThisFrame;
#else
            return Input.touchCount == 2 && Input.GetTouch(1).phase == TouchPhase.Began;
#endif
        }

        // Touch counts as the mouse: the first finger is the pointer.
        static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
#else
            return Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
#endif
        }

        static bool PointerPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0); // Unity turns the first touch into mouse button 0
#endif
        }

        static bool PointerReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }

        static bool OverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0;
        }
    }
}
