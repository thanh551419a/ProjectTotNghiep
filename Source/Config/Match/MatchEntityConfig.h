#pragma once
#pragma once

#include <cstdint>

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
constexpr Entity BALL = 6;
constexpr Entity NET  = 7;
}  // namespace GameConfig
