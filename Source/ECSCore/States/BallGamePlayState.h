#pragma once
#include "../../Constants/BallGameplayConstants.h"
using Entity = unsigned int;
using namespace BallStateFrame;
using namespace BallConfig;
struct BallGamePlayState
{
    static constexpr float DefaultLandingX = 9999.0f;

    float landingX = DefaultLandingX;

    int stateFrame = BallStateFrame::Alive;

    void Reset() { *this = BallGamePlayState{}; }
};
