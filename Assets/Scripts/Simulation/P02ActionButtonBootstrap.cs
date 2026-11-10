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
        public const float PrimaryDiameterPixels = 144f;
        public const float SecondaryRatio = 0.70f;
        public const float SecondaryDiameterPixels = PrimaryDiameterPixels * SecondaryRatio;
        private const float GapPixels = 8f;
        private const int DiscResolution = 128;
        private const float DiscEdgeInsetPixels = 3f;
        private const float DiscRingWidthPixels = 5f;
        private const float LabelInsetPixels = 5f;
        private const float OutlineOffsetPixels = 2f;
        private const int PrimaryFontSize = 20;
        private const int SecondaryFontSize = 14;
        private const int NewButtonFontSize = 16;
        private const int MinimumFontSize = 10;
        private const float LegacyRowWidthPixels = 408f;
        private const float LegacyRowHeightPixels = 72f;
        private const float LegacyBottomBandFraction = 0.16f;
        private const int MovementStyle = 0;
        private const int DefenseStyle = 1;
        private const int JumpStyle = 2;
        private const int SpikeStyle = 3;
        private const int SelectorStyle = 4;
        private static readonly Sprite[] DiscSprites = new Sprite[5];
        private static readonly Color32 DiscFill = new Color32(16, 27, 43, 245);
        private static readonly Color32 LabelColor = new Color32(245, 250, 255, 255);
        private static readonly Color32[] RingColors =
        {
            new Color32(79, 224, 234, 255),
            new Color32(111, 164, 255, 255),
            new Color32(255, 210, 93, 255),
            new Color32(255, 112, 93, 255),
            new Color32(255, 174, 113, 255)
        };
        private static Font labelFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            if (!EnsureButtons(SceneManager.GetActiveScene(), out var error))
                Debug.LogError("[P02ActionButtonBootstrap] " + error);
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
                CreateButton(root, canvas.gameObject.layer, SpikeInputAdapter.UiDedicatedSpike, "Spike", SecondaryDiameterPixels, scale);
            }
            var buttons = canvas.GetComponentsInChildren<UIButtonInputTest>(true);
            for (int id = 0; id <= SpikeInputAdapter.UiDedicatedSpike; id++)
                if (Count(buttons, id) != 1) return Fail("Expected exactly one touch control ID " + id, out error);
            var leftLabel = Find(buttons, 0).GetComponentInChildren<Text>(true);
            if (leftLabel != null) leftLabel.text = "<";
            var rightLabel = Find(buttons, 1).GetComponentInChildren<Text>(true);
            if (rightLabel != null) rightLabel.text = ">";
            var receiveLabel = Find(buttons, 2).GetComponentInChildren<Text>(true);
            if (receiveLabel != null) receiveLabel.text = "Receive";
            var serveLabel = Find(buttons, 3).GetComponentInChildren<Text>(true);
            if (serveLabel != null) serveLabel.text = "Serve";

            // All secondary controls share one exact diameter in screen pixels (70% of Jump).
            Position(buttons, 0, canvas, Screen.safeArea.xMin + 0.5f * SecondaryDiameterPixels + GapPixels,
                Screen.safeArea.yMin + 0.5f * SecondaryDiameterPixels + GapPixels, SecondaryDiameterPixels);
            Position(buttons, 1, canvas, Screen.safeArea.xMin + 1.5f * SecondaryDiameterPixels + 2f * GapPixels,
                Screen.safeArea.yMin + 0.5f * SecondaryDiameterPixels + GapPixels, SecondaryDiameterPixels);
            float right = Screen.safeArea.xMax;
            float bottom = Screen.safeArea.yMin;
            float primaryX = right - 0.5f * PrimaryDiameterPixels - GapPixels;
            float primaryY = bottom + 0.5f * PrimaryDiameterPixels + GapPixels;
            float orbit = 0.5f * (PrimaryDiameterPixels + SecondaryDiameterPixels) + 2f * GapPixels;
            float diag = 0.72f * orbit;
            Position(buttons, 4, canvas, primaryX, primaryY, PrimaryDiameterPixels);
            // Arena of Valor 3-satellite arc: Receive (bottom), Spike (middle), Serve (top)
            Position(buttons, 2, canvas, primaryX - orbit, primaryY, SecondaryDiameterPixels);
            Position(buttons, SpikeInputAdapter.UiDedicatedSpike, canvas,
                primaryX - diag, primaryY + diag, SecondaryDiameterPixels);
            Position(buttons, 3, canvas, primaryX, primaryY + orbit, SecondaryDiameterPixels);
            // Hold-to-select 3 Spike strengths fan around DedicatedSpike
            Position(buttons, 5, canvas, primaryX - orbit - SecondaryDiameterPixels - GapPixels, primaryY + 0.35f * orbit, SecondaryDiameterPixels);
            Position(buttons, 6, canvas, primaryX - orbit - SecondaryDiameterPixels - GapPixels, primaryY + 1.25f * orbit, SecondaryDiameterPixels);
            Position(buttons, 7, canvas, primaryX - 0.85f * orbit, primaryY + 1.95f * orbit, SecondaryDiameterPixels);

            for (int id = 0; id <= SpikeInputAdapter.UiDedicatedSpike; id++)
                StyleButton(Find(buttons, id), id);

            var state = root.GetComponent<SpikeActionHudState>();
            if (state == null) state = root.gameObject.AddComponent<SpikeActionHudState>();
            state.Configure(adapter, Find(buttons, 4), Find(buttons, 5), Find(buttons, 6), Find(buttons, 7));
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

        private static void StyleButton(UIButtonInputTest button, int id)
        {
            var image = button.GetComponent<Image>();
            if (image == null) return;
            int style = id <= 1 ? MovementStyle : id <= 3 ? DefenseStyle :
                id == 4 ? JumpStyle : id == SpikeInputAdapter.UiDedicatedSpike ? SpikeStyle : SelectorStyle;
            image.sprite = DiscSprite(style);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = true;
            var outline = button.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            label.font = LabelFont();
            label.fontStyle = FontStyle.Bold;
            label.fontSize = id == 4 ? PrimaryFontSize : SecondaryFontSize;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = MinimumFontSize;
            label.resizeTextMaxSize = id == 4 ? PrimaryFontSize : SecondaryFontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = LabelColor;
            label.raycastTarget = false;
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
            float radius = DiscResolution * 0.5f - DiscEdgeInsetPixels;
            float ringInner = radius - DiscRingWidthPixels;
            for (int y = 0; y < DiscResolution; y++)
            for (int x = 0; x < DiscResolution; x++)
            {
                float dx = x + 0.5f - DiscResolution * 0.5f;
                float dy = y + 0.5f - DiscResolution * 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float coverage = Mathf.Clamp01(radius + 0.5f - distance);
                Color32 color = distance >= ringInner ? RingColors[style] : DiscFill;
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

    /// <summary>Reacts to committed native state and selector events; never writes gameplay Transforms.</summary>
    public sealed class SpikeActionHudState : MonoBehaviour
    {
        private SpikeInputAdapter adapter;
        private UIButtonInputTest primary;
        private Text primaryLabel;
        private Image primaryImage;
        private Sprite jumpSprite;
        private Sprite spikeSprite;
        private bool selectorSubscribed;
        private readonly UIButtonInputTest[] options = new UIButtonInputTest[3];
        private SimulationDataTable table;

        public void Configure(SpikeInputAdapter input, UIButtonInputTest jump, UIButtonInputTest light,
            UIButtonInputTest medium, UIButtonInputTest strong)
        {
            UnsubscribeSelector();
            adapter = input;
            primary = jump;
            primaryLabel = jump != null ? jump.GetComponentInChildren<Text>(true) : null;
            primaryImage = jump != null ? jump.GetComponent<Image>() : null;
            jumpSprite = P02ActionButtonBootstrap.PrimarySprite(false);
            spikeSprite = P02ActionButtonBootstrap.PrimarySprite(true);
            options[0] = light; options[1] = medium; options[2] = strong;
            SubscribeSelector();
            ShowSelector(adapter.SelectorOpen);
            BindTable();
            UpdatePrimaryLabel();
        }
        private void OnEnable()
        {
            BindTable();
            SubscribeSelector();
            if (adapter != null) ShowSelector(adapter.SelectorOpen);
            UpdatePrimaryLabel();
        }
        private void OnDisable()
        {
            UnsubscribeSelector();
            if (table != null) table.OnSnapshotCommitted -= OnSnapshot;
            table = null;
        }
        private void SubscribeSelector()
        {
            if (selectorSubscribed || adapter == null || !isActiveAndEnabled) return;
            adapter.OnSpikeSelectorChanged += ShowSelector;
            selectorSubscribed = true;
        }
        private void UnsubscribeSelector()
        {
            if (selectorSubscribed && adapter != null) adapter.OnSpikeSelectorChanged -= ShowSelector;
            selectorSubscribed = false;
        }
        private void BindTable()
        {
            var current = SimulationDataTable.Instance;
            if (current == table) return;
            if (table != null) table.OnSnapshotCommitted -= OnSnapshot;
            table = current;
            if (table != null) table.OnSnapshotCommitted += OnSnapshot;
        }
        private void OnSnapshot(ulong tick) => UpdatePrimaryLabel();
        private void UpdatePrimaryLabel()
        {
            if (primary == null) return;
            bool spike = adapter != null && adapter.PrimaryIsSpike;
            if (primaryLabel != null) primaryLabel.text = spike ? "Spike" : "Jump";
            if (primaryImage != null) primaryImage.sprite = spike ? spikeSprite : jumpSprite;
        }
        private void ShowSelector(bool visible)
        {
            foreach (var option in options) if (option != null) option.gameObject.SetActive(visible);
        }
    }
}
