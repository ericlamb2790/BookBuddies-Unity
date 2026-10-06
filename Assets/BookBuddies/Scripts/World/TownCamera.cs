using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// The website's tilted town view: looks down at 57 degrees from 10 tiles away, follows your pet
    /// with a soft lag, and keeps it a little below the middle of the screen. Scroll or pinch to zoom.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TownCamera : MonoBehaviour
    {
        public const float Pitch = 57f;          // the site tilts the ground 33 degrees from flat-on
        public static Quaternion Facing => Quaternion.Euler(Pitch, 0, 0);

        [Tooltip("Tiles from the camera to your pet (the site uses 10).")]
        public float distance = 10f;
        [Tooltip("How far below the middle of the screen your pet sits (0 = centre).")]
        [Range(0, .4f)] public float petBelowCentre = .15f;
        [Range(.6f, 1.6f)] public float zoom = 1f;

        public Camera Cam { get; private set; }
        public Vector2 Focus; // map coordinates the camera is looking at

        /// <summary>
        /// While set, a cinematic is steering (see Cinema.Shot): following your pet and zooming are paused.
        /// The tilt never changes, so pets and props always face the camera.
        /// </summary>
        public bool Directed;

        /// <summary>The zoom you picked with the scroll wheel, pinch or bumpers. Cinematics come back to it.</summary>
        public float PlayerZoom { get; private set; } = 1f;
        public float BaseDistance => baseDistance;
        TownMap map;
        float baseDistance = -1;

        public void Setup(TownMap town, Vector2 start)
        {
            map = town;
            Focus = start;
            Cam = GetComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Palette.Sea;
            Cam.nearClipPlane = .3f;
            Cam.farClipPlane = 400f;
            transform.rotation = Facing;
            if (baseDistance < 0) baseDistance = distance;
            Place();
        }

        /// <summary>Glide toward a map point. Matches the site's 1 - 0.004^dt easing.</summary>
        public void Follow(Vector2 target, float dt)
        {
            if (map == null || Directed) return;
            target.x = Mathf.Clamp(target.x, 4, map.Width - 4);
            target.y = Mathf.Clamp(target.y, 6, map.Height - 3);
            Focus += (target - Focus) * (1 - Mathf.Pow(.004f, dt));
            Place();
        }

        void Update()
        {
            if (Directed) return;
            float zoomIn = PlazaInput.ZoomDelta();
            if (Mathf.Abs(zoomIn) > .001f) zoom = PlayerZoom = Mathf.Clamp(PlayerZoom - zoomIn * .08f, .6f, 1.6f);
        }

        /// <summary>Back to the normal view: the usual distance, your pet just below the middle.</summary>
        public void Release()
        {
            Directed = false;
            distance = baseDistance;
            zoom = PlayerZoom;
            petBelowCentre = .15f;
            Place();
        }

        /// <summary>Puts the camera where Focus, distance and zoom say. Cinematics call this every frame.</summary>
        public void Place()
        {
            if (!Cam) return;
            // Like the site: about 7.8 tiles tall on wide screens, about 11 on phones held upright.
            float tilesTall = (Cam.aspect > 1.05f ? 7.8f : 11f) * zoom;
            Cam.fieldOfView = 2 * Mathf.Atan(tilesTall / 2 / distance) * Mathf.Rad2Deg;

            // Aim a little past the pet so it sits below the middle of the screen.
            float p = Pitch * Mathf.Deg2Rad, t = petBelowCentre * 2 * Mathf.Tan(Cam.fieldOfView * .5f * Mathf.Deg2Rad);
            float ahead = t * distance / (Mathf.Sin(p) + t * Mathf.Cos(p));
            Vector3 aim = TownMap.ToWorld(Focus.x, Focus.y - ahead);
            transform.position = aim - transform.forward * distance;
        }

        /// <summary>The map point under a screen position, or null if it points at the sky.</summary>
        public Vector2? ScreenToMap(Vector2 screen)
        {
            var ray = Cam.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d)) return null;
            return TownMap.ToMap(ray.GetPoint(d));
        }
    }
}
