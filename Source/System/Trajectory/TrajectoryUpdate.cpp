#include "TrajectoryUpdate.h"
#include "../../Utils/SimLog.h"
#include "../TrajectoryTestConfig.h"

void UpdateLandingX(ComponentStorage* componentStorage)
{
    auto& trajectoryPool       = componentStorage->GetBallTrajectoryPool();
    auto& positionPool         = componentStorage->GetBallPositionPool();
    auto& BallGamelayStatePool = componentStorage->GetBallGameplayStatePool();

    auto ballTrajectory    = trajectoryPool.get(GameConfig::BALL);
    auto ballPosition      = positionPool.get(GameConfig::BALL);
    auto ballGameplayState = BallGamelayStatePool.get(DEFAULT_MATCH);

    SimMath::Vec2 landingPos = CalculateLandingPoint(ballPosition->position, ballTrajectory, SystemConfig::MIN_Y);

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

    // Công thức tính vận tốc pha bóng theo chỉ số tấn công (atk):
    // - Vận tốc áp dụng: appliedV0 = v0_bảng * (atk / 100.0f)
    //   (Ví dụ: vận tốc pha bóng trong bảng v0 = 124, chỉ số tấn công atk = 80 => appliedV0 = 124 * 80% = 99.2)
    const float atk       = TestTrajectory::getInstance().AttackPower;
    const float appliedV0 = trajectory.v0 * (atk / 100.0f);
    ballTrajectory->speed = reverseDirection * (appliedV0 / 100.0f) * 4.0f * trajectoryBoost.v0Multiplier;
    
    UpdateLandingX(componentStorage);

    const float x0 = ballPos->position.x;
    const float y0 = ballPos->position.y;
    const float xVertex = -ballTrajectory->b;
    const bool takesTwoBranches = (reverseDirection > 0) ? (xVertex > x0) : (xVertex < x0);
    const float nextX = x0 + ballTrajectory->speed;
    const float nextY = ballTrajectory->a * (nextX + ballTrajectory->b) * (nextX + ballTrajectory->b) + ballTrajectory->c;
    auto* ballState = componentStorage->GetBallGameplayStatePool().get(ChunkConfig::DEFAULT_MATCH);
    SIM_LOG("[SPIKE_DIAG] UpdateNewTrajectory: dir=%d, atk=%.1f, powerSpike(isSpike)=%d, ballPos=(%.2f,%.2f), "
            "a=%.6f, b=%.2f, xVertex=(-b)=%.2f, c(apexY)=%.2f, speed=%.2f, nextPos=(%.2f,%.2f), landingX=%.2f, "
            "branch=%s",
            reverseDirection, TestTrajectory::getInstance().AttackPower, powerSpike, x0, y0,
            ballTrajectory->a, ballTrajectory->b, xVertex, ballTrajectory->c, ballTrajectory->speed,
            nextX, nextY, ballState ? ballState->landingX : 0.0f,
            takesTwoBranches ? "2_BRANCHES_UP_THEN_DOWN" : "1_BRANCH_DOWNWARD_ONLY");
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
