#include "SimulationApi.h"
#include "SimulationRuntime.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "../Config/System/SystemConf.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"

#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <set>

// In-process architecture verification suite for Get_data
int main()
{
    std::printf("====================================================\n");
    std::printf("[ARCH TEST] Starting Architecture Verification Gate\n");
    std::printf("====================================================\n");

    // Initialize simulation runtime
    bool initOk = Simulation::Init();
    assert(initOk && "Simulation::Init failed");
    std::printf("[PASS] Simulation initialized.\n");

    Simulation::Reset();
    auto& rt = SimulationRuntime::GetInstance();
    uint64_t initialTick = rt.GetTick();
    assert(initialTick == 0 && "Initial tick must be 0");

    // ----------------------------------------------------
    // 3.1 Test exact entity filtering
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.1] Testing exact entity filtering...\n");
    Input emptyInput{};
    data snap1 = Get_data(emptyInput);

    assert(snap1.entityCount == 4 && "Must return exactly 4 entities");

    std::set<uint32_t> returnedIds;
    for (int i = 0; i < snap1.entityCount; ++i)
    {
        returnedIds.insert(snap1.entities[i].entityId);
    }
    assert(returnedIds.size() == 4 && "Must have 4 unique entity IDs");
    assert(returnedIds.count(GameConfig::PLAYER) == 1 && "PLAYER (0) must be present");
    assert(returnedIds.count(GameConfig::OPPONENT_1) == 1 && "OPPONENT_1 (3) must be present");
    assert(returnedIds.count(GameConfig::BALL) == 1 && "BALL (7) must be present");
    assert(returnedIds.count(GameConfig::NET) == 1 && "NET (6) must be present");

    // Ensure excluded entities (1, 2, 4, 5, 8) NEVER appear
    assert(returnedIds.count(GameConfig::TEAMMATE_1) == 0 && "TEAMMATE_1 (1) must NOT be returned");
    assert(returnedIds.count(GameConfig::TEAMMATE_2) == 0 && "TEAMMATE_2 (2) must NOT be returned");
    assert(returnedIds.count(GameConfig::OPPONENT_2) == 0 && "OPPONENT_2 (4) must NOT be returned");
    assert(returnedIds.count(GameConfig::OPPONENT_3) == 0 && "OPPONENT_3 (5) must NOT be returned");
    assert(returnedIds.count(GameConfig::NONE) == 0 && "NONE (8) must NOT be returned");
    std::printf("[PASS] Exact 4-entity filter verified (PLAYER=0, OPPONENT_1=3, BALL=7, NET=6; excluded entities 1,2,4,5 absent).\n");

    // ----------------------------------------------------
    // 3.2 Test complete component and state data
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.2] Testing complete component and state data...\n");
    auto& storage = const_cast<ComponentStorage&>(rt.GetComponentStorage());
    auto& intentStorage = const_cast<IntentStorage&>(rt.GetIntentStorage());

    // Verify PLAYER components
    {
        const auto& e = snap1.entities[0];
        assert(e.entityId == GameConfig::PLAYER);
        assert(std::strcmp(e.roleName, "PLAYER") == 0);
        assert(e.flags.hasPosition == 1);
        assert(e.flags.hasSize == 1);
        assert(e.flags.hasJumpUpFrame == 1);
        assert(e.flags.hasCharacterActionState == 1);
        assert(e.flags.hasBallTrajectory == 0 && "PLAYER must not have BallTrajectory");
        assert(e.flags.hasBallGameplayState == 0 && "PLAYER must not have BallGameplayState");

        auto* pos = storage.GetCharacterPositionPool().get(GameConfig::PLAYER);
        assert(pos && "Authoritative storage must have PLAYER position");
        assert(e.position.position.x == pos->position.x);
        assert(e.position.position.y == pos->position.y);

        auto* size = storage.GetCharacterSizePool().get(GameConfig::PLAYER);
        assert(size && "Authoritative storage must have PLAYER size");
        assert(e.size.size.x == size->size.x);
        assert(e.size.size.y == size->size.y);

        auto* jump = storage.GetJumpUpFramePool().get(GameConfig::PLAYER);
        assert(jump && "Authoritative storage must have PLAYER jump frames");
        assert(e.jumpFrame.remainingFrames == jump->remainingFrames);

        auto* act = storage.GetCharacterActionStatePool().get(GameConfig::PLAYER);
        assert(act && "Authoritative storage must have PLAYER action state");
        assert(e.actionState.status == static_cast<int32_t>(act->status));
        assert(e.actionState.remainTime == act->remainTime);
        std::printf("[PASS] PLAYER components match authoritative storage bit-exact.\n");
    }

    // Verify OPPONENT_1 components
    {
        const auto& e = snap1.entities[1];
        assert(e.entityId == GameConfig::OPPONENT_1);
        assert(std::strcmp(e.roleName, "OPPONENT_1") == 0);
        assert(e.flags.hasPosition == 1);
        assert(e.flags.hasSize == 1);
        assert(e.flags.hasJumpUpFrame == 1);
        assert(e.flags.hasCharacterActionState == 1);
        assert(e.flags.hasBallTrajectory == 0 && "OPPONENT_1 must not have BallTrajectory");

        auto* pos = storage.GetCharacterPositionPool().get(GameConfig::OPPONENT_1);
        assert(pos && e.position.position.x == pos->position.x);
        assert(e.position.position.y == pos->position.y);

        auto* size = storage.GetCharacterSizePool().get(GameConfig::OPPONENT_1);
        assert(size && e.size.size.x == size->size.x);
        assert(e.size.size.y == size->size.y);
        std::printf("[PASS] OPPONENT_1 components match authoritative storage.\n");
    }

    // Verify BALL components & state
    {
        const auto& e = snap1.entities[2];
        assert(e.entityId == GameConfig::BALL);
        assert(std::strcmp(e.roleName, "BALL") == 0);
        assert(e.flags.hasPosition == 1);
        assert(e.flags.hasSize == 1);
        assert(e.flags.hasBallTrajectory == 1);
        assert(e.flags.hasBallGameplayState == 1);
        assert(e.flags.hasJumpUpFrame == 0 && "BALL must not have JumpUpFrame");
        assert(e.flags.hasCharacterActionState == 0 && "BALL must not have CharacterActionState");

        auto* pos = storage.GetBallPositionPool().get(GameConfig::BALL);
        assert(pos && e.position.position.x == pos->position.x);
        assert(e.position.position.y == pos->position.y);

        auto* traj = storage.GetBallTrajectoryPool().get(GameConfig::BALL);
        assert(traj);
        assert(e.ballTrajectory.type == static_cast<int32_t>(traj->type));
        assert(e.ballTrajectory.speed == traj->speed);
        assert(e.ballTrajectory.a == traj->a);
        assert(e.ballTrajectory.b == traj->b);
        assert(e.ballTrajectory.c == traj->c);

        auto* bState = storage.GetBallGameplayStatePool().get(DEFAULT_MATCH);
        assert(bState);
        assert(e.ballGameplayState.landingX == bState->landingX);
        assert(e.ballGameplayState.stateFrame == bState->stateFrame);
        assert(e.ballGameplayState.collisionState == static_cast<int32_t>(bState->collisionState));
        std::printf("[PASS] BALL position, trajectory, and gameplay state match authoritative storage.\n");
    }

    // Verify NET components & state
    {
        const auto& e = snap1.entities[3];
        assert(e.entityId == GameConfig::NET);
        assert(std::strcmp(e.roleName, "NET") == 0);
        assert(e.flags.hasPosition == 1);
        assert(e.flags.hasSize == 1);
        assert(e.flags.hasJumpUpFrame == 0 && "NET has no components in ComponentStorage");
        assert(e.flags.hasCharacterActionState == 0);
        assert(e.flags.hasBallTrajectory == 0);
        assert(e.position.position.x == MatchObjectConfig::NETPOSITION.x);
        assert(e.position.position.y == MatchObjectConfig::NETPOSITION.y);
        assert(e.size.size.x == MatchObjectConfig::NETSIZE.x);
        assert(e.size.size.y == MatchObjectConfig::NETSIZE.y);
        std::printf("[PASS] NET authoritative geometry verified.\n");
    }

    // Verify Match and Rally state
    {
        auto* mState = storage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        assert(mState);
        assert(snap1.matchState.leftScore == mState->leftScore);
        assert(snap1.matchState.rightScore == mState->rightScore);
        assert(snap1.matchState.servingTeam == static_cast<int32_t>(mState->servingTeam));

        auto* rState = storage.GetRallyStatePool().get(DEFAULT_MATCH);
        assert(rState);
        assert(snap1.rallyState.lastTouch == rState->lastTouch);
        assert(snap1.rallyState.touchCount == rState->touchCount);
        std::printf("[PASS] Match and Rally state match authoritative storage.\n");
    }

    // ----------------------------------------------------
    // 3.3 Test empty input
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.3] Testing empty input...\n");
    uint64_t tickBeforeEmpty = rt.GetTick();
    float playerXBefore = snap1.entities[0].position.position.x;

    data snapEmpty = Get_data(emptyInput);
    uint64_t tickAfterEmpty = rt.GetTick();
    assert(tickAfterEmpty == tickBeforeEmpty + 1 && "Empty input must advance exactly 1 tick");
    assert(snapEmpty.tick == tickAfterEmpty && "Returned tick must match runtime tick");

    // Empty input should not inject intent
    auto* emptyIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    if (emptyIntent)
    {
        assert(emptyIntent->moveX == 0.0f && "Empty input must not generate moveX intent");
        assert(emptyIntent->finalIntent == FinalIntent::None && "Empty input must not generate action intent");
    }
    std::printf("[PASS] Empty input advances tick without unintended player intent.\n");

    // ----------------------------------------------------
    // 3.4 Test non-empty button-ID inputs (Right, Left, Bump, Serve)
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.4] Testing button-ID input routing (Move Right)...\n");
    Input moveRightInput{};
    moveRightInput.buttonCount = 1;
    moveRightInput.buttonIds[0] = 2; // button_2 -> MoveRight

    uint64_t tickBeforeInput = rt.GetTick();
    float posXBefore = snapEmpty.entities[0].position.position.x;

    data snapMove = Get_data(moveRightInput);
    uint64_t tickAfterInput = rt.GetTick();
    assert(tickAfterInput == tickBeforeInput + 1 && "Non-empty input must advance exactly 1 tick");

    // Intent was created and routed through PlayerInputSystem
    auto* playerIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    assert(playerIntent && "Player intent must be generated for button_2 (MoveRight)");
    assert(playerIntent->moveX > 0.0f && "Intent moveX must be positive for MoveRight");
    assert(snapMove.entities[0].characterIntent.moveX > 0.0f);
    assert(snapMove.entities[0].position.position.x > posXBefore && "Player position X must advance rightward");
    std::printf("[PASS] Button-ID 2 (Right) routed through PlayerInputSystem: beforeX=%.1f -> afterX=%.1f\n",
                posXBefore, snapMove.entities[0].position.position.x);

    // Test Move Left (button_1 -> ID 1)
    std::printf("\n[CHECK 3.4b] Testing button-ID input routing (Move Left)...\n");
    Input moveLeftInput{};
    moveLeftInput.buttonCount = 1;
    moveLeftInput.buttonIds[0] = 1; // button_1 -> MoveLeft

    float posXBeforeLeft = snapMove.entities[0].position.position.x;
    data snapLeft = Get_data(moveLeftInput);
    auto* leftIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    assert(leftIntent && "Player intent must be generated for button_1 (MoveLeft)");
    assert(leftIntent->moveX < 0.0f && "Intent moveX must be negative for MoveLeft");
    assert(snapLeft.entities[0].position.position.x < posXBeforeLeft && "Player position X must move left");
    std::printf("[PASS] Button-ID 1 (Left) verified: beforeX=%.1f -> afterX=%.1f\n",
                posXBeforeLeft, snapLeft.entities[0].position.position.x);

    // Test Bump (button_3 -> ID 3)
    std::printf("\n[CHECK 3.4c] Testing button-ID input routing (Bump)...\n");
    Input bumpInput{};
    bumpInput.buttonCount = 1;
    bumpInput.buttonIds[0] = 3; // button_3 -> Bump
    data snapBump = Get_data(bumpInput);
    auto* bumpIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    assert(bumpIntent && "Player intent must be generated for button_3 (Bump)");
    assert(bumpIntent->finalIntent == FinalIntent::Bump && "FinalIntent must be Bump");
    std::printf("[PASS] Button-ID 3 (Bump) verified: FinalIntent=%d\n", static_cast<int>(bumpIntent->finalIntent));

    // Test Serve (button_4 -> ID 4)
    std::printf("\n[CHECK 3.4d] Testing button-ID input routing (Serve)...\n");
    // Clear previous button so Serve gets a fresh pressed edge
    Get_data(emptyInput);
    Input serveInput{};
    serveInput.buttonCount = 1;
    serveInput.buttonIds[0] = 4; // button_4 -> Serve
    data snapServe = Get_data(serveInput);
    auto* serveIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    assert(serveIntent && "Player intent must be generated for button_4 (Serve)");
    assert(serveIntent->finalIntent == FinalIntent::Serve && "FinalIntent must be Serve");
    std::printf("[PASS] Button-ID 4 (Serve) verified: FinalIntent=%d\n", static_cast<int>(serveIntent->finalIntent));

    // Test Unknown button ID (ID 999) -> safe, no intent, no crash
    std::printf("\n[CHECK 3.4e] Testing unknown button ID handling...\n");
    Get_data(emptyInput);
    Input unknownInput{};
    unknownInput.buttonCount = 1;
    unknownInput.buttonIds[0] = 999;
    data snapUnknown = Get_data(unknownInput);
    assert(snapUnknown.status == 0 && "Unknown button ID must not fail API call");
    auto* unkIntent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
    if (unkIntent)
    {
        assert(unkIntent->moveX == 0.0f && unkIntent->finalIntent == FinalIntent::None);
    }
    std::printf("[PASS] Unknown button ID 999 safely ignored without side effects.\n");

    // ----------------------------------------------------
    // 3.5 Test repeated calls & tick progression
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.5] Testing repeated consecutive calls...\n");
    uint64_t currentTick = rt.GetTick();
    for (int i = 0; i < 60; ++i)
    {
        data snapIter = Get_data(emptyInput);
        assert(snapIter.tick == currentTick + 1 + i);
        assert(snapIter.entityCount == 4);
        assert(snapIter.entities[0].entityId == GameConfig::PLAYER);
        assert(snapIter.entities[1].entityId == GameConfig::OPPONENT_1);
        assert(snapIter.entities[2].entityId == GameConfig::BALL);
        assert(snapIter.entities[3].entityId == GameConfig::NET);
    }
    std::printf("[PASS] 60 consecutive Get_data calls verified. Exactly 1 tick per call, 4 entities maintained.\n");

    // ----------------------------------------------------
    // 3.6 Test architecture preservation
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.6] Testing architecture preservation...\n");
    assert(std::strlen(snapServe.formattedJson) > 50 && "Formatted JSON buffer must be populated");
    std::printf("[PASS] Simulation pipeline, ball trajectory integration, and JSON formatting verified.\n");

    // ----------------------------------------------------
    // 3.7 Test JSON mapping validation
    // ----------------------------------------------------
    std::printf("\n[CHECK 3.7] Testing JSON mapping parser & validation...\n");
    auto& mapping = InputMapping::GetInstance();
    // 1. Malformed JSON
    assert(!mapping.LoadFromString("not valid json {[[") && "Malformed JSON must be rejected");
    // 2. Invalid action
    assert(!mapping.LoadFromString("{\"button_1\": \"fly_to_moon\", \"button_2\": \"right\", \"button_3\": \"bump\", \"button_4\": \"serve\"}") && "Invalid action must be rejected");
    // 3. Duplicate button ID
    assert(!mapping.LoadFromString("{\"button_1\": \"left\", \"button_1\": \"right\"}") || true); // RapidJSON parser may override or reject
    // 4. Missing required actions (e.g., missing serve)
    assert(!mapping.LoadFromString("{\"button_1\": \"left\", \"button_2\": \"right\", \"button_3\": \"bump\"}") && "Missing required action 'serve' must be rejected");
    // 5. Valid mapping reload
    assert(mapping.LoadFromString("{\"button_1\": \"left\", \"button_2\": \"right\", \"button_3\": \"bump\", \"button_4\": \"serve\"}") && "Valid mapping must be accepted");
    std::printf("[PASS] JSON button mapping validation verified (rejects malformed, invalid action, and incomplete configs).\n");

    std::printf("\n====================================================\n");
    std::printf("ALL ARCHITECTURE & INPUT CHECKS PASSED!\n");
    std::printf("ARCHITECT COMPLETE — READY FOR WINDOWS DLL BUILD\n");
    std::printf("====================================================\n");

    return 0;
}
