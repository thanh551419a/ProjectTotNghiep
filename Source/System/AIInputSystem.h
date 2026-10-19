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
    // self = nhân vật mà hệ thống này điều khiển. Mọi luật bên dưới được lật theo phía sân
    // của nhân vật đó, nên dùng được cho cả OPPONENT_1 (phải) và PLAYER (trái, chế độ bot-vs-bot).
    AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage, GameConfig::Entity self);

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

    // ---------------- cấu hình theo phía sân (tính một lần trong constructor) ----------------
    GameConfig::Entity _self = GameConfig::OPPONENT_1;
    bool _onLeft        = false;  // nhân vật đứng sân trái hay phải
    Team _myTeam        = Team::RIGHT;
    float _readyX       = 0.0f;   // vị trí thủ thế giữa sân mình
    int _attackDirection = -1;    // hướng đánh, y hệt "direction = (index < 3) ? 1 : -1"
    float _myOuterLineX = 0.0f;   // vạch biên 9m phía sân mình
    float _oppOuterLineX = 0.0f;  // vạch biên 9m phía sân đối phương

    // Cú CHẠM BÓNG gần nhất mà chính nhân vật này thực hiện trong lượt hiện tại.
    // RallyState chỉ lưu lastTouch (ai chạm) và touchCount (mấy lần), KHÔNG lưu loại cú,
    // nên phải tự theo dõi. Đặt lại về None ngay khi người khác chạm bóng.
    FinalIntent _lastOwnContact = FinalIntent::None;

    // ---------------- NHẢY ĐẬP SAU CÚ SET CỦA MÌNH (xem ADR-012) ----------------
    // Model quyết định CÓ đánh và đánh CÚ GÌ; còn LÚC NÀO nhảy thì do vật lý, vì model
    // học từng frame không học được thời điểm của một hành động hiếm và ngẫu nhiên.
    bool _jumpDrawnForSet = false;  // đã rút thăm "có nhảy đập không" cho cú Set này chưa
    bool _jumpPlanned     = false;  // kết quả rút thăm: sẽ nhảy đập
    bool _airSpikePending = false;  // đã nhảy theo kế hoạch, chờ bóng vào vùng để đập
    int _framesSinceJump  = 0;

    // ---------------- chỉ dùng ở chế độ bot-vs-bot ----------------
    // Lệch nhẹ khỏi điểm rơi, đổi mỗi lần bóng được chạm. Xem ChaseOffset() trong .cpp.
    float _chaseOffset               = 0.0f;
    GameConfig::Entity _lastSeenTouch = GameConfig::NONE;

    // Trả về độ lệch cho lần đón bóng hiện tại, đổi số mới khi bóng vừa được ai đó chạm.
    float ChaseOffset(const RallyState* rallyState);

    // Điểm rơi này có nằm trong sân MÌNH không (để biết bóng có phải của mình).
    bool LandsOnMySide(float landingX) const;

    // Điểm rơi này có nằm TRONG sân đối phương không (để chọn cú đánh hợp lệ).
    bool LandsInsideOpponentCourt(float landingX) const;

    // ---------------- PHÒNG THỦ ----------------

    // Bóng có bay qua khỏi lưới một cách "sạch" (cao hơn mép trên của lưới tại
    // vị trí lưới) hay không, dựa trên đúng quỹ đạo parabol hiện tại của bóng.
    bool CrossesNetCleanly(BallTrajectoryComponent* ballTrajectory, PositionComponent* ballPos) const;

    // Phân loại xu hướng bay của bóng thành 1 trong 3 nhóm ở trên.
    BallTrend ClassifyBallTrend(BallGamePlayState* ballGameplayState,
                                BallTrajectoryComponent* ballTrajectory,
                                PositionComponent* ballPos) const;

    // ---------------- TẤN CÔNG ----------------

    // Đội mình đã dùng hết số chạm cho phép chưa.
    //
    // touchLimit = 1 cho bot luật (hành vi gốc: AI chỉ 1 chạm mỗi lượt nhận bóng).
    // Khi model AI điều khiển thì dùng MAX_TOUCH_PER_TEAM, bằng đúng quyền của người chơi -
    // nếu không thì bot không bao giờ có lượt chạm thứ hai để mà Set hay Jump, và model học
    // được hai hành động đó cũng vô dụng (đo được: Set/Jump F1 = 1.00 offline nhưng 0 lần
    // dùng trong 5 trận đấu với người).
    bool HasUsedItsTouch(const RallyState* rallyState, int touchLimit) const;

    // Dự đoán landingX nếu AI thực hiện phương án tấn công này, dùng công thức
    // y hệt UpdateNewTrajectory nhưng KHÔNG làm thay đổi state thật (chỉ mô phỏng).
    float PredictAttackLandingX(const AttackOption& option, PositionComponent* ballPos) const;

    // Mỗi frame: xét cả 3 phương án Light/Medium/Strong, chỉ giữ lại phương án
    // nào rơi TRONG sân đối phương, rồi random 1 phương án trong danh sách đó.
    FinalIntent PickAttackIntent(PositionComponent* ballPos) const;

    // Bảng 3 phương án tấn công, dùng chung cho PickAttackIntent và action masking.
    // Thông số phải y hệt UpdateTrajectoryFromIntent.cpp, nếu lệch thì mô phỏng sai điểm rơi.
    static const AttackOption* AttackOptions();

    // ACTION MASKING: cú đập này có thật sự rơi trong sân đối phương không?
    //
    // Model học từ dữ liệu KHÔNG đảm bảo hành động hợp lệ - nó chỉ nhìn vị trí rồi trả về một
    // cú, không mô phỏng vật lý. Thiếu lớp chặn này thì model có thể chọn cú bay ra ngoài biên
    // trong đúng tình huống mà luật biết tránh, tức là bot ML tệ hơn bot luật.
    bool SpikeLandsInCourt(FinalIntent spike, PositionComponent* ballPos) const;

    // ---------------- VÙNG DETECT ----------------

    // Khoảng cách tâm-bóng tới tâm-nhân-vật, tính theo % CHIỀU CAO nhân vật.
    //
    // Chép đúng công thức của DetectPlayerBall() trong TrajectoryMath.cpp - kể cả phần
    // tâm bị lệch bởi HEIGHT_PERCENT_CHANGE / WIDTH_PERCENT_CHANGE. Nếu lệch công thức
    // thì masking cho qua những cú mà game sẽ bỏ, hoặc chặn những cú game cho phép.
    // Trả về số âm nếu storage chưa sẵn sàng.
    float BallDetectPercent() const;

    // Cùng công thức, nhưng cho vị trí GIẢ ĐỊNH của nhân vật và bóng (dùng khi mô phỏng).
    float DetectPercentAt(float selfX, float selfY, float ballX, float ballY) const;

    // Nếu bấm Jump NGAY frame này, sau bao nhiêu frame bóng lọt vào vùng đập (< 50%)?
    // Mô phỏng đúng cơ chế của game: nhân vật lên JUMP_FRAME frame, mỗi frame +9,81 px
    // (frame đầu +19,62), rồi rơi 9,81 px/frame; bóng đi x += speed, y = a(x+b)^2 + c.
    // Trả về -1 nếu cả cú nhảy không gặp bóng.
    int FramesUntilAirContact() const;

    // Bóng có đang trong vùng detect của intent này không. Mỗi loại cú một bán kính:
    // Spike* < 50%, Bump <= 60%, Set <= 100% chiều cao nhân vật.
    bool BallInDetectZone(FinalIntent intent) const;
};
