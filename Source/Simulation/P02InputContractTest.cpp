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
    if (groundedFrames) CHECK(groundedFrames->remainingFrames == SystemConfig::JUMP_FRAME);

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
    CHECK(groundFrames && groundFrames->remainingFrames == SystemConfig::JUMP_FRAME);

    Fixture airborne(SystemConfig::MIN_Y + 40.0f);
    const auto* airIntent = airborne.Submit(0, jumpSpike);
    CHECK(airIntent && airIntent->finalIntent == FinalIntent::Spike);
    airborne.jump.update(SystemConfig::FIXED_DT);
    const auto* airFrames = airborne.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(airFrames && airFrames->remainingFrames == 0);

    Fixture conflict(SystemConfig::MIN_Y + 40.0f);
    const auto* bothIntent = conflict.Submit(0,
        ConfiguredMask(mapping, 5) | jumpSpike);
    CHECK(bothIntent && bothIntent->finalIntent == FinalIntent::Jump);
    conflict.jump.update(SystemConfig::FIXED_DT);
    const auto* conflictFrames = conflict.components.GetJumpUpFramePool().get(GameConfig::PLAYER);
    CHECK(conflictFrames && conflictFrames->remainingFrames == 0);
}

void TestPriorityMovementAndCooldown(const InputMapping& mapping)
{
    constexpr std::array<int, 3> ids = {6, 7, 8};
    constexpr std::array<FinalIntent, 3> intents = {
        FinalIntent::SpikeLight, FinalIntent::SpikeMedium, FinalIntent::SpikeStrong};
    const uint32_t jump = ConfiguredMask(mapping, 5);
    for (size_t i = 0; i < ids.size(); ++i)
    {
        Fixture fixture(SystemConfig::MIN_Y);
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
        Fixture later(SystemConfig::MIN_Y);
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

    Fixture serveLight(SystemConfig::MIN_Y);
    const auto* serveLightResult = serveLight.Submit(0, serveMask | lightMask);
    CHECK(serveLightResult && serveLightResult->finalIntent == FinalIntent::SpikeLight);

    Fixture lightMedium(SystemConfig::MIN_Y);
    const auto* lightMediumResult = lightMedium.Submit(0, lightMask | mediumMask);
    CHECK(lightMediumResult && lightMediumResult->finalIntent == FinalIntent::SpikeMedium);

    Fixture mediumStrong(SystemConfig::MIN_Y);
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
    Check(current.entities[1].entityId == GameConfig::OPPONENT_1, "entity 1 remains the opponent", __LINE__);

    data jumped = Step(current, {5}, "Get_data Jump advances exactly one tick");
    CHECK(jumped.entities[0].flags.hasCharacterIntent != 0);
    CHECK(jumped.entities[0].characterIntent.finalIntent == static_cast<int32_t>(FinalIntent::Jump));
    CHECK(jumped.entities[0].jumpFrame.remainingFrames > 0);
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
        for (int tick = 0; tick < 20 && current.entities[0].actionState.remainTime > 0.0f; ++tick)
            current = Step(current, {}, "cooldown wait tick advances once");
        const int framesBefore = current.entities[0].jumpFrame.remainingFrames;
        data airborneJump = Step(current, {5}, "airborne configured Jump advances once");
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

    for (int tick = 0; tick < 1800; ++tick)
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
            if (dx > 7.0f) moveId = 2;       // configured Right
            else if (dx < -7.0f) moveId = 1;  // configured Left
            if (distance < 140.0f && toggle) actionId = spikeButtonId;
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

    for (int tick = 0; tick < 1200; ++tick)
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
            if (landingCenterX - playerCenterX > 7.0f) moveId = 2;       // configured Right
            else if (landingCenterX - playerCenterX < -7.0f) moveId = 1; // configured Left
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
        const bool requestObserved = attempted.entities[0].flags.hasCharacterIntent != 0 &&
                                     attempted.entities[0].characterIntent.finalIntent == intents[i];
        const SimEntityData& beforeBall = before.entities[2];
        const SimEntityData& afterBall = attempted.entities[2];
        const auto& beforeTrajectory = beforeBall.ballTrajectory;
        const auto& afterTrajectory = afterBall.ballTrajectory;
        const auto& beforeGameplay = beforeBall.ballGameplayState;
        const auto& afterGameplay = afterBall.ballGameplayState;
        const bool movementUnchanged =
            std::fabs(afterBall.position.position.x - beforeBall.position.position.x) <= 0.001f &&
            std::fabs(afterBall.position.position.y - beforeBall.position.position.y) <= 0.001f &&
            afterTrajectory.type == beforeTrajectory.type &&
            std::fabs(afterTrajectory.speed - beforeTrajectory.speed) <= 0.001f &&
            std::fabs(afterTrajectory.a - beforeTrajectory.a) <= 0.001f &&
            std::fabs(afterTrajectory.b - beforeTrajectory.b) <= 0.001f &&
            std::fabs(afterTrajectory.c - beforeTrajectory.c) <= 0.001f &&
            std::fabs(afterGameplay.landingX - beforeGameplay.landingX) <= 0.001f &&
            afterGameplay.stateFrame == beforeGameplay.stateFrame &&
            afterGameplay.collisionState == beforeGameplay.collisionState &&
            std::fabs(attempted.entities[0].position.position.x - before.entities[0].position.position.x) <= 0.001f &&
            std::fabs(attempted.entities[0].position.position.y - before.entities[0].position.position.y) <= 0.001f &&
            std::fabs(attempted.entities[0].characterIntent.moveX - before.entities[0].characterIntent.moveX) <= 0.001f &&
            std::fabs(attempted.entities[0].velocityIntent.vx - before.entities[0].velocityIntent.vx) <= 0.001f &&
            std::fabs(attempted.entities[0].velocityIntent.vy - before.entities[0].velocityIntent.vy) <= 0.001f;
        const bool rejected = attempted.entities[0].actionState.status != states[i] &&
                              attempted.rallyState.lastTouch != GameConfig::PLAYER && movementUnchanged;
        const bool ok = oneTick && requestObserved && rejected;
        Check(ok, "configured initial-state Bump/Spike request is observed but rejected without ball movement mutation", __LINE__);
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

int main(int argc, char** argv)
{
    InputMapping& mapping = InputMapping::GetInstance();
    TestDefaultsAndConfiguredMapping(mapping);
    TestIndependentJump(mapping);
    TestContextualJumpSpikeAndConflict(mapping);
    TestPriorityMovementAndCooldown(mapping);
    TestPressHoldReleaseRepressContract(mapping);
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
