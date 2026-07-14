#include "FramePreparationSystem.h"

FramePreparationSystem::FramePreparationSystem(ComponentStorage* c, IntentStorage* i)
{
    _componentStorage = c;
    _intentStorage    = i;
}

namespace
{
inline void AttachBallToPlayer(ComponentStorage* componentStorage, Entity entity)
{
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    if (ballState->stateFrame != Reset)
        return;

    auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    auto ballPos  = componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto charPos  = componentStorage->GetCharacterPositionPool().get(entity);
    auto charSize = componentStorage->GetCharacterSizePool().get(entity);

    constexpr float GAP = 5.0f;

    float charCenterY = charPos->position.y + charSize->size.y * 0.5f;

    if (matchState->servingTeam == Team::LEFT)
    {
        ballPos->position = {charPos->position.x + charSize->size.x + GAP, charCenterY};
    }
    else
    {
        ballPos->position = {charPos->position.x - BallSize.x - GAP, charCenterY};
    }
}
inline void ResetBall(ComponentStorage* componentStorage)
{
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    if (ballState->stateFrame != Reset)
        return;

    auto ballTrajectory = componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);

    ballTrajectory->type  = TrajectoryType::Parabolic;
    ballTrajectory->a     = 0.0f;
    ballTrajectory->b     = 0.0f;
    ballTrajectory->c     = 0.0f;
    ballTrajectory->speed = 0.0f;

    ballState->landingX = 0;
}

inline void PrepareBall(ComponentStorage* componentStorage)
{
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    if (ballState->stateFrame != Reset)
        return;

    auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    AttachBallToPlayer(componentStorage, GameConfig::PLAYER);

    ResetBall(componentStorage);
}

inline void ResetCharacters(ComponentStorage* componentStorage)
{
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    if (ballState->stateFrame != FrameFlyUntilReset)
        return;

    auto& positionPool = componentStorage->GetCharacterPositionPool();
    auto matchState    = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    float left   = SystemConfig::offsetX;
    float bottom = SystemConfig::offsetY;

    float netX   = left + BigRect::RECT_WIDTH * 0.5f;
    float floorY = bottom;

    for (Entity i = GameConfig::PLAYER; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
    {
        auto pos = positionPool.get(i);

        if (!pos)
            continue;

        if (i < ChunkConfig::CHARACTER_PER_MATCH / 2)
        {
            constexpr float spacing = 180.0f;

            pos->position.x = netX - 400.0f + i * spacing;
            pos->position.y = floorY;
        }
        else
        {
            constexpr float spacing = 180.0f;

            pos->position.x = netX + 120.0f + (i - 3) * spacing;
            pos->position.y = floorY;
        }
    }

    auto servingPos = positionPool.get(matchState->servingEntity);

    if (!servingPos)
        return;

    if (matchState->servingTeam == Team::LEFT)
    {
        servingPos->position.x = BigRect::LEFT_POSITION_RESET_X;
    }
    else
    {
        servingPos->position.x = BigRect::RIGHT_POSITION_RESET_X;
    }
}
}  // namespace

void FramePreparationSystem::update()
{
    ResetCharacters(_componentStorage);
    PrepareBall(_componentStorage);
}
