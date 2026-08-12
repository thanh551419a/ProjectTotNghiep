#include "AIInputSystem.h"
#include <cstdlib>
#include <vector>

namespace
{
// Ngưỡng chiều cao bóng để AI thực hiện cú đánh trả (AI ra tay khi bóng đã
// rơi đủ thấp, trong tầm với, không cần nhảy) - giữ nguyên hành vi cũ.
constexpr float AI_ATTACK_HEIGHT_THRESHOLD = 300.0f;

// Vị trí "thủ thế" giữa sân AI - nơi AI lùi về khi bóng chưa thực sự
// hướng sang sân mình (case 1 & case 3 của phase Phòng Thủ).
constexpr float AI_READY_X = (BigRect::NET_X + BigRect::RIGHT_9M_LINE_X) * 0.5f;

// Hướng đánh của OPPONENT_1: luôn đánh từ phải sang trái, y hệt
// "direction = (index < 3) ? 1 : -1" trong UpdateTrajectoryFromIntent.cpp.
constexpr int AI_ATTACK_DIRECTION = -1;
}  // namespace

AIInputSystem::AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage    = intentStorage;
    _componentStorage = componentStorage;
}

bool AIInputSystem::HasUsedItsTouch(const RallyState* rallyState) const
{
    return rallyState->lastTouch == GameConfig::OPPONENT_1 && rallyState->touchCount > 0;
}

bool AIInputSystem::CrossesNetCleanly(BallTrajectoryComponent* ballTrajectory, PositionComponent* ballPos) const
{
    // Nếu bóng đã đứng bên sân AI rồi thì coi như đã băng qua lưới thành công.
    if (ballPos->position.x >= BigRect::NET_X)
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
    bool landingOnBotSide  = ballGameplayState->landingX >= BigRect::NET_X;
    bool crossesNetCleanly = CrossesNetCleanly(ballTrajectory, ballPos);

    if (!landingOnBotSide || !crossesNetCleanly)
    {
        // Case 1: landingX bên sân Player, HOẶC quỹ đạo bị lưới chặn -> chưa phải bóng của AI
        return BallTrend::TowardPlayerOrBlocked;
    }

    if (ballGameplayState->landingX > BigRect::RIGHT_9M_LINE_X)
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
    float b = CalculateB(a, c, ballPos->position.x, ballPos->position.y, AI_ATTACK_DIRECTION, /*isSpike=*/0);

    BallTrajectoryComponent simulated{};
    simulated.type  = TrajectoryType::Parabolic;
    simulated.a     = a;
    simulated.b     = b;
    simulated.c     = c;
    simulated.speed = AI_ATTACK_DIRECTION * (option.data.v0 / 100.0f) * 4.0f * option.boost.v0Multiplier;

    Vec2 landing = CalculateLandingPoint(ballPos->position, &simulated, SystemConfig::MIN_Y);
    return landing.x;
}

FinalIntent AIInputSystem::PickAttackIntent(PositionComponent* ballPos) const
{
    // 3 phương án y hệt thông số trong UpdateTrajectoryFromIntent.cpp
    const AttackOption options[3] = {
        {FinalIntent::SpikeLight, TrajectoryData{-0.045f, 80.0f}, TrajectoryBoost{1.6f, 3.0f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKETEMP},
        {FinalIntent::SpikeMedium, TrajectoryData{-0.008f, 80.0f}, TrajectoryBoost{4.0f, 3.8f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKETEMP},
        {FinalIntent::SpikeStrong, TrajectoryData{-0.0045f, 100.0f}, TrajectoryBoost{7.0f, 3.8f},
         TrajectoryConfig::DECREASE_C_FOR_SPIKE},
    };

    // Mỗi frame build lại danh sách các phương án mà nếu đánh sẽ rơi TRONG sân đối phương
    std::vector<FinalIntent> validOptions;
    validOptions.reserve(3);

    for (const auto& option : options)
    {
        float landingX = PredictAttackLandingX(option, ballPos);

        bool landsInsideOpponentCourt = landingX >= BigRect::LEFT_9M_LINE_X && landingX < BigRect::NET_X;

        if (landsInsideOpponentCourt)
        {
            validOptions.push_back(option.intent);
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
    bool canGenerateIntent    = characterStatusPool.get(GameConfig::OPPONENT_1)->status == ActionState::None;
    //auto AI1Pos               = _componentStorage->GetCharacterPositionPool().get(GameConfig::OPPONENT_1);
    if (!canGenerateIntent)  // hành động trước còn đang trong cooldown
    {
        return;
    }

    auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto ballGameplayState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto ballTrajectory    = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto ballPos           = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto& characterPos     = _componentStorage->GetCharacterPositionPool();
    auto AI1Pos            = characterPos.get(GameConfig::OPPONENT_1);
    auto& intentPool       = _intentStorage->GetCharacterIntentPool();
    auto matchState        = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    // ====== LUẬT: AI CHỈ 1 CHẠM MỖI LƯỢT NHẬN BÓNG ======
    if (HasUsedItsTouch(rallyState))
    {
        CharacterIntent intent{};

        float dx = AI_READY_X - AI1Pos->position.x;

        if (fabs(dx) > SystemConfig::SPEED)
        {
            intent.moveX = (dx > 0.0f) ? SystemConfig::SPEED : -SystemConfig::SPEED;
        }

        intentPool.add(GameConfig::OPPONENT_1, intent);
        return;
    }

   

    CharacterIntent intent{};
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;

    if (ballGameplayState->stateFrame == Alive)
    {
        BallTrend trend = ClassifyBallTrend(ballGameplayState, ballTrajectory, ballPos);

        // ================= PHASE: PHÒNG THỦ =================
        // Case 2 (TowardBotCourt) -> chạy tới điểm rơi để đón bóng.
        // Case 1 & 3              -> lùi về vị trí thủ thế giữa sân.
        float targetX = (trend == BallTrend::TowardBotCourt) ? ballGameplayState->landingX : AI_READY_X;

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
    else if (ballGameplayState->stateFrame == Reset && matchState->servingTeam == Team::RIGHT)
    {
        intent.finalIntent = FinalIntent::Serve;
    }

    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
    {
        intentPool.add(GameConfig::OPPONENT_1, intent);
    }
}
