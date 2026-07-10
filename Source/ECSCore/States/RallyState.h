#pragma once
#include <cstdint>


struct RallyState
{
    Entity lastTouch = GameConfig::NONE;

    Team possessionTeam = Team::NONE;

    uint8_t touchCount = 0;

    void Reset() { *this = RallyState{}; }
};
