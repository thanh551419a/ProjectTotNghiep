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
inline void UpdateBallDeadRule(IntentStorage* intentStorage, ComponentStorage* componentStorage, Vec2 previousPosBall)
{
    // Take current Pos
    auto ballPos = componentStorage->GetBallPositionPool().get(GameConfig::BALL);

    // take ballState
    auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    // kiểm tra ballPos

    // take MatchState
    auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    // take RallyState
    auto rallyState = componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    if (ballPos->position.y == SystemConfig::MIN_Y && ballState->stateFrame == Alive)
    {
        // AXLOG("Ball chạm đất lần đầu , set thành 50");
        ballState->stateFrame = FrameFlyUntilReset;
    }
    if (rallyState->touchCount > 3)
    {
        ballState->stateFrame = FrameFlyUntilReset;
    }
    if (ballState->stateFrame == FrameFlyUntilReset)
    {
        BallLandingArea ballLanding = GetBallLandingArea(ballPos);
        switch (ballLanding)
        {
        case LEFT_COURT:
            matchState->rightScore++;
            break;
        case RIGHT_COURT:
            matchState->leftScore++;
            break;
        case BALL_OUT:
            (rallyState->lastTouch < ChunkConfig::CHARACTER_PER_MATCH / 2) ? matchState->rightScore++
                                                                           : matchState->leftScore++;
            break;
        default:
            break;
        }
    }
}

void GameRuleEvaluationSystem::update() {
    //Rule 2 , ball Touch > 3 
    auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto matchState = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    auto ballGameplayState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    if (rallyState->touchCount <= MAX_TOUCH_PER_TEAM)
        return; // do nothing , still in rally
    AXLOG("TouchCount = %d , đã hết rally", rallyState->touchCount);
    Team possessionTeam = static_cast<Team>(rallyState->lastTouch / ChunkConfig::CHARACTER_PER_MATCH);

    Team winnerTeam = (possessionTeam == Team::LEFT) ? Team::RIGHT : Team::LEFT;
    
    if (winnerTeam == Team::LEFT)
    {
        matchState->leftScore++;
    }
    else
    {
        matchState->rightScore++;
    }
    if (matchState->servingTeam != winnerTeam)
    {
        matchState->servingTeam = winnerTeam;
        matchState->servingEntity++;

        int cPerSide = ChunkConfig::CHARACTER_PER_MATCH / 2;
        if ((cPerSide * (static_cast<int>(winnerTeam) + 1)) == matchState->servingEntity)
        {
            matchState->servingEntity = cPerSide * static_cast<int>(winnerTeam);
        }
    }

    rallyState->touchCount = 0;
    ballGameplayState->stateFrame = FrameFlyUntilReset;
    ballGameplayState->landingX   = 0;
}
