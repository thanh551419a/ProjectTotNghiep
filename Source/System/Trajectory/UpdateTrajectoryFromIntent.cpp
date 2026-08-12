#include "UpdateTrajectoryFromIntent.h"
#include "TrajectoryUpdate.h"
#include "RallyStateUtils.h"

USING_NS_AX;
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
            UpdateRallyTouch(_componentStorage, index, maxCharacterPerSideCourt);

            ApplyCharacterActionState(_componentStorage, index, lastEvent);
        }
    }
}
