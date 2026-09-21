#pragma once
// class nay de dinh nghia size cua bong va luoi + vi tri cua luoi
#include "../System/SystemConf.h"
#include "BigRect.h"
#include "../../SimMath/Vec2.h"
#include "../../SimMath/Size.h"

namespace MatchObjectConfig
{
    constexpr float netWidth    = 0.1f;
    constexpr float netHeight   = 2.8f;
    constexpr SimMath::Size BallSize = SimMath::Size(0.21f * SystemConfig::PIXELS_PER_METER,0.21f * SystemConfig::PIXELS_PER_METER);
    constexpr SimMath::Vec2 NETSIZE = SimMath::Vec2(netWidth* SystemConfig::PIXELS_PER_METER,
                                          netHeight* SystemConfig::PIXELS_PER_METER);
    constexpr SimMath::Vec2 NETPOSITION =
        SimMath::Vec2(SystemConfig::offsetX + BigRect::RECT_WIDTH * 0.5f - NETSIZE.x * 0.5f, SystemConfig::offsetY);
}  // namespace MatchObjectConfig


