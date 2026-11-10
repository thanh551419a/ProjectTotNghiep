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

        [SerializeField] private bool enableKeyboard = true;

        private ButtonMappingConfig mapping;

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
        private SpikeAction primaryPointerAction = SpikeAction.Jump;
        private float dedicatedSpikeDownTime;
        private bool dedicatedSpikeHeld;
        private bool dedicatedSpikeSelecting;
        private bool selectorOpen;
        private SpikeAction? hoveredSpikeOption;
        public bool SelectorOpen => selectorOpen;
        public SpikeAction? HoveredSpikeOption => hoveredSpikeOption;
        public bool PrimaryIsSpike => IsPlayerAirborne();
        public event Action<bool> OnSpikeSelectorChanged;

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
        }

        private void OnEnable()
        {
            BindUiButtons(gameObject.scene);
        }

        private void OnDisable()
        {
            foreach (var b in boundButtons)
            {
                if (b != null) b.OnInput -= HandleUiInput;
                if (b != null) b.OnLongPress -= HandleUiLongPress;
                if (b != null) b.OnHover -= HandleUiHover;
            }
            boundButtons.Clear();
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
            dedicatedSpikeHeld = false;
            dedicatedSpikeSelecting = false;
            hoveredSpikeOption = null;
            SetSelectorOpen(false);
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

        private void HandleUiInput(int uiButtonId, bool isPressed)
        {
            if (uiButtonId == UiDedicatedSpike)
            {
                if (isPressed) BeginDedicatedSpike(Time.unscaledTime);
                else EndDedicatedSpike(Time.unscaledTime);
                return;
            }
            if (uiButtonId >= UiSpikeLight && uiButtonId <= UiSpikeStrong)
            {
                if (isPressed) SelectSpikeStrength((SpikeAction)UiIdToAction(uiButtonId));
                return;
            }
            if (uiButtonId == UiJump)
            {
                if (isPressed) primaryPointerAction = IsPlayerAirborne() ? SpikeAction.SpikeMedium : SpikeAction.Jump;
                int primary = (int)primaryPointerAction;
                if (isPressed && !uiHeld[primary]) uiPulse[primary] = true;
                uiHeld[primary] = isPressed;
                return;
            }
            int a = UiIdToAction(uiButtonId);
            if (a < 0) return;
            if (isPressed && !uiHeld[a]) uiPulse[a] = true; // guarantees at least one tick for very short taps
            uiHeld[a] = isPressed;
        }

        private void HandleUiLongPress(int uiButtonId)
        {
            if (uiButtonId != UiDedicatedSpike) return;
            AdvanceDedicatedSpikeHold(UIButtonInputTest.LongPressSeconds);
        }

        private void HandleUiHover(int uiButtonId, bool isHovered)
        {
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
            if (!dedicatedSpikeHeld || dedicatedSpikeSelecting || heldSeconds < UIButtonInputTest.LongPressSeconds) return;
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

        private static bool IsPlayerAirborne()
        {
            var table = SimulationDataTable.Instance;
            return table != null && table.IsInitialized &&
                   table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out var player) && !player.isGrounded;
        }

        private void Update()
        {
            if (!enableKeyboard) return;
            // Latch key-downs so a tap shorter than one fixed step is never lost.
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) keyLatch[(int)SpikeAction.Left] = true;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) keyLatch[(int)SpikeAction.Right] = true;
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.DownArrow)) keyLatch[(int)SpikeAction.Bump] = true;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) keyLatch[(int)SpikeAction.Serve] = true;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) keyLatch[(int)SpikeAction.Jump] = true;
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
                case SpikeAction.Bump: return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.DownArrow);
                case SpikeAction.Serve: return Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
                case SpikeAction.Jump: return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                case SpikeAction.SpikeLight: return Input.GetKey(KeyCode.Z);
                case SpikeAction.SpikeMedium: return Input.GetKey(KeyCode.X);
                case SpikeAction.SpikeStrong: return Input.GetKey(KeyCode.C);
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Development test injection (used by GameplaySelfTestController only)
        // ------------------------------------------------------------------
        public void SetInjected(SpikeAction action, bool held) => injected[(int)action] = held;
        public bool GetInjected(SpikeAction action) => injected[(int)action];
        public void ClearInjected() => Array.Clear(injected, 0, ActionCount);

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

            NativeSimInput input = NativeSimInput.Empty;
            for (int i = 0; i < ActionCount; i++)
            {
                var a = (SpikeAction)i;
                SpikeInputSource sources = injected[i] ? SpikeInputSource.TestInjection : SpikeInputSource.None;
                if (!InjectionExclusive)
                {
                    if (uiHeld[i] || uiPulse[i]) sources |= SpikeInputSource.UiPointer;
                    if (keyLatch[i] || KeyboardHeld(a)) sources |= SpikeInputSource.Keyboard;
                }
                bool held = sources != SpikeInputSource.None;
                uiPulse[i] = false;
                keyLatch[i] = false;
                LastSubmittedActions[i] = held;
                LastSubmittedSources[i] = sources;
                if (held)
                {
                    SetButton(ref input, (int)input.buttonCount, IdFor(a));
                    input.buttonCount++;
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
                rows.Add($"source={LastSubmittedSources[i]},uiButtonId={uiButtonId},mappedAction={action},configuredNativeId={IdFor(action)}");
            }
            return rows.Count == 0 ? "[]" : "[" + string.Join(";", rows) + "]";
        }
    }
}
