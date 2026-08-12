#include "TrajectoryMath.h"

const char* ActionStateToString(ActionState state)
{
    switch (state)
    {
    case ActionState::None:
        return "None";
    case ActionState::Serve:
        return "Serve";
    case ActionState::Bump:
        return "Bump";
    case ActionState::Set:
        return "Set";
    case ActionState::Spike:
        return "Spike";
    case ActionState::SpikeLight:
        return "SpikeLight";
    case ActionState::SpikeMedium:
        return "SpikeMedium";
    case ActionState::SpikeStrong:
        return "SpikeStrong";
    default:
        return "Unknown";
    }
}

Team GetEntityTeam1(Entity entity)
{
    if (entity >= GameConfig::PLAYER && entity <= GameConfig::TEAMMATE_2)
    {
        return Team::LEFT;
    }

    if (entity >= GameConfig::OPPONENT_1 && entity <= GameConfig::OPPONENT_3)
    {
        return Team::RIGHT;
    }

    return Team::NONE;
}

DetectionResult DetectPlayerBall(const ObjectData* data, int characterIndex, int ballIndex, Size PercentChange)
{
    // CENTER
    Size SizeDelta;
    SizeDelta.x              = (data[characterIndex].size.x * PercentChange.x) / 100;
    SizeDelta.y              = (data[characterIndex].size.y * PercentChange.y) / 100;
    ax::Vec2 CharacterCenter = {data[characterIndex].pos.x + data[characterIndex].size.x * 0.5f + SizeDelta.x / 2,
                                data[characterIndex].pos.y + data[characterIndex].size.y * 0.5f + SizeDelta.y / 2};

    ax::Vec2 ballCenter = {data[ballIndex].pos.x + data[ballIndex].size.x * 0.5f,
                           data[ballIndex].pos.y + data[ballIndex].size.y * 0.5f};

    // VECTOR PLAYER -> BALL
    ax::Vec2 dir = ballCenter - CharacterCenter;

    // DISTANCE
    float distance = dir.length();

    float distancePercent = 0.0f;
    if (data[characterIndex].size.y > 0.0f)
    {
        distancePercent = distance / data[characterIndex].size.y * 100.0f;
    }

    // ANGLE (atan2 trả [-180,180], đổi sang [0,360))
    float angle = std::atan2(dir.y, dir.x) * 180.0f / static_cast<float>(M_PI);
    if (angle < 0.0f)
    {
        angle += 360.0f;
    }

    return {distancePercent, angle};
}

float CalculateC(float v0)
{
    return SystemConfig::MAX_C - (v0 - SystemConfig::MIN_V0) * SystemConfig::RATIO_C;
}

float CalculateB(float a, float c, float x0, float y0, int direction, int isSpike)
{
    float delta = sqrt((y0 - c) / a);

    float largerRoot  = delta - x0;
    float smallerRoot = -delta - x0;

    if (direction == -1)
        return isSpike ? smallerRoot : largerRoot;
    else  // direction == -1
        return isSpike ? largerRoot : smallerRoot;
}

Vec2 CalculateLandingPoint(const Vec2& startPos, BallTrajectoryComponent* trajectory, float groundY)
{
    if (trajectory->a == 0.0f)
    {
        return startPos;
    }

    float value = (groundY - trajectory->c) / trajectory->a;

    if (value < 0.0f)
    {
        return startPos;
    }

    float root = sqrtf(value);

    float x1 = -trajectory->b + root;
    float x2 = -trajectory->b - root;

    float landingX = (trajectory->speed > 0.0f) ? std::max(x1, x2) : std::min(x1, x2);

    return {landingX, groundY};
}
