#include "UpdateTrajectoryFromIntent.h"
#include "TrajectoryUpdate.h"
#include "RallyStateUtils.h"
#include "../../Config/Match/MatchRuleConfig.h"
#include "../../Config/Match/RunMode.h"
#include <random>

USING_NS_AX;
using namespace TrajectoryConfig;

namespace
{
// Ngẫu nhiên hoá quả giao bóng, CHỈ ở chế độ bot-vs-bot (tự sinh dữ liệu huấn luyện).
//
// Vì sao cần: hai bot tất định + vật lý tất định = rally lặp tuần hoàn. Đo thực tế trên
// 90 giây bot-vs-bot: 78 cú đập nhưng chỉ 4 tổ hợp vị trí khác nhau, khoảng cách giữa các
// cú đập đúng 67 frame không sai một frame, và chỉ ra duy nhất SpikeMedium.
//
// Nguyên tắc: đa dạng phải đến từ MÔI TRƯỜNG (điều kiện đầu mỗi pha bóng), không phải từ
// quyết định của bot. Nếu random chính lựa chọn cú đập thì nhãn mất khả năng học - đó là
// lý do PickAttackIntent đã bỏ std::rand() (xem ADR-006 ở repo AI).
//
// v0 đổi cả độ cao đỉnh (qua CalculateC) lẫn tốc độ ngang, nên là biến hiệu quả nhất để
// đổi điểm rơi. Khoảng giữ quanh giá trị gốc 100 (hợp lệ: MIN_V0 4 .. MAX_V0 160) để quả
// giao phần lớn vẫn vào sân, nếu không thì mất điểm ngay và pha bóng không có cú đập nào.
TrajectoryData RandomizeServe(const TrajectoryData& base, TrajectoryBoost& boost)
{
    static std::mt19937 rng{std::random_device{}()};
    std::uniform_real_distribution<float> v0Jitter(80.0f, 130.0f);
    std::uniform_real_distribution<float> flattenJitter(4.0f, 6.5f);

    boost.aFlattenFactor = flattenJitter(rng);
    return TrajectoryData{base.a, v0Jitter(rng)};
}
}  // namespace

UpdateTrajectoryFromIntent::UpdateTrajectoryFromIntent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage    = intentStorage;
    _componentStorage = componentStorage;
}

void UpdateTrajectoryFromIntent::update(ObjectData* data, float delta, std::ofstream* logFile)
{
    testTrajectory.frame++;

    auto& characterIntentPool = _intentStorage->GetCharacterIntentPool();
    auto intent               = characterIntentPool.components();
    auto entities             = characterIntentPool.entities();

    auto ball         = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    auto ballGamePlay = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto matchState   = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    auto stateFrame   = ballGamePlay->stateFrame;

    ActionState lastEvent;
    TrajectoryBoost boost = TrajectoryBoost{1.0f, 1.0f};
    int powerSpike        = 0;
    DetectionResult detectTemp;

    for (size_t i = 0; i < entities.size(); i++)
    {
        lastEvent     = ActionState::None;  // reset mỗi vòng
        int index    = entities[i];
        int direction = (index < 3) ? 1 : -1;
        boost         = TrajectoryBoost{1.0f, 1.0f};
        powerSpike    = 0;
        detectTemp    = DetectPlayerBall(data, index, 7, Size{0, 0});

        if (index == 7)
            continue;

        // ===================== SPIKE =====================
        if (intent[i].finalIntent == Spike)
        {
            auto detection = DetectPlayerBall(data, index, 7, Size{0, 0});

            testTrajectory.AttackPower = (index == 0) ? 180 : 150;

            TrajectoryData newTrajectory;
            if (index < ChunkConfig::CHARACTER_PER_MATCH)
            {
                newTrajectory = TrajectoryConfig::Get(detection.distancePercent, detection.angle);
            }

            AXLOG("Trajectory nhan duoc la : a = %f , v0 = %f", newTrajectory.a, newTrajectory.v0);

            if (newTrajectory.a != 0.0f || newTrajectory.v0 != 0.0f)
            {
                lastEvent = ActionState::Spike;
                if (testTrajectory.AttackPower > 170)
                {
                    powerSpike = 1;
                    boost      = TrajectoryBoost{6.0f, 5.0f};
                }
                else
                {
                    powerSpike = -1;
                }
                UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_SPIKE, powerSpike,
                                    boost);
            }
        }

        // ===================== BUMP =====================
        if (intent[i].finalIntent == Bump)
        {
            auto detection = DetectPlayerBall(
                data, index, 7, Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));

            auto ballState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

            MoveCharacterTowardLanding(_intentStorage, _componentStorage, index, ballState->landingX, SPEED);

            TrajectoryData newTrajectory = TrajectoryData(-0.006f, 120.0f);
            TryApplyTrajectory(ActionState::Bump, detection.distancePercent, SystemConfig::DISTANCE_DETECTION_BUMP,
                               /*inclusive=*/true, newTrajectory, boost, DECREASE_C_FOR_BUMP, powerSpike, direction,
                               _componentStorage, lastEvent);
        }

        // ===================== SET =====================
        if (intent[i].finalIntent == Set)
        {
            auto detection = DetectPlayerBall(
                data, index, 7, Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));

            TrajectoryData newTrajectory = TrajectoryData(-8.0f, 3.0f);
            TryApplyTrajectory(ActionState::Set, detection.distancePercent, SystemConfig::DISTANCE_DETECTION_SET,
                               /*inclusive=*/true, newTrajectory, boost, DECREASE_C_FOR_BUMP, powerSpike, direction,
                               _componentStorage, lastEvent);
        }

        // ===================== SERVE =====================
        if (intent[i].finalIntent == Serve)
        {
            if (stateFrame != Reset)
                continue;

            auto entityTeam = GetEntityTeam1(index);
            if (entityTeam != matchState->servingTeam)
                continue;

            lastEvent                    = ActionState::Serve;
            TrajectoryData newTrajectory = TrajectoryData(-0.0045f, 100.0f);
            if (ballGamePlay->stateFrame == Reset)
                ballGamePlay->stateFrame = Alive;
            boost = TrajectoryBoost{5.0f, 5.0f};
            if (RunMode::Get().botVsBot)
                newTrajectory = RandomizeServe(newTrajectory, boost);
            UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_SPIKE, powerSpike, boost);
        }

        // ===================== SPIKE LIGHT / MEDIUM / STRONG =====================
        if (intent[i].finalIntent == SpikeLight)
        {
            TryApplyTrajectory(ActionState::SpikeLight, detectTemp.distancePercent, 50.0f, /*inclusive=*/false,
                               TrajectoryData(-0.045f, 80.0f), TrajectoryBoost(1.6f, 3.0f), DECREASE_C_FOR_SPIKETEMP,
                               powerSpike, direction, _componentStorage, lastEvent);
        }
        if (intent[i].finalIntent == SpikeMedium)
        {
            TryApplyTrajectory(ActionState::SpikeMedium, detectTemp.distancePercent, 50.0f, /*inclusive=*/false,
                               TrajectoryData(-0.008f, 80.0f), TrajectoryBoost(4.0f, 3.8f), DECREASE_C_FOR_SPIKETEMP,
                               powerSpike, direction, _componentStorage, lastEvent);
        }
        if (intent[i].finalIntent == SpikeStrong)
        {
            TryApplyTrajectory(ActionState::SpikeStrong, detectTemp.distancePercent, 50.0f, /*inclusive=*/false,
                               TrajectoryData(-0.0045f, 100.0f), TrajectoryBoost(7.0f, 3.8f), DECREASE_C_FOR_SPIKE,
                               powerSpike, direction, _componentStorage, lastEvent);
        }

        // ===================== XỬ LÝ KHI CÓ SỰ KIỆN =====================
        if (lastEvent != ActionState::None)
        {
            LogTrajectoryEvent(logFile, testTrajectory.frame, index, lastEvent, _componentStorage);

            if (ballGamePlay->stateFrame != -1 || (lastEvent != ActionState::Serve && ballGamePlay->stateFrame == 0))
                return;

            auto maxCharacterPerSideCourt = ChunkConfig::CHARACTER_PER_MATCH / 2;
            UpdateRallyTouch(_componentStorage, index, maxCharacterPerSideCourt, lastEvent);

            ApplyCharacterActionState(_componentStorage, index, lastEvent);
        }
    }
}
