#pragma once
#include <array>
#include <cstdint>

enum class ActionState : uint8_t
{
    None = 0,

    Jump,
    Spike,
    Slide,
    Serve,
    Bump,
    Set,
    SpikeLight,
    SpikeMedium,
    SpikeStrong,
    Count
};

namespace ActionStateInfo
{
constexpr std::array<float, static_cast<size_t>(ActionState::Count)> Cooldown = {
    0.0f,    // None
    150.0f,  // Jump
    200.0f,  // Spike
    800.0f,  // Slide
    300.0f,  // Serve
    300.0f,  // Bump
    200.0f,   // Set
    150.0f,
    150.0f,
    150.0f
};
}

struct CharacterActionState
{
    ActionState status = ActionState::None;
    float remainTime   = 0.0f;
};
