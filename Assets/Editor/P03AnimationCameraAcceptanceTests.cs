using System;
using System.Linq;
using System.Reflection;
using Assets.Table;
using Native;
using Presentation.CameraSystem;
using Presentation.Character;
using Presentation.State;
using Simulation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class P03AnimationCameraAcceptanceTests
{
    private const string ControllerPath = "Assets/Animation/Sprites/KnightAnim.controller";
    private const string BumpClipPath = "Assets/Animation/Sprites/KnightBump.anim";
    private const string SpikeClipPath = "Assets/Animation/Sprites/KnightSpike.anim";
    private const string IdleClipPath = "Assets/Animation/Sprites/KnightIdle.anim";
    private const string JumpClipPath = "Assets/Animation/Sprites/KnightJump.anim";

    [MenuItem("Spike/Run P03 Animation and Camera Acceptance Tests")]
    public static void RunAll()
    {
        int pass = 0;
        int fail = 0;
        int blocked = 0;
        CheckStateMapping(ref pass, ref fail);
        CheckAnimatorController(ref pass, ref fail);
        RunNativeDrivenSpikeAnimations(ref pass, ref fail, ref blocked);
        CheckCameraBoundsAndStateIsolation(ref pass, ref fail, ref blocked);
        Debug.Log($"[P03_TEST_SUMMARY] pass={pass},fail={fail},blocked={blocked}; native-driven Spike sequence details are reported in P03_NATIVE_ANIM_SUMMARY");
        if (fail > 0) throw new InvalidOperationException($"P03 acceptance failed: {fail} assertion(s)");
    }

    private static void CheckStateMapping(ref int pass, ref int fail)
    {
        MethodInfo map = typeof(SimulationDataTable).GetMethod(
            "MapActionToCharacterState", BindingFlags.NonPublic | BindingFlags.Static);
        if (map == null)
        {
            Check("authoritative action mapper exists", false, ref pass, ref fail);
            return;
        }

        Check("accepted native SpikeLight maps distinctly", Map(map, SimActionState.SpikeLight, true) == CharacterState.SpikeLight, ref pass, ref fail);
        Check("accepted native SpikeMedium maps distinctly", Map(map, SimActionState.SpikeMedium, true) == CharacterState.SpikeMedium, ref pass, ref fail);
        Check("accepted native SpikeStrong maps distinctly", Map(map, SimActionState.SpikeStrong, true) == CharacterState.SpikeStrong, ref pass, ref fail);
        Check("legacy generic Spike mapping remains", Map(map, SimActionState.Spike, true) == CharacterState.Spike, ref pass, ref fail);
        Check("Bump mapping remains", Map(map, SimActionState.Bump, true) == CharacterState.Bump, ref pass, ref fail);
        Check("rejected/None grounded action does not map to Spike", Map(map, SimActionState.None, true) == CharacterState.Idle, ref pass, ref fail);
        Check("airborne None state remains Jump", Map(map, SimActionState.None, false) == CharacterState.Jump, ref pass, ref fail);
    }

    private static CharacterState Map(MethodInfo map, SimActionState action, bool grounded)
    {
        return (CharacterState)map.Invoke(null, new object[] { (int)action, grounded, 0f });
    }

    private static void CheckAnimatorController(ref int pass, ref int fail)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var bumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BumpClipPath);
        var spikeClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpikeClipPath);
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var jumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpClipPath);
        Check("active Knight controller, Bump clip, and Spike clip exist",
            controller != null && bumpClip != null && spikeClip != null && idleClip != null && jumpClip != null, ref pass, ref fail);
        if (controller == null || bumpClip == null || spikeClip == null) return;

        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimatorStateMachine groundMachine = FindMachine(root, "New StateMachine");
        AnimatorStateMachine airMachine = FindMachine(root, "InAir");
        AnimatorState grounded = FindState(groundMachine, "OnTheGround");
        AnimatorState airborne = FindState(airMachine, "InAir");
        AnimatorState bump = FindState(groundMachine, "KnightBump");

        Check("existing grounded, airborne, and Bump states remain", groundMachine != null && airMachine != null && grounded != null && airborne != null && bump != null, ref pass, ref fail);
        if (groundMachine == null || airMachine == null || grounded == null || airborne == null || bump == null) return;

        Check("existing Bump state still uses Bump clip", bump.motion == bumpClip, ref pass, ref fail);
        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeLight", CharacterState.SpikeLight, spikeClip, ref pass, ref fail);
        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeMedium", CharacterState.SpikeMedium, spikeClip, ref pass, ref fail);
        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeStrong", CharacterState.SpikeStrong, spikeClip, ref pass, ref fail);
    }

    private static void CheckSpikeState(AnimatorController controller, AnimatorStateMachine airMachine,
        AnimatorState grounded, AnimatorState airborne, string stateName, CharacterState stateValue,
        AnimationClip expectedClip, ref int pass, ref int fail)
    {
        AnimatorState[] sameName = EnumerateStates(controller.layers[0].stateMachine).Where(s => s.name == stateName).ToArray();
        AnimatorState state = FindState(airMachine, stateName);
        Check(stateName + " is one distinct Animator state", sameName.Length == 1 && state != null, ref pass, ref fail);
        if (state == null) return;

        Check(stateName + " directly references KnightSpike clip", state.motion == expectedClip, ref pass, ref fail);
        Check(stateName + " has grounded and airborne entry", HasStateCondition(grounded, state, (int)stateValue) && HasStateCondition(airborne, state, (int)stateValue), ref pass, ref fail);
        Check(stateName + " has accepted-state Any State entry", HasAnyStateCondition(controller.layers[0].stateMachine, state, (int)stateValue), ref pass, ref fail);
        Check(stateName + " recovers to Jump, Idle, or Move from authoritative State", 
            HasStateCondition(state, airborne, (int)CharacterState.Jump) &&
            HasStateCondition(state, grounded, (int)CharacterState.Idle) &&
            HasStateCondition(state, grounded, (int)CharacterState.Move), ref pass, ref fail);
    }

    private static void CheckCameraBoundsAndStateIsolation(ref int pass, ref int fail, ref int blocked)
    {
        CameraFollow[] followers = UnityEngine.Object.FindObjectsByType<CameraFollow>(FindObjectsInactive.Include);
        Check("gameplay scene has a CameraFollow component", followers.Length > 0, ref pass, ref fail);
        if (followers.Length == 0) return;

        CameraFollow follow = followers.FirstOrDefault(f => f.gameObject.scene.IsValid()) ?? followers[0];
        var table = SimulationDataTable.Instance;
        if (table == null || table.gameObject.scene != follow.gameObject.scene)
        {
            var sceneTables = UnityEngine.Object.FindObjectsByType<SimulationDataTable>(FindObjectsInactive.Include);
            table = sceneTables.FirstOrDefault(candidate => candidate.gameObject.scene == follow.gameObject.scene);
        }
        ulong tickBefore = table != null ? table.CurrentTick : 0;
        SimulationEntityRecord ballBefore = default;
        bool hasBallBefore = table != null && table.IsInitialized && table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out ballBefore);
        Vector3 startingPosition = follow.transform.position;
        Quaternion startingRotation = follow.transform.rotation;
        float startingZ = startingPosition.z;
        Camera camera = follow.GetComponent<Camera>();
        float startingOrthographicSize = camera != null ? camera.orthographicSize : 0f;
        follow.ApplyFramingNow();
        Rect court = Simulation.CourtGeometry.BigRectWorld;
        Rect visible = follow.LastVisibleRect;
        bool courtContained = visible.xMin <= court.xMin + 0.001f && visible.xMax >= court.xMax - 0.001f &&
                              visible.yMin <= court.yMin + 0.001f && visible.yMax >= court.yMax - 0.001f;
        Check("camera framing contains court bounds", courtContained, ref pass, ref fail);
        Check("camera has finite target and frame diagnostics", IsFinite(follow.LastClampedTarget) &&
              !float.IsNaN(follow.LastVisibleRect.width) && !float.IsInfinity(follow.LastVisibleRect.height), ref pass, ref fail);
        Check("camera preserves its configured Z", Mathf.Abs(follow.transform.position.z - startingZ) < 0.001f, ref pass, ref fail);
        Check("camera preserves its rotation", Quaternion.Angle(follow.transform.rotation, startingRotation) < 0.001f, ref pass, ref fail);

        if (camera != null)
        {
            var serialized = new SerializedObject(follow);
            var autoFit = serialized.FindProperty("autoFitCourt");
            var targetSize = serialized.FindProperty("targetOrthographicSize");
            Check("camera exposes Inspector auto-fit and target size controls",
                autoFit != null && targetSize != null, ref pass, ref fail);
            if (autoFit != null && targetSize != null)
            {
                bool originalAuto = autoFit.boolValue;
                float originalTarget = targetSize.floatValue;
                try
                {
                    autoFit.boolValue = false;
                    targetSize.floatValue = Mathf.Max(1f, startingOrthographicSize * 0.75f);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    follow.ApplyFramingNow();
                    Check("manual Inspector target size controls orthographic framing",
                        Mathf.Abs(camera.orthographicSize - targetSize.floatValue) < 0.01f, ref pass, ref fail);
                    Check("manual framing clamps camera to finite court target",
                        IsFinite(follow.LastClampedTarget) && IsFinite(follow.transform.position), ref pass, ref fail);
                }
                finally
                {
                    serialized.Update();
                    autoFit.boolValue = originalAuto;
                    targetSize.floatValue = originalTarget;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        if (hasBallBefore && table != null && table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfter))
        {
            Check("camera update does not advance or alter native table ball data",
                table.CurrentTick == tickBefore && ballAfter.posX == ballBefore.posX && ballAfter.posY == ballBefore.posY &&
                ballAfter.trajectoryType == ballBefore.trajectoryType && ballAfter.trajA == ballBefore.trajA &&
                ballAfter.trajB == ballBefore.trajB && ballAfter.trajC == ballBefore.trajC, ref pass, ref fail);
        }
        else
        {
            blocked++;
            Debug.LogWarning("[P03_TEST][BLOCKED] native ball-table non-mutation check requires initialized Play Mode simulation");
        }

        // Structural EditMode checks must not leave a camera change in the user scene.
        follow.transform.SetPositionAndRotation(startingPosition, startingRotation);
        if (camera != null) camera.orthographicSize = startingOrthographicSize;
    }

    private static void RunNativeDrivenSpikeAnimations(ref int pass, ref int fail, ref int blocked)
    {
        if (!Application.isPlaying)
        {
            blocked++;
            Debug.LogWarning("[P03_TEST][BLOCKED] accepted native Spike-to-Animator scenarios require the gameplay scene in Play Mode");
            return;
        }

        var manager = SpikeSimulationManager.Instance;
        if (manager == null)
            manager = UnityEngine.Object.FindFirstObjectByType<SpikeSimulationManager>(FindObjectsInactive.Include);
        if (manager == null || manager.State != SpikeSimulationManager.RunState.Running ||
            manager.DataTable == null || manager.InputAdapter == null)
        {
            blocked++;
            Debug.LogWarning("[P03_TEST][BLOCKED] native manager/input/DataTable is not running in the active Play Mode scene");
            return;
        }

        CheckRepeatedOneShotActionReplayAndHoldStability(manager, ref pass, ref fail);

        var adapter = manager.InputAdapter;
        bool oldManualMode = manager.ManualTickMode;
        bool oldExclusive = adapter.InjectionExclusive;
        var binders = UnityEngine.Object.FindObjectsByType<CharacterAnimationBinder>(FindObjectsInactive.Include);
        var animators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include);
        int[] ids = { (int)SpikeAction.SpikeLight, (int)SpikeAction.SpikeMedium, (int)SpikeAction.SpikeStrong };
        uint[] configuredIds = { adapter.IdFor(SpikeAction.SpikeLight), adapter.IdFor(SpikeAction.SpikeMedium), adapter.IdFor(SpikeAction.SpikeStrong) };
        int[] actionStates = { (int)SimActionState.SpikeLight, (int)SimActionState.SpikeMedium, (int)SimActionState.SpikeStrong };
        int[] unityStates = { (int)CharacterState.SpikeLight, (int)CharacterState.SpikeMedium, (int)CharacterState.SpikeStrong };
        string[] stateNames = { "SpikeLight", "SpikeMedium", "SpikeStrong" };
        uint[] seeds = { 71u, 72u, 73u };
        int validScenarios = 0;

        manager.ManualTickMode = true;
        adapter.InjectionExclusive = true;
        try
        {
            for (int scenario = 0; scenario < 3; scenario++)
            {
                manager.ResetMatch();
                SpikeNativeBridge.Sim_SeedRandom(seeds[scenario]);
                adapter.ClearInjected();
                if (!manager.StepOnce())
                {
                    Check(stateNames[scenario] + " fixture initial native snapshot commits", false, ref pass, ref fail);
                    continue;
                }

                bool toggle = false;
                bool accepted = false;
                bool mapped = false;
                bool branchObserved = false;
                bool recovered = false;
                ulong acceptedTick = 0;
                int actionId = ids[scenario];
                int expectedStatus = actionStates[scenario];
                int expectedUnityState = unityStates[scenario];
                int expectedHash = Animator.StringToHash(stateNames[scenario]);

                for (int tick = 0; tick < 1800 && !accepted; tick++)
                {
                    var table = manager.DataTable;
                    if (!table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) ||
                        !table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball)) break;

                    toggle = !toggle;
                    uint moveAction = 0;
                    uint spikeAction = 0;
                    if (ball.stateFrame == 0 && table.MatchRecord.servingTeam == 0)
                    {
                        if (toggle) spikeAction = adapter.IdFor(SpikeAction.Serve);
                    }
                    else if (ball.stateFrame == -1 && ball.ballSpeed < 0f && ball.landingX > 0f &&
                             ball.landingX < Simulation.CourtGeometry.NetX)
                    {
                        float playerCenterX = player.posX + player.sizeX * 0.5f;
                        float playerCenterY = player.posY + player.sizeY * 0.5f;
                        float ballCenterX = ball.posX + ball.sizeX * 0.5f;
                        float ballCenterY = ball.posY + ball.sizeY * 0.5f;
                        float dx = ball.landingX + ball.sizeX * 0.5f - playerCenterX;
                        float distance = Mathf.Sqrt((ballCenterX - playerCenterX) * (ballCenterX - playerCenterX) +
                                                    (ballCenterY - playerCenterY) * (ballCenterY - playerCenterY));
                        if (dx > 7f) moveAction = adapter.IdFor(SpikeAction.Right);
                        else if (dx < -7f) moveAction = adapter.IdFor(SpikeAction.Left);
                        if (distance < 140f && toggle) spikeAction = configuredIds[scenario];
                    }

                    adapter.ClearInjected();
                    if (moveAction == adapter.IdFor(SpikeAction.Left)) adapter.SetInjected(SpikeAction.Left, true);
                    else if (moveAction == adapter.IdFor(SpikeAction.Right)) adapter.SetInjected(SpikeAction.Right, true);
                    if (spikeAction == adapter.IdFor(SpikeAction.Serve)) adapter.SetInjected(SpikeAction.Serve, true);
                    else if (spikeAction == configuredIds[scenario]) adapter.SetInjected((SpikeAction)actionId, true);

                    float oldSpeed = ball.ballSpeed;
                    int oldTrajectory = ball.trajectoryType;
                    if (!manager.StepOnce()) break;
                    var nextPlayer = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    var nextBall = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                    bool trajectoryChanged = Mathf.Abs(nextBall.ballSpeed - oldSpeed) > 0.001f || nextBall.trajectoryType != oldTrajectory;
                    if (nextPlayer.actionStatus == expectedStatus && table.MatchRecord.lastTouchEntity == 0 && trajectoryChanged)
                    {
                        accepted = true;
                        acceptedTick = table.CurrentTick;
                        mapped = manager.StateTable != null && manager.StateTable.GetState(0).Value == expectedUnityState;
                    }

                    UpdatePresentation(binders, animators);
                    if (accepted) branchObserved = AnimatorHasState(animators, 0, expectedHash);
                }

                if (!accepted)
                {
                    blocked++;
                    Debug.LogWarning($"[P03_TEST][BLOCKED] {stateNames[scenario]} has no accepted valid native scenario after 1800 configured Get_data ticks (button={configuredIds[scenario]}, seed={seeds[scenario]})");
                    continue;
                }

                // Let the accepted state reach the Animator and then recover using native output.
                for (int tail = 0; tail < 36; tail++)
                {
                    adapter.ClearInjected();
                    if (!manager.StepOnce()) break;
                    UpdatePresentation(binders, animators);
                    branchObserved |= AnimatorHasState(animators, 0, expectedHash);
                    var player = manager.DataTable.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    if (player.actionStatus == (int)SimActionState.None &&
                        manager.StateTable.GetState(0).State != (CharacterState)unityStates[scenario] &&
                        !AnimatorHasState(animators, 0, expectedHash))
                    {
                        recovered = true;
                        break;
                    }
                }

                Check($"{stateNames[scenario]} accepted native status maps to Unity state at tick {acceptedTick}", mapped, ref pass, ref fail);
                Check($"{stateNames[scenario]} accepted state reaches the runtime Animator", branchObserved, ref pass, ref fail);
                Check($"{stateNames[scenario]} animation exits after native action state clears", recovered, ref pass, ref fail);
                if (mapped && branchObserved && recovered) validScenarios++;
            }
        }
        finally
        {
            adapter.ClearInjected();
            // Leave a valid authoritative snapshot for the camera isolation checks.
            // ResetMatch resets native state; one neutral Get_data tick commits that reset.
            manager.ManualTickMode = true;
            adapter.InjectionExclusive = true;
            manager.ResetMatch();
            manager.StepOnce();
            adapter.InjectionExclusive = oldExclusive;
            manager.ManualTickMode = oldManualMode;
        }

        Debug.Log($"[P03_NATIVE_ANIM_SUMMARY] validStrengths={validScenarios}/3; configured input adapter -> Get_data -> accepted ActionState -> DataTable -> Animator");
    }

    private static void CheckRepeatedOneShotActionReplayAndHoldStability(SpikeSimulationManager manager, ref int pass, ref int fail)
    {
        var binders = UnityEngine.Object.FindObjectsByType<CharacterAnimationBinder>(FindObjectsInactive.Include);
        var animators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include);
        CharacterAnimationBinder playerBinder = binders.FirstOrDefault(b =>
        {
            var v = b != null ? b.GetComponent<CharacterView>() : null;
            return v != null && v.Id == 0;
        });
        Animator playerAnimator = animators.FirstOrDefault(a =>
        {
            var v = a != null ? a.GetComponent<CharacterView>() : null;
            return v != null && v.Id == 0;
        });
        Check("player CharacterAnimationBinder and Animator exist for P1 replay check",
            playerBinder != null && playerAnimator != null, ref pass, ref fail);
        if (playerBinder == null || playerAnimator == null) return;

        bool oldManual = manager.ManualTickMode;
        bool oldExclusive = manager.InputAdapter.InjectionExclusive;
        manager.ManualTickMode = true;
        manager.InputAdapter.InjectionExclusive = true;
        try
        {
            manager.ResetMatch();
            manager.InputAdapter.ClearInjected();
            if (!manager.StepOnce()) return;

            var baseSnap = manager.LastNativeSnapshot;
            // Tick +1: Initial accepted SpikeLight (remainTime = 1.0f)
            baseSnap.tick += 1;
            baseSnap.entity0.actionState.status = (int)SimActionState.SpikeLight;
            baseSnap.entity0.actionState.remainTime = 1.0f;
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            for (int i = 0; i < 8; i++) playerAnimator.Update(1f / 60f);

            int replaysBeforeHold = playerBinder.ReplayTriggerCount;
            // Tick +2: Held button countdown (remainTime decreases 1.0f -> 0.8f); must NOT replay
            baseSnap.tick += 1;
            baseSnap.entity0.actionState.remainTime = 0.8f;
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            Check("held action countdown does not retrigger one-shot animation",
                playerBinder.ReplayTriggerCount == replaysBeforeHold, ref pass, ref fail);

            // Tick +3: Repeated accepted SpikeLight (remainTime resets upward 0.8f -> 1.0f); MUST replay once
            baseSnap.tick += 1;
            baseSnap.entity0.actionState.remainTime = 1.0f;
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            Check("repeated accepted same-state action retriggers animation from start",
                playerBinder.ReplayTriggerCount == replaysBeforeHold + 1, ref pass, ref fail);
        }
        finally
        {
            manager.ResetMatch();
            manager.StepOnce();
            manager.InputAdapter.InjectionExclusive = oldExclusive;
            manager.ManualTickMode = oldManual;
        }
    }

    private static void UpdatePresentation(CharacterAnimationBinder[] binders, Animator[] animators)
    {
        foreach (var binder in binders) if (binder != null) binder.Update();
        foreach (var animator in animators) if (animator != null && animator.isInitialized) animator.Update(1f / 60f);
    }

    private static bool AnimatorHasState(Animator[] animators, int entityId, int shortNameHash)
    {
        foreach (var animator in animators)
        {
            if (animator == null) continue;
            var view = animator.GetComponent<CharacterView>();
            if (view == null || view.Id != entityId) continue;
            var current = animator.GetCurrentAnimatorStateInfo(0);
            var next = animator.GetNextAnimatorStateInfo(0);
            if (current.shortNameHash == shortNameHash || next.shortNameHash == shortNameHash) return true;
        }
        return false;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static AnimatorStateMachine FindMachine(AnimatorStateMachine machine, string name)
    {
        if (machine == null) return null;
        if (machine.name == name) return machine;
        foreach (var child in machine.stateMachines)
        {
            AnimatorStateMachine found = FindMachine(child.stateMachine, name);
            if (found != null) return found;
        }
        return null;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name) =>
        machine == null ? null : machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    private static System.Collections.Generic.IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
    {
        if (machine == null) yield break;
        foreach (var child in machine.states) yield return child.state;
        foreach (var sub in machine.stateMachines)
            foreach (var state in EnumerateStates(sub.stateMachine)) yield return state;
    }

    private static bool HasStateCondition(AnimatorState source, AnimatorState destination, int value)
    {
        if (source == null || destination == null) return false;
        return source.transitions.Any(t => t.destinationState == destination && !t.hasExitTime &&
            t.conditions.Any(c => c.parameter == "State" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, value)));
    }

    private static bool HasAnyStateCondition(AnimatorStateMachine machine, AnimatorState destination, int value)
    {
        if (machine == null || destination == null) return false;
        return machine.anyStateTransitions.Any(t => t.destinationState == destination && !t.hasExitTime &&
            !t.canTransitionToSelf &&
            t.conditions.Any(c => c.parameter == "State" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, value)));
    }

    private static void Check(string name, bool ok, ref int pass, ref int fail)
    {
        if (ok) { pass++; Debug.Log("[P03_TEST][PASS] " + name); }
        else { fail++; Debug.LogError("[P03_TEST][FAIL] " + name); }
    }
}
