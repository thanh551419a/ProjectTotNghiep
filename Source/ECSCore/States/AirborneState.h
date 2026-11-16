#pragma once
#include <cstdint>

enum class AirborneState : uint8_t
{
    Grounded = 0,
    JumpUp   = 1,
    Fall     = 2
};
