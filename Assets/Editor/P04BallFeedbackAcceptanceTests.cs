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
        Check("ground effect uses one reusable ParticleSystem", presenter.ImpactParticles != null && presenter.ImpactParticles.main.maxParticles <= 24, ref pass, ref fail);
        Check("impact particle size is independent of the ball sprite's hierarchy scale", presenter.ImpactParticles != null && presenter.ImpactParticles.main.scalingMode == ParticleSystemScalingMode.Local, ref pass, ref fail);
        Check("audio uses one source and generated original placeholder clips", presenter.AudioSource != null && presenter.ProceduralAudioReady, ref pass, ref fail);
        Check("audio categories have independent configured volumes", presenter.SpikeVolume >= 0f && presenter.BumpVolume >= 0f && presenter.GroundImpactVolume >= 0f, ref pass, ref fail);
        Check("feedback component is bound to a SimulationDataTable when one exists", SimulationDataTable.Instance == null || !SimulationDataTable.Instance.IsInitialized || presenter.HasBoundTable, ref pass, ref fail);
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
            string[] invalidSpikeNames = { "SpikeLight", "SpikeMedium", "SpikeStrong" };
            for (int i = 0; i < invalidSpikes.Length; i++)
            {
                SimulationEntityRecord ballBeforeInvalid = table.GetEntity(SimulationDataTable.ENTITY_BALL);
                adapter.SetInjected(invalidSpikes[i], true);
                bool invalidRequestCommitted = manager.StepOnce();
                adapter.ClearInjected();
                yield return null;
                bool rejected = invalidRequestCommitted && table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord unchangedPlayer) &&
                                unchangedPlayer.actionStatus == (int)SimActionState.None &&
                                table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfterInvalid) &&
                                SameTrajectory(ballBeforeInvalid, ballAfterInvalid) &&
                                presenter.SpikeSoundCount == invalidSpikeSoundBaseline;
                Check($"invalid {invalidSpikeNames[i]} request in Reset produces no accepted action, trajectory change, or sound", rejected, ref pass, ref fail);
            }

            int invalidBumpSoundBaseline = presenter.BumpSoundCount;
            SimulationEntityRecord ballBeforeInvalidBump = table.GetEntity(SimulationDataTable.ENTITY_BALL);
            adapter.SetInjected(SpikeAction.Bump, true);
            bool invalidBumpCommitted = manager.StepOnce();
            adapter.ClearInjected();
            yield return null;
            bool hasPlayerAfterInvalidBump = table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord unchangedBumpPlayer);
            bool hasBallAfterInvalidBump = table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ballAfterInvalidBump);
            bool invalidBumpActionRejected = invalidBumpCommitted && hasPlayerAfterInvalidBump &&
                                             unchangedBumpPlayer.actionStatus == (int)SimActionState.None;
            bool invalidBumpNoSound = presenter.BumpSoundCount == invalidBumpSoundBaseline;
            bool invalidBumpTrajectoryUnchanged = invalidBumpCommitted && hasBallAfterInvalidBump &&
                                                  SameTrajectory(ballBeforeInvalidBump, ballAfterInvalidBump);
            Check("invalid Bump request in Reset produces no accepted action", invalidBumpActionRejected, ref pass, ref fail);
            Check("invalid Bump request in Reset produces no contact sound", invalidBumpNoSound, ref pass, ref fail);
            Check("invalid Bump request in Reset leaves authoritative trajectory unchanged", invalidBumpTrajectoryUnchanged, ref pass, ref fail);
            if (!invalidBumpTrajectoryUnchanged)
                Debug.LogError($"[P04_NATIVE_REJECTION_TRACE] invalidBump committed={invalidBumpCommitted}, action={ (hasPlayerAfterInvalidBump ? unchangedBumpPlayer.actionStatus : -1) }, trajectoryType={ballBeforeInvalidBump.trajectoryType}->{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajectoryType : -1)}, speed={ballBeforeInvalidBump.ballSpeed:F4}->{(hasBallAfterInvalidBump ? ballAfterInvalidBump.ballSpeed : 0f):F4}, traj=({ballBeforeInvalidBump.trajA:F4},{ballBeforeInvalidBump.trajB:F4},{ballBeforeInvalidBump.trajC:F4})->({(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajA : 0f):F4},{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajB : 0f):F4},{(hasBallAfterInvalidBump ? ballAfterInvalidBump.trajC : 0f):F4}), soundDelta={presenter.BumpSoundCount - invalidBumpSoundBaseline}");

            adapter.SetInjected(SpikeAction.Serve, true);
            bool serveCommitted = manager.StepOnce();
            adapter.ClearInjected();
            yield return null;
            bool served = serveCommitted && table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord servedBall) && servedBall.stateFrame == -1;
            Check("configured Serve input starts a native rally through Get_data", served, ref pass, ref fail);
            if (!served)
            {
                blocked++;
                Debug.LogWarning("[P04_TEST][BLOCKED] seeded Serve did not produce a live native ball; ground/trail runtime checks skipped.");
                yield break;
            }

            SimulationEntityRecord first = table.GetEntity(SimulationDataTable.ENTITY_BALL);
            int preLandingImpacts = presenter.GroundImpactEventCount;
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

                for (int i = 0; i < 12; i++)
                {
                    adapter.ClearInjected();
                    manager.StepOnce();
                    yield return null;
                }
                Check("persistent terminal output does not replay impact VFX/SFX", presenter.GroundImpactEventCount == preLandingImpacts + 1, ref pass, ref fail);

                // Native ApplyBallTrajectory counts the terminal frame down to Reset (0). Re-serve
                // through the configured pipeline and require a second real floor event in the same match.
                bool reachedReset = false;
                for (int i = 0; i < 70; i++)
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
                    if (table.MatchRecord.servingTeam == 0) adapter.SetInjected(SpikeAction.Serve, true);
                    bool secondServeTick = manager.StepOnce();
                    adapter.ClearInjected();
                    yield return null;
                    secondRallyStarted = secondServeTick && table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord secondServeBall) && secondServeBall.stateFrame == -1;
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

                for (int i = 0; i < 40; i++) yield return null;
                Check("trail geometry fades after the ball becomes terminal", presenter.Trail == null || presenter.Trail.positionCount == 0, ref pass, ref fail);
            }

            Check("feedback observation does not advance simulation independently", manager.LastTick.committed && table.CurrentTick == manager.LastNativeSnapshot.tick, ref pass, ref fail);

            // Use the same natural-rally fixtures as P03 for all configured strengths.
            SpikeAction[] spikeActions = { SpikeAction.SpikeLight, SpikeAction.SpikeMedium, SpikeAction.SpikeStrong };
            SimActionState[] spikeStates = { SimActionState.SpikeLight, SimActionState.SpikeMedium, SimActionState.SpikeStrong };
            uint[] spikeSeeds = { 71u, 72u, 73u };
            float[] acceptedTrailWidths = new float[spikeActions.Length];
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
                        if (dx > 7f) adapter.SetInjected(SpikeAction.Right, true);
                        else if (dx < -7f) adapter.SetInjected(SpikeAction.Left, true);
                        if (distance < 140f && toggle) adapter.SetInjected(requestedSpike, true);
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
            Debug.Log($"[P04_TRAIL_SPEED_TRACE] light={acceptedTrailWidths[0]:F4} medium={acceptedTrailWidths[1]:F4} strong={acceptedTrailWidths[2]:F4}");

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
        }

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
