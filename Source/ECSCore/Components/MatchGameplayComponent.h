#pragma once
#include <cstdint>
enum class Team : uint8_t
{
    LEFT,
    RIGHT,
    NONE
};
using Entity = uint16_t;
struct MatchGameplayComponent
{
    Team servingTeam;

    Entity servingEntity;

    int leftScore;

    int rightScore;
};
