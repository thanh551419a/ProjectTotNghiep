#pragma once

struct TrajectoryData
{
    float a = 0.0f;
    float v0 = 0.0f;

    constexpr TrajectoryData() = default;
    constexpr TrajectoryData(float a_, float v0_) : a(a_), v0(v0_) {}
};
