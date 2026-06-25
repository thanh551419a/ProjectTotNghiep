#pragma once

enum class TrajectoryType
{
    Linear,
    Parabolic
};

struct BallTrajectoryComponent
{
    TrajectoryType type;
    float speed;
    float a;
    float b;
    float c;
};
