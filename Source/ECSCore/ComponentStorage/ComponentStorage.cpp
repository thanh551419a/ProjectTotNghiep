#include "ComponentStorage.h"
#include "axmol.h"

void ComponentStorage::InitComponent()
{
    // =========================================================
    // CHARACTER + NET
    // =========================================================

    
    float left  = SystemConfig::offsetX;
    float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

    float bottom = SystemConfig::offsetY;
    float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

    float netX   = left + BigRect::RECT_WIDTH * 0.5f;
    float floorY = bottom;
    for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; i++)
    {
        PositionComponent posTemp;
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
        // =========================================================
        // POSITION
        // =========================================================

      // =========================================================
        // POSITION
        // Default Formation : 0 2 1 | 4 5 3
        // =========================================================

        constexpr float spacing = 180.0f;

        switch (i)
        {
        // ---------- LEFT ----------
        case GameConfig::PLAYER:  // slot 0
            posTemp.position = Vec2(netX - 400.0f, floorY);
            break;

        case GameConfig::TEAMMATE_2:  // slot 1
            posTemp.position = Vec2(netX - 400.0f + spacing, floorY);
            break;

        case GameConfig::TEAMMATE_1:  // slot 2
            posTemp.position = Vec2(netX - 400.0f + spacing * 2, floorY);
            break;

        // ---------- RIGHT ----------
        case GameConfig::OPPONENT_2:  // slot 0
            posTemp.position = Vec2(netX + 120.0f, floorY);
            break;

        case GameConfig::OPPONENT_3:  // slot 1
            posTemp.position = Vec2(netX + 120.0f + spacing, floorY);
            break;

        case GameConfig::OPPONENT_1:  // slot 2
            posTemp.position = Vec2(netX + 120.0f + spacing * 2, floorY);
            break;
        }
        //

        //AXLOG("add component CharacterStatusComponent cho entity %d, address pointer: %p", i, &tempValue);
        characterPositionPool.add(i, posTemp);
        characterSizePool.add(i, sizeTemp);
    }

    // =========================================================
    // BALL
    // =========================================================

    PositionComponent ballPos;
    ballPos.position = Vec2(netX - MatchObjectConfig::BallSize.x * 0.5f, floorY + 350.0f);

    ballPositionPool.add(GameConfig::BALL, ballPos);
    // ADD jumpUpFrame
    for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
    {
        JumpUpFrameComponent jumpComp;
        jumpComp.remainingFrames = 0;  // ban đầu chưa nhảy
        jumpUpFramePool.add(i, jumpComp);
    }


    BallTrajectoryComponent ballTrajectory;
    ballTrajectory.type = TrajectoryType::Parabolic;
    ballTrajectory.a    = 0.0f;
    ballTrajectory.b    = 0.0f;
    ballTrajectory.c    = 0.0f;
    ballTrajectory.speed = 0.0f;
    ballTrajectoryPool.add(GameConfig::BALL, ballTrajectory);
    InitState();
}
void ComponentStorage::InitState() {
    for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; i++)
    {
        auto tempValue = CharacterActionState(ActionState::None, 0);
        characterActionStatePool.add(i, tempValue);
    }
    BallGamePlayState ballGameplayState;
    ballGameplayStatePool.add(ChunkConfig::DEFAULT_MATCH, ballGameplayState);
    MatchState match;
    matchStatePool.add(DEFAULT_MATCH, match);
    RallyState rallyState;
    rallyStatePool.add(DEFAULT_MATCH, rallyState);
}
