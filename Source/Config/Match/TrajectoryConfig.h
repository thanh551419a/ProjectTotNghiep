#pragma once
#include "TrajectoryData.h"


namespace TrajectoryConfig
{
constexpr int DISTANCE_COUNT = 5;
constexpr int ANGLE_COUNT    = 5;

// =====================================================
// DISTANCE RANGE
//
// D1 : 56% - 59%
// D2 : 59% - 62%
// D3 : 62% - 65%
// D4 : 65% - 68%
// D5 : 68% - 70%
// =====================================================

constexpr float MIN_DISTANCE  = 56.0f;
constexpr float DISTANCE_STEP = 3.0f;

// =====================================================
// ANGLE RANGE
//
// A1 : 55° - 63°
// A2 : 63° - 71°
// A3 : 71° - 79°
// A4 : 79° - 87°
// A5 : 87° - 95°
// =====================================================

constexpr float MIN_ANGLE  = 55.0f;
constexpr float ANGLE_STEP = 8.0f;

// =====================================================
// TABLE[D][A]
// =====================================================

constexpr TrajectoryData Table[DISTANCE_COUNT][ANGLE_COUNT] = {
    // D1 (56%-59%)
    {{0.45f, 100.0f}, {0.60f, 110.0f}, {0.70f, 120.0f}, {0.55f, 105.0f}, {0.35f, 90.0f}},

    // D2 (59%-62%)
    {{0.55f, 110.0f}, {0.75f, 125.0f}, {0.90f, 140.0f}, {0.70f, 120.0f}, {0.45f, 95.0f}},

    // D3 (62%-65%)
    {{0.65f, 120.0f}, {0.85f, 140.0f}, {1.00f, 160.0f}, {0.80f, 135.0f}, {0.50f, 105.0f}},

    // D4 (65%-68%)
    {{0.60f, 115.0f}, {0.80f, 135.0f}, {0.95f, 155.0f}, {0.75f, 130.0f}, {0.45f, 100.0f}},

    // D5 (68%-70%)
    {{0.50f, 105.0f}, {0.70f, 125.0f}, {0.80f, 145.0f}, {0.65f, 120.0f}, {0.40f, 95.0f}}};

inline int GetDistanceIndex(float distancePercent)
{
    int index = static_cast<int>((distancePercent - MIN_DISTANCE) / DISTANCE_STEP);

    if (index < 0)
        index = 0;

    if (index >= DISTANCE_COUNT)
        index = DISTANCE_COUNT - 1;

    return index;
}

inline int GetAngleIndex(float angle)
{
    int index = static_cast<int>((angle - MIN_ANGLE) / ANGLE_STEP);

    if (index < 0)
        index = 0;

    if (index >= ANGLE_COUNT)
        index = ANGLE_COUNT - 1;

    return index;
}

inline const TrajectoryData& Get(float distancePercent, float angle)
{
    int d = GetDistanceIndex(distancePercent);
    int a = GetAngleIndex(angle);

    return Table[d][a];
}
}  // namespace TrajectoryConfig
