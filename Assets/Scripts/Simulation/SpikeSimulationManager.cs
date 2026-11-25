using System;
using System.Collections;
using System.Text;
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
        private const float HudTopReserve = 0.10f;
        private const float HudWidth = 360f, HudHeight = 82f;
        private const float HudReferenceHeight = 1080f, HudReferenceScale = 1.4f;
        private const float HudMarginMax = 10f, HudMarginHeightFraction = 0.01f;
        private const float HudAccentMinHeight = 2f, HudAccentHeight = 3f;
        private const float HudCardInset = 8f, HudCardRightX = 264f, HudCardWidth = 88f, HudCardHeight = 42f;
        private const float HudTeamTextTop = 9f, HudTeamTextHeight = 22f;
        private const float HudServeTop = 30f, HudServeHeight = 16f;
        private const float HudScoreTop = 7f, HudScoreHeight = 43f;
        private const float HudLeftScoreX = 100f, HudRightScoreX = 207f, HudScoreWidth = 53f;
        private const float HudSeparatorX = 153f, HudSeparatorWidth = 54f;
        private const float HudStatusTop = 52f, HudStatusHeight = 26f;
        private const int HudScoreFontSize = 30, HudSmallFontSize = 12, HudTeamFontSize = 15, HudBannerFontSize = 18;
        private const int LobbyTitleFontSize = 26, LobbyVsFontSize = 20, LobbyButtonFontSize = 22;
        private const int PlayerBadgeFontSize = 12, TopUtilityBtnFontSize = 16, PointBannerFontSize = 15;
        private const float LobbyCardWidth = 520f, LobbyCardHeight = 290f;
        private const float LobbyBtnWidth = 240f, LobbyBtnHeight = 56f;
        private const float TopUtilityBtnWidth = 168f, TopUtilityBtnHeight = 56f, TopUtilityBtnGap = 12f;
        private const float PlayerBadgeWidth = 58f, PlayerBadgeHeight = 22f;
        private const float PointBannerWidth = 340f, PointBannerHeight = 34f;
        private const float PauseModalWidth = 440f, PauseModalHeight = 296f;
        private const string HudPanelColor = "#101827";
        private const string HudPlayerColor = "#176A83";
        private const string HudOpponentColor = "#7A3D39";
        private const string HudAccentColor = "#FFB547";
        private const string StartButtonColor = "#FF6B2B";
        private const string LobbyBackdropColor = "#080D18E6";

        private const int InputTraceHistoryCapacity = 10;
        private const int InputTraceLogSbCapacity = 256;
        private const int InputTraceHudSbCapacity = 96;
        private const float InputTraceBoxLeft = 14f, InputTraceBoxTop = 14f, InputTraceBoxWidth = 360f, InputTraceMinWidth = 200f;
        private const float InputTraceScoreboardGap = 12f;
        private const float InputTracePaddingX = 10f, InputTracePaddingY = 7f;
        private const float InputTraceHeaderHeight = 19f, InputTraceDividerHeight = 1f, InputTraceDividerGap = 5f;
        private const float InputTraceRowHeight = 16f;
        private const float InputTraceMinScale = 0.60f, InputTraceMaxScale = 1.25f;
        private const int InputTraceHeaderFontSize = 12, InputTraceRowFontSize = 11, InputTraceMinFontSize = 9;
        private const string InputTraceBgColor = "#0A111EE0";
        private const string InputTraceDividerColor = "#24364F";
        private const string InputTracePressedColor = "#4DF2D0";
        private const string InputTraceHoldColor = "#FFC857";
        private const string InputTraceEmptyColor = "#8FA3BF";
        private const string InputTraceDefaultHeaderText = "INPUT TRACE  |  Last Input Tick: -";
        private const string InputTraceActiveHeaderPrefix = "INPUT TRACE  |  Last Input Tick: ";
        private const string InputTraceEmptyRowText = "Waiting for gameplay input...";

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
        [Tooltip("Show the Pre-Match START MATCH screen when entering Play Mode.")]
        [SerializeField] private bool showPreMatchStartScreen = false;
        [Tooltip("Maximum consecutive rejected snapshots before the simulation is stopped.")]
        [SerializeField] private int maxConsecutiveRejects = 30;

        [Header("Upper-Layer Input Trace (Bug Diagnostics)")]
        [SerializeField] private bool showInputTraceHud = true;
        [SerializeField] private bool logInputTraceToConsole = true;
        [SerializeField] private bool includeTestInjectionInTrace = false;

        [Header("Attack Serve Toss Trajectory Config (Live C++ RAM)")]
        [Tooltip("Live C++ RAM address of SimAttackServeConfig returned by Sim_GetAttackServeConfigPtr().")]
        [SerializeField] private string cppRamPointerAddress = "0x0";
        [Tooltip("Starting parabola coefficient a (tick 0 of hold) in C++ RAM.")]
        [SerializeField] private float attackServeBaseA = -10.80f;
        [Tooltip("Maximum parabola coefficient a (at max hold) in C++ RAM.")]
        [SerializeField] private float attackServeMaxA = -5.60f;
        [Tooltip("Starting parabola apex height c (tick 0 of hold) in C++ RAM.")]
        [SerializeField] private float attackServeBaseC = 465.0f;
        [Tooltip("Maximum parabola apex height c (at max hold) in C++ RAM.")]
        [SerializeField] private float attackServeMaxC = 609.0f;
        [Tooltip("Total hold steps (ticks) from baseA/baseC to maxA/maxC while holding Receive/Bump in C++ RAM.")]
        [SerializeField] private float attackServeTotalStep = 60.0f;
        [Tooltip("Vertical toss velocity v0 when releasing Receive/Bump in C++ RAM.")]
        [SerializeField] private float attackServeTossV0 = 85.0f;
        [SerializeField, HideInInspector] private bool attackServeConfigInitializedFromCpp = false;

        private static bool hasSessionAttackServeConfig;
        private static NativeAttackServeConfig sessionAttackServeConfig = NativeAttackServeConfig.Default;
        private NativeAttackServeConfig lastSyncedInspectorAttackServeConfig;
        private bool hasLastSyncedInspectorAttackServeConfig;
        private bool isApplyingExternalRamConfig;
        private IntPtr lastCachedAttackServeRamPtr;

        private struct InputTraceHudEntry
        {
            public string buttonName;
            public uint nativeButtonId;
            public int uiButtonId;
            public SpikeInputSource source;
            public SpikeInputAdapter.InputEventKind eventKind;
            public ulong startTick;
            public ulong endTick;
            public int tickSpan;
            public string displayLine;
        }

        private readonly InputTraceHudEntry[] inputTraceHistory = new InputTraceHudEntry[InputTraceHistoryCapacity];
        private int inputTraceHistoryCount;
        private readonly StringBuilder inputTraceLogSb = new StringBuilder(InputTraceLogSbCapacity);
        private readonly StringBuilder inputTraceHudSb = new StringBuilder(InputTraceHudSbCapacity);
        private ulong cachedInputTraceHeaderTick = ulong.MaxValue;
        private string inputTraceHeaderText = InputTraceDefaultHeaderText;

        private RunState state = RunState.Starting;
        private string fatalError;
        private bool nativeInitialized;
        private bool matchStarted;
        private bool isPaused;
        private bool audioMuted;
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
        public bool MatchStarted => matchStarted;
        public bool IsPaused => isPaused;
        public bool AudioMuted => audioMuted;

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
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = TickSeconds;
        }

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            if (Instance != null && Instance != this)
            {
                // Debug.LogError("[SpikeSimulationManager] A second manager was found; it is disabled so only one native runtime exists.", this);
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
            SimulationDataTable.SetInstance(dataTable);
            if (stateTable == null) stateTable = FindAnyObjectByType<CharacterStateTable>(FindObjectsInactive.Include);
            dataTable.ResetTable();
            BindHudSnapshot();

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
            // Debug.Log("[SpikeSimulationManager] Button mapping loaded: " + mapping);

            try
            {
                string seedText = Environment.GetEnvironmentVariable("SPIKE_SIM_SEED");
                if (!string.IsNullOrEmpty(seedText) && uint.TryParse(seedText, out uint seed))
                {
                    SpikeNativeBridge.Sim_SeedRandom(seed);
                    // Debug.Log($"[SpikeSimulationManager] Native RNG seeded with {seed}");
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
            hudSnapshotReady = false;
            tickCounter = 0;
            consecutiveRejects = 0;
            ClearInputTraceHistory();
            state = RunState.Running;
            matchStarted = true;
            InitializeAndSyncAttackServeConfigWithCppRam();
            // Commit one baseline snapshot so court entities render behind the pre-match lobby and DataTable is initialized immediately.
            StepOnce();
            // Debug.Log($"[SpikeSimulationManager] Native simulation running (fixedDeltaTime={Time.fixedDeltaTime:F5}, {nativeApiStatus}, matchStarted={matchStarted}).");
        }

        /// <summary>Dismisses the pre-match lobby screen and starts real-time match stepping from a fresh reset.</summary>
        public void StartMatch()
        {
            matchStarted = true;
            isPaused = false;
            SyncUnityTimeScaleWithPauseState();
            ResetMatch();
            StepOnce();
            // Debug.Log("[SpikeSimulationManager] Match started by player.");
        }

        public void SetPaused(bool paused)
        {
            if (state != RunState.Running) return;
            isPaused = paused;
            SyncUnityTimeScaleWithPauseState();
        }

        public void TogglePause()
        {
            if (!matchStarted || state != RunState.Running) return;
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                UIButtonInputTest.SetLayoutEditMode(false);
            }
            isPaused = !isPaused;
            SyncUnityTimeScaleWithPauseState();
        }

        private void SyncUnityTimeScaleWithPauseState()
        {
            if (!Application.isPlaying) return;
            bool shouldPauseTime = !ManualTickMode && state == RunState.Running && (isPaused || UIButtonInputTest.IsLayoutEditMode);
            if (shouldPauseTime)
            {
                if (Time.timeScale > 0f) Time.timeScale = 0f;
            }
            else if (Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
        }

        public void ToggleAudioMute()
        {
            audioMuted = !audioMuted;
            AudioListener.volume = audioMuted ? 0f : 1f;
        }

        private void FixedUpdate()
        {
            if (state != RunState.Running || ManualTickMode || !matchStarted || isPaused || UIButtonInputTest.IsLayoutEditMode) return;
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
                    hudSnapshotReady = false;
                    consecutiveRejects = 0;
                    ClearInputTraceHistory();
                }

                abiMethod = "Get_data";
                PushInspectorAttackServeConfigToCppRamIfChanged();
                snap = SpikeNativeBridge.Get_data(input);
            }
            catch (Exception ex)
            {
                Fail(abiMethod + " threw: " + ex.Message);
                return false;
            }

            ulong currentTick = snap.tick;
            RecordUpperLayerInputTrace(currentTick);

            tickCounter++;
            lastNativeSnapshot = snap;
            // UpdateFromNativeSnapshot atomically commits entities and fires OnSnapshotCommitted immediately
            // (driving same-phase CharacterDirectionConsumer.flipX) before SyncToCharacterStateTable runs.
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
                // Debug.Log($"[P02_INPUT_TRACE] sourceAndMapping={sourceTrace} unityFrame={lastTick.unityFrame} managerTick={lastTick.index} nativeTick={lastTick.nativeTick} submittedIds={SpikeInputAdapter.DescribeIds(lastTick.submittedIds)} nativeStatus={lastTick.nativeStatus} committed={lastTick.committed} playerBefore={FormatTraceEntity(hadPlayerBefore, playerBefore)} playerAfter={FormatTraceEntity(hasPlayerAfter, playerAfter)} opponentBefore={FormatTraceEntity(hadOpponentBefore, opponentBefore)} opponentAfter={FormatTraceEntity(hasOpponentAfter, opponentAfter)}");
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
            if (Application.isPlaying && !isPaused && !UIButtonInputTest.IsLayoutEditMode && Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
            if (!nativeInitialized) return;
            SpikeNativeBridge.Sim_Reset();
            dataTable.ResetTable();
            hudSnapshotReady = false;
            inputAdapter.ClearInjected();
            consecutiveRejects = 0;
            ClearInputTraceHistory();
            // Debug.Log("[SpikeSimulationManager] Match reset (Sim_Reset + table reset).");
        }

        private void Fail(string reason)
        {
            state = RunState.Failed;
            fatalError = reason;
            fatalErrorText = "Simulation stopped\n" + reason;
            // Debug.LogError("[SpikeSimulationManager] FATAL: " + reason, this);
        }

        private void Update()
        {
            if (Instance != this) return;
            PushInspectorAttackServeConfigToCppRamIfChanged();
            SyncUnityTimeScaleWithPauseState();
            if (!matchStarted && state == RunState.Running &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                StartMatch();
                return;
            }
            if (matchStarted && state == RunState.Running &&
                (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)))
            {
                TogglePause();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleAudioMute();
            }
            if (Input.GetKeyDown(KeyCode.R) && state == RunState.Running)
            {
                matchStarted = true;
                isPaused = false;
                ResetMatch();
            }
        }

        private void UpgradeLegacyAttackServeInspectorFieldsIfNeeded()
        {
            if (Mathf.Abs(attackServeBaseA - (-0.0080f)) < 0.0001f ||
                (Mathf.Abs(attackServeBaseA - (-8.0f)) < 0.001f && Mathf.Abs(attackServeMaxA - (-0.8888889f)) < 0.001f) ||
                attackServeTotalStep <= 0.001f)
            {
                attackServeBaseA = -10.80f;
                attackServeMaxA = -5.60f;
                attackServeTotalStep = 60.0f;
            }
            if (hasSessionAttackServeConfig &&
                (Mathf.Abs(sessionAttackServeConfig.baseA - (-0.0080f)) < 0.0001f ||
                 (Mathf.Abs(sessionAttackServeConfig.baseA - (-8.0f)) < 0.001f && Mathf.Abs(sessionAttackServeConfig.maxA - (-0.8888889f)) < 0.001f) ||
                 sessionAttackServeConfig.totalStep <= 0.001f))
            {
                sessionAttackServeConfig.baseA = -10.80f;
                sessionAttackServeConfig.maxA = -5.60f;
                sessionAttackServeConfig.totalStep = 60.0f;
            }
        }

        private NativeAttackServeConfig GetInspectorAttackServeConfig()
        {
            UpgradeLegacyAttackServeInspectorFieldsIfNeeded();
            return new NativeAttackServeConfig
            {
                baseA = attackServeBaseA,
                maxA = attackServeMaxA,
                baseC = attackServeBaseC,
                maxC = attackServeMaxC,
                totalStep = attackServeTotalStep,
                tossV0 = attackServeTossV0
            };
        }

        private void CacheAttackServeRamPointerHex(IntPtr ramPtr)
        {
            if (ramPtr != IntPtr.Zero && (ramPtr != lastCachedAttackServeRamPtr || string.IsNullOrEmpty(cppRamPointerAddress)))
            {
                lastCachedAttackServeRamPtr = ramPtr;
                cppRamPointerAddress = $"0x{ramPtr.ToInt64():X}";
            }
        }

        private void ApplyAttackServeConfigToInspectorFields(in NativeAttackServeConfig cfg, IntPtr ramPtr)
        {
            isApplyingExternalRamConfig = true;
            try
            {
                CacheAttackServeRamPointerHex(ramPtr);
                attackServeBaseA = cfg.baseA;
                attackServeMaxA = cfg.maxA;
                attackServeBaseC = cfg.baseC;
                attackServeMaxC = cfg.maxC;
                attackServeTotalStep = cfg.totalStep;
                attackServeTossV0 = cfg.tossV0;
                lastSyncedInspectorAttackServeConfig = cfg;
                hasLastSyncedInspectorAttackServeConfig = true;
            }
            finally
            {
                isApplyingExternalRamConfig = false;
            }
        }

        private void HandleExternalAttackServeConfigRamChanged(NativeAttackServeConfig cfg, IntPtr ramPtr)
        {
            if (isApplyingExternalRamConfig) return;
            ApplyAttackServeConfigToInspectorFields(in cfg, ramPtr);
            sessionAttackServeConfig = cfg;
            hasSessionAttackServeConfig = true;
            attackServeConfigInitializedFromCpp = true;
        }

        private void InitializeAndSyncAttackServeConfigWithCppRam()
        {
            UpgradeLegacyAttackServeInspectorFieldsIfNeeded();
            IntPtr ptr = SpikeNativeBridge.GetAttackServeConfigRamPtr();
            CacheAttackServeRamPointerHex(ptr);

            NativeAttackServeConfig inspectorCfg = GetInspectorAttackServeConfig();
            NativeAttackServeConfig defaultCfg = NativeAttackServeConfig.Default;
            bool inspectorModifiedFromDefault = !NativeAttackServeConfig.AreEqual(in inspectorCfg, in defaultCfg);

            if (hasSessionAttackServeConfig)
            {
                if (inspectorModifiedFromDefault && !NativeAttackServeConfig.AreEqual(in inspectorCfg, in sessionAttackServeConfig))
                {
                    sessionAttackServeConfig = inspectorCfg;
                }
                ApplyAttackServeConfigToInspectorFields(in sessionAttackServeConfig, ptr);
                SpikeNativeBridge.TryWriteAttackServeConfigToRam(in sessionAttackServeConfig);
                attackServeConfigInitializedFromCpp = true;
                return;
            }

            if (!attackServeConfigInitializedFromCpp && !inspectorModifiedFromDefault)
            {
                if (SpikeNativeBridge.TryReadAttackServeConfigFromRam(out NativeAttackServeConfig cppCfg))
                {
                    ApplyAttackServeConfigToInspectorFields(in cppCfg, ptr);
                    sessionAttackServeConfig = cppCfg;
                    hasSessionAttackServeConfig = true;
                    attackServeConfigInitializedFromCpp = true;
                    return;
                }
            }

            attackServeConfigInitializedFromCpp = true;
            lastSyncedInspectorAttackServeConfig = inspectorCfg;
            hasLastSyncedInspectorAttackServeConfig = true;
            sessionAttackServeConfig = inspectorCfg;
            hasSessionAttackServeConfig = true;
            SpikeNativeBridge.TryWriteAttackServeConfigToRam(in inspectorCfg);
        }

        private void PushInspectorAttackServeConfigToCppRamIfChanged(bool forceWrite = false)
        {
            if (isApplyingExternalRamConfig) return;
            IntPtr ptr = SpikeNativeBridge.GetAttackServeConfigRamPtr();
            CacheAttackServeRamPointerHex(ptr);

            NativeAttackServeConfig currentCfg = GetInspectorAttackServeConfig();
            bool changed = forceWrite ||
                           !hasLastSyncedInspectorAttackServeConfig ||
                           !NativeAttackServeConfig.AreEqual(in currentCfg, in lastSyncedInspectorAttackServeConfig);
            if (!changed) return;

            lastSyncedInspectorAttackServeConfig = currentCfg;
            hasLastSyncedInspectorAttackServeConfig = true;
            sessionAttackServeConfig = currentCfg;
            hasSessionAttackServeConfig = true;
            attackServeConfigInitializedFromCpp = true;
            SpikeNativeBridge.TryWriteAttackServeConfigToRam(in currentCfg);
        }

        private void OnValidate()
        {
            if (isApplyingExternalRamConfig) return;
            NativeAttackServeConfig currentCfg = GetInspectorAttackServeConfig();
            sessionAttackServeConfig = currentCfg;
            hasSessionAttackServeConfig = true;
            attackServeConfigInitializedFromCpp = true;
            lastSyncedInspectorAttackServeConfig = currentCfg;
            hasLastSyncedInspectorAttackServeConfig = true;
            if (Application.isPlaying)
            {
                SpikeNativeBridge.TryWriteAttackServeConfigToRam(in currentCfg);
            }
        }

#if UNITY_EDITOR
        private void HandleEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange stateChange)
        {
            if (stateChange == UnityEditor.PlayModeStateChange.EnteredEditMode && hasSessionAttackServeConfig && this != null)
            {
                IntPtr ptr = SpikeNativeBridge.GetAttackServeConfigRamPtr();
                ApplyAttackServeConfigToInspectorFields(in sessionAttackServeConfig, ptr);
                attackServeConfigInitializedFromCpp = true;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

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
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
#endif
            if (Instance != this) return;
            state = RunState.Stopped;
            if (nativeInitialized)
            {
                try { SpikeNativeBridge.Sim_Shutdown(); } catch (Exception ex) { /* Debug.LogWarning("[SpikeSimulationManager] Sim_Shutdown: " + ex.Message); */ }
                nativeInitialized = false;
            }
            if (dataTable != null) dataTable.ResetTable();
            Instance = null;
        }

        // ------------------------------------------------------------------
        // HUD & Pre-Match Broadcast Lobby (reads the Data Table only)
        // ------------------------------------------------------------------
        private GUIStyle scoreStyle, smallStyle, bannerStyle, boxStyle, teamStyle, errorBoxStyle;
        private GUIStyle lobbyTitleStyle, lobbyVsStyle, startBtnStyle, lobbyBackdropStyle;
        private GUIStyle playerBadgeStyle, utilityBtnStyle, pointBannerStyle, targetMarkerStyle;
        private GUIStyle inputTraceBoxStyle, inputTraceHeaderStyle, inputTracePressedStyle, inputTraceHoldStyle, inputTraceEmptyStyle;
        private Texture2D panelTex, errorTex, accentTex, playerTex, opponentTex, startBtnTex, lobbyBackdropTex, utilityBtnTex;
        private Texture2D inputTraceBgTex, inputTraceDividerTex;
        private Camera mainCamCached;
        private float hudStyleScale = -1f;
        private float inputTraceStyleScale = -1f;
        private int cachedLeftScore = int.MinValue, cachedRightScore = int.MinValue;
        private int cachedServe = int.MinValue;
        private uint cachedTouchCount = uint.MaxValue;
        private int cachedBallStateFrame = int.MinValue, cachedWinner = int.MinValue;
        private bool cachedFinished, cachedHasBall;
        private bool hudSnapshotReady;
        private float cachedPeakRallySpeedKmh;
        private int cachedLastScorerTeam = -1;
        private string leftScoreText = "0", rightScoreText = "0", statusText = "", pointBannerText = "";
        private string fatalErrorText;

        private void OnEnable()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = TickSeconds;
            SpikeNativeBridge.OnAttackServeConfigRamChanged -= HandleExternalAttackServeConfigRamChanged;
            SpikeNativeBridge.OnAttackServeConfigRamChanged += HandleExternalAttackServeConfigRamChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
#endif
            if (Instance == this && dataTable != null) SimulationDataTable.SetInstance(dataTable);
            BindHudSnapshot();
            ColorUtility.TryParseHtmlString(HudPanelColor, out Color panelColor);
            ColorUtility.TryParseHtmlString(HudPlayerColor, out Color playerColor);
            ColorUtility.TryParseHtmlString(HudOpponentColor, out Color opponentColor);
            ColorUtility.TryParseHtmlString(HudAccentColor, out Color accentColor);
            ColorUtility.TryParseHtmlString(StartButtonColor, out Color startBtnColor);
            ColorUtility.TryParseHtmlString(LobbyBackdropColor, out Color lobbyBgColor);
            ColorUtility.TryParseHtmlString(InputTraceBgColor, out Color inputTraceBgColor);
            ColorUtility.TryParseHtmlString(InputTraceDividerColor, out Color inputTraceDividerColor);
            ColorUtility.TryParseHtmlString(InputTracePressedColor, out Color inputTracePressedColor);
            ColorUtility.TryParseHtmlString(InputTraceHoldColor, out Color inputTraceHoldColor);
            ColorUtility.TryParseHtmlString(InputTraceEmptyColor, out Color inputTraceEmptyColor);
            panelTex = MakeTex(panelColor);
            errorTex = MakeTex(new Color(0.55f, 0.06f, 0.10f, 0.92f));
            accentTex = MakeTex(accentColor);
            playerTex = MakeTex(playerColor);
            opponentTex = MakeTex(opponentColor);
            startBtnTex = MakeTex(startBtnColor);
            lobbyBackdropTex = MakeTex(lobbyBgColor);
            utilityBtnTex = MakeTex(new Color(0.09f, 0.14f, 0.24f, 0.92f));
            inputTraceBgTex = MakeTex(inputTraceBgColor);
            inputTraceDividerTex = MakeTex(inputTraceDividerColor);
            scoreStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            scoreStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            smallStyle.normal.textColor = new Color(0.88f, 0.93f, 1f);
            teamStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            teamStyle.normal.textColor = new Color(0.96f, 0.97f, 0.99f);
            bannerStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            bannerStyle.normal.textColor = Color.white;
            lobbyTitleStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            lobbyTitleStyle.normal.textColor = Color.white;
            lobbyVsStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            lobbyVsStyle.normal.textColor = accentColor;
            startBtnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            startBtnStyle.normal.background = startBtnTex;
            startBtnStyle.normal.textColor = Color.white;
            startBtnStyle.hover.background = accentTex;
            startBtnStyle.hover.textColor = new Color(0.06f, 0.09f, 0.15f, 1f);
            startBtnStyle.active.background = accentTex;
            startBtnStyle.active.textColor = new Color(0.06f, 0.09f, 0.15f, 1f);
            utilityBtnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            utilityBtnStyle.normal.background = utilityBtnTex;
            utilityBtnStyle.normal.textColor = new Color(0.92f, 0.96f, 1f, 1f);
            utilityBtnStyle.hover.background = accentTex;
            utilityBtnStyle.hover.textColor = new Color(0.06f, 0.09f, 0.15f, 1f);
            utilityBtnStyle.active.background = accentTex;
            utilityBtnStyle.active.textColor = new Color(0.06f, 0.09f, 0.15f, 1f);
            playerBadgeStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            playerBadgeStyle.normal.background = lobbyBackdropTex;
            playerBadgeStyle.normal.textColor = accentColor;
            pointBannerStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            pointBannerStyle.normal.background = lobbyBackdropTex;
            pointBannerStyle.normal.textColor = Color.white;
            targetMarkerStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            targetMarkerStyle.normal.textColor = accentColor;
            boxStyle = new GUIStyle();
            boxStyle.normal.background = panelTex;
            lobbyBackdropStyle = new GUIStyle();
            lobbyBackdropStyle.normal.background = lobbyBackdropTex;
            errorBoxStyle = new GUIStyle();
            errorBoxStyle.normal.background = errorTex;
            inputTraceBoxStyle = new GUIStyle();
            inputTraceBoxStyle.normal.background = inputTraceBgTex;
            inputTraceHeaderStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            inputTraceHeaderStyle.normal.textColor = accentColor;
            inputTracePressedStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            inputTracePressedStyle.normal.textColor = inputTracePressedColor;
            inputTraceHoldStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Normal, clipping = TextClipping.Clip };
            inputTraceHoldStyle.normal.textColor = inputTraceHoldColor;
            inputTraceEmptyStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Italic, clipping = TextClipping.Clip };
            inputTraceEmptyStyle.normal.textColor = inputTraceEmptyColor;
            hudStyleScale = -1f;
            inputTraceStyleScale = -1f;
        }

        private void OnDisable()
        {
            SpikeNativeBridge.OnAttackServeConfigRamChanged -= HandleExternalAttackServeConfigRamChanged;
            if (Application.isPlaying && Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
            if (dataTable != null) dataTable.OnSnapshotCommitted -= RefreshHudFromSnapshot;
            if (panelTex != null) Destroy(panelTex);
            if (errorTex != null) Destroy(errorTex);
            if (accentTex != null) Destroy(accentTex);
            if (playerTex != null) Destroy(playerTex);
            if (opponentTex != null) Destroy(opponentTex);
            if (startBtnTex != null) Destroy(startBtnTex);
            if (lobbyBackdropTex != null) Destroy(lobbyBackdropTex);
            if (utilityBtnTex != null) Destroy(utilityBtnTex);
            if (inputTraceBgTex != null) Destroy(inputTraceBgTex);
            if (inputTraceDividerTex != null) Destroy(inputTraceDividerTex);
            panelTex = errorTex = accentTex = playerTex = opponentTex = startBtnTex = lobbyBackdropTex = utilityBtnTex = null;
            inputTraceBgTex = inputTraceDividerTex = null;
        }

        private void EnsureStyles(float k)
        {
            if (Mathf.Approximately(hudStyleScale, k)) return;
            hudStyleScale = k;
            scoreStyle.fontSize = Mathf.RoundToInt(HudScoreFontSize * k);
            smallStyle.fontSize = Mathf.RoundToInt(HudSmallFontSize * k);
            teamStyle.fontSize = Mathf.RoundToInt(HudTeamFontSize * k);
            bannerStyle.fontSize = Mathf.RoundToInt(HudBannerFontSize * k);
            lobbyTitleStyle.fontSize = Mathf.RoundToInt(LobbyTitleFontSize * k);
            lobbyVsStyle.fontSize = Mathf.RoundToInt(LobbyVsFontSize * k);
            startBtnStyle.fontSize = Mathf.RoundToInt(LobbyButtonFontSize * k);
            utilityBtnStyle.fontSize = Mathf.RoundToInt(TopUtilityBtnFontSize * k);
            playerBadgeStyle.fontSize = Mathf.RoundToInt(PlayerBadgeFontSize * k);
            pointBannerStyle.fontSize = Mathf.RoundToInt(PointBannerFontSize * k);
            targetMarkerStyle.fontSize = Mathf.RoundToInt(HudTeamFontSize * k);
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void BindHudSnapshot()
        {
            if (dataTable == null) return;
            dataTable.OnSnapshotCommitted -= RefreshHudFromSnapshot;
            dataTable.OnSnapshotCommitted += RefreshHudFromSnapshot;
        }

        private void RefreshHudFromSnapshot(ulong tick)
        {
            if (dataTable == null || !dataTable.IsInitialized) return;
            var match = dataTable.MatchRecord;
            bool hasBall = dataTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var ball) &&
                           ball.hasBallGameplayState;
            int ballStateFrame = hasBall ? ball.stateFrame : int.MinValue;
            if (hasBall)
            {
                if (ballStateFrame == 0)
                {
                    cachedPeakRallySpeedKmh = 0f;
                }
                else if (ballStateFrame == -1)
                {
                    float simSpeed = Mathf.Abs(ball.ballSpeed);
                    if (simSpeed > cachedPeakRallySpeedKmh) cachedPeakRallySpeedKmh = simSpeed;
                }
            }
            bool statusChanged = cachedServe != match.servingTeam || cachedTouchCount != match.touchCount ||
                                 cachedFinished != match.finished || cachedHasBall != hasBall ||
                                 cachedBallStateFrame != ballStateFrame || cachedWinner != match.matchWinner;
            cachedServe = match.servingTeam;
            cachedTouchCount = match.touchCount;
            cachedFinished = match.finished;
            cachedHasBall = hasBall;
            cachedBallStateFrame = ballStateFrame;
            cachedWinner = match.matchWinner;
            if (match.leftScore != cachedLeftScore)
            {
                if (cachedLeftScore >= 0 && match.leftScore > cachedLeftScore) cachedLastScorerTeam = 0;
                cachedLeftScore = match.leftScore;
                leftScoreText = match.leftScore.ToString();
                statusChanged = true;
            }
            if (match.rightScore != cachedRightScore)
            {
                if (cachedRightScore >= 0 && match.rightScore > cachedRightScore) cachedLastScorerTeam = 1;
                cachedRightScore = match.rightScore;
                rightScoreText = match.rightScore.ToString();
                statusChanged = true;
            }

            if (statusChanged && cachedFinished)
            {
                string winner = cachedWinner == 0 ? "<color=#7FD3FF>VICTORY!</color>" :
                    cachedWinner == 1 ? "<color=#FF9C8A>DEFEAT</color>" : "MATCH COMPLETE";
                statusText = winner + "  •  Press R or Tap to Restart";
            }
            else if (statusChanged && !cachedHasBall) statusText = "Rally status unavailable";
            else if (statusChanged && cachedBallStateFrame == 0 && cachedServe == 0) statusText = "<color=#FFB547>YOUR SERVE</color>  •  Press SERVE (Space)";
            else if (statusChanged && cachedBallStateFrame == 0 && cachedServe == 1) statusText = "<color=#FFB547>CPU SERVING...</color>";
            else if (statusChanged && cachedBallStateFrame > 0)
            {
                statusText = "<color=#FFB547>POINT SCORED!</color>";
                string scorerLabel = cachedLastScorerTeam == 0
                    ? "<color=#7FD3FF>BLUE STRIKERS POINT!</color>"
                    : "<color=#FF9C8A>CRIMSON TITANS POINT!</color>";
                pointBannerText = scorerLabel;
            }
            else if (statusChanged && cachedBallStateFrame == -1) statusText = $"LIVE RALLY  •  TOUCHES: <color=#FFB547>{cachedTouchCount}/3</color>";
            else if (statusChanged) statusText = "Rally status unavailable";
            hudSnapshotReady = true;
        }

        private void OnGUI()
        {
            if (!showHud || Instance != this) return;
            Rect safe = Screen.safeArea;
            float safeTop = Screen.height - safe.yMax;
            float margin = Mathf.Min(HudMarginMax, Screen.height * HudMarginHeightFraction);
            float availableHeight = Mathf.Max(0f, Screen.height * HudTopReserve - safeTop - margin);
            float availableWidth = Mathf.Max(0f, safe.width - 2f * margin);
            float k = Mathf.Min(Screen.height / HudReferenceHeight * HudReferenceScale,
                availableHeight / HudHeight, availableWidth / HudWidth);
            if (k <= 0f) return;
            EnsureStyles(k);

            if (state == RunState.Failed)
            {
                var r = new Rect(Screen.width * 0.1f, Screen.height * 0.38f, Screen.width * 0.8f, 120 * k);
                GUI.Box(r, GUIContent.none, errorBoxStyle);
                GUI.Label(r, fatalErrorText, bannerStyle);
                return;
            }
            if (!hudSnapshotReady) return;

            float w = HudWidth * k, h = HudHeight * k;
            var panel = new Rect(safe.center.x - w * 0.5f, safeTop + margin, w, h);
            GUI.Box(panel, GUIContent.none, boxStyle);
            GUI.DrawTexture(new Rect(panel.x, panel.y, w, Mathf.Max(HudAccentMinHeight, HudAccentHeight * k)), accentTex);
            GUI.DrawTexture(new Rect(panel.x + HudCardInset * k, panel.y + HudCardInset * k, HudCardWidth * k, HudCardHeight * k), playerTex);
            GUI.DrawTexture(new Rect(panel.x + HudCardRightX * k, panel.y + HudCardInset * k, HudCardWidth * k, HudCardHeight * k), opponentTex);
            GUI.Label(new Rect(panel.x + HudCardInset * k, panel.y + HudTeamTextTop * k, HudCardWidth * k, HudTeamTextHeight * k), "YOU", teamStyle);
            GUI.Label(new Rect(panel.x + HudCardRightX * k, panel.y + HudTeamTextTop * k, HudCardWidth * k, HudTeamTextHeight * k), "CPU", teamStyle);
            if (cachedServe == 0)
                GUI.Label(new Rect(panel.x + HudCardInset * k, panel.y + HudServeTop * k, HudCardWidth * k, HudServeHeight * k), "● SERVE", smallStyle);
            if (cachedServe == 1)
                GUI.Label(new Rect(panel.x + HudCardRightX * k, panel.y + HudServeTop * k, HudCardWidth * k, HudServeHeight * k), "● SERVE", smallStyle);
            GUI.Label(new Rect(panel.x + HudLeftScoreX * k, panel.y + HudScoreTop * k, HudScoreWidth * k, HudScoreHeight * k), leftScoreText, scoreStyle);
            GUI.Label(new Rect(panel.x + HudSeparatorX * k, panel.y + HudScoreTop * k, HudSeparatorWidth * k, HudScoreHeight * k), ":", scoreStyle);
            GUI.Label(new Rect(panel.x + HudRightScoreX * k, panel.y + HudScoreTop * k, HudScoreWidth * k, HudScoreHeight * k), rightScoreText, scoreStyle);
            var statusRect = new Rect(panel.x, panel.y + HudStatusTop * k, w, HudStatusHeight * k);
            GUI.Label(statusRect, statusText, smallStyle);

            // Top-right Broadcast Utility Bar (Audio Mute & Pause)
            DrawTopRightUtilityBar(safe, safeTop, margin, k);

            // World-space HUD overlays: "▼ YOU" controlled-player indicator & floor landing target reticle
            if (matchStarted && !isPaused && !UIButtonInputTest.IsLayoutEditMode)
            {
                DrawWorldTrackingIndicators(k);
            }

            // Point Scored Broadcast Banner + Spike Speedometer (KM/H) — hidden while Custom UI Dock is open so they never overlap
            if (matchStarted && !UIButtonInputTest.IsLayoutEditMode && cachedBallStateFrame > 0 && !cachedFinished && !string.IsNullOrEmpty(pointBannerText))
            {
                float bw = PointBannerWidth * k;
                float bh = PointBannerHeight * k;
                var bannerRect = new Rect(safe.center.x - bw * 0.5f, panel.yMax + 8f * k, bw, bh);
                GUI.DrawTexture(new Rect(bannerRect.x, bannerRect.y, bw, Mathf.Max(2f, 3f * k)), accentTex);
                GUI.Label(bannerRect, pointBannerText, pointBannerStyle);
            }

            if (cachedFinished && !UIButtonInputTest.IsLayoutEditMode)
            {
                float replayW = LobbyBtnWidth * k;
                float replayH = LobbyBtnHeight * 0.85f * k;
                var replayRect = new Rect(safe.center.x - replayW * 0.5f, panel.yMax + 14f * k, replayW, replayH);
                if (GUI.Button(replayRect, "▶ PLAY AGAIN", startBtnStyle) || GUI.Button(panel, GUIContent.none, GUIStyle.none))
                {
                    StartMatch();
                }
            }

            if (!matchStarted && !ManualTickMode && state == RunState.Running && !UIButtonInputTest.IsLayoutEditMode)
            {
                DrawPreMatchStartOverlay(safe, k);
            }
            else if (isPaused && matchStarted && state == RunState.Running && !UIButtonInputTest.IsLayoutEditMode)
            {
                DrawPauseModalOverlay(safe, k);
            }

            if (showInputTraceHud && matchStarted)
            {
                DrawInputTraceHudOverlay(safe, safeTop, panel.x);
            }
        }

        private void ClearInputTraceHistory()
        {
            inputTraceHistoryCount = 0;
            cachedInputTraceHeaderTick = ulong.MaxValue;
            inputTraceHeaderText = InputTraceDefaultHeaderText;
        }

        private void UpdateInputTraceHeaderText(ulong currentTick)
        {
            if (cachedInputTraceHeaderTick == currentTick) return;
            cachedInputTraceHeaderTick = currentTick;
            inputTraceHudSb.Clear();
            inputTraceHudSb.Append(InputTraceActiveHeaderPrefix);
            inputTraceHudSb.Append(currentTick);
            inputTraceHeaderText = inputTraceHudSb.ToString();
        }

        private void RecordUpperLayerInputTrace(ulong currentTick)
        {
            if (inputAdapter == null) return;
            int totalTraceCount = inputAdapter.LastTraceCount;
            if (totalTraceCount <= 0) return;

            const SpikeInputSource gameplaySources = SpikeInputSource.UiPointer | SpikeInputSource.Keyboard;
            bool hasMatchedAny = false;

            for (int i = 0; i < totalTraceCount; i++)
            {
                SpikeInputAdapter.InputTraceItem item = inputAdapter.GetLastTraceItem(i);
                bool shouldInclude = ((item.source & gameplaySources) != 0) || includeTestInjectionInTrace;
                if (!shouldInclude) continue;

                if (!hasMatchedAny)
                {
                    hasMatchedAny = true;
                    UpdateInputTraceHeaderText(currentTick);
                    if (logInputTraceToConsole)
                    {
                        inputTraceLogSb.Clear();
                        inputTraceLogSb.Append("[INPUT_TRACE] tick=");
                        inputTraceLogSb.Append(currentTick);
                    }
                }

                AppendOrCoalesceHudTraceEntry(in item, currentTick);

                if (logInputTraceToConsole)
                {
                    string btnName = string.IsNullOrEmpty(item.buttonName) ? "Unknown" : item.buttonName;
                    inputTraceLogSb.Append(" | btn=");
                    inputTraceLogSb.Append(btnName);
                    inputTraceLogSb.Append(" (id=");
                    inputTraceLogSb.Append(item.nativeButtonId);
                    inputTraceLogSb.Append(", ui=");
                    inputTraceLogSb.Append(item.uiButtonId);
                    inputTraceLogSb.Append(", src=");
                    inputTraceLogSb.Append(FormatInputSourceLabel(item.source));
                    inputTraceLogSb.Append(") event=");
                    inputTraceLogSb.Append(item.eventKind == SpikeInputAdapter.InputEventKind.Pressed ? "PRESSED" : "HOLD");
                }
            }

            if (hasMatchedAny && logInputTraceToConsole)
            {
                Debug.Log(inputTraceLogSb.ToString());
            }
        }

        private void AppendOrCoalesceHudTraceEntry(in SpikeInputAdapter.InputTraceItem item, ulong currentTick)
        {
            string btnName = string.IsNullOrEmpty(item.buttonName) ? "Unknown" : item.buttonName;

            if (item.eventKind == SpikeInputAdapter.InputEventKind.Hold)
            {
                for (int h = inputTraceHistoryCount - 1; h >= 0; h--)
                {
                    ref InputTraceHudEntry existing = ref inputTraceHistory[h];
                    if (existing.eventKind == SpikeInputAdapter.InputEventKind.Hold &&
                        existing.nativeButtonId == item.nativeButtonId &&
                        string.Equals(existing.buttonName, btnName, StringComparison.Ordinal) &&
                        (existing.endTick + 1 == currentTick || existing.endTick == currentTick))
                    {
                        if (existing.endTick != currentTick)
                        {
                            existing.endTick = currentTick;
                            existing.tickSpan++;
                        }
                        existing.uiButtonId = item.uiButtonId;
                        existing.source |= item.source;
                        existing.displayLine = BuildHudTraceDisplayLine(in existing);
                        return;
                    }
                }
            }

            int insertIndex;
            if (inputTraceHistoryCount < InputTraceHistoryCapacity)
            {
                insertIndex = inputTraceHistoryCount;
                inputTraceHistoryCount++;
            }
            else
            {
                Array.Copy(inputTraceHistory, 1, inputTraceHistory, 0, InputTraceHistoryCapacity - 1);
                insertIndex = InputTraceHistoryCapacity - 1;
            }

            var entry = new InputTraceHudEntry
            {
                buttonName = btnName,
                nativeButtonId = item.nativeButtonId,
                uiButtonId = item.uiButtonId,
                source = item.source,
                eventKind = item.eventKind,
                startTick = currentTick,
                endTick = currentTick,
                tickSpan = 1,
                displayLine = null
            };
            entry.displayLine = BuildHudTraceDisplayLine(in entry);
            inputTraceHistory[insertIndex] = entry;
        }

        private string BuildHudTraceDisplayLine(in InputTraceHudEntry entry)
        {
            inputTraceHudSb.Clear();
            inputTraceHudSb.Append("t=");
            inputTraceHudSb.Append(entry.startTick);
            if (entry.eventKind == SpikeInputAdapter.InputEventKind.Hold)
            {
                if (entry.endTick > entry.startTick)
                {
                    inputTraceHudSb.Append("..");
                    inputTraceHudSb.Append(entry.endTick);
                }
                inputTraceHudSb.Append(" (");
                inputTraceHudSb.Append(entry.tickSpan);
                inputTraceHudSb.Append("t)");
            }

            inputTraceHudSb.Append(" | ");
            inputTraceHudSb.Append(entry.buttonName);
            if (entry.nativeButtonId == 0)
            {
                inputTraceHudSb.Append(" (ui=");
                inputTraceHudSb.Append(entry.uiButtonId);
            }
            else
            {
                inputTraceHudSb.Append(" (id=");
                inputTraceHudSb.Append(entry.nativeButtonId);
            }
            inputTraceHudSb.Append(", ");
            inputTraceHudSb.Append(FormatInputSourceLabel(entry.source));
            inputTraceHudSb.Append(") | ");
            inputTraceHudSb.Append(entry.eventKind == SpikeInputAdapter.InputEventKind.Pressed ? "PRESSED" : "HOLD");
            return inputTraceHudSb.ToString();
        }

        private static string FormatInputSourceLabel(SpikeInputSource source)
        {
            bool ui = (source & SpikeInputSource.UiPointer) != 0;
            bool kb = (source & SpikeInputSource.Keyboard) != 0;
            bool test = (source & SpikeInputSource.TestInjection) != 0;
            if (ui && kb && test) return "UI+KB+TEST";
            if (ui && kb) return "UI+KB";
            if (ui && test) return "UI+TEST";
            if (kb && test) return "KB+TEST";
            if (ui) return "UI";
            if (kb) return "KB";
            if (test) return "TEST";
            return "NONE";
        }

        private void EnsureInputTraceStyles(float traceScale)
        {
            if (Mathf.Approximately(inputTraceStyleScale, traceScale)) return;
            inputTraceStyleScale = traceScale;
            int headerFont = Mathf.Max(InputTraceMinFontSize, Mathf.RoundToInt(InputTraceHeaderFontSize * traceScale));
            int rowFont = Mathf.Max(InputTraceMinFontSize, Mathf.RoundToInt(InputTraceRowFontSize * traceScale));
            inputTraceHeaderStyle.fontSize = headerFont;
            inputTracePressedStyle.fontSize = rowFont;
            inputTraceHoldStyle.fontSize = rowFont;
            inputTraceEmptyStyle.fontSize = rowFont;
        }

        private void DrawInputTraceHudOverlay(Rect safe, float safeTop, float scoreboardLeftX)
        {
            if (inputTraceBoxStyle == null) return;

            float traceScale = Mathf.Clamp(Screen.height / HudReferenceHeight, InputTraceMinScale, InputTraceMaxScale);
            EnsureInputTraceStyles(traceScale);

            float boxX = safe.xMin + InputTraceBoxLeft;
            float boxY = safeTop + InputTraceBoxTop;
            float boxW = InputTraceBoxWidth * traceScale;
            float maxBeforeScoreboard = scoreboardLeftX - boxX - InputTraceScoreboardGap;
            if (maxBeforeScoreboard >= InputTraceMinWidth && boxW > maxBeforeScoreboard)
            {
                boxW = maxBeforeScoreboard;
            }

            float padX = InputTracePaddingX * traceScale;
            float padY = InputTracePaddingY * traceScale;
            float headerH = InputTraceHeaderHeight * traceScale;
            float dividerGap = InputTraceDividerGap * traceScale;
            float rowH = InputTraceRowHeight * traceScale;
            int visibleRows = inputTraceHistoryCount > 0 ? inputTraceHistoryCount : 1;
            float boxH = padY * 2f + headerH + dividerGap + InputTraceDividerHeight + visibleRows * rowH;

            var boxRect = new Rect(boxX, boxY, boxW, boxH);
            GUI.Box(boxRect, GUIContent.none, inputTraceBoxStyle);
            GUI.DrawTexture(new Rect(boxX, boxY, boxW, Mathf.Max(HudAccentMinHeight, HudAccentHeight * traceScale)), accentTex);

            float contentX = boxX + padX;
            float contentW = Mathf.Max(0f, boxW - padX * 2f);
            float cursorY = boxY + padY;

            GUI.Label(new Rect(contentX, cursorY, contentW, headerH), inputTraceHeaderText, inputTraceHeaderStyle);
            cursorY += headerH + dividerGap * 0.5f;

            if (inputTraceDividerTex != null)
            {
                GUI.DrawTexture(new Rect(contentX, cursorY, contentW, InputTraceDividerHeight), inputTraceDividerTex);
            }
            cursorY += InputTraceDividerHeight + dividerGap * 0.5f;

            if (inputTraceHistoryCount == 0)
            {
                GUI.Label(new Rect(contentX, cursorY, contentW, rowH), InputTraceEmptyRowText, inputTraceEmptyStyle);
                return;
            }

            for (int i = 0; i < inputTraceHistoryCount; i++)
            {
                ref readonly InputTraceHudEntry entry = ref inputTraceHistory[i];
                GUIStyle rowStyle = entry.eventKind == SpikeInputAdapter.InputEventKind.Pressed
                    ? inputTracePressedStyle
                    : inputTraceHoldStyle;
                GUI.Label(new Rect(contentX, cursorY + i * rowH, contentW, rowH), entry.displayLine, rowStyle);
            }
        }

        private void DrawTopRightUtilityBar(Rect safe, float safeTop, float margin, float k)
        {
            float topBarScale = Mathf.Clamp(
                Mathf.Min(Mathf.Max(1f, safe.width) / 1920f, Mathf.Max(1f, Screen.height) / 1080f),
                0.35f,
                2.5f);
            utilityBtnStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(TopUtilityBtnFontSize * topBarScale));

            float btnW = TopUtilityBtnWidth * topBarScale;
            float btnH = TopUtilityBtnHeight * topBarScale;
            float gap = TopUtilityBtnGap * topBarScale;
            float rightEdge = safe.xMax - 14f * topBarScale;
            float topY = safeTop + Mathf.Max(margin, 38f * topBarScale - btnH * 0.5f);

            var pauseRect = new Rect(rightEdge - btnW, topY, btnW, btnH);
            var muteRect = new Rect(pauseRect.x - gap - btnW, topY, btnW, btnH);

            float accentH = Mathf.Max(2f, 3f * topBarScale);
            GUI.DrawTexture(new Rect(muteRect.x, muteRect.y, btnW, accentH), accentTex);
            string muteLabel = audioMuted ? "🔇 MUTE" : "🔊 BGM ON";
            if (GUI.Button(muteRect, muteLabel, utilityBtnStyle))
            {
                ToggleAudioMute();
            }

            if (matchStarted)
            {
                GUI.DrawTexture(new Rect(pauseRect.x, pauseRect.y, btnW, accentH), accentTex);
                string pauseLabel = isPaused ? "▶ RESUME" : "⏸ PAUSE";
                if (GUI.Button(pauseRect, pauseLabel, utilityBtnStyle))
                {
                    TogglePause();
                }
            }
        }

        private void DrawWorldTrackingIndicators(float k)
        {
            if (dataTable == null || !dataTable.IsInitialized) return;
            if (mainCamCached == null) mainCamCached = Camera.main;
            if (mainCamCached == null) return;

            // 1. Controlled Player Overhead Indicator ("▼ YOU")
            if (dataTable.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out var player) &&
                player.hasPosition && player.hasSize)
            {
                Vector3 anchor = SimulationEntityReader.AnchorWorld(player);
                float headWorldY = anchor.y + SimulationEntityReader.ToWorldLength(player.sizeY) + 0.42f;
                Vector3 screenPt = mainCamCached.WorldToScreenPoint(new Vector3(anchor.x, headWorldY, 0f));
                if (screenPt.z > 0f)
                {
                    float guiX = screenPt.x;
                    float guiY = Screen.height - screenPt.y;
                    float bw = PlayerBadgeWidth * k;
                    float bh = PlayerBadgeHeight * k;
                    var badgeRect = new Rect(guiX - bw * 0.5f, guiY - bh, bw, bh);
                    GUI.Label(badgeRect, "<color=#7FD3FF>▼ YOU</color>", playerBadgeStyle);
                }
            }

            // 2. Ball Floor Landing Target Reticle ("( ⊕ )") during active rally
            if (cachedHasBall && cachedBallStateFrame == -1 &&
                dataTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out var ball) &&
                ball.hasPosition && ball.hasSize)
            {
                Vector3 ballWorld = SimulationEntityReader.AnchorWorld(ball);
                float floorWorldY = CourtGeometry.ToWorldY(CourtGeometry.FloorY);
                if (ballWorld.y > floorWorldY + 0.35f)
                {
                    Vector3 floorPt = mainCamCached.WorldToScreenPoint(new Vector3(ballWorld.x, floorWorldY, 0f));
                    if (floorPt.z > 0f)
                    {
                        float gx = floorPt.x;
                        float gy = Screen.height - floorPt.y;
                        float mw = 48f * k;
                        float mh = 20f * k;
                        GUI.Label(new Rect(gx - mw * 0.5f, gy - mh * 0.5f, mw, mh), "<color=#FFB547>◎</color>", targetMarkerStyle);
                    }
                }
            }
        }

        private void DrawPauseModalOverlay(Rect safe, float k)
        {
            float cardW = Mathf.Min(safe.width * 0.86f, PauseModalWidth * k);
            float cardH = Mathf.Min(safe.height * 0.78f, PauseModalHeight * k);
            var card = new Rect(safe.center.x - cardW * 0.5f, safe.center.y - cardH * 0.5f, cardW, cardH);

            GUI.Box(card, GUIContent.none, lobbyBackdropStyle);
            GUI.DrawTexture(new Rect(card.x, card.y, cardW, Mathf.Max(3f, 4f * k)), accentTex);
            GUI.DrawTexture(new Rect(card.x, card.yMax - Mathf.Max(2f, 3f * k), cardW, Mathf.Max(2f, 3f * k)), accentTex);

            GUI.Label(new Rect(card.x, card.y + 14f * k, cardW, 32f * k),
                "MATCH <color=#FFB547>PAUSED</color>", lobbyTitleStyle);
            GUI.Label(new Rect(card.x, card.y + 44f * k, cardW, 18f * k),
                "<color=#7FD3FF>GAMEPLAY OPTIONS & HUD CUSTOMIZATION</color>", smallStyle);

            float btnW = 246f * k;
            float btnH = 40f * k;
            float startY = card.y + 72f * k;
            float stepY = 50f * k;

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY, btnW, btnH), "▶  RESUME MATCH", startBtnStyle))
            {
                isPaused = false;
            }

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY, btnW, btnH), "⚙  CUSTOMIZE HUD LAYOUT", utilityBtnStyle))
            {
                isPaused = false;
                UIButtonInputTest.SetLayoutEditMode(true);
            }

            string audioLabel = audioMuted ? "🔇  UNMUTE AUDIO (M)" : "🔊  MUTE AUDIO (M)";
            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY * 2f, btnW, btnH), audioLabel, utilityBtnStyle))
            {
                ToggleAudioMute();
            }

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY * 3f, btnW, btnH), "↻  RESTART MATCH (R)", utilityBtnStyle))
            {
                isPaused = false;
                ResetMatch();
                StepOnce();
            }
        }

        private void DrawPreMatchStartOverlay(Rect safe, float k)
        {
            float cardW = Mathf.Min(safe.width * 0.90f, LobbyCardWidth * k);
            float cardH = Mathf.Min(safe.height * 0.78f, LobbyCardHeight * k);
            var card = new Rect(safe.center.x - cardW * 0.5f, safe.center.y - cardH * 0.5f, cardW, cardH);

            GUI.Box(card, GUIContent.none, lobbyBackdropStyle);
            GUI.DrawTexture(new Rect(card.x, card.y, cardW, Mathf.Max(3f, 5f * k)), accentTex);
            GUI.DrawTexture(new Rect(card.x, card.yMax - Mathf.Max(2f, 3f * k), cardW, Mathf.Max(2f, 3f * k)), accentTex);

            GUI.Label(new Rect(card.x, card.y + 18f * k, cardW, 36f * k),
                "THE SPIKE CROSS  <color=#FFB547>REMASTERED</color>", lobbyTitleStyle);
            GUI.Label(new Rect(card.x, card.y + 52f * k, cardW, 22f * k),
                "<color=#7FD3FF>CHAMPIONSHIP STADIUM MATCH</color>  •  3 vs 3 EXHIBITION", smallStyle);

            float teamBoxW = 160f * k;
            float teamBoxH = 68f * k;
            float teamBoxY = card.y + 88f * k;
            var leftTeamRect = new Rect(card.center.x - teamBoxW - 38f * k, teamBoxY, teamBoxW, teamBoxH);
            var rightTeamRect = new Rect(card.center.x + 38f * k, teamBoxY, teamBoxW, teamBoxH);

            GUI.DrawTexture(leftTeamRect, playerTex);
            GUI.DrawTexture(rightTeamRect, opponentTex);
            GUI.Label(new Rect(leftTeamRect.x, leftTeamRect.y + 10f * k, teamBoxW, 26f * k), "YOU (HOME)", teamStyle);
            GUI.Label(new Rect(leftTeamRect.x, leftTeamRect.y + 36f * k, teamBoxW, 20f * k), "<color=#7FD3FF>BLUE STRIKERS</color>", smallStyle);
            GUI.Label(new Rect(rightTeamRect.x, rightTeamRect.y + 10f * k, teamBoxW, 26f * k), "CPU (AWAY)", teamStyle);
            GUI.Label(new Rect(rightTeamRect.x, rightTeamRect.y + 36f * k, teamBoxW, 20f * k), "<color=#FF9C8A>CRIMSON TITANS</color>", smallStyle);

            GUI.Label(new Rect(card.center.x - 36f * k, teamBoxY + 18f * k, 72f * k, 32f * k), "VS", lobbyVsStyle);

            GUI.Label(new Rect(card.x + 16f * k, teamBoxY + teamBoxH + 14f * k, cardW - 32f * k, 22f * k),
                "Controls:  <color=#FFB547>◄ ►</color> Move   •   <color=#FFB547>JUMP / AIR SPIKE</color>   •   <color=#FFB547>HOLD SPIKE</color> Power Wheel", smallStyle);

            float btnW = LobbyBtnWidth * k;
            float btnH = LobbyBtnHeight * k;
            var btnRect = new Rect(card.center.x - btnW * 0.5f, card.yMax - btnH - 22f * k, btnW, btnH);
            GUI.DrawTexture(new Rect(btnRect.x - 2f * k, btnRect.y - 2f * k, btnW + 4f * k, btnH + 4f * k), accentTex);
            if (GUI.Button(btnRect, "▶  START MATCH", startBtnStyle))
            {
                StartMatch();
            }
        }
    }
}
