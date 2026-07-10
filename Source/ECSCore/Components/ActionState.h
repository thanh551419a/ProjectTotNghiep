#pragma once
#include <cstdint>
enum class ActionState : uint8_t
{
    None,
    Slide,
    Stun,
    KnockBack,
    Block,
};

struct CharacterActionState
{
    ActionState status = ActionState::None;
    uint8_t remainFrame = 0;
};
