#include "AIInputSystem.h"
// MatchRunner dùng model riêng (vòng tự học) mà không đụng model của game chính:
// target MatchRunner định nghĩa SPIKE_AI_ACTION_MODEL_HEADER trỏ sang Source/AI/GeneratedAuto/.
#ifdef SPIKE_AI_ACTION_MODEL_HEADER
#    include SPIKE_AI_ACTION_MODEL_HEADER
#else
#    include "../AI/Generated/spike_ai_action_random_forest.h"
#endif
#include "../Config/Match/RunMode.h"
#include "../AI/MlFeaturesV1.h"
#include "../Config/Match/MatchObjectConfig.h"
#include <array>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <random>
#include <vector>

namespace
{
// Ánh xạ tên lớp của model sang FinalIntent.
//
// PHẢI tra theo TÊN, không được hard-code thứ tự index. Thứ tự kClassNames do sklearn sinh
// ra theo tập nhãn CÓ MẶT trong dữ liệu train, nên nó đổi khi dữ liệu đổi: bản train trên
// dữ liệu tổng hợp có "Bump" ở index 0, bản train trên 6 trận thật có "Jump" ở index 0 và
// không có "Bump". Hard-code thứ tự thì model vẫn chạy mà ra hành động sai âm thầm.
FinalIntent IntentFromClassName(const char* name)
{
    if (std::strcmp(name, "Jump") == 0)        return FinalIntent::Jump;
    if (std::strcmp(name, "Spike") == 0)       return FinalIntent::Spike;
    if (std::strcmp(name, "Feint") == 0)       return FinalIntent::Feint;
    if (std::strcmp(name, "Block") == 0)       return FinalIntent::Block;
    if (std::strcmp(name, "Bump") == 0)        return FinalIntent::Bump;
    if (std::strcmp(name, "Slide") == 0)       return FinalIntent::Slide;
    if (std::strcmp(name, "Set") == 0)         return FinalIntent::Set;
    if (std::strcmp(name, "Serve") == 0)       return FinalIntent::Serve;
    if (std::strcmp(name, "SpikeLight") == 0)  return FinalIntent::SpikeLight;
    if (std::strcmp(name, "SpikeMedium") == 0) return FinalIntent::SpikeMedium;
    if (std::strcmp(name, "SpikeStrong") == 0) return FinalIntent::SpikeStrong;
    return FinalIntent::None;  // ke ca "None", va bat ky ten la nao
}

// Ngưỡng chiều cao bóng để AI thực hiện cú đánh trả (AI ra tay khi bóng đã
// rơi đủ thấp, trong tầm với, không cần nhảy) - giữ nguyên hành vi cũ.
constexpr float AI_ATTACK_HEIGHT_THRESHOLD = 300.0f;

// LẤY MẪU THEO XÁC SUẤT thay cho argmax khi chọn hành động từ model.
//
// VÌ SAO CẦN: đo trên 5 trận người chơi vs bot ML, bot thực hiện 419 cú đập nhưng
// 97,1% (407/419) là SpikeMedium; Set chỉ được dùng 2 lần trong cả 5 trận.
// Nguyên nhân KHÔNG phải thiếu dữ liệu - `Set` có 104 mẫu, nhiều nhất trong tập train.
// Nguyên nhân là argmax: với cùng một trạng thái, model luôn trả về đúng một lớp, nên
// bot không bao giờ biến hoá. Thêm bao nhiêu dữ liệu cũng không sửa được bằng argmax.
//
// PHẠM VI: chỉ đổi "ra tay BẰNG GÌ", KHÔNG đổi "CÓ ra tay hay không".
// Quyết định có/không: từ 16/10 do ngưỡng USE_ACT_THRESHOLD bên dưới (model mới đã bỏ rò
// rỉ nhãn nên argmax gần như luôn ra None); đặt USE_ACT_THRESHOLD = false để về argmax.
//
// Đặt false để quay lại argmax, dùng khi cần đo đối chứng A/B.
constexpr bool USE_PROBA_SAMPLING = true;

// Seed cố định để mỗi lần chạy lại cho ra cùng kết quả - cần cho việc đo lặp lại.
// Đổi số này nếu muốn một chuỗi ngẫu nhiên khác.
// Seed nằm ở RunMode::Get().seed (mặc định 20261010) để MatchRunner chạy mỗi lần một seed khác.

// LÀM PHẲNG phân phối trước khi rút thăm: w' = w^(1/T).
//
// VÌ SAO CẦN: chỉ lấy mẫu theo xác suất thô là KHÔNG đủ. Đo trên 2 trận bot-vs-bot,
// SpikeMedium vẫn chiếm 97,8% (trước khi lấy mẫu: 97,1%) - gần như không đổi.
// Nguyên nhân: xét từng frame thì phân phối gần suy biến - trung vị xác suất của cú
// đập mạnh nhất tại mỗi frame là 0,9038, nên rút thăm vẫn ra đúng nó.
//
// Đo trên 2924 frame mà model chọn một cú đập (chuẩn hoá lại trong 3 cú):
//
//     T     SpikeLight   SpikeMedium   SpikeStrong
//   1.0          5,3%         72,0%         22,1%     <- không làm phẳng
//   2.0         13,2%         59,4%         24,9%
//   3.0         18,3%         51,0%         27,0%     <- đang dùng
//   5.0         23,2%         43,0%         28,8%
//   8.0         26,1%         38,4%         29,8%
//
// T = 1 tắt hẳn việc làm phẳng. T càng lớn càng đa dạng nhưng càng hay chọn cú mà
// model cho là kém hơn - đây là đánh đổi, phải đo tỉ số để biết mức nào đáng.
constexpr float PROBA_SAMPLING_TEMPERATURE = 3.0f;

// ================= HƯỚNG A (16/10): NGƯỠNG + NHẢY THEO VẬT LÝ - xem ADR-012 =================
//
// QUYẾT ĐỊNH "CÓ RA TAY KHÔNG" BẰNG NGƯỠNG thay cho argmax.
//
// VÌ SAO: model mới (played_v1_aligned) đã bỏ được rò rỉ nhãn (ADR-011), nên xác suất của
// nó là xác suất THẬT - và hành động hiếm hiếm khi vượt 0,5. Đo trên trạng thái bot gặp:
// P(ra tay) > 0,3 ở 20,3% frame bóng trong tầm, chỉ 0,53% frame bóng ở xa. Tức model
// BIẾT lúc nào nên đánh, nhưng argmax gần như luôn trả về None.
//
// Đặt false để quay lại cách cũ (argmax quyết định có/không).
constexpr bool USE_ACT_THRESHOLD = true;
constexpr float ACT_THRESHOLD   = 0.3f;

// Model KHÔNG được tự đề xuất Jump. Model học từng frame đạt Jump F1 = 0.000 trên dữ liệu
// thật: đúng khoảnh khắc người chơi nhảy, họ chỉ nhảy 16,3% số lần, nên xác suất model
// đưa ra tối đa ~0,16 ở MỌI frame tương tự - cho nó đề xuất thì nó nhảy bừa ngẫu nhiên.
// Jump do luật vật lý bên dưới quyết định.
// Giá trị dùng thật: RunMode::Get().modelMayJump (mặc định false như hằng số này).
constexpr bool MODEL_MAY_PROPOSE_JUMP = false;

// Sau cú Set của chính mình, người chơi nhảy đập 31,4% số lần (383/1220 cú Set, 330 trận
// trong Logs/v1); 34,0% đập từ mặt đất; còn lại đánh kiểu khác. Bot rút thăm MỘT lần cho
// mỗi cú Set với đúng tỉ lệ này -> "tuỳ quả".
// Giá trị dùng thật: RunMode::Get().jumpAfterSetProb (mặc định 0,314 như hằng số này).
constexpr float P_JUMP_AFTER_SET = 0.314f;

// Nhảy ở frame ĐẦU TIÊN mà mô phỏng cho thấy bóng sẽ vào vùng đập sau [MIN_T, MAX_T] frame.
// Nhân vật lên tới đỉnh sau JUMP_FRAME = 30 frame.
//
// Chọn bằng cách áp chính luật này lên 207 chuỗi Set -> Jump -> Spike ĐẬP TRÚNG của người
// chơi (Logs/v1) và so frame luật nhảy với frame người nhảy:
//     cửa sổ    không kích hoạt   lệch trung vị   |lệch| <= 6 frame
//     [20,31]       28/207           -10                30%         <- nhảy quá sớm
//     [20,26]       28/207            -2                67%
//     [18,25]       28/207             0                74%         <- đang dùng
//     [16,22]       28/207            +3                75%
// Ở frame người nhảy, mô phỏng đoán thời điểm chạm lệch thực tế trung vị 1 frame.
// (150/357 chuỗi người bấm đập nhưng bóng KHÔNG vào vùng - đập hụt, game bỏ qua - nên
// không dùng để chỉnh.)
constexpr int JUMP_CONTACT_MIN_T = 18;
constexpr int JUMP_CONTACT_MAX_T = 25;
constexpr int JUMP_SIM_FRAMES    = 70;

std::mt19937& Rng()
{
    static std::mt19937 rng(RunMode::Get().seed);
    return rng;
}

bool IsSpikeIntent(FinalIntent i)
{
    return i == FinalIntent::SpikeLight || i == FinalIntent::SpikeMedium ||
           i == FinalIntent::SpikeStrong;
}

// Các intent CHẠM vào bóng (do UpdateTrajectoryFromIntent xử lý).
// `Jump` KHÔNG nằm đây: nó do JumpStartSystem xử lý và không chạm bóng.
bool IsContactIntent(FinalIntent i)
{
    return IsSpikeIntent(i) || i == FinalIntent::Spike || i == FinalIntent::Set ||
           i == FinalIntent::Bump || i == FinalIntent::Serve;
}

}  // namespace

AIInputSystem::AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage, GameConfig::Entity self)
{
    _intentStorage    = intentStorage;
    _componentStorage = componentStorage;
    _self             = self;

    // Cùng quy ước với "direction = (index < 3) ? 1 : -1" của UpdateTrajectoryFromIntent.cpp
    // và "entity < CHARACTER_PER_MATCH / 2" của ApplyHorizontalMovement.
    _onLeft          = self < ChunkConfig::CHARACTER_PER_MATCH / 2;
    _myTeam          = _onLeft ? Team::LEFT : Team::RIGHT;
    _attackDirection = _onLeft ? 1 : -1;

    // Vị trí "thủ thế" giữa sân mình - nơi AI lùi về khi bóng chưa thực sự hướng sang sân mình.
    _readyX = _onLeft ? (BigRect::LEFT_9M_LINE_X + BigRect::NET_X) * 0.5f
                      : (BigRect::NET_X + BigRect::RIGHT_9M_LINE_X) * 0.5f;

    _myOuterLineX  = _onLeft ? BigRect::LEFT_9M_LINE_X : BigRect::RIGHT_9M_LINE_X;
    _oppOuterLineX = _onLeft ? BigRect::RIGHT_9M_LINE_X : BigRect::LEFT_9M_LINE_X;
}

float AIInputSystem::ChaseOffset(const RallyState* rallyState)
{
    // CHỈ ở chế độ bot-vs-bot (tự sinh dữ liệu). Game của người chơi không đổi gì.
    //
    // Vì sao cần: bot luôn chạy tới ĐÚNG điểm rơi nên không bao giờ đỡ trượt. Hai bot như vậy
    // đánh với nhau thì pha bóng không kết thúc - đo thực tế 10.782 frame chỉ có 1 quả giao
    // bóng, và vì mỗi lần chạy chỉ có một pha bóng nên ngẫu nhiên hoá quả giao vô dụng.
    //
    // Lệch nhẹ khỏi điểm rơi giải quyết cả hai việc:
    //   - bot đứng ở nhiều vị trí khác nhau -> tập cú đập hợp lệ khác nhau -> nhãn
    //     spike_choice có đủ cả ba lớp thay vì một lớp duy nhất;
    //   - đôi lúc lệch quá thì đỡ trượt -> pha bóng kết thúc -> có quả giao mới.
    //
    // Lưu ý: chỗ ngẫu nhiên là VỊ TRÍ ĐỨNG, không phải lựa chọn cú đập. Lựa chọn cú đập vẫn
    // là hàm tất định của trạng thái nên vẫn học được (xem ADR-006 ở repo AI).
    if (!RunMode::Get().botVsBot)
        return 0.0f;

    // Biên độ chọn theo đo thực tế. Nhân vật cao 2m = 160px, cú đập chỉ ăn khi
    // distancePercent < 50 nên tầm với là 80px; bóng ở độ cao khác tâm nhân vật nên ngân sách
    // lệch NGANG chỉ còn khoảng 66px. Hai biên độ cho hai kết quả trái ngược, cần cả hai:
    //   ±35 : nằm trong tầm với -> pha bóng kéo dài, nhiều cú đập (158 cú / 3 phút) nhưng
    //         chỉ 2 quả giao bóng nên tình huống lặp lại.
    //   ±70 : thường xuyên trượt -> pha bóng kết thúc, 85 quả giao bóng và mọi tình huống
    //         đều khác nhau, nhưng chỉ còn 8 cú đập.
    // Trộn: phần lớn lệch vừa để pha bóng sống, thỉnh thoảng lệch mạnh để pha bóng kết thúc
    // và mở một quả giao mới (quả giao cũng đã được ngẫu nhiên hoá trong
    // UpdateTrajectoryFromIntent). Nhờ vậy vừa nhiều cú đập vừa nhiều tình huống khác nhau.
    // Biên độ đã đo thực nghiệm, mỗi lần 3 phút bot-vs-bot (tầm với ngang khoảng 66px):
    //
    //   lệch        quả giao   cú đập   tình huống khác nhau   lớp xuất hiện
    //   0              1         78              4            Medium
    //   0 + giao random 1       221              5            Strong
    //   ±30..35        2        158              4            Medium
    //   ±60           85          8              8            Medium, Strong
    //   trượt hẳn 25% 87          7              7            Medium
    //
    // Kết luận: lệch vị trí KHÔNG mở được bế tắc. Lệch nhỏ thì pha bóng sống mãi nên tình
    // huống lặp lại; lệch đủ lớn để kết thúc pha bóng thì bot trượt ngay quả đỡ giao, chưa
    // kịp đập quả nào. Không có khoảng trung gian.
    //
    // Giữ ±30: cấu hình cho nhiều cú đập nhất mà vẫn đa dạng hơn lệch 0. Nút thắt thật nằm ở
    // PickAttackIntent - luật "chọn cú mạnh nhất còn trong sân" làm SpikeLight gần như không
    // bao giờ được chọn (chỉ khi đứng rất sát lưới). Muốn đủ ba lớp thì luật phải chia theo
    // dải khoảng cách tới lưới (gần -> Light, vừa -> Medium, xa -> Strong): vẫn tất định nên
    // vẫn học được, mà cả ba lớp đều xuất hiện.
    constexpr float NEAR_JITTER = 30.0f;

    if (rallyState->lastTouch != _lastSeenTouch)
    {
        static std::mt19937 rng{RunMode::Get().seed * 2654435761u + 1u};  // theo seed: đo lặp lại được

        _lastSeenTouch = rallyState->lastTouch;
        _chaseOffset   = std::uniform_real_distribution<float>(-NEAR_JITTER, NEAR_JITTER)(rng);
    }
    return _chaseOffset;
}

bool AIInputSystem::LandsOnMySide(float landingX) const
{
    return _onLeft ? landingX < BigRect::NET_X : landingX >= BigRect::NET_X;
}

bool AIInputSystem::LandsInsideOpponentCourt(float landingX) const
{
    return _onLeft ? (landingX > BigRect::NET_X && landingX <= _oppOuterLineX)
                   : (landingX >= _oppOuterLineX && landingX < BigRect::NET_X);
}

bool AIInputSystem::HasUsedItsTouch(const RallyState* rallyState, int touchLimit) const
{
    return rallyState->lastTouch == _self && rallyState->touchCount >= touchLimit;
}

bool AIInputSystem::CrossesNetCleanly(BallTrajectoryComponent* ballTrajectory, PositionComponent* ballPos) const
{
    // Nếu bóng đã đứng bên sân AI rồi thì coi như đã băng qua lưới thành công.
    if (LandsOnMySide(ballPos->position.x))
        return true;

    if (ballTrajectory->a == 0.0f)
        return false;  // chưa có quỹ đạo xác định -> chưa thể khẳng định gì

    // Quỹ đạo là 1 parabol CỐ ĐỊNH trong không gian (y = a*(x+b)^2 + c), nên
    // có thể tính thẳng độ cao của bóng tại vị trí lưới, không phụ thuộc
    // bóng đang bay tới hay đang lùi khỏi lưới.
    float x      = BigRect::NET_X;
    float yAtNet = ballTrajectory->a * (x + ballTrajectory->b) * (x + ballTrajectory->b) + ballTrajectory->c;

    float netTopY = MatchObjectConfig::NETPOSITION.y + MatchObjectConfig::NETSIZE.y;

    return yAtNet > netTopY;
}

AIInputSystem::BallTrend AIInputSystem::ClassifyBallTrend(BallGamePlayState* ballGameplayState,
                                                          BallTrajectoryComponent* ballTrajectory,
                                                          PositionComponent* ballPos) const
{
    bool landingOnBotSide  = LandsOnMySide(ballGameplayState->landingX);
    bool crossesNetCleanly = CrossesNetCleanly(ballTrajectory, ballPos);

    if (!landingOnBotSide || !crossesNetCleanly)
    {
        // Case 1: landingX bên sân đối phương, HOẶC quỹ đạo bị lưới chặn -> chưa phải bóng của AI
        return BallTrend::TowardPlayerOrBlocked;
    }

    if (_onLeft ? ballGameplayState->landingX < _myOuterLineX : ballGameplayState->landingX > _myOuterLineX)
    {
        // Case 3: rơi bên sân AI nhưng ra ngoài vạch biên -> để bóng rơi, AI thắng điểm tự động
        return BallTrend::GoingOutOfBounds;
    }

    // Case 2: rơi TRONG sân AI, quỹ đạo qua lưới sạch -> bóng thật sự đang bay tới, phải phòng thủ
    return BallTrend::TowardBotCourt;
}

float AIInputSystem::PredictAttackLandingX(const AttackOption& option, PositionComponent* ballPos) const
{
    float a = option.data.a / option.boost.aFlattenFactor;

    float c = CalculateC(option.data.v0);
    if (ballPos->position.y > c)
        c = ballPos->position.y;
    c = c - c * option.decreaseCPercent / 100.0f;

    // powerSpike = 0: y hệt nhánh SpikeLight/Medium/Strong trong UpdateTrajectoryFromIntent.cpp
    float b = CalculateB(a, c, ballPos->position.x, ballPos->position.y, _attackDirection, /*isSpike=*/0);

    BallTrajectoryComponent simulated{};
    simulated.type  = TrajectoryType::Parabolic;
    simulated.a     = a;
    simulated.b     = b;
    simulated.c     = c;
    simulated.speed = _attackDirection * (option.data.v0 / 100.0f) * 4.0f * option.boost.v0Multiplier;

    Vec2 landing = CalculateLandingPoint(ballPos->position, &simulated, SystemConfig::MIN_Y);
    return landing.x;
}

const AIInputSystem::AttackOption* AIInputSystem::AttackOptions()
{
    // 3 phương án y hệt thông số trong UpdateTrajectoryFromIntent.cpp
    static const AttackOption options[3] = {
        {FinalIntent::SpikeLight, TrajectoryData{-0.045f, 80.0f}, TrajectoryBoost{1.6f, 3.0f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKETEMP},
        {FinalIntent::SpikeMedium, TrajectoryData{-0.008f, 80.0f}, TrajectoryBoost{4.0f, 3.8f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKETEMP},
        {FinalIntent::SpikeStrong, TrajectoryData{-0.0045f, 100.0f}, TrajectoryBoost{7.0f, 3.8f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKE},
    };
    return options;
}

void AIInputSystem::SimulateAttack(const AttackOption& option, PositionComponent* ballPos, float& landingX,
                                   int& frames, float* stepOut) const
{
    // Cùng công thức quỹ đạo với PredictAttackLandingX, nhưng đi từng frame để biết bóng bay bao lâu.
    float a = option.data.a / option.boost.aFlattenFactor;
    float c = CalculateC(option.data.v0);
    if (ballPos->position.y > c)
        c = ballPos->position.y;
    c          = c - c * option.decreaseCPercent / 100.0f;
    float b    = CalculateB(a, c, ballPos->position.x, ballPos->position.y, _attackDirection, /*isSpike=*/0);
    float step = _attackDirection * (option.data.v0 / 100.0f) * 4.0f * option.boost.v0Multiplier;
    if (stepOut)
        *stepOut = step;

    float x = ballPos->position.x;
    frames  = 0;
    while (frames < 600)
    {
        ++frames;
        x += step;
        if (a * (x + b) * (x + b) + c <= SystemConfig::MIN_Y)
            break;
    }
    landingX = PredictAttackLandingX(option, ballPos);  // điểm rơi chuẩn, y như mask đang dùng

    // Qua lưới sạch không: PredictAttackLandingX bỏ qua lưới, nên cú bay thấp (hay gặp ở SpikeLight)
    // có thể "rơi trong sân" trên giấy mà thực tế đập lưới. Đo ở bản đầu: chuyên gia mất 248/407 điểm
    // vì cú SpikeLight của chính nó.
    const float yAtNet  = a * (BigRect::NET_X + b) * (BigRect::NET_X + b) + c;
    const float netTopY = MatchObjectConfig::NETPOSITION.y + MatchObjectConfig::NETSIZE.y;
    if (yAtNet <= netTopY + MatchObjectConfig::BallSize.height)
        landingX = _onLeft ? BigRect::NET_X - 1.0f : BigRect::NET_X + 1.0f;  // coi như rơi lại sân mình
}

FinalIntent AIInputSystem::BestSpike(PositionComponent* ballPos, float* marginOut, bool varied) const
{
    // Đối thủ chạy SPEED px/frame tới điểm rơi (luật phòng thủ của bot luật, cũng là tốc độ người chơi).
    // Tầm với ~ nửa chiều rộng nhân vật + một khoảng đệm; chọn hơi rộng để chuyên gia không lạc quan.
    constexpr float OPPONENT_REACH = 60.0f;
    auto* oppPos  = _componentStorage->GetCharacterPositionPool().get(_onLeft ? GameConfig::OPPONENT_1
                                                                              : GameConfig::PLAYER);
    auto* oppSize = _componentStorage->GetCharacterSizePool().get(_onLeft ? GameConfig::OPPONENT_1
                                                                          : GameConfig::PLAYER);
    const float oppCenter = oppPos ? oppPos->position.x + (oppSize ? oppSize->size.x * 0.5f : 0.0f) : 0.0f;

    const AttackOption* options = AttackOptions();
    FinalIntent best = FinalIntent::None;
    float bestMargin = -1e9f;
    float margins[3];
    for (int i = 0; i < 3; ++i)
    {
        margins[i]    = -1e9f;
        float landing = 0.0f;
        float step    = 0.0f;
        int frames    = 0;
        SimulateAttack(options[i], ballPos, landing, frames, &step);
        // Vạch cuối sân: game xét "ra ngoài" theo vị trí bóng ở FRAME chạm đất, mà frame cuối bóng còn đi
        // thêm tới một bước (SpikeStrong ~28 px/frame) sau điểm giao quỹ đạo với mặt đất. Đo ở bản trước:
        // 145/170 điểm chuyên gia mất là SpikeStrong ra ngoài, bóng chết ở x 658-677 (vạch 650).
        // Nên điểm rơi phải cách vạch cuối ít nhất một bước bay + cỡ bóng.
        const float safety = std::fabs(step) + MatchObjectConfig::BallSize.width;
        const bool inside  = _onLeft ? (landing > BigRect::NET_X && landing <= _oppOuterLineX - safety)
                                     : (landing >= _oppOuterLineX + safety && landing < BigRect::NET_X);
        if (!inside)
            continue;
        margins[i] = std::fabs(landing - oppCenter) - SystemConfig::SPEED * frames - OPPONENT_REACH;
        if (margins[i] > bestMargin)
        {
            bestMargin = margins[i];
            best       = options[i].intent;
        }
    }

    // Đa dạng (chuyên gia, varied = true): khi có NHIỀU cú chắc ăn (khoảng dư > 0), rút thăm giữa chúng,
    // cú càng chắc càng dễ được chọn - thay vì luôn đúng một cú tốt nhất. Nhờ vậy model học được cả bỏ nhỏ
    // (SpikeLight) lẫn đập mạnh ở cùng loại tình huống, như người chơi "tuỳ quả".
    if (varied && bestMargin > 0.0f)
    {
        float total = 0.0f;
        for (int i = 0; i < 3; ++i)
            total += margins[i] > 0.0f ? margins[i] : 0.0f;
        static std::mt19937 rng{RunMode::Get().seed * 1597334677u + 5u};  // theo seed: đo lặp lại được
        float r = std::uniform_real_distribution<float>(0.0f, total)(rng);
        for (int i = 0; i < 3; ++i)
        {
            if (margins[i] <= 0.0f)
                continue;
            r -= margins[i];
            if (r <= 0.0f)
            {
                best = options[i].intent;
                break;
            }
        }
    }
    if (best == FinalIntent::None)
        best = PickAttackIntent(ballPos);  // không cú nào chắc vào sân: cách chọn của bot luật
    if (marginOut)
        *marginOut = bestMargin;
    return best;
}

bool AIInputSystem::UpdateExpertPossession(const RallyState* rally, const BallGamePlayState* ball,
                                           const MatchState* match)
{
    RunModeData& mode = RunMode::Get();
    if (mode.expertProb <= 0.0f)
    {
        mode.expertNow = false;
        return false;
    }
    // Lượt cầm bóng của mình bắt đầu khi đối thủ vừa chạm bóng (bóng sắp sang), hoặc khi tới lượt
    // mình giao. Mình chạm bóng thì vẫn là lượt cũ - không rút thăm lại giữa chuỗi Set -> Jump -> Spike.
    const int touch       = static_cast<int>(rally->lastTouch);
    const bool myServe    = ball->stateFrame != Alive && match->servingTeam == _myTeam;
    const int key         = myServe ? -2 : touch;
    if (key != _expertPossessionKey)
    {
        _expertPossessionKey = key;
        if (myServe || (touch != static_cast<int>(_self) && touch != static_cast<int>(GameConfig::NONE)))
        {
            static std::mt19937 rng{mode.seed * 3266489917u + 3u};  // theo seed: đo lặp lại được
            _expertThisPossession = std::uniform_real_distribution<float>(0.0f, 1.0f)(rng) < mode.expertProb;
            // Lượt này chuyên gia có "đập nhanh" không (đập ngay quả đầu khi bóng vào vùng detect).
            static std::mt19937 quickRng{mode.seed * 1103515245u + 11u};
            _expertQuickThisPossession =
                std::uniform_real_distribution<float>(0.0f, 1.0f)(quickRng) < mode.expertQuickSpike;
        }
    }
    mode.expertNow = _expertThisPossession;
    return _expertThisPossession;
}

FinalIntent AIInputSystem::ExpertDecide(FinalIntent ruleIntent, const RallyState* rally,
                                        const BallGamePlayState* ball, PositionComponent* ballPos,
                                        PositionComponent* selfPos)
{
    if (ball->stateFrame != Alive)
        return ruleIntent;  // giao bóng: như luật (Serve đúng lượt)

    const RunModeData& mode = RunMode::Get();
    const bool varied       = mode.expertVaried;
    const bool onGround     = selfPos->position.y <= SystemConfig::MIN_Y;
    const bool inSpike      = BallInDetectZone(FinalIntent::SpikeMedium);
    const bool afterOwnSet  = _lastOwnContact == FinalIntent::Set && rally->lastTouch == _self;

    // 1. Đang trên không: bóng vào vùng thì đập (cú chắc ăn, có rút thăm nếu varied).
    if (!onGround)
    {
        if (!inSpike)
            return FinalIntent::None;
        // Đo 50 trận (seed 1600000): đập trên không chỉ SpikeStrong ghi điểm (chuyên gia 520/1105,
        // bot ML 277/706); SpikeMedium trên không 0/2298 - bot luật luôn đỡ được. Nên trên không ưu
        // tiên SpikeStrong nếu cú đó qua lưới và rơi chắc trong sân (expertAirStrong).
        if (mode.expertAirStrong && SpikeLandsInCourt(FinalIntent::SpikeStrong, ballPos))
            return FinalIntent::SpikeStrong;
        return BestSpike(ballPos, nullptr, varied);
    }

    // 2. Vừa Set xong: thường thì nhảy đúng lúc để chạm bóng ở gần đỉnh (cùng cửa sổ với luật nhảy,
    //    ADR-012). Một phần các cú Set (expertGroundAfterSet) KHÔNG nhảy mà chờ bóng rơi vừa tầm rồi đập
    //    từ mặt đất - cách ghi điểm thứ hai, để model không chỉ biết một khuôn Set -> Jump -> Spike.
    if (afterOwnSet)
    {
        if (!_expertSetSeen)
        {
            _expertSetSeen = true;
            static std::mt19937 rng{mode.seed * 2869860233u + 7u};  // theo seed: đo lặp lại được
            _expertGroundThisSet = std::uniform_real_distribution<float>(0.0f, 1.0f)(rng) < mode.expertGroundAfterSet;
        }
        if (!_expertGroundThisSet)
        {
            const int t = FramesUntilAirContact();
            if (t >= JUMP_CONTACT_MIN_T && t <= JUMP_CONTACT_MAX_T)
                return FinalIntent::Jump;
        }
        return inSpike ? BestSpike(ballPos, nullptr, varied) : FinalIntent::None;  // đập từ đất khi bóng vào tầm
    }

    // 3. Bóng vào vùng đập: đập ngay nếu đối thủ không kịp đón (khoảng dư > 0).
    const int teamTouches = rally->lastTouch == _self ? rally->touchCount : 0;
    const bool canSet     = teamTouches <= MatchRuleConfig::MAX_TOUCH_PER_TEAM - 2;  // Set xong còn 1 chạm để đập
    if (inSpike)
    {
        float margin      = 0.0f;
        FinalIntent spike = BestSpike(ballPos, &margin, varied);
        // Đập nhanh: quả đầu của lượt (đội mình chưa chạm), bóng vừa vào vùng detect và có ít nhất một cú
        // đập qua lưới + rơi trong sân (margin > -1e8) thì đập luôn, không Set - cách ghi điểm người dùng
        // muốn ("bóng vào vùng detect thì spike luôn"), áp cho một phần các lượt (expertQuickSpike).
        const bool quick = _expertQuickThisPossession && teamTouches == 0 && margin > mode.expertQuickMinMargin;
        if (margin > 0.0f || !canSet || quick)
            return spike;
    }

    // Lượt đập nhanh, quả đầu: vùng Set (<= 100%) rộng hơn vùng đập (< 50%) nên bóng luôn vào vùng Set
    // trước - không Set mà chờ bóng vào vùng đập (bước 3). Nếu bóng tới vùng đập mà không có cú vào sân
    // hoặc khoảng dư <= expertQuickMinMargin thì quick = false, vẫn Set như thường (bước 4).
    const bool waitQuick = _expertQuickThisPossession && teamTouches == 0 && !inSpike;
    // Set xa lưới: đo 50 trận (seed 1600000), Set cách lưới < 430 px thì chuỗi Set -> Jump -> Spike ghi
    // 0% (bóng trôi ~100 px về lưới, SpikeStrong ra ngoài, chỉ còn SpikeMedium bị đỡ); Set xa hơn ghi
    // ~30%. Nên chờ bóng đi sâu hơn rồi mới Set - trừ khi bóng đã vào vùng đập (sắp rơi, Set luôn).
    const bool waitFar = mode.expertSetMinDist > 0.0f && teamTouches == 0 && !inSpike &&
                         std::fabs(selfPos->position.x - BigRect::NET_X) < mode.expertSetMinDist;
    if ((waitQuick || waitFar) && ruleIntent == FinalIntent::Set)
        return FinalIntent::None;

    // 4. Còn chạm: Set để chuẩn bị nhảy đập trên cao.
    if (canSet && !waitQuick && !waitFar && BallInDetectZone(FinalIntent::Set))
        return FinalIntent::Set;

    // 5. Không còn đường nào hơn: bóng trong vùng thì đập, không thì để luật quyết.
    return inSpike ? BestSpike(ballPos, nullptr, varied) : ruleIntent;
}

bool AIInputSystem::SpikeLandsInCourt(FinalIntent spike, PositionComponent* ballPos) const
{
    const AttackOption* options = AttackOptions();

    for (int i = 0; i < 3; ++i)
    {
        if (options[i].intent != spike)
            continue;
        if (!RunMode::Get().safeSpikeMask)
            return LandsInsideOpponentCourt(PredictAttackLandingX(options[i], ballPos));  // cách cũ

        // Cùng phép kiểm tra đã sửa cho chuyên gia (BestSpike): bóng phải QUA LƯỚI (SimulateAttack đưa điểm
        // rơi về sân mình nếu không qua) và rơi cách vạch cuối ít nhất một bước bay + cỡ bóng - vì game xét
        // "ra ngoài" theo vị trí bóng ở frame chạm đất, frame cuối bóng còn đi thêm tới một bước.
        // Đo 21/10: ~85% điểm bot ML mất là do cú đập của chính nó (230/273 điểm với model v3) - đúng loại
        // lỗi chuyên gia từng mắc trước khi sửa.
        float landing = 0.0f;
        float step    = 0.0f;
        int frames    = 0;
        SimulateAttack(options[i], ballPos, landing, frames, &step);
        const float safety = std::fabs(step) + MatchObjectConfig::BallSize.width;
        return _onLeft ? (landing > BigRect::NET_X && landing <= _oppOuterLineX - safety)
                       : (landing >= _oppOuterLineX + safety && landing < BigRect::NET_X);
    }
    return false;  // không phải một trong 3 cú đập -> coi như không hợp lệ
}

float AIInputSystem::BallDetectPercent() const
{
    auto* selfPos  = _componentStorage->GetCharacterPositionPool().get(_self);
    auto* selfSize = _componentStorage->GetCharacterSizePool().get(_self);
    auto* ballPos  = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    if (!selfPos || !ballPos)
        return -1.0f;
    return DetectPercentAt(selfPos->position.x, selfPos->position.y, ballPos->position.x, ballPos->position.y);
}

float AIInputSystem::DetectPercentAt(float selfX, float selfY, float ballX, float ballY) const
{
    auto* selfSize = _componentStorage->GetCharacterSizePool().get(_self);
    if (!selfSize || selfSize->size.y <= 0.0f)
        return -1.0f;

    // Y HỆT DetectPlayerBall() trong TrajectoryMath.cpp. Tâm nhân vật bị lệch đi bởi
    // WIDTH_PERCENT_CHANGE (+15%) và HEIGHT_PERCENT_CHANGE (-20%), bỏ phần này thì vùng
    // detect tính ra lệch tâm và masking sai.
    const Vec2 sizeDelta{selfSize->size.x * SystemConfig::WIDTH_PERCENT_CHANGE / 100.0f,
                         selfSize->size.y * SystemConfig::HEIGHT_PERCENT_CHANGE / 100.0f};
    const Vec2 selfCenter{selfX + selfSize->size.x * 0.5f + sizeDelta.x / 2.0f,
                          selfY + selfSize->size.y * 0.5f + sizeDelta.y / 2.0f};
    const Vec2 ballCenter{ballX + MatchObjectConfig::BallSize.width * 0.5f,
                          ballY + MatchObjectConfig::BallSize.height * 0.5f};

    return (ballCenter - selfCenter).length() / selfSize->size.y * 100.0f;
}

int AIInputSystem::FramesUntilAirContact() const
{
    auto* selfPos = _componentStorage->GetCharacterPositionPool().get(_self);
    auto* ballPos = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto* traj    = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    if (!selfPos || !ballPos || !traj || traj->type != TrajectoryType::Parabolic)
        return -1;

    // Chép đúng ApplyIntentToComponent.h + VelocityIntentSystem: frame nhảy đầu tiên nhân
    // vật còn ở mặt đất nên không bị trọng lực, được +2*9,81; các frame sau +2*9,81 - 9,81.
    // Đã khớp log: 265 -> 284,62 -> 294,43 -> ... -> 569,11 ở frame thứ 30.
    const float rise = -2.0f * SystemConfig::GRAVITY;
    float selfY      = selfPos->position.y;
    float ballX      = ballPos->position.x;
    for (int t = 1; t <= JUMP_SIM_FRAMES; ++t)
    {
        if (t == 1)
            selfY += rise;
        else if (t <= SystemConfig::JUMP_FRAME)
            selfY += rise + SystemConfig::GRAVITY;
        else
            selfY += SystemConfig::GRAVITY;
        if (selfY <= SystemConfig::MIN_Y)
            return -1;  // đã tiếp đất mà chưa gặp bóng

        ballX += traj->speed;
        const float ballY = traj->a * (ballX + traj->b) * (ballX + traj->b) + traj->c;
        if (ballY <= SystemConfig::MIN_Y)
            return -1;  // bóng chạm đất trước

        const float percent = DetectPercentAt(selfPos->position.x, selfY, ballX, ballY);
        if (percent >= 0.0f && percent < 50.0f)
            return t;
    }
    return -1;
}

bool AIInputSystem::BallInDetectZone(FinalIntent intent) const
{
    const float percent = BallDetectPercent();
    if (percent < 0.0f)
        return false;  // chưa đọc được trạng thái -> không cho chạm

    // Ngưỡng và phép so sánh phải khớp UpdateTrajectoryFromIntent.cpp:
    //   Spike*  : 50.0f  với inclusive = false  -> phải DƯỚI ngưỡng
    //   Bump    : DISTANCE_DETECTION_BUMP = 60  với inclusive = true  -> được phép bằng
    //   Set     : DISTANCE_DETECTION_SET  = 100 với inclusive = true
    //   Serve   : không qua TryApplyTrajectory, chỉ cần stateFrame == Reset và đúng lượt
    if (IsSpikeIntent(intent) || intent == FinalIntent::Spike)
        return percent < 50.0f;
    if (intent == FinalIntent::Bump)
        return percent <= SystemConfig::DISTANCE_DETECTION_BUMP;
    if (intent == FinalIntent::Set)
        return percent <= SystemConfig::DISTANCE_DETECTION_SET;
    return true;  // Serve và các intent không chạm bóng: để luật khác xử lý
}

FinalIntent AIInputSystem::PickAttackIntent(PositionComponent* ballPos) const
{
    const AttackOption* options = AttackOptions();

    // Mỗi frame build lại danh sách các phương án mà nếu đánh sẽ rơi TRONG sân đối phương
    std::vector<FinalIntent> validOptions;
    validOptions.reserve(3);

    // CHỈ phía bot ML: dùng phép kiểm tra đã sửa (qua lưới + cách vạch cuối một bước bay, SpikeLandsInCourt).
    // Đo 100 trận 21/10: 704/1.103 điểm bot ML tự đánh hỏng là SpikeLight từ mặt đất đập lưới - do hàm này
    // chọn theo phép kiểm tra cũ (không xét lưới). Bot luật giữ nguyên cách cũ để phép so sánh vẫn công bằng.
    const RunModeData& mode = RunMode::Get();
    const bool safeForMl    = mode.safeRuleAttack && static_cast<int>(_self) == mode.mlAgentEntity;

    for (int i = 0; i < 3; ++i)
    {
        const bool ok = safeForMl ? SpikeLandsInCourt(options[i].intent, ballPos)
                                  : LandsInsideOpponentCourt(PredictAttackLandingX(options[i], ballPos));
        if (ok)
        {
            validOptions.push_back(options[i].intent);
        }
    }

    // Bot ML mà không cú nào qua được phép kiểm tra mới: lùi về cách cũ, không tệ hơn trước.
    if (validOptions.empty() && safeForMl)
    {
        for (int i = 0; i < 3; ++i)
        {
            if (LandsInsideOpponentCourt(PredictAttackLandingX(options[i], ballPos)))
                validOptions.push_back(options[i].intent);
        }
    }

    if (validOptions.empty())
    {
        // Không phương án nào chắc chắn "trong sân" (vd AI đứng vị trí xấu) ->
        // vẫn phải đánh để không bỏ bóng, random cả 3 làm phương án dự phòng.
        return options[std::rand() % 3].intent;
    }

    return validOptions[std::rand() % validOptions.size()];
}

void AIInputSystem::update()
{
    auto& characterStatusPool = _componentStorage->GetCharacterActionStatePool();
    bool canGenerateIntent    = characterStatusPool.get(_self)->status == ActionState::None;
    if (!canGenerateIntent)  // hành động trước còn đang trong cooldown
    {
        return;
    }

    auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto ballGameplayState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto ballTrajectory    = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto ballPos           = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto& characterPos     = _componentStorage->GetCharacterPositionPool();
    auto AI1Pos            = characterPos.get(_self);
    auto& intentPool       = _intentStorage->GetCharacterIntentPool();
    auto matchState        = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    // ====== SỐ CHẠM CHO PHÉP ======
    // Bot luật: 1 chạm mỗi lượt nhận bóng (hành vi gốc, giữ nguyên).
    // Bot ML: tối đa MAX_TOUCH_PER_TEAM = 3, bằng quyền của người chơi. Luật game chỉ phạt
    // khi touchCount > MAX_TOUCH_PER_TEAM (GameRuleEvaluationSystem), và
    // MAX_TOUCH_PER_CHARACTER được khai báo nhưng không chỗ nào dùng - không có lỗi "chạm
    // hai lần" nên việc này hợp luật.
    // Cú đỡ của mình chỉ còn hiệu lực tới khi người khác chạm bóng. Thiếu dòng này, một cú
    // Set từ lượt trước sẽ cho phép Jump mãi mãi (luật 4 bên dưới).
    if (rallyState->lastTouch != _self)
    {
        _expertSetSeen       = false;
        _expertGroundThisSet = false;
        _lastOwnContact  = FinalIntent::None;
        _jumpDrawnForSet = false;
        _jumpPlanned     = false;
        _airSpikePending = false;
    }

    // Cú nhảy theo kế hoạch đã xong (tiếp đất) mà chưa đập được -> thôi, không treo mãi.
    if (_airSpikePending)
    {
        ++_framesSinceJump;
        if (_framesSinceJump > 2 && AI1Pos->position.y <= SystemConfig::MIN_Y)
            _airSpikePending = false;
    }

    const bool mlDrivesMe = static_cast<int>(_self) == RunMode::Get().mlAgentEntity;
    const int touchLimit =
        mlDrivesMe ? MatchRuleConfig::MAX_TOUCH_PER_TEAM : MatchRuleConfig::MAX_TOUCH_PER_CHARACTER;

    if (HasUsedItsTouch(rallyState, touchLimit))
    {
        CharacterIntent intent{};

        float dx = _readyX - AI1Pos->position.x;

        if (fabs(dx) > SystemConfig::SPEED)
        {
            intent.moveX = (dx > 0.0f) ? SystemConfig::SPEED : -SystemConfig::SPEED;
        }

        intentPool.add(_self, intent);
        return;
    }

   

    CharacterIntent intent{};
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;

    if (ballGameplayState->stateFrame == Reset && matchState->servingTeam == _myTeam)
    {
        intent.finalIntent = FinalIntent::Serve;
    }
    else if (ballGameplayState->stateFrame == Alive)
    {
        BallTrend trend = ClassifyBallTrend(ballGameplayState, ballTrajectory, ballPos);

        // ================= PHASE: PHÒNG THỦ =================
        // Case 2 (TowardBotCourt) -> chạy tới điểm rơi để đón bóng.
        // Case 1 & 3              -> lùi về vị trí thủ thế giữa sân.
        float targetX = (trend == BallTrend::TowardBotCourt)
                            ? ballGameplayState->landingX + ChaseOffset(rallyState)
                            : _readyX;

        float dx = targetX - AI1Pos->position.x;

        if (fabs(dx) > SystemConfig::SPEED)
        {
            intent.moveX = (dx > 0.0f) ? SystemConfig::SPEED : -SystemConfig::SPEED;
        }
        else
        {
            intent.moveX = 0.0f;

            // ================= PHASE: TẤN CÔNG =================
            // Chỉ tấn công khi: bóng thật sự đang bay tới (case 2), AI đã đứng
            // đúng điểm rơi, và bóng đã đủ thấp trong tầm với.
            if (trend == BallTrend::TowardBotCourt && ballPos->position.y < AI_ATTACK_HEIGHT_THRESHOLD)
            {
                intent.finalIntent = PickAttackIntent(ballPos);
            }
        }
    }

    // Model được hỏi ở MỌI frame, không chỉ khi luật đã quyết định làm gì đó.
    //
    // TRƯỚC ĐÂY khối này nằm trong `if (moveX != 0 || finalIntent != None)`, nên model chỉ
    // được hỏi khi luật đã sinh ra một hành động hoặc một bước di chuyển. Hệ quả đo được:
    //
    //   - `Jump` chỉ dùng 1 lần trong 5 trận. Lần theo luồng luật: khi bot đã đứng đúng
    //     điểm rơi (moveX = 0) mà bóng còn cao (ball_y >= AI_ATTACK_HEIGHT_THRESHOLD = 300)
    //     thì luật không sinh gì cả -> model không được hỏi. Đó chính là khoảnh khắc người
    //     chơi bật nhảy. Trong dữ liệu người chơi, 102/155 lần `Jump` được theo sau bởi một
    //     cú đập, và 31 lần `Jump` -> `Serve` (giao bóng bật nhảy).
    //
    //   - `SpikeStrong` rất ít. Ngưỡng 300 nghĩa là model chỉ được hỏi khi ball_y < 300,
    //     nhưng trong dữ liệu người chơi `SpikeStrong` xảy ra ở ball_y ~540. Model chưa bao
    //     giờ được hỏi trong đúng vùng độ cao mà nó học cú đó.
    //
    // Luật game KHÔNG đổi: số chạm, lỗi, quỹ đạo đều y nguyên. Chỉ đổi thời điểm model
    // được hỏi. Hai luật masking bên dưới vẫn bảo đảm model không xoá được quyết định của
    // luật và không ra cú bay ngoài sân.
    //
    // ============ CHUYÊN GIA GHI ĐIỂM (chỉ MatchRunner, expertProb > 0) ============
    // Lượt cầm bóng được giao cho chuyên gia thì chuyên gia quyết định thay model + luật nhảy.
    // Di chuyển vẫn theo luật như mọi bot khác, nên trạng thái bot gặp là trạng thái bot ML thật.
    const bool expertNow = mlDrivesMe && UpdateExpertPossession(rallyState, ballGameplayState, matchState);
    if (expertNow)
    {
        intent.finalIntent = ExpertDecide(intent.finalIntent, rallyState, ballGameplayState, ballPos, AI1Pos);
    }

    // Khối model chỉ chạy khi chuyên gia không điều khiển lượt này.
    if (!expertNow)
    {
        // ============ MODEL AI QUYẾT ĐỊNH HÀNH ĐỘNG ============
        // Model `action` (schema v1, 21 feature, macro-F1 0.730 với decision_tree) quyết định
        // LÀM GÌ ở frame này: None / Jump / Bump / Set / Serve / Spike nhẹ-vừa-mạnh.
        //
        // Khác bản trước dùng `spike_choice` 6 feature: bản đó chỉ đổi được CƯỜNG ĐỘ cú đập và
        // train trên 41 mẫu (macro-F1 0.485). Bản này thấy cả trạng thái bóng nên quyết định
        // được cả loại hành động.
        //
        // 21 feature do MlFeaturesV1::Build tính, đã đối chiếu khớp 21/21 với features.build()
        // của Python trên 3512 frame, cho cả hai phía sân.
        if (static_cast<int>(_self) == RunMode::Get().mlAgentEntity)
        {
            // RANDOM FOREST, KHONG dung Decision Tree - day la DIEU KIEN CAN cho phan
            // lay mau theo xac suat o duoi, khong phai chuyen chon model cho vui.
            //
            // Do tren toan bo Logs/v1 (316 tran, 1.012.969 frame), trung vi do tu tin:
            //     Decision Tree : 1.0000   (97,8% frame co top >= 0.99)
            //     Random Forest : 0.9391   (24,3% frame co top >= 0.99)
            //
            // Dau ra cua MOT cay la phan phoi o la, gan nhu one-hot. Lay mau tu one-hot thi
            // luon ra dung mot lop, va temperature cung vo nghia vi 1^(1/T) = 1 va 0^(1/T) = 0.
            // Tuc dung DT se AM THAM tat toan bo co che da dang -> quay ve dung van de
            // 97% SpikeMedium ban dau. DT re hon nhieu (header 74 KB vs 8.827 KB, macro-F1
            // 0.672 vs 0.671) nhung khong dung duoc o day.
            //
            // Schema v1 (21 feature), KHONG dung v2: do 2 lan doc lap, v2 deu te hon
            // (0.352 -> 0.335 tren 12 tran; 0.671 -> 0.623 tren 315 tran). Xem ADR-010.
            //
            // Train KHONG co class_weight: balanced. Do tren 7 tran nguoi choi (15/10):
            //
            //     model                  macro-F1   de xuat cu dap   bong trong tam   do chinh xac
            //     RF + balanced            0.671        2145 frame        88              4,10%
            //     RF khong weight          0.782          53 frame        53            100,00%
            //     DT khong weight          0.794        1770 frame        68              3,84%
            //
            // balanced nhan trong so lop hiem len theo N/(k*n_c) - SpikeLight co 447 mau tren
            // 268.619 frame nen bi day rat cao, model hang hai de xuat cu dap o ca nhung
            // trang thai ma nhan that la None: trung vi khoang cach bot<->bong khi de xuat la
            // 1806 px, tuc bong dang o nua san ben kia.
            //
            // Chu y DT: macro-F1 CAO NHAT (0.794) ma do chinh xac de xuat THAP NHAT (3,84%).
            // Hai chi so di nguoc chieu, nen phai chon model theo muc tieu that chu khong
            // theo chi so quen dung. Xem scripts/eval_proposal_precision.py.
            namespace ml = spike_ai::action_random_forest;
            static_assert(ml::kNumFeatures == MlFeaturesV1::kNumFeatures, "so feature lech giua model va MlFeaturesV1");

            // Golden test chạy một lần: 20 vector từ Python phải cho cùng kết quả ở C++.
            static const bool goldenOk = ml::RunGoldenTest();
            if (!goldenOk)
            {
                AXLOG("[MLAction] GOLDEN TEST FAIL - model C++ lech voi Python, hay export lai!");
            }

            float x[MlFeaturesV1::kNumFeatures];
            if (goldenOk && MlFeaturesV1::Build(_componentStorage, _self, x))
            {
                // Dựng bảng ánh xạ một lần, tra theo TÊN lớp trong header đã export
                static const std::array<FinalIntent, ml::kNumClasses> kIntentOf = [] {
                    std::array<FinalIntent, ml::kNumClasses> m{};
                    for (int i = 0; i < ml::kNumClasses; ++i)
                        m[i] = IntentFromClassName(ml::kClassNames[i]);
                    return m;
                }();
                float proba[ml::kNumClasses];
                ml::PredictProba(x, proba);

                int topIdx = 0;
                for (int c = 1; c < ml::kNumClasses; ++c)
                {
                    if (proba[c] > proba[topIdx]) topIdx = c;
                }
                FinalIntent proposed = kIntentOf[topIdx];

                // CÓ RA TAY KHÔNG: ngưỡng trên P(ra tay) = 1 - P(None), xem USE_ACT_THRESHOLD.
                float pNone = 0.0f;
                for (int c = 0; c < ml::kNumClasses; ++c)
                {
                    if (kIntentOf[c] == FinalIntent::None) pNone = proba[c];
                }
                const bool serveTurn = ballGameplayState->stateFrame == Reset && matchState->servingTeam == _myTeam;
                const bool wantsToAct = USE_ACT_THRESHOLD ? (1.0f - pNone) > ACT_THRESHOLD
                                                          : proposed != FinalIntent::None;
                if (USE_ACT_THRESHOLD && !wantsToAct)
                {
                    proposed = FinalIntent::None;
                }
                if (!RunMode::Get().modelMayJump && proposed == FinalIntent::Jump)
                {
                    proposed = FinalIntent::None;  // nếu không lấy mẫu thì cũng không được nhảy
                }

                // LẤY MẪU THEO XÁC SUẤT (xem ghi chú USE_PROBA_SAMPLING ở đầu file).
                //
                // Chỉ chạy khi đã quyết định là CÓ ra tay - ở đây chỉ chọn ra tay BẰNG GÌ.
                if (USE_PROBA_SAMPLING && wantsToAct)
                {
                    if (USE_ACT_THRESHOLD)
                        proposed = FinalIntent::None;  // không còn cú hợp lệ nào thì đứng yên
                    // Loại cú đập bay ra ngoài sân NGAY TRONG trọng số, tức là LỌC TRƯỚC
                    // khi rút thăm. Nếu rút thăm trước rồi mới loại thì mỗi lần rút phải
                    // bỏ đi, và kết quả lại dồn về cú có xác suất cao nhất - đúng cái
                    // argmax đang làm, tức là không sửa được gì.
                    float weight[ml::kNumClasses];
                    float total = 0.0f;
                    for (int c = 0; c < ml::kNumClasses; ++c)
                    {
                        const FinalIntent cand = kIntentOf[c];
                        const bool usable = cand != FinalIntent::None &&
                                            (RunMode::Get().modelMayJump || cand != FinalIntent::Jump) &&
                                            (cand != FinalIntent::Serve || serveTurn) &&
                                            (!IsSpikeIntent(cand) || SpikeLandsInCourt(cand, ballPos));
                        // Làm phẳng bằng temperature (xem PROBA_SAMPLING_TEMPERATURE).
                        // std::pow với số âm/0 là an toàn ở đây vì proba luôn >= 0.
                        weight[c] = usable ? std::pow(proba[c], 1.0f / PROBA_SAMPLING_TEMPERATURE)
                                           : 0.0f;
                        total += weight[c];
                    }

                    if (total > 0.0f)
                    {
                        std::uniform_real_distribution<float> pick(0.0f, total);
                        float r = pick(Rng());
                        for (int c = 0; c < ml::kNumClasses; ++c)
                        {
                            if (weight[c] <= 0.0f) continue;
                            r -= weight[c];
                            if (r <= 0.0f)
                            {
                                proposed = kIntentOf[c];
                                break;
                            }
                        }
                    }
                    // total == 0 -> không có hành động nào hợp lệ. Cách argmax: giữ nguyên
                    // `proposed` và để các luật masking bên dưới xử lý; cách ngưỡng: None.
                }

                // ACTION MASKING: model chỉ được THÊM hoặc ĐỔI hành động, KHÔNG được XOÁ
                // hành động mà luật đã quyết.
                //
                // Luật 1 - model không được ghi đè bằng None.
                //   Đây là bug đã làm trận đứng vĩnh viễn: luật đặt Serve khi tới lượt giao
                //   bóng, model trả về None và xoá mất, nên bot không bao giờ giao. Đo được
                //   10.597 frame đứng im ở trạng thái Reset chờ bot phải giao bóng.
                //   Gốc rễ: ở trạng thái đó feature là ball_alive=1 nhưng ball_a=0, ball_vx=0
                //   - bóng vừa "sống" vừa đứng im, tổ hợp không tồn tại trong 124.172 frame
                //   dữ liệu train, nên model rơi vào nhánh None. Serve có F1 = 1.00 trong
                //   cross-validation mà vẫn vô dụng ở đây.
                //
                // Luật 2 - cú đập phải thật sự rơi trong sân đối phương.
                //   Model không mô phỏng vật lý nên có thể chọn cú bay ra ngoài biên, trong
                //   đúng tình huống mà PickAttackIntent biết tránh. Đo được: model đề xuất cú
                //   không hợp lệ 95% số lần ở sân phải.
                //
                // Hai luật này bảo đảm bot ML KHÔNG BAO GIỜ tệ hơn bot luật: xấu nhất là nó
                // giữ nguyên quyết định của luật.
                //
                // Các hành động khác không cần chặn: UpdateTrajectoryFromIntent tự bỏ qua
                // Bump/Set/Spike khi bóng ngoài tầm (distancePercent >= 50) và bỏ qua Serve
                // khi không phải lượt mình, đều không để lại tác dụng phụ nào.
                const bool erasesRuleAction = proposed == FinalIntent::None &&
                                              intent.finalIntent != FinalIntent::None;

                // Vẫn giữ kiểm tra này dù phần lấy mẫu đã lọc: nó còn phải bảo vệ nhánh
                // argmax (khi USE_PROBA_SAMPLING = false, hoặc khi total == 0).
                const bool invalidSpike = IsSpikeIntent(proposed) && !SpikeLandsInCourt(proposed, ballPos);

                // Luật 3 - CHẠM BÓNG chỉ được khi bóng Ở TRONG VÙNG DETECT.
                //
                // Dùng đúng công thức DetectPlayerBall() của game (xem BallInDetectZone):
                // khoảng cách tâm-bóng tới tâm-nhân-vật, theo % chiều cao nhân vật, với
                // ngưỡng riêng từng cú - Spike* < 50%, Bump <= 60%, Set <= 100%.
                //
                // BẢN TRƯỚC CHỈ KIỂM CHIỀU DỌC (ball_y - self_y < 35) và đó là một lỗi:
                // đo được 10/10 lần model đề xuất Set đều lúc bóng nằm trên đất sau khi mất
                // điểm, cách bot 324-1131 đơn vị theo chiều ngang - chênh lệch dọc bằng 0 nên
                // luật cũ cho qua hết. Game tự bỏ qua nên không gây hại trực tiếp, nhưng mọi
                // số đo về Set/SpikeStrong thành số rác.
                //
                // Nhờ luật này, "Jump tới khi bóng chạm vào vùng Detect -> Spike" thành khả
                // thi: bot nhảy lên, và cú đập chỉ nổ khi bóng thật sự vào vùng với tới được.
                //
                // Ngoại lệ: nếu chính LUẬT cũng đang muốn một cú chạm (vd Serve lúc Reset)
                // thì không chặn - model chỉ đang đổi loại cú, không tạo thêm lần chạm.
                const bool contactOutOfReach = IsContactIntent(proposed) &&
                                               !BallInDetectZone(proposed) &&
                                               intent.finalIntent == FinalIntent::None;

                // Luật 4 - chỉ được Jump trong hai tình huống, theo yêu cầu của người chơi:
                //
                //   (1) Giao bóng: có thể Serve luôn, hoặc Jump rồi Serve ở điểm cao nhất.
                //   (2) Đối thủ đánh sang, bóng vào vùng detect -> mình Set -> Jump -> chờ
                //       bóng rơi lại vào vùng detect -> Spike Light/Medium/Strong.
                //
                // Ngoài hai chỗ đó thì KHÔNG nhảy: bóng vào vùng detect thì đập thẳng từ mặt
                // đất, không cần nhảy.
                //
                // LỊCH SỬ: luật này từng bị tôi BỎ ngày 14/10 vì đọc sai mô tả của người chơi -
                // tôi hiểu "spike luôn" là "nhảy rồi đập ngay", trong khi nó nghĩa là "đập
                // thẳng từ mặt đất, không nhảy". Bỏ luật thì bot nhảy bừa trở lại. Đã hoàn lại.
                //
                // Nhận cả Bump ngoài Set: đo trên 315 trận, người chơi bấm Shift (Bump) 1037
                // lần và S (Set) 1081 lần - gần bằng nhau, cú đỡ đầu tiên được ghi thành một
                // trong hai tuỳ phím. Chặn chỉ theo Set sẽ loại oan gần một nửa số chuỗi.
                const bool justReceivedMyself =
                    (_lastOwnContact == FinalIntent::Set || _lastOwnContact == FinalIntent::Bump) &&
                    rallyState->lastTouch == _self;
                const bool myServeNow = ballGameplayState->stateFrame == Reset &&
                                        matchState->servingTeam == _myTeam;
                const bool unjustifiedJump =
                    proposed == FinalIntent::Jump && !justReceivedMyself && !myServeNow;

                // Luật 5 - Serve chỉ khi đúng lượt mình giao. Smoke test 16/10: model đề xuất
                // Serve giữa không trung; game bỏ qua nhưng làm sai số đếm hành động.
                const bool invalidServe = proposed == FinalIntent::Serve && !serveTurn;
                // Ngược lại: tới lượt giao thì không được đổi Serve thành cú khác - game bỏ qua
                // cú đó và bot đứng mãi không giao (cùng kiểu lỗi của Luật 1).
                const bool replacesServe = serveTurn && intent.finalIntent == FinalIntent::Serve &&
                                           proposed != FinalIntent::Serve;

                if (!erasesRuleAction && !invalidSpike && !contactOutOfReach && !unjustifiedJump && !invalidServe &&
                    !replacesServe)
                {
                    intent.finalIntent = proposed;
                }

                // ============ NHẢY ĐẬP SAU CÚ SET: VẬT LÝ QUYẾT ĐỊNH LÚC NHẢY ============
                // Chuỗi của người chơi: đối thủ đánh sang -> Set -> chờ bóng rơi -> Jump ->
                // đập ở gần điểm cao nhất. Xem các hằng số P_JUMP_AFTER_SET, JUMP_CONTACT_* ở
                // đầu file và ADR-012.
                const bool afterOwnSet = _lastOwnContact == FinalIntent::Set && rallyState->lastTouch == _self &&
                                         ballGameplayState->stateFrame == Alive;
                const bool onGround = AI1Pos->position.y <= SystemConfig::MIN_Y;

                // (1) Rút thăm MỘT lần cho mỗi cú Set: lần này có nhảy đập không.
                if (afterOwnSet && !_jumpDrawnForSet)
                {
                    _jumpDrawnForSet = true;
                    std::uniform_real_distribution<float> coin(0.0f, 1.0f);
                    const RunModeData& mode = RunMode::Get();
                    float pJump             = mode.jumpAfterSetProb;
                    // Dồn lượt nhảy cho cú Set XA lưới (jumpFarDist > 0): đo 50 trận seed 1600000, Set cách
                    // lưới < 430 px thì Set -> Jump -> Spike ghi 0% (chỉ còn SpikeMedium, bị đỡ), xa hơn
                    // ghi 19-28%. jumpFarProb chọn sao cho tỉ lệ nhảy TỔNG vẫn ~31,4% như dữ liệu người chơi.
                    if (mode.jumpFarDist > 0.0f)
                        pJump = std::fabs(AI1Pos->position.x - BigRect::NET_X) >= mode.jumpFarDist
                                    ? mode.jumpFarProb
                                    : mode.jumpNearProb;
                    _jumpPlanned = coin(Rng()) < pJump;
                }

                // (2) Nhảy đúng frame mà mô phỏng cho thấy sẽ gặp bóng ở gần đỉnh cú nhảy.
                //     Không bao giờ thoả (bóng rơi quá nhanh, đứng sai chỗ) thì không nhảy, và
                //     luật/model sẽ đập từ mặt đất như bình thường.
                if (_jumpPlanned && afterOwnSet && onGround && !IsContactIntent(intent.finalIntent))
                {
                    const int t = FramesUntilAirContact();
                    if (t >= JUMP_CONTACT_MIN_T && t <= JUMP_CONTACT_MAX_T)
                    {
                        intent.finalIntent = FinalIntent::Jump;
                        _jumpPlanned       = false;
                        _airSpikePending   = true;
                        _framesSinceJump   = 0;
                    }
                }
                // (3) Đang trên không theo kế hoạch và bóng đã vào vùng đập: phải đập.
                //     Cú NÀO thì vẫn do model chọn - rút thăm trong 3 cú đập theo xác suất
                //     của model (cùng temperature), chỉ trong các cú rơi vào sân đối phương.
                else if (_airSpikePending && !onGround && !IsSpikeIntent(intent.finalIntent) &&
                         BallInDetectZone(FinalIntent::SpikeMedium))
                {
                    float weight[ml::kNumClasses];
                    float total = 0.0f;
                    for (int c = 0; c < ml::kNumClasses; ++c)
                    {
                        const FinalIntent cand = kIntentOf[c];
                        const bool usable      = IsSpikeIntent(cand) && SpikeLandsInCourt(cand, ballPos);
                        weight[c] = usable ? std::pow(proba[c], 1.0f / PROBA_SAMPLING_TEMPERATURE) : 0.0f;
                        total += weight[c];
                    }
                    FinalIntent spike = FinalIntent::None;
                    if (total > 0.0f)
                    {
                        std::uniform_real_distribution<float> pick(0.0f, total);
                        float r = pick(Rng());
                        for (int c = 0; c < ml::kNumClasses && spike == FinalIntent::None; ++c)
                        {
                            if (weight[c] <= 0.0f) continue;
                            r -= weight[c];
                            if (r <= 0.0f) spike = kIntentOf[c];
                        }
                    }
                    // Model cho cả 3 cú xác suất 0, hoặc không cú nào rơi trong sân: dùng
                    // đúng cách chọn của bot luật để không bỏ bóng.
                    intent.finalIntent = spike != FinalIntent::None ? spike : PickAttackIntent(ballPos);
                }
                if (!onGround && IsSpikeIntent(intent.finalIntent))
                {
                    _airSpikePending = false;
                }
            }
        }
    }

    // Ghi nhớ cú chạm của chính mình, cho luật 4 ở frame sau.
    if (IsContactIntent(intent.finalIntent))
    {
        _lastOwnContact = intent.finalIntent;
    }

    // Vẫn không đưa intent rỗng vào pool: nếu cả luật và model đều không muốn làm gì thì
    // frame này bot không có ý định nào, y như hành vi cũ.
    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
    {
        intentPool.add(_self, intent);
    }
}
