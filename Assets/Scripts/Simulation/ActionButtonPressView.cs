using UnityEngine;
using UnityEngine.UI;

namespace Simulation
{
    /// <summary>
    /// Provides a 3D elevated bottom-lip shadow and tactile sink-down press animation for gameplay HUD buttons.
    /// Zero allocations in Update; early-outs whenever currentPress matches targetPress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionButtonPressView : MonoBehaviour
    {
        public const float RestShadowOffsetY = -8f;
        public const float PressedShadowOffsetY = -1.5f;
        public const float PressSinkOffsetY = -7f;
        public const float PressedScaleMultiplier = 0.91f;
        public const float MinCustomScale = 0.55f;
        public const float MaxCustomScale = 1.75f;

        private const float InstantPressImpulse = 0.75f;
        private const float PressSpeed = 22f;
        private const float ReleaseSpeed = 14f;
        private const float SelectionOutlineOffset = 3.5f;

        private static readonly Color RestFaceColor = Color.white;
        private static readonly Color PressedFaceColor = new Color(0.76f, 0.88f, 1.00f, 1.00f);
        private static readonly Color SelectedFaceColor = new Color(1.00f, 0.96f, 0.82f, 1.00f);
        private static readonly Color RestLabelColor = new Color32(248, 252, 255, 255);
        private static readonly Color PressedLabelColor = new Color32(195, 232, 255, 255);
        private static readonly Color LipShadowColor = new Color(0.02f, 0.05f, 0.12f, 0.82f);
        private static readonly Color SelectionOutlineColor = new Color(1.00f, 0.85f, 0.20f, 0.98f);

        private RectTransform rectTransform;
        private UIButtonInputTest inputButton;
        private Image faceImage;
        private Text labelText;
        private Shadow lipShadow;
        private Outline selectionOutline;

        private int buttonId;
        private float uiScale = 1f;
        private Vector2 baseAnchoredPosition;
        private float customScale = 1f;
        private float currentPress;
        private float targetPress;
        private bool isSelected;
        private bool subscribed;

        public int ButtonId => buttonId;
        public Vector2 BaseAnchoredPosition => baseAnchoredPosition;
        public float CustomScale => customScale;
        public float CurrentPress => currentPress;
        public float TargetPress => targetPress;
        public bool IsSelected => isSelected;
        public Shadow LipShadow => lipShadow;
        public Outline SelectionOutline => selectionOutline;

        public void Configure(UIButtonInputTest button, float currentUiScale)
        {
            Unsubscribe();

            rectTransform = (RectTransform)transform;
            inputButton = button != null ? button : GetComponent<UIButtonInputTest>();
            buttonId = inputButton != null ? inputButton.ButtonId : 0;
            uiScale = Mathf.Max(0.25f, currentUiScale);

            Button legacyButton = GetComponent<Button>();
            if (legacyButton != null)
            {
                legacyButton.transition = Selectable.Transition.None;
                legacyButton.enabled = false;
            }

            faceImage = GetComponent<Image>();
            labelText = GetComponentInChildren<Text>(true);
            selectionOutline = GetComponent<Outline>();
            if (selectionOutline == null)
            {
                selectionOutline = gameObject.AddComponent<Outline>();
            }

            lipShadow = FindOrCreateLipShadow();
            lipShadow.effectColor = LipShadowColor;
            lipShadow.useGraphicAlpha = true;
            lipShadow.enabled = true;

            baseAnchoredPosition = rectTransform.anchoredPosition;
            if (customScale <= 0f)
            {
                customScale = 1f;
            }

            Subscribe();

            bool pressedNow = inputButton != null && inputButton.IsPressed && !UIButtonInputTest.IsLayoutEditMode;
            targetPress = pressedNow ? 1f : 0f;
            currentPress = targetPress;
            ApplyVisualState(currentPress);
        }

        public void SetBaseLayout(Vector2 basePosition, float scale)
        {
            if (rectTransform == null)
            {
                rectTransform = (RectTransform)transform;
            }

            baseAnchoredPosition = basePosition;
            customScale = Mathf.Clamp(scale, MinCustomScale, MaxCustomScale);
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                currentPress = 0f;
                targetPress = 0f;
            }
            ApplyVisualState(currentPress);
        }

        public void SetSelectedHighlight(bool selected)
        {
            isSelected = selected;
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                currentPress = 0f;
                targetPress = 0f;
            }
            ApplyVisualState(currentPress);
        }

        private void OnEnable()
        {
            Subscribe();
            if (rectTransform != null)
            {
                bool pressedNow = inputButton != null && inputButton.IsPressed && !UIButtonInputTest.IsLayoutEditMode;
                targetPress = pressedNow ? 1f : 0f;
                currentPress = targetPress;
                ApplyVisualState(currentPress);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            currentPress = 0f;
            targetPress = 0f;
            if (rectTransform != null)
            {
                ApplyVisualState(0f);
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(currentPress, targetPress))
            {
                return;
            }

            float speed = targetPress > currentPress ? PressSpeed : ReleaseSpeed;
            float dt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
            currentPress = Mathf.MoveTowards(currentPress, targetPress, speed * dt);
            ApplyVisualState(currentPress);
        }

        private void Subscribe()
        {
            if (subscribed || inputButton == null)
            {
                return;
            }

            inputButton.PressedStateChanged += OnPressedStateChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || inputButton == null)
            {
                subscribed = false;
                return;
            }

            inputButton.PressedStateChanged -= OnPressedStateChanged;
            subscribed = false;
        }

        private void OnPressedStateChanged(bool pressed)
        {
            if (UIButtonInputTest.IsLayoutEditMode)
            {
                targetPress = 0f;
                currentPress = 0f;
                ApplyVisualState(0f);
                return;
            }

            targetPress = pressed ? 1f : 0f;
            if (pressed && currentPress < InstantPressImpulse)
            {
                currentPress = InstantPressImpulse;
            }
            ApplyVisualState(currentPress);
        }

        private void ApplyVisualState(float press)
        {
            if (rectTransform == null)
            {
                return;
            }

            float clampedPress = Mathf.Clamp01(press);
            float sinkY = PressSinkOffsetY * uiScale * clampedPress;
            rectTransform.anchoredPosition = new Vector2(baseAnchoredPosition.x, baseAnchoredPosition.y + sinkY);

            float effectiveScale = customScale * Mathf.Lerp(1f, PressedScaleMultiplier, clampedPress);
            rectTransform.localScale = new Vector3(effectiveScale, effectiveScale, 1f);

            if (lipShadow != null)
            {
                float shadowY = Mathf.Lerp(RestShadowOffsetY, PressedShadowOffsetY, clampedPress) * uiScale;
                lipShadow.effectDistance = new Vector2(0f, shadowY);
            }

            if (selectionOutline != null)
            {
                bool showSelection = isSelected && UIButtonInputTest.IsLayoutEditMode;
                selectionOutline.enabled = showSelection;
                if (showSelection)
                {
                    selectionOutline.effectColor = SelectionOutlineColor;
                    float offset = SelectionOutlineOffset * uiScale;
                    selectionOutline.effectDistance = new Vector2(offset, offset);
                }
            }

            if (faceImage != null)
            {
                Color baseColor = (isSelected && UIButtonInputTest.IsLayoutEditMode) ? SelectedFaceColor : RestFaceColor;
                faceImage.color = Color.Lerp(baseColor, PressedFaceColor, clampedPress);
            }

            if (labelText != null)
            {
                labelText.color = Color.Lerp(RestLabelColor, PressedLabelColor, clampedPress);
            }
        }

        private Shadow FindOrCreateLipShadow()
        {
            Shadow[] shadows = GetComponents<Shadow>();
            for (int i = 0; i < shadows.Length; i++)
            {
                Shadow candidate = shadows[i];
                if (candidate != null && candidate.GetType() == typeof(Shadow))
                {
                    return candidate;
                }
            }

            return gameObject.AddComponent<Shadow>();
        }
    }
}
