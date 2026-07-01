#pragma once
#include <cstdint>
enum class CharacterStatus : uint8_t
{
    None,
    Slide,
    Stun,
    KnockBack,
    Block,
};

struct CharacterStatusComponent
{
    CharacterStatus status = CharacterStatus::None;
    uint8_t remainFrame = 0;
};
