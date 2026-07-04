#pragma once
#include <vector>
#include "../../Config/ECS/ChunkConfig.h"
struct CharacterStat
{
    float AttackPower;
    float DefensePower;
    float Speed;
    float JumpPower;
};

struct MatchCharacterStat
{
    CharacterStat stats[ChunkConfig::CHARACTER_PER_MATCH];
};
