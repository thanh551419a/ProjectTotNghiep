#include "UpdateTrajectoryFromIntent.h"
#include "TrajectoryUpdate.h"
#include "RallyStateUtils.h"
#include <algorithm>
#include <cmath>

using namespace TrajectoryConfig;

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
    ActionState characterActionEvent;
    TrajectoryBoost boost = TrajectoryBoost{1.0f, 1.0f};
    int powerSpike        = 0;
    DetectionResult detectTemp;

    for (size_t i = 0; i < entities.size(); i++)
    {
        lastEvent            = ActionState::None;  // reset mỗi vòng (sự kiện chạm bóng thực sự)
        characterActionEvent = ActionState::None;  // trạng thái nhân vật + cooldown (cho phép nhóm Spike đánh trượt / Fake Spike)
        int index    = entities[i];
        int direction = (index < 3) ? 1 : -1;
        boost         = TrajectoryBoost{1.0f, 1.0f};
        powerSpike    = 0;
        detectTemp    = DetectPlayerBall(data, index, 7, SimMath::Size{0, 0});

        if (index == 7)
            continue;

        // Mặc định 100% v0 cho các pha phát/đỡ/chuyền (Serve, Bump, Set);
        // các pha tấn công (Spike, SpikeLight, SpikeMedium, SpikeStrong) lấy chỉ số AttackPower trong RAM của nhân vật.
        const float characterAtk   = _componentStorage->GetCharacterAttackStat(index);
        testTrajectory.AttackPower = 100.0f;

        // ===================== SPIKE =====================
        if (intent[i].finalIntent == Spike && stateFrame == Alive)
        {
            testTrajectory.AttackPower = characterAtk;
            characterActionEvent       = ActionState::Spike;

            auto detection = DetectPlayerBall(data, index, 7, SimMath::Size{0, 0});

            TrajectoryData newTrajectory;
            if (index < ChunkConfig::CHARACTER_PER_MATCH)
            {
                newTrajectory = TrajectoryConfig::Get(detection.distancePercent, detection.angle);
            }


            if (newTrajectory.a != 0.0f || newTrajectory.v0 != 0.0f)
            {
                if (testTrajectory.AttackPower > 170)
                {
                    powerSpike = 1;
                    boost      = TrajectoryBoost{SystemConfig::FIXED_V0_MULTIPLIER, 5.0f};
                }
                else
                {
                    powerSpike = -1;
                    boost      = TrajectoryBoost{SystemConfig::FIXED_V0_MULTIPLIER, 1.0f};
                }
                constexpr float kMaxSpikeDistance = TrajectoryConfig::MIN_DISTANCE +
                                                    TrajectoryConfig::DISTANCE_STEP * TrajectoryConfig::DISTANCE_COUNT;
                TryApplyTrajectory(ActionState::Spike, detection.distancePercent, kMaxSpikeDistance,
                                   /*inclusive=*/true, newTrajectory, boost, DECREASE_C_FOR_SPIKE, powerSpike,
                                   direction, _componentStorage, lastEvent);
            }
        }

        // ===================== BUMP =====================
        if (intent[i].finalIntent == Bump && stateFrame == Alive)
        {
            auto ballState  = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
            auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
            if (rallyState != nullptr && rallyState->touchCount == 0)
            {
                continue;
            }

            auto detection = DetectPlayerBall(
                data, index, 7, SimMath::Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));

            const float baseSpeed   = DefaultCharacterStats::kReferenceBaseSpeed[index];
            const float statSpeed   = _componentStorage->GetCharacterSpeedStat(index);
            const float moveSpeed   = SPEED * (baseSpeed > 0.0f ? (statSpeed / baseSpeed) : 1.0f);
            MoveCharacterTowardLanding(_intentStorage, _componentStorage, index, ballState->landingX, moveSpeed);

            const float baseDefense = DefaultCharacterStats::kTable[index].DefensePower;
            const float statDefense = _componentStorage->GetCharacterDefenseStat(index);
            const float bumpRadius  = SystemConfig::DISTANCE_DETECTION_BUMP * (baseDefense > 0.0f ? (statDefense / baseDefense) : 1.0f);

            TrajectoryData newTrajectory = TrajectoryConfig::GetBump();
            TryApplyTrajectory(ActionState::Bump, detection.distancePercent, bumpRadius,
                               /*inclusive=*/true, newTrajectory, boost, DECREASE_C_FOR_BUMP, powerSpike, direction,
                               _componentStorage, lastEvent);
        }

        // ===================== PREPARE / LAUNCH ATTACK SERVE (2-PHASE HOLD BUMP SERVE) =====================
        if (intent[i].finalIntent == FinalIntent::PrepareAttackServe && stateFrame == Reset)
        {
            auto entityTeam = GetEntityTeam1(index);
            if (entityTeam == matchState->servingTeam)
            {
                _componentStorage->SetPreparingAttackServe(true);
                auto* ballTrajectory = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
                if (ballTrajectory != nullptr && ball != nullptr)
                {
                    const auto serveCfg    = SystemConfig::GetScaledAttackServeConfig();
                    ballTrajectory->type   = TrajectoryType::Parabolic;
                    const float baseA      = serveCfg.baseA;
                    const float maxA       = serveCfg.maxA;
                    const float baseC      = serveCfg.baseC;
                    const float maxC       = serveCfg.maxC;
                    const float totalSteps = std::max(1.0f, std::fabs(serveCfg.totalStep));
                    const float stepA      = std::fabs(maxA - baseA) / totalSteps;
                    const float stepC      = std::fabs(maxC - baseC) / totalSteps;
                    if (ballTrajectory->a == 0.0f || ballTrajectory->c <= SystemConfig::MIN_Y)
                    {
                        ballTrajectory->a = baseA;
                        ballTrajectory->c = baseC;
                    }
                    if (maxA >= baseA)
                    {
                        ballTrajectory->a = std::clamp(ballTrajectory->a + stepA, baseA, maxA);
                    }
                    else
                    {
                        ballTrajectory->a = std::clamp(ballTrajectory->a - stepA, maxA, baseA);
                    }
                    if (maxC >= baseC)
                    {
                        ballTrajectory->c = std::clamp(std::max(ballTrajectory->c, baseC) + stepC, baseC, maxC);
                    }
                    else
                    {
                        ballTrajectory->c = std::clamp(std::min(ballTrajectory->c, baseC) - stepC, maxC, baseC);
                    }
                    if (ballTrajectory->c < ball->position.y)
                    {
                        ballTrajectory->c = ball->position.y;
                    }
                    ballTrajectory->b = CalculateB(ballTrajectory->a, ballTrajectory->c, ball->position.x,
                                                   ball->position.y, direction, /*isSpike=*/-1);
                    if (ballTrajectory->a < 0.0f && ballTrajectory->c >= SystemConfig::MIN_Y)
                    {
                        const float root = std::sqrt(std::max(0.0f, (SystemConfig::MIN_Y - ballTrajectory->c) / ballTrajectory->a));
                        ballGamePlay->landingX = -ballTrajectory->b + static_cast<float>(direction) * root;
                    }
                    ballTrajectory->speed    = 0.0f;
                    ballGamePlay->stateFrame = BallStateFrame::Reset;
                }
            }
        }
        if (intent[i].finalIntent == FinalIntent::LaunchAttackServe && stateFrame == Reset)
        {
            auto entityTeam = GetEntityTeam1(index);
            if (entityTeam == matchState->servingTeam)
            {
                _componentStorage->SetPreparingAttackServe(false);
                auto* ballTrajectory = _componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
                if (ballTrajectory != nullptr && ball != nullptr)
                {
                    const auto serveCfg  = SystemConfig::GetScaledAttackServeConfig();
                    ballTrajectory->type = TrajectoryType::Parabolic;
                    if (ballTrajectory->a == 0.0f || ballTrajectory->c <= SystemConfig::MIN_Y)
                    {
                        ballTrajectory->a = serveCfg.baseA;
                        ballTrajectory->c = serveCfg.baseC;
                    }
                    if (ballTrajectory->c < ball->position.y)
                    {
                        ballTrajectory->c = ball->position.y;
                    }
                    ballTrajectory->b        = CalculateB(ballTrajectory->a, ballTrajectory->c, ball->position.x,
                                                          ball->position.y, direction, /*isSpike=*/-1);
                    ballTrajectory->speed    = static_cast<float>(direction) *
                                               (serveCfg.tossV0 / 100.0f) * 4.0f;
                    ballGamePlay->stateFrame = BallStateFrame::Alive;
                    _componentStorage->SetLastBallAction(ActionState::Bump, BallStateFrame::Alive);
                    UpdateLandingX(_componentStorage);
                    // CRITICAL: Keep rallyState->touchCount == 0 (do NOT set lastEvent or call UpdateRallyTouch)
                    // so the server's mid-air jump spike counts as touch 1!
                }
            }
        }

        // ===================== SET =====================
        if (intent[i].finalIntent == Set && stateFrame == Alive)
        {
            auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
            if (rallyState != nullptr && rallyState->touchCount == 0)
            {
                continue;
            }
            auto detection = DetectPlayerBall(
                data, index, 7, SimMath::Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));

            TrajectoryData newTrajectory = TrajectoryConfig::GetSet();
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
            TrajectoryData newTrajectory = TrajectoryConfig::GetServe();
            if (ballGamePlay->stateFrame == Reset)
                ballGamePlay->stateFrame = Alive;
            _componentStorage->SetLastBallAction(ActionState::Serve, BallStateFrame::Alive);
            boost = TrajectoryConfig::GetServeBoost();
            UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_SPIKE, powerSpike, boost);
        }

        // ===================== SPIKE LIGHT / MEDIUM / STRONG =====================
        // Các event SpikeLight / SpikeMedium / SpikeStrong được phép play (cập nhật vào ActionState + Cooldown)
        // ngay khi có lệnh truyền xuống (kể cả khi đánh trượt hoặc bóng chưa Alive), và nếu bóng đang Alive + trong tầm thì cập nhật quỹ đạo bóng:
        constexpr float kMaxOptionSpikeDistance = TrajectoryConfig::MIN_DISTANCE +
                                                  TrajectoryConfig::DISTANCE_STEP * TrajectoryConfig::DISTANCE_COUNT;
        if (intent[i].finalIntent == SpikeLight)
        {
            characterActionEvent = ActionState::SpikeLight;
            if (stateFrame == Alive)
            {
                testTrajectory.AttackPower = characterAtk;
                TryApplyTrajectory(ActionState::SpikeLight, detectTemp.distancePercent, kMaxOptionSpikeDistance, /*inclusive=*/true,
                                   TrajectoryConfig::GetSpikeLight(), TrajectoryConfig::GetSpikeLightBoost(), DECREASE_C_FOR_SPIKETEMP,
                                   powerSpike, direction, _componentStorage, lastEvent);
            }
        }
        if (intent[i].finalIntent == SpikeMedium)
        {
            characterActionEvent = ActionState::SpikeMedium;
            if (stateFrame == Alive)
            {
                testTrajectory.AttackPower = characterAtk;
                TryApplyTrajectory(ActionState::SpikeMedium, detectTemp.distancePercent, kMaxOptionSpikeDistance, /*inclusive=*/true,
                                   TrajectoryConfig::GetSpikeMedium(), TrajectoryConfig::GetSpikeMediumBoost(), DECREASE_C_FOR_SPIKETEMP,
                                   powerSpike, direction, _componentStorage, lastEvent);
            }
        }
        if (intent[i].finalIntent == SpikeStrong)
        {
            characterActionEvent = ActionState::SpikeStrong;
            if (stateFrame == Alive)
            {
                testTrajectory.AttackPower = characterAtk;
                TryApplyTrajectory(ActionState::SpikeStrong, detectTemp.distancePercent, kMaxOptionSpikeDistance, /*inclusive=*/true,
                                   TrajectoryConfig::GetSpikeStrong(), TrajectoryConfig::GetSpikeStrongBoost(), DECREASE_C_FOR_SPIKE,
                                   powerSpike, direction, _componentStorage, lastEvent);
            }
        }

        // ===================== XỬ LÝ KHI CÓ SỰ KIỆN =====================
        const auto CancelJumpAscentOnStrike = [&](ActionState ev) {
            if (ev == ActionState::Spike || ev == ActionState::SpikeLight ||
                ev == ActionState::SpikeMedium || ev == ActionState::SpikeStrong ||
                ev == ActionState::Serve)
            {
                if (auto* jumpUp = _componentStorage->GetJumpUpFramePool().get(index))
                {
                    jumpUp->jumpUpTime      = 0.0f;
                    jumpUp->remainingFrames = 0;
                    jumpUp->apexHangTime    = 0.0f;
                }
            }
        };

        if (lastEvent != ActionState::None)
        {
            CancelJumpAscentOnStrike(lastEvent);
            LogTrajectoryEvent(logFile, testTrajectory.frame, index, lastEvent, _componentStorage);

            if (ballGamePlay->stateFrame != -1 || (lastEvent != ActionState::Serve && ballGamePlay->stateFrame == 0))
                return;

            auto maxCharacterPerSideCourt = ChunkConfig::CHARACTER_PER_MATCH / 2;
            UpdateRallyTouch(_componentStorage, index, maxCharacterPerSideCourt);

            ApplyCharacterActionState(_componentStorage, index, lastEvent);
        }
        else if (characterActionEvent != ActionState::None)
        {
            // Nhóm Spike (Spike / SpikeLight / SpikeMedium / SpikeStrong) khi bóng đang Alive
            // nhưng ngoài vùng chạm bóng (Fake Spike / đánh trượt): vẫn cập nhật ActionState + Cooldown,
            // cắt ngay pha bay lên (jumpUpTime = 0) để nhân vật rơi xuống đất,
            // không cập nhật quỹ đạo bóng và không tính chạm bóng (UpdateRallyTouch).
            CancelJumpAscentOnStrike(characterActionEvent);
            ApplyCharacterActionState(_componentStorage, index, characterActionEvent);
        }
    }
}
