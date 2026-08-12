#include "TrajectoryUpdate.h"

void UpdateLandingX(ComponentStorage* componentStorage)
{
    auto& trajectoryPool       = componentStorage->GetBallTrajectoryPool();
    auto& positionPool         = componentStorage->GetBallPositionPool();
    auto& BallGamelayStatePool = componentStorage->GetBallGameplayStatePool();

    auto ballTrajectory    = trajectoryPool.get(GameConfig::BALL);
    auto ballPosition      = positionPool.get(GameConfig::BALL);
    auto ballGameplayState = BallGamelayStatePool.get(DEFAULT_MATCH);

    Vec2 landingPos = CalculateLandingPoint(ballPosition->position, ballTrajectory, SystemConfig::MIN_Y);

    ballGameplayState->landingX = landingPos.x;
}

void UpdateNewTrajectory(const TrajectoryData& trajectory,
                         ComponentStorage* componentStorage,
                         int reverseDirection,
                         int decreaseC,
                         int powerSpike,
                         TrajectoryBoost trajectoryBoost)
{
    auto& ballTrajectoryPool = componentStorage->GetBallTrajectoryPool();
    auto& posBallPool        = componentStorage->GetBallPositionPool();

    auto ballPos        = posBallPool.get(GameConfig::BALL);
    auto ballTrajectory = ballTrajectoryPool.get(GameConfig::BALL);

    ballTrajectory->type = TrajectoryType::Parabolic;
    ballTrajectory->a    = trajectory.a / trajectoryBoost.aFlattenFactor;

    float c = CalculateC(trajectory.v0);
    if (ballPos->position.y > c)
    {
        c = ballPos->position.y;
    }
    ballTrajectory->c     = c - c * decreaseC / 100;
    ballTrajectory->b     = CalculateB(ballTrajectory->a, ballTrajectory->c, ballPos->position.x, ballPos->position.y,
                                       reverseDirection, powerSpike);
    ballTrajectory->speed = reverseDirection * (trajectory.v0 / 100.0f) * 4.0f * trajectoryBoost.v0Multiplier;

    UpdateLandingX(componentStorage);
}

bool TryApplyTrajectory(ActionState state,
                        float distancePercent,
                        float distanceThreshold,
                        bool inclusive,
                        const TrajectoryData& trajectoryData,
                        TrajectoryBoost boost,
                        int decreaseC,
                        int powerSpike,
                        int direction,
                        ComponentStorage* componentStorage,
                        ActionState& lastEvent)
{
    bool withinRange = inclusive ? (distancePercent <= distanceThreshold) : (distancePercent < distanceThreshold);
    if (!withinRange)
        return false;

    lastEvent = state;
    UpdateNewTrajectory(trajectoryData, componentStorage, direction, decreaseC, powerSpike, boost);
    return true;
}
