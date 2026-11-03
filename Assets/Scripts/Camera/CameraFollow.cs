using UnityEngine;
using Simulation;

namespace Presentation.CameraSystem
{
    /// <summary>
    /// Fixed orthographic camera that always frames the WHOLE native play area.
    ///
    /// Framing order (no character scaling is involved):
    ///   1. Required rect = CourtGeometry.RequiredVisibleWorld (BigRect 2720 x 1000 px + floor band),
    ///      expanded by a small safety margin.
    ///   2. orthographicSize = max(requiredHalfHeight (incl. HUD reserve), requiredHalfWidth / aspect),
    ///      so neither axis is ever cropped on any aspect ratio (16:9, 20:9, 4:3 ...). Extra space on the
    ///      looser axis is filled by the procedural backdrop instead of cropping.
    ///   3. Re-evaluated every LateUpdate (handles rotation / window resize / RenderTexture targets).
    /// The camera never follows the ball.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class CameraFollow : MonoBehaviour
    {
        [Tooltip("World-unit margin added left/right of the play area.")]
        [SerializeField] private float horizontalMargin = 0.6f;
        [Tooltip("World-unit margin added above/below the play area.")]
        [SerializeField] private float verticalMargin = 0.4f;
        [Tooltip("Fraction of the viewport height kept free at the top for the score HUD.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float topHudReserve = 0.10f;
        [Tooltip("Fraction of the viewport height kept free at the bottom for the on-screen touch buttons.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float bottomControlsReserve = 0.16f;

        public float TopHudReserve => topHudReserve;
        public float BottomControlsReserve => bottomControlsReserve;
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.08f, 0.17f, 1f);

        private Camera cam;

        public Rect LastRequiredRect { get; private set; }
        public Rect LastVisibleRect { get; private set; }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            ApplyFramingNow();
        }

        private void LateUpdate() => ApplyFramingNow();

        /// <summary>Fits the camera to the play area for the current viewport (also called by the capture probe).</summary>
        public void ApplyFramingNow()
        {
            if (cam == null) cam = GetComponent<Camera>();
            float aspect = cam.aspect;
            if (aspect <= 0f || float.IsNaN(aspect)) return;

            Rect req = CourtGeometry.RequiredVisibleWorld;
            req = Rect.MinMaxRect(req.xMin - horizontalMargin, req.yMin - verticalMargin, req.xMax + horizontalMargin, req.yMax + verticalMargin);
            LastRequiredRect = req;

            float usable = 1f - topHudReserve - bottomControlsReserve;
            float halfHeightForHeight = req.height * 0.5f / usable;
            float halfHeightForWidth = req.width * 0.5f / aspect;
            float ortho = Mathf.Max(halfHeightForHeight, halfHeightForWidth);
            cam.orthographicSize = ortho;

            // Centre the required rect inside the band between the bottom controls reserve and the top HUD reserve.
            float visibleH = ortho * 2f;
            float camY = req.center.y - visibleH * (bottomControlsReserve - topHudReserve) * 0.5f;
            Vector3 p = transform.position;
            transform.position = new Vector3(req.center.x, camY, p.z == 0f ? -10f : p.z);

            float visibleW = visibleH * aspect;
            LastVisibleRect = new Rect(req.center.x - visibleW * 0.5f, camY - ortho, visibleW, visibleH);
        }
    }
}