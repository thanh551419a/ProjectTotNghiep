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
                    follow.ApplyFramingNow();
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
            Check($"native HandleBallState sets floor terminal stateFrame in [80..110] (+500..1000ms over 50-frame baseline, actual={terminalStateFrame})",
                terminalStateFrame >= 80 && terminalStateFrame <= 110, ref pass, ref fail);

            // Verify special high-altitude airborne Spike / Boom Jump unlocks vertical Y follow, and landing relocks Y to GroundLockedY.
            var specialSnap = manager.LastNativeSnapshot;
            specialSnap.tick += 1;
            specialSnap.entity0.isGrounded = 0;
            specialSnap.entity0.airborneState = (int)SimAirborneState.JumpUp;
            specialSnap.entity0.actionState.status = (int)SimActionState.SpikeStrong;
            specialSnap.entity0.actionState.remainTime = 0.4f;
            specialSnap.entity0.position.position.y = Simulation.CourtGeometry.FloorY + 220f;
            if (specialSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity2.position.position.y = Simulation.CourtGeometry.FloorY + 340f;
            if (specialSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
                specialSnap.entity3.position.position.y = Simulation.CourtGeometry.FloorY + 340f;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            bool specialUnlocked = follow.IsSpecialVerticalFollowActive && follow.LastClampedTarget.y > follow.GroundLockedY + 0.05f;

            specialSnap.tick += 1;
            specialSnap.entity0.isGrounded = 1;
            specialSnap.entity0.airborneState = (int)SimAirborneState.Grounded;
            specialSnap.entity0.actionState.status = (int)SimActionState.None;
            specialSnap.entity0.actionState.remainTime = 0f;
            specialSnap.entity0.position.position.y = Simulation.CourtGeometry.FloorY;
            manager.DataTable.UpdateFromNativeSnapshot(specialSnap);
            follow.ApplyFramingNow();
            bool groundedRelocked = !follow.IsSpecialVerticalFollowActive &&
                                    Mathf.Abs(follow.LastClampedTarget.y - follow.GroundLockedY) < 0.005f;
            Check("camera unlocks vertical Y follow only during special airborne Spike/Boom Jump and relocks to GroundLockedY when grounded",
                specialUnlocked && groundedRelocked, ref pass, ref fail);

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
                        if (distance < 78f && toggle) spikeAction = configuredIds[scenario];
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
                float playerWorldH = playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.sprite != null
                    ? playerReader.SpriteRenderer.sprite.bounds.size.y * playerReader.LastAppliedScale
                    : -1f;
                float opponentWorldH = opponentReader.SpriteRenderer != null && opponentReader.SpriteRenderer.sprite != null
                    ? opponentReader.SpriteRenderer.sprite.bounds.size.y * opponentReader.LastAppliedScale
                    : -1f;
                Check($"Player (id=0, sizeY=160) renders with world height 2.00m (actual={playerWorldH:F3}m) and Opponent_1 (id=3, sizeY=120) renders with world height 1.50m (actual={opponentWorldH:F3}m)",
                    Mathf.Abs(playerWorldH - 2.00f) <= 0.01f && Mathf.Abs(opponentWorldH - 1.50f) <= 0.01f, ref pass, ref fail);

                float[] testSizesPx = { 160f, 120f, 200f };
                float[] expectedHeightsWorld = { 2.00f, 1.50f, 2.50f };
                bool dynamicScaleOk = true;
                var sizeSnap = manager.LastNativeSnapshot;
                for (int sIdx = 0; sIdx < testSizesPx.Length && dynamicScaleOk; sIdx++)
                {
                    sizeSnap.tick += 1;
                    sizeSnap.entity0.size.size.y = testSizesPx[sIdx];
                    manager.DataTable.UpdateFromNativeSnapshot(sizeSnap);
                    playerReader.ConsumeTableState(sizeSnap.tick);
                    float dynamicH = playerReader.SpriteRenderer != null && playerReader.SpriteRenderer.sprite != null
                        ? playerReader.SpriteRenderer.sprite.bounds.size.y * playerReader.LastAppliedScale
                        : -1f;
                    dynamicScaleOk &= Mathf.Abs(dynamicH - expectedHeightsWorld[sIdx]) <= 0.01f &&
                                      Mathf.Abs(playerReader.transform.localScale.y - playerReader.LastAppliedScale) <= 0.001f;
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
                manager.DataTable.UpdateFromNativeSnapshot(acceptedSnap);
                manager.DataTable.SyncToCharacterStateTable(manager.StateTable);
                UpdatePresentation(binders, animators);

                int boundParam = playerAnimator.GetInteger("State");
                int targetHash = Animator.StringToHash(expectedAnimatorNames[i]);
                bool clipPlaying = AnimatorHasState(animators, 0, targetHash);
                Check($"playAnim gate: authoritative status={acceptedNativeStatuses[i]} enters State={expectedBinderStates[i]} and plays {expectedAnimatorNames[i]}",
                    boundParam == expectedBinderStates[i] && clipPlaying, ref pass, ref fail);
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

    private static void Check(string name, bool ok, ref int pass, ref int fail)
    {
        if (ok) { pass++; Debug.Log("[P03_TEST][PASS] " + name); }
        else { fail++; Debug.LogError("[P03_TEST][FAIL] " + name); }
    }
}
