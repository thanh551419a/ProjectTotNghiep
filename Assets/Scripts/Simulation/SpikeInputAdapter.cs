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
    public class SpikeInputAdapter : MonoBehaviour
    {
        public const int ActionCount = 8;

        /// <summary>UI button ids used by UIButtonInputTest components on the gameplay Canvas.</summary>
        public const int UiLeft = 0, UiRight = 1, UiBump = 2, UiServe = 3;
        public const int UiJump = 4, UiSpikeLight = 5, UiSpikeMedium = 6, UiSpikeStrong = 7;
        public const int UiDedicatedSpike = 8;

        public const float HoldThresholdSeconds = UIButtonInputTest.LongPressSeconds;
        public const float DefaultActionCooldownSeconds = 0.20f;
        public const float BumpServeCooldownSeconds = 0.30f;
        public const float SliderDeadzone = 0.12f;

        [SerializeField] private bool enableKeyboard = true;

        private ButtonMappingConfig mapping;
        private float nextAllowedActionTime;

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
        public bool SelectorOpen => selectorOpen;
        public SpikeAction? HoveredSpikeOption => hoveredSpikeOption;
        public bool PrimaryIsSpike => IsPlayerAirborne();
        public bool LastJumpSubmittedAsJumpSpike { get; private set; }
        public MovementControlMode CurrentMovementMode { get; private set; } = MovementControlMode.TwoButtons;
        public float SliderHorizontalAxis { get; private set; }
        public event Action<bool> OnSpikeSelectorChanged;
        public event Action<MovementControlMode> MovementModeChanged;

        /// <summary>When true, only test-injected actions are submitted (deterministic self-test).</summary>
        public bool InjectionExclusive { get; set; }

        public ButtonMappingConfig Mapping => mapping;
        public uint[] LastSubmittedIds { get; private set; } = Array.Empty<uint>();
        public bool[] LastSubmittedActions { get; } = new bool[ActionCount];
        public SpikeInputSource[] LastSubmittedSources { get; } = new SpikeInputSource[ActionCount];
        public int LastSubmitFrame { get; private set; } = -1;
        public long SubmitCount { get; private set; }

        public void Configure(ButtonMappingConfig config)
        {
            mapping = config ?? throw new ArgumentNullException(nameof(config));
            nextAllowedActionTime = 0f;
        }

        private void Reset()
        {
            nextAllowedActionTime = 0f;
        }

        private void OnEnable()
        {
            nextAllowedActionTime = 0f;
            UIButtonInputTest.LayoutEditModeChanged += HandleLayoutEditModeChanged;
            BindUiButtons(gameObject.scene);
            if (UIButtonInputTest.IsLayoutEditMode)
                HandleLayoutEditModeChanged(true);
        }

        private void OnDisable()
        {
            nextAllowedActionTime = 0f;
            UIButtonInputTest.LayoutEditModeChanged -= HandleLayoutEditModeChanged;
            foreach (var b in boundButtons)
            {
                if (b != null) b.OnInput -= HandleUiInput;
                if (b != null) b.OnHold -= HandleUiHold;
                if (b != null) b.OnLongPress -= HandleUiLongPress;
                if (b != null) b.OnHover -= HandleUiHover;
            }
            boundButtons.Clear();
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
            SliderHorizontalAxis = 0f;
            sliderLeftActive = false;
            sliderRightActive = false;
            uiJumpPressedWhileAirborne = false;
            kbJumpPressedWhileAirborne = false;
            LastJumpSubmittedAsJumpSpike = false;
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
                    if (boundButtons.Contains(b)) continue;
                    b.OnInput += HandleUiInput;
                    b.OnHold += HandleUiHold;
                    b.OnLongPress += HandleUiLongPress;
                    b.OnHover += HandleUiHover;
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

        private void CancelDedicatedSpike()
        {
            dedicatedSpikeHeld = false;
            dedicatedSpikeSelecting = false;
            hoveredSpikeOption = null;
            SetSelectorOpen(false);
        }

        private static bool IsNativePlayerActionCoolingDown()
        {
            var table = SimulationDataTable.Instance;
            return table != null && table.IsInitialized &&
                   table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out var player) &&
                   player.hasCharacterActionState && player.actionRemainTime > 0f;
        }

        public bool IsActionOnCooldown()
        {
            return IsNativePlayerActionCoolingDown() || (Application.isPlaying && Time.unscaledTime < nextAllowedActionTime);
        }

        private static float CooldownDurationFor(SpikeAction action)
        {
            switch (action)
            {
                case SpikeAction.Serve:
                    return BumpServeCooldownSeconds;
                case SpikeAction.SpikeLight:
                case SpikeAction.SpikeMedium:
                case SpikeAction.SpikeStrong:
                    return DefaultActionCooldownSeconds;
                default:
                    return 0f;
            }
        }

        private bool TryConsumeActionCooldown(SpikeAction action)
        {
            float duration = CooldownDurationFor(action);
            if (duration <= 0f) return true;
            if (IsActionOnCooldown()) return false;
            if (Application.isPlaying)
            {
                nextAllowedActionTime = Time.unscaledTime + duration;
            }
            return true;
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
                    if (!dedicatedSpikeHeld)
                    {
                        EndDedicatedSpike(Time.unscaledTime);
                        return;
                    }
                    if (selectorOpen && !hoveredSpikeOption.HasValue)
                    {
                        hoveredSpikeOption = TryFindHoveredSpikeOptionFromScreenPointer();
                    }
                    if (IsActionOnCooldown())
                    {
                        CancelDedicatedSpike();
                        return;
                    }
                    EndDedicatedSpike(Time.unscaledTime);
                    if (Application.isPlaying &&
                        (uiPulse[(int)SpikeAction.SpikeLight] ||
                         uiPulse[(int)SpikeAction.SpikeMedium] ||
                         uiPulse[(int)SpikeAction.SpikeStrong]))
                    {
                        nextAllowedActionTime = Time.unscaledTime + DefaultActionCooldownSeconds;
                    }
                }
                return;
            }
            if (uiButtonId >= UiSpikeLight && uiButtonId <= UiSpikeStrong)
            {
                if (!isPressed) return;
                if (IsActionOnCooldown())
                {
                    CancelDedicatedSpike();
                    return;
                }
                if (SelectSpikeStrength((SpikeAction)UiIdToAction(uiButtonId)) && Application.isPlaying)
                {
                    nextAllowedActionTime = Time.unscaledTime + DefaultActionCooldownSeconds;
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
                bool airborne = IsPlayerAirborne();
                if (airborne && IsActionOnCooldown())
                {
                    return;
                }
                if (airborne && Application.isPlaying)
                {
                    nextAllowedActionTime = Time.unscaledTime + DefaultActionCooldownSeconds;
                }
                uiJumpPressedWhileAirborne = airborne;
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
            if (!TryConsumeActionCooldown(mappedAction)) return;
            uiPulse[a] = true;
            uiHeld[a] = false;
        }

        private void HandleUiHold(int uiButtonId)
        {
            if (UIButtonInputTest.IsLayoutEditMode) return;
            if (uiButtonId == UiDedicatedSpike)
            {
                if (dedicatedSpikeHeld)
                {
                    AdvanceDedicatedSpikeHold(Mathf.Max(0f, Time.unscaledTime - dedicatedSpikeDownTime));
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
        }

        private void HandleUiHover(int uiButtonId, bool isHovered)
        {
            if (UIButtonInputTest.IsLayoutEditMode) return;
            if (uiButtonId < UiSpikeLight || uiButtonId > UiSpikeStrong) return;
            var action = (SpikeAction)UiIdToAction(uiButtonId);
            if (isHovered && selectorOpen)
            {
                hoveredSpikeOption = action;
            }
            else if (!isHovered && hoveredSpikeOption == action)
            {
                hoveredSpikeOption = null;
            }
        }

        private SpikeAction? TryFindHoveredSpikeOptionFromScreenPointer()
        {
            Vector2 screenPos = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            for (int i = 0; i < boundButtons.Count; i++)
            {
                var btn = boundButtons[i];
                if (btn == null || !btn.isActiveAndEnabled) continue;
                int id = btn.ButtonId;
                if (id < UiSpikeLight || id > UiSpikeStrong) continue;
                var rt = btn.transform as RectTransform;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                {
                    return (SpikeAction)UiIdToAction(id);
                }
            }
            return null;
        }

        public void SetHoveredSpikeOption(SpikeAction? option)
        {
            if (!selectorOpen || (option.HasValue && (option.Value < SpikeAction.SpikeLight || option.Value > SpikeAction.SpikeStrong)))
            {
                hoveredSpikeOption = null;
                return;
            }
            hoveredSpikeOption = option;
        }

        /// <summary>Starts a dedicated Spike gesture. Time is unscaled seconds for deterministic EditMode tests.</summary>
        public void BeginDedicatedSpike(float unscaledTime)
        {
            dedicatedSpikeDownTime = unscaledTime;
            dedicatedSpikeHeld = true;
            dedicatedSpikeSelecting = false;
            hoveredSpikeOption = null;
        }

        /// <summary>Opens the strength selector once the gesture reaches the hold threshold.</summary>
        public void AdvanceDedicatedSpikeHold(float heldSeconds)
        {
            if (!dedicatedSpikeHeld || dedicatedSpikeSelecting || heldSeconds < HoldThresholdSeconds) return;
            dedicatedSpikeSelecting = true;
            SetSelectorOpen(true);
        }

        /// <summary>Ends a gesture; a short tap requests Medium once, releasing a hold submits any hovered option and closes the selector immediately.</summary>
        public void EndDedicatedSpike(float unscaledTime)
        {
            if (!dedicatedSpikeHeld)
            {
                hoveredSpikeOption = null;
                SetSelectorOpen(false);
                return;
            }
            AdvanceDedicatedSpikeHold(Mathf.Max(0f, unscaledTime - dedicatedSpikeDownTime));
            dedicatedSpikeHeld = false;
            if (!dedicatedSpikeSelecting)
            {
                uiPulse[(int)SpikeAction.SpikeMedium] = true;
            }
            else if (hoveredSpikeOption.HasValue)
            {
                uiPulse[(int)hoveredSpikeOption.Value] = true;
            }
            hoveredSpikeOption = null;
            SetSelectorOpen(false);
        }

        /// <summary>Chooses a visible selector option; returns false when the selector is closed.</summary>
        public bool SelectSpikeStrength(SpikeAction action)
        {
            if (!selectorOpen || action < SpikeAction.SpikeLight || action > SpikeAction.SpikeStrong) return false;
            uiPulse[(int)action] = true;
            dedicatedSpikeHeld = false;
            dedicatedSpikeSelecting = false;
            hoveredSpikeOption = null;
            SetSelectorOpen(false);
            return true;
        }

        private void SetSelectorOpen(bool value)
        {
            if (!value) hoveredSpikeOption = null;
            if (selectorOpen == value) return;
            selectorOpen = value;
            OnSpikeSelectorChanged?.Invoke(value);
        }

        private bool IsPlayerAirborne()
        {
            var table = SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized) return false;
            if (!table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out var player)) return false;
            if (!player.isGrounded) return true;
            if (player.airborneState != SimAirborneState.Grounded) return true;
            if (player.hasPosition && player.posY > SimulationEntityReader.COURT_FLOOR_Y + 0.5f) return true;
            return false;
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
            if (!enableKeyboard) return;
            // Latch key-downs so a tap shorter than one fixed step is never lost.
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) keyLatch[(int)SpikeAction.Left] = true;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) keyLatch[(int)SpikeAction.Right] = true;
            if (IsBumpKeyDown()) keyLatch[(int)SpikeAction.Bump] = true;

            bool airborne = IsPlayerAirborne();
            bool spaceDown = Input.GetKeyDown(KeyCode.Space);
            bool enterDown = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if ((enterDown || (spaceDown && !airborne)) && TryConsumeActionCooldown(SpikeAction.Serve))
            {
                keyLatch[(int)SpikeAction.Serve] = true;
            }
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || (spaceDown && airborne))
            {
                if (!airborne || !IsActionOnCooldown())
                {
                    if (airborne && Application.isPlaying)
                    {
                        nextAllowedActionTime = Time.unscaledTime + DefaultActionCooldownSeconds;
                    }
                    kbJumpPressedWhileAirborne = airborne;
                    keyLatch[(int)SpikeAction.Jump] = true;
                }
            }
            if (Input.GetKeyDown(KeyCode.Z) && TryConsumeActionCooldown(SpikeAction.SpikeLight)) keyLatch[(int)SpikeAction.SpikeLight] = true;
            if (Input.GetKeyDown(KeyCode.X) && TryConsumeActionCooldown(SpikeAction.SpikeMedium)) keyLatch[(int)SpikeAction.SpikeMedium] = true;
            if (Input.GetKeyDown(KeyCode.C) && TryConsumeActionCooldown(SpikeAction.SpikeStrong)) keyLatch[(int)SpikeAction.SpikeStrong] = true;
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
            nextAllowedActionTime = 0f;
            SliderHorizontalAxis = 0f;
            sliderLeftActive = false;
            sliderRightActive = false;
            uiJumpPressedWhileAirborne = false;
            kbJumpPressedWhileAirborne = false;
            LastJumpSubmittedAsJumpSpike = false;
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
                    if (btn == null || !btn.isActiveAndEnabled || !btn.AllowHold || !btn.IsPressed) continue;
                    int act = UiIdToAction(btn.ButtonId);
                    if (act == (int)SpikeAction.Left || act == (int)SpikeAction.Right || act == (int)SpikeAction.Bump)
                    {
                        uiHeld[act] = true;
                    }
                }
            }

            LastJumpSubmittedAsJumpSpike = false;
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
