#pragma once
#include <cstdint>
#include "../System/SystemConf.h"
#include "MatchObjectConfig.h"
namespace GameConfig
{
    using Entity = uint32_t;

    // ======================================================
    // ENTITY INDEX MAP
    // ======================================================

    constexpr Entity PLAYER = 0;

    // TEAMMATE
    constexpr Entity TEAMMATE_1 = 1;
    constexpr Entity TEAMMATE_2 = 2;

    // OPPONENT
    constexpr Entity OPPONENT_1 = 3;
    constexpr Entity OPPONENT_2 = 4;
    constexpr Entity OPPONENT_3 = 5;

    // OBJECTS
    constexpr Entity NET = 6;
    constexpr Entity BALL  = 7;
    constexpr Entity NONE    = 8;

}  // namespace GameConfig

