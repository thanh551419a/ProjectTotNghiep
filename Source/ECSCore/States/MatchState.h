#pragma once
#include <cstdint>

enum class Team : uint8_t
{
    NONE = 0,
    LEFT,
    RIGHT
};

struct MatchState
{
    int leftScore  = 0;
    int rightScore = 0;

    Team servingTeam     = Team::NONE;
    Entity servingEntity = 0;

    bool finished = false;
    Team winner   = Team::NONE;

    void Reset() { *this = MatchState{}; }
};
