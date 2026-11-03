using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Native court configuration mirrored from the authoritative C++ sources, plus the ONE
    /// native-pixel -> Unity-world conversion used by every renderer (characters, ball, net,
    /// court lines, camera framing).
    ///
    /// Source of every constant (verified 2026-10-23):
    ///   SystemConf.h   : PIXELS_PER_METER = 80, offsetX = 10, offsetY = 265 (MIN_Y = 265)
    ///   BigRect.h      : RECT_WIDTH = 2720, RECT_HEIGHT = 1000, RECT_X = offsetX, RECT_Y = offsetY,
    ///                    NET_X = RECT_X + RECT_WIDTH/2 = 1370,
    ///                    LEFT/RIGHT_3M_LINE_X = NET_X -/+ 3*PPM = 1130 / 1610,
    ///                    LEFT/RIGHT_9M_LINE_X = NET_X -/+ 9*PPM = 650 / 2090
    ///   MatchObjectConfig.h : NETSIZE = (0.1*PPM, 2.8*PPM) = (8, 224),
    ///                    NETPOSITION = (offsetX + RECT_WIDTH/2 - NETSIZE.x/2, offsetY) = (1366, 265),
    ///                    BallSize = 0.21*PPM = 16.8
    ///
    /// Conversion convention: origin = net centre on the floor (NET_X, FLOOR_Y); +x right, +y up;
    /// 1 Unity unit = 1 metre = 80 native pixels. Native positions are AABB bottom-left corners.
    /// </summary>
    public static class CourtGeometry
    {
        public const float PixelsPerMeter = 80f;
        public const float RectX = 10f;
        public const float RectY = 265f;
        public const float RectWidth = 2720f;
        public const float RectHeight = 1000f;
        public const float FloorY = RectY;
        public const float NetX = RectX + RectWidth * 0.5f;                  // 1370
        public const float Left3mX = NetX - 3f * PixelsPerMeter;              // 1130
        public const float Right3mX = NetX + 3f * PixelsPerMeter;             // 1610
        public const float Left9mX = NetX - 9f * PixelsPerMeter;              // 650
        public const float Right9mX = NetX + 9f * PixelsPerMeter;             // 2090
        public const float NetWidthPx = 0.1f * PixelsPerMeter;                // 8
        public const float NetHeightPx = 2.8f * PixelsPerMeter;               // 224
        public const float NetPosX = RectX + RectWidth * 0.5f - NetWidthPx * 0.5f; // 1366
        public const float NetPosY = RectY;                                   // 265
        public const float BallSizePx = 0.21f * PixelsPerMeter;               // 16.8

        /// <summary>Visual depth (world units) of the drawn court floor band around the floor line.</summary>
        public const float CourtBandHalfDepth = 0.75f;

        public static float ToWorldX(float simX) => (simX - NetX) / PixelsPerMeter;
        public static float ToWorldY(float simY) => (simY - FloorY) / PixelsPerMeter;
        public static float ToWorldLength(float px) => px / PixelsPerMeter;
        public static Vector2 ToWorld(float simX, float simY) => new Vector2(ToWorldX(simX), ToWorldY(simY));

        /// <summary>World-space rectangle of the native BigRect play area.</summary>
        public static Rect BigRectWorld =>
            Rect.MinMaxRect(ToWorldX(RectX), ToWorldY(RectY), ToWorldX(RectX + RectWidth), ToWorldY(RectY + RectHeight));

        /// <summary>
        /// Everything that must stay visible: the BigRect play area plus the drawn court band below the floor.
        /// </summary>
        public static Rect RequiredVisibleWorld
        {
            get
            {
                Rect r = BigRectWorld;
                return Rect.MinMaxRect(r.xMin, r.yMin - CourtBandHalfDepth, r.xMax, r.yMax);
            }
        }
    }
}
