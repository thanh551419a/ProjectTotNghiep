using System.Collections.Generic;
using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Draws the volleyball court and medieval/stadium arena backdrop procedurally.
    /// Every court coordinate comes from the native court configuration mirrored in CourtGeometry and is
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
        [SerializeField] private Color lineColor = new Color(0.98f, 0.96f, 0.90f, 1f);
        [SerializeField] private Color courtColor = new Color(0.82f, 0.49f, 0.24f, 1f);
        [SerializeField] private Color frontZoneColor = new Color(0.90f, 0.57f, 0.30f, 1f);
        [SerializeField] private Color freeZoneColor = new Color(0.14f, 0.27f, 0.38f, 1f);
        [SerializeField] private Color playAreaOutlineColor = new Color(0.55f, 0.80f, 1f, 0.22f);
        [SerializeField] private Color skyTop = new Color(0.04f, 0.07f, 0.15f, 1f);
        [SerializeField] private Color skyBottom = new Color(0.16f, 0.22f, 0.36f, 1f);

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
            BuildArenaArchitecture(mat, xRectL, xRectR, xEndL, xEndR, xNet, yTop, d);

            // Floor surfaces + hardwood plank striping
            LineGeometry.Quad(root, "FreeZoneFloor", mat, freeZoneColor, Rect.MinMaxRect(xRectL, -d, xRectR, d), SortFreeZone);
            LineGeometry.Quad(root, "CourtFloor", mat, courtColor, Rect.MinMaxRect(xEndL, -d, xEndR, d), SortCourt);
            LineGeometry.Quad(root, "FrontZoneFloor", mat, frontZoneColor, Rect.MinMaxRect(x3L, -d, x3R, d), SortFrontZone);
            BuildHardwoodFloorDetailing(mat, xEndL, xEndR, x3L, x3R, xNet, d);

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

        private void BuildHardwoodFloorDetailing(Material mat, float xEndL, float xEndR, float x3L, float x3R, float xNet, float d)
        {
            Color plankLight = new Color(1f, 0.88f, 0.68f, 0.10f);
            Color plankDark = new Color(0.28f, 0.14f, 0.05f, 0.10f);
            Color floorGloss = new Color(1f, 0.96f, 0.85f, 0.08f);

            int plankCount = 12;
            float bandH = (2f * d) / plankCount;
            for (int i = 0; i < plankCount; i++)
            {
                float y0 = -d + i * bandH;
                float y1 = y0 + bandH * 0.82f;
                Color c = (i % 2 == 0) ? plankLight : plankDark;
                LineGeometry.Quad(root, "FloorPlank_" + i, mat, c, Rect.MinMaxRect(xEndL, y0, xEndR, y1), SortFrontZone + 1);
            }

            // Stadium overhead light reflection sheen on the hardwood court
            LineGeometry.Quad(root, "FloorCenterSheen", mat, floorGloss,
                Rect.MinMaxRect(x3L - 1.2f, -d * 0.55f, x3R + 1.2f, d * 0.55f), SortFrontZone + 2);
            LineGeometry.Quad(root, "FloorNetShadow", mat, new Color(0.04f, 0.06f, 0.10f, 0.24f),
                Rect.MinMaxRect(xNet - 0.18f, -d, xNet + 0.18f, d), SortFrontZone + 3);
        }

        private void BuildArenaArchitecture(Material mat, float xRectL, float xRectR, float xEndL, float xEndR, float xNet, float yTop, float d)
        {
            Color deepStone = new Color(0.08f, 0.11f, 0.19f, 1f);
            Color midStone = new Color(0.12f, 0.16f, 0.26f, 1f);
            Color pillarStone = new Color(0.17f, 0.22f, 0.34f, 1f);
            Color trussSteel = new Color(0.14f, 0.19f, 0.29f, 1f);
            Color torchGlow = new Color(0.98f, 0.68f, 0.28f, 0.18f);
            Color spotlightCore = new Color(1.00f, 0.97f, 0.86f, 0.95f);
            Color spotlightBeam = new Color(0.76f, 0.90f, 1.00f, 0.055f);
            Color leftBanner = new Color(0.14f, 0.54f, 0.90f, 0.90f);
            Color rightBanner = new Color(0.86f, 0.22f, 0.20f, 0.90f);
            Color bannerTrim = new Color(0.98f, 0.78f, 0.30f, 0.94f);
            Color underFloor = new Color(0.06f, 0.08f, 0.14f, 1f);
            Color bleacherStep = new Color(0.10f, 0.14f, 0.23f, 0.96f);
            Color crowdSilhouette = new Color(0.05f, 0.07f, 0.13f, 0.92f);

            // Sub-floor foundation below the court band + apron trim
            LineGeometry.Quad(root, "SubFloorFoundation", mat, underFloor, Rect.MinMaxRect(xRectL - 10f, -12f, xRectR + 10f, -d), SortFreeZone - 5);
            LineGeometry.Quad(root, "SubFloorApronTrim", mat, new Color(0.22f, 0.34f, 0.50f, 0.55f), Rect.MinMaxRect(xRectL, -d - 0.14f, xRectR, -d), SortFreeZone - 2);

            // Distant stadium upper bowl & lower gallery
            float wallTop = yTop * 0.64f;
            LineGeometry.Quad(root, "ArenaFarWall", mat, deepStone, Rect.MinMaxRect(xRectL - 6f, d, xRectR + 6f, wallTop), SortBackground + 15);
            LineGeometry.Quad(root, "ArenaLowerGallery", mat, midStone, Rect.MinMaxRect(xRectL - 4f, d, xRectR + 4f, yTop * 0.36f), SortBackground + 25);

            // Tiered spectator bleachers & crowd silhouettes along the lower gallery
            int tierCount = 4;
            float tierBaseY = d + 0.25f;
            float tierStepH = (yTop * 0.32f - tierBaseY) / tierCount;
            for (int t = 0; t < tierCount; t++)
            {
                float ty0 = tierBaseY + t * tierStepH;
                float ty1 = ty0 + tierStepH * 0.65f;
                LineGeometry.Quad(root, "BleacherTier_" + t, mat, bleacherStep,
                    Rect.MinMaxRect(xEndL - 3.0f, ty0, xEndR + 3.0f, ty1), SortBackground + 27);
            }

            int spectatorCount = 36;
            for (int s = 0; s < spectatorCount; s++)
            {
                float st = s / (float)(spectatorCount - 1);
                float sx = Mathf.Lerp(xEndL - 2.6f, xEndR + 2.6f, st);
                int row = s % tierCount;
                float sy = tierBaseY + row * tierStepH + tierStepH * 0.35f;
                float headH = 0.28f + (s % 3) * 0.05f;
                LineGeometry.Quad(root, "Spectator_" + s, mat, crowdSilhouette,
                    Rect.MinMaxRect(sx - 0.14f, sy, sx + 0.14f, sy + headH), SortBackground + 28);
            }

            // Warm horizon court-side LED / torchlight glow above the court floor
            LineGeometry.Quad(root, "ArenaHorizonGlow", mat, torchGlow, Rect.MinMaxRect(xEndL - 2.5f, d, xEndR + 2.5f, d + 1.45f), SortBackground + 30);
            LineGeometry.Quad(root, "CourtsideAdBoard", mat, new Color(0.11f, 0.18f, 0.30f, 0.95f),
                Rect.MinMaxRect(xEndL - 1.5f, d, xEndR + 1.5f, d + 0.42f), SortBackground + 31);
            LineGeometry.Quad(root, "CourtsideAdBoardTrim", mat, bannerTrim,
                Rect.MinMaxRect(xEndL - 1.5f, d + 0.40f, xEndR + 1.5f, d + 0.45f), SortBackground + 32);

            // Stadium colonnade pillars & battlements
            int pillarCount = 9;
            for (int i = 0; i < pillarCount; i++)
            {
                float t = i / (float)(pillarCount - 1);
                float px = Mathf.Lerp(xEndL - 3.2f, xEndR + 3.2f, t);
                LineGeometry.Quad(root, "ArenaPillar_" + i, mat, pillarStone,
                    Rect.MinMaxRect(px - 0.28f, d, px + 0.28f, wallTop + 0.45f), SortBackground + 35);

                if (i < pillarCount - 1)
                {
                    float mx = (px + Mathf.Lerp(xEndL - 3.2f, xEndR + 3.2f, (i + 1) / (float)(pillarCount - 1))) * 0.5f;
                    LineGeometry.Quad(root, "Battlement_" + i, mat, deepStone,
                        Rect.MinMaxRect(mx - 0.45f, wallTop, mx + 0.45f, wallTop + 0.32f), SortBackground + 20);
                }
            }

            // Overhead stadium roof catwalk truss & volumetric spotlight rigs
            float roofY = yTop * 0.88f;
            LineGeometry.Quad(root, "StadiumRoofTruss", mat, trussSteel,
                Rect.MinMaxRect(xRectL - 4f, roofY - 0.22f, xRectR + 4f, roofY + 0.22f), SortBackground + 38);

            int lightCount = 6;
            for (int l = 0; l < lightCount; l++)
            {
                float lt = l / (float)(lightCount - 1);
                float lx = Mathf.Lerp(xEndL - 1.2f, xEndR + 1.2f, lt);
                // Volumetric light cone reaching down toward the court
                LineGeometry.Quad(root, "SpotlightBeamOuter_" + l, mat, spotlightBeam,
                    Rect.MinMaxRect(lx - 1.35f, d, lx + 1.35f, roofY - 0.22f), SortBackground + 36);
                LineGeometry.Quad(root, "SpotlightBeamInner_" + l, mat, spotlightBeam,
                    Rect.MinMaxRect(lx - 0.65f, d, lx + 0.65f, roofY - 0.22f), SortBackground + 37);
                // Lamp housing & bright LED core
                LineGeometry.Quad(root, "SpotlightHousing_" + l, mat, trussSteel,
                    Rect.MinMaxRect(lx - 0.48f, roofY - 0.38f, lx + 0.48f, roofY - 0.16f), SortBackground + 39);
                LineGeometry.Quad(root, "SpotlightLamp_" + l, mat, spotlightCore,
                    Rect.MinMaxRect(lx - 0.36f, roofY - 0.42f, lx + 0.36f, roofY - 0.34f), SortBackground + 40);
            }

            // Team heraldic banners flanking the centre net line
            float bannerTop = wallTop - 0.20f;
            float bannerBottom = yTop * 0.20f;
            float leftBx = Mathf.Lerp(xEndL, xNet, 0.52f);
            float rightBx = Mathf.Lerp(xNet, xEndR, 0.48f);
            LineGeometry.Quad(root, "LeftTeamBannerTrim", mat, bannerTrim, Rect.MinMaxRect(leftBx - 0.48f, bannerBottom - 0.06f, leftBx + 0.48f, bannerTop + 0.06f), SortBackground + 42);
            LineGeometry.Quad(root, "LeftTeamBanner", mat, leftBanner, Rect.MinMaxRect(leftBx - 0.40f, bannerBottom, leftBx + 0.40f, bannerTop), SortBackground + 45);
            LineGeometry.Quad(root, "RightTeamBannerTrim", mat, bannerTrim, Rect.MinMaxRect(rightBx - 0.48f, bannerBottom - 0.06f, rightBx + 0.48f, bannerTop + 0.06f), SortBackground + 42);
            LineGeometry.Quad(root, "RightTeamBanner", mat, rightBanner, Rect.MinMaxRect(rightBx - 0.40f, bannerBottom, rightBx + 0.40f, bannerTop), SortBackground + 45);
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
            Color horizonWarm = new Color(0.24f, 0.19f, 0.32f, 1f);
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { name = "SkyGradient", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                Color c = t < 0.35f
                    ? Color.Lerp(skyBottom, horizonWarm, Mathf.SmoothStep(0f, 1f, t / 0.35f))
                    : Color.Lerp(horizonWarm, skyTop, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.65f));
                tex.SetPixel(0, y, c);
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
