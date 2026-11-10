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
        private const float LobbyCardWidth = 520f, LobbyCardHeight = 290f;
        private const float LobbyBtnWidth = 240f, LobbyBtnHeight = 56f;
        private const string HudPanelColor = "#101827";
        private const string HudPlayerColor = "#176A83";
        private const string HudOpponentColor = "#7A3D39";
        private const string HudAccentColor = "#FFB547";
        private const string StartButtonColor = "#FF6B2B";
        private const string LobbyBackdropColor = "#080D18E6";

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
        [SerializeField] private bool showPreMatchStartScreen = true;
        [Tooltip("Maximum consecutive rejected snapshots before the simulation is stopped.")]
        [SerializeField] private int maxConsecutiveRejects = 30;

        private RunState state = RunState.Starting;
        private string fatalError;
        private bool nativeInitialized;
        private bool matchStarted;
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
            hudSnapshotReady = false;
            tickCounter = 0;
            consecutiveRejects = 0;
            state = RunState.Running;
            matchStarted = !showPreMatchStartScreen;
            // Commit one baseline snapshot so court entities render behind the pre-match lobby and DataTable is initialized immediately.
            StepOnce();
            Debug.Log($"[SpikeSimulationManager] Native simulation running (fixedDeltaTime={Time.fixedDeltaTime:F5}, {nativeApiStatus}, matchStarted={matchStarted}).");
        }

        /// <summary>Dismisses the pre-match lobby screen and starts real-time match stepping from a fresh reset.</summary>
        public void StartMatch()
        {
            matchStarted = true;
            ResetMatch();
            StepOnce();
            Debug.Log("[SpikeSimulationManager] Match started by player.");
        }

        private void FixedUpdate()
        {
            if (state != RunState.Running || ManualTickMode || !matchStarted) return;
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
            hudSnapshotReady = false;
            inputAdapter.ClearInjected();
            consecutiveRejects = 0;
            Debug.Log("[SpikeSimulationManager] Match reset (Sim_Reset + table reset).");
        }

        private void Fail(string reason)
        {
            state = RunState.Failed;
            fatalError = reason;
            fatalErrorText = "Simulation stopped\n" + reason;
            Debug.LogError("[SpikeSimulationManager] FATAL: " + reason, this);
        }

        private void Update()
        {
            if (Instance != this) return;
            if (!matchStarted && state == RunState.Running &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                StartMatch();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R) && state == RunState.Running)
            {
                matchStarted = true;
                ResetMatch();
            }
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
        // HUD & Pre-Match Broadcast Lobby (reads the Data Table only)
        // ------------------------------------------------------------------
        private GUIStyle scoreStyle, smallStyle, bannerStyle, boxStyle, teamStyle, errorBoxStyle;
        private GUIStyle lobbyTitleStyle, lobbyVsStyle, startBtnStyle, lobbyBackdropStyle;
        private Texture2D panelTex, errorTex, accentTex, playerTex, opponentTex, startBtnTex, lobbyBackdropTex;
        private float hudStyleScale = -1f;
        private int cachedLeftScore = int.MinValue, cachedRightScore = int.MinValue;
        private int cachedServe = int.MinValue;
        private uint cachedTouchCount = uint.MaxValue;
        private int cachedBallStateFrame = int.MinValue, cachedWinner = int.MinValue;
        private bool cachedFinished, cachedHasBall;
        private bool hudSnapshotReady;
        private string leftScoreText = "0", rightScoreText = "0", statusText = "";
        private string fatalErrorText;

        private void OnEnable()
        {
            BindHudSnapshot();
            ColorUtility.TryParseHtmlString(HudPanelColor, out Color panelColor);
            ColorUtility.TryParseHtmlString(HudPlayerColor, out Color playerColor);
            ColorUtility.TryParseHtmlString(HudOpponentColor, out Color opponentColor);
            ColorUtility.TryParseHtmlString(HudAccentColor, out Color accentColor);
            ColorUtility.TryParseHtmlString(StartButtonColor, out Color startBtnColor);
            ColorUtility.TryParseHtmlString(LobbyBackdropColor, out Color lobbyBgColor);
            panelTex = MakeTex(panelColor);
            errorTex = MakeTex(new Color(0.55f, 0.06f, 0.10f, 0.92f));
            accentTex = MakeTex(accentColor);
            playerTex = MakeTex(playerColor);
            opponentTex = MakeTex(opponentColor);
            startBtnTex = MakeTex(startBtnColor);
            lobbyBackdropTex = MakeTex(lobbyBgColor);
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
            boxStyle = new GUIStyle();
            boxStyle.normal.background = panelTex;
            lobbyBackdropStyle = new GUIStyle();
            lobbyBackdropStyle.normal.background = lobbyBackdropTex;
            errorBoxStyle = new GUIStyle();
            errorBoxStyle.normal.background = errorTex;
            hudStyleScale = -1f;
        }

        private void OnDisable()
        {
            if (dataTable != null) dataTable.OnSnapshotCommitted -= RefreshHudFromSnapshot;
            if (panelTex != null) Destroy(panelTex);
            if (errorTex != null) Destroy(errorTex);
            if (accentTex != null) Destroy(accentTex);
            if (playerTex != null) Destroy(playerTex);
            if (opponentTex != null) Destroy(opponentTex);
            if (startBtnTex != null) Destroy(startBtnTex);
            if (lobbyBackdropTex != null) Destroy(lobbyBackdropTex);
            panelTex = errorTex = accentTex = playerTex = opponentTex = startBtnTex = lobbyBackdropTex = null;
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
                cachedLeftScore = match.leftScore;
                leftScoreText = match.leftScore.ToString();
                statusChanged = true;
            }
            if (match.rightScore != cachedRightScore)
            {
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
            else if (statusChanged && cachedBallStateFrame > 0) statusText = "<color=#FFB547>POINT SCORED!</color>";
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
            if (cachedFinished)
            {
                float replayW = LobbyBtnWidth * k;
                float replayH = LobbyBtnHeight * 0.85f * k;
                var replayRect = new Rect(safe.center.x - replayW * 0.5f, panel.yMax + 14f * k, replayW, replayH);
                if (GUI.Button(replayRect, "▶ PLAY AGAIN", startBtnStyle) || GUI.Button(panel, GUIContent.none, GUIStyle.none))
                {
                    StartMatch();
                }
            }

            if (!matchStarted && !ManualTickMode && state == RunState.Running)
            {
                DrawPreMatchStartOverlay(safe, k);
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
