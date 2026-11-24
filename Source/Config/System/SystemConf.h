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

    static constexpr float MIN_JUMP_STAT            = 100.0f; // Chỉ số nhảy tối thiểu của nhân vật
    static constexpr float BASE_JUMP_STAT           = 170.0f; // Mốc chỉ số nhảy chuẩn (170 -> BASE_JUMP_TIME_UP = 0.45s) & ngưỡng Boom Jump
    static constexpr float MAX_REF_JUMP_STAT        = 200.0f; // Mốc chỉ số nhảy 200 ứng với độ cao nhảy tối đa 4.00m
    static constexpr float MAX_JUMP_HEIGHT_AT_200_M = 4.00f;  // Độ cao nhảy tối đa (4.00m tính từ Ground = MIN_Y; góc trái dưới đạt max = 4.00m - 1/2 height character)
    static constexpr float BASE_JUMP_TIME_UP        = 0.45f;  // Thời gian bay lên chuẩn (0.45s) tại BASE_JUMP_STAT = 170 (Source of Truth)
    static constexpr float JUMP_UP_TIME             = BASE_JUMP_TIME_UP;
    static constexpr float JUMP_STAT_REF_HEIGHT_M   = 2.50f;  // Chỉ số jumping = 100 tương đương chiều cao nhảy 2m50
    static constexpr int JUMP_FRAME                 = static_cast<int>(JUMP_UP_TIME * TICK_RATE + 0.5f);  // 27 frames tại 60Hz (ứng với BASE_JUMP_STAT = 170)
    // static constexpr float DEFAULT_SPECIAL_JUMP_FACTOR = 1.0f; // Giữ comment để sử dụng cho Special Ability ở giai đoạn sau
    static constexpr float MIN_SPIKE_HEIGHT_RATIO   = 0.80f;  // Phải bay lên đạt ít nhất 80% độ cao nhảy tối đa (jumpUpTime còn <= 20%) mới được phép Spike trên không

    // =========================================================
    // COOLDOWN SOURCE-OF-TRUTH ANCHOR (Derived from BASE_JUMP_TIME_UP = 0.45s = 450ms)
    // =========================================================
    static constexpr float COOLDOWN_BASE_TIME_MS    = BASE_JUMP_TIME_UP * 1000.0f;    // 450.0ms
    static constexpr float COOLDOWN_JUMP_RATIO      = MIN_SPIKE_HEIGHT_RATIO;         // 0.80f * 450ms = 360.0ms (khóa toàn bộ hành động trừ di chuyển trong 80% thời gian bay lên)
    static constexpr float COOLDOWN_SPIKE_RATIO     = 1.00f;                          // 1.00f * 450ms = 450.0ms (1 lần đập mỗi cú nhảy)
    static constexpr float COOLDOWN_SET_RATIO       = 0.50f;                          // 0.50f * 450ms = 225.0ms
    static constexpr float COOLDOWN_BUMP_RATIO      = 2.0f / 3.0f;                    // 0.6667f * 450ms = 300.0ms (chỉ cooldown khi chạm bóng)
    static constexpr float COOLDOWN_SERVE_RATIO     = 0.0f;                           // Serve không có cooldown (0.0ms)
    static constexpr float COOLDOWN_SLIDE_RATIO     = 1.75f;                          // 1.75f * 450ms = 787.5ms

    static constexpr float MIN_Y = 265.0f;

    inline constexpr float ClampJumpStat(float rawJumpStat)
    {
        return (rawJumpStat < MIN_JUMP_STAT) ? MIN_JUMP_STAT : rawJumpStat;
    }

    inline constexpr float ComputeJumpUpDurationSeconds(float rawJumpStat)
    {
        const float effectiveJump = ClampJumpStat(rawJumpStat);
        // Công thức: BASE_JUMP_TIME_UP * (jump / BASE_JUMP_STAT) /* * DEFAULT_SPECIAL_JUMP_FACTOR */
        return BASE_JUMP_TIME_UP * (effectiveJump / BASE_JUMP_STAT);
    }

    inline constexpr int ComputeJumpUpFrames(float rawJumpStat)
    {
        return static_cast<int>(ComputeJumpUpDurationSeconds(rawJumpStat) * static_cast<float>(TICK_RATE) + 0.5f);
    }

    inline constexpr float ComputeMaxJumpDisplacementAboveMinYPx(float rawJumpStat, float charHeightPx)
    {
        // Ground là MIN_Y.
        // Khi jump = 200 (MAX_REF_JUMP_STAT), nhân vật chỉ nhảy cao 4.00m tính từ Ground = MIN_Y,
        // tương ứng góc trái dưới (pos->position.y) có độ dịch chuyển tối đa trên MIN_Y là:
        // maxBottomLeftDisplacementAt200Px = 4.00m * PIXELS_PER_METER - 0.5f * charHeightPx (ví dụ 320px - 80px = 240px = 3.00m).
        const float effectiveJump = ClampJumpStat(rawJumpStat);
        const float charHalfHeightPx = 0.5f * charHeightPx;
        const float maxBottomLeftDisplacementAt200Px =
            (MAX_JUMP_HEIGHT_AT_200_M * PIXELS_PER_METER) - charHalfHeightPx;
        return (effectiveJump / MAX_REF_JUMP_STAT) * maxBottomLeftDisplacementAt200Px;
    }

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
    // FULL-WORLD SLOW-MOTION CONFIG (JumpSpike Ball Contact Only)
    // =========================================================
    static constexpr int SLOW_MO_CHANCE_PERCENT       = 100;  // Tỉ lệ % (0..100) kích hoạt Slow-Motion khi JumpSpike đập trúng bóng
    static constexpr int SLOW_MO_SCORE_CHANCE_PERCENT = 0;    // Tắt Slow-Motion sau khi bóng chạm đất ăn điểm để không bị zoom lần 2 khi bóng nảy ra xa
    static constexpr int SLOW_MO_SCORE_DELAY_MIN_MS   = 100;  // k_min (ms) trễ sau khi ăn điểm
    static constexpr int SLOW_MO_SCORE_DELAY_MAX_MS   = 220;  // k_max (ms) trễ sau khi ăn điểm
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

    // =========================================================
    // 2-PHASE HOLD-BUMP ATTACK SERVE TOSS CONFIG (Direct RAM Struct: baseA, maxA, baseC, maxC, totalStep, tossV0)
    // =========================================================
    struct SimAttackServeConfig
    {
        float baseA;      // Default: -10.80f (thang x10^-3 -> -0.0108f sau NormalizeAttackServeA)
        float maxA;       // Default: -5.60f  (thang x10^-3 -> -0.0056f sau NormalizeAttackServeA)
        float baseC;      // Default: 465.0f  (2.50m trên MIN_Y = 265.0f)
        float maxC;       // Default: 609.0f  (4.30m trên MIN_Y = 265.0f)
        float totalStep;  // Default: 60.0f   (tổng số bước giữ nút để tăng từ base tới max: step = (max - base) / totalStep)
        float tossV0;     // Default: 85.0f
    };

    inline SimAttackServeConfig g_attackServeConfig = {
        -10.80f,      // baseA (-0.0108f after NormalizeAttackServeA)
        -5.60f,       // maxA  (-0.0056f after NormalizeAttackServeA)
        465.0f,       // baseC
        609.0f,       // maxC
        60.0f,        // totalStep
        85.0f         // tossV0
    };

    inline constexpr float NormalizeAttackServeA(float rawA)
    {
        // Trong hệ tọa độ pixel (2720x1540), hệ số mở a của parabol nằm ở thang 10^-3 (-0.0100f .. -0.0008f).
        // Nếu người dùng nhập theo thang x10^-3 (ví dụ -8, -3, -2 với |rawA| >= 0.1f),
        // tự động nhân 0.001f sang hệ số parabol thực tế để quỹ đạo không bị co thành đường thẳng đứng 14px.
        const float absA    = (rawA >= 0.0f) ? rawA : -rawA;
        const float signedA = (rawA > 0.0f) ? -rawA : rawA;
        return (absA >= 0.1f) ? (signedA * 0.001f) : signedA;
    }

    inline float& ATTACK_SERVE_BASE_A     = g_attackServeConfig.baseA;
    inline float& ATTACK_SERVE_MAX_A      = g_attackServeConfig.maxA;
    inline float& ATTACK_SERVE_BASE_C     = g_attackServeConfig.baseC;
    inline float& ATTACK_SERVE_MAX_C      = g_attackServeConfig.maxC;
    inline float& ATTACK_SERVE_TOTAL_STEP = g_attackServeConfig.totalStep;
    inline float& ATTACK_SERVE_TOSS_V0    = g_attackServeConfig.tossV0;
};
