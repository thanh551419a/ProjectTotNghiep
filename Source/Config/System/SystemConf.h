#pragma once

class SystemConfig
{
public:
    // =========================================================
    // WINDOW (Client render only)
    // =========================================================
    static constexpr int WINDOW_WIDTH  = 1920;
    static constexpr int WINDOW_HEIGHT = 1080;

    static constexpr bool FULLSCREEN = false;
    static constexpr bool BORDERLESS = false;
    static constexpr bool RESIZABLE  = false;

    static constexpr const char* WINDOW_TITLE = "The Spike Cross Remaster";

    // =========================================================
    // VIRTUAL / DESIGN RESOLUTION (Game world view)
    // =========================================================

    // dùng để scale world -> client render
    static constexpr float VIRTUAL_WIDTH  = 2720.0f;
    static constexpr float VIRTUAL_HEIGHT = 1540.0f;
    // ~16:9 chuẩn hơn 2760x1040

    // =========================================================
    // ECS SIMULATION
    // =========================================================
    static constexpr int TICK_RATE  = 60;
    static constexpr float FIXED_DT = 1.0f / static_cast<float>(TICK_RATE);

    // =========================================================
    // PHYSICS WORLD SCALE (server authoritative)
    // =========================================================

    static constexpr float PIXELS_PER_METER = 80.0f;

    static constexpr float GRAVITY = -9.81f;

    static constexpr int JUMP_FRAME = 30;

    static constexpr float MIN_Y = 265.0f;

    static constexpr float MIN_X_LEFT = 0.0f + 10.0f;

    static constexpr float MAX_X_LEFT = 1366.0f - 0.5f*PIXELS_PER_METER ;

    static constexpr float MIN_X_RIGHT = MAX_X_LEFT + 20.0f;

    static constexpr float MAX_X_RIGHT = VIRTUAL_WIDTH;

    static constexpr float SPEED = 10.0f;
    // =========================================================
    // COURT (real-world meter unit)
    // =========================================================

    static constexpr float COURT_WIDTH  = 34.0f;
    static constexpr float COURT_HEIGHT = 12.5f;

    // =========================================================
    // DEBUG (client only)
    // =========================================================

    static constexpr bool DEBUG_RENDER = true;
    static constexpr bool SHOW_AABB    = true;
    static constexpr bool SHOW_HITBOX  = true;

    // =========================================================
    // OFFSET (client only)
    // dùng để canh chỉnh big rect cho đẹp
    static constexpr float offsetX = 10.0f;
    static constexpr float offsetY = 265.0f;
};
