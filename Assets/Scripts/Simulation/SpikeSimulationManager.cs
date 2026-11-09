using System;
using System.Collections;
using Assets.Table;
using Native;
using Native.Snapshot;
using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// Owns the native simulation lifecycle and the ONE data path:
    ///
    ///   SpikeInputAdapter.BuildInput()  (configured button IDs, button_mapping.json)
    ///     -> SpikeNativeBridge.Get_data(Input)      exactly one native tick per FixedUpdate (60 Hz)
    ///     -> SimulationDataTable.UpdateFromNativeSnapshot  strict validation + atomic commit
    ///     -> SimulationEntityReader / CharacterStateTable   (visuals read the table only)
    ///
    /// It binds to scene objects only and never spawns gameplay visuals. It never moves Transforms.
    /// Any configuration / ABI / native-load failure stops the simulation and is shown on screen;
    /// there is no fallback to fabricated state.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(SpikeInputAdapter))]
    public class SpikeSimulationManager : MonoBehaviour
    {
        public const float TickSeconds = 1f / 60f;

        public enum RunState { Starting, Running, Failed, Stopped }

        /// <summary>Per-tick trace used by HUD, diagnostics and the development self-test.</summary>
        public struct TickRecord
        {
            public long index;            // manager tick counter (Get_data calls)
            public int unityFrame;        // Time.frameCount when submitted
            public uint[] submittedIds;   // configured button ids sent to Get_data
            public int nativeStatus;
            public ulong nativeTick;
            public bool committed;
            public string rejectReason;
        }

        public static SpikeSimulationManager Instance { get; private set; }

        [SerializeField] private SimulationDataTable dataTable;
        [SerializeField] private CharacterStateTable stateTable;
        [SerializeField] private SpikeInputAdapter inputAdapter;
        [SerializeField] private bool showHud = true;
        [Tooltip("Maximum consecutive rejected snapshots before the simulation is stopped.")]
        [SerializeField] private int maxConsecutiveRejects = 30;

        private RunState state = RunState.Starting;
        private string fatalError;
        private bool nativeInitialized;
        private int consecutiveRejects;
        private long tickCounter;
        private NativeSimData lastNativeSnapshot;
        private TickRecord lastTick;

        // Optional API v2 JSON diagnostics (only fetched on demand, e.g. F1 overlay visible)
        private bool apiV2Available;
        private string nativeApiStatus = "not probed";
        private SimSnapshotSchema snapshotSchema;
        private SimSnapshotData decodedSnapshot;
        private int decodeFailureCount;
        private int validationIssueCount;
        private int tickMismatchCount;

        public event Action<TickRecord> OnTick;

        public RunState State => state;
        public string FatalError => fatalError;
        public bool IsInitialized => state == RunState.Running;
        public SimulationDataTable DataTable => dataTable;
        public CharacterStateTable StateTable => stateTable;
        public SpikeInputAdapter InputAdapter => inputAdapter;
        public TickRecord LastTick => lastTick;
        public NativeSimData LastNativeSnapshot => lastNativeSnapshot;
        public long TickCounter => tickCounter;
        /// <summary>When true FixedUpdate does not tick (used only by tests that step manually).</summary>
        public bool ManualTickMode { get; set; }
        public bool ShowHud { get => showHud; set => showHud = value; }

        // Diagnostics overlay API
        public bool ApiV2Available => apiV2Available;
        public string NativeApiStatus => nativeApiStatus;
        public SimSnapshotSchema SnapshotSchema => snapshotSchema;
        public SimSnapshotData DecodedSnapshot => decodedSnapshot;
        public int DecodeFailureCount => decodeFailureCount;
        public int ValidationIssueCount => validationIssueCount;
        public int CrossCheckMismatchCount => 0;
        public int TickMismatchCount => tickMismatchCount;
        public SimComparisonResult LastCrossCheck => null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null; // domain reload is disabled in this project (EnterPlayModeOptions)
            Application.runInBackground = true;
        }

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[SpikeSimulationManager] A second manager was found; it is disabled so only one native runtime exists.", this);
                enabled = false;
                return;
            }
            Instance = this;
            state = RunState.Starting;
            fatalError = null;

            // Native code advances 1/60 s per Get_data call; drive it at the same rate.
            Time.fixedDeltaTime = TickSeconds;

            if (inputAdapter == null) inputAdapter = GetComponent<SpikeInputAdapter>();
            if (dataTable == null) dataTable = GetComponent<SimulationDataTable>();
            if (dataTable == null) dataTable = FindAnyObjectByType<SimulationDataTable>();
            if (dataTable == null) dataTable = gameObject.AddComponent<SimulationDataTable>();
            if (SimulationDataTable.Instance == null) SimulationDataTable.SetInstance(dataTable);
            if (stateTable == null) stateTable = FindAnyObjectByType<CharacterStateTable>(FindObjectsInactive.Include);
            dataTable.ResetTable();

            string streamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, ButtonMappingConfig.FileName);
            if (!streamingPath.Contains("://") && System.IO.File.Exists(streamingPath))
            {
                try
                {
                    var cfg = ButtonMappingConfig.Parse(System.IO.File.ReadAllText(streamingPath), streamingPath);
                    CompleteNativeInitialization(cfg);
                }
                catch (Exception ex)
                {
                    Fail("Button mapping: " + ex.Message);
                }
            }
        }

        private IEnumerator Start()
        {
            if (Instance != this || state == RunState.Running || state == RunState.Failed) yield break;

            ButtonMappingConfig mapping = null;
            string mappingError = null;
            yield return ButtonMappingConfig.Load((cfg, err) => { mapping = cfg; mappingError = err; });
            if (mapping == null)
            {
                Fail("Button mapping: " + (mappingError ?? "unknown error"));
                yield break;
            }
            CompleteNativeInitialization(mapping);
        }

        private void CompleteNativeInitialization(ButtonMappingConfig mapping)
        {
            string abiProblem = NativeAbiLayout.Validate();
            if (abiProblem != null)
            {
                Fail("Managed/native ABI mismatch: " + abiProblem);
                return;
            }

            inputAdapter.Configure(mapping);
            Debug.Log("[SpikeSimulationManager] Button mapping loaded: " + mapping);

            try
            {
                string seedText = Environment.GetEnvironmentVariable("SPIKE_SIM_SEED");
                if (!string.IsNullOrEmpty(seedText) && uint.TryParse(seedText, out uint seed))
                {
                    SpikeNativeBridge.Sim_SeedRandom(seed);
                    Debug.Log($"[SpikeSimulationManager] Native RNG seeded with {seed}");
                }

                if (!SpikeNativeBridge.Sim_Initialize(mapping.NativePath))
                {
                    Fail($"Sim_Initialize returned false for config '{mapping.NativePath}'");
                    return;
                }
                nativeInitialized = true;
                apiV2Available = SpikeNativeJson.ProbeApiV2(out nativeApiStatus);
            }
            catch (DllNotFoundException ex)
            {
                Fail("Native library SpikeCrossCore not found: " + ex.Message);
                return;
            }
            catch (EntryPointNotFoundException ex)
            {
                Fail("Native library is missing an export: " + ex.Message);
                return;
            }

            dataTable.ResetTable();
            tickCounter = 0;
            consecutiveRejects = 0;
            state = RunState.Running;
            Debug.Log($"[SpikeSimulationManager] Native simulation running (fixedDeltaTime={Time.fixedDeltaTime:F5}, {nativeApiStatus}).");
        }

        private void FixedUpdate()
        {
            if (state != RunState.Running || ManualTickMode) return;
            StepOnce();
        }

        /// <summary>
        /// Runs exactly one native tick through the real pipeline. Returns true if the snapshot was committed.
        /// </summary>
        public bool StepOnce()
        {
            if (state != RunState.Running) return false;
            if (inputAdapter != null && inputAdapter.Mapping == null)
            {
                string streamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, ButtonMappingConfig.FileName);
                if (!streamingPath.Contains("://") && System.IO.File.Exists(streamingPath))
                    inputAdapter.Configure(ButtonMappingConfig.Parse(System.IO.File.ReadAllText(streamingPath), streamingPath));
            }

            bool hadPlayerBefore = dataTable.TryGetEntity(0, out var playerBefore);
            bool hadOpponentBefore = dataTable.TryGetEntity(3, out var opponentBefore);
            NativeSimInput input = inputAdapter.BuildInput();
            string sourceTrace = inputAdapter.BuildTraceSummary();
            NativeSimData snap;
            string abiMethod = "Sim_CheckAndResetMatch";
            try
            {
                int resetResult = SpikeNativeBridge.Sim_CheckAndResetMatch();
                if (resetResult < 0)
                {
                    Fail($"Sim_CheckAndResetMatch returned {resetResult}");
                    return false;
                }
                if (resetResult == 1)
                {
                    dataTable.ResetTable();
                    consecutiveRejects = 0;
                }

                abiMethod = "Get_data";
                snap = SpikeNativeBridge.Get_data(input);
            }
            catch (Exception ex)
            {
                Fail(abiMethod + " threw: " + ex.Message);
                return false;
            }

            tickCounter++;
            lastNativeSnapshot = snap;
            bool committed = dataTable.UpdateFromNativeSnapshot(snap);
            if (committed)
            {
                consecutiveRejects = 0;
                dataTable.SyncToCharacterStateTable(stateTable);
            }
            else if (++consecutiveRejects >= maxConsecutiveRejects)
            {
                Fail($"{consecutiveRejects} consecutive snapshots rejected; last reason: {dataTable.LastRejectReason}");
            }

            lastTick = new TickRecord
            {
                index = tickCounter,
                unityFrame = inputAdapter.LastSubmitFrame,
                submittedIds = inputAdapter.LastSubmittedIds,
                nativeStatus = snap.status,
                nativeTick = snap.tick,
                committed = committed,
                rejectReason = committed ? null : dataTable.LastRejectReason
            };
            OnTick?.Invoke(lastTick);
            if (Environment.GetEnvironmentVariable("SPIKE_INPUT_TRACE") == "1")
            {
                bool hasPlayerAfter = dataTable.TryGetEntity(0, out var playerAfter);
                bool hasOpponentAfter = dataTable.TryGetEntity(3, out var opponentAfter);
                Debug.Log($"[P02_INPUT_TRACE] sourceAndMapping={sourceTrace} unityFrame={lastTick.unityFrame} managerTick={lastTick.index} nativeTick={lastTick.nativeTick} submittedIds={SpikeInputAdapter.DescribeIds(lastTick.submittedIds)} nativeStatus={lastTick.nativeStatus} committed={lastTick.committed} playerBefore={FormatTraceEntity(hadPlayerBefore, playerBefore)} playerAfter={FormatTraceEntity(hasPlayerAfter, playerAfter)} opponentBefore={FormatTraceEntity(hadOpponentBefore, opponentBefore)} opponentAfter={FormatTraceEntity(hasOpponentAfter, opponentAfter)}");
            }
            return committed;
        }

        private static string FormatTraceEntity(bool available, SimulationEntityRecord entity)
        {
            if (!available) return "unavailable";
            return $"id={entity.entityId},pos=({entity.posX:F2},{entity.posY:F2}),grounded={entity.isGrounded},jumpFrames={entity.jumpRemainingFrames},moveX={entity.moveX:F2},intent={entity.finalIntent},action={entity.actionStatus}";
        }

        /// <summary>Restarts the match: native Sim_Reset + empty table (tick restarts from 0).</summary>
        public void ResetMatch()
        {
            if (!nativeInitialized) return;
            SpikeNativeBridge.Sim_Reset();
            dataTable.ResetTable();
            inputAdapter.ClearInjected();
            consecutiveRejects = 0;
            Debug.Log("[SpikeSimulationManager] Match reset (Sim_Reset + table reset).");
        }

        private void Fail(string reason)
        {
            state = RunState.Failed;
            fatalError = reason;
            Debug.LogError("[SpikeSimulationManager] FATAL: " + reason, this);
        }

        private void Update()
        {
            if (Instance != this) return;
            if (Input.GetKeyDown(KeyCode.R) && state == RunState.Running) ResetMatch();
        }

        /// <summary>
        /// Development diagnostics only: fetches + decodes the API v2 JSON snapshot of the current tick.
        /// Not part of the gameplay path (called by the F1 overlay while it is visible).
        /// </summary>
        public void RefreshDiagnosticsSnapshot()
        {
            if (state != RunState.Running || !apiV2Available) return;
            if (snapshotSchema == null)
            {
                if (SpikeNativeJson.GetSchemaJson(out string schemaJson) != SpikeNativeBridge.SIM_OK) { apiV2Available = false; return; }
                try { snapshotSchema = SimSnapshotSchema.Parse(schemaJson); }
                catch (JsonFormatException ex) { apiV2Available = false; nativeApiStatus = "schema parse error: " + ex.Message; return; }
            }
            if (SpikeNativeJson.GetSnapshotJson(out string json) != SpikeNativeBridge.SIM_OK) { decodeFailureCount++; return; }
            var decoded = SimSnapshotDecoder.Decode(json, snapshotSchema);
            if (!decoded.IsUsable) decodeFailureCount++;
            if (!decoded.Validation.Ok) validationIssueCount++;
            if (decoded.IsUsable && dataTable.IsInitialized && decoded.Tick != dataTable.CurrentTick) tickMismatchCount++;
            decodedSnapshot = decoded;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            state = RunState.Stopped;
            if (nativeInitialized)
            {
                try { SpikeNativeBridge.Sim_Shutdown(); } catch (Exception ex) { Debug.LogWarning("[SpikeSimulationManager] Sim_Shutdown: " + ex.Message); }
                nativeInitialized = false;
            }
            if (dataTable != null) dataTable.ResetTable();
            Instance = null;
        }

        // ------------------------------------------------------------------
        // HUD (reads the Data Table only)
        // ------------------------------------------------------------------
        private GUIStyle scoreStyle, smallStyle, bannerStyle, boxStyle;
        private Texture2D panelTex, errorTex, accentTex;

        private void EnsureStyles(float k)
        {
            if (panelTex == null)
            {
                panelTex = MakeTex(new Color(0.05f, 0.08f, 0.15f, 0.86f));
                errorTex = MakeTex(new Color(0.55f, 0.06f, 0.10f, 0.92f));
                accentTex = MakeTex(new Color(0.92f, 0.74f, 0.28f, 0.95f));
            }
            scoreStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(32 * k), richText = true };
            scoreStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(14 * k), richText = true };
            smallStyle.normal.textColor = new Color(0.88f, 0.93f, 1f);
            bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(18 * k), wordWrap = true };
            bannerStyle.normal.textColor = Color.white;
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = panelTex;
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!showHud || Instance != this) return;
            const float topHudReserve = 0.10f; // Matches the accepted camera's top HUD band.
            Rect safe = Screen.safeArea;
            float safeTop = Screen.height - safe.yMax;
            float margin = Mathf.Min(10f, Screen.height * 0.01f);
            float availableHeight = Mathf.Max(0f, Screen.height * topHudReserve - safeTop - margin);
            float availableWidth = Mathf.Max(0f, safe.width - 2f * margin);
            float k = Mathf.Min(Screen.height / 1080f * 1.4f,
                availableHeight / 82f, availableWidth / 360f);
            if (k <= 0f) return;
            EnsureStyles(k);

            if (state == RunState.Failed)
            {
                var r = new Rect(Screen.width * 0.1f, Screen.height * 0.38f, Screen.width * 0.8f, 120 * k);
                var eb = new GUIStyle(boxStyle); eb.normal.background = errorTex;
                GUI.Box(r, GUIContent.none, eb);
                GUI.Label(r, "Simulation stopped\n" + fatalError, bannerStyle);
                return;
            }
            if (dataTable == null || !dataTable.IsInitialized) return;

            var m = dataTable.MatchRecord;
            float w = 360 * k, h = 82 * k;
            var panel = new Rect(safe.center.x - w * 0.5f, safeTop + margin, w, h);
            GUI.Box(panel, GUIContent.none, boxStyle);
            if (accentTex != null)
                GUI.DrawTexture(new Rect(panel.x, panel.y, w, Mathf.Max(2f, 3f * k)), accentTex);

            string leftServe = m.servingTeam == 0 ? "<color=#F5C451>● </color>" : "";
            string rightServe = m.servingTeam == 1 ? "<color=#F5C451> ●</color>" : "";
            GUI.Label(new Rect(panel.x, panel.y + 4 * k, w, 44 * k),
                $"{leftServe}<color=#7FD3FF>YOU</color>  {m.leftScore}  <color=#F5C451>:</color>  {m.rightScore}  <color=#FF9C8A>CPU</color>{rightServe}", scoreStyle);

            string status;
            bool hasBall = dataTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var ball) &&
                           ball.hasBallGameplayState;
            if (m.finished)
            {
                string winner = m.matchWinner == 0 ? "<color=#7FD3FF>VICTORY!</color>" :
                    m.matchWinner == 1 ? "<color=#FF9C8A>DEFEAT</color>" : "MATCH COMPLETE";
                status = winner + "  •  Press R or Tap to Restart";
            }
            else if (!hasBall) status = "Rally status unavailable";
            else if (ball.stateFrame == 0 && m.servingTeam == 0) status = "<color=#F5C451>YOUR SERVE</color>  •  Press SERVE (Space)";
            else if (ball.stateFrame == 0 && m.servingTeam == 1) status = "<color=#FF9C8A>CPU SERVING...</color>";
            else if (ball.stateFrame > 0) status = "<color=#F5C451>POINT SCORED!</color>";
            else if (ball.stateFrame == -1) status = $"LIVE RALLY  •  TOUCHES: <color=#F5C451>{m.touchCount}/3</color>";
            else status = "Rally status unavailable";
            var statusRect = new Rect(panel.x, panel.y + 48 * k, w, 30 * k);
            GUI.Label(statusRect, status, smallStyle);
            if (m.finished && GUI.Button(panel, GUIContent.none, GUIStyle.none)) ResetMatch();
        }
    }
}
