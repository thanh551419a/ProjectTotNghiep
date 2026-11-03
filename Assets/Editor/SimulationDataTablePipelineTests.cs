using System;
using UnityEngine;
using UnityEditor;
using Assets.Table;
using Simulation;
using Native;

public static class SimulationDataTablePipelineTests
{
    [MenuItem("Spike/Run Data Table Pipeline Tests")]
    public static void RunAllTests()
    {
        Debug.Log("========================================================");
        Debug.Log("RUNNING MANDATORY DATA TABLE INTEGRATION TESTS (A - E)");
        Debug.Log("========================================================");

        int passed = 0;
        int failed = 0;

        // ----------------------------------------------------
        // TEST A - Table Creation and Initialization
        // ----------------------------------------------------
        var table = SimulationDataTable.GetOrCreateInstance();
        bool testA_exists = table != null;
        table.ResetTable();
        bool testA_emptyBeforeInit = !table.IsInitialized && table.CurrentTick == 0 && table.EntityCount == 0;

        var invalidSnap = new NativeSimData { status = -1, entityCount = 0 };
        bool testA_rejectInvalid = !table.UpdateFromNativeSnapshot(invalidSnap);

        bool testA_ok = testA_exists && testA_emptyBeforeInit && testA_rejectInvalid;
        RecordResult("Test A: Table Creation & Initialization", testA_ok, ref passed, ref failed);

        // ----------------------------------------------------
        // TEST B - Snapshot Ingestion via Real Native API
        // ----------------------------------------------------
        string mappingPath = System.IO.Path.Combine(Application.streamingAssetsPath, ButtonMappingConfig.FileName);
        var mapping = ButtonMappingConfig.Parse(System.IO.File.ReadAllText(mappingPath), mappingPath);
        bool initOk = NativeAbiLayout.Validate() == null && SpikeNativeBridge.Sim_Initialize(mappingPath);
        SpikeNativeBridge.Sim_Reset();
        var snap0 = SpikeNativeBridge.Get_data(NativeSimInput.Empty);

        bool testB_valid = initOk && snap0.status >= 0 && snap0.entityCount == 4;
        bool testB_ingested = table.UpdateFromNativeSnapshot(snap0);

        bool hasP0 = table.TryGetEntity(0, out var rec0);
        bool hasOpp = table.TryGetEntity(3, out var rec3);
        bool hasBall = table.TryGetEntity(7, out var rec7);
        bool hasNet = table.TryGetEntity(6, out var rec6);

        bool testB_mappingOk = hasP0 && rec0.roleName == "PLAYER" &&
                               hasOpp && rec3.roleName == "OPPONENT_1" &&
                               hasBall && rec7.roleName == "BALL" &&
                               hasNet && rec6.roleName == "NET";

        bool testB_flagsPreserved = rec0.hasPosition && rec0.hasSize && rec0.hasCharacterActionState &&
                                    rec7.hasPosition && rec7.hasBallTrajectory && rec7.hasBallGameplayState &&
                                    rec6.hasPosition && rec6.hasSize;

        bool testB_tickMatches = table.CurrentTick == snap0.tick;
        bool testB_ok = testB_valid && testB_ingested && testB_mappingOk && testB_flagsPreserved && testB_tickMatches;
        RecordResult("Test B: Snapshot Ingestion & Entity Mapping", testB_ok, ref passed, ref failed);

        // ----------------------------------------------------
        // TEST C - Updated Values Replace Stale Values
        // ----------------------------------------------------
        float prevPlayerX = rec0.posX;
        ulong prevTick = table.CurrentTick;

        for (int i = 0; i < 30; i++)
        {
            var moveInput = NativeSimInput.FromButtons(mapping.RightId);
            var simStep = SpikeNativeBridge.Get_data(moveInput);
            table.UpdateFromNativeSnapshot(simStep);
        }

        table.TryGetEntity(0, out var rec0_after);
        table.TryGetEntity(7, out var rec7_after);
        table.TryGetEntity(6, out var rec6_after);

        bool testC_playerMoved = rec0_after.posX > prevPlayerX;
        bool testC_tickAdvanced = table.CurrentTick > prevTick;
        bool testC_netUncorrupted = rec6_after.hasPosition && rec6_after.posX == rec6.posX;

        ulong tickBeforeRead = SpikeNativeBridge.Sim_GetTick();
        table.TryGetEntity(0, out _);
        ulong tickAfterRead = SpikeNativeBridge.Sim_GetTick();
        bool testC_readDoesNotTick = (tickBeforeRead == tickAfterRead);

        bool testC_ok = testC_playerMoved && testC_tickAdvanced && testC_netUncorrupted && testC_readDoesNotTick;
        RecordResult("Test C: Stale Replacement & Non-advancing Reads", testC_ok, ref passed, ref failed);

        // ----------------------------------------------------
        // TEST D - Table-to-Render Flow (Coordinate Conversion & Visuals)
        // ----------------------------------------------------
        var (pWorldX, pWorldY) = SimulationEntityReader.SimToWorld(rec0_after.posX, rec0_after.posY);
        var (bWorldX, bWorldY) = SimulationEntityReader.SimToWorld(rec7_after.posX, rec7_after.posY);
        var (oppWorldX, oppWorldY) = SimulationEntityReader.SimToWorld(rec3.posX, rec3.posY);
        var (netWorldX, netWorldY) = SimulationEntityReader.SimToWorld(rec6.posX, rec6.posY);

        float expectedPWorldX = (rec0_after.posX - 1370.0f) * 0.0125f;
        float expectedPWorldY = (rec0_after.posY - 265.0f) * 0.0125f;
        bool testD_coordsCorrect = Mathf.Abs(pWorldX - expectedPWorldX) < 1e-4f && Mathf.Abs(pWorldY - expectedPWorldY) < 1e-4f;
        bool testD_ballFromTable = rec7_after.hasPosition && bWorldX != 0f;

        bool testD_ok = testD_coordsCorrect && testD_ballFromTable;
        RecordResult("Test D: Table-to-Render Coordinate Conversion", testD_ok, ref passed, ref failed);

        // ----------------------------------------------------
        // TEST E - Lifecycle and Failure Handling
        // ----------------------------------------------------
        var corruptSnap = new NativeSimData { status = -1, entityCount = 4 };
        bool rejected = !table.UpdateFromNativeSnapshot(corruptSnap);
        bool tablePreserved = table.CurrentTick > 0 && table.EntityCount == 4;

        // A valid but OLDER tick must never overwrite a newer committed tick.
        var staleSnap = snap0;
        bool staleRejected = !table.UpdateFromNativeSnapshot(staleSnap) && table.CurrentTick > snap0.tick;

        // Duplicate entity ids must be rejected as a whole.
        var dupSnap = SpikeNativeBridge.Get_data(NativeSimInput.Empty);
        dupSnap.entity1 = dupSnap.entity0;
        bool dupRejected = SimulationDataTable.ValidateSnapshot(dupSnap) != null;

        table.ResetTable();
        bool resetOk = !table.IsInitialized && table.CurrentTick == 0 && table.EntityCount == 0;

        bool testE_ok = rejected && tablePreserved && staleRejected && dupRejected && resetOk;
        RecordResult("Test E: Lifecycle, Reset, Stale-tick and Failure Handling", testE_ok, ref passed, ref failed);

        SpikeNativeBridge.Sim_Shutdown();
        if (!Application.isPlaying && table != null && table.gameObject.name == "[SimulationDataTable]") UnityEngine.Object.DestroyImmediate(table.gameObject);

        Debug.Log("========================================================");
        Debug.Log($"DATA TABLE INTEGRATION SUMMARY: Passed={passed}, Failed={failed}");
        Debug.Log("========================================================");
        if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    private static void RecordResult(string testName, bool ok, ref int passed, ref int failed)
    {
        if (ok)
        {
            passed++;
            Debug.Log($"[PASS] {testName}");
        }
        else
        {
            failed++;
            Debug.LogError($"[FAIL] {testName}");
        }
    }
}
