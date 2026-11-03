#include "StateStorage.h"

void StateStorage::Init()
{
    BallGamePlayState ballGameplayState;
    ballGameplayStatePool.add(ChunkConfig::DEFAULT_MATCH, ballGameplayState);
    MatchState match;
    matchStatePool.add(ChunkConfig::DEFAULT_MATCH, match);
    RallyState rallyState;
    rallyStatePool.add(ChunkConfig::DEFAULT_MATCH, rallyState);
}
