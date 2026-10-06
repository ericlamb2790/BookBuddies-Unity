using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookBuddies.UI
{
    /// <summary>
    /// The gamepad as a mouse while a menu is up. When a screen is open (UiStack.Any or PlazaInput.MenuOpen) and the
    /// player is on a gamepad, a soft amber pointer appears: the left stick moves it (gently at first, faster the
    /// longer you push, slower over buttons), A is a real left click through the EventSystem (hover, press, click,
    /// and drags for sliders and lists), the d-pad hops between controls, the right stick scrolls the list under it,
    /// and B backs out of the top screen. Moving the mouse hides it and gives the mouse back.
    /// It also routes Esc and B to UiStack.Back first, and reads PlazaInput once a frame.
    /// The picture is Resources/BookBuddies/UI/Cursor/cursor.png, with its tip near the top-left corner.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VirtualCursor : MonoBehaviour
    {
        const float Speed = 1150;            // reference pixels per second, stick fully over
        const float RampSeconds = .45f;      // from careful to full speed
        const float SlowOverControls = .5f;  // a light magnetism: easier to stop on a button
        const float ScrollSpeed = 1500;      // reference pixels per second
        const float GlideSeconds = .18f;
        const float PictureSize = 40;
        static readonly Vector2 Tip = new Vector2(.14f, .9f); // where the point is in cursor.png (0,0 bottom-left)

        static VirtualCursor instance;

        /// <summary>True while the on-screen cursor is showing and driving the menus.</summary>
        public static bool Active => instance != null && instance.active;

        Canvas canvas;
        RectTransform pointer, ring;
        Image ringImage;
        CanvasGroup fade;
        PointerEventData data;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        Vector2 position, lastPosition;
        Selectable hovered, home;
        RectTransform glideTo;
        Vector2 glideFrom;
        float glideAt, heldFor, pulse;
        bool active, wasHeld;

        /// <summary>Creates the cursor once (UiKit.EnsureEventSystem calls this).</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var canvas = UiKit.MakeCanvas("Virtual cursor", 32000);
            Destroy(canvas.GetComponent<GraphicRaycaster>()); // the cursor never catches clicks itself
            instance = canvas.gameObject.AddComponent<VirtualCursor>();
            instance.canvas = canvas;
            instance.Build();
            DontDestroyOnLoad(canvas.gameObject);
        }

        /// <summary>
        /// A screen just opened: on a gamepad the cursor glides to its first control; with the keyboard that
        /// control is highlighted; with the mouse nothing changes.
        /// </summary>
        public static void FocusFirst(Selectable first)
        {
            if (first == null) return;
            Ensure();
            instance.home = first;
            if (PlazaInput.UsingGamepad) instance.Glide((RectTransform)first.transform);
            else if (!PlazaInput.UsingPointer && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        void Build()
        {
            var root = (RectTransform)canvas.transform;
            fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0;
            fade.blocksRaycasts = fade.interactable = false;

            ringImage = UiKit.Panel(root, "hover ring", Palette.Amber, 0);
            ringImage.sprite = UiKit.Ring(26, 3);
            ringImage.type = Image.Type.Sliced;
            ringImage.raycastTarget = false;
            ring = ringImage.rectTransform.Pin(Vector2.zero, Vector2.zero, new Vector2(52, 52));
            ring.pivot = new Vector2(.5f, .5f);

            pointer = UiKit.Node("pointer", root).Pin(Vector2.zero, Vector2.zero, new Vector2(PictureSize, PictureSize));
            pointer.pivot = Tip;
            var picture = Picture();
            var shadow = UiKit.Node("shadow", pointer).Fill().gameObject.AddComponent<Image>();
            shadow.sprite = picture;
            shadow.color = new Color(.24f, .16f, .04f, .35f);
            shadow.raycastTarget = false;
            ((RectTransform)shadow.transform).anchoredPosition = new Vector2(3, -4);
            var image = UiKit.Node("paw", pointer).Fill().gameObject.AddComponent<Image>();
            image.sprite = picture;
            image.raycastTarget = false;
            if (picture == UiKit.Glow) image.color = Palette.Amber;
            position = new Vector2(Screen.width, Screen.height) / 2;
        }

        static Sprite Picture()
        {
            var tex = Resources.Load<Texture2D>("BookBuddies/UI/Cursor/cursor");
            if (tex == null) return UiKit.Glow;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Tip, tex.width / PictureSize, 0, SpriteMeshType.FullRect);
        }

        void Update()
        {
            PlazaInput.Poll();
            var es = EventSystem.current;
            bool menu = UiStack.Any || PlazaInput.MenuOpen;
            Back(es);
            if (es == null) return;
            if (menu && !PlazaInput.UsingGamepad) HighlightWithArrows(es);

            bool want = menu && PlazaInput.UsingGamepad;
            if (want != active) SetActive(want, es);
            if (active)
            {
                Move();
                Hover(es);
                Click(es);
                Scroll();
                Hop();
            }
            Draw();
        }

        // Esc or B backs out of the top screen; while typing in one, the first press just stops typing.
        void Back(EventSystem es)
        {
            if (!UiStack.Any || !PlazaInput.Down(PlazaAction.Back)) return;
            if (PlazaInput.Typing) { if (es != null) es.SetSelectedGameObject(null); }
            else UiStack.Back();
            PlazaInput.Consume(PlazaAction.Back);
        }

        // keyboard: an arrow press with nothing highlighted picks the screen's first control
        void HighlightWithArrows(EventSystem es)
        {
            if (es.currentSelectedGameObject != null || !PlazaInput.ArrowPressed()) return;
            var first = Usable(home) ? home : FirstControl();
            if (first != null) es.SetSelectedGameObject(first.gameObject);
        }

        void SetActive(bool on, EventSystem es)
        {
            active = on;
            es.sendNavigationEvents = !on; // the cursor, not the d-pad highlight, drives menus now
            Cursor.visible = !on;
            if (on)
            {
                es.SetSelectedGameObject(null);
                wasHeld = PlazaInput.ClickHeld(); // an A still held from opening the menu isn't a click
                data = new PointerEventData(es) { pointerId = -50, button = PointerEventData.InputButton.Left, position = position };
                return;
            }
            Cancel();
            Enter(null);
            hovered = null;
            glideTo = null;
        }

        // ---- moving ----

        void Move()
        {
            float k = canvas.scaleFactor, dt = Time.unscaledDeltaTime;
            var stick = PlazaInput.CursorStick();
            if (stick != Vector2.zero)
            {
                glideTo = null;
                heldFor += dt;
                float ramp = Mathf.Lerp(.45f, 1, Mathf.Clamp01(heldFor / RampSeconds));
                float push = Mathf.Pow(Mathf.Clamp01(stick.magnitude), 1.6f); // fine control near the middle
                float slow = hovered != null ? SlowOverControls : 1;
                position += stick.normalized * (push * ramp * slow * Speed * k * dt);
            }
            else heldFor = 0;

            if (glideTo != null)
            {
                if (!glideTo.gameObject.activeInHierarchy) glideTo = null;
                else
                {
                    float t = (Time.unscaledTime - glideAt) / GlideSeconds;
                    position = Vector2.Lerp(glideFrom, Centre(glideTo), UiKit.EaseOut(t));
                    if (t >= 1) glideTo = null;
                }
            }
            position.x = Mathf.Clamp(position.x, 0, Screen.width - 1);
            position.y = Mathf.Clamp(position.y, 0, Screen.height - 1);
        }

        void Glide(RectTransform to)
        {
            glideTo = to;
            glideFrom = position;
            glideAt = Time.unscaledTime;
        }

        // d-pad: hop to the nearest control in that direction
        void Hop()
        {
            var step = PlazaInput.DpadStep();
            if (step == Vector2Int.zero) return;
            Vector2 dir = ((Vector2)step).normalized;
            Selectable best = null;
            float bestScore = float.MaxValue;
            foreach (var s in Candidates())
            {
                var to = Centre((RectTransform)s.transform) - position;
                float along = Vector2.Dot(to, dir);
                if (along < 8) continue;
                float score = along + Mathf.Abs(to.x * dir.y - to.y * dir.x) * 2;
                if (score < bestScore) { bestScore = score; best = s; }
            }
            if (best != null) { Glide((RectTransform)best.transform); Sound.Play("tap", .4f); }
        }

        // ---- pointing ----

        void Hover(EventSystem es)
        {
            data.delta = position - lastPosition;
            data.position = lastPosition = position;
            hits.Clear();
            es.RaycastAll(data, hits);
            data.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default;
            var over = data.pointerCurrentRaycast.gameObject;
            Enter(over);
            var control = over != null ? over.GetComponentInParent<Selectable>() : null;
            hovered = Usable(control) && !IsBackdrop(control) ? control : null;
        }

        // pointer exit for what we left, pointer enter for what we're over now (like a mouse)
        void Enter(GameObject target)
        {
            if (data.pointerEnter == target) return;
            var shared = SharedParent(data.pointerEnter, target);
            if (data.pointerEnter != null)
                for (var t = data.pointerEnter.transform; t != null && t != shared; t = t.parent)
                {
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerExitHandler);
                    data.hovered.Remove(t.gameObject);
                }
            data.pointerEnter = target;
            if (target != null)
                for (var t = target.transform; t != null && t != shared; t = t.parent)
                {
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerEnterHandler);
                    data.hovered.Add(t.gameObject);
                }
        }

        static Transform SharedParent(GameObject a, GameObject b)
        {
            if (a == null || b == null) return null;
            for (var t = a.transform; t != null; t = t.parent)
                if (b.transform.IsChildOf(t)) return t;
            return null;
        }

        // A: press, drag and release, just like the left mouse button
        void Click(EventSystem es)
        {
            bool held = PlazaInput.ClickHeld();
            var over = data.pointerCurrentRaycast.gameObject;
            if (held && !wasHeld) Press(es, over);
            else if (held) Drag(es);
            else if (wasHeld) Release(over);
            wasHeld = held;
        }

        void Press(EventSystem es, GameObject over)
        {
            PlazaInput.Consume(PlazaAction.Use);
            pulse = 1;
            data.eligibleForClick = true;
            data.delta = Vector2.zero;
            data.dragging = false;
            data.useDragThreshold = true;
            data.pressPosition = data.position;
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            if (ExecuteEvents.GetEventHandler<ISelectHandler>(over) != es.currentSelectedGameObject) es.SetSelectedGameObject(null, data);
            var pressed = ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.pointerDownHandler);
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            data.pointerPress = pressed != null ? pressed : click;
            data.rawPointerPress = over;
            data.pointerClick = click;
            data.clickTime = Time.unscaledTime;
            data.clickCount = 1;
            data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(over);
            if (data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
        }

        void Drag(EventSystem es)
        {
            if (data.pointerDrag == null) return;
            float threshold = es.pixelDragThreshold;
            if (!data.dragging && (data.pressPosition - data.position).sqrMagnitude >= threshold * threshold)
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
            }
            if (!data.dragging) return;
            if (data.pointerPress != data.pointerDrag)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler); // a drag on a list isn't a click on its button
                data.eligibleForClick = false;
                data.pointerPress = data.rawPointerPress = null;
            }
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
        }

        void Release(GameObject over)
        {
            if (data.pointerPress != null) ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            if (data.eligibleForClick && data.pointerClick != null && data.pointerClick == click)
                ExecuteEvents.Execute(data.pointerClick, data, ExecuteEvents.pointerClickHandler);
            if (data.pointerDrag != null && data.dragging)
            {
                ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.dropHandler);
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            }
            ClearPress();
        }

        // the cursor hid mid-press: let go without clicking
        void Cancel()
        {
            if (data.pointerPress != null) ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            if (data.pointerDrag != null && data.dragging) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            ClearPress();
        }

        void ClearPress()
        {
            data.eligibleForClick = data.dragging = false;
            data.pointerPress = data.rawPointerPress = data.pointerClick = data.pointerDrag = null;
        }

        // right stick: scroll the list under the cursor, or the open screen's list
        void Scroll()
        {
            var stick = PlazaInput.ScrollStick();
            if (stick == Vector2.zero) return;
            var target = ScrollUnder(data.pointerCurrentRaycast.gameObject);
            if (target == null) return;
            float step = ScrollSpeed * Time.unscaledDeltaTime / Mathf.Max(1, target.scrollSensitivity);
            data.scrollDelta = new Vector2(target.horizontal ? -stick.x : 0, target.vertical ? stick.y : 0) * step;
            ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.scrollHandler);
        }

        ScrollRect ScrollUnder(GameObject over)
        {
            var handler = ExecuteEvents.GetEventHandler<IScrollHandler>(over);
            if (handler != null && handler.TryGetComponent<ScrollRect>(out var under)) return under;
            ScrollRect best = null;
            float bestD = float.MaxValue;
            var top = UiStack.Top as Component;
            if (top == null) return null;
            foreach (var s in top.GetComponentsInChildren<ScrollRect>())
            {
                float d = (Centre((RectTransform)s.transform) - position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // ---- finding controls ----

        IEnumerable<Selectable> Candidates()
        {
            var top = UiStack.Top as Component;
            foreach (var s in Selectable.allSelectablesArray)
            {
                if (!Usable(s) || s.navigation.mode == Navigation.Mode.None || IsBackdrop(s)) continue;
                if (top != null && !s.transform.IsChildOf(top.transform)) continue;
                var c = Centre((RectTransform)s.transform);
                if (c.x < 0 || c.y < 0 || c.x > Screen.width || c.y > Screen.height) continue;
                yield return s;
            }
        }

        // the top-left control of the open screen
        Selectable FirstControl()
        {
            Selectable best = null;
            float bestScore = float.MaxValue;
            foreach (var s in Candidates())
            {
                var c = Centre((RectTransform)s.transform);
                float score = c.x - c.y * 4;
                if (score < bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        static bool Usable(Selectable s) => s != null && s.isActiveAndEnabled && s.IsInteractable();

        // a full-screen "tap outside to close" layer isn't something to aim at
        bool IsBackdrop(Selectable s)
        {
            var r = ((RectTransform)s.transform).rect.size * canvas.scaleFactor;
            return r.x >= Screen.width * .9f && r.y >= Screen.height * .9f;
        }

        static Vector2 Centre(RectTransform r) => RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));

        // ---- drawing ----

        void Draw()
        {
            float dt = Time.unscaledDeltaTime, k = canvas.scaleFactor;
            fade.alpha = Mathf.MoveTowards(fade.alpha, active ? 1 : 0, dt * 8);
            if (fade.alpha <= 0) return;
            pulse = Mathf.MoveTowards(pulse, 0, dt * 6);
            pointer.anchoredPosition = position / k;
            float press = PlazaInput.ClickHeld() && active ? .88f : 1;
            pointer.localScale = Vector3.one * Mathf.Lerp(pointer.localScale.x, press, dt * 20);

            ring.anchoredPosition = position / k;
            float show = hovered != null ? 1 : 0;
            ringImage.color = Palette.Amber.WithAlpha(Mathf.MoveTowards(ringImage.color.a, show * .9f, dt * 7));
            float size = (hovered != null ? 1 : .7f) - pulse * .25f;
            ring.localScale = Vector3.one * Mathf.Lerp(ring.localScale.x, size, dt * 14);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Cursor.visible = true;
        }
    }
}
