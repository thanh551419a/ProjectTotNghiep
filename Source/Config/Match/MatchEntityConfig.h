#pragma once
#pragma once

#include <cstdint>
#include "../System/SystemConf.h"
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
constexpr float BallSize = 0.21f * SystemConfig::PIXELS_PER_METER;
}  // namespace GameConfig

