#pragma once

struct TrajectoryData
{
    float a = 0.0f;
    float v0 = 0.0f;

    constexpr TrajectoryData() = default;
    constexpr TrajectoryData(float a_, float v0_) : a(a_), v0(v0_) {}
};

struct TrajectoryBoost
{
    float v0Multiplier = 1.0f;
    float aFlattenFactor = 1.0f;

    constexpr TrajectoryBoost() = default;
    constexpr TrajectoryBoost(float v, float a) : v0Multiplier(v), aFlattenFactor(a) {}
};
