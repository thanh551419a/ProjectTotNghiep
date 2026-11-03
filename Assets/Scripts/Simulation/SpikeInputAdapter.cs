using System;
using System.Collections.Generic;
using Native;
using UnityEngine;

namespace Simulation
{
    /// <summary>Logical player actions exposed by the configured native button mapping.</summary>
    public enum SpikeAction
    {
        Left = 0,
        Right = 1,
        Bump = 2,
        Serve = 3
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
        public const int ActionCount = 4;

        /// <summary>UI button ids used by UIButtonInputTest components on the gameplay Canvas.</summary>
        public const int UiLeft = 0, UiRight = 1, UiBump = 2, UiServe = 3;

        [Tooltip("Seconds after the last UI 'held' event before a UI action is considered released (missed pointer-up guard).")]
        [SerializeField] private float uiHoldTimeout = 0.25f;
        [SerializeField] private bool enableKeyboard = true;

        private ButtonMappingConfig mapping;

        private readonly bool[] uiHeld = new bool[ActionCount];
        private readonly float[] uiLastEvent = new float[ActionCount];
        private readonly bool[] uiPulse = new bool[ActionCount];
        private readonly bool[] keyLatch = new bool[ActionCount];
        private readonly bool[] injected = new bool[ActionCount];
        private readonly List<UIButtonInputTest> boundButtons = new List<UIButtonInputTest>();

        /// <summary>When true, only test-injected actions are submitted (deterministic self-test).</summary>
        public bool InjectionExclusive { get; set; }

        public ButtonMappingConfig Mapping => mapping;
        public uint[] LastSubmittedIds { get; private set; } = Array.Empty<uint>();
        public bool[] LastSubmittedActions { get; } = new bool[ActionCount];
        public int LastSubmitFrame { get; private set; } = -1;
        public long SubmitCount { get; private set; }

        public void Configure(ButtonMappingConfig config)
        {
            mapping = config ?? throw new ArgumentNullException(nameof(config));
        }

        private void OnEnable()
        {
            BindUiButtons();
        }

        private void OnDisable()
        {
            foreach (var b in boundButtons)
            {
                if (b != null) b.OnInput -= HandleUiInput;
            }
            boundButtons.Clear();
            Array.Clear(uiHeld, 0, ActionCount);
            Array.Clear(uiPulse, 0, ActionCount);
        }

        /// <summary>Subscribes to every UIButtonInputTest in the loaded scene(s) exactly once.</summary>
        public void BindUiButtons()
        {
            foreach (var b in FindObjectsByType<UIButtonInputTest>(FindObjectsInactive.Include))
            {
                if (boundButtons.Contains(b)) continue;
                b.OnInput += HandleUiInput;
                boundButtons.Add(b);
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
                default: return -1;
            }
        }

        private void HandleUiInput(int uiButtonId, bool isPressed)
        {
            int a = UiIdToAction(uiButtonId);
            if (a < 0) return;
            if (isPressed && !uiHeld[a]) uiPulse[a] = true; // guarantees at least one tick for very short taps
            uiHeld[a] = isPressed;
            uiLastEvent[a] = Time.unscaledTime;
        }

        private void Update()
        {
            if (!enableKeyboard) return;
            // Latch key-downs so a tap shorter than one fixed step is never lost.
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) keyLatch[(int)SpikeAction.Left] = true;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) keyLatch[(int)SpikeAction.Right] = true;
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.DownArrow)) keyLatch[(int)SpikeAction.Bump] = true;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) keyLatch[(int)SpikeAction.Serve] = true;
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
                default: return mapping.ServeId;
            }
        }

        /// <summary>
        /// Merges all sources into the native Input struct (configured button IDs). Called exactly
        /// once per native tick by SpikeSimulationManager. Consumes one-shot latches/pulses.
        /// </summary>
        public NativeSimInput BuildInput()
        {
            if (mapping == null) throw new InvalidOperationException("SpikeInputAdapter used before the button mapping was configured");

            float now = Time.unscaledTime;
            var ids = new List<uint>(ActionCount);
            for (int i = 0; i < ActionCount; i++)
            {
                var a = (SpikeAction)i;
                bool held;
                if (InjectionExclusive)
                {
                    held = injected[i];
                }
                else
                {
                    if (uiHeld[i] && now - uiLastEvent[i] > uiHoldTimeout) uiHeld[i] = false; // stuck-button guard
                    held = injected[i] || uiHeld[i] || uiPulse[i] || keyLatch[i] || KeyboardHeld(a);
                }
                uiPulse[i] = false;
                keyLatch[i] = false;
                LastSubmittedActions[i] = held;
                if (held) ids.Add(IdFor(a));
            }

            LastSubmittedIds = ids.ToArray();
            LastSubmitFrame = Time.frameCount;
            SubmitCount++;
            return ids.Count == 0 ? NativeSimInput.Empty : NativeSimInput.FromButtons(LastSubmittedIds);
        }

        public static string DescribeIds(uint[] ids) => ids == null || ids.Length == 0 ? "[]" : "[" + string.Join(",", ids) + "]";
    }
}
