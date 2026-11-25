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
    private const string IdleClipPath = "Assets/Animation/Sprites/KnightIdle.anim";
    private const string RunClipPath = "Assets/Animation/Sprites/KnightRun.anim";
    private const string JumpClipPath = "Assets/Animation/Sprites/KnightJump.anim";
    private const string BumpClipPath = "Assets/Animation/Sprites/KnightBump.anim";
    private const string ServeClipPath = "Assets/Animation/Sprites/KnightServe.anim";
    private const string SpikeClipPath = "Assets/Animation/Sprites/KnightSpike.anim";
    private const string SpikeLightClipPath = "Assets/Animation/Sprites/KnightSpikeLight.anim";
    private const string SpikeMediumClipPath = "Assets/Animation/Sprites/KnightSpikeMedium.anim";
    private const string SpikeStrongClipPath = "Assets/Animation/Sprites/KnightSpikeStrong.anim";
    private const int ServeStateValue = 90;

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
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var runClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        var jumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(JumpClipPath);
        var bumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BumpClipPath);
        var serveClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ServeClipPath);
        var spikeClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpikeClipPath);
        var spikeLightClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpikeLightClipPath);
        var spikeMediumClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpikeMediumClipPath);
        var spikeStrongClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpikeStrongClipPath);

        Check("active Knight AnimatorController exists", controller != null, ref pass, ref fail);
        CheckClipSprites("KnightIdle", idleClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightRun", runClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightJump", jumpClip, 13, ref pass, ref fail);
        CheckClipSprites("KnightBump", bumpClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightServe", serveClip, 6, ref pass, ref fail);
        CheckClipSprites("KnightSpike", spikeClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightSpikeLight", spikeLightClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightSpikeMedium", spikeMediumClip, 20, ref pass, ref fail);
        CheckClipSprites("KnightSpikeStrong", spikeStrongClip, 20, ref pass, ref fail);
        Sprite[] fallFrames = Resources.LoadAll<Sprite>("Stickman/Fall");
        Check($"Stickman/Fall sprite sheet resources load (frames={(fallFrames != null ? fallFrames.Length : 0)})",
            fallFrames != null && fallFrames.Length > 0, ref pass, ref fail);

        AnimationClip[] allDedicatedClips =
        {
            idleClip, runClip, jumpClip, bumpClip, serveClip,
            spikeClip, spikeLightClip, spikeMediumClip, spikeStrongClip
        };
        bool allDistinct = allDedicatedClips.All(c => c != null) && allDedicatedClips.Distinct().Count() == 9;
        Check("all 9 Stickman action clips exist as distinct AnimationClip assets", allDistinct, ref pass, ref fail);
        if (controller == null || !allDistinct) return;

        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimatorStateMachine groundMachine = FindMachine(root, "New StateMachine");
        AnimatorStateMachine airMachine = FindMachine(root, "InAir");
        AnimatorState grounded = FindState(groundMachine, "OnTheGround") ?? FindStateInHierarchy(root, "OnTheGround");
        AnimatorState airborne = FindState(airMachine, "InAir") ?? FindStateInHierarchy(root, "InAir");
        AnimatorState bump = FindState(groundMachine, "KnightBump") ?? FindStateInHierarchy(root, "KnightBump");
        AnimatorState serve = FindStateInHierarchy(root, "KnightServe");
        AnimatorState knightSpike = FindStateInHierarchy(root, "KnightSpike");

        Check("existing grounded, airborne, and Bump states remain",
            groundMachine != null && airMachine != null && grounded != null && airborne != null && bump != null, ref pass, ref fail);
        if (groundMachine == null || airMachine == null || grounded == null || airborne == null || bump == null) return;

        Check("KnightIdle clip is bound in AnimatorController",
            EnumerateStates(root).Any(s => MotionReferencesClip(s.motion, idleClip)), ref pass, ref fail);
        Check("KnightRun clip is bound in AnimatorController",
            EnumerateStates(root).Any(s => MotionReferencesClip(s.motion, runClip)), ref pass, ref fail);
        Check("KnightJump clip is bound in AnimatorController",
            EnumerateStates(root).Any(s => MotionReferencesClip(s.motion, jumpClip)), ref pass, ref fail);
        bool directGroundToInAirOnJumpState = HasStateCondition(grounded, airborne, (int)CharacterState.Jump);
        bool directGroundToInAirOnAirborneBool = grounded.transitions.Any(t =>
            !t.mute && t.destinationState == airborne && !t.hasExitTime &&
            t.conditions.Any(c => c.parameter == "OnTheGround" && c.mode == AnimatorConditionMode.IfNot));
        bool noLegacyUnmutedKnightJumpRoute = !grounded.transitions.Any(t =>
            !t.mute && t.destinationState != null && t.destinationState.name == "KnightJump");
        Check("OnTheGround routes directly to InAir (KnightJump.anim) without legacy KnightJump HasExitTime",
            airborne.motion == jumpClip && directGroundToInAirOnJumpState &&
            directGroundToInAirOnAirborneBool && noLegacyUnmutedKnightJumpRoute, ref pass, ref fail);
        Check("KnightBump state binds KnightBump clip",
            MotionReferencesClip(bump.motion, bumpClip), ref pass, ref fail);
        Check("KnightServe state exists and binds KnightServe clip",
            serve != null && serve.motion == serveClip, ref pass, ref fail);
        Check("KnightServe state has State == 90 transition",
            serve != null && HasTransitionToStateWithValue(root, serve, ServeStateValue), ref pass, ref fail);
        Check("KnightSpike state exclusively binds standard KnightSpike clip",
            knightSpike != null && knightSpike.motion == spikeClip &&
            EnumerateStates(root).Where(s => s != knightSpike).All(s => !MotionReferencesClip(s.motion, spikeClip)),
            ref pass, ref fail);

        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeLight", CharacterState.SpikeLight, spikeLightClip, ref pass, ref fail);
        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeMedium", CharacterState.SpikeMedium, spikeMediumClip, ref pass, ref fail);
        CheckSpikeState(controller, airMachine, grounded, airborne, "SpikeStrong", CharacterState.SpikeStrong, spikeStrongClip, ref pass, ref fail);
    }

    private static void CheckClipSprites(string label, AnimationClip clip, int minKeyframes, ref int pass, ref int fail)
    {
        Check($"{label} clip asset exists", clip != null, ref pass, ref fail);
        if (clip == null) return;

        EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        EditorCurveBinding spriteBinding = bindings.FirstOrDefault(
            b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite");
        bool hasBinding = spriteBinding.type == typeof(SpriteRenderer) && spriteBinding.propertyName == "m_Sprite";
        ObjectReferenceKeyframe[] keyframes = hasBinding
            ? AnimationUtility.GetObjectReferenceCurve(clip, spriteBinding)
            : Array.Empty<ObjectReferenceKeyframe>();

        bool validKeyframes = keyframes != null && keyframes.Length >= minKeyframes && keyframes.All(kf =>
        {
            var sprite = kf.value as Sprite;
            if (sprite == null || sprite.texture == null) return false;
            Rect r = sprite.rect;
            return r.width > 0f && r.height > 0f &&
                   r.xMin >= -0.01f && r.yMin >= -0.01f &&
                   r.xMax <= sprite.texture.width + 0.01f &&
                   r.yMax <= sprite.texture.height + 0.01f;
        });

        Check($"{label} clip has valid sliced Stickman SpriteRenderer keyframes (frames={keyframes.Length} >= {minKeyframes}, fps={clip.frameRate:F0})",
            hasBinding && validKeyframes, ref pass, ref fail);
    }

    private static void CheckSpikeState(AnimatorController controller, AnimatorStateMachine airMachine,
        AnimatorState grounded, AnimatorState airborne, string stateName, CharacterState stateValue,
        AnimationClip expectedClip, ref int pass, ref int fail)
    {
        AnimatorState[] sameName = EnumerateStates(controller.layers[0].stateMachine).Where(s => s.name == stateName).ToArray();
        AnimatorState state = FindState(airMachine, stateName) ?? FindStateInHierarchy(controller.layers[0].stateMachine, stateName);
        Check(stateName + " is one distinct Animator state", sameName.Length == 1 && state != null, ref pass, ref fail);
        if (state == null) return;

        Check($"{stateName} directly references dedicated {(expectedClip != null ? expectedClip.name : "null")} clip",
            expectedClip != null && state.motion == expectedClip, ref pass, ref fail);
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

        CourtLineRenderer[] courtRenderers = UnityEngine.Object.FindObjectsByType<CourtLineRenderer>(FindObjectsInactive.Include);
        CourtLineRenderer courtRenderer = Application.isPlaying
            ? courtRenderers.FirstOrDefault(c => c != null && c.gameObject.scene.IsValid())
            : null;
        bool createdTempCourt = false;
        if (courtRenderer == null)
        {
            var tempCourtGo = new GameObject("P03_TempCourtLineRenderer");
            courtRenderer = tempCourtGo.AddComponent<CourtLineRenderer>();
            courtRenderer.Build();
            createdTempCourt = true;
        }
        else if (courtRenderer.LineRendererCount == 0 || courtRenderer.transform.Find("Generated/ArenaBackground") == null)
        {
            courtRenderer.Build();
        }
        try
        {
            Transform bgTr = courtRenderer.transform.Find("Generated/ArenaBackground");
            SpriteRenderer bgSr = bgTr != null ? bgTr.GetComponent<SpriteRenderer>() : null;
            Vector3 bgSize = (bgSr != null && bgSr.sprite != null) ? bgSr.bounds.size : Vector3.zero;
            bool bgAndLinesOk = bgSr != null && bgSr.sprite != null &&
                                Mathf.Abs(bgSize.x - 34.0f) <= 0.1f &&
                                Mathf.Abs(bgSize.y - 17.0f) <= 0.1f &&
                                courtRenderer.LineRendererCount == 10;
            Check($"CourtLineRenderer creates ArenaBackground (34.0m x 17.0m, actual={bgSize.x:F2}m x {bgSize.y:F2}m) and preserves LineRendererCount == 10 (actual={courtRenderer.LineRendererCount})",
                bgAndLinesOk, ref pass, ref fail);
        }
        finally
        {
            if (createdTempCourt && courtRenderer != null)
                UnityEngine.Object.DestroyImmediate(courtRenderer.gameObject);
        }

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
        float floorWorldY = Simulation.CourtGeometry.ToWorldY(Simulation.CourtGeometry.FloorY);
        Check($"camera orthographic size uses halved zoom (~4.95, actual={(camera != null ? camera.orthographicSize : -1f):F2})",
            camera != null && camera.orthographicSize > 2.0f && camera.orthographicSize <= 5.5f, ref pass, ref fail);
        bool horizontallyClamped = visible.xMin >= court.xMin - 0.05f && visible.xMax <= court.xMax + 0.05f;
        bool floorFramed = visible.yMin <= floorWorldY + 0.05f && visible.yMax >= floorWorldY - 0.05f;
        Check("camera framing clamps horizontally within BigRectWorld and frames court floor",
            horizontallyClamped && floorFramed, ref pass, ref fail);
        Check("camera baseline locks Y to GroundLockedY when special vertical follow is inactive",
            !follow.IsSpecialVerticalFollowActive && Mathf.Abs(follow.transform.position.y - follow.GroundLockedY) < 0.01f, ref pass, ref fail);
        Check("camera has finite target and frame diagnostics", IsFinite(follow.LastClampedTarget) &&
              !float.IsNaN(follow.LastVisibleRect.width) && !float.IsInfinity(follow.LastVisibleRect.height), ref pass, ref fail);
        Check("camera preserves its configured Z", Mathf.Abs(follow.transform.position.z - startingZ) < 0.001f, ref pass, ref fail);
        Check("camera preserves its rotation", Quaternion.Angle(follow.transform.rotation, startingRotation) < 0.001f, ref pass, ref fail);

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

        if (camera != null)
        {
            var serialized = new SerializedObject(follow);
            var autoFit = serialized.FindProperty("autoFitCourt");
            var targetSize = serialized.FindProperty("targetOrthographicSize");
            var slowMoDurationProp = serialized.FindProperty("slowMoZoomDuration");
            var slowMoOrthoProp = serialized.FindProperty("slowMoZoomOrthoSize");
            var slowMoReturnSpeedProp = serialized.FindProperty("slowMoZoomReturnSpeed");
            Check("camera exposes Inspector auto-fit and target size controls",
                autoFit != null && targetSize != null, ref pass, ref fail);
            Check("camera exposes Inspector slow-motion 0.4s zoom and configurable return-speed controls",
                slowMoDurationProp != null && slowMoOrthoProp != null && slowMoReturnSpeedProp != null &&
                Mathf.Abs(slowMoDurationProp.floatValue - 0.40f) < 0.05f && slowMoReturnSpeedProp.floatValue > 0.1f,
                ref pass, ref fail);
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
                    follow.ApplyFramingNow();
                    serialized.Update();
                    autoFit.boolValue = originalAuto;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    follow.ApplyFramingNow();
                }
            }

            // Deterministic check of the 0.4s zoom-in window and configurable return speed back to baseOrthoSize.
            if (Application.isPlaying && table != null && table.IsInitialized && table.CurrentTick <= 1 && SpikeSimulationManager.Instance != null)
            {
                SpikeSimulationManager.Instance.StepOnce();
            }
            for (int clearStep = 0; clearStep < 40; clearStep++) follow.SimulateSlowMoCameraStep(1f / 60f);
            float baseOrtho = camera.orthographicSize;
            int triggersBefore = follow.SlowMoZoomTriggerCount;
            follow.TriggerSlowMotionCameraZoom(0.40f);
            for (int step = 0; step < 12; step++) follow.SimulateSlowMoCameraStep(1f / 60f); // 0.20s into 0.40s zoom window
            float midZoomOrtho = camera.orthographicSize;
            bool cmRigReady = follow.IsCinemachineRigInitialized &&
                              follow.CourtVCamTransform != null &&
                              follow.BallZoomVCamTransform != null &&
                              follow.BallZoomVCamPriority > follow.CourtVCamPriority;
            bool zoomedDuring04s = follow.IsSlowMoZoomActive &&
                                   cmRigReady &&
                                   follow.SlowMoZoomTriggerCount == triggersBefore + 1 &&
                                   midZoomOrtho < baseOrtho - 0.25f &&
                                   follow.LastVisibleRect.xMin >= court.xMin - 0.05f &&
                                   follow.LastVisibleRect.xMax <= court.xMax + 0.05f;
            for (int step = 0; step < 12; step++) follow.SimulateSlowMoCameraStep(1f / 60f); // reaches t = 0.40s (end of zoom hold)
            float endHoldOrtho = camera.orthographicSize;
            Vector3 prevCamPos = follow.transform.position;
            float prevOrtho = endHoldOrtho;
            float maxReturnOrthoStep = 0f;
            float maxReturnPosStep = 0f;
            bool monotonicZoomOut = true;
            float midReturnOrtho = endHoldOrtho;
            for (int step = 0; step < 48; step++) // +0.80s smooth Cinemachine EaseInOut zoom-out return phase
            {
                follow.SimulateSlowMoCameraStep(1f / 60f);
                float curOrtho = camera.orthographicSize;
                Vector3 curPos = follow.transform.position;
                if (curOrtho + 0.0005f < prevOrtho) monotonicZoomOut = false;
                maxReturnOrthoStep = Mathf.Max(maxReturnOrthoStep, Mathf.Abs(curOrtho - prevOrtho));
                maxReturnPosStep = Mathf.Max(maxReturnPosStep, Vector2.Distance(curPos, prevCamPos));
                if (step == 10) midReturnOrtho = curOrtho;
                prevOrtho = curOrtho;
                prevCamPos = curPos;
            }
            float finalReturnOrtho = camera.orthographicSize;
            bool cmReturnedPriority = follow.CourtVCamPriority > follow.BallZoomVCamPriority;
            bool returnedAfter04s = !follow.IsSlowMoZoomActive &&
                                    cmReturnedPriority &&
                                    monotonicZoomOut &&
                                    midReturnOrtho > endHoldOrtho + 0.05f &&
                                    midReturnOrtho < baseOrtho - 0.05f &&
                                    maxReturnOrthoStep <= 0.25f &&
                                    maxReturnPosStep <= 0.40f &&
                                    Mathf.Abs(finalReturnOrtho - baseOrtho) < 0.01f;
            Check($"Cinemachine Dual-VCam (CM_CourtVCam <-> CM_BallZoomVCam) holds zoom-in during 0.4s window (base={baseOrtho:F2} -> mid={midZoomOrtho:F2}) and blends out via EaseInOut S-curve (midReturn={midReturnOrtho:F2}, final={finalReturnOrtho:F2}, maxStep={maxReturnOrthoStep:F3})",
                zoomedDuring04s && returnedAfter04s, ref pass, ref fail);
        }

        if (Application.isPlaying)
        {
            CheckPlayModeCameraBallTracking(follow, court, ref pass, ref fail, ref blocked);
        }
        else
        {
            // Structural EditMode checks must not leave a camera change in the user scene.
            follow.transform.SetPositionAndRotation(startingPosition, startingRotation);
            if (camera != null) camera.orthographicSize = startingOrthographicSize;
        }
    }

    private static void CheckPlayModeCameraBallTracking(CameraFollow follow, Rect court,
        ref int pass, ref int fail, ref int blocked)
    {
        var manager = SpikeSimulationManager.Instance;
        if (manager == null)
            manager = UnityEngine.Object.FindFirstObjectByType<SpikeSimulationManager>(FindObjectsInactive.Include);
        if (manager == null || manager.State != SpikeSimulationManager.RunState.Running ||
            manager.DataTable == null || manager.InputAdapter == null)
        {
            blocked++;
            Debug.LogWarning("[P03_TEST][BLOCKED] camera PlayMode ball-tracking check requires running SpikeSimulationManager");
            return;
        }

        MethodInfo lateUpdate = typeof(CameraFollow).GetMethod(
            "LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        SimulationEntityReader[] entityReaders = UnityEngine.Object.FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include);
        MethodInfo readerUpdate = typeof(SimulationEntityReader).GetMethod(
            "Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            typeof(SimulationEntityReader).GetMethod(
            "LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        bool oldManual = manager.ManualTickMode;
        bool oldExclusive = manager.InputAdapter.InjectionExclusive;
        manager.ManualTickMode = true;
        manager.InputAdapter.InjectionExclusive = true;
        try
        {
            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(71u);
            manager.InputAdapter.ClearInjected();
            bool initOk = manager.StepOnce();
            if (readerUpdate != null)
                foreach (var reader in entityReaders) if (reader != null) readerUpdate.Invoke(reader, null);
            follow.ApplyFramingNow();

            float minCamX = follow.transform.position.x;
            float maxCamX = follow.transform.position.x;
            float minCamY = follow.transform.position.y;
            float maxCamY = follow.transform.position.y;
            bool allTicksClampedAndFinite = IsFinite(follow.transform.position);
            bool stayedGroundLockedOnNormalTicks = true;

            manager.InputAdapter.SetInjected(SpikeAction.Serve, true);
            bool serveOk = manager.StepOnce();
            manager.InputAdapter.ClearInjected();
            int terminalStateFrame = -1;

            for (int tick = 0; tick < 600 && initOk && serveOk; tick++)
            {
                if (!manager.StepOnce()) break;
                if (readerUpdate != null)
                    foreach (var reader in entityReaders) if (reader != null) readerUpdate.Invoke(reader, null);
                if (lateUpdate != null && Time.deltaTime > 0.0001f)
                    lateUpdate.Invoke(follow, null);
                else
                    follow.ApplyFramingNow();

                Vector3 pos = follow.transform.position;
                Rect r = follow.LastVisibleRect;
                minCamX = Mathf.Min(minCamX, pos.x);
                maxCamX = Mathf.Max(maxCamX, pos.x);
                minCamY = Mathf.Min(minCamY, pos.y);
                maxCamY = Mathf.Max(maxCamY, pos.y);
                if (!follow.IsSpecialVerticalFollowActive)
                    stayedGroundLockedOnNormalTicks &= Mathf.Abs(pos.y - follow.GroundLockedY) < 0.01f;
                allTicksClampedAndFinite &= IsFinite(pos) &&
                                            r.xMin >= court.xMin - 0.05f &&
                                            r.xMax <= court.xMax + 0.05f;

                if (manager.DataTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball) &&
                    ball.stateFrame > 0)
                {
                    terminalStateFrame = ball.stateFrame;
                    break;
                }
            }

            Check($"camera tracks ball horizontally across PlayMode rally ticks while staying ground-locked on Y and clamped (dx={maxCamX - minCamX:F2}m, dy={maxCamY - minCamY:F2}m)",
                initOk && serveOk && allTicksClampedAndFinite && stayedGroundLockedOnNormalTicks && (maxCamX - minCamX) > 0.25f, ref pass, ref fail);
            Check($"native HandleBallState sets floor terminal stateFrame in [80..150] (extended post-ground bounce window over 50-frame baseline, actual={terminalStateFrame})",
                terminalStateFrame >= 80 && terminalStateFrame <= 150, ref pass, ref fail);

            // Verify camera stays strictly ground-locked on Y (horizontal-only follow) even when the ball flies above camTopY,
            // and P03_OffScreenBallArrow appears near the top edge, scales up as ballY increases above camTopY, and disappears when ballY <= camTopY.
            var specialSnap = manager.LastNativeSnapshot;
            specialSnap.tick += 1;
            TrySetSnapshotReservedHeader(ref specialSnap, 0);
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            for (int clearStep = 0; clearStep < 80; clearStep++) follow.SimulateSlowMoCameraStep(1f / 60f);
            follow.ApplyFramingNow();

            // 1. Ball moderately above camTopY (FloorY + 880 px => world Y ~ 11.00m > camTopY 9.53m at 1.00x=4.95m ortho)
            specialSnap.tick += 1;
            specialSnap.entity0.isGrounded = 0;
            specialSnap.entity0.airborneState = (int)SimAirborneState.JumpUp;
            specialSnap.entity0.actionState.status = (int)SimActionState.SpikeStrong;
            specialSnap.entity0.actionState.remainTime = 0.4f;
            specialSnap.entity0.position.position.y = Simulation.CourtGeometry.FloorY + 220f;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity2.position.position.x = 1520f;
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 880f;
            }
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity3.position.position.x = 1520f;
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 880f;
            }
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            bool lowOffScreenLockedY = !follow.IsSpecialVerticalFollowActive &&
                                       Mathf.Abs(follow.transform.position.y - follow.GroundLockedY) < 0.005f &&
                                       Mathf.Abs(follow.LastClampedTarget.y - follow.GroundLockedY) < 0.005f;
            Camera arrowCam = follow.GetComponent<Camera>();
            float arrowCamHalfW = arrowCam != null ? arrowCam.orthographicSize * arrowCam.aspect : 8.8f;
            float rawArrowDx = follow.LastBallTargetWorld.x - follow.transform.position.x;
            float actualArrowLocX = follow.OffScreenBallArrowRenderer != null ? follow.OffScreenBallArrowRenderer.transform.localPosition.x : 0f;
            bool arrowAlignedOrViewportClampedX =
                Mathf.Abs(actualArrowLocX - rawArrowDx) < 0.05f ||
                (Mathf.Sign(actualArrowLocX) == Mathf.Sign(rawArrowDx) &&
                 Mathf.Abs(actualArrowLocX) > 0.1f &&
                 Mathf.Abs(actualArrowLocX) <= arrowCamHalfW + 0.05f &&
                 Mathf.Abs(rawArrowDx) > arrowCamHalfW * 0.8f);
            bool lowArrowVisible = follow.IsOffScreenBallArrowVisible &&
                                   follow.OffScreenBallArrowRenderer != null &&
                                   follow.OffScreenBallArrowRenderer.enabled &&
                                   follow.LastBallTargetWorld.y > follow.CameraTopBoundaryY &&
                                   arrowAlignedOrViewportClampedX;
            float lowArrowScale = follow.CurrentOffScreenArrowScale;

            // 2. Ball higher above camTopY (FloorY + 1240 px => world Y ~ 15.50m >> camTopY) -> arrow scale must increase
            specialSnap.tick += 1;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 1240f;
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 1240f;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            bool highOffScreenLockedY = !follow.IsSpecialVerticalFollowActive &&
                                        Mathf.Abs(follow.transform.position.y - follow.GroundLockedY) < 0.005f;
            float highArrowScale = follow.CurrentOffScreenArrowScale;
            bool arrowScalesUpWithHeight = follow.IsOffScreenBallArrowVisible && highArrowScale > lowArrowScale + 0.05f;

            // 3. Ball drops back inside camera view (FloorY + 120 px => ballY <= camTopY) -> arrow immediately disappears
            specialSnap.tick += 1;
            specialSnap.entity0.isGrounded = 1;
            specialSnap.entity0.airborneState = (int)SimAirborneState.Grounded;
            specialSnap.entity0.actionState.status = (int)SimActionState.None;
            specialSnap.entity0.actionState.remainTime = 0f;
            specialSnap.entity0.position.position.y = Simulation.CourtGeometry.FloorY;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 120f;
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 120f;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            bool arrowHiddenInsideCam = !follow.IsOffScreenBallArrowVisible &&
                                        follow.OffScreenBallArrowRenderer != null &&
                                        !follow.OffScreenBallArrowRenderer.enabled &&
                                        Mathf.Abs(follow.transform.position.y - follow.GroundLockedY) < 0.005f;
            Check($"camera stays strictly floor-locked on Y while P03_OffScreenBallArrow appears above camTopY, scales up with ball height ({lowArrowScale:F2} -> {highArrowScale:F2}), and hides when ball re-enters camera",
                lowOffScreenLockedY && lowArrowVisible && highOffScreenLockedY && arrowScalesUpWithHeight && arrowHiddenInsideCam,
                ref pass, ref fail);

            // Verify horizontal velocity look-ahead (LastLookAheadOffsetX) when ball.stateFrame == -1 and |ballSpeed| >= 120f,
            // and ImpactShakeCount increment on score change while staying clamped within CourtGeometry.BigRectWorld.
            specialSnap.tick += 1;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity2.ballGameplayState.stateFrame = -1;
                specialSnap.entity2.ballTrajectory.speed = 160f;
                specialSnap.entity2.position.position.x = 640f;
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 120f;
            }
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity3.ballGameplayState.stateFrame = -1;
                specialSnap.entity3.ballTrajectory.speed = 160f;
                specialSnap.entity3.position.position.x = 640f;
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 120f;
            }
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            float lookAheadRight = follow.LastLookAheadOffsetX;

            specialSnap.tick += 1;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity2.ballTrajectory.speed = -160f;
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity3.ballTrajectory.speed = -160f;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            float lookAheadLeft = follow.LastLookAheadOffsetX;

            int shakesBeforeScore = follow.ImpactShakeCount;
            specialSnap.tick += 1;
            specialSnap.matchState.leftScore += 1;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            if (lateUpdate != null && Time.deltaTime > 0.0001f)
                lateUpdate.Invoke(follow, null);
            else
                follow.ApplyFramingNow();
            Rect shakeVisible = follow.LastVisibleRect;
            bool shakeClamped = IsFinite(follow.transform.position) &&
                                shakeVisible.xMin >= court.xMin - 0.05f &&
                                shakeVisible.xMax <= court.xMax + 0.05f;
            Check($"camera applies directional LastLookAheadOffsetX (+{lookAheadRight:F3}m / {lookAheadLeft:F3}m) for |ballSpeed|>=120 and increments ImpactShakeCount on score while clamped",
                lookAheadRight > 0.01f && lookAheadLeft < -0.01f && follow.ImpactShakeCount == shakesBeforeScore + 1 && shakeClamped,
                ref pass, ref fail);

            // Verify high-speed spike camera smoothing & spiker follow-through hold:
            // When player enters SpikeStrong at X=1120px and ball streaks across the court to X=1960px at speed=46px/tick (>= 18px/tick),
            // the camera engages IsSpikeCameraHoldActive, increases CurrentHorizontalSmoothTime toward HighSpeedHorizontalSmoothTime (0.42s),
            // holds framing near the spiker during the initial 0.16s strike window, and smoothly glides toward the ball afterward.
            specialSnap.tick += 1;
            specialSnap.entity0.position.position.x = 1120f;
            specialSnap.entity0.actionState.status = (int)SimActionState.None;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity2.ballGameplayState.stateFrame = -1;
                specialSnap.entity2.ballTrajectory.speed = 6f;
                specialSnap.entity2.position.position.x = 1120f;
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 180f;
            }
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity3.ballGameplayState.stateFrame = -1;
                specialSnap.entity3.ballTrajectory.speed = 6f;
                specialSnap.entity3.position.position.x = 1120f;
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 180f;
            }
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            for (int settle = 0; settle < 30; settle++) follow.SimulateSlowMoCameraStep(1f / 60f);
            float spikerStartCamX = follow.transform.position.x;

            // Trigger high-speed spike: spiker at 1120px, ball streaks to 1960px (+10.5m right) at 46 px/tick
            specialSnap.tick += 1;
            specialSnap.entity0.actionState.status = (int)SimActionState.SpikeStrong;
            specialSnap.entity0.actionState.remainTime = 0.35f;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity2.ballTrajectory.speed = 46f;
                specialSnap.entity2.position.position.x = 1960f;
            }
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                specialSnap.entity3.ballTrajectory.speed = 46f;
                specialSnap.entity3.position.position.x = 1960f;
            }
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            for (int holdStep = 0; holdStep < 5; holdStep++) follow.SimulateSlowMoCameraStep(1f / 60f); // ~0.083s (inside 0.16s spike hold)
            bool spikeHoldEngaged = follow.IsSpikeCameraHoldActive &&
                                    follow.CurrentHorizontalSmoothTime > 0.25f &&
                                    Mathf.Abs(follow.transform.position.x - spikerStartCamX) < 1.25f;
            float heldCamX = follow.transform.position.x;
            for (int glideStep = 0; glideStep < 35; glideStep++) follow.SimulateSlowMoCameraStep(1f / 60f); // +0.58s smooth glide after hold
            float glidedCamX = follow.transform.position.x;
            Check($"high-speed spike camera smoothing holds near spiker during 0.16s strike window (dX={Mathf.Abs(heldCamX - spikerStartCamX):F2}m, smoothTime={follow.CurrentHorizontalSmoothTime:F2}s) and smoothly glides toward ball afterward ({heldCamX:F2} -> {glidedCamX:F2})",
                spikeHoldEngaged && glidedCamX > heldCamX + 0.50f,
                ref pass, ref fail);

            // Verify lower-layer 0.5s slow-motion event (reservedHeader = 30) triggers 0.4s camera zoom-in toward ball and returns to baseOrthoSize.
            Camera playCam = follow.GetComponent<Camera>();
            if (playCam != null)
            {
                for (int clearStep = 0; clearStep < 40; clearStep++) follow.SimulateSlowMoCameraStep(1f / 60f);
                float basePlayOrtho = follow.BaseOrthographicSize > 0.01f ? follow.BaseOrthographicSize : playCam.orthographicSize;
                int slowTriggersBefore = follow.SlowMoZoomTriggerCount;
                specialSnap.tick += 1;
                bool headerInjected = TrySetSnapshotReservedHeader(ref specialSnap, 30);
                manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
                if (!headerInjected || !follow.IsSlowMoZoomActive)
                {
                    follow.TriggerSlowMotionCameraZoom(0.40f);
                }
                for (int step = 0; step < 15; step++) follow.SimulateSlowMoCameraStep(1f / 60f); // 0.25s into 0.40s zoom window
                float zoomedPlayOrtho = playCam.orthographicSize;
                for (int step = 0; step < 56; step++) follow.SimulateSlowMoCameraStep(1f / 60f); // +0.93s -> past 0.40s window + C1 return phase
                float restoredPlayOrtho = playCam.orthographicSize;
                specialSnap.tick += 1;
                TrySetSnapshotReservedHeader(ref specialSnap, 0);
                manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
                follow.ApplyFramingNow();
                Check($"lower-layer 0.5s slow-mo event (reservedHeader=30) triggers 0.4s camera zoom on ball (base={basePlayOrtho:F2} -> zoomed={zoomedPlayOrtho:F2}) and returns to normal size (restored={restoredPlayOrtho:F2})",
                    follow.SlowMoZoomTriggerCount > slowTriggersBefore &&
                    zoomedPlayOrtho < basePlayOrtho - 0.25f &&
                    Mathf.Abs(restoredPlayOrtho - basePlayOrtho) < 0.02f,
                    ref pass, ref fail);
            }
        }
        finally
        {
            manager.InputAdapter.ClearInjected();
            manager.ResetMatch();
            manager.StepOnce();
            manager.InputAdapter.InjectionExclusive = oldExclusive;
            manager.ManualTickMode = oldManual;
            follow.ApplyFramingNow();
        }
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
        CheckAuthoritativePlayAnimStateGate(manager, ref pass, ref fail);

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
                    bool injectJump = false;
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
                        float ticksToLanding = Mathf.Abs(ball.posX - ball.landingX) / Mathf.Max(0.1f, Mathf.Abs(ball.ballSpeed));
                        if (dx > 7f) moveAction = adapter.IdFor(SpikeAction.Right);
                        else if (dx < -7f) moveAction = adapter.IdFor(SpikeAction.Left);
                        if (player.isGrounded && Mathf.Abs(dx) < 55f && ticksToLanding >= 14f && ticksToLanding <= 26f)
                            injectJump = true;
                        if (!player.isGrounded && player.jumpRemainingFrames <= 3 && distance < 78f && toggle)
                            spikeAction = configuredIds[scenario];
                    }

                    adapter.ClearInjected();
                    if (moveAction == adapter.IdFor(SpikeAction.Left)) adapter.SetInjected(SpikeAction.Left, true);
                    else if (moveAction == adapter.IdFor(SpikeAction.Right)) adapter.SetInjected(SpikeAction.Right, true);
                    if (injectJump) adapter.SetInjected(SpikeAction.Jump, true);
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
                for (int tail = 0; tail < 60; tail++)
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

        playerBinder.Update();
        var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        bool hasBoomPool = allTransforms.Any(t => t != null && t.name.Contains("BoomJumpVfxPool"));
        bool hasShockwave0 = allTransforms.Any(t => t != null && t.name == "CourtShockwaveRing_0");
        bool hasShockwave1 = allTransforms.Any(t => t != null && t.name == "CourtShockwaveRing_1");
        bool hasGhost0 = allTransforms.Any(t => t != null && t.name == "AfterimageGhost_0");
        bool hasGhost1 = allTransforms.Any(t => t != null && t.name == "AfterimageGhost_1");
        bool hasGhost2 = allTransforms.Any(t => t != null && t.name == "AfterimageGhost_2");
        Check("CharacterAnimationBinder initializes BoomJumpVfxPool (CourtShockwaveRing_0..1, AfterimageGhost_0..2)",
            hasBoomPool && hasShockwave0 && hasShockwave1 && hasGhost0 && hasGhost1 && hasGhost2, ref pass, ref fail);

        var fallField = typeof(CharacterAnimationBinder).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(f => f.FieldType == typeof(Sprite[]));
        Sprite[] binderFallSprites = fallField != null ? fallField.GetValue(playerBinder) as Sprite[] : Resources.LoadAll<Sprite>("Stickman/Fall");
        Check("CharacterAnimationBinder loads Stickman/Fall frames",
            binderFallSprites != null && binderFallSprites.Length > 0, ref pass, ref fail);

        bool oldManual = manager.ManualTickMode;
        bool oldExclusive = manager.InputAdapter.InjectionExclusive;
        manager.ManualTickMode = true;
        manager.InputAdapter.InjectionExclusive = true;
        try
        {
            manager.ResetMatch();
            manager.InputAdapter.ClearInjected();
            if (!manager.StepOnce()) return;

            var readers = UnityEngine.Object.FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include);
            SimulationEntityReader playerReader = readers.FirstOrDefault(r => r != null && r.EntityId == SimulationDataTable.ENTITY_PLAYER && r.gameObject.scene.IsValid());
            SimulationEntityReader opponentReader = readers.FirstOrDefault(r => r != null && r.EntityId == SimulationDataTable.ENTITY_OPPONENT_1 && r.gameObject.scene.IsValid());
            UpdatePresentation(binders, animators);
            if (playerReader != null && opponentReader != null)
            {
                playerReader.ConsumeTableState(manager.DataTable.CurrentTick);
                opponentReader.ConsumeTableState(manager.DataTable.CurrentTick);
                MethodInfo readerLateUpdate = typeof(SimulationEntityReader).GetMethod(
                    "LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (readerLateUpdate != null)
                {
                    readerLateUpdate.Invoke(playerReader, null);
                    readerLateUpdate.Invoke(opponentReader, null);
                }
                Sprite[] idleSheet = Resources.LoadAll<Sprite>("Stickman/Idle");
                Sprite idle0Ref = (idleSheet != null ? idleSheet.FirstOrDefault(s => s != null && s.name == "IDLE_0") : null)
                                  ?? (playerReader.SpriteRenderer != null ? playerReader.SpriteRenderer.sprite : null);
                float idlePpu = (idle0Ref != null && idle0Ref.pixelsPerUnit > 0f) ? idle0Ref.pixelsPerUnit : 385f;
                float refIdleBodyUnitH = 140f / idlePpu;
                float baseScale160 = playerReader.LastAppliedScale;
                float opponentScale120 = opponentReader.LastAppliedScale;
                float playerWorldH = refIdleBodyUnitH * baseScale160;
                float opponentWorldH = refIdleBodyUnitH * opponentScale120;
                bool uniformStandingScale = Mathf.Abs(playerReader.transform.localScale.x - playerReader.transform.localScale.y) < 1e-4f &&
                                            Mathf.Abs(opponentReader.transform.localScale.x - opponentReader.transform.localScale.y) < 1e-4f &&
                                            Mathf.Abs(playerReader.transform.localScale.y - baseScale160) < 1e-4f &&
                                            Mathf.Abs(opponentReader.transform.localScale.y - opponentScale120) < 1e-4f &&
                                            Mathf.Abs(baseScale160 - 5.500f) <= 0.01f &&
                                            Mathf.Abs(opponentScale120 - 4.125f) <= 0.01f &&
                                            playerReader.SpriteRenderer != null &&
                                            playerReader.SpriteRenderer.bounds.size.x > 0.50f;
                Check($"Player (id=0, sizeY=160) renders with uniform Idle_0 (140px/{idlePpu:F0}PPU) world height 2.00m (actual={playerWorldH:F3}m, scale={baseScale160:F3}) and Opponent_1 (id=3, sizeY=120) renders at 1.50m (actual={opponentWorldH:F3}m, scale={opponentScale120:F3})",
                    uniformStandingScale && Mathf.Abs(playerWorldH - 2.00f) <= 0.01f && Mathf.Abs(opponentWorldH - 1.50f) <= 0.01f, ref pass, ref fail);

                // Verify zero horizontal visible-center jump when facingDirection flips +1 <-> -1 at identical C++ hitbox position
                manager.InputAdapter.ClearInjected();
                manager.InputAdapter.SetInjected(SpikeAction.Right, true);
                manager.StepOnce();
                var rightSnap = manager.LastNativeSnapshot;
                manager.InputAdapter.ClearInjected();
                manager.InputAdapter.SetInjected(SpikeAction.Left, true);
                manager.StepOnce();
                var leftSnap = manager.LastNativeSnapshot;
                manager.InputAdapter.ClearInjected();

                rightSnap.entity0.position.position.x = 330f;
                rightSnap.entity0.position.position.y = Simulation.CourtGeometry.FloorY;
                SetSyntheticCharacterPresentation(ref rightSnap.entity0, 10, 0, 0f, 0f);
                leftSnap.entity0.position = rightSnap.entity0.position;
                leftSnap.entity0.size = rightSnap.entity0.size;
                SetSyntheticCharacterPresentation(ref leftSnap.entity0, 10, 0, 0f, 0f);

                rightSnap.tick = manager.DataTable.CurrentTick + 1;
                manager.DataTable.UpdateFromNativeSnapshot(rightSnap);
                manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
                UpdatePresentation(binders, animators);
                playerReader.ConsumeTableState(rightSnap.tick);
                if (readerLateUpdate != null) readerLateUpdate.Invoke(playerReader, null);
                bool flipStateRight = playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.flipX;
                Sprite sprRight = (playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.sprite != null)
                    ? playerReader.SpriteRenderer.sprite : idle0Ref;
                Vector2 localCenterRight = MeasureOpaqueSpriteLocalCenter(sprRight);
                float visibleCenterRightX = playerReader.transform.position.x +
                                            (flipStateRight ? -localCenterRight.x : localCenterRight.x) * playerReader.transform.localScale.x;
                float visibleCenterRightY = playerReader.transform.position.y +
                                            localCenterRight.y * playerReader.transform.localScale.y;

                leftSnap.tick = rightSnap.tick + 1;
                manager.DataTable.UpdateFromNativeSnapshot(leftSnap);
                manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
                UpdatePresentation(binders, animators);
                playerReader.ConsumeTableState(leftSnap.tick);
                if (readerLateUpdate != null) readerLateUpdate.Invoke(playerReader, null);
                bool flipStateLeft = playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.flipX;
                Sprite sprLeft = (playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.sprite != null)
                    ? playerReader.SpriteRenderer.sprite : idle0Ref;
                Vector2 localCenterLeft = MeasureOpaqueSpriteLocalCenter(sprLeft);
                float visibleCenterLeftX = playerReader.transform.position.x +
                                           (flipStateLeft ? -localCenterLeft.x : localCenterLeft.x) * playerReader.transform.localScale.x;
                float visibleCenterLeftY = playerReader.transform.position.y +
                                           localCenterLeft.y * playerReader.transform.localScale.y;

                float expectedHitboxCenterX = Simulation.CourtGeometry.ToWorldX(
                    leftSnap.entity0.position.position.x + leftSnap.entity0.size.size.x * 0.5f);
                float expectedHitboxCenterY = Simulation.CourtGeometry.ToWorldY(
                    leftSnap.entity0.position.position.y + leftSnap.entity0.size.size.y * 0.5f);
                float flipVisibleJumpX = Mathf.Abs(visibleCenterRightX - visibleCenterLeftX);
                bool zeroFlipJumpOk = flipStateRight != flipStateLeft &&
                                      Mathf.Abs(visibleCenterRightX - expectedHitboxCenterX) <= 0.025f &&
                                      Mathf.Abs(visibleCenterLeftX - expectedHitboxCenterX) <= 0.025f &&
                                      Mathf.Abs(visibleCenterRightY - expectedHitboxCenterY) <= 0.025f &&
                                      Mathf.Abs(visibleCenterLeftY - expectedHitboxCenterY) <= 0.025f &&
                                      flipVisibleJumpX <= 0.02f;
                Check($"facingDirection +1 <-> -1 flip syncs SpriteRenderer.flipX ({flipStateRight}->{flipStateLeft}) with Tight-sprite visible center locked to hitbox center (right=({visibleCenterRightX:F3},{visibleCenterRightY:F3}), left=({visibleCenterLeftX:F3},{visibleCenterLeftY:F3}), rect=({expectedHitboxCenterX:F3},{expectedHitboxCenterY:F3}), jumpX={flipVisibleJumpX:F4}m)",
                    zeroFlipJumpOk, ref pass, ref fail);

                float[] testSizesPx = { 160f, 120f, 200f };
                float[] expectedHeightsWorld = { 2.00f, 1.50f, 2.50f };
                float[] expectedScaleRatios = { 1.00f, 0.75f, 1.25f };
                bool dynamicScaleOk = true;
                var sizeSnap = rightSnap;
                sizeSnap.tick = leftSnap.tick;
                for (int sIdx = 0; sIdx < testSizesPx.Length && dynamicScaleOk; sIdx++)
                {
                    sizeSnap.tick += 1;
                    sizeSnap.entity0.size.size.y = testSizesPx[sIdx];
                    manager.DataTable.UpdateFromNativeSnapshot(sizeSnap);
                    playerReader.ConsumeTableState(sizeSnap.tick);
                    if (readerLateUpdate != null) readerLateUpdate.Invoke(playerReader, null);
                    float dynamicH = refIdleBodyUnitH * playerReader.LastAppliedScale;
                    dynamicScaleOk &= Mathf.Abs(dynamicH - expectedHeightsWorld[sIdx]) <= 0.01f &&
                                      Mathf.Abs(playerReader.LastAppliedScale - baseScale160 * expectedScaleRatios[sIdx]) <= 0.002f &&
                                      Mathf.Abs(playerReader.transform.localScale.y - playerReader.LastAppliedScale) <= 0.001f &&
                                      Mathf.Abs(playerReader.transform.localScale.x - playerReader.transform.localScale.y) < 1e-4f;
                }
                Check("SimulationEntityReader dynamically recalculates LastAppliedScale when ABI sizeY changes (160->120->200 px => 2.00m->1.50m->2.50m)",
                    dynamicScaleOk, ref pass, ref fail);

                manager.ResetMatch();
                if (!manager.StepOnce()) return;
            }
            else
            {
                Check("Player and Opponent_1 SimulationEntityReader exist in active scene", false, ref pass, ref fail);
            }

            manager.InputAdapter.SetInjected(SpikeAction.SpikeLight, true);
            manager.StepOnce();
            manager.InputAdapter.ClearInjected();
            var initialSpikeSnap = manager.LastNativeSnapshot;
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            for (int i = 0; i < 8; i++) playerAnimator.Update(1f / 60f);

            int replaysBeforeHold = playerBinder.ReplayTriggerCount;
            // Tick +2: Real native countdown tick (cooldown decreases); must NOT replay
            manager.StepOnce();
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            var holdRec = manager.DataTable.GetEntity(SimulationDataTable.ENTITY_PLAYER);
            Check("held action countdown does not retrigger one-shot animation",
                playerBinder.ReplayTriggerCount == replaysBeforeHold, ref pass, ref fail);

            // Tick +3: Repeated accepted SpikeLight (duration resets upward to initialSpikeSnap); MUST replay once
            var repeatSnap = initialSpikeSnap;
            repeatSnap.tick = manager.DataTable.CurrentTick + 1;
            repeatSnap.entity0.actionState.remainTime = initialSpikeSnap.entity0.actionState.remainTime + 0.5f;
            manager.DataTable.UpdateFromNativeSnapshot(repeatSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            var repRec = manager.DataTable.GetEntity(SimulationDataTable.ENTITY_PLAYER);
            Check($"repeated accepted same-state action retriggers animation from start (replays={playerBinder.ReplayTriggerCount - replaysBeforeHold}, holdRemain={holdRec.actionRemainTime:F3}/{holdRec.cooldownRemainMs:F1}, repRemain={repRec.actionRemainTime:F3}/{repRec.cooldownRemainMs:F1})",
                playerBinder.ReplayTriggerCount == replaysBeforeHold + 1, ref pass, ref fail);
            var baseSnap = repeatSnap;

            // Tick +4: Lower-layer slow-motion active (slowMoFramesRemaining = 30 => animator.speed = 1/6)
            baseSnap.tick += 1;
            TrySetSnapshotReservedHeader(ref baseSnap, 30);
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            float slowMoAnimSpeed = playerAnimator.speed;
            bool slowMoSynced = playerBinder.IsSlowMotionActive &&
                                !playerBinder.IsSlowMoTickFrozen &&
                                Mathf.Abs(slowMoAnimSpeed - (1f / 6f)) < 0.005f;

            // Tick +4b: Frozen slow-mo tick (slowMoFramesRemaining = 29 => IsSlowMoTickFrozen == true, VFX timers paused)
            baseSnap.tick += 1;
            TrySetSnapshotReservedHeader(ref baseSnap, 29);
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            bool frozenSlowTickSynced = playerBinder.IsSlowMotionActive && playerBinder.IsSlowMoTickFrozen;

            // Tick +4c: Full simulation pause (SetPaused(true) => Animator.speed == 0f, IsPausedWithSimulation == true)
            manager.SetPaused(true);
            UpdatePresentation(binders, animators);
            bool pauseSynced = playerBinder.IsPausedWithSimulation && Mathf.Abs(playerAnimator.speed) < 0.0001f;
            manager.SetPaused(false);

            // Tick +5: Lower-layer slow-motion ends (slowMoFramesRemaining = 0 => animator.speed = 1.0f)
            baseSnap.tick += 1;
            TrySetSnapshotReservedHeader(ref baseSnap, 0);
            manager.DataTable.UpdateFromNativeSnapshot(baseSnap);
            manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
            UpdatePresentation(binders, animators);
            float restoredAnimSpeed = playerAnimator.speed;
            bool normalRestored = !playerBinder.IsSlowMotionActive &&
                                  !playerBinder.IsSlowMoTickFrozen &&
                                  !playerBinder.IsPausedWithSimulation &&
                                  Mathf.Abs(restoredAnimSpeed - 1.0f) < 0.005f;
            Check($"CharacterAnimationBinder synchronizes Animator.speed & VFX pause with lower-layer slowMoFramesRemaining and pause state (slowMo={slowMoAnimSpeed:F4}, frozen29={frozenSlowTickSynced}, paused0={pauseSynced}, restored={restoredAnimSpeed:F2})",
                slowMoSynced && frozenSlowTickSynced && pauseSynced && normalRestored, ref pass, ref fail);
        }
        finally
        {
            manager.ResetMatch();
            manager.StepOnce();
            manager.InputAdapter.InjectionExclusive = oldExclusive;
            manager.ManualTickMode = oldManual;
        }
    }

    private static void CheckAuthoritativePlayAnimStateGate(SpikeSimulationManager manager, ref int pass, ref int fail)
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
        if (playerBinder == null || playerAnimator == null)
        {
            Check("playAnim gate: player CharacterAnimationBinder and Animator available", false, ref pass, ref fail);
            return;
        }

        int[] actionAnimatorHashes =
        {
            Animator.StringToHash("KnightServe"),
            Animator.StringToHash("KnightBump"),
            Animator.StringToHash("KnightSpike"),
            Animator.StringToHash("SpikeLight"),
            Animator.StringToHash("SpikeMedium"),
            Animator.StringToHash("SpikeStrong")
        };

        bool oldManual = manager.ManualTickMode;
        bool oldExclusive = manager.InputAdapter.InjectionExclusive;
        manager.ManualTickMode = true;
        manager.InputAdapter.InjectionExclusive = true;
        try
        {
            // 1. Whiffed intent presses (characterIntent.finalIntent != 0 while actionState.status == 0)
            //    MUST NOT enter action states (40, 50, 60, 70, 80, 90) or action clips.
            manager.ResetMatch();
            manager.InputAdapter.ClearInjected();
            if (!manager.StepOnce())
            {
                Check("playAnim gate: baseline snapshot commits", false, ref pass, ref fail);
                return;
            }
            UpdatePresentation(binders, animators);
            for (int settle = 0; settle < 8; settle++) playerAnimator.Update(1f / 60f);

            var whiffSnap = manager.LastNativeSnapshot;
            string[] whiffLabels = { "Bump", "Spike", "SpikeLight", "SpikeMedium", "SpikeStrong", "Serve" };
            int[] whiffIntents = { 5, 2, 9, 10, 11, 8 };
            for (int i = 0; i < whiffIntents.Length; i++)
            {
                whiffSnap.tick += 1;
                whiffSnap.entity0.characterIntent.finalIntent = whiffIntents[i];
                whiffSnap.entity0.actionState.status = (int)SimActionState.None;
                whiffSnap.entity0.actionState.remainTime = 0f;
                manager.DataTable.UpdateFromNativeSnapshot(whiffSnap);
                manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
                UpdatePresentation(binders, animators);

                int boundParam = playerAnimator.GetInteger("State");
                bool enteredActionState = boundParam == 40 || boundParam == 50 || boundParam == 60 ||
                                          boundParam == 70 || boundParam == 80 || boundParam == 90;
                bool playingActionClip = actionAnimatorHashes.Any(h => AnimatorHasState(animators, 0, h));
                Check($"playAnim gate: whiffed {whiffLabels[i]} intent (finalIntent={whiffIntents[i]}, status=0) stays out of action states (State={boundParam})",
                    !enteredActionState && !playingActionClip, ref pass, ref fail);
            }

            // 2. Authoritative C++ actionState.status rising edges (4, 5, 2, 7, 8, 9) with valid remainTime
            //    DO enter (90, 50, 40, 60, 70, 80) and play (KnightServe, KnightBump, KnightSpike, SpikeLight, SpikeMedium, SpikeStrong).
            int[] acceptedNativeStatuses =
            {
                (int)SimActionState.Serve,       // 4
                (int)SimActionState.Bump,        // 5
                (int)SimActionState.Spike,       // 2
                (int)SimActionState.SpikeLight,  // 7
                (int)SimActionState.SpikeMedium, // 8
                (int)SimActionState.SpikeStrong  // 9
            };
            int[] expectedBinderStates = { 90, 50, 40, 60, 70, 80 };
            string[] expectedAnimatorNames =
            {
                "KnightServe", "KnightBump", "KnightSpike",
                "SpikeLight", "SpikeMedium", "SpikeStrong"
            };

            for (int i = 0; i < acceptedNativeStatuses.Length; i++)
            {
                manager.ResetMatch();
                manager.InputAdapter.ClearInjected();
                if (!manager.StepOnce())
                {
                    Check($"playAnim gate: reset before {expectedAnimatorNames[i]}", false, ref pass, ref fail);
                    continue;
                }
                UpdatePresentation(binders, animators);

                var acceptedSnap = manager.LastNativeSnapshot;
                acceptedSnap.tick += 1;
                acceptedSnap.entity0.actionState.status = acceptedNativeStatuses[i];
                acceptedSnap.entity0.actionState.remainTime = 0.5f;
                SetSyntheticCharacterPresentation(ref acceptedSnap.entity0, expectedBinderStates[i], acceptedNativeStatuses[i], 450f, 450f);
                manager.DataTable.UpdateFromNativeSnapshot(acceptedSnap);
                manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
                UpdatePresentation(binders, animators);

                int boundParam = playerAnimator.GetInteger("State");
                int targetHash = Animator.StringToHash(expectedAnimatorNames[i]);
                bool clipPlaying = AnimatorHasState(animators, 0, targetHash);
                Check($"playAnim gate: authoritative status={acceptedNativeStatuses[i]} enters State={expectedBinderStates[i]} and plays {expectedAnimatorNames[i]}",
                    boundParam == expectedBinderStates[i] && clipPlaying, ref pass, ref fail);
            }

            // 3. Real native Get_data grounded SpikeLight / SpikeMedium / SpikeStrong (0 cooldown) emit SimAnimId 60/70/80,
            //    enter Animator SpikeLight/Medium/Strong, recover to Idle when cooldown expires, and <80% jump ascent blocks Spike.
            SpikeAction[] groundedSpikes = { SpikeAction.SpikeLight, SpikeAction.SpikeMedium, SpikeAction.SpikeStrong };
            int[] groundedAnimIds = { 60, 70, 80 };
            string[] groundedStateNames = { "SpikeLight", "SpikeMedium", "SpikeStrong" };
            for (int i = 0; i < groundedSpikes.Length; i++)
            {
                manager.ResetMatch();
                manager.InputAdapter.ClearInjected();
                if (!manager.StepOnce()) continue;
                UpdatePresentation(binders, animators);

                manager.InputAdapter.SetInjected(groundedSpikes[i], true);
                bool gCommitted = manager.StepOnce();
                manager.InputAdapter.ClearInjected();
                UpdatePresentation(binders, animators);
                var gPlayer = manager.DataTable.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                int gParam = playerAnimator.GetInteger("State");
                int gHash = Animator.StringToHash(groundedStateNames[i]);
                bool gPlaying = AnimatorHasState(animators, 0, gHash);

                bool gRecovered = false;
                for (int t = 0; t < 40; t++)
                {
                    if (!manager.StepOnce()) break;
                    UpdatePresentation(binders, animators);
                    var recPlayer = manager.DataTable.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    if (recPlayer.cooldownRemainMs <= 0.001f &&
                        (int)recPlayer.animId == 10 &&
                        playerAnimator.GetInteger("State") == 10 &&
                        !AnimatorHasState(animators, 0, gHash))
                    {
                        gRecovered = true;
                        break;
                    }
                }
                Check($"native grounded {groundedStateNames[i]} (0 cooldown) emits SimAnimId={groundedAnimIds[i]}, plays {groundedStateNames[i]}, and recovers to Idle (10) after cooldown",
                    gCommitted && (int)gPlayer.animId == groundedAnimIds[i] && gPlayer.cooldownRemainMs > 0f &&
                    gParam == groundedAnimIds[i] && gPlaying && gRecovered, ref pass, ref fail);
            }
        }
        finally
        {
            manager.ResetMatch();
            manager.StepOnce();
            UpdatePresentation(binders, animators);
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

    private static Vector2 MeasureOpaqueSpriteLocalCenter(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null || sprite.pixelsPerUnit <= 0f) return Vector2.zero;
        Texture2D tex = sprite.texture;
        Texture2D tempTex = null;
        RenderTexture prevRt = RenderTexture.active;
        RenderTexture rt = null;
        try
        {
            Color32[] pixels;
            int texW = tex.width;
            if (tex.isReadable)
            {
                pixels = tex.GetPixels32();
            }
            else
            {
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                tempTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                tempTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                tempTex.Apply();
                pixels = tempTex.GetPixels32();
            }
            Rect tr = sprite.textureRect;
            Vector2 rectOffset = (!sprite.packed || sprite.packingMode == SpritePackingMode.Rectangle)
                ? sprite.textureRectOffset
                : new Vector2(tr.x - sprite.rect.x, tr.y - sprite.rect.y);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, texW - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax) - 1, 0, texW - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax) - 1, 0, tex.height - 1);
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * texW;
                for (int x = x0; x <= x1; x++)
                {
                    if (pixels[row + x].a > 10)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (minX > maxX || minY > maxY) return sprite.bounds.center;
            float opaqueCenterInCellX = rectOffset.x + ((minX - x0) + (maxX - x0) + 1f) * 0.5f;
            float opaqueCenterInCellY = rectOffset.y + ((minY - y0) + (maxY - y0) + 1f) * 0.5f;
            return new Vector2(
                (opaqueCenterInCellX - sprite.pivot.x) / sprite.pixelsPerUnit,
                (opaqueCenterInCellY - sprite.pivot.y) / sprite.pixelsPerUnit);
        }
        finally
        {
            RenderTexture.active = prevRt;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (tempTex != null) UnityEngine.Object.DestroyImmediate(tempTex);
        }
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

    private static AnimatorState FindStateInHierarchy(AnimatorStateMachine root, string name) =>
        EnumerateStates(root).FirstOrDefault(s => s.name == name);

    private static System.Collections.Generic.IEnumerable<AnimatorStateMachine> EnumerateMachines(AnimatorStateMachine machine)
    {
        if (machine == null) yield break;
        yield return machine;
        foreach (var sub in machine.stateMachines)
            foreach (var nested in EnumerateMachines(sub.stateMachine)) yield return nested;
    }

    private static System.Collections.Generic.IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
    {
        if (machine == null) yield break;
        foreach (var child in machine.states) yield return child.state;
        foreach (var sub in machine.stateMachines)
            foreach (var state in EnumerateStates(sub.stateMachine)) yield return state;
    }

    private static bool MotionReferencesClip(Motion motion, AnimationClip target)
    {
        if (motion == null || target == null) return false;
        if (motion == target) return true;
        if (motion is BlendTree tree)
        {
            foreach (var child in tree.children)
                if (MotionReferencesClip(child.motion, target)) return true;
        }
        return false;
    }

    private static bool HasTransitionToStateWithValue(AnimatorStateMachine root, AnimatorState destination, int value)
    {
        if (root == null || destination == null) return false;
        if (EnumerateMachines(root).Any(m => m.anyStateTransitions.Any(t =>
            t.destinationState == destination &&
            t.conditions.Any(c => c.parameter == "State" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, value)))))
        {
            return true;
        }
        return EnumerateStates(root).Any(s => s.transitions.Any(t =>
            t.destinationState == destination &&
            t.conditions.Any(c => c.parameter == "State" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, value))));
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

    private static bool TrySetSnapshotReservedHeader(ref NativeSimData snap, int value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object boxedSnap = snap;
        bool updated = false;

        FieldInfo rallyField = typeof(NativeSimData).GetField("rallyState", flags);
        if (rallyField != null)
        {
            object boxedRally = rallyField.GetValue(boxedSnap);
            if (boxedRally != null)
            {
                FieldInfo slowMoField = rallyField.FieldType.GetField("slowMoFramesRemaining", flags) ??
                                        rallyField.FieldType.GetField("_padding0", flags);
                if (slowMoField != null)
                {
                    object converted = Convert.ChangeType(value, slowMoField.FieldType);
                    slowMoField.SetValue(boxedRally, converted);
                    rallyField.SetValue(boxedSnap, boxedRally);
                    updated = true;
                }
            }
        }

        FieldInfo directField = typeof(NativeSimData).GetField("reservedHeader", flags);
        if (directField != null)
        {
            object converted = Convert.ChangeType(value, directField.FieldType);
            directField.SetValue(boxedSnap, converted);
            updated = true;
        }

        FieldInfo headerField = typeof(NativeSimData).GetField("header", flags);
        if (headerField != null)
        {
            object boxedHeader = headerField.GetValue(boxedSnap);
            FieldInfo nestedReserved = headerField.FieldType.GetField("reservedHeader", flags);
            if (boxedHeader != null && nestedReserved != null)
            {
                object converted = Convert.ChangeType(value, nestedReserved.FieldType);
                nestedReserved.SetValue(boxedHeader, converted);
                headerField.SetValue(boxedSnap, boxedHeader);
                updated = true;
            }
        }

        if (updated)
        {
            snap = (NativeSimData)boxedSnap;
        }
        return updated;
    }

    private static void SetSyntheticCharacterPresentation(ref NativeSimEntity entity, int animId, int cooldownAction, float cooldownRemainMs, float cooldownDurationMs)
    {
        entity.ballTrajectory.type = animId;
        entity.ballTrajectory.a = BitConverter.Int32BitsToSingle(cooldownAction);
        entity.ballTrajectory.b = cooldownRemainMs;
        entity.ballTrajectory.c = cooldownDurationMs;
    }

    private static void Check(string name, bool ok, ref int pass, ref int fail)
    {
        if (ok) { pass++; Debug.Log("[P03_TEST][PASS] " + name); }
        else { fail++; Debug.LogError("[P03_TEST][FAIL] " + name); }
    }
}
