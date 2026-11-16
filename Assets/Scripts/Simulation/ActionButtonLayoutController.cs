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
        public const string ToggleButtonName = "LayoutEditToggleButton";
        public const string ToolbarPanelName = "LayoutEditorToolbar";
        public const string ScaleSliderName = "SizeSlider";
        public const string HasSavedLayoutPrefKey = "SpikeHud_HasSavedCustomLayout";
        private const string PosXPrefPrefix = "SpikeHud_BtnPosX_";
        private const string PosYPrefPrefix = "SpikeHud_BtnPosY_";
        private const string ScalePrefPrefix = "SpikeHud_BtnScale_";

        public const float MinScale = ActionButtonPressView.MinCustomScale;
        public const float MaxScale = ActionButtonPressView.MaxCustomScale;
        public const float ScaleStep = 0.10f;

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
            "SPIKE"
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

        private int selectedButtonId = 4;
        private bool subscribedEditMode;

        public bool IsEditMode => UIButtonInputTest.IsLayoutEditMode;
        public int SelectedButtonId => selectedButtonId;
        public RectTransform ToggleButtonRect => toggleButtonRect;
        public RectTransform ToolbarRect => toolbarRect;
        public LayoutScaleSlider ScaleSlider => scaleSlider;

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
        }

        public void Configure(Canvas targetCanvas, UIButtonInputTest[] buttons, float currentUiScale, Font font)
        {
            canvas = targetCanvas;
            canvasRect = targetCanvas != null ? (RectTransform)targetCanvas.transform : null;
            uiScale = Mathf.Max(0.25f, currentUiScale);
            uiFont = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            RefreshEditModeVisuals(UIButtonInputTest.IsLayoutEditMode);
        }

        private void OnEnable()
        {
            SubscribeEditMode();
            RefreshEditModeVisuals(UIButtonInputTest.IsLayoutEditMode);
        }

        private void OnDisable()
        {
            UnsubscribeEditMode();
        }

        private void SubscribeEditMode()
        {
            if (subscribedEditMode)
            {
                return;
            }

            UIButtonInputTest.LayoutEditModeChanged += OnLayoutEditModeChanged;
            subscribedEditMode = true;
        }

        private void UnsubscribeEditMode()
        {
            if (!subscribedEditMode)
            {
                return;
            }

            UIButtonInputTest.LayoutEditModeChanged -= OnLayoutEditModeChanged;
            subscribedEditMode = false;
        }

        private void OnLayoutEditModeChanged(bool editMode)
        {
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

        public void SelectButton(int buttonId)
        {
            if (buttonId < 0 || buttonId >= ButtonCount)
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

            RefreshToolbarInfo();
        }

        public Vector2 GetButtonAnchoredPosition(int buttonId)
        {
            if (buttonId < 0 || buttonId >= ButtonCount)
            {
                return Vector2.zero;
            }

            return currentAnchoredPositions[buttonId];
        }

        public float GetButtonScale(int buttonId)
        {
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

        public void SetButtonScreenPosition(int buttonId, Vector2 screenPos, bool saveImmediately = false)
        {
            if (buttonId < 0 || buttonId >= ButtonCount || buttonRects[buttonId] == null || canvasRect == null)
            {
                return;
            }

            RectTransform rect = buttonRects[buttonId];
            float halfSize = Mathf.Max(24f, rect.sizeDelta.x * canvas.scaleFactor * currentScales[buttonId] * 0.5f);
            float minX = halfSize;
            float maxX = Mathf.Max(minX, Screen.width - halfSize);
            float minY = halfSize;
            // Reserve top 15% of screen during drag so gameplay buttons cannot be dropped over the Scoreboard or Custom UI Dock
            float topReservedPx = Screen.height * 0.14f;
            float maxY = Mathf.Max(minY, Screen.height - topReservedPx - halfSize * 0.5f);
            Vector2 clampedScreen = new Vector2(
                Mathf.Clamp(screenPos.x, minX, maxX),
                Mathf.Clamp(screenPos.y, minY, maxY));

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, clampedScreen, cam, out Vector2 localPoint);
            rect.position = canvasRect.TransformPoint(localPoint);
            Vector2 newAnchored = rect.anchoredPosition;

            SetButtonAnchoredPosition(buttonId, newAnchored, saveImmediately);
        }

        public void SetSelectedScale(float scale)
        {
            SetButtonScale(selectedButtonId, scale, true);
        }

        public void SetButtonScale(int buttonId, float scale, bool saveImmediately = true)
        {
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
            SetButtonScale(selectedButtonId, currentScales[selectedButtonId] + delta, true);
        }

        public void ResetSelectedButton()
        {
            int id = selectedButtonId;
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
            return (buttonId >= 0 && buttonId < ButtonCount) ? buttonRects[buttonId] : null;
        }

        private void RefreshEditModeVisuals(bool editMode)
        {
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

            for (int id = 0; id < ButtonCount; id++)
            {
                if (pressViews[id] != null)
                {
                    pressViews[id].SetSelectedHighlight(editMode && id == selectedButtonId);
                }
            }

            if (editMode)
            {
                RefreshToolbarInfo();
            }
        }

        private void RefreshToolbarInfo()
        {
            if (selectedInfoLabel != null && selectedButtonId >= 0 && selectedButtonId < ButtonCount)
            {
                int pct = Mathf.RoundToInt(currentScales[selectedButtonId] * 100f);
                selectedInfoLabel.text = "SELECTED: " + ButtonDisplayNames[selectedButtonId] + "   •   SIZE: " + pct + "%";
            }

            if (scaleSlider != null && selectedButtonId >= 0 && selectedButtonId < ButtonCount)
            {
                scaleSlider.SetNormalizedWithoutNotify(Mathf.InverseLerp(MinScale, MaxScale, currentScales[selectedButtonId]));
            }
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

            Transform existingToggle = transform.Find(ToggleButtonName);
            if (existingToggle == null)
            {
                toggleButtonRect = CreateToolbarButton(
                    transform,
                    layer,
                    ToggleButtonName,
                    "⚙ CUSTOM UI",
                    new Vector2(168f * invScale, 40f * invScale),
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
                toggleButtonImage = toggleButtonRect.GetComponent<Image>();
                toggleButtonOutline = toggleButtonRect.GetComponent<Outline>();
                toggleButtonLabel = toggleButtonRect.GetComponentInChildren<Text>(true);
            }

            // Dock Toggle Button in Top-Left utility slot (completely separate from Top-Center Scoreboard & Top-Right BGM/Pause)
            PositionScreenPoint(
                toggleButtonRect,
                Screen.safeArea.xMin + 98f * uiScale,
                Screen.safeArea.yMax - 30f * uiScale);

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
                toolbarRect.sizeDelta = new Vector2(820f * invScale, 112f * invScale);

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
                infoRect.anchoredPosition = new Vector2(-135f * invScale, 30f * invScale);
                infoRect.sizeDelta = new Vector2(510f * invScale, 26f * invScale);
                selectedInfoLabel = infoObj.GetComponent<Text>();
                selectedInfoLabel.font = uiFont;
                selectedInfoLabel.fontStyle = FontStyle.Bold;
                selectedInfoLabel.fontSize = Mathf.Max(11, Mathf.RoundToInt(16f * invScale));
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
                hintRect.anchoredPosition = new Vector2(235f * invScale, 30f * invScale);
                hintRect.sizeDelta = new Vector2(310f * invScale, 24f * invScale);
                hintSubLabel = hintObj.GetComponent<Text>();
                hintSubLabel.font = uiFont;
                hintSubLabel.fontStyle = FontStyle.Italic;
                hintSubLabel.fontSize = Mathf.Max(10, Mathf.RoundToInt(13f * invScale));
                hintSubLabel.alignment = TextAnchor.MiddleRight;
                hintSubLabel.color = new Color32(148, 163, 184, 255);
                hintSubLabel.text = "Tap & drag any button to reposition";
                hintSubLabel.raycastTarget = false;

                float rowY = -18f * invScale;

                RectTransform minusBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SizeMinusButton",
                    "- 10%",
                    new Vector2(88f * invScale, 38f * invScale),
                    ToolbarBtnColor,
                    () => AdjustSelectedButtonScale(-ScaleStep),
                    out _,
                    out _);
                minusBtn.anchoredPosition = new Vector2(-348f * invScale, rowY);

                scaleSlider = CreateScaleSlider(toolbarRect, layer, new Vector2(196f * invScale, 26f * invScale), invScale);
                ((RectTransform)scaleSlider.transform).anchoredPosition = new Vector2(-190f * invScale, rowY);

                RectTransform plusBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SizePlusButton",
                    "+ 10%",
                    new Vector2(88f * invScale, 38f * invScale),
                    ToolbarBtnColor,
                    () => AdjustSelectedButtonScale(ScaleStep),
                    out _,
                    out _);
                plusBtn.anchoredPosition = new Vector2(-32f * invScale, rowY);

                RectTransform resetBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "ResetButton",
                    "RESET BTN",
                    new Vector2(108f * invScale, 38f * invScale),
                    ToolbarBtnColor,
                    ResetSelectedButton,
                    out _,
                    out _);
                resetBtn.anchoredPosition = new Vector2(80f * invScale, rowY);

                RectTransform resetAllBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "ResetAllButton",
                    "RESET ALL",
                    new Vector2(108f * invScale, 38f * invScale),
                    ToolbarWarnBtnColor,
                    ResetAllButtons,
                    out _,
                    out _);
                resetAllBtn.anchoredPosition = new Vector2(200f * invScale, rowY);

                RectTransform saveExitBtn = CreateToolbarButton(
                    toolbarRect,
                    layer,
                    "SaveExitButton",
                    "DONE / SAVE",
                    new Vector2(128f * invScale, 38f * invScale),
                    ToolbarAccentBtnColor,
                    SaveAndExitEditMode,
                    out _,
                    out _);
                saveExitBtn.anchoredPosition = new Vector2(330f * invScale, rowY);
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
                Screen.safeArea.yMax - 156f * uiScale);
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
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            label = textObj.GetComponent<Text>();
            label.font = uiFont;
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 14;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 15;
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
            GameObject sliderObj = new GameObject(
                ScaleSliderName,
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
            knobRect.sizeDelta = new Vector2(16f * invScale, 32f * invScale);
            Image knobImage = knobObj.GetComponent<Image>();
            knobImage.color = SliderKnobColor;
            knobImage.raycastTarget = false;
            Shadow knobShadow = knobObj.GetComponent<Shadow>();
            knobShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            knobShadow.effectDistance = new Vector2(0f, -2f);

            LayoutScaleSlider slider = sliderObj.GetComponent<LayoutScaleSlider>();
            slider.Configure(sliderRect, fillRect, knobRect, normalized =>
            {
                float targetScale = Mathf.Lerp(MinScale, MaxScale, normalized);
                SetButtonScale(selectedButtonId, targetScale, true);
            });
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
}
