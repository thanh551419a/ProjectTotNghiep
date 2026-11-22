using System;
using System.Collections.Generic;
using System.IO;
using Assets.Table;
using Native;
using Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class P02InputAcceptanceTests
{
    [MenuItem("Spike/Run P02 Input Acceptance Tests")]
    public static void RunAll()
    {
        int pass = 0, fail = 0, blocked = 0;
        bool initialized = false;
        SimulationDataTable table = null;
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, ButtonMappingConfig.FileName);
            var map = ButtonMappingConfig.Parse(File.ReadAllText(path), path);
            if (Application.isPlaying)
            {
                TestGeometry(ref pass, ref fail);
                TestBootstrap(map, ref pass, ref fail, ref blocked);
                TestMultiPointerRelease(ref pass, ref fail);
                Debug.Log($"[P02_UI_TEST_SUMMARY] pass={pass},fail={fail},blocked={blocked}; real pointer gameplay=NOT_RUN");
                if (fail > 0) SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + fail);
                if (Application.isBatchMode && SessionState.GetInt("SpikeQaAcceptanceStage", 0) == 0)
                    EditorApplication.Exit(fail > 0 ? 1 : blocked > 0 ? 77 : 0);
                return;
            }
            bool ids = Has(map, "left", 1) && Has(map, "right", 2) && Has(map, "bump", 3) && Has(map, "serve", 4) &&
                       Has(map, "jump_only", 5) && Has(map, "spikelight", 6) && Has(map, "spikemedium", 7) && Has(map, "spikestrong", 8);
            Check("configured IDs 1-8 preserve legacy mapping", ids, ref pass, ref fail);
            string abi = NativeAbiLayout.Validate();
            Check("managed/native ABI layout", abi == null, ref pass, ref fail, abi ?? "");
            if (!SpikeNativeBridge.Sim_Initialize(path))
                throw new InvalidOperationException("Sim_Initialize returned false");
            initialized = true;
            table = SimulationDataTable.GetOrCreateInstance();

            SpikeNativeBridge.Sim_Reset(); table.ResetTable();
            var start = Step(map, table);
            Check("snapshot maps player/opponent and one Get_data call advances one tick",
                start.status >= 0 && start.entityCount == 4 && start.tick == 1 &&
                start.entity0.entityId == 0 && start.entity1.entityId == 3 &&
                table.TryGetEntity(0, out var player) && player.roleName == "PLAYER" &&
                table.TryGetEntity(3, out var opponent) && opponent.roleName == "OPPONENT_1",
                ref pass, ref fail);

            SpikeNativeBridge.Sim_Reset(); table.ResetTable();
            var preJump = Step(map, table);
            bool startGrounded = preJump.entity0.isGrounded != 0 &&
                                 preJump.entity0.airborneState == (int)SimAirborneState.Grounded &&
                                 table.GetEntityAirborneState(0) == SimAirborneState.Grounded;
            var grounded = Step(map, table, map.JumpId);
            bool groundedTable = table.TryGetEntity(0, out var groundedRecord) &&
                                 groundedRecord.finalIntent == 1 && groundedRecord.jumpRemainingFrames == 15;
            Check("grounded Jump intent and jump frames (15 remaining after tick 1 of 16-frame 0.265s jump for JumpPower=95->100)",
                grounded.entity0.characterIntent.finalIntent == 1 && grounded.entity0.jumpFrame.remainingFrames == 15 && groundedTable,
                ref pass, ref fail, $"actualRemaining={grounded.entity0.jumpFrame.remainingFrames}");
            float maxJumpPosY = grounded.entity0.position.position.y;
            bool sawJumpUp = grounded.entity0.jumpFrame.remainingFrames > 0 &&
                             grounded.entity0.airborneState == (int)SimAirborneState.JumpUp &&
                             table.GetEntityAirborneState(0) == SimAirborneState.JumpUp;
            var airborne = Step(map, table);
            maxJumpPosY = Mathf.Max(maxJumpPosY, airborne.entity0.position.position.y);
            for (int i = 0; i < 16 && airborne.entity0.isGrounded != 0; i++)
            {
                if (airborne.entity0.jumpFrame.remainingFrames > 0 &&
                    airborne.entity0.airborneState == (int)SimAirborneState.JumpUp &&
                    table.GetEntityAirborneState(0) == SimAirborneState.JumpUp)
                {
                    sawJumpUp = true;
                }
                airborne = Step(map, table);
                maxJumpPosY = Mathf.Max(maxJumpPosY, airborne.entity0.position.position.y);
            }
            if (airborne.entity0.isGrounded == 0)
            {
                if (airborne.entity0.jumpFrame.remainingFrames > 0 &&
                    airborne.entity0.airborneState == (int)SimAirborneState.JumpUp &&
                    table.GetEntityAirborneState(0) == SimAirborneState.JumpUp)
                {
                    sawJumpUp = true;
                }
                int framesBefore = airborne.entity0.jumpFrame.remainingFrames;
                var airJump = Step(map, table, map.JumpId);
                maxJumpPosY = Mathf.Max(maxJumpPosY, airJump.entity0.position.position.y);
                Check("airborne Jump during 0%->80% ascent is gated to None (0), never Spike, and does not restart",
                    airJump.entity0.characterIntent.finalIntent == 0 &&
                    airJump.entity0.actionState.status != 2 &&
                    airJump.entity0.jumpFrame.remainingFrames < 16 &&
                    airJump.entity0.jumpFrame.remainingFrames <= framesBefore, ref pass, ref fail);

                bool sawFall = false;
                bool sawLandedAfterFall = false;
                var arcStep = airJump;
                for (int i = 0; i < 120; i++)
                {
                    maxJumpPosY = Mathf.Max(maxJumpPosY, arcStep.entity0.position.position.y);
                    if (arcStep.entity0.jumpFrame.remainingFrames > 0 &&
                        arcStep.entity0.airborneState == (int)SimAirborneState.JumpUp &&
                        table.GetEntityAirborneState(0) == SimAirborneState.JumpUp)
                    {
                        sawJumpUp = true;
                    }
                    if (sawJumpUp && arcStep.entity0.jumpFrame.remainingFrames == 0 &&
                        arcStep.entity0.isGrounded == 0 &&
                        arcStep.entity0.airborneState == (int)SimAirborneState.Fall &&
                        table.GetEntityAirborneState(0) == SimAirborneState.Fall)
                    {
                        sawFall = true;
                    }
                    if (sawFall && arcStep.entity0.isGrounded != 0 &&
                        arcStep.entity0.airborneState == (int)SimAirborneState.Grounded &&
                        table.GetEntityAirborneState(0) == SimAirborneState.Grounded)
                    {
                        sawLandedAfterFall = true;
                        break;
                    }
                    arcStep = Step(map, table);
                }
                Check("Single Time-Anchor jumpUpTime (16 ticks for JumpPower=95->100) lifts PLAYER above ground to expected peak posY",
                    maxJumpPosY > 340.0f && maxJumpPosY <= 540.0f, ref pass, ref fail, $"peakPosY={maxJumpPosY:F2}");
                Check("SimAirborneState transitions Grounded (0) -> JumpUp (1) -> Fall (2) -> Grounded (0)",
                    startGrounded && sawJumpUp && sawFall && sawLandedAfterFall, ref pass, ref fail,
                    $"startGrounded={startGrounded},jumpUp={sawJumpUp},fall={sawFall},landed={sawLandedAfterFall}");
            }
            else Check("airborne Jump fixture", false, ref pass, ref fail, "player did not leave ground");

            TestLegacyConfiguredControls(map, table, ref pass, ref fail);
            TestMovementAndEdges(map, table, ref pass, ref fail);
            TestSpikeRequestMapping(map, table, ref pass, ref fail, ref blocked);
            TestSourceTrace(map, table, ref pass, ref fail);
            TestDedicatedSpikeSelector(map, ref pass, ref fail);
            TestGeometry(ref pass, ref fail);
            TestBootstrap(map, ref pass, ref fail, ref blocked);
            TestMultiPointerRelease(ref pass, ref fail);
        }
        catch (Exception ex) { Check("P02 test harness exception", false, ref pass, ref fail, ex.ToString()); }
        finally
        {
            if (initialized) SpikeNativeBridge.Sim_Shutdown();
            if (!Application.isPlaying && table != null && table.gameObject != null &&
                table.gameObject.name == "[SimulationDataTable]") UnityEngine.Object.DestroyImmediate(table.gameObject);
        }
        Debug.Log($"[P02_TEST_SUMMARY] pass={pass},fail={fail},blocked={blocked}; real pointer gameplay=NOT_RUN; valid Spike acceptance=BLOCKED");
        if (fail > 0) SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + fail);
        if (Application.isBatchMode && SessionState.GetInt("SpikeQaAcceptanceStage", 0) == 0)
            EditorApplication.Exit(fail > 0 ? 1 : blocked > 0 ? 77 : 0);
    }

    private static NativeSimData Step(ButtonMappingConfig map, SimulationDataTable table, params uint[] ids)
    {
        ulong before = SpikeNativeBridge.Sim_GetTick();
        var input = ids == null || ids.Length == 0 ? NativeSimInput.Empty : NativeSimInput.FromButtons(ids);
        var snap = SpikeNativeBridge.Get_data(input);
        bool committed = table.UpdateFromNativeSnapshot(snap);
        if (snap.status < 0 || snap.entityCount != 4 || snap.tick != before + 1 || !committed)
            throw new InvalidOperationException($"Get_data one-tick/commit assertion failed {before}->{snap.tick}, status={snap.status}, count={snap.entityCount}, {map}");
        ulong tick = SpikeNativeBridge.Sim_GetTick();
        table.TryGetEntity(0, out _);
        if (SpikeNativeBridge.Sim_GetTick() != tick) throw new InvalidOperationException("DataTable read advanced native simulation");
        return snap;
    }

    private static void TestMovementAndEdges(ButtonMappingConfig map, SimulationDataTable table, ref int pass, ref int fail)
    {
        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var together = Step(map, table, map.RightId, map.JumpId);
        Check("Right + Jump coexist", together.entity0.characterIntent.finalIntent == 1 &&
              together.entity0.characterIntent.moveX > 0 && together.entity1.entityId == 3, ref pass, ref fail);

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var press = Step(map, table, map.RightId);
        var hold = Step(map, table, map.RightId);
        var release = Step(map, table);
        var repress = Step(map, table, map.RightId);
        Check("press/hold/release/re-press and one tick per Get_data",
            press.entity0.characterIntent.moveX > 0 && hold.entity0.characterIntent.moveX > 0 &&
            Mathf.Approximately(release.entity0.characterIntent.moveX, 0) &&
            repress.entity0.characterIntent.moveX > 0 &&
            hold.tick == press.tick + 1 && release.tick == hold.tick + 1 && repress.tick == release.tick + 1,
            ref pass, ref fail);
    }

    private static void TestLegacyConfiguredControls(ButtonMappingConfig map, SimulationDataTable table,
        ref int pass, ref int fail)
    {
        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var left = Step(map, table, map.LeftId);
        Check("configured legacy Left ID 1 produces negative moveX",
            left.entity0.characterIntent.moveX < 0f, ref pass, ref fail);

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var right = Step(map, table, map.RightId);
        Check("configured legacy Right ID 2 produces positive moveX",
            right.entity0.characterIntent.moveX > 0f, ref pass, ref fail);

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var bump = Step(map, table, map.BumpId);
        table.TryGetEntity(0, out var bumpRec);
        var moveBump = Step(map, table, map.RightId, map.BumpId);
        table.TryGetEntity(0, out var moveBumpRec);
        Check("configured legacy Bump ID 3 selects Bump intent, has no Slide while moving, and incurs 0ms cooldown without ball contact",
            bump.entity0.characterIntent.finalIntent == 5 &&
            bump.entity0.actionState.status == (int)SimActionState.None &&
            bumpRec.animId == SimAnimId.Idle &&
            bumpRec.cooldownRemainMs <= 0.001f &&
            moveBump.entity0.characterIntent.moveX > 0f &&
            moveBump.entity0.actionState.status == (int)SimActionState.None &&
            moveBumpRec.animId == SimAnimId.Move &&
            moveBumpRec.cooldownRemainMs <= 0.001f, ref pass, ref fail);

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var serve = Step(map, table, map.ServeId);
        table.TryGetEntity(0, out var serveRec);
        Check("configured legacy Serve ID 4 selects Serve intent with SimAnimId.Serve (90) and 0ms cooldown",
            serve.entity0.characterIntent.finalIntent == 8 &&
            serve.entity0.actionState.status == (int)SimActionState.Serve &&
            (int)serveRec.animId == 90 &&
            serveRec.cooldownRemainMs <= 0.001f &&
            serveRec.cooldownDurationMs <= 0.001f &&
            serve.entity0.actionState.remainTime <= 0.001f, ref pass, ref fail);
    }

    private static void TestSpikeRequestMapping(ButtonMappingConfig map, SimulationDataTable table, ref int pass, ref int fail, ref int blocked)
    {
        uint[] ids = { map.SpikeLightId, map.SpikeMediumId, map.SpikeStrongId };
        int[] intents = { 9, 10, 11 };
        int[] expectedStatuses = { (int)SimActionState.SpikeLight, (int)SimActionState.SpikeMedium, (int)SimActionState.SpikeStrong };
        int[] expectedAnimIds = { 60, 70, 80 };
        string[] names = { "Light", "Medium", "Strong" };
        for (int i = 0; i < ids.Length; i++)
        {
            SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
            var gSnap = Step(map, table, ids[i]);
            table.TryGetEntity(0, out var gRec);
            uint nextId = ids[(i + 1) % ids.Length];
            Step(map, table, nextId);
            table.TryGetEntity(0, out var gCooldownRec);
            Check($"Grounded Spike {names[i]} (0 cooldown) is accepted, emits SimAnimId {expectedAnimIds[i]}, and enters Spike cooldown",
                gSnap.entity0.characterIntent.finalIntent == intents[i] &&
                gSnap.entity0.actionState.status == expectedStatuses[i] &&
                (int)gRec.animId == expectedAnimIds[i] &&
                (int)gRec.cooldownAction == expectedStatuses[i] &&
                gRec.cooldownRemainMs > 0f &&
                gRec.cooldownDurationMs > 0f &&
                gRec.cooldownDurationMs <= 450.5f &&
                (int)gCooldownRec.cooldownAction == expectedStatuses[i] &&
                gCooldownRec.cooldownRemainMs > 0f &&
                gCooldownRec.cooldownRemainMs < gRec.cooldownRemainMs, ref pass, ref fail);

            SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
            Step(map, table, map.JumpId);
            Step(map, table);
            var earlyAirSnap = Step(map, table, ids[i]);
            table.TryGetEntity(0, out var earlyRec);
            Check($"Airborne Spike {names[i]} during <80% jump ascent is blocked with 0ms cooldown",
                earlyAirSnap.entity0.jumpFrame.remainingFrames > 3 &&
                earlyAirSnap.entity0.characterIntent.finalIntent == 0 &&
                earlyAirSnap.entity0.actionState.status == (int)SimActionState.None &&
                earlyRec.animId == SimAnimId.JumpUp &&
                earlyRec.cooldownRemainMs <= 0.001f, ref pass, ref fail);

            SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
            Step(map, table, map.JumpId);
            for (int j = 0; j < 13; j++) Step(map, table);
            var snap = Step(map, table, ids[i]);
            table.TryGetEntity(0, out var lateRec);
            Check($"Airborne Spike {names[i]} at >=80% jump ascent (0 cooldown) maps to intent {intents[i]} and emits SimAnimId {expectedAnimIds[i]}",
                snap.entity0.characterIntent.finalIntent == intents[i] &&
                snap.entity0.actionState.status == expectedStatuses[i] &&
                (int)lateRec.animId == expectedAnimIds[i] &&
                lateRec.cooldownRemainMs > 0f, ref pass, ref fail);
        }

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        Step(map, table, map.ServeId);
        NativeSimData readySnap = default;
        for (int t = 0; t < 45; t++)
        {
            readySnap = Step(map, table);
            if (readySnap.entity0.actionState.status == (int)SimActionState.None &&
                readySnap.entity0.actionState.remainTime <= 0f)
                break;
        }
        Step(map, table, map.JumpId);
        for (int j = 0; j < 13; j++) Step(map, table);
        table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var ballBeforeWhiff);
        var whiffSnap = Step(map, table, map.SpikeMediumId);
        table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var ballAfterWhiff);
        var cooldownSnap = Step(map, table, map.SpikeStrongId);
        bool fakeSpikeAndCooldownOk =
            Mathf.Approximately(ballBeforeWhiff.ballSpeed, ballAfterWhiff.ballSpeed) &&
            ((whiffSnap.entity0.actionState.status == (int)SimActionState.None &&
              whiffSnap.entity0.characterIntent.finalIntent == 10) ||
             (whiffSnap.entity0.actionState.status == (int)SimActionState.SpikeMedium &&
              whiffSnap.entity0.actionState.remainTime > 0f &&
              cooldownSnap.entity0.actionState.status == (int)SimActionState.SpikeMedium &&
              cooldownSnap.entity0.actionState.remainTime > 0f &&
              cooldownSnap.entity0.actionState.remainTime < whiffSnap.entity0.actionState.remainTime));
        Check("active-rally Fake Spike (whiff) preserves ball trajectory without false hit acceptance",
            fakeSpikeAndCooldownOk, ref pass, ref fail);
        blocked++;
        Debug.LogWarning("[P02_BLOCKED] Intent receipt does not prove valid/invalid Spike acceptance. No verified natural rally fixture using configured IDs 6-8 exists; tests do not mutate authoritative state.");
    }

    private static void TestSourceTrace(ButtonMappingConfig map, SimulationDataTable table, ref int pass, ref int fail)
    {
        var go = new GameObject("P02SourceTraceTest");
        try
        {
            var adapter = go.AddComponent<SpikeInputAdapter>();
            adapter.Configure(map); adapter.InjectionExclusive = true;
            adapter.SetInjected(SpikeAction.Jump, true);
            var payload = adapter.BuildInput();
            string trace = adapter.BuildTraceSummary();
            Check("input-source diagnostics retain payload and identify injection",
                payload.buttonCount == 1 && payload.btn0 == map.JumpId &&
                adapter.LastSubmittedSources[(int)SpikeAction.Jump] == SpikeInputSource.TestInjection &&
                trace.Contains("source=TestInjection") && trace.Contains("configuredNativeId=5") &&
                !trace.Contains("uiButtonId=4"), ref pass, ref fail);

            adapter.ClearInjected();
            adapter.InjectionExclusive = false;
            var handleUiInput = typeof(SpikeInputAdapter).GetMethod(
                "HandleUiInput",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            bool bumpHoldOk = false;
            if (handleUiInput != null)
            {
                handleUiInput.Invoke(adapter, new object[] { 2, true });
                var holdTick1 = adapter.BuildInput();
                var holdTick2 = adapter.BuildInput();
                var holdTick3 = adapter.BuildInput();
                handleUiInput.Invoke(adapter, new object[] { 2, false });
                var releasedTick = adapter.BuildInput();
                bumpHoldOk = holdTick1.buttonCount == 1 && holdTick1.btn0 == map.BumpId &&
                             holdTick2.buttonCount == 1 && holdTick2.btn0 == map.BumpId &&
                             holdTick3.buttonCount == 1 && holdTick3.btn0 == map.BumpId &&
                             releasedTick.buttonCount == 0;
            }
            Check("holding UiBump (2) continuously submits configured Bump ID 3 across consecutive ticks and stops on release",
                bumpHoldOk, ref pass, ref fail);

            bool pureJumpButtonOk = false;
            if (handleUiInput != null && table != null)
            {
                SpikeNativeBridge.Sim_Reset();
                table.ResetTable();
                var groundSnap = Step(map, table);
                handleUiInput.Invoke(adapter, new object[] { SpikeInputAdapter.UiJump, true });
                var groundedJumpTick = adapter.BuildInput();
                bool groundedSubmitsJumpId = groundedJumpTick.buttonCount == 1 &&
                                             groundedJumpTick.btn0 == map.JumpId &&
                                             !adapter.LastJumpSubmittedAsJumpSpike;
                handleUiInput.Invoke(adapter, new object[] { SpikeInputAdapter.UiJump, false });

                var airSnap = groundSnap;
                airSnap.tick += 1;
                airSnap.entity0.isGrounded = 0;
                airSnap.entity0.airborneState = (short)SimAirborneState.JumpUp;
                airSnap.entity0.position.position.y = 420f;
                airSnap.entity0.actionState.status = (int)SimActionState.None;
                airSnap.entity0.actionState.remainTime = 0f;
                table.UpdateFromNativeSnapshot(airSnap);

                handleUiInput.Invoke(adapter, new object[] { SpikeInputAdapter.UiJump, true });
                var airborneJumpTick = adapter.BuildInput();
                bool airborneStaysPureJumpId = airborneJumpTick.buttonCount == 1 &&
                                               airborneJumpTick.btn0 == map.JumpId &&
                                               !adapter.LastJumpSubmittedAsJumpSpike;
                handleUiInput.Invoke(adapter, new object[] { SpikeInputAdapter.UiJump, false });

                pureJumpButtonOk = groundedSubmitsJumpId && airborneStaysPureJumpId;
            }
            Check("pressing UiJump (4) submits pure JumpId (5) both grounded and airborne without dual-role JumpSpike",
                pureJumpButtonOk, ref pass, ref fail);

            adapter.SetMovementControlMode(MovementControlMode.HorizontalSlider);
            adapter.SetSliderHorizontalAxis(-0.6f);
            var sliderLeftTick = adapter.BuildInput();
            adapter.SetSliderHorizontalAxis(0.6f);
            var sliderRightTick = adapter.BuildInput();
            adapter.SetSliderHorizontalAxis(0f);
            var sliderNeutralTick = adapter.BuildInput();
            adapter.SetMovementControlMode(MovementControlMode.TwoButtons);
            var restoredTwoButtonsTick = adapter.BuildInput();
            bool sliderAdapterOk = sliderLeftTick.buttonCount == 1 && sliderLeftTick.btn0 == map.LeftId &&
                                   sliderRightTick.buttonCount == 1 && sliderRightTick.btn0 == map.RightId &&
                                   sliderNeutralTick.buttonCount == 0 &&
                                   restoredTwoButtonsTick.buttonCount == 0;
            Check("HorizontalSlider movement mode submits LeftId (1) on -0.6f, RightId (2) on +0.6f, 0 on neutral, and restores TwoButtons",
                sliderAdapterOk, ref pass, ref fail);

            bool ramStatsOk = false;
            if (table != null)
            {
                try
                {
                    SpikeNativeBridge.TryResetCharacterStats();
                    SpikeNativeBridge.Sim_Reset();
                    table.ResetTable();
                    var defBase = Step(map, table);
                    var defJump = Step(map, table, map.JumpId);
                    float defaultFirstTickDeltaY = defJump.entity0.position.position.y - defBase.entity0.position.position.y;

                    bool setOk = SpikeNativeBridge.TrySetCharacterStats(0, 80f, 85f, 95f, 120f);
                    bool getOk = SpikeNativeBridge.TryGetCharacterStats(0, out float atk, out float def, out float spd, out float jmp);
                    bool valuesMatch = setOk && getOk &&
                                       Mathf.Approximately(atk, 80f) && Mathf.Approximately(def, 85f) &&
                                       Mathf.Approximately(spd, 95f) && Mathf.Approximately(jmp, 120f);

                    SpikeNativeBridge.Sim_Reset();
                    table.ResetTable();
                    var customBase = Step(map, table);
                    var customJump = Step(map, table, map.JumpId);
                    float customFirstTickDeltaY = customJump.entity0.position.position.y - customBase.entity0.position.position.y;

                    bool resetOk = SpikeNativeBridge.TryResetCharacterStats();
                    bool getDefOk = SpikeNativeBridge.TryGetCharacterStats(0, out float rAtk, out float rDef, out float rSpd, out float rJmp);
                    bool defaultsRestored = resetOk && getDefOk &&
                                            Mathf.Approximately(rAtk, 180f) && Mathf.Approximately(rDef, 80f) &&
                                            Mathf.Approximately(rSpd, 90f) && Mathf.Approximately(rJmp, 95f);

                    ramStatsOk = valuesMatch &&
                                 defaultFirstTickDeltaY > 4.0f &&
                                 customFirstTickDeltaY > 4.0f &&
                                 Mathf.Abs(customFirstTickDeltaY - defaultFirstTickDeltaY) <= 1.0f &&
                                 customJump.entity0.jumpFrame.remainingFrames > defJump.entity0.jumpFrame.remainingFrames &&
                                 defaultsRestored;
                }
                finally
                {
                    SpikeNativeBridge.TryResetCharacterStats();
                }
            }
            Check("Sim_SetCharacterStats updates RAM stats (80,85,95,120), scales jumpUpTime frames (15 -> 18 remaining), and Sim_ResetCharacterStats restores (180,80,90,95)",
                ramStatsOk, ref pass, ref fail);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private static void TestDedicatedSpikeSelector(ButtonMappingConfig map, ref int pass, ref int fail)
    {
        var go = new GameObject("P02DedicatedSpikeTest");
        try
        {
            var adapter = go.AddComponent<SpikeInputAdapter>();
            adapter.Configure(map);
            adapter.InjectionExclusive = false;
            adapter.BeginDedicatedSpike(10f);
            adapter.EndDedicatedSpike(10.1f);
            var tap = adapter.BuildInput();
            Check("dedicated Spike tap submits Medium ID 7 once",
                tap.buttonCount == 1 && tap.btn0 == map.SpikeMediumId, ref pass, ref fail);

            adapter.BeginDedicatedSpike(20f);
            adapter.AdvanceDedicatedSpikeHold(0.219f);
            Check("selector remains closed before 0.22 seconds", !adapter.SelectorOpen,
                ref pass, ref fail);
            adapter.AdvanceDedicatedSpikeHold(0.22f);
            var held = adapter.BuildInput();
            Check("0.22 second hold opens selector without submitting a Spike",
                adapter.SelectorOpen && held.buttonCount == 0, ref pass, ref fail);
            bool lightSelected = adapter.SelectSpikeStrength(SpikeAction.SpikeLight);
            adapter.EndDedicatedSpike(20.23f);
            var light = adapter.BuildInput();
            Check("selector Light submits ID 6 and closes",
                lightSelected && light.buttonCount == 1 && light.btn0 == map.SpikeLightId && !adapter.SelectorOpen,
                ref pass, ref fail);

            adapter.BeginDedicatedSpike(30f);
            adapter.AdvanceDedicatedSpikeHold(0.22f);
            adapter.SetHoveredSpikeOption(SpikeAction.SpikeStrong);
            adapter.EndDedicatedSpike(30.23f);
            var strong = adapter.BuildInput();
            Check("selector Strong submits ID 8 and closes on hold release",
                strong.buttonCount == 1 && strong.btn0 == map.SpikeStrongId && !adapter.SelectorOpen,
                ref pass, ref fail);

            adapter.BeginDedicatedSpike(40f);
            adapter.AdvanceDedicatedSpikeHold(0.22f);
            adapter.EndDedicatedSpike(40.25f);
            Check("selector closes immediately when hold is released without selecting",
                !adapter.SelectorOpen, ref pass, ref fail);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private static void TestGeometry(ref int pass, ref int fail)
    {
        if (!Application.isPlaying) return;
        var buttons = UnityEngine.Object.FindObjectsByType<UIButtonInputTest>(FindObjectsInactive.Include);
        RectTransform primary = null;
        RectTransform receive = null, dedicatedSpike = null, serve = null;
        var secondaries = new List<RectTransform>();
        foreach (var button in buttons)
        {
            var rt = button.GetComponent<RectTransform>();
            if (button.ButtonId == 4) primary = rt;
            else if (button.ButtonId >= 0 && button.ButtonId <= SpikeInputAdapter.UiDedicatedSpike)
            {
                secondaries.Add(rt);
                if (button.ButtonId == 2) receive = rt;
                else if (button.ButtonId == SpikeInputAdapter.UiDedicatedSpike) dedicatedSpike = rt;
                else if (button.ButtonId == 3) serve = rt;
            }
        }
        bool ratio = primary != null && secondaries.Count == 8 && primary.rect.width > 0f;
        if (ratio)
            foreach (var sec in secondaries)
                ratio &= sec != null && Mathf.Abs(sec.rect.width / primary.rect.width - 0.70f) < 0.001f;
        Check("all secondary button diameters are exactly 0.70 of primary Jump/Spike button", ratio, ref pass, ref fail);
        bool arcOrder = receive != null && dedicatedSpike != null && serve != null &&
                        receive.position.y < dedicatedSpike.position.y &&
                        dedicatedSpike.position.y < serve.position.y &&
                        primary.position.x > Screen.width * 0.5f;
        Check("right-side Arena of Valor cluster orders Receive (bottom) < Spike (middle) < Serve (top)", arcOrder, ref pass, ref fail);
    }

    private static void TestBootstrap(ButtonMappingConfig map, ref int pass, ref int fail, ref int blocked)
    {
        if (!Application.isPlaying)
        {
            blocked++;
            Debug.LogWarning("[P02_BLOCKED] Scene bootstrap idempotency runs only in Play Mode; the Editor scene remains untouched.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        bool first = P02ActionButtonBootstrap.EnsureButtons(scene, out string error1);
        bool second = P02ActionButtonBootstrap.EnsureButtons(scene, out string error2);
        var counts = new int[8];
        var counts9 = new int[9];
        var labelsOk = true;
        var pressViewsOk = true;
        Canvas canvas = null;
        UIButtonInputTest btn4 = null, btn5 = null, btn6 = null, btn7 = null, btn8 = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var button in root.GetComponentsInChildren<UIButtonInputTest>(true))
            {
                if (canvas == null) canvas = button.GetComponentInParent<Canvas>();
                if (button.ButtonId >= 0 && button.ButtonId < counts9.Length)
                {
                    counts9[button.ButtonId]++;
                    pressViewsOk &= button.GetComponent<ActionButtonPressView>() != null;
                    if (button.ButtonId == 4) btn4 = button;
                    else if (button.ButtonId == 5) btn5 = button;
                    else if (button.ButtonId == 6) btn6 = button;
                    else if (button.ButtonId == 7) btn7 = button;
                    else if (button.ButtonId == SpikeInputAdapter.UiDedicatedSpike) btn8 = button;
                }
                if (button.ButtonId >= 0 && button.ButtonId < counts.Length)
                {
                    counts[button.ButtonId]++;
                    if (button.ButtonId >= 4)
                    {
                        var text = button.GetComponentInChildren<Text>(true);
                        labelsOk &= text != null && !string.IsNullOrEmpty(text.text);
                    }
                }
            }
        }
        bool unique = true;
        foreach (int count in counts) unique &= count == 1;
        Check("active-scene bootstrap is idempotent and retains exactly eight labeled buttons",
            first && second && unique && labelsOk, ref pass, ref fail, error1 ?? error2 ?? "");

        bool unique9 = true;
        foreach (int count in counts9) unique9 &= count == 1;
        int canvasButtonCount = canvas != null ? canvas.GetComponentsInChildren<UIButtonInputTest>(true).Length : -1;
        var layoutController = canvas != null ? canvas.GetComponentInChildren<ActionButtonLayoutController>(true) : null;
        Check("all 9 gameplay controls (0..8) have ActionButtonPressView and canvas has exactly 9 UIButtonInputTest",
            unique9 && pressViewsOk && canvasButtonCount == 9 && layoutController != null,
            ref pass, ref fail, $"canvasButtonCount={canvasButtonCount},pressViewsOk={pressViewsOk},layoutController={layoutController != null}");

        if (btn4 != null && btn5 != null && btn6 != null && btn7 != null)
        {
            var eventSystem = EventSystem.current;
            bool createdTempEventSystem = false;
            if (eventSystem == null)
            {
                var esGo = new GameObject("P02TempEventSystem");
                eventSystem = esGo.AddComponent<EventSystem>();
                createdTempEventSystem = true;
            }
            try
            {
                var editEvents = new List<bool>();
                Action<int, bool> handler = (_, pressed) => editEvents.Add(pressed);
                btn4.OnInput += handler;
                try
                {
                    var holdAdapter = SpikeSimulationManager.Instance != null
                        ? SpikeSimulationManager.Instance.InputAdapter
                        : UnityEngine.Object.FindFirstObjectByType<SpikeInputAdapter>(FindObjectsInactive.Include);
                    if (btn8 != null && holdAdapter != null)
                    {
                        UIButtonInputTest.SetLayoutEditMode(false);
                        bool hiddenBeforeHold = !btn5.gameObject.activeSelf && !btn6.gameObject.activeSelf && !btn7.gameObject.activeSelf;
                        btn8.OnPointerDown(new PointerEventData(eventSystem) { pointerId = 88 });
                        holdAdapter.AdvanceDedicatedSpikeHold(0.25f);
                        var adapterUpdate = typeof(SpikeInputAdapter).GetMethod("Update",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (adapterUpdate != null) adapterUpdate.Invoke(holdAdapter, null);
                        if (canvas != null)
                        {
                            foreach (var mb in canvas.GetComponentsInChildren<MonoBehaviour>(true))
                            {
                                if (mb == null || mb == holdAdapter) continue;
                                var mUpd = mb.GetType().GetMethod("Update",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                                    null, Type.EmptyTypes, null);
                                if (mUpd != null && mb.GetType().Namespace != "UnityEngine.UI")
                                    mUpd.Invoke(mb, null);
                            }
                        }
                        bool shownOnHold = holdAdapter.SelectorOpen &&
                                           btn5.gameObject.activeSelf && btn6.gameObject.activeSelf && btn7.gameObject.activeSelf;
                        btn8.OnPointerUp(new PointerEventData(eventSystem) { pointerId = 88 });
                        if (adapterUpdate != null) adapterUpdate.Invoke(holdAdapter, null);
                        if (canvas != null)
                        {
                            foreach (var mb in canvas.GetComponentsInChildren<MonoBehaviour>(true))
                            {
                                if (mb == null || mb == holdAdapter) continue;
                                var mUpd = mb.GetType().GetMethod("Update",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                                    null, Type.EmptyTypes, null);
                                if (mUpd != null && mb.GetType().Namespace != "UnityEngine.UI")
                                    mUpd.Invoke(mb, null);
                            }
                        }
                        bool hiddenAfterRelease = !holdAdapter.SelectorOpen &&
                                                  !btn5.gameObject.activeSelf && !btn6.gameObject.activeSelf && !btn7.gameObject.activeSelf;
                        Check("Action_5..7 stay hidden by default, reveal when DedicatedSpike (8) is held >=0.22s, and hide on release",
                            hiddenBeforeHold && shownOnHold && hiddenAfterRelease, ref pass, ref fail,
                            $"hiddenBefore={hiddenBeforeHold},shownOnHold={shownOnHold},hiddenAfter={hiddenAfterRelease}");
                    }

                    var setEditMethod = layoutController != null
                        ? layoutController.GetType().GetMethod("SetEditMode",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        : null;
                    UIButtonInputTest.SetLayoutEditMode(true);
                    if (!(btn5.gameObject.activeSelf && btn6.gameObject.activeSelf && btn7.gameObject.activeSelf) && setEditMethod != null)
                        setEditMethod.Invoke(layoutController, new object[] { true });
                    bool actions5To7Shown = btn5.gameObject.activeSelf && btn6.gameObject.activeSelf && btn7.gameObject.activeSelf;
                    btn4.OnPointerDown(new PointerEventData(eventSystem) { pointerId = 91 });
                    btn4.OnPointerUp(new PointerEventData(eventSystem) { pointerId = 91 });
                    bool suppressedInEditMode = editEvents.Count == 0 && UIButtonInputTest.IsLayoutEditMode;

                    if (setEditMethod != null)
                        setEditMethod.Invoke(layoutController, new object[] { false });
                    UIButtonInputTest.SetLayoutEditMode(false);
                    btn4.OnPointerDown(new PointerEventData(eventSystem) { pointerId = 92 });
                    btn4.OnPointerUp(new PointerEventData(eventSystem) { pointerId = 92 });
                    bool restoredAfterEditMode = !UIButtonInputTest.IsLayoutEditMode &&
                                                 editEvents.Count == 2 && editEvents[0] && !editEvents[1];
                    Check("SetLayoutEditMode(true) suppresses gameplay inputs, shows Action_5..7, and SetLayoutEditMode(false) restores normal input",
                        suppressedInEditMode && actions5To7Shown && restoredAfterEditMode, ref pass, ref fail,
                        $"suppressed={suppressedInEditMode},actions5To7Shown={actions5To7Shown},restored={restoredAfterEditMode}");

                    if (layoutController != null)
                    {
                        layoutController.SetEditMode(true);
                        RectTransform toggleRt = layoutController.ToggleButtonRect;
                        RectTransform toolbarRt = layoutController.ToolbarRect;
                        bool hudRefsValid = toggleRt != null && toolbarRt != null && toolbarRt.gameObject.activeSelf && layoutController.ScaleSlider != null;
                        // Top-left toggle is left of center scoreboard; floating toolbar is below top-center scoreboard (top 10% of screen)
                        bool nonOverlappingDock = hudRefsValid &&
                                                  toggleRt.position.x < Screen.width * 0.35f &&
                                                  toolbarRt.position.y < Screen.height * 0.90f;

                        layoutController.ResetAllButtons();
                        layoutController.SelectButton(4);
                        Vector2 defaultPos4 = layoutController.GetButtonAnchoredPosition(4);
                        Vector2 draggedPos4 = defaultPos4 + new Vector2(-24f, 18f);
                        layoutController.MoveButtonAnchoredPosition(4, draggedPos4);
                        bool dragOk = Vector2.Distance(layoutController.GetButtonAnchoredPosition(4), draggedPos4) < 0.01f;

                        layoutController.SetSelectedScale(1.20f);
                        bool sliderScaleOk = Mathf.Abs(layoutController.GetButtonScale(4) - 1.20f) < 0.005f;
                        layoutController.AdjustSelectedButtonScale(-ActionButtonLayoutController.ScaleStep);
                        bool minusStepOk = Mathf.Abs(layoutController.GetButtonScale(4) - 1.10f) < 0.005f;
                        layoutController.AdjustSelectedButtonScale(ActionButtonLayoutController.ScaleStep);
                        bool plusStepOk = Mathf.Abs(layoutController.GetButtonScale(4) - 1.20f) < 0.005f;

                        layoutController.ResetSelectedButton();
                        bool resetBtnOk = Vector2.Distance(layoutController.GetButtonAnchoredPosition(4), defaultPos4) < 0.01f &&
                                          Mathf.Abs(layoutController.GetButtonScale(4) - 1f) < 0.005f;

                        layoutController.SetButtonScale(2, 1.25f);
                        layoutController.ResetAllButtons();
                        bool resetAllOk = Mathf.Abs(layoutController.GetButtonScale(2) - 1f) < 0.005f &&
                                          !ActionButtonLayoutController.HasSavedCustomLayout;

                        layoutController.SaveAndExitEditMode();
                        bool doneSaveOk = !UIButtonInputTest.IsLayoutEditMode && ActionButtonLayoutController.HasSavedCustomLayout;
                        layoutController.ResetAllButtons();

                        layoutController.SetEditMode(true);
                        var sceneAdapter = SpikeSimulationManager.Instance != null ? SpikeSimulationManager.Instance.InputAdapter : UnityEngine.Object.FindFirstObjectByType<SpikeInputAdapter>(FindObjectsInactive.Include);
                        bool initialTwoButtons = sceneAdapter != null &&
                                                 sceneAdapter.CurrentMovementMode == MovementControlMode.TwoButtons &&
                                                 layoutController.MovementSliderRect != null &&
                                                 !layoutController.MovementSliderRect.gameObject.activeSelf;
                        layoutController.ToggleMovementMode();
                        bool switchedToSlider = sceneAdapter != null &&
                                                sceneAdapter.CurrentMovementMode == MovementControlMode.HorizontalSlider &&
                                                layoutController.MovementSliderRect != null &&
                                                layoutController.MovementSliderRect.gameObject.activeSelf;
                        layoutController.ToggleMovementMode();
                        bool restoredTwoButtons = sceneAdapter != null &&
                                                  sceneAdapter.CurrentMovementMode == MovementControlMode.TwoButtons &&
                                                  layoutController.MovementSliderRect != null &&
                                                  !layoutController.MovementSliderRect.gameObject.activeSelf;
                        layoutController.ResetAllButtons();
                        layoutController.SetEditMode(false);
                        bool moveToggleOk = initialTwoButtons && switchedToSlider && restoredTwoButtons;

                        Check("LayoutEditToggleButton and LayoutEditorToolbar dock without scoreboard overlap and all customization controls pass",
                            nonOverlappingDock && dragOk && sliderScaleOk && minusStepOk && plusStepOk && resetBtnOk && resetAllOk && doneSaveOk && moveToggleOk,
                            ref pass, ref fail,
                            $"dock={nonOverlappingDock},drag={dragOk},slider={sliderScaleOk},minus={minusStepOk},plus={plusStepOk},resetBtn={resetBtnOk},resetAll={resetAllOk},doneSave={doneSaveOk},moveToggle={moveToggleOk}");
                    }
                }
                finally
                {
                    UIButtonInputTest.SetLayoutEditMode(false);
                    btn4.OnInput -= handler;
                }
            }
            finally
            {
                if (createdTempEventSystem && eventSystem != null)
                    UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
            }
        }
    }

    private static void TestMultiPointerRelease(ref int pass, ref int fail)
    {
        var buttonObject = new GameObject("P02SyntheticPointerButton");
        var eventSystemObject = new GameObject("P02SyntheticEventSystem");
        try
        {
            var eventSystem = eventSystemObject.AddComponent<EventSystem>();
            var button = buttonObject.AddComponent<UIButtonInputTest>();
            button.Configure(4, true);
            var events = new List<bool>();
            button.OnInput += (_, pressed) => events.Add(pressed);
            button.OnPointerDown(new PointerEventData(eventSystem) { pointerId = 11 });
            button.OnPointerDown(new PointerEventData(eventSystem) { pointerId = 12 });
            button.OnPointerUp(new PointerEventData(eventSystem) { pointerId = 11 });
            bool firstLiftKeepsHeld = events.Count == 1 && events[0];
            button.OnPointerUp(new PointerEventData(eventSystem) { pointerId = 12 });
            bool lastLiftReleases = events.Count == 2 && !events[1];
            Check("same-button multi-pointer releases only after last pointer lifts",
                firstLiftKeepsHeld && lastLiftReleases, ref pass, ref fail,
                "synthetic handler test; real UI pointer gameplay is NOT_RUN");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(buttonObject);
            UnityEngine.Object.DestroyImmediate(eventSystemObject);
        }
    }

    private static bool Has(ButtonMappingConfig mapping, string name, uint value) =>
        mapping.ActionToId.TryGetValue(name, out uint actual) && actual == value;

    private static void Check(string name, bool ok, ref int pass, ref int fail, string detail = "")
    {
        if (ok) { pass++; Debug.Log($"[PASS] {name} {detail}"); }
        else { fail++; Debug.LogError($"[FAIL] {name} {detail}"); }
    }
}
