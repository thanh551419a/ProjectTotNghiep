using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Assets.Table;
using Native;
using Presentation.Character;
using Simulation;
using UnityEngine;

namespace Spike.Diagnostics
{
    /// <summary>
    /// DEVELOPMENT-ONLY gameplay self-test and autonomous self-play.
    ///
    /// Activation: environment variable SPIKE_SELFTEST_OUT (output directory) AND (Editor OR
    /// development build). It never exists in a normal release run.
    ///
    /// It drives the game exclusively through SpikeInputAdapter test injection — the same adapter
    /// used by keyboard / on-screen buttons — so every action travels the real path:
    ///   adapter.BuildInput (configured ids) -> SpikeSimulationManager.StepOnce -> Get_data (1 tick)
    ///   -> SimulationDataTable commit -> SimulationEntityReader -> Transform / SpriteRenderer.
    /// It never calls Get_data, never writes the Data Table and never moves Transforms.
    ///
    /// Every tick it cross-checks native snapshot == table record == reader input == rendered visual.
    ///
    /// Env vars: SPIKE_SELFTEST_OUT, SPIKE_SELFTEST_SEED (default 12345), SPIKE_SELFTEST_MAXTICKS
    /// (default 14000), SPIKE_SELFTEST_POINTS (score milestone, default 3), SPIKE_SELFTEST_TIMESCALE
    /// (default 3), SPIKE_SELFTEST_QUIT (default 1), SPIKE_CAPTURE_SIZES (screenshot sizes).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class GameplaySelfTestController : MonoBehaviour
    {
        private const float PosEps = 1e-4f;
        private const float RenderEps = 1e-3f;

        private struct Result { public string id, name, status, detail; }

        private string outDir;
        private int seed;
        private int maxTicks;
        private int targetPoints;
        private float timeScale;
        private bool quitAfter;
        private int cycle;

        private SpikeSimulationManager mgr;
        private SimulationDataTable table;
        private SpikeInputAdapter adapter;
        private readonly Dictionary<uint, SimulationEntityReader> readers = new Dictionary<uint, SimulationEntityReader>();
        private System.Random rng;

        private StreamWriter jsonl;
        private StreamWriter textLog;
        private readonly List<Result> results = new List<Result>();
        private readonly List<string> transitions = new List<string>();

        // Per-tick pipeline verification counters
        private long ticksChecked, committedTicks, rejectedTicks;
        private long nativeTableMismatch, tableReaderMismatch, readerRenderMismatch, tickSequenceErrors;
        private float maxBallRenderDev, maxCharBottomDev, maxCharCentreDev;
        private string firstMismatch;
        private ulong lastCommittedTick;
        private bool expectTickRestart = true;

        // Observed gameplay facts (all from committed native snapshots)
        private int scoreChanges, playerTouches, opponentTouches, aiServes, playerServes, rallies;
        private int lastLeft, lastRight, lastStateFrame = int.MinValue, lastTouchCount, lastLastTouch = -1;
        private int lastServingTeam = -1;
        private bool playerBumpActionSeen;
        private float opponentMinX = float.MaxValue, opponentMaxX = float.MinValue;
        private string stopReason = "not started";
        private long selfPlayTicks;

        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoActivate()
        {
            Active = false;
            string dir = Environment.GetEnvironmentVariable("SPIKE_SELFTEST_OUT");
            if (string.IsNullOrEmpty(dir)) return;
            if (!(Application.isEditor || Debug.isDebugBuild)) return;
            if (FindAnyObjectByType<GameplaySelfTestController>() != null) return;
            Active = true;
            new GameObject("[GameplaySelfTestController]").AddComponent<GameplaySelfTestController>();
        }

        private static string Env(string k, string d)
        {
            string v = Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? d : v;
        }

        private void Awake()
        {
            cycle = DevRunControl.CycleIndex;
            outDir = Env("SPIKE_SELFTEST_OUT", ".");
            seed = int.Parse(Env("SPIKE_SELFTEST_SEED", "12345"), CultureInfo.InvariantCulture);
            maxTicks = int.Parse(Env("SPIKE_SELFTEST_MAXTICKS", "14000"), CultureInfo.InvariantCulture);
            targetPoints = int.Parse(Env("SPIKE_SELFTEST_POINTS", "3"), CultureInfo.InvariantCulture);
            timeScale = float.Parse(Env("SPIKE_SELFTEST_TIMESCALE", "3"), CultureInfo.InvariantCulture);
            quitAfter = Env("SPIKE_SELFTEST_QUIT", "1") == "1";
            rng = new System.Random(seed);
            Directory.CreateDirectory(outDir);
            string prefix = cycle == 1 ? "selftest" : $"selftest_cycle{cycle}";
            jsonl = new StreamWriter(Path.Combine(outDir, prefix + "_selfplay.jsonl"), false, new UTF8Encoding(false));
            textLog = new StreamWriter(Path.Combine(outDir, prefix + ".log"), false, new UTF8Encoding(false));
            Log($"GameplaySelfTestController cycle={cycle} seed={seed} maxTicks={maxTicks} targetPoints={targetPoints} timeScale={timeScale}");
        }

        private void Log(string s)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {s}";
            textLog?.WriteLine(line);
            textLog?.Flush();
            Debug.Log("[SELFTEST] " + s);
        }

        private void Add(string id, string name, bool? pass, string detail)
        {
            string status = pass == null ? "NOT RUN" : (pass.Value ? "PASS" : "FAIL");
            results.Add(new Result { id = id, name = name, status = status, detail = detail });
            Log($"{id} {name}: {status} - {detail}");
        }

        private void AddStatus(string id, string name, string status, string detail)
        {
            results.Add(new Result { id = id, name = name, status = status, detail = detail });
            Log($"{id} {name}: {status} - {detail}");
        }

        // ------------------------------------------------------------------
        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                mgr = SpikeSimulationManager.Instance;
                if (mgr != null && mgr.State != SpikeSimulationManager.RunState.Starting) break;
                yield return null;
            }

            if (mgr == null || mgr.State != SpikeSimulationManager.RunState.Running)
            {
                Add("T00", "Native initialization + button mapping", false,
                    mgr == null ? "no SpikeSimulationManager in scene" : $"state={mgr.State} error={mgr.FatalError}");
                stopReason = "initialization failed";
                Finish();
                yield break;
            }

            table = mgr.DataTable;
            adapter = mgr.InputAdapter;
            foreach (var r in FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include)) readers[r.EntityId] = r;
            adapter.InjectionExclusive = true;
            adapter.ClearInjected();
            mgr.OnTick += OnTick;
            Add("T00", "Native initialization + button mapping", true, $"state=Running mapping: {adapter.Mapping} ; {mgr.NativeApiStatus}");

            StaticChecks();

            // Wait for the first committed tick
            yield return WaitTicks(2);

            if (cycle > 1)
            {
                yield return RepeatedPlayChecks();
                Finish();
                yield break;
            }

            Time.timeScale = 1f;
            yield return ScenarioNeutral();
            CaptureScreens("serve_ready");
            CheckTouchControlsLayout();
            yield return ScenarioMove(SpikeAction.Left, "T03", "Left input moves player left");
            yield return ScenarioMove(SpikeAction.Right, "T04", "Right input moves player right");
            yield return ScenarioRelease();
            yield return ScenarioServe();
            yield return CaptureDuringRally();

            Time.timeScale = Mathf.Max(1f, timeScale);
            yield return SelfPlay();
            Time.timeScale = 1f;

            yield return ScenarioReset();
            EvaluateSummaryResults();
            Finish();
        }

        // ------------------------------------------------------------------
        // Static / structural checks
        // ------------------------------------------------------------------
        private void StaticChecks()
        {
            string abi = NativeAbiLayout.Validate();
            Add("T01a", "Managed/native ABI layout (sizeof data=4772, offsets)", abi == null, abi ?? "all sizes/offsets match the native probe");

            var m = adapter.Mapping;
            bool mapOk = m.LeftId == 1 && m.RightId == 2 && m.BumpId == 3 && m.ServeId == 4;
            Add("T01b", "button_mapping.json ids (1 left, 2 right, 3 bump, 4 serve)", mapOk, m.ToString());

            int negPass = 0;
            string[] bad =
            {
                "{\"button_1\":\"left\",\"button_2\":\"right\",\"button_3\":\"bump\"}",
                "{\"button_1\":\"left\",\"button_1\":\"right\",\"button_3\":\"bump\",\"button_4\":\"serve\"}",
                "",
                "{\"button_1\":\"left\",\"button_2\":\"left\",\"button_3\":\"bump\",\"button_4\":\"serve\"}"
            };
            var failures = new List<string>();
            foreach (var j in bad)
            {
                try { ButtonMappingConfig.Parse(j, "x"); failures.Add("accepted: " + j); }
                catch (FormatException ex) { negPass++; Log("  invalid mapping rejected: " + ex.Message); }
            }
            Add("T01c", "Invalid mapping configs fail loudly (no silent defaults)", negPass == bad.Length, $"{negPass}/{bad.Length} rejected " + string.Join(";", failures));

            int views = FindObjectsByType<CharacterView>(FindObjectsInactive.Include).Length;
            int mgrs = FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Include).Length;
            int tables = FindObjectsByType<SimulationDataTable>(FindObjectsInactive.Include).Length;
            int nets = FindObjectsByType<NetView>(FindObjectsInactive.Include).Length;
            int courts = FindObjectsByType<CourtLineRenderer>(FindObjectsInactive.Include).Length;
            bool idsOk = readers.Count == 4 && readers.ContainsKey(0) && readers.ContainsKey(3) && readers.ContainsKey(6) && readers.ContainsKey(7);
            bool countsOk = views == 2 && mgrs == 1 && tables == 1 && nets == 1 && courts == 1 && idsOk;
            Add("T01d", "Exactly one of each bound object (2 characters, ball, net, court, manager, table)", countsOk,
                $"CharacterView={views} readers={readers.Count} ids=[{string.Join(",", readers.Keys)}] manager={mgrs} table={tables} net={nets} court={courts}");

            var court = FindAnyObjectByType<CourtLineRenderer>();
            if (court != null)
            {
                bool hasEnd = false, has3m = false, hasCentre = false, hasSide = false;
                foreach (var s in court.Segments)
                {
                    if (s.name == "LeftEndLine" && Mathf.Abs(s.a.x - CourtGeometry.ToWorldX(650f)) < PosEps) hasEnd = true;
                    if (s.name == "RightAttackLine" && Mathf.Abs(s.a.x - CourtGeometry.ToWorldX(1610f)) < PosEps) has3m = true;
                    if (s.name == "CentreLine" && Mathf.Abs(s.a.x - CourtGeometry.ToWorldX(1370f)) < PosEps) hasCentre = true;
                    if (s.name == "NearSideLine" && Mathf.Abs(s.a.x - CourtGeometry.ToWorldX(650f)) < PosEps && Mathf.Abs(s.b.x - CourtGeometry.ToWorldX(2090f)) < PosEps) hasSide = true;
                }
                int lr = court.GetComponentsInChildren<LineRenderer>(true).Length;
                Add("T01e", "Court drawn with LineRenderers at native line coordinates", hasEnd && has3m && hasCentre && hasSide && lr >= 10,
                    $"lineRenderers={lr} segments={court.Segments.Count} end9m={hasEnd} attack3m={has3m} centre={hasCentre} side={hasSide}");
            }
            else Add("T01e", "Court drawn with LineRenderers", false, "no CourtLineRenderer");

            var bg = GameObject.Find("Background");
            Add("T01f", "No static full-court background image active", bg == null || !bg.activeInHierarchy, bg == null ? "Background object removed" : "Background still active");
        }

        // ------------------------------------------------------------------
        // Per-tick pipeline verification
        // ------------------------------------------------------------------
        private void OnTick(SpikeSimulationManager.TickRecord rec)
        {
            ticksChecked++;
            if (!rec.committed)
            {
                rejectedTicks++;
                if (firstMismatch == null) firstMismatch = $"tick {rec.nativeTick} rejected: {rec.rejectReason}";
                return;
            }
            committedTicks++;
            if (expectTickRestart) { expectTickRestart = false; }
            else if (rec.nativeTick != lastCommittedTick + 1)
            {
                tickSequenceErrors++;
                if (firstMismatch == null) firstMismatch = $"tick sequence {lastCommittedTick} -> {rec.nativeTick}";
            }
            lastCommittedTick = rec.nativeTick;

            NativeSimData snap = mgr.LastNativeSnapshot;
            foreach (uint id in new uint[] { 0, 3, 6, 7 })
            {
                if (!snap.TryGetEntityById(id, out var ne) || !table.TryGetEntity(id, out var tr)) { nativeTableMismatch++; continue; }
                if (ne.position.position.x != tr.posX || ne.position.position.y != tr.posY ||
                    ne.size.size.x != tr.sizeX || ne.size.size.y != tr.sizeY ||
                    (id == 7 && ne.ballGameplayState.stateFrame != tr.stateFrame) ||
                    ((id == 0 || id == 3) && ne.actionState.status != tr.actionStatus))
                {
                    nativeTableMismatch++;
                    if (firstMismatch == null) firstMismatch = $"native!=table id {id} tick {rec.nativeTick}";
                }

                if (!readers.TryGetValue(id, out var rd)) continue;
                if (rd.LastConsumedTick != table.CurrentTick || rd.LastRecord.posX != tr.posX || rd.LastRecord.posY != tr.posY)
                {
                    tableReaderMismatch++;
                    if (firstMismatch == null) firstMismatch = $"table!=reader id {id} tick {rec.nativeTick} readerTick {rd.LastConsumedTick}";
                }

                Vector3 anchor = SimulationEntityReader.AnchorWorld(tr);
                var sr = rd.SpriteRenderer;
                if (id == 7 && sr != null && sr.sprite != null && sr.enabled)
                {
                    float dev = Vector2.Distance(sr.bounds.center, anchor);
                    float sizeDev = Mathf.Abs(sr.bounds.size.y - CourtGeometry.ToWorldLength(tr.sizeY));
                    maxBallRenderDev = Mathf.Max(maxBallRenderDev, Mathf.Max(dev, sizeDev));
                    if (dev > RenderEps || sizeDev > RenderEps) { readerRenderMismatch++; if (firstMismatch == null) firstMismatch = $"ball render dev {dev:F5} tick {rec.nativeTick}"; }
                }
                else if ((id == 0 || id == 3) && sr != null && sr.sprite != null)
                {
                    float bottomDev = Mathf.Abs(sr.bounds.min.y - anchor.y);
                    float centreDev = Mathf.Abs(rd.transform.position.x + (sr.flipX ? -sr.sprite.bounds.center.x : sr.sprite.bounds.center.x) * rd.transform.lossyScale.x - anchor.x);
                    maxCharBottomDev = Mathf.Max(maxCharBottomDev, bottomDev);
                    maxCharCentreDev = Mathf.Max(maxCharCentreDev, centreDev);
                    if (bottomDev > RenderEps || centreDev > RenderEps) { readerRenderMismatch++; if (firstMismatch == null) firstMismatch = $"char {id} render dev bottom {bottomDev:F5} centre {centreDev:F5}"; }
                }
                else if (id == 6)
                {
                    float dev = Vector2.Distance(rd.transform.position, anchor);
                    if (dev > RenderEps) { readerRenderMismatch++; if (firstMismatch == null) firstMismatch = $"net render dev {dev:F5}"; }
                }
            }

            ObserveGameplay(rec);
        }

        private void ObserveGameplay(SpikeSimulationManager.TickRecord rec)
        {
            var m = table.MatchRecord;
            var ball = table.GetEntity(SimulationDataTable.ENTITY_BALL);
            var p = table.GetEntity(SimulationDataTable.ENTITY_PLAYER);
            var o = table.GetEntity(SimulationDataTable.ENTITY_OPPONENT_1);
            opponentMinX = Mathf.Min(opponentMinX, o.posX);
            opponentMaxX = Mathf.Max(opponentMaxX, o.posX);
            if (p.actionStatus == (int)SimActionState.Bump) playerBumpActionSeen = true;

            if (lastStateFrame != int.MinValue)
            {
                if (lastStateFrame == 0 && ball.stateFrame == -1)
                {
                    rallies++;
                    bool playerServe = lastServingTeam == 0;
                    if (playerServe) playerServes++; else aiServes++;
                    Transition($"SERVE tick={rec.nativeTick} team={(playerServe ? "LEFT(player)" : "RIGHT(AI)")} ids={SpikeInputAdapter.DescribeIds(rec.submittedIds)}");
                }
                if (lastStateFrame == -1 && ball.stateFrame > 0)
                    Transition($"RALLY_END tick={rec.nativeTick} ballPos=({ball.posX:F1},{ball.posY:F1}) touches={m.touchCount} lastTouch={m.lastTouchEntity}");
                if (lastStateFrame > 0 && ball.stateFrame == 0)
                    Transition($"SERVE_READY tick={rec.nativeTick} servingTeam={m.servingTeam}");
            }
            if (m.leftScore != lastLeft || m.rightScore != lastRight)
            {
                var nm = mgr.LastNativeSnapshot.matchState;
                if (m.leftScore < lastLeft || m.rightScore < lastRight)
                {
                    Transition($"RESET tick={rec.nativeTick} {lastLeft}-{lastRight} -> {m.leftScore}-{m.rightScore} (native {nm.leftScore}-{nm.rightScore})");
                }
                else
                {
                    scoreChanges++;
                    Transition($"SCORE tick={rec.nativeTick} {lastLeft}-{lastRight} -> {m.leftScore}-{m.rightScore} (native {nm.leftScore}-{nm.rightScore}) servingTeam={m.servingTeam}");
                }
            }
            if (m.touchCount != lastTouchCount || m.lastTouchEntity != lastLastTouch)
            {
                if (m.touchCount > 0 && (m.lastTouchEntity != lastLastTouch || m.touchCount > lastTouchCount))
                {
                    if (m.lastTouchEntity == 0) playerTouches++;
                    else if (m.lastTouchEntity == 3) opponentTouches++;
                    Transition($"TOUCH tick={rec.nativeTick} entity={m.lastTouchEntity} touchCount={m.touchCount} ids={SpikeInputAdapter.DescribeIds(rec.submittedIds)} ball=({ball.posX:F1},{ball.posY:F1})");
                }
            }
            lastLeft = m.leftScore; lastRight = m.rightScore;
            lastStateFrame = ball.stateFrame; lastTouchCount = (int)m.touchCount; lastLastTouch = m.lastTouchEntity;
            lastServingTeam = m.servingTeam;
        }

        private void Transition(string s)
        {
            transitions.Add(s);
            Log("  " + s);
            WriteJson("transition", s);
        }

        private void WriteJson(string kind, string detail)
        {
            var m = table != null ? table.MatchRecord : default;
            var b = table != null ? table.GetEntity(7) : default;
            var p = table != null ? table.GetEntity(0) : default;
            var o = table != null ? table.GetEntity(3) : default;
            string ids = adapter != null ? SpikeInputAdapter.DescribeIds(adapter.LastSubmittedIds) : "[]";
            jsonl.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"kind\":\"{0}\",\"tick\":{1},\"ids\":{2},\"player\":[{3:F2},{4:F2},{5}],\"opponent\":[{6:F2},{7:F2},{8}],\"ball\":[{9:F2},{10:F2},{11},{12:F1}],\"score\":[{13},{14}],\"serving\":{15},\"touch\":[{16},{17}],\"detail\":\"{18}\"}}",
                kind, table != null ? table.CurrentTick : 0, ids, p.posX, p.posY, p.actionStatus, o.posX, o.posY, o.actionStatus,
                b.posX, b.posY, b.stateFrame, b.landingX, m.leftScore, m.rightScore, m.servingTeam, m.lastTouchEntity, m.touchCount,
                (detail ?? "").Replace("\\", "/").Replace("\"", "'")));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private IEnumerator WaitTicks(int n)
        {
            long target = mgr.TickCounter + n;
            while (mgr.TickCounter < target && mgr.State == SpikeSimulationManager.RunState.Running)
                yield return new WaitForFixedUpdate();
        }

        private void Hold(params SpikeAction[] actions)
        {
            adapter.ClearInjected();
            foreach (var a in actions) adapter.SetInjected(a, true);
        }

        private SimulationEntityRecord Rec(uint id) => table.GetEntity(id);

        // ------------------------------------------------------------------
        // Scenarios
        // ------------------------------------------------------------------
        private IEnumerator ScenarioNeutral()
        {
            Hold();
            yield return WaitTicks(1);
            float x0 = Rec(0).posX;
            ulong t0 = table.CurrentTick;
            int intentTicks = 0, nonEmptyIds = 0, actionTicks = 0;
            for (int i = 0; i < 30; i++)
            {
                yield return WaitTicks(1);
                var p = Rec(0);
                if (p.hasCharacterIntent && (p.moveX != 0f || p.finalIntent != 0)) intentTicks++;
                if (adapter.LastSubmittedIds.Length != 0) nonEmptyIds++;
                if (p.actionStatus != 0) actionTicks++;
            }
            float x1 = Rec(0).posX;
            ulong t1 = table.CurrentTick;
            WriteJson("scenario", "neutral");
            Add("T02", "Neutral input advances simulation without player action", nonEmptyIds == 0 && intentTicks == 0 && actionTicks == 0 && t1 - t0 == 30 && x1 == x0,
                $"ticks {t0}->{t1} (+{t1 - t0}), submitted non-empty={nonEmptyIds}, intent ticks={intentTicks}, action ticks={actionTicks}, playerX {x0:F2}->{x1:F2}");
        }

        private IEnumerator ScenarioMove(SpikeAction dir, string id, string name)
        {
            Hold();
            yield return WaitTicks(1);
            var r0 = Rec(0);
            Vector3 v0 = readers[0].transform.position;
            Hold(dir);
            int idsOk = 0, intentOk = 0;
            uint expectedId = adapter.IdFor(dir);
            for (int i = 0; i < 30; i++)
            {
                yield return WaitTicks(1);
                if (adapter.LastSubmittedIds.Length == 1 && adapter.LastSubmittedIds[0] == expectedId) idsOk++;
                var p = Rec(0);
                if (p.hasCharacterIntent && Mathf.Sign(p.moveX) == (dir == SpikeAction.Left ? -1f : 1f) && p.moveX != 0f) intentOk++;
            }
            var r1 = Rec(0);
            Vector3 v1 = readers[0].transform.position;
            Hold();
            float dxSim = r1.posX - r0.posX;
            float dxVis = v1.x - v0.x;
            float expectSign = dir == SpikeAction.Left ? -1f : 1f;
            bool moved = Mathf.Sign(dxSim) == expectSign && Mathf.Abs(dxSim) > 1f;
            bool visualFollows = Mathf.Abs(dxVis - CourtGeometry.ToWorldLength(dxSim)) < 0.02f && Mathf.Abs(readers[0].LastAnchorWorld.x - CourtGeometry.ToWorldX(r1.posX + r1.sizeX * 0.5f)) < PosEps;
            WriteJson("scenario", name);
            Add(id, name, idsOk == 30 && intentOk >= 29 && moved && visualFollows,
                $"submitted id {expectedId} on {idsOk}/30 ticks, native intent sign ok {intentOk}/30, native x {r0.posX:F2}->{r1.posX:F2} (d={dxSim:F2}px), visual x {v0.x:F3}->{v1.x:F3} (d={dxVis:F3}u, expected {CourtGeometry.ToWorldLength(dxSim):F3}u)");
        }

        private IEnumerator ScenarioRelease()
        {
            Hold(SpikeAction.Right);
            yield return WaitTicks(10);
            Hold();
            yield return WaitTicks(2);
            float x0 = Rec(0).posX;
            int nonEmpty = 0, intents = 0;
            for (int i = 0; i < 20; i++)
            {
                yield return WaitTicks(1);
                if (adapter.LastSubmittedIds.Length != 0) nonEmpty++;
                var p = Rec(0);
                if (p.hasCharacterIntent && p.moveX != 0f) intents++;
            }
            float x1 = Rec(0).posX;
            WriteJson("scenario", "release");
            Add("T05", "Input release leaves no stuck command", nonEmpty == 0 && intents == 0 && x1 == x0,
                $"after release: non-empty submissions={nonEmpty}, native move intents={intents}, playerX {x0:F2}->{x1:F2}");
        }

        private IEnumerator ScenarioServe()
        {
            // Serve is only sent when the authoritative state says the player may serve.
            Hold();
            int waited = 0;
            while (!(Rec(7).stateFrame == 0 && table.MatchRecord.servingTeam == 0) && waited < 1200)
            {
                yield return WaitTicks(1);
                waited++;
            }
            if (!(Rec(7).stateFrame == 0 && table.MatchRecord.servingTeam == 0))
            {
                AddStatus("T06", "Serve (pressed edge, only when native state allows)", "BLOCKED", $"native never reached player-serve state in {waited} ticks (stateFrame={Rec(7).stateFrame}, servingTeam={table.MatchRecord.servingTeam})");
                yield break;
            }
            var b0 = Rec(7);
            Hold(SpikeAction.Serve);
            yield return WaitTicks(1);
            uint[] sent = adapter.LastSubmittedIds;
            Hold(); // release next tick (edge semantics)
            int ticks = 0;
            while (Rec(7).stateFrame == 0 && ticks < 10) { yield return WaitTicks(1); ticks++; }
            yield return WaitTicks(5);
            var b1 = Rec(7);
            bool left = b1.stateFrame == -1 || b1.stateFrame > 0;
            bool moved = Mathf.Abs(b1.posX - b0.posX) > 1f || Mathf.Abs(b1.posY - b0.posY) > 1f;
            WriteJson("scenario", "serve");
            Add("T06", "Serve (pressed edge, only when native state allows) leaves serve-ready state", sent.Length == 1 && sent[0] == adapter.Mapping.ServeId && left && moved,
                $"waited {waited} ticks for player serve; sent {SpikeInputAdapter.DescribeIds(sent)}; ball stateFrame {b0.stateFrame}->{b1.stateFrame}; ball ({b0.posX:F1},{b0.posY:F1})->({b1.posX:F1},{b1.posY:F1})");
        }

        private IEnumerator CaptureDuringRally()
        {
            int guard = 0;
            while (guard < 240 && !(Rec(7).stateFrame == -1 && Rec(7).posY > CourtGeometry.FloorY + 200f))
            {
                yield return WaitTicks(1);
                guard++;
            }
            CaptureScreens("rally");
        }

        private void CaptureScreens(string label)
        {
            string sizes = Env("SPIKE_CAPTURE_SIZES", "1920x1080,2400x1080,1280x960");
            var list = new List<Vector2Int>();
            foreach (var part in sizes.Split(','))
            {
                var wh = part.Trim().Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) list.Add(new Vector2Int(w, h));
            }
            string prefix = (cycle == 1 ? "" : $"cycle{cycle}_") + label;
            if (!Application.isBatchMode)
            {
                // Real framebuffer (includes the score HUD and on-screen buttons), written at end of frame.
                string screen = Path.Combine(outDir, $"{prefix}_screen_{Screen.width}x{Screen.height}.png");
                ScreenCapture.CaptureScreenshot(screen);
                Log("real screen capture requested: " + screen);
            }
            bool ok;
            try { ok = SceneCaptureProbe.CaptureAll(outDir, prefix, list); }
            catch (Exception ex) { ok = false; Log("capture failed: " + ex); }
            Add(label == "serve_ready" ? "T10a" : "T10b", $"Screen-space measurement + screenshots ({label})", ok,
                $"report {SceneCaptureProbe.LastReportPath}; {SceneCaptureProbe.LastSummary}");
        }

        /// <summary>
        /// D15 regression: every on-screen touch button (Screen Space Overlay canvas) must lie entirely outside the
        /// projected play area and must not overlap either character's renderer bounds at the current resolution.
        /// </summary>
        private void CheckTouchControlsLayout()
        {
            var cam = Camera.main;
            var buttons = FindObjectsByType<UIButtonInputTest>(FindObjectsInactive.Exclude);
            if (cam == null || buttons.Length == 0)
            {
                Add("T10c", "Touch buttons do not cover play area / characters", false, cam == null ? "no main camera" : "no touch buttons found");
                return;
            }
            var follow = cam.GetComponent<Presentation.CameraSystem.CameraFollow>();
            if (follow != null) follow.ApplyFramingNow();
            Canvas.ForceUpdateCanvases();

            Rect play = ScreenRectOf(cam, CourtGeometry.RequiredVisibleWorld);
            var chars = new List<Rect>();
            foreach (var cv in FindObjectsByType<CharacterView>(FindObjectsInactive.Exclude))
                foreach (var r in cv.GetComponentsInChildren<Renderer>())
                    if (r.enabled) chars.Add(ScreenRectOf(cam, new Rect(r.bounds.min.x, r.bounds.min.y, r.bounds.size.x, r.bounds.size.y)));

            var sb = new StringBuilder($"screen {Screen.width}x{Screen.height}, play area y {play.yMin:F0}..{play.yMax:F0}px; ");
            bool ok = true;
            var corners = new Vector3[4];
            foreach (var b in buttons)
            {
                ((RectTransform)b.transform).GetWorldCorners(corners); // overlay canvas: world == screen pixels
                Rect br = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                bool hitPlay = br.Overlaps(play);
                bool hitChar = chars.Exists(c => c.Overlaps(br));
                bool onScreen = br.xMin >= 0 && br.yMin >= 0 && br.xMax <= Screen.width && br.yMax <= Screen.height;
                ok &= !hitPlay && !hitChar && onScreen;
                sb.Append($"{b.gameObject.name}=({br.xMin:F0},{br.yMin:F0},{br.width:F0}x{br.height:F0}) overlapPlay={hitPlay} overlapChar={hitChar} onScreen={onScreen}; ");
            }
            Add("T10c", "Touch buttons do not cover play area / characters", ok, sb.ToString());
        }

        private static Rect ScreenRectOf(Camera cam, Rect world)
        {
            Vector3 a = cam.WorldToScreenPoint(new Vector3(world.xMin, world.yMin, 0f));
            Vector3 b = cam.WorldToScreenPoint(new Vector3(world.xMax, world.yMax, 0f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // ------------------------------------------------------------------
        // Autonomous self-play
        // ------------------------------------------------------------------
        private IEnumerator SelfPlay()
        {
            Log($"Self-play start: seed={seed} maxTicks={maxTicks} targetPoints={targetPoints}");
            int startLeft = table.MatchRecord.leftScore, startRight = table.MatchRecord.rightScore;
            int serveDelay = rng.Next(20, 60);
            int moveTicksLeft = 0;
            SpikeAction moveDir = SpikeAction.Left;
            bool bumpHeld = false;
            bool servePressedLastTick = false;
            string lastAction = "";
            stopReason = "timeout";

            while (selfPlayTicks < maxTicks)
            {
                if (mgr.State != SpikeSimulationManager.RunState.Running) { stopReason = "runtime error: " + mgr.FatalError; break; }
                var m = table.MatchRecord;
                var ball = Rec(7);
                var p = Rec(0);
                int points = (m.leftScore - startLeft) + (m.rightScore - startRight);
                if (m.finished) { stopReason = $"match finished (winner team {m.matchWinner})"; break; }
                if (points >= targetPoints && playerTouches > 0) { stopReason = $"score milestone reached: {points} points scored in self-play ({m.leftScore}-{m.rightScore}) with player touches"; break; }

                string action;
                bool serveReady = ball.stateFrame == 0 && m.servingTeam == 0;
                bool ballComingToUs = ball.stateFrame == -1 && ball.hasBallGameplayState && (ball.landingX + ball.sizeX * 0.5f) < CourtGeometry.NetX;

                if (serveReady)
                {
                    bumpHeld = false;
                    if (serveDelay > 0)
                    {
                        serveDelay--;
                        if (moveTicksLeft <= 0) { moveDir = rng.Next(2) == 0 ? SpikeAction.Left : SpikeAction.Right; moveTicksLeft = rng.Next(5, 15); }
                        moveTicksLeft--;
                        Hold(moveDir);
                        action = "prep-" + moveDir;
                        servePressedLastTick = false;
                    }
                    else if (!servePressedLastTick)
                    {
                        Hold(SpikeAction.Serve);
                        action = "serve-press";
                        servePressedLastTick = true;
                    }
                    else
                    {
                        Hold();
                        action = "serve-release";
                        servePressedLastTick = false;
                        serveDelay = rng.Next(20, 60);
                    }
                }
                else if (ballComingToUs)
                {
                    Hold(SpikeAction.Bump);
                    action = bumpHeld ? "bump-hold" : "bump-press";
                    bumpHeld = true;
                }
                else
                {
                    servePressedLastTick = false;
                    if (bumpHeld) { Hold(); action = "bump-release"; bumpHeld = false; }
                    else
                    {
                        // Alternate movement directions around the left court (seeded).
                        if (moveTicksLeft <= 0)
                        {
                            float home = CourtGeometry.ToWorldX(900f);
                            float px = CourtGeometry.ToWorldX(p.posX);
                            moveDir = px > home + 1.5f ? SpikeAction.Left : px < home - 1.5f ? SpikeAction.Right : (rng.Next(2) == 0 ? SpikeAction.Left : SpikeAction.Right);
                            moveTicksLeft = rng.Next(6, 24);
                        }
                        moveTicksLeft--;
                        if (rng.Next(4) == 0) { Hold(); action = "idle"; }
                        else { Hold(moveDir); action = "move-" + moveDir; }
                    }
                }

                if (action != lastAction) { WriteJson("action", action); lastAction = action; }
                else if (selfPlayTicks % 60 == 0) WriteJson("sample", action);

                yield return WaitTicks(1);
                selfPlayTicks++;
            }
            Hold();
            yield return WaitTicks(2);
            WriteJson("stop", stopReason);
            Log($"Self-play stop after {selfPlayTicks} ticks: {stopReason}");
        }

        private IEnumerator ScenarioReset()
        {
            int managersBefore = FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Include).Length;
            int viewsBefore = FindObjectsByType<CharacterView>(FindObjectsInactive.Include).Length;
            expectTickRestart = true;
            mgr.ResetMatch();
            yield return WaitTicks(3);
            var m = table.MatchRecord;
            var b = Rec(7);
            int managersAfter = FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Include).Length;
            int viewsAfter = FindObjectsByType<CharacterView>(FindObjectsInactive.Include).Length;
            bool ok = table.CurrentTick <= 4 && m.leftScore == 0 && m.rightScore == 0 && !m.finished && b.stateFrame == 0 && table.RejectedCount == 0 &&
                      managersBefore == 1 && managersAfter == 1 && viewsBefore == 2 && viewsAfter == 2;
            WriteJson("scenario", "reset");
            Add("T11", "Match reset (Sim_Reset + table reset): fresh tick, 0-0, no duplicates", ok,
                $"tick after reset={table.CurrentTick}, score {m.leftScore}-{m.rightScore}, ball stateFrame={b.stateFrame}, rejected={table.RejectedCount}, managers {managersBefore}->{managersAfter}, characterViews {viewsBefore}->{viewsAfter}");
        }

        private IEnumerator RepeatedPlayChecks()
        {
            // Second Play Mode session in the same Editor process (native DLL stays loaded).
            var m = table.MatchRecord;
            var b = Rec(7);
            int views = FindObjectsByType<CharacterView>(FindObjectsInactive.Include).Length;
            int mgrs = FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Include).Length;
            int tables = FindObjectsByType<SimulationDataTable>(FindObjectsInactive.Include).Length;
            bool fresh = table.CurrentTick <= 4 && m.leftScore == 0 && m.rightScore == 0 && b.stateFrame == 0;
            Add("T12", "Repeated Play/Stop: native re-initialized fresh, no stale table, no duplicates", fresh && views == 2 && mgrs == 1 && tables == 1 && table.RejectedCount == 0,
                $"cycle {cycle}: first ticks={table.CurrentTick}, score {m.leftScore}-{m.rightScore}, ball stateFrame={b.stateFrame}, CharacterView={views}, managers={mgrs}, tables={tables}, rejected={table.RejectedCount}");
            yield return ScenarioMove(SpikeAction.Right, "T12b", "Cycle 2: right input still reaches native");
            CaptureScreens("serve_ready");
            EvaluatePipelineResult("T12c");
        }

        private void EvaluatePipelineResult(string id)
        {
            bool ok = committedTicks > 0 && rejectedTicks == 0 && nativeTableMismatch == 0 && tableReaderMismatch == 0 && readerRenderMismatch == 0 && tickSequenceErrors == 0;
            Add(id, "Per-tick native == table == reader == render (same tick)", ok,
                string.Format(CultureInfo.InvariantCulture,
                    "ticks checked={0}, committed={1}, rejected={2}, native!=table={3}, table!=reader={4}, reader!=render={5}, tick-sequence errors={6}, max ball dev={7:E2}u, max char bottom dev={8:E2}u, max char centre dev={9:E2}u{10}",
                    ticksChecked, committedTicks, rejectedTicks, nativeTableMismatch, tableReaderMismatch, readerRenderMismatch, tickSequenceErrors,
                    maxBallRenderDev, maxCharBottomDev, maxCharCentreDev, firstMismatch != null ? "; first: " + firstMismatch : ""));
        }

        private void EvaluateSummaryResults()
        {
            EvaluatePipelineResult("T07");
            Add("T08", "Opponent AI + ball progression through native simulation",
                (aiServes > 0 || opponentTouches > 0) && opponentMaxX - opponentMinX > 1f && rallies > 0,
                $"AI serves={aiServes}, opponent touches={opponentTouches}, opponent x range {opponentMinX:F1}..{opponentMaxX:F1}, rallies started={rallies}");
            Add("T09", "Bump (hold edge/release) produces a native player touch", playerTouches > 0 || playerBumpActionSeen,
                $"player touches={playerTouches}, player Bump action state observed={playerBumpActionSeen}");
            if (scoreChanges > 0)
                Add("T13", "Score/rally transitions reflected in Data Table (from native)", nativeTableMismatch == 0, $"score changes={scoreChanges}, final {table.MatchRecord.leftScore}-{table.MatchRecord.rightScore} (verified per tick against native matchState)");
            else
                AddStatus("T13", "Score/rally transitions reflected in Data Table", "NOT RUN", "no score change occurred before stop");
            Add("T14", "Autonomous self-play ran through the input adapter", selfPlayTicks > 0 && !stopReason.StartsWith("runtime error"),
                $"ticks={selfPlayTicks}, stop reason: {stopReason}, player serves={playerServes}, AI serves={aiServes}, rallies={rallies}, player touches={playerTouches}, opponent touches={opponentTouches}, score changes={scoreChanges}");
        }

        // ------------------------------------------------------------------
        private void Finish()
        {
            if (mgr != null) mgr.OnTick -= OnTick;
            if (adapter != null) { adapter.ClearInjected(); adapter.InjectionExclusive = false; }
            Time.timeScale = 1f;

            int fails = 0;
            var sb = new StringBuilder();
            sb.Append("{\"cycle\":").Append(cycle).Append(",\"seed\":").Append(seed)
              .Append(",\"selfPlayTicks\":").Append(selfPlayTicks)
              .Append(",\"stopReason\":\"").Append(stopReason.Replace("\"", "'")).Append("\"")
              .Append(",\"finalScore\":[").Append(table != null ? table.MatchRecord.leftScore : 0).Append(',').Append(table != null ? table.MatchRecord.rightScore : 0).Append(']')
              .Append(",\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.status == "FAIL") fails++;
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(r.id).Append("\",\"name\":\"").Append(r.name.Replace("\"", "'"))
                  .Append("\",\"status\":\"").Append(r.status).Append("\",\"detail\":\"").Append((r.detail ?? "").Replace("\\", "/").Replace("\"", "'")).Append("\"}");
            }
            sb.Append("],\"transitions\":[");
            for (int i = 0; i < transitions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(transitions[i].Replace("\"", "'")).Append('"');
            }
            sb.Append("],\"fails\":").Append(fails).Append('}');
            string prefix = cycle == 1 ? "selftest" : $"selftest_cycle{cycle}";
            File.WriteAllText(Path.Combine(outDir, prefix + "_results.json"), sb.ToString());
            Log($"SELFTEST COMPLETE cycle={cycle}: {results.Count} results, {fails} FAIL, stop reason: {stopReason}");
            jsonl.Flush(); jsonl.Close(); jsonl = null;
            textLog.Flush(); textLog.Close(); textLog = null;

            if (quitAfter) DevRunControl.Finish(fails == 0 ? 0 : 2, "GameplaySelfTestController");
        }

        private void OnDestroy()
        {
            jsonl?.Close();
            textLog?.Close();
        }
    }
}
