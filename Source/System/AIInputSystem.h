#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/MatchRuleConfig.h"
#include "../Config/Match/BigRect.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "Trajectory/TrajectoryMath.h"
#include "../System/TrajectoryTestConfig.h"

class AIInputSystem
{
public:
    AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);

    void update();

private:
    // ============================================================
    // XU HƯỚNG CỦA BÓNG (dùng cho phase PHÒNG THỦ)
    // ============================================================
    enum class BallTrend
    {
        TowardPlayerOrBlocked,  // case 1: landingX bên sân Player, HOẶC bị lưới chặn -> chưa phải bóng của AI
        TowardBotCourt,         // case 2: landingX trong sân AI, quỹ đạo qua lưới sạch -> phải phòng thủ
        GoingOutOfBounds        // case 3: landingX bên sân AI nhưng ra ngoài biên -> kệ, khỏi đuổi theo
    };

    // Cấu hình 1 phương án tấn công (Light / Medium / Strong), y hệt thông số
    // dùng trong UpdateTrajectoryFromIntent.cpp để mô phỏng đúng kết quả thật.
    struct AttackOption
    {
        FinalIntent intent;
        TrajectoryData data;
        TrajectoryBoost boost;
        float decreaseCPercent;
    };

private:
    IntentStorage* _intentStorage       = nullptr;
    ComponentStorage* _componentStorage = nullptr;

    // ---------------- PHÒNG THỦ ----------------

    // Bóng có bay qua khỏi lưới một cách "sạch" (cao hơn mép trên của lưới tại
    // vị trí lưới) hay không, dựa trên đúng quỹ đạo parabol hiện tại của bóng.
    bool CrossesNetCleanly(BallTrajectoryComponent* ballTrajectory, PositionComponent* ballPos) const;

    // Phân loại xu hướng bay của bóng thành 1 trong 3 nhóm ở trên.
    BallTrend ClassifyBallTrend(BallGamePlayState* ballGameplayState,
                                BallTrajectoryComponent* ballTrajectory,
                                PositionComponent* ballPos) const;

    // ---------------- TẤN CÔNG ----------------

    // AI (OPPONENT_1) chỉ được chạm bóng đúng 1 lần cho mỗi lượt bóng sang sân nó
    // (khác với Player được phép tối đa MatchRuleConfig::MAX_TOUCH_PER_TEAM chạm liên tiếp).
    bool HasUsedItsTouch(const RallyState* rallyState) const;

    // Dự đoán landingX nếu AI thực hiện phương án tấn công này, dùng công thức
    // y hệt UpdateNewTrajectory nhưng KHÔNG làm thay đổi state thật (chỉ mô phỏng).
    float PredictAttackLandingX(const AttackOption& option, PositionComponent* ballPos) const;

    // Mỗi frame: xét cả 3 phương án Light/Medium/Strong, chỉ giữ lại phương án
    // nào rơi TRONG sân đối phương, rồi random 1 phương án trong danh sách đó.
    FinalIntent PickAttackIntent(PositionComponent* ballPos) const;
};
