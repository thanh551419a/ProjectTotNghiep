using Assets.Table;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Simulation
{
    /// <summary>Builds the touch HUD while retaining native IDs 1-8 and the legacy button objects.</summary>
    public static class P02ActionButtonBootstrap
    {
        public const string RootName = "SpikeActionButtons";
        public const string DedicatedSpikeName = "DedicatedSpike";
        public const string CooldownOverlayName = "CooldownOverlay";
        public const string CooldownTimerName = "CooldownTimer";
        public const float PrimaryDiameterPixels = 236f;
        public const float SecondaryRatio = 0.70f;
        public const float SecondaryDiameterPixels = PrimaryDiameterPixels * SecondaryRatio;
        private const float ReferenceScreenWidth = 1920f;
        private const float ReferenceScreenHeight = 1080f;
        private const float GapPixels = 14f;
        private const float LeftSideMarginPixels = 34f;
        private const float LeftBottomMarginPixels = 40f;
        private const float MovementButtonGapPixels = 26f;
        private const float RightSideMarginPixels = 28f;
        private const float RightBottomMarginPixels = 28f;
        private const int DiscResolution = 256;
        private const float DiscEdgeInsetPixels = 5f;
        private const float DiscRingWidthPixels = 10f;
        private const float LabelInsetPixels = 10f;
        private const float OutlineOffsetPixels = 2f;
        private const int PrimaryFontSize = 30;
        private const int SecondaryFontSize = 20;
        private const int NewButtonFontSize = 20;
        private const int PrimaryCooldownFontSize = 22;
        private const int SecondaryCooldownFontSize = 16;
        private const int MinimumFontSize = 12;
        private const float LegacyRowWidthPixels = 408f;
        private const float LegacyRowHeightPixels = 72f;
        private const float LegacyBottomBandFraction = 0.16f;
        private const int MoveLeftStyle = 0;
        private const int DefenseStyle = 1;
        private const int JumpStyle = 2;
        private const int SpikeStyle = 3;
        private const int SelectorStyle = 4;
        private const int MoveRightStyle = 5;
        private static readonly Sprite[] DiscSprites = new Sprite[6];
        private static readonly Color32 DiscInnerFill = new Color32(28, 48, 78, 248);
        private static readonly Color32 DiscOuterFill = new Color32(12, 21, 36, 248);
        private static readonly Color32 LabelColor = new Color32(248, 252, 255, 255);
        private static readonly Color32 ArrowCoreColor = new Color32(248, 253, 255, 255);
        internal static readonly Color32 CooldownOverlayColor = new Color32(8, 12, 22, 185);
        internal static readonly Color32 CooldownTimerColor = new Color32(255, 224, 130, 255);
        private static readonly Vector2 CooldownTimerAnchorMin = new Vector2(0.10f, 0.12f);
        private static readonly Vector2 CooldownTimerAnchorMax = new Vector2(0.90f, 0.44f);
        private static readonly Color32[] RingColors =
        {
            new Color32(79, 224, 234, 255),
            new Color32(111, 164, 255, 255),
            new Color32(255, 210, 93, 255),
            new Color32(255, 112, 93, 255),
            new Color32(255, 174, 113, 255),
            new Color32(79, 224, 234, 255)
        };
        private const string FallbackGameplayScenePath = "Assets/Resources/New Scene.unity";
        private static Font labelFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (EnsureButtons(activeScene, out var error)) return;
            if (activeScene.path != FallbackGameplayScenePath &&
                UnityEngine.Object.FindAnyObjectByType<SpikeSimulationManager>() == null)
            {
                SceneManager.sceneLoaded -= OnFallbackSceneLoaded;
                SceneManager.sceneLoaded += OnFallbackSceneLoaded;
#if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    FallbackGameplayScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
                SceneManager.LoadScene(0);
#endif
                return;
            }
            // Debug.LogError("[P02ActionButtonBootstrap] " + error);
        }

        private static void OnFallbackSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFallbackSceneLoaded;
            if (!EnsureButtons(scene, out var error))
            {
                // Debug.LogError("[P02ActionButtonBootstrap] " + error);
            }
        }

        public static bool EnsureButtons(Scene scene, out string error)
        {
            error = null;
            if (!scene.IsValid() || !scene.isLoaded) return Fail("Gameplay scene is unavailable.", out error);
            SpikeSimulationManager manager = null;
            int managerCount = 0;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<SpikeSimulationManager>(FindObjectsInactive.Exclude))
                if (candidate.gameObject.scene == scene && candidate.isActiveAndEnabled) { manager = candidate; managerCount++; }
            if (managerCount != 1) return Fail("Expected one active SpikeSimulationManager.", out error);
            int eventCount = 0;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude))
                if (candidate.gameObject.scene == scene && candidate.isActiveAndEnabled &&
                    candidate.GetComponent<BaseInputModule>() != null) eventCount++;
            if (eventCount != 1) return Fail("Expected one active EventSystem.", out error);

            Canvas canvas = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled) continue;
                var controls = candidate.GetComponentsInChildren<UIButtonInputTest>(true);
                if (Count(controls, 0) == 1 && Count(controls, 1) == 1 &&
                    Count(controls, 2) == 1 && Count(controls, 3) == 1)
                {
                    if (canvas != null) return Fail("Multiple gameplay Canvases contain legacy controls.", out error);
                    canvas = candidate;
                }
            }
            if (canvas == null || canvas.GetComponent<GraphicRaycaster>() == null)
                return Fail("Gameplay Canvas or GraphicRaycaster is missing.", out error);
            var adapter = manager.GetComponent<SpikeInputAdapter>();
            if (adapter == null) return Fail("SpikeInputAdapter is missing.", out error);
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            var root = canvas.transform.Find(RootName) as RectTransform;
            if (root == null)
            {
                var obj = new GameObject(RootName, typeof(RectTransform), typeof(SpikeActionHudState));
                obj.layer = canvas.gameObject.layer;
                root = (RectTransform)obj.transform;
                root.SetParent(canvas.transform, false);
                root.anchorMin = root.anchorMax = Vector2.zero;
                root.pivot = Vector2.zero;
                CreateButton(root, canvas.gameObject.layer, 4, "Jump", PrimaryDiameterPixels, scale);
                CreateButton(root, canvas.gameObject.layer, 5, "Spike Light", SecondaryDiameterPixels, scale);
                CreateButton(root, canvas.gameObject.layer, 6, "Spike Medium", SecondaryDiameterPixels, scale);
                CreateButton(root, canvas.gameObject.layer, 7, "Spike Strong", SecondaryDiameterPixels, scale);
                CreateButton(root, canvas.gameObject.layer, SpikeInputAdapter.UiDedicatedSpike, "Spike Medium", SecondaryDiameterPixels, scale);
            }
            var buttons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
            for (int id = 0; id <= SpikeInputAdapter.UiDedicatedSpike; id++)
                if (Count(buttons, id) != 1) return Fail("Expected exactly one touch control ID " + id, out error);

            float widthScale = Mathf.Max(1f, Screen.safeArea.width) / ReferenceScreenWidth;
            float heightScale = Mathf.Max(1f, Screen.height) / ReferenceScreenHeight;
            float uiScale = Mathf.Clamp(Mathf.Min(widthScale, heightScale), 0.35f, 2.5f);
            float primaryDiameter = PrimaryDiameterPixels * uiScale;
            float secondaryDiameter = SecondaryDiameterPixels * uiScale;
            float gap = GapPixels * uiScale;
            float leftMargin = LeftSideMarginPixels * uiScale;
            float leftBottom = LeftBottomMarginPixels * uiScale;
            float moveGap = MovementButtonGapPixels * uiScale;
            float rightMargin = RightSideMarginPixels * uiScale;
            float rightBottom = RightBottomMarginPixels * uiScale;

            // All secondary controls share one exact diameter in screen pixels (70% of Jump).
            Position(buttons, 0, canvas, Screen.safeArea.xMin + leftMargin + 0.5f * secondaryDiameter,
                Screen.safeArea.yMin + leftBottom + 0.5f * secondaryDiameter, secondaryDiameter);
            Position(buttons, 1, canvas, Screen.safeArea.xMin + leftMargin + 1.5f * secondaryDiameter + moveGap,
                Screen.safeArea.yMin + leftBottom + 0.5f * secondaryDiameter, secondaryDiameter);
            float right = Screen.safeArea.xMax;
            float bottom = Screen.safeArea.yMin;
            float primaryX = right - rightMargin - 0.5f * primaryDiameter;
            float primaryY = bottom + rightBottom + 0.5f * primaryDiameter;
            float orbit = 0.5f * (primaryDiameter + secondaryDiameter) + 1.35f * gap;
            float diag = 0.72f * orbit;
            Position(buttons, 4, canvas, primaryX, primaryY, primaryDiameter);
            // Arena of Valor 3-satellite arc: Receive (bottom), Spike (middle), Serve (top)
            Position(buttons, 2, canvas, primaryX - orbit, primaryY, secondaryDiameter);
            Position(buttons, SpikeInputAdapter.UiDedicatedSpike, canvas,
                primaryX - diag, primaryY + diag, secondaryDiameter);
            Position(buttons, 3, canvas, primaryX, primaryY + orbit, secondaryDiameter);
            // Hold-to-select 3 Spike strengths fan around DedicatedSpike
            Position(buttons, 5, canvas, primaryX - orbit - secondaryDiameter - gap, primaryY + 0.32f * orbit, secondaryDiameter);
            Position(buttons, 6, canvas, primaryX - orbit - secondaryDiameter - gap, primaryY + 1.15f * orbit, secondaryDiameter);
            Position(buttons, 7, canvas, primaryX - 0.85f * orbit, primaryY + 1.80f * orbit, secondaryDiameter);

            for (int id = 0; id <= SpikeInputAdapter.UiDedicatedSpike; id++)
                StyleButton(Find(buttons, id), id);

            var layoutController = root.GetComponent<ActionButtonLayoutController>();
            if (layoutController == null) layoutController = root.gameObject.AddComponent<ActionButtonLayoutController>();
            layoutController.Configure(canvas, buttons, uiScale, LabelFont(), adapter);

            var state = root.GetComponent<SpikeActionHudState>();
            if (state == null) state = root.gameObject.AddComponent<SpikeActionHudState>();
            state.Configure(adapter, buttons);
            adapter.BindUiButtons(scene);
            return true;
        }

        // Retained for callers that still inspect the historical four-button row calculation.
        public static bool TryComputeRowRect(int width, int height, Rect safeArea, out Rect row)
        {
            row = new Rect(safeArea.center.x - LegacyRowWidthPixels * 0.5f,
                Mathf.Max(safeArea.yMin, GapPixels), LegacyRowWidthPixels, LegacyRowHeightPixels);
            return width > 0 && height > 0 && row.xMin >= safeArea.xMin && row.xMax <= safeArea.xMax &&
                   row.yMax <= safeArea.yMax && row.yMax <= height * LegacyBottomBandFraction;
        }

        private static void Position(UIButtonInputTest[] buttons, int id, Canvas canvas, float x, float y, float diameter)
        {
            var rect = (RectTransform)Find(buttons, id).transform;
            var canvasRect = (RectTransform)canvas.transform;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(x, y), camera, out var local);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.position = canvasRect.TransformPoint(local);
            rect.sizeDelta = Vector2.one * (diameter / Mathf.Max(0.01f, canvas.scaleFactor));
            var element = rect.GetComponent<LayoutElement>();
            if (element != null) element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = diameter / Mathf.Max(0.01f, canvas.scaleFactor);
            Find(buttons, id).Configure(id, true);
        }

        private static void CreateButton(Transform root, int layer, int id, string label, float diameter, float scale)
        {
            var obj = new GameObject(id == SpikeInputAdapter.UiDedicatedSpike ? DedicatedSpikeName : "Action_" + id,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(UIButtonInputTest));
            obj.layer = layer;
            obj.transform.SetParent(root, false);
            ((RectTransform)obj.transform).sizeDelta = Vector2.one * diameter / scale;
            obj.GetComponent<UIButtonInputTest>().Configure(id, true);
            var image = obj.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            var outline = obj.GetComponent<Outline>();
            outline.enabled = false;
            outline.effectDistance = Vector2.one * OutlineOffsetPixels / scale;
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.layer = layer;
            textObject.transform.SetParent(obj.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.one * LabelInsetPixels;
            textRect.offsetMax = -Vector2.one * LabelInsetPixels;
            var text = textObject.GetComponent<Text>();
            text.font = LabelFont();
            text.text = label;
            text.fontSize = NewButtonFontSize;
            text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = MinimumFontSize;
            text.resizeTextMaxSize = NewButtonFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = LabelColor;
            text.raycastTarget = false;
        }

        public static string GetSpikeActionDisplayName(SpikeAction action)
        {
            switch (action)
            {
                case SpikeAction.SpikeLight: return "Spike Light";
                case SpikeAction.SpikeStrong: return "Spike Strong";
                default: return "Spike Medium";
            }
        }

        private static int StyleForId(int id)
        {
            return id == 0 ? MoveLeftStyle : id == 1 ? MoveRightStyle : id <= 3 ? DefenseStyle :
                id == 4 ? JumpStyle : id == SpikeInputAdapter.UiDedicatedSpike ? SpikeStyle : SelectorStyle;
        }

        internal static Text FindMainLabel(UIButtonInputTest button)
        {
            if (button == null) return null;
            Transform named = button.transform.Find("Label");
            if (named != null)
            {
                Text namedText = named.GetComponent<Text>();
                if (namedText != null) return namedText;
            }
            foreach (var candidate in button.GetComponentsInChildren<Text>(true))
            {
                if (candidate != null && candidate.gameObject.name != CooldownTimerName)
                    return candidate;
            }
            return null;
        }

        private static void StyleButton(UIButtonInputTest button, int id)
        {
            var image = button.GetComponent<Image>();
            if (image == null) return;
            int style = StyleForId(id);
            image.sprite = DiscSprite(style);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = true;
            var outline = button.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;

            foreach (var childImg in button.GetComponentsInChildren<Image>(true))
            {
                if (childImg != image && childImg.gameObject.name != CooldownOverlayName)
                    childImg.enabled = false;
            }

            var label = FindMainLabel(button);
            if (label == null)
            {
                var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textObject.layer = button.gameObject.layer;
                textObject.transform.SetParent(button.transform, false);
                var textRect = (RectTransform)textObject.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.one * LabelInsetPixels;
                textRect.offsetMax = -Vector2.one * LabelInsetPixels;
                label = textObject.GetComponent<Text>();
            }
            label.transform.SetAsFirstSibling();
            label.font = LabelFont();
            label.fontStyle = FontStyle.Bold;
            label.fontSize = id == 4 ? PrimaryFontSize : SecondaryFontSize;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = MinimumFontSize;
            label.resizeTextMaxSize = id == 4 ? PrimaryFontSize : SecondaryFontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = LabelColor;
            label.raycastTarget = false;
            if (id == 0 || id == 1) label.text = string.Empty;
            else if (id == 2) label.text = "Bump";
            else if (id == 3) label.text = "SERVE";
            else if (id == 5) label.text = "Spike Light";
            else if (id == 6) label.text = "Spike Medium";
            else if (id == 7) label.text = "Spike Strong";
            else if (id == SpikeInputAdapter.UiDedicatedSpike) label.text = "Spike Medium";
        }

        internal static void BuildOrCacheCooldownWidgets(UIButtonInputTest button, int id, out Image overlayImage, out Text timerText)
        {
            overlayImage = null;
            timerText = null;
            if (button == null) return;

            var label = FindMainLabel(button);
            if (label != null) label.transform.SetAsFirstSibling();

            Transform overlayTr = button.transform.Find(CooldownOverlayName);
            if (overlayTr == null)
            {
                var overlayObj = new GameObject(CooldownOverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                overlayObj.layer = button.gameObject.layer;
                overlayTr = overlayObj.transform;
                overlayTr.SetParent(button.transform, false);
            }
            var overlayRect = (RectTransform)overlayTr;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            overlayImage = overlayTr.GetComponent<Image>();
            overlayImage.sprite = DiscSprite(StyleForId(id));
            overlayImage.type = Image.Type.Filled;
            overlayImage.fillMethod = Image.FillMethod.Radial360;
            overlayImage.fillOrigin = (int)Image.Origin360.Top;
            overlayImage.fillClockwise = false;
            overlayImage.fillAmount = 0f;
            overlayImage.color = CooldownOverlayColor;
            overlayImage.preserveAspect = true;
            overlayImage.raycastTarget = false;
            overlayImage.enabled = false;

            Transform timerTr = button.transform.Find(CooldownTimerName);
            if (timerTr == null)
            {
                var timerObj = new GameObject(CooldownTimerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                timerObj.layer = button.gameObject.layer;
                timerTr = timerObj.transform;
                timerTr.SetParent(button.transform, false);
            }
            var timerRect = (RectTransform)timerTr;
            timerRect.anchorMin = CooldownTimerAnchorMin;
            timerRect.anchorMax = CooldownTimerAnchorMax;
            timerRect.offsetMin = Vector2.zero;
            timerRect.offsetMax = Vector2.zero;

            int maxFont = id == SpikeInputAdapter.UiJump ? PrimaryCooldownFontSize : SecondaryCooldownFontSize;
            timerText = timerTr.GetComponent<Text>();
            timerText.font = LabelFont();
            timerText.fontStyle = FontStyle.Bold;
            timerText.fontSize = maxFont;
            timerText.resizeTextForBestFit = true;
            timerText.resizeTextMinSize = MinimumFontSize;
            timerText.resizeTextMaxSize = maxFont;
            timerText.alignment = TextAnchor.MiddleCenter;
            timerText.color = CooldownTimerColor;
            timerText.raycastTarget = false;
            timerText.text = string.Empty;
            timerText.enabled = false;

            overlayTr.SetAsLastSibling();
            timerTr.SetAsLastSibling();
        }

        internal static Sprite PrimarySprite(bool spike)
        {
            return DiscSprite(spike ? SpikeStyle : JumpStyle);
        }

        private static Font LabelFont()
        {
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return labelFont;
        }

        private static Sprite DiscSprite(int style)
        {
            if (DiscSprites[style] != null) return DiscSprites[style];
            var texture = new Texture2D(DiscResolution, DiscResolution, TextureFormat.RGBA32, false);
            texture.name = "Action Disc " + style;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[DiscResolution * DiscResolution];
            float half = DiscResolution * 0.5f;
            float radius = half - DiscEdgeInsetPixels;
            float ringInner = radius - DiscRingWidthPixels;
            float subRingOuter = ringInner - 5f;
            float subRingInner = subRingOuter - 2.5f;
            Color32 ringCol = RingColors[style];
            bool isArrow = style == MoveLeftStyle || style == MoveRightStyle;
            float arrowDir = style == MoveRightStyle ? 1f : -1f;

            for (int y = 0; y < DiscResolution; y++)
            for (int x = 0; x < DiscResolution; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float coverage = Mathf.Clamp01(radius + 0.75f - distance);
                if (coverage <= 0f)
                {
                    pixels[y * DiscResolution + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float radialT = Mathf.Clamp01(distance / Mathf.Max(1f, ringInner));
                Color32 baseFill = Color32.Lerp(DiscInnerFill, DiscOuterFill, radialT * radialT);

                // Subtle top glass highlight inside the medallion
                if (dy > 0f && distance < subRingInner)
                {
                    float gloss = Mathf.Clamp01(dy / subRingInner) * (1f - radialT * 0.65f) * 0.22f;
                    baseFill = Color32.Lerp(baseFill, new Color32(180, 225, 255, 248), gloss);
                }

                Color32 color = baseFill;
                if (distance >= ringInner)
                {
                    color = ringCol;
                }
                else if (distance >= subRingInner && distance <= subRingOuter)
                {
                    color = Color32.Lerp(baseFill, ringCol, 0.55f);
                }

                if (isArrow && distance < subRingInner - 4f)
                {
                    float ax = dx * arrowDir;
                    float ay = Mathf.Abs(dy);
                    // Primary bold chevron + trailing accent chevron in normalized pixel space
                    float dMain = DistanceToSegment(ax, ay, -14f, 46f, 34f, 0f);
                    float dSub = DistanceToSegment(ax, ay, -44f, 32f, -8f, 0f);
                    float glow = Mathf.Max(Mathf.Clamp01(1f - (dMain - 8f) / 10f), Mathf.Clamp01(1f - (dSub - 5f) / 8f) * 0.8f);
                    float core = Mathf.Max(Mathf.Clamp01(10.5f - dMain), Mathf.Clamp01(7.0f - dSub));
                    if (glow > 0f) color = Color32.Lerp(color, ringCol, glow * 0.85f);
                    if (core > 0f) color = Color32.Lerp(color, ArrowCoreColor, core);
                }

                color.a = (byte)(color.a * coverage);
                pixels[y * DiscResolution + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, DiscResolution, DiscResolution),
                Vector2.one * 0.5f, DiscResolution);
            sprite.name = texture.name;
            DiscSprites[style] = sprite;
            return sprite;
        }

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float wx = px - ax, wy = py - ay;
            float c1 = vx * wx + vy * wy;
            if (c1 <= 0f) return Mathf.Sqrt(wx * wx + wy * wy);
            float c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Mathf.Sqrt((px - bx) * (px - bx) + (py - by) * (py - by));
            float t = c1 / c2;
            float projX = ax + t * vx, projY = ay + t * vy;
            return Mathf.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
        }

        private static int Count(UIButtonInputTest[] controls, int id)
        {
            int count = 0;
            foreach (var control in controls) if (control.ButtonId == id) count++;
            return count;
        }
        private static UIButtonInputTest Find(UIButtonInputTest[] controls, int id)
        {
            foreach (var control in controls) if (control.ButtonId == id) return control;
            return null;
        }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }

    /// <summary>Reacts to committed native state, cooldowns, and selector events; never polls in Update or writes gameplay Transforms.</summary>
    public sealed class SpikeActionHudState : MonoBehaviour
    {
        private const int TrackedButtonCount = 7;
        private const int MaxCachedTenths = 99;
        private const float CooldownVisibleThresholdSec = 0.01f;
        private static readonly int[] TrackedButtonIds =
        {
            SpikeInputAdapter.UiBump,
            SpikeInputAdapter.UiServe,
            SpikeInputAdapter.UiJump,
            SpikeInputAdapter.UiDedicatedSpike,
            SpikeInputAdapter.UiSpikeLight,
            SpikeInputAdapter.UiSpikeMedium,
            SpikeInputAdapter.UiSpikeStrong
        };
        private static readonly Color StandardReadyColor = Color.white;
        private static readonly Color HoveredOptionColor = Color.white;
        private static readonly Color UnhoveredOptionColor = new Color(0.90f, 0.90f, 0.94f, 0.92f);
        private static readonly Color CooldownDimColor = new Color(0.64f, 0.67f, 0.74f, 0.92f);
        private static readonly Color HoveredCooldownDimColor = new Color(0.78f, 0.80f, 0.86f, 0.96f);
        private static readonly Color32 HoveredOutlineColor = new Color32(255, 228, 140, 240);
        private static readonly string[] CooldownTenthsLabels = BuildCooldownTenthsLabels();

        private struct CooldownButtonSlot
        {
            public int buttonId;
            public bool isSpikeOption;
            public SpikeAction optionAction;
            public Image baseImage;
            public Outline outline;
            public Image overlayImage;
            public Text timerText;
            public int lastTenths;
        }

        private SpikeInputAdapter adapter;
        private UIButtonInputTest primary;
        private Text primaryLabel;
        private Image primaryImage;
        private Image primaryOverlayImage;
        private UIButtonInputTest dedicatedSpike;
        private Text dedicatedSpikeLabel;
        private Sprite jumpSprite;
        private Sprite spikeSprite;
        private bool selectorSubscribed;
        private SpikeAction? hoveredOption;
        private readonly UIButtonInputTest[] options = new UIButtonInputTest[3];
        private readonly CooldownButtonSlot[] cooldownSlots = new CooldownButtonSlot[TrackedButtonCount];
        private int configuredSlotCount;
        private SimulationDataTable table;

        private static string[] BuildCooldownTenthsLabels()
        {
            var labels = new string[MaxCachedTenths + 1];
            for (int i = 0; i <= MaxCachedTenths; i++)
            {
                labels[i] = (i * 0.1f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }
            return labels;
        }

        public void Configure(SpikeInputAdapter input, UIButtonInputTest jump, UIButtonInputTest light,
            UIButtonInputTest medium, UIButtonInputTest strong)
        {
            UIButtonInputTest[] allButtons = null;
            if (jump != null)
            {
                var canvas = jump.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    allButtons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
                }
            }
            if (allButtons == null || allButtons.Length == 0)
            {
                allButtons = new[] { jump, light, medium, strong };
            }
            Configure(input, allButtons);
        }

        internal void Configure(SpikeInputAdapter input, UIButtonInputTest[] allButtons)
        {
            UnsubscribeSelector();
            adapter = input;
            primary = FindById(allButtons, SpikeInputAdapter.UiJump);
            primaryLabel = P02ActionButtonBootstrap.FindMainLabel(primary);
            primaryImage = primary != null ? primary.GetComponent<Image>() : null;
            dedicatedSpike = FindById(allButtons, SpikeInputAdapter.UiDedicatedSpike);
            dedicatedSpikeLabel = P02ActionButtonBootstrap.FindMainLabel(dedicatedSpike);
            jumpSprite = P02ActionButtonBootstrap.PrimarySprite(false);
            spikeSprite = P02ActionButtonBootstrap.PrimarySprite(true);
            options[0] = FindById(allButtons, SpikeInputAdapter.UiSpikeLight);
            options[1] = FindById(allButtons, SpikeInputAdapter.UiSpikeMedium);
            options[2] = FindById(allButtons, SpikeInputAdapter.UiSpikeStrong);

            configuredSlotCount = 0;
            primaryOverlayImage = null;
            for (int i = 0; i < TrackedButtonIds.Length; i++)
            {
                int btnId = TrackedButtonIds[i];
                UIButtonInputTest btn = FindById(allButtons, btnId);
                if (btn == null) continue;

                P02ActionButtonBootstrap.BuildOrCacheCooldownWidgets(btn, btnId, out Image overlay, out Text timer);
                bool isOption = btnId >= SpikeInputAdapter.UiSpikeLight && btnId <= SpikeInputAdapter.UiSpikeStrong;
                SpikeAction optAction = btnId == SpikeInputAdapter.UiSpikeLight ? SpikeAction.SpikeLight :
                                        btnId == SpikeInputAdapter.UiSpikeMedium ? SpikeAction.SpikeMedium :
                                        SpikeAction.SpikeStrong;
                if (btnId == SpikeInputAdapter.UiJump)
                {
                    primaryOverlayImage = overlay;
                }

                cooldownSlots[configuredSlotCount++] = new CooldownButtonSlot
                {
                    buttonId = btnId,
                    isSpikeOption = isOption,
                    optionAction = optAction,
                    baseImage = btn.GetComponent<Image>(),
                    outline = btn.GetComponent<Outline>(),
                    overlayImage = overlay,
                    timerText = timer,
                    lastTenths = -1
                };
            }

            hoveredOption = adapter != null ? adapter.HoveredSpikeOption : null;
            SubscribeSelector();
            ShowSelector((adapter != null && adapter.SelectorOpen) || UIButtonInputTest.IsLayoutEditMode);
            BindTable();
            UpdatePrimaryLabel();
            UpdateDedicatedSpikeLabel();
            RefreshCooldownAndHoverVisuals();
        }

        public bool TryGetCooldownVisual(int buttonId, out Image overlayImage, out Text timerText)
        {
            for (int i = 0; i < configuredSlotCount; i++)
            {
                if (cooldownSlots[i].buttonId == buttonId)
                {
                    overlayImage = cooldownSlots[i].overlayImage;
                    timerText = cooldownSlots[i].timerText;
                    return overlayImage != null && timerText != null;
                }
            }
            overlayImage = null;
            timerText = null;
            return false;
        }

        private static UIButtonInputTest FindById(UIButtonInputTest[] buttons, int id)
        {
            if (buttons == null) return null;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].ButtonId == id)
                    return buttons[i];
            }
            return null;
        }

        private void OnEnable()
        {
            BindTable();
            SubscribeSelector();
            hoveredOption = adapter != null ? adapter.HoveredSpikeOption : null;
            ShowSelector((adapter != null && adapter.SelectorOpen) || UIButtonInputTest.IsLayoutEditMode);
            UpdatePrimaryLabel();
            UpdateDedicatedSpikeLabel();
            RefreshCooldownAndHoverVisuals();
        }

        private void OnDisable()
        {
            UnsubscribeSelector();
            UnbindTable();
        }

        private void OnDestroy()
        {
            UnsubscribeSelector();
            UnbindTable();
        }

        private void SubscribeSelector()
        {
            if (selectorSubscribed || !isActiveAndEnabled) return;
            if (adapter != null)
            {
                adapter.OnSpikeSelectorChanged += ShowSelector;
                adapter.OnHoveredSpikeOptionChanged += OnHoveredOptionChanged;
                adapter.OnActiveDedicatedSpikeActionChanged += OnActiveDedicatedSpikeChanged;
            }
            UIButtonInputTest.LayoutEditModeChanged += OnLayoutEditModeChanged;
            selectorSubscribed = true;
        }

        private void UnsubscribeSelector()
        {
            if (!selectorSubscribed) return;
            if (adapter != null)
            {
                adapter.OnSpikeSelectorChanged -= ShowSelector;
                adapter.OnHoveredSpikeOptionChanged -= OnHoveredOptionChanged;
                adapter.OnActiveDedicatedSpikeActionChanged -= OnActiveDedicatedSpikeChanged;
            }
            UIButtonInputTest.LayoutEditModeChanged -= OnLayoutEditModeChanged;
            selectorSubscribed = false;
        }

        private void UnbindTable()
        {
            if (table != null)
            {
                table.OnSnapshotCommitted -= OnSnapshot;
                table = null;
            }
        }

        private void OnHoveredOptionChanged(SpikeAction? option)
        {
            hoveredOption = option;
            UpdateDedicatedSpikeLabel();
            RefreshCooldownAndHoverVisuals();
        }

        private void OnActiveDedicatedSpikeChanged(SpikeAction action)
        {
            UpdateDedicatedSpikeLabel();
            RefreshCooldownAndHoverVisuals();
        }

        private void OnLayoutEditModeChanged(bool editMode)
        {
            ShowSelector((adapter != null && adapter.SelectorOpen) || editMode);
        }

        private void BindTable()
        {
            var current = SimulationDataTable.Instance;
            if (current == null && adapter != null)
            {
                current = adapter.GetComponent<SimulationDataTable>();
            }
            if (current == table) return;
            if (table != null) table.OnSnapshotCommitted -= OnSnapshot;
            table = current;
            if (table != null) table.OnSnapshotCommitted += OnSnapshot;
        }

        private void OnSnapshot(ulong tick)
        {
            UpdatePrimaryLabel();
            UpdateDedicatedSpikeLabel();
            RefreshCooldownAndHoverVisuals();
        }

        private void UpdatePrimaryLabel()
        {
            if (primary == null) return;
            bool spike = adapter != null && adapter.PrimaryIsSpike;
            if (primaryLabel != null) primaryLabel.text = spike ? "Spike" : "Jump";
            Sprite activeSprite = spike ? spikeSprite : jumpSprite;
            if (primaryImage != null) primaryImage.sprite = activeSprite;
            if (primaryOverlayImage != null) primaryOverlayImage.sprite = activeSprite;
        }

        private void UpdateDedicatedSpikeLabel()
        {
            if (dedicatedSpikeLabel == null) return;
            SpikeAction displayedAction = adapter != null
                ? (adapter.HoveredSpikeOption ?? adapter.ActiveDedicatedSpikeAction)
                : SpikeAction.SpikeMedium;
            dedicatedSpikeLabel.text = P02ActionButtonBootstrap.GetSpikeActionDisplayName(displayedAction);
        }

        private void RefreshCooldownAndHoverVisuals()
        {
            for (int i = 0; i < configuredSlotCount; i++)
            {
                ref CooldownButtonSlot slot = ref cooldownSlots[i];
                bool onCooldown = false;
                float remainSec = 0f;
                float normalizedFill = 0f;

                if (slot.buttonId != SpikeInputAdapter.UiServe && adapter != null)
                {
                    onCooldown = adapter.TryGetButtonCooldown(slot.buttonId, out remainSec, out normalizedFill) &&
                                 normalizedFill > 0f;
                }

                if (slot.overlayImage != null)
                {
                    if (onCooldown)
                    {
                        slot.overlayImage.fillAmount = normalizedFill;
                        if (!slot.overlayImage.enabled) slot.overlayImage.enabled = true;
                    }
                    else if (slot.overlayImage.enabled || slot.overlayImage.fillAmount > 0f)
                    {
                        slot.overlayImage.fillAmount = 0f;
                        slot.overlayImage.enabled = false;
                    }
                }

                if (slot.timerText != null)
                {
                    if (onCooldown && remainSec > CooldownVisibleThresholdSec)
                    {
                        int tenths = Mathf.Clamp(Mathf.CeilToInt(remainSec * 10f), 1, MaxCachedTenths);
                        if (slot.lastTenths != tenths)
                        {
                            slot.lastTenths = tenths;
                            slot.timerText.text = CooldownTenthsLabels[tenths];
                        }
                        if (!slot.timerText.enabled) slot.timerText.enabled = true;
                    }
                    else if (slot.timerText.enabled)
                    {
                        slot.timerText.enabled = false;
                        slot.lastTenths = -1;
                    }
                }

                bool isHovered = slot.isSpikeOption && hoveredOption.HasValue && hoveredOption.Value == slot.optionAction;
                if (slot.outline != null && slot.isSpikeOption)
                {
                    if (isHovered)
                    {
                        slot.outline.effectColor = HoveredOutlineColor;
                        if (!slot.outline.enabled) slot.outline.enabled = true;
                    }
                    else if (slot.outline.enabled)
                    {
                        slot.outline.enabled = false;
                    }
                }

                if (slot.baseImage != null)
                {
                    Color targetColor;
                    if (slot.isSpikeOption)
                    {
                        targetColor = onCooldown
                            ? (isHovered ? HoveredCooldownDimColor : CooldownDimColor)
                            : (isHovered ? HoveredOptionColor : UnhoveredOptionColor);
                    }
                    else
                    {
                        targetColor = onCooldown ? CooldownDimColor : StandardReadyColor;
                    }

                    if (slot.baseImage.color != targetColor)
                    {
                        slot.baseImage.color = targetColor;
                    }
                }
            }
        }

        private void ShowSelector(bool visible)
        {
            bool effectiveVisible = visible || UIButtonInputTest.IsLayoutEditMode;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] != null) options[i].gameObject.SetActive(effectiveVisible);
            }
            if (effectiveVisible)
            {
                RefreshCooldownAndHoverVisuals();
            }
        }
    }
}
