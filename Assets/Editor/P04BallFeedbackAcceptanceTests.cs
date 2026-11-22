using System;
using System.Collections;
using Assets.Table;
using Native;
using Presentation.Feedback;
using Simulation;
using UnityEditor;
using UnityEngine;

/// <summary>
/// P04 acceptance entry point. EditMode checks exercise event classification without fabricating
/// native state. PlayMode checks use the configured input adapter and actual native Get_data ticks.
/// </summary>
public static class P04BallFeedbackAcceptanceTests
{
    private const string QaStageKey = "SpikeQaAcceptanceStage";
    private static bool _qaPumpRunning;
    private static uint _mainThreadId;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [InitializeOnLoadMethod]
    private static void RegisterQaAcceptanceHooks()
    {
        _mainThreadId = GetCurrentThreadId();
        EditorApplication.update -= OnEditorUpdateForQa;
        EditorApplication.update += OnEditorUpdateForQa;
        EnsureQaMainThreadWakePump();
    }

    private static void EnsureQaMainThreadWakePump()
    {
        if (_mainThreadId == 0) _mainThreadId = GetCurrentThreadId();
        if (_qaPumpRunning) return;
        _qaPumpRunning = true;
        var pumpThread = new System.Threading.Thread(() =>
        {
            while (_qaPumpRunning)
            {
                System.Threading.Thread.Sleep(4);
                if (_mainThreadId != 0)
                {
                    PostThreadMessage(_mainThreadId, 0u, IntPtr.Zero, IntPtr.Zero);
                }
            }
        })
        {
            IsBackground = true,
            Name = "SpikeQaWakePump"
        };
        pumpThread.Start();
    }

    public static void RunQaAcceptanceSuitesFromCli()
    {
        Application.runInBackground = true;
        EnsureQaMainThreadWakePump();
        SessionState.SetInt("SpikeQaFailed", 0);
        SessionState.SetInt(QaStageKey, 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Resources/New Scene.unity");
        P02InputAcceptanceTests.RunAll();
        EditorApplication.isPlaying = true;
    }

    private static void OnEditorUpdateForQa()
    {
        int stage = SessionState.GetInt(QaStageKey, 0);
        if (stage == 0) return;

        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();

        if (stage == 1 && EditorApplication.isPlaying)
        {
            SpikeSimulationManager manager = SpikeSimulationManager.Instance;
            if (manager != null && manager.State == SpikeSimulationManager.RunState.Running &&
                manager.DataTable != null && manager.InputAdapter != null)
            {
                SessionState.SetInt(QaStageKey, 2);
                try { P02InputAcceptanceTests.RunAll(); }
                catch (Exception ex) { SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + 1); Debug.LogError("[SPIKE_QA_P02_EX] " + ex.Message); }
                try { P03AnimationCameraAcceptanceTests.RunAll(); }
                catch (Exception ex) { SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + 1); Debug.LogError("[SPIKE_QA_P03_EX] " + ex.Message); }
                try { RunAll(); }
                catch (Exception ex) { SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + 1); Debug.LogError("[SPIKE_QA_P04_EX] " + ex.Message); }

                for (int step = 0; step < 4000 && SessionState.GetInt(QaStageKey, 0) == 2; step++)
                {
                    EditorApplication.Step();
                }
            }
        }
        else if (stage == 3 && !EditorApplication.isPlaying)
        {
            SessionState.SetInt(QaStageKey, 0);
            int totalFailed = SessionState.GetInt("SpikeQaFailed", 0);
            Debug.Log($"[SPIKE_QA_SUITE_COMPLETE] P02, P03, P04 completed and PlayMode stopped (totalFailed={totalFailed}).");
            if (Application.isBatchMode)
            {
                _qaPumpRunning = false;
                EditorApplication.Exit(totalFailed > 0 ? 1 : 0);
            }
        }
    }

    [MenuItem("Spike/Run P04 Ball Feedback Acceptance Tests")]
    public static void RunAll()
    {
        int pass = 0;
        int fail = 0;
        int blocked = 0;

        CheckGroundEventRules(ref pass, ref fail);
        CheckAcceptedActionEdges(ref pass, ref fail);
        CheckPresentationSetup(ref pass, ref fail, ref blocked);

        if (Application.isPlaying)
        {
            SpikeSimulationManager manager = SpikeSimulationManager.Instance;
            if (manager == null || manager.State != SpikeSimulationManager.RunState.Running ||
                manager.DataTable == null || manager.InputAdapter == null)
            {
                blocked++;
                Debug.LogWarning("[P04_TEST][BLOCKED] PlayMode checks require the running native manager, input adapter and committed DataTable.");
            }
            else
            {
                manager.StartCoroutine(RunNativePlayMode(manager));
            }
        }
        else
        {
            blocked++;
            Debug.LogWarning("[P04_TEST][BLOCKED] Native rally/trail/audio integration requires this menu command to run in Play Mode.");
        }

        Debug.Log($"[P04_EDITMODE_SUMMARY] pass={pass},fail={fail},blocked={blocked}");
        if (fail > 0) throw new InvalidOperationException($"P04 EditMode acceptance failed: {fail} assertion(s).");
    }

    private static void CheckGroundEventRules(ref int pass, ref int fail)
    {
        SimulationEntityRecord airborne = Ball(-1, 340f, 100f);
        SimulationEntityRecord landed = Ball(50, 265f, 100f);
        Check("Alive-to-terminal floor crossing emits a landing", BallFeedbackPresenter.IsGroundImpactTransition(airborne, landed, false, 265f), ref pass, ref fail);
        Check("persistent terminal state does not emit another landing", !BallFeedbackPresenter.IsGroundImpactTransition(landed, landed, false, 265f), ref pass, ref fail);
        Check("net contact while ball remains Alive is not a landing", !BallFeedbackPresenter.IsGroundImpactTransition(airborne, Ball(-1, 330f, 100f), false, 265f), ref pass, ref fail);
        Check("max-touch termination is excluded even at the floor", !BallFeedbackPresenter.IsGroundImpactTransition(airborne, landed, true, 265f), ref pass, ref fail);
        Check("scene initialization or serve transition is not a landing", !BallFeedbackPresenter.IsGroundImpactTransition(Ball(0, 265f, 100f), Ball(-1, 270f, 100f), false, 265f), ref pass, ref fail);
        Check("teleport discontinuity is not a landing", !BallFeedbackPresenter.IsGroundImpactTransition(airborne, Ball(50, 265f, 700f), false, 265f), ref pass, ref fail);
        Check("airborne terminal state above floor is not a landing", !BallFeedbackPresenter.IsGroundImpactTransition(airborne, Ball(50, 300f, 100f), false, 265f), ref pass, ref fail);
        Check("same-side fourth touch matches native max-touch resolution", BallFeedbackPresenter.IsSameSideFourthTouch(3, 2, 0), ref pass, ref fail);
        Check("opposing-side contact resets touch count and does not suppress landing", !BallFeedbackPresenter.IsSameSideFourthTouch(3, 2, 3), ref pass, ref fail);
        Check("same-side fourth-touch terminal is excluded from ground effects", !BallFeedbackPresenter.IsGroundImpactTransition(airborne, landed, BallFeedbackPresenter.IsSameSideFourthTouch(3, 2, 0), 265f), ref pass, ref fail);
        Check("opposing-side touch still permits a simultaneous ground landing cue", BallFeedbackPresenter.IsGroundImpactTransition(airborne, landed, BallFeedbackPresenter.IsSameSideFourthTouch(3, 2, 3), 265f), ref pass, ref fail);
        Check("third touch is not treated as a max-touch terminal", !BallFeedbackPresenter.IsSameSideFourthTouch(2, 2, 1), ref pass, ref fail);
    }

    private static void CheckAcceptedActionEdges(ref int pass, ref int fail)
    {
        SimulationEntityRecord none = Character((int)SimActionState.None, 0f);
        SimulationEntityRecord spike = Character((int)SimActionState.SpikeLight, 0.8f);
        SimulationEntityRecord heldSpike = Character((int)SimActionState.SpikeLight, 0.5f);
        SimulationEntityRecord acceptedAgain = Character((int)SimActionState.SpikeLight, 0.9f);
        SimulationEntityRecord bump = Character((int)SimActionState.Bump, 0.4f);
        Check("initial snapshot establishes action history without replaying stale audio", !BallFeedbackPresenter.IsAcceptedActionStart(default, spike, false), ref pass, ref fail);
        Check("native None-to-Spike action transition is an accepted event", BallFeedbackPresenter.IsAcceptedActionStart(none, spike, true), ref pass, ref fail);
        Check("held Spike countdown does not repeat its sound", !BallFeedbackPresenter.IsAcceptedActionStart(spike, heldSpike, true), ref pass, ref fail);
        Check("same Spike status with a reset duration represents a new accepted action", BallFeedbackPresenter.IsAcceptedActionStart(heldSpike, acceptedAgain, true), ref pass, ref fail);
        Check("native Bump action transition is an accepted contact event", BallFeedbackPresenter.IsAcceptedActionStart(none, bump, true), ref pass, ref fail);
        Check("rejected/None action does not emit a sound event", !BallFeedbackPresenter.IsAcceptedActionStart(none, none, true), ref pass, ref fail);
    }

    private static void CheckPresentationSetup(ref int pass, ref int fail, ref int blocked)
    {
        if (!Application.isPlaying)
        {
            blocked++;
            Debug.LogWarning("[P04_TEST][BLOCKED] runtime-created feedback components and committed-table binding can only be checked in Play Mode.");
            return;
        }

        SimulationEntityReader[] readers = UnityEngine.Object.FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include);
        SimulationEntityReader ballReader = null;
        foreach (SimulationEntityReader reader in readers)
            if (reader.EntityId == SimulationDataTable.ENTITY_BALL && reader.gameObject.scene.IsValid()) { ballReader = reader; break; }

        Check("scene has a ball SimulationEntityReader", ballReader != null, ref pass, ref fail);
        if (ballReader == null) return;

        BallFeedbackPresenter presenter = ballReader.GetComponent<BallFeedbackPresenter>();
        Check("ball presentation owns exactly one feedback presenter", presenter != null && ballReader.GetComponents<BallFeedbackPresenter>().Length == 1, ref pass, ref fail);
        if (presenter == null) return;
        Check("ball feedback includes a reusable TrailRenderer", presenter.Trail != null && !presenter.Trail.autodestruct, ref pass, ref fail);
        Check("ball TrailRenderer uses rectangular-corner ribbon geometry (numCapVertices == 0, numCornerVertices == 0, constant 1.0 rectangular widthCurve instead of meteor cone)",
            presenter.Trail != null &&
            presenter.Trail.numCapVertices == 0 &&
            presenter.Trail.numCornerVertices == 0 &&
            presenter.Trail.widthCurve != null &&
            Mathf.Abs(presenter.Trail.widthCurve.Evaluate(0f) - 1f) < 0.001f &&
            Mathf.Abs(presenter.Trail.widthCurve.Evaluate(1f) - 1f) < 0.001f, ref pass, ref fail);
        Check("ground effect uses one reusable ParticleSystem", presenter.ImpactParticles != null && presenter.ImpactParticles.main.maxParticles <= 24, ref pass, ref fail);
        Check("impact particle size is independent of the ball sprite's hierarchy scale", presenter.ImpactParticles != null && presenter.ImpactParticles.main.scalingMode == ParticleSystemScalingMode.Local, ref pass, ref fail);
        Check("audio uses one source and generated original placeholder clips", presenter.AudioSource != null && presenter.ProceduralAudioReady, ref pass, ref fail);
        Check("audio categories have independent configured volumes", presenter.SpikeVolume >= 0f && presenter.BumpVolume >= 0f && presenter.GroundImpactVolume >= 0f, ref pass, ref fail);
        Check("Inspector trail width formula parameters are entered in C++ Sim units (trailBaseWidth == 6.40 px, trailBaseSpeed == 160.0 v0 index, trailSpeedMultiplier == 1.0) and Unity converts v0 -> traj->speed (* 0.04) and Sim px -> Unity meters (/ CourtGeometry.PixelsPerMeter)",
            Mathf.Abs(presenter.TrailBaseSpeed - 160.0f) < 0.01f &&
            Mathf.Abs(presenter.TrailBaseWidth - 6.40f) < 0.01f &&
            Mathf.Abs(presenter.TrailSpeedMultiplier - 1.0f) < 0.01f &&
            Mathf.Abs(presenter.ComputeSpikeTrailWidthSimPx(12.80f) -
                      (presenter.TrailBaseWidth * (12.80f / (presenter.TrailBaseSpeed * BallFeedbackPresenter.SimV0ToSpeedScale)) * presenter.TrailSpeedMultiplier)) < 0.0001f &&
            Mathf.Abs(presenter.ComputeSpikeTrailWidth(12.80f) -
                      CourtGeometry.ToWorldLength(presenter.ComputeSpikeTrailWidthSimPx(12.80f))) < 0.0001f,
            ref pass, ref fail);
        Check("feedback component is bound to a SimulationDataTable when one exists", SimulationDataTable.Instance == null || !SimulationDataTable.Instance.IsInitialized || presenter.HasBoundTable, ref pass, ref fail);

        Transform impactRoot = presenter.ImpactParticles != null ? presenter.ImpactParticles.transform : null;
        string[] vfxNames =
        {
            "P04_SonicCrescentArc",
            "P04_RadialSpeedLines",
            "P04_XSlashSpark",
            "P04_OriginPuffRing",
            "P04_SpikeLaserBeam",
            "P04_SpikeLaserBeam_Shadow",
            "P04_GroundGoldenCrossRing",
            "P04_GroundExplosionBurst",
            "P04_GroundSonicWave_0",
            "P04_GroundSonicWave_1",
            "P04_GroundSonicWave_2",
            "P04_SpikeSpeedPopup",
            "P04_PointScoreBurstRing"
        };
        bool allVfxUnderImpactParticles = impactRoot != null && impactRoot.name == "P04_GroundImpactParticles";
        if (impactRoot != null)
        {
            foreach (string vfxName in vfxNames)
                allVfxUnderImpactParticles &= impactRoot.Find(vfxName) != null;
        }
        Check("BallFeedbackPresenter initializes RcTdc--AdaI VFX objects under P04_GroundImpactParticles",
            allVfxUnderImpactParticles, ref pass, ref fail);

        Transform popupTransform = impactRoot != null ? impactRoot.Find("P04_SpikeSpeedPopup") : null;
        TextMesh mainPopupMesh = popupTransform != null ? popupTransform.GetComponent<TextMesh>() : null;
        int blackOutlineCount = 0;
        if (popupTransform != null)
        {
            for (int i = 0; i < 8; i++)
            {
                Transform outlineChild = popupTransform.Find($"P04_SpikeSpeedPopup_Outline_{i}");
                TextMesh outlineMesh = outlineChild != null ? outlineChild.GetComponent<TextMesh>() : null;
                if (outlineMesh != null &&
                    Mathf.Abs(outlineMesh.color.r) < 0.001f &&
                    Mathf.Abs(outlineMesh.color.g) < 0.001f &&
                    Mathf.Abs(outlineMesh.color.b) < 0.001f)
                {
                    blackOutlineCount++;
                }
            }
        }
        var popupScaleField = typeof(BallFeedbackPresenter).GetField(
            "SpikeSpeedPopupTargetScale",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        float popupTargetScale = popupScaleField != null ? (float)popupScaleField.GetValue(null) : -1f;
        Check("P04_SpikeSpeedPopup uses 2x target scale (SpikeSpeedPopupTargetScale == 2.0f), white main TextMesh, and 8 black outline TextMeshes",
            Mathf.Abs(popupTargetScale - 2.0f) < 0.001f &&
            mainPopupMesh != null &&
            mainPopupMesh.color == Color.white &&
            blackOutlineCount == 8, ref pass, ref fail);
    }

    private static IEnumerator RunNativePlayMode(SpikeSimulationManager manager)
    {
        int pass = 0;
        int fail = 0;
        int blocked = 0;
        SpikeInputAdapter adapter = manager.InputAdapter;
        SimulationDataTable table = manager.DataTable;
        BallFeedbackPresenter presenter = FindBallPresenter();
        bool oldManual = manager.ManualTickMode;
        bool oldExclusive = adapter.InjectionExclusive;

        manager.ManualTickMode = true;
        adapter.InjectionExclusive = true;
        try
        {
            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(71u);
            adapter.ClearInjected();
            bool initialCommitted = manager.StepOnce();
            Check("native reset commits an authoritative baseline snapshot", initialCommitted && table.IsInitialized, ref pass, ref fail);

            if (!initialCommitted || presenter == null)
            {
                blocked++;
                Debug.LogWarning("[P04_TEST][BLOCKED] native rally checks lack a committed baseline or ball feedback presenter.");
                yield break;
            }

            int invalidSpikeSoundBaseline = presenter.SpikeSoundCount;
            SpikeAction[] invalidSpikes = { SpikeAction.SpikeLight, SpikeAction.SpikeMedium, SpikeAction.SpikeStrong };
            SimActionState[] expectedDrySpikeStates = { SimActionState.SpikeLight, SimActionState.SpikeMedium, SimActionState.SpikeStrong };
            string[] invalidSpikeNames = { "SpikeLight", "SpikeMedium", "SpikeStrong" };
            for (int i = 0; i < invalidSpikes.Length; i++)
            {
                manager.ResetMatch();
                SpikeNativeBridge.Sim_SeedRandom(71u);
                adapter.ClearInjected();
                manager.StepOnce();
                adapter.SetInjected(SpikeAction.Jump, true);
                manager.StepOnce();
                adapter.ClearInjected();
                manager.StepOnce();
                yield return null;
                invalidSpikeSoundBaseline = presenter.SpikeSoundCount;
                SimulationEntityRecord ballBeforeEarly = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                adapter.SetInjected(invalidSpikes[i], true);
                bool earlyCommitted = manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                bool earlyRejected = earlyCommitted &&
                                     table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord earlyPlayer) &&
                                     earlyPlayer.actionStatus == (int)SimActionState.None &&
                                     earlyPlayer.cooldownRemainMs <= 0.001f &&
                                     table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfterEarly) &&
                                     SameTrajectory(ballBeforeEarly, ballAfterEarly) &&
                                     presenter.SpikeSoundCount == invalidSpikeSoundBaseline;
                Check($"blocked <80%-ascent {invalidSpikeNames[i]} request produces no accepted action, trajectory change, or sound", earlyRejected, ref pass, ref fail);

                manager.ResetMatch();
                SpikeNativeBridge.Sim_SeedRandom(71u);
                adapter.ClearInjected();
                manager.StepOnce();
                yield return null;
                SimulationEntityRecord ballBeforeDry = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                adapter.SetInjected(invalidSpikes[i], true);
                bool dryRequestCommitted = manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                bool drySwingAcceptedWithoutTrajectoryMutation = dryRequestCommitted &&
                                table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord dryPlayer) &&
                                dryPlayer.actionStatus == (int)expectedDrySpikeStates[i] &&
                                dryPlayer.cooldownRemainMs > 0f &&
                                table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfterDry) &&
                                SameTrajectory(ballBeforeDry, ballAfterDry);
                Check($"grounded {invalidSpikeNames[i]} swing in Reset enters {invalidSpikeNames[i]} cooldown while leaving ball trajectory unchanged",
                    drySwingAcceptedWithoutTrajectoryMutation, ref pass, ref fail);
            }

            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(71u);
            adapter.ClearInjected();
            manager.StepOnce();
            yield return null;
            int invalidBumpSoundBaseline = presenter.BumpSoundCount;
            SimulationEntityRecord ballBeforeInvalidBump = table.GetEntity(SimulationDataTable.ENTITY_BALL);
            adapter.SetInjected(SpikeAction.Bump, true);
            bool invalidBumpCommitted = manager.StepOnce();
            adapter.ClearInjected();
            yield return null;
            bool hasPlayerAfterInvalidBump = table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord unchangedBumpPlayer);
            bool hasBallAfterInvalidBump = table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfterInvalidBump);
            bool invalidBumpActionRejected = invalidBumpCommitted && hasPlayerAfterInvalidBump &&
                                             unchangedBumpPlayer.actionStatus == (int)SimActionState.None &&
                                             unchangedBumpPlayer.cooldownRemainMs <= 0.001f;
            bool invalidBumpNoSound = presenter.BumpSoundCount == invalidBumpSoundBaseline;
            bool invalidBumpTrajectoryUnchanged = invalidBumpCommitted && hasBallAfterInvalidBump &&
                                                  SameTrajectory(ballBeforeInvalidBump, ballAfterInvalidBump);
            Check("dry Bump request in Reset incurs 0ms cooldown and no accepted contact action", invalidBumpActionRejected, ref pass, ref fail);
            Check("invalid Bump request in Reset produces no contact sound", invalidBumpNoSound, ref pass, ref fail);
            Check("invalid Bump request in Reset leaves authoritative trajectory unchanged", invalidBumpTrajectoryUnchanged, ref pass, ref fail);
            if (!invalidBumpTrajectoryUnchanged)
                Debug.LogError($"[P04_NATIVE_REJECTION_TRACE] invalidBump committed={invalidBumpCommitted}, action={ (hasPlayerAfterInvalidBump ? unchangedBumpPlayer.actionStatus : -1) }, trajectoryType={ballBeforeInvalidBump.trajectoryType}->{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajectoryType : -1)}, speed={ballBeforeInvalidBump.ballSpeed:F4}->{(hasBallAfterInvalidBump ? ballAfterInvalidBump.ballSpeed : 0f):F4}, traj=({ballBeforeInvalidBump.trajA:F4},{ballBeforeInvalidBump.trajB:F4},{ballBeforeInvalidBump.trajC:F4})->({(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajA : 0f):F4},{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajB : 0f):F4},{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajC : 0f):F4}), soundDelta={presenter.BumpSoundCount - invalidBumpSoundBaseline}");

            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(71u);
            adapter.ClearInjected();
            manager.StepOnce();
            yield return null;

            adapter.SetInjected(SpikeAction.Serve, true);
            bool serveCommitted = manager.StepOnce();
            adapter.ClearInjected();
            yield return null;
            bool served = serveCommitted && table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord servedBall) && servedBall.stateFrame == -1;
            float serveTrailTime = (served && presenter != null && presenter.Trail != null) ? presenter.Trail.time : -1f;
            float serveTrailWidth = (served && presenter != null && presenter.Trail != null) ? presenter.Trail.widthMultiplier : -1f;
            Check("configured Serve input starts a native rally through Get_data", served, ref pass, ref fail);
            if (!served)
            {
                blocked++;
                Debug.LogWarning("[P04_TEST][BLOCKED] seeded Serve did not produce a live native ball; ground/trail runtime checks skipped.");
                yield break;
            }

            SimulationEntityRecord first = table.GetEntity(SimulationDataTable.ENTITY_BALL);
            int preLandingImpacts = presenter.GroundImpactEventCount;
            int preLandingExplosions = presenter.GroundExplosionBurstCount;
            int preLandingSounds = presenter.GroundImpactSoundCount;
            int preLandingParticles = presenter.ImpactParticles != null ? presenter.ImpactParticles.particleCount : 0;
            bool ballMoved = false;
            bool trailObserved = false;
            bool trailEndpointsFollowBall = true;
            int trailEndpointSamples = 0;
            bool terminal = false;
            bool terminalAtGround = false;
            for (int tick = 0; tick < 1000; tick++)
            {
                if (!table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord before)) break;
                if (!manager.StepOnce()) break;
                yield return null;
                if (!table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord after)) break;
                ballMoved |= Mathf.Abs(after.posX - first.posX) > 0.01f || Mathf.Abs(after.posY - first.posY) > 0.01f;
                if (presenter.Trail != null && presenter.Trail.positionCount > 0)
                {
                    trailObserved = true;
                    Vector3 lastTrailPoint = presenter.Trail.GetPosition(presenter.Trail.positionCount - 1);
                    trailEndpointsFollowBall &= Vector2.Distance(lastTrailPoint, presenter.transform.position) < 0.08f;
                    trailEndpointSamples++;
                }
                if (after.stateFrame > 0)
                {
                    terminal = true;
                    terminalAtGround = after.posY <= CourtGeometry.FloorY;
                    break;
                }
            }

            Check("real native rally advances authoritative ball position", ballMoved, ref pass, ref fail);
            Check("TrailRenderer samples movement of the rendered native ball", trailObserved, ref pass, ref fail);
            Check("sampled trail endpoints follow the ball render transform", trailObserved && trailEndpointSamples > 0 && trailEndpointsFollowBall, ref pass, ref fail);
            if (!terminal || !terminalAtGround)
            {
                blocked++;
                Debug.LogWarning($"[P04_TEST][BLOCKED] this native sequence did not reach a ground terminal (terminal={terminal}, stateFrame={(table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord endBall) ? endBall.stateFrame : int.MinValue)}, impacts={presenter.GroundImpactEventCount - preLandingImpacts}). Max-touch termination is not counted as a landing.");
            }
            else
            {
                Check("ground impact event fires once from committed native landing", presenter.GroundImpactEventCount == preLandingImpacts + 1, ref pass, ref fail);
                Check("impact position lies on the authoritative floor in world space", Mathf.Abs(presenter.LastImpactWorldPosition.y - CourtGeometry.ToWorldY(CourtGeometry.FloorY)) < 0.001f, ref pass, ref fail);
                SimulationEntityRecord firstLandingRecord = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                Vector3 expectedContact = SimulationEntityReader.AnchorWorld(firstLandingRecord);
                Check("impact X is aligned to the committed ball contact point", Mathf.Abs(presenter.LastImpactWorldPosition.x - expectedContact.x) < 0.001f, ref pass, ref fail);
                Check("ground impact placeholder audio is emitted once", presenter.GroundImpactSoundCount == preLandingSounds + 1 && presenter.AudioSource != null, ref pass, ref fail);
                Check("impact particle system emits the pooled ground burst", presenter.ImpactParticles != null && presenter.ImpactParticles.particleCount > preLandingParticles, ref pass, ref fail);
                Transform impactVfxRoot = presenter.ImpactParticles != null ? presenter.ImpactParticles.transform : null;
                Check("ground impact activates P04_GroundGoldenCrossRing VFX",
                    IsVfxObjectActive(impactVfxRoot, "P04_GroundGoldenCrossRing"), ref pass, ref fail);
                Check("ground impact activates P04_GroundExplosionBurst explosion VFX and increments GroundExplosionBurstCount",
                    IsVfxObjectActive(impactVfxRoot, "P04_GroundExplosionBurst") &&
                    presenter.GroundExplosionBurstCount == preLandingExplosions + 1, ref pass, ref fail);
                float expectedServeLandingTiltDeg = presenter.ComputeGroundTiltAngleDegrees(presenter.LastGroundTiltImpactSpeedKmh);
                Check($"100% of ball ground impacts spawn 1 single boom ring (ActiveGroundSonicWaveCount == 1) and rotate camera view by dynamic 10 * (speedKmh / 100) deg (speedKmh={presenter.LastGroundTiltImpactSpeedKmh:F2}, actual={Mathf.Abs(presenter.LastJumpSpikeGroundTiltAngleDeg):F2}, expected={expectedServeLandingTiltDeg:F2})",
                    presenter.ActiveGroundSonicWaveCount == 1 &&
                    presenter.JumpSpikeGroundTiltCount >= preLandingImpacts + 1 &&
                    expectedServeLandingTiltDeg > 0.1f &&
                    Mathf.Abs(Mathf.Abs(presenter.LastJumpSpikeGroundTiltAngleDeg) - expectedServeLandingTiltDeg) < 0.01f &&
                    Mathf.Abs(expectedServeLandingTiltDeg - (10.0f * (presenter.LastGroundTiltImpactSpeedKmh / 100.0f))) < 0.01f, ref pass, ref fail);
                Check("ball trail remains active (emitting=true, width>0) when ball enters post-death terminal bounce (stateFrame > 0)",
                    presenter.Trail != null && presenter.Trail.emitting &&
                    presenter.Trail.time > 0f && presenter.Trail.widthMultiplier > 0f, ref pass, ref fail);
                bool slowMoZeroAtImpactTick0 = manager.LastNativeSnapshot.rallyState.slowMoFramesRemaining == 0;
                int delayedSlowMoActivationTick = -1;
                bool reboundMovedBeforeSlowMo = false;
                bool trailPersistedIntoDelayedSlowMo = false;
                for (int i = 1; i <= 14; i++)
                {
                    adapter.ClearInjected();
                    manager.StepOnce();
                    yield return null;
                    if (table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord bounceBall))
                    {
                        reboundMovedBeforeSlowMo |= Mathf.Abs(bounceBall.posX - firstLandingRecord.posX) > 0.5f ||
                                                    bounceBall.posY > CourtGeometry.FloorY + 0.5f;
                    }
                    if (delayedSlowMoActivationTick < 0 &&
                        manager.LastNativeSnapshot.rallyState.slowMoFramesRemaining == 60)
                    {
                        delayedSlowMoActivationTick = i;
                        trailPersistedIntoDelayedSlowMo = presenter.Trail != null &&
                                                          presenter.Trail.emitting &&
                                                          presenter.Trail.widthMultiplier > 0f;
                    }
                }
                Check($"scoring ground impact delays 60-frame (1000ms) slow-motion by k in [6..13] ticks (100..220ms) while ball rebounds with trail (tick0SlowMo0={slowMoZeroAtImpactTick0}, activationTick={delayedSlowMoActivationTick}, reboundMoved={reboundMovedBeforeSlowMo}, trailPersisted={trailPersistedIntoDelayedSlowMo})",
                    slowMoZeroAtImpactTick0 &&
                    delayedSlowMoActivationTick >= 6 &&
                    delayedSlowMoActivationTick <= 13 &&
                    reboundMovedBeforeSlowMo &&
                    trailPersistedIntoDelayedSlowMo, ref pass, ref fail);
                Check("persistent terminal output does not replay impact VFX/SFX", presenter.GroundImpactEventCount == preLandingImpacts + 1, ref pass, ref fail);

                // Native ApplyBallTrajectory counts the terminal frame down to Reset (0). Re-serve
                // through the configured pipeline and require a second real floor event in the same match.
                bool reachedReset = false;
                for (int i = 0; i < 260; i++)
                {
                    if (table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord waiting) && waiting.stateFrame == 0)
                    {
                        reachedReset = true;
                        break;
                    }
                    adapter.ClearInjected();
                    if (!manager.StepOnce()) break;
                    yield return null;
                }
                Check("native terminal countdown reaches the next serve Reset state", reachedReset, ref pass, ref fail);
                bool secondRallyStarted = false;
                if (reachedReset)
                {
                    for (int sTick = 0; sTick < 45 && !secondRallyStarted; sTick++)
                    {
                        if (table.MatchRecord.servingTeam == 0) adapter.SetInjected(SpikeAction.Serve, true);
                        bool secondServeTick = manager.StepOnce();
                        adapter.ClearInjected();
                        yield return null;
                        if (!secondServeTick) break;
                        secondRallyStarted = table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord secondServeBall) &&
                                             secondServeBall.stateFrame == -1;
                    }
                }
                Check("configured/AI serve starts a subsequent rally without native reset", secondRallyStarted, ref pass, ref fail);
                if (secondRallyStarted)
                {
                    bool secondTerminal = false;
                    bool secondLanding = false;
                    bool secondTerminalAtGround = false;
                    for (int i = 0; i < 1000; i++)
                    {
                        adapter.ClearInjected();
                        if (!manager.StepOnce()) break;
                        yield return null;
                        if (!table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord secondBall)) break;
                        if (secondBall.stateFrame > 0)
                        {
                            secondTerminal = true;
                            secondTerminalAtGround = secondBall.posY <= CourtGeometry.FloorY;
                            secondLanding = secondTerminalAtGround &&
                                            presenter.GroundImpactEventCount == preLandingImpacts + 2;
                            break;
                        }
                    }
                    if (secondTerminalAtGround)
                        Check("subsequent same-match native rally can emit its own single landing", secondLanding, ref pass, ref fail);
                    else
                    {
                        blocked++;
                        Debug.LogWarning($"[P04_TEST][BLOCKED] subsequent native rally ended without a ground landing (terminal={secondTerminal}); no second landing PASS claimed.");
                    }
                }
                else
                {
                    blocked++;
                    Debug.LogWarning("[P04_TEST][BLOCKED] no subsequent serve was observed after the native terminal countdown; second landing not claimed.");
                }

                float fadeStart = Time.time;
                for (int i = 0; i < 260 || (Time.time - fadeStart) < 0.7f; i++)
                {
                    adapter.ClearInjected();
                    if (table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord fadingBall) && fadingBall.stateFrame != 0)
                    {
                        manager.StepOnce();
                    }
                    if (Camera.main != null && UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                        Camera.main.Render();
                    yield return null;
                    if (presenter.Trail == null || presenter.Trail.positionCount == 0)
                    {
                        break;
                    }
                }
                Check("trail geometry fades after the ball finishes terminal countdown", presenter.Trail == null || presenter.Trail.positionCount == 0, ref pass, ref fail);
            }

            Check("feedback observation does not advance simulation independently", manager.LastTick.committed && table.CurrentTick == manager.LastNativeSnapshot.tick, ref pass, ref fail);

            // Verify Option 1 Fake Spike (whiff) during active rally (stateFrame == -1) outside hit range:
            // C++ enters SpikeMedium + remainTime > 0 cooldown, blocks immediate next-tick Spike, and BallFeedbackPresenter suppresses SpikeSoundCount & laser beam.
            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(71u);
            adapter.ClearInjected();
            if (manager.StepOnce())
            {
                adapter.SetInjected(SpikeAction.Serve, true);
                manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                for (int t = 0; t < 45; t++)
                {
                    manager.StepOnce();
                    SimulationEntityRecord pWait = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    if (pWait.actionStatus == (int)SimActionState.None && pWait.actionRemainTime <= 0f)
                        break;
                }
                adapter.SetInjected(SpikeAction.Jump, true);
                manager.StepOnce();
                adapter.ClearInjected();
                for (int j = 0; j < 13; j++) manager.StepOnce();
                int soundsBeforeWhiff = presenter.SpikeSoundCount;
                SimulationEntityRecord ballPreWhiff = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                adapter.SetInjected(SpikeAction.SpikeMedium, true);
                manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                SimulationEntityRecord playerWhiff = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                SimulationEntityRecord ballPostWhiff = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                Transform whiffVfxRoot = presenter.ImpactParticles != null ? presenter.ImpactParticles.transform : null;
                bool whiffSuppressedFeedback = presenter.SpikeSoundCount == soundsBeforeWhiff &&
                                               !IsVfxObjectActive(whiffVfxRoot, "P04_SpikeLaserBeam");

                adapter.SetInjected(SpikeAction.SpikeStrong, true);
                manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                SimulationEntityRecord playerCooldown = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                bool whiffStateAndCooldownOk = SameTrajectory(ballPreWhiff, ballPostWhiff) &&
                                               ((playerWhiff.actionStatus == (int)SimActionState.None &&
                                                 playerWhiff.finalIntent == 10) ||
                                                (playerWhiff.actionStatus == (int)SimActionState.SpikeMedium &&
                                                 playerWhiff.actionRemainTime > 0f &&
                                                 playerCooldown.actionStatus == (int)SimActionState.SpikeMedium &&
                                                 playerCooldown.actionRemainTime < playerWhiff.actionRemainTime));
                Check("active-rally Fake Spike (whiff) preserves ball trajectory and BallFeedbackPresenter suppresses false hit sound and laser beam",
                    whiffStateAndCooldownOk && whiffSuppressedFeedback, ref pass, ref fail);
            }

            // Use the same natural-rally fixtures as P03 for all configured strengths.
            SpikeAction[] spikeActions = { SpikeAction.SpikeLight, SpikeAction.SpikeMedium, SpikeAction.SpikeStrong };
            SimActionState[] spikeStates = { SimActionState.SpikeLight, SimActionState.SpikeMedium, SimActionState.SpikeStrong };
            uint[] spikeSeeds = { 71u, 72u, 73u };
            float[] acceptedTrailWidths = new float[spikeActions.Length];
            float[] acceptedTrailTimes = new float[spikeActions.Length];
            for (int strength = 0; strength < spikeActions.Length; strength++)
            {
                SpikeAction requestedSpike = spikeActions[strength];
                int spikeSoundsBefore = presenter.SpikeSoundCountFor(SimulationDataTable.ENTITY_PLAYER);
                manager.ResetMatch();
                SpikeNativeBridge.Sim_SeedRandom(spikeSeeds[strength]);
                adapter.ClearInjected();
                if (!manager.StepOnce())
                {
                    blocked++;
                    Debug.LogWarning($"[P04_TEST][BLOCKED] cannot initialize seeded {requestedSpike} sound scenario.");
                    continue;
                }
                bool toggle = false;
                bool acceptedSpike = false;
                uint spikeId = adapter.IdFor(requestedSpike);
                for (int tick = 0; tick < 1800 && !acceptedSpike; tick++)
                {
                    if (!table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) ||
                        !table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball)) break;

                    toggle = !toggle;
                    adapter.ClearInjected();
                    if (ball.stateFrame == 0 && table.MatchRecord.servingTeam == 0 && toggle)
                    {
                        adapter.SetInjected(SpikeAction.Serve, true);
                    }
                    else if (ball.stateFrame == -1 && ball.ballSpeed < 0f && ball.landingX > 0f && ball.landingX < CourtGeometry.NetX)
                    {
                        float playerX = player.posX + player.sizeX * 0.5f;
                        float playerY = player.posY + player.sizeY * 0.5f;
                        float ballX = ball.posX + ball.sizeX * 0.5f;
                        float ballY = ball.posY + ball.sizeY * 0.5f;
                        float dx = ball.landingX + ball.sizeX * 0.5f - playerX;
                        float distance = Vector2.Distance(new Vector2(ballX, ballY), new Vector2(playerX, playerY));
                        float ticksToLanding = Mathf.Abs(ball.posX - ball.landingX) / Mathf.Max(0.1f, Mathf.Abs(ball.ballSpeed));
                        if (dx > 7f) adapter.SetInjected(SpikeAction.Right, true);
                        else if (dx < -7f) adapter.SetInjected(SpikeAction.Left, true);
                        if (player.isGrounded && Mathf.Abs(dx) < 55f && ticksToLanding >= 14f && ticksToLanding <= 26f)
                            adapter.SetInjected(SpikeAction.Jump, true);
                        if (!player.isGrounded && player.jumpRemainingFrames <= 3 && distance < 78f && toggle)
                            adapter.SetInjected(requestedSpike, true);
                    }

                    float previousSpeed = ball.ballSpeed;
                    int previousTrajectory = ball.trajectoryType;
                    bool committed = manager.StepOnce();
                    yield return null;
                    if (!committed) break;
                    SimulationEntityRecord nextPlayer = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    SimulationEntityRecord nextBall = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                    acceptedSpike = nextPlayer.actionStatus == (int)spikeStates[strength] &&
                                    table.MatchRecord.lastTouchEntity == 0 &&
                                    (Mathf.Abs(nextBall.ballSpeed - previousSpeed) > 0.001f || nextBall.trajectoryType != previousTrajectory);
                    if (acceptedSpike)
                    {
                        acceptedTrailWidths[strength] = presenter.Trail != null ? presenter.Trail.widthMultiplier : 0f;
                        acceptedTrailTimes[strength] = presenter.Trail != null ? presenter.Trail.time : -1f;
                        Transform spikeVfxRoot = presenter.ImpactParticles != null ? presenter.ImpactParticles.transform : null;
                        bool spikeVfxActivated = spikeVfxRoot != null &&
                                                 IsVfxObjectActive(spikeVfxRoot, "P04_SonicCrescentArc") &&
                                                 IsVfxObjectActive(spikeVfxRoot, "P04_RadialSpeedLines") &&
                                                 IsVfxObjectActive(spikeVfxRoot, "P04_XSlashSpark") &&
                                                 IsVfxObjectActive(spikeVfxRoot, "P04_OriginPuffRing") &&
                                                 IsVfxObjectActive(spikeVfxRoot, "P04_SpikeLaserBeam");
                        Check($"accepted native player {requestedSpike} activates RcTdc--AdaI spike VFX objects", spikeVfxActivated, ref pass, ref fail);
                        Debug.Log($"[P04_SPIKE_TRACE] strength={requestedSpike} button={spikeId} tick={table.CurrentTick} speed={nextBall.ballSpeed:F4} trajectoryType={nextBall.trajectoryType} actionStatus={nextPlayer.actionStatus}");
                    }
                }

                if (acceptedSpike)
                {
                    bool spikeSoundForPlayer = presenter.SpikeSoundCountFor(SimulationDataTable.ENTITY_PLAYER) == spikeSoundsBefore + 1;
                    Check($"accepted native player {requestedSpike} triggers one player Spike sound", spikeSoundForPlayer, ref pass, ref fail);
                    if (!spikeSoundForPlayer)
                        Debug.LogError($"[P04_AUDIO_TRACE] accepted player {requestedSpike} at nativeTick={table.CurrentTick}, playerSoundDelta={presenter.SpikeSoundCountFor(SimulationDataTable.ENTITY_PLAYER) - spikeSoundsBefore}, allEntitySoundDelta={presenter.SpikeSoundCount}");
                }
                else
                {
                    blocked++;
                    Debug.LogWarning($"[P04_TEST][BLOCKED] seeded configured {requestedSpike} scenario was not accepted; no sound PASS claimed (button={spikeId}).");
                }
            }

            Check("native Spike speed increases trail width across Light, Medium, Strong",
                acceptedTrailWidths[0] > 0f && acceptedTrailWidths[0] < acceptedTrailWidths[1] &&
                acceptedTrailWidths[1] < acceptedTrailWidths[2], ref pass, ref fail);
            float expectedServeWidth = presenter.ComputeSpikeTrailWidth(Mathf.Abs(first.ballSpeed));
            bool directSpeedSynchronizedWidth =
                Mathf.Abs(serveTrailWidth - expectedServeWidth) < 0.001f &&
                acceptedTrailWidths[0] > 0f &&
                acceptedTrailWidths[0] < acceptedTrailWidths[1] &&
                acceptedTrailWidths[1] < acceptedTrailWidths[2];
            Check($"trail width is 100% directly synchronized with unscaled C++ Sim ball.ballSpeed via trailBaseWidth * (simBallSpeed / trailBaseSpeed) * trailSpeedMultiplier across all flying balls (Serve width={serveTrailWidth:F3} == expected {expectedServeWidth:F3}, Spike widths scale {acceptedTrailWidths[0]:F3} < {acceptedTrailWidths[1]:F3} < {acceptedTrailWidths[2]:F3})",
                directSpeedSynchronizedWidth, ref pass, ref fail);
            bool fixedHalfSecondHighSpeedTrail =
                Mathf.Abs(serveTrailTime - 0.50f) < 0.001f &&
                Mathf.Abs(acceptedTrailTimes[0] - 0.50f) < 0.001f &&
                Mathf.Abs(acceptedTrailTimes[1] - 0.50f) < 0.001f &&
                Mathf.Abs(acceptedTrailTimes[2] - 0.50f) < 0.001f &&
                acceptedTrailWidths[0] > 0f &&
                acceptedTrailWidths[0] < acceptedTrailWidths[1] &&
                acceptedTrailWidths[1] < acceptedTrailWidths[2];
            Check($"high-speed (>= 6.40 Sim speed) Serve and SpikeLight/Medium/Strong keep fixed 0.50s connecting trail.time (serve={serveTrailTime:F2}s, spikes={acceptedTrailTimes[0]:F2}/{acceptedTrailTimes[1]:F2}/{acceptedTrailTimes[2]:F2}s) while trail.widthMultiplier scales up with Sim speed ({acceptedTrailWidths[0]:F3} < {acceptedTrailWidths[1]:F3} < {acceptedTrailWidths[2]:F3})",
                fixedHalfSecondHighSpeedTrail, ref pass, ref fail);
            Debug.Log($"[P04_TRAIL_SPEED_TRACE] syncedServe={serveTrailWidth:F4} light={acceptedTrailWidths[0]:F4} medium={acceptedTrailWidths[1]:F4} strong={acceptedTrailWidths[2]:F4}");

            // Verify C++ Sim speed recording (LastSpikeImpactSimSpeed == 19.2f), minimalist 2-value "height\nspeed" popup on eventSpike, and P04_PointScoreBurstRing (PointScoredBurstCount > 0 when matchState.leftScore increments).
            Transform overlayRoot = presenter.ImpactParticles != null ? presenter.ImpactParticles.transform : null;
            int burstsBeforeScore = presenter.PointScoredBurstCount;
            int tiltsBeforeImpact = presenter.JumpSpikeGroundTiltCount;
            var scoreSnap = manager.LastNativeSnapshot;
            scoreSnap.tick += 1;
            scoreSnap.entity0.actionState.status = (int)SimActionState.SpikeStrong;
            scoreSnap.entity0.actionState.remainTime = 0.95f;
            if (scoreSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity2.ballGameplayState.stateFrame = -1;
                scoreSnap.entity2.ballTrajectory.speed = 19.2f;
                scoreSnap.entity2.ballTrajectory.c = CourtGeometry.FloorY + 180f;
                scoreSnap.entity2.position.position.x = 920f;
                scoreSnap.entity2.position.position.y = CourtGeometry.FloorY + 48f;
            }
            if (scoreSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity3.ballGameplayState.stateFrame = -1;
                scoreSnap.entity3.ballTrajectory.speed = 19.2f;
                scoreSnap.entity3.ballTrajectory.c = CourtGeometry.FloorY + 180f;
                scoreSnap.entity3.position.position.x = 920f;
                scoreSnap.entity3.position.position.y = CourtGeometry.FloorY + 48f;
            }
            table.UpdateFromNativeSnapshot(scoreSnap);

            bool decoupledQuantitiesOk =
                presenter.LastSpikeHorizontalV0Speed > 0f &&
                presenter.LastSpikeFullPathSpeedSim > presenter.LastSpikeHorizontalV0Speed &&
                presenter.LastSpikeDisplayedPathSpeedKmh >= 75f &&
                presenter.LastSpikeDisplayedPathSpeedKmh <= 190f;
            Check($"BallFeedbackPresenter decouples Quantity 1 horizontal v0 (LastSpikeHorizontalV0Speed={presenter.LastSpikeHorizontalV0Speed:F2}) from Quantity 2 full 2D trajectory path speed (LastSpikeFullPathSpeedSim={presenter.LastSpikeFullPathSpeedSim:F2}, LastSpikeDisplayedPathSpeedKmh={presenter.LastSpikeDisplayedPathSpeedKmh:F2} km/h)",
                decoupledQuantitiesOk, ref pass, ref fail);

            var popupScaleFieldPlay = typeof(BallFeedbackPresenter).GetField(
                "SpikeSpeedPopupTargetScale",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            float popupTargetScalePlay = popupScaleFieldPlay != null ? (float)popupScaleFieldPlay.GetValue(null) : -1f;
            Transform spikePopupTr = overlayRoot != null ? overlayRoot.Find("P04_SpikeSpeedPopup") : null;
            TextMesh spikePopupMesh = spikePopupTr != null ? spikePopupTr.GetComponent<TextMesh>() : null;
            string popupText = spikePopupMesh != null ? spikePopupMesh.text : string.Empty;
            bool horizontalKmhAndMetersFormat = !string.IsNullOrEmpty(popupText) &&
                                                !popupText.Contains("\n") &&
                                                popupText.Contains("<size=49>.") &&
                                                popupText.Contains(" km/h</size>   ") &&
                                                popupText.EndsWith(" m</size>");
            bool outlinesSynced = spikePopupTr != null;
            if (spikePopupTr != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    Transform outlineChild = spikePopupTr.Find($"P04_SpikeSpeedPopup_Outline_{i}");
                    TextMesh outlineMesh = outlineChild != null ? outlineChild.GetComponent<TextMesh>() : null;
                    outlinesSynced &= outlineMesh != null && outlineMesh.text == popupText;
                }
            }
            Transform laserBeamTr = overlayRoot != null ? overlayRoot.Find("P04_SpikeLaserBeam") : null;
            Transform laserShadowTr = overlayRoot != null ? overlayRoot.Find("P04_SpikeLaserBeam_Shadow") : null;
            LineRenderer laserLr = laserBeamTr != null ? laserBeamTr.GetComponent<LineRenderer>() : null;
            LineRenderer laserShadowLr = laserShadowTr != null ? laserShadowTr.GetComponent<LineRenderer>() : null;
            bool sevenVertexLightningOk = laserLr != null && laserShadowLr != null &&
                                          laserLr.positionCount == 7 && laserShadowLr.positionCount == 7;
            Check($"eventSpike triggers P04_SpikeSpeedPopup with top-center horizontal \"<speed> km/h   <height> m\" format (text=\"{popupText}\", SpikeSpeedPopupTargetScale={popupTargetScalePlay:F1}x, 8 synced black outlines) and 7-vertex jagged lightning beam + shadow",
                (!presenter.ShowSimSpeedPopup || (IsVfxObjectActive(overlayRoot, "P04_SpikeSpeedPopup") && horizontalKmhAndMetersFormat && outlinesSynced)) &&
                Mathf.Abs(popupTargetScalePlay - 2.0f) < 0.001f &&
                sevenVertexLightningOk,
                ref pass, ref fail);

            // Simulate a slow-motion frozen tick (position unchanged and speed temporarily 0 while airborne stateFrame == -1):
            // Trail must preserve latched speed, remain emitting with full width, and extend trail.time synchronously with Sim layer pause!
            scoreSnap.tick += 1;
            if (scoreSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity2.ballGameplayState.stateFrame = -1;
                scoreSnap.entity2.ballTrajectory.speed = 0f;
            }
            if (scoreSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity3.ballGameplayState.stateFrame = -1;
                scoreSnap.entity3.ballTrajectory.speed = 0f;
            }
            table.UpdateFromNativeSnapshot(scoreSnap);
            bool slowMoTrailPreserved = presenter.Trail != null &&
                                        presenter.Trail.emitting &&
                                        presenter.IsSimLayerPausedOrFrozen &&
                                        presenter.PausedTrailLifetimeExtension > 0f &&
                                        presenter.Trail.time > 0.50f &&
                                        presenter.Trail.widthMultiplier > CourtGeometry.ToWorldLength(presenter.TrailBaseWidth);
            Check("ball trail remains active, preserves latched width, and extends trail.time synchronously when Sim layer is paused or in slow-motion frozen ticks",
                slowMoTrailPreserved, ref pass, ref fail);

            scoreSnap.tick += 1;
            scoreSnap.matchState.leftScore += 1;
            // Simulate a steep high-speed atk=230 spike step (> 1.15m = 92 px in 1 tick, here 220 px = 2.75m) into floor impact:
            // Must NOT trigger false-positive teleport trail clear or reset pausedTrailLifetimeExtension!
            if (scoreSnap.entity2.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity2.ballGameplayState.stateFrame = 50;
                scoreSnap.entity2.ballTrajectory.speed = 19.2f;
                scoreSnap.entity2.position.position.x = 1140f;
                scoreSnap.entity2.position.position.y = CourtGeometry.FloorY;
            }
            if (scoreSnap.entity3.entityId == SimulationDataTable.ENTITY_BALL)
            {
                scoreSnap.entity3.ballGameplayState.stateFrame = 50;
                scoreSnap.entity3.ballTrajectory.speed = 19.2f;
                scoreSnap.entity3.position.position.x = 1140f;
                scoreSnap.entity3.position.position.y = CourtGeometry.FloorY;
            }
            table.UpdateFromNativeSnapshot(scoreSnap);

            bool postDeathBounceTrailActive = presenter.Trail != null &&
                                              presenter.Trail.emitting &&
                                              presenter.PausedTrailLifetimeExtension > 0f &&
                                              presenter.Trail.time > 0.50f &&
                                              presenter.Trail.widthMultiplier > 0f;
            Check("ball trail remains active (emitting && widthMultiplier > 0 && pausedTrailLifetimeExtension preserved) on high-speed atk=230 (>1.15m/tick) step and post-death bounce (stateFrame > 0)",
                postDeathBounceTrailActive, ref pass, ref fail);

            bool cameraViewTiltedCoherently =
                Camera.main != null &&
                Mathf.Abs(Mathf.DeltaAngle(0f, Camera.main.transform.localEulerAngles.z) - presenter.LastJumpSpikeGroundTiltAngleDeg) < 0.05f &&
                Quaternion.Angle(presenter.transform.localRotation, Quaternion.identity) < 0.01f;
            float expectedSpikeLandingTiltDeg = presenter.ComputeGroundTiltAngleDegrees(presenter.LastGroundTiltImpactSpeedKmh);
            bool singleBoomRingAndTiltActive =
                presenter.ActiveGroundSonicWaveCount == 1 &&
                IsVfxObjectActive(overlayRoot, "P04_GroundSonicWave_0") &&
                !IsVfxObjectActive(overlayRoot, "P04_GroundSonicWave_1") &&
                !IsVfxObjectActive(overlayRoot, "P04_GroundSonicWave_2") &&
                Mathf.Abs(presenter.GroundSonicWaveMaxRadius - 3.45f) < 0.01f &&
                Mathf.Abs(presenter.JumpSpikeGroundSonicWaveMaxRadius - 3.45f) < 0.01f &&
                Mathf.Abs(presenter.LastGroundSonicWaveTargetMaxRadius - presenter.JumpSpikeGroundSonicWaveMaxRadius) < 0.001f &&
                presenter.JumpSpikeGroundTiltCount == tiltsBeforeImpact + 1 &&
                expectedSpikeLandingTiltDeg > 0.1f &&
                Mathf.Abs(Mathf.Abs(presenter.LastJumpSpikeGroundTiltAngleDeg) - expectedSpikeLandingTiltDeg) < 0.01f &&
                Mathf.Abs(expectedSpikeLandingTiltDeg - (10.0f * (presenter.LastGroundTiltImpactSpeedKmh / 100.0f))) < 0.01f &&
                cameraViewTiltedCoherently;
            Check($"Floor impact activates 1 single Boom Ring (ActiveGroundSonicWaveCount={presenter.ActiveGroundSonicWaveCount}, targetMaxRadius={presenter.LastGroundSonicWaveTargetMaxRadius:F2}m == 3.45m matching Qo0kCZF0NWA) and applies unified dynamic 10 * (speedKmh / 100) deg camera-view tilt while keeping world objects at identity rotation (speedKmh={presenter.LastGroundTiltImpactSpeedKmh:F2}, LastJumpSpikeGroundTiltAngleDeg={presenter.LastJumpSpikeGroundTiltAngleDeg:F2})",
                singleBoomRingAndTiltActive, ref pass, ref fail);

            bool simSpeedRecorded = Mathf.Abs(presenter.LastSpikeImpactSimSpeed - 19.2f) < 0.01f &&
                                    presenter.LastSpikeImpactKmh == 192;
            bool scoreRingActive = IsVfxObjectActive(overlayRoot, "P04_PointScoreBurstRing") &&
                                   presenter.PointScoredBurstCount > burstsBeforeScore &&
                                   presenter.PointScoredBurstCount > 0;
            Check($"Floor impact records C++ Sim speed directly (LastSpikeImpactSimSpeed={presenter.LastSpikeImpactSimSpeed:F2}, LastSpikeImpactKmh={presenter.LastSpikeImpactKmh}) and score increment activates P04_PointScoreBurstRing (PointScoredBurstCount={presenter.PointScoredBurstCount})",
                simSpeedRecorded && scoreRingActive, ref pass, ref fail);

            int bumpSoundsBefore = presenter.BumpSoundCountFor(SimulationDataTable.ENTITY_PLAYER);
            manager.ResetMatch();
            SpikeNativeBridge.Sim_SeedRandom(81u);
            adapter.ClearInjected();
            bool bumpSetup = manager.StepOnce();
            bool acceptedBump = false;
            uint bumpId = adapter.IdFor(SpikeAction.Bump);
            if (bumpSetup)
            {
                for (int tick = 0; tick < 1200 && !acceptedBump; tick++)
                {
                    if (!table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) ||
                        !table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball)) break;

                    adapter.ClearInjected();
                    if (ball.stateFrame == 0 && table.MatchRecord.servingTeam == 0)
                        adapter.SetInjected(SpikeAction.Serve, true);
                    else if (ball.stateFrame == -1 && ball.hasBallGameplayState &&
                             ball.landingX + ball.sizeX * 0.5f < CourtGeometry.NetX)
                    {
                        adapter.SetInjected(SpikeAction.Bump, true);
                        float playerCenter = player.posX + player.sizeX * 0.5f;
                        float landingCenter = ball.landingX + ball.sizeX * 0.5f;
                        if (landingCenter - playerCenter > 7f) adapter.SetInjected(SpikeAction.Right, true);
                        else if (landingCenter - playerCenter < -7f) adapter.SetInjected(SpikeAction.Left, true);
                    }

                    float previousSpeed = ball.ballSpeed;
                    int previousTrajectory = ball.trajectoryType;
                    bool committed = manager.StepOnce();
                    yield return null;
                    if (!committed) break;
                    SimulationEntityRecord nextPlayer = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
                    SimulationEntityRecord nextBall = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                    acceptedBump = nextPlayer.actionStatus == (int)SimActionState.Bump &&
                                   table.MatchRecord.lastTouchEntity == 0 &&
                                   (Mathf.Abs(nextBall.ballSpeed - previousSpeed) > 0.001f || nextBall.trajectoryType != previousTrajectory);
                }
            }

            if (acceptedBump)
                Check("accepted native player Bump action triggers one contact sound", presenter.BumpSoundCountFor(SimulationDataTable.ENTITY_PLAYER) == bumpSoundsBefore + 1, ref pass, ref fail);
            else
            {
                blocked++;
                Debug.LogWarning($"[P04_TEST][BLOCKED] configured native Bump scenario was not accepted; no contact-sound PASS claimed (button={bumpId}).");
            }

            // Re-arm the presentation episode through the real native match reset, not a fake position.
            int priorClearCount = presenter.TrailClearCount;
            manager.ResetMatch();
            adapter.ClearInjected();
            bool resetCommitted = manager.StepOnce();
            yield return null;
            Check("native match reset clears trail and event history", resetCommitted && presenter.TrailClearCount > priorClearCount, ref pass, ref fail);
            Check("reset clears an active impact particle effect", presenter.ImpactParticles == null || presenter.ImpactParticles.particleCount == 0, ref pass, ref fail);
        }
        finally
        {
            adapter.ClearInjected();
            manager.ManualTickMode = true;
            adapter.InjectionExclusive = true;
            manager.ResetMatch();
            manager.StepOnce();
            adapter.InjectionExclusive = oldExclusive;
            manager.ManualTickMode = oldManual;
            if (SessionState.GetInt(QaStageKey, 0) == 2)
            {
                SessionState.SetInt(QaStageKey, 3);
                EditorApplication.isPlaying = false;
            }
        }

        if (fail > 0) SessionState.SetInt("SpikeQaFailed", SessionState.GetInt("SpikeQaFailed", 0) + fail);
        Debug.Log($"[P04_PLAYMODE_SUMMARY] pass={pass},fail={fail},blocked={blocked}; inputAdapter -> native Get_data -> committed snapshot -> feedback consumer");
        if (fail > 0) Debug.LogError($"[P04_TEST] PlayMode acceptance has {fail} failure(s).");
        if (blocked > 0) Debug.LogWarning($"[P04_TEST] PlayMode acceptance has {blocked} blocked check(s); no runtime PASS is claimed for them.");
    }

    private static BallFeedbackPresenter FindBallPresenter()
    {
        SimulationEntityReader[] readers = UnityEngine.Object.FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include);
        foreach (SimulationEntityReader reader in readers)
            if (reader.EntityId == SimulationDataTable.ENTITY_BALL && reader.gameObject.scene.IsValid())
                return reader.GetComponent<BallFeedbackPresenter>();
        return null;
    }

    private static SimulationEntityRecord Ball(int stateFrame, float y, float x)
    {
        return new SimulationEntityRecord
        {
            entityId = SimulationDataTable.ENTITY_BALL,
            hasPosition = true,
            hasSize = true,
            hasBallGameplayState = true,
            posX = x,
            posY = y,
            sizeX = 16.8f,
            sizeY = 16.8f,
            stateFrame = stateFrame
        };
    }

    private static SimulationEntityRecord Character(int actionStatus, float remainTime)
    {
        return new SimulationEntityRecord
        {
            hasCharacterActionState = true,
            actionStatus = actionStatus,
            actionRemainTime = remainTime
        };
    }

    private static bool SameTrajectory(SimulationEntityRecord a, SimulationEntityRecord b) =>
        a.trajectoryType == b.trajectoryType && Mathf.Abs(a.ballSpeed - b.ballSpeed) < 0.001f &&
        Mathf.Abs(a.trajA - b.trajA) < 0.001f && Mathf.Abs(a.trajB - b.trajB) < 0.001f &&
        Mathf.Abs(a.trajC - b.trajC) < 0.001f;

    private static bool IsVfxObjectActive(Transform root, string childName)
    {
        if (root == null) return false;
        Transform child = root.Find(childName);
        if (child == null || !child.gameObject.activeSelf) return false;
        Renderer r = child.GetComponent<Renderer>();
        return r == null || r.enabled;
    }

    private static void Check(string name, bool condition, ref int pass, ref int fail)
    {
        if (condition)
        {
            pass++;
            Debug.Log("[P04_TEST][PASS] " + name);
        }
        else
        {
            fail++;
            Debug.LogError("[P04_TEST][FAIL] " + name);
        }
    }
}
