#pragma once
#include "TrajectoryData.h"


namespace TrajectoryConfig
{
    constexpr int DISTANCE_COUNT = 5;
    constexpr int ANGLE_COUNT    = 5;
    constexpr float DECREASE_C_FOR_BUMP = 10.0f;
    constexpr float DECREASE_C_FOR_SET  = 40.0f;
    constexpr float DECREASE_C_FOR_SPIKE = 0.0f;
    constexpr float DECREASE_C_FOR_SPIKETEMP = 20.0f;
    // =====================================================
    // DISTANCE RANGE
    //
    // D1 : 56% - 59%
    // D2 : 59% - 62%
    // D3 : 62% - 65%
    // D4 : 65% - 68%
    // D5 : 68% - 70%
    // =====================================================

    constexpr float MIN_DISTANCE  = 20.0f;
    constexpr float DISTANCE_STEP = 8.0f;

    // =====================================================
    // ANGLE RANGE
    //
    // A1 : 55° - 63°
    // A2 : 63° - 71°
    // A3 : 71° - 79°
    // A4 : 79° - 87°
    // A5 : 87° - 95°
    // =====================================================

    constexpr float MIN_ANGLE  = 0.0f;
    constexpr float ANGLE_STEP = 50.0f;

    // =====================================================
    // TABLE[D][A]
    // =====================================================

    constexpr TrajectoryData Table[DISTANCE_COUNT][ANGLE_COUNT] = {
        // D1 (56%-59%)
        {{-0.0045f, 100.0f}, {-0.0060f, 110.0f}, {-0.0070f, 120.0f}, {-0.0055f, 105.0f}, {-0.0035f, 90.0f}},

        // D2 (59%-62%)
        {{-0.0055f, 110.0f}, {-0.0075f, 125.0f}, {-0.0090f, 140.0f}, {-0.0070f, 120.0f}, {-0.0045f, 95.0f}},

        // D3 (62%-65%)
        {{-0.0065f, 120.0f}, {-0.0085f, 140.0f}, {-0.0100f, 160.0f}, {-0.0080f, 135.0f}, {-0.0050f, 105.0f}},

        // D4 (65%-68%)
        {{-0.0060f, 115.0f}, {-0.0080f, 135.0f}, {-0.0095f, 155.0f}, {-0.0075f, 130.0f}, {-0.0045f, 100.0f}},

        // D5 (68%-70%)
        {{-0.0050f, 105.0f}, {-0.0070f, 125.0f}, {-0.0080f, 145.0f}, {-0.0065f, 120.0f}, {-0.0040f, 95.0f}}};

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


    inline TrajectoryData Get(float distancePercent, float angle)
    {
        constexpr float MAX_DISTANCE = MIN_DISTANCE + DISTANCE_STEP * DISTANCE_COUNT;  // 60.0f

        if (distancePercent < 0.0f || distancePercent > MAX_DISTANCE)
        {
            return TrajectoryData({0.0f, 0.0f});
        }

        int d = GetDistanceIndex(distancePercent);
        int a = GetAngleIndex(angle);

        return Table[d][a];
    }
    inline TrajectoryData GetByIndex(int x, int y)
    {
        return Table[x][y];
    }

}  // namespace TrajectoryConfig
