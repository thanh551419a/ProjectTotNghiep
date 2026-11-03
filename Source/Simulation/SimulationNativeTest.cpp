#include "SpikeNativeApi.h"
#include <cassert>
#include <cmath>
#include <cstdio>

int main()
{
    std::printf("====================================================\n");
    std::printf("[SpikeCross Native Test] Starting Standalone Tests\n");
    std::printf("====================================================\n");

    // 1. Initialize
    bool initOk = Sim_Initialize(nullptr);
    assert(initOk && "Sim_Initialize failed!");
    std::printf("[PASS] Sim_Initialize succeeded.\n");

    // 2. Initial Snapshot check
    SimSnapshot initialSnap{};
    Sim_GetSnapshot(&initialSnap);
    assert(initialSnap.frameNumber == 0);
    assert(initialSnap.entities[0].posY > 200.0f);
    assert(initialSnap.ball.posY > 200.0f);
    assert(initialSnap.match.leftScore == 0);
    assert(initialSnap.match.rightScore == 0);
    std::printf("[PASS] Initial snapshot valid: Player0=(%.1f, %.1f), Ball=(%.1f, %.1f), Score=%d-%d\n",
                initialSnap.entities[0].posX, initialSnap.entities[0].posY,
                initialSnap.ball.posX, initialSnap.ball.posY,
                initialSnap.match.leftScore, initialSnap.match.rightScore);

    // 3. Step without input
    float startPlayerX = initialSnap.entities[0].posX;
    Sim_Update(0.0166667f);
    SimSnapshot step1Snap{};
    Sim_GetSnapshot(&step1Snap);
    assert(step1Snap.frameNumber == 1);
    std::printf("[PASS] Step 1 update succeeded (frame=1).\n");

    // 4. Test Player Movement (Move Right via legacy API)
    std::printf("[TEST] Testing Player Movement (Legacy Sim_SetInput)...\n");
    Sim_SetInput(0, SIM_BUTTON_MOVE_RIGHT, 0.0f);
    for (int i = 0; i < 10; i++)
    {
        Sim_Update(0.0166667f);
    }
    SimSnapshot moveSnap{};
    Sim_GetSnapshot(&moveSnap);
    assert(moveSnap.entities[0].posX > startPlayerX);
    assert(moveSnap.entities[0].facingDirection == 1);
    std::printf("[PASS] Player moved right: startX=%.1f -> newX=%.1f\n", startPlayerX, moveSnap.entities[0].posX);

    // 4b. Test Abstract Button ID Input Pipeline (Sim_SubmitButtonInput)
    std::printf("[TEST] Testing Abstract Button ID Input Pipeline (Sim_SubmitButtonInput)...\n");
    Sim_SetInput(0, 0, 0.0f); // Clear previous input

    // Test Invalid button ID
    int32_t badBtnRc = Sim_SubmitButtonInput(0, 99, 1);
    assert(badBtnRc == SIM_ERR_INVALID_ARGUMENT && "Invalid button ID must be rejected!");
    std::printf("[PASS] Invalid button ID 99 rejected with SIM_ERR_INVALID_ARGUMENT.\n");

    // Test Invalid entity ID
    int32_t badEntRc = Sim_SubmitButtonInput(10, 1, 1);
    assert(badEntRc == SIM_ERR_INVALID_ARGUMENT && "Invalid entity ID must be rejected!");
    std::printf("[PASS] Invalid entity ID 10 rejected with SIM_ERR_INVALID_ARGUMENT.\n");

    // Test Button 1 (MoveRight) press + hold
    float beforeBtnX = moveSnap.entities[0].posX;
    int32_t btnPressRc = Sim_SubmitButtonInput(0, 1, 1); // Pressed
    assert(btnPressRc == SIM_OK && "Button 1 press must succeed!");
    int32_t btnHoldRc = Sim_SubmitButtonInput(0, 1, 2); // Held
    assert(btnHoldRc == SIM_OK && "Button 1 hold must succeed!");

    Sim_Update(0.0166667f);
    SimSnapshot btnMoveSnap1{};
    Sim_GetSnapshot(&btnMoveSnap1);
    float afterBtnX1 = btnMoveSnap1.entities[0].posX;
    assert(afterBtnX1 > beforeBtnX && "Button 1 must produce rightward movement!");
    std::printf("[PASS] Button 1 (MoveRight) pressed/held: beforeX=%.1f -> afterX=%.1f (delta=%.1f px)\n",
                beforeBtnX, afterBtnX1, afterBtnX1 - beforeBtnX);

    // Continue holding for 5 frames
    for (int i = 0; i < 5; ++i)
    {
        Sim_SubmitButtonInput(0, 1, 2); // Held
        Sim_Update(0.0166667f);
    }
    SimSnapshot btnMoveSnap2{};
    Sim_GetSnapshot(&btnMoveSnap2);
    float afterBtnX2 = btnMoveSnap2.entities[0].posX;
    assert(afterBtnX2 > afterBtnX1 && "Holding Button 1 must continue movement!");
    std::printf("[PASS] Button 1 continuous hold verified: pos=%.1f\n", afterBtnX2);

    // Release Button 1
    int32_t btnRelRc = Sim_SubmitButtonInput(0, 1, 0); // Released
    assert(btnRelRc == SIM_OK && "Button 1 release must succeed!");
    Sim_Update(0.0166667f);
    SimSnapshot btnStopSnap{};
    Sim_GetSnapshot(&btnStopSnap);
    float stopX = btnStopSnap.entities[0].posX;
    assert(std::fabs(stopX - afterBtnX2) < 0.01f && "Releasing button must stop movement!");
    std::printf("[PASS] Button 1 release stopped movement cleanly at X=%.1f.\n", stopX);

    // 5. Test Player Jump
    std::printf("[TEST] Testing Player Jump...\n");
    float groundY = moveSnap.entities[0].posY;
    Sim_SetInput(0, SIM_BUTTON_JUMP_SPIKE, 0.0f);
    Sim_Update(0.0166667f); // Jump trigger frame
    Sim_SetInput(0, 0, 0.0f); // Release button

    bool ascended = false;
    for (int i = 0; i < 20; i++)
    {
        Sim_Update(0.0166667f);
        SimSnapshot jumpSnap{};
        Sim_GetSnapshot(&jumpSnap);
        if (jumpSnap.entities[0].posY > groundY + 10.0f)
        {
            ascended = true;
        }
    }
    assert(ascended && "Player did not ascend on jump!");
    std::printf("[PASS] Player jump successfully lifted entity off ground.\n");

    // 6. Test AI Simulation & Ball Trajectory
    std::printf("[TEST] Testing AI & Ball Trajectory over 120 frames...\n");
    Sim_Reset();
    Sim_SetInput(0, SIM_BUTTON_SERVE, 0.0f); // Player serves
    Sim_Update(0.0166667f);
    Sim_SetInput(0, 0, 0.0f);

    for (int i = 0; i < 300; i++)
    {
        Sim_Update(0.0166667f);
    }
    SimSnapshot rallySnap{};
    Sim_GetSnapshot(&rallySnap);
    std::printf("[PASS] 300 frames simulated. BallPos=(%.1f, %.1f), Opponent1Pos=(%.1f, %.1f), Touches=%d, Score=%d-%d, StateFrame=%d\n",
                rallySnap.ball.posX, rallySnap.ball.posY,
                rallySnap.entities[3].posX, rallySnap.entities[3].posY,
                rallySnap.match.touchCount,
                rallySnap.match.leftScore, rallySnap.match.rightScore,
                rallySnap.ball.stateFrame);

    // 7. Test Reset
    std::printf("[TEST] Testing Simulation Reset...\n");
    Sim_Reset();
    SimSnapshot resetSnap{};
    Sim_GetSnapshot(&resetSnap);
    assert(resetSnap.frameNumber == 0);
    assert(resetSnap.match.leftScore == 0);
    assert(resetSnap.match.rightScore == 0);
    std::printf("[PASS] Simulation Reset restored initial state cleanly.\n");

    // 8. Shutdown
    Sim_Shutdown();
    std::printf("[PASS] Simulation Shutdown succeeded.\n");

    std::printf("====================================================\n");
    std::printf("ALL C++ STANDALONE NATIVE SIMULATION TESTS PASSED!\n");
    std::printf("====================================================\n");
    return 0;
}
