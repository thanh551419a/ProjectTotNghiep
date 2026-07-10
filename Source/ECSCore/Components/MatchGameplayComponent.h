#pragma once
#include <cstdint>
using Team = uint16_t;
using Entity = uint16_t;
struct MatchGameplayComponent
{
    Team servingTeam;

    Entity servingEntity;

    int leftScore;

    int rightScore;
};
