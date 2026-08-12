#pragma once
// class này để định nghĩa size của bóng và lưới + vị trí của lưới
#include "../System/SystemConf.h"
#include "BigRect.h"
#include "axmol.h"

namespace MatchObjectConfig
{
    constexpr float netWidth    = 0.1f;
    constexpr float netHeight   = 2.43f;
    constexpr ax::Size BallSize = ax::Size(0.21f * SystemConfig::PIXELS_PER_METER,0.21f * SystemConfig::PIXELS_PER_METER); // Size of the ball in pixels
    constexpr ax::Vec2 NETSIZE = ax::Vec2(netWidth* SystemConfig::PIXELS_PER_METER,
                                          netHeight* SystemConfig::PIXELS_PER_METER);  // Size of the net as a vector
    constexpr ax::Vec2 NETPOSITION =
        ax::Vec2(SystemConfig::offsetX + BigRect::RECT_WIDTH * 0.5f - NETSIZE.x * 0.5f, SystemConfig::offsetY);
}  // namespace MatchObjectConfig


