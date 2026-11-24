#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/BigRect.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "../Constants/BallGameplayConstants.h"
#include "../Config/System/SystemConf.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"
#include "../ECSCore/Components/PositionComponent.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/States/ActionState.h"
#include "../Gameplay/FinalIntent.h"
#include "../Input/InputMapping.h"
#include "../System/JumpStartSystem.h"
#include "../System/PlayerInputSystem.h"
#include "../System/AIInputSystem.h"
#include "../System/FramePreparationSystem.h"
#include "../System/GameRuleEvaluationSystem.h"
#include "../Utils/ApplyIntentToComponent.h"
#include "../System/Trajectory/UpdateTrajectoryFromIntent.h"
#include "../System/Trajectory/TrajectoryUpdate.h"
#include "../Utils/GetData.h"
#include "SpikeNativeApi.h"
#include "SimulationApi.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cmath>
#include <initializer_list>
#include <string>

namespace
{
int failures = 0;

void Check(bool condition, const char* expression, int line)
{
    std::printf("[%s] line %d: %s\n", condition ? "PASS" : "FAIL", line, expression);
    if (!condition) ++failures;
}

#define CHECK(expression) Check(static_cast<bool>(expression), #expression, __LINE__)

constexpr const char* kFormatB = R"json({
  "button_1":"left",
  "button_2":"right",
  "button_3":"bump",
  "button_4":"serve",
  "button_5":"jump_only",
  "button_6":"spikelight",
  "button_7":"spikemedium",
  "button_8":"spikestrong"
})json";

struct Fixture
{
    ComponentStorage components;
    IntentStorage intents;
    PlayerInputSystem input{&intents, &components};
    JumpStartSystem jump{&intents, &components};

    explicit Fixture(float playerY, float cooldown = 0.0f, int remainingFrames = 0)
    {
        components.GetCharacterPositionPool().add(
            GameConfig::PLAYER,
            PositionComponent{SimMath::Vec2(100.0f, playerY)});
        components.GetCharacterActionStatePool().add(
            GameConfig::PLAYER,
            CharacterActionState{ActionState::None, cooldown});
        components.GetJumpUpFramePool().add(
            GameConfig::PLAYER,
            JumpUpFrameComponent{remainingFrames});
    }

    const CharacterIntent* Submit(uint32_t holding, uint32_t pressed)
    {
        intents.clear();
        input.update(SystemConfig::FIXED_DT, SimInputFrame{holding, pressed, 0.0f});
        return intents.GetCharacterIntentPool().get(GameConfig::PLAYER);
    }
};

uint32_t ConfiguredMask(const InputMapping& mapping, int id)
{
    GameAction action = GameAction::None;
    if (!mapping.MapButtonToAction(id, action)) return 0;
    return mapping.ActionToButtonMask(action);
}

void TestDefaultsAndConfiguredMapping(InputMapping& mapping)
{
    GameAction action = GameAction::None;
    for (int id = 0; id <= 8; ++id)
    {
        CHECK(mapping.MapButtonToAction(id, action));
        CHECK(action == static_cast<GameAction>(id + 1));
    }
    CHECK(mapping.MapButtonToAction(9, action));
    CHECK(action == GameAction::Jump);
    CHECK(static_cast<uint32_t>(GameAction::JumpSpike) == 3u);
    CHECK(static_cast<uint32_t>(GameAction::SpikeStrong) == 9u);
    CHECK(static_cast<uint32_t>(GameAction::Jump) == 10u);
    CHECK(static_cast<uint32_t>(SimButton::MoveLeft) == 1u);
    CHECK(static_cast<uint32_t>(SimButton::MoveRight) == 2u);
    CHECK(static_cast<uint32_t>(SimButton::JumpSpike) == 4u);
    CHECK(static_cast<uint32_t>(SimButton::Bump) == 8u);
    CHECK(static_cast<uint32_t>(SimButton::Set) == 16u);
    CHECK(static_cast<uint32_t>(SimButton::Serve) == 32u);
    CHECK(static_cast<uint32_t>(SimButton::SpikeLight) == 64u);
    CHECK(static_cast<uint32_t>(SimButton::SpikeMedium) == 128u);
    CHECK(static_cast<uint32_t>(SimButton::SpikeStrong) == 256u);
    CHECK(static_cast<uint32_t>(SimButton::Jump) == 512u);
    CHECK(SIM_BUTTON_JUMP == (1 << 9));

    CHECK(mapping.LoadFromString(kFormatB));
    constexpr std::array<GameAction, 8> expected = {
        GameAction::MoveLeft, GameAction::MoveRight, GameAction::Bump,
        GameAction::Serve, GameAction::Jump, GameAction::SpikeLight,
        GameAction::SpikeMedium, GameAction::SpikeStrong};
    for (int id = 1; id <= 8; ++id)
    {
        CHECK(mapping.MapButtonToAction(id, action));
        CHECK(action == expected[static_cast<size_t>(id - 1)]);
        CHECK(mapping.ActionToButtonMask(action) != 0);
    }
    CHECK(mapping.MapButtonToAction(9, action));
    CHECK(action == GameAction::JumpSpike);
    CHECK(InputMapping::StringToAction("jump") == GameAction::JumpSpike);
    CHECK(InputMapping::StringToAction("jumpspike") == GameAction::JumpSpike);
    CHECK(InputMapping::StringToAction("jump_only") == GameAction::Jump);
    CHECK(sizeof(Input) == 36u);
    CHECK(offsetof(Input, buttonCount) == 0u);
    CHECK(offsetof(Input, buttonIds) == 4u);
}

void TestIndependentJump(const InputMapping& mapping)
{
    const uint32_t jumpMask = ConfiguredMask(mapping, 5);
    CHECK(jumpMask == static_cast<uint32_t>(SimButton::Jump));

    Fixture grounded(SystemConfig::MIN_Y);
    const auto* groundedIntent = grounded.Submit(0, jumpMask);
    CHECK(groundedIntent != nullptr);
    if (groundedIntent) CHECK(groundedIntent->finalIntent == FinalIntent::Jump);
    grounded.jump.update(SystemConfig::FIXED_DT);
    const auto* groundedFrames = grounded.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(groundedFrames != nullptr);
    if (groundedFrames)
    {
        const float playerJumpStat = grounded.components.GetCharacterJumpingStat(GameConfig::PLAYER);
        CHECK(groundedFrames->remainingFrames == SystemConfig::ComputeJumpUpFrames(playerJumpStat));
        CHECK(std::fabs(groundedFrames->jumpUpTime - SystemConfig::ComputeJumpUpDurationSeconds(playerJumpStat)) < 1e-5f);
    }

    const float airborneY = SystemConfig::MIN_Y + 50.0f;
    Fixture airborne(airborneY, 0.0f, 12);
    const auto* airborneIntent = airborne.Submit(0, jumpMask);
    CHECK(airborneIntent != nullptr);
    if (airborneIntent) CHECK(airborneIntent->finalIntent == FinalIntent::Jump);
    airborne.jump.update(SystemConfig::FIXED_DT);
    const auto* airborneFrames = airborne.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    const auto* airbornePosition = airborne.components.GetCharacterPositionPool().get(GameConfig::PLAYER);
    CHECK(airborneFrames != nullptr);
    if (airborneFrames) CHECK(airborneFrames->remainingFrames == 12);
    CHECK(airbornePosition != nullptr);
    if (airbornePosition) CHECK(airbornePosition->position.y == airborneY);
}

void TestContextualJumpSpikeAndConflict(const InputMapping& mapping)
{
    const uint32_t jumpSpike = mapping.ActionToButtonMask(GameAction::JumpSpike);
    Fixture grounded(SystemConfig::MIN_Y);
    const auto* groundIntent = grounded.Submit(0, jumpSpike);
    CHECK(groundIntent && groundIntent->finalIntent == FinalIntent::Jump);
    grounded.jump.update(SystemConfig::FIXED_DT);
    const auto* groundFrames = grounded.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    const float groundPlayerJumpStat = grounded.components.GetCharacterJumpingStat(GameConfig::PLAYER);
    CHECK(groundFrames && groundFrames->remainingFrames == SystemConfig::ComputeJumpUpFrames(groundPlayerJumpStat));

    Fixture airborne(SystemConfig::MIN_Y + 40.0f);
    const auto* airIntent = airborne.Submit(0, jumpSpike);
    CHECK(airIntent && airIntent->finalIntent == FinalIntent::Spike);
    airborne.jump.update(SystemConfig::FIXED_DT);
    const auto* airFrames = airborne.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(airFrames && airFrames->remainingFrames == 0);

    // Verify 80% jump height gate (MIN_SPIKE_HEIGHT_RATIO = 0.80f):
    // 1. From jump start (0%) to < 80% height, ALL non-movement actions (Spike, SpikeLight, SpikeMedium, SpikeStrong,
    //    Serve, Bump, Set, Jump) are disabled, while horizontal movement (MoveLeft/MoveRight) still works.
    // 2. While grounded (pos.y == MIN_Y), SpikeLight, SpikeMedium, SpikeStrong are disabled (cannot flat-spike from ground).
    // 3. Once >= 80% height is reached (and during the falling phase jumpUpTime == 0, pos.y > MIN_Y),
    //    Spike, SpikeLight, SpikeMedium, SpikeStrong are unlocked.
    {
        const uint32_t moveLeftMask    = ConfiguredMask(mapping, 1);
        const uint32_t bumpMask        = mapping.ActionToButtonMask(GameAction::Bump);
        const uint32_t setMask         = mapping.ActionToButtonMask(GameAction::Set);
        const uint32_t serveMask       = mapping.ActionToButtonMask(GameAction::Serve);
        const uint32_t spikeLightMask  = mapping.ActionToButtonMask(GameAction::SpikeLight);
        const uint32_t spikeMediumMask = mapping.ActionToButtonMask(GameAction::SpikeMedium);
        const uint32_t spikeStrongMask = mapping.ActionToButtonMask(GameAction::SpikeStrong);

        // Grounded: SpikeLight / SpikeMedium / SpikeStrong ARE accepted when remainTime == 0,
        // and BLOCKED when remainTime > 0!
        Fixture groundedSpikes(SystemConfig::MIN_Y);
        const auto* groundedLight = groundedSpikes.Submit(0, spikeLightMask);
        CHECK(groundedLight && groundedLight->finalIntent == FinalIntent::SpikeLight);
        const auto* groundedMedium = groundedSpikes.Submit(0, spikeMediumMask);
        CHECK(groundedMedium && groundedMedium->finalIntent == FinalIntent::SpikeMedium);
        const auto* groundedStrong = groundedSpikes.Submit(0, spikeStrongMask);
        CHECK(groundedStrong && groundedStrong->finalIntent == FinalIntent::SpikeStrong);

        Fixture groundedSpikesOnCooldown(SystemConfig::MIN_Y, 450.0f);
        CHECK(groundedSpikesOnCooldown.Submit(0, spikeLightMask) == nullptr);
        CHECK(groundedSpikesOnCooldown.Submit(0, spikeMediumMask) == nullptr);
        CHECK(groundedSpikesOnCooldown.Submit(0, spikeStrongMask) == nullptr);

        // Early ascent (< 80% height): all action buttons blocked, movement allowed
        Fixture earlyAscent(SystemConfig::MIN_Y + 40.0f);
        auto* earlyJump = earlyAscent.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
        const float fullDur = SystemConfig::ComputeJumpUpDurationSeconds(
            earlyAscent.components.GetCharacterJumpingStat(GameConfig::PLAYER));
        earlyJump->jumpUpTime = fullDur * 0.50f; // only 50% height reached (< 80%)
        earlyJump->remainingFrames = 8;
        CHECK(earlyAscent.Submit(0, jumpSpike) == nullptr);
        CHECK(earlyAscent.Submit(0, spikeLightMask) == nullptr);
        CHECK(earlyAscent.Submit(0, spikeMediumMask) == nullptr);
        CHECK(earlyAscent.Submit(0, spikeStrongMask) == nullptr);
        CHECK(earlyAscent.Submit(0, serveMask) == nullptr);
        CHECK(earlyAscent.Submit(0, setMask) == nullptr);
        CHECK(earlyAscent.Submit(bumpMask, 0) == nullptr);
        const auto* moveDuringEarlyAscent = earlyAscent.Submit(moveLeftMask, jumpSpike | spikeStrongMask | serveMask);
        CHECK(moveDuringEarlyAscent && moveDuringEarlyAscent->moveX < 0.0f &&
              moveDuringEarlyAscent->finalIntent == FinalIntent::None);

        // Late ascent (>= 80% height) & Falling phase (jumpUpTime == 0, pos.y > MIN_Y): unlocked!
        earlyJump->jumpUpTime = fullDur * 0.18f; // 82% height reached (>= 80%)
        earlyJump->remainingFrames = 3;
        const auto* lateAscentSpike = earlyAscent.Submit(0, jumpSpike);
        CHECK(lateAscentSpike && lateAscentSpike->finalIntent == FinalIntent::Spike);
        const auto* lateAscentLight = earlyAscent.Submit(0, spikeLightMask);
        CHECK(lateAscentLight && lateAscentLight->finalIntent == FinalIntent::SpikeLight);
        const auto* lateAscentMedium = earlyAscent.Submit(0, spikeMediumMask);
        CHECK(lateAscentMedium && lateAscentMedium->finalIntent == FinalIntent::SpikeMedium);
        const auto* lateAscentStrong = earlyAscent.Submit(0, spikeStrongMask);
        CHECK(lateAscentStrong && lateAscentStrong->finalIntent == FinalIntent::SpikeStrong);

        // Late ascent (>= 80% height) WITH active cooldown (remainTime > 0, even if status == Spike): MUST be blocked!
        auto* earlyActState = earlyAscent.components.GetCharacterActionStatePool().get(GameConfig::PLAYER);
        earlyActState->status = ActionState::Spike;
        earlyActState->remainTime = 200.0f;
        CHECK(earlyAscent.Submit(0, spikeLightMask) == nullptr);
        CHECK(earlyAscent.Submit(0, spikeMediumMask) == nullptr);
        CHECK(earlyAscent.Submit(0, spikeStrongMask) == nullptr);
        earlyActState->status = ActionState::None;
        earlyActState->remainTime = 0.0f;

        // Falling low near ground (pos.y = MIN_Y + 25.0f, jumpUpTime = 0.0f): low/flat spike on descent is allowed!
        earlyJump->jumpUpTime = 0.0f;
        earlyJump->remainingFrames = 0;
        const auto* fallingLowStrong = earlyAscent.Submit(0, spikeStrongMask);
        CHECK(fallingLowStrong && fallingLowStrong->finalIntent == FinalIntent::SpikeStrong);

        // Verify jump = 200 max height: bottom-left displacement above Ground (MIN_Y) == 4.0m * 80 - 0.5 * 160 == 240px (3.0m)
        const float dispAt200 = SystemConfig::ComputeMaxJumpDisplacementAboveMinYPx(200.0f, 160.0f);
        const float dispAt100 = SystemConfig::ComputeMaxJumpDisplacementAboveMinYPx(100.0f, 160.0f);
        CHECK(std::fabs(dispAt200 - 240.0f) < 1e-4f);
        CHECK(std::fabs((SystemConfig::MIN_Y + dispAt200 + 80.0f) - (SystemConfig::MIN_Y + 4.0f * SystemConfig::PIXELS_PER_METER)) < 1e-4f);
        CHECK(std::fabs(dispAt100 - 120.0f) < 1e-4f);
        CHECK(std::fabs(SystemConfig::ComputeJumpUpDurationSeconds(170.0f) - 0.45f) < 1e-5f);
        CHECK(std::fabs(ActionStateInfo::Cooldown[static_cast<size_t>(ActionState::Spike)] - 450.0f) < 1e-4f);
        CHECK(std::fabs(ActionStateInfo::Cooldown[static_cast<size_t>(ActionState::Jump)] - 360.0f) < 1e-4f);
        CHECK(std::fabs(ActionStateInfo::Cooldown[static_cast<size_t>(ActionState::Serve)] - 0.0f) < 1e-4f);
    }

    Fixture conflict(SystemConfig::MIN_Y + 40.0f);
    const auto* bothIntent = conflict.Submit(0,
        ConfiguredMask(mapping, 5) | jumpSpike);
    CHECK(bothIntent && bothIntent->finalIntent == FinalIntent::Jump);
    conflict.jump.update(SystemConfig::FIXED_DT);
    const auto* conflictFrames = conflict.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(conflictFrames && conflictFrames->remainingFrames == 0);

    // Case 1 diagnostic check: FinalIntent::Spike in mid-air (atk=180 > 170 -> powerSpike=1 vs atk=150 <= 170 -> powerSpike=-1)
    {
        ComponentStorage storage;
        storage.InitComponent();
        IntentStorage intents;
        UpdateTrajectoryFromIntent trajSys(&intents, &storage);

        auto* ballState = storage.GetBallGameplayStatePool().get(DEFAULT_MATCH);
        ballState->stateFrame = BallStateFrame::Alive;

        // 1a. PLAYER (index 0, atk=180 > 170 -> powerSpike=1 -> 1_BRANCH_DOWNWARD_ONLY), close range (<20%) & normal range
        auto* pPos = storage.GetCharacterPositionPool().get(GameConfig::PLAYER);
        auto* bPos = storage.GetBallPositionPool().get(GameConfig::BALL);
        pPos->position = SimMath::Vec2(900.0f, 500.0f);
        bPos->position = SimMath::Vec2(915.0f, 575.0f); // distancePercent ~3.3% (<20%), close-range mid-air hit
        intents.clear();
        intents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
        ObjectData* objData = GetPositionData(&storage);
        trajSys.update(objData, SystemConfig::FIXED_DT, nullptr);
        auto* bTraj = storage.GetBallTrajectoryPool().get(GameConfig::BALL);
        CHECK(bTraj && bTraj->speed > 0.0f && (-bTraj->b) < bPos->position.x); // xVertex < x0 -> 1_BRANCH_DOWNWARD_ONLY

        bPos->position = SimMath::Vec2(945.0f, 610.0f); // distancePercent ~36% in [20,60]
        intents.clear();
        intents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
        objData = GetPositionData(&storage);
        trajSys.update(objData, SystemConfig::FIXED_DT, nullptr);
        bTraj = storage.GetBallTrajectoryPool().get(GameConfig::BALL);
        CHECK(bTraj && bTraj->speed > 0.0f && (-bTraj->b) < bPos->position.x); // xVertex < x0 -> 1_BRANCH_DOWNWARD_ONLY

        // 1b. OPPONENT_1 (index 3, in-RAM edited atk=150 <= 170 -> powerSpike=-1 -> 2_BRANCHES_UP_THEN_DOWN)
        CHECK(Sim_SetCharacterStats(GameConfig::OPPONENT_1, 150.0f, 95.0f, 82.0f, 88.0f) == SIM_OK);
        float readAtk = 0.0f, readDef = 0.0f, readSpd = 0.0f, readJmp = 0.0f;
        CHECK(Sim_GetCharacterStats(GameConfig::OPPONENT_1, &readAtk, &readDef, &readSpd, &readJmp) == SIM_OK);
        CHECK(readAtk == 150.0f && readDef == 95.0f && readSpd == 82.0f && readJmp == 88.0f);

        auto* oPos = storage.GetCharacterPositionPool().get(GameConfig::OPPONENT_1);
        oPos->position = SimMath::Vec2(1800.0f, 500.0f);
        bPos->position = SimMath::Vec2(1835.0f, 590.0f); // distancePercent ~40% in [20,60)
        intents.clear();
        intents.GetCharacterIntentPool().add(GameConfig::OPPONENT_1, CharacterIntent{0.0f, FinalIntent::Spike});
        objData = GetPositionData(&storage);
        trajSys.update(objData, SystemConfig::FIXED_DT, nullptr);
        CHECK(bTraj && (-bTraj->b) < bPos->position.x); // for dir=-1, xVertex < x0 means ahead -> 2_BRANCHES_UP_THEN_DOWN

        // Verify v0 = 124, atk = 80 => appliedV0 = 124 * 80% = 99.2 -> speed = (99.2 / 100) * 4.0 = 3.968
        TestTrajectory::getInstance().AttackPower = 80.0f;
        UpdateNewTrajectory(TrajectoryData(-0.005f, 124.0f), &storage, /*reverseDirection=*/1,
                            /*decreaseC=*/0, /*powerSpike=*/0, TrajectoryBoost(1.0f, 1.0f));
        CHECK(std::fabs(bTraj->speed - ((99.2f / 100.0f) * 4.0f)) < 1e-4f);
        CHECK(Sim_ResetCharacterStats() == SIM_OK);

        // 1c. Option 1 Fake Spike / Whiff check: when ball is out of range (distancePercent > 60%) while Alive,
        // Spike family sets ActionState + Cooldown WITHOUT mutating ballTrajectory or rallyState, and blocks spam on next tick;
        // whereas Bump (Hold action) out of range does NOT enter cooldown and continues auto-moving toward landingX.
        {
            auto* pState = storage.GetCharacterActionStatePool().get(GameConfig::PLAYER);
            auto* rState = storage.GetRallyStatePool().get(DEFAULT_MATCH);
            pState->status = ActionState::None;
            pState->remainTime = 0.0f;
            rState->lastTouch = GameConfig::OPPONENT_1;
            rState->touchCount = 1;

            pPos->position = SimMath::Vec2(500.0f, 400.0f);
            bPos->position = SimMath::Vec2(1500.0f, 500.0f); // far away: distancePercent > 600%
            ballState->landingX = 700.0f;
            const float speedBeforeWhiff = bTraj->speed;

            // Whiff a mid-air Spike
            intents.clear();
            intents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
            objData = GetPositionData(&storage);
            trajSys.update(objData, SystemConfig::FIXED_DT, nullptr);
            CHECK(pState->status == ActionState::Spike);
            const float expectedWhiffCooldownMs = ActionStateInfo::ComputeCooldownMsForJumpStat(
                ActionState::Spike, storage.GetCharacterJumpingStat(GameConfig::PLAYER));
            CHECK(std::fabs(pState->remainTime - expectedWhiffCooldownMs) < 1e-4f);
            CHECK(bTraj->speed == speedBeforeWhiff);
            CHECK(rState->lastTouch == GameConfig::OPPONENT_1);

            // PlayerInputSystem must block continuous Spike spam while remainTime > 0
            PlayerInputSystem inputSys(&intents, &storage);
            intents.clear();
            inputSys.update(SystemConfig::FIXED_DT, SimInputFrame{0, jumpSpike, 0.0f});
            CHECK(intents.GetCharacterIntentPool().get(GameConfig::PLAYER) == nullptr);

            // Reset cooldown and verify Hold Bump out of range does NOT trigger cooldown and still sets moveX toward landingX
            pState->status = ActionState::None;
            pState->remainTime = 0.0f;
            intents.clear();
            intents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Bump});
            objData = GetPositionData(&storage);
            trajSys.update(objData, SystemConfig::FIXED_DT, nullptr);
            CHECK(pState->status == ActionState::None);
            CHECK(pState->remainTime == 0.0f);
            const auto* bumpIntentAfter = intents.GetCharacterIntentPool().get(GameConfig::PLAYER);
            CHECK(bumpIntentAfter && bumpIntentAfter->moveX > 0.0f);
        }

        // 1d. Option 1 Bot 500ms Post-Score Pause (AIInputSystem) & 400ms Walk-Back vs Teleport (FramePreparationSystem)
        {
            ComponentStorage resetStorage;
            resetStorage.InitComponent();
            IntentStorage resetIntents;
            FramePreparationSystem framePrep(&resetStorage, &resetIntents);
            AIInputSystem aiSys(&resetIntents, &resetStorage);

            auto* mState = resetStorage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
            auto* bState = resetStorage.GetBallGameplayStatePool().get(DEFAULT_MATCH);
            auto* pPos2  = resetStorage.GetCharacterPositionPool().get(GameConfig::PLAYER);
            auto* oPos2  = resetStorage.GetCharacterPositionPool().get(GameConfig::OPPONENT_1);

            const float netX = SystemConfig::offsetX + BigRect::RECT_WIDTH * 0.5f;
            const float playerTargetX = netX - 400.0f; // 966.0f when leftServingEntity == PLAYER (slot 0)

            // Simulate rally ending with Bot scoring: stateFrame = 1 -> transitions to 0 (Reset), rightScore = 1, servingTeam = RIGHT
            mState->rightScore  = 1;
            mState->servingTeam = Team::RIGHT;
            bState->stateFrame  = 1;
            framePrep.update(); // records _prevStateFrame = 1

            // Place PLAYER 70px left of playerTargetX (within 400ms = 168px walk-back range) and airborne (y = 400),
            // and OPPONENT_1 (serving entity) at x = 1800, y = 380
            pPos2->position = SimMath::Vec2(playerTargetX - 70.0f, 400.0f);
            oPos2->position = SimMath::Vec2(1800.0f, 380.0f);

            // Transition to Reset (0)
            bState->stateFrame = BallStateFrame::Reset;
            for (int tick = 1; tick <= 30; ++tick)
            {
                resetIntents.clear();
                framePrep.update();
                aiSys.update();
                // During the 30 ticks (500ms), AI must NOT serve yet
                const auto* aiIntent = resetIntents.GetCharacterIntentPool().get(GameConfig::OPPONENT_1);
                CHECK(aiIntent == nullptr || aiIntent->finalIntent != FinalIntent::Serve);

                if (tick == 1)
                {
                    // On tick 1 of Reset: Y snapped to ground (265.0f), serving entity (OPPONENT_1) teleported to RIGHT_POSITION_RESET_X,
                    // while non-serving PLAYER did NOT teleport X and received MoveRight (+7.0f) walk-back intent!
                    CHECK(pPos2->position.y == SystemConfig::MIN_Y);
                    CHECK(oPos2->position.y == SystemConfig::MIN_Y);
                    CHECK(std::fabs(oPos2->position.x - BigRect::RIGHT_POSITION_RESET_X) < 0.01f);
                    CHECK(std::fabs(pPos2->position.x - (playerTargetX - 70.0f)) < 0.01f);
                    const auto* pWalkIntent = resetIntents.GetCharacterIntentPool().get(GameConfig::PLAYER);
                    CHECK(pWalkIntent && pWalkIntent->moveX > 0.0f);
                }

                ApplyHorizontalMovement(&resetIntents, &resetStorage, nullptr);
            }

            // Within <= 24 ticks (400ms), PLAYER walked smoothly to exact playerTargetX!
            CHECK(std::fabs(pPos2->position.x - playerTargetX) < 0.01f);

            // On tick 31 (after 500ms = 30 ticks), AI emits Serve!
            resetIntents.clear();
            framePrep.update();
            aiSys.update();
            const auto* aiServeIntent = resetIntents.GetCharacterIntentPool().get(GameConfig::OPPONENT_1);
            CHECK(aiServeIntent && aiServeIntent->finalIntent == FinalIntent::Serve);

            // Also verify pre-calculation teleport when distance > maxReachableDist (300px > 168px)
            bState->stateFrame = 1;
            framePrep.update();
            pPos2->position = SimMath::Vec2(playerTargetX - 300.0f, 350.0f);
            bState->stateFrame = BallStateFrame::Reset;
            resetIntents.clear();
            framePrep.update();
            CHECK(pPos2->position.y == SystemConfig::MIN_Y);
            CHECK(std::fabs(pPos2->position.x - playerTargetX) < 0.01f); // Teleported immediately!

            // 1e. Regression check: during stateFrame > 0 (post-point ball roll), PLAYER moving right near net
            // must be clamped to MAX_X_LEFT (never crossing onto opponent court), and holding Bump when landingX
            // is on opponent side (>= NET_X) must NOT pull PLAYER toward opponent court or override MoveLeft!
            bState->stateFrame = 45; // post-point roll stateFrame > 0
            pPos2->position    = SimMath::Vec2(SystemConfig::MAX_X_LEFT, SystemConfig::MIN_Y);
            for (int tick = 0; tick < 20; ++tick)
            {
                resetIntents.clear();
                resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{7.0f, FinalIntent::None});
                ApplyHorizontalMovement(&resetIntents, &resetStorage, nullptr);
            }
            CHECK(pPos2->position.x <= SystemConfig::MAX_X_LEFT);

            bState->stateFrame = BallStateFrame::Alive;
            bState->landingX   = 1880.0f; // opponent court (> BigRect::NET_X)
            pPos2->position    = SimMath::Vec2(1100.0f, SystemConfig::MIN_Y);
            resetIntents.clear();
            resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{-7.0f, FinalIntent::Bump});
            UpdateTrajectoryFromIntent resetTrajSys(&resetIntents, &resetStorage);
            ObjectData* resetObjData = GetPositionData(&resetStorage);
            resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
            const auto* pIntentAfterBump = resetIntents.GetCharacterIntentPool().get(GameConfig::PLAYER);
            CHECK(pIntentAfterBump && pIntentAfterBump->moveX < 0.0f); // MoveLeft preserved, not pulled to 1880.0f!

            // 1f. Full-World Slow-Motion (0.5s = 30 frames, 1/6 speed, 20% random ONLY on JumpSpike ball contact):
            // - Verify whiffing JumpSpike (out of range) NEVER triggers slow-motion (0/100 trials).
            // - Verify hitting JumpSpike (in range via TryApplyTrajectory) triggers slowMoFramesRemaining == 30 (~20% of trials).
            // - Verify 1/6 speed gate: out of 30 frames, exactly 5 frames (remaining % 6 == 0) advance and 25 frames freeze!
            auto* bPos2 = resetStorage.GetBallPositionPool().get(GameConfig::BALL);
            resetStorage.TriggerSlowMotion(0);
            int whiffSlowMoCount = 0;
            pPos2->position = SimMath::Vec2(500.0f, 450.0f);
            bPos2->position = SimMath::Vec2(1500.0f, 500.0f); // out of range (whiff)
            for (int trial = 0; trial < 100; ++trial)
            {
                resetIntents.clear();
                resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
                resetObjData = GetPositionData(&resetStorage);
                resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
                if (resetStorage.GetSlowMoFramesRemaining() > 0)
                    ++whiffSlowMoCount;
            }
            CHECK(whiffSlowMoCount == 0); // Whiff NEVER triggers slow-motion!

            constexpr int kExpectedSlowMoFrames = (SystemConfig::SLOW_MO_DURATION_MS * SystemConfig::TICK_RATE) / 1000;
            int hitSlowMoCount = 0;
            std::srand(42u);
            for (int trial = 0; trial < 200; ++trial)
            {
                resetStorage.TriggerSlowMotion(0);
                pPos2->position = SimMath::Vec2(900.0f, 500.0f);
                bPos2->position = SimMath::Vec2(945.0f, 610.0f); // in range (JumpSpike ball contact!)
                resetIntents.clear();
                resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
                resetObjData = GetPositionData(&resetStorage);
                resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
                if (resetStorage.GetSlowMoFramesRemaining() == kExpectedSlowMoFrames)
                    ++hitSlowMoCount;
            }
            const int expectedSlowMoHits = (200 * SystemConfig::SLOW_MO_CHANCE_PERCENT) / 100;
            CHECK(hitSlowMoCount >= expectedSlowMoHits - 25 && hitSlowMoCount <= expectedSlowMoHits + 25);

            // Verify 1/6 speed countdown gate over kExpectedSlowMoFrames (60 frames = 1.0s)
            resetStorage.TriggerSlowMotion(kExpectedSlowMoFrames);
            int activeStepFrames = 0;
            int frozenFrames     = 0;
            while (resetStorage.GetSlowMoFramesRemaining() > 0)
            {
                const int rem = resetStorage.DecrementSlowMoFrames();
                if ((rem % 6) == 0)
                    ++activeStepFrames;
                else
                    ++frozenFrames;
            }
            CHECK(activeStepFrames == (kExpectedSlowMoFrames / 6) &&
                  frozenFrames == (kExpectedSlowMoFrames - (kExpectedSlowMoFrames / 6)) &&
                  resetStorage.GetSlowMoFramesRemaining() == 0);

            // Verify ground-impact / scoring clears slow-motion and does NOT schedule a second slow-motion / camera zoom
            // when the ball hits the ground and bounces away (SLOW_MO_SCORE_CHANCE_PERCENT == 0):
            {
                GameRuleEvaluationSystem ruleSys(&resetStorage, &resetIntents);
                auto* rSt = resetStorage.GetRallyStatePool().get(DEFAULT_MATCH);
                mState->leftScore  = 0;
                mState->rightScore = 0;
                rSt->lastTouch     = GameConfig::PLAYER;
                rSt->touchCount    = 1;
                bState->stateFrame = BallStateFrame::Alive;
                bPos2->position    = SimMath::Vec2(1800.0f, SystemConfig::MIN_Y); // right court ground contact
                resetStorage.TriggerSlowMotion(30); // active slow-mo from JumpSpike before ground hit

                ruleSys.update();
                CHECK(mState->leftScore == 1);
                CHECK(bState->stateFrame > 0);
                CHECK(resetStorage.GetSlowMoFramesRemaining() == 0); // Cleared immediately on ground contact!
                CHECK(resetStorage.GetPendingSlowMoDelayFrames() == -1); // No delayed 2nd slow-mo scheduled!

                for (int d = 0; d < 20; ++d)
                {
                    ApplyBallTrajectory(&resetIntents, &resetStorage);
                    CHECK(resetStorage.GetSlowMoFramesRemaining() == 0);
                }
                mState->leftScore = 0;
            }

            // 1g. Post-Ground-Impact Parabolic Bounce with atk = 230:
            // - Also verify SpikeLight (decreaseC = 20%) at high ball y0 = 750.0f never produces NaN b (c clamped >= y0 after decreaseC)!
            // - Log exact v0, appliedV0, v0Multiplier, and ballTrajectory->speed for Serve, SpikeLight, SpikeMedium, SpikeStrong, and JumpSpike at atk = 180 and atk = 230!
            // - Stage 1 (when impactSpeed > k on Bounce 1): trajectory->c INCREASES above the incoming spike c, and speed decays by retainRatio (55%) without artificial capping!
            // - Stage 2 (when impactSpeed <= k on Bounce 2 & 3 after speed decays): trajectory->c GRADUALLY DECREASES!
            // - When stateFrame decrements from 1 -> 0 (Reset), trajectory & speed immediately reset to 0!
            auto* bTraj2 = resetStorage.GetBallTrajectoryPool().get(GameConfig::BALL);
            for (float testAtk : {180.0f, 230.0f})
            {
                CHECK(Sim_SetCharacterStats(GameConfig::PLAYER, testAtk, 80.0f, 90.0f, 95.0f) == SIM_OK);
                auto logActionSpeed = [&](const char* actionLabel, FinalIntent intentType, float px, float py, float bx, float by, int stFrame) {
                    bState->stateFrame = stFrame;
                    pPos2->position    = SimMath::Vec2(px, py);
                    bPos2->position    = SimMath::Vec2(bx, by);
                    resetIntents.clear();
                    resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, intentType});
                    resetObjData = GetPositionData(&resetStorage);
                    resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
                    std::printf("[V0_SPEED_LOG] atk=%.1f | %-11s | appliedAtk=%.1f | speed=%.2f px/tick (x10=%.1f) | a=%.6f | c=%.2f\n",
                                testAtk, actionLabel, TestTrajectory::getInstance().AttackPower, bTraj2->speed,
                                std::fabs(bTraj2->speed) * 10.0f, bTraj2->a, bTraj2->c);
                };
                auto* mSt = resetStorage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
                mSt->servingTeam = Team::LEFT;
                logActionSpeed("Serve",       FinalIntent::Serve,       200.0f, 265.0f, 220.0f, 320.0f, BallStateFrame::Reset);
                logActionSpeed("SpikeLight",  FinalIntent::SpikeLight,  1250.0f, 670.0f, 1280.0f, 750.0f, BallStateFrame::Alive);
                logActionSpeed("SpikeMedium", FinalIntent::SpikeMedium, 1250.0f, 520.0f, 1280.0f, 600.0f, BallStateFrame::Alive);
                logActionSpeed("SpikeStrong", FinalIntent::SpikeStrong, 1250.0f, 520.0f, 1280.0f, 600.0f, BallStateFrame::Alive);
                logActionSpeed("JumpSpike",   FinalIntent::Spike,       1200.0f, 520.0f, 1245.0f, 630.0f, BallStateFrame::Alive);
            }

            // Sub-check A: SpikeLight (decreaseC = 20) at high ball y0 = 750.0f (> CalculateC(80) * 0.8 = 683.08f)
            CHECK(Sim_SetCharacterStats(GameConfig::PLAYER, 230.0f, 80.0f, 90.0f, 95.0f) == SIM_OK);
            bState->stateFrame = BallStateFrame::Alive;
            pPos2->position    = SimMath::Vec2(1250.0f, 670.0f);
            bPos2->position    = SimMath::Vec2(1280.0f, 750.0f); // distancePercent ~15% (< 50%), y0 = 750.0f
            resetIntents.clear();
            resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::SpikeLight});
            resetObjData = GetPositionData(&resetStorage);
            resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
            CHECK(!std::isnan(bTraj2->b));
            CHECK(bTraj2->c >= 750.0f);
            CHECK(std::fabs(bTraj2->speed - 11.776f) < 0.01f);
            CHECK(std::fabs(resetStorage.GetLastSpikeContactHeight() - 750.0f) < 0.01f);
            CHECK(offsetof(data, rallyState) + offsetof(SimRallyStateData, spikeContactHeight) == 674u);

            // Sub-check B: JumpSpike with atk = 230 across Bounce 1 (Stage 1: c increases) -> Bounce 2 & 3 (Stage 2: c decreases)
            bState->stateFrame = BallStateFrame::Alive;
            pPos2->position    = SimMath::Vec2(1200.0f, 520.0f);
            bPos2->position    = SimMath::Vec2(1245.0f, 630.0f); // JumpSpike near net so ball crosses onto opponent court (> 1380)
            resetIntents.clear();
            resetIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
            resetObjData = GetPositionData(&resetStorage);
            resetTrajSys.update(resetObjData, SystemConfig::FIXED_DT, nullptr);
            CHECK(std::fabs(resetStorage.GetLastSpikeContactHeight() - 630.0f) < 0.01f);

            const float initialSpikeC     = bTraj2->c;
            const float initialSpikeSpeed = bTraj2->speed;
            CHECK(initialSpikeSpeed > SystemConfig::BOUNCE_SPEED_THRESHOLD_K); // 18.216 > 14.4f

            // Step until Bounce 1 (first ground impact on opponent court with impactSpeed > k)
            bState->stateFrame = 1200;
            bool hitBounce1 = false;
            for (int step = 0; step < 150 && !hitBounce1; ++step)
            {
                ApplyBallTrajectory(&resetIntents, &resetStorage);
                if (bPos2->position.y == SystemConfig::MIN_Y)
                    hitBounce1 = true;
            }
            const float bounce1C     = bTraj2->c;
            const float bounce1Speed = bTraj2->speed;
            const float expectedBounce1C = std::max(initialSpikeC, SystemConfig::MIN_Y) +
                                           (std::fabs(initialSpikeSpeed) / SystemConfig::BOUNCE_SPEED_THRESHOLD_K) *
                                               SystemConfig::BOUNCE_C_MULTIPLIER;
            const float expectedBounce1Speed = initialSpikeSpeed * SystemConfig::BOUNCE_SPEED_DECAY;
            CHECK(hitBounce1);
            CHECK(bounce1C > initialSpikeC); // Stage 1: c INCREASES on Bounce 1!
            CHECK(std::fabs(bounce1C - expectedBounce1C) < 0.05f);
            CHECK(std::fabs(bounce1Speed - expectedBounce1Speed) < 0.05f);

            // Next tick after Bounce 1 -> ball bounces HIGH up into the air above MIN_Y!
            ApplyBallTrajectory(&resetIntents, &resetStorage);
            CHECK(bPos2->position.y > SystemConfig::MIN_Y);

            // Step until Bounce 2 (subsequent ground impact with impactSpeed <= k)
            bool hitBounce2 = false;
            for (int step = 0; step < 500 && !hitBounce2; ++step)
            {
                ApplyBallTrajectory(&resetIntents, &resetStorage);
                if (bPos2->position.y == SystemConfig::MIN_Y)
                    hitBounce2 = true;
            }
            const float bounce2C = bTraj2->c;
            const float bounce2Speed = bTraj2->speed;
            CHECK(hitBounce2);
            CHECK(std::fabs(bounce1Speed) <= SystemConfig::BOUNCE_SPEED_THRESHOLD_K);
            CHECK(bounce2C < bounce1C && bounce2C > SystemConfig::MIN_Y); // Stage 2: c GRADUALLY DECREASES on Bounce 2!

            // Step until Bounce 3 (further gradual decrease of c)
            ApplyBallTrajectory(&resetIntents, &resetStorage);
            CHECK(bPos2->position.y > SystemConfig::MIN_Y);
            bool hitBounce3 = false;
            for (int step = 0; step < 500 && !hitBounce3; ++step)
            {
                ApplyBallTrajectory(&resetIntents, &resetStorage);
                if (bPos2->position.y == SystemConfig::MIN_Y)
                    hitBounce3 = true;
            }
            const float bounce3C = bTraj2->c;
            CHECK(hitBounce3);
            CHECK(bounce3C < bounce2C && bounce3C > SystemConfig::MIN_Y); // Stage 2: c GRADUALLY DECREASES on Bounce 3!

            std::printf("[BOUNCE_ATK230] initialSpike(c=%.2f, speed=%.2f) -> Bounce1(c=%.2f, speed=%.2f) -> Bounce2(c=%.2f, speed=%.2f) -> Bounce3(c=%.2f, speed=%.2f)\n",
                        initialSpikeC, initialSpikeSpeed, bounce1C, bounce1Speed, bounce2C,
                        bounce2Speed, bounce3C, bTraj2->speed);
            CHECK(Sim_ResetCharacterStats() == SIM_OK);

            // Step 3: When stateFrame decrements from 1 -> 0 (Reset), trajectory & speed immediately reset to 0!
            bState->stateFrame = 1;
            ApplyBallTrajectory(&resetIntents, &resetStorage);
            CHECK(bState->stateFrame == BallStateFrame::Reset);
            CHECK(bTraj2->a == 0.0f && bTraj2->b == 0.0f && bTraj2->c == 0.0f && bTraj2->speed == 0.0f);

            // 1h. 2-Phase Hold-Bump Attack Serve (PrepareAttackServe -> LaunchAttackServe -> Airborne Jump across 9m line -> Whiff reset to 650.0f vs Spike contact touchCount = 1)
            // and BallSize (0.36m = 28.8px, 15/79 of 1.90m character) + DetectPlayerBall ballRadius subtraction check:
            {
                CHECK(std::fabs(MatchObjectConfig::BallSize.x - 0.36f * SystemConfig::PIXELS_PER_METER) < 1e-4f);
                CHECK(std::fabs(MatchObjectConfig::BallSize.y - 0.36f * SystemConfig::PIXELS_PER_METER) < 1e-4f);

                ComponentStorage serveStorage;
                serveStorage.InitComponent();
                IntentStorage serveIntents;
                FramePreparationSystem serveFramePrep(&serveStorage, &serveIntents);
                PlayerInputSystem serveInputSys(&serveIntents, &serveStorage);
                UpdateTrajectoryFromIntent serveTrajSys(&serveIntents, &serveStorage);

                auto* sMatch = serveStorage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
                auto* sBallState = serveStorage.GetBallGameplayStatePool().get(DEFAULT_MATCH);
                auto* sRally = serveStorage.GetRallyStatePool().get(DEFAULT_MATCH);
                auto* sBallTraj = serveStorage.GetBallTrajectoryPool().get(GameConfig::BALL);
                auto* sPlayerPos = serveStorage.GetCharacterPositionPool().get(GameConfig::PLAYER);
                const uint32_t bumpBit = mapping.ActionToButtonMask(GameAction::Bump);

                sMatch->servingTeam = Team::LEFT;
                sBallState->stateFrame = BallStateFrame::Reset;
                sRally->Reset();
                serveFramePrep.update();

                // Verify initial Reset parabola initialized to (NormalizeAttackServeA(ATTACK_SERVE_BASE_A), ATTACK_SERVE_BASE_C, speed = 0)
                // and landingX is computed to the full end of the parabola (> ballPos.x):
                auto* sBallPos = serveStorage.GetBallPositionPool().get(GameConfig::BALL);
                const float normBaseA = SystemConfig::NormalizeAttackServeA(SystemConfig::ATTACK_SERVE_BASE_A);
                const float normMaxA  = SystemConfig::NormalizeAttackServeA(SystemConfig::ATTACK_SERVE_MAX_A);
                CHECK(std::fabs(sBallTraj->a - normBaseA) < 1e-6f);
                CHECK(std::fabs(sBallTraj->c - SystemConfig::ATTACK_SERVE_BASE_C) < 1e-4f);
                CHECK(sBallTraj->speed == 0.0f);
                CHECK(sBallPos && sBallState->landingX > sBallPos->position.x);

                // Phase 1: Hold Bump for 10 ticks -> PrepareAttackServe widens a, increases c, and advances landingX while keeping stateFrame == Reset and speed == 0
                float prevA = sBallTraj->a;
                float prevC = sBallTraj->c;
                float prevLandingX = sBallState->landingX;
                for (int holdTick = 0; holdTick < 10; ++holdTick)
                {
                    serveIntents.clear();
                    serveFramePrep.update();
                    serveInputSys.update(SystemConfig::FIXED_DT, SimInputFrame{bumpBit, (holdTick == 0 ? bumpBit : 0u), 0.0f, 0u});
                    const auto* pInt = serveIntents.GetCharacterIntentPool().get(GameConfig::PLAYER);
                    CHECK(pInt && pInt->finalIntent == FinalIntent::PrepareAttackServe);
                    ObjectData* sObj = GetPositionData(&serveStorage);
                    serveTrajSys.update(sObj, SystemConfig::FIXED_DT, nullptr);
                    ApplyBallTrajectory(&serveIntents, &serveStorage);
                }
                CHECK(sBallState->stateFrame == BallStateFrame::Reset);
                CHECK(sBallTraj->speed == 0.0f);
                CHECK(sBallTraj->a > prevA && sBallTraj->a <= normMaxA);
                CHECK(sBallTraj->c > prevC && sBallTraj->c <= SystemConfig::ATTACK_SERVE_MAX_C);
                CHECK(sBallState->landingX > prevLandingX);

                // Continue holding Bump for 100 more ticks to verify strict upper caps:
                for (int holdTick = 0; holdTick < 100; ++holdTick)
                {
                    serveIntents.clear();
                    serveFramePrep.update();
                    serveInputSys.update(SystemConfig::FIXED_DT, SimInputFrame{bumpBit, 0u, 0.0f, 0u});
                    ObjectData* sObj = GetPositionData(&serveStorage);
                    serveTrajSys.update(sObj, SystemConfig::FIXED_DT, nullptr);
                    ApplyBallTrajectory(&serveIntents, &serveStorage);
                }
                CHECK(std::fabs(sBallTraj->a - normMaxA) < 1e-6f);
                CHECK(std::fabs(sBallTraj->c - SystemConfig::ATTACK_SERVE_MAX_C) < 1e-4f);
                const float maxApexHeightMeters = (sBallTraj->c - SystemConfig::MIN_Y) / SystemConfig::PIXELS_PER_METER;
                CHECK(std::fabs(maxApexHeightMeters - 4.30f) < 1e-4f && maxApexHeightMeters <= 4.3001f);

                // Phase 2: Release Bump -> LaunchAttackServe launches toss into court (stateFrame = Alive, speed = 1.80f, touchCount == 0!)
                serveIntents.clear();
                serveFramePrep.update();
                serveInputSys.update(SystemConfig::FIXED_DT, SimInputFrame{0u, 0u, 0.0f, bumpBit});
                const auto* launchInt = serveIntents.GetCharacterIntentPool().get(GameConfig::PLAYER);
                CHECK(launchInt && launchInt->finalIntent == FinalIntent::LaunchAttackServe);
                ObjectData* sObj = GetPositionData(&serveStorage);
                serveTrajSys.update(sObj, SystemConfig::FIXED_DT, nullptr);
                CHECK(sBallState->stateFrame == BallStateFrame::Alive);
                CHECK(sRally->touchCount == 0);
                CHECK(std::fabs(sBallTraj->speed - (SystemConfig::ATTACK_SERVE_TOSS_V0 / 100.0f) * 4.0f) < 1e-4f);

                // 9m Line Rule Check A: While grounded and touchCount == 0, player cannot walk past LEFT_9M_LINE_X (650.0f)
                sPlayerPos->position = SimMath::Vec2(BigRect::LEFT_9M_LINE_X, SystemConfig::MIN_Y);
                serveIntents.clear();
                serveIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{14.0f, FinalIntent::None});
                ApplyHorizontalMovement(&serveIntents, &serveStorage, nullptr);
                CHECK(std::fabs(sPlayerPos->position.x - BigRect::LEFT_9M_LINE_X) < 1e-4f);

                // 9m Line Rule Check B: While airborne (pos.y > MIN_Y) and touchCount == 0, player CAN jump across LEFT_9M_LINE_X into court (> 650.0f)
                sPlayerPos->position = SimMath::Vec2(BigRect::LEFT_9M_LINE_X + 50.0f, SystemConfig::MIN_Y + 80.0f);
                serveIntents.clear();
                serveIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{14.0f, FinalIntent::None});
                ApplyHorizontalMovement(&serveIntents, &serveStorage, nullptr);
                CHECK(sPlayerPos->position.x > BigRect::LEFT_9M_LINE_X + 50.0f);

                // 9m Line Rule Check C: If player lands on ground (pos.y <= MIN_Y) while touchCount == 0 (missed/whiffed jump serve)
                // even with FinalIntent::None and no horizontal movement input, ApplyVerticalVelocity snaps x back to LEFT_9M_LINE_X (650.0f)!
                serveIntents.clear();
                serveIntents.GetVelocityIntentPool().add(GameConfig::PLAYER, VelocityIntent{0.0f, -100.0f});
                ApplyVerticalVelocity(&serveIntents, &serveStorage);
                CHECK(sPlayerPos->position.y == SystemConfig::MIN_Y);
                CHECK(std::fabs(sPlayerPos->position.x - BigRect::LEFT_9M_LINE_X) < 1e-4f);

                // 9m Line Rule Check D: When player jumps into court and spikes the tossed ball in mid-air, touchCount becomes 1 (> 0)
                // and landing inside the court (> 650.0f) is allowed for normal rally play!
                sPlayerPos->position = SimMath::Vec2(720.0f, 450.0f);
                sBallPos->position   = SimMath::Vec2(755.0f, 540.0f);
                serveIntents.clear();
                serveIntents.GetCharacterIntentPool().add(GameConfig::PLAYER, CharacterIntent{0.0f, FinalIntent::Spike});
                sObj = GetPositionData(&serveStorage);
                serveTrajSys.update(sObj, SystemConfig::FIXED_DT, nullptr);
                CHECK(sRally->touchCount == 1);
                CHECK(sRally->lastTouch == GameConfig::PLAYER);
                serveIntents.clear();
                serveIntents.GetVelocityIntentPool().add(GameConfig::PLAYER, VelocityIntent{0.0f, -200.0f});
                ApplyVerticalVelocity(&serveIntents, &serveStorage);
                CHECK(sPlayerPos->position.y == SystemConfig::MIN_Y);
                CHECK(std::fabs(sPlayerPos->position.x - 720.0f) < 1e-4f); // Stays at 720.0f inside court since touchCount == 1!
            }
        }
    }
}

void TestPriorityMovementAndCooldown(const InputMapping& mapping)
{
    constexpr std::array<int, 3> ids = {6, 7, 8};
    constexpr std::array<FinalIntent, 3> intents = {
        FinalIntent::SpikeLight, FinalIntent::SpikeMedium, FinalIntent::SpikeStrong};
    const uint32_t jump = ConfiguredMask(mapping, 5);
    const float unlockedAirY = SystemConfig::MIN_Y + 40.0f; // airborne in falling/unlocked phase (jumpUpTime == 0)
    for (size_t i = 0; i < ids.size(); ++i)
    {
        Fixture fixture(unlockedAirY);
        const auto* result = fixture.Submit(0, jump | ConfiguredMask(mapping, ids[i]));
        CHECK(result && result->finalIntent == intents[i]);
    }

    Fixture bump(SystemConfig::MIN_Y);
    const auto* bumpResult = bump.Submit(
        mapping.ActionToButtonMask(GameAction::Bump), jump);
    CHECK(bumpResult && bumpResult->finalIntent == FinalIntent::Bump);

    constexpr std::array<GameAction, 5> laterActions = {
        GameAction::Set, GameAction::Serve, GameAction::SpikeLight,
        GameAction::SpikeMedium, GameAction::SpikeStrong};
    constexpr std::array<FinalIntent, 5> laterIntents = {
        FinalIntent::Set, FinalIntent::Serve, FinalIntent::SpikeLight,
        FinalIntent::SpikeMedium, FinalIntent::SpikeStrong};
    for (size_t i = 0; i < laterActions.size(); ++i)
    {
        Fixture later(unlockedAirY);
        const uint32_t laterMask = mapping.ActionToButtonMask(laterActions[i]);
        const auto* laterResult = later.Submit(0, jump | laterMask);
        CHECK(laterResult && laterResult->finalIntent == laterIntents[i]);
    }

    // Preserve the existing adjacent last-assignment-wins chain. Bump is held;
    // the remaining actions are press-edge requests.
    const uint32_t jumpSpike = mapping.ActionToButtonMask(GameAction::JumpSpike);
    const uint32_t bumpMask = mapping.ActionToButtonMask(GameAction::Bump);
    const uint32_t setMask = mapping.ActionToButtonMask(GameAction::Set);
    const uint32_t serveMask = mapping.ActionToButtonMask(GameAction::Serve);
    const uint32_t lightMask = mapping.ActionToButtonMask(GameAction::SpikeLight);
    const uint32_t mediumMask = mapping.ActionToButtonMask(GameAction::SpikeMedium);
    const uint32_t strongMask = mapping.ActionToButtonMask(GameAction::SpikeStrong);

    Fixture jumpSpikeBump(SystemConfig::MIN_Y);
    const auto* jumpSpikeBumpResult = jumpSpikeBump.Submit(bumpMask, jumpSpike);
    CHECK(jumpSpikeBumpResult && jumpSpikeBumpResult->finalIntent == FinalIntent::Bump);

    Fixture bumpSet(SystemConfig::MIN_Y);
    const auto* bumpSetResult = bumpSet.Submit(bumpMask, setMask);
    CHECK(bumpSetResult && bumpSetResult->finalIntent == FinalIntent::Set);

    Fixture setServe(SystemConfig::MIN_Y);
    const auto* setServeResult = setServe.Submit(0, setMask | serveMask);
    CHECK(setServeResult && setServeResult->finalIntent == FinalIntent::Serve);

    Fixture serveLight(unlockedAirY);
    const auto* serveLightResult = serveLight.Submit(0, serveMask | lightMask);
    CHECK(serveLightResult && serveLightResult->finalIntent == FinalIntent::SpikeLight);

    Fixture lightMedium(unlockedAirY);
    const auto* lightMediumResult = lightMedium.Submit(0, lightMask | mediumMask);
    CHECK(lightMediumResult && lightMediumResult->finalIntent == FinalIntent::SpikeMedium);

    Fixture mediumStrong(unlockedAirY);
    const auto* mediumStrongResult = mediumStrong.Submit(0, mediumMask | strongMask);
    CHECK(mediumStrongResult && mediumStrongResult->finalIntent == FinalIntent::SpikeStrong);

    Fixture movement(SystemConfig::MIN_Y);
    const auto* movementResult = movement.Submit(ConfiguredMask(mapping, 1), jump);
    CHECK(movementResult != nullptr);
    if (movementResult)
    {
        CHECK(movementResult->finalIntent == FinalIntent::Jump);
        CHECK(movementResult->moveX < 0.0f);
    }

    Fixture cooldown(SystemConfig::MIN_Y, 1.0f);
    CHECK(cooldown.Submit(0, jump) == nullptr);
    cooldown.jump.update(SystemConfig::FIXED_DT);
    const auto* cooldownState = cooldown.components.GetCharacterActionStatePool().get(GameConfig::PLAYER);
    const auto* cooldownFrames = cooldown.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(cooldownState && cooldownState->remainTime == 1.0f);
    CHECK(cooldownState && cooldownState->status == ActionState::None);
    CHECK(cooldownFrames && cooldownFrames->remainingFrames == 0);
}

void TestPressHoldReleaseRepressContract(const InputMapping& mapping)
{
    const uint32_t jump = ConfiguredMask(mapping, 5);
    Fixture fixture(SystemConfig::MIN_Y);
    const auto* press = fixture.Submit(jump, jump);
    CHECK(press && press->finalIntent == FinalIntent::Jump);
    const auto* held = fixture.Submit(jump, 0);
    CHECK(held == nullptr); // held without a new pressed edge does not retrigger the action
    const auto* released = fixture.Submit(0, 0);
    CHECK(released == nullptr);
    const auto* repressed = fixture.Submit(jump, jump);
    CHECK(repressed && repressed->finalIntent == FinalIntent::Jump);
}

void TestTrajectoryAcceptanceBlocker()
{
    // The local fixture intentionally does not write ball/player authoritative state.
    // Existing S07 diagnostics use Sim_SetInput masks, not configured Get_data IDs.
    // A valid natural-rally sequence using IDs 6/7/8 has not yet been established.
    std::puts("[BLOCKED] Valid/invalid Spike trajectory acceptance through configured Get_data IDs requires a deterministic natural-rally fixture.");
}

Input InputWith(std::initializer_list<uint32_t> ids)
{
    Input input{};
    input.buttonCount = static_cast<uint32_t>(ids.size());
    size_t index = 0;
    for (uint32_t id : ids)
        if (index < MAX_INPUT_BUTTONS) input.buttonIds[index++] = id;
    if (index != input.buttonCount) input.buttonCount = static_cast<uint32_t>(index);
    return input;
}

bool CheckRuntimeStep(const data& previous, const data& current, const char* label)
{
    const bool ok = current.status == SIM_OK && current.entityCount == 4 &&
                    current.tick == previous.tick + 1 &&
                    current.entities[0].entityId == GameConfig::PLAYER &&
                    current.entities[1].entityId == GameConfig::OPPONENT_1;
    Check(ok, label, __LINE__);
    return ok;
}

data Step(const data& previous, std::initializer_list<uint32_t> ids, const char* label)
{
    const data current = Get_data(InputWith(ids));
    CheckRuntimeStep(previous, current, label);
    return current;
}

void TestConfiguredGetDataJump(const char* mappingPath)
{
    if (mappingPath == nullptr || mappingPath[0] == '\0')
    {
        std::puts("[BLOCKED] Pass the real button_mapping.json path to exercise configured Get_data IDs.");
        return;
    }
    if (!Sim_Initialize(mappingPath))
    {
        std::puts("[FAIL] Sim_Initialize rejected the configured button mapping.");
        ++failures;
        return;
    }

    Sim_Reset();
    data current = Get_data(Input{});
    Check(current.status == SIM_OK && current.entityCount == 4, "Get_data initial snapshot", __LINE__);
    CHECK(current.tick == 1u);
    Check(current.entities[0].isGrounded != 0, "configured pipeline starts with grounded player", __LINE__);
    CHECK(current.entities[0].airborneState == SIM_AIRBORNE_GROUNDED);
    Check(current.entities[1].entityId == GameConfig::OPPONENT_1, "entity 1 remains the opponent", __LINE__);

    data jumped = Step(current, {5}, "Get_data Jump advances exactly one tick");
    CHECK(jumped.entities[0].flags.hasCharacterIntent != 0);
    CHECK(jumped.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::Jump));
    CHECK(jumped.entities[0].jumpFrame.remainingFrames > 0);
    CHECK(jumped.entities[0].airborneState == SIM_AIRBORNE_JUMP_UP);
    current = Step(jumped, {}, "empty input release advances one tick");

    bool airborne = current.entities[0].isGrounded == 0;
    for (int tick = 0; tick < 40 && !airborne; ++tick)
    {
        current = Step(current, {}, "airborne transition tick advances once");
        airborne = current.entities[0].isGrounded == 0;
    }
    CHECK(airborne);
    if (airborne)
    {
        for (int tick = 0; tick < 80 && current.entities[0].actionState.remainTime > 0.0f; ++tick)
            current = Step(current, {}, "cooldown wait tick advances once");

        // During early ascent (< 80% height, remainingFrames > 3), non-movement actions are locked:
        if (current.entities[0].jumpFrame.remainingFrames > 3)
        {
            data lockedEarlyJump = Step(current, {5}, "early ascent (<80%) Jump is locked");
            CHECK(lockedEarlyJump.entities[0].flags.hasCharacterIntent == 0 ||
                  lockedEarlyJump.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::None));
            current = Step(lockedEarlyJump, {}, "release after early ascent lock check");
        }

        // Advance until >= 80% jump height (remainingFrames <= 3) so airborne actions unlock
        while (current.entities[0].jumpFrame.remainingFrames > 3)
            current = Step(current, {}, "advance to >= 80% jump height");

        const int framesBefore = current.entities[0].jumpFrame.remainingFrames;
        data airborneJump = Step(current, {5}, "airborne configured Jump at >= 80% height advances once");
        CHECK(airborneJump.entities[0].flags.hasCharacterIntent != 0);
        CHECK(airborneJump.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::Jump));
        CHECK(airborneJump.entities[0].actionState.status != SIM_ACTION_SPIKE);
        CHECK(airborneJump.entities[0].jumpFrame.remainingFrames < SystemConfig::JUMP_FRAME);
        CHECK(airborneJump.entities[0].jumpFrame.remainingFrames <= framesBefore);

        data held = Step(airborneJump, {5}, "held configured Jump advances once");
        CHECK(held.entities[0].flags.hasCharacterIntent == 0 ||
              held.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::None));
        data released = Step(held, {}, "configured Jump release advances once");
        data repressed = Step(released, {5}, "configured Jump re-press advances once");
        CHECK(repressed.entities[0].flags.hasCharacterIntent != 0);
        CHECK(repressed.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::Jump));

        for (int tick = 0; tick < 120 && repressed.entities[0].jumpFrame.remainingFrames > 0; ++tick)
            repressed = Step(repressed, {}, "wait for JumpUp frames to finish");
        CHECK(repressed.entities[0].isGrounded == 0);
        CHECK(repressed.entities[0].airborneState == SIM_AIRBORNE_FALL);

        for (int tick = 0; tick < 120 && repressed.entities[0].isGrounded == 0; ++tick)
            repressed = Step(repressed, {}, "wait for landing");
        CHECK(repressed.entities[0].isGrounded != 0);
        CHECK(repressed.entities[0].airborneState == SIM_AIRBORNE_GROUNDED);
    }

    Sim_Reset();
    data beforeMoveJump = Get_data(Input{});
    data moveJump = Step(beforeMoveJump, {2, 5}, "Right + Jump configured input advances once");
    CHECK(moveJump.entities[0].flags.hasCharacterIntent != 0);
    CHECK(moveJump.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::Jump));
    CHECK(moveJump.entities[0].characterIntent.moveX > 0.0f);
    CHECK(moveJump.entities[1].entityId == GameConfig::OPPONENT_1);

    Sim_Shutdown();
}

bool RunConfiguredSpikeScenario(const char* mappingPath, uint32_t seed, uint32_t spikeButtonId,
                                int32_t expectedActionState, const char* name)
{
    if (!Sim_Initialize(mappingPath)) return false;
    Sim_Reset();
    Sim_SeedRandom(seed);
    data observed = Get_data(Input{});
    bool toggle = false;

    for (int tick = 0; tick < 2400; ++tick)
    {
        toggle = !toggle;
        const auto& player = observed.entities[0];
        const auto& ball = observed.entities[2];
        const int stateFrame = ball.ballGameplayState.stateFrame;
        uint32_t moveId = 0;
        uint32_t actionId = 0;

        if (stateFrame == 0 && observed.matchState.servingTeam == 0)
        {
            if (toggle) actionId = 4; // configured Serve
        }
        else if (stateFrame == -1 && ball.ballTrajectory.speed < 0.0f &&
                 ball.ballGameplayState.landingX > 0.0f &&
                 ball.ballGameplayState.landingX < BigRect::NET_X)
        {
            const float playerCenterX = player.position.position.x + player.size.size.x * 0.5f;
            const float playerCenterY = player.position.position.y + player.size.size.y * 0.5f;
            const float ballCenterX = ball.position.position.x + MatchObjectConfig::BallSize.x * 0.5f;
            const float ballCenterY = ball.position.position.y + MatchObjectConfig::BallSize.y * 0.5f;
            const float dx = (ball.ballGameplayState.landingX + MatchObjectConfig::BallSize.x * 0.5f) - playerCenterX;
            const float distance = std::hypot(ballCenterX - playerCenterX, ballCenterY - playerCenterY);
            const float playerMoveStep = 7.0f * (DefaultCharacterStats::kTable[GameConfig::PLAYER].Speed /
                                                 DefaultCharacterStats::kReferenceBaseSpeed[GameConfig::PLAYER]);
            if (dx > playerMoveStep) moveId = 2;       // configured Right
            else if (dx < -playerMoveStep) moveId = 1; // configured Left
            if (distance < player.size.size.y * 0.5f) actionId = spikeButtonId;
        }

        data next;
        if (moveId != 0 && actionId != 0)
            next = Get_data(InputWith({moveId, actionId}));
        else if (moveId != 0)
            next = Get_data(InputWith({moveId}));
        else if (actionId != 0)
            next = Get_data(InputWith({actionId}));
        else
            next = Get_data(Input{});

        if (next.status != SIM_OK || next.entityCount != 4 || next.tick != observed.tick + 1 ||
            next.entities[0].entityId != GameConfig::PLAYER ||
            next.entities[1].entityId != GameConfig::OPPONENT_1)
        {
            std::printf("[FAIL] %s: Get_data snapshot/tick/entity contract violated at loop tick %d\n", name, tick);
            Sim_Shutdown();
            return false;
        }

        const bool trajectoryChanged =
            std::fabs(next.entities[2].ballTrajectory.speed - observed.entities[2].ballTrajectory.speed) > 0.001f ||
            next.entities[2].ballTrajectory.type != observed.entities[2].ballTrajectory.type;
        const bool accepted = next.entities[0].actionState.status == expectedActionState &&
                              next.rallyState.lastTouch == GameConfig::PLAYER && trajectoryChanged;
        if (accepted)
        {
            std::printf("[PASS] %s: configured ID %u accepted at native tick %llu; action state %d, lastTouch %u\n",
                        name, spikeButtonId, static_cast<unsigned long long>(next.tick),
                        next.entities[0].actionState.status, next.rallyState.lastTouch);
            Sim_Shutdown();
            return true;
        }
        observed = next;
    }

    std::printf("[BLOCKED] %s: no accepted action through configured ID %u in 1800 natural Get_data ticks\n",
                name, spikeButtonId);
    Sim_Shutdown();
    return false;
}

bool RunConfiguredBumpScenario(const char* mappingPath)
{
    if (!Sim_Initialize(mappingPath)) return false;
    Sim_Reset();
    Sim_SeedRandom(81u);
    data observed = Get_data(Input{});
    bool toggle = false;

    for (int tick = 0; tick < 2400; ++tick)
    {
        toggle = !toggle;
        const auto& player = observed.entities[0];
        const auto& ball = observed.entities[2];
        uint32_t moveId = 0;
        uint32_t actionId = 0;

        if (ball.ballGameplayState.stateFrame == BallStateFrame::Reset &&
            observed.matchState.servingTeam == 0 && toggle)
        {
            actionId = 4; // configured Serve
        }
        else if (ball.ballGameplayState.stateFrame == BallStateFrame::Alive &&
                 ball.ballGameplayState.landingX + MatchObjectConfig::BallSize.x * 0.5f < BigRect::NET_X)
        {
            const float playerCenterX = player.position.position.x + player.size.size.x * 0.5f;
            const float landingCenterX = ball.ballGameplayState.landingX + MatchObjectConfig::BallSize.x * 0.5f;
            const float playerMoveStep = 7.0f * (DefaultCharacterStats::kTable[GameConfig::PLAYER].Speed /
                                                 DefaultCharacterStats::kReferenceBaseSpeed[GameConfig::PLAYER]);
            if (landingCenterX - playerCenterX > playerMoveStep) moveId = 2;       // configured Right
            else if (landingCenterX - playerCenterX < -playerMoveStep) moveId = 1; // configured Left
            actionId = 3; // configured Bump is held while the ball approaches the player side
        }

        data next;
        if (moveId != 0 && actionId != 0)
            next = Get_data(InputWith({moveId, actionId}));
        else if (moveId != 0)
            next = Get_data(InputWith({moveId}));
        else if (actionId != 0)
            next = Get_data(InputWith({actionId}));
        else
            next = Get_data(Input{});

        if (next.status != SIM_OK || next.entityCount != 4 || next.tick != observed.tick + 1 ||
            next.entities[0].entityId != GameConfig::PLAYER ||
            next.entities[1].entityId != GameConfig::OPPONENT_1)
        {
            std::printf("[FAIL] configured Bump: Get_data snapshot/tick/entity contract violated at tick %d\n", tick);
            Sim_Shutdown();
            return false;
        }

        const auto& beforeTrajectory = ball.ballTrajectory;
        const auto& afterTrajectory = next.entities[2].ballTrajectory;
        const bool trajectoryChanged =
            std::fabs(afterTrajectory.speed - beforeTrajectory.speed) > 0.001f ||
            afterTrajectory.type != beforeTrajectory.type;
        const bool accepted = next.entities[0].actionState.status == SIM_ACTION_BUMP &&
                              next.rallyState.lastTouch == GameConfig::PLAYER && trajectoryChanged;
        if (accepted)
        {
            std::printf("[PASS] configured Bump: ID 3 accepted at native tick %llu; action state %d, lastTouch %u\n",
                        static_cast<unsigned long long>(next.tick), next.entities[0].actionState.status,
                        next.rallyState.lastTouch);
            Sim_Shutdown();
            return true;
        }
        observed = next;
    }

    std::puts("[BLOCKED] configured Bump: no accepted action through Reset -> Serve -> Alive fixture in 1200 ticks");
    Sim_Shutdown();
    return false;
}

bool TestConfiguredInvalidActiveBallActions(const char* mappingPath)
{
    if (!Sim_Initialize(mappingPath)) return false;
    constexpr std::array<uint32_t, 4> ids = {3, 6, 7, 8};
    constexpr std::array<int32_t, 4> intents = {
        static_cast<int32_t>(FinalIntent::Bump),
        static_cast<int32_t>(FinalIntent::SpikeLight),
        static_cast<int32_t>(FinalIntent::SpikeMedium),
        static_cast<int32_t>(FinalIntent::SpikeStrong)};
    constexpr std::array<int32_t, 4> states = {
        SIM_ACTION_BUMP,
        SIM_ACTION_SPIKE_LIGHT, SIM_ACTION_SPIKE_MEDIUM, SIM_ACTION_SPIKE_STRONG};
    constexpr std::array<const char*, 4> names = {
        "Bump", "SpikeLight", "SpikeMedium", "SpikeStrong"};

    bool allRejected = true;
    for (size_t i = 0; i < ids.size(); ++i)
    {
        Sim_Reset();
        data before = Get_data(Input{});
        if (before.entities[2].ballGameplayState.stateFrame != 0)
        {
            std::printf("[BLOCKED] Cannot classify initial-state Spike request invalid; ball stateFrame=%d\n",
                        before.entities[2].ballGameplayState.stateFrame);
            allRejected = false;
            continue;
        }
        data attempted = Get_data(InputWith({ids[i]}));
        const bool oneTick = attempted.tick == before.tick + 1 && attempted.status == SIM_OK;
        // All 4 grounded actions (Bump=3, SpikeLight=6, SpikeMedium=7, SpikeStrong=8) are accepted as intents when remainTime == 0:
        // - Bump (id 3) in Reset (servingTeam == LEFT) generates PrepareAttackServe (or Bump) without mutating ball position/speed or setting SIM_ACTION_BUMP/cooldown.
        // - SpikeLight/Medium/Strong (ids 6/7/8) trigger grounded character action state + cooldown + animId without mutating ball movement or rallyState!
        const bool requestObserved =
            attempted.entities[0].flags.hasCharacterIntent != 0 &&
            (attempted.entities[0].characterIntent.finalIntent == intents[i] ||
             (ids[i] == 3 && attempted.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::PrepareAttackServe)));
        const SimEntityData& beforeBall = before.entities[2];
        const SimEntityData& afterBall = attempted.entities[2];
        const auto& beforeTrajectory = beforeBall.ballTrajectory;
        const auto& afterTrajectory = afterBall.ballTrajectory;
        const auto& beforeGameplay = beforeBall.ballGameplayState;
        const auto& afterGameplay = afterBall.ballGameplayState;
        const bool trajectoryParamsUnchangedOrPrepared =
            (ids[i] == 3)
                ? (afterTrajectory.a >= beforeTrajectory.a && afterTrajectory.c >= beforeTrajectory.c &&
                   afterGameplay.landingX >= beforeGameplay.landingX)
                : (std::fabs(afterTrajectory.a - beforeTrajectory.a) <= 0.001f &&
                   std::fabs(afterTrajectory.b - beforeTrajectory.b) <= 0.001f &&
                   std::fabs(afterTrajectory.c - beforeTrajectory.c) <= 0.001f &&
                   std::fabs(afterGameplay.landingX - beforeGameplay.landingX) <= 0.001f);
        const bool movementUnchanged =
            std::fabs(afterBall.position.position.x - beforeBall.position.position.x) <= 0.001f &&
            std::fabs(afterBall.position.position.y - beforeBall.position.position.y) <= 0.001f &&
            afterTrajectory.type == beforeTrajectory.type &&
            std::fabs(afterTrajectory.speed - beforeTrajectory.speed) <= 0.001f &&
            trajectoryParamsUnchangedOrPrepared &&
            afterGameplay.stateFrame == beforeGameplay.stateFrame &&
            afterGameplay.collisionState == beforeGameplay.collisionState &&
            std::fabs(attempted.entities[0].position.position.x - before.entities[0].position.position.x) <= 0.001f &&
            std::fabs(attempted.entities[0].position.position.y - before.entities[0].position.position.y) <= 0.001f &&
            std::fabs(attempted.entities[0].characterIntent.moveX - before.entities[0].characterIntent.moveX) <= 0.001f &&
            std::fabs(attempted.entities[0].velocityIntent.vx - before.entities[0].velocityIntent.vx) <= 0.001f &&
            std::fabs(attempted.entities[0].velocityIntent.vy - before.entities[0].velocityIntent.vy) <= 0.001f;
        const bool expectedStateOutcome =
            (ids[i] == 3)
                ? (attempted.entities[0].actionState.status != states[i] &&
                   attempted.entities[0].characterPresentation.cooldownRemainMs == 0.0f)
                : (attempted.entities[0].actionState.status == states[i] &&
                   attempted.entities[0].characterPresentation.cooldownRemainMs > 0.0f);
        const bool rejectedBallMutation =
            expectedStateOutcome &&
            attempted.rallyState.lastTouch != GameConfig::PLAYER &&
            movementUnchanged;
        const bool ok = oneTick && requestObserved && rejectedBallMutation;
        Check(ok, "configured initial-state Bump/Spike request obeys grounded cooldown/action rules without ball movement mutation", __LINE__);
        if (!movementUnchanged)
        {
            std::printf("[FAIL] initial Reset %s mutated movement: playerPos=(%.4f,%.4f)->(%.4f,%.4f) "
                        "playerMoveX=%.4f->%.4f ballPos=(%.4f,%.4f)->(%.4f,%.4f) "
                        "trajectory(type=%d speed=%.4f a=%.4f b=%.4f c=%.4f landingX=%.4f) -> "
                        "(type=%d speed=%.4f a=%.4f b=%.4f c=%.4f landingX=%.4f)\n",
                        names[i], before.entities[0].position.position.x, before.entities[0].position.position.y,
                        attempted.entities[0].position.position.x, attempted.entities[0].position.position.y,
                        before.entities[0].characterIntent.moveX, attempted.entities[0].characterIntent.moveX,
                        beforeBall.position.position.x, beforeBall.position.position.y,
                        afterBall.position.position.x, afterBall.position.position.y,
                        beforeTrajectory.type, beforeTrajectory.speed, beforeTrajectory.a, beforeTrajectory.b,
                        beforeTrajectory.c, beforeGameplay.landingX,
                        afterTrajectory.type, afterTrajectory.speed, afterTrajectory.a, afterTrajectory.b,
                        afterTrajectory.c, afterGameplay.landingX);
        }
        allRejected &= ok;
    }
    Sim_Shutdown();
    return allRejected;
}

int TestConfiguredSpikeAcceptance(const char* mappingPath)
{
    if (mappingPath == nullptr || mappingPath[0] == '\0')
    {
        std::puts("[BLOCKED] Spike integration needs the actual Unity button_mapping.json path.");
        return 77;
    }

    const bool invalidRejected = TestConfiguredInvalidActiveBallActions(mappingPath);
    if (!invalidRejected)
        std::puts("[FAIL] One or more initial-state invalid Bump/Spike attempts mutated state or were accepted.");
    const bool light = RunConfiguredSpikeScenario(mappingPath, 71, 6, SIM_ACTION_SPIKE_LIGHT, "Spike Light");
    const bool medium = RunConfiguredSpikeScenario(mappingPath, 72, 7, SIM_ACTION_SPIKE_MEDIUM, "Spike Medium");
    const bool strong = RunConfiguredSpikeScenario(mappingPath, 73, 8, SIM_ACTION_SPIKE_STRONG, "Spike Strong");
    const bool bump = RunConfiguredBumpScenario(mappingPath);
    return !invalidRejected ? 1 : (light && medium && strong && bump ? 0 : 77); // CTest marks missing valid-action fixtures as skipped.
}
}

void TestAuthoritativeAnimAndCooldownGetDataContract()
{
    // Verify ABI sizes & exact zero-shift byte offsets in SimEntityData (156B) and data (4772B)
    CHECK(sizeof(Input) == 36u);
    CHECK(sizeof(SimCharacterPresentationData) == 20u);
    CHECK(sizeof(SimEntityData) == 156u);
    CHECK(sizeof(data) == 4772u);
    CHECK(offsetof(SimEntityData, ballTrajectory) == 124u);
    CHECK(offsetof(SimEntityData, characterPresentation) == 124u);
    CHECK(offsetof(SimCharacterPresentationData, animId) == 0u);
    CHECK(offsetof(SimCharacterPresentationData, cooldownAction) == 4u);
    CHECK(offsetof(SimCharacterPresentationData, cooldownRemainMs) == 8u);
    CHECK(offsetof(SimCharacterPresentationData, cooldownDurationMs) == 12u);
    CHECK(offsetof(SimEntityData, ballGameplayState) == 144u);

    Sim_Shutdown();
    CHECK(Sim_Initialize(nullptr));
    CHECK(InputMapping::GetInstance().LoadFromString(kFormatB));
    Sim_Reset();

    ComponentStorage defaultStorage;
    defaultStorage.InitComponent();
    const float playerJumpStat = defaultStorage.GetCharacterJumpingStat(GameConfig::PLAYER);
    const float expectedSpikeCooldownMs = ActionStateInfo::ComputeCooldownMsForJumpStat(ActionState::SpikeLight, playerJumpStat);

    // 1. Idle & Move animId on ground
    data s0 = Get_data(Input{});
    CHECK(s0.entities[0].characterPresentation.animId == SIM_ANIM_IDLE);
    CHECK(s0.entities[0].characterPresentation.cooldownAction == SIM_ACTION_NONE);
    CHECK(s0.entities[0].characterPresentation.cooldownRemainMs == 0.0f);
    CHECK(s0.entities[0].characterPresentation.cooldownDurationMs == 0.0f);

    data sMove = Get_data(InputWith({2})); // MoveRight
    CHECK(sMove.entities[0].characterPresentation.animId == SIM_ANIM_MOVE);
    CHECK(sMove.entities[0].characterPresentation.cooldownRemainMs == 0.0f);

    // 2. Grounded SpikeLight / SpikeMedium / SpikeStrong with remainTime == 0 ARE accepted,
    //    emit SIM_ANIM_SPIKE_LIGHT / MEDIUM / STRONG, set ~450ms cooldown, and block spam while remainTime > 0!
    struct GroundSpikeCase
    {
        uint32_t btnId;
        int32_t expectedAct;
        int32_t expectedAnim;
    };
    constexpr std::array<GroundSpikeCase, 3> groundSpikeCases = {{
        {6u, SIM_ACTION_SPIKE_LIGHT, SIM_ANIM_SPIKE_LIGHT},
        {7u, SIM_ACTION_SPIKE_MEDIUM, SIM_ANIM_SPIKE_MEDIUM},
        {8u, SIM_ACTION_SPIKE_STRONG, SIM_ANIM_SPIKE_STRONG},
    }};
    for (const auto& tc : groundSpikeCases)
    {
        Sim_Reset();
        (void)Get_data(Input{});
        data hit = Get_data(InputWith({tc.btnId}));
        CHECK(hit.entities[0].isGrounded != 0);
        CHECK(hit.entities[0].actionState.status == tc.expectedAct);
        CHECK(hit.entities[0].characterPresentation.animId == tc.expectedAnim);
        CHECK(hit.entities[0].characterPresentation.cooldownAction == tc.expectedAct);
        CHECK(std::fabs(hit.entities[0].characterPresentation.cooldownRemainMs - expectedSpikeCooldownMs) < 1e-3f);
        CHECK(std::fabs(hit.entities[0].characterPresentation.cooldownDurationMs - expectedSpikeCooldownMs) < 1e-3f);

        // Next tick with release then immediate re-press while remainTime > 0: MUST be blocked!
        data rel = Get_data(Input{});
        CHECK(rel.entities[0].characterPresentation.cooldownRemainMs > 0.0f);
        data blocked = Get_data(InputWith({tc.btnId}));
        CHECK(blocked.entities[0].flags.hasCharacterIntent == 0 ||
              blocked.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::None));
    }

    // 3. Serve sets 0 cooldown and SIM_ANIM_SERVE on Serve tick, then clears to IDLE on next tick!
    Sim_Reset();
    data preServe = Get_data(Input{});
    CHECK(preServe.entities[2].ballGameplayState.stateFrame == BallStateFrame::Reset);
    data served = Get_data(InputWith({4})); // Serve
    CHECK(served.entities[0].actionState.status == SIM_ACTION_SERVE);
    CHECK(served.entities[0].characterPresentation.animId == SIM_ANIM_SERVE);
    CHECK(served.entities[0].characterPresentation.cooldownAction == SIM_ACTION_NONE);
    CHECK(served.entities[0].characterPresentation.cooldownRemainMs == 0.0f);
    CHECK(served.entities[0].characterPresentation.cooldownDurationMs == 0.0f);

    data afterServe = Get_data(Input{});
    CHECK(afterServe.entities[0].actionState.status == SIM_ACTION_NONE);
    CHECK(afterServe.entities[0].characterPresentation.animId == SIM_ANIM_IDLE);
    CHECK(afterServe.entities[0].characterPresentation.cooldownRemainMs == 0.0f);

    // 4. Bump whiff (when ball has traveled far out of bumpRadius) sets 0 cooldown and NEVER Slide,
    //    whereas Bump on actual ball contact sets SIM_ANIM_BUMP and ~300ms cooldown!
    data farBall = afterServe;
    for (int t = 0; t < 25; ++t)
    {
        farBall = Get_data(Input{});
    }
    data bumpWhiff = Get_data(InputWith({3})); // Hold Bump when ball is far away across court
    CHECK(bumpWhiff.entities[0].actionState.status == SIM_ACTION_NONE);
    CHECK(bumpWhiff.entities[0].characterPresentation.cooldownAction == SIM_ACTION_NONE);
    CHECK(bumpWhiff.entities[0].characterPresentation.cooldownRemainMs == 0.0f);
    CHECK(bumpWhiff.entities[0].characterPresentation.cooldownDurationMs == 0.0f);
    CHECK(bumpWhiff.entities[0].actionState.status != SIM_ACTION_SLIDE);

    // Verify Bump actual ball contact sets SIM_ANIM_BUMP and 300ms skill cooldown:
    Sim_Reset();
    (void)Get_data(Input{});
    (void)Get_data(InputWith({4})); // Serve launches ball right next to PLAYER
    data bumpContact = Get_data(InputWith({3})); // Bump while ball is within PLAYER bumpRadius
    const float expectedBumpCooldownMs = ActionStateInfo::ComputeCooldownMsForJumpStat(ActionState::Bump, playerJumpStat);
    CHECK(bumpContact.entities[0].actionState.status == SIM_ACTION_BUMP);
    CHECK(bumpContact.entities[0].characterPresentation.animId == SIM_ANIM_BUMP);
    CHECK(bumpContact.entities[0].characterPresentation.cooldownAction == SIM_ACTION_BUMP);
    CHECK(std::fabs(bumpContact.entities[0].characterPresentation.cooldownRemainMs - expectedBumpCooldownMs) < 1e-3f);
    CHECK(std::fabs(bumpContact.entities[0].characterPresentation.cooldownDurationMs - expectedBumpCooldownMs) < 1e-3f);

    // 5. Airborne Jump -> <80% ascent blocked (0 skill cooldown reported) -> >=80% ascent accepted -> Fall animId!
    Sim_Reset();
    (void)Get_data(Input{});
    data jumpTick = Get_data(InputWith({5})); // Jump
    CHECK(jumpTick.entities[0].jumpFrame.remainingFrames > 0);
    CHECK(jumpTick.entities[0].characterPresentation.animId == SIM_ANIM_JUMP_UP);
    // Plain Jump ascent lock must NOT report skill button cooldown:
    CHECK(jumpTick.entities[0].characterPresentation.cooldownAction == SIM_ACTION_NONE);
    CHECK(jumpTick.entities[0].characterPresentation.cooldownRemainMs == 0.0f);
    CHECK(jumpTick.entities[0].characterPresentation.cooldownDurationMs == 0.0f);

    data airEarly = Get_data(Input{}); // release
    CHECK(airEarly.entities[0].isGrounded == 0);
    // Below 80% jump ascent: SpikeStrong (8) is blocked
    data airBlocked = Get_data(InputWith({8}));
    CHECK(airBlocked.entities[0].flags.hasCharacterIntent == 0 ||
          airBlocked.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::None));
    airEarly = Get_data(Input{});

    // Advance until Jump ascent cooldown ends AND >= 80% height (remainingFrames <= 3)
    while (airEarly.entities[0].actionState.remainTime > 0.0f || airEarly.entities[0].jumpFrame.remainingFrames > 3)
    {
        airEarly = Get_data(Input{});
    }
    CHECK(airEarly.entities[0].isGrounded == 0);
    data airAccepted = Get_data(InputWith({8})); // SpikeStrong in mid-air >= 80%
    CHECK(airAccepted.entities[0].actionState.status == SIM_ACTION_SPIKE_STRONG);
    CHECK(airAccepted.entities[0].characterPresentation.animId == SIM_ANIM_SPIKE_STRONG);
    CHECK(airAccepted.entities[0].characterPresentation.cooldownAction == SIM_ACTION_SPIKE_STRONG);
    CHECK(std::fabs(airAccepted.entities[0].characterPresentation.cooldownRemainMs - expectedSpikeCooldownMs) < 1e-3f);
    CHECK(std::fabs(airAccepted.entities[0].characterPresentation.cooldownDurationMs - expectedSpikeCooldownMs) < 1e-3f);

    // Verify SIM_ANIM_FALL during pure jump descent after JumpUp frames finish
    Sim_Reset();
    (void)Get_data(Input{});
    data pureJump = Get_data(InputWith({5}));
    while (pureJump.entities[0].jumpFrame.remainingFrames > 0 || pureJump.entities[0].actionState.remainTime > 0.0f)
    {
        pureJump = Get_data(Input{});
    }
    CHECK(pureJump.entities[0].isGrounded == 0);
    CHECK(pureJump.entities[0].airborneState == SIM_AIRBORNE_FALL);
    CHECK(pureJump.entities[0].characterPresentation.animId == SIM_ANIM_FALL);

    // 6. End-to-End Get_data Hold-Bump Attack Serve Contract (PrepareAttackServe == 13 -> LaunchAttackServe == 14):
    //    Verify entities[2] (entityId == 7 == BALL) exposes ballTrajectory.{a,b,c} and landingX during Reset hold,
    //    and releasing Bump via Get_data(Input{}) transitions ball to Alive (-1) with speed > 0 and touchCount == 0.
    Sim_Reset();
    data serveReset0 = Get_data(Input{});
    CHECK(serveReset0.entities[2].entityId == GameConfig::BALL);
    CHECK(serveReset0.entities[2].ballGameplayState.stateFrame == BallStateFrame::Reset);
    CHECK(std::fabs(serveReset0.entities[2].ballTrajectory.a - SystemConfig::NormalizeAttackServeA(SystemConfig::ATTACK_SERVE_BASE_A)) < 1e-6f);
    CHECK(std::fabs(serveReset0.entities[2].ballTrajectory.c - SystemConfig::ATTACK_SERVE_BASE_C) < 1e-4f);

    data serveHold = serveReset0;
    for (int h = 0; h < 10; ++h)
    {
        serveHold = Get_data(InputWith({3})); // Hold Bump (button 3)
        CHECK(serveHold.entities[0].flags.hasCharacterIntent != 0);
        CHECK(serveHold.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::PrepareAttackServe));
        CHECK(serveHold.entities[2].ballGameplayState.stateFrame == BallStateFrame::Reset);
        CHECK(serveHold.entities[2].ballTrajectory.speed == 0.0f);
    }
    CHECK(serveHold.entities[2].ballTrajectory.a > serveReset0.entities[2].ballTrajectory.a);
    CHECK(serveHold.entities[2].ballTrajectory.c > serveReset0.entities[2].ballTrajectory.c);
    CHECK(serveHold.entities[2].ballGameplayState.landingX > serveReset0.entities[2].ballGameplayState.landingX);

    data serveLaunch = Get_data(Input{}); // Release Bump
    CHECK(serveLaunch.entities[0].flags.hasCharacterIntent != 0);
    CHECK(serveLaunch.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::LaunchAttackServe));
    CHECK(serveLaunch.entities[2].ballGameplayState.stateFrame == BallStateFrame::Alive);
    CHECK(serveLaunch.entities[2].ballTrajectory.speed > 0.0f);
    CHECK(serveLaunch.rallyState.touchCount == 0u);

    // 7. Direct RAM Pointer Contract (Sim_GetAttackServeConfigPtr, 24 bytes = 6 floats: baseA, maxA, baseC, maxC, totalStep, tossV0) & Starting Stats Contract:
    CHECK(sizeof(SystemConfig::SimAttackServeConfig) == 24u);
    auto* cfgPtr = Sim_GetAttackServeConfigPtr();
    CHECK(cfgPtr == &SystemConfig::g_attackServeConfig);
    if (cfgPtr != nullptr)
    {
        const auto origCfg = *cfgPtr;
        // Verify entering -3 and -2 (x10^-3 scale) normalizes to -0.0030f and -0.0020f and increments by (max - base) / totalStep:
        cfgPtr->baseA     = -3.0f;
        cfgPtr->maxA      = -2.0f;
        cfgPtr->baseC     = 450.0f;
        cfgPtr->maxC      = 550.0f;
        cfgPtr->totalStep = 10.0f;
        Sim_Reset();
        data liveTuned = Get_data(Input{});
        CHECK(std::fabs(liveTuned.entities[2].ballTrajectory.a - (-0.0030f)) < 1e-6f);
        CHECK(std::fabs(liveTuned.entities[2].ballTrajectory.c - 450.0f) < 1e-4f);
        data step1 = Get_data(InputWith({3})); // Hold Bump 1 step: +0.0001f on a, +10.0f on c
        CHECK(std::fabs(step1.entities[2].ballTrajectory.a - (-0.0029f)) < 1e-6f);
        CHECK(std::fabs(step1.entities[2].ballTrajectory.c - 460.0f) < 1e-4f);
        *cfgPtr = origCfg;
    }

    float pAtk = 0.0f, pDef = 0.0f, pSpd = 0.0f, pJmp = 0.0f;
    float oAtk = 0.0f, oDef = 0.0f, oSpd = 0.0f, oJmp = 0.0f;
    CHECK(Sim_ResetCharacterStats() == SIM_OK);
    CHECK(Sim_GetCharacterStats(GameConfig::PLAYER, &pAtk, &pDef, &pSpd, &pJmp) == SIM_OK);
    CHECK(pAtk == 300.0f && pSpd == 300.0f && pJmp == 300.0f);
    CHECK(Sim_GetCharacterStats(GameConfig::OPPONENT_1, &oAtk, &oDef, &oSpd, &oJmp) == SIM_OK);
    CHECK(oAtk == 20.0f);

    Sim_Shutdown();
}

int main(int argc, char** argv)
{
    InputMapping& mapping = InputMapping::GetInstance();
    TestDefaultsAndConfiguredMapping(mapping);
    TestIndependentJump(mapping);
    TestContextualJumpSpikeAndConflict(mapping);
    TestPriorityMovementAndCooldown(mapping);
    TestPressHoldReleaseRepressContract(mapping);
    TestAuthoritativeAnimAndCooldownGetDataContract();
    CHECK(mapping.LoadFromString(kFormatB));
    if (argc >= 3 && std::string(argv[1]) == "--get-data")
        TestConfiguredGetDataJump(argv[2]);
    else if (argc >= 3 && std::string(argv[1]) == "--spikes")
    {
        const int spikeResult = TestConfiguredSpikeAcceptance(argv[2]);
        if (failures != 0)
            std::printf("P02InputContractTest failed before spike integration: %d assertion(s)\n", failures);
        return failures != 0 ? 1 : spikeResult;
    }
    else
        TestTrajectoryAcceptanceBlocker();
    if (failures != 0)
        std::printf("P02InputContractTest failed: %d assertion(s)\n", failures);
    else
        std::puts("P02InputContractTest local contract assertions passed; trajectory acceptance remains BLOCKED.");
    return failures == 0 ? 0 : 1;
}
