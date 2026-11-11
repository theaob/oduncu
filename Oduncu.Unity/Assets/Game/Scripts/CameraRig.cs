using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// The fixed 3/4 orthographic camera (design section 12.5): pans over the map, jumps to a
    /// point, and snaps between three zoom levels (close, normal, overview; design section 7).
    /// World x and z are simulation x and y; one world unit is one cell.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>Orthographic half-heights in cells for close, normal and overview.</summary>
        public static readonly float[] ZoomSizes = { 6f, 10f, 17f };
        public const int ZoomClose = 0;
        public const int ZoomNormal = 1;
        public const int ZoomOverview = 2;

        private const float Pitch = 50f;
        private const float Yaw = 45f;
        private const float Distance = 80f;
        private const float ZoomSmoothing = 12f;

        public Camera Camera { get; private set; }
        public int Zoom { get; private set; } = ZoomNormal;
        public Vector3 Focus => _focus;

        private Vector3 _focus;
        private float _mapSize = 64f;
        private float _size;

        public void Init(Camera cam, int mapSize)
        {
            Camera = cam;
            _mapSize = mapSize;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            _size = ZoomSizes[Zoom];
            cam.orthographicSize = _size;
            JumpTo(new Vector3(mapSize / 2f, 0f, mapSize / 2f));
        }

        public void SetZoom(int level)
        {
            Zoom = Mathf.Clamp(level, 0, ZoomSizes.Length - 1);
        }

        public void ZoomIn() => SetZoom(Zoom - 1);

        public void ZoomOut() => SetZoom(Zoom + 1);

        public void Pan(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            JumpTo(_focus + worldDelta);
        }

        public void JumpTo(Vector3 world)
        {
            _focus = new Vector3(Mathf.Clamp(world.x, 0f, _mapSize), 0f, Mathf.Clamp(world.z, 0f, _mapSize));
            Place();
        }

        /// <summary>Where a screen point meets the ground plane.</summary>
        public Vector3 ScreenToGround(Vector2 screen)
        {
            Ray ray = Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            float d;
            return plane.Raycast(ray, out d) ? ray.GetPoint(d) : _focus;
        }

        public Vector2 WorldToScreen(Vector3 world)
        {
            return Camera.WorldToScreenPoint(world);
        }

        /// <summary>Whether a world point is inside the screen, for "select all of a type on screen".</summary>
        public bool IsOnScreen(Vector3 world)
        {
            Vector3 v = Camera.WorldToViewportPoint(world);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        private void LateUpdate()
        {
            if (Camera == null) return;
            float target = ZoomSizes[Zoom];
            _size = Mathf.Lerp(_size, target, 1f - Mathf.Exp(-ZoomSmoothing * Time.unscaledDeltaTime));
            if (Mathf.Abs(_size - target) < 0.01f) _size = target;
            Camera.orthographicSize = _size;
            Place();
        }

        private void Place()
        {
            if (Camera == null) return;
            Camera.transform.position = _focus - Camera.transform.forward * Distance;
        }
    }
}
