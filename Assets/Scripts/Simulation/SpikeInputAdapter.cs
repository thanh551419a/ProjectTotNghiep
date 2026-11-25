using System;
using System.Collections.Generic;
using Native;
using Assets.Table;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Simulation
{
    /// <summary>Logical player actions exposed by the configured native button mapping.</summary>
    public enum SpikeAction
    {
        Left = 0,
        Right = 1,
        Bump = 2,
        Serve = 3,
        Jump = 4,
        SpikeLight = 5,
        SpikeMedium = 6,
        SpikeStrong = 7
    }

    public enum MovementControlMode
    {
        TwoButtons = 0,
        HorizontalSlider = 1
    }

    [Flags]
    public enum SpikeInputSource : byte
    {
        None = 0,
        UiPointer = 1,
        Keyboard = 2,
        TestInjection = 4
    }

    /// <summary>
    /// THE single Unity input adapter. Every input source (keyboard, on-screen UI buttons,
    /// development test injection) is merged here and converted into the configured native
    /// button IDs (button_mapping.json) exactly once per simulation tick via BuildInput().
    ///
    /// It never calls native code, never writes the Data Table and never moves Transforms.
    /// </summary>
    public sealed class SpikeInputAdapter : MonoBehaviour
    {
        public enum InputEventKind : byte
        {
            Pressed = 0,
            Hold = 1
        }

        public struct InputTraceItem
        {
            public string buttonName;
            public uint nativeButtonId;
            public int uiButtonId;
            public SpikeInputSource source;
            public InputEventKind eventKind;
        }

        public const int ActionCount = 8;
        public const int MaxTraceItemsPerTick = 10;

        /// <summary>UI button ids used by UIButtonInputTest components on the gameplay Canvas.</summary>
        public const int UiLeft = 0, UiRight = 1, UiBump = 2, UiServe = 3;
        public const int UiJump = 4, UiSpikeLight = 5, UiSpikeMedium = 6, UiSpikeStrong = 7;
        public const int UiDedicatedSpike = 8;

        public const float HoldThresholdSeconds = UIButtonInputTest.LongPressSeconds;
        public const float SliderDeadzone = 0.12f;
        private const int NoUiButtonId = -1;
        private const uint NoNativeButtonId = 0u;
        private const uint MaxNativeMaskBits = 16u;
        private const string JumpSpikeTraceName = "JumpSpike";
        private const string DedicatedSpikeTraceName = "DedicatedSpike";
        private const string SpikeSelectorLightTraceName = "SpikeSelector(Light)";
        private const string SpikeSelectorMediumTraceName = "SpikeSelector(Medium)";
        private const string SpikeSelectorStrongTraceName = "SpikeSelector(Strong)";
        private static readonly string[] ActionTraceNames =
        {
            "Left",
            "Right",
            "Bump",
            "Serve",
            "Jump",
            "SpikeLight",
            "SpikeMedium",
            "SpikeStrong"
        };
        private const float DedicatedSpikeCancelDeadzoneRatio = 0.45f;
        private const float SpikeOptionSnapRadiusMultiplier = 1.85f;
        private const float MinSpikeOptionWidthScreenPx = 52f;
        private const float PointerMoveEpsilonSq = 1f;
        private const float MillisecondsPerSecond = 1000f;

        [SerializeField] private bool enableKeyboard = true;

        private ButtonMappingConfig mapping;
        private Canvas boundCanvas;

        private readonly bool[] uiHeld = new bool[ActionCount];
        private readonly bool[] uiPulse = new bool[ActionCount];
        private readonly bool[] keyLatch = new bool[ActionCount];
        private readonly bool[] injected = new bool[ActionCount];
        private readonly List<UIButtonInputTest> boundButtons = new List<UIButtonInputTest>();
        private readonly uint[][] submittedIdBuffers =
        {
            Array.Empty<uint>(), new uint[1], new uint[2], new uint[3], new uint[4],
            new uint[5], new uint[6], new uint[7], new uint[8]
        };
        private bool sliderLeftActive;
        private bool sliderRightActive;
        private bool uiJumpPressedWhileAirborne;
        private bool kbJumpPressedWhileAirborne;
        private float dedicatedSpikeDownTime;
        private bool dedicatedSpikeHeld;
        private bool dedicatedSpikeSelecting;
        private bool selectorOpen;
        private SpikeAction? hoveredSpikeOption;
        private int dedicatedSpikePointerId = int.MinValue;
        private Vector2 dedicatedSpikePointerPos;
        private bool hasDedicatedSpikePointer;
        private bool hoveredOptionSetExplicitly;
        private SpikeAction activeDedicatedSpikeAction = SpikeAction.SpikeMedium;
        private readonly InputTraceItem[] lastTraceItems = new InputTraceItem[MaxTraceItemsPerTick];
        private ushort prevSubmittedNativeMask;
        private bool prevDedicatedSpikeHeldInTick;
        private bool pulsedFromDedicatedSpike;
        public bool SelectorOpen => selectorOpen;
        public SpikeAction? HoveredSpikeOption => hoveredSpikeOption;
        public SpikeAction ActiveDedicatedSpikeAction => activeDedicatedSpikeAction;
        public bool PrimaryIsSpike => IsPlayerAirborne();
        public bool LastJumpSubmittedAsJumpSpike { get; private set; }
        public MovementControlMode CurrentMovementMode { get; private set; } = MovementControlMode.TwoButtons;
        public float SliderHorizontalAxis { get; private set; }
        public event Action<bool> OnSpikeSelectorChanged;
        public event Action<SpikeAction?> OnHoveredSpikeOptionChanged;
        public event Action<SpikeAction> OnActiveDedicatedSpikeActionChanged;
        public event Action<MovementControlMode> MovementModeChanged;

        /// <summary>When true, only test-injected actions are submitted (deterministic self-test).</summary>
        public bool InjectionExclusive { get; set; }

        public ButtonMappingConfig Mapping => mapping;
        public uint[] LastSubmittedIds { get; private set; } = Array.Empty<uint>();
        public bool[] LastSubmittedActions { get; } = new bool[ActionCount];
        public SpikeInputSource[] LastSubmittedSources { get; } = new SpikeInputSource[ActionCount];
        public int LastSubmitFrame { get; private set; } = -1;
        public long SubmitCount { get; private set; }
        public int LastTraceCount { get; private set; }
        public InputTraceItem GetLastTraceItem(int index) => lastTraceItems[index];

        public void Configure(ButtonMappingConfig config)
        {
            mapping = config ?? throw new ArgumentNullException(nameof(config));
        }

        private void Reset()
        {
        }

        private void OnEnable()
        {
            UIButtonInputTest.LayoutEditModeChanged += HandleLayoutEditModeChanged;
            BindUiButtons(gameObject.scene);
            if (UIButtonInputTest.IsLayoutEditMode)
                HandleLayoutEditModeChanged(true);
        }

        private void OnDisable()
        {
            UIButtonInputTest.LayoutEditModeChanged -= HandleLayoutEditModeChanged;
            foreach (var b in boundButtons)
            {
                if (b != null) b.OnInput -= HandleUiInput;
                if (b != null) b.OnHold -= HandleUiHold;
                if (b != null) b.OnLongPress -= HandleUiLongPress;
                if (b != null) b.OnHover -= HandleUiHover;
                if (b != null) b.OnPointerPositionChanged -= HandleUiPointerPositionChanged;
            }
            boundButtons.Clear();
            boundCanvas = null;
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
            SliderHorizontalAxis = 0f;
            sliderLeftActive = false;
            sliderRightActive = false;
            uiJumpPressedWhileAirborne = false;
            kbJumpPressedWhileAirborne = false;
            LastJumpSubmittedAsJumpSpike = false;
            ResetTraceState();
            CancelDedicatedSpike();
        }

        /// <summary>Subscribes to every UIButtonInputTest in the loaded scene(s) exactly once.</summary>
        public void BindUiButtons()
        {
            BindUiButtons(gameObject.scene);
        }

        public void BindUiButtons(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var b in root.GetComponentsInChildren<UIButtonInputTest>(true))
                {
                    if (boundCanvas == null) boundCanvas = b.GetComponentInParent<Canvas>();
                    if (!b.AllowHold && (b.ButtonId == UiLeft || b.ButtonId == UiRight || b.ButtonId == UiBump || b.ButtonId == UiDedicatedSpike))
                    {
                        b.Configure(b.ButtonId, true);
                    }
                    if (boundButtons.Contains(b)) continue;
                    b.OnInput += HandleUiInput;
                    b.OnHold += HandleUiHold;
                    b.OnLongPress += HandleUiLongPress;
                    b.OnHover += HandleUiHover;
                    b.OnPointerPositionChanged += HandleUiPointerPositionChanged;
                    boundButtons.Add(b);
                }
            }
        }

        private static int UiIdToAction(int uiId)
        {
            switch (uiId)
            {
                case UiLeft: return (int)SpikeAction.Left;
                case UiRight: return (int)SpikeAction.Right;
                case UiBump: return (int)SpikeAction.Bump;
                case UiServe: return (int)SpikeAction.Serve;
                case UiJump: return (int)SpikeAction.Jump;
                case UiSpikeLight: return (int)SpikeAction.SpikeLight;
                case UiSpikeMedium: return (int)SpikeAction.SpikeMedium;
                case UiSpikeStrong: return (int)SpikeAction.SpikeStrong;
                default: return -1;
            }
        }

        private static int UiButtonIdForAction(SpikeAction action)
        {
            switch (action)
            {
                case SpikeAction.Left: return UiLeft;
                case SpikeAction.Right: return UiRight;
                case SpikeAction.Bump: return UiBump;
                case SpikeAction.Serve: return UiServe;
                case SpikeAction.Jump: return UiJump;
                case SpikeAction.SpikeLight: return UiSpikeLight;
                case SpikeAction.SpikeMedium: return UiSpikeMedium;
                case SpikeAction.SpikeStrong: return UiSpikeStrong;
                default: return NoUiButtonId;
            }
        }

        private static string GetSelectorTraceName(SpikeAction? option)
        {
            if (!option.HasValue) return DedicatedSpikeTraceName;
            switch (option.Value)
            {
                case SpikeAction.SpikeLight: return SpikeSelectorLightTraceName;
                case SpikeAction.SpikeMedium: return SpikeSelectorMediumTraceName;
                case SpikeAction.SpikeStrong: return SpikeSelectorStrongTraceName;
                default: return DedicatedSpikeTraceName;
            }
        }

        private void ResetTraceState()
        {
            prevSubmittedNativeMask = 0;
            prevDedicatedSpikeHeldInTick = false;
            pulsedFromDedicatedSpike = false;
            LastTraceCount = 0;
        }

        private void HandleLayoutEditModeChanged(bool editMode)
        {
            if (!editMode) return;
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
            SliderHorizontalAxis = 0f;
            sliderLeftActive = false;
            sliderRightActive = false;
            uiJumpPressedWhileAirborne = false;
            kbJumpPressedWhileAirborne = false;
            ResetTraceState();
            CancelDedicatedSpike();
        }

        public void SetMovementControlMode(MovementControlMode mode)
        {
            bool changed = CurrentMovementMode != mode;
            CurrentMovementMode = mode;
            uiHeld[(int)SpikeAction.Left] = false;
            uiHeld[(int)SpikeAction.Right] = false;
            uiPulse[(int)SpikeAction.Left] = false;
            uiPulse[(int)SpikeAction.Right] = false;
            SetSliderHorizontalAxis(0f);
            if (changed)
            {
                MovementModeChanged?.Invoke(mode);
            }
        }

        public void ToggleMovementControlMode()
        {
            SetMovementControlMode(CurrentMovementMode == MovementControlMode.TwoButtons
                ? MovementControlMode.HorizontalSlider
                : MovementControlMode.TwoButtons);
        }

        public void SetSliderHorizontalAxis(float axis)
        {
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                SliderHorizontalAxis = 0f;
                sliderLeftActive = false;
                sliderRightActive = false;
                return;
            }

            float clamped = Mathf.Clamp(axis, -1f, 1f);
            SliderHorizontalAxis = clamped;
            if (clamped <= -SliderDeadzone)
            {
                sliderLeftActive = true;
                sliderRightActive = false;
            }
            else if (clamped >= SliderDeadzone)
            {
                sliderLeftActive = false;
                sliderRightActive = true;
            }
            else
            {
                sliderLeftActive = false;
                sliderRightActive = false;
            }
        }

        private void ResetDedicatedSpikePointer()
        {
            dedicatedSpikePointerId = int.MinValue;
            dedicatedSpikePointerPos = Vector2.zero;
            hasDedicatedSpikePointer = false;
            hoveredOptionSetExplicitly = false;
        }

        private void CancelDedicatedSpike()
        {
            dedicatedSpikeHeld = false;
            dedicatedSpikeSelecting = false;
            ResetDedicatedSpikePointer();
            SetHoveredSpikeOptionInternal(null);
            SetSelectorOpen(false);
        }

        public bool TryGetButtonCooldown(int buttonId, out float remainSeconds, out float normalizedFill)
        {
            remainSeconds = 0f;
            normalizedFill = 0f;

            if (buttonId == UiServe)
            {
                return false;
            }

            var table = SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized)
            {
                return false;
            }

            if (!table.TryGetEntityCooldown(SpikeNativeBridge.ENTITY_PLAYER, out SimActionState cdAction, out float remainMs, out float durationMs) ||
                remainMs <= 0f)
            {
                return false;
            }

            bool matchesButton;
            switch (buttonId)
            {
                case UiBump:
                    matchesButton = cdAction == SimActionState.Bump;
                    break;

                case UiDedicatedSpike:
                case UiSpikeLight:
                case UiSpikeMedium:
                case UiSpikeStrong:
                    matchesButton = IsSpikeCooldownAction(cdAction);
                    break;

                case UiJump:
                    matchesButton = PrimaryIsSpike && IsSpikeCooldownAction(cdAction);
                    break;

                default:
                    matchesButton = false;
                    break;
            }

            if (!matchesButton)
            {
                return false;
            }

            remainSeconds = remainMs / MillisecondsPerSecond;
            normalizedFill = Mathf.Clamp01(remainMs / Mathf.Max(1f, durationMs));
            return true;
        }

        private static bool IsSpikeCooldownAction(SimActionState action)
        {
            return action == SimActionState.Spike ||
                   action == SimActionState.SpikeLight ||
                   action == SimActionState.SpikeMedium ||
                   action == SimActionState.SpikeStrong;
        }

        public bool IsActionOnCooldown()
        {
            return false;
        }

        public bool IsSpikeActionOnCooldown()
        {
            return false;
        }

        public bool IsGroundActionOnCooldown()
        {
            return false;
        }

        private void HandleUiInput(int uiButtonId, bool isPressed)
        {
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                if (isPressed) return;
                if (uiButtonId == UiDedicatedSpike)
                {
                    CancelDedicatedSpike();
                    return;
                }
            }
            if (uiButtonId == UiDedicatedSpike)
            {
                if (isPressed)
                {
                    BeginDedicatedSpike(Time.unscaledTime);
                }
                else
                {
                    EndDedicatedSpike(Time.unscaledTime);
                }
                return;
            }
            if (uiButtonId >= UiSpikeLight && uiButtonId <= UiSpikeStrong)
            {
                if (!isPressed) return;
                var spikeAction = (SpikeAction)UiIdToAction(uiButtonId);
                if (!SelectSpikeStrength(spikeAction))
                {
                    SetActiveDedicatedSpikeAction(spikeAction);
                    CancelDedicatedSpike();
                }
                return;
            }
            if (uiButtonId == UiJump)
            {
                if (!isPressed)
                {
                    uiHeld[(int)SpikeAction.Jump] = false;
                    uiJumpPressedWhileAirborne = false;
                    return;
                }
                uiJumpPressedWhileAirborne = IsPlayerAirborne();
                uiPulse[(int)SpikeAction.Jump] = true;
                uiHeld[(int)SpikeAction.Jump] = false;
                return;
            }
            int a = UiIdToAction(uiButtonId);
            if (a < 0) return;
            var mappedAction = (SpikeAction)a;
            if (mappedAction == SpikeAction.Left || mappedAction == SpikeAction.Right || mappedAction == SpikeAction.Bump)
            {
                if (isPressed && !uiHeld[a]) uiPulse[a] = true; // guarantees at least one tick for very short taps
                uiHeld[a] = isPressed;
                return;
            }
            if (!isPressed)
            {
                uiHeld[a] = false;
                return;
            }
            uiPulse[a] = true;
            uiHeld[a] = false;
        }

        private void HandleUiPointerPositionChanged(int uiButtonId, int pointerId, Vector2 screenPos)
        {
            if (UIButtonInputTest.IsLayoutEditMode || uiButtonId != UiDedicatedSpike || !dedicatedSpikeHeld) return;

            if (!hasDedicatedSpikePointer)
            {
                dedicatedSpikePointerId = pointerId;
                dedicatedSpikePointerPos = screenPos;
                hasDedicatedSpikePointer = true;
            }
            else if (pointerId == dedicatedSpikePointerId)
            {
                if ((screenPos - dedicatedSpikePointerPos).sqrMagnitude > PointerMoveEpsilonSq)
                {
                    hoveredOptionSetExplicitly = false;
                }
                dedicatedSpikePointerPos = screenPos;
            }
            else
            {
                return;
            }

            if (selectorOpen && !hoveredOptionSetExplicitly)
            {
                SetHoveredSpikeOptionInternal(TryFindHoveredSpikeOptionFromScreenPointer(dedicatedSpikePointerPos, eventCamera: null));
            }
        }

        private void HandleUiHold(int uiButtonId)
        {
            if (UIButtonInputTest.IsLayoutEditMode) return;
            if (uiButtonId == UiDedicatedSpike)
            {
                if (dedicatedSpikeHeld)
                {
                    AdvanceDedicatedSpikeHold(Mathf.Max(0f, Time.unscaledTime - dedicatedSpikeDownTime));
                    RefreshDedicatedSpikeHoverFromPointer();
                }
                return;
            }
            int a = UiIdToAction(uiButtonId);
            if (a == (int)SpikeAction.Left || a == (int)SpikeAction.Right || a == (int)SpikeAction.Bump)
            {
                uiHeld[a] = true;
            }
        }

        private void HandleUiLongPress(int uiButtonId)
        {
            if (UIButtonInputTest.IsLayoutEditMode || uiButtonId != UiDedicatedSpike) return;
            AdvanceDedicatedSpikeHold(HoldThresholdSeconds);
            RefreshDedicatedSpikeHoverFromPointer();
        }

        private void HandleUiHover(int uiButtonId, bool isHovered)
        {
            if (UIButtonInputTest.IsLayoutEditMode) return;
            if (uiButtonId < UiSpikeLight || uiButtonId > UiSpikeStrong) return;
            var action = (SpikeAction)UiIdToAction(uiButtonId);
            if (isHovered && selectorOpen)
            {
                hoveredOptionSetExplicitly = true;
                SetHoveredSpikeOptionInternal(action);
            }
            else if (!isHovered && hoveredSpikeOption == action)
            {
                hoveredOptionSetExplicitly = false;
                SetHoveredSpikeOptionInternal(null);
            }
        }

        private void RefreshDedicatedSpikeHoverFromPointer()
        {
            if (!selectorOpen || !hasDedicatedSpikePointer) return;
            TryRefreshDedicatedSpikePointerPosition();
            if (!hoveredOptionSetExplicitly)
            {
                SetHoveredSpikeOptionInternal(TryFindHoveredSpikeOptionFromScreenPointer(dedicatedSpikePointerPos, eventCamera: null));
            }
        }

        private bool TryRefreshDedicatedSpikePointerPosition()
        {
            if (!hasDedicatedSpikePointer || !Application.isPlaying) return false;
            int touchCount = Input.touchCount;
            if (dedicatedSpikePointerId >= 0)
            {
                for (int i = 0; i < touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.fingerId == dedicatedSpikePointerId)
                    {
                        if ((t.position - dedicatedSpikePointerPos).sqrMagnitude > PointerMoveEpsilonSq)
                        {
                            hoveredOptionSetExplicitly = false;
                        }
                        dedicatedSpikePointerPos = t.position;
                        return true;
                    }
                }
                return true;
            }

            if (touchCount == 0 && Input.GetMouseButton(0))
            {
                Vector2 mousePos = Input.mousePosition;
                if ((mousePos - dedicatedSpikePointerPos).sqrMagnitude > PointerMoveEpsilonSq)
                {
                    hoveredOptionSetExplicitly = false;
                }
                dedicatedSpikePointerPos = mousePos;
            }
            return true;
        }

        private Camera ResolveUiCamera(Camera eventCamera)
        {
            if (eventCamera != null) return eventCamera;
            if (boundCanvas != null && boundCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                return boundCanvas.worldCamera;
            }
            return null;
        }

        private static Vector2 GetRectCenterScreenPoint(RectTransform rt, Camera uiCamera, out float halfWidthScreenPx)
        {
            Rect r = rt.rect;
            Vector3 worldCenter = rt.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
            Vector3 worldRight = rt.TransformPoint(new Vector3(r.xMax, r.center.y, 0f));
            Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldCenter);
            Vector2 rightScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldRight);
            halfWidthScreenPx = Vector2.Distance(centerScreen, rightScreen);
            return centerScreen;
        }

        private SpikeAction? TryFindHoveredSpikeOptionFromScreenPointer(Vector2 screenPos, Camera eventCamera)
        {
            Camera cam = ResolveUiCamera(eventCamera);

            // 1. Direct hit test on Action_5..Action_7 (UiSpikeLight..UiSpikeStrong)
            for (int i = 0; i < boundButtons.Count; i++)
            {
                var btn = boundButtons[i];
                if (btn == null || !btn.isActiveAndEnabled) continue;
                int id = btn.ButtonId;
                if (id < UiSpikeLight || id > UiSpikeStrong) continue;
                var rt = btn.transform as RectTransform;
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam))
                {
                    return (SpikeAction)UiIdToAction(id);
                }
            }

            // 2. Center cancel deadzone on DedicatedSpike (Action_8): holding in place does NOT auto-pick an option
            for (int i = 0; i < boundButtons.Count; i++)
            {
                var btn = boundButtons[i];
                if (btn == null || !btn.isActiveAndEnabled || btn.ButtonId != UiDedicatedSpike) continue;
                var dedicatedRt = btn.transform as RectTransform;
                if (dedicatedRt == null) break;
                Vector2 dedicatedCenter = GetRectCenterScreenPoint(dedicatedRt, cam, out float dedicatedRadiusScreenPx);
                float cancelDeadzonePx = DedicatedSpikeCancelDeadzoneRatio * Mathf.Max(1f, dedicatedRadiusScreenPx);
                if ((screenPos - dedicatedCenter).sqrMagnitude <= cancelDeadzonePx * cancelDeadzonePx)
                {
                    return null;
                }
                break;
            }

            // 3. Fan-out radial selection: nearest option center within 1.85x option width (~305 px at 1080p)
            SpikeAction? nearestOption = null;
            float nearestDistSq = float.MaxValue;
            for (int i = 0; i < boundButtons.Count; i++)
            {
                var btn = boundButtons[i];
                if (btn == null || !btn.isActiveAndEnabled) continue;
                int id = btn.ButtonId;
                if (id < UiSpikeLight || id > UiSpikeStrong) continue;
                var rt = btn.transform as RectTransform;
                if (rt == null) continue;
                Vector2 centerScreen = GetRectCenterScreenPoint(rt, cam, out float optionHalfWidthPx);
                float optionWidthScreenPx = Mathf.Max(MinSpikeOptionWidthScreenPx, optionHalfWidthPx * 2f);
                float maxRadiusPx = SpikeOptionSnapRadiusMultiplier * optionWidthScreenPx;
                float distSq = (screenPos - centerScreen).sqrMagnitude;
                if (distSq <= maxRadiusPx * maxRadiusPx && distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestOption = (SpikeAction)UiIdToAction(id);
                }
            }
            return nearestOption;
        }

        private void SetHoveredSpikeOptionInternal(SpikeAction? option)
        {
            if (!selectorOpen || (option.HasValue && (option.Value < SpikeAction.SpikeLight || option.Value > SpikeAction.SpikeStrong)))
            {
                option = null;
            }
            if (hoveredSpikeOption == option) return;
            hoveredSpikeOption = option;
            OnHoveredSpikeOptionChanged?.Invoke(hoveredSpikeOption);
        }

        public void SetHoveredSpikeOption(SpikeAction? option)
        {
            hoveredOptionSetExplicitly = selectorOpen && option.HasValue &&
                                         option.Value >= SpikeAction.SpikeLight &&
                                         option.Value <= SpikeAction.SpikeStrong;
            SetHoveredSpikeOptionInternal(option);
        }

        public void SetActiveDedicatedSpikeAction(SpikeAction action)
        {
            if (action < SpikeAction.SpikeLight || action > SpikeAction.SpikeStrong) return;
            if (activeDedicatedSpikeAction == action) return;
            activeDedicatedSpikeAction = action;
            OnActiveDedicatedSpikeActionChanged?.Invoke(activeDedicatedSpikeAction);
        }

        /// <summary>Starts a dedicated Spike gesture. Time is unscaled seconds for deterministic EditMode tests.</summary>
        public void BeginDedicatedSpike(float unscaledTime)
        {
            dedicatedSpikeDownTime = unscaledTime;
            dedicatedSpikeHeld = true;
            dedicatedSpikeSelecting = false;
            ResetDedicatedSpikePointer();
            SetHoveredSpikeOptionInternal(null);
        }

        /// <summary>Opens the strength selector once the gesture reaches the hold threshold.</summary>
        public void AdvanceDedicatedSpikeHold(float heldSeconds)
        {
            if (!dedicatedSpikeHeld || dedicatedSpikeSelecting || heldSeconds < HoldThresholdSeconds) return;
            dedicatedSpikeSelecting = true;
            SetSelectorOpen(true);
            RefreshDedicatedSpikeHoverFromPointer();
        }

        /// <summary>Ends a gesture; a short tap requests ActiveDedicatedSpikeAction once, while releasing a hold switches ActiveDedicatedSpikeAction to any hovered option without firing and closes the selector immediately.</summary>
        public void EndDedicatedSpike(float unscaledTime)
        {
            if (!dedicatedSpikeHeld)
            {
                ResetDedicatedSpikePointer();
                SetHoveredSpikeOptionInternal(null);
                SetSelectorOpen(false);
                return;
            }
            AdvanceDedicatedSpikeHold(Mathf.Max(0f, unscaledTime - dedicatedSpikeDownTime));
            dedicatedSpikeHeld = false;
            if (!dedicatedSpikeSelecting)
            {
                uiPulse[(int)activeDedicatedSpikeAction] = true;
                pulsedFromDedicatedSpike = true;
            }
            else
            {
                RefreshDedicatedSpikeHoverFromPointer();
                if (hoveredSpikeOption.HasValue)
                {
                    SetActiveDedicatedSpikeAction(hoveredSpikeOption.Value);
                }
            }
            ResetDedicatedSpikePointer();
            SetHoveredSpikeOptionInternal(null);
            SetSelectorOpen(false);
        }

        /// <summary>Chooses a visible selector option to replace ActiveDedicatedSpikeAction without firing; returns false when the selector is closed.</summary>
        public bool SelectSpikeStrength(SpikeAction action)
        {
            if (!selectorOpen || action < SpikeAction.SpikeLight || action > SpikeAction.SpikeStrong) return false;
            SetActiveDedicatedSpikeAction(action);
            dedicatedSpikeHeld = false;
            dedicatedSpikeSelecting = false;
            ResetDedicatedSpikePointer();
            SetHoveredSpikeOptionInternal(null);
            SetSelectorOpen(false);
            return true;
        }

        private void SetSelectorOpen(bool value)
        {
            if (!value)
            {
                hoveredOptionSetExplicitly = false;
                if (hoveredSpikeOption.HasValue)
                {
                    hoveredSpikeOption = null;
                    OnHoveredSpikeOptionChanged?.Invoke(null);
                }
            }
            if (selectorOpen == value) return;
            selectorOpen = value;
            OnSpikeSelectorChanged?.Invoke(value);
        }

        private bool IsPlayerAirborne()
        {
            var table = SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized) return false;
            SimAnimId anim = table.GetEntityAnim(SpikeNativeBridge.ENTITY_PLAYER);
            return anim == SimAnimId.JumpUp || anim == SimAnimId.Fall;
        }

        private static bool IsBumpKeyDown()
        {
            return Input.GetKeyDown(KeyCode.S) ||
                   Input.GetKeyDown(KeyCode.K) ||
                   Input.GetKeyDown(KeyCode.J) ||
                   Input.GetKeyDown(KeyCode.DownArrow) ||
                   Input.GetKeyDown(KeyCode.LeftShift) ||
                   Input.GetKeyDown(KeyCode.RightShift);
        }

        private static bool IsBumpKeyHeld()
        {
            return Input.GetKey(KeyCode.S) ||
                   Input.GetKey(KeyCode.K) ||
                   Input.GetKey(KeyCode.J) ||
                   Input.GetKey(KeyCode.DownArrow) ||
                   Input.GetKey(KeyCode.LeftShift) ||
                   Input.GetKey(KeyCode.RightShift);
        }

        private void Update()
        {
            if (dedicatedSpikeHeld && Application.isPlaying)
            {
                AdvanceDedicatedSpikeHold(Mathf.Max(0f, Time.unscaledTime - dedicatedSpikeDownTime));
                RefreshDedicatedSpikeHoverFromPointer();
            }
            if (!enableKeyboard) return;
            // Latch key-downs so a tap shorter than one fixed step is never lost.
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) keyLatch[(int)SpikeAction.Left] = true;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) keyLatch[(int)SpikeAction.Right] = true;
            if (IsBumpKeyDown()) keyLatch[(int)SpikeAction.Bump] = true;

            bool airborne = IsPlayerAirborne();
            bool spaceDown = Input.GetKeyDown(KeyCode.Space);
            bool enterDown = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (enterDown || (spaceDown && !airborne))
            {
                keyLatch[(int)SpikeAction.Serve] = true;
            }
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || (spaceDown && airborne))
            {
                kbJumpPressedWhileAirborne = airborne;
                keyLatch[(int)SpikeAction.Jump] = true;
            }
            if (Input.GetKeyDown(KeyCode.Z)) keyLatch[(int)SpikeAction.SpikeLight] = true;
            if (Input.GetKeyDown(KeyCode.X)) keyLatch[(int)SpikeAction.SpikeMedium] = true;
            if (Input.GetKeyDown(KeyCode.C)) keyLatch[(int)SpikeAction.SpikeStrong] = true;
        }

        private bool KeyboardHeld(SpikeAction a)
        {
            if (!enableKeyboard) return false;
            switch (a)
            {
                case SpikeAction.Left: return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                case SpikeAction.Right: return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                case SpikeAction.Bump: return IsBumpKeyHeld();
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Development test injection (used by GameplaySelfTestController only)
        // ------------------------------------------------------------------
        public void SetInjected(SpikeAction action, bool held) => injected[(int)action] = held;
        public bool GetInjected(SpikeAction action) => injected[(int)action];
        public void ClearInjected()
        {
            Array.Clear(injected, 0, ActionCount);
            SliderHorizontalAxis = 0f;
            sliderLeftActive = false;
            sliderRightActive = false;
            uiJumpPressedWhileAirborne = false;
            kbJumpPressedWhileAirborne = false;
            LastJumpSubmittedAsJumpSpike = false;
        }

        public void ClearAll()
        {
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
            Array.Clear(keyLatch, 0, ActionCount);
            ClearInjected();
            CancelDedicatedSpike();
            ResetTraceState();
        }

        public uint IdFor(SpikeAction a)
        {
            switch (a)
            {
                case SpikeAction.Left: return mapping.LeftId;
                case SpikeAction.Right: return mapping.RightId;
                case SpikeAction.Bump: return mapping.BumpId;
                case SpikeAction.Serve: return mapping.ServeId;
                case SpikeAction.Jump: return mapping.JumpId;
                case SpikeAction.SpikeLight: return mapping.SpikeLightId;
                case SpikeAction.SpikeMedium: return mapping.SpikeMediumId;
                case SpikeAction.SpikeStrong: return mapping.SpikeStrongId;
                default: throw new ArgumentOutOfRangeException(nameof(a), a, "Unknown action");
            }
        }

        /// <summary>
        /// Merges all sources into the native Input struct (configured button IDs). Called exactly
        /// once per native tick by SpikeSimulationManager. Consumes one-shot latches/pulses.
        /// </summary>
        public NativeSimInput BuildInput()
        {
            if (mapping == null) throw new InvalidOperationException("SpikeInputAdapter used before the button mapping was configured");

            if (!UIButtonInputTest.IsLayoutEditMode)
            {
                for (int bIdx = 0; bIdx < boundButtons.Count; bIdx++)
                {
                    var btn = boundButtons[bIdx];
                    if (btn == null || !btn.isActiveAndEnabled || !btn.IsPressed) continue;
                    int act = UiIdToAction(btn.ButtonId);
                    if (act == (int)SpikeAction.Left || act == (int)SpikeAction.Right || act == (int)SpikeAction.Bump)
                    {
                        uiHeld[act] = true;
                    }
                }
            }

            LastJumpSubmittedAsJumpSpike = false;
            LastTraceCount = 0;
            ushort currentSubmittedNativeMask = 0;
            NativeSimInput input = NativeSimInput.Empty;
            for (int i = 0; i < ActionCount; i++)
            {
                var a = (SpikeAction)i;
                SpikeInputSource sources = injected[i] ? SpikeInputSource.TestInjection : SpikeInputSource.None;
                if (!InjectionExclusive)
                {
                    bool sliderActive = (a == SpikeAction.Left && sliderLeftActive) ||
                                        (a == SpikeAction.Right && sliderRightActive);
                    if (uiHeld[i] || uiPulse[i] || sliderActive) sources |= SpikeInputSource.UiPointer;
                    if (keyLatch[i] || KeyboardHeld(a)) sources |= SpikeInputSource.Keyboard;
                }
                bool held = sources != SpikeInputSource.None;
                uiPulse[i] = false;
                keyLatch[i] = false;
                LastSubmittedActions[i] = held;
                LastSubmittedSources[i] = sources;
                if (held)
                {
                    uint buttonId = IdFor(a);
                    if (a == SpikeAction.Jump)
                    {
                        bool gameplayAirborneJump =
                            ((sources & SpikeInputSource.UiPointer) != 0 && uiJumpPressedWhileAirborne) ||
                            ((sources & SpikeInputSource.Keyboard) != 0 && kbJumpPressedWhileAirborne);
                        if (gameplayAirborneJump && IsPlayerAirborne())
                        {
                            buttonId = mapping.JumpSpikeId;
                            LastJumpSubmittedAsJumpSpike = true;
                        }
                        if (!uiHeld[i])
                        {
                            uiJumpPressedWhileAirborne = false;
                        }
                        kbJumpPressedWhileAirborne = false;
                    }
                    SetButton(ref input, (int)input.buttonCount, buttonId);
                    input.buttonCount++;

                    ushort bit = buttonId < MaxNativeMaskBits ? (ushort)(1u << (int)buttonId) : (ushort)0;
                    currentSubmittedNativeMask |= bit;
                    if (LastTraceCount < MaxTraceItemsPerTick)
                    {
                        InputEventKind kind = (bit != 0 && (prevSubmittedNativeMask & bit) != 0)
                            ? InputEventKind.Hold
                            : InputEventKind.Pressed;
                        string traceButtonName = (a == SpikeAction.Jump && LastJumpSubmittedAsJumpSpike)
                            ? JumpSpikeTraceName
                            : ActionTraceNames[i];
                        int traceUiButtonId = (sources & SpikeInputSource.UiPointer) != 0
                            ? (pulsedFromDedicatedSpike && a == activeDedicatedSpikeAction ? UiDedicatedSpike : UiButtonIdForAction(a))
                            : NoUiButtonId;
                        lastTraceItems[LastTraceCount++] = new InputTraceItem
                        {
                            buttonName = traceButtonName,
                            nativeButtonId = buttonId,
                            uiButtonId = traceUiButtonId,
                            source = sources,
                            eventKind = kind
                        };
                    }
                }
                else if (a == SpikeAction.Jump)
                {
                    if (!uiHeld[i])
                    {
                        uiJumpPressedWhileAirborne = false;
                    }
                    kbJumpPressedWhileAirborne = false;
                }
            }

            if (dedicatedSpikeHeld && LastTraceCount < MaxTraceItemsPerTick)
            {
                InputEventKind dsKind = prevDedicatedSpikeHeldInTick ? InputEventKind.Hold : InputEventKind.Pressed;
                string dsName = dedicatedSpikeSelecting ? GetSelectorTraceName(hoveredSpikeOption) : DedicatedSpikeTraceName;
                lastTraceItems[LastTraceCount++] = new InputTraceItem
                {
                    buttonName = dsName,
                    nativeButtonId = NoNativeButtonId,
                    uiButtonId = UiDedicatedSpike,
                    source = SpikeInputSource.UiPointer,
                    eventKind = dsKind
                };
            }

            prevSubmittedNativeMask = currentSubmittedNativeMask;
            prevDedicatedSpikeHeldInTick = dedicatedSpikeHeld;
            pulsedFromDedicatedSpike = false;

            int count = (int)input.buttonCount;
            var ids = submittedIdBuffers[count];
            for (int i = 0; i < count; i++) ids[i] = input.GetButton(i);
            LastSubmittedIds = ids;
            LastSubmitFrame = Time.frameCount;
            SubmitCount++;
            return input;
        }

        private static void SetButton(ref NativeSimInput input, int slot, uint id)
        {
            switch (slot)
            {
                case 0: input.btn0 = id; break;
                case 1: input.btn1 = id; break;
                case 2: input.btn2 = id; break;
                case 3: input.btn3 = id; break;
                case 4: input.btn4 = id; break;
                case 5: input.btn5 = id; break;
                case 6: input.btn6 = id; break;
                case 7: input.btn7 = id; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        public static string DescribeIds(uint[] ids) => ids == null || ids.Length == 0 ? "[]" : "[" + string.Join(",", ids) + "]";

        /// <summary>Diagnostic provenance only; this metadata never changes the native payload.</summary>
        public string BuildTraceSummary()
        {
            var rows = new List<string>();
            for (int i = 0; i < ActionCount; i++)
            {
                if (LastSubmittedSources[i] == SpikeInputSource.None) continue;
                var action = (SpikeAction)i;
                string uiButtonId = (LastSubmittedSources[i] & SpikeInputSource.UiPointer) != 0 ? i.ToString() : "none";
                uint nativeId = (action == SpikeAction.Jump && LastJumpSubmittedAsJumpSpike)
                    ? mapping.JumpSpikeId
                    : IdFor(action);
                rows.Add($"source={LastSubmittedSources[i]},uiButtonId={uiButtonId},mappedAction={action},configuredNativeId={nativeId}");
            }
            return rows.Count == 0 ? "[]" : "[" + string.Join(";", rows) + "]";
        }
    }
}
