#pragma once
#include "../System/SystemConf.h"
class BigRect
{
public:
    // =========================================================
    // WINDOW (Client render only)
    // =========================================================

    static constexpr float RECT_WIDTH = 2720.0f;
    static constexpr float RECT_HEIGHT = 1000.0f;
    // =========================================================
    // BIG RECT POSITION (GLOBAL SPACE)
    // =========================================================

    static constexpr float RECT_X = SystemConfig::offsetX;
    static constexpr float RECT_Y = SystemConfig::offsetY;

    // =========================================================
    // FLOOR
    // =========================================================

    // mặt đất của sân
    static constexpr float FLOOR_Y = RECT_Y;

    // =========================================================
    // NET
    // =========================================================

    // lưới nằm giữa sân
    static constexpr float NET_X = RECT_X + RECT_WIDTH * 0.5f;

    // =========================================================
    // COURT LINES
    // =========================================================

    // scale:
    // 18m thực = full width mỗi nửa sân
    // => 9m mỗi bên từ lưới

    

    // ---------------------------------------------------------
    // 3M LINE
    // ---------------------------------------------------------

    static constexpr float LEFT_3M_LINE_X = NET_X - (3.0f * SystemConfig::PIXELS_PER_METER);

    static constexpr float RIGHT_3M_LINE_X = NET_X + (3.0f * SystemConfig::PIXELS_PER_METER);

    // ---------------------------------------------------------
    // 9M LINE (biên cuối sân)
    // ---------------------------------------------------------

    static constexpr float LEFT_9M_LINE_X = NET_X - (9.0f * SystemConfig::PIXELS_PER_METER);

    static constexpr float RIGHT_9M_LINE_X = NET_X + (9.0f * SystemConfig::PIXELS_PER_METER);
};
