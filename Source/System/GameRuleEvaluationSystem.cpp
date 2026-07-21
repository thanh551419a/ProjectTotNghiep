#include "GameRuleEvaluationSystem.h"
GameRuleEvaluationSystem::GameRuleEvaluationSystem(ComponentStorage* c, IntentStorage* i) {
    _componentStorage = c;
    _intentStorage    = i;
}
enum BallLandingArea
{
    BALL_OUT    = 0,
    LEFT_COURT  = 1,
    RIGHT_COURT = 2
};
inline BallLandingArea GetBallLandingArea(PositionComponent* ballPos)
{
    if (ballPos == nullptr)
        return BALL_OUT;
    const float x = ballPos->position.x;
    if (x < BigRect::LEFT_9M_LINE_X || x > BigRect::RIGHT_9M_LINE_X)
    {
        return BALL_OUT;
    }
    if (x < BigRect::NET_X)
    {
        return LEFT_COURT;
    }
    return RIGHT_COURT;
}
inline void HandleBallState(BallGamePlayState* ballState)
{
    ballState->stateFrame = FrameFlyUntilReset;
    ballState->landingX   = 0;
}

inline void HandleMatchState(MatchState* matchState, Team winnerTeam)
{
    // Update Score
    if (winnerTeam == Team::LEFT)
        matchState->leftScore++;
    else
        matchState->rightScore++;

    // Update Serve
    AXLOG("[GameRule] MatchState ptr : %p", matchState);
    if (matchState->servingTeam != winnerTeam)
    {
        matchState->servingTeam = winnerTeam;

        if (winnerTeam == Team::LEFT)
        {
            matchState->leftServingEntity++;

            if (matchState->leftServingEntity > GameConfig::TEAMMATE_2)
            {
                matchState->leftServingEntity = GameConfig::PLAYER;
            }

            AXLOG("Left Serving Entity: %d", matchState->leftServingEntity);
        }
        else
        {
            matchState->rightServingEntity++;

            if (matchState->rightServingEntity > GameConfig::OPPONENT_3)
            {
                matchState->rightServingEntity = GameConfig::OPPONENT_1;
            }

            AXLOG("Right Serving Entity: %d", matchState->rightServingEntity);
        }
    }
}

inline void ResetRallyState(RallyState* rallyState)
{
    rallyState->Reset();
}
inline void ResolveRallyEnd(ComponentStorage* componentStorage, Team winnerTeam)
{
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    auto rallyState = componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);

    HandleMatchState(matchState, winnerTeam);

    HandleBallState(ballState);

    ResetRallyState(rallyState);
}

void GameRuleEvaluationSystem::update() {
   
    auto ballState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

    auto matchState = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);

    auto ballPos = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    
    if (ballState->stateFrame != -1)
        return;

    if (rallyState->touchCount > MAX_TOUCH_PER_TEAM)
    {
        AXLOG("TouchCount = %d", rallyState->touchCount);

        Team possessionTeam = static_cast<Team>(rallyState->lastTouch / (ChunkConfig::CHARACTER_PER_MATCH / 2));

        Team winnerTeam = (possessionTeam == Team::LEFT) ? Team::RIGHT : Team::LEFT;

        ResolveRallyEnd(_componentStorage, winnerTeam);

        return;
    }

    if (ballPos->position.y <= MIN_Y)
    {
        Team winnerTeam;

        switch (GetBallLandingArea(ballPos))
        {
        case LEFT_COURT:
            winnerTeam = Team::RIGHT;
            break;

        case RIGHT_COURT:
            winnerTeam = Team::LEFT;
            break;

        case BALL_OUT:
            winnerTeam = (rallyState->lastTouch < ChunkConfig::CHARACTER_PER_MATCH / 2) ? Team::RIGHT : Team::LEFT;
            break;

        default:
            return;
        }

        ResolveRallyEnd(_componentStorage, winnerTeam);

        return;
    }
}
