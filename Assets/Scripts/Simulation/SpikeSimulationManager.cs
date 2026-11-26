using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Table;
using Native;
using Native.Snapshot;
using UnityEngine;
using UnityEngine.Networking;
using BallFeedbackPresenter = Presentation.Feedback.BallFeedbackPresenter;
using GraphicRaycaster = UnityEngine.UI.GraphicRaycaster;

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
        private const int PlayerBadgeFontSize = 12, TopUtilityBtnFontSize = 14, TopUtilityMinFontSize = 9, PointBannerFontSize = 15;
        private const float LobbyCardWidth = 520f, LobbyCardHeight = 290f;
        private const float LobbyBtnWidth = 240f, LobbyBtnHeight = 56f;
        private const float TopUtilityReferenceWidth = 1920f, TopUtilityReferenceHeight = 1080f;
        private const float TopUtilityPauseBtnWidth = 118f, TopUtilityAudioBtnWidth = 126f, TopUtilityGuideBtnWidth = 106f;
        private const float TopUtilityBtnHeight = 46f, TopUtilityBtnGap = 8f, TopUtilityRightMargin = 14f;
        private const float TopUtilityTopAnchorY = 34f, TopUtilityScoreboardClearance = 14f;
        private const float TopUtilityMinScale = 0.25f, TopUtilityMaxScale = 2.2f;
        private const float PlayerBadgeWidth = 58f, PlayerBadgeHeight = 22f;
        private const float PointBannerWidth = 340f, PointBannerHeight = 34f;
        private const float PauseModalWidth = 492f, PauseModalHeight = 388f;
        private const float PauseModalMaxSafeWidthFraction = 0.90f, PauseModalMaxSafeHeightFraction = 0.86f;
        private const float PauseMixerInsetX = 24f, PauseMixerLabelWidth = 84f, PauseMixerPercentWidth = 48f;
        private const float PauseMixerMuteBtnWidth = 88f, PauseMixerMuteBtnHeight = 32f, PauseMixerColGap = 8f;
        private const float PauseMixerBgmRowTop = 70f, PauseMixerSfxRowTop = 112f, PauseMixerRowHeight = 34f;
        private const float PauseMixerSliderOffsetY = 10f, PauseMixerSliderHeight = 18f;
        private const float PauseDividerTop = 156f, PauseDividerHeight = 1f;
        private const float PauseActionBtnWidth = 292f, PauseActionBtnHeight = 38f;
        private const float PauseActionStartTop = 168f, PauseActionStepY = 46f;
        private const int PauseMixerFontSize = 14, PauseActionBtnFontSize = 15;
        private const float DefaultFallbackBgmVolume = 0.16f, DefaultFallbackSfxVolume = 1.00f;
        private const float VolumeSliderUnmuteThreshold = 0.005f, VolumeChangeEpsilon = 0.0005f;
        private const int VolumePercentStepCount = 101;
        private const string TopUtilityPauseLabel = "⏸ PAUSE";
        private const string TopUtilityResumeLabel = "▶ RESUME";
        private const string TopUtilitySfxOnLabel = "🔊 SFX ON";
        private const string TopUtilitySfxOffLabel = "🔇 SFX OFF";
        private const string TopUtilityBgmOnLabel = "🎵 BGM ON";
        private const string TopUtilityBgmOffLabel = "🔇 BGM OFF";
        private const string TopUtilityGuideLabel = "? GUIDE";
        private const string PauseBgmRowLabel = "🎵 BGM";
        private const string PauseSfxRowLabel = "🔊 SFX";
        private const string PauseChannelOnLabel = "🔊 ON";
        private const string PauseChannelOffLabel = "🔇 OFF";
        private const string PauseResumeBtnLabel = "▶  RESUME MATCH";
        private const string PauseGuideBtnLabel = "📖  HƯỚNG DẪN CHƠI";
        private const string PauseCustomHudBtnLabel = "⚙  CUSTOMIZE HUD LAYOUT";
        private const string PauseRestartBtnLabel = "↻  RESTART MATCH (R)";
        private const string HudPanelColor = "#101827";
        private const string HudPlayerColor = "#176A83";
        private const string HudOpponentColor = "#7A3D39";
        private const string HudAccentColor = "#FFB547";
        private const string StartButtonColor = "#FF6B2B";
        private const string LobbyBackdropColor = "#080D18E6";

        // Start-of-Game 90% Black Opacity Interactive Tutorial Spotlight Overlay (7 Steps)
        public const int TutorialTotalStepCount = 7;
        private const int TutorialUtilityBarStepIndex = 6;
        private const int TutorialMaxWaitFramesForHudController = 12;
        private const float TutorialBackdropAlpha = 0.90f;
        private const float TutorialSpotlightPadding = 10f;
        private const float TutorialSpotlightBorderThickness = 3f;
        private const float TutorialSpotlightOuterGlowExpand = 5f;
        private const float TutorialCardBaseWidth = 568f, TutorialCardBaseHeight = 278f;
        private const float TutorialCardMaxWidthFraction = 0.58f, TutorialCardMaxHeightFraction = 0.62f;
        private const float TutorialCardSafeMargin = 16f, TutorialCardInnerPadX = 22f;
        private const float TutorialConnectorGap = 54f, TutorialConnectorMinGap = 22f;
        private const float TutorialConnectorLineThickness = 3f;
        private const float TutorialArrowBadgeSize = 32f, TutorialArrowSpotGap = 6f;
        private const float TutorialSkipBtnWidth = 148f, TutorialSkipBtnHeight = 44f;
        private const float TutorialSkipMarginRight = 16f, TutorialSkipMarginTop = 14f, TutorialSkipSpotGap = 12f;
        private const float TutorialPrevBtnWidth = 148f, TutorialNextBtnWidth = 182f, TutorialNavBtnHeight = 42f;
        private const float TutorialStepDotSize = 8f, TutorialStepDotActiveWidth = 20f, TutorialStepDotGap = 7f;
        private const int TutorialBadgeFontSize = 13, TutorialTitleFontSize = 19, TutorialBodyFontSize = 15;
        private const int TutorialNavBtnFontSize = 15, TutorialSkipBtnFontSize = 15, TutorialArrowFontSize = 17;
        private const float FallbackSliderLeftMargin = 42f, FallbackSliderBottomMargin = 44f;
        private const float FallbackSliderWidth = 380f, FallbackSliderHeight = 96f;
        private const float FallbackTopLeftPillX = 16f, FallbackTopLeftPillY = 14f;
        private const float FallbackTopLeftPillWidth = 168f, FallbackTopLeftPillHeight = 46f;
        private const float PrimaryVsSecondaryWidthRatio = 1.15f;
        private const string TutorialSkipBtnLabel = "BỎ QUA ✕";
        private const string TutorialPrevBtnLabel = "◄ TRƯỚC";
        private const string TutorialNextBtnLabel = "TIẾP THEO ►";
        private const string TutorialStartPlayBtnLabel = "▶ BẮT ĐẦU CHƠI";
        private const string ArrowLeftGlyph = "◄";
        private const string ArrowRightGlyph = "►";
        private const string ArrowUpGlyph = "▲";
        private const string ArrowDownGlyph = "▼";

        private static readonly string[] PercentStrings0To100 = BuildPercentStrings0To100();

        private static readonly string[] TutorialStepBadges =
        {
            "HƯỚNG DẪN CHƠI  •  BƯỚC 1/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 2/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 3/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 4/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 5/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 6/7",
            "HƯỚNG DẪN CHƠI  •  BƯỚC 7/7"
        };

        private static readonly string[] TutorialStepTitles =
        {
            "THANH TRƯỢT DI CHUYỂN",
            "PHÁT BÓNG CƠ BẢN (SERVE)",
            "ĐỠ BÓNG & TUNG BÓNG TẤN CÔNG (BUMP)",
            "BẬT NHẢY & ĐIỂM TREO ĐỈNH (JUMP)",
            "ĐẬP BÓNG & VUỐT ĐỔI LỰC (SPIKE)",
            "TÙY CHỈNH GIAO DIỆN (⚙ CUSTOM UI)",
            "ÂM THANH BGM / SFX & TẠM DỪNG"
        };

        private static readonly string[] TutorialStepBodies =
        {
            "Kéo sang <b>trái / phải</b> để di chuyển nhân vật trên sân (hoặc nhấn phím <color=#FFB547>A / D</color>).",
            "Đầu pha bóng khi cầm bóng ở cuối sân, bấm <color=#FFB547>SERVE</color> để giao bóng an toàn sang sân đối thủ.",
            "Trong pha bóng bấm <color=#7FD3FF>BUMP</color> để đỡ bóng bổng.\n<color=#FFB547>★ Mẹo giao bóng tấn công:</color> Lúc chuẩn bị phát bóng, <b>NHẤN GIỮ BUMP</b> để ngắm đường chấm trắng, thả ra để tung bóng cao rồi chạy đà nhảy đập!",
            "Bấm <color=#FFB547>JUMP</color> để bật nhảy. Khi đạt từ <color=#7FD3FF>80% tầm nhảy</color> trở lên nhân vật sẽ có nhịp lơ lửng nhẹ ở đỉnh — đây là lúc mở khóa cú <b>Đập bóng</b>!",
            "Đang trên không (<color=#7FD3FF>>= 80% tầm nhảy</color>) và gần bóng, <b>CHẠM</b> để đập bóng!\n<color=#FFB547>★ Mẹo đổi lực:</color> <b>NHẤN GIỮ >= 0.22s rồi KÉO NHẸ</b> trên nút Spike để chuyển giữa <b>Spike Light</b> (bỏ nhỏ), <b>Spike Medium</b> và <b>Spike Strong</b> (đập sấm sét).",
            "Kéo thả vị trí và chỉnh kích thước nút bấm / thanh trượt vừa tay bạn.",
            "Bật / tắt riêng Nhạc nền (<color=#7FD3FF>BGM</color>) và Hiệu ứng (<color=#FFB547>SFX</color>) để dễ test, hoặc mở <color=#FFB547>PAUSE</color> để chỉnh thanh âm lượng chi tiết."
        };

        private enum TutorialArrowDirection
        {
            Left = 0,
            Right = 1,
            Up = 2,
            Down = 3
        }

        private static string[] BuildPercentStrings0To100()
        {
            var table = new string[VolumePercentStepCount];
            for (int i = 0; i < VolumePercentStepCount; i++)
            {
                table[i] = i.ToString(CultureInfo.InvariantCulture) + "%";
            }
            return table;
        }

        private const int InputTraceLogSbCapacity = 256;
        private const string MetaConfigFileName = "meta_config.json";
        private const float MinValidMultiplierReality = 0.01f;
        private static readonly Regex MultiplierRealityRegex =
            new Regex("\"multiplierReality\"\\s*:\\s*([-+]?\\d*\\.?\\d+(?:[eE][-+]?\\d+)?)", RegexOptions.Compiled);

        private const float DefaultAttackServeBaseA = -13.0f;
        private const float DefaultAttackServeMaxA = -9.5f;
        private const float DefaultAttackServeBaseC = 425.0f;
        private const float DefaultAttackServeMaxC = 595.0f;
        private const float DefaultAttackServeTotalStep = 60.0f;
        private const float DefaultAttackServeTossV0 = 56.0f;

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
        [SerializeField, HideInInspector] private bool showInputTraceHud = false;
        [SerializeField] private bool logInputTraceToConsole = true;
        [SerializeField] private bool includeTestInjectionInTrace = false;

        [Header("Attack Serve Toss Trajectory Config (Live C++ RAM)")]
        [Tooltip("Live C++ RAM address of SimAttackServeConfig returned by Sim_GetAttackServeConfigPtr().")]
        [SerializeField] private string cppRamPointerAddress = "0x0";
        [Tooltip("Starting parabola coefficient a (tick 0 of hold) in C++ RAM.")]
        [SerializeField] private float attackServeBaseA = DefaultAttackServeBaseA;
        [Tooltip("Maximum parabola coefficient a (at max hold) in C++ RAM.")]
        [SerializeField] private float attackServeMaxA = DefaultAttackServeMaxA;
        [Tooltip("Starting parabola apex height c (tick 0 of hold) in C++ RAM.")]
        [SerializeField] private float attackServeBaseC = DefaultAttackServeBaseC;
        [Tooltip("Maximum parabola apex height c (at max hold) in C++ RAM.")]
        [SerializeField] private float attackServeMaxC = DefaultAttackServeMaxC;
        [Tooltip("Total hold steps (ticks) from baseA/baseC to maxA/maxC while holding Receive/Bump in C++ RAM.")]
        [SerializeField] private float attackServeTotalStep = DefaultAttackServeTotalStep;
        [Tooltip("Vertical toss velocity v0 when releasing Receive/Bump in C++ RAM.")]
        [SerializeField] private float attackServeTossV0 = DefaultAttackServeTossV0;
        [SerializeField, HideInInspector] private bool attackServeConfigInitializedFromCpp = false;

        private static bool hasSessionAttackServeConfig;
        private static NativeAttackServeConfig sessionAttackServeConfig = NativeAttackServeConfig.Default;
        private NativeAttackServeConfig lastSyncedInspectorAttackServeConfig;
        private bool hasLastSyncedInspectorAttackServeConfig;
        private bool isApplyingExternalRamConfig;
        private IntPtr lastCachedAttackServeRamPtr;

        private readonly StringBuilder inputTraceLogSb = new StringBuilder(InputTraceLogSbCapacity);

        private RunState state = RunState.Starting;
        private string fatalError;
        private bool nativeInitialized;
        private bool matchStarted;
        private bool isPaused;
        private bool manualTickMode;
        private bool fallbackBgmMuted;
        private bool fallbackSfxMuted;
        private float fallbackBgmVolume = DefaultFallbackBgmVolume;
        private float fallbackSfxVolume = DefaultFallbackSfxVolume;
        private bool hasPendingFallbackAudioSync;
        private bool hasInitializedFromAudioPresenter;
        private bool isTutorialActive;
        private bool tutorialPendingAutoOpen;
        private bool hasAutoShownTutorialThisPlaySession;
        private int tutorialAutoOpenEarliestFrame;
        private int tutorialStepIndex;
        private Rect lastUtilityAudioPauseClusterRect;
        private GraphicRaycaster cachedHudGraphicRaycaster;
        private bool disabledHudGraphicRaycasterForTutorial;
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
        public bool ManualTickMode
        {
            get => manualTickMode;
            set
            {
                manualTickMode = value;
                if (manualTickMode && (isTutorialActive || tutorialPendingAutoOpen))
                {
                    tutorialPendingAutoOpen = false;
                    DismissTutorial();
                }
                SyncUnityTimeScaleWithPauseState();
            }
        }
        public bool ShowHud { get => showHud; set => showHud = value; }
        public bool ShowInputTraceHud => showInputTraceHud;
        public bool MatchStarted => matchStarted;
        public bool IsPaused => isPaused || isTutorialActive;
        public bool IsTutorialActive => isTutorialActive;
        public int CurrentTutorialStepIndex => tutorialStepIndex;
        public bool IsBgmMuted => ResolveIsBgmMuted();
        public bool IsSfxMuted => ResolveIsSfxMuted();
        public float BgmVolume => ResolveBgmVolume();
        public float SfxVolume => ResolveSfxVolume();
        public bool AudioMuted => IsBgmMuted && IsSfxMuted;

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
            hasSessionAttackServeConfig = false;
            sessionAttackServeConfig = NativeAttackServeConfig.Default;
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
            isPaused = false;
            isTutorialActive = false;
            tutorialPendingAutoOpen = false;
            hasAutoShownTutorialThisPlaySession = false;
            tutorialStepIndex = 0;
            hasInitializedFromAudioPresenter = false;
            hasPendingFallbackAudioSync = false;
            cachedHudGraphicRaycaster = null;
            disabledHudGraphicRaycasterForTutorial = false;

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

            string metaStreamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, MetaConfigFileName);
            bool isPackagedStreamingPath = metaStreamingPath.Contains("://");
            bool hasAndroidMultiplier = false;
            float parsedMultiplier = 0f;

            if (isPackagedStreamingPath)
            {
                using (UnityWebRequest req = UnityWebRequest.Get(metaStreamingPath))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        string metaJson = req.downloadHandler.text;
                        if (!string.IsNullOrEmpty(metaJson))
                        {
                            try
                            {
                                string persistentMetaPath = System.IO.Path.Combine(Application.persistentDataPath, MetaConfigFileName);
                                System.IO.File.WriteAllText(persistentMetaPath, metaJson);
                            }
                            catch { }

                            hasAndroidMultiplier = TryParseMultiplierReality(metaJson, out parsedMultiplier);
                        }
                    }
                }
            }

            bool deferStep = isPackagedStreamingPath && hasAndroidMultiplier;
            CompleteNativeInitialization(mapping, deferInitialStep: deferStep);
            if (state == RunState.Running && deferStep)
            {
                SpikeNativeBridge.SetMultiplierReality(parsedMultiplier);
                SpikeNativeBridge.Sim_Reset();
                InitializeAndSyncAttackServeConfigWithCppRam();
                StepOnce();
                ScheduleStartOfGameTutorialIfEligible();
            }
        }

        private static bool TryParseMultiplierReality(string json, out float multiplier)
        {
            multiplier = 0f;
            if (string.IsNullOrWhiteSpace(json)) return false;
            Match match = MultiplierRealityRegex.Match(json);
            if (!match.Success) return false;
            if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                return false;
            if (float.IsNaN(val) || float.IsInfinity(val) || val <= MinValidMultiplierReality)
                return false;
            multiplier = val;
            return true;
        }

        private void CompleteNativeInitialization(ButtonMappingConfig mapping, bool deferInitialStep = false)
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
            state = RunState.Running;
            matchStarted = true;
            InitializeAndSyncAttackServeConfigWithCppRam();
            if (!deferInitialStep)
            {
                // Commit one baseline snapshot so court entities render behind the pre-match lobby and DataTable is initialized immediately.
                StepOnce();
                ScheduleStartOfGameTutorialIfEligible();
            }
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
            ScheduleStartOfGameTutorialIfEligible();
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
            if (isTutorialActive)
            {
                DismissTutorial();
                return;
            }
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                UIButtonInputTest.SetLayoutEditMode(false);
            }
            isPaused = !isPaused;
            SyncUnityTimeScaleWithPauseState();
        }

        public void OpenTutorial()
        {
            if (!Application.isPlaying || ManualTickMode || Application.isBatchMode)
            {
                tutorialPendingAutoOpen = false;
                isTutorialActive = false;
                return;
            }
            tutorialPendingAutoOpen = false;
            isPaused = false;
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                UIButtonInputTest.SetLayoutEditMode(false);
            }
            tutorialStepIndex = 0;
            isTutorialActive = true;
            if (inputAdapter != null)
            {
                inputAdapter.ClearInjected();
            }
            SetGameplayHudRaycastBlocked(true);
            SyncUnityTimeScaleWithPauseState();
        }

        public void DismissTutorial()
        {
            tutorialPendingAutoOpen = false;
            isTutorialActive = false;
            SetGameplayHudRaycastBlocked(false);
            SyncUnityTimeScaleWithPauseState();
        }

        private void ScheduleStartOfGameTutorialIfEligible()
        {
            if (hasAutoShownTutorialThisPlaySession) return;
            hasAutoShownTutorialThisPlaySession = true;
            if (!Application.isPlaying || ManualTickMode || Application.isBatchMode || !matchStarted)
            {
                tutorialPendingAutoOpen = false;
                isTutorialActive = false;
                return;
            }
            tutorialStepIndex = 0;
            tutorialPendingAutoOpen = true;
            tutorialAutoOpenEarliestFrame = Time.frameCount + 1;
            SyncUnityTimeScaleWithPauseState();
        }

        private void SetGameplayHudRaycastBlocked(bool blocked)
        {
            if (blocked)
            {
                if (cachedHudGraphicRaycaster == null && ActionButtonLayoutController.Instance != null)
                {
                    Canvas hudCanvas = ActionButtonLayoutController.Instance.GetComponentInParent<Canvas>();
                    if (hudCanvas != null)
                    {
                        cachedHudGraphicRaycaster = hudCanvas.GetComponent<GraphicRaycaster>();
                    }
                }
                if (cachedHudGraphicRaycaster != null && cachedHudGraphicRaycaster.enabled)
                {
                    cachedHudGraphicRaycaster.enabled = false;
                    disabledHudGraphicRaycasterForTutorial = true;
                }
            }
            else
            {
                if (disabledHudGraphicRaycasterForTutorial && cachedHudGraphicRaycaster != null)
                {
                    cachedHudGraphicRaycaster.enabled = true;
                }
                disabledHudGraphicRaycasterForTutorial = false;
            }
        }

        private void SyncUnityTimeScaleWithPauseState()
        {
            if (!Application.isPlaying) return;
            bool shouldPauseTime = !ManualTickMode && state == RunState.Running &&
                                   (isPaused || isTutorialActive || tutorialPendingAutoOpen || UIButtonInputTest.IsLayoutEditMode);
            if (shouldPauseTime)
            {
                if (Time.timeScale > 0f) Time.timeScale = 0f;
            }
            else if (Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
        }

        private void SyncAudioPresenterIfReady()
        {
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter == null) return;

            if (hasPendingFallbackAudioSync)
            {
                presenter.SetBgmVolume(fallbackBgmVolume);
                presenter.SetBgmMuted(fallbackBgmMuted);
                presenter.SetSfxVolume(fallbackSfxVolume);
                presenter.SetSfxMuted(fallbackSfxMuted);
                hasPendingFallbackAudioSync = false;
                hasInitializedFromAudioPresenter = true;
                return;
            }

            if (!hasInitializedFromAudioPresenter)
            {
                fallbackBgmMuted = presenter.IsBgmMuted;
                fallbackBgmVolume = presenter.BgmVolume;
                fallbackSfxMuted = presenter.IsSfxMuted;
                fallbackSfxVolume = presenter.SfxVolume;
                hasInitializedFromAudioPresenter = true;
            }
        }

        private bool ResolveIsBgmMuted()
        {
            SyncAudioPresenterIfReady();
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                fallbackBgmMuted = presenter.IsBgmMuted;
            }
            return fallbackBgmMuted;
        }

        private bool ResolveIsSfxMuted()
        {
            SyncAudioPresenterIfReady();
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                fallbackSfxMuted = presenter.IsSfxMuted;
            }
            return fallbackSfxMuted;
        }

        private float ResolveBgmVolume()
        {
            SyncAudioPresenterIfReady();
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                fallbackBgmVolume = presenter.BgmVolume;
            }
            return fallbackBgmVolume;
        }

        private float ResolveSfxVolume()
        {
            SyncAudioPresenterIfReady();
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                fallbackSfxVolume = presenter.SfxVolume;
            }
            return fallbackSfxVolume;
        }

        public void SetBgmMuted(bool muted)
        {
            SyncAudioPresenterIfReady();
            fallbackBgmMuted = muted;
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                presenter.SetBgmMuted(muted);
                hasInitializedFromAudioPresenter = true;
            }
            else
            {
                hasPendingFallbackAudioSync = true;
            }
        }

        public void ToggleBgmMuted()
        {
            SetBgmMuted(!ResolveIsBgmMuted());
        }

        public void SetBgmVolume(float volume01)
        {
            SyncAudioPresenterIfReady();
            float clamped = Mathf.Clamp01(volume01);
            fallbackBgmVolume = clamped;
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                presenter.SetBgmVolume(clamped);
                hasInitializedFromAudioPresenter = true;
            }
            else
            {
                hasPendingFallbackAudioSync = true;
            }
        }

        public void SetSfxMuted(bool muted)
        {
            SyncAudioPresenterIfReady();
            fallbackSfxMuted = muted;
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                presenter.SetSfxMuted(muted);
                hasInitializedFromAudioPresenter = true;
            }
            else
            {
                hasPendingFallbackAudioSync = true;
            }
        }

        public void ToggleSfxMuted()
        {
            SetSfxMuted(!ResolveIsSfxMuted());
        }

        public void SetSfxVolume(float volume01)
        {
            SyncAudioPresenterIfReady();
            float clamped = Mathf.Clamp01(volume01);
            fallbackSfxVolume = clamped;
            BallFeedbackPresenter presenter = BallFeedbackPresenter.Instance;
            if (presenter != null)
            {
                presenter.SetSfxVolume(clamped);
                hasInitializedFromAudioPresenter = true;
            }
            else
            {
                hasPendingFallbackAudioSync = true;
            }
        }

        public void ToggleAudioMute()
        {
            bool shouldMuteBoth = !ResolveIsBgmMuted() || !ResolveIsSfxMuted();
            SetBgmMuted(shouldMuteBoth);
            SetSfxMuted(shouldMuteBoth);
        }

        private void FixedUpdate()
        {
            if (state != RunState.Running || ManualTickMode || !matchStarted ||
                isPaused || isTutorialActive || tutorialPendingAutoOpen || UIButtonInputTest.IsLayoutEditMode)
            {
                return;
            }
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
            if (Application.isPlaying && !isPaused && !isTutorialActive && !tutorialPendingAutoOpen &&
                !UIButtonInputTest.IsLayoutEditMode && Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
            if (!nativeInitialized) return;
            SpikeNativeBridge.Sim_Reset();
            PushInspectorAttackServeConfigToCppRamIfChanged();
            dataTable.ResetTable();
            hudSnapshotReady = false;
            inputAdapter.ClearInjected();
            consecutiveRejects = 0;
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
            SyncAudioPresenterIfReady();
            PushInspectorAttackServeConfigToCppRamIfChanged();

            if (ManualTickMode || Application.isBatchMode)
            {
                if (tutorialPendingAutoOpen || isTutorialActive)
                {
                    tutorialPendingAutoOpen = false;
                    DismissTutorial();
                }
            }
            else if (tutorialPendingAutoOpen && matchStarted && state == RunState.Running)
            {
                bool layoutReady = ActionButtonLayoutController.Instance != null ||
                                   Time.frameCount >= tutorialAutoOpenEarliestFrame + TutorialMaxWaitFramesForHudController;
                if (Time.frameCount >= tutorialAutoOpenEarliestFrame && layoutReady)
                {
                    OpenTutorial();
                }
            }

            if (isTutorialActive && !disabledHudGraphicRaycasterForTutorial && ActionButtonLayoutController.Instance != null)
            {
                SetGameplayHudRaycastBlocked(true);
            }

            SyncUnityTimeScaleWithPauseState();

            if (isTutorialActive)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    DismissTutorial();
                }
                return;
            }

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

        private NativeAttackServeConfig GetInspectorAttackServeConfig()
        {
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
            IntPtr ptr = SpikeNativeBridge.GetAttackServeConfigRamPtr();
            CacheAttackServeRamPointerHex(ptr);

            if (SpikeNativeBridge.TryReadAttackServeConfigFromRam(out NativeAttackServeConfig cppCfg))
            {
                ApplyAttackServeConfigToInspectorFields(in cppCfg, ptr);
                sessionAttackServeConfig = cppCfg;
                hasSessionAttackServeConfig = true;
                attackServeConfigInitializedFromCpp = true;
                return;
            }

            NativeAttackServeConfig inspectorCfg = GetInspectorAttackServeConfig();
            ApplyAttackServeConfigToInspectorFields(in inspectorCfg, ptr);
            sessionAttackServeConfig = inspectorCfg;
            hasSessionAttackServeConfig = true;
            attackServeConfigInitializedFromCpp = true;
        }

        private void PushInspectorAttackServeConfigToCppRamIfChanged()
        {
            if (isApplyingExternalRamConfig || !hasLastSyncedInspectorAttackServeConfig) return;
            IntPtr ptr = SpikeNativeBridge.GetAttackServeConfigRamPtr();
            CacheAttackServeRamPointerHex(ptr);

            NativeAttackServeConfig currentCfg = GetInspectorAttackServeConfig();
            if (NativeAttackServeConfig.AreEqual(in currentCfg, in lastSyncedInspectorAttackServeConfig)) return;

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
            if (!Application.isPlaying || !nativeInitialized || state != RunState.Running || !hasLastSyncedInspectorAttackServeConfig) return;

            PushInspectorAttackServeConfigToCppRamIfChanged();
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
        private GUIStyle pauseTitleStyle, pauseSubStyle, pauseMixerLabelStyle, pauseMixerPercentStyle, pauseActionBtnStyle, pauseStartBtnStyle;
        private GUIStyle tutorialCardBoxStyle, tutorialBadgeStyle, tutorialTitleStyle, tutorialBodyStyle;
        private GUIStyle tutorialNavBtnStyle, tutorialPrimaryBtnStyle, tutorialSkipBtnStyle, tutorialArrowStyle;
        private Texture2D panelTex, errorTex, accentTex, playerTex, opponentTex, startBtnTex, lobbyBackdropTex, utilityBtnTex;
        private Texture2D tutorialDimTex, tutorialCardTex, tutorialGlowTex, tutorialDividerTex, tutorialDotInactiveTex, tutorialSkipBtnTex;
        private Camera mainCamCached;
        private float hudStyleScale = -1f;
        private float pauseModalStyleScale = -1f;
        private float tutorialStyleScale = -1f;
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
            panelTex = MakeTex(panelColor);
            errorTex = MakeTex(new Color(0.55f, 0.06f, 0.10f, 0.92f));
            accentTex = MakeTex(accentColor);
            playerTex = MakeTex(playerColor);
            opponentTex = MakeTex(opponentColor);
            startBtnTex = MakeTex(startBtnColor);
            lobbyBackdropTex = MakeTex(lobbyBgColor);
            utilityBtnTex = MakeTex(new Color(0.09f, 0.14f, 0.24f, 0.92f));
            tutorialDimTex = MakeTex(new Color(0f, 0f, 0f, TutorialBackdropAlpha));
            tutorialCardTex = MakeTex(new Color(0.05f, 0.09f, 0.16f, 0.98f));
            tutorialGlowTex = MakeTex(new Color(0.38f, 0.86f, 1.00f, 0.42f));
            tutorialDividerTex = MakeTex(new Color(0.26f, 0.40f, 0.58f, 0.45f));
            tutorialDotInactiveTex = MakeTex(new Color(0.26f, 0.34f, 0.48f, 0.85f));
            tutorialSkipBtnTex = MakeTex(new Color(0.16f, 0.20f, 0.30f, 0.96f));
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
            Color hoverDarkText = new Color(0.06f, 0.09f, 0.15f, 1f);
            startBtnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            startBtnStyle.normal.background = startBtnTex;
            startBtnStyle.normal.textColor = Color.white;
            startBtnStyle.hover.background = accentTex;
            startBtnStyle.hover.textColor = hoverDarkText;
            startBtnStyle.active.background = accentTex;
            startBtnStyle.active.textColor = hoverDarkText;
            utilityBtnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            utilityBtnStyle.normal.background = utilityBtnTex;
            utilityBtnStyle.normal.textColor = new Color(0.92f, 0.96f, 1f, 1f);
            utilityBtnStyle.hover.background = accentTex;
            utilityBtnStyle.hover.textColor = hoverDarkText;
            utilityBtnStyle.active.background = accentTex;
            utilityBtnStyle.active.textColor = hoverDarkText;
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

            pauseTitleStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            pauseTitleStyle.normal.textColor = Color.white;
            pauseSubStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            pauseSubStyle.normal.textColor = new Color(0.88f, 0.93f, 1f);
            pauseMixerLabelStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            pauseMixerLabelStyle.normal.textColor = new Color(0.94f, 0.97f, 1f);
            pauseMixerPercentStyle = new GUIStyle { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            pauseMixerPercentStyle.normal.textColor = accentColor;
            pauseActionBtnStyle = new GUIStyle(utilityBtnStyle);
            pauseStartBtnStyle = new GUIStyle(startBtnStyle);

            tutorialCardBoxStyle = new GUIStyle();
            tutorialCardBoxStyle.normal.background = tutorialCardTex;
            tutorialBadgeStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            tutorialBadgeStyle.normal.textColor = new Color(0.50f, 0.83f, 1.00f, 1f);
            tutorialTitleStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            tutorialTitleStyle.normal.textColor = accentColor;
            tutorialBodyStyle = new GUIStyle { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Normal, wordWrap = true, richText = true };
            tutorialBodyStyle.normal.textColor = new Color(0.93f, 0.96f, 1.00f, 1f);
            tutorialNavBtnStyle = new GUIStyle(utilityBtnStyle);
            tutorialPrimaryBtnStyle = new GUIStyle(startBtnStyle);
            tutorialSkipBtnStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            tutorialSkipBtnStyle.normal.background = tutorialSkipBtnTex;
            tutorialSkipBtnStyle.normal.textColor = Color.white;
            tutorialSkipBtnStyle.hover.background = accentTex;
            tutorialSkipBtnStyle.hover.textColor = hoverDarkText;
            tutorialSkipBtnStyle.active.background = accentTex;
            tutorialSkipBtnStyle.active.textColor = hoverDarkText;
            tutorialArrowStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            tutorialArrowStyle.normal.background = accentTex;
            tutorialArrowStyle.normal.textColor = hoverDarkText;

            hudStyleScale = -1f;
            pauseModalStyleScale = -1f;
            tutorialStyleScale = -1f;
        }

        private void OnDisable()
        {
            SpikeNativeBridge.OnAttackServeConfigRamChanged -= HandleExternalAttackServeConfigRamChanged;
            SetGameplayHudRaycastBlocked(false);
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
            if (tutorialDimTex != null) Destroy(tutorialDimTex);
            if (tutorialCardTex != null) Destroy(tutorialCardTex);
            if (tutorialGlowTex != null) Destroy(tutorialGlowTex);
            if (tutorialDividerTex != null) Destroy(tutorialDividerTex);
            if (tutorialDotInactiveTex != null) Destroy(tutorialDotInactiveTex);
            if (tutorialSkipBtnTex != null) Destroy(tutorialSkipBtnTex);
            panelTex = errorTex = accentTex = playerTex = opponentTex = startBtnTex = lobbyBackdropTex = utilityBtnTex = null;
            tutorialDimTex = tutorialCardTex = tutorialGlowTex = tutorialDividerTex = tutorialDotInactiveTex = tutorialSkipBtnTex = null;
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

        private void EnsurePauseModalStyles(float mk)
        {
            if (Mathf.Approximately(pauseModalStyleScale, mk)) return;
            pauseModalStyleScale = mk;
            pauseTitleStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(LobbyTitleFontSize * mk));
            pauseSubStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(HudSmallFontSize * mk));
            pauseMixerLabelStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(PauseMixerFontSize * mk));
            pauseMixerPercentStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(PauseMixerFontSize * mk));
            pauseActionBtnStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(PauseActionBtnFontSize * mk));
            pauseStartBtnStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(LobbyButtonFontSize * 0.82f * mk));
        }

        private void EnsureTutorialStyles(float tk)
        {
            if (Mathf.Approximately(tutorialStyleScale, tk)) return;
            tutorialStyleScale = tk;
            tutorialBadgeStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(TutorialBadgeFontSize * tk));
            tutorialTitleStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(TutorialTitleFontSize * tk));
            tutorialBodyStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(TutorialBodyFontSize * tk));
            tutorialNavBtnStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(TutorialNavBtnFontSize * tk));
            tutorialPrimaryBtnStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(TutorialNavBtnFontSize * tk));
            tutorialSkipBtnStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(TutorialSkipBtnFontSize * tk));
            tutorialArrowStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(TutorialArrowFontSize * tk));
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

            // Top-right Broadcast Utility Bar (? GUIDE, BGM, SFX, PAUSE) — clamped to never overlap panel.xMax
            DrawTopRightUtilityBar(safe, safeTop, margin, panel.xMax);

            // World-space HUD overlays: "▼ YOU" controlled-player indicator & floor landing target reticle
            if (matchStarted && !isPaused && !isTutorialActive && !UIButtonInputTest.IsLayoutEditMode)
            {
                DrawWorldTrackingIndicators(k);
            }

            // Point Scored Broadcast Banner + Spike Speedometer (KM/H) — hidden while Custom UI Dock is open so they never overlap
            if (matchStarted && !isTutorialActive && !UIButtonInputTest.IsLayoutEditMode &&
                cachedBallStateFrame > 0 && !cachedFinished && !string.IsNullOrEmpty(pointBannerText))
            {
                float bw = PointBannerWidth * k;
                float bh = PointBannerHeight * k;
                var bannerRect = new Rect(safe.center.x - bw * 0.5f, panel.yMax + 8f * k, bw, bh);
                GUI.DrawTexture(new Rect(bannerRect.x, bannerRect.y, bw, Mathf.Max(2f, 3f * k)), accentTex);
                GUI.Label(bannerRect, pointBannerText, pointBannerStyle);
            }

            if (cachedFinished && !isTutorialActive && !UIButtonInputTest.IsLayoutEditMode)
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
            else if (isPaused && !isTutorialActive && matchStarted && state == RunState.Running && !UIButtonInputTest.IsLayoutEditMode)
            {
                DrawPauseModalOverlay(safe, k);
            }

            if (isTutorialActive && state == RunState.Running && !ManualTickMode && !Application.isBatchMode)
            {
                DrawTutorialSpotlightOverlay(safe, safeTop, margin, k);
            }
        }

        private void RecordUpperLayerInputTrace(ulong currentTick)
        {
            if (!logInputTraceToConsole || inputAdapter == null) return;
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
                    inputTraceLogSb.Clear();
                    inputTraceLogSb.Append("[INPUT_TRACE] tick=");
                    inputTraceLogSb.Append(currentTick);
                }

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

            if (hasMatchedAny)
            {
                Debug.Log(inputTraceLogSb.ToString());
            }
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

        private void ComputeTopRightUtilityBarLayout(
            Rect safe,
            float safeTop,
            float margin,
            float scoreboardRightX,
            out float topBarScale,
            out float horizontalFit,
            out Rect guideRect,
            out Rect bgmRect,
            out Rect sfxRect,
            out Rect pauseRect)
        {
            float rawScale = Mathf.Min(
                Mathf.Max(1f, safe.width) / TopUtilityReferenceWidth,
                Mathf.Max(1f, Screen.height) / TopUtilityReferenceHeight);
            int gapCount = matchStarted ? 3 : 2;
            float totalBaseWidth = TopUtilityGuideBtnWidth + TopUtilityAudioBtnWidth * 2f +
                                   (matchStarted ? TopUtilityPauseBtnWidth : 0f) +
                                   TopUtilityBtnGap * gapCount;
            float availableRightSpan = Mathf.Max(60f, safe.xMax - scoreboardRightX - TopUtilityScoreboardClearance);
            float fitRightScale = availableRightSpan / (totalBaseWidth + TopUtilityRightMargin);
            topBarScale = Mathf.Clamp(Mathf.Min(rawScale, fitRightScale), TopUtilityMinScale, TopUtilityMaxScale);

            float unpressedSpan = (totalBaseWidth + TopUtilityRightMargin) * topBarScale;
            horizontalFit = unpressedSpan > availableRightSpan
                ? availableRightSpan / Mathf.Max(1f, unpressedSpan)
                : 1f;

            float pauseW = TopUtilityPauseBtnWidth * topBarScale * horizontalFit;
            float audioW = TopUtilityAudioBtnWidth * topBarScale * horizontalFit;
            float guideW = TopUtilityGuideBtnWidth * topBarScale * horizontalFit;
            float btnH = TopUtilityBtnHeight * topBarScale;
            float gap = TopUtilityBtnGap * topBarScale * horizontalFit;
            float rightEdge = safe.xMax - TopUtilityRightMargin * topBarScale * horizontalFit;
            float topY = safeTop + Mathf.Max(margin, TopUtilityTopAnchorY * topBarScale - btnH * 0.5f);

            if (matchStarted)
            {
                pauseRect = new Rect(rightEdge - pauseW, topY, pauseW, btnH);
                sfxRect = new Rect(pauseRect.x - gap - audioW, topY, audioW, btnH);
            }
            else
            {
                pauseRect = new Rect(rightEdge, topY, 0f, btnH);
                sfxRect = new Rect(rightEdge - audioW, topY, audioW, btnH);
            }
            bgmRect = new Rect(sfxRect.x - gap - audioW, topY, audioW, btnH);
            guideRect = new Rect(bgmRect.x - gap - guideW, topY, guideW, btnH);
        }

        private void DrawTopRightUtilityBar(Rect safe, float safeTop, float margin, float scoreboardRightX)
        {
            ComputeTopRightUtilityBarLayout(
                safe,
                safeTop,
                margin,
                scoreboardRightX,
                out float topBarScale,
                out float horizontalFit,
                out Rect guideRect,
                out Rect bgmRect,
                out Rect sfxRect,
                out Rect pauseRect);

            utilityBtnStyle.fontSize = Mathf.Max(
                TopUtilityMinFontSize,
                Mathf.RoundToInt(TopUtilityBtnFontSize * topBarScale * horizontalFit));

            float clusterRight = matchStarted ? pauseRect.xMax : sfxRect.xMax;
            lastUtilityAudioPauseClusterRect = Rect.MinMaxRect(bgmRect.xMin, bgmRect.yMin, clusterRight, bgmRect.yMax);

            float accentH = Mathf.Max(2f, 3f * topBarScale);
            bool bgmMuted = ResolveIsBgmMuted();
            bool sfxMuted = ResolveIsSfxMuted();

            GUI.DrawTexture(new Rect(guideRect.x, guideRect.y, guideRect.width, accentH), accentTex);
            if (GUI.Button(guideRect, TopUtilityGuideLabel, utilityBtnStyle) && !isTutorialActive)
            {
                OpenTutorial();
            }

            GUI.DrawTexture(new Rect(bgmRect.x, bgmRect.y, bgmRect.width, accentH), accentTex);
            string bgmLabel = bgmMuted ? TopUtilityBgmOffLabel : TopUtilityBgmOnLabel;
            if (GUI.Button(bgmRect, bgmLabel, utilityBtnStyle) && !isTutorialActive)
            {
                ToggleBgmMuted();
            }

            GUI.DrawTexture(new Rect(sfxRect.x, sfxRect.y, sfxRect.width, accentH), accentTex);
            string sfxLabel = sfxMuted ? TopUtilitySfxOffLabel : TopUtilitySfxOnLabel;
            if (GUI.Button(sfxRect, sfxLabel, utilityBtnStyle) && !isTutorialActive)
            {
                ToggleSfxMuted();
            }

            if (matchStarted)
            {
                GUI.DrawTexture(new Rect(pauseRect.x, pauseRect.y, pauseRect.width, accentH), accentTex);
                string pauseLabel = isPaused ? TopUtilityResumeLabel : TopUtilityPauseLabel;
                if (GUI.Button(pauseRect, pauseLabel, utilityBtnStyle) && !isTutorialActive)
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
            float mk = Mathf.Min(
                k,
                (safe.width * PauseModalMaxSafeWidthFraction) / PauseModalWidth,
                (safe.height * PauseModalMaxSafeHeightFraction) / PauseModalHeight);
            if (mk <= 0f) return;
            EnsurePauseModalStyles(mk);

            float cardW = PauseModalWidth * mk;
            float cardH = PauseModalHeight * mk;
            var card = new Rect(safe.center.x - cardW * 0.5f, safe.center.y - cardH * 0.5f, cardW, cardH);

            GUI.Box(card, GUIContent.none, lobbyBackdropStyle);
            GUI.DrawTexture(new Rect(card.x, card.y, cardW, Mathf.Max(3f, 4f * mk)), accentTex);
            float bottomBarH = Mathf.Max(2f, 3f * mk);
            GUI.DrawTexture(new Rect(card.x, card.yMax - bottomBarH, cardW, bottomBarH), accentTex);

            GUI.Label(new Rect(card.x, card.y + 12f * mk, cardW, 30f * mk),
                "MATCH <color=#FFB547>PAUSED</color>", pauseTitleStyle);
            GUI.Label(new Rect(card.x, card.y + 42f * mk, cardW, 18f * mk),
                "<color=#7FD3FF>AUDIO MIXER & GAMEPLAY OPTIONS</color>", pauseSubStyle);

            DrawPauseAudioChannelRow(
                card,
                PauseMixerBgmRowTop * mk,
                mk,
                PauseBgmRowLabel,
                ResolveBgmVolume(),
                ResolveIsBgmMuted(),
                isBgmChannel: true);

            DrawPauseAudioChannelRow(
                card,
                PauseMixerSfxRowTop * mk,
                mk,
                PauseSfxRowLabel,
                ResolveSfxVolume(),
                ResolveIsSfxMuted(),
                isBgmChannel: false);

            float dividerInset = PauseMixerInsetX * mk;
            GUI.DrawTexture(
                new Rect(card.x + dividerInset, card.y + PauseDividerTop * mk, cardW - dividerInset * 2f, Mathf.Max(1f, PauseDividerHeight * mk)),
                tutorialDividerTex);

            float btnW = PauseActionBtnWidth * mk;
            float btnH = PauseActionBtnHeight * mk;
            float startY = card.y + PauseActionStartTop * mk;
            float stepY = PauseActionStepY * mk;

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY, btnW, btnH), PauseResumeBtnLabel, pauseStartBtnStyle))
            {
                isPaused = false;
                SyncUnityTimeScaleWithPauseState();
            }

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY, btnW, btnH), PauseGuideBtnLabel, pauseActionBtnStyle))
            {
                isPaused = false;
                OpenTutorial();
            }

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY * 2f, btnW, btnH), PauseCustomHudBtnLabel, pauseActionBtnStyle))
            {
                isPaused = false;
                UIButtonInputTest.SetLayoutEditMode(true);
                SyncUnityTimeScaleWithPauseState();
            }

            if (GUI.Button(new Rect(card.center.x - btnW * 0.5f, startY + stepY * 3f, btnW, btnH), PauseRestartBtnLabel, pauseActionBtnStyle))
            {
                isPaused = false;
                ResetMatch();
                StepOnce();
            }
        }

        private void DrawPauseAudioChannelRow(
            Rect card,
            float localRowTop,
            float mk,
            string rowLabel,
            float currentVolume01,
            bool isMuted,
            bool isBgmChannel)
        {
            float rowX = card.x + PauseMixerInsetX * mk;
            float rowY = card.y + localRowTop;
            float rowH = PauseMixerRowHeight * mk;
            float innerW = card.width - PauseMixerInsetX * 2f * mk;
            float labelW = PauseMixerLabelWidth * mk;
            float pctW = PauseMixerPercentWidth * mk;
            float muteBtnW = PauseMixerMuteBtnWidth * mk;
            float muteBtnH = PauseMixerMuteBtnHeight * mk;
            float colGap = PauseMixerColGap * mk;
            float sliderW = Mathf.Max(48f * mk, innerW - labelW - pctW - muteBtnW - colGap * 3f);

            var labelRect = new Rect(rowX, rowY, labelW, rowH);
            var sliderRect = new Rect(labelRect.xMax + colGap, rowY + PauseMixerSliderOffsetY * mk, sliderW, PauseMixerSliderHeight * mk);
            var pctRect = new Rect(sliderRect.xMax + colGap, rowY, pctW, rowH);
            var muteRect = new Rect(pctRect.xMax + colGap, rowY + (rowH - muteBtnH) * 0.5f, muteBtnW, muteBtnH);

            GUI.Label(labelRect, rowLabel, pauseMixerLabelStyle);

            float nextVolume = GUI.HorizontalSlider(sliderRect, Mathf.Clamp01(currentVolume01), 0f, 1f);
            if (Mathf.Abs(nextVolume - currentVolume01) > VolumeChangeEpsilon)
            {
                if (isBgmChannel)
                {
                    SetBgmVolume(nextVolume);
                    if (isMuted && nextVolume > VolumeSliderUnmuteThreshold)
                    {
                        SetBgmMuted(false);
                        isMuted = false;
                    }
                }
                else
                {
                    SetSfxVolume(nextVolume);
                    if (isMuted && nextVolume > VolumeSliderUnmuteThreshold)
                    {
                        SetSfxMuted(false);
                        isMuted = false;
                    }
                }
            }

            int pctIndex = Mathf.Clamp(Mathf.RoundToInt((isMuted ? 0f : nextVolume) * 100f), 0, VolumePercentStepCount - 1);
            GUI.Label(pctRect, PercentStrings0To100[pctIndex], pauseMixerPercentStyle);

            string toggleLabel = isMuted ? PauseChannelOffLabel : PauseChannelOnLabel;
            if (GUI.Button(muteRect, toggleLabel, pauseActionBtnStyle))
            {
                if (isBgmChannel) ToggleBgmMuted();
                else ToggleSfxMuted();
            }
        }

        private void DrawTutorialSpotlightOverlay(Rect safe, float safeTop, float margin, float k)
        {
            int step = Mathf.Clamp(tutorialStepIndex, 0, TutorialTotalStepCount - 1);
            tutorialStepIndex = step;

            float rawTk = Mathf.Min(
                Mathf.Max(1f, safe.width) / TopUtilityReferenceWidth,
                Mathf.Max(1f, Screen.height) / TopUtilityReferenceHeight);
            float maxCardScaleW = (safe.width * TutorialCardMaxWidthFraction) / TutorialCardBaseWidth;
            float maxCardScaleH = (safe.height * TutorialCardMaxHeightFraction) / TutorialCardBaseHeight;
            float tk = Mathf.Clamp(Mathf.Min(rawTk, maxCardScaleW, maxCardScaleH), TopUtilityMinScale, TopUtilityMaxScale);
            EnsureTutorialStyles(tk);

            Rect rawSpot = ResolveTutorialRawSpotlightRect(step, safe, safeTop, margin);
            float pad = TutorialSpotlightPadding * k;
            float spotXMin = Mathf.Clamp(rawSpot.xMin - pad, 0f, Screen.width);
            float spotYMin = Mathf.Clamp(rawSpot.yMin - pad, 0f, Screen.height);
            float spotXMax = Mathf.Clamp(rawSpot.xMax + pad, spotXMin, Screen.width);
            float spotYMax = Mathf.Clamp(rawSpot.yMax + pad, spotYMin, Screen.height);
            Rect spotRect = Rect.MinMaxRect(spotXMin, spotYMin, spotXMax, spotYMax);

            // 1. 90% black opacity 4-quad cutout around spotRect (spotRect stays at 100% full brightness)
            if (spotRect.yMin > 0f)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, spotRect.yMin), tutorialDimTex);
            }
            if (spotRect.yMax < Screen.height)
            {
                GUI.DrawTexture(new Rect(0f, spotRect.yMax, Screen.width, Screen.height - spotRect.yMax), tutorialDimTex);
            }
            if (spotRect.xMin > 0f && spotRect.height > 0f)
            {
                GUI.DrawTexture(new Rect(0f, spotRect.yMin, spotRect.xMin, spotRect.height), tutorialDimTex);
            }
            if (spotRect.xMax < Screen.width && spotRect.height > 0f)
            {
                GUI.DrawTexture(new Rect(spotRect.xMax, spotRect.yMin, Screen.width - spotRect.xMax, spotRect.height), tutorialDimTex);
            }

            // 2. Crisp glowing cyan halo + 3px gold highlight border around spotRect
            float glowExpand = Mathf.Max(2f, TutorialSpotlightOuterGlowExpand * tk);
            Rect outerGlowRect = Rect.MinMaxRect(
                Mathf.Max(0f, spotRect.xMin - glowExpand),
                Mathf.Max(0f, spotRect.yMin - glowExpand),
                Mathf.Min(Screen.width, spotRect.xMax + glowExpand),
                Mathf.Min(Screen.height, spotRect.yMax + glowExpand));
            DrawRectBorder(outerGlowRect, glowExpand, tutorialGlowTex);
            float borderPx = Mathf.Max(TutorialSpotlightBorderThickness, Mathf.Round(TutorialSpotlightBorderThickness * tk));
            DrawRectBorder(spotRect, borderPx, accentTex);

            // 3. Fast top-right "BỎ QUA ✕" button (shifts cleanly below spotRect on Step 7 so it never covers spotRect)
            float skipW = TutorialSkipBtnWidth * tk;
            float skipH = TutorialSkipBtnHeight * tk;
            float skipX = safe.xMax - TutorialSkipMarginRight * tk - skipW;
            float skipY = safeTop + TutorialSkipMarginTop * tk;
            var skipRect = new Rect(skipX, skipY, skipW, skipH);
            if (skipRect.Overlaps(outerGlowRect))
            {
                skipRect.y = Mathf.Min(
                    Screen.height - skipH - TutorialCardSafeMargin * tk,
                    outerGlowRect.yMax + TutorialSkipSpotGap * tk);
            }

            // 4. Dynamic Tutorial Card positioned in the open half of the screen (never covering spotRect or skipRect)
            Rect cardRect = ComputeTutorialCardRect(spotRect, skipRect, safe, safeTop, tk, out TutorialArrowDirection arrowDir);
            DrawTutorialConnectorAndArrow(cardRect, spotRect, arrowDir, tk);

            GUI.Box(cardRect, GUIContent.none, tutorialCardBoxStyle);
            DrawRectBorder(cardRect, Mathf.Max(2f, 2f * tk), accentTex);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, Mathf.Max(3f, 4f * tk)), accentTex);

            float innerX = cardRect.x + TutorialCardInnerPadX * tk;
            float innerW = cardRect.width - TutorialCardInnerPadX * 2f * tk;

            GUI.Label(new Rect(innerX, cardRect.y + 13f * tk, innerW, 20f * tk), TutorialStepBadges[step], tutorialBadgeStyle);
            GUI.Label(new Rect(innerX, cardRect.y + 34f * tk, innerW, 28f * tk), TutorialStepTitles[step], tutorialTitleStyle);
            GUI.DrawTexture(new Rect(innerX, cardRect.y + 66f * tk, innerW, Mathf.Max(1f, tk)), tutorialDividerTex);
            GUI.Label(new Rect(innerX, cardRect.y + 75f * tk, innerW, cardRect.height - 140f * tk), TutorialStepBodies[step], tutorialBodyStyle);

            // Footer navigation: "◄ TRƯỚC", 7-step indicator pills, "TIẾP THEO ►" / "▶ BẮT ĐẦU CHƠI"
            float navH = TutorialNavBtnHeight * tk;
            float navY = cardRect.yMax - navH - 14f * tk;
            float prevW = TutorialPrevBtnWidth * tk;
            float nextW = TutorialNextBtnWidth * tk;

            if (step > 0)
            {
                var prevRect = new Rect(innerX, navY, prevW, navH);
                if (GUI.Button(prevRect, TutorialPrevBtnLabel, tutorialNavBtnStyle))
                {
                    tutorialStepIndex = step - 1;
                }
            }

            DrawTutorialStepDots(cardRect.center.x, navY + navH * 0.5f, step, tk);

            var nextRect = new Rect(cardRect.xMax - TutorialCardInnerPadX * tk - nextW, navY, nextW, navH);
            string nextLabel = step >= TutorialTotalStepCount - 1 ? TutorialStartPlayBtnLabel : TutorialNextBtnLabel;
            if (GUI.Button(nextRect, nextLabel, tutorialPrimaryBtnStyle))
            {
                if (step >= TutorialTotalStepCount - 1)
                {
                    DismissTutorial();
                }
                else
                {
                    tutorialStepIndex = step + 1;
                }
            }

            DrawRectBorder(skipRect, Mathf.Max(1f, 2f * tk), accentTex);
            if (GUI.Button(skipRect, TutorialSkipBtnLabel, tutorialSkipBtnStyle))
            {
                DismissTutorial();
            }

            // Consume any leftover mouse/touch pointer events outside tutorial buttons so clicks never leak
            Event ev = Event.current;
            if (ev != null && (ev.type == EventType.MouseDown || ev.type == EventType.MouseUp || ev.type == EventType.MouseDrag))
            {
                ev.Use();
            }
        }

        private Rect ResolveTutorialRawSpotlightRect(int stepIndex, Rect safe, float safeTop, float margin)
        {
            float uiScale = Mathf.Min(
                Mathf.Max(1f, Screen.width) / TopUtilityReferenceWidth,
                Mathf.Max(1f, Screen.height) / TopUtilityReferenceHeight);
            float safeGuiBottom = Screen.height - safe.yMin;
            float primaryD = P02ActionButtonBootstrap.PrimaryDiameterPixels * uiScale;
            float secondaryD = P02ActionButtonBootstrap.SecondaryDiameterPixels * uiScale;
            float btnGap = 14f * uiScale;
            float rightMargin = 28f * uiScale;
            float bottomMargin = 28f * uiScale;

            switch (stepIndex)
            {
                case 0: // MovementSlider
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.MovementSlider, out Rect r) && r.width > 1f)
                        return r;
                    if (ActionButtonLayoutController.Instance != null &&
                        ActionButtonLayoutController.Instance.TryGetControlScreenRect(ActionButtonLayoutController.MovementSliderControlId, out r) && r.width > 1f)
                        return r;
                    float w = FallbackSliderWidth * uiScale;
                    float h = FallbackSliderHeight * uiScale;
                    return new Rect(safe.xMin + FallbackSliderLeftMargin * uiScale, safeGuiBottom - FallbackSliderBottomMargin * uiScale - h, w, h);
                }
                case 1: // ServeButton
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.ServeButton, out Rect r) && r.width > 1f)
                        return r;
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.DedicatedSpikeButton, out r) && r.width > 1f)
                        return r;
                    return new Rect(safe.xMax - rightMargin - primaryD, safeGuiBottom - bottomMargin - primaryD, primaryD, primaryD);
                }
                case 2: // BumpButton
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.BumpButton, out Rect r) && r.width > 1f)
                        return r;
                    return new Rect(
                        safe.xMax - rightMargin - primaryD - btnGap - secondaryD,
                        safeGuiBottom - bottomMargin - secondaryD,
                        secondaryD,
                        secondaryD);
                }
                case 3: // JumpButton
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.JumpButton, out Rect r) && r.width > 1f)
                        return r;
                    return new Rect(
                        safe.xMax - rightMargin - primaryD + (primaryD - secondaryD) * 0.5f,
                        safeGuiBottom - bottomMargin - primaryD - btnGap - secondaryD,
                        secondaryD,
                        secondaryD);
                }
                case 4: // DedicatedSpikeButton
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.DedicatedSpikeButton, out Rect r) && r.width > 1f)
                        return r;
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.ServeButton, out Rect serveR) && serveR.width > 1f)
                    {
                        bool hasBump = P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.BumpButton, out Rect bumpR) && bumpR.width > 1f;
                        if (!hasBump || serveR.width >= bumpR.width * PrimaryVsSecondaryWidthRatio)
                        {
                            return serveR;
                        }
                    }
                    return new Rect(safe.xMax - rightMargin - primaryD, safeGuiBottom - bottomMargin - primaryD, primaryD, primaryD);
                }
                case 5: // CustomUiToggleButton
                {
                    if (P02ActionButtonBootstrap.TryGetTutorialTargetScreenRect(TutorialSpotlightTarget.CustomUiToggleButton, out Rect r) && r.width > 1f)
                        return r;
                    return new Rect(
                        safe.xMin + FallbackTopLeftPillX * uiScale,
                        safeTop + FallbackTopLeftPillY * uiScale,
                        FallbackTopLeftPillWidth * uiScale,
                        FallbackTopLeftPillHeight * uiScale);
                }
                case TutorialUtilityBarStepIndex:
                default: // Step 7 (index 6): Top-Right BGM + SFX + PAUSE utility bar Rect
                {
                    if (lastUtilityAudioPauseClusterRect.width > 1f && lastUtilityAudioPauseClusterRect.height > 1f)
                        return lastUtilityAudioPauseClusterRect;
                    ComputeTopRightUtilityBarLayout(
                        safe,
                        safeTop,
                        margin,
                        safe.center.x + HudWidth * 0.5f,
                        out _,
                        out _,
                        out _,
                        out Rect bgmRect,
                        out Rect sfxRect,
                        out Rect pauseRect);
                    float rightMax = matchStarted ? pauseRect.xMax : sfxRect.xMax;
                    return Rect.MinMaxRect(bgmRect.xMin, bgmRect.yMin, rightMax, bgmRect.yMax);
                }
            }
        }

        private static Rect ComputeTutorialCardRect(
            Rect spotRect,
            Rect skipRect,
            Rect safe,
            float safeTop,
            float tk,
            out TutorialArrowDirection arrowDir)
        {
            float safeMargin = TutorialCardSafeMargin * tk;
            float minX = safe.xMin + safeMargin;
            float maxX = safe.xMax - safeMargin;
            float minY = safeTop + safeMargin;
            float maxY = Screen.height - safe.yMin - safeMargin;

            float cardW = Mathf.Min(TutorialCardBaseWidth * tk, Mathf.Max(220f, maxX - minX));
            float cardH = Mathf.Min(TutorialCardBaseHeight * tk, Mathf.Max(160f, maxY - minY));
            float preferredGap = TutorialConnectorGap * tk;
            float minGap = TutorialConnectorMinGap * tk;

            float spaceLeft = spotRect.xMin - minX;
            float spaceRight = maxX - spotRect.xMax;
            float spaceAbove = spotRect.yMin - minY;
            float spaceBelow = maxY - spotRect.yMax;

            Rect card;
            if (Mathf.Max(spaceLeft, spaceRight) >= cardW + minGap)
            {
                if (spaceLeft >= spaceRight)
                {
                    float x = Mathf.Clamp(spotRect.xMin - preferredGap - cardW, minX, spotRect.xMin - minGap - cardW);
                    float y = Mathf.Clamp(spotRect.center.y - cardH * 0.5f, minY, maxY - cardH);
                    card = new Rect(x, y, cardW, cardH);
                    arrowDir = TutorialArrowDirection.Right;
                }
                else
                {
                    float x = Mathf.Clamp(spotRect.xMax + preferredGap, spotRect.xMax + minGap, maxX - cardW);
                    float y = Mathf.Clamp(spotRect.center.y - cardH * 0.5f, minY, maxY - cardH);
                    card = new Rect(x, y, cardW, cardH);
                    arrowDir = TutorialArrowDirection.Left;
                }
            }
            else if (spaceAbove >= spaceBelow)
            {
                float x = Mathf.Clamp(spotRect.center.x - cardW * 0.5f, minX, maxX - cardW);
                float y = Mathf.Clamp(spotRect.yMin - preferredGap - cardH, minY, Mathf.Max(minY, spotRect.yMin - minGap - cardH));
                card = new Rect(x, y, cardW, cardH);
                arrowDir = TutorialArrowDirection.Down;
            }
            else
            {
                float x = Mathf.Clamp(spotRect.center.x - cardW * 0.5f, minX, maxX - cardW);
                float y = Mathf.Clamp(spotRect.yMax + preferredGap, Mathf.Min(maxY - cardH, spotRect.yMax + minGap), maxY - cardH);
                card = new Rect(x, y, cardW, cardH);
                arrowDir = TutorialArrowDirection.Up;
            }

            if (card.Overlaps(skipRect))
            {
                float shiftedDownY = skipRect.yMax + 10f * tk;
                if (shiftedDownY + card.height <= maxY && !new Rect(card.x, shiftedDownY, card.width, card.height).Overlaps(spotRect))
                {
                    card.y = shiftedDownY;
                }
                else
                {
                    card.x = Mathf.Max(minX, skipRect.xMin - 10f * tk - card.width);
                }
            }

            return card;
        }

        private void DrawTutorialConnectorAndArrow(Rect cardRect, Rect spotRect, TutorialArrowDirection arrowDir, float tk)
        {
            float badgeS = TutorialArrowBadgeSize * tk;
            float spotGap = TutorialArrowSpotGap * tk;
            float lineT = Mathf.Max(2f, TutorialConnectorLineThickness * tk);
            float safePad = TutorialCardSafeMargin * tk;

            if (arrowDir == TutorialArrowDirection.Right)
            {
                float badgeX = spotRect.xMin - spotGap - badgeS;
                float badgeY = Mathf.Clamp(spotRect.center.y - badgeS * 0.5f, safePad, Screen.height - safePad - badgeS);
                float endY = badgeY + badgeS * 0.5f;
                float startX = cardRect.xMax;
                float startY = Mathf.Clamp(endY, cardRect.yMin + 20f * tk, cardRect.yMax - 20f * tk);
                if (badgeX > startX)
                {
                    float midX = (startX + badgeX) * 0.5f;
                    GUI.DrawTexture(new Rect(startX, startY - lineT * 0.5f, midX - startX + lineT * 0.5f, lineT), accentTex);
                    float vMin = Mathf.Min(startY, endY);
                    float vLen = Mathf.Abs(endY - startY) + lineT;
                    GUI.DrawTexture(new Rect(midX - lineT * 0.5f, vMin - lineT * 0.5f, lineT, vLen), accentTex);
                    GUI.DrawTexture(new Rect(midX - lineT * 0.5f, endY - lineT * 0.5f, badgeX - midX + lineT * 0.5f, lineT), accentTex);
                }
                GUI.Label(new Rect(badgeX, badgeY, badgeS, badgeS), ArrowRightGlyph, tutorialArrowStyle);
            }
            else if (arrowDir == TutorialArrowDirection.Left)
            {
                float badgeX = spotRect.xMax + spotGap;
                float badgeY = Mathf.Clamp(spotRect.center.y - badgeS * 0.5f, safePad, Screen.height - safePad - badgeS);
                float endX = badgeX + badgeS;
                float endY = badgeY + badgeS * 0.5f;
                float startX = cardRect.xMin;
                float startY = Mathf.Clamp(endY, cardRect.yMin + 20f * tk, cardRect.yMax - 20f * tk);
                if (startX > endX)
                {
                    float midX = (endX + startX) * 0.5f;
                    GUI.DrawTexture(new Rect(endX, endY - lineT * 0.5f, midX - endX + lineT * 0.5f, lineT), accentTex);
                    float vMin = Mathf.Min(startY, endY);
                    float vLen = Mathf.Abs(endY - startY) + lineT;
                    GUI.DrawTexture(new Rect(midX - lineT * 0.5f, vMin - lineT * 0.5f, lineT, vLen), accentTex);
                    GUI.DrawTexture(new Rect(midX - lineT * 0.5f, startY - lineT * 0.5f, startX - midX + lineT * 0.5f, lineT), accentTex);
                }
                GUI.Label(new Rect(badgeX, badgeY, badgeS, badgeS), ArrowLeftGlyph, tutorialArrowStyle);
            }
            else if (arrowDir == TutorialArrowDirection.Down)
            {
                float badgeX = Mathf.Clamp(spotRect.center.x - badgeS * 0.5f, safePad, Screen.width - safePad - badgeS);
                float badgeY = spotRect.yMin - spotGap - badgeS;
                float endX = badgeX + badgeS * 0.5f;
                float startX = Mathf.Clamp(endX, cardRect.xMin + 24f * tk, cardRect.xMax - 24f * tk);
                float startY = cardRect.yMax;
                if (badgeY > startY)
                {
                    float midY = (startY + badgeY) * 0.5f;
                    GUI.DrawTexture(new Rect(startX - lineT * 0.5f, startY, lineT, midY - startY + lineT * 0.5f), accentTex);
                    float hMin = Mathf.Min(startX, endX);
                    float hLen = Mathf.Abs(endX - startX) + lineT;
                    GUI.DrawTexture(new Rect(hMin - lineT * 0.5f, midY - lineT * 0.5f, hLen, lineT), accentTex);
                    GUI.DrawTexture(new Rect(endX - lineT * 0.5f, midY - lineT * 0.5f, lineT, badgeY - midY + lineT * 0.5f), accentTex);
                }
                GUI.Label(new Rect(badgeX, badgeY, badgeS, badgeS), ArrowDownGlyph, tutorialArrowStyle);
            }
            else
            {
                float badgeX = Mathf.Clamp(spotRect.center.x - badgeS * 0.5f, safePad, Screen.width - safePad - badgeS);
                float badgeY = spotRect.yMax + spotGap;
                float endX = badgeX + badgeS * 0.5f;
                float endY = badgeY + badgeS;
                float startX = Mathf.Clamp(endX, cardRect.xMin + 24f * tk, cardRect.xMax - 24f * tk);
                float startY = cardRect.yMin;
                if (startY > endY)
                {
                    float midY = (endY + startY) * 0.5f;
                    GUI.DrawTexture(new Rect(endX - lineT * 0.5f, endY, lineT, midY - endY + lineT * 0.5f), accentTex);
                    float hMin = Mathf.Min(startX, endX);
                    float hLen = Mathf.Abs(endX - startX) + lineT;
                    GUI.DrawTexture(new Rect(hMin - lineT * 0.5f, midY - lineT * 0.5f, hLen, lineT), accentTex);
                    GUI.DrawTexture(new Rect(startX - lineT * 0.5f, midY - lineT * 0.5f, lineT, startY - midY + lineT * 0.5f), accentTex);
                }
                GUI.Label(new Rect(badgeX, badgeY, badgeS, badgeS), ArrowUpGlyph, tutorialArrowStyle);
            }
        }

        private void DrawTutorialStepDots(float centerX, float centerY, int activeStep, float tk)
        {
            float inactiveW = TutorialStepDotSize * tk;
            float activeW = TutorialStepDotActiveWidth * tk;
            float dotH = TutorialStepDotSize * tk;
            float gap = TutorialStepDotGap * tk;
            float totalW = activeW + (TutorialTotalStepCount - 1) * inactiveW + (TutorialTotalStepCount - 1) * gap;
            float curX = centerX - totalW * 0.5f;
            float y = centerY - dotH * 0.5f;

            for (int i = 0; i < TutorialTotalStepCount; i++)
            {
                float w = i == activeStep ? activeW : inactiveW;
                GUI.DrawTexture(new Rect(curX, y, w, dotH), i == activeStep ? accentTex : tutorialDotInactiveTex);
                curX += w + gap;
            }
        }

        private static void DrawRectBorder(Rect rect, float thickness, Texture2D tex)
        {
            if (tex == null || rect.width <= 0f || rect.height <= 0f || thickness <= 0f) return;
            float t = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * 0.5f);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, t), tex);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - t, rect.width, t), tex);
            if (rect.height > t * 2f)
            {
                GUI.DrawTexture(new Rect(rect.xMin, rect.yMin + t, t, rect.height - t * 2f), tex);
                GUI.DrawTexture(new Rect(rect.xMax - t, rect.yMin + t, t, rect.height - t * 2f), tex);
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
