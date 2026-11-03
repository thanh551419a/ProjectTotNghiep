#pragma once

#include "../ComponentPools/ComponentPool.h"
#include "../States/BallGamePlayState.h"
#include "../States/MatchState.h"
#include "../States/RallyState.h"
#include "../../Config/ECS/ChunkConfig.h"
#include "../../Config/Match/MatchEntityConfig.h"

class StateStorage
{
public:
    ComponentPool<BallGamePlayState> ballGameplayStatePool;
    ComponentPool<MatchState> matchStatePool;
    ComponentPool<RallyState> rallyStatePool;

public:
    ComponentPool<BallGamePlayState>& GetBallGameplayStatePool() { return ballGameplayStatePool; }
    ComponentPool<MatchState>& GetMatchGamePlayStatePool() { return matchStatePool; }
    ComponentPool<RallyState>& GetRallyStatePool() { return rallyStatePool; }

    void Reset()
    {
        ballGameplayStatePool.clear();
        matchStatePool.clear();
        rallyStatePool.clear();
    }

    void Init();
};
