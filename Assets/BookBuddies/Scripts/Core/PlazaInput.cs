using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BookBuddies
{
    /// <summary>Things you can do from a keyboard or gamepad. See PlazaInput.Down for the bindings.</summary>
    public enum PlazaAction { Use, Back, Menu, Emotes, Tricks, Chat, Hop, ZoomIn, ZoomOut, Map, HideHud, Photo, Bag, Hero, Emote1, Emote2, Emote3, Emote4, Emote5, Emote6, Emote7, Emote8 }

    /// <summary>
    /// Mouse, touch, keyboard and gamepad in one place. A tap on a touch screen works exactly like a mouse click.
    /// Works with either Unity input setting (old Input Manager or the Input System package).
    ///
    /// Keyboard: WASD or arrows walk, E use or sit, Space hop, Q emotes, F tricks, Enter or T chat, I bag, C hero,
    ///           1-8 quick emotes, + and - zoom, M map, H hide the HUD, P or F12 photo, Esc back or menu.
    /// Gamepad:  left stick or d-pad walk, A use, B back, X emotes, Y tricks, LB and RB zoom,
    ///           Start menu, Select chat, right stick click map. With a menu open the left stick moves an
    ///           on-screen cursor (A clicks, B backs out, the right stick scrolls; see UI/VirtualCursor).
    /// </summary>
    public static class PlazaInput
    {
        const float TapSlop = 14f;     // pixels a finger may slide and still count as a tap
        const float TapTime = .45f;    // seconds
        const float StickDeadZone = .35f;
        const float CursorDeadZone = .18f;
        const float RepeatDelay = .38f, RepeatEvery = .11f; // d-pad held in a menu

        /// <summary>True while a text box has focus, so typing doesn't walk the pet or fire shortcuts. Updated by Poll.</summary>
        public static bool Typing;

        /// <summary>Set while a menu is open, so the stick and arrow keys move through it instead of walking.</summary>
        public static bool MenuOpen;

        /// <summary>Set during cinematics and full-screen menus: the town ignores walking, taps and shortcuts.</summary>
        public static bool Locked;

        /// <summary>True when the last thing the player touched was the mouse or screen (menus then skip the gamepad highlight).</summary>
        public static bool UsingPointer { get; private set; } = true;

        /// <summary>True when the last thing the player touched was a gamepad (menus then show the on-screen cursor and button hints).</summary>
        public static bool UsingGamepad { get; private set; }

        static Vector2 downAt, lastPointer;
        static float downTime, repeatAt;
        static bool down, downOnUi;
        static Vector2Int dpadHeld, dpadStep;
        static int usedFrame = -1;
        static readonly HashSet<PlazaAction> used = new HashSet<PlazaAction>();
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>
        /// Call once a frame before anything reads input (the virtual cursor does): notices which device the
        /// player is using, whether a text box has focus, and d-pad presses.
        /// </summary>
        public static void Poll()
        {
            Typing = FieldFocused();
            if (GamepadTouched()) { UsingGamepad = true; UsingPointer = false; }
            else if (PointerTouched()) { UsingGamepad = false; UsingPointer = true; }
            else if (KeyboardTouched()) { UsingGamepad = false; UsingPointer = false; }
            StepDpad();
        }

        /// <summary>Marks an action as handled this frame, so later Down calls ignore it (B closing a menu doesn't also open one).</summary>
        public static void Consume(PlazaAction a)
        {
            if (usedFrame != Time.frameCount) { used.Clear(); usedFrame = Time.frameCount; }
            used.Add(a);
        }

        static bool Consumed(PlazaAction a) => usedFrame == Time.frameCount && used.Contains(a);

        /// <summary>True on the frame a short click or tap ends on the world (not on a button).</summary>
        public static bool Tapped(out Vector2 screen)
        {
            screen = PointerPosition();
            if (PointerPressed()) { UsingPointer = true; UsingGamepad = false; down = true; downAt = screen; downTime = Time.unscaledTime; downOnUi = OverUi(screen); }
            if (!PointerReleased() || !down) return false;
            if (Locked) { down = false; return false; }
            down = false;
            return !downOnUi && (screen - downAt).magnitude < TapSlop && Time.unscaledTime - downTime < TapTime;
        }

        /// <summary>Walking direction from keys or stick, in map terms (x right, y down the map). Zero when idle or in a menu.</summary>
        public static Vector2 Move()
        {
            if (Typing || MenuOpen || Locked || UI.UiStack.Any) return Vector2.zero;
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
            if (Consumed(a) || !Pressed(a)) return false;
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
                case PlazaAction.Bag: return K(Key.I);
                case PlazaAction.Hero: return K(Key.C);
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
                case PlazaAction.Bag: return Input.GetKeyDown(KeyCode.I);
                case PlazaAction.Hero: return Input.GetKeyDown(KeyCode.C);
                case PlazaAction.ZoomIn: return Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.JoystickButton5);
                case PlazaAction.ZoomOut: return Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.JoystickButton4);
                default: return Input.GetKeyDown(KeyCode.Alpha1 + (a - PlazaAction.Emote1));
            }
#endif
        }

        /// <summary>Positive to zoom in: scroll up, pinch out, + key or right bumper. Zero while a menu is open.</summary>
        public static float ZoomDelta()
        {
            if (Locked || UI.UiStack.Any) return 0;
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

        // ---- the gamepad as a mouse (read by the virtual cursor) ----

        /// <summary>The left stick, for moving the on-screen cursor. Zero inside a small dead zone.</summary>
        public static Vector2 CursorStick()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            var v = g != null ? g.leftStick.ReadValue() : Vector2.zero;
#else
            var v = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#endif
            return v.magnitude < CursorDeadZone ? Vector2.zero : v;
        }

        /// <summary>The right stick, for scrolling lists under the cursor (Input System only).</summary>
        public static Vector2 ScrollStick()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            var v = g != null ? g.rightStick.ReadValue() : Vector2.zero;
            return v.magnitude < CursorDeadZone ? Vector2.zero : v;
#else
            return Vector2.zero;
#endif
        }

        /// <summary>The gamepad's A button: the cursor's left click.</summary>
        public static bool ClickHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.current != null && Gamepad.current.buttonSouth.isPressed;
#else
            return Input.GetKey(KeyCode.JoystickButton0);
#endif
        }

        /// <summary>One step on the d-pad this frame (repeating while held), or zero. Up is +y.</summary>
        public static Vector2Int DpadStep() => dpadStep;

        /// <summary>An arrow key pressed this frame (moves the keyboard highlight in menus).</summary>
        public static bool ArrowPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && (k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
#endif
        }

        // ---- which device is in use ----

        static bool GamepadTouched()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            if (g == null) return false;
            return g.leftStick.ReadValue().magnitude > StickDeadZone || g.rightStick.ReadValue().magnitude > StickDeadZone || g.dpad.ReadValue() != Vector2.zero
                || g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame
                || g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame || g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame;
#else
            for (var b = KeyCode.JoystickButton0; b <= KeyCode.JoystickButton19; b++)
                if (Input.GetKeyDown(b)) return true;
            bool keys = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);
            return !keys && new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).magnitude > StickDeadZone;
#endif
        }

        // the mouse moved or clicked, or a finger touched the screen
        static bool PointerTouched()
        {
            var at = PointerPosition();
            bool moved = (at - lastPointer).sqrMagnitude > 9;
            lastPointer = at;
#if ENABLE_INPUT_SYSTEM
            bool scrolled = Mouse.current != null && Mouse.current.scroll.ReadValue() != Vector2.zero;
#else
            bool scrolled = Input.mouseScrollDelta != Vector2.zero;
#endif
            return moved || scrolled || PointerPressed();
        }

        static bool KeyboardTouched()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
#else
            return Input.anyKeyDown && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1);
#endif
        }

        static void StepDpad()
        {
#if ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            Vector2 raw = g != null ? g.dpad.ReadValue() : Vector2.zero;
#else
            Vector2 raw = Vector2.zero; // the old Input Manager has no standard d-pad axes; the stick still moves the cursor
#endif
            var dir = new Vector2Int(Mathf.RoundToInt(raw.x), Mathf.RoundToInt(raw.y));
            dpadStep = Vector2Int.zero;
            if (dir != dpadHeld) { dpadHeld = dir; dpadStep = dir; repeatAt = Time.unscaledTime + RepeatDelay; }
            else if (dir != Vector2Int.zero && Time.unscaledTime >= repeatAt) { dpadStep = dir; repeatAt = Time.unscaledTime + RepeatEvery; }
        }

        static bool FieldFocused()
        {
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<UnityEngine.UI.InputField>(out var field) && field.isFocused;
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
