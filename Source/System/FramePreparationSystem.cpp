#include "FramePreparationSystem.h"

FramePreparationSystem::FramePreparationSystem(ComponentStorage* c, IntentStorage* i)
{
    _componentStorage = c;
    _intentStorage    = i;
}

namespace
{
    inline void AttachBallToCharacter(ComponentStorage* componentStorage, Entity entity)
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

        if (matchState->servingTeam == Team::RIGHT)
        {
            ballPos->position = {charPos->position.x - BallSize.x - GAP, charCenterY};
        }
        else
        {
            ballPos->position = {charPos->position.x + charSize->size.x + GAP, charCenterY};
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

        if (matchState -> servingTeam ==  Team::LEFT) AttachBallToCharacter(componentStorage, GameConfig::PLAYER);
        else if (matchState->servingTeam == Team::RIGHT) AttachBallToCharacter(componentStorage, GameConfig::OPPONENT_1);

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

        constexpr float spacing = 180.0f;

        // ================= LEFT =================
        {
            int serve = matchState->leftServingEntity - GameConfig::PLAYER;

            Entity order[3] = {
                static_cast<Entity>(GameConfig::PLAYER + serve),
                static_cast<Entity>(GameConfig::PLAYER + (serve + 2) % 3),  // serve - 1
                static_cast<Entity>(GameConfig::PLAYER + (serve + 1) % 3)   // serve + 1
            };

            for (int slot = 0; slot < 3; ++slot)
            {
                positionPool.get(order[slot])->position = {netX - 400.0f + slot * spacing, floorY};
            }
        }

        // ================= RIGHT =================
        {
            int serve = matchState->rightServingEntity - GameConfig::OPPONENT_1;

            Entity order[3] = {static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 1) % 3),
                               static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 2) % 3),
                               static_cast<Entity>(GameConfig::OPPONENT_1 + serve)};

            for (int slot = 0; slot < 3; ++slot)
            {
                positionPool.get(order[slot])->position = {netX + 120.0f + slot * spacing, floorY};
            }
        }
    }
    inline void SetServingEntityPosition(ComponentStorage* componentStorage)
    {
        auto matchState    = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        auto& positionPool = componentStorage->GetCharacterPositionPool();

        Entity servingEntity;
        float servingX;

        if (matchState->servingTeam == Team::LEFT)
        {
            servingEntity = matchState->leftServingEntity;
            servingX      = BigRect::LEFT_POSITION_RESET_X;
        }
        else
        {
            servingEntity = matchState->rightServingEntity;
            servingX      = BigRect::RIGHT_POSITION_RESET_X;
        }

        auto pos = positionPool.get(servingEntity);
        if (pos == nullptr)
            return;

        pos->position.x = servingX;
    }
    inline void SetServingEntityPositionVer2(ComponentStorage* componentStorage) {
        auto& positionPool = componentStorage->GetCharacterPositionPool();
        auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        Entity servingEntity;
        float servingX;
        if (matchState->servingTeam == Team::LEFT)
        {
            servingEntity = GameConfig::PLAYER;
            servingX      = BigRect::LEFT_POSITION_RESET_X;
        }
        else
        {
            servingEntity = GameConfig::OPPONENT_1;
            servingX      = BigRect::RIGHT_POSITION_RESET_X;
        }
        auto pos = positionPool.get(servingEntity);
        if (pos == nullptr)
            return;

        pos->position.x = servingX;
    }
    }  // namespace

void FramePreparationSystem::update()
{
    int stateFrame = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH)->stateFrame;
    if (stateFrame == FrameFlyUntilReset)
    {
        ResetCharacters(_componentStorage);
        SetServingEntityPositionVer2(_componentStorage);
    }
    
    PrepareBall(_componentStorage);
}
