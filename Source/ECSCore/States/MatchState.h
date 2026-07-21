#pragma once
#include <cstdint>

enum class Team : uint8_t
{
    LEFT,
    RIGHT,
    NONE
};

struct MatchState
{
    int leftScore  = 0;
    int rightScore = 0;

    Team servingTeam = Team::LEFT;

    Entity leftServingEntity  = GameConfig::PLAYER;
    Entity rightServingEntity = GameConfig::OPPONENT_1;

    bool finished = false;
    Team matchWinner   = Team::NONE;

    void Reset() { *this = MatchState{}; }
};
