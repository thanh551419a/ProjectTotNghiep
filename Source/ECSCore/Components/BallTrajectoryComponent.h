#pragma once

enum class TrajectoryType
{
    Linear,
    Parabolic
};

struct BallTrajectoryComponent
{
    TrajectoryType type;

    float a;
    float b;
    float c;
};
