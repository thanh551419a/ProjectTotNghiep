using Native;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Simulation
{
    /// <summary>
    /// Manages in-game HUD button customization (position drag, scale slider/buttons, reset, and PlayerPrefs persistence).
    /// Toolbar and toggle buttons intentionally use LayoutToolbarButton instead of UIButtonInputTest so
    /// gameplay UIButtonInputTest queries always return strictly the 9 gameplay buttons (0..8).
    /// Docked in dedicated non-overlapping screen regions (Top-Left toggle pill + below-scoreboard floating dock).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionButtonLayoutController : MonoBehaviour
    {
        public const int ButtonCount = 9;
        public const int MovementSliderControlId = ButtonCount; // Virtual ID 9 for MovementHorizontalSlider in Edit HUD mode
        public const int EditableControlCount = ButtonCount + 1;
        public const string ToggleButtonName = "LayoutEditToggleButton";
        public const string MoveModeToggleButtonName = "MoveModeToggleButton";
        public const string MovementSliderName = "MovementHorizontalSlider";
        public const string RamStatsToggleButtonName = "RamStatsToggleButton";
        public const string RamStatsPanelName = "RamStatsEditorPanel";
        public const string ToolbarPanelName = "LayoutEditorToolbar";
        public const string ScaleSliderName = "SizeSlider";
        public const string HasSavedLayoutPrefKey = "SpikeHud_HasSavedCustomLayout";
        private const string PosXPrefPrefix = "SpikeHud_BtnPosX_";
        private const string PosYPrefPrefix = "SpikeHud_BtnPosY_";
        private const string ScalePrefPrefix = "SpikeHud_BtnScale_";
        private const string SliderPosXPrefKey = "SpikeHud_SliderPosX";
        private const string SliderPosYPrefKey = "SpikeHud_SliderPosY";
        private const string SliderScalePrefKey = "SpikeHud_SliderScale";

        public const float MinScale = ActionButtonPressView.MinCustomScale;
        public const float MaxScale = ActionButtonPressView.MaxCustomScale;
        public const float SliderMinScale = 0.55f;
        public const float SliderMaxScale = 2.60f;
        public const float ScaleStep = 0.10f;
        public const float MinStatValue = 20f;
        public const float MaxStatValue = 300f;
        public const float StatStep = 10f;

        private static readonly string[] ButtonDisplayNames =
        {
            "MOVE LEFT",
            "MOVE RIGHT",
            "RECEIVE",
            "SERVE",
            "JUMP / PRIMARY",
            "SPIKE LIGHT",
            "SPIKE MEDIUM",
            "SPIKE STRONG",
            "SPIKE",
            "MOVEMENT SLIDER"
        };

        // "The Spike Cross" Broadcast Obsidian & Electric Cyan/Gold Palette
        private static readonly Color32 ToggleNormalColor = new Color32(14, 21, 37, 238);     // #0E1525 @ 93%
        private static readonly Color32 ToggleActiveColor = new Color32(16, 185, 129, 248);   // Emerald #10B981 when Done/Active
        private static readonly Color32 ToggleOutlineNormal = new Color32(56, 189, 248, 210); // Electric Cyan #38BDF8
        private static readonly Color32 ToggleOutlineActive = new Color32(245, 158, 11, 255); // Warm Gold #F59E0B
        private static readonly Color32 ToolbarBgColor = new Color32(14, 21, 37, 242);        // #0E1525 @ 95%
        private static readonly Color32 ToolbarBorderColor = new Color32(56, 189, 248, 180);  // Cyan border
        private static readonly Color32 ToolbarAccentStripeColor = new Color32(245, 158, 11, 255); // Gold top bar
        private static readonly Color32 ToolbarBtnColor = new Color32(30, 41, 59, 248);       // Slate pill #1E293B
        private static readonly Color32 ToolbarAccentBtnColor = new Color32(16, 185, 129, 248); // Emerald #10B981
        private static readonly Color32 ToolbarWarnBtnColor = new Color32(239, 68, 68, 245);  // Coral Red #EF4444
        private static readonly Color32 SliderTrackColor = new Color32(15, 23, 42, 255);
        private static readonly Color32 SliderFillColor = new Color32(56, 189, 248, 255);     // Electric Cyan #38BDF8
        private static readonly Color32 SliderKnobColor = new Color32(245, 158, 11, 255);     // Warm Gold #F59E0B

        private Canvas canvas;
        private RectTransform canvasRect;
        private float uiScale = 1f;
        private Font uiFont;
        private SpikeInputAdapter inputAdapter;

        private readonly ActionButtonPressView[] pressViews = new ActionButtonPressView[ButtonCount];
        private readonly RectTransform[] buttonRects = new RectTransform[ButtonCount];
        private readonly Vector2[] defaultAnchoredPositions = new Vector2[ButtonCount];
        private readonly Vector2[] currentAnchoredPositions = new Vector2[ButtonCount];
        private readonly float[] currentScales = new float[ButtonCount];

        private RectTransform toggleButtonRect;
        private Image toggleButtonImage;
        private Outline toggleButtonOutline;
        private Text toggleButtonLabel;
        private RectTransform toolbarRect;
        private Text selectedInfoLabel;
        private Text hintSubLabel;
        private LayoutScaleSlider scaleSlider;

        // Feature A: Movement Mode Toggle + Horizontal Slider
        private RectTransform moveModeToggleButtonRect;
        private Image moveModeToggleImage;
        private Outline moveModeToggleOutline;
        private Text moveModeToggleLabel;
        private RectTransform movementSliderRect;
        private MovementHorizontalSliderControl movementSlider;
        private Outline movementSliderOutline;
        private Vector2 defaultSliderAnchoredPosition;
        private Vector2 currentSliderAnchoredPosition;
        private float currentSliderScale = 1f;
        private SpikeInputAdapter subscribedAdapter;

        // Feature B: In-RAM Character Stats Editor
        private RectTransform ramStatsToggleButtonRect;
        private Image ramStatsToggleImage;
        private Outline ramStatsToggleOutline;
        private Text ramStatsToggleLabel;
        private RectTransform ramStatsPanelRect;
        private Image targetPlayerBtnImage;
        private Image targetOpponentBtnImage;
        private readonly LayoutScaleSlider[] statSliders = new LayoutScaleSlider[4];
        private readonly Text[] statValueLabels = new Text[4];
        private Text ramStatsStatusLabel;
        private bool ramStatsEditorOpen;
        private uint selectedStatsEntityId = 0u;
        private float curAtk = 180f;
        private float curDef = 80f;
        private float curSpd = 90f;
        private float curJmp = 95f;

        private int selectedButtonId = 4;
        private bool subscribedEditMode;

        public bool IsEditMode => UIButtonInputTest.IsLayoutEditMode;
        public int SelectedButtonId => selectedButtonId;
        public RectTransform ToggleButtonRect => toggleButtonRect;
        public RectTransform ToolbarRect => toolbarRect;
        public LayoutScaleSlider ScaleSlider => scaleSlider;
        public RectTransform MoveModeToggleButtonRect => moveModeToggleButtonRect;
        public RectTransform MovementSliderRect => movementSliderRect;
        public MovementHorizontalSliderControl MovementSlider => movementSlider;
        public RectTransform RamStatsToggleButtonRect => ramStatsToggleButtonRect;
        public RectTransform RamStatsPanelRect => ramStatsPanelRect;
        public bool IsRamStatsEditorOpen => ramStatsEditorOpen;
        public uint SelectedStatsEntityId => selectedStatsEntityId;

        public static bool HasSavedCustomLayout => PlayerPrefs.GetInt(HasSavedLayoutPrefKey, 0) == 1;

        public static void ClearSavedCustomLayout()
        {
            PlayerPrefs.DeleteKey(HasSavedLayoutPrefKey);
            for (int id = 0; id < ButtonCount; id++)
            {
                PlayerPrefs.DeleteKey(PosXPrefPrefix + id);
                PlayerPrefs.DeleteKey(PosYPrefPrefix + id);
                PlayerPrefs.DeleteKey(ScalePrefPrefix + id);
            }
            PlayerPrefs.DeleteKey(SliderPosXPrefKey);
            PlayerPrefs.DeleteKey(SliderPosYPrefKey);
            PlayerPrefs.DeleteKey(SliderScalePrefKey);
        }

        public void Configure(Canvas targetCanvas, UIButtonInputTest[] buttons, float currentUiScale, Font font, SpikeInputAdapter adapter = null)
        {
            canvas = targetCanvas;
            canvasRect = targetCanvas != null ? (RectTransform)targetCanvas.transform : null;
            uiScale = Mathf.Max(0.25f, currentUiScale);
            uiFont = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (adapter != null)
            {
                inputAdapter = adapter;
            }
            else if (inputAdapter == null && SpikeSimulationManager.Instance != null)
            {
                inputAdapter = SpikeSimulationManager.Instance.InputAdapter;
            }

            for (int id = 0; id < ButtonCount; id++)
            {
                UIButtonInputTest btn = FindButton(buttons, id);
                if (btn == null)
                {
                    continue;
                }

                RectTransform rect = (RectTransform)btn.transform;
                buttonRects[id] = rect;

                ActionButtonPressView pressView = btn.GetComponent<ActionButtonPressView>();
                if (pressView == null)
                {
                    pressView = btn.gameObject.AddComponent<ActionButtonPressView>();
                }
                pressView.Configure(btn, uiScale);
                pressViews[id] = pressView;

                defaultAnchoredPositions[id] = rect.anchoredPosition;
                currentAnchoredPositions[id] = rect.anchoredPosition;
                currentScales[id] = 1f;
                pressView.SetBaseLayout(defaultAnchoredPositions[id], 1f);

                ActionButtonDragHandle dragHandle = btn.GetComponent<ActionButtonDragHandle>();
                if (dragHandle == null)
                {
                    dragHandle = btn.gameObject.AddComponent<ActionButtonDragHandle>();
                }
                dragHandle.Configure(this, id);
            }

            EnsureEditorHud();
            LoadSavedLayoutIfPresent();
            SubscribeEditMode();
            SelectStatsEntity(selectedStatsEntityId);
            RefreshMovementModeVisuals();
            RefreshRamStatsVisuals();
            RefreshEditModeVisuals(UIButtonInputTest.IsLayoutEditMode);
        }

        private void OnEnable()
        {
            SubscribeEditMode();
            RefreshMovementModeVisuals();
            RefreshRamStatsVisuals();
            RefreshEditModeVisuals(UIButtonInputTest.IsLayoutEditMode);
        }

        private void OnDisable()
        {
            UnsubscribeEditMode();
        }

        private void SubscribeEditMode()
        {
            if (!subscribedEditMode)
            {
                UIButtonInputTest.LayoutEditModeChanged += OnLayoutEditModeChanged;
                subscribedEditMode = true;
            }

            if (subscribedAdapter != inputAdapter)
            {
                if (subscribedAdapter != null)
                {
                    subscribedAdapter.MovementModeChanged -= OnMovementModeChanged;
                }
                subscribedAdapter = inputAdapter;
                if (subscribedAdapter != null)
                {
                    subscribedAdapter.MovementModeChanged += OnMovementModeChanged;
                }
            }
        }

        private void UnsubscribeEditMode()
        {
            if (subscribedEditMode)
            {
                UIButtonInputTest.LayoutEditModeChanged -= OnLayoutEditModeChanged;
                subscribedEditMode = false;
            }

            if (subscribedAdapter != null)
            {
                subscribedAdapter.MovementModeChanged -= OnMovementModeChanged;
                subscribedAdapter = null;
            }
        }

        private void OnMovementModeChanged(MovementControlMode mode)
        {
            RefreshMovementModeVisuals();
        }

        private void OnLayoutEditModeChanged(bool editMode)
        {
            if (editMode && ramStatsEditorOpen)
            {
                ramStatsEditorOpen = false;
                RefreshRamStatsVisuals();
            }
            if (editMode && SpikeSimulationManager.Instance != null && SpikeSimulationManager.Instance.IsPaused)
            {
                SpikeSimulationManager.Instance.SetPaused(false);
            }
            RefreshEditModeVisuals(editMode);
        }

        public void SetEditMode(bool enabled)
        {
            if (!enabled)
            {
                SaveCustomLayout();
            }
            UIButtonInputTest.SetLayoutEditMode(enabled);
        }

        // ------------------------------------------------------------------
        // Feature A: Movement Mode Switching (2 Buttons <-> Horizontal Slider)
        // ------------------------------------------------------------------
        public void SetMovementMode(MovementControlMode mode)
        {
            if (inputAdapter == null && SpikeSimulationManager.Instance != null)
            {
                inputAdapter = SpikeSimulationManager.Instance.InputAdapter;
            }
            if (inputAdapter != null)
            {
                inputAdapter.SetMovementControlMode(mode);
            }
            RefreshMovementModeVisuals();
        }

        public void ToggleMovementMode()
        {
            if (inputAdapter == null && SpikeSimulationManager.Instance != null)
            {
                inputAdapter = SpikeSimulationManager.Instance.InputAdapter;
            }
            if (inputAdapter != null)
            {
                inputAdapter.ToggleMovementControlMode();
            }
            RefreshMovementModeVisuals();
        }

        // ------------------------------------------------------------------
        // Feature B: Live In-RAM Character Stats Editor
        // ------------------------------------------------------------------
        public void OpenRamStatsEditor(bool open)
        {
            if (open && UIButtonInputTest.IsLayoutEditMode)
            {
                SetEditMode(false);
            }
            ramStatsEditorOpen = open;
            if (open)
            {
                SelectStatsEntity(selectedStatsEntityId);
            }
            RefreshRamStatsVisuals();
        }

        public void ToggleRamStatsEditor()
        {
            OpenRamStatsEditor(!ramStatsEditorOpen);
        }

        public void SelectStatsEntity(uint entityId)
        {
            selectedStatsEntityId = entityId == 3u ? 3u : 0u;
            SpikeNativeBridge.TryGetCharacterStats(selectedStatsEntityId, out curAtk, out curDef, out curSpd, out curJmp);
            curAtk = Mathf.Clamp(curAtk, MinStatValue, MaxStatValue);
            curDef = Mathf.Clamp(curDef, MinStatValue, MaxStatValue);
            curSpd = Mathf.Clamp(curSpd, MinStatValue, MaxStatValue);
            curJmp = Mathf.Clamp(curJmp, MinStatValue, MaxStatValue);
            RefreshRamStatsSlidersWithoutNotify();
            if (ramStatsStatusLabel != null)
            {
                string role = selectedStatsEntityId == 3u ? "OPPONENT (ID 3)" : "PLAYER (ID 0)";
                ramStatsStatusLabel.text = "LIVE IN RAM • " + role;
            }
        }

        public void ApplyCharacterStatsToRam(uint entityId, float atk, float def, float spd, float jmp)
        {
            uint targetId = entityId == 3u ? 3u : 0u;
            float clampedAtk = Mathf.Clamp(Mathf.Round(atk), MinStatValue, MaxStatValue);
            float clampedDef = Mathf.Clamp(Mathf.Round(def), MinStatValue, MaxStatValue);
            float clampedSpd = Mathf.Clamp(Mathf.Round(spd), MinStatValue, MaxStatValue);
            float clampedJmp = Mathf.Clamp(Mathf.Round(jmp), MinStatValue, MaxStatValue);

            SpikeNativeBridge.TrySetCharacterStats(targetId, clampedAtk, clampedDef, clampedSpd, clampedJmp);

            if (targetId == selectedStatsEntityId)
            {
                curAtk = clampedAtk;
                curDef = clampedDef;
                curSpd = clampedSpd;
                curJmp = clampedJmp;
                RefreshRamStatsSlidersWithoutNotify();
                if (ramStatsStatusLabel != null)
                {
                    string role = targetId == 3u ? "OPPONENT (ID 3)" : "PLAYER (ID 0)";
                    ramStatsStatusLabel.text = "RAM APPLIED • " + role;
                }
            }
        }

        public void ResetCharacterStatsInRam()
        {
            SpikeNativeBridge.TryResetCharacterStats();
            SelectStatsEntity(selectedStatsEntityId);
            if (ramStatsStatusLabel != null)
            {
                ramStatsStatusLabel.text = "RAM RESET TO DEFAULT";
            }
        }

        public void GetDisplayedCharacterStats(out float atk, out float def, out float spd, out float jmp)
        {
            atk = curAtk;
            def = curDef;
            spd = curSpd;
            jmp = curJmp;
        }

        private void AdjustStatByStep(int statIndex, float delta)
        {
            float atk = curAtk, def = curDef, spd = curSpd, jmp = curJmp;
            switch (statIndex)
            {
                case 0: atk += delta; break;
                case 1: jmp += delta; break;
                case 2: spd += delta; break;
                case 3: def += delta; break;
            }
            ApplyCharacterStatsToRam(selectedStatsEntityId, atk, def, spd, jmp);
        }

        private void OnStatSliderNormalizedChanged(int statIndex, float normalized)
        {
            float val = Mathf.Round(Mathf.Lerp(MinStatValue, MaxStatValue, normalized));
            float atk = curAtk, def = curDef, spd = curSpd, jmp = curJmp;
            switch (statIndex)
            {
                case 0: atk = val; break;
                case 1: jmp = val; break;
                case 2: spd = val; break;
                case 3: def = val; break;
            }
            ApplyCharacterStatsToRam(selectedStatsEntityId, atk, def, spd, jmp);
        }

        public void SelectButton(int buttonId)
        {
            if (buttonId < 0 || buttonId >= EditableControlCount)
            {
                return;
            }

            selectedButtonId = buttonId;
            for (int id = 0; id < ButtonCount; id++)
            {
                if (pressViews[id] != null)
                {
                    pressViews[id].SetSelectedHighlight(UIButtonInputTest.IsLayoutEditMode && id == selectedButtonId);
                }
            }

            RefreshMovementSliderHighlight(UIButtonInputTest.IsLayoutEditMode && selectedButtonId == MovementSliderControlId);
            RefreshToolbarInfo();
        }

        private void RefreshMovementSliderHighlight(bool selected)
        {
            if (movementSliderOutline != null)
            {
                movementSliderOutline.enabled = selected;
            }
        }

        public Vector2 GetButtonAnchoredPosition(int buttonId)
        {
            if (buttonId == MovementSliderControlId)
            {
                return currentSliderAnchoredPosition;
            }
            if (buttonId < 0 || buttonId >= ButtonCount)
            {
                return Vector2.zero;
            }

            return currentAnchoredPositions[buttonId];
        }

        public float GetButtonScale(int buttonId)
        {
            if (buttonId == MovementSliderControlId)
            {
                return currentSliderScale;
            }
            if (buttonId < 0 || buttonId >= ButtonCount)
            {
                return 1f;
            }

            return currentScales[buttonId];
        }

        public void MoveButtonAnchoredPosition(int buttonId, Vector2 anchoredPos)
        {
            SetButtonAnchoredPosition(buttonId, anchoredPos, true);
        }

        public void SetButtonAnchoredPosition(int buttonId, Vector2 anchoredPos, bool saveImmediately = false)
        {
            if (buttonId == MovementSliderControlId)
            {
                if (movementSliderRect == null)
                {
                    return;
                }
                currentSliderAnchoredPosition = anchoredPos;
                ApplyMovementSliderBaseLayout(currentSliderAnchoredPosition, currentSliderScale);
                if (buttonId == selectedButtonId)
                {
                    RefreshToolbarInfo();
                }
                if (saveImmediately)
                {
                    SaveCustomLayout();
                }
                return;
            }

            if (buttonId < 0 || buttonId >= ButtonCount || pressViews[buttonId] == null)
            {
                return;
            }

            currentAnchoredPositions[buttonId] = anchoredPos;
            pressViews[buttonId].SetBaseLayout(anchoredPos, currentScales[buttonId]);
            if (buttonId == selectedButtonId)
            {
                RefreshToolbarInfo();
            }

            if (saveImmediately)
            {
                SaveCustomLayout();
            }
        }

        private void ApplyMovementSliderBaseLayout(Vector2 anchoredPos, float scale)
        {
            if (movementSliderRect == null)
            {
                return;
            }
            movementSliderRect.anchoredPosition = anchoredPos;
            movementSliderRect.localScale = new Vector3(scale, scale, 1f);
        }

        public void SetButtonScreenPosition(int buttonId, Vector2 screenPos, bool saveImmediately = false)
        {
            if (buttonId == MovementSliderControlId)
            {
                if (movementSliderRect == null || canvasRect == null)
                {
                    return;
                }

                float halfW = Mathf.Max(36f, movementSliderRect.sizeDelta.x * canvas.scaleFactor * currentSliderScale * 0.5f);
                float halfH = Mathf.Max(24f, movementSliderRect.sizeDelta.y * canvas.scaleFactor * currentSliderScale * 0.5f);
                float minX = halfW;
                float maxX = Mathf.Max(minX, Screen.width - halfW);
                float minY = halfH;
                float topReservedPx = Screen.height * 0.14f;
                float maxY = Mathf.Max(minY, Screen.height - topReservedPx - halfH * 0.5f);
                Vector2 clampedScreen = new Vector2(
                    Mathf.Clamp(screenPos.x, minX, maxX),
                    Mathf.Clamp(screenPos.y, minY, maxY));

                Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, clampedScreen, cam, out Vector2 localPoint);
                movementSliderRect.position = canvasRect.TransformPoint(localPoint);
                Vector2 newAnchored = movementSliderRect.anchoredPosition;

                SetButtonAnchoredPosition(MovementSliderControlId, newAnchored, saveImmediately);
                return;
            }

            if (buttonId < 0 || buttonId >= ButtonCount || buttonRects[buttonId] == null || canvasRect == null)
            {
                return;
            }

            RectTransform rect = buttonRects[buttonId];
            float halfSize = Mathf.Max(24f, rect.sizeDelta.x * canvas.scaleFactor * currentScales[buttonId] * 0.5f);
            float minXBtn = halfSize;
            float maxXBtn = Mathf.Max(minXBtn, Screen.width - halfSize);
            float minYBtn = halfSize;
            // Reserve top 15% of screen during drag so gameplay buttons cannot be dropped over the Scoreboard or Custom UI Dock
            float topReservedBtnPx = Screen.height * 0.14f;
            float maxYBtn = Mathf.Max(minYBtn, Screen.height - topReservedBtnPx - halfSize * 0.5f);
            Vector2 clampedBtnScreen = new Vector2(
                Mathf.Clamp(screenPos.x, minXBtn, maxXBtn),
                Mathf.Clamp(screenPos.y, minYBtn, maxYBtn));

            Camera btnCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, clampedBtnScreen, btnCam, out Vector2 btnLocalPoint);
            rect.position = canvasRect.TransformPoint(btnLocalPoint);
            Vector2 newBtnAnchored = rect.anchoredPosition;

            SetButtonAnchoredPosition(buttonId, newBtnAnchored, saveImmediately);
        }

        public void SetSelectedScale(float scale)
        {
            SetButtonScale(selectedButtonId, scale, true);
        }

        public void SetButtonScale(int buttonId, float scale, bool saveImmediately = true)
        {
            if (buttonId == MovementSliderControlId)
            {
                if (movementSliderRect == null)
                {
                    return;
                }
                float clampedSlider = Mathf.Clamp(scale, SliderMinScale, SliderMaxScale);
                currentSliderScale = clampedSlider;
                ApplyMovementSliderBaseLayout(currentSliderAnchoredPosition, clampedSlider);
                if (buttonId == selectedButtonId)
                {
                    RefreshToolbarInfo();
                }
                if (saveImmediately)
                {
                    SaveCustomLayout();
                }
                return;
            }

            if (buttonId < 0 || buttonId >= ButtonCount || pressViews[buttonId] == null)
            {
                return;
            }

            float clamped = Mathf.Clamp(scale, MinScale, MaxScale);
            currentScales[buttonId] = clamped;
            pressViews[buttonId].SetBaseLayout(currentAnchoredPositions[buttonId], clamped);

            if (buttonId == selectedButtonId)
            {
                RefreshToolbarInfo();
            }

            if (saveImmediately)
            {
                SaveCustomLayout();
            }
        }

        public void AdjustSelectedButtonScale(float delta)
        {
            float current = selectedButtonId == MovementSliderControlId
                ? currentSliderScale
                : (selectedButtonId >= 0 && selectedButtonId < ButtonCount ? currentScales[selectedButtonId] : 1f);
            SetButtonScale(selectedButtonId, current + delta, true);
        }

        public void ResetSelectedButton()
        {
            int id = selectedButtonId;
            if (id == MovementSliderControlId)
            {
                currentSliderAnchoredPosition = defaultSliderAnchoredPosition;
                currentSliderScale = 1f;
                ApplyMovementSliderBaseLayout(defaultSliderAnchoredPosition, 1f);
                SaveCustomLayout();
                RefreshToolbarInfo();
                return;
            }

            if (id < 0 || id >= ButtonCount || pressViews[id] == null)
            {
                return;
            }

            currentAnchoredPositions[id] = defaultAnchoredPositions[id];
            currentScales[id] = 1f;
            pressViews[id].SetBaseLayout(defaultAnchoredPositions[id], 1f);
            SaveCustomLayout();
            RefreshToolbarInfo();
        }

        public void ResetAllToDefaults() => ResetAllButtons();

        public void ResetAllButtons()
        {
            for (int id = 0; id < ButtonCount; id++)
            {
                currentAnchoredPositions[id] = defaultAnchoredPositions[id];
                currentScales[id] = 1f;
                if (pressViews[id] != null)
                {
                    pressViews[id].SetBaseLayout(defaultAnchoredPositions[id], 1f);
                }
            }

            currentSliderAnchoredPosition = defaultSliderAnchoredPosition;
            currentSliderScale = 1f;
            ApplyMovementSliderBaseLayout(defaultSliderAnchoredPosition, 1f);

            ClearSavedCustomLayout();
            RefreshToolbarInfo();
        }

        public void SaveCustomLayout()
        {
            for (int id = 0; id < ButtonCount; id++)
            {
                PlayerPrefs.SetFloat(PosXPrefPrefix + id, currentAnchoredPositions[id].x);
                PlayerPrefs.SetFloat(PosYPrefPrefix + id, currentAnchoredPositions[id].y);
                PlayerPrefs.SetFloat(ScalePrefPrefix + id, currentScales[id]);
            }

            PlayerPrefs.SetFloat(SliderPosXPrefKey, currentSliderAnchoredPosition.x);
            PlayerPrefs.SetFloat(SliderPosYPrefKey, currentSliderAnchoredPosition.y);
            PlayerPrefs.SetFloat(SliderScalePrefKey, currentSliderScale);

            PlayerPrefs.SetInt(HasSavedLayoutPrefKey, 1);
            PlayerPrefs.Save();
        }

        public void SaveAndExitEditMode()
        {
            SaveCustomLayout();
            UIButtonInputTest.SetLayoutEditMode(false);
        }

        public bool LoadSavedLayoutIfPresent()
        {
            if (!HasSavedCustomLayout)
            {
                return false;
            }

            for (int id = 0; id < ButtonCount; id++)
            {
                if (pressViews[id] == null)
                {
                    continue;
                }

                float x = PlayerPrefs.GetFloat(PosXPrefPrefix + id, defaultAnchoredPositions[id].x);
                float y = PlayerPrefs.GetFloat(PosYPrefPrefix + id, defaultAnchoredPositions[id].y);
                float s = Mathf.Clamp(PlayerPrefs.GetFloat(ScalePrefPrefix + id, 1f), MinScale, MaxScale);
                currentAnchoredPositions[id] = new Vector2(x, y);
                currentScales[id] = s;
                pressViews[id].SetBaseLayout(currentAnchoredPositions[id], s);
            }

            if (movementSliderRect != null)
            {
                float sx = PlayerPrefs.GetFloat(SliderPosXPrefKey, defaultSliderAnchoredPosition.x);
                float sy = PlayerPrefs.GetFloat(SliderPosYPrefKey, defaultSliderAnchoredPosition.y);
                float ss = Mathf.Clamp(PlayerPrefs.GetFloat(SliderScalePrefKey, 1f), SliderMinScale, SliderMaxScale);
                currentSliderAnchoredPosition = new Vector2(sx, sy);
                currentSliderScale = ss;
                ApplyMovementSliderBaseLayout(currentSliderAnchoredPosition, currentSliderScale);
            }

            RefreshToolbarInfo();
            return true;
        }

        internal Camera GetCanvasEventCamera()
        {
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        internal RectTransform GetButtonRect(int buttonId)
        {
            if (buttonId == MovementSliderControlId)
            {
                return movementSliderRect;
            }
            return (buttonId >= 0 && buttonId < ButtonCount) ? buttonRects[buttonId] : null;
        }

        private void RefreshMovementModeVisuals()
        {
            MovementControlMode mode = inputAdapter != null
                ? inputAdapter.CurrentMovementMode
                : MovementControlMode.TwoButtons;
            bool sliderMode = mode == MovementControlMode.HorizontalSlider;

            if (buttonRects[0] != null)
            {
                buttonRects[0].gameObject.SetActive(!sliderMode);
            }
            if (buttonRects[1] != null)
            {
                buttonRects[1].gameObject.SetActive(!sliderMode);
            }
            if (movementSliderRect != null)
            {
                movementSliderRect.gameObject.SetActive(sliderMode);
                if (!sliderMode && movementSlider != null)
                {
                    movementSlider.ResetToCenterWithoutNotify();
                }
            }

            if (moveModeToggleLabel != null)
            {
                moveModeToggleLabel.text = sliderMode ? "⇄ MOVE: SLIDER" : "⇄ MOVE: 2 BTN";
            }
            if (moveModeToggleImage != null)
            {
                moveModeToggleImage.color = sliderMode ? ToggleActiveColor : ToggleNormalColor;
            }
            if (moveModeToggleOutline != null)
            {
                moveModeToggleOutline.effectColor = sliderMode ? (Color)ToggleOutlineActive : (Color)ToggleOutlineNormal;
            }

            if (UIButtonInputTest.IsLayoutEditMode)
            {
                if (sliderMode && (selectedButtonId == 0 || selectedButtonId == 1))
                {
                    SelectButton(MovementSliderControlId);
                }
                else if (!sliderMode && selectedButtonId == MovementSliderControlId)
                {
                    SelectButton(0);
                }
            }
        }

        private void RefreshRamStatsVisuals()
        {
            if (ramStatsToggleLabel != null)
            {
                ramStatsToggleLabel.text = ramStatsEditorOpen ? "✕ RAM STATS" : "⚡ RAM STATS";
            }
            if (ramStatsToggleImage != null)
            {
                ramStatsToggleImage.color = ramStatsEditorOpen ? ToggleActiveColor : ToggleNormalColor;
            }
            if (ramStatsToggleOutline != null)
            {
                ramStatsToggleOutline.effectColor = ramStatsEditorOpen ? (Color)ToggleOutlineActive : (Color)ToggleOutlineNormal;
            }
            if (ramStatsPanelRect != null)
            {
                ramStatsPanelRect.gameObject.SetActive(ramStatsEditorOpen);
                if (ramStatsEditorOpen)
                {
                    ramStatsPanelRect.SetAsLastSibling();
                }
            }
            RefreshRamStatsSlidersWithoutNotify();
        }

        private void RefreshRamStatsSlidersWithoutNotify()
        {
            if (targetPlayerBtnImage != null)
            {
                targetPlayerBtnImage.color = selectedStatsEntityId == 0u ? ToolbarAccentBtnColor : ToolbarBtnColor;
            }
            if (targetOpponentBtnImage != null)
            {
                targetOpponentBtnImage.color = selectedStatsEntityId == 3u ? ToolbarAccentBtnColor : ToolbarBtnColor;
            }

            float[] vals = { curAtk, curJmp, curSpd, curDef };
            for (int i = 0; i < 4; i++)
            {
                if (statSliders[i] != null)
                {
                    statSliders[i].SetNormalizedWithoutNotify(Mathf.InverseLerp(MinStatValue, MaxStatValue, vals[i]));
                }
                if (statValueLabels[i] != null)
                {
                    statValueLabels[i].text = Mathf.RoundToInt(vals[i]).ToString();
                }
            }
        }

        private void RefreshEditModeVisuals(bool editMode)
        {
            if (editMode && inputAdapter != null &&
                inputAdapter.CurrentMovementMode == MovementControlMode.HorizontalSlider &&
                (selectedButtonId == 0 || selectedButtonId == 1))
            {
                selectedButtonId = MovementSliderControlId;
            }
            else if (editMode && (inputAdapter == null || inputAdapter.CurrentMovementMode == MovementControlMode.TwoButtons) &&
                     selectedButtonId == MovementSliderControlId)
            {
                selectedButtonId = 4;
            }

            if (toggleButtonLabel != null)
            {
                toggleButtonLabel.text = editMode ? "✓ DONE EDITING" : "⚙ CUSTOM UI";
            }

            if (toggleButtonImage != null)
            {
                toggleButtonImage.color = editMode ? ToggleActiveColor : ToggleNormalColor;
            }

            if (toggleButtonOutline != null)
            {
                toggleButtonOutline.effectColor = editMode ? (Color)ToggleOutlineActive : (Color)ToggleOutlineNormal;
            }

            if (toolbarRect != null)
            {
                toolbarRect.gameObject.SetActive(editMode);
                if (editMode)
                {
                    toolbarRect.SetAsLastSibling();
                }
            }

            if (toggleButtonRect != null)
            {
                toggleButtonRect.SetAsLastSibling();
            }
            if (moveModeToggleButtonRect != null)
            {
                moveModeToggleButtonRect.SetAsLastSibling();
            }
            if (ramStatsToggleButtonRect != null)
            {
                ramStatsToggleButtonRect.SetAsLastSibling();
            }

            for (int id = 0; id < ButtonCount; id++)
            {
                if (pressViews[id] != null)
                {
                    pressViews[id].SetSelectedHighlight(editMode && id == selectedButtonId);
                }
            }

            RefreshMovementSliderHighlight(editMode && selectedButtonId == MovementSliderControlId);

            if (editMode)
            {
                RefreshToolbarInfo();
            }
        }

        private void RefreshToolbarInfo()
        {
            if (selectedInfoLabel != null && selectedButtonId >= 0 && selectedButtonId < EditableControlCount)
            {
                float curScale = selectedButtonId == MovementSliderControlId ? currentSliderScale : currentScales[selectedButtonId];
                int pct = Mathf.RoundToInt(curScale * 100f);
                selectedInfoLabel.text = "SELECTED: " + ButtonDisplayNames[selectedButtonId] + "   •   SIZE: " + pct + "%";
            }

            if (scaleSlider != null && selectedButtonId >= 0 && selectedButtonId < EditableControlCount)
            {
                if (selectedButtonId == MovementSliderControlId)
                {
                    scaleSlider.SetNormalizedWithoutNotify(Mathf.InverseLerp(SliderMinScale, SliderMaxScale, currentSliderScale));
                }
                else
                {
                    scaleSlider.SetNormalizedWithoutNotify(Mathf.InverseLerp(MinScale, MaxScale, currentScales[selectedButtonId]));
                }
            }
        }

        private static Sprite cachedCapsuleSprite;
        private static Sprite cachedCircleSprite;

        private static Sprite CreateCapsuleSprite(int width = 256, int height = 96)
        {
            if (cachedCapsuleSprite != null)
            {
                return cachedCapsuleSprite;
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "SpikeSliderCapsule",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[width * height];
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float radius = halfH - 1.5f;
            float spineHalfX = Mathf.Max(0f, halfW - 1.5f - radius);

            for (int y = 0; y < height; y++)
            {
                float dy = (y + 0.5f) - halfH;
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs((x + 0.5f) - halfW) - spineHalfX);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float coverage = Mathf.Clamp01((radius + 1.0f - dist) * 0.5f);
                    if (coverage <= 0f)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Subtle soft inner rim for clean stadium capsule definition
                    float rimBand = 1f - Mathf.Clamp01(Mathf.Abs(dist - (radius - 1.6f)) / 2.2f);
                    byte lum = (byte)Mathf.RoundToInt(Mathf.Lerp(232f, 255f, rimBand * 0.55f));
                    byte alpha = (byte)Mathf.RoundToInt(255f * coverage);
                    pixels[y * width + x] = new Color32(lum, lum, (byte)Mathf.Min(255, lum + 4), alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            cachedCapsuleSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
            cachedCapsuleSprite.name = tex.name;
            return cachedCapsuleSprite;
        }

        private static Sprite CreateCircleSprite(int resolution = 128)
        {
            if (cachedCircleSprite != null)
            {
                return cachedCircleSprite;
            }

            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "SpikeSliderKnobCircle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[resolution * resolution];
            float center = resolution * 0.5f;
            float radius = center - 1.5f;

            for (int y = 0; y < resolution; y++)
            {
                float dy = (y + 0.5f) - center;
                for (int x = 0; x < resolution; x++)
                {
                    float dx = (x + 0.5f) - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float coverage = Mathf.Clamp01((radius + 1.0f - dist) * 0.5f);
                    if (coverage <= 0f)
                    {
                        pixels[y * resolution + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float rimBand = 1f - Mathf.Clamp01(Mathf.Abs(dist - (radius - 1.5f)) / 2.0f);
                    byte lum = (byte)Mathf.RoundToInt(Mathf.Lerp(234f, 255f, rimBand * 0.50f));
                    byte alpha = (byte)Mathf.RoundToInt(255f * coverage);
                    pixels[y * resolution + x] = new Color32(lum, lum, (byte)Mathf.Min(255, lum + 4), alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            cachedCircleSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                100f);
            cachedCircleSprite.name = tex.name;
            return cachedCircleSprite;
        }

        private void EnsureEditorHud()
        {
            if (canvas == null)
            {
                return;
            }

            float canvasScale = Mathf.Max(0.01f, canvas.scaleFactor);
            float invScale = uiScale / canvasScale;
            int layer = canvas.gameObject.layer;
            Vector2 topPillSize = new Vector2(182f * invScale, 56f * invScale);

            Transform existingToggle = transform.Find(ToggleButtonName);
            if (existingToggle == null)
            {
                toggleButtonRect = CreateToolbarButton(
                    transform,
                    layer,
                    ToggleButtonName,
                    "⚙ CUSTOM UI",
                    topPillSize,
                    ToggleNormalColor,
                    () =>
                    {
                        bool next = !UIButtonInputTest.IsLayoutEditMode;
                        if (!next)
                        {
                            SaveCustomLayout();
                        }
                        UIButtonInputTest.SetLayoutEditMode(next);
                    },
                    out toggleButtonImage,
                    out toggleButtonLabel);

                toggleButtonOutline = toggleButtonRect.gameObject.AddComponent<Outline>();
                toggleButtonOutline.effectColor = ToggleOutlineNormal;
                toggleButtonOutline.effectDistance = new Vector2(1.5f * invScale, -1.5f * invScale);
            }
            else
            {
                toggleButtonRect = (RectTransform)existingToggle;
                toggleButtonRect.sizeDelta = topPillSize;
                toggleButtonImage = toggleButtonRect.GetComponent<Image>();
                toggleButtonOutline = toggleButtonRect.GetComponent<Outline>();
                toggleButtonLabel = toggleButtonRect.GetComponentInChildren<Text>(true);
            }

            // Dock Toggle Button in Top-Left utility slot #1 (completely separate from Top-Center Scoreboard & Top-Right BGM/Pause)
            PositionScreenPoint(
                toggleButtonRect,
                Screen.safeArea.xMin + 104f * uiScale,
                Screen.safeArea.yMax - 38f * uiScale);

            Transform existingToolbar = transform.Find(ToolbarPanelName);
            if (existingToolbar == null)
            {
                GameObject panelObj = new GameObject(
                    ToolbarPanelName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Outline),
                    typeof(Shadow));
                panelObj.layer = layer;
                toolbarRect = (RectTransform)panelObj.transform;
                toolbarRect.SetParent(transform, false);
                toolbarRect.anchorMin = toolbarRect.anchorMax = new Vector2(0.5f, 0.5f);
                toolbarRect.pivot = new Vector2(0.5f, 0.5f);
                toolbarRect.sizeDelta = new Vector2(880f * invScale, 128f * invScale);

                Image panelImage = panelObj.GetComponent<Image>();
                panelImage.color = ToolbarBgColor;
                panelImage.raycastTarget = true;

                Outline panelOutline = panelObj.GetComponent<Outline>();
                panelOutline.effectColor = ToolbarBorderColor;
                panelOutline.effectDistance = new Vector2(1.5f * invScale, -1.5f * invScale);

                Shadow panelShadow = FindPureShadow(panelObj);
                if (panelShadow != null)
                {
                    panelShadow.effectColor = new Color(0f, 0f, 0f, 0.80f);
                    panelShadow.effectDistance = new Vector2(0f, -7f * invScale);
                }

                // Top Gold Accent Bar
                GameObject stripeObj = new GameObject("TopAccentStripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                stripeObj.layer = layer;
                RectTransform stripeRect = (RectTransform)stripeObj.transform;
                stripeRect.SetParent(toolbarRect, false);
                stripeRect.anchorMin = new Vector2(0f, 1f);
                stripeRect.anchorMax = new Vector2(1f, 1f);
                stripeRect.pivot = new Vector2(0.5f, 1f);
                stripeRect.anchoredPosition = Vector2.zero;
                stripeRect.sizeDelta = new Vector2(0f, 4f * invScale);
                Image stripeImg = stripeObj.GetComponent<Image>();
                stripeImg.color = ToolbarAccentStripeColor;
                stripeImg.raycastTarget = false;

                // Header Row: Selected Button Name + Scale %
                GameObject infoObj = new GameObject("SelectedButtonLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                infoObj.layer = layer;
                RectTransform infoRect = (RectTransform)infoObj.transform;
                infoRect.SetParent(toolbarRect, false);
                infoRect.anchorMin = infoRect.anchorMax = new Vector2(0.5f, 0.5f);
                infoRect.pivot = new Vector2(0.5f, 0.5f);
                infoRect.anchoredPosition = new Vector2(-150f * invScale, 36f * invScale);
                infoRect.sizeDelta = new Vector2(530f * invScale, 28f * invScale);
                selectedInfoLabel = infoObj.GetComponent<Text>();
                selectedInfoLabel.font = uiFont;
                selectedInfoLabel.fontStyle = FontStyle.Bold;
                selectedInfoLabel.fontSize = Mathf.Max(12, Mathf.RoundToInt(16f * invScale));
                selectedInfoLabel.alignment = TextAnchor.MiddleLeft;
                selectedInfoLabel.color = new Color32(245, 158, 11, 255);
                selectedInfoLabel.raycastTarget = false;

                // Header Right Hint Text
                GameObject hintObj = new GameObject("DragHintLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                hintObj.layer = layer;
                RectTransform hintRect = (RectTransform)hintObj.transform;
                hintRect.SetParent(toolbarRect, false);
                hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.5f);
                hintRect.pivot = new Vector2(0.5f, 0.5f);
                hintRect.anchoredPosition = new Vector2(255f * invScale, 36f * invScale);
                hintRect.sizeDelta = new Vector2(320f * invScale, 26f * invScale);
                hintSubLabel = hintObj.GetComponent<Text>();
                hintSubLabel.font = uiFont;
                hintSubLabel.fontStyle = FontStyle.Italic;
                hintSubLabel.fontSize = Mathf.Max(11, Mathf.RoundToInt(14f * invScale));
                hintSubLabel.alignment = TextAnchor.MiddleRight;
                hintSubLabel.color = new Color32(148, 163, 184, 255);
                hintSubLabel.text = "Tap & drag any button to reposition";
                hintSubLabel.raycastTarget = false;

                float rowY = -20f * invScale;

                RectTransform minusBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SizeMinusButton",
                    "- 10%",
                    new Vector2(96f * invScale, 50f * invScale),
                    ToolbarBtnColor,
                    () => AdjustSelectedButtonScale(-ScaleStep),
                    out _,
                    out _);
                minusBtn.anchoredPosition = new Vector2(-376f * invScale, rowY);

                scaleSlider = CreateScaleSlider(toolbarRect, layer, new Vector2(204f * invScale, 36f * invScale), invScale);
                ((RectTransform)scaleSlider.transform).anchoredPosition = new Vector2(-210f * invScale, rowY);

                RectTransform plusBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SizePlusButton",
                    "+ 10%",
                    new Vector2(96f * invScale, 50f * invScale),
                    ToolbarBtnColor,
                    () => AdjustSelectedButtonScale(ScaleStep),
                    out _,
                    out _);
                plusBtn.anchoredPosition = new Vector2(-44f * invScale, rowY);

                RectTransform resetBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "ResetButton",
                    "RESET BTN",
                    new Vector2(118f * invScale, 50f * invScale),
                    ToolbarBtnColor,
                    ResetSelectedButton,
                    out _,
                    out _);
                resetBtn.anchoredPosition = new Vector2(74f * invScale, rowY);

                RectTransform resetAllBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "ResetAllButton",
                    "RESET ALL",
                    new Vector2(118f * invScale, 50f * invScale),
                    ToolbarWarnBtnColor,
                    ResetAllButtons,
                    out _,
                    out _);
                resetAllBtn.anchoredPosition = new Vector2(202f * invScale, rowY);

                RectTransform saveExitBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SaveExitButton",
                    "DONE / SAVE",
                    new Vector2(142f * invScale, 50f * invScale),
                    ToolbarAccentBtnColor,
                    SaveAndExitEditMode,
                    out _,
                    out _);
                saveExitBtn.anchoredPosition = new Vector2(342f * invScale, rowY);
            }
            else
            {
                toolbarRect = (RectTransform)existingToolbar;
                Transform infoTr = toolbarRect.Find("SelectedButtonLabel");
                if (infoTr != null)
                {
                    selectedInfoLabel = infoTr.GetComponent<Text>();
                }
                scaleSlider = toolbarRect.GetComponentInChildren<LayoutScaleSlider>(true);
            }

            // Dock LayoutEditorToolbar cleanly BELOW the Top-Center Scoreboard (zero overlap with Scoreboard or Top-Left/Top-Right buttons)
            PositionScreenPoint(
                toolbarRect,
                Screen.safeArea.center.x,
                Screen.safeArea.yMax - 176f * uiScale);

            EnsureMovementModeHud(layer, invScale, topPillSize);
            EnsureRamStatsHud(layer, invScale, topPillSize);
        }

        private void EnsureMovementModeHud(int layer, float invScale, Vector2 topPillSize)
        {
            Transform existingMoveToggle = transform.Find(MoveModeToggleButtonName);
            if (existingMoveToggle == null)
            {
                moveModeToggleButtonRect = CreateToolbarButton(
                    transform,
                    layer,
                    MoveModeToggleButtonName,
                    "⇄ MOVE: 2 BTN",
                    topPillSize,
                    ToggleNormalColor,
                    ToggleMovementMode,
                    out moveModeToggleImage,
                    out moveModeToggleLabel);

                moveModeToggleOutline = moveModeToggleButtonRect.gameObject.AddComponent<Outline>();
                moveModeToggleOutline.effectColor = ToggleOutlineNormal;
                moveModeToggleOutline.effectDistance = new Vector2(1.5f * invScale, -1.5f * invScale);
            }
            else
            {
                moveModeToggleButtonRect = (RectTransform)existingMoveToggle;
                moveModeToggleButtonRect.sizeDelta = topPillSize;
                moveModeToggleImage = moveModeToggleButtonRect.GetComponent<Image>();
                moveModeToggleOutline = moveModeToggleButtonRect.GetComponent<Outline>();
                moveModeToggleLabel = moveModeToggleButtonRect.GetComponentInChildren<Text>(true);
            }

            // Top-Left utility slot #2 (182x56 at x=294, well left of Top-Center Scoreboard [745..1175])
            PositionScreenPoint(
                moveModeToggleButtonRect,
                Screen.safeArea.xMin + 294f * uiScale,
                Screen.safeArea.yMax - 38f * uiScale);

            Vector2 capsuleTrackSize = new Vector2(336f * invScale, 128f * invScale);
            Vector2 circleKnobSize = new Vector2(114f * invScale, 114f * invScale);
            Color capsuleTrackColor = new Color(0.08f, 0.10f, 0.13f, 0.46f);
            Color idleKnobColor = new Color(0.06f, 0.08f, 0.10f, 0.68f);

            Transform existingSlider = transform.Find(MovementSliderName);
            if (existingSlider == null)
            {
                GameObject sliderObj = new GameObject(
                    MovementSliderName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(MovementHorizontalSliderControl));
                sliderObj.layer = layer;
                movementSliderRect = (RectTransform)sliderObj.transform;
                movementSliderRect.SetParent(transform, false);
                movementSliderRect.anchorMin = movementSliderRect.anchorMax = new Vector2(0.5f, 0.5f);
                movementSliderRect.pivot = new Vector2(0.5f, 0.5f);
                movementSliderRect.sizeDelta = capsuleTrackSize;

                Image trackImg = sliderObj.GetComponent<Image>();
                trackImg.sprite = CreateCapsuleSprite(256, 96);
                trackImg.type = Image.Type.Simple;
                trackImg.preserveAspect = false;
                trackImg.color = capsuleTrackColor;
                trackImg.raycastTarget = true;

                // Clean minimalist circular thumb knob matching The Spike Cross
                GameObject handleObj = new GameObject(
                    "HandleKnob",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                handleObj.layer = layer;
                RectTransform handleRect = (RectTransform)handleObj.transform;
                handleRect.SetParent(movementSliderRect, false);
                handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
                handleRect.pivot = new Vector2(0.5f, 0.5f);
                handleRect.sizeDelta = circleKnobSize;
                handleRect.anchoredPosition = Vector2.zero;

                Image handleImg = handleObj.GetComponent<Image>();
                handleImg.sprite = CreateCircleSprite(128);
                handleImg.type = Image.Type.Simple;
                handleImg.preserveAspect = true;
                handleImg.color = idleKnobColor;
                handleImg.raycastTarget = false;

                movementSlider = sliderObj.GetComponent<MovementHorizontalSliderControl>();
                movementSlider.Configure(movementSliderRect, handleRect, handleImg, invScale, axis =>
                {
                    if (inputAdapter != null)
                    {
                        inputAdapter.SetSliderHorizontalAxis(axis);
                    }
                });
            }
            else
            {
                movementSliderRect = (RectTransform)existingSlider;
                movementSliderRect.sizeDelta = capsuleTrackSize;
                Image trackImg = movementSliderRect.GetComponent<Image>();
                if (trackImg != null)
                {
                    trackImg.sprite = CreateCapsuleSprite(256, 96);
                    trackImg.type = Image.Type.Simple;
                    trackImg.color = capsuleTrackColor;
                }
                movementSlider = movementSliderRect.GetComponent<MovementHorizontalSliderControl>();
                if (movementSlider != null)
                {
                    RectTransform handleRect = movementSliderRect.Find("HandleKnob") as RectTransform;
                    Image handleImg = null;
                    if (handleRect != null)
                    {
                        handleRect.sizeDelta = circleKnobSize;
                        handleImg = handleRect.GetComponent<Image>();
                        if (handleImg != null)
                        {
                            handleImg.sprite = CreateCircleSprite(128);
                            handleImg.type = Image.Type.Simple;
                            handleImg.preserveAspect = true;
                            handleImg.color = idleKnobColor;
                        }
                    }
                    movementSlider.Configure(movementSliderRect, handleRect, handleImg, invScale, axis =>
                    {
                        if (inputAdapter != null)
                        {
                            inputAdapter.SetSliderHorizontalAxis(axis);
                        }
                    });
                }
            }

            // Bottom-Left movement zone (220f, 108f) matching The Spike Cross stadium capsule placement
            PositionScreenPoint(
                movementSliderRect,
                Screen.safeArea.xMin + 220f * uiScale,
                Screen.safeArea.yMin + 108f * uiScale);

            defaultSliderAnchoredPosition = movementSliderRect.anchoredPosition;
            currentSliderAnchoredPosition = defaultSliderAnchoredPosition;
            currentSliderScale = 1f;
            ApplyMovementSliderBaseLayout(defaultSliderAnchoredPosition, 1f);

            movementSliderOutline = movementSliderRect.GetComponent<Outline>();
            if (movementSliderOutline == null)
            {
                movementSliderOutline = movementSliderRect.gameObject.AddComponent<Outline>();
            }
            movementSliderOutline.effectColor = ToggleOutlineActive;
            movementSliderOutline.effectDistance = new Vector2(3f * invScale, -3f * invScale);
            movementSliderOutline.enabled = UIButtonInputTest.IsLayoutEditMode && selectedButtonId == MovementSliderControlId;

            ActionButtonDragHandle sliderDragHandle = movementSliderRect.GetComponent<ActionButtonDragHandle>();
            if (sliderDragHandle == null)
            {
                sliderDragHandle = movementSliderRect.gameObject.AddComponent<ActionButtonDragHandle>();
            }
            sliderDragHandle.Configure(this, MovementSliderControlId);
        }

        private void EnsureRamStatsHud(int layer, float invScale, Vector2 topPillSize)
        {
            Transform existingStatsToggle = transform.Find(RamStatsToggleButtonName);
            if (existingStatsToggle == null)
            {
                ramStatsToggleButtonRect = CreateToolbarButton(
                    transform,
                    layer,
                    RamStatsToggleButtonName,
                    "⚡ RAM STATS",
                    topPillSize,
                    ToggleNormalColor,
                    ToggleRamStatsEditor,
                    out ramStatsToggleImage,
                    out ramStatsToggleLabel);

                ramStatsToggleOutline = ramStatsToggleButtonRect.gameObject.AddComponent<Outline>();
                ramStatsToggleOutline.effectColor = ToggleOutlineNormal;
                ramStatsToggleOutline.effectDistance = new Vector2(1.5f * invScale, -1.5f * invScale);
            }
            else
            {
                ramStatsToggleButtonRect = (RectTransform)existingStatsToggle;
                ramStatsToggleButtonRect.sizeDelta = topPillSize;
                ramStatsToggleImage = ramStatsToggleButtonRect.GetComponent<Image>();
                ramStatsToggleOutline = ramStatsToggleButtonRect.GetComponent<Outline>();
                ramStatsToggleLabel = ramStatsToggleButtonRect.GetComponentInChildren<Text>(true);
            }

            // Top-Left utility slot #3 (182x56 at x=484, right edge=575 < Scoreboard left edge=745)
            PositionScreenPoint(
                ramStatsToggleButtonRect,
                Screen.safeArea.xMin + 484f * uiScale,
                Screen.safeArea.yMax - 38f * uiScale);

            string[] rowNames = { "ATK", "JMP", "SPD", "DEF" };
            string[] sliderNames = { "AtkSlider", "JmpSlider", "SpdSlider", "DefSlider" };
            string[] valLabelNames = { "AtkValueLabel", "JmpValueLabel", "SpdValueLabel", "DefValueLabel" };

            Transform existingPanel = transform.Find(RamStatsPanelName);
            if (existingPanel == null)
            {
                GameObject panelObj = new GameObject(
                    RamStatsPanelName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Outline),
                    typeof(Shadow));
                panelObj.layer = layer;
                ramStatsPanelRect = (RectTransform)panelObj.transform;
                ramStatsPanelRect.SetParent(transform, false);
                ramStatsPanelRect.anchorMin = ramStatsPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
                ramStatsPanelRect.pivot = new Vector2(0.5f, 0.5f);
                ramStatsPanelRect.sizeDelta = new Vector2(536f * invScale, 392f * invScale);

                Image bgImg = panelObj.GetComponent<Image>();
                bgImg.color = ToolbarBgColor;
                bgImg.raycastTarget = true;

                Outline border = panelObj.GetComponent<Outline>();
                border.effectColor = ToolbarBorderColor;
                border.effectDistance = new Vector2(1.5f * invScale, -1.5f * invScale);

                Shadow panelShadow = FindPureShadow(panelObj);
                if (panelShadow != null)
                {
                    panelShadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
                    panelShadow.effectDistance = new Vector2(0f, -7f * invScale);
                }

                // Top Gold Stripe
                GameObject stripeObj = new GameObject("TopAccentStripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                stripeObj.layer = layer;
                RectTransform stripeRect = (RectTransform)stripeObj.transform;
                stripeRect.SetParent(ramStatsPanelRect, false);
                stripeRect.anchorMin = new Vector2(0f, 1f);
                stripeRect.anchorMax = new Vector2(1f, 1f);
                stripeRect.pivot = new Vector2(0.5f, 1f);
                stripeRect.anchoredPosition = Vector2.zero;
                stripeRect.sizeDelta = new Vector2(0f, 4f * invScale);
                Image stripeImg = stripeObj.GetComponent<Image>();
                stripeImg.color = ToolbarAccentStripeColor;
                stripeImg.raycastTarget = false;

                // Title Label
                GameObject titleObj = new GameObject("HeaderLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                titleObj.layer = layer;
                RectTransform titleRect = (RectTransform)titleObj.transform;
                titleRect.SetParent(ramStatsPanelRect, false);
                titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
                titleRect.pivot = new Vector2(0.5f, 0.5f);
                titleRect.anchoredPosition = new Vector2(-30f * invScale, 166f * invScale);
                titleRect.sizeDelta = new Vector2(436f * invScale, 30f * invScale);
                Text titleTxt = titleObj.GetComponent<Text>();
                titleTxt.font = uiFont;
                titleTxt.fontStyle = FontStyle.Bold;
                titleTxt.fontSize = Mathf.Max(12, Mathf.RoundToInt(16f * invScale));
                titleTxt.alignment = TextAnchor.MiddleLeft;
                titleTxt.color = new Color32(245, 158, 11, 255);
                titleTxt.text = "⚡ IN-RAM CHARACTER STATS EDITOR";
                titleTxt.raycastTarget = false;

                RectTransform closeBtn = CreateToolbarButton(
                    ramStatsPanelRect,
                    layer,
                    "CloseRamStatsButton",
                    "✕",
                    new Vector2(46f * invScale, 40f * invScale),
                    ToolbarBtnColor,
                    () => OpenRamStatsEditor(false),
                    out _,
                    out _);
                closeBtn.anchoredPosition = new Vector2(232f * invScale, 166f * invScale);

                // Target Selector Row: PLAYER (ID 0) & OPPONENT (ID 3)
                RectTransform playerTargetBtn = CreateToolbarButton(
                    ramStatsPanelRect,
                    layer,
                    "TargetPlayerButton",
                    "PLAYER (ID 0)",
                    new Vector2(236f * invScale, 50f * invScale),
                    ToolbarAccentBtnColor,
                    () => SelectStatsEntity(0u),
                    out targetPlayerBtnImage,
                    out _);
                playerTargetBtn.anchoredPosition = new Vector2(-124f * invScale, 114f * invScale);

                RectTransform opponentTargetBtn = CreateToolbarButton(
                    ramStatsPanelRect,
                    layer,
                    "TargetOpponentButton",
                    "OPPONENT (ID 3)",
                    new Vector2(236f * invScale, 50f * invScale),
                    ToolbarBtnColor,
                    () => SelectStatsEntity(3u),
                    out targetOpponentBtnImage,
                    out _);
                opponentTargetBtn.anchoredPosition = new Vector2(124f * invScale, 114f * invScale);

                // 4 Live Stat Rows: ATK, JMP, SPD, DEF
                for (int r = 0; r < 4; r++)
                {
                    int statIndex = r;
                    float rowY = (50f - r * 54f) * invScale;

                    GameObject lblObj = new GameObject(rowNames[r] + "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                    lblObj.layer = layer;
                    RectTransform lblRect = (RectTransform)lblObj.transform;
                    lblRect.SetParent(ramStatsPanelRect, false);
                    lblRect.anchorMin = lblRect.anchorMax = new Vector2(0.5f, 0.5f);
                    lblRect.pivot = new Vector2(0.5f, 0.5f);
                    lblRect.anchoredPosition = new Vector2(-224f * invScale, rowY);
                    lblRect.sizeDelta = new Vector2(60f * invScale, 34f * invScale);
                    Text rowLbl = lblObj.GetComponent<Text>();
                    rowLbl.font = uiFont;
                    rowLbl.fontStyle = FontStyle.Bold;
                    rowLbl.fontSize = Mathf.Max(12, Mathf.RoundToInt(16f * invScale));
                    rowLbl.alignment = TextAnchor.MiddleLeft;
                    rowLbl.color = new Color32(226, 232, 240, 255);
                    rowLbl.text = rowNames[r];
                    rowLbl.raycastTarget = false;

                    RectTransform minusStatBtn = CreateToolbarButton(
                        ramStatsPanelRect,
                        layer,
                        rowNames[r] + "MinusButton",
                        "-10",
                        new Vector2(64f * invScale, 46f * invScale),
                        ToolbarBtnColor,
                        () => AdjustStatByStep(statIndex, -StatStep),
                        out _,
                        out _);
                    minusStatBtn.anchoredPosition = new Vector2(-152f * invScale, rowY);

                    statSliders[r] = CreateCustomSlider(
                        ramStatsPanelRect,
                        layer,
                        sliderNames[r],
                        new Vector2(204f * invScale, 36f * invScale),
                        invScale,
                        norm => OnStatSliderNormalizedChanged(statIndex, norm));
                    ((RectTransform)statSliders[r].transform).anchoredPosition = new Vector2(-6f * invScale, rowY);

                    RectTransform plusStatBtn = CreateToolbarButton(
                        ramStatsPanelRect,
                        layer,
                        rowNames[r] + "PlusButton",
                        "+10",
                        new Vector2(64f * invScale, 46f * invScale),
                        ToolbarBtnColor,
                        () => AdjustStatByStep(statIndex, StatStep),
                        out _,
                        out _);
                    plusStatBtn.anchoredPosition = new Vector2(140f * invScale, rowY);

                    GameObject valObj = new GameObject(valLabelNames[r], typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                    valObj.layer = layer;
                    RectTransform valRect = (RectTransform)valObj.transform;
                    valRect.SetParent(ramStatsPanelRect, false);
                    valRect.anchorMin = valRect.anchorMax = new Vector2(0.5f, 0.5f);
                    valRect.pivot = new Vector2(0.5f, 0.5f);
                    valRect.anchoredPosition = new Vector2(214f * invScale, rowY);
                    valRect.sizeDelta = new Vector2(62f * invScale, 34f * invScale);
                    Text valTxt = valObj.GetComponent<Text>();
                    valTxt.font = uiFont;
                    valTxt.fontStyle = FontStyle.Bold;
                    valTxt.fontSize = Mathf.Max(12, Mathf.RoundToInt(16f * invScale));
                    valTxt.alignment = TextAnchor.MiddleRight;
                    valTxt.color = new Color32(56, 189, 248, 255);
                    valTxt.text = "100";
                    valTxt.raycastTarget = false;
                    statValueLabels[r] = valTxt;
                }

                // Footer Status + RESET DEFAULT Button
                GameObject statusObj = new GameObject("RamStatsStatusLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                statusObj.layer = layer;
                RectTransform statusRect = (RectTransform)statusObj.transform;
                statusRect.SetParent(ramStatsPanelRect, false);
                statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
                statusRect.pivot = new Vector2(0.5f, 0.5f);
                statusRect.anchoredPosition = new Vector2(-98f * invScale, -162f * invScale);
                statusRect.sizeDelta = new Vector2(292f * invScale, 30f * invScale);
                ramStatsStatusLabel = statusObj.GetComponent<Text>();
                ramStatsStatusLabel.font = uiFont;
                ramStatsStatusLabel.fontStyle = FontStyle.Italic;
                ramStatsStatusLabel.fontSize = Mathf.Max(11, Mathf.RoundToInt(13f * invScale));
                ramStatsStatusLabel.alignment = TextAnchor.MiddleLeft;
                ramStatsStatusLabel.color = new Color32(148, 163, 184, 255);
                ramStatsStatusLabel.text = "LIVE IN RAM • PLAYER (ID 0)";
                ramStatsStatusLabel.raycastTarget = false;

                RectTransform resetRamBtn = CreateToolbarButton(
                    ramStatsPanelRect,
                    layer,
                    "ResetRamStatsButton",
                    "RESET DEFAULT",
                    new Vector2(182f * invScale, 50f * invScale),
                    ToolbarWarnBtnColor,
                    ResetCharacterStatsInRam,
                    out _,
                    out _);
                resetRamBtn.anchoredPosition = new Vector2(158f * invScale, -162f * invScale);
            }
            else
            {
                ramStatsPanelRect = (RectTransform)existingPanel;
                Transform pBtn = ramStatsPanelRect.Find("TargetPlayerButton");
                if (pBtn != null) targetPlayerBtnImage = pBtn.GetComponent<Image>();
                Transform oBtn = ramStatsPanelRect.Find("TargetOpponentButton");
                if (oBtn != null) targetOpponentBtnImage = oBtn.GetComponent<Image>();
                for (int r = 0; r < 4; r++)
                {
                    Transform sTr = ramStatsPanelRect.Find(sliderNames[r]);
                    if (sTr != null) statSliders[r] = sTr.GetComponent<LayoutScaleSlider>();
                    Transform vTr = ramStatsPanelRect.Find(valLabelNames[r]);
                    if (vTr != null) statValueLabels[r] = vTr.GetComponent<Text>();
                }
                Transform stTr = ramStatsPanelRect.Find("RamStatsStatusLabel");
                if (stTr != null) ramStatsStatusLabel = stTr.GetComponent<Text>();
            }

            // Dock RamStatsEditorPanel below the Top-Left utility bar (zero overlap with Scoreboard, Pause, or Movement zone)
            PositionScreenPoint(
                ramStatsPanelRect,
                Screen.safeArea.xMin + 286f * uiScale,
                Screen.safeArea.yMax - 274f * uiScale);
        }

        private static Shadow FindPureShadow(GameObject go)
        {
            Shadow[] shadows = go.GetComponents<Shadow>();
            for (int i = 0; i < shadows.Length; i++)
            {
                if (shadows[i] != null && shadows[i].GetType() == typeof(Shadow))
                {
                    return shadows[i];
                }
            }
            return null;
        }

        private void PositionScreenPoint(RectTransform target, float screenX, float screenY)
        {
            if (target == null || canvasRect == null)
            {
                return;
            }

            Camera cam = GetCanvasEventCamera();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(screenX, screenY), cam, out Vector2 local);
            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.position = canvasRect.TransformPoint(local);
        }

        private RectTransform CreateToolbarButton(
            Transform parent,
            int layer,
            string objName,
            string text,
            Vector2 size,
            Color32 baseColor,
            System.Action onClick,
            out Image image,
            out Text label)
        {
            GameObject obj = new GameObject(
                objName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Shadow),
                typeof(LayoutToolbarButton));
            obj.layer = layer;
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            image = obj.GetComponent<Image>();
            image.color = baseColor;
            image.raycastTarget = true;

            Shadow shadow = obj.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.78f);
            shadow.effectDistance = new Vector2(0f, -4f);

            GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObj.layer = layer;
            RectTransform textRect = (RectTransform)textObj.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);

            label = textObj.GetComponent<Text>();
            label.font = uiFont;
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 16;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = text;
            label.raycastTarget = false;

            LayoutToolbarButton handler = obj.GetComponent<LayoutToolbarButton>();
            handler.Configure(image, shadow, onClick);
            return rect;
        }

        private LayoutScaleSlider CreateScaleSlider(Transform parent, int layer, Vector2 size, float invScale)
        {
            return CreateCustomSlider(parent, layer, ScaleSliderName, size, invScale, normalized =>
            {
                float min = selectedButtonId == MovementSliderControlId ? SliderMinScale : MinScale;
                float max = selectedButtonId == MovementSliderControlId ? SliderMaxScale : MaxScale;
                float targetScale = Mathf.Lerp(min, max, normalized);
                SetButtonScale(selectedButtonId, targetScale, true);
            });
        }

        private LayoutScaleSlider CreateCustomSlider(
            Transform parent,
            int layer,
            string sliderObjName,
            Vector2 size,
            float invScale,
            System.Action<float> onNormalizedChanged)
        {
            GameObject sliderObj = new GameObject(
                sliderObjName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline),
                typeof(LayoutScaleSlider));
            sliderObj.layer = layer;
            RectTransform sliderRect = (RectTransform)sliderObj.transform;
            sliderRect.SetParent(parent, false);
            sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.sizeDelta = size;

            Image trackImage = sliderObj.GetComponent<Image>();
            trackImage.color = SliderTrackColor;
            trackImage.raycastTarget = true;

            Outline trackOutline = sliderObj.GetComponent<Outline>();
            trackOutline.effectColor = new Color32(56, 189, 248, 120);
            trackOutline.effectDistance = new Vector2(1f, -1f);

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObj.layer = layer;
            RectTransform fillRect = (RectTransform)fillObj.transform;
            fillRect.SetParent(sliderRect, false);
            fillRect.anchorMin = new Vector2(0f, 0.18f);
            fillRect.anchorMax = new Vector2(0.5f, 0.82f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImage = fillObj.GetComponent<Image>();
            fillImage.color = SliderFillColor;
            fillImage.raycastTarget = false;

            GameObject knobObj = new GameObject("Knob", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            knobObj.layer = layer;
            RectTransform knobRect = (RectTransform)knobObj.transform;
            knobRect.SetParent(sliderRect, false);
            knobRect.anchorMin = knobRect.anchorMax = new Vector2(0.5f, 0.5f);
            knobRect.pivot = new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta = new Vector2(16f * invScale, Mathf.Max(26f, size.y + 6f * invScale));
            Image knobImage = knobObj.GetComponent<Image>();
            knobImage.color = SliderKnobColor;
            knobImage.raycastTarget = false;
            Shadow knobShadow = knobObj.GetComponent<Shadow>();
            knobShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            knobShadow.effectDistance = new Vector2(0f, -2f);

            LayoutScaleSlider slider = sliderObj.GetComponent<LayoutScaleSlider>();
            slider.Configure(sliderRect, fillRect, knobRect, onNormalizedChanged);
            return slider;
        }

        private static UIButtonInputTest FindButton(UIButtonInputTest[] buttons, int id)
        {
            if (buttons == null)
            {
                return null;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].ButtonId == id)
                {
                    return buttons[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Drag & selection handler attached to each gameplay button (0..8).
    /// Completely dormant when UIButtonInputTest.IsLayoutEditMode is false.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionButtonDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ActionButtonLayoutController controller;
        private int buttonId;
        private Vector2 screenDragOffset;
        private bool hasDragOffset;

        public void Configure(ActionButtonLayoutController layoutController, int id)
        {
            controller = layoutController;
            buttonId = id;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!UIButtonInputTest.IsLayoutEditMode || controller == null)
            {
                return;
            }

            controller.SelectButton(buttonId);
            CacheDragOffset(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!UIButtonInputTest.IsLayoutEditMode || controller == null)
            {
                return;
            }

            controller.SelectButton(buttonId);
            CacheDragOffset(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!UIButtonInputTest.IsLayoutEditMode || controller == null || eventData == null)
            {
                return;
            }

            controller.SelectButton(buttonId);
            Vector2 targetScreen = hasDragOffset ? (eventData.position + screenDragOffset) : eventData.position;
            controller.SetButtonScreenPosition(buttonId, targetScreen, false);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!UIButtonInputTest.IsLayoutEditMode || controller == null)
            {
                return;
            }

            hasDragOffset = false;
            controller.SaveCustomLayout();
        }

        private void CacheDragOffset(PointerEventData eventData)
        {
            if (eventData == null || controller == null)
            {
                hasDragOffset = false;
                return;
            }

            RectTransform rect = controller.GetButtonRect(buttonId);
            if (rect == null)
            {
                hasDragOffset = false;
                return;
            }

            Vector2 buttonScreenPos = RectTransformUtility.WorldToScreenPoint(controller.GetCanvasEventCamera(), rect.position);
            Vector2 delta = buttonScreenPos - eventData.position;
            if (delta.sqrMagnitude <= 40000f)
            {
                screenDragOffset = delta;
                hasDragOffset = true;
            }
            else
            {
                screenDragOffset = Vector2.zero;
                hasDragOffset = false;
            }
        }
    }

    /// <summary>
    /// Lightweight pointer button for the Layout Editor HUD that never uses UIButtonInputTest.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LayoutToolbarButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        private RectTransform rectTransform;
        private Shadow shadow;
        private System.Action onClick;
        private Vector2 baseAnchoredPosition;
        private bool baseCaptured;
        private bool isPressed;

        public void Configure(Image targetImage, Shadow targetShadow, System.Action clickCallback)
        {
            rectTransform = (RectTransform)transform;
            shadow = targetShadow;
            onClick = clickCallback;
            baseCaptured = false;
        }

        public void TriggerClick()
        {
            onClick?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SetPressedVisual(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            SetPressedVisual(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetPressedVisual(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            SetPressedVisual(false);
            onClick?.Invoke();
        }

        private void SetPressedVisual(bool pressed)
        {
            if (rectTransform == null)
            {
                rectTransform = (RectTransform)transform;
            }

            if (!baseCaptured)
            {
                baseAnchoredPosition = rectTransform.anchoredPosition;
                baseCaptured = true;
            }

            if (isPressed == pressed)
            {
                return;
            }

            isPressed = pressed;
            rectTransform.anchoredPosition = pressed
                ? new Vector2(baseAnchoredPosition.x, baseAnchoredPosition.y - 3f)
                : baseAnchoredPosition;
            rectTransform.localScale = pressed ? new Vector3(0.94f, 0.94f, 1f) : Vector3.one;
            if (shadow != null)
            {
                shadow.effectDistance = pressed ? new Vector2(0f, -1f) : new Vector2(0f, -4f);
            }
        }
    }

    /// <summary>
    /// Interactive horizontal drag slider for adjusting the selected button's custom scale (55% to 175%).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LayoutScaleSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private RectTransform trackRect;
        private RectTransform fillRect;
        private RectTransform knobRect;
        private System.Action<float> onValueChanged;
        private float normalizedValue = 0.375f;

        public float NormalizedValue => normalizedValue;

        public void Configure(
            RectTransform track,
            RectTransform fill,
            RectTransform knob,
            System.Action<float> callback)
        {
            trackRect = track;
            fillRect = fill;
            knobRect = knob;
            onValueChanged = callback;
            SetNormalizedWithoutNotify(normalizedValue);
        }

        public void SetNormalizedWithoutNotify(float normalized)
        {
            normalizedValue = Mathf.Clamp01(normalized);
            if (fillRect != null)
            {
                fillRect.anchorMin = new Vector2(0f, 0.18f);
                fillRect.anchorMax = new Vector2(normalizedValue, 0.82f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
            }

            if (knobRect != null && trackRect != null)
            {
                float width = trackRect.rect.width;
                if (width <= 1f)
                {
                    width = trackRect.sizeDelta.x;
                }
                float x = (normalizedValue - 0.5f) * width;
                knobRect.anchoredPosition = new Vector2(x, 0f);
            }
        }

        public void SetScaleNormalized(float normalized)
        {
            SetNormalizedWithoutNotify(normalized);
            onValueChanged?.Invoke(normalizedValue);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            UpdateFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateFromPointer(eventData);
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            if (trackRect == null || eventData == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                trackRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 local))
            {
                float width = trackRect.rect.width;
                if (width <= 1f)
                {
                    width = Mathf.Max(1f, trackRect.sizeDelta.x);
                }

                float t = Mathf.Clamp01((local.x / width) + 0.5f);
                SetScaleNormalized(t);
            }
        }
    }

    /// <summary>
    /// Bottom-left minimalist stadium-capsule horizontal movement slider matching The Spike Cross.
    /// Circular thumb knob glides inside semicircular caps and snaps back to (0, 0) on release.
    /// Never uses UIButtonInputTest so gameplay button count invariants (0..8) remain untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovementHorizontalSliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private static readonly Color IdleKnobColor = new Color(0.06f, 0.08f, 0.10f, 0.68f);
        private static readonly Color ActiveKnobColor = new Color(0.14f, 0.18f, 0.24f, 0.82f);

        private RectTransform trackRect;
        private RectTransform handleRect;
        private Image handleImage;
        private float capInset = 6f;
        private System.Action<float> onAxisChanged;
        private float currentAxis;
        private int activePointerId = int.MinValue;

        public float CurrentAxis => currentAxis;

        public void Configure(
            RectTransform track,
            RectTransform handle,
            Image knobImg,
            float invScale,
            System.Action<float> axisCallback)
        {
            trackRect = track;
            handleRect = handle;
            handleImage = knobImg;
            capInset = 6f * Mathf.Max(0.25f, invScale);
            onAxisChanged = axisCallback;
            ResetToCenterWithoutNotify();
        }

        public void ResetToCenterWithoutNotify()
        {
            activePointerId = int.MinValue;
            SetKnobVisualPressed(false);
            ApplyAxisVisual(0f);
        }

        public void SetAxis(float axis)
        {
            SetKnobVisualPressed(Mathf.Abs(axis) > 0.001f || activePointerId != int.MinValue);
            ApplyAxisVisual(axis);
            onAxisChanged?.Invoke(currentAxis);
        }

        private void OnDisable()
        {
            activePointerId = int.MinValue;
            SetKnobVisualPressed(false);
            ApplyAxisVisual(0f);
            onAxisChanged?.Invoke(0f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (UIButtonInputTest.IsLayoutEditMode || eventData == null)
            {
                return;
            }
            activePointerId = eventData.pointerId;
            SetKnobVisualPressed(true);
            UpdateFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (UIButtonInputTest.IsLayoutEditMode || eventData == null)
            {
                return;
            }
            if (activePointerId != int.MinValue && eventData.pointerId != activePointerId)
            {
                return;
            }
            SetKnobVisualPressed(true);
            UpdateFromPointer(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null && activePointerId != int.MinValue && eventData.pointerId != activePointerId)
            {
                return;
            }
            activePointerId = int.MinValue;
            SetKnobVisualPressed(false);
            ApplyAxisVisual(0f);
            onAxisChanged?.Invoke(0f);
        }

        private float ComputeUsableHalfRange()
        {
            if (trackRect == null)
            {
                return 96f;
            }
            float trackWidth = trackRect.rect.width;
            if (trackWidth <= 1f)
            {
                trackWidth = Mathf.Max(1f, trackRect.sizeDelta.x);
            }
            float knobSize = handleRect != null ? Mathf.Max(20f, handleRect.sizeDelta.x) : 114f;
            return Mathf.Max(16f, (trackWidth - knobSize) * 0.5f - capInset);
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            if (trackRect == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                trackRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 local))
            {
                float usableHalfRange = ComputeUsableHalfRange();
                float normalizedAxis = Mathf.Clamp(local.x / usableHalfRange, -1f, 1f);
                SetAxis(normalizedAxis);
            }
        }

        private void SetKnobVisualPressed(bool pressed)
        {
            if (handleImage != null)
            {
                handleImage.color = pressed ? ActiveKnobColor : IdleKnobColor;
            }
        }

        private void ApplyAxisVisual(float axis)
        {
            currentAxis = Mathf.Clamp(axis, -1f, 1f);
            if (handleRect != null && trackRect != null)
            {
                float usableHalfRange = ComputeUsableHalfRange();
                handleRect.anchoredPosition = new Vector2(currentAxis * usableHalfRange, 0f);
            }
        }
    }
}
