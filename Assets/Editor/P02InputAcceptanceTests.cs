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
                if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : blocked > 0 ? 77 : 0);
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

            SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
            var grounded = Step(map, table, map.JumpId);
            bool groundedTable = table.TryGetEntity(0, out var groundedRecord) &&
                                 groundedRecord.finalIntent == 1 && groundedRecord.jumpRemainingFrames > 0;
            Check("grounded Jump intent and jump frames",
                grounded.entity0.characterIntent.finalIntent == 1 && grounded.entity0.jumpFrame.remainingFrames > 0 && groundedTable,
                ref pass, ref fail);
            var airborne = Step(map, table);
            for (int i = 0; i < 16 && airborne.entity0.isGrounded != 0; i++) airborne = Step(map, table);
            if (airborne.entity0.isGrounded == 0)
            {
                int framesBefore = airborne.entity0.jumpFrame.remainingFrames;
                var airJump = Step(map, table, map.JumpId);
                Check("airborne Jump remains Jump, never Spike, and does not restart",
                    airJump.entity0.characterIntent.finalIntent == 1 &&
                    airJump.entity0.actionState.status != 2 &&
                    airJump.entity0.jumpFrame.remainingFrames < 30 &&
                    airJump.entity0.jumpFrame.remainingFrames <= framesBefore, ref pass, ref fail);
            }
            else Check("airborne Jump fixture", false, ref pass, ref fail, "player did not leave ground");

            TestLegacyConfiguredControls(map, table, ref pass, ref fail);
            TestMovementAndEdges(map, table, ref pass, ref fail);
            TestSpikeRequestMapping(map, table, ref pass, ref fail, ref blocked);
            TestSourceTrace(map, ref pass, ref fail);
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
        if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : blocked > 0 ? 77 : 0);
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
        Check("configured legacy Bump ID 3 selects Bump intent",
            bump.entity0.characterIntent.finalIntent == 5, ref pass, ref fail);

        SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
        var serve = Step(map, table, map.ServeId);
        Check("configured legacy Serve ID 4 selects Serve intent in reset serve state",
            serve.entity0.characterIntent.finalIntent == 8, ref pass, ref fail);
    }

    private static void TestSpikeRequestMapping(ButtonMappingConfig map, SimulationDataTable table, ref int pass, ref int fail, ref int blocked)
    {
        uint[] ids = { map.SpikeLightId, map.SpikeMediumId, map.SpikeStrongId };
        int[] intents = { 9, 10, 11 };
        string[] names = { "Light", "Medium", "Strong" };
        for (int i = 0; i < ids.Length; i++)
        {
            SpikeNativeBridge.Sim_Reset(); table.ResetTable(); Step(map, table);
            var snap = Step(map, table, ids[i]);
            Check($"Spike {names[i]} maps to intent {intents[i]}",
                snap.entity0.characterIntent.finalIntent == intents[i], ref pass, ref fail);
        }
        blocked++;
        Debug.LogWarning("[P02_BLOCKED] Intent receipt does not prove valid/invalid Spike acceptance. No verified natural rally fixture using configured IDs 6-8 exists; tests do not mutate authoritative state.");
    }

    private static void TestSourceTrace(ButtonMappingConfig map, ref int pass, ref int fail)
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
        var labelsOk = true;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var button in root.GetComponentsInChildren<UIButtonInputTest>(true))
            {
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
