#pragma once
#include "../../Constants/BallGameplayConstants.h"
using Entity = unsigned int;
using namespace BallStateFrame;
using namespace BallConfig;
enum class BallCollisionState : uint8_t
{
    None = 0,
    NetTop,
    NetMiddle,
    NetBottom,
};
struct BallGamePlayState
{
    static constexpr float DefaultLandingX = 9999.0f;

    float landingX = DefaultLandingX;

    int stateFrame = BallStateFrame::Alive;

    BallCollisionState collisionState = BallCollisionState::None;

    void Reset() { *this = BallGamePlayState{}; }
};
