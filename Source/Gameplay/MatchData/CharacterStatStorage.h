#pragma once
#include "../../ECSCore/ComponentPools/ComponentPool.h"
#include "CharacterStats.h"

class CharacterStatStorage
{
private:
    ComponentPool<MatchCharacterStat> matchStatPool;

public:
    ComponentPool<MatchCharacterStat>& GetMatchStatPool() { return matchStatPool; }
    CharacterStatStorage() {
        AXLOG("có chạy vào CharacterStatStorage");
        initDemo();
    }
    void initDemo()
    {
        // Initialize demo data for matchStatPool
        MatchCharacterStat matchStats;
        matchStatPool.add(0, matchStats); // Add to entity 0
    }
    void reset() {
        matchStatPool.clear();
        MatchCharacterStat matchStats;
        matchStatPool.add(0, matchStats);  // Add to entity 0
    }
};
