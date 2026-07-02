#pragma once
using Entity = unsigned int;
struct BallGamePlayComponent
{
    static constexpr float DefaultLandingX               = 9999.0f;
    static constexpr GameConfig::Entity DefaultLastTouch = GameConfig::NONE;
    float landingX                                       = DefaultLandingX;
    GameConfig::Entity lastTouch                         = DefaultLastTouch;
};
