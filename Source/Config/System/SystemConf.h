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
    static constexpr float MIN_SPIKE_HEIGHT_RATIO   = 0.80f;  // Phải bay lên đạt ít nhất 80% độ cao nhảy tối đa mới được phép Spike trên không
    static constexpr float JUMP_FAST_TIME_RATIO     = 0.70f;  // 70% thời gian đầu bay lên đi 80% độ cao (và 70% thời gian cuối rơi xuống đi 80% độ cao)
    static constexpr float JUMP_FAST_HEIGHT_RATIO   = 0.80f;  // 80% độ cao ở pha nhanh; 20% độ cao sát đỉnh đi trong 30% thời gian (giả lập treo người trên không)
    static constexpr float JUMP_APEX_HANG_TIME      = 0.10f;  // Đứng yên hoàn toàn ở đỉnh nhảy trong 0.10s (6 frames tại 60Hz)
    static constexpr int   JUMP_APEX_HANG_FRAMES    = static_cast<int>(JUMP_APEX_HANG_TIME * TICK_RATE + 0.5f); // 6 frames tại 60Hz

    // =========================================================
    // COOLDOWN SOURCE-OF-TRUTH ANCHOR (Derived from BASE_JUMP_TIME_UP = 0.45s = 450ms)
    // =========================================================
    static constexpr float COOLDOWN_BASE_TIME_MS    = BASE_JUMP_TIME_UP * 1000.0f;    // 450.0ms
    static constexpr float COOLDOWN_JUMP_RATIO      = JUMP_FAST_TIME_RATIO;           // 0.70f * 450ms = 315.0ms (khớp thời điểm đạt 80% độ cao ở 70% thời gian bay lên)
    static constexpr float COOLDOWN_SPIKE_RATIO     = 1.00f;                          // 1.00f * 450ms = 450.0ms (1 lần đập mỗi cú nhảy)
    static constexpr float COOLDOWN_SET_RATIO       = 0.50f;                          // 0.50f * 450ms = 225.0ms
    static constexpr float COOLDOWN_BUMP_RATIO      = 2.0f / 3.0f;                    // 0.6667f * 450ms = 300.0ms (chỉ cooldown khi chạm bóng)
    static constexpr float COOLDOWN_SERVE_RATIO     = 0.0f;                           // Serve không có cooldown (0.0ms)
    static constexpr float COOLDOWN_SLIDE_RATIO     = 1.75f;                          // 1.75f * 450ms = 787.5ms

    static constexpr float MIN_Y = 265.0f;

    inline float g_multiplierReality = 1.0f;

    inline float GetMultiplierReality()
    {
        return (g_multiplierReality > 0.01f) ? g_multiplierReality : 1.0f;
    }

    inline void SetMultiplierReality(float value)
    {
        g_multiplierReality = (value > 0.01f) ? value : 1.0f;
    }

    inline float GetBallTrajectoryMultiplier()
    {
        return 1.0f + 0.5f * (GetMultiplierReality() - 1.0f);
    }

    inline float ScaleHeightByReality(float rawY)
    {
        return MIN_Y + (rawY - MIN_Y) * GetBallTrajectoryMultiplier();
    }

    inline float ScaleCurvatureAByReality(float rawA)
    {
        return rawA * GetBallTrajectoryMultiplier();
    }

    inline float ScaleAIAttackHeightThreshold(float rawThresholdY)
    {
        return MIN_Y + (rawThresholdY - MIN_Y) * GetMultiplierReality();
    }

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

    inline float ComputeMaxJumpDisplacementAboveMinYPx(float rawJumpStat, float charHeightPx)
    {
        // Ground là MIN_Y.
        // Khi jump = 200 (MAX_REF_JUMP_STAT), nhân vật chỉ nhảy cao 4.00m tính từ Ground = MIN_Y (ở 1.0x reality),
        // tương ứng góc trái dưới (pos->position.y) có độ dịch chuyển tối đa trên MIN_Y nhân theo GetMultiplierReality().
        const float effectiveJump = ClampJumpStat(rawJumpStat);
        const float charHalfHeightPx = 0.5f * charHeightPx;
        const float maxBottomLeftDisplacementAt200Px =
            (MAX_JUMP_HEIGHT_AT_200_M * PIXELS_PER_METER) - charHalfHeightPx;
        const float baseDisplacementPx = (effectiveJump / MAX_REF_JUMP_STAT) * maxBottomLeftDisplacementAt200Px;
        return baseDisplacementPx * GetMultiplierReality();
    }

    inline float ComputeJumpAscentStepDy(float elapsedBeforeSec,
                                         float elapsedAfterSec,
                                         float fullJumpDurationSec,
                                         float totalUpwardDistancePx)
    {
        if (fullJumpDurationSec <= 1e-5f || totalUpwardDistancePx <= 0.0f || elapsedAfterSec <= elapsedBeforeSec)
        {
            return 0.0f;
        }
        const float t0    = (elapsedBeforeSec < 0.0f) ? 0.0f : elapsedBeforeSec;
        const float t1    = (elapsedAfterSec > fullJumpDurationSec) ? fullJumpDurationSec : elapsedAfterSec;
        const float tFast = JUMP_FAST_TIME_RATIO * fullJumpDurationSec;
        const float tSlow = (1.0f - JUMP_FAST_TIME_RATIO) * fullJumpDurationSec;
        const float hFast = JUMP_FAST_HEIGHT_RATIO * totalUpwardDistancePx;
        const float hSlow = (1.0f - JUMP_FAST_HEIGHT_RATIO) * totalUpwardDistancePx;
        const float vFast = (tFast > 1e-6f) ? (hFast / tFast) : 0.0f;
        const float vSlow = (tSlow > 1e-6f) ? (hSlow / tSlow) : 0.0f;

        const float endFast   = (t1 < tFast) ? t1 : tFast;
        const float startFast = (t0 < tFast) ? t0 : tFast;
        const float dtFast    = (endFast > startFast) ? (endFast - startFast) : 0.0f;

        const float startSlow = (t0 > tFast) ? t0 : tFast;
        const float endSlow   = (t1 > tFast) ? t1 : tFast;
        const float dtSlow    = (endSlow > startSlow) ? (endSlow - startSlow) : 0.0f;

        return vFast * dtFast + vSlow * dtSlow;
    }

    inline float ComputeJumpFallStepVy(float currentPosY,
                                       float rawJumpStat,
                                       float charHeightPx,
                                       float dtSec)
    {
        if (currentPosY <= MIN_Y || dtSec <= 0.0f)
        {
            return 0.0f;
        }
        const float totalHeight = ComputeMaxJumpDisplacementAboveMinYPx(rawJumpStat, charHeightPx);
        const float totalDownT  = ComputeJumpUpDurationSeconds(rawJumpStat); // T_down == T_up symmetric
        if (totalHeight <= 1e-5f || totalDownT <= 1e-5f)
        {
            return GRAVITY;
        }

        const float tSlow     = (1.0f - JUMP_FAST_TIME_RATIO) * totalDownT;
        const float tFast     = JUMP_FAST_TIME_RATIO * totalDownT;
        const float hSlow     = (1.0f - JUMP_FAST_HEIGHT_RATIO) * totalHeight;
        const float hFast     = JUMP_FAST_HEIGHT_RATIO * totalHeight;
        const float speedSlow = (tSlow > 1e-6f) ? (hSlow / tSlow) : 0.0f;
        const float speedFast = (tFast > 1e-6f) ? (hFast / tFast) : 0.0f;
        const float kneeY     = MIN_Y + hFast;

        float dropPx = 0.0f;
        if (currentPosY > kneeY && speedSlow > 1e-6f)
        {
            const float distAboveKnee = currentPosY - kneeY;
            const float timeToKnee    = distAboveKnee / speedSlow;
            if (timeToKnee >= dtSec)
            {
                dropPx = speedSlow * dtSec;
            }
            else
            {
                dropPx = distAboveKnee + speedFast * (dtSec - timeToKnee);
            }
        }
        else
        {
            dropPx = speedFast * dtSec;
        }

        const float maxDropToGround = currentPosY - MIN_Y;
        if (dropPx >= maxDropToGround - 1e-3f)
        {
            return -maxDropToGround;
        }
        return -dropPx;
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
    static constexpr float BOUNCE_C_MULTIPLIER      = 120.0f; // Hệ số nhân độ cao tăng thêm của c khi |speed| > k (ở 1.0x reality)
    static constexpr float DecreaseSpeedAfterBounce = 45.0f;  // Mỗi lần bóng chạm đất nảy lên sẽ giảm tốc đi DecreaseSpeedAfterBounce % (0..100)
    static constexpr float BOUNCE_SPEED_DECAY       = (100.0f - DecreaseSpeedAfterBounce) / 100.0f;

    inline float GetBounceCMultiplier()
    {
        return BOUNCE_C_MULTIPLIER * GetBallTrajectoryMultiplier();
    }

    // =========================================================
    // 2-PHASE HOLD-BUMP ATTACK SERVE TOSS CONFIG (Direct RAM Struct: baseA, maxA, baseC, maxC, totalStep, tossV0)
    // =========================================================
    struct SimAttackServeConfig
    {
        float baseA;      // 1.0x reality Source of Truth (thang x10^-3 -> NormalizeAttackServeA)
        float maxA;       // 1.0x reality Source of Truth
        float baseC;      // 1.0x reality Source of Truth
        float maxC;       // 1.0x reality Source of Truth
        float totalStep;  // Tổng số bước giữ nút để tăng từ base tới max: step = (max - base) / totalStep
        float tossV0;     // Tốc độ tung bóng
    };

    inline SimAttackServeConfig g_attackServeConfig = {
        -13.00f,      // baseA (1.0x reality; scaled by GetMultiplierReality() in GetScaledAttackServeConfig)
        -9.50f,       // maxA  (1.0x reality)
        425.0f,       // baseC (1.0x reality: 2.00m apex)
        595.0f,       // maxC  (1.0x reality: 4.125m apex)
        60.0f,        // totalStep (1.00s hold @ 60Hz)
        56.0f         // tossV0 (2.24 px/tick -> ~1.30s up + 1.39s down = 2.69s)
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

    inline SimAttackServeConfig GetScaledAttackServeConfig()
    {
        return SimAttackServeConfig{
            ScaleCurvatureAByReality(NormalizeAttackServeA(g_attackServeConfig.baseA)),
            ScaleCurvatureAByReality(NormalizeAttackServeA(g_attackServeConfig.maxA)),
            ScaleHeightByReality(g_attackServeConfig.baseC),
            ScaleHeightByReality(g_attackServeConfig.maxC),
            g_attackServeConfig.totalStep,
            g_attackServeConfig.tossV0
        };
    }

    inline float& ATTACK_SERVE_BASE_A     = g_attackServeConfig.baseA;
    inline float& ATTACK_SERVE_MAX_A      = g_attackServeConfig.maxA;
    inline float& ATTACK_SERVE_BASE_C     = g_attackServeConfig.baseC;
    inline float& ATTACK_SERVE_MAX_C      = g_attackServeConfig.maxC;
    inline float& ATTACK_SERVE_TOTAL_STEP = g_attackServeConfig.totalStep;
    inline float& ATTACK_SERVE_TOSS_V0    = g_attackServeConfig.tossV0;

    // =========================================================
    // CAMERA ZOOM-IN vs NORMAL CLASSIFICATION BY BALL LAST ACTION
    // =========================================================
    inline constexpr float CAMERA_ZOOM_IN_ORTHO_RATIO = 0.80f; // Camera -20% (Zoom In) to aim easily
    inline constexpr float CAMERA_NORMAL_ORTHO_RATIO  = 1.00f; // Normal camera to see full trail

    // Returns true if the ball's last action should trigger Camera Zoom-In (-20% OrthoSize),
    // false if it should keep Normal Camera (1.00x OrthoSize) to see the full flight trail.
    inline constexpr bool ShouldZoomInCameraForBallAction(uint8_t lastActCode, int stateFrame)
    {
        if (stateFrame == 0) // Reset (ball back on server's hand)
            return true;
        // ActionState enum (ActionState.h / SimActionState in SpikeNativeApi.h):
        // None=0, Jump=1, Spike=2, Slide=3, Serve=4, Bump=5, Set=6, SpikeLight=7, SpikeMedium=8, SpikeStrong=9
        // Serve(4), Spike(2), SpikeLight(7), SpikeMedium(8), SpikeStrong(9) -> Normal Camera (false), preserved during stateFrame > 0 post-ground bounce!
        if (lastActCode == 2 || lastActCode == 4 || lastActCode == 7 || lastActCode == 8 || lastActCode == 9)
            return false;
        // Bump(5, including LaunchAttackServe), Set(6), None(0) -> Zoom In Camera -20% (true)
        return true;
    }
};
