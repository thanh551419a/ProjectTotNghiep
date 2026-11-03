using System.Collections.Generic;
using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Draws the volleyball court procedurally with LineRenderers (no background image).
    /// Every coordinate comes from the native court configuration mirrored in CourtGeometry and is
    /// converted with the same origin/axis convention as the native snapshot (net centre on the floor).
    ///
    /// Side-view presentation: the floor line (native y = 265) is the centre of a shallow floor band
    /// (±CourtBandHalfDepth) so the court markings remain readable from the side camera:
    ///   near / far side lines, end lines (native 9 m lines x = 650 / 2090), attack lines
    ///   (3 m lines x = 1130 / 1610), centre line under the net (x = 1370), free zone up to the
    ///   BigRect edges (x = 10 / 2730) and the BigRect play-area outline (2720 x 1000).
    /// Building is idempotent: previously generated children are removed first.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CourtLineRenderer : MonoBehaviour
    {
        public struct Segment
        {
            public string name;
            public Vector2 a, b;
        }

        [SerializeField] private Material lineMaterial;
        [SerializeField] private float lineWidth = 0.07f;
        [SerializeField] private Color lineColor = new Color(0.98f, 0.97f, 0.92f, 1f);
        [SerializeField] private Color courtColor = new Color(0.86f, 0.50f, 0.27f, 1f);
        [SerializeField] private Color frontZoneColor = new Color(0.91f, 0.58f, 0.33f, 1f);
        [SerializeField] private Color freeZoneColor = new Color(0.13f, 0.36f, 0.48f, 1f);
        [SerializeField] private Color playAreaOutlineColor = new Color(0.55f, 0.80f, 1f, 0.22f);
        [SerializeField] private Color skyTop = new Color(0.05f, 0.08f, 0.17f, 1f);
        [SerializeField] private Color skyBottom = new Color(0.12f, 0.22f, 0.38f, 1f);

        public const int SortBackground = -300, SortFreeZone = -200, SortCourt = -190, SortFrontZone = -185, SortOutline = -150, SortLines = -100;

        private readonly List<Segment> segments = new List<Segment>();
        private Transform root;

        public IReadOnlyList<Segment> Segments => segments;
        public int LineRendererCount { get; private set; }

        private void Awake() => Build();

        public void Build()
        {
            if (root == null)
            {
                var existing = transform.Find("Generated");
                root = existing != null ? existing : new GameObject("Generated").transform;
                root.SetParent(transform, false);
            }
            LineGeometry.ClearChildren(root);
            segments.Clear();
            LineRendererCount = 0;

            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            Material mat = LineGeometry.ResolveMaterial(lineMaterial);
            float d = CourtGeometry.CourtBandHalfDepth;
            float xRectL = CourtGeometry.ToWorldX(CourtGeometry.RectX);
            float xRectR = CourtGeometry.ToWorldX(CourtGeometry.RectX + CourtGeometry.RectWidth);
            float yTop = CourtGeometry.ToWorldY(CourtGeometry.RectY + CourtGeometry.RectHeight);
            float xEndL = CourtGeometry.ToWorldX(CourtGeometry.Left9mX);
            float xEndR = CourtGeometry.ToWorldX(CourtGeometry.Right9mX);
            float x3L = CourtGeometry.ToWorldX(CourtGeometry.Left3mX);
            float x3R = CourtGeometry.ToWorldX(CourtGeometry.Right3mX);
            float xNet = CourtGeometry.ToWorldX(CourtGeometry.NetX);

            // Backdrop (vertical gradient) well beyond the visible area so letterbox margins are covered too.
            BuildBackdrop(mat, new Rect(xRectL - 30f, -20f, (xRectR - xRectL) + 60f, yTop + 40f));

            // Floor surfaces
            LineGeometry.Quad(root, "FreeZoneFloor", mat, freeZoneColor, Rect.MinMaxRect(xRectL, -d, xRectR, d), SortFreeZone);
            LineGeometry.Quad(root, "CourtFloor", mat, courtColor, Rect.MinMaxRect(xEndL, -d, xEndR, d), SortCourt);
            LineGeometry.Quad(root, "FrontZoneFloor", mat, frontZoneColor, Rect.MinMaxRect(x3L, -d, x3R, d), SortFrontZone);

            // Play-area (BigRect) outline
            AddLine("PlayAreaOutline", playAreaOutlineColor, lineWidth * 0.6f, SortOutline, true,
                new Vector2(xRectL, 0f), new Vector2(xRectR, 0f), new Vector2(xRectR, yTop), new Vector2(xRectL, yTop));
            AddLine("FreeZoneNearEdge", playAreaOutlineColor, lineWidth * 0.6f, SortOutline, false, new Vector2(xRectL, -d), new Vector2(xRectR, -d));
            AddLine("FreeZoneFarEdge", playAreaOutlineColor, lineWidth * 0.6f, SortOutline, false, new Vector2(xRectL, d), new Vector2(xRectR, d));

            // Court boundary: side lines + end lines
            AddLine("NearSideLine", lineColor, lineWidth, SortLines, false, new Vector2(xEndL, -d), new Vector2(xEndR, -d));
            AddLine("FarSideLine", lineColor, lineWidth, SortLines, false, new Vector2(xEndL, d), new Vector2(xEndR, d));
            AddLine("LeftEndLine", lineColor, lineWidth, SortLines, false, new Vector2(xEndL, -d), new Vector2(xEndL, d));
            AddLine("RightEndLine", lineColor, lineWidth, SortLines, false, new Vector2(xEndR, -d), new Vector2(xEndR, d));
            // Attack (3 m) lines and centre line under the net
            AddLine("LeftAttackLine", lineColor, lineWidth, SortLines, false, new Vector2(x3L, -d), new Vector2(x3L, d));
            AddLine("RightAttackLine", lineColor, lineWidth, SortLines, false, new Vector2(x3R, -d), new Vector2(x3R, d));
            AddLine("CentreLine", lineColor, lineWidth * 1.2f, SortLines, false, new Vector2(xNet, -d), new Vector2(xNet, d));
        }

        private void AddLine(string name, Color color, float width, int order, bool loop, params Vector2[] pts)
        {
            var p3 = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) p3[i] = pts[i];
            LineGeometry.Line(root, name, LineGeometry.ResolveMaterial(lineMaterial), color, width, order, loop, p3);
            LineRendererCount++;
            for (int i = 0; i + 1 < pts.Length; i++) segments.Add(new Segment { name = name, a = pts[i], b = pts[i + 1] });
            if (loop && pts.Length > 2) segments.Add(new Segment { name = name, a = pts[pts.Length - 1], b = pts[0] });
        }

        private void BuildBackdrop(Material mat, Rect area)
        {
            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { name = "SkyGradient", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                tex.SetPixel(0, y, Color.Lerp(skyBottom, skyTop, Mathf.SmoothStep(0f, 1f, t)));
            }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 1f);
            sprite.hideFlags = HideFlags.DontSave;
            var go = new GameObject("Backdrop");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(area.center.x, area.center.y, 0f);
            go.transform.localScale = new Vector3(area.width, area.height / h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = SortBackground;
        }
    }
}
