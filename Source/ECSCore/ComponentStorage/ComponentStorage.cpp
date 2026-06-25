#include "ComponentStorage.h"
#include "../../Config/System/SystemConf.h"
#include "../../Config/Match/BigRect.h"
#include "axmol.h"

void ComponentStorage::InitDemo()
{
    // =========================================================
    // CHARACTER + NET
    // =========================================================

    for (int i = 0; i <= GameConfig::NET; i++)
    {
        PositionComponent posTemp;

        float left  = SystemConfig::offsetX;
        float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

        float bottom = SystemConfig::offsetY;
        float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

        float netX   = left + BigRect::RECT_WIDTH * 0.5f;
        float floorY = bottom;

        SizeComponent sizeTemp;

        // =========================================================
        // SIZE
        // =========================================================

        if (i == GameConfig::PLAYER)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::TEAMMATE_1)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.9f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::TEAMMATE_2)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 2.0f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_1)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.5f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_2)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.6f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::OPPONENT_3)
        {
            sizeTemp.size = Vec2(0.5f * SystemConfig::PIXELS_PER_METER, 1.7f * SystemConfig::PIXELS_PER_METER);
        }
        else if (i == GameConfig::NET)
        {
            sizeTemp.size = Vec2(0.1f * SystemConfig::PIXELS_PER_METER, 2.43f * SystemConfig::PIXELS_PER_METER);
        }

        // =========================================================
        // POSITION
        // =========================================================

        if (i == GameConfig::PLAYER || i == GameConfig::TEAMMATE_1 || i == GameConfig::TEAMMATE_2)
        {
            float spacing = 180.0f;

            float x = netX - 400.0f + i * spacing;
            float y = floorY;

            posTemp.position = Vec2(x, y);
        }
        else if (i == GameConfig::OPPONENT_1 || i == GameConfig::OPPONENT_2 || i == GameConfig::OPPONENT_3)
        {
            float spacing = 180.0f;

            float x = netX + 120.0f + (i - 3) * spacing;
            float y = floorY;

            posTemp.position = Vec2(x, y);
        }
        else if (i == GameConfig::NET)
        {
            posTemp.position = Vec2(netX - sizeTemp.size.x * 0.5f, floorY);
        }

        characterPositionPool.add(i, posTemp);
        sizePool.add(i, sizeTemp);
    }

    // =========================================================
    // BALL
    // =========================================================

    float left  = SystemConfig::offsetX;
    float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

    float bottom = SystemConfig::offsetY;
    float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

    float netX   = left + BigRect::RECT_WIDTH * 0.5f;
    float floorY = bottom;

    PositionComponent ballPos;
    ballPos.position = Vec2(netX - GameConfig::BallSize * 0.5f, floorY + 350.0f);

    ballPositionPool.add(GameConfig::BALL, ballPos);
    // ADD jumpUpFrame
    for (int i = GameConfig::PLAYER; i <= GameConfig::OPPONENT_3; ++i)
    {
        JumpUpFrameComponent jumpComp;
        jumpComp.remainingFrames = 0;  // ban đầu chưa nhảy
        JumpUpFramePool.add(i, jumpComp);
    }
    BallTrajectoryComponent ballTrajectory;
    ballTrajectory.type = TrajectoryType::Parabolic;
    ballTrajectory.a    = 0.0f;
    ballTrajectory.b    = 0.0f;
    ballTrajectory.c    = 0.0f;
    ballTrajectory.speed = 0.0f;
    BallTrajectoryPool.add(GameConfig::BALL, ballTrajectory);
    auto k = BallTrajectoryPool.get(GameConfig::BALL);
    AXLOG("Address pointer của BallTrajectoryComponent trong BallTrajectoryPool: %p", k);
}
