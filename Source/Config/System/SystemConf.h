#pragma once

namespace SystemConfig
{
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
    static constexpr int TICK_RATE       = 60;
    static constexpr float FIXED_DT      = 1.0f / static_cast<float>(TICK_RATE);
    static constexpr float TIME_PER_FRAME = 1.0f / static_cast<float>(TICK_RATE);

    // =========================================================
    // PHYSICS WORLD SCALE (server authoritative)
    // =========================================================

    static constexpr float PIXELS_PER_METER = 80.0f;

    static constexpr float GRAVITY = -9.81f;

    static constexpr float JUMP_UP_TIME           = 0.75f;  // Thời gian nhảy lên cố định 0.75s (không phụ thuộc FPS)
    static constexpr float JUMP_STAT_REF_HEIGHT_M = 2.50f;  // Chỉ số jumping = 100 tương đương chiều cao nhảy 2m50
    static constexpr int JUMP_FRAME               = static_cast<int>(JUMP_UP_TIME * TICK_RATE);  // 45 frames tại 60Hz

    static constexpr float MIN_Y = 265.0f;

    static constexpr float MIN_X_LEFT = 0.0f + 10.0f;

    static constexpr float MAX_X_LEFT = 1366.0f - 0.5f*PIXELS_PER_METER ;

    static constexpr float MIN_X_RIGHT = MAX_X_LEFT + 20.0f;

    static constexpr float MAX_X_RIGHT = VIRTUAL_WIDTH;

    static constexpr float SPEED = 7.0f;
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
    // =========================================================
    // TRAJECTORY CONFIG
    // =========================================================
    static constexpr float MIN_V0 = 4.0f;
    static constexpr float MAX_V0 = 160.0f;
    static constexpr float FIXED_V0_MULTIPLIER = 7.20f;  // Hệ số nhân v0 cố định cho Spike theo ma trận để tốc độ bay thực tế trên sân (px/s) khớp với game mẫu (~3800..5530 px/s ngang, ~4750..6912 px/s toàn đường đi 2D)

    static constexpr float MIN_C = 700.0f;
    static constexpr float MAX_C = 1000.0f;

    static constexpr float RATIO_C = (MAX_C - MIN_C) / (MAX_V0 - MIN_V0);

    // =========================================================
    // DECREASE SIZE CHARACTER
    // =========================================================

    static constexpr float HEIGHT_PERCENT_CHANGE = -20.0f; // giảm 20%
    static constexpr float WIDTH_PERCENT_CHANGE  = 15.0f;  // tăng 15%

    // =========================================================
    // DISTANCE DETECTION
    //=========================================================

    static constexpr float DISTANCE_DETECTION_SET = 100.0f;  // khoảng cách detection của player với ball
    static constexpr float DISTANCE_DETECTION_BUMP = 60.0f;  // khoảng cách detection của player với ball

    // =========================================================
    // FULL-WORLD SLOW-MOTION CONFIG (JumpSpike Ball Contact & Delayed Scoring Ground Contact)
    // =========================================================
    static constexpr int SLOW_MO_CHANCE_PERCENT       = 100;  // Tỉ lệ % (0..100) kích hoạt Slow-Motion khi JumpSpike đập trúng bóng
    static constexpr int SLOW_MO_SCORE_CHANCE_PERCENT = 100;  // Tỉ lệ % (0..100) kích hoạt Slow-Motion sau khi bóng chạm đất ăn điểm
    static constexpr int SLOW_MO_SCORE_DELAY_MIN_MS   = 100;  // k_min (ms) trễ sau khi ăn điểm để bóng đi 1 đoạn có trail rồi mới Slow-Motion (6 frames @ 60Hz)
    static constexpr int SLOW_MO_SCORE_DELAY_MAX_MS   = 220;  // k_max (ms) trễ sau khi ăn điểm (< tổng thời gian chết của bóng) mới bật Slow-Motion (13 frames @ 60Hz)
    static constexpr int SLOW_MO_DURATION_MS          = 1000; // Thời lượng Slow-Motion (1000ms = 1.0s = 60 frames ở 60Hz)
    static constexpr int SLOW_MO_SPEED_DIVISOR        = 6;    // Hệ số làm chậm 1/6 (chỉ chạy 1 trên mỗi 6 frames)

    // =========================================================
    // DEAD-BALL GROUND BOUNCE CONFIG (Post-Ground-Impact Parabolic Bounce)
    // =========================================================
    // Khi bóng chạm đất (y <= MIN_Y):
    // - Giai đoạn 1 (Nếu vận tốc đập |speed| > BOUNCE_SPEED_THRESHOLD_K (k), khi atk lớn vượt ngưỡng như atk = 230):
    //   Tăng c lên trước so với đỉnh parabol hiện tại: c = max(c, MIN_Y) + (|speed| / BOUNCE_SPEED_THRESHOLD_K) * BOUNCE_C_MULTIPLIER
    // - Giai đoạn 2 (Nếu |speed| <= BOUNCE_SPEED_THRESHOLD_K ở các lần chạm đất tiếp theo sau khi giảm tốc):
    //   Giảm dần độ cao nảy c theo tỉ lệ giữ lại vận tốc (c = MIN_Y + (c - MIN_Y) * BOUNCE_SPEED_DECAY) cho tới khi hết lực nảy.
    static constexpr float BOUNCE_SPEED_THRESHOLD_K = 57.6f;  // Ngưỡng k (8.0f * FIXED_V0_MULTIPLIER: atk = 180 -> JumpSpike D1/D2 |speed| <= 57.024 <= k; atk = 230 -> |speed| = 59.62..105.98 > k)
    static constexpr float BOUNCE_C_MULTIPLIER      = 120.0f; // Hệ số nhân độ cao tăng thêm của c khi |speed| > k
    static constexpr float DecreaseSpeedAfterBounce = 45.0f;  // Mỗi lần bóng chạm đất nảy lên sẽ giảm tốc đi DecreaseSpeedAfterBounce % (0..100)
    static constexpr float BOUNCE_SPEED_DECAY       = (100.0f - DecreaseSpeedAfterBounce) / 100.0f;
};
