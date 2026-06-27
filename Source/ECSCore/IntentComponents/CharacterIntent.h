#pragma once
#include "../../Gameplay/FinalIntent.h"
using enum FinalIntent;
struct CharacterIntent
{
    float moveX = 0.0f;
    FinalIntent finalIntent = None;
};
